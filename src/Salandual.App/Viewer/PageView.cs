// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/PageView.cs

using System.Text;
using System.Text.Json;
using Avalonia.Controls;

namespace Salandual.App.Viewer;

internal interface IPageView : IDisposable
{
    Control Control { get; }

    event Action? Loaded;

    event Action<Uri>? Navigating;

    event Action<string>? Message;

    void Show(string html, Uri folder);

    void ShowHtml(string file, string? text, bool scripts, bool webContent);

    Task<string?> RunAsync(string script);

    void FocusPage();
}

internal static partial class PageScript
{
    public const string Install = """
        (() => {
          if (window.top !== window) return 'frame';
          if (window.__sd) { window.__sd.report(); return 'again'; }
          // Put in before an HTML page's own scripts (M5 step 4): the browser's channel and JSON are taken now, and each
          // message carries this window's key, which the page can't read, so a page can't speak for the viewer.
          const channel = window.chrome && window.chrome.webview, send = channel ? channel.postMessage.bind(channel) : null, json = JSON.stringify, key = '{{KEY}}';
          const post = m => { try { m.k = key; send(json(m)); } catch (e) { } };
          // In a page's text box, field or list the keys that type stay there (an HTML page's form).
          const typing = t => t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName));
          const ours = e => {
            const k = e.key, c = e.ctrlKey, a = e.altKey;
            if (/^F\d{1,2}$/.test(k) || k === 'Escape') return true;
            if (typing(e.target) && !(c && /^[fghoprstw]$/i.test(k))) return false;
            if (k === 'Backspace') return true;
            if (k === ' ' && !c && !a) return true;
            if (a && (k === 'ArrowLeft' || k === 'ArrowRight')) return true;
            if (c && !a && /^[fghoprstw0=+\-]$/i.test(k)) return true;
            if (!c && !a && !e.metaKey && (k === 'o' || k === 'O')) return true;
            return false;
          };
          // A real click or key (a page's script can't make one): for a moment after it the page may follow a link.
          const gesture = e => { if (e.isTrusted) post({ t: 'gesture' }); };
          window.addEventListener('pointerdown', gesture, true);
          window.addEventListener('keydown', gesture, true);
          document.addEventListener('keydown', e => {
            if (!ours(e)) return;
            e.preventDefault(); e.stopPropagation();
            post({ t: 'key', key: e.key, ctrl: e.ctrlKey, alt: e.altKey, shift: e.shiftKey });
          }, true);
          document.addEventListener('wheel', e => {
            if (!e.ctrlKey) return;
            e.preventDefault();
            post({ t: 'zoom', by: e.deltaY < 0 ? 1 : -1 });
          }, { passive: false, capture: true });
          let hover = null;
          document.addEventListener('mouseover', e => {
            const a = e.target.closest ? e.target.closest('a[href]') : null;
            const h = a ? a.href : null;
            if (h !== hover) { hover = h; post({ t: 'hover', href: h }); }
          }, true);
          document.addEventListener('contextmenu', e => {
            e.preventDefault();
            const a = e.target.closest ? e.target.closest('a[href]') : null;
            const img = e.target.closest ? e.target.closest('img') : null;
            const pic = img && img.complete && img.naturalWidth > 0 ? (img.currentSrc || img.src) : null;
            const cell = e.target.closest ? e.target.closest('.cell.code[data-cell]') : null;
            post({ t: 'menu', x: e.clientX, y: e.clientY, href: a ? a.href : null, img: pic, cell: cell ? cell.dataset.cell : null, selection: String(getSelection()).length > 0 });
          }, true);
          document.addEventListener('click', e => {
            const b = e.target.closest ? e.target.closest('button.unfold') : null;
            const fold = b ? b.closest('.fold') : null;
            const out = fold ? fold.previousElementSibling : null;
            if (!out) return;
            e.preventDefault();
            const open = !out.classList.toggle('folded');
            fold.firstElementChild.textContent = open ? fold.dataset.all : fold.dataset.more;
            b.textContent = open ? fold.dataset.hide : fold.dataset.show;
          }, true);
          const hidden = n => n.parentElement && n.parentElement.closest('.folded .rest, .hidecode .cell.code > .cb.in, .hidecode .cell.code.noout');
          const heads = () => Array.from(document.querySelectorAll('h1[id],h2[id],h3[id],h4[id],h5[id],h6[id]'));
          let current, pending = false;
          const report = () => {
            pending = false;
            const all = heads();
            let id = all.length ? all[0].id : null;
            for (const h of all) { if (h.getBoundingClientRect().top <= 80) id = h.id; else break; }
            if (window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 2 && all.length) {
              const last = all.filter(h => h.getBoundingClientRect().top < window.innerHeight);
              if (last.length) id = last[last.length - 1].id;
            }
            if (id !== current) { current = id; post({ t: 'heading', id }); }
          };
          window.addEventListener('scroll', () => { if (!pending) { pending = true; requestAnimationFrame(report); } }, { passive: true });
          let marks = [], at = -1;
          const clear = () => {
            for (const m of marks) { const p = m.parentNode; if (p) { p.replaceChild(document.createTextNode(m.textContent), m); p.normalize(); } }
            marks = []; at = -1;
          };
          const state = () => JSON.stringify({ count: marks.length, index: at });
          const show = () => {
            marks.forEach((m, i) => m.className = i === at ? 'sd-hit current' : 'sd-hit');
            if (at >= 0) marks[at].scrollIntoView({ block: 'center' });
            return state();
          };
          // The editor's preview: a click on a block says which line of the file it starts on.
          document.addEventListener('click', e => {
            const el = e.target.closest ? e.target.closest('[data-line]') : null;
            if (el && !(e.target.closest && e.target.closest('a[href]'))) post({ t: 'line', line: +el.dataset.line });
          }, true);
          const flash = document.createElement('style');
          // Find's marks in a page with its own look too (a Markdown page's colours when it has them).
          flash.textContent = '.sd-flash { animation: sdflash .7s ease-out; } @keyframes sdflash { from { background: var(--match, #FCE9A6); } }'
            + ' mark.sd-hit { background: var(--match, #FCE9A6); color: inherit; } mark.sd-hit.current { background: var(--match-cur, #F5B83D); outline: 1px solid #8A8A8A; }';
          const style = () => (document.head || document.documentElement).appendChild(flash);
          if (document.head || document.readyState !== 'loading') style(); else document.addEventListener('DOMContentLoaded', style);
          window.__sd = {
            report,
            // The editor's preview, as you type: the page's body changes in place, the place in it stays, what changed is lit.
            replace: html => {
              const main = document.querySelector('main');
              if (!main) return 'missing';
              const y = window.scrollY;
              // Blocks compared without their line numbers (a new line moves every block under it) and without the last
              // change's light; only the run between the blocks that stayed the same, from the top and from the end, is lit.
              const key = c => c.outerHTML.replace(/ data-line="\d+"/g, '').replace(/ class="sd-flash"/g, '').replace(/ sd-flash/g, '');
              const before = Array.from(main.children, key);
              main.innerHTML = html;
              window.scrollTo(0, y);
              const now = Array.from(main.children);
              const after = now.map(key);
              let top = 0;
              while (top < before.length && top < after.length && before[top] === after[top]) top++;
              let end = 0;
              while (end < before.length - top && end < after.length - top && before[before.length - 1 - end] === after[after.length - 1 - end]) end++;
              const changed = now.slice(top, now.length - end);
              if (changed.length && changed.length < now.length) changed.forEach(c => c.classList.add('sd-flash'));
              setTimeout(() => changed.forEach(c => { c.classList.remove('sd-flash'); if (!c.classList.length) c.removeAttribute('class'); }), 800);
              report();
              return 'ok';
            },
            // Scroll together: the block of line n (from 0) at the top, between two blocks in proportion.
            line: n => {
              const all = Array.from(document.querySelectorAll('main [data-line]'));
              if (!all.length) return 'none';
              let before = null, after = null;
              for (const el of all) { const l = +el.dataset.line; if (l <= n) { if (!before || l >= +before.dataset.line) before = el; } else if (!after || l < +after.dataset.line) after = el; }
              const top = el => el.getBoundingClientRect().top + window.scrollY;
              let y = 0;
              if (before && after) { const a = +before.dataset.line, b = +after.dataset.line; y = top(before) + (top(after) - top(before)) * (n - a) / Math.max(1, b - a); }
              else if (before) y = top(before);
              window.scrollTo(0, Math.max(0, y - 12));
              return 'ok';
            },
            fraction: f => { window.scrollTo(0, f * Math.max(0, document.documentElement.scrollHeight - window.innerHeight)); return 'ok'; },
            // The file has a mistake: the last good preview stays, greyed.
            dim: on => { document.body.style.opacity = on ? '0.45' : ''; return 'ok'; },
            go: id => { const el = document.getElementById(id); if (el) { el.scrollIntoView({ block: 'start' }); return 'ok'; } return 'missing'; },
            zoom: z => { document.body.style.zoom = z; return 'ok'; },
            scroll: (dy, page) => { window.scrollBy(0, page ? dy * window.innerHeight * 0.9 : dy); return 'ok'; },
            top: end => { window.scrollTo(0, end ? document.documentElement.scrollHeight : 0); return 'ok'; },
            where: () => String(window.scrollY),
            to: y => { window.scrollTo(0, y); return 'ok'; },
            selection: () => String(getSelection()),
            selectAll: () => { const r = document.createRange(); r.selectNodeContents(document.querySelector('main') || document.body); const s = getSelection(); s.removeAllRanges(); s.addRange(r); return 'ok'; },
            clear: () => { clear(); return state(); },
            find: (q, matchCase, wholeWords, regex) => {
              clear();
              if (!q) return state();
              const esc = regex ? '(?:' + q + ')' : q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
              let re;
              try { re = new RegExp(wholeWords ? '(?<![\\p{L}\\p{N}_])' + esc + '(?![\\p{L}\\p{N}_])' : esc, matchCase ? 'gu' : 'giu'); }
              catch { return JSON.stringify({ count: 0, index: -1, bad: true }); } // not a regular expression the page can read
              const root = document.querySelector('main') || document.body;
              const walk = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
              const nodes = [];
              for (let n = walk.nextNode(); n; n = walk.nextNode()) nodes.push(n);
              for (const n of nodes) {
                if (hidden(n)) continue; // a folded output's rest, hidden code
                const text = n.nodeValue; re.lastIndex = 0;
                const found = []; let m;
                while ((m = re.exec(text)) !== null) { if (m[0].length === 0) { re.lastIndex++; continue; } found.push([m.index, m[0].length]); }
                if (!found.length) continue;
                const frag = document.createDocumentFragment(); let i = 0;
                for (const [s, l] of found) {
                  if (s > i) frag.appendChild(document.createTextNode(text.slice(i, s)));
                  const mark = document.createElement('mark'); mark.textContent = text.slice(s, s + l);
                  frag.appendChild(mark); marks.push(mark); i = s + l;
                }
                if (i < text.length) frag.appendChild(document.createTextNode(text.slice(i)));
                n.parentNode.replaceChild(frag, n);
              }
              at = marks.findIndex(m => m.getBoundingClientRect().bottom >= 0);
              if (at < 0 && marks.length) at = 0;
              return show();
            },
            next: forward => {
              if (!marks.length) return state();
              at = (at + (forward ? 1 : -1) + marks.length) % marks.length;
              return show();
            }
          };
          report();
          return 'ok';
        })()
        """;

