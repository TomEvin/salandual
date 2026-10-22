// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ImageView.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Salandual.Imaging;

namespace Salandual.App.Viewer;

internal enum ZoomMode { Fit, Width, Actual, Free }

internal sealed class ImageView : Control
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<ImageView>();

    private const double MinZoom = 0.01, MaxZoom = 64, Step = 1.25;

    private Bitmap? _bitmap;
    private DecodedImage? _image;
    private Bitmap? _detailBitmap;
    private DecodedImage? _detail;
    private double _zoom = 1;
    private Vector _pan;
    private Point? _dragFrom;

    static ImageView()
    {
        AffectsRender<ImageView>(BackgroundProperty);
        FocusableProperty.OverrideDefaultValue<ImageView>(true);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public event Action? ViewChanged;

    public DecodedImage? Image => _image;

    public DecodedImage? Detail => _detail;

    public ZoomMode Mode { get; private set; } = ZoomMode.Fit;

    public int Turns { get; private set; }

    public bool FlippedHorizontally { get; private set; }

    public bool FlippedVertically { get; private set; }

    public PixelSize Size => _image is not { } i ? default : Turns % 2 == 0 ? new PixelSize(i.FullWidth, i.FullHeight) : new PixelSize(i.FullHeight, i.FullWidth);

    public double Zoom => _zoom;

    public Vector Pan => _pan;

    private double Scaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    public bool NeedsDetail => _image is { IsReduced: true } image && _detail is null && _zoom > image.Density * 1.02;

    public void SetImage(DecodedImage image, Bitmap bitmap, ZoomMode mode, bool keepTurn = false)
    {
        var old = (_bitmap, _detailBitmap);
        (_image, _bitmap, _detail, _detailBitmap) = (image, bitmap, null, null);
        old._bitmap?.Dispose();
        old._detailBitmap?.Dispose();
        if (!keepTurn)
        {
            Turns = 0;
            FlippedHorizontally = FlippedVertically = false;
        }

        SetMode(mode);
    }

    public void SetDetail(DecodedImage detail, Bitmap bitmap)
    {
        _detailBitmap?.Dispose();
        (_detail, _detailBitmap) = (detail, bitmap);
        Changed();
    }

    public void Clear()
    {
        _bitmap?.Dispose();
        _detailBitmap?.Dispose();
        (_image, _bitmap, _detail, _detailBitmap) = (null, null, null, null);
        Changed();
    }

    public void SetMode(ZoomMode mode)
    {
        Mode = mode;
        _pan = default;
        _zoom = mode switch
        {
            ZoomMode.Fit => FitZoom(width: false),
            ZoomMode.Width => FitZoom(width: true),
            ZoomMode.Actual => 1,
            _ => _zoom,
        };
        if (mode is ZoomMode.Width or ZoomMode.Actual)
        {
            var (w, h) = Shown();
            _pan = new Vector((Bounds.Width - w) / 2, (Bounds.Height - h) / 2);
        }

        Changed();
    }

    private double FitZoom(bool width)
    {
        var size = Size;
        if (size.Width == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return 1;
        }

        var x = Bounds.Width * Scaling / size.Width;
        var y = Bounds.Height * Scaling / size.Height;
        return Math.Min(1, width ? x : Math.Min(x, y));
    }

    public void ZoomBy(double factor, Point? around = null)
    {
        if (_image is null)
        {
            return;
        }

        var at = around ?? Bounds.Center;
        var next = Math.Clamp(_zoom * factor, Math.Min(MinZoom, FitZoom(false)), MaxZoom);
        if ((_zoom < 1 && next > 1) || (_zoom > 1 && next < 1))
        {
            next = 1;
        }

        var center = Bounds.Center - _pan;
        var offset = at - center;
        var ratio = next / _zoom;
        _zoom = next;
        Mode = ZoomMode.Free;
        var newCenter = at - (offset * ratio);
        _pan = Bounds.Center - newCenter;
        Changed();
    }

    public void ZoomTo(double zoom)
    {
        if (_image is null || !(zoom > 0))
        {
            return;
        }

        var next = Math.Clamp(zoom, MinZoom, MaxZoom);
        var ratio = next / _zoom;
        _zoom = next;
        Mode = ZoomMode.Free;
        _pan *= ratio;
        Changed();
    }

    public void PanBy(Vector by)
    {
        _pan += by;
        Changed();
    }

    public void Rotate(int quarterTurns)
    {
        var turn = FlippedHorizontally != FlippedVertically ? -quarterTurns : quarterTurns;
        Turns = (((Turns + turn) % 4) + 4) % 4;
        SetMode(Mode == ZoomMode.Free ? ZoomMode.Fit : Mode);
    }

    public void Flip(bool horizontally)
    {
        if (horizontally)
        {
            FlippedHorizontally = !FlippedHorizontally;
        }
        else
        {
            FlippedVertically = !FlippedVertically;
        }

        Changed();
    }

    private (double Width, double Height) Shown()
    {
        var size = Size;
        var dips = _zoom / Scaling;
        return (size.Width * dips, size.Height * dips);
    }

    private void ClampPan()
    {
        var (w, h) = Shown();
        var maxX = Math.Max(0, (w - Bounds.Width) / 2);
        var maxY = Math.Max(0, (h - Bounds.Height) / 2);
        _pan = new Vector(Math.Clamp(_pan.X, -maxX, maxX), Math.Clamp(_pan.Y, -maxY, maxY));
    }

    private void Changed()
    {
        ClampPan();
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (Mode is ZoomMode.Fit or ZoomMode.Width)
        {
            var pan = _pan;
            SetMode(Mode);
            if (Mode == ZoomMode.Width && e.PreviousSize.Height > 0)
            {
                _pan = new Vector(0, pan.Y);
                Changed();
            }
        }
        else
        {
            Changed();
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Background is { } background)
        {
            context.FillRectangle(background, bounds);
        }

        if (_image is null || _bitmap is null)
        {
            return;
        }

        var (bitmap, image) = _detailBitmap is not null && _detail is not null && _zoom > _image.Density * 1.02 ? (_detailBitmap, _detail) : (_bitmap, _image);
        var (w, h) = Shown();
        var (uw, uh) = Turns % 2 == 0 ? (w, h) : (h, w);
        var center = bounds.Center - _pan;
        var transform = Matrix.CreateTranslation(-uw / 2, -uh / 2) * Matrix.CreateRotation(Turns * Math.PI / 2) * Matrix.CreateScale(FlippedHorizontally ? -1 : 1, FlippedVertically ? -1 : 1) * Matrix.CreateTranslation(center.X, center.Y);
        var pixelsPerScreenPixel = image.Width / (uw * Scaling);
        var quality = pixelsPerScreenPixel > 1.01 ? BitmapInterpolationMode.HighQuality
            : pixelsPerScreenPixel < 0.25 ? BitmapInterpolationMode.None
            : BitmapInterpolationMode.MediumQuality;
        using (context.PushClip(bounds))
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = quality }))
        using (context.PushTransform(transform))
        {
            context.DrawImage(bitmap, new Rect(0, 0, image.Width, image.Height), new Rect(0, 0, uw, uh));
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y != 0)
        {
            ZoomBy(Math.Pow(Step, Math.Sign(e.Delta.Y)), e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        if (e.ClickCount == 2)
        {
            if (Mode == ZoomMode.Fit)
            {
                ZoomBy(1 / _zoom, e.GetPosition(this));
            }
            else
            {
                SetMode(ZoomMode.Fit);
            }

            e.Handled = true;
            return;
        }

        _dragFrom = e.GetPosition(this);
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is { } from)
        {
            var at = e.GetPosition(this);
            _dragFrom = at;
            PanBy(from - at);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragFrom = null;
        Cursor = null;
    }

    private void EndDrag(IPointer pointer)
    {
        if (_dragFrom is not null)
        {
            _dragFrom = null;
            pointer.Capture(null);
            Cursor = null;
        }
    }

    public static unsafe Bitmap ToBitmap(DecodedImage image)
    {
        fixed (byte* p = image.Pixels)
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, (nint)p, new PixelSize(image.Width, image.Height), new Vector(96, 96), image.Width * 4);
        }
    }
}
