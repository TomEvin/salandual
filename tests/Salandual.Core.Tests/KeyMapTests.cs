// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/KeyMapTests.cs

using Salandual.Core.Commands;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class KeyMapTests
{
    private static string? Resolve(CommandRegistry r, string chord) => r.Resolve(KeyChord.Parse(chord));

    private static CommandRegistry TotalCommander()
    {
        var registry = CommandRegistry.CreateDefault();
        registry.UseKeyMap(KeyMapName.TotalCommander);
        return registry;
    }

    [Fact]
    public void TotalCommander_GivesItsKeysToSalandualsCommands()
    {
        var r = TotalCommander();
        Assert.Equal(KeyMapName.TotalCommander, r.KeyMap);
        Assert.Equal("Panel.Refresh", Resolve(r, "F2"));
        Assert.Equal("File.QuickRename", Resolve(r, "Shift+F6"));
        Assert.Equal("App.Menu", Resolve(r, "F9"));
        Assert.Equal("Compare.Directories", Resolve(r, "Shift+F2"));
        Assert.Equal("HotPaths.Menu", Resolve(r, "Ctrl+D"));
        Assert.Equal("File.OccupiedSpace", Resolve(r, "Ctrl+L"));
        Assert.Equal("Selection.Restore", Resolve(r, "Num/"));
        Assert.Equal("Selection.SameExtension", Resolve(r, "Alt+Num+"));
        Assert.Equal("Lists.WorkingDirectories", Resolve(r, "Alt+Down"));
        Assert.Equal(["Shift+F6"], r.ChordsFor("File.QuickRename").Select(c => c.ToString()));
        Assert.Equal(["F2", "Ctrl+R"], r.ChordsFor("Panel.Refresh").Select(c => c.ToString()));
    }

    [Fact]
    public void TotalCommander_CommandsItHasNoKeyFor_KeepSalamandersKeys_WhereFree()
    {
        var r = TotalCommander();
        Assert.Equal("Compare.Files", Resolve(r, "Ctrl+Shift+C"));
        Assert.Equal("App.Options", Resolve(r, "Ctrl+Shift+O"));
        Assert.Equal("Panel.ToggleHidden", Resolve(r, "Ctrl+H"));
        Assert.Equal("Drive.SwitchC", Resolve(r, "Shift+C"));
        Assert.Equal("Focus.Next", Resolve(r, "Down"));

        Assert.DoesNotContain(KeyChord.Parse("Ctrl+F10"), r.ChordsFor("Compare.Directories"));
        Assert.Null(Resolve(r, "Alt+Shift+D"));
    }

    [Fact]
    public void TotalCommander_KeysForWhatIsntBuilt_DoNothing_AndAreNamed()
    {
        var r = TotalCommander();
        foreach (var key in new[] { "Ctrl+T", "Ctrl+Q", "Ctrl+M", "Ctrl+W", "Ctrl+Up", "Ctrl+F10", "Alt+F10" })
        {
            Assert.Null(Resolve(r, key));
            Assert.NotNull(r.NotYet(KeyChord.Parse(key)));
        }

        Assert.Equal("New tab", r.NotYet(KeyChord.Parse("Ctrl+T"))!.Title);
        Assert.All(r.NotYetKeys, k => Assert.DoesNotContain("KeyMap_", k.Title, StringComparison.Ordinal));
    }

    [Fact]
    public void TotalCommander_EveryCommandItNames_IsSalanduals()
    {
        var registry = CommandRegistry.CreateDefault();
        foreach (var (keys, target) in KeyMaps.TotalCommander)
        {
            Assert.True(target.StartsWith('~') || registry.TryGet(target, out _), target);
            Assert.All(keys.Split(' '), k => Assert.True(KeyChord.TryParse(k, out _), k));
        }

        var chords = KeyMaps.TotalCommander.SelectMany(e => e.Keys.Split(' ')).Select(KeyChord.Parse).ToList();
        Assert.Equal(chords.Count, chords.Distinct().Count());
    }

    [Fact]
    public void BackToSalamander_RestoresTheDefaults_AndSaysWhatChanges()
    {
        var r = TotalCommander();
        var changed = 0;
        r.KeyMapChanged += (_, _) => changed++;
        var back = r.Differences(KeyMapName.TotalCommander, KeyMapName.Salamander);
        Assert.Contains(back, d => d.Chord == KeyChord.Parse("F2") && d.Before == "Panel.Refresh" && d.After == "File.QuickRename");
        Assert.Contains(back, d => d.Chord == KeyChord.Parse("Ctrl+T") && d.Before == "" && d.After == "Navigation.ShortcutTarget");
        Assert.DoesNotContain(back, d => d.Chord == KeyChord.Parse("F5"));
        Assert.Equal("F2", back[0].Chord.ToString());

        r.UseKeyMap(KeyMapName.Salamander);
        Assert.Equal(1, changed);
        Assert.Empty(r.NotYetKeys);
        Assert.Equal("File.QuickRename", Resolve(r, "F2"));
        Assert.Equal("Navigation.ShortcutTarget", Resolve(r, "Ctrl+T"));
        Assert.Equal(back.Count, r.Differences(KeyMapName.Salamander, KeyMapName.TotalCommander).Count);
        foreach (var command in r.Commands)
        {
            Assert.Equal(command.DefaultChords, r.ChordsFor(command.Id));
        }
    }

    [Fact]
    public void Settings_KeepTheKeyMapAndQuickSearch_AndMendUnknownValues()
    {
        var s = new AppSettings
        {
            Keyboard = new KeyboardSettings { KeyMap = (KeyMapName)7 },
            Panels = new PanelSettings { QuickSearch = (QuickSearchStart)9 },
        }.Normalize();
        Assert.Equal(KeyMapName.Salamander, s.Keyboard.KeyMap);
        Assert.Equal(QuickSearchStart.Letters, s.Panels.QuickSearch);
        Assert.Equal(KeyMapName.Salamander, (new AppSettings { Keyboard = null! }).Normalize().Keyboard.KeyMap);
        Assert.Equal(SettingsPart.Keyboard, SettingsTransfer.PartOf("keyboard.keyMap"));
        Assert.Equal("Total Commander", KeyMaps.Title(KeyMapName.TotalCommander));
        Assert.NotNull(Strings.KeyMap_Salamander);
    }
}
