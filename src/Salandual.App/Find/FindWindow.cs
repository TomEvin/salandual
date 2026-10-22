// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Find/FindWindow.cs

using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.App.Theme;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Search;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;
using Salandual.Core.Viewer;

namespace Salandual.App.Find;

internal sealed record FindRequest(string LookIn)
{
    public IFileSystemProvider Files { get; init; } = new Salandual.Platform.Windows.FileSystem.LocalFileSystemProvider();

    public Func<string, Stream> Open { get; init; } = path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);

    public Func<string?> OtherPanelFolder { get; init; } = () => null;

    public Func<IReadOnlyList<string>> LocalDrives { get; init; } = () => [];

    public Action<SearchHit> ShowInPanel { get; init; } = _ => { };

    public Action<string> OpenFolder { get; init; } = _ => { };

    public Action<string> OpenFile { get; init; } = _ => { };

    public Action<string, IReadOnlyList<string>, FindQuery?> View { get; init; } = (_, _, _) => { };

    public Action<string> Edit { get; init; } = _ => { };

    public Action<string, IReadOnlyList<string>, bool, Window> Properties { get; init; } = (_, _, _, _) => { };

    public Action<string> OpenWith { get; init; } = _ => { };

    public Func<OperationKind, IReadOnlyList<SearchHit>, Window, Task<OperationJob?>> CopyOrMove { get; init; } = (_, _, _) => Task.FromResult<OperationJob?>(null);

    public Func<IReadOnlyList<SearchHit>, bool, Window, Task<OperationJob?>> Delete { get; init; } = (_, _, _) => Task.FromResult<OperationJob?>(null);

    public Action<SearchHit> Rename { get; init; } = _ => { };

    public Action<MenuItem, ContextMenu, string, IReadOnlyList<string>, Window> WindowsCommands { get; init; } = (_, _, _, _, _) => { };

    public Action<string, string> OpenDuplicates { get; init; } = (_, _) => { };

    public Action<IReadOnlyList<SearchHit>, string> ShowResults { get; init; } = (_, _) => { };

    public Action<string> OpenOptions { get; init; } = _ => { };

    public Func<string, bool> Exists { get; init; } = path => File.Exists(path) || Directory.Exists(path);

    public IClipboardAccess? Clipboard { get; init; }
}

internal sealed partial class FindWindow : Window
{
    private readonly SettingsService _settings;
    private readonly FindRequest _request;
    private readonly bool _editDefaults;
    private bool _closeConfirmed;
    private readonly ComboBox _named;
    private readonly ComboBox _lookIn;
    private readonly ComboBox _depth;
    private readonly NumericUpDown _levels;
    private readonly ToggleButton _insideZip;
    private readonly StackPanel _textRows = new() { Spacing = 6 };
    private readonly List<TextRow> _rows = [];
    private readonly ToggleButton _matchCase;
    private readonly ToggleButton _wholeWords;
    private readonly ComboBox _allOrAny;
    private readonly Control _allOrAnyGroup;
    private readonly TextBlock _problem;
    private readonly TextBlock _title = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _findButton;
    private readonly Border _progress;
    private SearchEncodings _encodings = SearchEncodings.Auto;
    private CancellationTokenSource? _stop;
    private readonly ConcurrentQueue<SearchHit> _incoming = new();
    private readonly List<SearchHit> _found = [];
    private readonly DispatcherTimer _flush = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private SearchProgress _lastProgress;
    private SearchSummary? _summary;
    private FindQuery? _viewQuery;
    private bool _focusOnFirst;
    private string _activeFolder;
    private string _what = "";