    public static string WithBase(string html, Uri folder)
    {
        var tag = $"<base href=\"{System.Net.WebUtility.HtmlEncode(folder.AbsoluteUri)}\">";
        var head = HeadTag().Match(html);
        if (head.Success)
        {
            return html.Insert(head.Index + head.Length, tag);
        }

        var doctype = DoctypeTag().Match(html);
        return doctype.Success ? html.Insert(doctype.Index + doctype.Length, tag) : tag + html;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"<head(\s[^>]*)?>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex HeadTag();

    [System.Text.RegularExpressions.GeneratedRegex(@"^﻿?\s*<!doctype[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex DoctypeTag();

    public static string Quote(string text) => "\"" + JsonEncodedText.Encode(text) + "\"";

    public static string Call(string function, params object[] arguments)
    {
        var call = new StringBuilder("window.__sd && window.__sd.").Append(function).Append('(');
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                call.Append(',');
            }

            call.Append(arguments[i] switch
            {
                string s => Quote(s),
                bool b => b ? "true" : "false",
                double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                int n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => "null",
            });
        }

        return call.Append(')').ToString();
    }

    public static string? Unwrap(string? value)
    {
        if (value is null || value == "null")
        {
            return null;
        }

        if (value.Length >= 2 && value[0] == '"')
        {
            try
            {
                using var json = JsonDocument.Parse(value);
                return json.RootElement.GetString();
            }
            catch (JsonException)
            {
                return value;
            }
        }

        return value;
    }
}

internal sealed class WebPageView : IPageView
{
    private readonly NativeWebView _web = new();
    private readonly string _key = Guid.NewGuid().ToString("N");
    private const long GestureMs = 2000;
    private readonly List<Uri> _byItself = [];
    private long _gesture = long.MinValue / 2;
    private WebViewGuard? _guard;
    private Action? _pending;
    private bool _adapterReady;
    private bool _allowNext;
    private bool _pageScripts;
    private string? _tempFile;

