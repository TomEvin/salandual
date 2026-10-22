// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/SyntaxRulesTests.cs

using System.Text;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.Core.Tests;

public sealed class SyntaxRulesTests
{
    private static (List<string> Pieces, int State) Colour(SyntaxLanguage language, string line, int state = 0)
    {
        var spans = new List<SyntaxSpan>();
        var end = SyntaxRules.Tokenize(language, line, state, spans);
        return ([.. spans.OrderBy(s => s.Start).Select(s => line.Substring(s.Start, s.Length) + ":" + s.Kind)], end);
    }

    private static SyntaxKind? KindOf(SyntaxLanguage language, string line, string text, int state = 0)
    {
        var spans = new List<SyntaxSpan>();
        SyntaxRules.Tokenize(language, line, state, spans);
        var at = line.IndexOf(text, StringComparison.Ordinal);
        Assert.True(at >= 0, text);
        return spans.Where(s => s.Start <= at && at < s.Start + s.Length).Select(s => (SyntaxKind?)s.Kind).FirstOrDefault();
    }

    [Theory]
    [InlineData("settings.json", SyntaxLanguage.Json)]
    [InlineData("analysis.IPYNB", SyntaxLanguage.Json)]
    [InlineData("log.jsonl", SyntaxLanguage.Json)]
    [InlineData("docker-compose.yml", SyntaxLanguage.Yaml)]
    [InlineData("Salandual.App.csproj", SyntaxLanguage.Xml)]
    [InlineData("App.axaml", SyntaxLanguage.Xml)]
    [InlineData("Salandual.slnx", SyntaxLanguage.Xml)]
    [InlineData("index.htm", SyntaxLanguage.Html)]
    [InlineData("README.md", SyntaxLanguage.Markdown)]
    [InlineData("site.css", SyntaxLanguage.Css)]
    [InlineData("app.js", SyntaxLanguage.JavaScript)]
    [InlineData("notes.txt", SyntaxLanguage.None)]
    [InlineData("Program.cs", SyntaxLanguage.None)]
    [InlineData("Makefile", SyntaxLanguage.None)]
    public void LanguageFor_GoesByTheExtension(string name, SyntaxLanguage expected) => Assert.Equal(expected, SyntaxRules.LanguageFor(name));

    [Fact]
    public void Json_KeysStringsNumbersKeywords_EscapesApart_AndCommentsOverLines()
    {
        var (pieces, state) = Colour(SyntaxLanguage.Json, "  \"name\": \"Árvíz \\\"x\\\" \\u00E1\", \"n\": -1.5e3, \"ok\": true, \"none\": null }");
        Assert.Equal(["\"name\":Key", "::Punctuation", "\"Árvíz :String", "\\\":Escape", "x:String", "\\\":Escape", " :String", "\\u00E1:Escape", "\":String", ",:Punctuation",
            "\"n\":Key", "::Punctuation", "-1.5e3:Number", ",:Punctuation", "\"ok\":Key", "::Punctuation", "true:Keyword", ",:Punctuation", "\"none\":Key", "::Punctuation", "null:Keyword", "}:Punctuation"], pieces);
        Assert.Equal(0, state);

        var (open, inside) = Colour(SyntaxLanguage.Json, "[1, /* a comment");
        Assert.Contains("/* a comment:Comment", open);
        Assert.NotEqual(0, inside);
        var (middle, still) = Colour(SyntaxLanguage.Json, "  \"not a key\": 2", inside);
        Assert.Equal(["  \"not a key\": 2:Comment"], middle);
        var (close, after) = Colour(SyntaxLanguage.Json, "end */ 3] // done", still);
        Assert.Equal(["end */:Comment", "3:Number", "]:Punctuation", "// done:Comment"], close);
        Assert.Equal(0, after);
    }

