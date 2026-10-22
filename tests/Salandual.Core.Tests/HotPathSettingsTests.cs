// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/HotPathSettingsTests.cs

using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class HotPathSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-hotpaths-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static HotPathSettings Sample() => new HotPathSettings()
        .With(new HotPath(1, "Downloads", @"%USERPROFILE%\Downloads"))
        .With(new HotPath(2, "Photos", @"D:\Backup\Photos"))
        .With(new HotPath(11, "NAS", @"\\nas\photos", InStrip: false));

    [Theory]
    [InlineData(1, "Ctrl+1")]
    [InlineData(9, "Ctrl+9")]
    [InlineData(10, "Ctrl+0")]
    [InlineData(11, null)]
    public void KeyOf(int slot, string? key) => Assert.Equal(key, HotPathSettings.KeyOf(slot));

    [Fact]
    public void DigitZero_IsSlotTen() => Assert.Equal(10, HotPathSettings.SlotOfDigit(0));

    [Fact]
    public void NextFree_IsTheFirstEmptySlot_FromOne()
    {
        var s = Sample();
        Assert.Equal(3, s.NextFreeSlot());
        Assert.Equal(1, new HotPathSettings().NextFreeSlot());
        for (var slot = 3; slot <= 30; slot++)
        {
            s = s.With(new HotPath(slot, "x", @"C:\"));
        }

        Assert.Null(s.NextFreeSlot());
    }

    [Fact]
    public void With_Replaces_Without_Clears_SwapMovesBothWays()
    {
        var s = Sample().With(new HotPath(2, "Fotók", @"D:\Fotók"));
        Assert.Equal("Fotók", s[2]!.Name);
        Assert.Equal([1, 2, 11], s.Paths.Select(p => p.Slot));

        s = s.Swap(1, 2);
        Assert.Equal("Fotók", s[1]!.Name);
        Assert.Equal("Downloads", s[2]!.Name);

        s = s.Swap(2, 3);
        Assert.Null(s[2]);
        Assert.Equal("Downloads", s[3]!.Name);

        s = s.Without(3);
        Assert.Null(s[3]);
        Assert.Equal([1, 11], s.Paths.Select(p => p.Slot));
    }

    [Fact]
    public void Normalize_DropsBadEntries_AndNamesUnnamedOnes()
    {
        var s = new AppSettings
        {
            HotPaths = new HotPathSettings
            {
                Paths =
                [
                    new HotPath(3, "", @"C:\Users\user\Pictures\Nyaralás 2026"),
                    new HotPath(3, "duplicate", @"C:\X"),
                    new HotPath(0, "zero", @"C:\X"),
                    new HotPath(31, "too far", @"C:\X"),
                    new HotPath(5, "no path", "  "),
                    new HotPath(1, " Root ", @"D:\"),
                ],
            },
        }.Normalize().HotPaths;

        Assert.Equal([1, 3], s.Paths.Select(p => p.Slot));
        Assert.Equal("Root", s[1]!.Name);
        Assert.Equal("Nyaralás 2026", s[3]!.Name);
    }

    [Theory]
    [InlineData(@"C:\Users\user\Pictures\Nyaralás 2026", "Nyaralás 2026")]
    [InlineData(@"D:\", "D:")]
    [InlineData(@"\\nas\photos\", "photos")]
    public void DefaultName(string path, string name) => Assert.Equal(name, HotPathSettings.DefaultName(path));

    [Fact]
    public void Expand_EnvironmentVariables_AndSalamanderSyntax()
    {
        var profile = Environment.GetEnvironmentVariable("USERPROFILE")!;
        Assert.Equal(profile + @"\source", HotPathSettings.Expand(@"%USERPROFILE%\source"));
        Assert.Equal(profile + @"\source", HotPathSettings.Expand(@"$[USERPROFILE]\source"));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\Temp", HotPathSettings.Expand(@"$(WinDir)\Temp"));
        Assert.Equal(@"C:\a$b", HotPathSettings.Expand(@"C:\a$$b"));
        Assert.Equal(@"C:\$(Nope)", HotPathSettings.Expand(@"C:\$(Nope)"));
    }

    [Fact]
    public void DriveMenu_Letters_AndPlacesInOrder()
    {
        var d = new DriveMenuSettings();
        Assert.True(d.IsDriveShown('c'));
        var hidden = DriveMenuSettings.Toggle("", 'b', on: true);
        hidden = DriveMenuSettings.Toggle(hidden, 'A', on: true);
        Assert.Equal("AB", hidden);
        Assert.Equal("A", DriveMenuSettings.Toggle(hidden, 'B', on: false));

        var normalized = new AppSettings
        {
            DriveMenu = new DriveMenuSettings { HiddenDrives = "zz1a", Places = [new PlaceSetting("Music", true, true), new PlaceSetting("Unknown", true, true)] },
        }.Normalize().DriveMenu;
        Assert.Equal("AZ", normalized.HiddenDrives);
        Assert.Equal(DriveMenuSettings.DefaultPlaces.Select(p => p.Id), normalized.Places.Select(p => p.Id));
        Assert.True(normalized.Place("Music").InStrip);
        Assert.False(normalized.Place("Videos").InMenu);
    }

    [Fact]
    public void SavedAndLoaded_ThroughTheSettingsFile()
    {
        var store = SettingsStore.InDirectory(_dir);
        var settings = new AppSettings
        {
            HotPaths = Sample() with { ShiftDigits = true },
            DriveMenu = new DriveMenuSettings { HiddenDrives = "A", SeparatorsAfter = "D", FreeSpace = false },
        }.Normalize();
        store.Save(settings);

        var back = store.Load().Settings;
        Assert.True(back.HotPaths.ShiftDigits);
        Assert.Equal(settings.HotPaths.Paths, back.HotPaths.Paths);
        Assert.Equal("A", back.DriveMenu.HiddenDrives);
        Assert.Equal("D", back.DriveMenu.SeparatorsAfter);
        Assert.False(back.DriveMenu.FreeSpace);
        Assert.Equal(settings.DriveMenu.Places, back.DriveMenu.Places);
    }
}