    public WebPageView()
    {
        _web.EnvironmentRequested += (_, e) =>
        {
            if (e is Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs w)
            {
                var data = Salandual.Core.ProductInfo.GetDataDirectory(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                w.UserDataFolder = Path.Combine(data, "WebView2");
                w.AllowSingleSignOnUsingOSPrimaryAccount = false;
            }
        };
        _web.AdapterCreated += (_, _) =>
        {
            if (_guard is null && _web.TryGetPlatformHandle() is Avalonia.Platform.IWindowsWebView2PlatformHandle { CoreWebView2: not 0 } handle)
            {
                _guard = new WebViewGuard(handle.CoreWebView2);
                _guard.AddScript(Script);
                if (handle.CoreWebView2Controller is not 0 and var controller)
                {
                    _guard.OnEscape(controller, () => Avalonia.Threading.Dispatcher.UIThread.Post(() => Message?.Invoke(EscapeMessage)));
                }
                _guard.WentByItself += address =>
                {
                    if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
                    {
                        _byItself.Add(uri);
                        if (_byItself.Count > 32)
                        {
                            _byItself.RemoveAt(0);
                        }
                    }
                };
            }

            _adapterReady = true;
            var pending = _pending;
            _pending = null;
            pending?.Invoke();
        };
        _web.NavigationStarted += (_, e) =>
        {
            if (_allowNext)
            {
                _allowNext = false;
                return;
            }

            e.Cancel = true;
            if (e.Request is { } uri)
            {
                Follow(uri);
            }
        };
        _web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is { } uri)
            {
                Follow(uri);
            }
        };
        _web.NavigationCompleted += async (_, e) =>
        {
            if (e.IsSuccess)
            {
                if (!_pageScripts)
                {
                    await RunAsync(Script);
                }

                Loaded?.Invoke();
            }
        };
        _web.WebMessageReceived += (_, e) =>
        {
            if (PageScript.Unwrap(e.Body) is not { } body || Ours(body) is not { } type)
            {
                return;
            }

            if (type == "gesture")
            {
                _gesture = Environment.TickCount64;
                return;
            }

            Message?.Invoke(body);
        };
    }

