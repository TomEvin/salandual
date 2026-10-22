// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/MarkdownViewerTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Viewer;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class MarkdownViewerTests : IDisposable
{
    private const string Readme = """
        # Salandual

        A file manager, in the spirit of [Open Salamander](https://github.com/OpenSalamander/salamander).

        ## Getting started

        Read [the other file](other.md#more) or look at [the picture](picture.png), [nothing](gone.md), the [licence](#licence).

        ![The main window](https://example.org/shot.png)

        ## Licence

        GPL.
        """;

    private const string Notebook = """
        { "nbformat": 4, "nbformat_minor": 5,
          "metadata": { "kernelspec": { "display_name": "Python 3 (ipykernel)", "language": "python", "name": "python3" } },
          "cells": [
            { "cell_type": "markdown", "metadata": {}, "source": "# Sales" },
            { "cell_type": "code", "execution_count": 1, "metadata": {}, "source": "print(1)", "outputs": [ { "output_type": "stream", "name": "stdout", "text": "1\n" } ] } ] }
        """;

    private const string Dot = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-md-").FullName;
    private readonly TextClipboard _clip = new();
    private readonly FakePage _page = new();
    private readonly List<Uri> _web = [];

    public MarkdownViewerTests()
    {
        File.WriteAllText(Path.Join(_dir, "README.md"), Readme);
        File.WriteAllText(Path.Join(_dir, "other.md"), "# Other\n\n## More\n\nText.");
        File.WriteAllText(Path.Join(_dir, "picture.png"), "not really a picture");
        File.WriteAllText(Path.Join(_dir, "notes.txt"), "plain text");
        File.WriteAllText(Path.Join(_dir, "analysis.ipynb"), Notebook);
        File.WriteAllText(Path.Join(_dir, "broken.ipynb"), "{ not a notebook");
    }

    public void Dispose()
    {
        for (var i = 0; ; i++)
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
                return;
            }
            catch (IOException) when (i < 50)
            {
                Thread.Sleep(20);
            }
        }
    }

    private async Task<MainWindow> Open(string focus = "README.md", bool page = true)
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.FileClipboardAccess = _clip;
        w.OpenWebLink = uri =>
        {
            _web.Add(uri);
            return true;
        };
        if (page)
        {
            w.PageViewFactory = () => _page;
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
        for (var i = 0; i < 1000 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private string Link(string relative) => "file:///" + _dir.Replace('\\', '/') + "/" + relative;

    private static bool Shows(Control c) => TopLevel.GetTopLevel(c) is not null && c.IsEffectivelyVisible;

    private static bool Waiting(ViewerWindow v)
        => v.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Starting the page view…" && t.IsEffectivelyVisible);

    private static List<string> Status(ViewerWindow v) => [.. v.StatusCells.Select(c => (c.Child as TextBlock)?.Text ?? "")];

    [AvaloniaFact]
    public async Task ThePagesZoomTools_GoAndComeBack_AllAtOnce()
    {
        var w = await Open();
        try
        {
            var v = await Viewer(w);
            var tools = v.PageZoomTools.Children.ToList();
            Assert.Equal(3, tools.Count);
            var seen = new HashSet<int>();
            for (var width = 1400.0; width >= v.MinWidth; width -= 4)
            {
                v.Width = width;
                Dispatcher.UIThread.RunJobs();
                v.UpdateLayout();
                var room = v.ToolbarLeft.Bounds.Width;
                var shown = tools.Count(t => Avalonia.VisualExtensions.TranslatePoint(t, default, v.ToolbarLeft) is { } p && p.X + t.Bounds.Width <= room + 0.5);
                Assert.True(shown is 0 or 3, $"at {width} px {shown} of the 3 zoom tools are shown");
                seen.Add(shown);
            }

            Assert.True(seen.SetEquals([0, 3]), $"seen {string.Join(",", seen)}");
        }
        finally
        {
            w.CloseNow();
        }
    }

    private async Task<ViewerWindow> Viewer(MainWindow w, bool load = true)
    {
        var shown = _page.Shown.Count;
        Press(w, Key.F3);
        await Until(() => w.Viewers.Count > 0 && _page.Shown.Count > shown);
        var v = w.Viewers[^1];
        if (load)
        {
            await Load(v);
        }

        return v;
    }

    private async Task Load(ViewerWindow v)
    {
        _page.Load();
        await Until(() => v.PageReady && v.PageShown.IsCompleted);
    }

    private static void Click(MenuFlyout menu, string header)
    {
        var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, header));
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task F3OnMarkdown_ShowsARingWhileTheBrowserStarts_ThenThePage_NeverTheSource()
    {
        var w = await Open();
        try
        {
            var v = await Viewer(w, load: false);
            Assert.True(v.IsMarkdown);
            Assert.True(v.IsPage);
            Assert.False(v.PageReady);
            Assert.False(Shows(v.TextView));
            Assert.True(Waiting(v));
            Assert.Contains("starting the page view…", Status(v));
            Assert.Contains("<h1 id=\"salandual\">Salandual</h1>", _page.Shown[^1].Html, StringComparison.Ordinal);
            Assert.Equal(new Uri(_dir + "\\"), _page.Shown[^1].Folder);

            await Load(v);
            Assert.False(Shows(v.TextView));
            Assert.False(Waiting(v));
            var status = Status(v);
            Assert.Equal("29 words", status[0]);
            Assert.Equal("1 min read", status[1]);
            Assert.Equal("3 headings", status[2]);
            Assert.Contains("CommonMark + GitHub extras", status);
            Assert.DoesNotContain("starting the page view…", status);
            Assert.Equal("2 / 2 Markdown files", v.Counter.Text);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task AFileWithAByteOrderMark_ItsFirstLineIsStillAHeading()
    {
        File.WriteAllBytes(Path.Join(_dir, "bom.md"), [0xEF, 0xBB, 0xBF, .. "# Post 1\n\nText."u8]);
        var w = await Open("bom.md");
        try
        {
            var v = await Viewer(w);
            Assert.Equal(["Post 1"], v.Page!.Headings.Select(h => h.Text));
            Assert.Contains("<h1 id=\"post-1\">Post 1</h1>", _page.Shown[^1].Html, StringComparison.Ordinal);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task RenderedTextAndHex_F6F5F4_TheViewButtonsInEitherOrder()
    {
        var w = await Open();
        var settings = w.Services.Settings;
        try
        {
            var v = await Viewer(w);
            Assert.Equal(["Rendered", "Text", "Hex"], v.ModeOrder);
            Press(v, Key.F5);
            Assert.False(v.IsPage);
            Assert.False(v.IsHex);
            Press(v, Key.F4);
            Assert.True(v.IsHex);
            var renders = _page.Shown.Count;
            Press(v, Key.F6);
            Assert.True(v.IsPage);
            Assert.Equal(renders, _page.Shown.Count);

            settings.Update(s => s with { Viewers = s.Viewers with { ViewButtons = ViewButtonOrder.KeyOrder } });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Hex", "Text", "Rendered"], v.ModeOrder);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task TheKeyOrder_IsForEveryViewer()
    {
        var w = await Open("notes.txt");
        try
        {
            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { ViewButtons = ViewButtonOrder.KeyOrder } });
            Press(w, Key.F3);
            await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Document is not null);
            var v = w.Viewers[^1];
            Assert.False(v.IsMarkdown);
            Assert.Equal(["Hex", "Text"], v.ModeOrder);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Contents_OShowsTheHeadings_TheOneBeingReadIsMarked_AndItsButtonMovesItAcross()
    {
        var w = await Open();
        var settings = w.Services.Settings;
        try
        {
            var v = await Viewer(w);
            Assert.False(v.ContentsPanel.IsVisible);
            Press(v, Key.O);
            Assert.True(settings.Current.Viewers.MarkdownContents);
            Assert.True(v.ContentsPanel.IsVisible);
            Assert.False(v.ContentsOnLeft);
            Assert.Equal(["salandual", "getting-started", "licence"], v.ContentsItems.Select(i => i.Id));

            _page.Post("""{"t":"heading","id":"getting-started"}""");
            await Until(() => v.ContentsItems.Single(i => i.Current).Id == "getting-started");

            var item = v.ContentsPanel.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, "licence"));
            item.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => _page.Scripts.Contains("""window.__sd && window.__sd.go("licence")"""));

            var side = v.ContentsPanel.GetLogicalDescendants().OfType<Button>().First(b => b.Tag is null);
            side.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(settings.Current.Viewers.MarkdownContentsLeft);
            Assert.True(v.ContentsOnLeft);

            Press(v, Key.O);
            Assert.False(v.ContentsPanel.IsVisible);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Links_TheWebOpensTheBrowser_MarkdownOpensHere_AnotherFileItsOwnViewer()
    {
        var w = await Open();
        try
        {
            var v = await Viewer(w);
            _page.Click("https://github.com/OpenSalamander/salamander");
            await Until(() => _web.Count == 1);
            Assert.Equal("https://github.com/OpenSalamander/salamander", _web[0].AbsoluteUri);

            _page.Click(Link("#licence"));
            await Until(() => _page.Scripts.Contains("""window.__sd && window.__sd.go("licence")"""));

            _page.Click(_dir + "\\gone.md");
            await Until(() => Status(v).Contains("gone.md wasn't found"));

            _page.Post("""{"t":"hover","href":"https://github.com/OpenSalamander/salamander"}""");
            await Until(() => Status(v)[0] == "https://github.com/OpenSalamander/salamander · opens the browser");
            _page.Post("""{"t":"hover","href":null}""");
            await Until(() => Status(v)[0] == "29 words");

            var shown = _page.Shown.Count;
            _page.Click(Link("other.md#more"));
            await Until(() => v.FilePath.EndsWith("other.md", StringComparison.Ordinal) && _page.Shown.Count > shown);
            Assert.Contains("<h1 id=\"other\">Other</h1>", _page.Shown[^1].Html, StringComparison.Ordinal);
            Assert.Single(v.BackStack);
            await Load(v);
            Assert.Contains("""window.__sd && window.__sd.go("more")""", _page.Scripts);

            Press(v, Key.Left, RawInputModifiers.Alt);
            await Until(() => v.FilePath.EndsWith("README.md", StringComparison.Ordinal));
            Assert.Empty(v.BackStack);

            _page.Click(_dir + "\\picture.png");
            await Until(() => w.Viewers.Count == 2);
            Assert.EndsWith("picture.png", w.Viewers[1].FilePath, StringComparison.Ordinal);
            await Until(() => w.Viewers[1].IsImage);

            _page.Click("mailto:someone@example.org");
            _page.Click("javascript:alert(1)");
            Dispatcher.UIThread.RunJobs();
            Assert.Single(_web);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task WebImages_ShowByDefault_OffLeavesAPlaceholder_AndShowWebImagesLoadsThemForThisFile()
    {
        var w = await Open();
        var settings = w.Services.Settings;
        try
        {
            var v = await Viewer(w);
            Assert.Contains("<img src=\"https://example.org/shot.png\"", _page.Shown[^1].Html, StringComparison.Ordinal);

            settings.Update(s => s with { Viewers = s.Viewers with { MarkdownWebImages = false } });
            await Until(() => _page.Shown[^1].Html.Contains("class=\"webimage\"", StringComparison.Ordinal));
            Assert.Contains("""window.__sd && window.__sd.where()""", _page.Scripts);
            Assert.DoesNotContain("<img src=\"https://", _page.Shown[^1].Html, StringComparison.Ordinal);
            await Load(v);

            _page.Click(MarkdownPage.ShowWebImagesLink);
            await Until(() => _page.Shown[^1].Html.Contains("<img src=\"https://example.org/shot.png\"", StringComparison.Ordinal));
            Assert.False(settings.Current.Viewers.MarkdownWebImages);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task ThePagesKeys_ComeToTheViewer_ZoomBoxAndCtrlWheel_FindAndCopy()
    {
        var w = await Open();
        try
        {
            var v = await Viewer(w);
            _page.Post("""{"t":"zoom","by":1}""");
            await Until(() => v.PageZoom == 125);
            Assert.Contains("window.__sd && window.__sd.zoom(1.25)", _page.Scripts);
            v.PageZoomBox.Box.Focus();
            v.PageZoomBox.Box.Text = "333 %";
            Press(v, Key.Enter);
            Assert.Equal(333, v.PageZoom);
            Assert.Contains("window.__sd && window.__sd.zoom(3.33)", _page.Scripts);
            _page.Post("""{"t":"zoom","by":-1}""");
            await Until(() => v.PageZoom == 200);
            Press(v, Key.D0, RawInputModifiers.Control);
            Assert.Equal(100, v.PageZoom);

            _page.Results["find"] = """{"count":5,"index":1}""";
            Press(v, Key.F, RawInputModifiers.Control);
            v.FindBox.Text = "Salandual";
            await Until(() => v.HitsText == "2 of 5");
            Assert.Contains("""window.__sd && window.__sd.find("Salandual",false,false,false)""", _page.Scripts);
            _page.Results["next"] = """{"count":5,"index":2}""";
            Press(v, Key.F3);
            await Until(() => v.HitsText == "3 of 5");

            Assert.True(v.RegexFlag.IsVisible);
            _page.Results["find"] = """{"count":0,"index":-1,"bad":true}""";
            v.RegexFlag.IsChecked = true;
            v.RegexFlag.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await Until(() => v.HitsText == "Can't search for this");
            Assert.Contains("""window.__sd && window.__sd.find("Salandual",false,false,true)""", _page.Scripts);
            v.RegexFlag.IsChecked = false;
            Press(v, Key.Escape);
            Assert.Contains("window.__sd && window.__sd.clear()", _page.Scripts);
            Assert.Contains(v, w.Viewers);

            _page.Results["selection"] = "picked words";
            Press(v, Key.C, RawInputModifiers.Control);
            await Until(() => _clip.Text == "picked words");
            Click(v.BuildMoreMenu(), "Copy as Markdown");
            Assert.Equal(Readme, _clip.Text);

            _page.Post("""{"t":"menu","x":40,"y":40,"href":null,"selection":false}""");
            await Until(() => v.LastStatusMenu?.Items.OfType<MenuItem>().Any(i => Equals(i.Header, "Copy as HTML")) == true);
            Click(v.LastStatusMenu!, "Copy as HTML");
            Assert.StartsWith("<!doctype html>", _clip.Text, StringComparison.Ordinal);
            v.LastStatusMenu!.Hide();
            _clip.SetText("");
            Click(v.LastStatusMenu!, "Copy as Markdown");
            Assert.Equal(Readme, _clip.Text);
            Assert.DoesNotContain(v.LastStatusMenu!.Items.OfType<MenuItem>(), i => Equals(i.Header, "Copy the image"));

            v.LastStatusMenu!.Hide();
            File.WriteAllBytes(Path.Join(_dir, "dot.png"), Convert.FromBase64String(Dot));
            var local = new Uri(Path.Join(_dir, "dot.png")).AbsoluteUri;
            _page.Post($$"""{"t":"menu","x":40,"y":40,"href":null,"img":"{{local}}","selection":false}""");
            await Until(() => v.LastStatusMenu?.Items.OfType<MenuItem>().Any(i => Equals(i.Header, "Copy the image")) == true);
            Click(v.LastStatusMenu!, "Copy the image");
            await Until(() => _clip.Png is not null);
            Assert.NotEmpty(_clip.Dib!);
            v.LastStatusMenu!.Hide();
            _clip.SetImage([], null);
            await v.CopyPageImageAsync("data:image/png;base64," + Dot);
            Assert.NotNull(_clip.Png);

            _page.Post("""{"t":"key","key":"F5","ctrl":false,"alt":false,"shift":false}""");
            await Until(() => !v.IsPage);
            Press(v, Key.F6);
            _page.Post("""{"t":"key","key":"Backspace","ctrl":false,"alt":false,"shift":false}""");
            await Until(() => v.FilePath.EndsWith("other.md", StringComparison.Ordinal));
            Assert.True(v.IsPage);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Notebook_IsAPage_RenderedJsonHex_WithItsStatus_AndCopies()
    {
        var w = await Open("analysis.ipynb");
        try
        {
            var v = await Viewer(w);
            Assert.True(v.IsNotebook);
            Assert.True(v.IsPage);
            Assert.Equal(["Rendered", "JSON", "Hex"], v.ModeOrder);
            Assert.Equal("1 / 2 notebooks", v.Counter.Text);
            Assert.Contains("In [1]:", _page.Shown[^1].Html, StringComparison.Ordinal);
            Assert.Contains("2 cells: 1 code, 1 Markdown", Status(v));
            Assert.Contains("Python 3 (ipykernel)", Status(v));
            Assert.Contains("nbformat 4.5", Status(v));
            Assert.DoesNotContain("CommonMark + GitHub extras", Status(v));

            Click(v.BuildMoreMenu(), "Copy all code");
            Assert.Equal("# # Sales\n\nprint(1)\n", _clip.Text);
            Click(v.BuildMoreMenu(), "Copy as JSON");
            Assert.Equal(Notebook, _clip.Text);
            _page.Post("""{"t":"menu","x":40,"y":40,"href":null,"cell":"0","selection":false}""");
            await Until(() => v.LastStatusMenu?.Items.OfType<MenuItem>().Any(i => Equals(i.Header, "Copy the cell's code")) == true);
            Click(v.LastStatusMenu!, "Copy the cell's code");
            Assert.Equal("print(1)", _clip.Text);
            Assert.DoesNotContain(v.LastStatusMenu!.Items.OfType<MenuItem>(), i => Equals(i.Header, "Copy as Markdown"));
            v.LastStatusMenu!.Hide();

            var shown = _page.Shown.Count;
            Click(v.BuildMoreMenu(), "Hide code");
            Assert.True(w.Services.Settings.Current.Viewers.NotebookHideCode);
            await Until(() => _page.Shown.Count > shown);
            Assert.Contains("<body class=\"hidecode\">", _page.Shown[^1].Html, StringComparison.Ordinal);
            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { NotebookHideCode = false } });

            Press(v, Key.F5);
            Assert.False(v.IsPage);
            Assert.Equal(["Rendered", "JSON", "Hex"], v.ModeOrder);

            Press(v, Key.Space);
            await Until(() => v.FilePath.EndsWith("broken.ipynb", StringComparison.Ordinal) && v.Document is not null && !v.IsNotebook);
            Assert.False(v.IsPage);
            Assert.Equal(["Text", "Hex"], v.ModeOrder);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Text_IsColouredByTheFilesKind_AndAFileOverItsLargest_OpensAsText_WithOpenAnyway()
    {
        File.WriteAllText(Path.Join(_dir, "settings.json"), "{ \"a\": 1 }");
        File.WriteAllText(Path.Join(_dir, "big.md"), "# Big\n\n" + new string('x', 1_200_000));
        var w = await Open("settings.json");
        var masks = w.Services.Settings.Current.Viewers.Masks;
        try
        {
            Press(w, Key.F3);
            await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Document is not null);
            var v = w.Viewers[^1];
            Assert.Equal(SyntaxLanguage.Json, v.TextView.Language);
            await Until(() => v.TextView.Syntax?.IsExact == true);
            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { TextColors = false } });
            await Until(() => v.TextView.Language == SyntaxLanguage.None);
            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { TextColors = true } });
            await Until(() => v.TextView.Language == SyntaxLanguage.Json);
            v.Close();
            await Until(() => !w.Viewers.Contains(v));

            var md = masks.FindIndex(m => m.View == ViewerKind.Markdown);
            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { Masks = [.. masks.Select((m, i) => i == md ? m with { LargestMB = 1 } : m)] } });
            await w.LeftPanel.Panel.NavigateAsync(_dir, "big.md");
            var shown = _page.Shown.Count;
            Press(w, Key.F3);
            await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Document is not null && w.Viewers[^1].IsOverLimit);
            v = w.Viewers[^1];
            Assert.False(v.IsPage);
            Assert.False(v.IsHex);
            Assert.Equal(SyntaxLanguage.Markdown, v.TextView.Language);
            Assert.Equal(shown, _page.Shown.Count);

            var anyway = v.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, Salandual.App.Resources.AppStrings.Viewer_OpenAnyway));
            anyway.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => _page.Shown.Count > shown);
            Assert.False(v.IsOverLimit);
            Assert.True(v.IsMarkdown);
        }
        finally
        {
            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { Masks = masks, TextColors = true } });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task EscClosesTheViewer_UnlessSetToAltF4Only()
    {
        var w = await Open("notes.txt");
        try
        {
            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { Close = ViewerClose.AltF4Only } });
            Press(w, Key.F3);
            await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Document is not null);
            var v = w.Viewers[^1];
            Press(v, Key.F, RawInputModifiers.Control);
            Assert.True(v.FindBox.IsVisible);
            Press(v, Key.Escape);
            Press(v, Key.Escape);
            Assert.Contains(v, w.Viewers);
            v.KeyPress(Key.F4, RawInputModifiers.Alt, PhysicalKey.None, null);
            await Until(() => !w.Viewers.Contains(v));

            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { Close = ViewerClose.EscOrAltF4 } });
            Press(w, Key.F3);
            await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Document is not null);
            v = w.Viewers[^1];
            v.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            await Until(() => !w.Viewers.Contains(v));
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task WithoutABrowser_MarkdownShowsAsText()
    {
        var w = await Open(page: false);
        try
        {
            Press(w, Key.F3);
            await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Document is not null);
            var v = w.Viewers[^1];
            Assert.False(v.IsMarkdown);
            Assert.False(v.IsPage);
            Assert.Equal(["Text", "Hex"], v.ModeOrder);
        }
        finally
        {
            w.CloseNow();
        }
    }

    private sealed class FakePage : IPageView
    {
        public List<(string Html, Uri Folder)> Shown { get; } = [];

        public List<string> Scripts { get; } = [];

        public Dictionary<string, string> Results { get; } = new() { ["where"] = "120" };

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
            var name = script.StartsWith("window.__sd && window.__sd.", StringComparison.Ordinal) ? script[27..script.IndexOf('(', StringComparison.Ordinal)] : "";
            return Task.FromResult(Results.TryGetValue(name, out var result) ? result : (string?)"ok");
        }

        public void FocusPage()
        {
        }

        public void Load()
        {
            Loaded?.Invoke();
            Dispatcher.UIThread.RunJobs();
        }

        public void Click(string address)
        {
            Navigating?.Invoke(new Uri(address));
            Dispatcher.UIThread.RunJobs();
        }

        public void Post(string json)
        {
            Message?.Invoke(json);
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
        }
    }

    private sealed class TextClipboard : IClipboardAccess
    {
        public string? Text { get; private set; }

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut) => true;

        public byte[]? Dib { get; private set; }

        public byte[]? Png { get; private set; }

        public bool SetImage(byte[] dib, byte[]? png)
        {
            (Dib, Png) = (dib, png);
            return true;
        }

        public bool SetText(string text)
        {
            Text = text;
            SequenceNumber++;
            return true;
        }

        public void Clear()
        {
        }

        public ClipboardContent? Read() => new();
    }
}
