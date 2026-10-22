// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Compare.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Compare;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Compare;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App;

public partial class MainWindow
{
    private Border? _compareBar;
    private readonly TextBlock _compareText = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _compareStop = new() { Content = AppStrings.Compare_Stop };
    private readonly Button _compareList = new() { Content = AppStrings.Compare_ListButton };
    private readonly Button _compareFiles = new() { Content = AppStrings.Compare_FilesButton };
    private readonly Button _compareRefresh = new() { Content = AppStrings.Compare_Refresh };
    private readonly Button _compareHide = new();
    private CancellationTokenSource? _comparing;
    private CompareSession? _session;
    private string? _shownLevel;
    private CompareDirectoriesOptions? _lastCompare;
    private readonly (string Path, HashSet<string> Names)?[] _comparedSelection = new (string, HashSet<string>)?[2];

    internal sealed record CompareSession(string Left, string Right, CompareDirectoriesOptions Options, bool Select, bool ShowList)
    {
        public DirectoryCompareResult? Result { get; set; }
    }

    internal CompareDirectoriesDialog? CompareDialog { get; private set; }

    internal DirectoryCompareResult? Comparison { get; private set; }

    internal string? ComparedLevel => _shownLevel;

    internal Task? Comparing { get; private set; }

    internal DifferencesWindow? DifferencesList { get; private set; }

    internal Border? CompareBar => _compareBar;

    internal string CompareBarText => _compareText.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : _compareText.Text ?? "";

    internal Button CompareListButton => _compareList;

    internal Button CompareRefreshButton => _compareRefresh;

    internal Button CompareFilesButton => _compareFiles;

    internal FileCompareWindow? FileCompare { get; private set; }

    private readonly List<FileCompareWindow> _fileCompares = [];

    internal Button CompareHideButton => _compareHide;

    internal async Task CompareDirectoriesAsync()
    {
        if (_comparing is not null || CompareDialog is not null)
        {
            return;
        }

        var left = _panels[0].Panel;
        var right = _panels[1].Panel;
        var panelLeft = left.Results is null ? left.Path : "";
        var panelRight = right.Results is null ? right.Path : "";
        if (FromSelection() is not { } paths)
        {
            if (panelLeft.Length == 0 || panelRight.Length == 0)
            {
                ShowNotice(AppStrings.Compare_NeedFolders);
                return;
            }

            if (string.Equals(panelLeft, panelRight, StringComparison.OrdinalIgnoreCase))
            {
                ShowNotice(AppStrings.Compare_SameFolder);
                return;
            }

            paths = (panelLeft, panelRight, AppStrings.Compare_FromPanels);
        }

        var settings = _services.Settings.Current.Compare;
        var dialog = CompareDialog = new CompareDirectoriesDialog(_lastCompare is { } last ? settings with { Directories = last } : settings, paths.Left, paths.Right, paths.From, panelLeft, panelRight);
        CompareDirectoriesChoice? choice;
        try
        {
            choice = await dialog.ShowDialog<CompareDirectoriesChoice?>(this);
        }
        finally
        {
            CompareDialog = null;
        }

        if (choice is null)
        {
            return;
        }

        _lastCompare = choice.Options;
        if (choice.SaveDefaults)
        {
            _services.Settings.Update(s => s with { Compare = s.Compare with { Directories = choice.Options, SelectInPanels = choice.SelectInPanels, ShowList = choice.ShowList } });
        }

        await (Comparing = RunCompareAsync(new CompareSession(choice.Left, choice.Right, choice.Options, choice.SelectInPanels, choice.ShowList), openList: true));
    }

