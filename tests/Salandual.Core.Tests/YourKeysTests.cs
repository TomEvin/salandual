// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/YourKeysTests.cs

using Salandual.Core.Commands;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class YourKeysTests
{
    private static KeyChord K(string text) => KeyChord.Parse(text);

    [Fact]
    public void YourKeys_AreOnTopOfTheMap()
    {
        var registry = CommandRegistry.CreateDefault();
        registry.UseKeyMap(KeyMapName.Salamander, [new CommandKeys("File.Copy", [K("F5"), K("Ctrl+Alt+C")])]);
        Assert.Equal("File.Copy", registry.Resolve(K("Ctrl+Alt+C")));
        Assert.Equal([K("F5"), K("Ctrl+Alt+C")], registry.ChordsFor("File.Copy"));
        Assert.Equal([K("F5")], registry.MapChordsFor("File.Copy"));
        Assert.True(registry.IsYours("File.Copy"));
        Assert.False(registry.IsYours("File.Move"));
    }

    [Fact]
    public void AKeyReassigned_IsTakenFromTheOtherCommand()
    {
        var registry = CommandRegistry.CreateDefault();
        var (keys, from) = YourKeys.Assign(registry, [], "Compare.Directories", K("Alt+F7"));
        Assert.Equal("Find.Files", from);
        registry.UseKeyMap(KeyMapName.Salamander, keys);
        Assert.Equal("Compare.Directories", registry.Resolve(K("Alt+F7")));
        Assert.Equal([K("Ctrl+F10"), K("Alt+F7")], registry.ChordsFor("Compare.Directories"));
        Assert.Equal([K("Ctrl+F")], registry.ChordsFor("Find.Files"));
        Assert.Equal([(K("Alt+F7"), "Compare.Directories")], registry.TakenFrom("Find.Files"));

        keys = YourKeys.Reset(YourKeys.Reset(keys, "Compare.Directories"), "Find.Files");
        Assert.Empty(keys);
    }

    [Fact]
    public void AKeyRemoved_AndPutBack_LeavesNothingOfYours()
    {
        var registry = CommandRegistry.CreateDefault();
        var keys = YourKeys.Remove(registry, [], "Find.Files", K("Ctrl+F"));
        registry.UseKeyMap(KeyMapName.Salamander, keys);
        Assert.Null(registry.Resolve(K("Ctrl+F")));
        Assert.Equal([K("Alt+F7")], registry.ChordsFor("Find.Files"));

        var (back, from) = YourKeys.Assign(registry, keys, "Find.Files", K("Ctrl+F"));
        Assert.Null(from);
        Assert.Empty(back);
    }

    [Fact]
    public void YourKeys_StayWhenTheMapChanges_AndGoFirst()
    {
        var registry = CommandRegistry.CreateDefault();
        var changed = 0;
        registry.KeyMapChanged += (_, _) => changed++;
        List<CommandKeys> yours = [new CommandKeys("Find.Files", [K("Alt+F7"), K("Ctrl+Shift+F")])];
        registry.UseKeyMap(KeyMapName.Salamander, yours);
        Assert.Equal(1, changed);
        registry.UseKeyMap(KeyMapName.Salamander, yours);
        Assert.Equal(1, changed);

        registry.UseKeyMap(KeyMapName.TotalCommander);
        Assert.Equal(2, changed);
        Assert.Equal("Find.Files", registry.Resolve(K("Ctrl+Shift+F")));
        Assert.Null(registry.NotYet(K("Ctrl+Shift+F")));
        Assert.Equal([K("Alt+F7"), K("Ctrl+Shift+F")], registry.ChordsFor("Find.Files"));
        Assert.Equal(yours, registry.Yours);
    }

    [Fact]
    public void AKeyIsAskedAboutWhoHasIt()
    {
        var registry = CommandRegistry.CreateDefault();
        Assert.Equal("Find.Files", YourKeys.Holder(registry, "Compare.Directories", K("Alt+F7")).Command);
        Assert.True(YourKeys.Holder(registry, "Find.Files", K("Alt+F7")).IsFree);
        Assert.True(YourKeys.Holder(registry, "File.Copy", K("Ctrl+Shift+F7")).IsFree);
        registry.UseKeyMap(KeyMapName.TotalCommander);
        Assert.NotNull(YourKeys.Holder(registry, "File.Copy", K("Ctrl+M")).NotYet);
    }

    [Theory]
    [InlineData("File.View", "V", KeyRefusal.Types)]
    [InlineData("File.View", "7", KeyRefusal.Types)]
    [InlineData("File.View", "Space", KeyRefusal.Selects)]
    [InlineData("File.View", "Down", KeyRefusal.Moves)]
    [InlineData("File.View", "PageUp", KeyRefusal.Moves)]
    [InlineData("File.View", "Enter", KeyRefusal.Opens)]
    [InlineData("File.View", "Escape", KeyRefusal.Closes)]
    [InlineData("File.View", "Tab", KeyRefusal.Switches)]
    [InlineData("File.View", "Backspace", KeyRefusal.Deletes)]
    [InlineData("File.View", "Alt+F", KeyRefusal.OpensMenu)]
    [InlineData("File.View", "Ctrl+V", KeyRefusal.None)]
    [InlineData("File.View", "Shift+V", KeyRefusal.None)]
    [InlineData("File.View", "Alt+Up", KeyRefusal.None)]
    [InlineData("File.View", "F11", KeyRefusal.None)]
    [InlineData("Focus.Previous", "Up", KeyRefusal.None)]
    [InlineData("Navigation.Parent", "Backspace", KeyRefusal.None)]
    public void SomeKeys_CantBeGiven(string command, string key, KeyRefusal why)
        => Assert.Equal(why, YourKeys.WhyNot(CommandRegistry.CreateDefault(), command, K(key)));

    [Fact]
    public void TheMenuLetters_AreNamed()
    {
        Assert.Contains("F", YourKeys.MenuLetters);
        Assert.Equal("Files", YourKeys.MenuOf("f"));
    }

    [Fact]
    public void TheSettings_KeepEachCommandOnce_WithKeysThatParse()
    {
        var keyboard = new AppSettings
        {
            Keyboard = new KeyboardSettings
            {
                Keys =
                [
                    new CommandKeys { Command = "File.Copy", Keys = ["F5"] },
                    new CommandKeys { Command = " ", Keys = ["F6"] },
                    new CommandKeys { Command = "File.Copy", Keys = ["ctrl+alt+c", "nonsense", "Ctrl+Alt+C"] },
                    null!,
                ],
            },
        }.Normalize().Keyboard;
        var only = Assert.Single(keyboard.Keys);
        Assert.Equal("File.Copy", only.Command);
        Assert.Equal(["Ctrl+Alt+C"], only.Keys);
    }

    [Fact]
    public void AnUnknownCommand_IsLeftAlone()
    {
        var registry = CommandRegistry.CreateDefault();
        registry.UseKeyMap(KeyMapName.Salamander, [new CommandKeys("Gone.Command", [K("F5")])]);
        Assert.Equal("File.Copy", registry.Resolve(K("F5")));
    }
}
