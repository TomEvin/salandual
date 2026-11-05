// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/HighlightTests.cs

using System.Text.Json;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Core.Tests;

public sealed class HighlightTests
{
    private static FileEntry File(string name, FileAttributes attributes = FileAttributes.Normal) => new(name, 1, 0, attributes);

    private static FileEntry Folder(string name, FileAttributes attributes = 0) => new(name, 0, 0, FileAttributes.Directory | attributes);

    [Fact]
    public void Defaults_AreWhatSalandualDidBefore_TheIdeasOff_TheFirstMatchWins()
    {
        var rules = HighlightSettings.Defaults();
        Assert.Equal(["Encrypted", "Compressed", "Hidden and system", "Archives", "Programs", "Temporary files"], rules.Select(r => r.Name));
        Assert.Equal([true, true, true, false, false, false], rules.Select(r => r.On));

        var h = new Highlighter(rules);
        Assert.Equal("Encrypted", h.Match(File("a.txt", FileAttributes.Encrypted | FileAttributes.Compressed))?.Name);
        Assert.Equal("Compressed", h.Match(Folder("Old", FileAttributes.Compressed))?.Name);
        Assert.Equal("Hidden and system", h.Match(File("desktop.ini", FileAttributes.System))?.Name);
        Assert.Equal("Hidden and system", h.Match(Folder(".git", FileAttributes.Hidden))?.Name);
        Assert.Null(h.Match(File("backup.7z")));

        var all = new Highlighter(rules.Select(r => r with { On = true }));
        Assert.Equal("Archives", all.Match(File("backup.7z"))?.Name);
        Assert.Null(all.Match(Folder("photos.7z")));
        Assert.Equal("Temporary files", all.Match(File("~lock.docx"))?.Name);
        Assert.Equal("Programs", all.Match(File("SETUP.EXE"))?.Name);
    }

    [Fact]
    public void Masks_LeaveOut_MustNotHave_AllOrOneOf_AndABrokenMaskMatchesNothing()
    {
        var rule = new HighlightRule { Name = "Pictures", Masks = "*.jpg;*.heic | IMG_9*", MustNotHave = FileAttributes.ReadOnly };
        Assert.True(Highlighter.Matches(rule, File("IMG_0410.jpg")));
        Assert.False(Highlighter.Matches(rule, File("IMG_9001.jpg")));
        Assert.False(Highlighter.Matches(rule, File("IMG_0412.heic", FileAttributes.ReadOnly)));

        var both = new HighlightRule { Name = "x", MustHave = FileAttributes.Hidden | FileAttributes.ReadOnly };
        Assert.False(Highlighter.Matches(both, File("a", FileAttributes.Hidden)));
        Assert.True(Highlighter.Matches(both, File("a", FileAttributes.Hidden | FileAttributes.ReadOnly)));
        Assert.True(Highlighter.Matches(both with { AnyOf = true }, File("a", FileAttributes.Hidden)));

        Assert.False(Highlighter.Matches(new HighlightRule { Name = "x", Masks = "regex:([" }, File("a.txt")));
        Assert.True(Highlighter.Matches(new HighlightRule { Name = "x", Masks = "regex:^a\\d$" }, File("a1")));
    }

    [Fact]
    public void Colours_TheThemesOwnForDimmedBlueGreen_TheShadeByTheBackground_ReadableOnIt()
    {
        var tokens = ColorTokens.Resolve(ThemeCatalog.BuiltIn[0]);
        var white = new Rgb(255, 255, 255);
        Assert.Null(Highlighter.Colour(NameColour.Normal, white, t => tokens[t]));
        Assert.Equal(tokens[ColorTokens.SecondaryText], Highlighter.Colour(NameColour.Dimmed, white, t => tokens[t]));
        Assert.Equal(tokens[ColorTokens.CompressedFiles], Highlighter.Colour(NameColour.Blue, white, t => tokens[t]));
        Assert.Equal(ColorMath.ParseHex("#7E57C2"), Highlighter.Colour(NameColour.Purple, white, t => tokens[t]));
        var dark = new Rgb(0x27, 0x27, 0x27);
        Assert.Equal(ColorMath.ParseHex("#B39DDB"), Highlighter.Colour(NameColour.Purple, dark, t => tokens[t]));
        foreach (var colour in Enum.GetValues<NameColour>().Where(c => Highlighter.Shades(c) is not null && c is not (NameColour.Blue or NameColour.Green)))
        {
            foreach (var background in new[] { white, dark, new Rgb(0xF3, 0xF3, 0xF3) })
            {
                Assert.True(ColorMath.Contrast(Highlighter.Colour(colour, background, t => tokens[t])!.Value, background) >= 4.5, $"{colour} on {background}");
            }
        }

        Assert.Equal(ColorMath.ParseHex("#123456"), Highlighter.Colour(NameColour.Own, dark, t => tokens[t], "#ABCDEF", "#123456"));
    }

