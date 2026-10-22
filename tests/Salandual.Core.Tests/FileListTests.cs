// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/FileListTests.cs

using Salandual.Core.Listing;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class FileListTests
{
    private static readonly DateTime _when = new DateTime(2026, 10, 15, 21, 47, 0, DateTimeKind.Local).ToUniversalTime();

    private static readonly FileListItem[] _items =
    [
        new(@"C:\Projects\Website\README.md", 4301, _when, FileAttributes.Archive),
        new(@"C:\Projects\Website\CHANGELOG.md", 19046, _when, FileAttributes.ReadOnly | FileAttributes.Archive),
        new(@"C:\Projects\Website\assets", 0, _when, FileAttributes.Directory),
        new(@"C:\go.bat", 12, _when, FileAttributes.Normal),
    ];

    private static string Expand(string line, params FileListItem[] items)
    {
        var parsed = FileListLine.Parse(line, out var error, out _, name => name == "HOME" ? @"C:\Users\me" : null);
        Assert.True(parsed is not null, error);
        return parsed.Expand(items.Length > 0 ? items : _items, FileListContext.Default);
    }

    [Fact]
    public void TheNames_EachOnALine()
        => Assert.Equal("README.md\r\nCHANGELOG.md\r\nassets\r\ngo.bat\r\n", Expand("$(FileName)$(CRLF)"));

    [Fact]
    public void ANameSplits_IntoItsPartAndExtension_NotAFolders()
        => Assert.Equal("README|md;CHANGELOG|md;assets|;go|bat;", Expand("$(FileNamePart)|$(FileExtension);"));

    [Fact]
    public void TheSize_IsTheBytes_AndAFolderSaysSo()
        => Assert.Equal("4301 19046 Folder 12 ", Expand("$(FileSize) "));

    [Fact]
    public void TheDateTimeAndAttributes_AsThePanelsShowThem()
        => Assert.Equal("2026-10-15 21:47 A|", Expand("$(FileDate) $(FileTime) $(FileAttributes)|", _items[0]));

    [Fact]
    public void NoAttributes_IsADash()
        => Assert.Equal("-", Expand("$(FileAttributes)", _items[3]));

    [Fact]
    public void ThePathsAndTheDrive_AsSalamander()
    {
        Assert.Equal(@"C:|\Projects\Website\|C:\Projects\Website|C:\Projects\Website\README.md", Expand("$(Drive)|$(Path)|$(FullPath)|$(FullName)", _items[0]));
        Assert.Equal(@"C:|\|C:\", Expand("$(Drive)|$(Path)|$(FullPath)", _items[3]));
        var share = new FileListItem(@"\\server\share\docs\a.txt", 1, _when, FileAttributes.Archive);
        Assert.Equal(@"\\server\share|\docs\", Expand("$(Drive)|$(Path)", share));
    }

    [Fact]
    public void AWidth_CutsOrFills_AndMax_IsTheLongest()
    {
        Assert.Equal("READ|CHAN|asse|go.b|", Expand("$(FileName:4)|"));
        Assert.Equal("README.md   |CHANGELOG.md|assets      |go.bat      |", Expand("$(FileName:max)|"));
        Assert.Equal("go.bat  |", Expand("$(filename:8)|", _items[3]));
    }

    [Fact]
    public void DollarsAndTheEnvironment()
    {
        Assert.Equal(@"$5 C:\Users\me go.bat", Expand("$$5 $[HOME] $(FileName)", _items[3]));
        Assert.Equal("a\tb\nc\r", Expand("a$(TAB)b$(LF)c$(CR)", _items[3]));
    }

    [Theory]
    [InlineData("$(FileNam)", "There is no variable $(FileNam).", 0)]
    [InlineData("x $(FileName", "$( needs a ) after the name.", 2)]
    [InlineData("$(FileName:0)", "After the : comes a width from 1 to 9999, or max.", 10)]
    [InlineData("$(FileName:wide)", "After the : comes a width from 1 to 9999, or max.", 10)]
    [InlineData("5$", "A $ starts a variable: $(…) or $[…]. Write $$ for a dollar.", 1)]
    [InlineData("$[NOPE]", "There is no environment variable NOPE.", 0)]
    [InlineData("$[HOME", "$[ needs a ] after the name.", 0)]
    public void ALineThatCantBeRead_SaysWhyAndWhere(string line, string error, int at)
    {
        Assert.Null(FileListLine.Parse(line, out var why, out var where, name => name == "HOME" ? "h" : null));
        Assert.Equal(error, why);
        Assert.Equal(at, where);
    }

    [Fact]
    public void TheNewVariables()
    {
        var ctx = new FileListContext(DisplayFormat.Default, @"C:\Projects", p => p.Replace("CHANGELOG.md", "CHANGE~1.MD").Replace("Projects", "PROJEC~1"));
        string Line(string line, params FileListItem[] items) => FileListLine.Parse(line, out _, out _)!.Expand(items.Length > 0 ? items : _items, ctx);
        Assert.Equal(@"Website\README.md|Website\CHANGELOG.md|Website\assets|C:\go.bat|", Line("$(RelativeName)|"));
        Assert.Equal("Website|Website|Website|C:|", Line("$(FolderName)|"));
        Assert.Equal("1/4 2/4 3/4 4/4 ", Line("$(Number)/$(Count) "));
        Assert.Equal("File File Folder File ", Line("$(Type) "));
        Assert.Equal("5 KB|19 KB|Folder|12 B|", Line("$(FileSizeShort)|"));
        Assert.Equal("2026-10-15 21:47:00", Line("$(FileDateTime)", _items[0]));
        Assert.Equal(@"CHANGE~1.MD|\PROJEC~1\Website\", Line("$(FileDOSName)|$(DOSPath)", _items[1]));
        Assert.Equal(@"README.md|\Projects\Website\", FileListLine.Parse("$(FileDOSName)|$(DOSPath)", out _, out _)!.Expand([_items[0]], FileListContext.Default));
        var made = new FileListItem(@"C:\x.txt", 1, _when, FileAttributes.Archive, new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Local).ToUniversalTime());
        Assert.Equal("2026-01-02 03:04", Line("$(Created)", made));
        Assert.Equal("", Line("$(Created)", new FileListItem(@"C:\no\such\file.txt", 1, _when, FileAttributes.Archive)));
        Assert.Equal("1 |2 |3 |4 |", Line("$(Number:2)|"));
    }

    [Fact]
    public void FilesNamed_TheFilesTheMaskNames()
    {
        Assert.Equal(_items, FileListScan.Named(_items, null, foldersToo: false));
        Assert.Equal(["README.md", "CHANGELOG.md"], FileListScan.Named(_items, FileMask.Parse("*.md"), foldersToo: false).Select(i => i.Name));
        Assert.Equal(["README.md", "CHANGELOG.md", "assets"], FileListScan.Named(_items, FileMask.Parse("*.md"), foldersToo: true).Select(i => i.Name));
        Assert.Equal(["go.bat"], FileListScan.Named(_items, FileMask.Parse("|*.md"), foldersToo: false).Select(i => i.Name));
    }

    [Fact]
    public void TheFirstLine_TakesNoFile()
    {
        var first = FileListLine.Parse("Name$(TAB)Size$(CRLF)", out _, out _)!;
        Assert.False(first.NamesAFile);
        Assert.True(first.EndsWithBreak);
        Assert.Equal("Name\tSize\r\n", first.ExpandOnce());
        Assert.Equal("7 items", FileListLine.Parse("$(Count) items", out _, out _)!.ExpandOnce(7));
        Assert.False(FileListLine.Parse("$(Count)", out _, out _)!.NamesAFile);

        Assert.True(FileListLine.Parse("$(FileName)", out _, out _)!.NamesAFile);
        Assert.False(FileListLine.Parse("Name", out _, out _)!.EndsWithBreak);
    }

    [Fact]
    public void TheBuiltInLines_AllRead()
    {
        Assert.Equal(5, FileListTemplate.BuiltIn.Count);
        foreach (var t in FileListTemplate.BuiltIn)
        {
            Assert.NotNull(FileListLine.Parse(t.First, out _, out _));
            Assert.NotNull(FileListLine.Parse(t.Line, out _, out _));
        }

        var csv = FileListTemplate.BuiltIn[3];
        Assert.Equal("\"README.md\";4301;2026-10-15;\"C:\\Projects\\Website\"\r\n", FileListLine.Parse(csv.Line, out _, out _)!.Expand([_items[0]], FileListContext.Default));
    }

    [Fact]
    public void Settings_DropEmptyAndRepeatedNames()
    {
        var s = new FileListSettings
        {
            Mine = [new("Wiki", "", "$(FileName)"), new("  ", "", "x"), new("wiki", "", "y"), new(" Two ", null!, "z")],
            FileName = " ",
        };
        var n = new AppSettings { FileList = s }.Normalize().FileList;
        Assert.Equal(["Wiki", "Two"], n.Mine.Select(m => m.Name));
        Assert.Equal("", n.Mine[1].First);
        Assert.Equal("files.txt", n.FileName);
    }
}
