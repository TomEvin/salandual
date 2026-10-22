// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/EditorFileTests.cs

using System.Text;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.Core.Tests;

public sealed class EditorFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-editor-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Load_DetectsEncodingBomLineEndingsAndIndentation()
    {
        var utf8Bom = EditorFiles.Load(Write("a.json", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{\r\n  \"á\": 1\r\n}\r\n")]));
        Assert.Equal(("utf-8", true, LineEnding.CrLf, IndentStyle.Spaces2), (utf8Bom.Encoding.Id, utf8Bom.Bom, utf8Bom.LineEnding, utf8Bom.Indent));
        Assert.Equal("{\r\n  \"á\": 1\r\n}\r\n", utf8Bom.Text);
        Assert.Equal("UTF-8 with BOM", utf8Bom.EncodingName);

        var lf = EditorFiles.Load(Write("b.yml", Encoding.UTF8.GetBytes("a:\n    b: 1\n    c:\n        d: 2\n")));
        Assert.Equal((false, LineEnding.Lf, IndentStyle.Spaces4), (lf.Bom, lf.LineEnding, lf.Indent));

        var mixed = EditorFiles.Load(Write("c.txt", Encoding.UTF8.GetBytes("one\r\ntwo\nthree")));
        Assert.Null(mixed.LineEnding);
        Assert.Null(mixed.Indent);

        Assert.Equal(IndentStyle.Tabs, EditorFiles.DetectIndent("<a>\n\t<b/>\n\t<c/>\n</a>"));

        var bytes = Encoding.ASCII.GetBytes(new string('x', TextEncodings.SampleSize + 10)).Concat(new byte[] { 0xE9 }).ToArray();
        var ansi = EditorFiles.Load(Write("d.txt", bytes));
        Assert.False(ansi.Encoding.IsUtf8);
        Assert.Equal(bytes.Length, ansi.Text.Length);
    }

    [Fact]
    public void Prepare_ChangesLineEndingsOnlyWhenChosen_TrimsAndEnds()
    {
        Assert.Equal("a\r\nb\nc", EditorFiles.Prepare("a\r\nb\nc", null, false, FinalNewline.Keep));
        Assert.Equal("a\nb\nc", EditorFiles.Prepare("a\r\nb\nc", LineEnding.Lf, false, FinalNewline.Keep));
        Assert.Equal("a\r\nb\r\nc\r\n", EditorFiles.Prepare("a\nb\nc", LineEnding.CrLf, false, FinalNewline.Always));
        Assert.Equal("a\r\nb", EditorFiles.Prepare("a  \r\nb\t", LineEnding.CrLf, true, FinalNewline.Keep));
    }

    [Fact]
    public void Save_ReplacesTheFile_KeepsABackupOnce_AndNoticesChanges()
    {
        var path = Write("notes.txt", Encoding.UTF8.GetBytes("old"));
        var first = EditorFiles.Load(path);
        Assert.Equal(DiskState.Same, EditorFiles.Check(path, first.Size, first.WriteTimeUtc));

        var (size, time) = EditorFiles.Save(path, EditorFiles.Encode("new", first.Encoding, bom: false), backup: true);
        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal("old", File.ReadAllText(path + ".bak"));
        Assert.Equal(3, size);
        Assert.Empty(Directory.GetFiles(_dir, "*.saving"));

        EditorFiles.Save(path, EditorFiles.Encode("newer", first.Encoding, bom: true), backup: false);
        Assert.Equal("old", File.ReadAllText(path + ".bak"));
        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'n'], File.ReadAllBytes(path)[..4]);

        Assert.Equal(DiskState.Changed, EditorFiles.Check(path, size, time));
        File.Delete(path);
        Assert.Equal(DiskState.Gone, EditorFiles.Check(path, size, time));

        var fresh = Path.Combine(_dir, "fresh.txt");
        EditorFiles.Save(fresh, EditorFiles.Encode("hi", TextEncodings.Default, false), backup: true);
        Assert.Equal("hi", File.ReadAllText(fresh));
        Assert.False(File.Exists(fresh + ".bak"));
    }

    [Fact]
    public void Unrepresentable_CountsWhatACodePageCantHold()
    {
        var latin2 = TextEncodings.Find("cp1250")!;
        Assert.Equal(0, EditorFiles.Unrepresentable("Árvíztűrő", latin2));
        Assert.Equal(2, EditorFiles.Unrepresentable("東京", latin2));
        Assert.Equal(0, EditorFiles.Unrepresentable("東京", TextEncodings.Default));
    }

    [Fact]
    public void Table_ChoosesColoursAndLimits_ByTheFirstMaskThatFits()
    {
        var s = new EditorSettings();
        Assert.Equal(SyntaxLanguage.Json, s.ColorsFor("settings.json"));
        Assert.Equal(SyntaxLanguage.Xml, s.ColorsFor("Salandual.App.csproj"));
        Assert.Equal(SyntaxLanguage.None, s.ColorsFor("Program.cs"));
        Assert.Equal(20L * 1024 * 1024, s.ColorsUpToBytes("a.json"));
        Assert.Equal(10L * 1024 * 1024, s.ColorsUpToBytes("a.yml"));
        Assert.Equal(0, s.ColorsUpToBytes("a.cs"));
        Assert.Equal(50L * 1024 * 1024, s.LargestBytes("a.cs"));

        s.Masks[0].LargestMB = 0;
        Assert.Equal(0, s.LargestBytes("a.json"));
        var normal = new AppSettings { Editors = s with { Masks = [new() { Mask = " ", Colors = SyntaxLanguage.Python, ColorsUpToMB = -5 }] } }.Normalize().Editors;
        Assert.Equal(("*.*", SyntaxLanguage.None, 1L), (normal.Masks[0].Mask, normal.Masks[0].Colors, normal.Masks[0].ColorsUpToMB));
    }
}