    [Fact]
    public void OldSettings_DimmedOff_TurnsTheThirdRuleOff_AndNormalizingAgainChangesNothing()
    {
        var old = new AppSettings { Panels = new PanelSettings { DimHidden = false } }.Normalize();
        Assert.False(old.Highlighting.Rules![2].On);
        Assert.True(old.Highlighting.Rules[0].On);
        Assert.True(old.Panels.DimHidden);
        Assert.Same(old.Highlighting, old.Normalize().Highlighting);

        var fresh = new AppSettings().Normalize();
        Assert.Null(fresh.Highlighting.Rules);
        Assert.Same(fresh.Highlighting, fresh.Normalize().Highlighting);
    }

    [Fact]
    public void FolderLooks_ByPath_FollowARenameWithTheFoldersInside_KeptInTheSettingsFile()
    {
        var look = new FolderLook { Colour = NameColour.Orange, Mark = "★" };
        var h = new HighlightSettings()
            .WithFolder(@"D:\Work\Clients\", look)
            .WithFolder(@"D:\Work\Clients\Acme", new FolderLook { Colour = NameColour.Teal })
            .WithFolder(@"D:\Work\ClientsOld", new FolderLook { Mark = "!" });
        Assert.Equal(look, h.Folders[@"d:\work\clients"]);
        var moved = h.Moved(@"D:\Work\Clients", @"E:\Archive\Customers")!;
        Assert.Equal(look, moved.Folders[@"E:\Archive\Customers"]);
        Assert.Equal(NameColour.Teal, moved.Folders[@"E:\Archive\Customers\Acme"].Colour);
        Assert.True(moved.Folders.ContainsKey(@"D:\Work\ClientsOld"));
        Assert.False(moved.Folders.ContainsKey(@"D:\Work\Clients"));
        Assert.Null(h.Moved(@"D:\Other", @"D:\Else"));
        Assert.False(h.WithFolder(@"D:\Work\Clients", null).Folders.ContainsKey(@"D:\Work\Clients"));

        var json = JsonSerializer.Serialize(new AppSettings { Highlighting = h });
        var back = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Normalize();
        Assert.Equal(look, back.Highlighting.Folders[@"D:\WORK\CLIENTS"]);
    }

    [Fact]
    public void FolderLooks_Normalize_DropsWhatCouldBeHarmful()
    {
        var h = new HighlightSettings
        {
            Folders = new()
            {
                [@"C:\a"] = new FolderLook { Mark = "<script>" },
                [@"C:\b"] = new FolderLook { Image = @"..\..\Windows\evil.png" },
                [@"C:\c"] = new FolderLook { Image = "0a1b2c.png", Colour = NameColour.Own },
            },
        };
        var n = new AppSettings { Highlighting = h }.Normalize().Highlighting;
        Assert.False(n.Folders.ContainsKey(@"C:\a"));
        Assert.False(n.Folders.ContainsKey(@"C:\b"));
        Assert.Equal(new FolderLook { Image = "0a1b2c.png" }, n.Folders[@"C:\c"]);
        Assert.True(FolderMarks.IsImageName("0a1b2c.png"));
        Assert.False(FolderMarks.IsImageName("a/b.png"));
        Assert.False(FolderMarks.IsImageName("ab12.exe"));
    }

    [Fact]
    public void ARuleWithoutAName_OrAnOwnColourThatIsNotOne_IsCleanedUp()
    {
        var n = new AppSettings
        {
            Highlighting = new HighlightSettings
            {
                Rules = [new HighlightRule { Name = "  " }, new HighlightRule { Name = "Mine", Colour = NameColour.Own, OwnLight = "red", OwnDark = "#000000", MustHave = FileAttributes.Hidden, MustNotHave = FileAttributes.Hidden }],
            },
        }.Normalize().Highlighting;
        var rule = Assert.Single(n.Rules!);
        Assert.Equal(NameColour.Normal, rule.Colour);
        Assert.Equal((FileAttributes)0, rule.MustNotHave);
    }
}
