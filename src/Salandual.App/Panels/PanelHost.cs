// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/PanelHost.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Panels;

public sealed class PanelHost : Border
{
    private readonly Grid _grid;
    private ChangeDirectoryBox? _cd;
    private Border? _cdBar;
    private Border? _cdList;
    private string _infoTemplate = PanelSettings.DefaultInformationLine;
    private int _spaceVersion;
    private InlineRename? _rename;
    private Border? _resultsBar;
    private TextBlock? _resultsText;
    private Button? _resultsBack;
    private Button? _resultsJump;

    public PanelHost(FilePanel panel, CommandRegistry commands)
    {
        Panel = panel;
        Tabs = new TabStrip { IsVisible = false };
        Strip = new DriveStrip();
        DirectoryLine = new DirectoryLine();
        View = new FilePanelView { Panel = panel, Commands = commands };
        Info = new InformationLineView();

        var grid = _grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        grid.Children.Add(Strip);
        Grid.SetRow(DirectoryLine, 1);
        grid.Children.Add(DirectoryLine);
        Grid.SetRow(View, 2);
        grid.Children.Add(View);
        Grid.SetRow(Info, 3);
        grid.Children.Add(Info);

        var dock = new DockPanel();
        DockPanel.SetDock(Tabs, Dock.Top);
        dock.Children.Add(Tabs);
        dock.Children.Add(grid);
        Child = dock;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
        Bind(BorderBrushProperty, this.GetResourceObservable("Brush.Border"));
        Bind(BackgroundProperty, this.GetResourceObservable("Brush.PanelBackground"));

        panel.Changed += OnPanelChanged;
        DirectoryLine.View = panel.View;
        View.WaitingChanged += (_, _) => UpdateInfo();
        View.LockChanged += (_, _) =>
        {
            var free = !View.IsLocked;
            Strip.IsHitTestVisible = DirectoryLine.IsHitTestVisible = Tabs.IsHitTestVisible = free;
            Strip.Opacity = DirectoryLine.Opacity = Tabs.Opacity = free ? 1 : 0.55;
        };
        DirectoryLine.NavigateRequested += (_, path) => GoTo(path);
        DirectoryLine.FilterOffRequested += (_, _) => { Panel.ClearFilter(); View.Focus(); };
        DirectoryLine.ShowNamesRequested += (_, _) => { Panel.ShowHiddenNames(); View.Focus(); };
    }

    public event EventHandler<string>? NoticeRequested;

    internal void ShowError(string message) => NoticeRequested?.Invoke(this, message);

    public event EventHandler<(string From, string To)>? Renamed;

    internal void RaiseRenamed(string from, string to) => Renamed?.Invoke(this, (from, to));

    public bool IsRenaming => _rename?.IsOpen == true;

    internal InlineRename? Rename => _rename;

    public void BeginRename(Salandual.Core.Operations.IOperationFileSystem fs)
    {
        if (_rename is null)
        {
            var layer = new Canvas { ClipToBounds = true, ZIndex = 5 };
            Grid.SetRow(layer, 2);
            Grid.SetRowSpan(layer, 2);
            _grid.Children.Add(layer);
            _rename = new InlineRename(this, layer);
        }

        _rename.Begin(fs);
    }

    public void CancelRename() => _rename?.Cancel();

    private ChangeDirectoryBox EnsureChangeDirectory()
    {
        if (_cd is not null)
        {
            return _cd;
        }

        var cd = _cd = new ChangeDirectoryBox();
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        row.Children.Add(cd.Input);
        Grid.SetColumn(cd.BrowseButton, 1);
        row.Children.Add(cd.BrowseButton);
        _cdBar = new Border { Child = row, IsVisible = false, Padding = new Thickness(0, 4, 8, 4), Margin = new Thickness(0, 0, 0, 1) };
        _cdBar.Bind(BackgroundProperty, _cdBar.GetResourceObservable("Brush.ColumnHeader"));
        Grid.SetRow(_cdBar, 1);
        _grid.Children.Add(_cdBar);

        _cdList = cd.List;
        _cdList.IsVisible = false;
        _cdList.VerticalAlignment = VerticalAlignment.Top;
        _cdList.CornerRadius = new CornerRadius(0, 0, 8, 8);
        _cdList.ZIndex = 10;
        Grid.SetRow(_cdList, 2);
        Grid.SetRowSpan(_cdList, 2);
        _grid.Children.Add(_cdList);

        cd.Accepted += (_, target) =>
        {
            EndChangeDirectory();
            _ = Panel.NavigateAsync(target.Folder, target.FocusName);
        };
        cd.Cancelled += (_, _) => EndChangeDirectory();
        cd.Input.LostFocus += (_, _) => Dispatcher.UIThread.Post(CloseIfFocusLeft, DispatcherPriority.Background);
        return cd;
    }

