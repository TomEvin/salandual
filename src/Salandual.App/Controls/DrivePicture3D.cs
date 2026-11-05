// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/DrivePicture3D.cs

using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Controls;

public sealed class DrivePicture3D : ThemedControl
{
    private const double C = 0.866, S = 0.5;
    private const double Pad = 10;
    private static readonly TimeSpan Enter = TimeSpan.FromMilliseconds(360);

    internal static readonly TimeSpan Grow = TimeSpan.FromMilliseconds(1300), GrowDelay = TimeSpan.FromMilliseconds(200);

    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _shownAt;
    private DriveMedium _medium;
    private double _fraction;
    private SpaceLevel _level;
    private bool _fill, _busy, _animate = true;
    private IBrush _accent = null!, _warning = null!, _danger = null!, _surface = null!;
    private Color _floor;

    private static readonly IBrush Alu = Diagonal(("#F1F3F5", 0), ("#C9CED4", 0.55), ("#A9B0B8", 1));
    private static readonly IBrush AluSide = Across(("#8D949C", 0), ("#6E757D", 1));
    private static readonly IBrush Graphite = Diagonal(("#4A5058", 0), ("#1F2328", 1));
    private static readonly IBrush Pcb = Diagonal(("#245C3E", 0), ("#123323", 1));
    private static readonly IBrush Gold = Across(("#F2D27A", 0), ("#B98F1F", 1));
    private static readonly IBrush ChipTop = Diagonal(("#3A3D42", 0), ("#16181B", 1));
    private static readonly IBrush Plastic = Diagonal(("#5C636B", 0), ("#2B3036", 1));
    private static readonly IBrush DiscFace = Diagonal(("#E8F3FF", 0), ("#F6E6FF", 0.25), ("#E4FFF0", 0.45), ("#FFF6DA", 0.65), ("#E2E9F2", 0.85), ("#C5CDD6", 1));
    private static readonly IBrush Sheen = Across(("#00FFFFFF", 0), ("#B3FFFFFF", 0.5), ("#00FFFFFF", 1));
    private static readonly IBrush Gloss = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#59FFFFFF"), 0), new GradientStop(Color.Parse("#00FFFFFF"), 0.5) },
    }.ToImmutable();
    private static readonly IBrush Platter = new RadialGradientBrush
    {
        Center = new RelativePoint(0.35, 0.35, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.35, 0.35, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.Parse("#C8CDD3"), 0.5), new GradientStop(Color.Parse("#9AA1A9"), 1) },
    }.ToImmutable();
    private static readonly IBrush Base = Solid("#59000000");
    private static readonly IImmutableBrush Spark = Solid("#F4FAFF");
    private static readonly IImmutableBrush Copper = Solid("#73E8C566");
    private static readonly IImmutableBrush Socket = Solid("#E6121417");

    public DrivePicture3D()
    {
        _frames.Tick += (_, _) => Tick();
        ClipToBounds = false;
    }

    public DriveMedium Medium
    {
        get => _medium;
        set
        {
            _medium = value;
            InvalidateVisual();
        }
    }

    public double Fraction
    {
        get => _fraction;
        set
        {
            _fraction = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            InvalidateVisual();
        }
    }

    public SpaceLevel Level
    {
        get => _level;
        set
        {
            _level = value;
            InvalidateVisual();
        }
    }

    public bool Fill
    {
        get => _fill;
        set
        {
            _fill = value;
            InvalidateVisual();
        }
    }

    public bool Busy
    {
        get => _busy;
        set
        {
            if (_busy == value)
            {
                return;
            }

            _busy = value;
            Run();
            InvalidateVisual();
        }
    }

    public bool Animate
    {
        get => _animate;
        set
        {
            _animate = value;
            Run();
            InvalidateVisual();
        }
    }

    internal bool IsMoving => _frames.IsEnabled;

    internal double ShownFraction => _fraction * Ease(Progress(_clock.Elapsed - _shownAt - GrowDelay, Grow));

    public static bool CanDraw(DriveMedium medium) => DrivePicture.CanDraw(medium);

    protected override void LoadTheme()
    {
        _accent = Brush("Accent");
        _warning = Brush("Warning");
        _danger = Brush("Danger");
        _surface = Brush("PanelBackground");
        var dark = _surface is ISolidColorBrush s && ((0.299 * s.Color.R) + (0.587 * s.Color.G) + (0.114 * s.Color.B)) < 128;
        _floor = Color.FromArgb(dark ? (byte)115 : (byte)41, 0, 0, 0);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _shownAt = _clock.Elapsed;
        Run();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _frames.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Run()
    {
        if (_animate && VisualRoot is not null && Moving(_clock.Elapsed))
        {
            _frames.Start();
        }
        else
        {
            _frames.Stop();
        }
    }

    private bool Moving(TimeSpan now)
        => _busy || now - _shownAt < GrowDelay + Grow;

    private void Tick()
    {
        InvalidateVisual();
        if (!Moving(_clock.Elapsed))
        {
            _frames.Stop();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var box = Box(_medium);
        return new Size(box.Width, box.Height);
    }

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var box = Box(_medium);
        var scale = Math.Min(Bounds.Width / box.Width, Bounds.Height / box.Height);
        if (scale <= 0)
        {
            return;
        }

        var now = _clock.Elapsed;
        var since = now - _shownAt;
        var enter = _animate ? Ease(Progress(since, Enter)) : 1;
        var t = _animate ? now.TotalSeconds : 0;
        var frame = new Frame(
            _fill ? (_animate ? ShownFraction : _fraction) : -1,
            _level switch { SpaceLevel.AlmostFull => _danger, SpaceLevel.GettingFull => _warning, _ => _accent },
            _busy,
            _busy && _animate ? (t % 0.7) / 0.7 * 13 : 0,
            _busy && _animate ? (t % 0.9) / 0.9 * 360 : 0,
            _busy && _animate ? (t % 1.6) / 1.6 * 360 : 0,
            !_busy || !_animate || (t % 0.9) >= 0.45 ? 1 : 0.35);

        var fit = Matrix.CreateTranslation(-box.X, -box.Y) * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation((Bounds.Width - (box.Width * scale)) / 2, (Bounds.Height - (box.Height * scale)) / 2);
        using (context.PushOpacity(enter))
        using (context.PushTransform(Matrix.CreateTranslation(0, (1 - enter) * 12) * fit))
        {
            switch (_medium)
            {
                case DriveMedium.HardDisk: HardDisk(context, frame); break;
                case DriveMedium.Ssd: Ssd(context, frame); break;
                case DriveMedium.Nvme: Nvme(context, frame); break;
                case DriveMedium.UsbStick: UsbStick(context, frame); break;
                case DriveMedium.External: External(context, frame); break;
                case DriveMedium.SdCard: SdCard(context, frame); break;
                case DriveMedium.CardReader: CardReader(context, frame); break;
                case DriveMedium.Optical: Disc(context, frame); break;
                case DriveMedium.Network: Network(context, frame); break;
            }
        }
    }

    private readonly record struct Frame(double Used, IBrush UsedBrush, bool Busy, double Run, double Spin, double SheenTurn, double Light);

    private void HardDisk(DrawingContext c, Frame f)
    {
        const double W = 146, D = 101, H = 22;
        var top = Rr(0, 0, W, D, 5);
        Shadow(c, f, -6, -6, W + 12, D + 12);
        Slab(c, top, 0, H, AluSide, Alu, () =>
        {
            using (c.PushGeometryClip(top))
            {
                for (var y = 0.0; y < D; y += 3)
                {
                    c.FillRectangle(Solid("#1FFFFFFF"), new Rect(0, y, W, 1));
                }
            }

            c.DrawGeometry(null, Pen("#1F000000", 1), Rr(6, 6, W - 12, D - 12, 4));
            c.DrawEllipse(Platter, Pen("#2E000000", 1), new Point(58, 50), 42, 42);
            using (Turn(c, f.SheenTurn, 58, 50))
            using (c.PushOpacity(0.8))
            {
                c.DrawEllipse(Sheen, null, new Point(58, 50), 40, 6);
            }

            if (f.Used >= 0)
            {
                Ring(c, 58, 50, 34, 8, f.Used, f.UsedBrush, "#24000000");
            }

            c.DrawEllipse(Solid("#8F969E"), null, new Point(58, 50), 10, 10);
            c.DrawEllipse(Solid("#D7DBDF"), null, new Point(58, 50), 4, 4);
            c.DrawGeometry(Solid("#7C838B"), null, Polygon(118, 84, 118, 76, 72, 52, 68, 58, 114, 86));
            c.DrawEllipse(Solid("#6C737B"), null, new Point(118, 82), 8, 8);
            c.DrawGeometry(Solid("#EBFFFFFF"), null, Rr(106, 10, 34, 56, 2));
            c.FillRectangle(Solid("#9AA1A9"), new Rect(110, 16, 22, 3));
            c.FillRectangle(Solid("#C2C7CC"), new Rect(110, 23, 26, 2));
            c.FillRectangle(Solid("#C2C7CC"), new Rect(110, 28, 18, 2));
            foreach (var (x, y) in new[] { (8.0, 8.0), (W - 8, 8.0), (8.0, D - 8), (W - 8, D - 8) })
            {
                c.DrawEllipse(Solid("#8A9199"), null, new Point(x, y), 2.6, 2.6);
            }
        });
        using (c.PushTransform(SideX(W)))
        {
            c.DrawGeometry(Solid("#1B1E22"), null, Rr(20, 6, 22, 7, 1));
            c.DrawGeometry(Solid("#1B1E22"), null, Rr(48, 6, 34, 7, 1));
            Light(c, f, 90, 11, 2.2, still: true);
        }
    }

    private void Ssd(DrawingContext c, Frame f)
    {
        const double W = 100, D = 70, H = 9;
        var top = Rr(0, 0, W, D, 4);
        Shadow(c, f, -5, -5, W + 10, D + 10);
        Slab(c, top, 0, H, Solid("#2A2E33"), Graphite, () =>
        {
            c.DrawGeometry(Gloss, null, top);
            FillOn(c, top, new Rect(0, 0, W, D), f, across: true);
            c.DrawGeometry(Solid("#F2F4F6F8"), null, Rr(12, 12, 76, 46, 3));
            c.DrawGeometry(Solid("#5C636B"), null, Rr(18, 20, 30, 5, 1));
            c.FillRectangle(Solid("#B8BEC5"), new Rect(18, 30, 50, 3));
            c.FillRectangle(Solid("#B8BEC5"), new Rect(18, 37, 40, 3));
            Mark(c, "SSD", 74, 51, 9, "#5C636B", end: true);
        });
        using (c.PushTransform(SideX(W)))
        {
            c.DrawGeometry(Solid("#0E1012"), null, Rr(10, 2, 14, 5, 1));
            c.DrawGeometry(Solid("#0E1012"), null, Rr(28, 2, 28, 5, 1));
            Light(c, f, 62, 4.5, 1.6);
        }
    }

    private void Nvme(DrawingContext c, Frame f)
    {
        const double W = 120, D = 30, H = 2;
        var board = Rr(0, 0, W, D, 2);
        Shadow(c, f, -4, -4, W + 8, D + 8);
        Slab(c, board, 0, H, Solid("#0E2418"), Pcb, () =>
        {
            c.DrawGeometry(Gloss, null, board);
            for (var i = 0; i < 14; i++)
            {
                if (i != 4)
                {
                    c.FillRectangle(Gold, new Rect(0.5, 2 + (i * 1.9), 6, 1.2));
                }
            }

            c.DrawEllipse(Gold, null, new Point(W - 4, D / 2), 3, 3);
            c.DrawEllipse(Solid("#123323"), null, new Point(W - 4, D / 2), 1.4, 1.4);
            Circuit(c, f, 0.7, [108, 2.6, 6.5, 2.6], [108, 27.3, 6.5, 27.3], [12, 8.3, 6.5, 8.3], [12, 12.1, 6.5, 12.1], [12, 15.9, 6.5, 15.9], [12, 19.7, 6.5, 19.7], [12, 23.5, 6.5, 23.5], [34, 11, 28, 11], [34, 19, 28, 19]);
        });

        Slab(c, Rr(12, 7, 16, 16, 1), H, 2.2, Solid("#0D0F11"), ChipTop, () => c.FillRectangle(Solid("#5C636B"), new Rect(15, 10, 10, 2)));
        for (var i = 0; i < 4; i++)
        {
            var x = 34 + (i * 19);
            var part = f.Used < 0 ? 0 : Math.Clamp((f.Used * 4) - i, 0, 1);
            var chip = Rr(x, 5, 16, 20, 1);
            Slab(c, chip, H, 2.2, Solid("#0D0F11"), ChipTop, () =>
            {
                if (part > 0)
                {
                    FillOn(c, chip, new Rect(x, 5, 16, 20), f with { Used = part }, across: true);
                }
            });
        }

        using (c.PushTransform(Plane(H + 0.1)))
        {
            c.FillRectangle(Solid("#99E9EDF0"), new Rect(34, 27, 20, 1.6));
            Light(c, f, W - 12, 6, 1.5, still: true);
        }
    }

    private void UsbStick(DrawingContext c, Frame f)
    {
        const double W = 64, D = 22, H = 10;
        var body = Rr(0, 0, W, D, 7);
        Shadow(c, f, -18, -4, W + 22, D + 8);
        Slab(c, Rr(-14, 4, 16, 14, 1), 2.5, 5, Solid("#8C939B"), Alu, () =>
        {
            c.FillRectangle(Solid("#2B3036"), new Rect(-11, 7, 4, 3));
            c.FillRectangle(Solid("#2B3036"), new Rect(-11, 12, 4, 3));
        });
        Slab(c, body, 0, H, Solid("#22262B"), Plastic, () =>
        {
            c.DrawGeometry(Gloss, null, body);
            FillOn(c, body, new Rect(0, 0, W, D), f, across: true);
            c.DrawEllipse(Solid("#1B1E22"), null, new Point(W - 8, D / 2), 3.5, 3.5);
            Light(c, f, 8, D / 2, 1.6);
        });
    }

    private void External(DrawingContext c, Frame f)
    {
        const double W = 112, D = 78, H = 18;
        var body = Rr(0, 0, W, D, 12);
        Shadow(c, f, -6, -6, W + 12, D + 12);
        Slab(c, body, 0, H, Solid("#1E2226"), Graphite, () =>
        {
            c.DrawGeometry(Gloss, null, body);
            FillOn(c, body, new Rect(0, 0, W, D), f, across: true);
            c.DrawGeometry(null, Pen("#24FFFFFF", 1), Rr(10, 10, W - 20, D - 20, 8));
            Light(c, f, W - 14, D - 12, 2);
        });
        var cable = new StreamGeometry();
        using (var g = cable.Open())
        {
            var from = new Point((C * W) - (C * 40), (S * W) + (S * 40) - 8);
            g.BeginFigure(from, false);
            g.CubicBezierTo(from + new Vector(20, 4), from + new Vector(26, 20), from + new Vector(34, 40));
            g.EndFigure(false);
        }

        c.DrawGeometry(null, Pen("#4A5058", 4, round: true), cable);
    }

    private void SdCard(DrawingContext c, Frame f)
    {
        const double W = 48, D = 64, H = 3;
        var card = Card(W, D, 10, 3, roundBottom: true);
        Shadow(c, f, -4, -4, W + 8, D + 8);
        Slab(c, card, 0, H, Solid("#151A22"), Graphite, () =>
        {
            FillOn(c, card, new Rect(0, 0, W, D), f, across: false);
            for (var i = 0; i < 6; i++)
            {
                c.DrawGeometry(Gold, null, Rr(12 + (i * 5.5), 3, 3.6, 9, 0.6));
            }

            Circuit(c, f, 0.8, [3.5, D - 4, 3.5, 15, 13.8, 15, 13.8, 12], [44.5, D - 4, 44.5, 15, 41.3, 15, 41.3, 12], [19, 22, 19, 12], [24.5, 22, 24.5, 12], [30, 22, 30, 12], [35.5, 22, 35.5, 12]);
            c.DrawGeometry(Solid("#EBF4F6F8"), null, Rr(6, 22, 36, 30, 2));
            Mark(c, "SD", 24, 42, 11, "#2B3036");
        });
    }

    private void CardReader(DrawingContext c, Frame f)
    {
        const double W = 90, D = 50, H = 16;
        var body = Rr(0, 0, W, D, 6);
        Shadow(c, f, -6, -6, W + 12, D + 12);
        Slab(c, body, 0, H, Solid("#1E2226"), Plastic, () =>
        {
            c.DrawGeometry(Gloss, null, body);
            c.DrawGeometry(Solid("#0A0B0D"), null, Rr(22, 22, 46, 4, 1));
            Light(c, f, W - 10, D - 9, 2);
        });

        const double CardW = 40, CardH = 44;
        var card = Card(CardW, CardH, 8, 2, roundBottom: false);
        using (c.PushTransform(Matrix.CreateTranslation(25, H) * FrontY(24)))
        {
            c.DrawGeometry(Solid("#1B2330"), null, card);
            if (f.Used > 0)
            {
                using (c.PushGeometryClip(card))
                using (c.PushOpacity(0.7))
                {
                    c.FillRectangle(f.UsedBrush, new Rect(0, 0, CardW, CardH * f.Used));
                }
            }

            Circuit(c, f, 0.8, [3.5, CardH - 4, 3.5, 13, 11.7, 13, 11.7, 10], [36.5, CardH - 4, 36.5, 13, 33.7, 13, 33.7, 10], [17.2, 16, 17.2, 10], [22.7, 16, 22.7, 10], [28.2, 16, 28.2, 10]);
            for (var i = 0; i < 5; i++)
            {
                c.DrawGeometry(Gold, null, Rr(10 + (i * 5.5), 3, 3.4, 7, 0.5));
            }

            c.DrawGeometry(Solid("#E6F4F6F8"), null, Rr(6, 16, 28, 18, 2));
        }
    }

    private void Disc(DrawingContext c, Frame f)
    {
        const double R = 58;
        Shadow(c, f, -R - 4, -R - 4, (2 * R) + 8, (2 * R) + 8);
        Slab(c, new EllipseGeometry(new Rect(-R, -R, 2 * R, 2 * R)), 0, 1.6, Solid("#A5ADB6"), DiscFace, () =>
        {
            c.DrawEllipse(Gloss, null, default, R, R);
            using (Turn(c, f.Spin, 0, 0))
            {
                c.DrawGeometry(null, Pen("#595C636B", 5, round: true), Arc(0, -50, 43, -25, 50));
                c.DrawGeometry(null, Pen("#405C636B", 3, round: true), Arc(-30, 40, -48, 14, 50));
                Mark(c, "DATA", 0, -28, 9, "#8C5C636B");
            }

            using (Turn(c, f.SheenTurn, 0, 0))
            using (c.PushOpacity(0.7))
            {
                c.DrawEllipse(Sheen, null, default, R - 4, 8);
            }

            if (f.Used >= 0)
            {
                Ring(c, 0, 0, 44, 12, f.Used, f.UsedBrush, "#1A000000");
            }

            c.DrawEllipse(Solid("#8CFFFFFF"), Pen("#1A000000", 1), default, 18, 18);
            c.DrawEllipse(_surface, Pen("#33000000", 1), default, 7, 7);
        });
    }

    private void Network(DrawingContext c, Frame f)
    {
        const double W = 64, D = 78, H = 92;
        var box = Rr(0, 0, W, D, 4);
        Shadow(c, f, -6, -6, W + 12, D + 12);
        Slab(c, box, 0, H, Solid("#2B3036"), Graphite, () => c.DrawGeometry(Gloss, null, box));

        using (c.PushTransform(new Matrix(1, 0, 0, -1, 0, H) * FrontY(D)))
        {
            c.FillRectangle(Solid("#1F2328"), new Rect(0, 0, W, H));
            c.FillRectangle(Solid("#14FFFFFF"), new Rect(0, H - 3, W, 3));
            for (var i = 0; i < 2; i++)
            {
                var x = 8 + (i * 26);
                var bay = Rr(x, 10, 22, 66, 2);
                c.DrawGeometry(Solid("#3A4047"), null, bay);
                if (f.Used > 0)
                {
                    using (c.PushGeometryClip(bay))
                    using (c.PushOpacity(0.85))
                    {
                        c.FillRectangle(f.UsedBrush, new Rect(x, 10 + (66 * (1 - f.Used)), 22, 66 * f.Used));
                    }
                }

                c.FillRectangle(Solid("#59FFFFFF"), new Rect(x + 4, 14, 14, 2));
                Light(c, f, x + 11, 84, 1.8);
            }
        }

        using (c.PushTransform(SideX(W)))
        {
            c.FillRectangle(Solid("#14FFFFFF"), new Rect(10, H - 20, D - 20, 1.4));
            c.FillRectangle(Solid("#14FFFFFF"), new Rect(10, H - 26, D - 20, 1.4));
        }
    }

    private static Matrix Plane(double z) => new(C, S, -C, S, 0, -z);

    private static Matrix FrontY(double y0) => new(C, S, 0, -1, -C * y0, S * y0);

    private static Matrix SideX(double x0) => new(-C, S, 0, -1, C * x0, S * x0);

    private static void Slab(DrawingContext c, Geometry shape, double z, double h, IBrush side, IBrush top, Action? details = null)
    {
        var step = Math.Max(0.5, h / 24);
        for (var k = 0.0; k <= h; k += step)
        {
            using (c.PushTransform(Plane(z + k)))
            {
                c.DrawGeometry(k < 0.6 ? Base : side, null, shape);
            }
        }

        using (c.PushTransform(Plane(z + h)))
        {
            c.DrawGeometry(top, null, shape);
            details?.Invoke();
        }
    }

    private void Shadow(DrawingContext c, Frame f, double x, double y, double w, double h)
    {
        var spread = 6;
        var shade = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(_floor, 0), new GradientStop(_floor, 0.55), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1) },
        };
        using (c.PushTransform(Plane(0)))
        {
            c.DrawGeometry(shade, null, Rr(x - spread, y - spread, w + (2 * spread), h + (2 * spread), (Math.Min(w, h) / 3) + spread));
        }
    }

    private static void FillOn(DrawingContext c, Geometry shape, Rect r, Frame f, bool across)
    {
        if (f.Used <= 0)
        {
            return;
        }

        using (c.PushGeometryClip(shape))
        using (c.PushOpacity(0.88))
        {
            c.FillRectangle(f.UsedBrush, across ? r.WithWidth(r.Width * f.Used) : new Rect(r.X, r.Bottom - (r.Height * f.Used), r.Width, r.Height * f.Used));
        }
    }

    private static void Ring(DrawingContext c, double cx, double cy, double r, double w, double u, IBrush used, string track)
    {
        var center = new Point(cx, cy);
        c.DrawEllipse(null, Pen(track, w), center, r, r);
        var pen = new ImmutablePen(used.ToImmutable(), w, lineCap: PenLineCap.Round);
        if (u >= 0.995)
        {
            c.DrawEllipse(null, pen, center, r, r);
        }
        else if (u > 0.005)
        {
            var angle = u * 2 * Math.PI;
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(new Point(cx, cy - r), false);
                g.ArcTo(new Point(cx + (r * Math.Sin(angle)), cy - (r * Math.Cos(angle))), new Size(r, r), 0, u > 0.5, SweepDirection.Clockwise);
                g.EndFigure(false);
            }

            c.DrawGeometry(null, pen, arc);
        }
    }

    private void Circuit(DrawingContext c, Frame f, double w, params double[][] paths)
    {
        var lines = paths.Select(Polyline).ToList();
        var trace = new ImmutablePen(Copper, w, lineCap: PenLineCap.Round);
        foreach (var line in lines)
        {
            c.DrawGeometry(null, trace, line);
        }

        if (!f.Busy)
        {
            return;
        }

        ImmutablePen Volt(IImmutableBrush brush, double width) => new(brush, width, new ImmutableDashStyle([3 / width, 10 / width], -f.Run / width), PenLineCap.Round);
        var glow = Volt(new ImmutableSolidColorBrush(_accent is ISolidColorBrush a ? a.Color : Colors.DodgerBlue, 0.35), w * 3.2);
        var core = Volt(Spark, w * 1.2);
        foreach (var line in lines)
        {
            c.DrawGeometry(null, glow, line);
            c.DrawGeometry(null, core, line);
        }
    }

    private void Light(DrawingContext c, Frame f, double x, double y, double r, bool still = false)
    {
        var at = new Point(x, y);
        c.DrawEllipse(Socket, null, at, r * 1.7, r * 1.7);
        var lit = !still && f.Light == 1 && f.Busy;
        using (c.PushOpacity(lit ? 1 : 0.35))
        {
            c.DrawEllipse(_accent, null, at, r, r);
        }

        if (lit)
        {
            c.DrawEllipse(Spark, null, at, r * 0.45, r * 0.45);
        }
    }

    private static DrawingContext.PushedState Turn(DrawingContext c, double degrees, double cx, double cy)
        => c.PushTransform(Matrix.CreateTranslation(-cx, -cy) * Matrix.CreateRotation(degrees * Math.PI / 180) * Matrix.CreateTranslation(cx, cy));

    private void Mark(DrawingContext c, string text, double x, double y, double size, string colour, bool end = false)
    {
        var t = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Bold.FontFamily, FontStyle.Normal, FontWeight.Bold), size, Solid(colour));
        c.DrawText(t, new Point(end ? x - t.Width : x - (t.Width / 2), y - t.Baseline));
    }

    private static Rect Box(DriveMedium medium)
    {
        (double X0, double Y0, double Z0, double X1, double Y1, double Z1)[] parts = medium switch
        {
            DriveMedium.HardDisk => [(-6, -6, 0, 152, 107, 22)],
            DriveMedium.Ssd => [(-5, -5, 0, 105, 75, 9)],
            DriveMedium.Nvme => [(-4, -4, 0, 124, 34, 4.2)],
            DriveMedium.UsbStick => [(-18, -4, 0, 68, 26, 10)],
            DriveMedium.External => [(-6, -6, 0, 118, 84, 18)],
            DriveMedium.SdCard => [(-4, -4, 0, 52, 68, 3)],
            DriveMedium.CardReader => [(-6, -6, 0, 96, 56, 16), (25, 24, 16, 65, 24, 60)],
            DriveMedium.Optical => [(-62, -62, 0, 62, 62, 1.6)],
            DriveMedium.Network => [(-6, -6, 0, 70, 84, 92)],
            _ => [(0, 0, 0, 100, 60, 10)],
        };
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        void Add(double sx, double sy)
        {
            left = Math.Min(left, sx);
            right = Math.Max(right, sx);
            top = Math.Min(top, sy);
            bottom = Math.Max(bottom, sy);
        }

        foreach (var p in parts)
        {
            foreach (var x in new[] { p.X0, p.X1 })
            {
                foreach (var y in new[] { p.Y0, p.Y1 })
                {
                    foreach (var z in new[] { p.Z0, p.Z1 })
                    {
                        Add((C * x) - (C * y), (S * x) + (S * y) - z);
                    }
                }
            }
        }

        if (medium == DriveMedium.External)
        {
            Add((C * 72) + 34, (S * 152) - 8 + 40);
        }

        return new Rect(left - Pad, top - Pad, right - left + (2 * Pad), bottom - top + (2 * Pad));
    }

    internal static double Progress(TimeSpan elapsed, TimeSpan length) => Math.Clamp(elapsed.TotalMilliseconds / length.TotalMilliseconds, 0, 1);

    internal static double Ease(double k)
    {
        if (k <= 0 || k >= 1)
        {
            return Math.Clamp(k, 0, 1);
        }

        static double Bezier(double t, double p1, double p2) => (3 * (1 - t) * (1 - t) * t * p1) + (3 * (1 - t) * t * t * p2) + (t * t * t);
        double lo = 0, hi = 1, mid = k;
        for (var i = 0; i < 24; i++)
        {
            mid = (lo + hi) / 2;
            if (Bezier(mid, 0.1, 0.2) < k)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return Bezier(mid, 0.9, 1);
    }

    private static StreamGeometry Polyline(double[] xy) => Lines(xy, closed: false);

    private static StreamGeometry Polygon(params double[] xy) => Lines(xy, closed: true);

    private static StreamGeometry Lines(double[] xy, bool closed)
    {
        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(new Point(xy[0], xy[1]), closed);
            for (var i = 2; i + 1 < xy.Length; i += 2)
            {
                g.LineTo(new Point(xy[i], xy[i + 1]));
            }

            g.EndFigure(closed);
        }

        return shape;
    }

    private static StreamGeometry Card(double w, double h, double cut, double r, bool roundBottom)
    {
        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(new Point(cut, 0), true);
            g.LineTo(new Point(w - r, 0));
            g.QuadraticBezierTo(new Point(w, 0), new Point(w, r));
            if (roundBottom)
            {
                g.LineTo(new Point(w, h - r));
                g.QuadraticBezierTo(new Point(w, h), new Point(w - r, h));
                g.LineTo(new Point(r, h));
                g.QuadraticBezierTo(new Point(0, h), new Point(0, h - r));
            }
            else
            {
                g.LineTo(new Point(w, h));
                g.LineTo(new Point(0, h));
            }

            g.LineTo(new Point(0, cut));
            g.EndFigure(true);
        }

        return shape;
    }

    private static StreamGeometry Arc(double x0, double y0, double x1, double y1, double r)
    {
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(new Point(x0, y0), false);
            g.ArcTo(new Point(x1, y1), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        return arc;
    }

    private static RectangleGeometry Rr(double x, double y, double w, double h, double r) => new(new Rect(x, y, w, h), r, r);

    private static ImmutableSolidColorBrush Solid(string colour) => new(Color.Parse(colour));

    private static ImmutablePen Pen(string colour, double width, bool round = false)
        => new(Solid(colour), width, lineCap: round ? PenLineCap.Round : PenLineCap.Flat);

    private static IBrush Diagonal(params (string Colour, double At)[] stops) => Gradient(new RelativePoint(1, 1, RelativeUnit.Relative), stops);

    private static IBrush Across(params (string Colour, double At)[] stops) => Gradient(new RelativePoint(1, 0, RelativeUnit.Relative), stops);

    private static IBrush Gradient(RelativePoint end, (string Colour, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = end };
        foreach (var (colour, at) in stops)
        {
            brush.GradientStops.Add(new GradientStop(Color.Parse(colour), at));
        }

        return brush.ToImmutable();
    }
}