    private (string Left, string Right, string From)? FromSelection()
    {
        var active = Array.IndexOf(_panels, ActivePanel);
        var folders = SelectedFolders(active);
        if (folders is null)
        {
            return null;
        }

        var otherFolders = SelectedFolders(1 - active);
        var other = _panels[1 - active].Panel;
        var otherFolder = other.Results is null ? other.Path : "";
        return folders.Count switch
        {
            2 => (folders[0], folders[1], active == 0 ? AppStrings.Compare_FromTwoLeft : AppStrings.Compare_FromTwoRight),
            1 when otherFolders is [var theOther] => active == 0 ? (folders[0], theOther, AppStrings.Compare_FromEach) : (theOther, folders[0], AppStrings.Compare_FromEach),
            1 when active == 0 => (folders[0], otherFolder, AppStrings.Compare_FromOneLeft),
            1 => (otherFolder, folders[0], AppStrings.Compare_FromOneRight),
            _ => null,
        };
    }

    private List<string>? SelectedFolders(int side)
    {
        var panel = _panels[side].Panel;
        if (panel.Results is not null || panel.Path.Length == 0)
        {
            return null;
        }

        var selected = panel.Items.Where((_, i) => !panel.IsUp(i) && panel.IsSelected(i)).ToList();
        if (_comparedSelection[side] is { } made && string.Equals(made.Path, panel.Path, StringComparison.OrdinalIgnoreCase) && made.Names.SetEquals(selected.Select(e => e.Name)))
        {
            return null;
        }

        return [.. selected.Where(e => e.IsDirectory).Select(e => _services.Files.Combine(panel.Path, e.Name))];
    }

    private async Task RunCompareAsync(CompareSession session, bool openList = false)
    {
        HideComparison();
        _session = session;
        session.Result = null;
        var list = openList && (session.ShowList || !PanelsShow(session)) ? ListWindowNow(session) : DifferencesList is { } open && ReferenceEquals(open.Tag, session) ? open : null;
        var bar = PanelsShow(session) && _services.Settings.Current.Compare.ShowBar;
        using var stop = _comparing = new CancellationTokenSource();
        var progress = default(CompareProgress);
        ShowProgress();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => ShowProgress());
        timer.Start();
        DirectoryCompareResult result;
        try
        {
            var showHidden = _panels[0].Panel.ShowHidden || _panels[1].Panel.ShowHidden;
            var compare = new DirectoryCompare(_services.Files, OpenToCompare, session.Options, showHidden);
            result = await compare.RunAsync(session.Left, session.Right, p => progress = p, stop.Token);
        }
        finally
        {
            timer.Stop();
            _comparing = null;
        }

        session.Result = result;
        if (_session != session)
        {
            return;
        }

        if (_compareBar is not null)
        {
            _compareBar.IsVisible = false;
        }

        if (DifferencesList is { } shown && ReferenceEquals(shown.Tag, session))
        {
            shown.Show(result, session.Options);
        }

        if (PanelsShow(session))
        {
            ShowLevel(session, "", session.Select);
        }

