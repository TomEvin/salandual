// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Image.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Imaging;
using Salandual.Platform.Windows.FileSystem;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal sealed partial class ViewerWindow
{
    private readonly ImageView _imageView = new();
    private readonly Grid _imagePane = new();
    private readonly TextBlock _zoomBadge = Badge();
    private readonly TextBlock _imageNote = Badge();
    private readonly Dictionary<string, Task<DecodedImage>> _preload = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _closing = new();
    private PixelSize _preloadTarget;
    private CancellationTokenSource? _imageCancel;
    private DecodedImage? _detailTried;
    private bool _detailBusy;
    private bool _imageMode;
    private bool _isImage;
    private long _imageLength;
    private int _lastStep = 1;
    private WindowState _beforeFullScreen = WindowState.Normal;

    private ToggleButton _imageModeButton = null!;
    private FitRow _imageTools = null!;

    internal FitRow ZoomTools { get; private set; } = null!;
    private ToggleButton _fit = null!;
    private ToggleButton _width = null!;
    private ToggleButton _actual = null!;
    private ZoomBox _zoomBox = null!;
    private Button _copyImage = null!;

    internal Button CopyImageButton => _copyImage;
    private Button _zoomOut = null!;
    private Button _zoomIn = null!;
    private Button _rotateLeft = null!;
    private Button _rotateRight = null!;
    private Button _fullScreen = null!;

    internal ImageView ImageView => _imageView;

    internal ZoomBox ImageZoomBox => _zoomBox;

    internal ToggleButton ActualButton => _actual;

    internal bool IsImage => _imageMode;

    internal Task DetailLoading { get; private set; } = Task.CompletedTask;

    internal string ZoomBadgeText => _zoomBadge.Text ?? "";

    internal string ImageNoteText => _imageNote.IsVisible ? _imageNote.Text ?? "" : "";

    private static TextBlock Badge() => new() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)) };

    private void BuildImagePane()
    {
        _imagePane.Children.Add(_imageView);
        foreach (var (text, left) in new[] { (_zoomBadge, true), (_imageNote, false) })
        {
            _imagePane.Children.Add(new Border
            {
                Child = text,
                Background = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2),
                Margin = new Thickness(12),
                HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
                [!IsVisibleProperty] = text[!IsVisibleProperty],
            });
        }

        _imageView.ViewChanged += OnImageViewChanged;
        ActualThemeVariantChanged += (_, _) => ApplyCanvas();
    }

    private Control BuildImageTools()
    {
        ToggleButton ZoomButton(string text, string key, string tip, ZoomMode mode)
        {
            var b = Toggle(Label(null, text, key), tip);
            b.Click += (_, _) => _imageView.SetMode(mode);
            return b;
        }

        _fit = ZoomButton(AppStrings.Viewer_Fit, "F5", AppStrings.Viewer_FitTip, ZoomMode.Fit);
        _width = ZoomButton(AppStrings.Viewer_Width, "F6", AppStrings.Viewer_WidthTip, ZoomMode.Width);
        _actual = ZoomButton(AppStrings.Viewer_Actual, "F7", AppStrings.Viewer_ActualTip, ZoomMode.Actual);
        var zoomOut = _zoomOut = Tool(Glyph("Icon.ZoomOut"), AppStrings.Viewer_ZoomOutTip);
        zoomOut.Click += (_, _) => _imageView.ZoomBy(0.8);
        var zoomIn = _zoomIn = Tool(Glyph("Icon.ZoomIn"), AppStrings.Viewer_ZoomInTip);
        zoomIn.Click += (_, _) => _imageView.ZoomBy(1.25);
        _zoomBox = new ZoomBox { Margin = new Thickness(2, 0) };
        _zoomBox.Chosen += percent => _imageView.ZoomTo(percent / 100);
        _zoomBox.Done += () => _imageView.Focus();
        var left = _rotateLeft = Tool(Glyph("Icon.RotateLeft"), AppStrings.Viewer_RotateLeftTip);
        left.Click += (_, _) => _imageView.Rotate(-1);
        var right = _rotateRight = Tool(Glyph("Icon.RotateRight"), AppStrings.Viewer_RotateRightTip);
        right.Click += (_, _) => _imageView.Rotate(1);
        var full = _fullScreen = Tool(Glyph("Icon.FullScreen"), AppStrings.Viewer_FullScreenTip);
        full.Click += (_, _) => ToggleFullScreen();
        _imageTools = Row(_fit, _width, _actual, ZoomTools = Group(zoomOut, _zoomBox, zoomIn), Separator(), left, right, full);
        _copyImage = Tool(Glyph("Icon.Copy"), AppStrings.Viewer_CopyImageTip);
        _copyImage.Click += (_, _) => _ = CopyImageAsync();
        return _imageTools;
    }

    private void UpdateImageTools()
    {
        var mode = _imageView.Mode;
        _fit.IsChecked = mode == ZoomMode.Fit;
        _width.IsChecked = mode == ZoomMode.Width;
        _actual.IsChecked = mode == ZoomMode.Actual;
        _zoomBox.Percent = _imageView.Zoom * 100;
    }

    internal static string Percent(double zoom) => ZoomBox.Format(zoom * 100);

    private static ZoomMode ToMode(ImageZoom zoom) => zoom switch
    {
        ImageZoom.Width => ZoomMode.Width,
        ImageZoom.Actual => ZoomMode.Actual,
        _ => ZoomMode.Fit,
    };

    private PixelSize DecodeTarget()
    {
        var size = _content.Bounds.Size;
        if (size.Width < 50 || size.Height < 50)
        {
            size = new Size(Math.Max(400, ClientSize.Width), Math.Max(300, ClientSize.Height - 100));
        }

        var scale = RenderScaling;
        var (w, h) = ((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
        return Options.ImageZoom switch
        {
            ImageZoom.Actual => new PixelSize(0, 0),
            ImageZoom.Width => new PixelSize(w, ImageDecoder.MaxSide),
            _ => new PixelSize(w, h),
        };
    }

    private void CancelImage()
    {
        _imageCancel?.Cancel();
        _imageCancel = null;
    }

    private async Task LoadImage(string path, bool keepTurn)
    {
        CancelImage();
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _imageCancel = cancel;
        var token = cancel.Token;
        DropDocument();
        ShowImageMode();
        _imageNote.Text = AppStrings.Viewer_Decoding;
        _imageNote.IsVisible = _imageView.Image is null;

        var target = DecodeTarget();
        DecodedImage image;
        Bitmap bitmap;
        try
        {
            _imageLength = new FileInfo(WindowsPath.ToExtended(path)).Length;
            image = await (TakePreload(path, target) ?? Task.Run(() => ImageDecoder.Decode(WindowsPath.ToExtended(path), target.Width, target.Height, token), token));
            bitmap = await Task.Run(() => ImageView.ToBitmap(image), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is ImageDecodeException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!token.IsCancellationRequested)
            {
                _imageView.Clear();
                ShowMessage(Loc.Format(AppStrings.Viewer_ImageFailed, WindowsPath.GetName(path), e.Message));
                UpdateToolbar();
            }

            return;
        }

        if (token.IsCancellationRequested)
        {
            bitmap.Dispose();
            return;
        }

        _message.IsVisible = false;
        _content.IsVisible = true;
        _detailTried = null;
        _imageView.SetImage(image, bitmap, ToMode(Options.ImageZoom), keepTurn);
        UpdateToolbar();
        UpdateStatus();
        Preload();
    }

    private void DropDocument()
    {
        if (_document is { } old)
        {
            _document = null;
            _text.Document = null;
            _hex.Document = null;
            old.Dispose();
        }
    }

    private void ShowImageMode()
    {
        if (_findBar.IsVisible)
        {
            CloseFind();
        }

        _imageMode = true;
        _mediaMode = false;
        _treeMode = false;
        _hexMode = false;
        _content.Content = _imageHost;
        _vertical.IsVisible = false;
        _horizontal.IsVisible = false;
        ApplyCanvas();
        UpdateToolbar();
        UpdateStatus();
        Dispatcher.UIThread.Post(() => _imageView.Focus(), DispatcherPriority.Input);
    }

    internal void ShowImage()
    {
        if (!_isImage)
        {
            return;
        }

        if (_imageView.Image is null && !_message.IsVisible)
        {
            _ = LoadAsync(_path, ViewerKind.Image);
            return;
        }

        _message.IsVisible = _imageView.Image is null;
        _content.IsVisible = _imageView.Image is not null;
        ShowImageMode();
    }

    internal async Task ShowHexAsync()
    {
        if (_document is null && (_isImage || _isMedia))
        {
            ExitFullScreen();
            Loading = OpenBytes(_path);
            await Loading;
            return;
        }

        SetMode(true);
    }

    private async Task OpenBytes(string path)
    {
        if (!await OpenDocument(path, Options.Encoding) || !string.Equals(path, _path, StringComparison.Ordinal))
        {
            return;
        }

        SetMode(true, 0);
        _statusTimer.Start();
        UpdateToolbar();
        UpdateStatus();
    }

    private void OnImageViewChanged()
    {
        if (!_imageMode)
        {
            return;
        }

        if (_imageView.NeedsDetail && !_detailBusy && !ReferenceEquals(_detailTried, _imageView.Image))
        {
            DetailLoading = LoadDetail();
        }

        UpdateImageOverlay();
        UpdateImageTools();
        UpdateStatus();
    }

    private async Task LoadDetail()
    {
        if (_imageView.Image is not { } image || _imageCancel is not { } cancel)
        {
            return;
        }

        var path = WindowsPath.ToExtended(_path);
        var token = cancel.Token;
        _detailTried = image;
        _detailBusy = true;
        UpdateImageOverlay();
        try
        {
            var detail = await Task.Run(() => image.IsPreview ? ImageDecoder.DecodeFull(path, token: token) : ImageDecoder.Decode(path, token: token), token);
            var bitmap = await Task.Run(() => ImageView.ToBitmap(detail), token);
            if (!token.IsCancellationRequested && ReferenceEquals(_imageView.Image, image))
            {
                _imageView.SetDetail(detail, bitmap);
            }
            else
            {
                bitmap.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is ImageDecodeException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
        finally
        {
            _detailBusy = false;
            if (ReferenceEquals(_imageView.Image, image))
            {
                UpdateImageOverlay();
                UpdateStatus();
            }
        }
    }

    private void Preload()
    {
        var list = _request.Siblings;
        if (_siblingIndex < 0)
        {
            return;
        }

        var target = DecodeTarget();
        if (target != _preloadTarget)
        {
            _preload.Clear();
            _preloadTarget = target;
        }

        var wanted = new[] { _siblingIndex + _lastStep, _siblingIndex - _lastStep }
            .Where(i => i >= 0 && i < list.Count && Options.KindFor(WindowsPath.GetName(list[i]), alternate: false) == ViewerKind.Image)
            .Select(i => list[i])
            .ToList();
        foreach (var stale in _preload.Keys.Where(k => !wanted.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            _preload.Remove(stale);
        }

        var token = _closing.Token;
        foreach (var path in wanted.Where(p => !_preload.ContainsKey(p)))
        {
            var extended = WindowsPath.ToExtended(path);
            var task = Task.Run(() => ImageDecoder.Decode(extended, target.Width, target.Height, token), token);
            _ = task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            _preload[path] = task;
        }
    }

    private Task<DecodedImage>? TakePreload(string path, PixelSize target)
        => target == _preloadTarget && _preload.Remove(path, out var task) && !task.IsCanceled ? task : null;

    private void UpdateImageOverlay()
    {
        var zoom = Percent(_imageView.Zoom);
        _zoomBadge.Text = _imageView.Mode switch
        {
            ZoomMode.Fit => Loc.Format(AppStrings.Viewer_FitBadge, zoom),
            ZoomMode.Width => Loc.Format(AppStrings.Viewer_WidthBadge, zoom),
            _ => zoom,
        };
        _zoomBadge.IsVisible = _imageView.Image is not null;
        if (_detailBusy)
        {
            _imageNote.Text = AppStrings.Viewer_DecodingFull;
            _imageNote.IsVisible = true;
        }
        else if (_imageView.Image is { IsPreview: true } preview && _imageView.Detail is null)
        {
            _imageNote.Text = Loc.Format(AppStrings.Viewer_EmbeddedPreview, preview.Width, preview.Height);
            _imageNote.IsVisible = true;
        }
        else
        {
            _imageNote.IsVisible = false;
        }
    }

    private void UpdateImageStatus()
    {
        var left = new List<Border>();
        var right = new List<Border>();
        if (_imageView.Image is { } image)
        {
            var size = _imageView.Size;
            Status(left, Loc.Format(AppStrings.Viewer_ImageSize, size.Width, size.Height));
            Status(left, Percent(_imageView.Zoom));
            if (_detailBusy)
            {
                Status(left, AppStrings.Viewer_DecodingFullStatus, busy: true);
            }
            else if (image.IsPreview && _imageView.Detail is null)
            {
                Status(left, Loc.Format(AppStrings.Viewer_EmbeddedPreviewStatus, image.Width, image.Height));
            }

            Status(right, ImageFormats.Name(_path) ?? Loc.Format(AppStrings.Viewer_CameraRaw, Path.GetExtension(_path).TrimStart('.').ToUpperInvariant()), strong: true);
            Status(right, DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture).Size(_imageLength));
        }

        ShowStatus(left, right);
    }

    private void ApplyCanvas()
    {
        var o = Options;
        _imageView.Background = o.ImageCanvas switch
        {
            CanvasColor.Black => Brushes.Black,
            CanvasColor.Grey => new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A)),
            CanvasColor.Magenta => new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0xFF)),
            CanvasColor.Theme => this.TryFindResource("Brush.PanelBackground", ActualThemeVariant, out var brush) ? brush as IBrush : null,
            CanvasColor.Custom when ColorMath.TryParseHex(o.CanvasCustom, out var c) => new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)),
            _ => new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
        };
    }

    internal static string CanvasLabel(CanvasColor canvas) => canvas switch
    {
        CanvasColor.Black => AppStrings.Viewer_Canvas_Black,
        CanvasColor.Grey => AppStrings.Viewer_Canvas_Grey,
        CanvasColor.Magenta => AppStrings.Viewer_Canvas_Magenta,
        CanvasColor.Theme => AppStrings.Viewer_Canvas_Theme,
        CanvasColor.Custom => AppStrings.Common_Custom,
        _ => AppStrings.Viewer_Canvas_Dark,
    };

    private bool ImageKey(Key key, KeyModifiers mods)
    {
        var plain = mods == KeyModifiers.None;
        var page = 0.1 * Math.Max(_imageView.Bounds.Width, _imageView.Bounds.Height);
        switch (key)
        {
            case Key.F5 when plain: _imageView.SetMode(ZoomMode.Fit); return true;
            case Key.F6 when plain: _imageView.SetMode(ZoomMode.Width); return true;
            case Key.F7 when plain: _imageView.SetMode(ZoomMode.Actual); return true;
            case Key.Add or Key.OemPlus: _imageView.ZoomBy(1.25); return true;
            case Key.Subtract or Key.OemMinus: _imageView.ZoomBy(0.8); return true;
            case Key.L when plain: _imageView.Rotate(-1); return true;
            case Key.R when plain: _imageView.Rotate(1); return true;
            case Key.H when plain: _imageView.Flip(horizontally: true); return true;
            case Key.V when plain: _imageView.Flip(horizontally: false); return true;
            case Key.F11 when plain: ToggleFullScreen(); return true;
            case Key.E when plain: ToggleInfo(); return true;
            case Key.Escape when plain && WindowState == WindowState.FullScreen: ExitFullScreen(); return true;
            case Key.C when mods == KeyModifiers.Control: _ = CopyImageAsync(); return true;
            case Key.Left when plain: _imageView.PanBy(new Vector(-page, 0)); return true;
            case Key.Right when plain: _imageView.PanBy(new Vector(page, 0)); return true;
            case Key.Up when plain: _imageView.PanBy(new Vector(0, -page)); return true;
            case Key.Down when plain: _imageView.PanBy(new Vector(0, page)); return true;
            case Key.F or Key.G or Key.A or Key.S when mods == KeyModifiers.Control: return true;
            case Key.F2 or Key.F3 or Key.F8 when plain || mods == KeyModifiers.Shift: return true;
            default: return false;
        }
    }

    internal void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            ExitFullScreen();
            return;
        }

        _beforeFullScreen = WindowState;
        WindowState = WindowState.FullScreen;
        SetChrome(false);
    }

    private void ExitFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _beforeFullScreen;
            SetChrome(true);
        }
    }

    private void SetChrome(bool visible)
    {
        if (Content is Panel { Children: [var titleBar, ..] })
        {
            titleBar.IsVisible = visible;
        }

        _toolbar.IsVisible = visible;
        _status.IsVisible = visible;
        UpdateInfo();
    }

    private static (byte[] Dib, byte[] Png) ClipboardImage(DecodedImage image)
    {
        using var bitmap = ImageView.ToBitmap(image);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return (ImageFormats.ToDib(image), stream.ToArray());
    }

    internal async Task CopyImageAsync()
    {
        if (_imageView.Image is null || _request.Clipboard is not { } clipboard)
        {
            return;
        }

        var path = WindowsPath.ToExtended(_path);
        var (turns, flipH, flipV) = (_imageView.Turns, _imageView.FlippedHorizontally, _imageView.FlippedVertically);
        try
        {
            var (dib, png) = await Task.Run(() =>
            {
                var image = ImageFormats.Turn(ImageDecoder.Decode(path), turns);
                image = flipH ? ImageFormats.Flip(image, horizontally: true) : image;
                image = flipV ? ImageFormats.Flip(image, horizontally: false) : image;
                return ClipboardImage(image);
            });
            ShowStatusNote(clipboard.SetImage(dib, png) ? AppStrings.Viewer_ImageCopied : AppStrings.Viewer_ClipboardBusy);
        }
        catch (Exception e) when (e is ImageDecodeException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            ShowStatusNote(Loc.Format(AppStrings.Viewer_CopyFailed, e.Message));
        }
    }
}
