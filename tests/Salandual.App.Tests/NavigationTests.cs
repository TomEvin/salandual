// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/NavigationTests.cs

using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Salandual.App.Tests;

public sealed class NavigationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-nav-").FullName;
    private readonly string _a;
    private readonly string _b;

    public NavigationTests()
    {
        _a = Directory.CreateDirectory(Path.Join(_dir, "Alpha")).FullName;
        _b = Directory.CreateDirectory(Path.Join(_dir, "Beta")).FullName;
        Directory.CreateDirectory(Path.Join(_a, "Inner"));
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

    [AvaloniaFact]
    public async Task CtrlU_SwapsThePanels_WithTheirSelection()
    {
        var w = await Open();
        var left = w.LeftPanel.Panel;
        left.SetFocus(left.Items.ToList().FindIndex(e => e.Name == "a.txt"));
        left.Toggle(left.Focus);

        Press(w, Key.U, RawInputModifiers.Control);
        Assert.Equal(_b, w.LeftPanel.Panel.Path);
        Assert.Equal(_a, w.RightPanel.Panel.Path);
        Assert.True(w.RightPanel.Panel.IsSelected(w.RightPanel.Panel.Focus));
        Assert.Same(w.LeftPanel, w.ActivePanel);
        Assert.Equal(_b, w.LeftPanel.DirectoryLine.Path);

        Press(w, Key.U, RawInputModifiers.Control);
        Assert.Equal(_a, w.LeftPanel.Panel.Path);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlF11_MaximizesTheActivePanel_TabBringsBothBack()
    {
        var w = await Open();
        Press(w, Key.F11, RawInputModifiers.Control);
        Assert.True(w.IsPanelMaximized);
        Assert.False(w.RightPanel.IsVisible);

        Press(w, Key.F11, RawInputModifiers.Control);
        Assert.False(w.IsPanelMaximized);
        Assert.True(w.RightPanel.IsVisible);

        Press(w, Key.F11, RawInputModifiers.Control);
        Press(w, Key.Tab);
        Assert.False(w.IsPanelMaximized);
        Assert.True(w.RightPanel.IsVisible);
        Assert.Same(w.RightPanel, w.ActivePanel);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AltLeft_GoesBack_AltRight_Forward()
    {
        var w = await Open();
        var panel = w.LeftPanel.Panel;
        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == "Inner"));
        Press(w, Key.Enter);
        await Until(() => panel.Path.EndsWith("Inner", StringComparison.Ordinal));

        Press(w, Key.Left, RawInputModifiers.Alt);
        await Until(() => panel.Path == _a);
        Assert.Equal("Inner", panel.FocusedEntry?.Name);

        Press(w, Key.Right, RawInputModifiers.Alt);
        await Until(() => panel.Path.EndsWith("Inner", StringComparison.Ordinal));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlDot_TakesTheOtherPanelsFolder_CtrlShiftRight_OpensInTheOther()
    {
        var w = await Open();
        Press(w, Key.OemPeriod, RawInputModifiers.Control);
        await Until(() => w.LeftPanel.Panel.Path == _b);

        await w.LeftPanel.Panel.NavigateAsync(_a);
        var panel = w.LeftPanel.Panel;
        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == "Inner"));
        Press(w, Key.Right, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Until(() => w.RightPanel.Panel.Path == Path.Join(_a, "Inner"));
        Assert.Same(w.LeftPanel, w.ActivePanel);

        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == "a.txt"));
        Press(w, Key.Right, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Until(() => w.RightPanel.Panel.Path == _a && w.RightPanel.Panel.FocusedEntry?.Name == "a.txt");
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Drive_GoesToItsLastFolder_OrTheRootOnTheSameDrive()
    {
        var w = await Open();
        w.LastFolderPerDrive[@"Q:\"] = _b;
        w.GoToDrive(w.LeftPanel, @"Q:\");
        await Until(() => w.LeftPanel.Panel.Path == _b);

        var root = Path.GetPathRoot(_b)!;
        w.GoToDrive(w.LeftPanel, root);
        await Until(() => string.Equals(w.LeftPanel.Panel.Path.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(_b, w.LastFolderPerDrive[@"Q:\"]);
        w.CloseNow();
    }
}
