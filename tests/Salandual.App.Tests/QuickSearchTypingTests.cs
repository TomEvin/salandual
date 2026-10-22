// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/QuickSearchTypingTests.cs

using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Salandual.App.Tests;

public sealed class QuickSearchTypingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-qs-").FullName;

    public QuickSearchTypingTests()
    {
        foreach (var name in new[] { "alpha.txt", "albatros.txt", "beta.txt", "1984.txt" })
        {
            File.WriteAllText(Path.Join(_dir, name), "x");
        }
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    internal static void Type(MainWindow w, string text)
    {
        foreach (var c in text)
        {
            var key = char.IsAsciiDigit(c) ? Key.D0 + (c - '0') : Key.A + (char.ToUpperInvariant(c) - 'A');
            w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, c.ToString());
            w.KeyTextInput(c.ToString());
            w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, c.ToString());
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task TypingSeveralLetters_KeepsExtendingTheSearch()
    {
        var w = new MainWindow { Width = 1000, Height = 600 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();

        Type(w, "alb");
        Assert.Equal("alb", w.LeftPanel.Panel.QuickSearchText);
        Assert.Equal("albatros.txt", w.LeftPanel.Panel.FocusedEntry?.Name);

        Type(w, "1");
        Assert.Equal("alb", w.LeftPanel.Panel.QuickSearchText);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TypingDigits_SearchesNamesStartingWithThem()
    {
        var w = new MainWindow { Width = 1000, Height = 600 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();

        Type(w, "198");
        Assert.Equal("198", w.LeftPanel.Panel.QuickSearchText);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ShiftLetter_InsideQuickSearch_IsAnUpperCaseLetter_NotADriveSwitch()
    {
        var w = new MainWindow { Width = 1000, Height = 600 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();

        Type(w, "a");
        w.KeyPress(Key.L, RawInputModifiers.Shift, PhysicalKey.None, "L");
        w.KeyTextInput("L");
        w.KeyRelease(Key.L, RawInputModifiers.Shift, PhysicalKey.None, "L");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("aL", w.LeftPanel.Panel.QuickSearchText);
        Assert.Equal(_dir, w.LeftPanel.Panel.Path);
        Assert.Null(w.NoticeMessage);
        w.CloseNow();
    }
}