    [Fact]
    public void Yaml_PlainValuesAreStrings_AsInVsCode_NumbersOnlyWhenNumbers()
    {
        Assert.Equal(["# the shop:Comment"], Colour(SyntaxLanguage.Yaml, "# the shop").Pieces);
        Assert.Equal(["name:Key", "::Punctuation", "PR Build:String"], Colour(SyntaxLanguage.Yaml, "name: PR Build").Pieces);
        Assert.Equal(["image:Key", "::Punctuation", "\"webshop:2026.10\":String"], Colour(SyntaxLanguage.Yaml, "    image: \"webshop:2026.10\"").Pieces);
        Assert.Equal(["-:Punctuation", "8080:80:String"], Colour(SyntaxLanguage.Yaml, "  - 8080:80").Pieces);
        Assert.Equal(["interval:Key", "::Punctuation", "10:Number", "# seconds:Comment"], Colour(SyntaxLanguage.Yaml, "  interval: 10 # seconds").Pieces);
        Assert.Equal(["on:Key", "::Punctuation", "true:Keyword"], Colour(SyntaxLanguage.Yaml, "on: true").Pieces);
        Assert.Equal(["environment:Key", "::Punctuation", "&env:Marker"], Colour(SyntaxLanguage.Yaml, "    environment: &env").Pieces);
        Assert.Equal(["date:Key", "::Punctuation", "2026-10-15:String"], Colour(SyntaxLanguage.Yaml, "date: 2026-10-15").Pieces);
        Assert.Equal(["---:Punctuation"], Colour(SyntaxLanguage.Yaml, "---").Pieces);
        Assert.Equal(["{:Punctuation", "a:Key", "::Punctuation", "1:Number", ",:Punctuation", "b:Key", "::Punctuation", "x:String", "}:Punctuation"], Colour(SyntaxLanguage.Yaml, "{ a: 1, b: x }").Pieces);
        Assert.Equal(["types:Key", "::Punctuation", "[:Punctuation", "opened:String", ",:Punctuation", "closed:String", "]:Punctuation"], Colour(SyntaxLanguage.Yaml, "types: [opened, closed]").Pieces);

        Assert.Equal(["group:Key", "::Punctuation", "pr-${{ github.ref }}:String"], Colour(SyntaxLanguage.Yaml, "group: pr-${{ github.ref }}").Pieces);
        Assert.Equal(["'it''s':String"], Colour(SyntaxLanguage.Yaml, "- 'it''s'").Pieces.Skip(1).ToList());
    }

    [Fact]
    public void Yaml_BlockScalar_IsTextUntilALineIsNoLongerIndented()
    {
        var (start, state) = Colour(SyntaxLanguage.Yaml, "  script: |");
        Assert.Equal(["script:Key", "::Punctuation", "|:Punctuation"], start);
        Assert.NotEqual(0, state);

        var (body, inBlock) = Colour(SyntaxLanguage.Yaml, "    run: echo # not a comment", state);
        Assert.Equal(["run: echo # not a comment:String"], body);
        var (blank, stillIn) = Colour(SyntaxLanguage.Yaml, "", inBlock);
        Assert.Empty(blank);
        Assert.Equal(inBlock, stillIn);

        var (next, after) = Colour(SyntaxLanguage.Yaml, "  other: 1", stillIn);
        Assert.Equal(["other:Key", "::Punctuation", "1:Number"], next);
        Assert.Equal(0, after);
    }

    [Fact]
    public void Xml_TagsAttributesValues_CommentsAndTagsOverLines()
    {
        Assert.Equal(["<:Punctuation", "PackageReference:Tag", "Include:Attribute", "\"Markdig\":String", "/>:Punctuation"],
            Colour(SyntaxLanguage.Xml, "    <PackageReference Include=\"Markdig\" />").Pieces);
        Assert.Equal(["<:Punctuation", "a:Tag", ">:Punctuation", "&amp;:Escape", "</:Punctuation", "a:Tag", ">:Punctuation"], Colour(SyntaxLanguage.Xml, "<a>x &amp; y</a>").Pieces);
        Assert.Equal(["<?:Punctuation", "xml:Tag", "version:Attribute", "\"1.0\":String", "?>:Punctuation"], Colour(SyntaxLanguage.Xml, "<?xml version=\"1.0\"?>").Pieces);
        Assert.Equal(SyntaxKind.Escape, KindOf(SyntaxLanguage.Xml, "<A Value=\"[Format(&quot;{0}&quot;)]\" />", "&quot;"));

        var (open, inComment) = Colour(SyntaxLanguage.Xml, "<b/> <!-- starts");
        Assert.Equal("<!-- starts:Comment", open[^1]);
        var (rest, after) = Colour(SyntaxLanguage.Xml, "<not a=\"tag\"> -->", inComment);
        Assert.Equal(["<not a=\"tag\"> -->:Comment"], rest);
        Assert.Equal(0, after);

        var (tagStart, inTag) = Colour(SyntaxLanguage.Xml, "<Item Name=\"x\"");
        var (tagEnd, done) = Colour(SyntaxLanguage.Xml, "      Value='y'>text", inTag);
        Assert.Equal(["Value:Attribute", "'y':String", ">:Punctuation"], tagEnd);
        Assert.Equal(0, done);
        Assert.Equal("Item:Tag", tagStart[1]);
        Assert.Empty(Colour(SyntaxLanguage.Xml, "a < b").Pieces);
    }

