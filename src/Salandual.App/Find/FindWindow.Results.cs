// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Find/FindWindow.Results.cs

using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Options;
using Salandual.App.Theme;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Search;
using Salandual.Core.Vfs;

namespace Salandual.App.Find;

internal sealed partial class FindWindow
{
    private const string Columns = "3*,4*,90,140,52";

    private readonly AvaloniaList<SearchHit> _shown = [];
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TextBlock _header = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _notice = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _stopButton = new();
    private readonly Button _logButton = new();
    private readonly List<SearchLogEntry> _log = [];
    private readonly ConcurrentQueue<SearchLogEntry> _logIncoming = new();
    private readonly Grid _columnHeader = new() { ColumnDefinitions = new ColumnDefinitions(Columns) };
    private ResultColumn _sortColumn = ResultColumn.Name;
    private bool _descending;
    private DisplayFormat _format = DisplayFormat.Default;
    private readonly Button _showInPanel = new();
    private TextBlock? _deleteKey;

    internal ListBox ResultList => _list;

    internal IReadOnlyList<SearchHit> Shown => _shown;

    internal IReadOnlyList<SearchLogEntry> Log => _log;

    internal string HeaderText => _header.Text ?? string.Concat(_header.Inlines?.OfType<Run>().Select(r => r.Text) ?? []);

    internal string NoticeText => _notice.Text ?? "";

    internal Button LogButton => _logButton;

    internal Button StopButton => _stopButton;

    internal FindLogWindow? LogWindow { get; private set; }

    internal Button ShowInPanelButton => _showInPanel;

    internal void ShowResultsInPanel()
    {
        var hits = _found.Where(h => h.Archive is null).ToList();
        if (hits.Count > 0 && !IsSearching)
        {
            _request.ShowResults(hits, _duplicates ? Loc.Format(AppStrings.Find_DupWhat, _what) : _what);
        }
    }

    private Control BuildResults()
    {
        _format = DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture);
        _stopButton.Content = Loc.Format(AppStrings.Find_Stop, "Esc");
        _stopButton.Classes.Add("fstop");
        _stopButton.Padding = new Thickness(14, 5);
        _stopButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _stopButton.VerticalAlignment = VerticalAlignment.Stretch;
        _stopButton.IsVisible = false;
        _stopButton.Click += (_, _) => Stop();
        _logButton.Classes.Add("ftool");
        _logButton.IsVisible = false;
        _logButton.Click += (_, _) => ShowLog();
        ToolTip.SetTip(_logButton, AppStrings.Find_LogTip);
        _notice.Res(TextBlock.ForegroundProperty, "Brush.Warning");

