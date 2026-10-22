// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/EditorToolsTests.cs

using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Editor;
using Salandual.App.Options;
using Salandual.App.Viewer;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class EditorToolsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-editor2b-").FullName;

    public EditorToolsTests()
    {
        Write("a-broken.json", "{\n  \"theme\": {\n    \"accent\": 1\n  }\n  \"follow\": true\n}\n");
        Write("b-min.json", "{\"b\":1,\"a\":{\"y\":[1,2],\"x\":null}}\n");
        Write("c-compose.yml", "services:\n    web:\n        image: nginx\n    db:\n        image: pg\n");
        Write("d-project.xml", "<Project>\n<A>\n<B>1</B>\n</A>\n</Project>\n");
        Write("e-readme.md", "# Title\n\nSome text.\n\n## Part\n\nMore.\n");
        Write("f-analysis.ipynb", "{\"cells\":[{\"cell_type\":\"markdown\",\"metadata\":{},\"source\":[\"# Sales\"]}],\"metadata\":{},\"nbformat\":4,\"nbformat_minor\":5}\n");
        Write("g-logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><script>alert(1)</script><rect width=\"5\" height=\"5\"/></svg>\n");
        Write("h-index.html", "<!doctype html>\n<html><head><title>Index</title></head>\n<body><main><h1>Hello</h1></main><script>document.title = 'Ran'</script></body></html>\n");
        Write("h-app.sln", "Microsoft Visual Studio Solution File\n");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string name, string text) => File.WriteAllText(Path.Join(_dir, name), text, new UTF8Encoding(false));

    private async Task<MainWindow> Open(string focus, Func<IPageView>? page = null)
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        if (page is not null)
        {
            w.PageViewFactory = page;
        }

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

    private static async Task<EditorWindow> Editor(MainWindow w)
    {
        Press(w, Key.F4);
        await Until(() => w.Editors.Count > 0 && w.Editors[^1].IsLoaded);
        var e = w.Editors[^1];
        await e.Loading;
        await e.Checking;
        Dispatcher.UIThread.RunJobs();
        return e;
    }

    private static async Task Checked(EditorWindow e)
    {
        await e.CheckAsync();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    private static void Close(MainWindow w, EditorWindow e)
    {
        e.Editor.Document.UndoStack.MarkAsOriginalFile();
        e.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Json_AMistake_IsMarked_OnTheToolbarTheStatusLineAndALine_GoToSelectsIt()
    {
        var w = await Open("a-broken.json");
        var e = await Editor(w);
        var problem = Assert.Single(e.Problems);
        Assert.Equal(5, problem.Line);
        Assert.True(e.ProblemsLine.IsVisible);
        Assert.Equal("1 problem", e.CheckText);
        Assert.Contains(e.StatusTexts, t => t.Text == "● 1 problem");
        Capture(e, "editor2b-json-problem.png");

        e.GoToProblem();
        Assert.Equal(problem.Offset, e.Editor.SelectionStart);

        var line4 = e.Editor.Document.GetLineByNumber(4);
        e.Editor.Document.Insert(line4.EndOffset, ",");
        await Checked(e);
        Assert.Empty(e.Problems);
        Assert.False(e.ProblemsLine.IsVisible);
        Assert.Equal("Valid JSON", e.CheckText);
        Close(w, e);
    }

    [AvaloniaFact]
    public async Task ClosingWithAProblem_TheQuestionSaysItCanBeSavedAnyway()
    {
        var w = await Open("a-broken.json");
        var e = await Editor(w);
        e.Editor.Document.Insert(0, " ");
        await Checked(e);
        e.Close();
        await Until(() => e.CloseQuestion is not null);
        var question = e.CloseQuestion!;
        Assert.Contains(question.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("line 5", StringComparison.Ordinal) == true);
        question.Close("discard");
        await Until(() => w.Editors.Count == 0);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Folding_JsonBlocksFold_FoldAllAndUnfoldAll_WithCtrlK()
    {
        var w = await Open("b-min.json");
        var e = await Editor(w);
        await e.FormatAsync();
        await Checked(e);
        Assert.True(e.FoldCount >= 3);
        Capture(e, "editor2b-json-folds.png");

        Press(e, Key.K, RawInputModifiers.Control);
        Press(e, Key.D0, RawInputModifiers.Control);
        Assert.All(e.Folding!.AllFoldings, f => Assert.True(f.IsFolded));
        Press(e, Key.K, RawInputModifiers.Control);
        Press(e, Key.J, RawInputModifiers.Control);
        Assert.All(e.Folding!.AllFoldings, f => Assert.False(f.IsFolded));

        w.Services.Settings.Update(s => s with { Editors = s.Editors with { Folding = false } });
        await Until(() => e.Folding is null);
        Close(w, e);
    }

    [AvaloniaFact]
    public async Task Yaml_And_Xml_AreChecked_AndFormatReindents()
    {
        var w = await Open("c-compose.yml");
        var yaml = await Editor(w);
        Assert.Equal("Valid YAML", yaml.CheckText);
        w.Services.Settings.Update(s => s with { Editors = s.Editors with { FormatIndent = FormatIndent.Spaces2 } });
        await yaml.FormatAsync();
        Assert.Equal("services:\n  web:\n    image: nginx\n  db:\n    image: pg\n", yaml.Editor.Document.Text);
        yaml.Editor.Document.Insert(yaml.Editor.Document.TextLength, "x: [1,\n");
        await Checked(yaml);
        Assert.Single(yaml.Problems);
        Close(w, yaml);

        var w2 = await Open("d-project.xml");
        var xml = await Editor(w2);
        Assert.Equal("Well-formed XML", xml.CheckText);
        await xml.FormatAsync();
        Assert.Equal("<Project>\n    <A>\n        <B>1</B>\n    </A>\n</Project>\n", xml.Editor.Document.Text);
        xml.Editor.Undo();
        Assert.Equal("<Project>\n<A>\n<B>1</B>\n</A>\n</Project>\n", xml.Editor.Document.Text);
        Close(w2, xml);
    }

    [AvaloniaFact]
    public async Task Json_FormatMinifySortKeys_EachOneUndo_AndAMistakeSaysWhy()
    {
        var w = await Open("b-min.json");
        var e = await Editor(w);
        w.Services.Settings.Update(s => s with { Editors = s.Editors with { FormatIndent = FormatIndent.Spaces2 } });
        Dispatcher.UIThread.RunJobs();

        Press(e, Key.F, RawInputModifiers.Shift | RawInputModifiers.Alt);
        await e.Formatting;
        Assert.Equal("{\n  \"b\": 1,\n  \"a\": {\n    \"y\": [\n      1,\n      2\n    ],\n    \"x\": null\n  }\n}\n", e.Editor.Document.Text);

        await e.SortKeysAsync();
        Assert.StartsWith("{\n  \"a\": {\n    \"x\": null,\n    \"y\"", e.Editor.Document.Text, StringComparison.Ordinal);

        await e.MinifyAsync();
        Assert.Equal("{\"a\":{\"x\":null,\"y\":[1,2]},\"b\":1}", e.Editor.Document.Text.TrimEnd());
        e.Editor.Undo();
        Assert.StartsWith("{\n  \"a\"", e.Editor.Document.Text, StringComparison.Ordinal);

        e.Editor.Document.Insert(1, "\"oops\" ");
        await Checked(e);
        await e.FormatAsync();
        Assert.True(e.NoteBar.IsVisible);
        Assert.StartsWith("{\"oops\" ", e.Editor.Document.Text, StringComparison.Ordinal);
        Close(w, e);
    }

    [AvaloniaFact]
    public async Task CommentLine_AndConvertIndentation()
    {
        var w = await Open("c-compose.yml");
        var e = await Editor(w);
        var doc = e.Editor.Document;
        e.Editor.Select(doc.GetLineByNumber(2).Offset, doc.GetLineByNumber(3).EndOffset - doc.GetLineByNumber(2).Offset);
        Press(e, Key.OemQuestion, RawInputModifiers.Control);
        Assert.Equal("services:\n    # web:\n    #     image: nginx\n    db:\n        image: pg\n", doc.Text);
        Press(e, Key.OemQuestion, RawInputModifiers.Control);
        Assert.Equal("services:\n    web:\n        image: nginx\n    db:\n        image: pg\n", doc.Text);

        e.ConvertIndentation(IndentStyle.Spaces2);
        Assert.Equal("services:\n  web:\n    image: nginx\n  db:\n    image: pg\n", doc.Text);
        Assert.Contains(e.StatusTexts, t => t.Text == "Spaces: 2");
        Close(w, e);
    }

    [AvaloniaFact]
    public async Task Preview_Markdown_OpensByItself_FollowsTheTyping_AndF6HidesIt()
    {
        var page = new FakePage();
        var w = await Open("e-readme.md", () => page);
        var e = await Editor(w);
        await Until(() => e.PreviewOpen && page.Shown.Count == 1);
        Assert.Contains("data-line=\"0\"", page.Shown[0].Html, StringComparison.Ordinal);
        Assert.False(e.PreviewBelow);
        page.Load();
        Dispatcher.UIThread.RunJobs();
        Capture(e, "editor2b-md-preview.png");

        e.Editor.Document.Insert(e.Editor.Document.TextLength, "\nA new line.\n");
        await Until(() => page.Scripts.Any(s => s.Contains("__sd.replace(", StringComparison.Ordinal) && s.Contains("A new line.", StringComparison.Ordinal)));
        Assert.Single(page.Shown);

        e.SyncPreview();
        Assert.Contains(page.Scripts, s => s.StartsWith("window.__sd && window.__sd.line(", StringComparison.Ordinal));

        page.Post("{\"t\":\"line\",\"line\":4}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, e.Editor.TextArea.Caret.Line);

        Press(e, Key.F6);
        Assert.False(e.PreviewOpen);
        Press(e, Key.F6);
        Assert.True(e.PreviewOpen);

        e.MovePreview(below: true);
        Assert.True(e.PreviewBelow);
        Assert.True(w.Services.Settings.Current.Editors.SpotFor(PreviewKind.Markdown).Below);

        w.Services.Settings.Update(s => s with { Editors = s.Editors with { PreviewSpots = [s.Editors.SpotFor(PreviewKind.Markdown) with { Share = 0.3 }] } });
        e.HalfAndHalf();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.5, w.Services.Settings.Current.Editors.SpotFor(PreviewKind.Markdown).Share);
        Assert.True(w.Services.Settings.Current.Editors.SpotFor(PreviewKind.Markdown).Below);
        Close(w, e);
    }

    [AvaloniaFact]
    public async Task Preview_NotebookWithF6_AMistakeKeepsTheLastGoodPageGreyed()
    {
        var page = new FakePage();
        var w = await Open("f-analysis.ipynb", () => page);
        var e = await Editor(w);
        Assert.Equal(PreviewKind.Notebook, e.PreviewKindNow);
        Assert.False(e.PreviewOpen);
        Assert.True(e.PreviewToggle.IsVisible);
        Press(e, Key.F6);
        await e.PreviewRendering;
        Assert.Single(page.Shown);
        Assert.Contains("Sales", page.Shown[0].Html, StringComparison.Ordinal);
        page.Load();
        Dispatcher.UIThread.RunJobs();

        e.Editor.Document.Text = "{\"cells\": [";
        await Until(() => e.PreviewNote.IsVisible);
        await Until(() => page.Scripts.Any(s => s.Contains("__sd.dim(true)", StringComparison.Ordinal)));
        Assert.Single(page.Shown);
        Close(w, e);
    }

    [AvaloniaFact]
    public async Task Preview_Svg_ShowsThePictureWithoutItsScripts_AndALinkGoesToTheBrowserOnly()
    {
        var page = new FakePage();
        var w = await Open("g-logo.svg", () => page);
        var opened = new List<Uri>();
        w.OpenWebLink = uri =>
        {
            opened.Add(uri);
            return true;
        };
        var e = await Editor(w);
        Press(e, Key.F6);
        await e.PreviewRendering;
        var html = Assert.Single(page.Shown).Html;
        Assert.Contains("<rect", html, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", html, StringComparison.Ordinal);

        e.OnPreviewLink(new Uri("https://example.com/"));
        e.OnPreviewLink(new Uri("file:///C:/Windows/notepad.exe"));
        Assert.Equal([new Uri("https://example.com/")], opened);
        Close(w, e);
    }

    [AvaloniaFact]
    public async Task Preview_Html_WithF6_ThePageItselfWithItsScripts_LoadedAgainAfterAPauseInTyping()
    {
        var page = new FakePage();
        var w = await Open("h-index.html", () => page);
        var e = await Editor(w);
        Assert.Equal(PreviewKind.Html, e.PreviewKindNow);
        Assert.False(e.PreviewOpen);
        Assert.True(e.PreviewToggle.IsVisible);
        Press(e, Key.F6);
        await e.PreviewRendering;
        var shown = Assert.Single(page.ShownHtml);
        Assert.EndsWith("h-index.html", shown.File, StringComparison.Ordinal);
        Assert.Contains("<h1>Hello</h1>", shown.Text, StringComparison.Ordinal);
        Assert.True(shown.Scripts);
        Assert.True(shown.WebContent);
        Assert.Empty(page.Shown);
        page.Load();
        Dispatcher.UIThread.RunJobs();

        e.Editor.Document.Insert(e.Editor.Document.Text.IndexOf("</main>", StringComparison.Ordinal), "<p>more</p>");
        Assert.Single(page.ShownHtml);
        await Until(() => page.ShownHtml.Count == 2);
        Assert.Contains("<p>more</p>", page.ShownHtml[1].Text, StringComparison.Ordinal);
        e.Editor.Undo();

        page.Post("{\"t\":\"menu\",\"x\":20,\"y\":30,\"href\":\"https://example.org/\",\"selection\":true}");
        await Until(() => e.LastContextMenu is not null);
        var headers = e.LastContextMenu!.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Equal(["Copy", "Select all", "Open link", "Copy link address"], headers);
        e.LastContextMenu.Hide();
        Close(w, e);
    }

    [AvaloniaFact]
    public async Task Preview_SwitchesSides_LeftOrRight_AboveOrBelow_RememberedPerKind()
    {
        var page = new FakePage();
        var w = await Open("h-index.html", () => page);
        var e = await Editor(w);
        Press(e, Key.F6);
        await e.PreviewRendering;
        Assert.False(e.PreviewBefore);
        e.SwapPreviewSide();
        Assert.True(e.PreviewBefore);
        Assert.True(w.Services.Settings.Current.Editors.SpotFor(PreviewKind.Html).Before);
        e.MovePreview(below: true);
        Assert.True(e.PreviewBelow);
        Assert.True(e.PreviewBefore);
        e.HalfAndHalf();
        Assert.True(w.Services.Settings.Current.Editors.SpotFor(PreviewKind.Html).Before);
        e.SwapPreviewSide();
        Assert.False(e.PreviewBefore);
        Assert.False(w.Services.Settings.Current.Editors.SpotFor(PreviewKind.Markdown).Before);
        Close(w, e);
    }

    [AvaloniaFact]
    public async Task AViewerOpenedFromTheEditor_BringsNoWindowForwardWhenClosed()
    {
        var w = await Open("h-index.html");
        var e = await Editor(w);
        var activated = 0;
        w.Activated += (_, _) => activated++;
        Press(e, Key.F3);
        await Until(() => w.Viewers.Count == 1);
        var v = w.Viewers[0];
        v.Activate();
        Dispatcher.UIThread.RunJobs();
        activated = 0;
        v.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, activated);

        w.Activate();
        w.LeftPanel.View.Focus();
        Press(w, Key.F3);
        await Until(() => w.Viewers.Count == 1);
        v = w.Viewers[0];
        v.Activate();
        Dispatcher.UIThread.RunJobs();
        activated = 0;
        v.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(activated > 0);
        Close(w, e);
    }

    [AvaloniaFact]
    public async Task RightClickInTheText_TheUsualItems_EnabledAsTheyCanBe()
    {
        var w = await Open("h-index.html");
        var e = await Editor(w);
        var menu = e.ShowTextMenu();
        MenuItem Item(string header) => menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, header));
        Assert.Equal(["Undo", "Redo", "Cut", "Copy", "Paste", "Delete", "Select all"],
            menu.Items.OfType<MenuItem>().Select(i => i.Header as string).Where(h => h is not null).ToList());
        Assert.False(Item("Undo").IsEnabled);
        Assert.False(Item("Copy").IsEnabled);
        Assert.True(Item("Paste").IsEnabled);
        Assert.Contains(menu.Items.OfType<MenuItem>(), i => Equals(i.Tag, "Comment line"));
        menu.Hide();

        Item("Select all").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(e.Editor.Document.TextLength, e.Editor.SelectionLength);
        menu = e.ShowTextMenu();
        Assert.True(menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Copy")).IsEnabled);
        menu.Hide();
        Close(w, e);
    }

    [AvaloniaFact]
    public async Task Preview_ABigFile_UpdatesOnSave()
    {
        var page = new FakePage();
        var w = await Open("e-readme.md", () => page);
        w.Services.Settings.Update(s => s with { Editors = s.Editors with { PreviewLiveUpToMB = 0 } });
        var e = await Editor(w);
        await Until(() => page.Shown.Count == 1);
        page.Load();
        Dispatcher.UIThread.RunJobs();
        e.Editor.Document.Insert(0, "x");
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();
        Assert.True(e.PreviewNote.IsVisible);
        Assert.DoesNotContain(page.Scripts, s => s.Contains("__sd.replace(", StringComparison.Ordinal));
        Assert.True(await e.SaveAsync());
        await e.PreviewRendering;
        Assert.Contains(page.Scripts, s => s.Contains("__sd.replace(", StringComparison.Ordinal));
        e.Close();
        w.CloseNow();
    }

    private static void WithExternalRow(MainWindow w) => w.Services.Settings.Update(s => s with
    {
        Editors = s.Editors with { Masks = [new EditorMask { Mask = "*.sln", Program = "\"C:\\Tools\\Code.exe\" --reuse \"$(FullName)\"" }, .. s.Editors.Masks] },
    });

    [AvaloniaFact]
    public async Task ExternalRow_F4StartsItsProgram_WithTheFileAsOneArgument_NeverTheEditor()
    {
        var w = await Open("h-app.sln");
        WithExternalRow(w);
        var launcher = Assert.IsType<RecordingProgramLauncher>(w.Programs);
        Press(w, Key.F4);
        await Until(() => launcher.Started.Count == 1);
        Assert.Empty(w.Editors);
        var (program, arguments) = launcher.Started[0];
        Assert.Equal("C:\\Tools\\Code.exe", program);
        Assert.Equal(["--reuse", Path.Join(_dir, "h-app.sln")], arguments);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlShiftF4_ListsTheEditors_AndTheInternalOneOpensEvenAnExternalRow()
    {
        var w = await Open("h-app.sln");
        WithExternalRow(w);
        Press(w, Key.F4, RawInputModifiers.Control | RawInputModifiers.Shift);
        var menu = w.EditWithMenu!;
        var items = menu.Items.OfType<MenuItem>().Select(m => m.Tag as string).Where(t => t is not null).ToList();
        Assert.Equal(["Internal editor", "Code", "Windows' default app", "Choose a program…"], items);
        Assert.True(menu.Items.OfType<MenuItem>().Single(m => (string?)m.Tag == "Code").IsChecked);

        menu.Items.OfType<MenuItem>().Single(m => (string?)m.Tag == "Internal editor").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => w.Editors.Count == 1 && w.Editors[0].IsLoaded);
        Assert.Empty(((RecordingProgramLauncher)w.Programs).Started);
        w.Editors[0].Close();

        Press(w, Key.F4, RawInputModifiers.Control | RawInputModifiers.Shift);
        w.EditWithMenu!.Items.OfType<MenuItem>().Single(m => (string?)m.Tag == "Windows' default app").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal([Path.Join(_dir, "h-app.sln")], ((RecordingProgramLauncher)w.Programs).Edited);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ShiftF4_MakesANewFile_AndOpensIt_AnExistingNameOpensThatFile()
    {
        var w = await Open("b-min.json");
        w.Services.Settings.Update(s => s with { Editors = s.Editors with { NewLineEnding = LineEnding.Lf } });
        Press(w, Key.F4, RawInputModifiers.Shift);
        await Until(() => w.EditNewDialog is { IsVisible: true });
        var dialog = w.EditNewDialog!;
        Assert.Equal("new.txt", dialog.NameBox.Text);
        Assert.Equal((0, 3), (dialog.NameBox.SelectionStart, dialog.NameBox.SelectionEnd));

        dialog.NameBox.Text = "bad|name.txt";
        dialog.Accept();
        Assert.True(dialog.ErrorText.IsVisible);
        dialog.NameBox.Text = "notes.md";
        dialog.Accept();
        await Until(() => w.Editors.Count == 1 && w.Editors[0].IsLoaded);
        var e = w.Editors[0];
        await e.Loading;
        Assert.True(File.Exists(Path.Join(_dir, "notes.md")));
        Assert.Equal(LineEnding.Lf, e.File.LineEnding);
        Assert.Equal("notes.md", w.LeftPanel.Panel.FocusedEntry?.Name);
        Dispatcher.UIThread.RunJobs();
        Assert.True(e.Editor.TextArea.IsKeyboardFocusWithin || e.Editor.TextArea.IsFocused);
        e.Close();

        Press(w, Key.F4, RawInputModifiers.Shift);
        await Until(() => w.EditNewDialog is { IsVisible: true });
        w.EditNewDialog!.NameBox.Text = "b-min.json";
        w.EditNewDialog.Accept();
        await Until(() => w.Editors.Count == 1 && w.Editors[0].IsLoaded);
        await w.Editors[0].Loading;
        Assert.StartsWith("{\"b\":1", w.Editors[0].Editor.Document.Text, StringComparison.Ordinal);
        w.Editors[0].Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task OptionsEditors_AnExternalRow_HasItsProgram_AndTheNewSettingsAreThere()
    {
        var services = AppServices.CreateIsolated();
        var o = new OptionsWindow(services) { Width = 1100, Height = 900 };
        o.Show();
        o.ShowCategory("editors");
        Dispatcher.UIThread.RunJobs();
        var combo = o.EditorMaskGrid!.Children.OfType<ComboBox>().First();
        combo.SelectedIndex = combo.Items.Count - 1;
        await Until(() => services.Settings.Current.Editors.Masks[0].Program is not null);
        Assert.Equal("notepad.exe \"$(FullName)\"", services.Settings.Current.Editors.Masks[0].Program);
        Dispatcher.UIThread.RunJobs();
        var texts = o.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Check while typing", texts);
        Assert.Contains("Fold markers", texts);
        Assert.Contains("Opens by itself for", texts);
        Assert.Contains("Live up to", texts);
        Assert.Contains(o.GetVisualDescendants().OfType<TextBox>(), b => b.Text == "notepad.exe \"$(FullName)\"");
        Capture(o, "editor2b-options.png");
        o.Close();
    }

    private sealed class FakePage : IPageView
    {
        public List<(string Html, Uri Folder)> Shown { get; } = [];

        public List<string> Scripts { get; } = [];

        public Control Control { get; } = new Border();

        public event Action? Loaded;

        public event Action<Uri>? Navigating;

        public event Action<string>? Message;

        public void Show(string html, Uri folder) => Shown.Add((html, folder));

        public List<(string File, string? Text, bool Scripts, bool WebContent)> ShownHtml { get; } = [];

        public void ShowHtml(string file, string? text, bool scripts, bool webContent) => ShownHtml.Add((file, text, scripts, webContent));

        public Task<string?> RunAsync(string script)
        {
            Scripts.Add(script);
            return Task.FromResult<string?>("ok");
        }

        public void FocusPage()
        {
        }

        public void Load() => Loaded?.Invoke();

        public void Post(string json) => Message?.Invoke(json);

        public void Go(Uri uri) => Navigating?.Invoke(uri);

        public void Dispose()
        {
        }
    }
}