    public FilePanel Panel { get; private set; }

    public void Attach(FilePanel panel)
    {
        if (ReferenceEquals(panel, Panel))
        {
            return;
        }

        Panel.Changed -= OnPanelChanged;
        Panel = panel;
        panel.Changed += OnPanelChanged;
        View.Panel = panel;
        if (_settings is { } settings)
        {
            ApplyModel(panel, settings);
        }

        OnPanelChanged(panel, PanelChange.Items | PanelChange.Focus | PanelChange.Selection | PanelChange.View);
    }

    public TabStrip Tabs { get; }

    private AppSettings? _settings;

    public event EventHandler<string>? PathChanged;

    public DriveStrip Strip { get; }

    public DirectoryLine DirectoryLine { get; }

    public FilePanelView View { get; }

    public InformationLineView Info { get; }

    public bool IsActive
    {
        get => View.IsActivePanel;
        set
        {
            View.IsActivePanel = value;
            DirectoryLine.IsActive = value;
        }
    }

    public bool IsEditingPath => _cdBar?.IsVisible == true;

    internal ChangeDirectoryBox ChangeDirectory => EnsureChangeDirectory();

    public void ApplySettings(AppSettings settings, DisplayFormat format)
    {
        _settings = settings;
        var bars = settings.Layout.Bars;
        View.RowHeight = Math.Round(Salandual.App.Theme.TextScale.Of(settings.Layout.RowHeight));
        View.ClickToRename = settings.Panels.ClickToRename;
        View.ViewColumns = settings.Panels.ColumnViews;
        View.FolderSize = settings.Panels.FolderSize;
        View.ColumnAlignment = settings.Panels.ColumnAlignment;
        View.LongNames = settings.Panels.LongNames;
        View.GitColors = settings.Panels.Git.ColorNames;
        View.GitColumnAuto = settings.Panels.Git.ColumnInRepositories;
        View.RowStripes = settings.Panels.RowStripes;
        View.FocusLook = settings.Panels.FocusLook;
        View.SelectionLook = settings.Panels.SelectionLook;
        View.FocusBackground = settings.Panels.FocusBackground;
        View.FocusBackgroundLook = settings.Panels.FocusBackgroundLook;
        View.RightButtonSelects = settings.Mouse.RightClickUnselected == RightClickUnselected.RightButtonSelects;
        View.QuickSearchWithCtrlAlt = settings.Panels.QuickSearch == QuickSearchStart.CtrlAltLetter;
        View.ShowHeader = bars.ColumnHeader;
        View.Format = format;
        DirectoryLine.Format = format;
        Strip.IsVisible = bars.DriveStrip && settings.Layout.Strip == StripMode.PerPanel;
        DirectoryLine.IsVisible = bars.DirectoryLine;
        DirectoryLine.ShowMaximizeButton = settings.Layout.DirectoryLine.MaximizeButton;
        DirectoryLine.ShowFreeSpace = settings.DriveMenu.FreeSpaceInLine;
        DirectoryLine.ShowViewButton = settings.Panels.ViewButton;
        View.ThumbnailSize = settings.Panels.ThumbnailSize;
        View.TileLines = settings.Panels.TileLines;
        View.AltWheelViews = settings.Panels.AltWheelViews;
        View.ColumnChars = settings.Panels.ColumnsWidth;
        View.ColumnsPreview = settings.Panels.ColumnsPreview;
        View.PeekOnHover = settings.Panels.PeekOnHover;
        View.Animate = Salandual.Platform.Windows.Shell.SystemFacts.AnimationEffects();
        Info.IsVisible = bars.InformationLine;
        _infoTemplate = settings.Panels.InformationLine;
        ApplyModel(Panel, settings);
        UpdateInfo();
    }

