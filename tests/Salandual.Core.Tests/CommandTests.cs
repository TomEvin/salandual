// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/CommandTests.cs

using System.Globalization;
using Salandual.Core.Commands;

namespace Salandual.Core.Tests;

public sealed class CommandTests
{
    [Theory]
    [InlineData("F5", "F5")]
    [InlineData("Shift+Ctrl+f5", "Ctrl+Shift+F5")]
    [InlineData("Num +", "Num+")]
    [InlineData("Shift+Num +", "Shift+Num+")]
    [InlineData("Ctrl+= (+)", "Ctrl+=")]
    [InlineData("Ctrl+-", "Ctrl+-")]
    [InlineData("Page Down", "PageDown")]
    [InlineData("Ctrl+\\", "Ctrl+\\")]
    [InlineData("Alt+Shift+F10", "Alt+Shift+F10")]
    [InlineData("ctrl+a", "Ctrl+A")]
    public void KeyChord_ParsesSalamanderNotation(string text, string canonical)
        => Assert.Equal(canonical, KeyChord.Parse(text).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+NoSuchKey")]
    [InlineData("F25")]
    public void KeyChord_RejectsInvalid(string text) => Assert.False(KeyChord.TryParse(text, out _));

    [Fact]
    public void DefaultRegistry_HasNoConflicts_AndResolvesCopy()
    {
        var registry = CommandRegistry.CreateDefault();

        Assert.Equal("File.Copy", registry.Resolve(KeyChord.Parse("F5")));
        Assert.Equal("Selection.SameExtension", registry.Resolve(KeyChord.Parse("Shift+Num+")));
        Assert.Equal("HotPaths.Go1", registry.Resolve(KeyChord.Parse("Shift+1")));
        Assert.Null(registry.Resolve(KeyChord.Parse("Ctrl+Alt+F12")));
    }

    [Fact]
    public void Registering_TheSameChordTwice_Fails()
    {
        var registry = new CommandRegistry();
        registry.Register(new CommandDefinition("A", "A", "Test", [KeyChord.Parse("F5")], "M1"));

        Assert.Throws<InvalidOperationException>(() => registry.Register(new CommandDefinition("B", "B", "Test", [KeyChord.Parse("F5")], "M1")));
    }

    [Fact]
    public void DefaultKeyMap_CoversTheSalamanderPanelReference()
    {
        var registry = CommandRegistry.CreateDefault();
        var missing = new List<string>();
        var checkedCount = 0;

        foreach (var chordText in ReferencePanelChords())
        {
            Assert.True(KeyChord.TryParse(chordText, out var chord), $"Reference chord does not parse: '{chordText}'");
            checkedCount++;
            if (registry.Resolve(chord) is null)
            {
                missing.Add(chordText);
            }
        }

        Assert.True(checkedCount > 150, $"Only {checkedCount} chords were read from the reference.");
        Assert.True(missing.Count == 0, "Not bound: " + string.Join(", ", missing));
    }

    private static IEnumerable<string> ReferencePanelChords()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "docs", "reference", "keyboard-shortcuts.md"));
        var start = Array.IndexOf(lines, "### Panels");
        Assert.True(start >= 0, "'### Panels' section not found.");

        for (var i = start + 1; i < lines.Length && !lines[i].StartsWith("### ", StringComparison.Ordinal); i++)
        {
            var line = lines[i];
            if (!line.StartsWith("| `", StringComparison.Ordinal))
            {
                continue;
            }

            var cell = line.Split(" | ")[0].TrimStart('|', ' ').Replace("\\``", "Grave`", StringComparison.Ordinal);
            foreach (var part in cell.Split(" or "))
            {
                var text = part.Replace("`", "", StringComparison.Ordinal).Trim();
                if (text is "Alt" or "A..Z" or "Alt+A..Z")
                {
                    continue;
                }

                foreach (var expanded in Expand(text))
                {
                    yield return expanded;
                }
            }
        }
    }

    private static IEnumerable<string> Expand(string text)
    {
        if (text.EndsWith("Left/Right", StringComparison.Ordinal))
        {
            var prefix = text[..^"Left/Right".Length];
            return [prefix + "Left", prefix + "Right"];
        }

        if (text.Contains("0..9", StringComparison.Ordinal))
        {
            return Enumerable.Range(0, 10).Select(d => text.Replace("0..9", d.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }

        if (text.Contains("A..Z", StringComparison.Ordinal))
        {
            return Enumerable.Range('A', 26).Select(c => text.Replace("A..Z", ((char)c).ToString(), StringComparison.Ordinal));
        }

        return [text];
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Salandual.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (Salandual.slnx) not found.");
    }
}
