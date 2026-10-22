// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/EditingTests.cs

using Salandual.Core.Editing;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.Core.Tests;

public sealed class EditingTests
{

    [Fact]
    public void Json_ValidFile_HasNoProblems_AndFolds()
    {
        var text = "{\n  \"a\": 1,\n  \"b\": [\n    true,\n    null\n  ],\n  \"c\": {}\n}\n";
        var result = JsonText.Check(text, allowComments: false);
        Assert.DoesNotContain(result.Problems, _ => true);
        Assert.Equal(2, result.Folds.Count);
        var b = result.Folds.Single(f => f.Start == text.IndexOf('[') + 1);
        Assert.Equal(text.IndexOf(']'), b.End);
        Assert.Equal(2, b.Lines);
    }

    [Fact]
    public void Json_MissingComma_SaysWhereAndBetweenWhat()
    {
        var text = "{\n  \"theme\": {\n    \"accent\": 1\n  }\n  \"followWindowsAccent\": true\n}";
        var problem = Assert.Single(JsonText.Check(text, allowComments: false).Problems);
        Assert.Equal(5, problem.Line);
        Assert.Equal(3, problem.Column);
        Assert.Contains("}", problem.Message, StringComparison.Ordinal);
        Assert.Contains("line 4", problem.Message, StringComparison.Ordinal);
        Assert.Contains("\"followWindowsAccent\"", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"a\" 1}", 1, 2)]
    [InlineData("{\"a\": }", 1, 7)]
    [InlineData("[1, 2,]", 1, 6)]
    [InlineData("{\"a\": True}", 1, 7)]
    [InlineData("{\"a\": 'x'}", 1, 7)]
    [InlineData("{\"a\": \"x}", 1, 7)]
    [InlineData("[1, 2}", 1, 6)]
    [InlineData("{\n\"a\": [1, 2\n", 2, 6)]
    [InlineData("{} []", 1, 4)]
    [InlineData("{\"a\": 01}", 1, 7)]
    [InlineData("// note\n{}", 1, 1)]
    public void Json_Mistakes_AreFound(string text, int line, int column)
    {
        var problem = Assert.Single(JsonText.Check(text, allowComments: false).Problems);
        Assert.Equal((line, column), (problem.Line, problem.Column));
        Assert.False(string.IsNullOrWhiteSpace(problem.Message));
    }

    [Fact]
    public void Jsonc_AllowsComments_AndTheLastComma()
    {
        Assert.True(JsonText.AllowsComments("settings.json"));
        Assert.True(JsonText.AllowsComments("tsconfig.build.json"));
        Assert.True(JsonText.AllowsComments("a.jsonc"));
        Assert.False(JsonText.AllowsComments("package.json"));
        Assert.DoesNotContain(JsonText.Check("{\n  // the theme\n  \"a\": 1, /* x */\n  \"b\": [1, 2,],\n}", allowComments: true).Problems, _ => true);
    }

    [Fact]
    public void Json_NameUsedTwice_IsAProblem_ButFormatStillWorks()
    {
        var text = "{\"a\": 1,\n \"a\": 2}";
        var problem = Assert.Single(JsonText.Check(text, allowComments: false).Problems);
        Assert.Equal(2, problem.Line);
        Assert.Contains("line 1", problem.Message, StringComparison.Ordinal);
        Assert.NotNull(JsonText.Format(text, "  ", "\n", allowComments: false));
    }

    [Fact]
    public void Json_Format_KeepsValuesAndComments()
    {
        var text = "{\"a\":1.50e3,\"b\":[\"x\\u00e9\",{}],// note\n\"c\":{\"d\":null}}\n";
        var formatted = JsonText.Format(text, "  ", "\r\n", allowComments: true);
        Assert.Equal("{\r\n  \"a\": 1.50e3,\r\n  \"b\": [\r\n    \"x\\u00e9\",\r\n    {}\r\n  ], // note\r\n  \"c\": {\r\n    \"d\": null\r\n  }\r\n}\r\n", formatted);
        Assert.Null(JsonText.Format("{\"a\" 1}", "  ", "\n", allowComments: false));
    }

    [Fact]
    public void Json_Format_OfASelection_UsesItsIndentation()
        => Assert.Equal("{\n      \"x\": 1\n    }", JsonText.Format("{\"x\":1}", "  ", "\n", allowComments: false, prefix: "    "));

    [Fact]
    public void Json_Minify_PutsItOnOneLine()
    {
        Assert.Equal("{\"a\":[1,2],\"b\":\"x y\"}", JsonText.Minify("{\n  \"a\": [1, 2],\n  \"b\": \"x y\"\n}\n", allowComments: false));
        Assert.Equal("{/* note*/\"a\":1}", JsonText.Minify("{ // note\n \"a\": 1 }", allowComments: true));
    }

