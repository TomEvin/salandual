// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Viewer.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.LogicalTree;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.App.Viewer;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App;

public partial class MainWindow
{
    private readonly List<ViewerWindow> _viewers = [];

    internal IReadOnlyList<ViewerWindow> Viewers => _viewers;

    internal Func<Uri, bool> OpenWebLink { get; set; } = Salandual.Platform.Windows.Shell.WebLinks.Open;

    internal Func<string, int, Task<IconPixels?>>? VideoThumbnail
    {
        get => _videoThumbnail ?? (_services.IsIsolated ? null : Salandual.Platform.Windows.Shell.ShellIconLoader.LoadThumbnailAsync);
        set => _videoThumbnail = value;
    }

    private Func<string, int, Task<IconPixels?>>? _videoThumbnail;

    internal Func<IPageView>? PageViewFactory
    {
        get => _pageViewFactory ?? (_services.IsIsolated ? null : () => new WebPageView());
        set => _pageViewFactory = value;
    }

    private Func<IPageView>? _pageViewFactory;

    internal ContextMenu? ViewWithMenu { get; private set; }

    private static string? FocusedFile(PanelHost host)
        => host.Panel.FocusedEntry is { IsDirectory: false } entry && host.Panel.Path.Length > 0 ? WindowsPath.Join(host.Panel.Path, entry.Name) : null;

    internal ViewerWindow? OpenViewer(PanelHost host, bool alternate)
    {
        if (FocusedFile(host) is not { } path)
        {
            return null;
        }

        var kind = _services.Settings.Current.Viewers.KindFor(WindowsPath.GetName(path), alternate);
        return OpenViewer(host, path, kind);
    }

    internal ViewerWindow OpenViewer(PanelHost host, string path, ViewerKind kind, bool fromEditor = false, IReadOnlyList<string>? siblings = null, Window? comeBackTo = null, Salandual.Core.Viewer.FindQuery? findText = null)
    {
        var viewers = _services.Settings.Current.Viewers;
        var find = _services.Settings.Current.Find;
        var row = viewers.RowFor(WindowsPath.GetName(path));
        var folder = host.Panel.Path;
        siblings = siblings is null
            ? host.Panel.Items
                .Where(e => !e.IsDirectory && viewers.RowFor(e.Name) == row)
                .Select(e => WindowsPath.Join(folder, e.Name))
                .ToList()
            : [.. siblings.Where(file => viewers.RowFor(WindowsPath.GetName(file)) == row)];
        var request = new ViewerRequest(path, kind)
        {
            Siblings = siblings,
            Clipboard = FileClipboardAccess,
            ShowInPanel = file => _ = ShowInPanelAsync(host, file),
            OpenOptions = category => _ = OpenOptionsAsync(category),
            OpenWeb = uri => OpenWebLink(uri),
            OpenFile = OpenFileAsync,
            Thumbnail = VideoThumbnail,
            PageView = PageViewFactory,
            OpenViewer = file => OpenViewer(host, file, _services.Settings.Current.Viewers.KindFor(WindowsPath.GetName(file), alternate: false)),
            FindText = find.ViewAtMatch || find.ViewerKeepsText ? findText : null,
            GoToFirstMatch = find.ViewAtMatch,
            KeepFindText = find.ViewerKeepsText,
        };
        var window = new ViewerWindow(_services.Settings, request);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = ViewerPosition(window, _viewers.Count);
        if (_services.Settings.Current.Viewers.WindowMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }

        _viewers.Add(window);
        var wasActive = false;
        window.Closing += (_, _) =>
        {
            wasActive = !fromEditor && Viewer.NativeFocus.IsInFront(window);
            if (wasActive)
            {
                (comeBackTo is { IsVisible: true } ? comeBackTo : this).Activate();
            }
        };
        window.Closed += (_, _) =>
        {
            _viewers.Remove(window);
            if (wasActive)
            {
                (comeBackTo is { IsVisible: true } ? comeBackTo : this).Activate();
            }
        };
        window.Show();
        return window;
    }