    internal static void ApplyModel(FilePanel panel, AppSettings settings)
    {
        panel.SelectFoldersWithNumPlus = settings.Panels.SelectFoldersWithNumPlus;
        panel.FilterThisFolderOnly = settings.Panels.FilterThisFolderOnly;
        panel.HiddenNamesStay = settings.Panels.HiddenNamesStay;
        panel.QuickFilterFirst = settings.Panels.QuickFilterFirst;
        panel.FlatAskAbove = settings.Panels.FlatAskAbove;
        panel.SetFlatFolderShows(settings.Panels.FlatFolders);
        panel.SetHideDotFiles(settings.Panels.HideDotFiles);
    }

    public void GoTo(string path)
    {
        _ = Panel.NavigateAsync(path);
        View.Focus();
    }

    public void BeginChangeDirectory(ChangeDirectoryContext context)
    {
        var cd = EnsureChangeDirectory();

        var left = DirectoryLine.IsVisible ? DirectoryLine.DriveButtonBounds.Right + 6 : 6;
        _cdBar!.Margin = new Thickness(left - 4, 0, 0, 1);
        _cdBar.Padding = new Thickness(4, 4, 8, 4);
        _cdBar.MinHeight = DirectoryLine.IsVisible ? 0 : 34;
        _cdList!.Margin = new Thickness(left, -1, 8, 0);
        _cdBar.IsVisible = true;
        _cdList.IsVisible = true;
        cd.Open(context);
        cd.Input.Focus();
    }

    private void EndChangeDirectory()
    {
        if (_cd is null || _cdBar is not { IsVisible: true } || _cdList is null)
        {
            return;
        }

        var hadFocus = _cd.Input.IsFocused;
        _cdBar.IsVisible = false;
        _cdList.IsVisible = false;
        if (hadFocus)
        {
            View.Focus();
        }
    }

    private void CloseIfFocusLeft()
    {
        if (_cd is null || _cdBar is not { IsVisible: true } || _cdList is null || _cd.IsBusyElsewhere || _cd.Input.IsFocused)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused && (_cdBar.IsVisualAncestorOf(focused) || _cdList.IsVisualAncestorOf(focused)))
        {
            return;
        }

