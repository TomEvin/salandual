// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/DataTreeTests.cs

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Salandual.Core.Viewer;

namespace Salandual.Core.Tests;

public sealed class DataTreeTests
{
    private const string Settings = """
        {
          "$schema": "https://salandual.example/schema/settings-3.json",
          "schemaVersion": 3,
          "viewers": {
            "close": "EscOrAltF4",
            "masks": [
              { "mask": "*.md;*.markdown", "viewer": "Markdown" },
              { "mask": "*.ipynb", "viewer": "Notebook" }
            ]
          },
          "editors": { "fullUpToMB": 20, "on": true, "none": null, "comment": "Árvíztűrő \"tükör\" 東京\n2" },
          "a b": 1
        }
        """;

    private static DataTree Read(string text, DataFormat format, bool allowComments = false) => DataTree.Read(text, format, allowComments, TestContext.Current.CancellationToken);

    private static DataNode Find(DataTree tree, string path) => tree.Walk().Single(n => tree.PathOf(n, DataPathStyle.Dollar) == path);

    [Fact]
    public void Json_ReadsKeysValuesAndLines()
    {
        var tree = Read(Settings, DataFormat.Json);

        Assert.Null(tree.Problem);
        Assert.Equal(DataKind.Object, tree.Root.Kind);
        Assert.Equal(["$schema", "schemaVersion", "viewers", "editors", "a b"], tree.Root.Children.Select(c => c.Name));
        var viewer = Find(tree, "$.viewers.masks[1].viewer");
        Assert.Equal("Notebook", tree.Value(viewer));
        Assert.Equal(DataKind.String, viewer.Kind);
        Assert.Equal(8, viewer.Line);
        Assert.Equal(1, viewer.Parent!.Index);
        Assert.Equal("3", tree.Value(Find(tree, "$.schemaVersion")));
        Assert.Equal(DataKind.Number, Find(tree, "$.schemaVersion").Kind);
        Assert.Equal(DataKind.Boolean, Find(tree, "$.editors.on").Kind);
        Assert.Equal(DataKind.Null, Find(tree, "$.editors.none").Kind);
        Assert.Equal("Árvíztűrő \"tükör\" 東京\n2", tree.Value(Find(tree, "$.editors.comment")));
        Assert.Equal("\"Árvíztűrő \"tükör\" 東京⏎2\"", tree.Shown(Find(tree, "$.editors.comment")));
        Assert.Equal(12, tree.Values);
        Assert.Equal(4, tree.Levels);
    }

    [Fact]
    public void Paths_InEachStyle_AndXPath()
    {
        var tree = Read(Settings, DataFormat.Json);
        var viewer = Find(tree, "$.viewers.masks[1].viewer");

        Assert.Equal("viewers.masks[1].viewer", tree.PathOf(viewer, DataPathStyle.Dotted));
        Assert.Equal("/viewers/masks/1/viewer", tree.PathOf(viewer, DataPathStyle.Slash));
        Assert.Equal("$['a b']", tree.PathOf(tree.Root.Children[^1], DataPathStyle.Dollar));
        Assert.Equal("$.$schema", tree.PathOf(tree.Root.Children[0], DataPathStyle.Dollar));
        Assert.Equal("$", tree.PathOf(tree.Root, DataPathStyle.Dollar));
        Assert.Equal("/", tree.PathOf(tree.Root, DataPathStyle.Slash));
        Assert.Equal("", tree.PathOf(tree.Root, DataPathStyle.Dotted));

        var slash = Read("""{ "a/b": { "c~d": 1 } }""", DataFormat.Json);
        Assert.Equal("/a~1b/c~0d", slash.PathOf(slash.Root.Children[0].Children[0], DataPathStyle.Slash));
    }

    [Fact]
    public void CopyTheBranch_IsTheFilesOwnText_MovedLeft()
    {
        var tree = Read(Settings, DataFormat.Json);
        var masks = Find(tree, "$.viewers.masks");

        Assert.Equal("""
            [
              { "mask": "*.md;*.markdown", "viewer": "Markdown" },
              { "mask": "*.ipynb", "viewer": "Notebook" }
            ]
            """.ReplaceLineEndings("\n"), tree.Branch(masks).ReplaceLineEndings("\n"));
        Assert.Equal("""{ "mask": "*.ipynb", "viewer": "Notebook" }""", tree.Branch(masks.Children[1]));
        Assert.Equal("Notebook", tree.CopyValue(Find(tree, "$.viewers.masks[1].viewer")));
        Assert.Equal(tree.Branch(masks), tree.CopyValue(masks));
    }

    [Fact]
    public void Json_AMistake_GivesItsLineColumnAndMessage_InsteadOfATree()
    {
        var tree = Read("{\n  \"a\": 1\n  \"b\": 2\n}", DataFormat.Json);

        Assert.NotNull(tree.Problem);
        Assert.Equal(3, tree.Problem!.Line);
        Assert.Contains("comma", tree.Problem.Message, StringComparison.OrdinalIgnoreCase);

        const string withComment = "{ // the size\n \"a\": 1, }";
        Assert.NotNull(Read(withComment, DataFormat.Json).Problem);
        var allowed = Read(withComment, DataFormat.Json, allowComments: true);
        Assert.Null(allowed.Problem);
        Assert.Equal("1", allowed.Value(allowed.Root.Children[0]));
        Assert.Equal(2, allowed.Root.Children[0].Line);
    }

