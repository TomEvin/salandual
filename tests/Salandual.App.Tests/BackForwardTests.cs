// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/BackForwardTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.Core.Commands;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class BackForwardTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-bf-").FullName;
    private readonly string _a;
    private readonly string _b;
    private readonly string _c;

    public BackForwardTests()
    {
        _a = Directory.CreateDirectory(Path.Join(_dir, "Alpha")).FullName;
        _b = Directory.CreateDirectory(Path.Join(_dir, "Beta")).FullName;
        _c = Directory.CreateDirectory(Path.Join(_dir, "Gamma")).FullName;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open(Func<BackForwardSettings, BackForwardSettings>? settings = null)
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        if (settings is not null)
        {
            Change(w, settings);
        }

        w.Show();
        foreach (var path in new[] { _a, _b, _c })
        {
            await w.LeftPanel.Panel.NavigateAsync(path);
        }

        await w.RightPanel.Panel.NavigateAsync(_b);
        await w.RightPanel.Panel.NavigateAsync(_a);
        w.LeftPanel.View.Focus();
        if (!ReferenceEquals(w.ActivePanel, w.LeftPanel))
        {
            w.Run("Panel.SwitchActive", PanelTarget.Active);
        }

        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame();
        return w;
    }

    private static void Change(MainWindow w, Func<BackForwardSettings, BackForwardSettings> change)
    {
        w.Services.Settings.Update(s => s with { Layout = s.Layout with { BackForward = change(s.Layout.BackForward) } });
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

    private static StripItem Chip(MainWindow w, StripItemKind kind, string target) => w.BackForwardBar.Items.Single(i => i.Kind == kind && i.Target == target);

    private static string Label(object? header) => header switch
    {
        string s => s.Replace("_", "", StringComparison.Ordinal),
        TextBlock t => t.Text ?? "",
        Control c => string.Join(" | ", c.GetLogicalDescendants().OfType<TextBlock>().Where(t => t is not Avalonia.Controls.Primitives.AccessText).Select(t => t.Text)),
        _ => "",
    };

    private static List<string> Rows(ContextMenu menu) => [.. menu.Items.OfType<MenuItem>().Select(m => Label(m.Header))];

    private static void Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task TheBar_HasARowOfItsOwn_ForTheActivePanel_GreyedWithNowhereToGo()
    {
        var w = await Open();
        Assert.True(w.BackForwardBar.IsVisible);
        Assert.Contains(w.BarRowStrips, r => r.Count == 1 && r[0] == w.BackForwardBar);
        Assert.Equal([MainWindow.NavBack, MainWindow.NavBack, MainWindow.NavForward, MainWindow.NavForward, MainWindow.NavUp], w.BackForwardBar.Items.Select(i => i.Target));
        Assert.Equal([StripItemKind.Nav, StripItemKind.NavList, StripItemKind.Nav, StripItemKind.NavList, StripItemKind.Nav], w.BackForwardBar.Items.Select(i => i.Kind));
        Assert.True(w.BackForwardBar.Items[3].SeparatorAfter);
        Assert.False(Chip(w, StripItemKind.Nav, MainWindow.NavBack).Dim);
        Assert.True(Chip(w, StripItemKind.Nav, MainWindow.NavForward).Dim);
        Assert.StartsWith("Back to " + _b, Chip(w, StripItemKind.Nav, MainWindow.NavBack).Tip, StringComparison.Ordinal);
        Assert.False(w.LeftPanel.DirectoryLine.ShowBackForward);

        w.BackForwardBar.Click(Chip(w, StripItemKind.Nav, MainWindow.NavBack));
        await Until(() => w.LeftPanel.Panel.Path == _b);
        Assert.False(Chip(w, StripItemKind.Nav, MainWindow.NavForward).Dim);
        w.BackForwardBar.Click(Chip(w, StripItemKind.Nav, MainWindow.NavForward));
        await Until(() => w.LeftPanel.Panel.Path == _c);

        w.BackForwardBar.Click(Chip(w, StripItemKind.Nav, MainWindow.NavUp));
        await Until(() => w.LeftPanel.Panel.Path == _dir);
        w.BackForwardBar.Click(Chip(w, StripItemKind.Nav, MainWindow.NavBack));
        await Until(() => w.LeftPanel.Panel.Path == _c);

        w.Run("Panel.SwitchActive", PanelTarget.Active);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Back to " + _b, Chip(w, StripItemKind.Nav, MainWindow.NavBack).Tip, StringComparison.Ordinal);
        w.BackForwardBar.Click(Chip(w, StripItemKind.Nav, MainWindow.NavBack));
        await Until(() => w.RightPanel.Panel.Path == _b);
        Assert.Equal(_c, w.LeftPanel.Panel.Path);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheList_ShowsThisPanelsSteps_NearestFirst_AndJumps()
    {
        var w = await Open();
        w.BackForwardBar.Click(Chip(w, StripItemKind.NavList, MainWindow.NavBack));
        var menu = w.StepsMenu!;
        var rows = Rows(menu);
        Assert.Equal("BACK IN THIS PANEL", rows[0]);
        Assert.Equal(_b, rows[1]);
        Assert.Equal(_a, rows[2]);
        Assert.Equal("Clear the steps", rows[^1]);

        w.BackForwardBar.Click(Chip(w, StripItemKind.NavList, MainWindow.NavBack));
        Assert.Null(w.StepsMenu);
        w.BackForwardBar.Click(Chip(w, StripItemKind.NavList, MainWindow.NavBack));
        menu = w.StepsMenu!;
        menu.Close();
        w.BackForwardBar.Click(Chip(w, StripItemKind.NavList, MainWindow.NavBack));
        Assert.Null(w.StepsMenu);
        w.BackForwardBar.Click(Chip(w, StripItemKind.NavList, MainWindow.NavBack));
        menu = w.StepsMenu!;

        Click(menu.Items.OfType<MenuItem>().ElementAt(2));
        await Until(() => w.LeftPanel.Panel.Path == _a);
        Assert.Equal([_c, _b], w.LeftPanel.Panel.ForwardHistory.Select(s => s.Path));
        menu.Close();

        w.BackForwardBar.Click(Chip(w, StripItemKind.NavList, MainWindow.NavForward));
        Assert.Equal(["FORWARD IN THIS PANEL", _b, _c, "Clear the steps"], Rows(w.StepsMenu!).Where(r => r.Length > 0));
        Click(w.StepsMenu!.Items.OfType<MenuItem>().Last());
        Assert.False(w.LeftPanel.Panel.CanGoBack || w.LeftPanel.Panel.CanGoForward);
        Assert.True(Chip(w, StripItemKind.Nav, MainWindow.NavBack).Dim);
        w.StepsMenu?.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task BothPanels_ListsTheOtherPanelsSteps_Marked_AndOneOfThemOpensHere()
    {
        var w = await Open(b => b with { Listed = StepsListed.BothPanels });
        w.BackForwardBar.Click(Chip(w, StripItemKind.NavList, MainWindow.NavBack));
        var rows = Rows(w.StepsMenu!);
        Assert.Equal("BACK IN BOTH PANELS", rows[0]);
        Assert.Equal([_b + " | right", _b + " | left", _a + " | left"], rows.Skip(1).Where(r => r.StartsWith(_dir, StringComparison.Ordinal)));

        Click(w.StepsMenu!.Items.OfType<MenuItem>().ElementAt(1));
        await Until(() => w.LeftPanel.Panel.Path == _b);
        Assert.Equal(_a, w.RightPanel.Panel.Path);
        Assert.Equal(_c, w.LeftPanel.Panel.BackHistory[^1].Path);
        w.StepsMenu?.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task HoldingTheButton_OpensTheList_WhenSoChosen_AndThereIsNoArrow()
    {
        var w = await Open(b => b with { ListOpens = StepsListOpens.Hold });
        Assert.Equal([MainWindow.NavBack, MainWindow.NavForward, MainWindow.NavUp], w.BackForwardBar.Items.Select(i => i.Target));
        Assert.True(w.BackForwardBar.HoldForList);
        Assert.Contains("Hold for the list", Chip(w, StripItemKind.Nav, MainWindow.NavBack).Tip, StringComparison.Ordinal);

        var at = w.BackForwardBar.TranslatePoint(w.BackForwardBar.ChipBounds(Chip(w, StripItemKind.Nav, MainWindow.NavBack))!.Value.Center, w)!.Value;
        w.MouseDown(at, Avalonia.Input.MouseButton.Left);
        Assert.Equal(_c, w.LeftPanel.Panel.Path);
        w.MouseUp(at, Avalonia.Input.MouseButton.Left);
        await Until(() => w.LeftPanel.Panel.Path == _b);

        w.BackForwardBar.Hold(Chip(w, StripItemKind.Nav, MainWindow.NavBack));
        Assert.NotNull(w.StepsMenu);
        Assert.Equal(_b, w.LeftPanel.Panel.Path);
        w.StepsMenu!.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task InTheDirectoryLines_EachPanelHasItsOwn_AndTheBarCanGo()
    {
        var w = await Open(b => b with { Place = BackForwardPlace.DirectoryLines });
        Assert.False(w.BackForwardBar.IsVisible);
        var right = w.RightPanel.DirectoryLine;
        Assert.True(right.ShowBackForward && right.CanGoBack && !right.CanGoForward);
        Assert.True(right.BackButtonBounds.Width > 0 && right.BackButtonBounds.Right < right.DriveButtonBounds.X);

        right.Click(right.BackButtonBounds.Center);
        await Until(() => w.RightPanel.Panel.Path == _b);
        Assert.Same(w.RightPanel, w.ActivePanel);
        Assert.Equal(_c, w.LeftPanel.Panel.Path);

        Dispatcher.UIThread.RunJobs();
        Assert.True(right.CanGoForward);
        right.Click(right.ForwardListBounds.Center);
        Assert.Equal("FORWARD IN THIS PANEL", Rows(w.StepsMenu!)[0]);
        w.StepsMenu!.Close();

        Change(w, b => b with { ListOpens = StepsListOpens.Hold });
        Assert.False(right.StepsListButtons);
        right.Held(forward: false);
        Assert.Equal("BACK IN THIS PANEL", Rows(w.StepsMenu!)[0]);
        w.StepsMenu!.Close();

        Change(w, b => b with { Place = BackForwardPlace.Both });
        Assert.True(w.BackForwardBar.IsVisible && right.ShowBackForward);
        Assert.True(w.Run("View.ToggleBackForwardBar", PanelTarget.Active));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(BackForwardPlace.DirectoryLines, w.Services.Settings.Current.Layout.BackForward.Place);
        Assert.False(w.BackForwardBar.IsVisible);
        Change(w, b => b with { Place = BackForwardPlace.Off });
        Assert.False(right.ShowBackForward);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheMousesSideButtons_StepInThePanelUnderTheMouse_UnlessSwitchedOff()
    {
        var w = await Open();
        var view = w.RightPanel.View;
        var over = view.TranslatePoint(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2), w)!.Value;
        w.MouseDown(over, Avalonia.Input.MouseButton.XButton1);
        w.MouseUp(over, Avalonia.Input.MouseButton.XButton1);
        await Until(() => w.RightPanel.Panel.Path == _b);
        Assert.Same(w.RightPanel, w.ActivePanel);
        w.MouseDown(over, Avalonia.Input.MouseButton.XButton2);
        w.MouseUp(over, Avalonia.Input.MouseButton.XButton2);
        await Until(() => w.RightPanel.Panel.Path == _a);

        Change(w, b => b with { MouseButtons = false });
        w.SideButton(w.LeftPanel, forward: false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(_c, w.LeftPanel.Panel.Path);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Pictures()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var w = await Open();
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "backforward-bar.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Change(w, b => b with { Place = BackForwardPlace.DirectoryLines });
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "backforward-lines.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Change(w, b => b with { Place = BackForwardPlace.Bar, ListOpens = StepsListOpens.Hold });
        w.Services.Settings.Update(s => s with { Layout = s.Layout with { Blocks = s.Layout.Blocks.Move(BarBlock.BackForward, new BlockMove(BlockPlace.WithOther, BarBlock.HotPaths, Before: true)) } });
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "backforward-shared.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheBarsMenu_MovesItLikeTheOtherBars_AndStepsKeptReachesEveryPanel()
    {
        var w = await Open();
        var menu = w.ShowBackForwardBarMenu();
        Assert.Equal(["Where the bar is", "Lock the bars", "Hide the back and forward bar", "Back and forward options…"], Rows(menu));
        var where = menu.Items.OfType<MenuItem>().First();
        Click(where.Items.OfType<MenuItem>().First(m => Label(m.Header) == "With the hot paths"));
        menu.Close();
        Assert.Contains(w.BarRowStrips, r => r.SequenceEqual([w.BackForwardBar, w.HotPathBar]));

        menu = w.ShowBackForwardBarMenu();
        Click(menu.Items.OfType<MenuItem>().ElementAt(2));
        menu.Close();
        Assert.False(w.BackForwardBar.IsVisible);
        Assert.Equal(BackForwardPlace.Off, w.Services.Settings.Current.Layout.BackForward.Place);
        Assert.True(w.Run("View.ToggleBackForwardBar", PanelTarget.Active));
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.BackForwardBar.IsVisible);

        Change(w, b => b with { StepsKept = 10 });
        Assert.Equal(10, w.LeftPanel.Panel.HistoryLimit);
        Assert.Equal(10, w.RightPanel.Panel.HistoryLimit);
        w.Run("Tab.New", PanelTarget.Active);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(10, w.LeftPanel.Panel.HistoryLimit);
        w.CloseNow();
    }
}