    [Fact]
    public void Html_DoctypeAsVsCode_StyleIsCss_ScriptIsJavaScript_UntilTheirEndTags()
    {
        Assert.Equal(["<!:Punctuation", "DOCTYPE:Tag", "html:Attribute", ">:Punctuation"], Colour(SyntaxLanguage.Html, "<!DOCTYPE html>").Pieces);

        var (open, inScript) = Colour(SyntaxLanguage.Html, "<script>var a = \"<b>\";");
        Assert.Equal(["<:Punctuation", "script:Tag", ">:Punctuation", "var:Keyword", "\"<b>\":String"], open);
        var (middle, still) = Colour(SyntaxLanguage.Html, "if (a < 1) { x = '<p>'; /* a", inScript);
        Assert.Equal(["if:Keyword", "1:Number", "'<p>':String", "/* a:Comment"], middle);
        var (close, after) = Colour(SyntaxLanguage.Html, "comment */ go(); </script><p class=\"x\">", still);
        Assert.Equal(["comment */:Comment", "go:Function", "</:Punctuation", "script:Tag", ">:Punctuation", "<:Punctuation", "p:Tag", "class:Attribute", "\"x\":String", ">:Punctuation"], close);
        Assert.Equal(0, after);

        var (style, inStyle) = Colour(SyntaxLanguage.Html, "<style>body { margin: 0; }");
        Assert.Equal(["<:Punctuation", "style:Tag", ">:Punctuation", "body:Tag", "{:Punctuation", "margin:Key", "0:Number", ";:Punctuation", "}:Punctuation"], style);
        Assert.Equal(0, Colour(SyntaxLanguage.Html, "</style>", inStyle).State);
    }

    [Fact]
    public void Css_SelectorsPropertiesValues_AndCommentsOverLines()
    {
        Assert.Equal(["#help_page:Attribute", ".header:Attribute", "a:Tag", ":hover:Attribute", "h1:Tag", "{:Punctuation"],
            Colour(SyntaxLanguage.Css, "#help_page .header a:hover, h1 {").Pieces);
        var (decl, _) = Colour(SyntaxLanguage.Css, "  border: 2px solid #fff !important; color: rgba(0, 0, 0, .5); background: var(--bg);", 2);
        Assert.Equal(["border:Key", "2px:Number", "solid:String", "#fff:Number", "!important:Keyword", ";:Punctuation", "color:Key", "rgba:Function", "0:Number", "0:Number", "0:Number", ".5:Number", ";:Punctuation", "background:Key", "var:Function", "--bg:Key", ";:Punctuation"], decl);
        Assert.Equal(["@media:Keyword", "screen:Tag", "{:Punctuation"], Colour(SyntaxLanguage.Css, "@media screen {").Pieces);

        var (rule, depth) = Colour(SyntaxLanguage.Css, "@media print { p.note {");
        Assert.Contains("p:Tag", rule);
        Assert.Equal(SyntaxKind.Key, KindOf(SyntaxLanguage.Css, "  font-family: Verdana, sans-serif;", "font-family", depth));
        var (open, inComment) = Colour(SyntaxLanguage.Css, "a { } /* starts");
        Assert.Equal("/* starts:Comment", open[^1]);
        Assert.Equal(["still */:Comment", "b:Tag"], Colour(SyntaxLanguage.Css, "still */ b", inComment).Pieces);
    }

