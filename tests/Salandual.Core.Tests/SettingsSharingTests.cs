// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/SettingsSharingTests.cs

using System.Text.Json.Nodes;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class SettingsSharingTests
{
    private static readonly DateTime _t0 = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlySet<SettingsPart> _parts = new SharingSettings().Parts.ToHashSet();

    private static AppSettings Font(AppSettings s, double size) => s with { Appearance = s.Appearance with { PanelFont = s.Appearance.PanelFont with { Size = size } } };

    private static SharedCopy Through(AppSettings settings, JsonObject? lastRead, SharedCopy? file, DateTime at, string pc)
        => SettingsSharing.Read(SettingsSharing.Write(SettingsSharing.FileAfter(settings, lastRead, file, _parts), pc, at, "Salandual 0.1"));

    [Fact]
    public void AChangeOnOnePc_IsTakenOnTheOther_PerSetting()
    {
        var desk = new AppSettings { HotPaths = new HotPathSettings { Paths = [new HotPath(1, "Photos", @"E:\Photos")] } };
        var file = Through(desk, null, null, _t0, "DESK");
        Assert.Equal("DESK", file.Pc);
        Assert.Equal(_t0, file.WrittenUtc);

        var laptop = new AppSettings { Panels = new PanelSettings { SizeUnits = SizeUnits.Binary } };
        var first = SettingsSharing.Merge(laptop, null, file, _parts, null);
        Assert.Contains("hotPaths.paths", first.Paths);
        Assert.Contains("panels.sizeUnits", first.Paths);
        laptop = first.Local;
        var laptopRead = file.Settings;

        laptop = Font(laptop, 14);
        file = Through(laptop, laptopRead, file, _t0.AddMinutes(1), "LAPTOP");
        laptopRead = file.Settings;
        var deskRead = SettingsSharing.Read(SettingsSharing.Write(SettingsSharing.Only(desk, _parts), "DESK", _t0, "x")).Settings;
        desk = desk with { HotPaths = desk.HotPaths.With(new HotPath(2, "Music", @"E:\Music")) };
        var atDesk = SettingsSharing.Merge(desk, deskRead, file, _parts, _t0.AddMinutes(2));
        Assert.Equal(["appearance.panelFont.size"], atDesk.Paths);
        Assert.Equal(14, atDesk.Local.Appearance.PanelFont.Size);
        Assert.Equal(2, atDesk.Local.HotPaths.Paths.Count);
        Assert.Contains(atDesk.Changes, c => c.Part == SettingsPart.Appearance && c.New == "14");
        Assert.Equal(0, atDesk.BothChanged);

        desk = atDesk.Local;
        file = Through(desk, file.Settings, file, _t0.AddMinutes(3), "DESK");
        var atLaptop = SettingsSharing.Merge(laptop, laptopRead, file, _parts, _t0.AddMinutes(1));
        Assert.Equal(["hotPaths.paths"], atLaptop.Paths);
    }

    [Fact]
    public void ChangedOnBoth_TheLaterChangeWins()
    {
        var start = new AppSettings();
        var read = SettingsSharing.Read(SettingsSharing.Write(SettingsSharing.Only(start, _parts), "DESK", _t0, "x"));
        var theirs = Through(Font(start, 15), read.Settings, read, _t0.AddMinutes(10), "LAPTOP");

        var older = SettingsSharing.Merge(Font(start, 14), read.Settings, theirs, _parts, _t0.AddMinutes(5));
        Assert.Equal(15, older.Local.Appearance.PanelFont.Size);
        Assert.Equal((1, 0), (older.BothChanged, older.KeptMine));

        var newer = SettingsSharing.Merge(Font(start, 14), read.Settings, theirs, _parts, _t0.AddMinutes(20));
        Assert.Equal(14, newer.Local.Appearance.PanelFont.Size);
        Assert.Equal((1, 1), (newer.BothChanged, newer.KeptMine));
        Assert.False(newer.HasChanges);
    }

    [Fact]
    public void Programs_HistoryWindowsAndThisPcsOwn_NeverGoAlong_UnlessTicked()
    {
        var mine = new AppSettings
        {
            Compare = new CompareSettings { Files = new FileCompareSettings { UseProgram = true, Program = @"C:\Tools\WinMergeU.exe" } },
            Find = new FindSettings { Texts = ["secret"] },
            Windows = new WindowSettings().With(WindowSettings.Main, new WindowPlace { Width = 1234, Height = 800 }),
            Sharing = new SharingSettings { Folder = @"C:\Users\you\OneDrive\Salandual" },
            Advanced = new AdvancedSettings(),
        };
        var text = SettingsSharing.Write(SettingsSharing.Only(mine, _parts), "DESK", _t0, "x");
        Assert.DoesNotContain("WinMergeU", text);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("1234", text);
        Assert.DoesNotContain("OneDrive", text);
        Assert.DoesNotContain("\"advanced\"", text);

        var other = SettingsSharing.Read(SettingsSharing.Write(SettingsSharing.Only(new AppSettings { Compare = new CompareSettings { Files = new FileCompareSettings { Program = @"D:\Evil.exe" } } },
            SharingSettings.Shareable.ToHashSet()), "LAPTOP", _t0, "x"));
        var merged = SettingsSharing.Merge(new AppSettings(), null, other, _parts, null);
        Assert.Equal("", merged.Local.Compare.Files.Program);
    }

    [Fact]
    public void AskedFirst_TheFileKeepsTheirChanges_WithMineAdded()
    {
        var start = new AppSettings();
        var read = SettingsSharing.Read(SettingsSharing.Write(SettingsSharing.Only(start, _parts), "DESK", _t0, "x"));
        var theirs = Through(Font(start, 15), read.Settings, read, _t0.AddMinutes(1), "LAPTOP");

        var mine = start with { Panels = start.Panels with { SizeUnits = SizeUnits.Binary } };
        var file = SettingsSharing.Read(SettingsSharing.Write(SettingsSharing.FileAfter(mine, read.Settings, theirs, _parts), "DESK", _t0.AddMinutes(2), "x"));
        Assert.Equal(15, file.Settings["appearance"]!["panelFont"]!["size"]!.GetValue<double>());
        Assert.Equal("Binary", file.Settings["panels"]!["sizeUnits"]!.GetValue<string>());

        var again = SettingsSharing.Merge(mine, read.Settings, file, _parts, _t0.AddMinutes(2));
        Assert.Equal(["appearance.panelFont.size"], again.Paths);

        var import = SettingsSharing.AsImport(mine, again, @"C:\Users\you\OneDrive\Salandual\Salandual settings.saldset", file);
        Assert.Equal([SettingsPart.Appearance], import.Parts);
        Assert.Equal(15, import.Apply(new HashSet<SettingsPart> { SettingsPart.Appearance }).Appearance.PanelFont.Size);
        Assert.Equal(SizeUnits.Binary, import.Apply(new HashSet<SettingsPart> { SettingsPart.Appearance }).Panels.SizeUnits);
    }

    [Fact]
    public void Undo_PutsBackOnlyWhatWasTaken()
    {
        var before = new AppSettings();
        var now = Font(before, 15) with { Panels = before.Panels with { SizeUnits = SizeUnits.Binary } };
        var undone = SettingsSharing.Restore(now, before, ["appearance.panelFont.size"]);
        Assert.Equal(FontSetting.DefaultPanelSize, undone.Appearance.PanelFont.Size);
        Assert.Equal(SizeUnits.Binary, undone.Panels.SizeUnits);
    }

    [Fact]
    public void NotASettingsFile_IsSaidSo()
    {
        Assert.Throws<InvalidDataException>(() => SettingsSharing.Read("{ \"hello\": 1 }"));
        Assert.Throws<InvalidDataException>(() => SettingsSharing.Read("not json"));
    }

    [Fact]
    public void TheClouds_FoundOnThePc()
    {
        var env = new Dictionary<string, string>
        {
            ["OneDrive"] = @"C:\Users\you\OneDrive",
            ["APPDATA"] = @"C:\Users\you\AppData\Roaming",
            ["LOCALAPPDATA"] = @"C:\Users\you\AppData\Local",
            ["USERPROFILE"] = @"C:\Users\you",
        };
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Users\you\OneDrive", @"D:\Dropbox", @"G:\My Drive" };
        var files = new Dictionary<string, string> { [@"C:\Users\you\AppData\Local\Dropbox\info.json"] = "{\"personal\": {\"path\": \"D:\\\\Dropbox\"}}" };

        var found = CloudFolders.Find(env.GetValueOrDefault, folders.Contains, files.GetValueOrDefault, [@"C:\", @"G:\"]);
        Assert.Equal(["OneDrive – Personal", "Dropbox", "Google Drive"], found.Select(f => f.Name));
        Assert.Equal(@"D:\Dropbox\Salandual", CloudFolders.SuggestedFolder(found[1]));
        Assert.Empty(CloudFolders.Find(_ => null, _ => false, _ => null, []));
    }

    [Fact]
    public void EveryCloud_FoundByItself_WithWorkAccountsByTheirNames()
    {
        var env = new Dictionary<string, string>
        {
            ["OneDrive"] = @"C:\Users\you\OneDrive - Example Ltd",
            ["OneDriveConsumer"] = @"C:\Users\you\OneDrive",
            ["OneDriveCommercial"] = @"C:\Users\you\OneDrive - Example Ltd",
            ["APPDATA"] = @"C:\Users\you\AppData\Roaming",
            ["LOCALAPPDATA"] = @"C:\Users\you\AppData\Local",
            ["USERPROFILE"] = @"C:\Users\you",
        };
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Users\you\OneDrive", @"C:\Users\you\OneDrive - Example Ltd", @"C:\Users\you\OneDrive - Sample School",
            @"C:\Users\you\Dropbox", @"C:\Users\you\Dropbox (Example Ltd)", @"C:\Users\you\Box", @"C:\Users\you\MEGA", @"C:\Users\you\Nextcloud",
            @"C:\Users\you\iCloudDrive",
        };
        var files = new Dictionary<string, string>
        {
            [@"C:\Users\you\AppData\Local\Dropbox\info.json"] = "{\"personal\": {\"path\": \"C:\\\\Users\\\\you\\\\Dropbox\"}, \"business\": {\"path\": \"C:\\\\Users\\\\you\\\\Dropbox (Example Ltd)\"}}",
        };
        (string, bool)[] accounts = [(@"C:\Users\you\OneDrive", false), (@"C:\Users\you\OneDrive - Example Ltd", true), (@"C:\Users\you\OneDrive - Sample School", true), (@"C:\Users\you\OneDrive - Gone", true)];
        CloudDriveFacts[] drives = [new(@"C:\", null, "System"), new(@"Y:\", @"\\Syncplicity\Syncplicity", null), new(@"P:\", null, "pCloud Drive"), new(@"Z:\", @"\\nas\media", null)];

        var found = CloudFolders.Find(env.GetValueOrDefault, folders.Contains, files.GetValueOrDefault, [@"C:\"], accounts, drives);

        Assert.Equal(
            ["OneDrive – Personal", "OneDrive – Example Ltd", "OneDrive – Sample School", "Dropbox", "Dropbox (Example Ltd)", "iCloud Drive", "Box", "MEGA", "Nextcloud", "Syncplicity", "pCloud"],
            found.Select(f => f.Name));
        Assert.Equal(@"Y:\", found.Single(f => f.Name == "Syncplicity").Path);
        Assert.True(found.Single(f => f.Name == "pCloud").IsDrive);
        Assert.False(found[0].IsDrive);
        Assert.DoesNotContain(found, f => f.Path.Contains("Gone", StringComparison.Ordinal));
    }
}
