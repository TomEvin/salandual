// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/HotPathTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.App.Panels;
using Salandual.Core.Commands;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class HotPathTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-hot-").FullName;
    private readonly string _a;
    private readonly string _b;

    public HotPathTests()
    {
        _a = Directory.CreateDirectory(Path.Join(_dir, "Alpha")).FullName;
        _b = Directory.CreateDirectory(Path.Join(_dir, "Beta")).FullName;
        File.WriteAllText(Path.Join(_a, "a.txt"), "x");
        File.WriteAllText(Path.Join(_b, "b.txt"), "x");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_a);
        await w.RightPanel.Panel.NavigateAsync(_b);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static HotPathSettings HotPaths(MainWindow w) => w.Services.Settings.Current.HotPaths;

    private static void Define(MainWindow w, params HotPath[] paths)
    {
        w.Services.Settings.Update(s => s with { HotPaths = paths.Aggregate(s.HotPaths, (h, p) => h.With(p)) });
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task CtrlShiftDigit_AssignsTheFolder_QuietlyWithRenameAndUndo()
    {
        var w = await Open();
        Press(w, Key.D3, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.Equal(new HotPath(3, "Alpha", _a), HotPaths(w)[3]);
        Assert.Contains("Hot path 3", w.NoticeMessage);
        Assert.Equal(["Rename…", "Undo"], w.NoticeButtons.Select(b => b.Content as string));
        Assert.Contains(w.HotPathBar.Items, i => i is { Kind: StripItemKind.HotPath, Label: "Alpha", Number: "3" });

        w.NoticeButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(HotPaths(w)[3]);
        Assert.DoesNotContain(w.HotPathBar.Items, i => i.Kind == StripItemKind.HotPath);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlDigit_Goes_CtrlAltDigit_GoesInTheOtherPanel()
    {
        var w = await Open();
        Define(w, new HotPath(1, "Beta", _b), new HotPath(10, "Alpha", _a));

        Press(w, Key.D1, RawInputModifiers.Control);
        await Until(() => w.LeftPanel.Panel.Path == _b);

        Press(w, Key.D0, RawInputModifiers.Control | RawInputModifiers.Alt);
        await Until(() => w.RightPanel.Panel.Path == _a);
        Assert.Same(w.LeftPanel, w.ActivePanel);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task EmptySlot_SaysHowToFillIt()
    {
        var w = await Open();
        Press(w, Key.D7, RawInputModifiers.Control);
        Assert.Equal(_a, w.LeftPanel.Panel.Path);
        Assert.Contains("Hot path 7 is empty", w.NoticeMessage);
        Assert.Contains("Ctrl+Shift+7", w.NoticeMessage);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ShiftDigit_OnlyGoesWhenSwitchedOn()
    {
        var w = await Open();
        Define(w, new HotPath(2, "Beta", _b));

        Press(w, Key.D2, RawInputModifiers.Shift);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(_a, w.LeftPanel.Panel.Path);

        w.Services.Settings.Update(s => s with { HotPaths = s.HotPaths with { ShiftDigits = true } });
        Press(w, Key.D2, RawInputModifiers.Shift);
        await Until(() => w.LeftPanel.Panel.Path == _b);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AddCurrentAndStar_FillTheFirstFreeSlot()
    {
        var w = await Open();
        Define(w, new HotPath(1, "Beta", _b));
        Assert.True(w.Run("HotPaths.AddCurrent", PanelTarget.Active));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(_a, HotPaths(w)[2]?.Path);
        Assert.Contains(w.HotPathBar.Items, i => i is { Kind: StripItemKind.HotPath, Number: "2" });

        Press(w, Key.Tab);
        w.HotPathBar.Click(new StripItem(StripItemKind.Add, "", ""));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(_b, HotPaths(w)[3]?.Path);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Strip_LeavesOutHotPathsMarkedOff_AndAllWhenSwitchedOff()
    {
        var w = await Open();
        Define(w, new HotPath(1, "Beta", _b), new HotPath(2, "Alpha", _a, InStrip: false));
        Assert.Equal(["Beta"], w.HotPathBar.Items.Where(i => i.Kind == StripItemKind.HotPath).Select(i => i.Label));

        Bars(w, b => b with { HotPathBar = false });
        Assert.False(w.HotPathBar.IsVisible);
        Assert.True(w.LeftPanel.Strip.IsVisible);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task HotPaths_AreInOneBarForTheWindow_TheDrivesStripsHaveDrivesOnly()
    {
        var w = await Open();
        Define(w, new HotPath(1, "Beta", _b));
        Assert.True(w.HotPathBar.IsVisible);
        Assert.True(w.HotPathBar.ShowAdd);
        foreach (var strip in new[] { w.LeftPanel.Strip, w.RightPanel.Strip })
        {
            Assert.All(strip.Items, i => Assert.True(i.Kind is StripItemKind.Drive or StripItemKind.Place));
            Assert.False(strip.ShowAdd);
        }

        w.Services.Settings.Update(s => s with { Layout = s.Layout with { Strip = StripMode.Shared } });
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.LeftPanel.Strip.IsVisible);
        Assert.True(w.HotPathBar.IsVisible);

        Bars(w, b => b with { DriveStrip = false });
        Assert.True(w.HotPathBar.IsVisible);

        Assert.True(w.Run("View.ToggleHotPathBar", PanelTarget.Active));
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.HotPathBar.IsVisible);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task HotPathBar_GoesInTheActivePanel_CtrlClickInTheOther()
    {
        var w = await Open();
        Define(w, new HotPath(4, "Beta", _b));
        Press(w, Key.Tab);
        await w.RightPanel.Panel.NavigateAsync(_a);
        var chip = w.HotPathBar.Items.Single(i => i.Kind == StripItemKind.HotPath);
        w.HotPathBar.Click(chip);
        await Until(() => w.RightPanel.Panel.Path == _b);
        Assert.Same(w.RightPanel, w.ActivePanel);
        Assert.Equal(_a, w.LeftPanel.Panel.Path);

        await w.RightPanel.Panel.NavigateAsync(_a);
        w.HotPathBar.Click(chip, ctrl: true);
        await Until(() => w.LeftPanel.Panel.Path == _b);
        Assert.Equal(_a, w.RightPanel.Panel.Path);
        Assert.Same(w.RightPanel, w.ActivePanel);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DriveMenuOpen_TypedKeysDoNotStartQuickSearch_ADigitGoesInThatPanel()
    {
        var w = await Open();
        Define(w, new HotPath(5, "Alpha", _a));
        w.ToggleDriveMenu(w.RightPanel);
        Dispatcher.UIThread.RunJobs();
        w.RightPanel.View.Focus();
        QuickSearchTypingTests.Type(w, "b");
        Assert.Null(w.RightPanel.Panel.QuickSearchText);
        Assert.True(w.DriveMenuFlyout?.IsOpen);

        QuickSearchTypingTests.Type(w, "5");
        await Until(() => w.RightPanel.Panel.Path == _a);
        Assert.Null(w.DriveMenuFlyout);
        Assert.Equal(_a, w.LeftPanel.Panel.Path);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DriveMenuOpen_EscCloses_AndTypingSearchesAgain()
    {
        var w = await Open();
        w.ToggleDriveMenu(w.LeftPanel);
        Dispatcher.UIThread.RunJobs();
        w.LeftPanel.View.Focus();
        Press(w, Key.Escape);
        Assert.Null(w.DriveMenuFlyout);

        QuickSearchTypingTests.Type(w, "a");
        Assert.Equal("a", w.LeftPanel.Panel.QuickSearchText);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task HotPathChip_RightClick_OffersOpenEditClear()
    {
        var w = await Open();
        Define(w, new HotPath(4, "Beta", _b));
        var chip = w.HotPathBar.Items.Single(i => i.Kind == StripItemKind.HotPath);
        var menu = w.ShowStripMenu(w.LeftPanel, w.HotPathBar, chip);
        Dispatcher.UIThread.RunJobs();
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Contains(items, i => i.Header as string == "_Edit…");
        Assert.Contains(items, i => i.Header as string == "Open in _other panel");

        items.Single(i => i.Header as string == "_Clear").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(HotPaths(w)[4]);
        Assert.Contains("cleared", w.NoticeMessage);
        menu.Close();

        var empty = w.ShowStripMenu(w.LeftPanel, w.HotPathBar, null);
        Dispatcher.UIThread.RunJobs();
        var add = empty.Items.OfType<MenuItem>().First();
        Assert.Equal("_Add this folder as hot path 1", add.Header);
        Assert.Contains(empty.Items.OfType<MenuItem>(), i => i.Header as string == "Hot _paths bar" && i.IsChecked);
        empty.Close();
        w.CloseNow();
    }

    private static void Bars(MainWindow w, Func<BarVisibility, BarVisibility> change)
    {
        w.Services.Settings.Update(s => s with { Layout = s.Layout with { Bars = change(s.Layout.Bars) } });
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task DriveButton_OpensTheMenu_ASecondClickClosesIt()
    {
        var w = await Open();
        Define(w, new HotPath(5, "Beta", _b));

        w.ToggleDriveMenu(w.LeftPanel);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.DriveMenuFlyout?.IsOpen);
        Assert.True(w.LeftPanel.DirectoryLine.IsDriveMenuOpen);
        var items = w.DriveMenuFlyout!.Items.OfType<MenuItem>().ToList();
        Assert.Contains(items, i => i.Tag is char c && c == 'C');
        Assert.Contains(items, i => i.Tag is char c && c == '5');
        Assert.Contains(items, i => !i.IsEnabled && ContainsText(i.Header, "Registry"));

        w.ToggleDriveMenu(w.LeftPanel);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(w.DriveMenuFlyout);
        Assert.False(w.LeftPanel.DirectoryLine.IsDriveMenuOpen);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AltF2_OpensTheRightPanelsDriveMenu_AHotPathGoesThere()
    {
        var w = await Open();
        Define(w, new HotPath(6, "Alpha", _a));
        Press(w, Key.F2, RawInputModifiers.Alt);
        Assert.True(w.DriveMenuFlyout?.IsOpen);
        Assert.True(w.RightPanel.DirectoryLine.IsDriveMenuOpen);

        var hot = w.DriveMenuFlyout!.Items.OfType<MenuItem>().Single(i => i.Tag is char c && c == '6');
        hot.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => w.RightPanel.Panel.Path == _a);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DriveMenu_FollowsTheOptions()
    {
        var w = await Open();
        w.Services.Settings.Update(s => s with
        {
            DriveMenu = s.DriveMenu with
            {
                HiddenDrives = "C",
                Places = [.. s.DriveMenu.Places.Select(p => p.Id == DriveMenuSettings.OtherPanel ? p with { InMenu = false } : p)],
            },
        });
        w.ToggleDriveMenu(w.LeftPanel);
        Dispatcher.UIThread.RunJobs();
        var items = w.DriveMenuFlyout!.Items.OfType<MenuItem>().ToList();
        Assert.DoesNotContain(items, i => i.Tag is char c && c == 'C');
        Assert.DoesNotContain(items, i => ContainsText(i.Header, "As other panel"));
        Assert.DoesNotContain(w.LeftPanel.Strip.Items, i => i is { Kind: StripItemKind.Drive, Target: @"C:\" });
        w.ToggleDriveMenu(w.LeftPanel);
        w.CloseNow();
    }

    [AvaloniaFact]
    public void ListFilter_MatchesDigitsNamesAndFolders()
    {
        HotPath[] paths = [new(1, "Projects", @"D:\Work\Projects"), new(10, "Photos", @"P:\Foto"), new(12, "Downloads", @"C:\Users\x\Downloads")];
        Assert.Equal([10], HotPathListDialog.Filter(paths, "0").Select(p => p.Slot));
        Assert.Equal([12], HotPathListDialog.Filter(paths, "12").Select(p => p.Slot));
        Assert.Equal([1, 10], HotPathListDialog.Filter(paths, "p").Select(p => p.Slot));
        Assert.Equal([10], HotPathListDialog.Filter(paths, "foto").Select(p => p.Slot));
        Assert.Equal(3, HotPathListDialog.Filter(paths, " ").Count);
    }

    [AvaloniaFact]
    public void ListDialog_EnterGoes_CtrlEnterGoesOther_DelClears()
    {
        var services = AppServices.CreateIsolated();
        services.Settings.Update(s => s with { HotPaths = s.HotPaths.With(new HotPath(1, "One", _a)).With(new HotPath(2, "Two", _b)) });
        HotPath? cleared = null;
        var dialog = new HotPathListDialog(services.Settings, p => cleared = p);
        dialog.Show();
        Assert.Equal(2, dialog.Rows.Count);
        Assert.Equal(0, dialog.SelectedIndex);

        dialog.FilterBox.Focus();
        dialog.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal(1, dialog.SelectedIndex);
        dialog.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal(2, cleared?.Slot);
        Assert.Null(services.Settings.Current.HotPaths[2]);
        Assert.Single(dialog.Rows);
        dialog.Close();
    }

    [AvaloniaFact]
    public void EditDialog_SavesNameFolderSlotAndBoxes()
    {
        var dialog = new HotPathEditDialog(new HotPath(3, "Alpha", _a));
        dialog.Show();
        dialog.NameBox.Text = "  Renamed ";
        dialog.SlotBox.SelectedIndex = 11;
        dialog.MenuBox.IsChecked = false;
        dialog.Accept();
        Assert.Equal(new HotPath(12, "Renamed", _a, InStrip: true, InMenu: false), dialog.Result);
    }

    [AvaloniaFact]
    public void EditDialog_NeedsAFolder_AndNamesAnEmptyNameAfterIt()
    {
        var dialog = new HotPathEditDialog(new HotPath(3, "Alpha", _a));
        dialog.Show();
        dialog.PathBox.Text = " ";
        dialog.Accept();
        Assert.Null(dialog.Result);
        Assert.True(dialog.IsVisible);

        dialog.PathBox.Text = _b;
        dialog.NameBox.Text = "";
        dialog.Accept();
        Assert.Equal("Beta", dialog.Result?.Name);
    }

    [AvaloniaFact]
    public async Task Options_ChoicesForAHiddenBar_AreGreyed()
    {
        var w = await Open();
        Define(w, new HotPath(1, "Beta", _b));
        var options = new OptionsWindow(w.Services);
        options.Show();
        options.ShowCategory("layout");
        Dispatcher.UIThread.RunJobs();
        Control Cards() => options.Page.GetLogicalDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Shared");
        var cards = Cards();
        Assert.True(cards.IsEffectivelyEnabled);
        options.PageScroll.Offset = new Avalonia.Vector(0, 200);
        Dispatcher.UIThread.RunJobs();

        Bars(w, b => b with { DriveStrip = false });
        Dispatcher.UIThread.RunJobs();
        Assert.False(cards.IsEffectivelyEnabled);
        Assert.Same(cards, Cards());
        Assert.Equal(200, options.PageScroll.Offset.Y);

        options.ShowCategory("hotpaths");
        Dispatcher.UIThread.RunJobs();
        CheckBox Box(string name) => options.Page.GetLogicalDescendants().OfType<CheckBox>().First(c => Avalonia.Automation.AutomationProperties.GetName(c) == name);
        Assert.False(Box("Desktop in the strip").IsEnabled);
        Assert.True(Box("Desktop in the drive menu").IsEnabled);
        Assert.True(Box("Hot path 1 in the hot paths bar").IsEnabled);

        var barBox = Box("Hot path 1 in the hot paths bar");
        Bars(w, b => b with { HotPathBar = false });
        Dispatcher.UIThread.RunJobs();
        Assert.False(barBox.IsEnabled);
        Assert.Same(barBox, Box("Hot path 1 in the hot paths bar"));

        Bars(w, b => b with { DriveStrip = true, HotPathBar = true });
        Dispatcher.UIThread.RunJobs();
        Assert.True(Box("Desktop in the strip").IsEnabled);
        Assert.True(Box("Hot path 1 in the hot paths bar").IsEnabled);
        options.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Options_HotPathsPage_EditsApplyAtOnce()
    {
        var w = await Open();
        var options = new OptionsWindow(w.Services) { PanelFolder = () => _b };
        options.Show();
        options.ShowCategory("hotpaths");
        Dispatcher.UIThread.RunJobs();
        var table = options.Page.GetLogicalDescendants().OfType<HotPathTable>().Single();

        table.List.SelectedIndex = 3;
        table.PathBox.Text = _b;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new HotPath(4, "Beta", _b), HotPaths(w)[4]);
        Assert.Contains(w.HotPathBar.Items, i => i is { Kind: StripItemKind.HotPath, Number: "4" });

        table.NameBox.Text = "Build";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Build", HotPaths(w)[4]?.Name);
        options.Close();
        w.CloseNow();
    }

    private static bool ContainsText(object? header, string text) => header switch
    {
        string s => s.Contains(text, StringComparison.Ordinal),
        TextBlock t => t.Text?.Contains(text, StringComparison.Ordinal) == true,
        Panel p => p.Children.Any(c => ContainsText(c, text)),
        _ => false,
    };
}

public sealed class HotPathCaptureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("salandual-hotcap-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string relative)
    {
        var dir = Directory.CreateDirectory(Path.Join(_root, relative)).FullName;
        File.WriteAllText(Path.Join(dir, "readme.md"), "x");
        return dir;
    }

    [AvaloniaFact]
    public async Task Capture_StripDriveMenuListEditAndOptions()
    {
        var projects = Folder(@"Work\Projects");
        var photos = Folder(@"Photos\Nyaralás 2026");
        var music = Folder("Music");
        var w = new MainWindow { Width = 1280, Height = 720 };
        w.Show();
        w.Services.Settings.Update(s => s with
        {
            HotPaths = s.HotPaths
                .With(new HotPath(1, "Projects", projects))
                .With(new HotPath(2, "Nyaralás 2026", photos))
                .With(new HotPath(3, "Music", music, InStrip: false))
                .With(new HotPath(11, "Work", Path.GetDirectoryName(projects)!)),
        });
        Assert.True(await w.LeftPanel.Panel.NavigateAsync(projects));
        Assert.True(await w.RightPanel.Panel.NavigateAsync(photos));
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();

        foreach (var dark in new[] { false, true })
        {
            var suffix = dark ? "dark" : "light";
            ((App)Avalonia.Application.Current!).Theme.Apply(AppearanceSettings.Fixed(dark ? ThemeCatalog.SalandualDarkId : ThemeCatalog.SalandualLightId));
            Dispatcher.UIThread.RunJobs();
            Save(w, $"m22-main-{suffix}.png");

            w.ToggleDriveMenu(w.LeftPanel);
            await Task.Delay(1700);
            Dispatcher.UIThread.RunJobs();
            if (w.DriveMenuFlyout?.Items.OfType<MenuItem>().FirstOrDefault() is { } first && TopLevel.GetTopLevel(first) is { } popup)
            {
                Save(popup, $"m22-drivemenu-{suffix}.png");
            }

            Save(w, $"m22-drivemenu-window-{suffix}.png");
            w.ToggleDriveMenu(w.LeftPanel);
            Dispatcher.UIThread.RunJobs();

            var list = new HotPathListDialog(w.Services.Settings);
            list.Show();
            Dispatcher.UIThread.RunJobs();
            Save(list, $"m22-list-{suffix}.png");
            list.Close();

            var edit = new HotPathEditDialog(w.Services.Settings.Current.HotPaths[2]!);
            edit.Show();
            Dispatcher.UIThread.RunJobs();
            Save(edit, $"m22-edit-{suffix}.png");
            edit.Close();

            var options = new OptionsWindow(w.Services) { PanelFolder = () => projects };
            options.Show();
            options.ShowCategory("hotpaths");
            Dispatcher.UIThread.RunJobs();
            Save(options, $"m22-options-{suffix}.png");
            options.PageScroll.Offset = new Avalonia.Vector(0, 640);
            Dispatcher.UIThread.RunJobs();
            Save(options, $"m22-options-drives-{suffix}.png");
            options.Close();
        }

        ((App)Avalonia.Application.Current!).Theme.Apply(new AppearanceSettings());
        w.CloseNow();
    }

    private static void Save(TopLevel w, string name)
    {
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
}
