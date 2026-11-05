// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.axaml.cs

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Salandual.App.Diagnostics;
using Salandual.App.Input;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;
using CoreModifiers = Salandual.Core.Commands.KeyModifiers;

namespace Salandual.App;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> _windowCommands =
    [
        "App.CommandSearch", "App.Exit", "App.Minimize", "App.MaximizeWindow", "App.Menu", "App.Options", "App.About", "Help.FirstSteps", "Help.Logs", "Options.ExportSettings", "Options.ImportSettings", "Options.ImportSalamander", "Options.ResetAllSettings", "Panel.SwitchActive", "View.ToggleMenuBar", "App.WindowMenu",
        "Panel.Swap", "Panel.Maximize", "Panel.ChangeDirectory", "Lists.WorkingDirectories", "Drive.Info", "Navigation.OtherPanelDirectory", "Navigation.OpenInOtherPanel",
        "Selection.SelectByMask", "Selection.UnselectByMask",
        "View.ToggleDriveStrip", "View.ToggleHotPathBar", "View.ToggleUserMenuBar", "View.ToggleBackForwardBar", "View.ToggleDirectoryLine", "View.ToggleColumnHeader", "View.ToggleInformationLine", "View.ToggleFunctionKeyBar",
        "Panel.ScrollUp", "Panel.ScrollDown", "Panel.ScrollLeft", "Panel.ScrollRight",
        "File.Copy", "File.Move", "File.Delete", "File.DeleteInverted", "File.CreateDirectory", "File.QuickRename", "File.Duplicate", "View.ToggleMiddleToolbar",
        "Clipboard.Copy", "Clipboard.Cut", "Clipboard.Paste", "Clipboard.PasteIntoFocused", "Clipboard.CopyFullName", "Clipboard.CopyName", "Clipboard.CopyPath", "Clipboard.CopyUncName",
        "File.ContextMenu", "Panel.DirectoryContextMenu", "File.Properties", "File.WindowsProperties",
        "File.View", "File.ViewAlternate", "File.ViewWith", "File.Edit", "File.EditNew", "File.EditWith", "Find.Files", "Find.Duplicates", "Compare.Directories", "Compare.Files", "File.MakeList",
        "File.OccupiedSpace", "Archive.Pack", "Archive.Unpack", "Archive.Test", "UserMenu.Open",
        "CommandLine.Toggle", "CommandLine.InsertName", "CommandLine.InsertDosName", "CommandLine.InsertDirectory", "CommandLine.InsertDirectoryDos", "CommandLine.InsertLeftDirectory",
        "CommandLine.InsertLeftDirectoryDos", "CommandLine.InsertRightDirectory", "CommandLine.InsertRightDirectoryDos", "Shell.CommandPrompt", "Shell.CommandPromptAdmin", "View.ToggleCommandLine",
        "View.ThumbnailsBigger", "View.ThumbnailsSmaller", "View.Mode8", "View.Mode9", "View.Mode0", "Panel.SmartNameWidth",
        "Panel.Filter", "Panel.ToggleDotFiles", "Panel.FlatView", "Panel.OpenInExplorer", "Navigation.ShortcutTarget", "File.NewMenu",
        "Network.Open", "Network.SharedDirectories", "Network.ConnectDrive", "Network.DisconnectDrive", "Server.Connect", "Server.Disconnect", "Server.Log", "Git.Menu", "Git.NewBranch",
    ];

    private readonly AppServices _services;
    private readonly PanelHost[] _panels;
    private readonly List<(MenuItem Item, MenuNode Node)> _menuItems = [];
    private List<(MenuItem Item, MenuNode Node)>? _unfilledMenus;
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private int _active;
    private double _split = 0.5;
    private readonly Dictionary<string, string> _lastFolder = new(StringComparer.OrdinalIgnoreCase);
    private int? _maximized;
    private EntrySorter _sorter;
    private Options.OptionsWindow? _options;
    private Dialogs.SelectMaskDialog? _maskDialog;

    public MainWindow()
        : this(AppServices.CreateIsolated())
    {
    }

    public MainWindow(AppServices services)
    {
        _services = services;
        foreach (var (drive, folder) in services.Session.LastFolderPerDrive)
        {
            _lastFolder[drive] = folder;
        }

        _working.Limit = services.Settings.Current.Layout.DirectoryLine.FoldersKept;
        _working.Load(services.Session.WorkingDirectories ?? []);

        StartupTrace.Mark("window-ctor");
        InitializeComponent();
        StartupTrace.Mark("window-created");

        var settings = services.Settings;
        Controls.WindowMemory.Remember(this, settings, WindowSettings.Main);
        _sorter = Sorter(settings.Current.Panels.NumericSort);
        _panels = [CreatePanel(0, _sorter), CreatePanel(1, _sorter)];
        _tabs[0].Add(new PanelTab(_panels[0].Panel));
        _tabs[1].Add(new PanelTab(_panels[1].Panel));
        StartupTrace.Mark("panels-created");
        WireNetwork();
        WireServers();
        Grid.SetColumn(_panels[1], 2);
        PanelsGrid.Children.Insert(0, _panels[0]);
        PanelsGrid.Children.Insert(1, _panels[1]);

        ShowInitialListing(_panels[0], services.Preloaded.Left, services.InitialPaths.Left, AppServices.DefaultPaths.Left);
        StartupTrace.Mark("left-shown");
        ShowInitialListing(_panels[1], services.Preloaded.Right, services.InitialPaths.Right, AppServices.DefaultPaths.Right);
        StartupTrace.Mark("panels-shown");
        RestoreTabs();

        ApplySettings();
        var closed = false;
        EventHandler<SettingsChangedEventArgs> apply = (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!closed)
            {
                ApplySettings();
            }
        });
        settings.Changed += apply;
        Closed += (_, _) =>
        {
            closed = true;
            settings.Changed -= apply;
        };
        EventHandler keyMapChanged = (_, _) => OnKeyMapChanged();
        services.Commands.KeyMapChanged += keyMapChanged;
        Closed += (_, _) => services.Commands.KeyMapChanged -= keyMapChanged;

        BuildMenu();
        WireArchives();
        FunctionKeys.Configure(services.Commands, id => CanExecute(id) && !GreyedInArchive(id) && !GreyedInTree(id));
        FunctionKeys.CommandInvoked += (_, id) => Run(id, PanelTarget.Active);
        InitCommandLine();
        SharedStrip.ItemClicked += (_, item) => OnStripItem(ActivePanel, item);
        SharedStrip.ItemMenuRequested += (_, r) => ShowStripMenu(ActivePanel, SharedStrip, r.Item);
        HotPathBar.ItemClicked += (_, item) => OnStripItem(ActivePanel, item, otherPanel: HotPathBar.ClickedWithCtrl);
        HotPathBar.ItemMenuRequested += (_, r) => ShowStripMenu(ActivePanel, HotPathBar, r.Item);
        UserMenuBar.ItemClicked += (_, item) => OnStripItem(ActivePanel, item);
        UserMenuBar.ItemMenuRequested += (_, r) => ShowStripMenu(ActivePanel, UserMenuBar, r.Item);
        WireBarGrips();
        WireBackForward();
        InitSplitter();
        _noticeTimer.Tick += (_, _) =>
        {
            Notice.IsVisible = false;
            _noticeTimer.Stop();
            ShowNextNotice();
        };
        _services.Settings.SaveFailed += OnSettingsSaveFailed;
        Closed += (_, _) => _services.Settings.SaveFailed -= OnSettingsSaveFailed;

        AddHandler(KeyDownEvent, OnModifierKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnModifierKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnMenuKey, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnAltWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnMenuKey, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => FunctionKeys.Modifiers = CoreModifiers.None;
        Activated += (_, _) =>
        {
            CheckCutStillOnClipboard();
            (Avalonia.Application.Current as App)?.Theme.CheckTextSize();
        };
        if ((Avalonia.Application.Current as App)?.Theme is { } theme)
        {
            EventHandler textSize = (_, _) => ApplySettings();
            theme.TextSizeChanged += textSize;
            Closed += (_, _) => theme.TextSizeChanged -= textSize;
        }
        InitFocusKeeping();
        _ = LoadDrivesAsync();
        SetActive(services.InitialActive, focus: false);
        StartupTrace.Mark("window-ready");
        EventHandler? firstLayout = null;
        firstLayout = (_, _) =>
        {
            LayoutUpdated -= firstLayout;
            StartupTrace.Mark("first-layout");
        };
        LayoutUpdated += firstLayout;

        Closing += OnClosingWithEditors;
        Closing += OnClosingWithJobs;
        Opened += (_, _) =>
        {
            StartupTrace.Mark("window-opened");
            _hwnd = TryGetPlatformHandle()?.Handle ?? 0;
            ActivePanel.View.Focus();
            RequestAnimationFrame(_ => RequestAnimationFrame(_ =>
            {
                StartupTrace.Mark("first-frame");
                FillMenus();
                RegisterDropTarget();
                InitSingleInstance();
                foreach (var host in _panels)
                {
                    host.Panel.StartWatching();
                }

                (Avalonia.Application.Current as App)?.Icons.Start();
                (Avalonia.Application.Current as App)?.Thumbnails.Start();

                ShowSettingsFileProblem();
                ShowLanguageProblems();
                StartLog();
                if (!_services.IsIsolated)
                {
                    StartSharing();
                }

                StartActivity();

                if (OfferFirstSteps(_services.Settings.LoadResult.Status, _services.IsIsolated, StartupTrace.IsEnabled))
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(async () => await ShowFirstStepsAsync(fromHelp: false), Avalonia.Threading.DispatcherPriority.Background);
                }

                if (!_services.IsIsolated)

                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(async () => await CheckLeftoverTempAsync(), Avalonia.Threading.DispatcherPriority.Background);
                }

                StartupTrace.Write();
                StartupTrace.DecodeSmokeImages();
                Avalonia.Threading.Dispatcher.UIThread.Post(async () => await SmokeViewThenExitAsync());
            }));
        };
        Closed += (_, _) =>
        {
            UnregisterDropTarget();
            CloseChildWindows();
            _logs?.Close();

            _services.SessionStore?.Save(new SessionState(SessionPath(_panels[0]), SessionPath(_panels[1]), _active, _lastFolder, _working.All, _panels[0].Panel.View, _panels[1].Panel.View)
            {
                Tabs = SavedTabs(),
                LeftTab = _tabOn[0],
                RightTab = _tabOn[1],
            });
            foreach (var tab in _tabs.SelectMany(t => t))
            {
                tab.Panel.Dispose();
            }
        };
    }

    internal AppServices Services => _services;

    internal PanelHost ActivePanel => _panels[_active];

    internal PanelHost LeftPanel => _panels[0];

    internal PanelHost RightPanel => _panels[1];

    internal IReadOnlyList<(MenuItem Item, MenuNode Node)> MenuItems
    {
        get
        {
            FillMenus();
            return _menuItems;
        }
    }

    private PanelHost CreatePanel(int index, EntrySorter sorter)
    {
        var settings = _services.Settings;
        var panel = new FilePanel(_services.PanelFiles, sorter, a => Dispatcher.UIThread.Post(a));
        panel.SetShowHidden(settings.Current.Panels.ShowHiddenFiles);
        panel.HistoryLimit = settings.Current.Layout.BackForward.StepsKept;
        panel.SetView(StartView(index));
        var host = new PanelHost(panel, _services.Commands);
        host.View.Activated += (_, _) => SetActive(index, focus: false);
        host.View.UnhandledCommand += (_, request) => request.Handled = Run(request.Id, PanelTarget.Active, request.Chord);
        host.View.OwnKey = chord => TryUserMenuKey(host, chord);
        host.View.RenameRequested += (_, _) => QuickRename(host);
        host.View.MenuRequested += (_, request) => OnPanelMenuRequested(host, request);
        host.View.DragRequested += (_, request) => StartDrag(host, request);
        host.View.IsCut = name => IsCut(host.Panel.Path, name) || IsDragged(host.Panel.Path, name);
        host.NoticeRequested += (_, message) => ShowNotice(message);
        host.View.Refused += (_, _) => ShowNotice(AppStrings.Panel_WaitingRefused);
        host.ServerTimeout = () => Remote.ServerAddress.Parse(host.Panel.LoadingPath) is { } loading ? _services.Servers.AnswerLimit(loading) : TimeSpan.FromSeconds(30);
        host.Strip.ItemClicked += (_, item) => OnStripItem(host, item);
        host.Strip.ItemMenuRequested += (_, r) => ShowStripMenu(host, host.Strip, r.Item);
        host.DirectoryLine.DriveButtonClicked += (_, _) => ToggleDriveMenu(host);
        host.DirectoryLine.PathMenuRequested += (_, point) => ShowPathMenu(host, point);
        host.DirectoryLine.SpaceMenuRequested += (_, _) => ShowSpaceMenu(host);
        WireDirectoryLine(host, index);
        WireQuickOpen(host);
        WireViews(host);
        WireFilter(host);
        WireFolderLooks(host);
        WireFlat(host);
        WireColumns(host);
        WireTabs(host);
        WireBackForward(host);
        WireGit(host);
        WireModel(panel);
        host.PathChanged += (_, _) => UpdateArchiveState(host);
        host.View.BeforeCommand = id => (id == "Navigation.Parent" && GoUpToThisPc(host))
                                        || (id == "Panel.Refresh" && ForgetServerListing(host));
        return host;
    }

    private void WireModel(FilePanel panel)
    {
        var settings = _services.Settings;
        panel.Changed += (_, change) =>
        {
            if ((change & (PanelChange.Focus | PanelChange.Selection | PanelChange.Items)) != 0 && _panels is not null && ReferenceEquals(ActivePanel.Panel, panel))
            {
                ScheduleShellMenu();
            }

            if ((change & PanelChange.Items) == 0)
            {
                return;
            }

            if (panel.ShowHidden != settings.Current.Panels.ShowHiddenFiles && HostOf(panel) is not null)
            {
                settings.Update(s => s with { Panels = s.Panels with { ShowHiddenFiles = panel.ShowHidden } });
            }

            RememberDriveFolder(panel.Path);
            UpdateTabTitles(panel);
            if (_panels is not null && ReferenceEquals(ActivePanel.Panel, panel))
            {
                UpdateActiveInfo();
            }
        };
        panel.Error += (_, message) =>
        {
            if (_panels is null || HostOf(panel) is not null)
            {
                ShowNotice(message);
            }
        };
        panel.ExecuteRequested += (_, path) => OnExecuteRequested(HostOf(panel) ?? ActivePanel, path);
        panel.HistoryChanged += (_, _) =>
        {
            if (_panels is not null && HostOf(panel) is not null)
            {
                UpdateBackForward();
            }
        };
        panel.LeavingFolder = (path, focus) => DivertFromLockedTab(panel, path, focus);
    }

    private static void ShowInitialListing(PanelHost host, Task<DirectoryListing>? preloaded, string path, string fallback)
    {
        if (preloaded is { IsCompletedSuccessfully: true })
        {
            host.Panel.ShowListing(preloaded.Result, watch: false);
        }
        else if (preloaded is not null)
        {
            preloaded.ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    host.Panel.ShowListing(t.Result);
                }
                else
                {
                    _ = NavigateOrFallback(host, path, fallback);
                }
            }), TaskScheduler.Default);
        }
        else
        {
            _ = NavigateOrFallback(host, path, fallback);
        }
    }

    private static async Task NavigateOrFallback(PanelHost host, string path, string fallback)
    {
        if (!await host.Panel.NavigateAsync(path) && path != fallback)
        {
            await host.Panel.NavigateAsync(fallback);
        }
    }

    private void SetActive(int index, bool focus)
    {
        if (_maximized is { } maximized && maximized != index)
        {
            ToggleMaximize();
        }

        _active = index;
        _panels[0].IsActive = index == 0;
        _panels[1].IsActive = index == 1;
        Splitter.RightActive = index == 1;
        FunctionKeys.InvalidateVisual();
        if (focus)
        {
            _panels[index].View.Focus();
        }

        UpdateActiveInfo();
        UpdateBackForward();
    }

    private PanelHost OtherPanel(PanelHost host) => ReferenceEquals(host, _panels[0]) ? _panels[1] : _panels[0];

    private void RememberDriveFolder(string path)
    {
        if (Platform.Windows.FileSystem.WindowsPath.GetRoot(path) is { } root)
        {
            _lastFolder[root] = path;
        }
    }

    internal async void GoToDrive(PanelHost host, string root)
    {
        var current = Platform.Windows.FileSystem.WindowsPath.GetRoot(host.Panel.Path);
        var sameDrive = string.Equals(current, root, StringComparison.OrdinalIgnoreCase);
        var target = !sameDrive && _lastFolder.TryGetValue(root, out var last) ? last : root;
        host.View.Focus();
        if (!await host.Panel.NavigateAsync(target) && target != root)
        {
            await host.Panel.NavigateAsync(root);
        }
    }

    internal void SwapPanels()
    {
        var left = _panels[0].Panel;
        var right = _panels[1].Panel;
        HideComparison();
        (_tabs[0], _tabs[1]) = (_tabs[1], _tabs[0]);
        (_tabOn[0], _tabOn[1]) = (_tabOn[1], _tabOn[0]);
        _panels[0].Attach(right);
        _panels[1].Attach(left);
        UpdateTabStrip(0);
        UpdateTabStrip(1);
        UpdateActiveInfo();
    }

    internal void ToggleMaximize(int? index = null)
    {
        var columns = PanelsGrid.ColumnDefinitions;
        if (_maximized is not null)
        {
            _maximized = null;
            columns[1].Width = GridLength.Auto;
            _panels[0].IsVisible = _panels[1].IsVisible = Splitter.IsVisible = true;
            ApplySplit(_split);
            UpdateMaximizeButtons();
            return;
        }

        var shown = index ?? _active;
        if (shown != _active)
        {
            SetActive(shown, focus: true);
        }

        _maximized = shown;
        var other = 1 - shown;
        columns[shown == 0 ? 0 : 2].Width = new GridLength(1, GridUnitType.Star);
        columns[other == 0 ? 0 : 2].Width = new GridLength(0);
        columns[1].Width = new GridLength(0);
        _panels[other].IsVisible = Splitter.IsVisible = false;
        UpdateMaximizeButtons();
    }

    internal bool IsPanelMaximized => _maximized is not null;

    internal IDictionary<string, string> LastFolderPerDrive => _lastFolder;

    private void OpenInOtherPanel(PanelHost host)
    {
        var panel = host.Panel;
        if (panel.FocusedEntry is not { } entry || panel.Path.Length == 0)
        {
            return;
        }

        var other = OtherPanel(host).Panel;
        if (panel.IsUp(panel.Focus))
        {
            if (_services.Files.GetParent(panel.Path) is { } parent)
            {
                _ = other.NavigateAsync(parent, _services.Files.GetName(panel.Path));
            }
        }
        else if (entry.IsDirectory)
        {
            _ = other.NavigateAsync(panel.ChildPath(panel.Path, entry.Name));
        }
        else
        {
            _ = other.NavigateAsync(panel.Path, entry.Name);
        }
    }

    private void UpdateActiveInfo()
    {
        var path = ActivePanel.Panel.Path;
        var layout = _services.Settings.Current.Layout;
        Title = WindowTitle.Expand(layout.WindowTitle, Core.Vfs.ShellPlace.Display(path), AppBuild.TitleName(layout.TitleShowsVersion));
        SharedStrip.Current = Platform.Windows.FileSystem.WindowsPath.GetRoot(path);
        UpdateCommandLine();
    }

    private async Task LoadDrivesAsync()
    {
        _drives = await DriveStrip.LoadAsync();
        UpdateStrips();
    }

    private HighlightSettings? _highlightingFrom;
    private Highlighter _highlighter = Highlighter.None;

    private void ApplySettings()
    {
        var s = _services.Settings.Current;
        var format = DisplayFormat.From(s.Panels, CultureInfo.CurrentCulture);
        var bars = s.Layout.Bars;
        _working.Limit = s.Layout.DirectoryLine.FoldersKept;
        _services.Commands.UseKeyMap(s.Keyboard.KeyMap, s.Keyboard.Keys);
        ApplyMenuBar(s.Layout);

        UpdateStrips();
        UpdateActiveInfo();
        FunctionKeys.IsVisible = bars.FunctionKeyBar;
        BarSizing.SetBarHeight(this, s.Layout.BarHeight);
        Splitter.ShowButtons = bars.MiddleToolbar;
        if (_sorter.NumericSort != s.Panels.NumericSort)
        {
            _sorter = Sorter(s.Panels.NumericSort);
        }

        if (Avalonia.Application.Current is App { Thumbnails: { } thumbs } && thumbs.Files != s.Panels.ThumbnailsOf)
        {
            thumbs.Files = s.Panels.ThumbnailsOf;
            foreach (var host in _panels)
            {
                host.View.InvalidateRows();
            }
        }

        if (!ReferenceEquals(_highlightingFrom, s.Highlighting))
        {
            _highlightingFrom = s.Highlighting;
            _highlighter = new Highlighter(s.Highlighting.Effective);
            _services.FolderLooks.Set(s.Highlighting.Folders);
        }

        foreach (var host in _panels)
        {
            host.ApplySettings(s, format);
            host.View.Highlighter = _highlighter;
            host.View.FolderLooks = _services.FolderLooks;
            host.DirectoryLine.FolderLooks = _services.FolderLooks;
            host.Panel.SetShowHidden(s.Panels.ShowHiddenFiles);
            host.Panel.SetSorter(_sorter);
        }

        ApplyTabSettings(s);
        ApplyServerSettings(s);
        ApplyStepsKept(s.Layout.BackForward.StepsKept);
        UpdateBackForward();

        KeepOwnViewsMade();

        if (!Splitter.IsDragging && _maximized is null)
        {
            ApplySplit(s.Layout.PanelSplit);
        }
    }

    private void UpdateBars(Func<BarVisibility, BarVisibility> change)
        => _services.Settings.Update(s => s with { Layout = s.Layout with { Bars = change(s.Layout.Bars) } });

    private void InitSplitter()
    {
        Splitter.Configure(CanExecute, ToolbarTip);
        Splitter.CommandInvoked += (_, id) =>
        {
            Run(id, PanelTarget.Active);
            if (_operationDialog is null && !ActivePanel.IsRenaming)
            {
                ActivePanel.View.Focus();
            }
        };
        Splitter.Dragging += (_, x) =>
        {
            var width = PanelsGrid.Bounds.Width - Splitter.Bounds.Width;
            if (width <= 0)
            {
                return;
            }

            ApplySplit((x - (Splitter.Bounds.Width / 2)) / width);
            ShowSplitBadge(x);
        };
        Splitter.DragEnded += (_, _) =>
        {
            SplitBadge.IsVisible = false;
            SaveSplit();
        };
        Splitter.ResetRequested += (_, _) =>
        {
            ApplySplit(0.5);
            SaveSplit();
            ShowSplitBadge(Splitter.Bounds.Center.X);
            DispatcherTimer.RunOnce(() => SplitBadge.IsVisible = false, TimeSpan.FromSeconds(1));
        };
    }

    private string ToolbarTip(string id)
    {
        var definition = _services.Commands[id];
        var name = id switch
        {
            "File.CreateDirectory" => AppStrings.Tip_NewFolder,
            "Find.Files" => AppStrings.Tip_Find,
            "File.Copy" => AppStrings.Tip_Copy,
            "File.Move" => AppStrings.Tip_Move,
            "File.QuickRename" => AppStrings.Tip_QuickRename,
            "Clipboard.Copy" => AppStrings.Tip_ClipboardCopy,
            "Archive.Pack" => AppStrings.Tip_Pack,
            "File.Email" => AppStrings.Tip_Email,
            _ => definition.Title,
        };
        var key = _services.Commands.FirstChord(id) is { } first ? "   " + first : "";
        if (!CanExecute(id))
        {
            return Loc.Format(AppStrings.Tip_ArrivesIn, name, key, CommandText.Milestone(definition.PlannedFor));
        }

        if (id is "File.Copy" or "File.Move" && ActivePanel.Panel.Path.Length > 0)
        {
            return $"{name}{key}\n{ActivePanel.Panel.Path}  →  {OtherPanel(ActivePanel).Panel.Path}";
        }

        return name + key;
    }

    private void ApplySplit(double ratio)
    {
        _split = Math.Clamp(ratio, LayoutSettings.MinPanelSplit, LayoutSettings.MaxPanelSplit);
        PanelsGrid.ColumnDefinitions[0].Width = new GridLength(_split, GridUnitType.Star);
        PanelsGrid.ColumnDefinitions[2].Width = new GridLength(1 - _split, GridUnitType.Star);
    }

    private void SaveSplit()
    {
        var split = Math.Round(_split, 3);
        _services.Settings.Update(s => s with { Layout = s.Layout with { PanelSplit = split } });
    }

    private void ShowSplitBadge(double x)
    {
        var left = (int)Math.Round(_split * 100);
        SplitBadgeText.Text = string.Create(CultureInfo.CurrentCulture, $"{left} % · {100 - left} %");
        SplitBadge.IsVisible = true;
        SplitBadge.Measure(Avalonia.Size.Infinity);
        var w = SplitBadge.DesiredSize.Width;
        SplitBadge.Margin = new Avalonia.Thickness(Math.Clamp(x - (w / 2), 0, Math.Max(0, PanelsGrid.Bounds.Width - w)), (PanelsGrid.Bounds.Height / 2) - 16, 0, 0);
    }

    private static bool CanExecute(string id) => IsBuilt(id);

    internal static bool IsBuilt(string id)
        => _windowCommands.Contains(id)
           || IsTabCommand(id)

           || FilePanel.HandledCommands.Contains(id)
           || IsHotPathOrDriveMenuCommand(id)
           || (id.StartsWith("Drive.Switch", StringComparison.Ordinal) && id.Length == "Drive.Switch".Length + 1);

    private bool IsChecked(string id) => id switch
    {
        "View.ToggleMenuBar" => _services.Settings.Current.Layout.Bars.MenuBar,
        "View.ToggleDriveStrip" => _services.Settings.Current.Layout.Bars.DriveStrip,
        "View.ToggleHotPathBar" => _services.Settings.Current.Layout.Bars.HotPathBar,
        "View.ToggleUserMenuBar" => _services.Settings.Current.Layout.Bars.UserMenuBar,
        "View.ToggleBackForwardBar" => _services.Settings.Current.Layout.BackForward.InBar,
        "View.ToggleDirectoryLine" => _services.Settings.Current.Layout.Bars.DirectoryLine,
        "View.ToggleColumnHeader" => _services.Settings.Current.Layout.Bars.ColumnHeader,
        "View.ToggleInformationLine" => _services.Settings.Current.Layout.Bars.InformationLine,
        "View.ToggleFunctionKeyBar" => _services.Settings.Current.Layout.Bars.FunctionKeyBar,
        "View.ToggleCommandLine" => _services.Settings.Current.Layout.Bars.CommandLine,
        "View.ToggleMiddleToolbar" => _services.Settings.Current.Layout.Bars.MiddleToolbar,
        "Panel.ToggleHidden" => _services.Settings.Current.Panels.ShowHiddenFiles,
        "Panel.ToggleDotFiles" => _services.Settings.Current.Panels.HideDotFiles,
        _ => false,
    };

    internal bool Run(string id, PanelTarget target, KeyChord? chord = null)
    {
        var host = target switch
        {
            PanelTarget.Left => _panels[0],
            PanelTarget.Right => _panels[1],
            _ => ActivePanel,
        };

        if (host.View.IsLocked && !FilePanelView.AllowedWhileWaiting(id))
        {
            ShowNotice(AppStrings.Panel_WaitingRefused);
            return true;
        }

        RememberCommand(id);
        if (IsHotPathOrDriveMenuCommand(id))
        {
            return RunHotPathCommand(id, host, chord);
        }

        if (IsTabCommand(id))
        {
            return RunTabCommand(id, host);
        }

        if (RunServerCommand(id, host) || RunGitCommand(id, host) || RunOnServer(id, host))
        {
            return true;
        }

        if (BlockedByArchive(id, host) || RunInArchive(id, host))
        {
            return true;
        }

        if (RunNetworkCommand(id, host) || RunInPlace(id, host))
        {
            return true;
        }

        switch (id)
        {
            case "App.CommandSearch":
                OpenQuickOpen();
                return true;
            case "App.WindowMenu":
                ShowWindowMenu(new Avalonia.Point(4, Controls.TitleBarParts.Height));
                return true;
            case "App.Exit":
                Close();
                return true;
            case "App.Minimize":
                WindowState = WindowState.Minimized;
                return true;
            case "App.MaximizeWindow":
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return true;
            case "App.Menu":
                OpenMenuBar();
                return true;
            case "View.ToggleMenuBar":
                UpdateBars(b => b with { MenuBar = !b.MenuBar });
                return true;
            case "Panel.OpenInExplorer":
                _ = OpenInExplorerAsync(host);
                return true;
            case "Navigation.ShortcutTarget":
                _ = GoToShortcutTargetAsync(host);
                return true;
            case "File.NewMenu":
                ShowNewMenu(host);
                return true;
            case "Selection.SelectByMask" or "Selection.UnselectByMask":
                _ = SelectByMaskAsync(host, id == "Selection.SelectByMask");
                return true;
            case "App.Options":
                _ = OpenOptionsAsync();
                return true;
            case "Help.FirstSteps":
                _ = ShowFirstStepsAsync(fromHelp: true);
                return true;
            case "App.About":
                _ = ShowAboutAsync();
                return true;
            case "Help.Logs":
                ShowLogs();
                return true;
            case "Options.ExportSettings":
                _ = SettingsFiles.ExportAsync();
                return true;
            case "Options.ImportSettings":
                _ = SettingsFiles.ImportAsync();
                return true;
            case "Options.ImportSalamander":
                _ = SettingsFiles.ImportSalamanderAsync();
                return true;
            case "Options.ResetAllSettings":
                _ = SettingsFiles.ResetAllAsync();
                return true;
            case "Panel.Swap":
                SwapPanels();
                return true;
            case "View.ThumbnailsBigger" or "View.ThumbnailsSmaller":
                StepThumbnailSize(id == "View.ThumbnailsBigger" ? 1 : -1);
                return true;
            case "View.Mode8" or "View.Mode9" or "View.Mode0":
                RunOwnView(id, host);
                return true;
            case "Panel.SmartNameWidth":
                ToggleSmartName(host);
                return true;
            case "Panel.Filter":
                _ = FilterAsync(host);
                return true;
            case "Panel.ToggleDotFiles":
                ToggleDotFiles();
                return true;
            case "Panel.FlatView":
                ToggleFlat(host);
                return true;
            case "Panel.Maximize":
                ToggleMaximize(Array.IndexOf(_panels, host));
                return true;
            case "Panel.ChangeDirectory":
                ChangeDirectory(host);
                return true;
            case "Lists.WorkingDirectories":
                ToggleWorkingDirectories(host);
                return true;
            case "Drive.Info":
                _ = ShowDriveInfoAsync(host);
                return true;
            case "Navigation.OtherPanelDirectory":
                if (OtherPanel(host).Panel.Path is { Length: > 0 } otherPath)
                {
                    _ = host.Panel.NavigateAsync(otherPath, OtherPanel(host).Panel.FocusedEntry?.Name);
                }

                return true;
            case "Navigation.OpenInOtherPanel":
                OpenInOtherPanel(host);
                return true;
            case var drive when drive.StartsWith("Drive.Switch", StringComparison.Ordinal) && drive.Length == "Drive.Switch".Length + 1:
                GoToDrive(host, id[^1] + @":\");
                return true;
            case "Panel.SwitchActive":
                SetActive(1 - _active, focus: true);
                return true;
            case "View.ToggleDriveStrip":
                UpdateBars(b => b with { DriveStrip = !b.DriveStrip });
                return true;
            case "View.ToggleHotPathBar":
                UpdateBars(b => b with { HotPathBar = !b.HotPathBar });
                break;
            case "View.ToggleUserMenuBar":
                UpdateBars(b => b with { UserMenuBar = !b.UserMenuBar });
                return true;
            case "View.ToggleBackForwardBar":
                UpdateBackForwardSettings(b => b.WithBar(!b.InBar));
                return true;
            case "View.ToggleDirectoryLine":
                UpdateBars(b => b with { DirectoryLine = !b.DirectoryLine });
                return true;
            case "View.ToggleColumnHeader":
                UpdateBars(b => b with { ColumnHeader = !b.ColumnHeader });
                return true;
            case "View.ToggleInformationLine":
                UpdateBars(b => b with { InformationLine = !b.InformationLine });
                return true;
            case "View.ToggleFunctionKeyBar":
                UpdateBars(b => b with { FunctionKeyBar = !b.FunctionKeyBar });
                return true;
            case "View.ToggleCommandLine":
                UpdateBars(b => b with { CommandLine = !b.CommandLine });
                return true;
            case "CommandLine.Toggle":
                ToggleCommandLine();
                return true;
            case var insert when insert.StartsWith("CommandLine.Insert", StringComparison.Ordinal):
                InsertForCommandLine(insert);
                return true;
            case "Shell.CommandPrompt" or "Shell.CommandPromptAdmin":
                OpenCommandShell(host, elevated: id == "Shell.CommandPromptAdmin");
                return true;
            case "View.ToggleMiddleToolbar":
                UpdateBars(b => b with { MiddleToolbar = !b.MiddleToolbar });
                return true;
            case "File.CreateDirectory":
                _ = NewFolderAsync(host);
                return true;
            case "File.QuickRename":
                QuickRename(host);
                return true;
            case "File.Duplicate":
                Duplicate(host);
                return true;
            case "Clipboard.Copy" or "Clipboard.Cut":
                CopyToClipboard(host, cut: id == "Clipboard.Cut");
                return true;
            case "Clipboard.Paste" or "Clipboard.PasteIntoFocused":
                _ = PasteAsync(host, intoFocused: id == "Clipboard.PasteIntoFocused");
                return true;
            case "Clipboard.CopyFullName" or "Clipboard.CopyName" or "Clipboard.CopyPath" or "Clipboard.CopyUncName":
                _ = CopyNamesAsync(host, id);
                return true;
            case "File.Copy" or "File.Move":
                _ = CopyOrMoveAsync(host, id == "File.Copy" ? Core.Operations.OperationKind.Copy : Core.Operations.OperationKind.Move);
                return true;
            case "File.Delete" or "File.DeleteInverted":
                _ = DeleteAsync(host, inverted: id == "File.DeleteInverted");
                return true;
            case "Archive.Pack":
                _ = PackAsync(host);
                return true;
            case "Archive.Unpack":
                _ = UnpackAsync(host);
                return true;
            case "Archive.Test":
                _ = TestArchivesAsync(host);
                return true;
            case "UserMenu.Open":
                ShowUserMenu(host);
                return true;
            case "File.ContextMenu" or "Panel.DirectoryContextMenu":
                ShowMenuFromKeyboard(host, background: id == "Panel.DirectoryContextMenu");
                return true;
            case "File.Properties" or "File.WindowsProperties":
                _ = ShowPropertiesAsync(host, DefaultTarget(host), windows: (id == "File.WindowsProperties") != (_services.Settings.Current.Mouse.AltEnter == PropertiesWindow.Windows));
                return true;
            case "File.View" or "File.ViewAlternate":
                OpenViewer(host, alternate: id == "File.ViewAlternate");
                return true;
            case "File.ViewWith":
                ShowViewWith(host);
                return true;
            case "File.Edit":
                _ = OpenEditorAsync(host);
                return true;
            case "File.EditNew":
                _ = EditNewFileAsync(host);
                return true;
            case "File.EditWith":
                ShowEditWith(host);
                return true;
            case "Find.Files":
                OpenFind(host);
                return true;
            case "Find.Duplicates":
                OpenDuplicates(host);
                return true;
            case "Compare.Directories":
                _ = CompareDirectoriesAsync();
                return true;
            case "Compare.Files":
                CompareFiles();
                return true;
            case "File.MakeList":
                _ = MakeFileListAsync(host);
                return true;
            case "File.OccupiedSpace":
                _ = OccupiedSpaceAsync(host);
                return true;
        }

        if (host.View.TryRunCommand(id))
        {
            return true;
        }

        if (_services.Commands.TryGet(id, out var definition))
        {
            ShowNotice(Loc.Format(AppStrings.Notice_ArrivesIn, definition.Title, CommandText.Milestone(definition.PlannedFor)));
            return true;
        }

        return false;
    }

    internal async Task OpenOptionsAsync(string? category = null, string? keyCommand = null)
    {
        if (_options is not null)
        {
            if (keyCommand is not null)
            {
                _options.OpenKeyRow(keyCommand);
            }
            else if (category is not null)
            {
                _options.ShowCategory(category);
            }

            _options.Activate();
            return;
        }

        _options = new Options.OptionsWindow(_services)
        {
            PanelFolder = () => ActivePanel.Panel.Path,
            PanelFolders = () => (_panels[0].Panel.Path, _panels[1].Panel.Path),
            TextClipboard = FileClipboardAccess,
            UserMenuPreview = () => ActivePanel.Panel.Path.Length > 0 ? UserMenuContextFor(ActivePanel) : null,
            UserMenuIcons = item => UserMenuIcon(item)?.Source,
        };
        var options = _options;
        options.ViewFile = file => OpenViewer(ActivePanel, file, ViewerKind.Text, siblings: [file], comeBackTo: options);
        LanguageHooks(options);
        if (keyCommand is not null)
        {
            _options.OpenKeyRow(keyCommand);
        }
        else if (category is not null)
        {
            _options.ShowCategory(category);
        }
        else if (_options.CurrentCategory == UserMenuPage)
        {
            _options.Rebuild();
        }

        try
        {
            await _options.ShowDialog(this);
        }
        finally
        {
            _options = null;
            ActivePanel.View.Focus();
        }
    }

    internal Options.OptionsWindow? OpenOptions => _options;

    internal async Task SelectByMaskAsync(PanelHost host, bool select)
    {
        if (_maskDialog is not null)
        {
            _maskDialog.Activate();
            return;
        }

        var s = _services.Settings.Current;
        _maskDialog = new Dialogs.SelectMaskDialog(select, select ? s.History.SelectMasks : s.History.UnselectMasks, s.Panels.SelectFoldersWithNumPlus);
        try
        {
            if (await _maskDialog.ShowDialog<FileMask?>(this) is { } mask)
            {
                host.Panel.SelectByMask(mask, select);
                _services.Settings.Update(x => x with
                {
                    History = select
                        ? x.History with { SelectMasks = HistorySettings.Push(x.History.SelectMasks, mask.Text) }
                        : x.History with { UnselectMasks = HistorySettings.Push(x.History.UnselectMasks, mask.Text) },
                });
            }
        }
        finally
        {
            _maskDialog = null;
            host.View.Focus();
        }
    }

    internal Dialogs.SelectMaskDialog? OpenMaskDialog => _maskDialog;

    internal void ShowNotice(string text, params (string Label, Action Run)[] actions) => ShowNotice(text, actions.Length > 0 ? 7 : 3, actions);

    internal void ShowNotice(string text, double seconds, params (string Label, Action Run)[] actions)
    {
        NoticeText.Text = text;
        NoticeBar.IsVisible = NoticeDetail.IsVisible = false;
        NoticeActions.Children.Clear();
        foreach (var (label, run) in actions)
        {
            var button = new Button { Content = label };

            button.Click += (_, _) =>
            {
                Notice.IsVisible = false;
                run();
                if (!Notice.IsVisible)
                {
                    ShowNextNotice();
                }
            };
            NoticeActions.Children.Add(button);
        }

        Notice.IsHitTestVisible = actions.Length > 0;
        Notice.IsVisible = true;
        _noticeTimer.Interval = TimeSpan.FromSeconds(seconds);
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private readonly Queue<(string Text, double Seconds, (string Label, Action Run)[] Actions)> _waitingNotices = new();

    internal void ShowNoticeInTurn(string text, double seconds, params (string Label, Action Run)[] actions)
    {
        if (Notice.IsVisible && Notice.IsHitTestVisible)
        {
            _waitingNotices.Enqueue((text, seconds, actions));
        }
        else
        {
            ShowNotice(text, seconds, actions);
        }
    }

    private void ShowNextNotice()
    {
        if (_waitingNotices.TryDequeue(out var next))
        {
            ShowNotice(next.Text, next.Seconds, next.Actions);
        }
    }

    internal void ShowNoticeProgress(double? fraction, string detail)
    {
        NoticeBar.IsIndeterminate = fraction is null;
        NoticeBar.Value = Math.Clamp(fraction ?? 0, 0, 1);
        NoticeBar.IsVisible = true;
        NoticeDetail.Text = detail;
        NoticeDetail.IsVisible = true;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    internal string? NoticeMessage => Notice.IsVisible ? NoticeText.Text : null;

    internal string? NoticeProgress => Notice.IsVisible && NoticeDetail.IsVisible ? NoticeDetail.Text : null;

    internal IReadOnlyList<Button> NoticeButtons => [.. NoticeActions.Children.OfType<Button>()];

    private void BuildMenu()
    {
        _unfilledMenus = [];
        foreach (var node in MainMenu.Create())
        {
            var item = new MenuItem { Header = node.Header };
            item.SubmenuOpened += (_, _) => RefreshMenuStates();
            _unfilledMenus.Add((item, node));
            MainMenuBar.Items.Add(item);
        }

        MainMenuBar.Closed += (_, _) =>
        {
            OnMenuBarClosed();
            if (!ActivePanel.IsEditingPath)
            {
                ActivePanel.View.Focus();
            }
        };
    }

    private void FillMenus()
    {
        if (_unfilledMenus is not { } menus)
        {
            return;
        }

        _unfilledMenus = null;
        foreach (var (item, node) in menus)
        {
            foreach (var child in node.Children ?? [])
            {
                item.Items.Add(CreateMenuItem(child));
            }
        }
    }

    private Control CreateMenuItem(MenuNode node)
    {
        if (node.IsSeparator)
        {
            return new Separator();
        }

        if (node.Dynamic == "View.Columns")
        {
            return ColumnsSubmenu(node);
        }

        var item = new MenuItem();
        if (node.Dynamic == "Find.Saved")
        {
            item.Header = node.Header;
            item.Items.Add(new MenuItem { Header = AppStrings.Find_SavedNone, IsEnabled = false });
            item.SubmenuOpened += (_, e) =>
            {
                if (ReferenceEquals(e.Source, item))
                {
                    FillSavedSearchesMenu(item);
                }
            };
            return item;
        }

        if (node.Children is not null)
        {
            item.Header = node.Header;
            foreach (var child in node.Children)
            {
                item.Items.Add(CreateMenuItem(child));
            }

            return item;
        }

        var definition = _services.Commands[node.CommandId!];
        item.Header = node.Header ?? definition.Title;
        item.InputGesture = MenuGesture(node.CommandId!);

        if (node.Checkable)
        {
            item.ToggleType = node.Radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox;
        }

        item.Click += (_, _) => Run(node.CommandId!, node.Target);
        _menuItems.Add((item, node));
        return item;
    }

    private KeyGesture? MenuGesture(string id) => _services.Commands.FirstChord(id) is { } chord ? KeyMapping.ToGesture(chord) : null;

    private void OnKeyMapChanged()
    {
        foreach (var (item, node) in _menuItems)
        {
            item.InputGesture = MenuGesture(node.CommandId!);
        }

        FunctionKeys.InvalidateVisual();
    }

    private void RefreshMenuStates()
    {
        foreach (var (item, node) in _menuItems)
        {
            item.IsEnabled = CanExecute(node.CommandId!);
            if (node.CommandId == "Panel.SmartNameWidth")
            {
                var host = HostFor(node.Target);
                item.IsEnabled = PanelViews.HasColumns(host.Panel.View);
                item.IsChecked = item.IsEnabled && IsSmartName(host);
                continue;
            }

            if (IsTabCommand(node.CommandId!))
            {
                item.IsEnabled = TabCommandEnabled(node.CommandId!, HostFor(node.Target));
                item.IsChecked = TabChecked(node.CommandId!, HostFor(node.Target));
                continue;
            }

            ShowOwnViewItem(item, node.CommandId!);
            if (node.CommandId == "Panel.FlatView")
            {
                item.IsChecked = HostFor(node.Target).Panel.IsFlat;
            }
            else if (node.Checkable)
            {
                item.IsChecked = node.Radio ? IsViewChecked(node.CommandId!, node.Target) : IsChecked(node.CommandId!);
            }
        }
    }

    private void OnModifierKey(object? sender, KeyEventArgs e)
    {
        var down = e.RoutedEvent == KeyDownEvent;
        var mods = CoreModifiers.None;
        if ((e.KeyModifiers & Avalonia.Input.KeyModifiers.Control) != 0)
        {
            mods |= CoreModifiers.Ctrl;
        }

        if ((e.KeyModifiers & Avalonia.Input.KeyModifiers.Alt) != 0)
        {
            mods |= CoreModifiers.Alt;
        }

        if ((e.KeyModifiers & Avalonia.Input.KeyModifiers.Shift) != 0)
        {
            mods |= CoreModifiers.Shift;
        }

        var own = e.Key switch
        {
            Key.LeftCtrl or Key.RightCtrl => CoreModifiers.Ctrl,
            Key.LeftAlt or Key.RightAlt => CoreModifiers.Alt,
            Key.LeftShift or Key.RightShift => CoreModifiers.Shift,
            _ => CoreModifiers.None,
        };
        mods = down ? mods | own : mods & ~own;
        FunctionKeys.Modifiers = mods;
    }
}