        void ShowProgress()
        {
            if (bar)
            {
                ShowComparing(progress);
            }

            list?.ShowComparing(progress);
        }
    }

    private static Stream OpenToCompare(string path)
        => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);

    private bool PanelsShow(CompareSession session) => LevelShown(session) == "";

    private string? LevelShown(CompareSession? session)
    {
        if (session is null || _panels[0].Panel.Results is not null || _panels[1].Panel.Results is not null)
        {
            return null;
        }

        var left = Relative(session.Left, _panels[0].Panel.Path);
        var right = Relative(session.Right, _panels[1].Panel.Path);
        return left is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ? left : null;

        static string? Relative(string root, string path)
        {
            if (string.Equals(root.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            var prefix = root.EndsWith('\\') ? root : root + "\\";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path[prefix.Length..].TrimEnd('\\') : null;
        }
    }

    private void ShowLevel(CompareSession session, string level, bool select)
    {
        if (session.Result is not { } result)
        {
            return;
        }

        HideComparison();
        var leftMarks = new Dictionary<string, DifferenceKind>(StringComparer.OrdinalIgnoreCase);
        var rightMarks = new Dictionary<string, DifferenceKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in result.In(level))
        {
            if (d.Kind is DifferenceKind.CountedSame or DifferenceKind.Same)
            {
                continue;
            }

            if (d.Selected.HasFlag(CompareSide.Left))
            {
                leftMarks[d.Name] = d.Kind;
            }

            if (d.Selected.HasFlag(CompareSide.Right))
            {
                rightMarks[d.Name] = d.Kind;
            }
        }

        var settings = _services.Settings.Current.Compare;
        Comparison = result;
        _shownLevel = level;
        _panels[0].Panel.ShowComparison(settings.ShowMarks ? leftMarks : null, select ? leftMarks.Keys : null);
        _panels[1].Panel.ShowComparison(settings.ShowMarks ? rightMarks : null, select ? rightMarks.Keys : null);
        if (select)
        {
            _comparedSelection[0] = (_panels[0].Panel.Path, new HashSet<string>(leftMarks.Keys, StringComparer.OrdinalIgnoreCase));
            _comparedSelection[1] = (_panels[1].Panel.Path, new HashSet<string>(rightMarks.Keys, StringComparer.OrdinalIgnoreCase));
        }
        _panels[0].Panel.Changed += OnComparedPanelChanged;
        _panels[1].Panel.Changed += OnComparedPanelChanged;
        if (settings.ShowBar)
        {
            ShowCompared(result, level);
        }

        DifferencesList?.UpdatePanelsButton();
    }

    internal void HideComparison()
    {
        foreach (var host in _panels)
        {
            host.Panel.Changed -= OnComparedPanelChanged;
            host.Panel.HideComparison();
        }

        _shownLevel = null;
        Comparison = null;
        if (_compareBar is not null && _comparing is null)
        {
            _compareBar.IsVisible = false;
        }

        DifferencesList?.UpdatePanelsButton();
    }

    private void OnComparedPanelChanged(object? sender, PanelChange change)
    {
        if (_shownLevel is { } level && LevelShown(_session) != level)
        {
            HideComparison();
        }
    }

    private DifferencesWindow ListWindowNow(CompareSession session)
    {
        if (DifferencesList is not { } window)
        {
            var request = new DifferencesRequest
            {
                ShowInPanels = node => _ = ShowInPanelsAsync(node),
                SelectInPanels = () =>
                {
                    if (_session is { } s && LevelShown(s) is { } level)
                    {
                        ShowLevel(s, level, select: true);
                        Activate();
                    }
                },
                PanelsShowThem = () => LevelShown(_session) is not null,
                Refresh = () =>
                {
                    if (_comparing is null && _session is { } s)
                    {
                        _ = Comparing = RunCompareAsync(s);
                    }
                },
                Stop = () => _comparing?.Cancel(),
                OpenOptions = () => _ = OpenOptionsAsync("compare"),
                View = path => OpenViewer(ActivePanel, path, _services.Settings.Current.Viewers.KindFor(Path.GetFileName(path), alternate: false), comeBackTo: DifferencesList),
                CompareFiles = (left, right) => OpenFileCompare(left, right),
            };
            window = DifferencesList = new DifferencesWindow(_services.Settings, request, session.Left, session.Right);
            window.Closed += (_, _) => DifferencesList = null;
            window.Tag = session;
            window.Show();
        }
        else
        {
            window.Tag = session;
            window.SetFolders(session.Left, session.Right);
            window.WindowState = window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState;
            window.Activate();
        }

        return window;
    }

    internal void CompareFiles()
    {
        if (FilesToCompare() is { } pair)
        {
            OpenFileCompare(pair.Left, pair.Right);
        }
        else
        {
            ShowNotice(AppStrings.Compare_FilesNone);
        }
    }

    internal (string Left, string Right)? FilesToCompare()
    {
        var other = 1 - _active;
        var mine = Selected(_active);
        if (mine.Count == 2)
        {
            return (mine[0], mine[1]);
        }

        var (left, right) = (Selected(0), Selected(1));
        if (left.Count == 1 && right.Count == 1)
        {
            return (left[0], right[0]);
        }

        var files = new[] { left.Count == 1 ? left[0] : FocusedFile(_panels[0]), right.Count == 1 ? right[0] : FocusedFile(_panels[1]) };
        if (files[_active] is { } file && _panels[other].Panel.Path.Length > 0 && _panels[other].Panel.Results is null
            && WindowsPath.Join(_panels[other].Panel.Path, WindowsPath.GetName(file)) is var twin && File.Exists(twin) && File.Exists(file)
            && !string.Equals(file, twin, StringComparison.OrdinalIgnoreCase))
        {
            return _active == 0 ? (file, twin) : (twin, file);
        }

        if (files[0] is { } l && files[1] is { } r && File.Exists(l) && File.Exists(r))
        {
            return (l, r);
        }

        return null;

        List<string> Selected(int side)
        {
            var panel = _panels[side].Panel;
            if (panel.Path.Length == 0)
            {
                return [];
            }

            var selected = panel.Items.Where((_, i) => !panel.IsUp(i) && panel.IsSelected(i)).ToList();
            if (_comparedSelection[side] is { } made && string.Equals(made.Path, panel.Path, StringComparison.OrdinalIgnoreCase) && made.Names.SetEquals(selected.Select(e => e.Name)))
            {
                return [];
            }

            return [.. selected.Where(e => !e.IsDirectory).Select(e => WindowsPath.Join(panel.Path, e.Name))];
        }
    }

    internal FileCompareWindow? OpenFileCompare(string left, string right)
    {
        var files = _services.Settings.Current.Compare.Files;
        if (files.UseProgram && files.Program.Length > 0)
        {
            StartCompareProgram(files.Program, files.ProgramArguments, left, right);
            return null;
        }

        var window = new FileCompareWindow(_services.Settings, left, right)
        {
            Host = new FileCompareHost
            {
                Clipboard = FileClipboardAccess,
                Edit = path => _ = OpenEditorAsync(ActivePanel, path),
                ShowInPanel = path => _ = ShowInPanelAsync(ActivePanel, path),
                OpenOptions = () => _ = OpenOptionsAsync("compare"),
                Program = CompareProgramToUse,
            },
        };
        FileCompare = window;
        _fileCompares.Add(window);
        window.Closed += (_, _) =>
        {
            _fileCompares.Remove(window);
            if (FileCompare == window)
            {
                FileCompare = null;
            }
        };
        window.Show();
        return window;
    }

    private (string Name, Action<string, string> Start)? CompareProgramToUse()
    {
        var files = _services.Settings.Current.Compare.Files;
        var known = CompareProgram.Known(File.Exists, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (files.Program.Length > 0)
        {
            var name = known.FirstOrDefault(k => string.Equals(k.Program.Path, files.Program, StringComparison.OrdinalIgnoreCase)).Program?.Name ?? Path.GetFileNameWithoutExtension(files.Program);
            return (name, (l, r) => StartCompareProgram(files.Program, files.ProgramArguments, l, r));
        }

        return known.FirstOrDefault(k => k.Found) is { Program: { } found } ? (found.Name, (l, r) => StartCompareProgram(found.Path, found.Arguments, l, r)) : null;
    }

    internal Func<string, IReadOnlyList<string>, StartProblem>? StartProgramForTests { get; set; }

    private void StartCompareProgram(string program, string arguments, string left, string right)
    {
        var name = Path.GetFileNameWithoutExtension(program);
        if (CompareProgram.IsShell(program))
        {
            ShowNotice(Loc.Format(AppStrings.CompareProgram_Shell, Path.GetFileName(program)));
            return;
        }

        var list = CompareProgram.ArgumentsFor(arguments, left, right);
        string? detail = null;
        var problem = StartProgramForTests is { } start ? start(program, list) : Programs.Start(program, list, Path.GetDirectoryName(left), out detail);
        var text = problem switch
        {
            StartProblem.NotFound => Loc.Format(AppStrings.CompareProgram_NotFound, name),
            StartProblem.NotAProgram => Loc.Format(AppStrings.CompareProgram_NotAProgram, name),
            StartProblem.Failed => Loc.Format(AppStrings.EditWith_Failed, name, detail ?? ""),
            _ => null,
        };
        if (text is not null)
        {
            ShowNotice(text, (AppStrings.CompareProgram_Options, () => _ = OpenOptionsAsync("compare")));
        }
    }

    internal async Task ShowInPanelsAsync(CompareNode node)
    {
        if (_session is not { } session)
        {
            return;
        }

        var sub = node.Item.Subfolder;
        await GoAsync(_panels[0].Panel, session.Left, node.Item.Left is not null);
        await GoAsync(_panels[1].Panel, session.Right, node.Item.Right is not null);
        if (LevelShown(session) is { } level && string.Equals(level, sub, StringComparison.OrdinalIgnoreCase))
        {
            ShowLevel(session, level, select: false);
        }

        Activate();

        async Task GoAsync(FilePanel panel, string root, bool has)
        {
            var folder = sub.Length == 0 ? root : _services.Files.Combine(root, sub);
            var focus = has ? node.Item.Name : null;
            while (!Directory.Exists(folder) && folder.Length > root.Length)
            {
                focus = Path.GetFileName(folder.TrimEnd('\\'));
                folder = Path.GetDirectoryName(folder.TrimEnd('\\')) ?? root;
            }

            await panel.NavigateAsync(folder, focus);
        }
    }

    private void ShowComparing(CompareProgress progress)
    {
        var bar = CompareBarNow();
        _compareStop.IsVisible = true;
        _compareList.IsVisible = _compareRefresh.IsVisible = _compareHide.IsVisible = false;
        var inlines = _compareText.Inlines!;
        inlines.Clear();
        inlines.Add(new Run(AppStrings.Compare_BarComparing) { FontWeight = FontWeight.SemiBold });
        inlines.Add(Dim("  " + Loc.Format(AppStrings.Compare_BarProgress, progress.Files.ToString("N0", CultureInfo.CurrentCulture), progress.Folders.ToString("N0", CultureInfo.CurrentCulture))));
        if (progress.CurrentFolder is { Length: > 0 } folder)
        {
            inlines.Add(Dim("  ·  " + folder));
        }

        bar.IsVisible = true;
    }

    private void ShowCompared(DirectoryCompareResult result, string level)
    {
        var bar = CompareBarNow();
        _compareStop.IsVisible = false;
        _compareList.IsVisible = _compareRefresh.IsVisible = _compareHide.IsVisible = true;
        var here = result.In(level).ToList();
        var inlines = _compareText.Inlines!;
        inlines.Clear();
        inlines.Add(new Run(AppStrings.Compare_BarCompared + " ") { FontWeight = FontWeight.SemiBold });
        var parts = 0;
        Part("←", AppStrings.Compare_BarOnlyLeft, DifferenceKind.OnlyLeft);
        Part("→", AppStrings.Compare_BarOnlyRight, DifferenceKind.OnlyRight);
        Part("▲", AppStrings.Compare_BarNewer, DifferenceKind.NewerOrBigger);
        Part("≠", AppStrings.Compare_BarDifferent, DifferenceKind.Different);
        if (parts == 0)
        {
            inlines.Add(new Run(AppStrings.Compare_BarSame));
        }

        var counted = here.Count(d => d.Kind == DifferenceKind.CountedSame);
        if (counted > 0)
        {
            inlines.Add(Dim("  ·  " + (counted == 1 ? AppStrings.Compare_BarCountedSameOne : Loc.Format(AppStrings.Compare_BarCountedSame, counted))));
        }

        if (result.NotRead > 0)
        {
            inlines.Add(Dim("  ·  " + Loc.Format(AppStrings.Compare_BarNotRead, result.NotRead)));
        }

        if (result.Problems.Count > 0)
        {
            inlines.Add(Dim("  ·  " + Loc.Format(AppStrings.Compare_BarProblems, result.Problems.Count)));
        }

        if (result.Cancelled)
        {
            inlines.Add(Dim("  ·  " + AppStrings.Compare_BarStopped));
        }

        ToolTip.SetTip(_compareText, result.Problems.Count > 0 ? string.Join("\n", result.Problems.Take(20)) : CompareBarText);
        bar.IsVisible = true;

        void Part(string mark, string text, DifferenceKind kind)
        {
            var count = here.Count(d => d.Kind == kind);
            if (count == 0)
            {
                return;
            }

            if (parts++ > 0)
            {
                inlines.Add(Dim("  ·  "));
            }

            var glyph = new Run(mark + " ") { FontWeight = FontWeight.SemiBold };
            glyph.Bind(TextElement.ForegroundProperty, _compareText.GetResourceObservable("Brush.Accent"));
            inlines.Add(glyph);
            inlines.Add(new Run(Loc.Format(text, count)));
        }
    }

    private Run Dim(string text)
    {
        var run = new Run(text);
        run.Bind(TextElement.ForegroundProperty, _compareText.GetResourceObservable("Brush.SecondaryText"));
        return run;
    }

    private Border CompareBarNow()
    {
        if (_compareBar is not null)
        {
            return _compareBar;
        }

        _compareText.Res(TextBlock.ForegroundProperty, "Brush.Text");
        _compareStop.Click += (_, _) => _comparing?.Cancel();
        _compareList.Click += (_, _) =>
        {
            if (_session is { Result: { } result } session)
            {
                ListWindowNow(session).Show(result, session.Options);
            }
        };
        _compareRefresh.Click += (_, _) =>
        {
            if (_comparing is null && _session is { } session)
            {
                _ = Comparing = RunCompareAsync(session);
            }
        };
        ToolTip.SetTip(_compareList, AppStrings.Compare_ListTip);
        ToolTip.SetTip(_compareFiles, AppStrings.Compare_FilesTip);
        _compareFiles.Click += (_, _) => CompareFiles();
        ToolTip.SetTip(_compareRefresh, AppStrings.Compare_RefreshTip);
        _compareHide.Content = AppIcons.Glyph("Icon.Cross", 10, 10, 1.5);
        _compareHide.Padding = new Thickness(8, 6);
        _compareHide.Background = Brushes.Transparent;
        _compareHide.BorderThickness = new Thickness(0);
        ToolTip.SetTip(_compareHide, AppStrings.Compare_HideTip);
        Avalonia.Automation.AutomationProperties.SetName(_compareHide, AppStrings.Compare_HideTip);
        _compareHide.Click += (_, _) => HideComparison();
        foreach (var button in new[] { _compareStop, _compareList, _compareFiles, _compareRefresh })
        {
            button.Padding = new Thickness(12, 4);
            button.VerticalAlignment = VerticalAlignment.Center;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 0, 0, 0), Children = { _compareStop, _compareList, _compareFiles, _compareRefresh, _compareHide } };
        var dock = new DockPanel { Children = { buttons, _compareText } };
        DockPanel.SetDock(buttons, Dock.Right);
        _compareBar = new Border
        {
            Child = dock,
            Padding = new Thickness(12, 6, 6, 6),
            Margin = new Thickness(8, 4, 8, 0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            IsVisible = false,
        }.Res(Border.BackgroundProperty, "Brush.TextSelection").Res(Border.BorderBrushProperty, "Brush.Border");
        DockPanel.SetDock(_compareBar, Dock.Bottom);
        RootDock.Children.Insert(RootDock.Children.IndexOf(BottomRow) + 1, _compareBar);
        return _compareBar;
    }

    internal async Task SmokeCompareAsync()
    {
        if (!Diagnostics.StartupTrace.IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_COMPARE") is not { Length: > 0 } pair
            || pair.Split(';') is not [var leftPath, var rightPath] || !Directory.Exists(leftPath) || !Directory.Exists(rightPath))
        {
            return;
        }

        var captures = Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } c ? Directory.CreateDirectory(c).FullName : null;
        await _panels[0].Panel.NavigateAsync(leftPath);
        await _panels[1].Panel.NavigateAsync(rightPath);
        _ = CompareDirectoriesAsync();
        for (var i = 0; i < 100 && CompareDialog is null; i++)
        {
            await Task.Delay(50);
        }

        if (CompareDialog is not { } dialog)
        {
            Diagnostics.StartupTrace.Append("compare: no dialog\n");
            return;
        }

        dialog.SubfoldersBox.IsChecked = true;
        await Task.Delay(400);
        if (captures is not null && dialog.TryGetPlatformHandle()?.Handle is { } dialogHwnd)
        {
            Diagnostics.WindowCapture.Save(dialogHwnd, Path.Combine(captures, "compare-dialog.png"));
        }

        dialog.Accept();
        for (var i = 0; i < 100 && Comparison is null; i++)
        {
            await Task.Delay(50);
        }

        Activate();
        await Task.Delay(400);
        if (captures is not null && TryGetPlatformHandle()?.Handle is { } hwnd)
        {
            Diagnostics.WindowCapture.Save(hwnd, Path.Combine(captures, "compare-panels.png"));
        }

        string Marks(int side) => string.Join(",", _panels[side].Panel.CompareMarks?.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase).Select(m => $"{m.Key}:{m.Value}") ?? []);
        string Selection(int side) => string.Join(",", _panels[side].Panel.Items.Where((_, i) => _panels[side].Panel.IsSelected(i)).Select(e => e.Name));
        Diagnostics.StartupTrace.Append(string.Create(CultureInfo.InvariantCulture,
            $"compare {Comparison?.Elapsed.TotalMilliseconds:0} ms left=[{Selection(0)}] marks=[{Marks(0)}] right=[{Selection(1)}] marks=[{Marks(1)}] bar=\"{CompareBarText}\"\n"));

        var left = _panels[0];
        if (left.Panel.Items.Select((e, i) => (e, i)).Where(x => !x.e.IsDirectory && File.Exists(Path.Join(rightPath, x.e.Name))).Select(x => (int?)x.i).FirstOrDefault() is { } twinIndex)
        {
            left.Panel.SetFocus(twinIndex);
            left.View.Focus();
            left.View.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.C, KeyModifiers = Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift, Source = left.View });
            for (var i = 0; i < 60 && FileCompare is null; i++)
            {
                await Task.Delay(50);
            }

            Diagnostics.StartupTrace.Append($"compare key Ctrl+Shift+C on {left.Panel.Items[twinIndex].Name}: {(FileCompare is { } opened ? Path.GetFileName(opened.LeftPath) + " | " + Path.GetFileName(opened.RightPath) : "no window, notice \"" + NoticeText.Text + "\"")}\n");
            FileCompare?.Close();
            await Task.Delay(200);
            Activate();
        }

        if (DifferencesList is { } list)
        {
            list.ExpandOrCollapseAll();
            list.Activate();
            await Task.Delay(500);
            if (captures is not null && list.TryGetPlatformHandle()?.Handle is { } listHwnd)
            {
                Diagnostics.WindowCapture.Save(listHwnd, Path.Combine(captures, "compare-list.png"));
            }

            Diagnostics.StartupTrace.Append(string.Create(CultureInfo.InvariantCulture,
                $"compare list count=\"{list.CountText}\" enter=\"{list.EnterText}\" rows={list.Rows.Count} [{string.Join(",", list.Rows.Select(r => r.Node.Path))}]\n"));

            var menu = list.OpenKindMenu();
            await Task.Delay(500);
            var menuItems = menu.Items.OfType<MenuItem>().ToList();
            if (captures is not null && menuItems.FirstOrDefault() is { } first && TopLevel.GetTopLevel(first)?.TryGetPlatformHandle()?.Handle is { } menuHwnd)
            {
                Diagnostics.WindowCapture.Save(menuHwnd, Path.Combine(captures, "compare-kind-menu.png"));
            }

            menuItems.LastOrDefault()?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Delay(200);
            Diagnostics.StartupTrace.Append($"compare kind menu open={menuItems.Count > 0 && menuItems[0].IsEffectivelyVisible} items={menuItems.Count} chosen=\"{list.DifferentChip.Content}\"\n");
            menu.Hide();

            list.SetKind(DifferenceFilter.LeftNewerAndLeftOrphans);
            list.NamesBox.Text = "*" + Path.GetExtension(list.Rows.FirstOrDefault(r => !r.Node.IsDirectory)?.Node.Item.Name ?? "");
            list.SetNot(true);
            await Task.Delay(500);
            if (captures is not null && list.TryGetPlatformHandle()?.Handle is { } filteredHwnd)
            {
                Diagnostics.WindowCapture.Save(filteredHwnd, Path.Combine(captures, "compare-list-filtered.png"));
            }

            Diagnostics.StartupTrace.Append($"compare list filtered chip=\"{list.DifferentChip.Content}\" rows=[{string.Join(",", list.Rows.Select(r => r.Node.Path))}]\n");
            list.NamesBox.Text = "";
            list.SetNot(false);
            list.SetKind(DifferenceFilter.Differences);
            if (list.Rows.FirstOrDefault(r => r.Node.Item.Subfolder.Length > 0 && r.Node.Item.Left is not null && r.Node.Item.Right is not null) is { } deep)
            {
                await ShowInPanelsAsync(deep.Node);
                Diagnostics.StartupTrace.Append($"compare show in panels {deep.Node.Path}: level=\"{ComparedLevel}\" marks=[{Marks(0)}]|[{Marks(1)}]\n");
            }

            if (list.Rows.FirstOrDefault(r => r.Node.Item.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && r.Node.Item.Left is not null) is { } md)
            {
                list.Activate();
                list.List.SelectedItem = md;
                list.View();
                await Task.Delay(2500);
                if (_viewers.LastOrDefault() is { } viewer)
                {
                    var headings = viewer.Page?.Headings.Count ?? -1;
                    var sent = viewer.PageView is { } page ? await page.RunAsync("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })) ? 'sent' : 'stopped'") : "no page";
                    await Task.Delay(800);
                    Diagnostics.StartupTrace.Append($"compare F3 {md.Node.Path}: headings={headings} esc={sent} closed={!viewer.IsVisible}\n");
                    if (viewer.IsVisible)
                    {
                        viewer.Close();
                    }
                }
            }

            list.SetView(CompareView.All);
            if (list.Rows.FirstOrDefault(r => !r.Node.IsDirectory && r.Node.Item.Kind != DifferenceKind.Same && r.Node.Item.Left is not null && r.Node.Item.Right is not null) is { } twoFiles)
            {
                list.Activate();
                list.List.SelectedItem = twoFiles;
                list.Enter();
                for (var i = 0; i < 100 && FileCompare is null; i++)
                {
                    await Task.Delay(50);
                }

                if (FileCompare is { } files)
                {
                    await Task.Delay(300);
                    await files.Comparing;
                    await Task.Delay(500);
                    if (captures is not null && files.TryGetPlatformHandle()?.Handle is { } filesHwnd)
                    {
                        Diagnostics.WindowCapture.Save(filesHwnd, Path.Combine(captures, "compare-files.png"));
                    }

                    Diagnostics.StartupTrace.Append($"compare files {twoFiles.Node.Path}: \"{files.SummaryText}\" position=\"{files.PositionText}\" kind={files.KindText} encoding={files.EncodingText} rows={files.View.RowCount}\n");

                    if (files.View.Blocks.Count > 0)
                    {
                        files.CopyAcross(0, toRight: true);
                        await files.Comparing;
                        var copied = $"\"{files.SummaryText}\" pending=\"{files.PendingText(1)}\"";
                        files.Undo();
                        await files.Comparing;
                        Diagnostics.StartupTrace.Append($"compare copy across: {copied} undo=\"{files.SummaryText}\" modified={files.IsModified}\n");
                    }

                    var word = (files.View.LeftLines?.FirstOrDefault(l => l.Trim().Length > 0) ?? "").Trim().Split(' ')[0];
                    files.OpenFind();
                    files.FindBox.Text = word;
                    await Task.Delay(100);
                    await files.Finding;
                    Diagnostics.StartupTrace.Append($"compare find \"{word}\": \"{files.FindCountText}\" hits={files.View.Hits.Count}\n");
                    files.CloseFind();

                    files.Close();
                }
                else
                {
                    Diagnostics.StartupTrace.Append("compare files: no window\n");
                }
            }

            list.Close();
        }

        HideComparison();
    }
}