    internal async Task SmokeViewAsync()
    {
        if (!Diagnostics.StartupTrace.IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_VIEW") is not { Length: > 0 } file || !File.Exists(file))
        {
            return;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var v = OpenViewer(ActivePanel, file, _services.Settings.Current.Viewers.KindFor(WindowsPath.GetName(file), alternate: false));
        var log = new System.Text.StringBuilder();
        var last = "";
        void Note(string when)
        {
            var now = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"pos={v.Position} client={v.ClientSize} frame={v.FrameSize} state={v.WindowState} image={v.IsImage}");
            if (now != last)
            {
                log.Append(System.Globalization.CultureInfo.InvariantCulture, $"view {when} {clock.ElapsedMilliseconds} ms {now}\n");
                last = now;
            }
        }

        v.PositionChanged += (_, _) => Note("moved");
        v.Resized += (_, _) => Note("resized");
        Note("shown");
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(50);
            Note("tick");
        }

        await v.Loading;
        Note("loaded");
        if (v.IsMarkdown)
        {
            var shown = await Task.WhenAny(v.PageShown, Task.Delay(20_000)) == v.PageShown;
            var script = v.PageView is { } page ? await page.RunAsync("typeof window.__sd") : null;
            var headings = v.PageView is { } p ? await p.RunAsync("String(document.querySelectorAll('h1,h2,h3,h4,h5,h6').length)") : null;
            var images = v.PageView is { } q ? await q.RunAsync("Array.from(document.images).filter(i => i.complete && i.naturalWidth > 0).length + '/' + document.images.length") : null;
            log.Append(System.Globalization.CultureInfo.InvariantCulture, $"markdown page shown={shown} {clock.ElapsedMilliseconds} ms script={script} headings={headings} images={images}\n");
            if (Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } folder && v.TryGetPlatformHandle()?.Handle is { } hwnd)
            {
                Directory.CreateDirectory(folder);
                await Task.Delay(800);
                Diagnostics.WindowCapture.Save(hwnd, Path.Combine(folder, "markdown-page.png"));
                _services.Settings.Update(s => s with { Viewers = s.Viewers with { MarkdownContents = true } });
                v.OpenFind();
                v.FindBox.Text = v.Page?.Headings.Skip(1).FirstOrDefault()?.Text;
                await Task.Delay(1200);
                Diagnostics.WindowCapture.Save(hwnd, Path.Combine(folder, "markdown-contents.png"));
            }

            if (v.PageView is { } view)
            {
                await view.RunAsync("document.querySelector('a[href^=\"http\"]').dispatchEvent(new MouseEvent('mouseover', { bubbles: true }))");
                await Task.Delay(300);
                var hover = v.StatusCells.FirstOrDefault()?.Child is Avalonia.Controls.TextBlock t ? t.Text : null;
                view.FocusPage();
                await Task.Delay(300);
                await view.RunAsync("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true }))");
                await Task.Delay(300);
                var findFocus = v.FindBox.IsFocused && Viewer.NativeFocus.IsOn(v);
                v.CloseFind();
                await view.RunAsync("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'F5', bubbles: true }))");
                await Task.Delay(300);
                log.Append(System.Globalization.CultureInfo.InvariantCulture, $"markdown hover={hover} find-focus={findFocus} f5-text={!v.IsPage}\n");
            }
        }

