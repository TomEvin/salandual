// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/TreeViewerTests.cs

using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Viewer;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class TreeViewerTests : IDisposable
{
    private const string Settings = """
        {
          "schemaVersion": 3,
          "viewers": {
            "close": "EscOrAltF4",
            "masks": [
              { "mask": "*.md", "viewer": "Markdown" },
              { "mask": "*.ipynb", "viewer": "Notebook" }
            ]
          },
          "editors": { "fullUpToMB": 20 }
        }
        """;

    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-tree-").FullName;
    private readonly TextClipboard _clip = new();

    public TreeViewerTests()
    {
        File.WriteAllText(Path.Join(_dir, "a-settings.json"), Settings, new UTF8Encoding(false));
        File.WriteAllText(Path.Join(_dir, "b-broken.json"), "{\n  \"a\": 1\n  \"b\": 2\n}\n");
        File.WriteAllText(Path.Join(_dir, "c-compose.yaml"), "services:\n  web:\n    image: nginx\n    ports: [80, 443]\n");
        File.WriteAllText(Path.Join(_dir, "d-app.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open(string focus)
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.FileClipboardAccess = _clip;
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir, focus);
        await w.RightPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(TopLevel w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static List<string> Status(ViewerWindow v) => [.. v.StatusCells.Select(c => (c.Child as TextBlock)?.Text ?? "")];

    private static async Task<ViewerWindow> Viewer(MainWindow w, RawInputModifiers mods = RawInputModifiers.None)
    {
        Press(w, Key.F3, mods);
        await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Document is not null);
        var v = w.Viewers[^1];
        v.Width = 1100;
        v.Height = 700;
        await v.Loading;
        v.CaptureRenderedFrame();
        return v;
    }

    private static string PathOf(ViewerWindow v) => v.SelectedPath ?? "";

    [AvaloniaFact]
    public async Task F3_OnJson_OpensTheTree_FirstLevelUnfolded_WithThePathOnTheStatusLine()
    {
        var w = await Open("a-settings.json");
        try
        {
            var v = await Viewer(w);

            Assert.True(v.IsTree);
            Assert.Equal(["Tree", "Text", "Hex"], v.ModeOrder);
            var rows = v.TreeView.Rows.Select(r => v.Tree!.PathOf(r.Node, DataPathStyle.Dollar)).ToList();
            Assert.Equal(["$", "$.schemaVersion", "$.viewers", "$.viewers.close", "$.viewers.masks", "$.editors", "$.editors.fullUpToMB"], rows);
            Assert.Equal("$", PathOf(v));
            var status = Status(v);
            Assert.Equal("$", status[0]);
            Assert.Contains("7 values · 4 levels", status);
            Assert.Contains("JSON · valid", status);
            Assert.Equal("1 / 2 JSON files", v.Counter.Text);

            Press(v, Key.Down);
            Press(v, Key.Down);
            Press(v, Key.Down);
            Press(v, Key.Down);
            Assert.Equal("$.viewers.masks", PathOf(v));
            Assert.Equal("$.viewers.masks", Status(v)[0]);
            Press(v, Key.Right);
            Assert.True(v.TreeView.IsOpen(v.TreeView.Selected!));
            Press(v, Key.Right);
            Assert.Equal("$.viewers.masks[0]", PathOf(v));
            Press(v, Key.Left);
            Assert.Equal("$.viewers.masks", PathOf(v));
            Press(v, Key.Left);
            Assert.False(v.TreeView.IsOpen(v.TreeView.Selected!));
            Press(v, Key.End);
            Assert.Equal("$.editors.fullUpToMB", PathOf(v));
            Press(v, Key.Home);
            Assert.Equal("$", PathOf(v));
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Enter_ShowsTheItemInText_AtItsLine_AndF6_ComesBack()
    {
        var w = await Open("a-settings.json");
        try
        {
            var v = await Viewer(w);
            var close = v.Tree!.Walk().Single(n => n.Name == "close");
            v.TreeView.SelectNode(close);

            Press(v, Key.Enter);
            await Until(() => !v.IsTree && Status(v).Count > 0 && Status(v)[0].StartsWith("Ln 4,", StringComparison.Ordinal));

            Assert.False(v.IsTree);
            Press(v, Key.F6);
            Assert.True(v.IsTree);
            Assert.Equal("$.viewers.close", PathOf(v));
            Press(v, Key.F4);
            await Until(() => v.IsHex);
            Press(v, Key.F6);
            Assert.True(v.IsTree);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Enter_FoldsABranch_CtrlEnterShowsText_AndTheFoldingKeys()
    {
        var w = await Open("a-settings.json");
        try
        {
            var v = await Viewer(w);
            var view = v.TreeView;
            var masks = v.Tree!.Walk().Single(n => n.Name == "masks");
            view.SelectNode(masks);

            Press(v, Key.Enter);
            Assert.True(view.IsOpen(masks));
            Assert.True(v.IsTree);
            Press(v, Key.Enter);
            Assert.False(view.IsOpen(masks));

            Press(v, Key.Add);
            Assert.True(view.IsOpen(masks));
            Press(v, Key.Subtract);
            Assert.False(view.IsOpen(masks));
            Press(v, Key.Multiply);
            Assert.All(masks.Children, c => Assert.True(view.IsOpen(c)));

            Press(v, Key.K, RawInputModifiers.Control);
            Press(v, Key.D0, RawInputModifiers.Control);
            Assert.Equal(4, view.Rows.Count);
            Press(v, Key.K, RawInputModifiers.Control);
            Press(v, Key.J, RawInputModifiers.Control);
            Assert.Equal(v.Tree.Walk().Count(), view.Rows.Count);

            view.SelectNode(masks);
            Press(v, Key.Enter, RawInputModifiers.Control);
            await Until(() => !v.IsTree && Status(v)[0].StartsWith("Ln 5,", StringComparison.Ordinal));
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Copy_TheValue_ThePath_AndTheBranch()
    {
        var w = await Open("a-settings.json");
        try
        {
            var v = await Viewer(w);
            var tree = v.Tree!;
            v.TreeView.SelectNode(tree.Walk().Single(n => n.Name == "close"));

            Press(v, Key.C, RawInputModifiers.Control);
            Assert.Equal("EscOrAltF4", _clip.Text);

            var menu = v.BuildMoreMenu();
            var headers = menu.Items.OfType<MenuItem>().Select(i => i.Header as string ?? i.Tag as string).ToList();
            Assert.Contains("Copy the path: $.viewers.close", headers);
            Assert.Contains("Copy the branch as JSON", headers);
            Assert.Contains("Show in Text", headers);
            Assert.Contains("Expand everything in it", headers);
            Assert.DoesNotContain("Copy the value", headers);
            Assert.DoesNotContain("Expand all", headers);
            Assert.DoesNotContain("Collapse all", headers);
            v.CopyTreePath();
            Assert.Equal("$.viewers.close", _clip.Text);

            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { TreePath = DataPathStyle.Slash } });
            await Until(() => Status(v)[0] == "/viewers/close");

            v.TreeView.Reveal(tree.Walk().Single(n => n.Name == "masks"));
            v.CopyTreeBranch();
            Assert.StartsWith("[\r\n  { \"mask\": \"*.md\"", _clip.Text, StringComparison.Ordinal);
            Assert.EndsWith("\r\n]", _clip.Text, StringComparison.Ordinal);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Find_InKeysAndValues_UnfoldsToTheMatch()
    {
        var w = await Open("a-settings.json");
        try
        {
            var v = await Viewer(w);
            Press(v, Key.F, RawInputModifiers.Control);
            Assert.True(v.FindBox.IsFocused);
            v.FindBox.Text = "notebook";
            await v.FindAsync(forward: true, from: 0);

            Assert.Equal("$.viewers.masks[1].viewer", PathOf(v));
            Assert.True(v.TreeView.IsOpen(v.Tree!.Walk().Single(n => n.Name == "masks")));
            Assert.Equal("1 of 1", v.HitsText);

            v.FindBox.Text = "mask";
            await v.FindAsync(forward: true, from: 0);
            Assert.Equal("1 of 3", v.HitsText);
            await v.FindAsync(forward: true);
            Assert.Equal("2 of 3", v.HitsText);
            Assert.Equal("$.viewers.masks[0].mask", PathOf(v));
            await v.FindAsync(forward: false);
            Assert.Equal("1 of 3", v.HitsText);

            Assert.True(v.RegexFlag.IsVisible);
            v.RegexFlag.IsChecked = true;
            v.FindBox.Text = "^mask$";
            await v.FindAsync(forward: true, from: 0);
            Assert.Equal("1 of 2", v.HitsText);
            v.FindBox.Text = "(";
            await v.FindAsync(forward: true, from: 0);
            Assert.Equal("Can't search for this", v.HitsText);
            v.RegexFlag.IsChecked = false;

            Press(v, Key.Escape);
            Assert.Null(v.TreeView.Matches);
            Assert.True(v.TreeView.IsFocused);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task AFileWithAMistake_OpensInText_AtTheMistake_WithABar()
    {
        var w = await Open("b-broken.json");
        try
        {
            var v = await Viewer(w);
            await Until(() => v.TreeProblem is not null && v.IsOverLimit);

            Assert.False(v.IsTree);
            Assert.Contains("line 3", v.LimitText, StringComparison.Ordinal);
            Assert.StartsWith("Shown as Text: the JSON has a mistake", v.LimitText, StringComparison.Ordinal);
            await Until(() => Status(v)[0].StartsWith("Ln 3,", StringComparison.Ordinal));
            Assert.Contains("Tree", v.ModeOrder);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task YamlAndXml_OpenAsTrees_XmlWithXPath()
    {
        var w = await Open("c-compose.yaml");
        try
        {
            var v = await Viewer(w);
            Assert.True(v.IsTree);
            Assert.Contains("YAML · valid", Status(v));
            Assert.Equal("1 / 1 YAML files", v.Counter.Text);
            v.TreeView.ExpandTo(int.MaxValue);
            Assert.Contains(v.TreeView.Rows, r => v.Tree!.PathOf(r.Node, DataPathStyle.Dollar) == "$.services.web.ports[1]");
            v.TreeView.CollapseAll();
            Assert.Equal(2, v.TreeView.Rows.Count);
            v.Close();

            await w.LeftPanel.Panel.NavigateAsync(_dir, "d-app.csproj");
            w.LeftPanel.View.Focus();
            var x = await Viewer(w);
            Assert.True(x.IsTree);
            x.TreeView.ExpandTo(int.MaxValue);
            x.TreeView.SelectNode(x.Tree!.Walk().Single(n => n.Name == "TargetFramework"));
            Assert.Equal("/Project/PropertyGroup/TargetFramework", Status(x)[0]);
            Assert.Contains("XML · valid", Status(x));
            Press(x, Key.C, RawInputModifiers.Control);
            Assert.Equal("net10.0", _clip.Text);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task AltF3_OpensText_AndF6_TheTree()
    {
        var w = await Open("a-settings.json");
        try
        {
            var v = await Viewer(w, RawInputModifiers.Alt);
            Assert.False(v.IsTree);
            Assert.Equal(["Tree", "Text", "Hex"], v.ModeOrder);

            Press(v, Key.F6);
            await Until(() => v.IsTree && v.Tree is not null);
            Assert.Equal("$", Status(v)[0]);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task OverTheLimit_OpensText_WithOpenTheTreeAnyway()
    {
        var big = new StringBuilder("[\n");
        for (var i = 0; i < 30_000; i++)
        {
            big.Append(System.Globalization.CultureInfo.InvariantCulture, $"  {{ \"id\": {i}, \"name\": \"item number {i}\" }},\n");
        }

        File.WriteAllText(Path.Join(_dir, "e-big.json"), big.Append("  {}\n]").ToString());
        var w = await Open("e-big.json");
        try
        {
            w.Services.Settings.Update(s => s with
            {
                Viewers = s.Viewers with { Masks = [.. s.Viewers.Masks.Select(m => m.View == ViewerKind.Tree && m.Mask.Contains(".json", StringComparison.Ordinal) ? m with { LargestMB = 1 } : m)] },
            });
            var v = await Viewer(w);

            Assert.False(v.IsTree);
            Assert.True(v.IsOverLimit);
            Assert.Contains("the Tree opens files up to 1", v.LimitText, StringComparison.Ordinal);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Capture_TheTree()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var w = await Open("a-settings.json");
        try
        {
            var v = await Viewer(w);
            var tree = v.Tree!;
            v.TreeView.Reveal(tree.Walk().Single(n => tree.PathOf(n, DataPathStyle.Dollar) == "$.viewers.masks[1].viewer"));
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            v.CaptureRenderedFrame()!.Save(Path.Combine(dir, "tree-json.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            v.Close();

            await w.LeftPanel.Panel.NavigateAsync(_dir, "d-app.csproj");
            w.LeftPanel.View.Focus();
            var x = await Viewer(w);
            x.TreeView.ExpandTo(int.MaxValue);
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            x.CaptureRenderedFrame()!.Save(Path.Combine(dir, "tree-xml.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            x.Close();

            await w.LeftPanel.Panel.NavigateAsync(_dir, "b-broken.json");
            w.LeftPanel.View.Focus();
            var b = await Viewer(w);
            await Until(() => b.IsOverLimit);
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            b.CaptureRenderedFrame()!.Save(Path.Combine(dir, "tree-mistake.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally
        {
            w.CloseNow();
        }
    }

    private sealed class TextClipboard : IClipboardAccess
    {
        public string? Text { get; private set; }

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut) => true;

        public bool SetText(string text)
        {
            Text = text;
            SequenceNumber++;
            return true;
        }

        public bool SetImage(byte[] dib, byte[]? png) => true;

        public void Clear()
        {
        }

        public ClipboardContent? Read() => new();
    }
}
