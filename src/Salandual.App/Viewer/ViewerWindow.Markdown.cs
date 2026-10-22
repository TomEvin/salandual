// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Markdown.cs

using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Imaging;
using Salandual.Platform.Windows.FileSystem;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal sealed partial class ViewerWindow
{

    private const double ContentsWidth = 230;

    private readonly Grid _pageHost = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), IsVisible = false };
    private readonly Panel _pageCell = new() { Focusable = true };
    private readonly Canvas _pageOverlay = new();
    private readonly Border _pageWaiting = new();
    private readonly Border _menuAnchor = new() { Width = 1, Height = 1 };
    private readonly Border _contents = new() { Width = ContentsWidth };
    private readonly StackPanel _contentsList = new() { Margin = new Thickness(8, 0, 8, 12) };
    private ToggleButton _pageModeButton = null!;
    private ToggleButton _contentsButton = null!;
    private Button _pageZoomOut = null!;
    private Button _pageZoomIn = null!;
    private ZoomBox _pageZoomBox = null!;
    private FitRow _pageTools = null!;
    private Border _findKinds = null!;
    private IPageView? _page;
    private bool _isMarkdown;
    private bool _isNotebook;
    private bool _pageMode;
    private bool _pageReady;
    private (string Path, string Key, MarkdownPage Page)? _rendered;
    private double _pageZoom = 100;
    private bool _webImagesHere;
    private readonly Stack<string> _back = new();
    private string? _hoverLink;
    private string? _currentHeading;
    private string? _pendingAnchor;
    private double? _restoreScroll;
    private TaskCompletionSource _pageShown = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal bool IsPage => _pageMode;

    internal bool IsMarkdown => _isMarkdown;

    internal bool IsNotebook => _isNotebook;

    internal bool PageReady => _pageReady;

    internal MarkdownPage? Page => _rendered is { } r && r.Path == _path ? r.Page : null;

    internal IPageView? PageView => _page;

    internal Border ContentsPanel => _contents;

    internal bool ContentsOnLeft => Grid.GetColumn(_contents) == 0;

    internal double PageZoom => _pageZoom;

    internal ZoomBox PageZoomBox => _pageZoomBox;

    internal FitRow PageZoomTools => _pageTools;

    internal Task PageRendering { get; private set; } = Task.CompletedTask;

    internal Task PageShown => _pageShown.Task;

    internal IReadOnlyCollection<string> BackStack => _back;

    private ToggleButton BuildPageModeButton()
    {
        _pageModeButton = Mode(AppStrings.Viewer_Rendered, "F6");
        _pageModeButton.Click += (_, _) => ShowPage();
        return _pageModeButton;
    }

    private FitRow BuildPageTools()
    {
        _pageZoomOut = Tool(Glyph("Icon.ZoomOut"), AppStrings.Viewer_PageZoomOutTip);
        _pageZoomOut.Click += (_, _) => ZoomPage(-1);
        _pageZoomBox = new ZoomBox { Margin = new Thickness(2, 0) };
        _pageZoomBox.Chosen += SetPageZoom;
        _pageZoomBox.Done += () =>
        {
            _pageCell.Focus();
            _page?.FocusPage();
        };
        _pageZoomIn = Tool(Glyph("Icon.ZoomIn"), AppStrings.Viewer_PageZoomInTip);
        _pageZoomIn.Click += (_, _) => ZoomPage(1);
        _pageTools = Group(_pageZoomOut, _pageZoomBox, _pageZoomIn);
        return _pageTools;
    }

    private ToggleButton BuildContentsButton()
    {
        _contentsButton = Toggle(Label("Icon.Contents", AppStrings.Viewer_Contents, "O"), AppStrings.Viewer_ContentsTip);
        _contentsButton.Click += (_, _) => UpdateViewer(v => v with { MarkdownContents = !v.MarkdownContents });
        return _contentsButton;
    }

    private Control BuildPageHost()
    {
        var side = Tool(Glyph("Icon.SwapSides"), AppStrings.Viewer_ContentsSideTip);
        side.Click += (_, _) => UpdateViewer(v => v with { MarkdownContentsLeft = !v.MarkdownContentsLeft });
        var closes = new TextBlock { Text = AppStrings.Viewer_ContentsCloses, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }
            .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
        var title = new TextBlock { Text = AppStrings.Viewer_Contents, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(closes, Dock.Right);
        var header = new DockPanel { Margin = new Thickness(12, 8, 10, 6), Children = { closes, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { title, side } } } };
        DockPanel.SetDock(header, Dock.Top);
        _contents.Child = new DockPanel { Children = { header, new ScrollViewer { Content = _contentsList, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } } };
        _contents.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        _contents.IsVisible = false;

        var note = new TextBlock { Text = AppStrings.Viewer_PageStartingNote, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center }
            .Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        var ring = new BusyRing { Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center }.Res(BusyRing.BrushProperty, "Brush.Accent");
        _pageWaiting.Child = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { ring, note } };
        _pageWaiting.Res(Border.BackgroundProperty, "Brush.PanelBackground");
        _pageCell.Children.Add(_pageWaiting);
        _pageOverlay.Children.Add(_menuAnchor);
        _pageCell.Children.Add(_pageOverlay);
        Grid.SetColumn(_pageCell, 1);
        _pageHost.Children.Add(_pageCell);
        _pageHost.Children.Add(_contents);
        Grid.SetColumnSpan(_pageHost, 2);
        Grid.SetRowSpan(_pageHost, 2);

        GotFocus += (_, e) =>
        {
            if (_pageMode && e.Source is TextBox)
            {
                NativeFocus.TakeBack(this);
            }
        };
        return _pageHost;
    }

    internal void ShowPage()
    {
        if (!_isMarkdown || _document is null)
        {
            return;
        }

        ShowPageMode();
        if (_isHtml ? _htmlShown != HtmlKey : Page is null || _rendered?.Key != PageOptionsNow().ToString())
        {
            RenderPage(keepScroll: false);
        }
    }

    private void ShowPageMode()
    {
        if (_findBar.IsVisible)
        {
            CloseFind();
        }

        _page ??= CreatePage();
        _pageMode = true;
        _hexMode = false;
        _treeMode = false;
        _imageMode = false;
        _mediaMode = false;
        _horizontal.IsVisible = false;
        _vertical.IsVisible = false;
        _content.IsVisible = false;
        LayoutPage();
        UpdateScriptsBar();
        UpdateToolbar();
        UpdateStatus();
    }

    private void LeavePage()
    {
        _pageMode = false;
        _pageHost.IsVisible = false;
        _content.IsVisible = !_message.IsVisible;
        _hoverLink = null;
        UpdateScriptsBar();
    }

    private void RefreshPage()
    {
        if (!_pageMode)
        {
            return;
        }

        LayoutPage();
        if (_isHtml ? _htmlShown is not null && _htmlShown != HtmlKey : Page is not null && _rendered?.Key != PageOptionsNow().ToString())
        {
            RenderPage(keepScroll: true);
        }
    }

    private IPageView? CreatePage()
    {
        if (_request.PageView?.Invoke() is not { } page)
        {
            return null;
        }

        page.Loaded += OnPageLoaded;
        page.Navigating += uri => Dispatcher.UIThread.Post(() => OnPageLink(uri));
        page.Message += json => Dispatcher.UIThread.Post(() => OnPageMessage(json));
        _pageCell.Children.Insert(0, page.Control);
        return page;
    }

    private void LayoutPage()
    {
        _pageHost.IsVisible = _pageMode;
        if (_page is { } page)
        {
            var c = page.Control;
            c.Width = _pageReady ? double.NaN : 1;
            c.Height = _pageReady ? double.NaN : 1;
            c.HorizontalAlignment = _pageReady ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            c.VerticalAlignment = _pageReady ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        }

        _pageWaiting.IsVisible = !_pageReady;
        var left = Options.MarkdownContentsLeft;
        Grid.SetColumn(_contents, left ? 0 : 2);
        _contents.BorderThickness = left ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0);
        _contents.IsVisible = _pageMode && _pageReady && Options.MarkdownContents && !_isHtml;
    }

    private PageOptions PageOptionsNow()
    {
        return new PageOptions
        {
            Colors = PageLook.Colors(this),
            Dark = PageLook.IsDark(this),
            FullWidth = Options.MarkdownFullWidth,
            Zoom = _pageZoom,
            WebImages = Options.MarkdownWebImages || _webImagesHere,
            HideCode = Options.NotebookHideCode,
            FoldOutputs = Options.NotebookFoldOutputs,
        };
    }

    private void RenderPage(bool keepScroll) => PageRendering = Render(keepScroll);

    private async Task Render(bool keepScroll)
    {
        if (_document is not { } d || _page is not { } page)
        {
            return;
        }

        if (_isHtml)
        {
            await RenderHtml(page, keepScroll);
            return;
        }

        var notebook = _isNotebook;
        var path = _path;
        var options = PageOptionsNow();
        double? scroll = null;
        if (keepScroll && _pageReady && double.TryParse(await page.RunAsync(PageScript.Call("where")), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            scroll = y;
        }

        MarkdownPage rendered;
        try
        {
            var text = await Task.Run(() => d.Text(d.TextStart, d.Length));
            rendered = await Task.Run(() => notebook ? NotebookPage.Render(text, options) : MarkdownPage.Render(text, options));
        }
        catch (NotebookFormatException e)
        {
            if (string.Equals(path, _path, StringComparison.Ordinal) && ReferenceEquals(d, _document))
            {
                _isMarkdown = _isNotebook = false;
                SetMode(false);
                ShowStatusNote(Loc.Format(AppStrings.Viewer_NotNotebook, e.Message));
            }

            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatusNote(Loc.Format(AppStrings.Viewer_CantOpen, WindowsPath.GetName(path), e.Message));
            return;
        }

        if (!string.Equals(path, _path, StringComparison.Ordinal) || !ReferenceEquals(d, _document) || _closing.IsCancellationRequested)
        {
            return;
        }

        _rendered = (path, options.ToString(), rendered);
        _restoreScroll = scroll;
        if (_pageShown.Task.IsCompleted)
        {
            _pageShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var folder = WindowsPath.GetParent(path) ?? path;
        page.Show(rendered.Html, new Uri(folder.EndsWith('\\') ? folder : folder + "\\"));
        BuildContents(rendered.Headings);
        UpdateStatus();
    }

    private void OnPageLoaded() => Dispatcher.UIThread.Post(async () =>
    {
        if (_page is not { } page)
        {
            return;
        }

        _pageReady = true;
        LayoutPage();
        if (_restoreScroll is { } y)
        {
            await page.RunAsync(PageScript.Call("to", y));
        }
        else if (_pendingAnchor is { Length: > 0 } anchor)
        {
            await page.RunAsync(PageScript.Call("go", anchor));
        }

        _restoreScroll = null;
        _pendingAnchor = null;
        if (_isHtml)
        {
            await ReadHtmlTitle(page);
        }

        if (_findBar.IsVisible && _pageMode)
        {
            await FindInPage(forward: true, restart: true);
        }

        UpdateToolbar();
        UpdateStatus();
        if (_pageMode && IsActive)
        {
            page.FocusPage();
        }

        _pageShown.TrySetResult();
    });

    private void BuildContents(IReadOnlyList<MarkdownHeading> headings)
    {
        _contentsList.Children.Clear();
        if (headings.Count == 0)
        {
            _contentsList.Children.Add(new TextBlock { Text = AppStrings.Viewer_NoHeadings, FontSize = 12, Margin = new Thickness(4) }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText"));
            return;
        }

        var top = headings.Min(h => h.Level);
        foreach (var heading in headings)
        {
            var text = new TextBlock { Text = heading.Text, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }
                .Res(TextBlock.ForegroundProperty, heading.Level > top ? "Brush.SecondaryText" : "Brush.Text");
            var item = new Button
            {
                Content = text,
                Tag = heading.Id,
                Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8 + (14 * Math.Min(4, heading.Level - top)), 3, 8, 3),
            };
            item.Classes.Add("vtool");
            ToolTip.SetTip(item, heading.Text);
            item.Click += (_, _) => _ = _page?.RunAsync(PageScript.Call("go", heading.Id));
            _contentsList.Children.Add(item);
        }

        MarkCurrentHeading();
    }

    private void MarkCurrentHeading()
    {
        foreach (var item in _contentsList.Children.OfType<Button>())
        {
            if (Equals(item.Tag, _currentHeading))
            {
                item.Res(Button.BackgroundProperty, "Brush.TextSelection");
            }
            else
            {
                item.ClearValue(Button.BackgroundProperty);
            }
        }
    }

    internal IReadOnlyList<(string Id, bool Current)> ContentsItems
        => [.. _contentsList.Children.OfType<Button>().Select(b => ((string)b.Tag!, Equals(b.Tag, _currentHeading)))];

    private void OnPageMessage(string json)
    {
        try
        {
            using var message = JsonDocument.Parse(json);
            var root = message.RootElement;
            string? Field(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            bool Flag(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
            switch (Field("t"))
            {
                case "key" when PageKeyName(Field("key")) is { } key:
                    var mods = (Flag("ctrl") ? KeyModifiers.Control : 0) | (Flag("alt") ? KeyModifiers.Alt : 0) | (Flag("shift") ? KeyModifiers.Shift : 0);
                    RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = mods, Source = this });
                    break;
                case "zoom":
                    ZoomPage(root.TryGetProperty("by", out var by) && by.TryGetInt32(out var n) ? Math.Sign(n) : 0);
                    break;
                case "hover":
                    _hoverLink = Field("href");
                    UpdateStatus();
                    break;
                case "heading":
                    _currentHeading = Field("id");
                    MarkCurrentHeading();
                    break;
                case "menu":
                    var x = root.TryGetProperty("x", out var px) && px.TryGetDouble(out var dx) ? dx : 0;
                    var y = root.TryGetProperty("y", out var py) && py.TryGetDouble(out var dy) ? dy : 0;
                    var cell = int.TryParse(Field("cell"), NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : (int?)null;
                    ShowPageMenu(new Point(x * _pageZoom / 100, y * _pageZoom / 100), Field("href"), Flag("selection"), Field("img"), cell);
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    internal static Key? PageKeyName(string? name) => name switch
    {
        null or "" => null,
        "Escape" => Key.Escape,
        "Backspace" => Key.Back,
        " " => Key.Space,
        "ArrowLeft" => Key.Left,
        "ArrowRight" => Key.Right,
        "0" => Key.D0,
        "=" or "+" => Key.OemPlus,
        "-" => Key.OemMinus,
        ['F', .. var digits] when int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 12 => Key.F1 + (f - 1),
        [var c] when char.IsAsciiLetter(c) => Key.A + (char.ToUpperInvariant(c) - 'A'),
        _ => null,
    };

    internal void OnPageLink(Uri uri)
    {
        if (uri.OriginalString.StartsWith(MarkdownPage.ShowWebImagesLink, StringComparison.OrdinalIgnoreCase))
        {
            _webImagesHere = true;
            RenderPage(keepScroll: true);
            return;
        }

        if (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        {
            _request.OpenWeb?.Invoke(uri);
            return;
        }

        if (!uri.IsFile)
        {
            return;
        }

        var target = uri.LocalPath;
        var anchor = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        var folder = WindowsPath.GetParent(_path) ?? "";
        var sameFile = string.Equals(target, _path, StringComparison.OrdinalIgnoreCase);
        var thisFolder = string.Equals(target.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        if (sameFile || (thisFolder && anchor.Length > 0))
        {
            if (anchor.Length > 0)
            {
                _ = _page?.RunAsync(PageScript.Call("go", anchor));
            }

            return;
        }

        var name = WindowsPath.GetName(target);
        if (Directory.Exists(WindowsPath.ToExtended(target)))
        {
            _request.ShowInPanel?.Invoke(target);
        }
        else if (!File.Exists(WindowsPath.ToExtended(target)))
        {
            ShowStatusNote(Loc.Format(AppStrings.Viewer_LinkMissing, name));
        }
        else if (_settings.Current.Viewers.KindFor(name, alternate: false) is var kind && IsPageKind(kind))
        {
            _back.Push(_path);
            _pendingAnchor = anchor;
            _siblingIndex = IndexOfPath(_request.Siblings, target);
            _ = LoadAsync(target, kind);
        }
        else
        {
            _request.OpenViewer?.Invoke(target);
        }
    }

    internal void GoBack()
    {
        if (_back.TryPop(out var previous))
        {
            _siblingIndex = IndexOfPath(_request.Siblings, previous);
            var kind = _settings.Current.Viewers.KindFor(WindowsPath.GetName(previous), alternate: false);
            _ = LoadAsync(previous, IsPageKind(kind) ? kind : ViewerKind.Markdown);
        }
    }

    private bool PageKey(Key key, KeyModifiers mods)
    {
        var plain = mods == KeyModifiers.None;
        var ctrl = mods == KeyModifiers.Control;
        void Run(string function, params object[] args) => _ = _page?.RunAsync(PageScript.Call(function, args));
        switch (key)
        {
            case Key.F6 when plain: return true;
            case Key.O when plain && !_isHtml: UpdateViewer(v => v with { MarkdownContents = !v.MarkdownContents }); return true;
            case Key.OemPlus or Key.Add when ctrl: ZoomPage(1); return true;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomPage(-1); return true;
            case Key.D0 or Key.NumPad0 when ctrl: SetPageZoom(100); return true;
            case Key.Left when mods == KeyModifiers.Alt: GoBack(); return true;
            case Key.C when ctrl: _ = CopyPageSelectionAsync(); return true;
            case Key.A when ctrl: Run("selectAll"); return true;
            case Key.G or Key.S or Key.P or Key.W or Key.O when ctrl: return true;
            case Key.F2 or Key.F8 when plain || mods == KeyModifiers.Shift: return true;
            case Key.Up when plain: Run("scroll", -40); return true;
            case Key.Down when plain: Run("scroll", 40); return true;
            case Key.PageUp when plain: Run("scroll", -1, true); return true;
            case Key.PageDown when plain: Run("scroll", 1, true); return true;
            case Key.Home when plain || ctrl: Run("top", false); return true;
            case Key.End when plain || ctrl: Run("top", true); return true;
            default: return false;
        }
    }

    private void ZoomPage(int direction)
    {
        if (direction == 0)
        {
            return;
        }

        var presets = ZoomBox.Presets;
        var next = direction > 0 ? presets.FirstOrDefault(p => p > _pageZoom + 0.001, presets[^1]) : presets.LastOrDefault(p => p < _pageZoom - 0.001, presets[0]);
        SetPageZoom(next);
    }

    internal void SetPageZoom(double percent)
    {
        _pageZoom = Math.Clamp(percent, 5, 6400);
        _ = _page?.RunAsync(PageScript.Call("zoom", _pageZoom / 100));
        if (_rendered is { } r && r.Path == _path)
        {
            _rendered = (r.Path, PageOptionsNow().ToString(), r.Page);
        }

        UpdateToolbar();
    }

    internal async Task CopyPageSelectionAsync()
    {
        if (_page is { } page && _request.Clipboard is { } clipboard && await page.RunAsync(PageScript.Call("selection")) is { Length: > 0 } text)
        {
            clipboard.SetText(text);
        }
    }

    private void CopyMarkdown()
    {
        if (_document is { } d && _request.Clipboard is { } clipboard && d.Length <= CopyLimit)
        {
            clipboard.SetText(d.Text(d.TextStart, d.Length));
        }
    }

    private void CopyAllCode()
    {
        if (Page?.Notebook is { } notebook && _request.Clipboard is { } clipboard)
        {
            clipboard.SetText(notebook.AllCode);
        }
    }

    private void CopyCellCode(int cell)
    {
        if (Page?.Notebook is { } notebook && cell >= 0 && cell < notebook.CellCode.Count && _request.Clipboard is { } clipboard)
        {
            clipboard.SetText(notebook.CellCode[cell]);
        }
    }

    private void CopyPageHtml()
    {
        if (Page is { } page && _request.Clipboard is { } clipboard)
        {
            clipboard.SetText(page.Html);
        }
    }

    internal async Task CopyPageImageAsync(string source)
    {
        if (_request.Clipboard is not { } clipboard)
        {
            return;
        }

        string? temp = null;
        try
        {
            string path;
            if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = source.IndexOf(',', StringComparison.Ordinal);
                if (comma < 0 || !source.AsSpan(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                temp = await PageImageTempAsync(Convert.FromBase64String(source[(comma + 1)..]), PageImageExtension(source[5..comma].Split(';')[0], null));
                path = temp;
            }
            else if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                path = WindowsPath.ToExtended(uri.LocalPath);
            }
            else if (uri is not null && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && PageOptionsNow().WebImages)
            {
                using var response = await PageImageClient.Value.GetAsync(uri, _closing.Token);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(_closing.Token);
                temp = await PageImageTempAsync(bytes, PageImageExtension(response.Content.Headers.ContentType?.MediaType, uri));
                path = temp;
            }
            else
            {
                return;
            }

            var (dib, png) = await Task.Run(() => ClipboardImage(ImageDecoder.Decode(path)));
            ShowStatusNote(clipboard.SetImage(dib, png) ? AppStrings.Viewer_ImageCopied : AppStrings.Viewer_ClipboardBusy);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception e) when (e is ImageDecodeException or IOException or UnauthorizedAccessException or OutOfMemoryException
            or FormatException or HttpRequestException or TaskCanceledException)
        {
            ShowStatusNote(Loc.Format(AppStrings.Viewer_CopyFailed, e.Message));
        }
        finally
        {
            if (temp is not null)
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static readonly Lazy<HttpClient> PageImageClient = new(() => new HttpClient(new SocketsHttpHandler { UseCookies = false, MaxAutomaticRedirections = 5 })
    {
        Timeout = TimeSpan.FromSeconds(15),
        MaxResponseContentBufferSize = 32 * 1024 * 1024,
    });

    private static async Task<string> PageImageTempAsync(byte[] bytes, string extension)
    {
        var temp = App.Temp.NewFile("image", extension);
        await File.WriteAllBytesAsync(temp, bytes);
        return temp;
    }

    private static string PageImageExtension(string? mediaType, Uri? address) => mediaType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/avif" => ".avif",
        "image/bmp" => ".bmp",
        "image/svg+xml" => ".svg",
        "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
        _ => address is not null && Path.GetExtension(address.AbsolutePath) is { Length: > 1 and <= 6 } ext ? ext : ".png",
    };

    private void ShowPageMenu(Point at, string? href, bool selection, string? image = null, int? cell = null)
    {
        Canvas.SetLeft(_menuAnchor, at.X);
        Canvas.SetTop(_menuAnchor, at.Y);
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        MenuItem Item(string header, string? key, Action click, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGesture = key is null ? null : KeyGesture.Parse(key), IsEnabled = enabled };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }

        Item(AppStrings.Common_Copy, "Ctrl+C", () => _ = CopyPageSelectionAsync(), selection && _request.Clipboard is not null);
        Item(AppStrings.Viewer_Menu_SelectAll, "Ctrl+A", () => _ = _page?.RunAsync(PageScript.Call("selectAll")));
        if (image is { Length: > 0 })
        {
            menu.Items.Add(new Separator());
            Item(AppStrings.Viewer_CopyImage, null, () => _ = CopyPageImageAsync(image), _request.Clipboard is not null);
        }

        menu.Items.Add(new Separator());
        if (_isNotebook)
        {
            if (cell is { } index)
            {
                Item(AppStrings.Viewer_CopyCellCode, null, () => CopyCellCode(index), _request.Clipboard is not null && Page?.Notebook is not null);
            }

            Item(AppStrings.Viewer_CopyAllCode, null, CopyAllCode, _request.Clipboard is not null && Page?.Notebook is not null);
            Item(AppStrings.Viewer_CopyJson, null, CopyMarkdown, _request.Clipboard is not null && _document is not null);
        }
        else if (_isHtml)
        {
            Item(AppStrings.Viewer_CopySource, null, CopyMarkdown, _request.Clipboard is not null && _document is not null);
        }
        else
        {
            Item(AppStrings.Viewer_CopyMarkdown, null, CopyMarkdown, _request.Clipboard is not null && _document is not null);
            Item(AppStrings.Viewer_CopyHtml, null, CopyPageHtml, _request.Clipboard is not null && Page is not null);
        }
        if (href is not null && Uri.TryCreate(href, UriKind.Absolute, out var link))
        {
            menu.Items.Add(new Separator());
            Item(AppStrings.Viewer_OpenLink, null, () => OnPageLink(link));
            Item(AppStrings.Viewer_CopyLink, null, () => _request.Clipboard?.SetText(link.IsFile ? link.LocalPath : link.AbsoluteUri), _request.Clipboard is not null);
        }

        menu.ShowAt(_menuAnchor);
        LastStatusMenu = menu;
    }

    private async Task FindInPage(bool forward, bool restart)
    {
        if (_page is not { } page)
        {
            return;
        }

        var text = _findBox.Text ?? "";
        var result = restart || text.Length == 0
            ? await page.RunAsync(PageScript.Call("find", text, _matchCase.IsChecked == true, _wholeWords.IsChecked == true, _regex.IsChecked == true))
            : await page.RunAsync(PageScript.Call("next", forward));
        if (text.Length == 0)
        {
            _hits.Text = "";
            return;
        }

        var (count, index, bad) = (0, -1, false);
        try
        {
            if (result is not null)
            {
                using var state = JsonDocument.Parse(result);
                count = state.RootElement.GetProperty("count").GetInt32();
                index = state.RootElement.GetProperty("index").GetInt32();
                bad = state.RootElement.TryGetProperty("bad", out var b) && b.GetBoolean();
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
        }

        if (bad)
        {
            _hits.Text = AppStrings.Viewer_BadQuery;
            _hits.Res(TextBlock.ForegroundProperty, "Brush.Danger");
            return;
        }

        if (count == 0)
        {
            _hits.Text = AppStrings.Viewer_NoHits;
            _hits.Res(TextBlock.ForegroundProperty, "Brush.Danger");
            return;
        }

        _hits.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _hits.Text = Loc.Format(AppStrings.Viewer_Hits, (index + 1).ToString("N0", CultureInfo.CurrentCulture), count.ToString("N0", CultureInfo.CurrentCulture));
    }

    private void UpdatePageStatus()
    {
        var left = new List<Border>();
        var right = new List<Border>();
        if (_hoverLink is { } link)
        {
            Status(left, LinkNote(link), strong: true);
        }
        else if (_isHtml)
        {
            HtmlStatus(left);
        }
        else if (Page is { Notebook: { } notebook } nbPage)
        {
            Status(left, notebook.CellsText);
            Status(left, Loc.Plural(nbPage.Headings.Count, AppStrings.Viewer_Headings_One, AppStrings.Viewer_Headings_Other));
        }
        else if (Page is { } page)
        {
            Status(left, Loc.Plural(page.Words, AppStrings.Viewer_Words_One, AppStrings.Viewer_Words_Other));
            if (page.Words > 0)
            {
                Status(left, Loc.Format(AppStrings.Viewer_ReadingTime, page.ReadingMinutes));
            }

            Status(left, Loc.Plural(page.Headings.Count, AppStrings.Viewer_Headings_One, AppStrings.Viewer_Headings_Other));
        }

        if (!_pageReady)
        {
            Status(left, AppStrings.Viewer_PageStarting, busy: true);
        }

        if (Page?.Notebook is { } info)
        {
            Status(right, info.Kernel);
            Status(right, Loc.Format(AppStrings.Viewer_NbFormat, info.Format));
        }
        else if (!_isNotebook)
        {
            Status(right, _isHtml ? AppStrings.Viewer_HtmlFlavour : AppStrings.Viewer_MarkdownFlavour);
        }

        if (_document is { } d)
        {
            Status(right, d.Encoding.Name);
            Status(right, DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture).Size(d.Length));
        }

        ShowStatus(left, right);
    }

    private string LinkNote(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            return href;
        }

        if (uri.OriginalString.StartsWith(MarkdownPage.ShowWebImagesLink, StringComparison.OrdinalIgnoreCase))
        {
            return Core.Resources.Strings.Md_ShowWebImages;
        }

        if (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        {
            return Loc.Format(AppStrings.Viewer_LinkWeb, uri.AbsoluteUri);
        }

        if (!uri.IsFile)
        {
            return Loc.Format(AppStrings.Viewer_LinkNotFollowed, uri.Scheme);
        }

        var target = uri.LocalPath;
        var folder = WindowsPath.GetParent(_path) ?? "";
        if (uri.Fragment.Length > 1 && (string.Equals(target, _path, StringComparison.OrdinalIgnoreCase) || string.Equals(target.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
        {
            return Loc.Format(AppStrings.Viewer_LinkHere, Uri.UnescapeDataString(uri.Fragment));
        }

        return IsPageKind(_settings.Current.Viewers.KindFor(WindowsPath.GetName(target), alternate: false))
            ? Loc.Format(AppStrings.Viewer_LinkMarkdown, target)
            : Loc.Format(AppStrings.Viewer_LinkFile, target);
    }
}
