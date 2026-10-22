// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/FileMaskTests.cs

using Salandual.Core.Listing;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class FileMaskTests
{
    [Theory]
    [InlineData("*.*", "photo.jpg", true)]
    [InlineData("*.*", "README", true)]
    [InlineData("*", "README", true)]
    [InlineData("*.jpg", "PHOTO.JPG", true)]
    [InlineData("*.jpg", "photo.jpeg", false)]
    [InlineData("*.a?", "anything.ai", true)]
    [InlineData("*.a?", ".a1", true)]
    [InlineData("*.a?", "x.abc", false)]
    [InlineData("*.txt;*.doc", "b.doc", true)]
    [InlineData("*.txt; *.doc", "b.doc", true)]
    [InlineData("|*.txt;*.doc", "a.txt", false)]
    [InlineData("|*.txt;*.doc", "a.md", true)]
    [InlineData("*.jpg;*.png|thumb*", "thumb_1.jpg", false)]
    [InlineData("*.jpg;*.png|thumb*", "IMG_1.png", true)]
    [InlineData("one;;mask", "one;mask", true)]
    [InlineData("one;;mask", "one", false)]
    [InlineData("*.", "README", true)]
    [InlineData("*.", "readme.md", false)]
    [InlineData("IMG_04??.*", "IMG_0412.HEIC", true)]
    [InlineData("IMG_04??.*", "IMG_041.HEIC", false)]
    [InlineData("árak.*", "ÁRAK.JSON", true)]
    [InlineData("日本*", "日本語メモ.txt", true)]
    public void Match(string mask, string name, bool expected) => Assert.Equal(expected, FileMask.Parse(mask)!.IsMatch(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";")]
    [InlineData("|")]
    [InlineData(null)]
    public void Parse_NoMask_IsNull(string? text) => Assert.Null(FileMask.Parse(text));

    [Fact]
    public void Parse_KeepsTheTypedText() => Assert.Equal("*.jpg;*.png", FileMask.Parse("  *.jpg;*.png ")!.Text);

    [Theory]
    [InlineData(@"IMG_\d+\.jpg", "IMG_2041.jpg", true)]
    [InlineData(@"IMG_\d+\.jpg", "img_7.JPG", true)]
    [InlineData(@"IMG_\d+\.jpg", "IMG_2041.jpg.bak", false)]
    [InlineData(@"IMG_\d+\.jpg", "old IMG_2041.jpg", false)]
    [InlineData("a|b", "b", true)]
    public void Regex_MatchesTheWholeName(string pattern, string name, bool expected)
    {
        var mask = FileMask.ParseRegex(pattern, out var problem);
        Assert.Null(problem);
        Assert.True(mask!.IsRegex);
        Assert.Equal(expected, mask.IsMatch(name));
    }

    [Fact]
    public void Regex_KeptInTheHistoryWithItsPrefix()
    {
        var mask = FileMask.ParseRegex(@"\d+", out _)!;
        Assert.Equal(@"regex:\d+", mask.Text);
        Assert.True(FileMask.FromHistory(mask.Text)!.IsMatch("2041"));
        Assert.False(FileMask.FromHistory("*.jpg")!.IsRegex);
        Assert.Null(FileMask.ParseRegex("(", out var problem));
        Assert.NotNull(problem);
        Assert.Null(FileMask.ParseRegex(" ", out _));
    }

    [Fact]
    public void History_NewestFirst_NoDuplicates_Capped()
    {
        var list = HistorySettings.Push(["*.jpg", "*.png"], "*.PNG");
        Assert.Equal(["*.PNG", "*.jpg"], list);

        for (var i = 0; i < 30; i++)
        {
            list = HistorySettings.Push(list, $"*.{i}");
        }

        Assert.Equal(HistorySettings.MaxItems, list.Count);
        Assert.Equal("*.29", list[0]);
    }
}