    public FindWindow(SettingsService settings, FindRequest request, bool editDefaults = false, bool duplicates = false)
    {
        _settings = settings;
        _request = request;
        _editDefaults = editDefaults;
        _duplicates = duplicates && !editDefaults;
        _activeFolder = request.LookIn;
        var find = settings.Current.Find;
        Width = find.WindowWidth;
        MinWidth = 480;
        if (editDefaults)
        {
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            Height = find.WindowHeight;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        this.Res(BackgroundProperty, "Brush.WindowBackground");
        Styles.Add(FindStyles.Create());

        _named = History(find.Named, "", AppStrings.Find_Named);
        NamedClear = AttachClear(_named);
        _lookIn = History(find.LookIn, "", AppStrings.Find_LookIn);
        if (editDefaults)
        {
            _lookIn.PlaceholderText = AppStrings.Find_ActivePanelFolder;
        }

        _depth = OptionsUi.Combo<int>([(0, AppStrings.Find_DepthAll), (1, AppStrings.Find_DepthNone), (2, AppStrings.Find_DepthDown)], 0, _ => UpdateDepth(), minWidth: 120);
        Avalonia.Automation.AutomationProperties.SetName(_depth, AppStrings.Find_Subfolders);
        _levels = new NumericUpDown { Minimum = 1, Maximum = 100, Increment = 1, Value = 2, FormatString = "0", Width = 124, IsVisible = false };
        Avalonia.Automation.AutomationProperties.SetName(_levels, AppStrings.Find_Levels);
        _insideZip = Toggle(AppStrings.Find_InsideArchives);
        ToolTip.SetTip(_insideZip, AppStrings.Find_InsideArchivesTip);

        _matchCase = Toggle(AppStrings.Find_MatchCase);
        _wholeWords = Toggle(AppStrings.Find_WholeWords);
        _allOrAny = OptionsUi.Combo<bool>([(true, AppStrings.Find_AllOfThem), (false, AppStrings.Find_AnyOfThem)], true, _ => UpdateTitle(), minWidth: 110);
        Avalonia.Automation.AutomationProperties.SetName(_allOrAny, AppStrings.Find_TheFileHas);
        _allOrAnyGroup = Row(OptionsUi.Text(AppStrings.Find_TheFileHas, 13), _allOrAny, OptionsUi.Text(AppStrings.Find_Anywhere, 12, brush: "Brush.TertiaryText"));
        _problem = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _problem.IsVisible = false;

        _findButton = new Button { Content = _duplicates ? AppStrings.Find_FindDuplicates : AppStrings.Find_Find, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        _findButton.Classes.Add("accent");
        _findButton.Click += (_, _) => _ = StartAsync(ResultMode.New);
        var findMore = new Button { Content = AppIcons.Glyph("Icon.ChevronDown", 10, 10, 1.6, "Brush.OnAccent"), Padding = new Thickness(8, 5), VerticalAlignment = VerticalAlignment.Stretch };
        findMore.Classes.Add("accent");
        Avalonia.Automation.AutomationProperties.SetName(findMore, AppStrings.Find_MoreWays);
        findMore.Flyout = FindMenu();

        _progress = new Border { Height = 3, Child = new ProgressBar { IsIndeterminate = true, MinHeight = 3, Height = 3, CornerRadius = new CornerRadius(0) }, Opacity = 0 };

        var dock = new DockPanel();
        if (editDefaults)
        {
            var criteria = BuildCriteria(null);
            var bar = BuildDefaultsBar();
            DockPanel.SetDock(bar, Dock.Bottom);
            dock.Children.Add(bar);
            dock.Children.Add(criteria);
        }
        else
        {
            var findButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Top };
            if (!_duplicates)
            {
                var saved = BuildSavedButton();
                saved.Margin = new Thickness(0, 0, 7, 0);
                findButtons.Children.Add(saved);
            }

            findButtons.Children.Add(_findButton);
            if (!_duplicates)
            {
                findButtons.Children.Add(findMore);
            }

            findButtons.Children.Add(_stopButton);

            var criteria = BuildCriteria(findButtons);
            var results = BuildResults();
            var keys = BuildKeys();
            DockPanel.SetDock(criteria, Dock.Top);
            DockPanel.SetDock(_progress, Dock.Top);
            DockPanel.SetDock(keys, Dock.Bottom);
            dock.Children.Add(criteria);
            dock.Children.Add(_progress);
            dock.Children.Add(keys);
            dock.Children.Add(results);
        }

        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { AppIcons.Glyph("Icon.Find", 16, 16, 1.3), _title } };
        Grid.SetColumn(_title, 1);
        _title.Res(TextBlock.ForegroundProperty, "Brush.Text");
        Content = TitleBarParts.ForWindow(this, title, dock);
        ApplyDefaults(find.Defaults);

        _flush.Tick += (_, _) => Flush();
        _named.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.TextProperty)
            {
                UpdateTitle();
            }
        };
        Opened += (_, _) =>
        {
            _named.Focus();
            SelectAllIn(_named);
        };
        Closing += OnClosing;
        Activated += (_, _) => DropRenamed();
        settings.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            settings.Changed -= OnSettingsChanged;
            _stop?.Cancel();
            _flush.Stop();
        };
    }

    internal void CloseWithSalandual()
    {
        _closeConfirmed = true;
        Close();
    }

    internal bool IsSearching => _stop is not null;

    internal Task? Searching { get; private set; }

    internal ComboBox NamedBox => _named;

    internal ComboBox LookInBox => _lookIn;

    internal ComboBox DepthBox => _depth;

    internal NumericUpDown LevelsBox => _levels;

    internal Button FindButton => _findButton;

    internal ToggleButton InsideZipBox => _insideZip;

    internal ToggleButton MatchCaseBox => _matchCase;

    internal ToggleButton WholeWordsBox => _wholeWords;

    internal ComboBox AllOrAnyBox => _allOrAny;

    internal TextBlock Problem => _problem;

    internal SettingsService Settings => _settings;

    internal Border ProgressStrip => _progress;

    internal IReadOnlyList<TextRow> TextRows => _rows;

    internal string TitleText => _title.Text ?? "";

    internal Button NamedClear { get; }

    internal bool EditsDefaults => _editDefaults;

    internal ConfirmDialog? CloseQuestion { get; private set; }

    internal SearchEncodings Encodings
    {
        get => _encodings;
        set
        {
            _encodings = value == SearchEncodings.None ? SearchEncodings.Auto : value;
            if (_encodingChip.Flyout is not null)
            {
                UpdateEncodingChip();
                _syncEncodings?.Invoke();
            }
        }
    }

    internal void LookInFolder(string folder)
    {
        if (!IsSearching && folder.Length > 0)
        {
            _activeFolder = folder;
            _lookIn.Text = folder;
        }
    }

    internal static ToggleButton Toggle(string text)
    {
        var toggle = new ToggleButton { Content = text, VerticalAlignment = VerticalAlignment.Center };
        toggle.Classes.Add("opt");
        return toggle;
    }

    private static ComboBox History(IReadOnlyList<string> items, string text, string name)
    {
        var box = new ComboBox { IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 300, Text = text };
        foreach (var item in items)
        {
            box.Items.Add(item);
        }

        Avalonia.Automation.AutomationProperties.SetName(box, name);
        return box;
    }

    internal static void SelectAllIn(Control box)
    {
        if (TextBoxIn(box) is { } text)
        {
            text.SelectAll();
        }
    }

    internal static TextBox? TextBoxIn(Control box)
        => box as TextBox ?? Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(box).OfType<TextBox>().FirstOrDefault();

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children)
        {
            child.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(child);
        }

        return row;
    }

    private void UpdateDepth()
    {
        _levels.IsVisible = _depth.SelectedIndex == 2;
    }

    private void UpdateTitle()
    {
        if (_editDefaults)
        {
            _title.Text = Title = AppStrings.Find_DefaultsTitle;
            return;
        }

        if (_duplicates)
        {
            _what = (_lookIn.Text ?? "").Trim();
            _title.Text = Title = Loc.Format(AppStrings.Find_DupTitle, _what);
            return;
        }

        var named = (_named.Text ?? "").Trim();
        var all = _allOrAny.SelectedIndex != 1;
        var has = _rows.Where(r => r.Contains && r.Text.Length > 0).Select(r => "“" + r.Text + "”").ToList();
        var not = _rows.Where(r => !r.Contains && r.Text.Length > 0).Select(r => "“" + r.Text + "”").ToList();
        var what = named.Length > 0 ? named : "*.*";
        if (has.Count > 0)
        {
            what = Loc.Format(AppStrings.Find_TitleContaining, what, string.Join(all ? AppStrings.Find_TitleAnd : AppStrings.Find_TitleOr, has));
        }

        if (not.Count > 0)
        {
            what = Loc.Format(AppStrings.Find_TitleNot, what, string.Join(", ", not));
        }

        _what = what;
        _title.Text = Loc.Format(AppStrings.Find_Title, what);
        Title = _title.Text;
    }

    internal SearchCriteria? ReadCriteria()
    {
        _problem.IsVisible = false;
        var lookIn = (_lookIn.Text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lookIn.Length == 0)
        {
            return Fail(AppStrings.Find_NoLookIn);
        }

        var criteria = new SearchCriteria
        {
            Named = (_named.Text ?? "").Trim(),
            LookIn = lookIn,
            Depth = _depth.SelectedIndex switch { 0 => null, 1 => 0, _ => (int)(_levels.Value ?? 1) },
            InsideZip = _insideZip.IsChecked == true,
            Texts = [.. _rows.Select(r => r.Read())],
            AllTexts = _allOrAny.SelectedIndex != 1,
            MatchCase = _matchCase.IsChecked == true,
            WholeWords = _wholeWords.IsChecked == true,
            Encodings = _encodings,
            SkippedFolders = _settings.Current.Find.SkippedFolders,
        };
        if (AdvancedProblem() is { } advancedProblem)
        {
            return Fail(advancedProblem);
        }

        criteria = ApplyMore(criteria);
        return TextsProblem(criteria) is { } problem ? Fail(problem) : criteria;

        SearchCriteria? Fail(string message)
        {
            _problem.Text = message;
            _problem.IsVisible = true;
            return null;
        }
    }

    private string? TextsProblem(SearchCriteria criteria)
        => criteria.HasTexts && ContentMatcher.Create(criteria, out var problem) is null && problem is not null
            ? _rows.Count > 1 ? Loc.Format(AppStrings.Find_ProblemInRow, problem.Row + 1, problem.Message) : problem.Message
            : null;

    internal void ApplyDefaults(SearchDefaults d)
    {
        _named.Text = d.Named;
        _lookIn.Text = d.LookIn.Length > 0 || _editDefaults ? d.LookIn : _activeFolder;
        _depth.SelectedIndex = d.Depth switch { null => 0, 0 => 1, _ => 2 };
        _levels.Value = d.Depth is > 0 and var levels ? levels : d.Levels;
        UpdateDepth();
        _insideZip.IsChecked = d.InsideZip;
        _matchCase.IsChecked = d.MatchCase;
        _wholeWords.IsChecked = d.WholeWords;
        _allOrAny.SelectedIndex = d.AllTexts ? 0 : 1;
        Encodings = d.Encodings;
        foreach (var row in _rows)
        {
            _textRows.Children.Remove(row.View);
        }

        _rows.Clear();
        foreach (var text in d.Texts.Count > 0 ? d.Texts : [new SearchText("")])
        {
            AddTextRow(text);
        }

        ApplyAdvanced(d);
        _problem.IsVisible = false;
        UpdateTitle();
        UpdateRows();
    }

    internal SearchDefaults ReadDefaults() => ReadAdvanced(new SearchDefaults
    {
        Named = (_named.Text ?? "").Trim(),
        LookIn = (_lookIn.Text ?? "").Trim(),
        Texts = [.. _rows.Select(r => r.Read()).Where(t => t.Text.Length > 0)],
        AllTexts = _allOrAny.SelectedIndex != 1,
        Depth = _depth.SelectedIndex switch { 0 => null, 1 => 0, _ => (int)(_levels.Value ?? 1) },
        Levels = (int)(_levels.Value ?? 2),
        InsideZip = _insideZip.IsChecked == true,
        MatchCase = _matchCase.IsChecked == true,
        WholeWords = _wholeWords.IsChecked == true,
        Encodings = _encodings,
    });

    internal bool SaveDefaults()
    {
        var check = new SearchCriteria { Texts = [.. _rows.Select(r => r.Read())], MatchCase = _matchCase.IsChecked == true, WholeWords = _wholeWords.IsChecked == true, Encodings = _encodings };
        if ((AdvancedProblem() ?? TextsProblem(check)) is { } problem)
        {
            _problem.Text = problem;
            _problem.IsVisible = true;
            return false;
        }

        var defaults = ReadDefaults();
        _settings.Update(s => s with { Find = s.Find with { Defaults = defaults } });
        Close();
        return true;
    }

    private Control BuildDefaultsBar()
    {
        var note = OptionsUi.Text(AppStrings.Find_DefaultsNote, 12, brush: "Brush.TertiaryText", wrap: true);
        note.VerticalAlignment = VerticalAlignment.Center;
        var builtIn = new Button { Content = AppStrings.Find_BuiltIn, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(builtIn, AppStrings.Find_BuiltInTip);
        builtIn.Click += (_, _) => ApplyDefaults(new SearchDefaults());
        var save = new Button { Content = AppStrings.Common_Save, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        save.Classes.Add("accent");
        save.Click += (_, _) => SaveDefaults();
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { builtIn, save, cancel } };
        var dock = new DockPanel { Margin = new Thickness(14, 10) };
        DockPanel.SetDock(buttons, Dock.Right);
        buttons.Margin = new Thickness(16, 0, 0, 0);
        dock.Children.Add(buttons);
        dock.Children.Add(note);
        return new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = dock }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.Cancel || _editDefaults)
        {
            return;
        }

        if (!_closeConfirmed && e.CloseReason == WindowCloseReason.WindowClosing && _settings.Current.Confirmations.CloseSearch)
        {
            e.Cancel = true;
            if (CloseQuestion is not null)
            {
                return;
            }

            var dialog = CloseQuestion = new ConfirmDialog(AppStrings.Find_Close_Title, IsSearching ? AppStrings.Find_Close_Searching : AppStrings.Find_Close_Text, AppStrings.Common_Close, AppStrings.Common_Cancel);
            bool close;
            try
            {
                close = await dialog.ShowDialog<bool>(this);
            }
            finally
            {
                CloseQuestion = null;
            }

            if (close)
            {
                _closeConfirmed = true;
                Close();
            }

            return;
        }

        Remember(window: true);
    }

    internal Task StartAsync(ResultMode mode)
    {
        if (IsSearching || ReadCriteria() is not { } criteria)
        {
            return Task.CompletedTask;
        }

        if (_duplicates)
        {
            return StartDuplicatesAsync(criteria);
        }

        Remember(window: false);
        RefreshHistory();
        if (mode != ResultMode.Remove)
        {
            _viewQuery = ViewQuery(criteria) ?? (mode == ResultMode.New ? null : _viewQuery);
        }

        _focusOnFirst = _settings.Current.Find.FocusResults;
        var stop = _stop = new CancellationTokenSource();
        var before = _found.ToList();
        if (mode == ResultMode.New)
        {
            _found.Clear();
            ShowResults();
        }

        _log.Clear();
        _incoming.Clear();
        _summary = null;
        _lastProgress = default;
        _progress.Opacity = 1;
        _findButton.IsEnabled = false;
        UpdateHeader();
        _flush.Start();

        var search = new FileSearch(_request.Files, _request.Open);
        var callbacks = new SearchCallbacks
        {
            Found = hit => _incoming.Enqueue(hit),
            Progress = p => _lastProgress = p,
            Log = entry => _logIncoming.Enqueue(entry),
        };
        var refine = mode is ResultMode.Keep or ResultMode.Remove;
        var work = Task.Run(async () =>
        {
            if (refine)
            {
                var kept = search.Filter(criteria, before, callbacks, stop.Token);
                return (new SearchSummary(0, before.Count, TimeSpan.Zero, false), (IReadOnlyList<SearchHit>?)kept);
            }

            return (await search.RunAsync(criteria, callbacks, stop.Token).ConfigureAwait(false), null);
        });
        return Searching = Finish();

        async Task Finish()
        {
            SearchSummary summary;
            IReadOnlyList<SearchHit>? kept = null;
            try
            {
                (summary, kept) = await work;
            }
            catch (OperationCanceledException)
            {
                summary = new SearchSummary(_lastProgress.Folders, _lastProgress.Files, TimeSpan.Zero, true);
            }

            _flush.Stop();
            var found = new List<SearchHit>();
            while (_incoming.TryDequeue(out var hit))
            {
                found.Add(hit);
            }

            if (refine)
            {
                _found.Clear();
                _found.AddRange(summary.Cancelled ? before : SearchResults.Combine(before, kept ?? [], mode));
            }
            else
            {
                var all = mode == ResultMode.New ? [.. _found, .. found] : SearchResults.Combine(before, [.. _found.Except(before), .. found], ResultMode.Add);
                _found.Clear();
                _found.AddRange(all);
            }

            DrainLog();
            _summary = summary;
            _stop = null;
            stop.Dispose();
            _progress.Opacity = 0;
            _findButton.IsEnabled = true;
            SortAndShow();
            UpdateHeader();
            FocusResultsOnce();
        }
    }

    private static FindQuery? ViewQuery(SearchCriteria criteria)
        => criteria.Texts.FirstOrDefault(t => t.Contains && t.Text.Length > 0) is { } text
            ? new FindQuery(text.Text, text.Kind == TextKind.Hex, criteria.MatchCase, criteria.WholeWords, text.Kind == TextKind.Regex)
            : null;

    internal FindQuery? ViewerQuery => _viewQuery;

    private void FocusResultsOnce()
    {
        if (!_focusOnFirst || _shown.Count == 0)
        {
            return;
        }

        _focusOnFirst = false;
        if (_list.SelectedIndex < 0)
        {
            _list.SelectedIndex = 0;
        }

        FocusList();
    }

    internal void FocusList()
    {
        _list.Focus();
        Dispatcher.UIThread.Post(() =>
        {
            if (_list.SelectedIndex >= 0 && _list.ContainerFromIndex(_list.SelectedIndex) is { } row)
            {
                row.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!e.Old.Find.SkippedFolders.SequenceEqual(e.New.Find.SkippedFolders, StringComparer.Ordinal))
        {
            Dispatcher.UIThread.Post(() =>
            {
                FillSkipped();
                UpdateSummary();
            });
        }
    }

    private void RefreshHistory()
    {
        var find = _settings.Current.Find;
        Refill(_named, find.Named);
        Refill(_lookIn, find.LookIn);
        foreach (var row in _rows)
        {
            Refill(row.Box, find.Texts);
        }

        static void Refill(ComboBox box, IReadOnlyList<string> items)
        {
            var text = box.Text;
            box.SelectedIndex = -1;
            box.Items.Clear();
            foreach (var item in items)
            {
                box.Items.Add(item);
            }

            box.Text = text;
        }
    }

    internal void Stop() => _stop?.Cancel();

    private void Flush()
    {
        var added = false;
        while (_incoming.TryDequeue(out var hit))
        {
            _found.Add(hit);
            added = true;
        }

        DrainLog();
        if (added)
        {
            AppendShown();
        }

        UpdateHeader();
    }

    private void Remember(bool window)
    {
        var named = (_named.Text ?? "").Trim();
        var lookIn = (_lookIn.Text ?? "").Trim();
        var texts = _rows.Select(r => r.Text).Where(t => t.Length > 0).ToList();
        var maximized = WindowState == WindowState.Maximized;
        var size = Bounds.Size;
        _settings.Update(s => s with
        {
            Find = s.Find with
            {
                Named = window ? s.Find.Named : HistorySettings.Push(s.Find.Named, named),
                LookIn = window ? s.Find.LookIn : HistorySettings.Push(s.Find.LookIn, lookIn),
                Texts = window ? s.Find.Texts : texts.AsEnumerable().Reverse().Aggregate(s.Find.Texts, HistorySettings.Push),
                WindowWidth = window && !maximized && size.Width > 0 ? size.Width : s.Find.WindowWidth,
                WindowHeight = window && !maximized && size.Height > 0 ? size.Height : s.Find.WindowHeight,
                WindowMaximized = window ? maximized : s.Find.WindowMaximized,
            },
        });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (_editDefaults)
        {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && !AnyDropDownOpen())
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && !AnyDropDownOpen())
            {
                SaveDefaults();
                e.Handled = true;
            }

            return;
        }

        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        switch (e.Key)
        {
            case Key.Escape when IsSearching:
                Stop();
                e.Handled = true;
                break;
            case Key.Escape when e.KeyModifiers == KeyModifiers.None && !AnyDropDownOpen():
                Close();
                e.Handled = true;
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && !AnyDropDownOpen():
                _ = StartAsync(ResultMode.New);
                e.Handled = true;
                break;
            case Key.W when ctrl && !_duplicates:
                _ = StartAsync(ResultMode.Add);
                e.Handled = true;
                break;
            case Key.I when ctrl && !_duplicates:
                _ = StartAsync(ResultMode.Keep);
                e.Handled = true;
                break;
            case Key.S when ctrl && !_duplicates:
                _ = StartAsync(ResultMode.Remove);
                e.Handled = true;
                break;
            case Key.D when ctrl && !_duplicates:
                _ = SaveSearchAsync();
                e.Handled = true;
                break;
            case >= Key.D1 and <= Key.D9 when ctrl && !_duplicates:
                e.Handled = ApplySaved(e.Key - Key.D1);
                break;
            case Key.P when ctrl:
                ShowResultsInPanel();
                e.Handled = true;
                break;
            case Key.L when ctrl:
                ShowLog();
                e.Handled = true;
                break;
        }
    }

    private bool AnyDropDownOpen()
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<ComboBox>().Any(c => c.IsDropDownOpen);

    internal static long? Bytes(string? number, int unit)
    {
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || value < 0)
        {
            return null;
        }

        return (long)Math.Min(value * Math.Pow(1024, unit), long.MaxValue / 2);
    }
}
