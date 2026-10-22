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
        "App.Exit", "App.Minimize", "App.MaximizeWindow", "App.Menu", "App.Options", "Help.FirstSteps", "Options.ExportSettings", "Options.ImportSettings", "Options.ResetAllSettings", "Panel.SwitchActive", "View.ToggleMenuBar", "App.WindowMenu",
        "Panel.Swap", "Panel.Maximize", "Panel.ChangeDirectory", "Lists.WorkingDirectories", "Drive.Info", "Navigation.OtherPanelDirectory", "Navigation.OpenInOtherPanel",
        "Selection.SelectByMask", "Selection.UnselectByMask",
        "View.ToggleDriveStrip", "View.ToggleHotPathBar", "View.ToggleDirectoryLine", "View.ToggleColumnHeader", "View.ToggleInformationLine", "View.ToggleFunctionKeyBar",
        "Panel.ScrollUp", "Panel.ScrollDown", "Panel.ScrollLeft", "Panel.ScrollRight",
        "File.Copy", "File.Move", "File.Delete", "File.DeleteInverted", "File.CreateDirectory", "File.QuickRename", "File.Duplicate", "View.ToggleMiddleToolbar",
        "Clipboard.Copy", "Clipboard.Cut", "Clipboard.Paste", "Clipboard.PasteIntoFocused", "Clipboard.CopyFullName", "Clipboard.CopyName", "Clipboard.CopyPath", "Clipboard.CopyUncName",
        "File.ContextMenu", "Panel.DirectoryContextMenu", "File.Properties", "File.WindowsProperties",
        "File.View", "File.ViewAlternate", "File.ViewWith", "File.Edit", "File.EditNew", "File.EditWith", "Find.Files", "Find.Duplicates", "Compare.Directories", "Compare.Files", "File.MakeList",
        "File.OccupiedSpace",
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

        _working.Load(services.Session.WorkingDirectories ?? []);

        StartupTrace.Mark("window-ctor");
        InitializeComponent();
        StartupTrace.Mark("window-created");

        var settings = services.Settings;
        _sorter = EntrySorter.ForCurrentCulture(settings.Current.Panels.NumericSort);
        _panels = [CreatePanel(0, _sorter), CreatePanel(1, _sorter)];
        StartupTrace.Mark("panels-created");
        Grid.SetColumn(_panels[1], 2);
        PanelsGrid.Children.Insert(0, _panels[0]);
        PanelsGrid.Children.Insert(1, _panels[1]);

        ShowInitialListing(_panels[0], services.Preloaded.Left, services.InitialPaths.Left, AppServices.DefaultPaths.Left);
        StartupTrace.Mark("left-shown");
        ShowInitialListing(_panels[1], services.Preloaded.Right, services.InitialPaths.Right, AppServices.DefaultPaths.Right);
        StartupTrace.Mark("panels-shown");

        ApplySettings();
        settings.Changed += (_, _) => Dispatcher.UIThread.Post(ApplySettings);
        EventHandler keyMapChanged = (_, _) => OnKeyMapChanged();
        services.Commands.KeyMapChanged += keyMapChanged;
        Closed += (_, _) => services.Commands.KeyMapChanged -= keyMapChanged;

        BuildMenu();
        FunctionKeys.Configure(services.Commands, CanExecute);
        FunctionKeys.CommandInvoked += (_, id) => Run(id, PanelTarget.Active);
        SharedStrip.ItemClicked += (_, item) => OnStripItem(ActivePanel, item);
        SharedStrip.ItemMenuRequested += (_, r) => ShowStripMenu(ActivePanel, SharedStrip, r.Item);
        HotPathBar.ItemClicked += (_, item) => OnStripItem(ActivePanel, item, otherPanel: HotPathBar.ClickedWithCtrl);
        HotPathBar.ItemMenuRequested += (_, r) => ShowStripMenu(ActivePanel, HotPathBar, r.Item);
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
        AddHandler(KeyUpEvent, OnMenuKey, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => FunctionKeys.Modifiers = CoreModifiers.None;
        Activated += (_, _) => CheckCutStillOnClipboard();
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

                ShowSettingsFileProblem();
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

            _services.SessionStore?.Save(new SessionState(_panels[0].Panel.Path, _panels[1].Panel.Path, _active, _lastFolder, _working.All));
            foreach (var host in _panels)
            {
                host.Panel.Dispose();
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
        var panel = new FilePanel(_services.Files, sorter, a => Dispatcher.UIThread.Post(a));
        panel.SetShowHidden(settings.Current.Panels.ShowHiddenFiles);
        var host = new PanelHost(panel, _services.Commands);
        host.View.Activated += (_, _) => SetActive(index, focus: false);
        host.View.UnhandledCommand += (_, request) => request.Handled = Run(request.Id, PanelTarget.Active, request.Chord);
        host.View.RenameRequested += (_, _) => QuickRename(host);
        host.View.MenuRequested += (_, request) => OnPanelMenuRequested(host, request);
        host.View.DragRequested += (_, request) => StartDrag(host, request);
        host.View.IsCut = name => IsCut(host.Panel.Path, name) || IsDragged(host.Panel.Path, name);
        host.NoticeRequested += (_, message) => ShowNotice(message);
        host.Strip.ItemClicked += (_, item) => OnStripItem(host, item);
        host.Strip.ItemMenuRequested += (_, r) => ShowStripMenu(host, host.Strip, r.Item);
        host.DirectoryLine.DriveButtonClicked += (_, _) => ToggleDriveMenu(host);
        host.DirectoryLine.PathMenuRequested += (_, point) => ShowPathMenu(host, point);
        WireDirectoryLine(host, index);
        panel.Changed += (_, change) =>
        {
            if ((change & (PanelChange.Focus | PanelChange.Selection | PanelChange.Items)) != 0 && ReferenceEquals(ActivePanel.Panel, panel))
            {
                ScheduleShellMenu();
            }

            if ((change & PanelChange.Items) == 0)
            {
                return;
            }

            if (panel.ShowHidden != settings.Current.Panels.ShowHiddenFiles)
            {
                settings.Update(s => s with { Panels = s.Panels with { ShowHiddenFiles = panel.ShowHidden } });
            }

            RememberDriveFolder(panel.Path);
            if (ReferenceEquals(ActivePanel.Panel, panel))
            {
                UpdateActiveInfo();
            }
        };
        panel.Error += (_, message) => ShowNotice(message);
        panel.ExecuteRequested += (_, path) => _ = OpenFileAsync(path);
        return host;
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
        if (focus)
        {
            _panels[index].View.Focus();
        }

        UpdateActiveInfo();
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
        _panels[0].Attach(right);
        _panels[1].Attach(left);
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
            _ = other.NavigateAsync(Path.Join(panel.Path, entry.Name));
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
        Title = WindowTitle.Expand(layout.WindowTitle, path, AppBuild.TitleName(layout.TitleShowsVersion));
        SharedStrip.Current = Platform.Windows.FileSystem.WindowsPath.GetRoot(path);
    }

    private async Task LoadDrivesAsync()
    {
        _drives = await DriveStrip.LoadAsync();
        UpdateStrips();
    }

    private void ApplySettings()
    {
        var s = _services.Settings.Current;
        var format = DisplayFormat.From(s.Panels, CultureInfo.CurrentCulture);
        var bars = s.Layout.Bars;
        _services.Commands.UseKeyMap(s.Keyboard.KeyMap);
        ApplyMenuBar(s.Layout);

        UpdateStrips();
        UpdateActiveInfo();
        FunctionKeys.IsVisible = bars.FunctionKeyBar;
        SharedStrip.IsVisible = bars.DriveStrip && s.Layout.Strip == StripMode.Shared;
        HotPathBar.IsVisible = bars.HotPathBar;
        Splitter.ShowButtons = bars.MiddleToolbar;
        if (_sorter.NumericSort != s.Panels.NumericSort)
        {
            _sorter = EntrySorter.ForCurrentCulture(s.Panels.NumericSort);
        }

        foreach (var host in _panels)
        {
            host.ApplySettings(s, format);
            host.Panel.SetShowHidden(s.Panels.ShowHiddenFiles);
            host.Panel.SetSorter(_sorter);
        }

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

           || FilePanel.HandledCommands.Contains(id)
           || IsHotPathOrDriveMenuCommand(id)
           || (id.StartsWith("Drive.Switch", StringComparison.Ordinal) && id.Length == "Drive.Switch".Length + 1);

    private bool IsChecked(string id) => id switch
    {
        "View.ToggleMenuBar" => _services.Settings.Current.Layout.Bars.MenuBar,
        "View.ToggleDriveStrip" => _services.Settings.Current.Layout.Bars.DriveStrip,
        "View.ToggleHotPathBar" => _services.Settings.Current.Layout.Bars.HotPathBar,
        "View.ToggleDirectoryLine" => _services.Settings.Current.Layout.Bars.DirectoryLine,
        "View.ToggleColumnHeader" => _services.Settings.Current.Layout.Bars.ColumnHeader,
        "View.ToggleInformationLine" => _services.Settings.Current.Layout.Bars.InformationLine,
        "View.ToggleFunctionKeyBar" => _services.Settings.Current.Layout.Bars.FunctionKeyBar,
        "View.ToggleMiddleToolbar" => _services.Settings.Current.Layout.Bars.MiddleToolbar,
        "Panel.ToggleHidden" => _services.Settings.Current.Panels.ShowHiddenFiles,
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

        if (IsHotPathOrDriveMenuCommand(id))
        {
            return RunHotPathCommand(id, host, chord);
        }

        switch (id)
        {
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
            case "Selection.SelectByMask" or "Selection.UnselectByMask":
                _ = SelectByMaskAsync(host, id == "Selection.SelectByMask");
                return true;
            case "App.Options":
                _ = OpenOptionsAsync();
                return true;
            case "Help.FirstSteps":
                _ = ShowFirstStepsAsync(fromHelp: true);
                return true;
            case "Options.ExportSettings":
                _ = SettingsFiles.ExportAsync();
                return true;
            case "Options.ImportSettings":
                _ = SettingsFiles.ImportAsync();
                return true;
            case "Options.ResetAllSettings":
                _ = SettingsFiles.ResetAllAsync();
                return true;
            case "Panel.Swap":
                SwapPanels();
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

    internal async Task OpenOptionsAsync(string? category = null)
    {
        if (_options is not null)
        {
            if (category is not null)
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
        };
        var options = _options;
        options.ViewFile = file => OpenViewer(ActivePanel, file, ViewerKind.Text, siblings: [file], comeBackTo: options);
        if (category is not null)
        {
            _options.ShowCategory(category);
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

    internal string? NoticeMessage => Notice.IsVisible ? NoticeText.Text : null;

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
            item.ToggleType = MenuItemToggleType.CheckBox;
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
            if (node.Checkable)
            {
                item.IsChecked = IsChecked(node.CommandId!);
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
