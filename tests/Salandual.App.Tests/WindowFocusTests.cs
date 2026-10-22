// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/WindowFocusTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Salandual.App.Tests;

public sealed class WindowFocusTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-focus-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open()
    {
        File.WriteAllText(Path.Join(_dir, "a.txt"), "x");
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir);
        await w.RightPanel.Panel.NavigateAsync(_dir);
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

    [AvaloniaFact]
    public void WindowButtons_TakeNoFocus()
    {
        var rule = Application.Current!.Styles.OfType<Style>()
            .Single(s => s.Selector?.ToString() == "WindowDrawnDecorations:not(:fullscreen) /template/ Button");

        Assert.Contains(rule.Setters.OfType<Setter>(), s => s.Property == InputElement.FocusableProperty && Equals(s.Value, false));
    }

    [AvaloniaFact]
    public async Task ThePathBeingEdited_GetsTheFocusBack_WhenTheWindowComesBack()
    {
        var w = await Open();
        Press(w, Key.F7, RawInputModifiers.Shift);
        var input = w.LeftPanel.ChangeDirectory.Input;
        Assert.True(input.IsFocused);

        w.Focusable = true;
        w.Focus();
        Assert.False(input.IsFocused);
        w.RestoreFocus();

        Assert.True(input.IsFocused);
        Assert.Same(input, w.LastFocus);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheActivePanel_GetsTheFocus_WhenTheLastElementIsGone()
    {
        var w = await Open();
        w.RightPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        Press(w, Key.F7, RawInputModifiers.Shift);
        Assert.True(w.RightPanel.ChangeDirectory.Input.IsFocused);

        w.RightPanel.ChangeDirectory.Input.IsVisible = false;
        w.Focusable = true;
        w.Focus();
        w.RestoreFocus();

        Assert.True(w.RightPanel.View.IsFocused);
        w.CloseNow();
    }
}