    [Fact]
    public void JsonLines_OneRecordPerLine()
    {
        var tree = Read("{\"id\":1,\"name\":\"a\"}\r\n\r\n{\"id\":2}\r\n[3,4]\r\n", DataFormat.JsonLines);

        Assert.Null(tree.Problem);
        Assert.Equal(DataKind.Array, tree.Root.Kind);
        Assert.Equal(3, tree.Root.Count);
        Assert.Equal([1, 3, 4], tree.Root.Children.Select(c => c.Line));
        Assert.Equal("$[1].id", tree.PathOf(tree.Root.Children[1].Children[0], DataPathStyle.Dollar));
        Assert.Equal("2", tree.Value(tree.Root.Children[1].Children[0]));
        Assert.Equal("[3,4]", tree.Branch(tree.Root.Children[2]));

        var bad = Read("{\"id\":1}\n{\"id\" 2}\n", DataFormat.JsonLines);
        Assert.Equal(2, bad.Problem!.Line);
    }

    [Fact]
    public void Yaml_KeysListsTypesAndLines()
    {
        const string yaml = """
            name: web
            port: 8080
            debug: false
            empty:
            quoted: "123"
            base: &base
              image: nginx
              tags: [a, b]
            copy: *base
            services:
              - name: one
                ratio: 0.5
              - two
            """;
        var tree = Read(yaml, DataFormat.Yaml);

        Assert.Null(tree.Problem);
        Assert.Equal(["name", "port", "debug", "empty", "quoted", "base", "copy", "services"], tree.Root.Children.Select(c => c.Name));
        Assert.Equal(DataKind.Number, Find(tree, "$.port").Kind);
        Assert.Equal(DataKind.Boolean, Find(tree, "$.debug").Kind);
        Assert.Equal(DataKind.Null, Find(tree, "$.empty").Kind);
        Assert.Equal(DataKind.String, Find(tree, "$.quoted").Kind);
        Assert.Equal("123", tree.Value(Find(tree, "$.quoted")));
        var alias = Find(tree, "$.copy");
        Assert.Equal(DataKind.Alias, alias.Kind);
        Assert.Equal("*base", tree.Value(alias));
        Assert.Equal(0, alias.Count);
        Assert.Equal("b", tree.Value(Find(tree, "$.base.tags[1]")));
        Assert.Equal(12, Find(tree, "$.services[0].ratio").Line);
        Assert.Equal("two", tree.Value(Find(tree, "$.services[1]")));
        Assert.Equal("image: nginx\ntags: [a, b]", tree.Branch(Find(tree, "$.base")).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Yaml_SeveralDocuments_AComplexKey_AndAMistake()
    {
        var docs = Read("a: 1\n---\nb: 2\n", DataFormat.Yaml);
        Assert.Equal(DataKind.Array, docs.Root.Kind);
        Assert.Equal("$[1].b", docs.PathOf(docs.Root.Children[1].Children[0], DataPathStyle.Dollar));

        var complex = Read("? [x, y]\n: both\nz: 3\n", DataFormat.Yaml);
        Assert.Null(complex.Problem);
        Assert.Equal(["[…]", "z"], complex.Root.Children.Select(c => c.Name));
        Assert.Equal("both", complex.Value(complex.Root.Children[0]));

        var bad = Read("a: 1\nb: [1, 2\nc: 3\n", DataFormat.Yaml);
        Assert.NotNull(bad.Problem);
    }

    [Fact]
    public void Yaml_ManyAliases_AreNotExpanded()
    {
        var yaml = new StringBuilder("a0: &a0 [x, x, x, x, x, x, x, x, x]\n");
        for (var i = 1; i <= 8; i++)
        {
            yaml.Append(System.Globalization.CultureInfo.InvariantCulture, $"a{i}: &a{i} [")
                .Append(string.Join(", ", Enumerable.Repeat($"*a{i - 1}", 9))).Append("]\n");
        }

        var clock = Stopwatch.StartNew();
        var tree = Read(yaml.ToString(), DataFormat.Yaml);

        Assert.Null(tree.Problem);
        Assert.True(tree.Walk().Count() < 200);
        Assert.True(clock.ElapsedMilliseconds < 2000);
    }

    [Fact]
    public void Xml_ElementsAttributesTextAndXPath()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable />
              </PropertyGroup>
              <ItemGroup>
                <Item Include="a.cs" note="1 &gt; 0" />
                <Item Include="b.cs">text <b>bold</b> more</Item>
              </ItemGroup>
            </Project>
            """;
        var tree = Read(xml, DataFormat.Xml);

        Assert.Null(tree.Problem);
        Assert.Equal(DataKind.Element, tree.Root.Kind);
        Assert.Equal("Project", tree.Root.Name);
        var attribute = tree.Root.Children[0];
        Assert.Equal(DataKind.Attribute, attribute.Kind);
        Assert.Equal("/Project/@Sdk", tree.PathOf(attribute, DataPathStyle.Dollar));
        var framework = tree.Root.Children[1].Children[0];
        Assert.Equal("net10.0", tree.Value(framework));
        Assert.False(framework.IsBranch);
        Assert.Equal(4, framework.Line);
        Assert.Equal("", tree.Value(tree.Root.Children[1].Children[1]));
        var items = tree.Root.Children[2].Children;
        Assert.Equal("/Project/ItemGroup/Item[2]", tree.PathOf(items[1], DataPathStyle.Dollar));
        Assert.Equal("/Project/ItemGroup/Item[1]/@note", tree.PathOf(items[0].Children[1], DataPathStyle.Dollar));
        Assert.Equal("1 > 0", tree.Value(items[0].Children[1]));
        Assert.Equal([DataKind.Attribute, DataKind.Text, DataKind.Element, DataKind.Text], items[1].Children.Select(c => c.Kind));
        Assert.Equal("/Project/ItemGroup/Item[2]/text()", tree.PathOf(items[1].Children[1], DataPathStyle.Dollar));
        Assert.Equal("""<Item Include="a.cs" note="1 &gt; 0" />""", tree.Branch(items[0]));
        Assert.Equal("""
            <PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
              <Nullable />
            </PropertyGroup>
            """.ReplaceLineEndings("\n"), tree.Branch(tree.Root.Children[1]).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Xml_NothingIsFetchedFromOutside_AndAMistakeSaysWhere()
    {
        var file = Path.Combine(Path.GetTempPath(), "salandual-tree-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "SECRET");
        try
        {
            var xml = $"""<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e SYSTEM "{new Uri(file).AbsoluteUri}">]><x>&e;</x>""";
            var tree = Read(xml, DataFormat.Xml);
            Assert.DoesNotContain(tree.Walk(), n => !n.IsBranch && tree.Value(n).Contains("SECRET", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(file);
        }

        var bad = Read("<a>\n  <b>\n</a>", DataFormat.Xml);
        Assert.Equal(3, bad.Problem!.Line);
    }

    [Fact]
    public void Find_LooksInKeysAndValues()
    {
        var tree = Read(Settings, DataFormat.Json);

        var found = tree.Find(new Regex("notebook", RegexOptions.IgnoreCase), TestContext.Current.CancellationToken);

        Assert.Equal(["$.viewers.masks[1].viewer"], found.Select(n => tree.PathOf(n, DataPathStyle.Dollar)));
        Assert.Equal(["$.viewers.masks"], tree.Find(new Regex("^masks$"), TestContext.Current.CancellationToken).Select(n => tree.PathOf(n, DataPathStyle.Dollar)));
        var keys = tree.Find(new Regex("MASK", RegexOptions.IgnoreCase), TestContext.Current.CancellationToken);
        Assert.Equal(["$.viewers.masks", "$.viewers.masks[0].mask", "$.viewers.masks[1].mask"], keys.Select(n => tree.PathOf(n, DataPathStyle.Dollar)));
        Assert.True(keys[1].Offset < keys[2].Offset);
    }

    [Fact]
    public void ASolutionFile_slnx_IsATree()
    {
        var tree = Read(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Salandual.slnx")), DataTree.FormatFor("Salandual.slnx")!.Value);
        Assert.Contains(tree.Walk(), n => tree.PathOf(n, DataPathStyle.Dollar) == "/Solution/Folder[1]/@Name" && tree.Value(n) == "/src/");
    }

    [Fact]
    public void FormatFor_TheTreesKindsOfFile()
    {
        Assert.Equal(DataFormat.Json, DataTree.FormatFor("settings.JSON"));
        Assert.Equal(DataFormat.Json, DataTree.FormatFor("tsconfig.jsonc"));
        Assert.Equal(DataFormat.JsonLines, DataTree.FormatFor("log.jsonl"));
        Assert.Equal(DataFormat.Yaml, DataTree.FormatFor("compose.yml"));
        Assert.Equal(DataFormat.Xml, DataTree.FormatFor("App.csproj"));
        Assert.Equal(DataFormat.Xml, DataTree.FormatFor("Salandual.slnx"));
        Assert.Null(DataTree.FormatFor("notes.txt"));
    }

    [Fact]
    public void ABigFile_IsReadQuickly()
    {
        var json = new StringBuilder("[\n");
        for (var i = 0; i < 100_000; i++)
        {
            json.Append(System.Globalization.CultureInfo.InvariantCulture, $"  {{ \"id\": {i}, \"name\": \"item {i}\", \"tags\": [\"a\", \"b\"], \"ok\": true }},\n");
        }

        json.Append("  {}\n]");
        var clock = Stopwatch.StartNew();
        var tree = Read(json.ToString(), DataFormat.Json);

        Assert.Null(tree.Problem);
        Assert.Equal(100_001, tree.Root.Count);
        Assert.Equal(500_000, tree.Values);
        Assert.Equal(100_002, tree.Root.Children[^1].Line);
        Assert.True(clock.ElapsedMilliseconds < 5000, $"{clock.ElapsedMilliseconds} ms");
    }
}