    public event Action? Loaded;

    public event Action<Uri>? Navigating;

    public event Action<string>? Message;

    public Control Control => _web;

    internal WebViewGuard? Guard => _guard;

    private string Script => PageScript.Install.Replace("{{KEY}}", _key, StringComparison.Ordinal);

    private const string EscapeMessage = """{"t":"key","key":"Escape"}""";

    public void Show(string html, Uri folder) => Go(() =>
    {
        _guard?.Scripts(true);
        _guard?.BlockWeb(false);
        _pageScripts = false;
        Navigate(new Uri(WriteTemp(PageScript.WithBase(html, folder), ".html")));
    });

    public void ShowHtml(string file, string? text, bool scripts, bool webContent) => Go(() =>
    {
        _guard?.Scripts(scripts);
        _guard?.BlockWeb(!webContent);
        _pageScripts = scripts;
        if (text is null)
        {
            DeleteTemp();
            Navigate(new Uri(file));
            return;
        }

        var folder = Path.GetDirectoryName(file) is { Length: > 0 } dir ? dir : file;
        var extension = Path.GetExtension(file) is { Length: > 1 } ext ? ext : ".html";
        Navigate(new Uri(WriteTemp(PageScript.WithBase(text, new Uri(folder.EndsWith('\\') ? folder : folder + "\\")), extension)));
    });

    public async Task<string?> RunAsync(string script)
    {
        try
        {
            return PageScript.Unwrap(await _web.InvokeScript(script));
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            return null;
        }
    }

    public void FocusPage() => _web.Focus();

    public void Dispose()
    {
        _guard?.Dispose();
        DeleteTemp();
    }

    private void Go(Action show)
    {
        if (!_adapterReady)
        {
            _pending = show;
            return;
        }

        show();
    }

    private void Navigate(Uri uri)
    {
        _allowNext = true;
        _byItself.Clear();
        _web.Navigate(uri);
    }

    private void Follow(Uri uri) => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
    {
        var index = _byItself.IndexOf(uri);
        if (index >= 0)
        {
            _byItself.RemoveAt(index);
            return;
        }

        if (Environment.TickCount64 - _gesture > GestureMs)
        {
            await Task.Delay(250);
            if (Environment.TickCount64 - _gesture > GestureMs + 250)
            {
                return;
            }
        }

        Navigating?.Invoke(uri);
    });

    private string? Ours(string body)
    {
        try
        {
            using var message = JsonDocument.Parse(body);
            var root = message.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("k", out var k) && k.ValueKind == JsonValueKind.String && k.GetString() == _key
                && root.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string WriteTemp(string html, string extension)
    {
        DeleteTemp();
        var temp = App.Temp.NewFile("page", extension);
        File.WriteAllText(temp, html, Encoding.UTF8);
        _tempFile = temp;
        return temp;
    }

    private void DeleteTemp()
    {
        if (_tempFile is { } file)
        {
            _tempFile = null;
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