        _showInPanel.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { AppIcons.Create("Icon.Panels")!, new TextBlock { Text = AppStrings.Find_ShowInPanelButton, VerticalAlignment = VerticalAlignment.Center } } };
        _showInPanel.Classes.Add("ftool");
        ToolTip.SetTip(_showInPanel, AppStrings.Find_ShowInPanelTip);
        Avalonia.Automation.AutomationProperties.SetName(_showInPanel, AppStrings.Find_ShowInPanelButton);
        _showInPanel.Click += (_, _) => ShowResultsInPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _notice } };
        if (_duplicates)
        {
            right.Children.Add(BuildSelectButton());
        }

        right.Children.Add(_showInPanel);
        right.Children.Add(_logButton);
        var head = new DockPanel { Height = 40, Margin = new Thickness(14, 0, 8, 0) };
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(_header);
        var headBorder = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = head }.Res(Border.BackgroundProperty, "Brush.PanelBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        _header.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");

        var titles = new (ResultColumn? Column, string Title, bool Right)[]
        {
            (ResultColumn.Name, AppStrings.Find_ColName, false),
            (ResultColumn.Folder, AppStrings.Find_ColFolder, false),
            (ResultColumn.Size, AppStrings.Find_ColSize, true),
            (ResultColumn.Modified, AppStrings.Find_ColModified, true),
            (null, AppStrings.Find_ColAttributes, false),
        };
        for (var i = 0; i < titles.Length; i++)
        {
            var (column, title, alignRight) = titles[i];
            var button = new Button
            {
                Content = title,
                Tag = column,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                IsEnabled = column is not null && !_duplicates,
            };
            button.Classes.Add("fcol");
            button.Click += (_, _) =>
            {
                if (column is { } c)
                {
                    Sort(c, toggle: true);
                }
            };
            Grid.SetColumn(button, i);
            _columnHeader.Children.Add(button);
        }

        var headerBorder = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = _columnHeader }.Res(Border.BorderBrushProperty, "Brush.Border");
        UpdateColumnTitles();

        _list.ItemsSource = _shown;
        _list.ItemTemplate = new FuncDataTemplate<SearchHit>((hit, _) => hit is null ? new Panel() : ResultRow(hit));
        _list.AddHandler(KeyDownEvent, OnListKey, RoutingStrategies.Tunnel);
        _list.ContextRequested += OnListContextRequested;
        _list.DoubleTapped += (_, e) =>
        {
            if (Focused is { } hit)
            {
                OpenItem(hit);
                e.Handled = true;
            }
        };
        _list.SelectionChanged += (_, _) => UpdateDeleteKey();
        _list.Res(ListBox.BackgroundProperty, "Brush.PanelBackground");
        Avalonia.Automation.AutomationProperties.SetName(_list, AppStrings.Find_Results);

        var dock = new DockPanel();
        DockPanel.SetDock(headBorder, Dock.Top);
        DockPanel.SetDock(headerBorder, Dock.Top);
        dock.Children.Add(headBorder);
        dock.Children.Add(headerBorder);
        dock.Children.Add(_list);
        UpdateHeader();
        return dock;
    }

    private Control BuildKeys()
    {
        var keys = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 16, LineSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var shownKeys = new List<(string[], string)> { (["Enter"], AppStrings.Find_KeyOpen), (["Ctrl+J"], AppStrings.Panel_ResultsJump), (["F3"], AppStrings.Find_KeyView) };
        if (_duplicates)
        {
            shownKeys.Add((["F8"], AppStrings.Find_KeyDelete));
        }

        foreach (var (key, what) in shownKeys)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            foreach (var k in key)
            {
                item.Children.Add(new Border { BorderThickness = new Thickness(1, 1, 1, 2), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0), Child = new TextBlock { Text = k, FontSize = 11, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace") } }
                    .Res(Border.BorderBrushProperty, "Brush.BorderStrong"));
            }

            var label = OptionsUi.Text(what, 12, brush: "Brush.SecondaryText");
            label.VerticalAlignment = VerticalAlignment.Center;
            item.Children.Add(label);
            if (key[0] == "F8")
            {
                _deleteKey = label;
            }

            keys.Children.Add(item);
        }

        var options = OptionsButton();
        options.Margin = new Thickness(16, 0, 0, 0);
        var dock = new DockPanel { Margin = new Thickness(14, 3, 4, 3) };
        DockPanel.SetDock(options, Dock.Right);
        dock.Children.Add(options);
        dock.Children.Add(keys);
        return new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = dock }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
    }

    internal Button OptionsButton()
    {
        var gear = new Button { Content = AppIcons.Glyph("Icon.Gear", 16, 16, 1.3), Padding = new Thickness(6, 4) };
        gear.Classes.Add("ftool");
        ToolTip.SetTip(gear, AppStrings.Find_OptionsTip);
        Avalonia.Automation.AutomationProperties.SetName(gear, AppStrings.Find_OptionsTip);
        gear.Click += (_, _) => _request.OpenOptions("compare");
        return gear;
    }

    private void UpdateDeleteKey()
    {
        if (_deleteKey is not null)
        {
            var count = _list.SelectedItems?.Count ?? 0;
            _deleteKey.Text = count > 1 ? Loc.Format(AppStrings.Find_KeyDeleteSelected, count) : AppStrings.Find_KeyDelete;
        }
    }

    private Control ResultRow(SearchHit hit)
    {
        var row = Row(hit);
        return _duplicates && _groupStart.TryGetValue(hit, out var group) ? new StackPanel { Children = { GroupHeader(group), row } } : row;
    }

    private Control Row(SearchHit hit)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), RowDefinitions = new RowDefinitions("24,Auto") };
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 4, 0) };
        name.Children.Add(new HitIcon(hit));
        name.Children.Add(Cell(hit.Name));
        if (_kept.TryGetValue(hit, out var kept))
        {
            var note = Cell("· " + kept);
            note.FontSize = 11.5;
            note.Margin = new Thickness(0);
            note.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
            name.Children.Add(note);
        }

        grid.Children.Add(name);
        var folder = Cell(hit.Folder, 1);
        ToolTip.SetTip(folder, hit.Folder);
        grid.Children.Add(folder);
        grid.Children.Add(Cell(hit.IsDirectory ? "" : _format.Size(hit.Size), 2, right: true));
        var local = hit.LastWriteUtc.ToLocalTime();
        grid.Children.Add(Cell(_format.Date(local) + " " + _format.Time(local), 3, right: true));
        grid.Children.Add(Cell(DisplayFormat.Attributes(hit.Attributes & ~FileAttributes.Directory), 4));

        if (hit.Line is { } line)
        {
            var text = new TextBlock { FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), FontSize = 12, Margin = new Thickness(36, 0, 8, 3), TextTrimming = TextTrimming.CharacterEllipsis };
            text.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
            if (line.Offset is { } offset)
            {
                text.Inlines!.Add(new Run(Loc.Format(AppStrings.Find_AtOffset, offset.ToString("X8", CultureInfo.InvariantCulture))));
            }
            else
            {
                var number = new Run(line.LineNumber.ToString(CultureInfo.CurrentCulture) + ": ");
                number.Bind(TextElement.ForegroundProperty, text.GetResourceObservable("Brush.TertiaryText"));
                text.Inlines!.Add(number);
                var start = Math.Clamp(line.Start, 0, line.Text.Length);
                var end = Math.Clamp(line.Start + line.Length, start, line.Text.Length);
                text.Inlines.Add(new Run(line.Text[..start]));
                var match = new Run(line.Text[start..end]);
                match.Bind(TextElement.BackgroundProperty, text.GetResourceObservable("Brush.ViewerMatch"));
                text.Inlines.Add(match);
                text.Inlines.Add(new Run(line.Text[end..]));
                if (line.MoreLines > 0)
                {
                    var more = new Run("  · " + Loc.Format(line.AtLeast ? AppStrings.Find_MoreLinesAtLeast : line.MoreLines == 1 ? AppStrings.Find_MoreLine : AppStrings.Find_MoreLines, line.MoreLines)) { FontFamily = FontFamily.Default };
                    more.Bind(TextElement.ForegroundProperty, text.GetResourceObservable("Brush.TertiaryText"));
                    text.Inlines.Add(more);
                }
            }

            Grid.SetRow(text, 1);
            Grid.SetColumnSpan(text, 5);
            grid.Children.Add(text);
        }

        return grid;

        static TextBlock Cell(string value, int column = 0, bool right = false)
        {
            var cell = new TextBlock
            {
                Text = value,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
                Margin = new Thickness(8, 0),
            };
            Grid.SetColumn(cell, column);
            return cell;
        }
    }

    private void UpdateColumnTitles()
    {
        foreach (var button in _columnHeader.Children.OfType<Button>())
        {
            var title = button.Tag switch
            {
                ResultColumn.Name => AppStrings.Find_ColName,
                ResultColumn.Folder => AppStrings.Find_ColFolder,
                ResultColumn.Size => AppStrings.Find_ColSize,
                ResultColumn.Modified => AppStrings.Find_ColModified,
                _ => AppStrings.Find_ColAttributes,
            };
            button.Content = button.Tag is ResultColumn c && c == _sortColumn && !_duplicates ? title + (_descending ? " ▼" : " ▲") : title;
        }
    }

    internal void Sort(ResultColumn column, bool toggle)
    {
        if (_duplicates)
        {
            return;
        }

        _descending = column == _sortColumn ? toggle != _descending : column is ResultColumn.Size or ResultColumn.Modified;
        _sortColumn = column;
        UpdateColumnTitles();
        SortAndShow();
    }

    private void SortAndShow()
    {
        var focused = Focused;
        if (!_duplicates)
        {
            _found.Sort(SearchResults.Sorter(_sortColumn, _descending));
        }

        ShowResults();
        if (focused is not null && _shown.IndexOf(focused) is var i and >= 0)
        {
            _list.SelectedIndex = i;
        }
        else if (_shown.Count > 0 && _list.SelectedIndex < 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private void ShowResults()
    {
        _shown.Clear();
        _shown.AddRange(_found);
    }

    private void AppendShown()
    {
        if (_found.Count > _shown.Count)
        {
            _shown.AddRange(_found.Skip(_shown.Count));
        }

        if (_list.SelectedIndex < 0 && _shown.Count > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private void DrainLog()
    {
        while (_logIncoming.TryDequeue(out var entry))
        {
            _log.Add(entry);
        }

        _logButton.Content = Loc.Format(AppStrings.Find_LogButton, _log.Count);
        _logButton.IsVisible = _log.Count > 0;
        if (LogWindow is { IsVisible: true } window)
        {
            window.Show(_log);
        }
    }

    private void UpdateHeader()
    {
        _header.Inlines!.Clear();
        _showInPanel.IsEnabled = !IsSearching && _found.Count > 0;
        if (_duplicates)
        {
            DuplicatesHeader(_header.Inlines);
            ShowStop();
            return;
        }

        var count = new Run(Loc.Format(_found.Count == 1 ? AppStrings.Find_FoundOne : AppStrings.Find_Found, _found.Count.ToString("N0", CultureInfo.CurrentCulture))) { FontWeight = FontWeight.SemiBold };
        count.Bind(TextElement.ForegroundProperty, _header.GetResourceObservable("Brush.Text"));
        if (IsSearching)
        {
            _header.Inlines.Add(count);
            var p = _lastProgress;
            _header.Inlines.Add(new Run("  ·  " + Loc.Format(AppStrings.Find_SoFar, p.Files.ToString("N0", CultureInfo.CurrentCulture), p.Folders.ToString("N0", CultureInfo.CurrentCulture))));
            if (p.CurrentFolder is { Length: > 0 })
            {
                var reading = new Run("  ·  " + Loc.Format(AppStrings.Find_Reading, p.CurrentFolder));
                reading.Bind(TextElement.ForegroundProperty, _header.GetResourceObservable("Brush.TertiaryText"));
                _header.Inlines.Add(reading);
            }
        }
        else if (_summary is { } s)
        {
            _header.Inlines.Add(count);
            var stats = s.Folders > 0 || s.Files > 0
                ? Loc.Format(AppStrings.Find_Searched, s.Files.ToString("N0", CultureInfo.CurrentCulture), s.Folders.ToString("N0", CultureInfo.CurrentCulture), s.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture))
                : "";
            if (stats.Length > 0)
            {
                _header.Inlines.Add(new Run("  ·  " + stats));
            }

            if (s.Cancelled)
            {
                _header.Inlines.Add(new Run("  ·  " + AppStrings.Find_Stopped));
            }
        }
        else
        {
            _header.Inlines.Add(new Run(AppStrings.Find_NothingYet));
        }

        ShowStop();
    }

    private void ShowStop()
    {
        if (IsSearching == _stopButton.IsVisible || _stopButton.Parent is not Panel buttons)
        {
            return;
        }

        if (IsSearching)
        {
            _stopButton.MinWidth = buttons.Bounds.Width;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        var inCriteria = focused is not null && buttons.Parent is Visual criteria && Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(criteria, focused);
        foreach (var button in buttons.Children)
        {
            button.IsVisible = button == _stopButton ? IsSearching : !IsSearching;
        }

        foreach (var box in (buttons.Parent as Panel)?.Children ?? [])
        {
            if (box != buttons)
            {
                box.IsEnabled = !IsSearching;
            }
        }

        if (IsSearching)
        {
            _focusBeforeSearch = inCriteria ? focused as InputElement : null;
            if (inCriteria)
            {
                _stopButton.Focus();
            }
        }
        else if (_focusBeforeSearch is { } before && (focused is null || focused == _stopButton || !focused.IsEffectivelyVisible))
        {
            _focusBeforeSearch = null;
            before.Focus();
        }
    }

    private InputElement? _focusBeforeSearch;

    internal SearchHit? Focused => _list.SelectedItem as SearchHit;

    internal IReadOnlyList<SearchHit> Selected => _list.SelectedItems?.OfType<SearchHit>().ToList() is { Count: > 0 } items ? items : Focused is { } f ? [f] : [];

    private void OnListKey(object? sender, KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        var handled = (e.Key, mods) switch
        {
            (Key.Enter, KeyModifiers.None) => Run(() => OpenItem(Focused)),
            (Key.Space, KeyModifiers.None) or (Key.J, KeyModifiers.Control) => Run(() => ShowInPanel(Focused)),
            (Key.F3, KeyModifiers.None) => Run(() => ViewItem(Focused)),
            (Key.F4, KeyModifiers.None) => Run(() => EditItem(Focused)),
            (Key.F5, KeyModifiers.None) => Run(() => CopyOrMove(OperationKind.Copy)),
            (Key.F6, KeyModifiers.None) => Run(() => CopyOrMove(OperationKind.Move)),
            (Key.F8 or Key.Delete, KeyModifiers.None) => Run(() => DeleteSelected(inverted: false)),
            (Key.F8 or Key.Delete, KeyModifiers.Shift) => Run(() => DeleteSelected(inverted: true)),
            (Key.F2, KeyModifiers.None) => Run(() => RenameItem(Focused)),
            (Key.H, KeyModifiers.Control) => Run(HideSelected),
            (Key.Enter, KeyModifiers.Alt) => Run(() => ShowProperties(windows: false)),
            (Key.Enter, KeyModifiers.Alt | KeyModifiers.Shift) => Run(() => ShowProperties(windows: true)),
            (Key.C, KeyModifiers.Control) or (Key.Insert, KeyModifiers.Control) => Run(() => CopyFiles(cut: false)),
            (Key.X, KeyModifiers.Control) or (Key.Delete, KeyModifiers.Shift | KeyModifiers.Control) => Run(() => CopyFiles(cut: true)),
            (Key.Insert, KeyModifiers.Alt) => Run(() => CopyText(h => h.FullPath)),
            (Key.Insert, KeyModifiers.Alt | KeyModifiers.Shift) => Run(() => CopyText(h => h.Name)),
            (Key.Insert, KeyModifiers.Control | KeyModifiers.Alt) => Run(() => CopyText(h => h.Folder)),
            _ => false,
        };
        e.Handled = handled;

        static bool Run(Action action)
        {
            action();
            return true;
        }
    }

    private bool InArchive(SearchHit? hit)
    {
        if (hit?.Archive is null)
        {
            return false;
        }

        _notice.Text = AppStrings.Find_InArchiveNotice;
        return true;
    }

    internal void OpenItem(SearchHit? hit)
    {
        _notice.Text = "";
        if (hit is null || InArchive(hit))
        {
            return;
        }

        if (hit.IsDirectory)
        {
            _request.OpenFolder(hit.FullPath);
        }
        else
        {
            _request.OpenFile(hit.FullPath);
        }
    }

    internal void ShowInPanel(SearchHit? hit)
    {
        _notice.Text = "";
        if (hit is null)
        {
            return;
        }

        if (hit.Archive is { } archive)
        {
            var slash = archive.LastIndexOf('\\');
            hit = new SearchHit(slash < 0 ? "" : archive[..slash], archive[(slash + 1)..], 0, 0, FileAttributes.Archive);
        }

        _request.ShowInPanel(hit);
    }

    internal void ViewItem(SearchHit? hit)
    {
        _notice.Text = "";
        if (hit is null || hit.IsDirectory || InArchive(hit))
        {
            return;
        }

        var siblings = _shown.Where(h => !h.IsDirectory && h.Archive is null).Select(h => h.FullPath).ToList();
        _request.View(hit.FullPath, siblings, _viewQuery);
    }

    internal void EditItem(SearchHit? hit)
    {
        _notice.Text = "";
        if (hit is null || hit.IsDirectory || InArchive(hit))
        {
            return;
        }

        _request.Edit(hit.FullPath);
    }

    internal void HideSelected()
    {
        var hide = Selected.ToHashSet();
        if (hide.Count == 0)
        {
            return;
        }

        var index = _list.SelectedIndex;
        _found.RemoveAll(hide.Contains);
        foreach (var hit in hide)
        {
            _shown.Remove(hit);
        }

        _list.SelectedIndex = Math.Min(Math.Max(index, 0), _shown.Count - 1);
        UpdateHeader();
        if (_duplicates)
        {
            Regroup();
        }
    }

    private void ShowProperties(bool windows)
    {
        if (Focused is not { Archive: null } focused)
        {
            return;
        }

        var selected = Selected.Where(h => h.Archive is null).ToList();
        if (selected.Count < 2)
        {
            _request.Properties(focused.Folder, [focused.Name], windows, this);
            return;
        }

        var root = SearchResults.CommonFolder(selected.Select(h => h.Folder));
        if (root.Length == 0)
        {
            var names = selected.Where(h => string.Equals(h.Folder, focused.Folder, StringComparison.OrdinalIgnoreCase)).Select(h => h.Name).ToList();
            _request.Properties(focused.Folder, names.Count > 0 ? names : [focused.Name], windows, this);
            return;
        }

        _request.Properties(root, [.. selected.Select(h => h.FullPath[root.Length..].TrimStart('\\'))], windows, this);
    }

    private void CopyFiles(bool cut)
    {
        var paths = Selected.Where(h => h.Archive is null).Select(h => h.FullPath).ToList();
        if (paths.Count > 0)
        {
            _request.Clipboard?.SetFiles(paths, cut);
        }
    }

    private void CopyText(Func<SearchHit, string> what)
    {
        var lines = Selected.Select(what).ToList();
        if (lines.Count > 0)
        {
            _request.Clipboard?.SetText(string.Join(Environment.NewLine, lines));
        }
    }

    internal void ShowLog()
    {
        if (LogWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }

        LogWindow = new FindLogWindow();
        LogWindow.Show(_log);
        LogWindow.Show(this);
    }
}

internal sealed class HitIcon : Control
{
    private readonly SearchHit _hit;
    private Panels.IconCache? _cache;

    public HitIcon(SearchHit hit)
    {
        _hit = hit;
        Width = 16;
        Height = 16;
        VerticalAlignment = VerticalAlignment.Center;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _cache = (Application.Current as App)?.Icons;
        if (_cache is not null)
        {
            _cache.Changed += OnChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_cache is not null)
        {
            _cache.Changed -= OnChanged;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var entry = new FileEntry(_hit.Name, _hit.Size, _hit.LastWriteUtcTicks, _hit.Attributes);
        if (_hit.Archive is null && _cache?.Get(_hit.Folder, entry, (int)Math.Round(16 * scale)) is { } bitmap)
        {
            context.DrawImage(bitmap, new Rect(0, 0, 16, 16));
            return;
        }

        var brush = this.FindResource("Brush.SecondaryText") as IBrush ?? Brushes.Gray;
        AppIcons.Draw(context, _hit.IsDirectory ? "Icon.Folder" : "Icon.File", new Point(0, 0), new Pen(brush, 1.2));
    }
}