        v.Close();
        Diagnostics.StartupTrace.Append(log.ToString());
    }

    internal async Task SmokeNotebookAsync()
    {
        if (!Diagnostics.StartupTrace.IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_NOTEBOOK") is not { Length: > 0 } file || !File.Exists(file))
        {
            return;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var v = OpenViewer(ActivePanel, file, _services.Settings.Current.Viewers.KindFor(WindowsPath.GetName(file), alternate: false));
        await v.Loading;
        var shown = await Task.WhenAny(v.PageShown, Task.Delay(20_000)) == v.PageShown;
        var log = new System.Text.StringBuilder();
        if (v.PageView is { } page)
        {
            async Task<string?> Ask(string script) => await page.RunAsync(script);
            var cells = await Ask("String(document.querySelectorAll('.cell').length)");
            var pictures = await Ask("Array.from(document.images).filter(i => i.complete && i.naturalWidth > 0).length + '/' + document.images.length");
            var tables = await Ask("String(document.querySelectorAll('.html table').length)");
            var scripts = await Ask("String(document.querySelectorAll('script').length)");
            var folded = await Ask("String(document.querySelectorAll('pre.folded').length)");
            if (Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } folder && v.TryGetPlatformHandle()?.Handle is { } hwnd)
            {
                Directory.CreateDirectory(folder);
                await Task.Delay(800);
                Diagnostics.WindowCapture.Save(hwnd, Path.Combine(folder, "notebook-page.png"));
                foreach (var (selector, name) in new[] { (".html", "table"), ("img.pic", "picture"), ("pre.err", "error"), ("pre.folded", "folded"), (".nocell", "notes") })
                {
                    await Ask($"(document.querySelector('{selector}') || document.body).scrollIntoView({{ block: 'start' }}); window.scrollBy(0, -80)");
                    await Task.Delay(400);
                    Diagnostics.WindowCapture.Save(hwnd, Path.Combine(folder, $"notebook-{name}.png"));
                }
            }

            await Ask("document.querySelector('button.unfold') && document.querySelector('button.unfold').click()");
            var unfolded = await Ask("String(document.querySelectorAll('pre.folded').length)");
            log.Append(System.Globalization.CultureInfo.InvariantCulture, $"notebook page shown={shown} {clock.ElapsedMilliseconds} ms notebook={v.IsNotebook} cells={cells} pictures={pictures} tables={tables} scripts={scripts} folded={folded}->{unfolded}\n");

            _services.Settings.Update(s => s with { Viewers = s.Viewers with { NotebookHideCode = true } });
            for (var i = 0; i < 250 && v.Page?.Html.Contains("<body class=\"hidecode\">", StringComparison.Ordinal) != true; i++)
            {
                await Task.Delay(20);
            }

            await Task.WhenAny(v.PageShown, Task.Delay(10_000));
            var hidden = await Ask("document.body.className") ?? "";
            var codeShown = await Ask("String(Array.from(document.querySelectorAll('pre.src')).filter(p => p.offsetParent !== null && !p.classList.contains('raw')).length)");
            _services.Settings.Update(s => s with { Viewers = s.Viewers with { NotebookHideCode = false } });
            log.Append(System.Globalization.CultureInfo.InvariantCulture, $"notebook hidecode={hidden.Contains("hidecode", StringComparison.Ordinal)} code-shown={codeShown}\n");
        }

        v.Close();
        Diagnostics.StartupTrace.Append(log.ToString());
    }

    internal async Task SmokeTextColorsAsync()
    {
        if (!Diagnostics.StartupTrace.IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_TEXT") is not { Length: > 0 } files)
        {
            return;
        }

        var log = new System.Text.StringBuilder();
        foreach (var file in files.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(File.Exists))
        {
            var v = OpenViewer(ActivePanel, file, ViewerKind.Text);
            await v.Loading;
            for (var i = 0; i < 250 && v.TextView.Syntax is { IsExact: false }; i++)
            {
                await Task.Delay(20);
            }

            if (Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } folder && v.TryGetPlatformHandle()?.Handle is { } hwnd)
            {
                Directory.CreateDirectory(folder);
                await Task.Delay(500);
                Diagnostics.WindowCapture.Save(hwnd, Path.Combine(folder, $"text-{Path.GetExtension(file).TrimStart('.')}.png"));
            }

            log.Append(System.Globalization.CultureInfo.InvariantCulture, $"text {WindowsPath.GetName(file)} colours={v.TextView.Language} exact={v.TextView.Syntax?.IsExact == true}\n");
            v.Close();
        }

        Diagnostics.StartupTrace.Append(log.ToString());
    }

    internal async Task SmokeTreeAsync()
    {
        if (!Diagnostics.StartupTrace.IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_TREE") is not { Length: > 0 } files)
        {
            return;
        }

        var log = new System.Text.StringBuilder();
        foreach (var file in files.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(File.Exists))
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var v = OpenViewer(ActivePanel, file, _services.Settings.Current.Viewers.KindFor(WindowsPath.GetName(file), alternate: false));
            await v.Loading;
            if (Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } folder && v.TryGetPlatformHandle()?.Handle is { } hwnd)
            {
                Directory.CreateDirectory(folder);
                await Task.Delay(500);
                Diagnostics.WindowCapture.Save(hwnd, Path.Combine(folder, $"tree-{Path.GetExtension(file).TrimStart('.')}.png"));
            }

            var tree = v.Tree;
            log.Append(System.Globalization.CultureInfo.InvariantCulture, $"tree {WindowsPath.GetName(file)} shown={v.IsTree} {clock.ElapsedMilliseconds} ms rows={v.TreeView.Rows.Count} values={tree?.Values} levels={tree?.Levels} path={v.SelectedPath} mistake={(v.TreeProblem is { } p ? $"line {p.Line}" : "none")}\n");
            v.Close();
        }

        Diagnostics.StartupTrace.Append(log.ToString());
    }

    internal async Task SmokeHtmlAsync()
    {
        if (!Diagnostics.StartupTrace.IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_HTML") is not { Length: > 0 } file || !File.Exists(file))
        {
            return;
        }

        var opened = new List<Uri>();
        var openWeb = OpenWebLink;
        OpenWebLink = uri =>
        {
            opened.Add(uri);
            return true;
        };
        var hits = 0;
        using var server = new System.Net.HttpListener();
        server.Prefixes.Add("http://localhost:47123/");
        server.Start();
        var serving = Task.Run(async () =>
        {
            var pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
            while (server.IsListening)
            {
                try
                {
                    var context = await server.GetContextAsync();
                    Interlocked.Increment(ref hits);
                    context.Response.ContentType = "image/png";
                    await context.Response.OutputStream.WriteAsync(pixel);
                    context.Response.Close();
                }
                catch (Exception e) when (e is System.Net.HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    break;
                }
            }
        });
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var v = OpenViewer(ActivePanel, file, _services.Settings.Current.Viewers.KindFor(WindowsPath.GetName(file), alternate: false));
            await v.Loading;
            var shown = await Task.WhenAny(v.PageShown, Task.Delay(20_000)) == v.PageShown;
            var ms = clock.ElapsedMilliseconds;
            await Task.Delay(1500);
            const string picture = "(() => { const i = document.getElementById('web'); return i && i.complete && i.naturalWidth > 0 ? 'loaded' : 'not loaded'; })()";
            var page = v.PageView!;
            var script = await page.RunAsync("typeof window.__sd");
            var ran = await page.RunAsync("document.body.dataset.ran || 'no'");
            var local = await page.RunAsync("document.body.dataset.local || 'none'");
            var web = await page.RunAsync(picture);
            var webHits = hits;
            await page.RunAsync("window.chrome.webview.postMessage(JSON.stringify({ t: 'key', key: 'F4' }))");
            await Task.Delay(300);
            var spoofed = !v.IsPage;
            await page.RunAsync("(() => { const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob(['x'])); a.download = 'salandual-smoke-download.txt'; document.body.appendChild(a); a.click(); return 'ok'; })()");
            await Task.Delay(1000);
            var downloads = Viewer.WebViewGuard.DownloadsCancelled;

            var byItself = opened.Count;
            var clicked = "no guard";
            var typed = "no guard";
            if (page is Viewer.WebPageView { Guard: { } guard })
            {
                var at = await page.RunAsync("(() => { const r = document.querySelector('a[href^=\"https\"]').getBoundingClientRect(); return Math.round(r.x + r.width / 2) + ',' + Math.round(r.y + r.height / 2); })()") ?? "0,0";
                var xy = at.Split(',');
                foreach (var type in (ReadOnlySpan<string>)["mousePressed", "mouseReleased"])
                {
                    guard.Input("Input.dispatchMouseEvent", $"{{\"type\":\"{type}\",\"x\":{xy[0]},\"y\":{xy[^1]},\"button\":\"left\",\"clickCount\":1}}");
                }

                await Task.Delay(1000);
                clicked = string.Join(" ", opened.Skip(byItself).Select(u => u.AbsolutePath));
                await page.RunAsync("document.getElementById('box').focus()");
                foreach (var type in (ReadOnlySpan<string>)["keyDown", "keyUp"])
                {
                    guard.Input("Input.dispatchKeyEvent", $"{{\"type\":\"{type}\",\"key\":\" \",\"code\":\"Space\",\"text\":\"{(type == "keyDown" ? " " : "")}\",\"windowsVirtualKeyCode\":32}}");
                }

                await Task.Delay(300);
                typed = await page.RunAsync("JSON.stringify(document.getElementById('box').value)") ?? "none";
            }
            var title = v.HtmlTitle;
            if (Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } folder && v.TryGetPlatformHandle()?.Handle is { } hwnd)
            {
                Directory.CreateDirectory(folder);
                Diagnostics.WindowCapture.Save(hwnd, Path.Combine(folder, "html-page.png"));
            }

            v.ToggleHtmlWeb();
            await v.PageRendering;
            await Task.WhenAny(v.PageShown, Task.Delay(20_000));
            await Task.Delay(500);
            var before = hits;
            var webOff = await page.RunAsync(picture);
            var offHits = hits - before;

            v.ToggleHtmlScripts();
            await v.PageRendering;
            await Task.WhenAny(v.PageShown, Task.Delay(20_000));
            var ranOff = await page.RunAsync("document.body.dataset.ran || 'no'");
            var noscript = await page.RunAsync("(() => { const n = document.querySelector('noscript'); return n && n.getBoundingClientRect().height > 0 ? 'shown' : 'hidden'; })()");
            var bar = v.IsOverLimit;
            Diagnostics.StartupTrace.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"html page shown={shown} {ms} ms script={script} ran={ran} title={title} local={local} web={web} hits={webHits} webOff={webOff} offHits={offHits} scriptsOff: ran={ranOff} noscript={noscript} bar={bar} byItself={byItself} clicked={clicked} typed={typed} spoofed={spoofed} downloadsCancelled={downloads}\n"));
            v.Activate();
            page.FocusPage();
            await Task.Delay(300);
            var avaloniaActive = v.IsActive;
            v.Close();
            await Task.Delay(500);
            Diagnostics.StartupTrace.Append($"html closed: front={IsActive} pageHadKeys={!Viewer.NativeFocus.IsOn(v)} avaloniaActive={avaloniaActive}\n");
        }
        finally
        {
            OpenWebLink = openWeb;
            server.Stop();
            await serving;
        }
    }

    private async Task SmokeViewThenExitAsync()
    {
        await SmokeViewAsync();
        await SmokeNotebookAsync();
        await SmokeTextColorsAsync();
        await SmokeTreeAsync();
        await SmokeHtmlAsync();
        await SmokeEditHtmlAsync();
        await SmokeEditAsync();
        await SmokeFindAsync();
        await SmokeCompareAsync();
        if (Diagnostics.StartupTrace.ExitAfterFirstFrame)
        {
            await SmokeCloseWithWindowsAsync();
            Close();
        }
    }

    private async Task SmokeCloseWithWindowsAsync()
    {
        if (!Diagnostics.StartupTrace.IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_VIEW") is not { Length: > 0 } file || !File.Exists(file))
        {
            return;
        }

        OpenViewer(ActivePanel, file, _services.Settings.Current.Viewers.KindFor(WindowsPath.GetName(file), alternate: false));
        OpenFind(ActivePanel);
        await Task.Delay(1000);
        Closed += (_, _) => Diagnostics.StartupTrace.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"close with windows: left open={OpenChildWindows().Count}\n"));
        Close();
        for (var i = 0; i < 100 && OpenCloseQuestion is null; i++)
        {
            await Task.Delay(50);
        }

        if (OpenCloseQuestion is not { } question)
        {
            Diagnostics.StartupTrace.Append("close with windows: no question\n");
            return;
        }

        await Task.Delay(400);
        if (Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_CAPTURE") is { Length: > 0 } folder && question.TryGetPlatformHandle()?.Handle is { } hwnd)
        {
            Directory.CreateDirectory(folder);
            Diagnostics.WindowCapture.Save(hwnd, Path.Combine(folder, "close-with-windows.png"));
        }

        var text = string.Join(" | ", question.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)));
        Diagnostics.StartupTrace.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"close with windows: asked open={OpenChildWindows().Count} text=\"{text.ReplaceLineEndings(" / ")}\"\n"));
        _closingConfirmed = true;
        question.Close(true);
    }

    internal ContextMenu? ShowViewWith(PanelHost host)
    {
        if (FocusedFile(host) is not { } path)
        {
            return null;
        }

        var at = host.View.NameBounds(host.Panel.Focus) is { } r ? new Point(r.X + 24, r.Bottom) : new Point(40, 40);
        var menu = new ContextMenu
        {
            PlacementTarget = host.View,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(at, new Size(1, 1)),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
        };
        menu.Items.Add(new MenuItem { Header = AppStrings.ViewWith_Title, IsEnabled = false });
        foreach (var (kind, label) in new[] { (ViewerKind.Automatic, AppStrings.ViewerKind_Automatic), (ViewerKind.Text, AppStrings.ViewerKind_Text), (ViewerKind.Hex, AppStrings.ViewerKind_Hex), (ViewerKind.Image, AppStrings.ViewerKind_Image), (ViewerKind.Media, AppStrings.ViewerKind_Media), (ViewerKind.Markdown, AppStrings.ViewerKind_Markdown), (ViewerKind.Notebook, AppStrings.ViewerKind_Notebook), (ViewerKind.Tree, AppStrings.ViewerKind_Tree), (ViewerKind.Html, AppStrings.ViewerKind_Html) })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => OpenViewer(host, path, kind);
            menu.Items.Add(item);
        }

        menu.Open(host.View);
        ViewWithMenu = menu;
        return menu;
    }

    internal PixelPoint ViewerPosition(Window viewer, int open)
    {
        var scale = DesktopScaling;
        var size = new PixelSize((int)(viewer.Width * scale), (int)(viewer.Height * scale));
        var mine = FrameSize ?? ClientSize;
        var step = (int)(28 * scale) * (open % 8);
        var x = Position.X + (((int)(mine.Width * scale) - size.Width) / 2) + step;
        var y = Position.Y + (((int)(mine.Height * scale) - size.Height) / 2) + step;
        if (Screens.ScreenFromWindow(this)?.WorkingArea is { } area)
        {
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - size.Width));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        }

        return new PixelPoint(x, y);
    }

    private async Task ShowInPanelAsync(PanelHost host, string file)
    {
        var folder = WindowsPath.GetParent(file) ?? host.Panel.Path;
        await host.Panel.NavigateAsync(folder, WindowsPath.GetName(file));
        SetActive(Array.IndexOf(_panels, host), focus: true);
        Activate();
    }
}
