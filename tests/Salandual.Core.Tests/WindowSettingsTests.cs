// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/WindowSettingsTests.cs

using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class WindowSettingsTests : IDisposable
{
    private static readonly (int, int, int, int)[] _twoScreens = [(0, 0, 1920, 1040), (1920, 0, 2560, 1400)];

    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-windows-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void APlace_IsSaved_AndReadBack()
    {
        var store = SettingsStore.InDirectory(_dir);
        var place = new WindowPlace { Width = 1500, Height = 900, Maximized = true, X = 2000, Y = 40 };
        store.Save(new AppSettings { Windows = new WindowSettings().With(WindowSettings.Main, place) });
        Assert.Equal(place, store.Load().Settings.Windows.For(WindowSettings.Main));
        Assert.Null(store.Load().Settings.Windows.For(WindowSettings.Options));
    }

    [Fact]
    public void AHandEditedPlace_IsCleanedUp()
    {
        var odd = new WindowSettings { Places = new() { ["main"] = new WindowPlace { Width = double.NaN, Height = 5, X = 10 }, [""] = new WindowPlace() } }.Normalize();
        var place = Assert.Single(odd.Places).Value;
        Assert.Equal(800, place.Width);
        Assert.Equal(150, place.Height);
        Assert.Null(place.X);
    }

    [Fact]
    public void OnScreen_WhenItsTitleBarShowsOnAScreen()
    {
        Assert.True(new WindowPlace { Width = 1280, Height = 800, X = 2100, Y = 100 }.IsOnScreen(_twoScreens, 1));
        Assert.True(new WindowPlace { Width = 1280, Height = 800, X = -1000, Y = 0 }.IsOnScreen(_twoScreens, 1));

        Assert.False(new WindowPlace { Width = 1280, Height = 800, X = 2100, Y = 100 }.IsOnScreen(_twoScreens[..1], 1));
        Assert.False(new WindowPlace { Width = 1280, Height = 800, X = 100, Y = 1030 }.IsOnScreen(_twoScreens[..1], 1));
        Assert.False(new WindowPlace { Width = 1280, Height = 800 }.IsOnScreen(_twoScreens, 1));
    }

    [Fact]
    public void WindowSizes_AreNeverExportedOrReset()
    {
        var mine = new AppSettings
        {
            Windows = new WindowSettings().With(WindowSettings.Options, new WindowPlace { Width = 1111, Height = 777 }),
            Viewers = new ViewerSettings { WindowWidth = 1333 },
            Editors = new EditorSettings { WindowWidth = 1444 },
        };
        var text = SettingsTransfer.Export(mine, new ExportChoice(History: true), DateTime.Now, "Salandual 0.1");
        Assert.DoesNotContain("1111", text);
        Assert.DoesNotContain("1333", text);
        Assert.DoesNotContain("1444", text);

        var kept = SettingsTransfer.ResetAll(mine, new ResetChoice(KeepThemes: false, KeepHotPaths: false, KeepHistory: false));
        Assert.Equal(1111, kept.Windows.For(WindowSettings.Options)!.Width);
        Assert.Equal(1333, kept.Viewers.WindowWidth);
        Assert.Equal(1444, kept.Editors.WindowWidth);
    }
}
