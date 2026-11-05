// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/PanelTabsTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class PanelTabsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-tabs-").FullName;
    private readonly string _work;
    private readonly string _photos;
    private readonly string _reports;

    public PanelTabsTests()
    {
        _work = Directory.CreateDirectory(Path.Join(_dir, "Work")).FullName;
        _photos = Directory.CreateDirectory(Path.Join(_dir, "Photos")).FullName;
        _reports = Directory.CreateDirectory(Path.Join(_work, "Reports")).FullName;
        File.WriteAllText(Path.Join(_work, "notes.txt"), "n");
        File.WriteAllText(Path.Join(_photos, "IMG_0412.HEIC"), "12");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open(AppServices? services = null, double width = 1100)
    {
        var w = services is null ? new MainWindow() : new MainWindow(services);
        w.Width = width;
        w.Height = 700;
        w.Show();
        if (services is null)
        {
            await w.LeftPanel.Panel.NavigateAsync(_work);
            await w.RightPanel.Panel.NavigateAsync(_photos);
        }

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

    private static async Task Until(Func<bool> condition, int tries = 500)
    {
        for (var i = 0; i < tries && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static string[] Paths(MainWindow w, PanelHost host) => [.. w.TabsOf(host).Select(t => t.Path)];

    private static Point TabPoint(MainWindow w, PanelHost host, int index)
    {
        w.CaptureRenderedFrame();
        var r = host.Tabs.TabBounds[index];
        return host.Tabs.TranslatePoint(new Point(r.X + 20, r.Center.Y), w)!.Value;
    }

    [AvaloniaFact]
    public async Task CtrlT_OpensTheSameFolder_CtrlTab_GoesRound_CtrlW_Closes_CtrlShiftT_BringsItBack()
    {
        var w = await Open();
        var left = w.LeftPanel;
        Assert.False(left.Tabs.IsVisible);

        left.Panel.SetView(PanelView.Brief);
        left.Panel.SetSort(new SortSpec(SortColumn.Size));
        var first = left.Panel;
        Press(w, Key.T, RawInputModifiers.Control);
        await Until(() => w.TabsOf(left).Count == 2 && left.Panel.Path == _work);
        Assert.NotSame(first, left.Panel);
        Assert.Equal(1, w.ShownTab(left));
        Assert.True(left.Tabs.IsVisible);
        Assert.Equal(PanelView.Brief, left.Panel.View);
        Assert.Equal(SortColumn.Size, left.Panel.Sort.Column);

        Assert.True(await left.Panel.NavigateAsync(_reports));
        Assert.Equal(["Work", "Reports"], left.Tabs.Items.Select(t => t.Title));
        Press(w, Key.Tab, RawInputModifiers.Control);
        Assert.Same(first, left.Panel);
        Assert.Equal(_work, left.Panel.Path);
        Press(w, Key.Tab, RawInputModifiers.Control);
        Assert.Equal(_reports, left.Panel.Path);
        Press(w, Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal(_work, left.Panel.Path);
        Press(w, Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal(_reports, left.Panel.Path);

        Press(w, Key.W, RawInputModifiers.Control);
        Assert.Single(w.TabsOf(left));
        Assert.Same(first, left.Panel);
        Assert.False(left.Tabs.IsVisible);
        Press(w, Key.W, RawInputModifiers.Control);
        Assert.Single(w.TabsOf(left));

        Press(w, Key.T, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Until(() => w.TabsOf(left).Count == 2 && left.Panel.Path == _reports);
        Assert.Equal(1, w.ShownTab(left));
        Assert.Equal([_work, _reports], Paths(w, left));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheStrip_ShowsAlways_AsASetting_AndItsMouse_Selects_Closes_Moves()
    {
        var w = await Open();
        var left = w.LeftPanel;
        w.Services.Settings.Update(s => s with { Layout = s.Layout with { TabBar = TabBarMode.Always } });
        Dispatcher.UIThread.RunJobs();
        Assert.True(left.Tabs.IsVisible);

        w.NewTab(left, _photos);
        w.NewTab(left, _reports);
        await Until(() => left.Panel.Path == _reports && w.TabsOf(left)[1].Path == _photos);
        Assert.Equal([_work, _photos, _reports], Paths(w, left));

        w.MouseDown(TabPoint(w, left, 0), MouseButton.Left);
        w.MouseUp(TabPoint(w, left, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(_work, left.Panel.Path);
        w.MouseDown(TabPoint(w, left, 1), MouseButton.Middle);
        w.MouseUp(TabPoint(w, left, 1), MouseButton.Middle);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([_work, _reports], Paths(w, left));

        var from = TabPoint(w, left, 0);
        var to = TabPoint(w, left, 1) + new Point(60, 0);
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(from + new Point(10, 0), RawInputModifiers.LeftMouseButton);
        w.MouseMove(to, RawInputModifiers.LeftMouseButton);
        w.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([_reports, _work], Paths(w, left));
        Assert.Equal(_work, left.Panel.Path);
        Assert.Equal(1, w.ShownTab(left));

        var drag = TabPoint(w, left, 1);
        var onRight = w.RightPanel.View.TranslatePoint(new Point(100, 100), w)!.Value;
        w.MouseDown(drag, MouseButton.Left);
        w.MouseMove(drag + new Point(10, 10), RawInputModifiers.LeftMouseButton);
        w.MouseMove(onRight, RawInputModifiers.LeftMouseButton);
        w.MouseUp(onRight, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([_reports], Paths(w, left));
        Assert.Equal([_photos, _work], Paths(w, w.RightPanel));
        Assert.Equal(_work, w.RightPanel.Panel.Path);
        Assert.True(w.RightPanel.IsActive);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ALockedTab_CantBeClosed_AndAsASetting_StaysInItsFolder()
    {
        var w = await Open();
        var left = w.LeftPanel;
        w.NewTab(left, _photos);
        await Until(() => left.Panel.Path == _photos);
        Assert.True(w.Run("Tab.Lock", Core.Commands.PanelTarget.Active));
        Assert.True(w.TabsOf(left)[1].Locked);
        Assert.True(left.Tabs.Items[1].Locked);

        Press(w, Key.W, RawInputModifiers.Control);
        Assert.Equal(2, w.TabsOf(left).Count);
        Assert.Equal(AppStrings.Tabs_LockedNoClose, w.NoticeText.Text);

        Assert.True(await left.Panel.NavigateAsync(_work));
        Assert.Equal(2, w.TabsOf(left).Count);

        w.Services.Settings.Update(s => s with { Panels = s.Panels with { Tabs = s.Panels.Tabs with { Locked = LockedTab.StaysInFolder } } });
        var locked = left.Panel;
        Assert.True(await locked.NavigateAsync(_reports));
        await Until(() => w.TabsOf(left).Count == 3 && left.Panel.Path == _reports);
        Assert.Equal(_work, locked.Path);
        Assert.Equal([_work, _work, _reports], Paths(w, left));
        Assert.Equal(2, w.ShownTab(left));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheTabsMenu_Colours_Copies_AndMovesToTheOtherPanel_AndCtrlU_TakesTheTabsAlong()
    {
        var w = await Open();
        var left = w.LeftPanel;
        var right = w.RightPanel;
        w.NewTab(left, _reports);
        await Until(() => left.Panel.Path == _reports);

        var menu = w.ShowTabMenu(left, 1, new Point(10, 10));
        var colour = menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == AppStrings.Tabs_Colour);
        var teal = colour.Items.OfType<MenuItem>().Single(i => (string?)i.Header == AppStrings.Hl_Teal);
        teal.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        Assert.Equal(NameColour.Teal, w.TabsOf(left)[1].Colour);
        Assert.Equal(NameColour.Teal, left.Tabs.Items[1].Colour);

        Assert.True(w.Run("Tab.CopyToOther", Core.Commands.PanelTarget.Left));
        await Until(() => right.Panel.Path == _reports);
        Assert.Equal([_photos, _reports], Paths(w, right));
        Assert.Equal([_work, _reports], Paths(w, left));

        w.Run("Tab.MoveToOther", Core.Commands.PanelTarget.Left);
        Assert.Equal([_work], Paths(w, left));
        Assert.Equal([_photos, _reports, _reports], Paths(w, right));
        Assert.Equal(NameColour.Teal, w.TabsOf(right)[2].Colour);

        w.Run("Panel.Swap", Core.Commands.PanelTarget.Active);
        Assert.Equal([_photos, _reports, _reports], Paths(w, left));
        Assert.Equal([_work], Paths(w, right));
        Assert.False(right.Tabs.IsVisible);
        Assert.True(left.Tabs.IsVisible);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ManyTabs_Shrink_ThenScroll_OrWrap()
    {
        var w = await Open(width: 900);
        var left = w.LeftPanel;
        for (var i = 0; i < 3; i++)
        {
            w.NewTab(left, _photos);
        }

        w.CaptureRenderedFrame();
        Assert.False(left.Tabs.Scrolls);
        for (var i = 0; i < 9; i++)
        {
            w.NewTab(left, _photos);
        }

        w.CaptureRenderedFrame();
        Assert.True(left.Tabs.Scrolls);
        Assert.All(left.Tabs.TabBounds, r => Assert.True(r.Width >= 72));
        Assert.Equal(w.TabsOf(left).Count - 1, w.ShownTab(left));

        var list = w.ShowTabList(left, default);
        Assert.Equal(w.TabsOf(left).Count, list.Items.Count);
        list.Close();

        w.Services.Settings.Update(s => s with { Panels = s.Panels with { Tabs = s.Panels.Tabs with { Many = ManyTabs.Rows } } });
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame();
        Assert.False(left.Tabs.Scrolls);
        Assert.True(left.Tabs.Rows > 1);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task FilesDroppedOnATab_GoIntoItsFolder()
    {
        var w = await Open();
        var left = w.LeftPanel;
        w.NewTab(left, _reports);
        await Until(() => left.Panel.Path == _reports);
        w.CaptureRenderedFrame();

        var at = TabPoint(w, left, 0);
        var file = Path.Join(_photos, "IMG_0412.HEIC");
        var feedback = w.DragEnterAt(new DropData([file], null), DragKeys.LeftButton, at, DropEffects.Copy | DropEffects.Move);
        Assert.Equal(DropEffects.Copy, feedback.Effect);
        Assert.Equal("Work", feedback.Note!.Insert);
        Assert.Equal(0, left.Tabs.DropIndex);
        w.DropAt(DragKeys.None, at, DropEffects.Copy | DropEffects.Move);
        Assert.Equal(-1, left.Tabs.DropIndex);
        await Until(() => File.Exists(Path.Join(_work, "IMG_0412.HEIC")));
        Assert.True(File.Exists(file));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AtStart_TheTabsComeBack_WithTheirLooks_AndAGoneFolderOpensItsParent()
    {
        var gone = Path.Join(_work, "Gone", "Deeper");
        var services = AppServices.CreateIsolated();
        var session = new SessionState(_reports, _photos, 0, new Dictionary<string, string>())
        {
            Tabs =
            [
                new SavedTab(0, gone, PanelView.Brief, Colour: NameColour.Teal),
                new SavedTab(0, _reports, Locked: true, Sort: new SortSpec(SortColumn.Time, true)),
                new SavedTab(0, _photos, Filter: "*.heic"),
                new SavedTab(1, _photos),
            ],
            LeftTab = 1,
        };
        services.UseSession(session, (_reports, _photos));
        var w = await Open(services);
        var left = w.LeftPanel;
        await Until(() => left.Panel.Path == _reports);

        Assert.Equal([gone, _reports, _photos], Paths(w, left));
        Assert.Equal(1, w.ShownTab(left));
        Assert.True(w.TabsOf(left)[1].Locked);
        Assert.Equal(new SortSpec(SortColumn.Time, true), left.Panel.Sort);
        Assert.Equal(NameColour.Teal, w.TabsOf(left)[0].Colour);
        Assert.Equal("Deeper", left.Tabs.Items[0].Title);
        Assert.Single(w.TabsOf(w.RightPanel));

        w.ShowTab(0, 0);
        await Until(() => left.Panel.Path == _work);
        Assert.Equal(PanelView.Brief, left.Panel.View);
        w.ShowTab(0, 2);
        await Until(() => left.Panel.Path == _photos);
        Assert.Equal("*.heic", left.Panel.Filter?.Mask.Text);
        w.CloseNow();

        var only = AppServices.CreateIsolated();
        only.Settings.Update(s => s with { Panels = s.Panels with { Tabs = s.Panels.Tabs with { AtStart = TabsAtStart.ActiveOnly } } });
        only.UseSession(session, (_reports, _photos));
        var w2 = await Open(only);
        await Until(() => w2.LeftPanel.Panel.Path == _reports);
        Assert.Single(w2.TabsOf(w2.LeftPanel));
        Assert.True(w2.TabsOf(w2.LeftPanel)[0].Locked);
        w2.CloseNow();
    }

    [AvaloniaFact]
    public async Task Options_Panels_HasTheTabs_AndTheStripLooksAsTheMockup()
    {
        var app = (App)Application.Current!;
        var options = new Options.OptionsWindow();
        options.Show();
        options.SearchBox.Text = "tab";
        options.SearchNow();
        Dispatcher.UIThread.RunJobs();
        foreach (var title in new[] { AppStrings.Opt_TabStrip, AppStrings.Opt_NewTab, AppStrings.Opt_LockedTab, AppStrings.Opt_TabColours, AppStrings.Opt_TabsAtStart, AppStrings.Opt_ManyTabs })
        {
            Assert.Contains(options.Find("tab"), i => i.Title == title);
        }

        Capture(options, "tabs-options.png", 980, 1000);
        options.Close();
        app.Services.Settings.Update(_ => new AppSettings());

        var w = await Open();
        w.NewTab(w.LeftPanel, _reports);
        w.NewTab(w.LeftPanel, _photos);
        w.ShowTab(0, 1);
        w.Run("Tab.Lock", Core.Commands.PanelTarget.Active);
        w.TabsOf(w.LeftPanel)[0].Colour = NameColour.Blue;
        w.NewTab(w.RightPanel, _work);
        w.TabsOf(w.RightPanel)[0].Colour = NameColour.Green;
        w.Services.Settings.Update(s => s with { Layout = s.Layout });
        await Until(() => w.LeftPanel.Panel.Path == _reports && w.RightPanel.Panel.Path == _work);
        Capture(w, "tabs-main.png", 1100, 500);
        w.Services.Settings.Update(s => s with { Panels = s.Panels with { Tabs = s.Panels.Tabs with { Colours = TabColourLook.Tint } } });
        Capture(w, "tabs-tint.png", 1100, 500);
        for (var i = 0; i < 12; i++)
        {
            w.NewTab(w.LeftPanel, _photos);
        }

        Capture(w, "tabs-scroll.png", 1100, 500);
        w.Services.Settings.Update(s => s with { Panels = s.Panels with { Tabs = s.Panels.Tabs with { Many = ManyTabs.Rows } } });
        Capture(w, "tabs-rows.png", 1100, 500);
        w.CloseNow();
    }

    private static void Capture(Window w, string name, double width, double height)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            w.Width = width;
            w.Height = height;
            for (var k = 0; k < 10; k++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task Enter_OnAFoldersShortcut_OpensTheFolder()
    {
        var link = await ShellLinks.CreateAsync(_reports, _photos);
        var w = await Open();
        var right = w.RightPanel;
        await right.Panel.RefreshAsync();
        right.Panel.SetFocus(right.Panel.Items.ToList().FindIndex(e => e.Name == Path.GetFileName(link)));
        w.Run("Navigation.Execute", Core.Commands.PanelTarget.Right);
        await Until(() => right.Panel.Path == _reports);
        Assert.True(File.Exists(link));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AtStart_AServersTab_ComesBackUnopened_AndThePanelShowsOneOnYourDisk()
    {
        const string server = "sftp://deploy@files.example.org/var/www";
        var services = AppServices.CreateIsolated();
        var session = new SessionState(server, _photos, 0, new Dictionary<string, string>())
        {
            Tabs = [new SavedTab(0, _reports), new SavedTab(0, server), new SavedTab(1, _photos)],
            LeftTab = 1,
        };
        Assert.Equal(_reports, AppServices.LocalTab(session, 0));
        services.UseSession(session, (_reports, _photos));
        var w = await Open(services);
        var left = w.LeftPanel;
        await Until(() => left.Panel.Path == _reports);
        Assert.Equal([_reports, server], Paths(w, left));
        Assert.Equal(0, w.ShownTab(left));
        Assert.Equal(0, ((Salandual.Remote.MemoryServers)w.Services.Connector).Servers.Values.Sum(s => s.Connects));
        w.CloseNow();
    }
}