    [Fact]
    public void JavaScript_KeywordsStringsNumbersFunctions_TemplatesWithCode()
    {
        Assert.Equal(["function:Keyword", "Initialize:Function", "return:Keyword", "null:Keyword", "// done:Comment"], Colour(SyntaxLanguage.JavaScript, "function Initialize() { return null; } // done").Pieces);
        Assert.Equal("const:Keyword", Colour(SyntaxLanguage.JavaScript, "const s = 'it\\'s';").Pieces[0]);
        Assert.Equal(SyntaxKind.Escape, KindOf(SyntaxLanguage.JavaScript, "const s = 'it\\'s';", "\\'"));
        Assert.Equal(SyntaxKind.Number, KindOf(SyntaxLanguage.JavaScript, "x = 0x1F + 1.5e3;", "0x1F"));
        Assert.Null(KindOf(SyntaxLanguage.JavaScript, "var a1 = b2;", "1"));

        var (template, state) = Colour(SyntaxLanguage.JavaScript, "return `<p class=\"${cls ? 'a' : f(1)}\">");
        Assert.Equal(["return:Keyword", "`<p class=\":String", "${:Punctuation", "'a':String", "f:Function", "1:Number", "}:Punctuation", "\">:String"], template);
        Assert.NotEqual(0, state);
        Assert.Equal(["</p>`:String"], Colour(SyntaxLanguage.JavaScript, "</p>`;", state).Pieces);
    }

    [Fact]
    public void Markdown_HeadingsListsInlineMarks_NestedAsInVsCode()
    {
        Assert.Equal(["## Getting started:Heading"], Colour(SyntaxLanguage.Markdown, "## Getting started").Pieces);
        Assert.Empty(Colour(SyntaxLanguage.Markdown, "#hashtag").Pieces);
        Assert.Equal(["-:Marker", "[x]:Marker", "`F4`:Code"], Colour(SyntaxLanguage.Markdown, "- [x] Editor (`F4`)").Pieces);
        Assert.Equal(["2.:Marker", "**:Strong", "bold:Strong", "**:Strong", "*:Emphasis", "it:Emphasis", "*:Emphasis", "[the plan](docs/08-plan.md):Link"],
            Colour(SyntaxLanguage.Markdown, "2. A **bold** and *it* see [the plan](docs/08-plan.md)").Pieces);
        Assert.Equal(["> Viewing never runs anything.:Quote"], Colour(SyntaxLanguage.Markdown, "> Viewing never runs anything.").Pieces);
        Assert.Equal(["`F3`:Code"], Colour(SyntaxLanguage.Markdown, "| `F3` | View |").Pieces);
        Assert.Empty(Colour(SyntaxLanguage.Markdown, "snake_case_name and 2 * 3 * 4").Pieces);

        Assert.Equal(SyntaxKind.Link, KindOf(SyntaxLanguage.Markdown, "- **Menu (mockup in [README](design/README.md)):** text", "[README]"));
        Assert.Equal(SyntaxKind.Code, KindOf(SyntaxLanguage.Markdown, "**Copy `F5` / Move**", "`F5`"));
        Assert.Equal(SyntaxKind.Strong, KindOf(SyntaxLanguage.Markdown, "**Copy `F5` / Move**", "/ Move"));
        Assert.Equal(SyntaxKind.Link, KindOf(SyntaxLanguage.Markdown, "### Rendering ([unicode.png](spikes/unicode.png))", "[unicode"));
        Assert.Equal(SyntaxKind.Heading, KindOf(SyntaxLanguage.Markdown, "### Rendering ([unicode.png](spikes/unicode.png))", "Rendering"));
        Assert.Equal(SyntaxKind.Tag, KindOf(SyntaxLanguage.Markdown, "Press <kbd>F3</kbd> to view", "kbd"));
        Assert.Equal(SyntaxKind.Tag, KindOf(SyntaxLanguage.Markdown, "<details><summary>More</summary>", "details"));
    }

