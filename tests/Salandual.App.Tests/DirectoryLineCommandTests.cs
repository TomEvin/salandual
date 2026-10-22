// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/DirectoryLineCommandTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Panels;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class DirectoryLineCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-dl-").FullName;
    private readonly string _a;
    private readonly string _b;
    private readonly string _inner;

    public DirectoryLineCommandTests()
    {
        _a = Directory.CreateDirectory(Path.Join(_dir, "Alpha")).FullName;
        _b = Directory.CreateDirectory(Path.Join(_dir, "Beta")).FullName;
        _inner = Directory.CreateDirectory(Path.Join(_a, "Inner")).FullName;
        File.WriteAllText(Path.Join(_a, "a.txt"), "x");
        File.WriteAllText(Path.Join(_b, "b.txt"), "x");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_a);
        await w.RightPanel.Panel.NavigateAsync(_b);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame();
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

    private static void Settings(MainWindow w, Func<DirectoryLineSettings, DirectoryLineSettings> change)
    {
        w.Services.Settings.Update(s => s with { Layout = s.Layout with { DirectoryLine = change(s.Layout.DirectoryLine) } });
        Dispatcher.UIThread.RunJobs();
    }

    private static ChangeDirectoryBox Box(MainWindow w) => w.LeftPanel.ChangeDirectory;

    [AvaloniaFact]
    public async Task ShiftF7_OpensInTheBar_WithThePathSelected_AndThisPanelsRecentFolders()
    {
        var w = await Open();
        await w.LeftPanel.Panel.NavigateAsync(_inner);
        await w.LeftPanel.Panel.NavigateAsync(_a);

        Press(w, Key.F7, RawInputModifiers.Shift);
        Assert.True(w.LeftPanel.IsEditingPath);
        var input = Box(w).Input;
        Assert.True(input.IsFocused);
        Assert.Equal(_a, input.Text);
        Assert.Equal((0, _a.Length), (Math.Min(input.SelectionStart, input.SelectionEnd), Math.Max(input.SelectionStart, input.SelectionEnd)));
        Assert.Contains(("Recent", _inner), Box(w).Suggestions);
        Assert.DoesNotContain(Box(w).Suggestions, s => s.Path == _b);

        Press(w, Key.Escape);
        Assert.False(w.LeftPanel.IsEditingPath);
        Assert.True(w.LeftPanel.View.IsFocused);
        Assert.Equal(_a, w.LeftPanel.Panel.Path);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Typing_OffersFolders_TabCompletes_EnterGoes()
    {
        var w = await Open();
        await w.LeftPanel.Panel.NavigateAsync(_dir);
        Press(w, Key.G, RawInputModifiers.Control);
        Box(w).Input.Text = "al";
        await Until(() => Box(w).Suggestions.Contains(("Folder", _a)));

        Press(w, Key.Tab);
        Assert.Equal(_a + "\\", Box(w).Input.Text);
        await Until(() => Box(w).Suggestions.Contains(("Folder", _inner)));

        Press(w, Key.Down);
        Assert.Equal(_inner, Box(w).Input.Text);
        Press(w, Key.Up);
        Assert.Equal(_a + "\\", Box(w).Input.Text);

        Press(w, Key.Enter);
        await Until(() => w.LeftPanel.Panel.Path == _a);
        Assert.False(w.LeftPanel.IsEditingPath);
        Assert.Equal("a.txt", w.LeftPanel.Panel.Items.Last().Name);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AWrongPath_SaysSo_AndKeepsTheText()
    {
        var w = await Open();
        Press(w, Key.F7, RawInputModifiers.Shift);
        Box(w).Input.Text = @"nope\deeper";
        Press(w, Key.Enter);
        await Until(() => Box(w).Error is not null);
        Assert.Contains("isn't a folder or a file here", Box(w).Error);
        Assert.Equal(@"nope\deeper", Box(w).Input.Text);
        Assert.True(w.LeftPanel.IsEditingPath);
        Assert.Contains("error", Box(w).Input.Classes);

        Box(w).Input.Text = "..";
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Box(w).Error);
        Press(w, Key.Enter);
        await Until(() => w.LeftPanel.Panel.Path == _dir);
        Assert.Equal("Alpha", w.LeftPanel.Panel.FocusedEntry?.Name);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AFilesPath_OpensItsFolder_WithTheFileFocused()
    {
        var w = await Open();
        Press(w, Key.F7, RawInputModifiers.Shift);
        Box(w).Input.Text = Path.Join(_b, "B.TXT");
        Press(w, Key.Enter);
        await Until(() => w.LeftPanel.Panel.Path == _b && w.LeftPanel.Panel.FocusedEntry?.Name == "b.txt");
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AltDigit_PicksARecentFolder()
    {
        var w = await Open();
        await w.LeftPanel.Panel.NavigateAsync(_inner);
        await w.LeftPanel.Panel.NavigateAsync(_a);
        Press(w, Key.F7, RawInputModifiers.Shift);
        Assert.Equal(_inner, Box(w).Suggestions[0].Path);
        Press(w, Key.D1, RawInputModifiers.Alt);
        await Until(() => w.LeftPanel.Panel.Path == _inner);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AClickOnTheCurrentFolder_ChangesDirectory_AClickOnAParent_OpensIt()
    {
        var w = await Open();
        var line = w.LeftPanel.DirectoryLine;
        line.Click(line.Crumbs[^1].Bounds.Center);
        Assert.True(w.LeftPanel.IsEditingPath);
        Press(w, Key.Escape);

        line.Click(new Avalonia.Point(line.EditZoneBounds.X + 3, line.EditZoneBounds.Center.Y));
        Assert.True(w.LeftPanel.IsEditingPath);
        Press(w, Key.Escape);

        line.Click(line.Crumbs[^2].Bounds.Center);
        await Until(() => w.LeftPanel.Panel.Path == _dir);
        Assert.False(w.LeftPanel.IsEditingPath);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheFocusGoingElsewhere_ClosesTheBar()
    {
        var w = await Open();
        Press(w, Key.F7, RawInputModifiers.Shift);
        w.RightPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.LeftPanel.IsEditingPath);
        Assert.True(w.RightPanel.View.IsFocused);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AsADialog_TheSameBox_OkGoes()
    {
        var w = await Open();
        Settings(w, d => d with { ChangeDirectory = ChangeDirectoryStyle.Dialog });
        Press(w, Key.G, RawInputModifiers.Control);
        await Until(() => w.OpenChangeDirectoryDialog is not null);
        var dialog = w.OpenChangeDirectoryDialog!;
        Assert.False(w.LeftPanel.IsEditingPath);
        Assert.Equal(_a, dialog.Box.Input.Text);

        dialog.Box.Input.Text = _b;
        dialog.Box.Go(_b);
        await Until(() => w.OpenChangeDirectoryDialog is null && w.LeftPanel.Panel.Path == _b);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AltF12_ListsThisPanelsFolders_ADigitGoes()
    {
        var w = await Open();
        await w.LeftPanel.Panel.NavigateAsync(_inner);
        await w.LeftPanel.Panel.NavigateAsync(_a);

        Press(w, Key.F12, RawInputModifiers.Alt);
        var menu = w.WorkingDirectoriesMenu;
        Assert.True(menu?.IsOpen);
        Assert.True(w.LeftPanel.DirectoryLine.IsHistoryOpen);
        var rows = menu!.Items.OfType<MenuItem>().Where(i => i.Tag is char).ToList();
        Assert.Equal(['1', '2'], rows.Select(r => (char)r.Tag!));

        Press(w, Key.D2);
        await Until(() => w.LeftPanel.Panel.Path == _inner);
        Assert.Null(w.WorkingDirectoriesMenu);
        Assert.False(w.LeftPanel.DirectoryLine.IsHistoryOpen);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Shared_ListsBothPanelsFolders_ClearKeepsTheCurrentOnes()
    {
        var w = await Open();
        Settings(w, d => d with { WorkingDirectories = WorkingDirectoriesScope.Shared });
        await w.LeftPanel.Panel.NavigateAsync(_inner);
        await w.LeftPanel.Panel.NavigateAsync(_a);
        var shared = w.Working.For(0, shared: true).Select(x => x.Path).ToList();
        Assert.Equal([_a, _inner, _b], shared.Take(3));

        w.LeftPanel.DirectoryLine.Click(w.LeftPanel.DirectoryLine.HistoryButtonBounds.Center);
        Assert.True(w.WorkingDirectoriesMenu?.IsOpen);
        var clear = w.WorkingDirectoriesMenu!.Items.OfType<MenuItem>().Single(i => i.Header as string == "_Clear the list");
        w.WorkingDirectoriesMenu.Hide();
        clear.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal([_a, _b], w.Working.For(0, shared: true).Select(x => x.Path));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlF1_ShowsDriveInformation_ForThePanelsDrive()
    {
        var w = await Open();
        Press(w, Key.F1, RawInputModifiers.Control);
        await Until(() => w.OpenDriveInfo is not null);
        var info = w.OpenDriveInfo!;
        await info.Reading;
        Assert.Equal(Path.GetPathRoot(_dir), info.Details?.Root);
        Assert.False(string.IsNullOrEmpty(info.Details?.FileSystem));
        Assert.True(info.Details!.Total > 0);
        Assert.NotNull(info.Ring);
        Assert.Equal(UsageRing.LevelOf(info.Details.Free, info.Details.Total), info.Ring!.Level);
        Assert.Equal(info.Ring.Level != SpaceLevel.Normal, info.StatusLine is not null);
        info.Close();
        await Until(() => w.OpenDriveInfo is null);

        w.LeftPanel.DirectoryLine.SetSpace(_a, 100, 400);
        w.CaptureRenderedFrame();
        w.LeftPanel.DirectoryLine.Click(w.LeftPanel.DirectoryLine.FreeSpaceBounds.Center);
        await Until(() => w.OpenDriveInfo is not null);
        w.OpenDriveInfo!.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DriveInformation_StaysInTheMiddleOfTheWindow_AfterItsDetailsArrive()
    {
        var w = await Open();
        w.Position = new Avalonia.PixelPoint(200, 100);
        Press(w, Key.F1, RawInputModifiers.Control);
        await Until(() => w.OpenDriveInfo is not null);
        var info = w.OpenDriveInfo!;
        await info.Reading;
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame();
        info.CaptureRenderedFrame();

        Assert.True(info.ClientSize.Height > 200);
        Assert.Equal(DialogPlacement.CenterOn(info, w), info.Position);

        info.Position = new Avalonia.PixelPoint(5, 5);
        info.Height = info.ClientSize.Height + 40;
        info.CaptureRenderedFrame();
        Assert.Equal(new Avalonia.PixelPoint(5, 5), info.Position);
        info.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DriveInformation_RenamesInPlace_F2StartsEscCancelsWithoutClosing()
    {
        var info = new Dialogs.DriveInfoDialog(_dir, Core.Listing.DisplayFormat.Default);
        info.Show();
        await info.Reading;
        Dispatcher.UIThread.RunJobs();
        Assert.True(info.CanRename);
        Assert.Null(info.LabelBox);

        info.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(info.IsRenaming);
        Assert.Equal(info.Details!.Label ?? "", info.LabelBox!.Text);
        Assert.True(info.LabelBox.IsFocused);

        info.LabelBox.Text = "a\"b";
        await info.SaveRenameAsync();
        Assert.Contains("can't have", info.RenameError);
        Assert.True(info.IsRenaming);

        info.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(info.IsRenaming);
        Assert.True(info.IsVisible);

        info.StartRename();
        await info.SaveRenameAsync();
        Assert.False(info.IsRenaming);
        info.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(info.IsVisible);
    }

    [Theory]
    [InlineData(50, 100, SpaceLevel.Normal)]
    [InlineData(10, 100, SpaceLevel.Normal)]
    [InlineData(9, 100, SpaceLevel.GettingFull)]
    [InlineData(3, 100, SpaceLevel.GettingFull)]
    [InlineData(2, 100, SpaceLevel.AlmostFull)]
    [InlineData(0, 0, SpaceLevel.Normal)]
    public void SpaceLevel_AmberBelowTenPercentFree_RedBelowThree(long free, long total, SpaceLevel level)
        => Assert.Equal(level, UsageRing.LevelOf(free, total));

    [AvaloniaFact]
    public async Task MaximizeButton_MaximizesAndRestores_AnOptionHidesIt()
    {
        var w = await Open();
        var line = w.RightPanel.DirectoryLine;
        line.Click(line.MaximizeButtonBounds.Center);
        Assert.True(w.IsPanelMaximized);
        Assert.Same(w.RightPanel, w.ActivePanel);
        Assert.True(line.IsMaximized);
        Assert.False(w.LeftPanel.IsVisible);

        w.CaptureRenderedFrame();
        line.Click(line.MaximizeButtonBounds.Center);
        Assert.False(w.IsPanelMaximized);
        Assert.False(line.IsMaximized);

        Settings(w, d => d with { MaximizeButton = false });
        w.CaptureRenderedFrame();
        Assert.Equal(default, line.MaximizeButtonBounds);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Drop_AFolderGoesThere_AFileOpensItsFolder_TextIsAPath()
    {
        var w = await Open();
        w.RightPanel.DirectoryLine.Drop(_inner);
        await Until(() => w.RightPanel.Panel.Path == _inner);
        Assert.Same(w.RightPanel, w.ActivePanel);

        w.LeftPanel.DirectoryLine.Drop("\"" + Path.Join(_b, "b.txt") + "\"\r\nsecond line ignored");
        await Until(() => w.LeftPanel.Panel.Path == _b && w.LeftPanel.Panel.FocusedEntry?.Name == "b.txt");

        w.LeftPanel.DirectoryLine.Drop("..");
        await Until(() => w.LeftPanel.Panel.Path == _dir);

        w.LeftPanel.DirectoryLine.Drop("no such thing");
        await Until(() => w.NoticeMessage?.Contains("isn't a folder or a file", StringComparison.Ordinal) == true);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Options_DirectoryLineSection_SetsTheThreeChoices()
    {
        var w = await Open();
        var options = new OptionsWindow(w.Services);
        options.Show();
        options.ShowCategory("layout");
        Dispatcher.UIThread.RunJobs();
        var buttons = options.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().ToList();
        buttons.First(b => b.Content as string == "Dialog").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        buttons.First(b => b.Content as string == "Both panels").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var d = w.Services.Settings.Current.Layout.DirectoryLine;
        Assert.Equal(ChangeDirectoryStyle.Dialog, d.ChangeDirectory);
        Assert.Equal(WorkingDirectoriesScope.Shared, d.WorkingDirectories);
        options.Close();
        w.CloseNow();
    }
}

public sealed class DirectoryLineCaptureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("salandual-dlcap-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string relative)
    {
        var dir = Directory.CreateDirectory(Path.Join(_root, relative)).FullName;
        File.WriteAllText(Path.Join(dir, "readme.md"), "x");
        return dir;
    }

    [AvaloniaFact]
    public async Task Capture_LineChangeDirectoryHistoryDriveInfo()
    {
        var projects = Folder(@"Projects\salandual");
        Folder(@"Projects\website");
        Folder(@"Projects archive");
        var photos = Folder(@"Pictures\Nyaralás 2026");
        var invoices = Folder(@"Documents\Invoices\2026");
        var w = new MainWindow { Width = 1280, Height = 720 };
        w.Show();
        foreach (var path in new[] { photos, invoices, projects })
        {
            Assert.True(await w.LeftPanel.Panel.NavigateAsync(path));
        }

        Assert.True(await w.RightPanel.Panel.NavigateAsync(invoices));
        Assert.True(await w.LeftPanel.Panel.NavigateAsync(Path.GetDirectoryName(projects)!));
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();

        foreach (var dark in new[] { false, true })
        {
            var suffix = dark ? "dark" : "light";
            ((App)Avalonia.Application.Current!).Theme.Apply(AppearanceSettings.Fixed(dark ? ThemeCatalog.SalandualDarkId : ThemeCatalog.SalandualLightId));
            Dispatcher.UIThread.RunJobs();
            Save(w, $"m23-line-{suffix}.png");

            w.ChangeDirectory(w.LeftPanel);
            Dispatcher.UIThread.RunJobs();
            Save(w, $"m23-cd-recent-{suffix}.png");
            w.LeftPanel.ChangeDirectory.Input.Text = Path.Join(_root, "Pro");
            for (var i = 0; i < 50 && !w.LeftPanel.ChangeDirectory.Suggestions.Any(s => s.Kind == "Folder"); i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            Save(w, $"m23-cd-typing-{suffix}.png");
            w.LeftPanel.ChangeDirectory.Input.Text = @"X:\nope";
            Dispatcher.UIThread.RunJobs();
            w.LeftPanel.ChangeDirectory.Go(@"X:\nope");
            for (var i = 0; i < 50 && w.LeftPanel.ChangeDirectory.Error is null; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            Save(w, $"m23-cd-error-{suffix}.png");
            w.LeftPanel.View.Focus();
            Dispatcher.UIThread.RunJobs();

            var dialog = new Dialogs.ChangeDirectoryDialog(new ChangeDirectoryContext(w.LeftPanel.Panel.Path, [projects, photos, invoices], false, w.LeftPanel.Panel.Path, invoices, w.Services.Settings.Current.HotPaths.Paths, false));
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            Save(dialog, $"m23-cd-dialog-{suffix}.png");
            dialog.Close();

            w.ToggleWorkingDirectories(w.LeftPanel);
            Dispatcher.UIThread.RunJobs();
            if (w.WorkingDirectoriesMenu?.Items.OfType<MenuItem>().FirstOrDefault() is { } first && TopLevel.GetTopLevel(first) is { } popup)
            {
                Save(popup, $"m23-history-{suffix}.png");
            }

            Save(w, $"m23-history-window-{suffix}.png");
            w.WorkingDirectoriesMenu?.Hide();
            Dispatcher.UIThread.RunJobs();

            var info = new Dialogs.DriveInfoDialog(projects, Core.Listing.DisplayFormat.Default);
            info.Show();
            await info.Reading;
            Dispatcher.UIThread.RunJobs();
            Save(info, $"m23-driveinfo-{suffix}.png");
            info.StartRename();
            Dispatcher.UIThread.RunJobs();
            Save(info, $"m23-driveinfo-rename-{suffix}.png");
            info.Close();

            w.ToggleMaximize(0);
            Dispatcher.UIThread.RunJobs();
            Save(w, $"m23-maximized-{suffix}.png");
            w.ToggleMaximize(0);
            Dispatcher.UIThread.RunJobs();
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
