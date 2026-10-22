// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Find/FindWindow.Duplicates.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Search;
using Salandual.Core.Settings;

namespace Salandual.App.Find;

internal sealed partial class FindWindow
{
    private readonly bool _duplicates;
    private readonly Dictionary<DuplicateKeys, ToggleButton> _same = [];
    private readonly TextBlock _sameNote = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<SearchHit, DuplicateGroup> _groupOf = [];
    private readonly Dictionary<SearchHit, DuplicateGroup> _groupStart = [];
    private readonly Dictionary<SearchHit, string> _kept = [];
    private List<DuplicateGroup> _groups = [];
    private DuplicateProgress? _comparing;
    private DuplicateKeys _groupedBy;

    internal bool FindsDuplicates => _duplicates;

    internal IReadOnlyList<DuplicateGroup> Groups => _groups;

    internal IReadOnlyDictionary<DuplicateKeys, ToggleButton> SameButtons => _same;

    internal DuplicateKeys SameKeys => _same.Where(s => s.Value.IsChecked == true).Aggregate(DuplicateKeys.None, (all, s) => all | s.Key);

    private Control BuildSame()
    {
        var keys = _settings.Current.Find.DuplicateKeys;
        var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (key, text) in new[] { (DuplicateKeys.Name, AppStrings.Find_SameName), (DuplicateKeys.Size, AppStrings.Find_SameSize), (DuplicateKeys.Content, AppStrings.Find_SameContent) })
        {
            var toggle = Toggle(text);
            toggle.IsChecked = keys.HasFlag(key);
            toggle.IsCheckedChanged += (_, _) => UpdateSameNote();
            _same[key] = toggle;
            row.Children.Add(toggle);
        }