    [Fact]
    public void Markdown_CodeBlocks_InTheirLanguage_PlainWithoutOne()
    {
        var (fence, inCode) = Colour(SyntaxLanguage.Markdown, "```python");
        Assert.Equal(["```python:Punctuation"], fence);
        var (code, still) = Colour(SyntaxLanguage.Markdown, "# not a heading: a comment", inCode);
        Assert.Equal(["# not a heading: a comment:Comment"], code);
        Assert.Equal(["def:Keyword", "f:Function", "\"hi\":String"], Colour(SyntaxLanguage.Markdown, "def f(): return \"hi\"", still).Pieces.Where(p => p != "return:Keyword").ToList());
        var (end, after) = Colour(SyntaxLanguage.Markdown, "```", still);
        Assert.Equal(["```:Punctuation"], end);
        Assert.Equal(0, after);

        var json = Colour(SyntaxLanguage.Markdown, "```jsonc").State;
        json = Colour(SyntaxLanguage.Markdown, "{ \"a\": 1, /* x", json).State;
        Assert.Equal(["y */:Comment", "}:Punctuation"], Colour(SyntaxLanguage.Markdown, "y */ }", json).Pieces);

        var plain = Colour(SyntaxLanguage.Markdown, "```").State;
        Assert.Empty(Colour(SyntaxLanguage.Markdown, "# not a heading", plain).Pieces);
        var shortFence = Colour(SyntaxLanguage.Markdown, "``", plain);
        Assert.Empty(shortFence.Pieces);
        Assert.Equal(0, Colour(SyntaxLanguage.Markdown, "```", shortFence.State).State);
    }

    [Fact]
    public async Task States_AreExactOnceRead_AndGuessedPastTheLimit()
    {
        var text = new StringBuilder("{\n /* a long comment\n");
        for (var i = 3; i < 1000; i++)
        {
            text.Append("   \"key").Append(i).Append("\": ").Append(i).Append(",\n");
        }

        text.Append(" */ \"end\": 1\n}\n");
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        var lines = new TextLines(new CachedBytes(new MemoryByteSource(bytes)), TextEncodings.Find(TextEncodings.Utf8)!, 0);
        long StartOf(int line)
        {
            var at = 0L;
            for (var i = 1; i < line; i++)
            {
                at = lines.LineAt(at).Next;
            }

            return at;
        }

        using var exact = new SyntaxStates(lines, SyntaxLanguage.Json, exactLimit: long.MaxValue);
        await exact.Completion;
        Assert.True(exact.IsExact);
        Assert.NotEqual(0, exact.StateAt(StartOf(600)));
        Assert.Equal(0, exact.StateAt(StartOf(1001)));
        Assert.Equal(0, exact.StateAt(StartOf(2)));

        using var guessed = new SyntaxStates(lines, SyntaxLanguage.Json, exactLimit: 100);
        Assert.False(guessed.IsExact);
        Assert.Equal(0, guessed.StateAt(StartOf(600)));
        Assert.NotEqual(0, guessed.StateAt(StartOf(10)));
    }

    [Fact]
    public void LargestFile_ComesFromTheRow_OrTheViewersDefault()
    {
        var v = new ViewerSettings();
        Assert.Equal(500L * 1024 * 1024, v.LargestBytes("photo.jpg", ViewerKind.Image));
        Assert.Equal(10L * 1024 * 1024, v.LargestBytes("README.md", ViewerKind.Markdown));
        Assert.Equal(200L * 1024 * 1024, v.LargestBytes("a.ipynb", ViewerKind.Notebook));
        Assert.Equal(0, v.LargestBytes("film.mkv", ViewerKind.Media));
        Assert.Equal(0, v.LargestBytes("notes.txt", ViewerKind.Text));

        var masks = ViewerSettings.DefaultMasks();
        var md = masks.FindIndex(m => m.View == ViewerKind.Markdown);
        masks[md] = masks[md] with { LargestMB = 0 };
        var image = masks.FindIndex(m => m.View == ViewerKind.Image);
        masks[image] = masks[image] with { LargestMB = 2000 };
        v = v with { Masks = masks };
        Assert.Equal(0, v.LargestBytes("README.md", ViewerKind.Markdown));
        Assert.Equal(2000L * 1024 * 1024, v.LargestBytes("photo.jpg", ViewerKind.Image));
        Assert.Equal(10L * 1024 * 1024, v.LargestBytes("photo.jpg", ViewerKind.Markdown));

        var normalized = (v with { Masks = [.. masks.Select(m => m with { LargestMB = m.LargestMB is null ? null : -5 })] }).Normalize();
        Assert.Equal(0, normalized.Masks[md].LargestMB);
        Assert.Null(normalized.Masks[masks.FindIndex(m => m.View == ViewerKind.Media)].LargestMB);
    }
}