    [Fact]
    public void Json_SortKeys_SortsEveryObject_ListsKeepTheirOrder()
    {
        var sorted = JsonText.SortKeys("{\"b\":1,\"A\":{\"z\":[3,1],\"y\":2},\"a\":0}", "  ", "\n", "", out var refusal);
        Assert.Equal(JsonText.SortRefusal.None, refusal);
        Assert.Equal("{\n  \"a\": 0,\n  \"A\": {\n    \"y\": 2,\n    \"z\": [\n      3,\n      1\n    ]\n  },\n  \"b\": 1\n}", sorted);
        Assert.Null(JsonText.SortKeys("{\"b\":1, // x\n\"a\":2}", "  ", "\n", "", out refusal));
        Assert.Equal(JsonText.SortRefusal.Comments, refusal);
        Assert.Null(JsonText.SortKeys("12", "  ", "\n", "", out refusal));
        Assert.Equal(JsonText.SortRefusal.NotObject, refusal);
    }

    [Fact]
    public void Json_DeepNesting_IsReadWithoutRecursion()
    {
        var deep = new string('[', 100_000) + new string(']', 100_000);
        Assert.DoesNotContain(JsonText.Check(deep, allowComments: false).Problems, _ => true);
    }

    [Fact]
    public void Xml_Check_FindsTheMistake_AndFolds()
    {
        var good = "<Project>\n  <PropertyGroup>\n    <A>1</A>\n  </PropertyGroup>\n</Project>\n";
        var result = XmlMarkup.Check(good);
        Assert.DoesNotContain(result.Problems, _ => true);
        Assert.Equal(2, result.Folds.Count);

        var problem = Assert.Single(XmlMarkup.Check("<a>\n  <b>\n</a>").Problems);
        Assert.Equal(3, problem.Line);
        Assert.DoesNotContain("Line 3", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_Check_NeverFetchesAnything()
    {
        var text = "<?xml version=\"1.0\"?>\n<!DOCTYPE a [ <!ENTITY e SYSTEM \"http://127.0.0.1:1/x\"> ]>\n<a>&e;</a>";
        _ = XmlMarkup.Check(text);
        var bomb = "<!DOCTYPE a [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\"><!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\"><!ENTITY d \"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;\"><!ENTITY e \"&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;\"><!ENTITY f \"&e;&e;&e;&e;&e;&e;&e;&e;&e;&e;\"><!ENTITY g \"&f;&f;&f;&f;&f;&f;&f;&f;&f;&f;\"><!ENTITY h \"&g;&g;&g;&g;&g;&g;&g;&g;&g;&g;\">]><a>&h;</a>";
        Assert.Single(XmlMarkup.Check(bomb).Problems);
    }

    [Fact]
    public void Xml_Format_ReindentsLines_KeepsTextAndAttributes()
    {
        var text = "<root>\n<a x=\"1\"\n   y=\"2\">\n<b>text\nmore</b>\n<!-- c -->\n</a>\n\n<pre xml:space=\"preserve\">\n keep\n</pre>\n</root>\n";
        var formatted = XmlMarkup.Format(text, "  ");
        Assert.Equal("<root>\n  <a x=\"1\"\n     y=\"2\">\n    <b>text\nmore</b>\n    <!-- c -->\n  </a>\n\n  <pre xml:space=\"preserve\">\n keep\n</pre>\n</root>\n", formatted);
        Assert.Null(XmlMarkup.Format("<a><b></a>", "  "));
    }

    [Fact]
    public void Yaml_Check_FindsTheMistake_AndKeysUsedTwice()
    {
        Assert.DoesNotContain(YamlText.Check("services:\n  web:\n    image: nginx\n").Problems, _ => true);
        var problem = Assert.Single(YamlText.Check("a: 1\nb: [1, 2\nc: 3\n").Problems);
        Assert.True(problem.Line >= 2);
        Assert.DoesNotContain("Idx:", problem.Message, StringComparison.Ordinal);

        var twice = Assert.Single(YamlText.Check("a: 1\nb:\n  x: 1\n  x: 2\n").Problems);
        Assert.Equal(4, twice.Line);
        Assert.Contains("line 3", twice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Yaml_Folds_FollowIndentation()
    {
        var text = "services:\n  web:\n    image: nginx\n    ports:\n      - 80\n  db:\n    image: pg\n";
        var folds = YamlText.Folds(text);
        Assert.Contains(folds, f => f.Start == text.IndexOf('\n') && f.Lines == 6);
        Assert.Contains(folds, f => f.Lines == 3);
        Assert.Contains(folds, f => f.Lines == 1);
    }

    [Fact]
    public void Yaml_Format_ReindentsEveryLevel_KeepsTheMeaning()
    {
        var text = "a:\n    b: 1\n    c:\n        - x\n        - y: 1\n          z: 2\n    # note\n    d: |\n        line one\n          indented\n    e: 2\n";
        var formatted = YamlText.Format(text, 2);
        Assert.Equal("a:\n  b: 1\n  c:\n    - x\n    - y: 1\n      z: 2\n  # note\n  d: |\n    line one\n      indented\n  e: 2\n", formatted);
        Assert.Null(YamlText.Format("a: [1,\n", 2));
    }

    [Fact]
    public void CommentLine_TogglesLineComments_AtTheSmallestIndentation()
    {
        var style = CommentStyle.For(SyntaxLanguage.Yaml, "a.yml")!;
        var commented = style.Toggle("  a: 1\n    b: 2\n\n");
        Assert.Equal("  # a: 1\n  #   b: 2\n\n", commented);
        Assert.Equal("  a: 1\n    b: 2\n\n", style.Toggle(commented));
    }

    [Fact]
    public void CommentLine_WrapsMarkupInABlock()
    {
        var style = CommentStyle.For(SyntaxLanguage.Xml, "a.xml")!;
        Assert.Equal("  <!-- <a/>\n  <b/> -->\n", style.Toggle("  <a/>\n  <b/>\n"));
        Assert.Equal("  <a/>\n  <b/>\n", style.Toggle("  <!-- <a/>\n  <b/> -->\n"));
        Assert.Equal("//", CommentStyle.For(SyntaxLanguage.None, "Program.cs")!.Start);
        Assert.Null(CommentStyle.For(SyntaxLanguage.None, "notes.txt"));
    }

    [Fact]
    public void ConvertIndentation_ChangesOnlyTheLeadingSpaces()
    {
        Assert.Equal("a\n    b  c\n        d\n     e\n", Indentation.Convert("a\n  b  c\n    d\n   e\n", IndentStyle.Spaces2, IndentStyle.Spaces4));
        Assert.Equal("a\n\tb\n\t\tc\n", Indentation.Convert("a\n    b\n        c\n", IndentStyle.Spaces4, IndentStyle.Tabs));
    }

    [Fact]
    public void ExternalCommand_SplitsFirst_ThenFillsTheVariables()
    {
        var command = ExternalCommand.Parse("\"C:\\Program Files\\Code\\Code.exe\" --goto \"$(FullName)\" -n")!;
        Assert.Equal("C:\\Program Files\\Code\\Code.exe", command.Program);
        Assert.Equal("Code", command.DisplayName);

        var args = command.ArgumentsFor("D:\\x\\a \"b\" & $(Name).txt");
        Assert.Equal(["--goto", "D:\\x\\a \"b\" & $(Name).txt", "-n"], args);
        Assert.Equal(["x.txt", "D:\\x", "x"], ExternalCommand.Parse("p.exe $(Name) $(FullPath) $(NameNoExt)")!.ArgumentsFor("D:\\x\\x.txt"));
        Assert.Null(ExternalCommand.Parse("   "));
    }

    [Fact]
    public void EditorTable_KnowsItsExternalEditors()
    {
        var settings = new EditorSettings { Masks = [new() { Mask = "*.sln", Program = "code.exe \"$(FullName)\"" }, new() { Mask = "*.*" }] };
        Assert.Equal("code.exe \"$(FullName)\"", settings.ProgramFor("a.sln"));
        Assert.Null(settings.ProgramFor("a.txt"));
        Assert.Single(settings.ExternalPrograms());
        Assert.Null(new EditorSettings { Masks = [new() { Mask = "*.*", Program = "  " }] }.Normalize().Masks[0].Program);
    }

    [Fact]
    public void Svg_Preview_LeavesOutWhatCouldRunOrReachOutside()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" onload=\"x()\"><script>alert(1)</script>"
            + "<a href=\"https://example.com\"><rect onclick=\"y()\" width=\"5\"/></a><use xlink:href=\"#r\"/><image href=\"http://example.com/p.png\"/>"
            + "<foreignObject><div>hi</div></foreignObject><set attributeName=\"href\" to=\"javascript:z()\"/><circle r=\"2\"/></svg>";
        var clean = SvgPage.Clean(svg);
        Assert.DoesNotContain("script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("onclick", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("foreignObject", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript", clean, StringComparison.Ordinal);
        Assert.Contains("#r", clean, StringComparison.Ordinal);
        Assert.Contains("<circle", clean, StringComparison.Ordinal);
        Assert.Contains("<rect", clean, StringComparison.Ordinal);

        var page = SvgPage.Render("<svg xmlns=\"http://www.w3.org/2000/svg\"/>", new PageOptions());
        Assert.Contains("default-src 'none'", page, StringComparison.Ordinal);
        Assert.Throws<SvgFormatException>(() => SvgPage.Render("<svg><g></svg>", new PageOptions()));
        Assert.Throws<SvgFormatException>(() => SvgPage.Render("<html/>", new PageOptions()));
    }

    [Fact]
    public void Markdown_Preview_MarksSourceLines()
    {
        var page = MarkdownPage.Render("# Title\n\nText\n\n- one\n- two\n", new PageOptions { SourceLines = true });
        Assert.Contains("data-line=\"0\"", page.Html, StringComparison.Ordinal);
        Assert.Contains("data-line=\"2\"", page.Html, StringComparison.Ordinal);
        Assert.Contains("data-line=\"5\"", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-line", MarkdownPage.Render("# Title", new PageOptions()).Html, StringComparison.Ordinal);
    }
}