        _sameNote.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
        _sameNote.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(_sameNote);
        UpdateSameNote();
        return new StackPanel { Spacing = 6, Children = { row, _problem } };
    }

    private void UpdateSameNote()
    {
        var keys = SameKeys;
        _sameNote.Text = keys.HasFlag(DuplicateKeys.Content) ? AppStrings.Find_SameNoteContent
            : keys == DuplicateKeys.Name ? AppStrings.Find_SameNoteName
            : AppStrings.Find_SameNoteOther;
    }

    private Task StartDuplicatesAsync(SearchCriteria criteria)
    {
        var keys = SameKeys;
        if (keys == DuplicateKeys.None)
        {
            ShowProblem(AppStrings.Find_SameNoneLit);
            return Task.CompletedTask;
        }

        Remember(window: false);
        RefreshHistory();
        _settings.Update(s => s with { Find = s.Find with { DuplicateKeys = keys } });
        var stop = _stop = new CancellationTokenSource();
        _found.Clear();
        SetGroups([]);
        _log.Clear();
        _summary = null;
        _comparing = null;
        _lastProgress = default;
        _progress.Opacity = 1;
        _findButton.IsEnabled = false;
        UpdateHeader();
        _flush.Start();

        var search = new FileSearch(_request.Files, _request.Open);
        var callbacks = new SearchCallbacks { Progress = p => _lastProgress = p, Log = entry => _logIncoming.Enqueue(entry) };
        var work = Task.Run(() => Duplicates.FindAsync(search, criteria, keys, callbacks, p => _comparing = p, stop.Token));
        return Searching = Finish();

        async Task Finish()
        {
            IReadOnlyList<DuplicateGroup> groups;
            SearchSummary summary;
            try
            {
                (groups, summary) = await work;
            }
            catch (OperationCanceledException)
            {
                (groups, summary) = ([], new SearchSummary(_lastProgress.Folders, _lastProgress.Files, TimeSpan.Zero, true));
            }

            _flush.Stop();
            DrainLog();
            _groupedBy = keys;
            _summary = summary;
            _stop = null;
            stop.Dispose();
            _progress.Opacity = 0;
            _findButton.IsEnabled = true;
            SetGroups(groups);
            UpdateHeader();
            FocusResultsOnce();
        }
    }

    private void SetGroups(IReadOnlyList<DuplicateGroup> groups)
    {
        _groups = [.. groups];
        _groupOf.Clear();
        _groupStart.Clear();
        _kept.Clear();
        _found.Clear();
        foreach (var group in _groups)
        {
            _groupStart[group.Files[0]] = group;
            foreach (var file in group.Files)
            {
                _groupOf[file] = group;
                _found.Add(file);
            }
        }

        ShowResults();
        if (_shown.Count > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private void Regroup()
    {
        var left = _found.ToHashSet();
        var groups = _groups.Select(g => g with { Files = [.. g.Files.Where(left.Contains)] }).Where(g => g.Files.Count > 1).ToList();
        var index = Math.Max(_list.SelectedIndex, 0);
        SetGroups(groups);
        _list.SelectedIndex = Math.Min(index, _shown.Count - 1);
        UpdateHeader();
    }

    internal string GroupText(DuplicateGroup group)
    {
        var parts = new List<string>();
        if (group.Name is { } name)
        {
            parts.Add(name);
        }

        parts.Add(Loc.Format(AppStrings.Find_GroupFiles, group.Files.Count.ToString("N0", CultureInfo.CurrentCulture)));
        if (group.Size is { } size)
        {
            parts.Add(Loc.Format(AppStrings.Find_GroupEach, _format.Size(size)));
        }

        parts.Add(group.SameContent ? AppStrings.Find_GroupSameContent
            : group.Name is not null && group.Size is not null ? AppStrings.Find_GroupSameNameSize
            : group.Name is not null ? AppStrings.Find_GroupSameName
            : AppStrings.Find_GroupSameSize);
        return string.Join(" · ", parts);
    }

    private Control GroupHeader(DuplicateGroup group)
    {
        var text = new TextBlock { Text = GroupText(group), FontSize = 12, Margin = new Thickness(8, 4, 8, 3), TextTrimming = TextTrimming.CharacterEllipsis };
        text.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        if (group.Name is not null)
        {
            text.FontWeight = FontWeight.SemiBold;
        }

        return new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = text }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
    }

    private void DuplicatesHeader(System.Collections.Generic.ICollection<Avalonia.Controls.Documents.Inline> inlines)
    {
        var count = new Avalonia.Controls.Documents.Run(Loc.Format(AppStrings.Find_DupFound, _found.Count.ToString("N0", CultureInfo.CurrentCulture), _groups.Count.ToString("N0", CultureInfo.CurrentCulture))) { FontWeight = FontWeight.SemiBold };
        count.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, _header.GetResourceObservable("Brush.Text"));
        inlines.Add(count);
        if (IsSearching)
        {
            var p = _lastProgress;
            inlines.Add(new Avalonia.Controls.Documents.Run("  ·  " + (_comparing is { } c
                ? Loc.Format(AppStrings.Find_DupComparing, c.Read.ToString("N0", CultureInfo.CurrentCulture), c.ToRead.ToString("N0", CultureInfo.CurrentCulture))
                : Loc.Format(AppStrings.Find_SoFar, p.Files.ToString("N0", CultureInfo.CurrentCulture), p.Folders.ToString("N0", CultureInfo.CurrentCulture)))));
            return;
        }

        var freed = _groups.Sum(g => g.Freeable);
        if (_groupedBy.HasFlag(DuplicateKeys.Size) || _groupedBy.HasFlag(DuplicateKeys.Content))
        {
            inlines.Add(new Avalonia.Controls.Documents.Run("  ·  " + Loc.Format(AppStrings.Find_DupFreed, _format.Size(freed))));
        }

        if (_summary is { } s)
        {
            inlines.Add(new Avalonia.Controls.Documents.Run("  ·  " + Loc.Format(AppStrings.Find_Searched, s.Files.ToString("N0", CultureInfo.CurrentCulture), s.Folders.ToString("N0", CultureInfo.CurrentCulture), s.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture))));
            if (s.Cancelled)
            {
                inlines.Add(new Avalonia.Controls.Documents.Run("  ·  " + AppStrings.Find_Stopped));
            }
        }
    }

    private Button BuildSelectButton()
    {
        var button = new Button { Content = AppStrings.Find_SelectMenu };
        button.Classes.Add("ftool");
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        Fill();
        flyout.Opening += (_, _) => Fill();
        button.Flyout = flyout;
        return SelectButton = button;

        void Fill()
        {
            flyout.Items.Clear();
            Add(AppStrings.Find_SelectButNewest, () => SelectAllBut(DuplicateKeep.Newest));
            Add(AppStrings.Find_SelectButOldest, () => SelectAllBut(DuplicateKeep.Oldest));
            Add(AppStrings.Find_SelectButFirst, () => SelectAllBut(DuplicateKeep.First));
            var folders = new MenuItem { Header = AppStrings.Find_SelectInFolder, IsEnabled = _found.Count > 0 };
            foreach (var folder in _found.Select(f => f.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).Take(30))
            {
                var item = new MenuItem { Header = folder };
                item.Click += (_, _) => SelectInFolder(folder);
                folders.Items.Add(item);
            }

            flyout.Items.Add(folders);
            flyout.Items.Add(new Separator());
            Add(AppStrings.Find_SelectNone, () => SelectDuplicates([], []));

            void Add(string header, Action run)
            {
                var item = new MenuItem { Header = header, IsEnabled = _groups.Count > 0 };
                item.Click += (_, _) => run();
                flyout.Items.Add(item);
            }
        }
    }

    internal Button? SelectButton { get; private set; }

    internal void SelectAllBut(DuplicateKeep keep)
    {
        var selected = new List<SearchHit>();
        var kept = new Dictionary<SearchHit, string>();
        foreach (var group in _groups)
        {
            var one = keep switch
            {
                DuplicateKeep.Newest => group.Files.MaxBy(f => f.LastWriteUtcTicks)!,
                DuplicateKeep.Oldest => group.Files.MinBy(f => f.LastWriteUtcTicks)!,
                _ => group.Files[0],
            };
            kept[one] = keep switch { DuplicateKeep.Newest => AppStrings.Find_KeptNewest, DuplicateKeep.Oldest => AppStrings.Find_KeptOldest, _ => AppStrings.Find_KeptFirst };
            selected.AddRange(group.Files.Where(f => f != one));
        }

        SelectDuplicates(selected, kept);
    }

    internal void SelectInFolder(string folder)
    {
        var selected = new List<SearchHit>();
        foreach (var group in _groups)
        {
            var inFolder = group.Files.Where(f => string.Equals(f.Folder, folder, StringComparison.OrdinalIgnoreCase)).ToList();
            if (inFolder.Count == group.Files.Count)
            {
                inFolder.RemoveAt(0);
            }

            selected.AddRange(inFolder);
        }

        SelectDuplicates(selected, []);
    }

    private void SelectDuplicates(IReadOnlyList<SearchHit> selected, Dictionary<SearchHit, string> kept)
    {
        _kept.Clear();
        foreach (var (hit, label) in kept)
        {
            _kept[hit] = label;
        }

        ShowResults();
        _list.SelectedItems?.Clear();
        foreach (var hit in selected)
        {
            _list.SelectedItems?.Add(hit);
        }

        UpdateDeleteKey();
        FocusList();
    }
}

internal enum DuplicateKeep
{
    Newest,
    Oldest,
    First,
}