        EndChangeDirectory();
    }

    internal Border? ResultsBar => _resultsBar;

    internal string ResultsText => _resultsText?.Text ?? "";

    internal Button? ResultsBack => _resultsBack;

    internal Button? ResultsJump => _resultsJump;

    internal Button? ResultsStop => _resultsStop;

    private Button? _resultsStop;
    private ContentControl? _resultsIcon;
    private string? _resultsKind;

    public Func<PanelResults, string>? GitListText { get; set; }

    private void UpdateResultsBar()
    {
        if (Panel.Results is not { } results)
        {
            if (_resultsBar is not null)
            {
                _resultsBar.IsVisible = false;
            }

            return;
        }

        if (_resultsBar is null)
        {
            _resultsText = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _resultsText.Bind(TextBlock.ForegroundProperty, _resultsText.GetResourceObservable("Brush.Accent"));
            _resultsBack = new Button { Padding = new Thickness(8, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            _resultsBack.Click += (_, _) =>
            {
                _ = Panel.LeaveResultsAsync();
                View.Focus();
            };
            _resultsJump = new Button { Padding = new Thickness(8, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            _resultsJump.Click += (_, _) =>
            {
                _ = Panel.GoToResultAsync();
                View.Focus();
            };
            _resultsStop = new Button { Content = AppStrings.Flat_Stop, Padding = new Thickness(8, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            ToolTip.SetTip(_resultsStop, AppStrings.Flat_StopTip);
            _resultsStop.Click += (_, _) =>
            {
                Panel.StopFlat();
                View.Focus();
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 8 };
            _resultsIcon = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(_resultsIcon);
            Grid.SetColumn(_resultsText, 1);
            row.Children.Add(_resultsText);
            Grid.SetColumn(_resultsStop, 2);
            row.Children.Add(_resultsStop);
            Grid.SetColumn(_resultsJump, 3);
            row.Children.Add(_resultsJump);
            Grid.SetColumn(_resultsBack, 4);
            row.Children.Add(_resultsBack);
            _resultsBar = new Border { Child = row, Padding = new Thickness(10, 3, 6, 3), MinHeight = 30 };
            _resultsBar.Bind(BackgroundProperty, _resultsBar.GetResourceObservable("Brush.InfoBackground"));
            _resultsBar.PointerPressed += (_, _) => View.Focus();
            Grid.SetRow(_resultsBar, 1);
            _grid.Children.Add(_resultsBar);
        }

        var back = WindowsPath.GetName(results.ReturnPath) is { Length: > 0 } name ? name : results.ReturnPath;
        var gitList = results.Changes is not null || results.History is not null || results.Commit is not null || results.Tree is not null || results.Tags is not null;
        var kind = gitList ? "Icon.GitBranch" : results.Flat is not null ? "Panel.FlatView" : "Icon.Find";
        if (_resultsKind != kind)
        {
            _resultsKind = kind;
            _resultsIcon!.Content = Salandual.App.Theme.AppIcons.Glyph(kind, 14, 14, 1.4, "Brush.Accent");
        }

        _resultsText!.Text = gitList ? results.What + (GitListText?.Invoke(results) is { Length: > 0 } words ? " · " + words : "")
            : results.Flat is { } flat ? FlatText(flat, back) : string.Join(" · ", new[] { AppStrings.Panel_ResultsHead, results.What, Loc.Format(AppStrings.Panel_ResultsCount, Panel.Count) }.Where(s => s.Length > 0));
        _resultsStop!.IsVisible = results.Flat is { Reading: true };
        _resultsBack!.Content = "← " + (results.Flat is not null || gitList || Bounds.Width is > 0 and < 420 ? AppStrings.Panel_ResultsBackShort : Loc.Format(AppStrings.Panel_ResultsBack, back));
        ToolTip.SetTip(_resultsBack, Loc.Format(results.Flat is not null ? AppStrings.Flat_BackTip : AppStrings.Panel_ResultsBackTip, results.ReturnPath));
        _resultsJump!.Content = "↗ " + (Bounds.Width is > 0 and < 420 ? AppStrings.Panel_ResultsJumpShort : AppStrings.Panel_ResultsJump);
        ToolTip.SetTip(_resultsJump, AppStrings.Panel_ResultsJumpTip);
        _resultsJump.IsEnabled = Panel.Count > 0;
        _resultsJump.IsVisible = results.Tree is null && results.Tags is null;
        _resultsBar.IsVisible = true;
    }

    private string FlatText(PanelFlat flat, string folder)
    {
        var head = flat.Folders.Count == 0 ? Loc.Format(AppStrings.Flat_Head, folder)
            : flat.Folders.Count == 1 ? Loc.Format(AppStrings.Flat_HeadOne, flat.Folders[0], folder)
            : Loc.Format(AppStrings.Flat_HeadFolders, flat.Folders.Count, folder);
        var files = Panel.ShownCount - Enumerable.Range(0, Panel.Count).Count(Panel.IsHeading);
        var count = files == flat.Files
            ? Loc.Plural(flat.Files, AppStrings.Flat_Files_One, AppStrings.Flat_Files_Other)
            : Loc.Format(AppStrings.Flat_FilesOf, files, flat.Files);
        var where = Loc.Plural(flat.FoldersRead, AppStrings.Flat_InFolders_One, AppStrings.Flat_InFolders_Other);
        return flat.Reading ? $"{head}: {AppStrings.Flat_Reading} · {count} {where}"
            : flat.Stopped ? $"{head} · {count} {where} · {AppStrings.Flat_Stopped}"
            : $"{head} · {count} {where}";
    }

    private void OnPanelChanged(object? sender, PanelChange change)
    {
        if ((change & PanelChange.View) != 0)
        {
            DirectoryLine.View = Panel.View;
        }

        if ((change & PanelChange.Items) != 0)
        {
            UpdateResultsBar();
            UpdateChips();
        }

        if ((change & PanelChange.Items) != 0 && DirectoryLine.Path != Panel.Path && Panel.Results is null)
        {
            DirectoryLine.Path = Panel.Path;
            Strip.Current = WindowsPath.GetRoot(Panel.Path);
            ReadFreeSpace(Panel.Path);
            PathChanged?.Invoke(this, Panel.Path);
        }

        if ((change & (PanelChange.Items | PanelChange.Focus | PanelChange.Selection | PanelChange.Sizes | PanelChange.QuickSearch)) != 0)
        {
            UpdateInfo();
        }
    }

    private void UpdateChips()
    {
        var results = Panel.Results is not null && !Panel.IsFlat;
        string[] masks = [.. Panel.Filter is { } f ? [f.Mask.Text] : Array.Empty<string>(), .. Panel.KeptQuickFilters.Select(k => "*" + k + "*")];
        DirectoryLine.FilterChip = results || masks.Length == 0 ? null : string.Join("  ", masks);
        var names = Panel.HiddenNameCount;
        DirectoryLine.NamesChip = results || names == 0 ? null : Loc.Format(names == 1 ? Salandual.Core.Resources.Strings.InfoLine_NamesHidden_One : Salandual.Core.Resources.Strings.InfoLine_NamesHidden_Other, names);
    }

    public Func<TimeSpan>? ServerTimeout { get; set; }

    public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(10);

    public Func<Core.Vfs.FileEntry, string?>? InfoInstead { get; set; }

    public Func<Core.Vfs.FileEntry, string?>? InfoExtra { get; set; }

    public Func<Core.Vfs.FileEntry, string?>? GitInfo
    {
        get => _gitInfo;
        set
        {
            _gitInfo = value;
            UpdateInfo();
        }
    }

    private Func<Core.Vfs.FileEntry, string?>? _gitInfo;

    internal void RefreshInfo() => UpdateInfo();

    private void UpdateInfo()
    {
        var format = View.Format;
        var left = Panel.FocusedEntry is { } own && !Panel.IsUp(Panel.Focus) && InfoInstead?.Invoke(own) is { } instead ? instead
            : Panel.FocusedEntry is { } focused
            ? InformationLine.Expand(_infoTemplate, focused, Panel.IsUp(Panel.Focus), format, Panel.TryGetDirectorySize(focused, out var dirSize) ? dirSize : null)
            : "";
        if (Panel.FocusedEntry is { } entry && !Panel.IsUp(Panel.Focus) && InfoExtra?.Invoke(entry) is { } extra)
        {
            left += "   " + extra;
        }
        else if (InfoExtra is null && Panel.FocusedEntry is { } file && !Panel.IsUp(Panel.Focus) && Panel.Path.Length > 0
            && Panel.ChildPath(Panel.Path, file.Name) is var torrentPath && TorrentFacts.MayRead(torrentPath, file)
            && TorrentFacts.Get(torrentPath, file, UpdateInfo) is { } torrent)
        {
            left += "   " + TorrentFacts.InfoText(torrent, format);
        }

        if (Panel.FocusedEntry is { } inRepository && !Panel.IsUp(Panel.Focus) && GitInfo?.Invoke(inRepository) is { } state)
        {
            left += "   " + state;
        }

        if (View.WaitingFor is { } waited)
        {
            var server = Salandual.Remote.ServerAddress.IsServerPath(Panel.LoadingPath);
            left = server && waited >= SlowAfter && ServerTimeout?.Invoke() is { } limit
                ? Loc.Format(AppStrings.Panel_WaitingSlow, (int)waited.TotalSeconds, (int)Math.Round(limit.TotalSeconds))
                : Loc.Format(server ? AppStrings.Panel_WaitingServer : AppStrings.Panel_WaitingFolder, (int)waited.TotalSeconds);
        }

        Info.Set(left, Panel.Path.Length > 0 ? InformationLine.Summary(Panel, format, CultureInfo.CurrentCulture) : "");
    }

    private void ReadFreeSpace(string path)
    {
        var version = ++_spaceVersion;
        _ = Task.Run(() =>
        {
            if (DiskSpace.TryGet(path, out var free, out var total))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (version == _spaceVersion)
                    {
                        DirectoryLine.SetSpace(path, free, total);
                    }
                });
            }
        });
    }
}
