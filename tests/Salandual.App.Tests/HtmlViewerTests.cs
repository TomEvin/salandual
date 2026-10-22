// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/HtmlViewerTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Salandual.App.Viewer;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class HtmlViewerTests : IDisposable
{
    private const string Report = """
        <!doctype html>
        <html><head><title>Webshop</title><style>body { background: #fbf8f1; }</style></head>
        <body><h1>Orders</h1><a href="other.html#top">other</a> <a href="https://example.org/admin">admin</a>
        <script>document.body.dataset.ran = 'yes';</script></body></html>
        """;

    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-html-").FullName;
    private readonly TextClipboard _clip = new();
    private readonly FakePage _page = new();
    private readonly List<Uri> _web = [];

    public HtmlViewerTests()
    {
        File.WriteAllText(Path.Join(_dir, "a-report.html"), Report);
        File.WriteAllText(Path.Join(_dir, "other.html"), "<html><body><p id=\"top\">Plain, no scripts.</p></body></html>");
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

    private async Task<MainWindow> Open(string focus = "a-report.html")
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.FileClipboardAccess = _clip;
        w.OpenWebLink = uri =>
        {
            _web.Add(uri);
            return true;
        };
        w.PageViewFactory = () => _page;
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

    private static List<string> Status(ViewerWindow v) => [.. v.StatusCells.Select(c => (c.Child as TextBlock)?.Text ?? "")];

    private async Task<ViewerWindow> Viewer(MainWindow w)
    {
        var shown = _page.ShownHtml.Count;
        Press(w, Key.F3);
        await Until(() => w.Viewers.Count > 0 && _page.ShownHtml.Count > shown);
        var v = w.Viewers[^1];
        await Load(v);
        return v;
    }

    private async Task Load(ViewerWindow v)
    {
        _page.Results["title"] = "Webshop";
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
    public async Task F3OnHtml_ShowsTheFileItself_WithItsScripts_RenderedTextHex()
    {
        var w = await Open();
        try
        {
            var v = await Viewer(w);
            Assert.True(v.IsHtml);
            Assert.True(v.IsPage);
            var shown = _page.ShownHtml[^1];
            Assert.Equal(Path.Join(_dir, "a-report.html"), shown.File);
            Assert.Null(shown.Text);
            Assert.True(shown.Scripts);
            Assert.True(shown.WebContent);
            Assert.Empty(_page.Shown);
            Assert.Equal(["Rendered", "Text", "Hex"], v.ModeOrder);
            Assert.Equal("1 / 2 HTML pages", v.Counter.Text);
            var status = Status(v);
            Assert.Equal("Webshop", status[0]);
            Assert.Contains("Scripts run", status);
            Assert.Contains("HTML", status);
            Assert.False(v.IsOverLimit);

            Press(v, Key.F5);
            Assert.False(v.IsPage);
            Press(v, Key.F6);
            Assert.True(v.IsPage);
            Assert.Single(_page.ShownHtml);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task ScriptsOff_APageWithScriptsGetsABar_RunScriptsForThisFile()
    {
        var w = await Open();
        w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { HtmlScripts = false } });
        try
        {
            var v = await Viewer(w);
            Assert.False(_page.ShownHtml[^1].Scripts);
            Assert.True(v.HtmlHasScripts);
            Assert.True(v.IsOverLimit);
            Assert.StartsWith("This page has scripts; they don't run.", v.LimitText, StringComparison.Ordinal);
            Assert.Contains("Scripts off", Status(v));

            Click(v.BuildMoreMenu(), "Run scripts in this file");
            await Until(() => _page.ShownHtml.Count == 2);
            Assert.True(_page.ShownHtml[^1].Scripts);
            Assert.False(v.IsOverLimit);
            Assert.False(w.Services.Settings.Current.Viewers.HtmlScripts);

            Press(v, Key.Space);
            await Until(() => _page.ShownHtml.Count == 3);
            Assert.False(_page.ShownHtml[^1].Scripts);
            Assert.False(v.HtmlHasScripts);
            Assert.False(v.IsOverLimit);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task ShowWebContent_CopyTheSource_AndLinks()
    {
        var w = await Open();
        w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { HtmlWebContent = false } });
        try
        {
            var v = await Viewer(w);
            Assert.False(_page.ShownHtml[^1].WebContent);
            Assert.Contains("Web content off", Status(v));
            Click(v.BuildMoreMenu(), "Show web content in this file");
            await Until(() => _page.ShownHtml.Count == 2);
            Assert.True(_page.ShownHtml[^1].WebContent);

            Click(v.BuildMoreMenu(), "Copy the source");
            Assert.Equal(Report, _clip.Text);

            _page.Click("https://example.org/admin");
            Assert.Equal([new Uri("https://example.org/admin")], _web);
            _page.Click("file:///" + _dir.Replace('\\', '/') + "/other.html#top");
            await Until(() => _page.ShownHtml.Count == 3);
            Assert.EndsWith("other.html", _page.ShownHtml[^1].File, StringComparison.Ordinal);
            Assert.Single(v.BackStack);
            await Load(v);
            Press(v, Key.Left, RawInputModifiers.Alt);
            await Until(() => _page.ShownHtml.Count == 4);
            Assert.EndsWith("a-report.html", _page.ShownHtml[^1].File, StringComparison.Ordinal);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [Fact]
    public void HasScripts_AScriptOrAnEventHandler()
    {
        Assert.True(ViewerWindow.HasScripts("<p>x</p><SCRIPT src=a.js></SCRIPT>"));
        Assert.True(ViewerWindow.HasScripts("<button onclick=\"go()\">Go</button>"));
        Assert.False(ViewerWindow.HasScripts("<p>Write onclick= in the text: it is only text.</p>"));
        Assert.False(ViewerWindow.HasScripts("<html><body><p>Plain.</p></body></html>"));
    }

    [Fact]
    public void WithBase_GoesInTheHead_ElseAfterTheDoctype()
    {
        var folder = new Uri(@"C:\Pages\");
        Assert.Equal("<html><head lang=\"en\"><base href=\"file:///C:/Pages/\"><title>t</title></head></html>",
            PageScript.WithBase("<html><head lang=\"en\"><title>t</title></head></html>", folder));
        Assert.Equal("<!DOCTYPE html><base href=\"file:///C:/Pages/\"><p>x</p>", PageScript.WithBase("<!DOCTYPE html><p>x</p>", folder));
        Assert.Equal("<base href=\"file:///C:/Pages/\"><header>x</header>", PageScript.WithBase("<header>x</header>", folder));
    }

    private sealed class TextClipboard : IClipboardAccess
    {
        public string? Text { get; private set; }

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut) => true;

        public bool SetImage(byte[] dib, byte[]? png) => true;

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

    private sealed class FakePage : IPageView
    {
        public List<(string Html, Uri Folder)> Shown { get; } = [];

        public List<(string File, string? Text, bool Scripts, bool WebContent)> ShownHtml { get; } = [];

        public Dictionary<string, string> Results { get; } = new() { ["where"] = "0" };

        public Control Control { get; } = new Border();

        public event Action? Loaded;

        public event Action<Uri>? Navigating;

        public event Action<string>? Message;

        public void Show(string html, Uri folder) => Shown.Add((html, folder));

        public void ShowHtml(string file, string? text, bool scripts, bool webContent) => ShownHtml.Add((file, text, scripts, webContent));

        public Task<string?> RunAsync(string script)
        {
            var name = script == "document.title" ? "title"
                : script.StartsWith("window.__sd && window.__sd.", StringComparison.Ordinal) ? script[27..script.IndexOf('(', StringComparison.Ordinal)] : "";
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
}
