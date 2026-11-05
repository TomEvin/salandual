// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/DrivePicture.cs

using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Controls;

public sealed class DrivePicture : ThemedControl
{
    private const double W = 200, H = 110;
    private DriveMedium _medium;
    private double _fraction;
    private SpaceLevel _level;
    private bool _fill, _animate = true;
    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _shownAt;
    private IBrush _accent = null!, _warning = null!, _danger = null!, _track = null!, _body = null!, _detail = null!, _metal = null!, _label = null!, _labelText = null!;
    private static readonly IBrush _pcb = new ImmutableSolidColorBrush(Color.FromRgb(0x2E, 0x6B, 0x4A));
    private static readonly IBrush _gold = new ImmutableSolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27));
    private static readonly IBrush _chip = new ImmutableSolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));

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

    internal double ShownFraction => _animate ? _fraction * DrivePicture3D.Ease(DrivePicture3D.Progress(_clock.Elapsed - _shownAt - DrivePicture3D.GrowDelay, DrivePicture3D.Grow)) : _fraction;

    public DrivePicture() => _frames.Tick += (_, _) =>
    {
        InvalidateVisual();
        if (!Growing)
        {
            _frames.Stop();
        }
    };

    private bool Growing => _clock.Elapsed - _shownAt < DrivePicture3D.GrowDelay + DrivePicture3D.Grow;

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
        if (_animate && _fill && VisualRoot is not null && Growing)
        {
            _frames.Start();
        }
        else
        {
            _frames.Stop();
        }
    }

    public static bool CanDraw(DriveMedium medium) => medium != DriveMedium.Unknown;

    protected override void LoadTheme()
    {
        _accent = Brush("Accent");
        _warning = Brush("Warning");
        _danger = Brush("Danger");
        _track = Brush("Pressed");
        _body = Tint(Brush("TertiaryText"), 0.32);
        _detail = Tint(Brush("SecondaryText"), 0.55);
        _metal = Tint(Brush("TertiaryText"), 0.6);
        _label = Tint(Brush("PanelBackground"), 0.85);
        _labelText = Brush("SecondaryText");
    }

    private static IBrush Tint(IBrush brush, double alpha)
        => brush is ISolidColorBrush s ? new ImmutableSolidColorBrush(s.Color, alpha) : brush;

    protected override Size MeasureOverride(Size availableSize) => new(W, H);

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var scale = Math.Min(Bounds.Width / W, Bounds.Height / H);
        if (scale <= 0)
        {
            return;
        }

        var used = _level switch { SpaceLevel.AlmostFull => _danger, SpaceLevel.GettingFull => _warning, _ => _accent };
        var u = _fill ? ShownFraction : -1;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - (W * scale)) / 2, (Bounds.Height - (H * scale)) / 2)))
        {
            switch (_medium)
            {
                case DriveMedium.HardDisk: HardDisk(context, u, used); break;
                case DriveMedium.Ssd: Ssd(context, u, used); break;
                case DriveMedium.Nvme: Nvme(context, u, used); break;
                case DriveMedium.UsbStick: UsbStick(context, u, used); break;
                case DriveMedium.External: External(context, u, used); break;
                case DriveMedium.SdCard: SdCard(context, new Rect(50, 6, 100, 98), u, used); break;
                case DriveMedium.CardReader: CardReader(context, u, used); break;
                case DriveMedium.Optical: Disc(context, u, used); break;
                case DriveMedium.Network: Network(context, u, used); break;
            }
        }
    }

    private void HardDisk(DrawingContext c, double u, IBrush used)
    {
        c.DrawRectangle(_body, null, new RoundedRect(new Rect(30, 4, 140, 102), 6));
        var center = new Point(92, 55);
        c.DrawEllipse(_metal, null, center, 44, 44);
        if (u >= 0)
        {
            Ring(c, center, 38, 10, u, used);
        }

        c.DrawEllipse(_detail, null, center, 9, 9);
        c.DrawEllipse(_metal, null, center, 4, 4);
        var arm = new StreamGeometry();
        using (var g = arm.Open())
        {
            g.BeginFigure(new Point(150, 90), true);
            g.LineTo(new Point(150, 82));
            g.LineTo(new Point(104, 58));
            g.LineTo(new Point(100, 64));
            g.LineTo(new Point(146, 92));
            g.EndFigure(true);
        }

        c.DrawGeometry(_detail, null, arm);
        c.DrawEllipse(_detail, null, new Point(150, 88), 8, 8);
        foreach (var (x, y) in new[] { (38, 12), (162, 12), (38, 98), (162, 98) })
        {
            c.DrawEllipse(_detail, null, new Point(x, y), 3, 3);
        }
    }

    private void Ssd(DrawingContext c, double u, IBrush used)
    {
        var body = new RoundedRect(new Rect(24, 10, 152, 90), 6);
        c.DrawRectangle(_body, null, body);
        FillLeft(c, body, u, used);
        c.DrawRectangle(_label, null, new RoundedRect(new Rect(44, 30, 112, 50), 3));
        Label(c, "SSD", new Point(100, 55), 14);
        c.DrawRectangle(_detail, null, new Rect(170, 34, 6, 12));
        c.DrawRectangle(_detail, null, new Rect(170, 52, 6, 22));
    }

    private void Nvme(DrawingContext c, double u, IBrush used)
    {
        c.DrawRectangle(_pcb, null, new RoundedRect(new Rect(8, 30, 184, 50), 3));
        for (var i = 0; i < 12; i++)
        {
            if (i != 3)
            {
                c.DrawRectangle(_gold, null, new Rect(9, 33 + (i * 3.8), 9, 2.4));
            }
        }

        c.DrawRectangle(_chip, null, new RoundedRect(new Rect(26, 40, 28, 30), 2));
        for (var i = 0; i < 4; i++)
        {
            var chip = new Rect(62 + (i * 30), 36, 24, 38);
            c.DrawRectangle(_chip, null, new RoundedRect(chip, 2));
            var part = u < 0 ? 0 : Math.Clamp((u * 4) - i, 0, 1);
            if (part > 0)
            {
                c.DrawRectangle(used, null, new RoundedRect(chip.WithWidth(chip.Width * part), 2));
            }
        }

        c.DrawEllipse(_gold, null, new Point(188, 55), 3, 3);
    }

    private void UsbStick(DrawingContext c, double u, IBrush used)
    {
        c.DrawRectangle(_metal, null, new RoundedRect(new Rect(14, 38, 34, 34), 2));
        c.DrawRectangle(_detail, null, new Rect(20, 46, 8, 6));
        c.DrawRectangle(_detail, null, new Rect(20, 58, 8, 6));
        var body = new RoundedRect(new Rect(46, 30, 130, 50), 10);
        c.DrawRectangle(_body, null, body);
        FillLeft(c, body, u, used);
        c.DrawEllipse(_detail, null, new Point(162, 55), 6, 6);
    }

    private void External(DrawingContext c, double u, IBrush used)
    {
        var cable = new StreamGeometry();
        using (var g = cable.Open())
        {
            g.BeginFigure(new Point(176, 55), false);
            g.CubicBezierTo(new Point(190, 55), new Point(192, 80), new Point(196, 104));
            g.EndFigure(false);
        }

        c.DrawGeometry(null, new ImmutablePen(_detail.ToImmutable(), 4), cable);
        var body = new RoundedRect(new Rect(30, 8, 146, 94), 12);
        c.DrawRectangle(_body, null, body);
        FillLeft(c, body, u, used);
        c.DrawRectangle(_label, null, new RoundedRect(new Rect(48, 26, 110, 58), 6));
        c.DrawEllipse(_detail, null, new Point(103, 55), 16, 16);
        c.DrawEllipse(_metal, null, new Point(103, 55), 5, 5);
    }

    private void SdCard(DrawingContext c, Rect r, double u, IBrush used)
    {
        var card = SdShape(r);
        c.DrawGeometry(_body, null, card);
        if (u > 0)
        {
            using (c.PushGeometryClip(card))
            {
                c.DrawRectangle(used, null, new Rect(r.X, r.Bottom - (r.Height * u), r.Width, r.Height * u));
            }
        }

        var k = r.Width / 100;
        for (var i = 0; i < 6; i++)
        {
            c.DrawRectangle(_gold, null, new RoundedRect(new Rect(r.X + ((26 + (i * 11)) * k), r.Y + (4 * k), 7 * k, 14 * k), 1));
        }

        c.DrawRectangle(_label, null, new RoundedRect(new Rect(r.X + (12 * k), r.Y + (38 * k), 76 * k, 40 * k), 3));
        Label(c, "SD", new Point(r.X + (50 * k), r.Y + (58 * k)), 13 * k);
    }

    private static StreamGeometry SdShape(Rect r)
    {
        var k = r.Width / 100;
        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(new Point(r.X + (20 * k), r.Y), true);
            g.LineTo(new Point(r.Right - (4 * k), r.Y));
            g.LineTo(new Point(r.Right, r.Y + (4 * k)));
            g.LineTo(new Point(r.Right, r.Bottom - (4 * k)));
            g.LineTo(new Point(r.Right - (4 * k), r.Bottom));
            g.LineTo(new Point(r.X + (4 * k), r.Bottom));
            g.LineTo(new Point(r.X, r.Bottom - (4 * k)));
            g.LineTo(new Point(r.X, r.Y + (20 * k)));
            g.EndFigure(true);
        }

        return shape;
    }

    private void CardReader(DrawingContext c, double u, IBrush used)
    {
        SdCard(c, new Rect(66, 4, 68, 66), u, used);
        c.DrawRectangle(_body, null, new RoundedRect(new Rect(20, 54, 160, 50), 8));
        c.DrawRectangle(_detail, null, new RoundedRect(new Rect(60, 54, 80, 5), 2));
        c.DrawEllipse(_accent, null, new Point(164, 90), 3, 3);
    }

    private void Disc(DrawingContext c, double u, IBrush used)
    {
        var center = new Point(100, 55);
        c.DrawEllipse(_metal, null, center, 52, 52);
        if (u >= 0)
        {
            Ring(c, center, 42, 12, u, used);
        }

        c.DrawEllipse(_body, null, center, 18, 18);
        c.DrawEllipse(_label, null, center, 7, 7);
    }

    private void Network(DrawingContext c, double u, IBrush used)
    {
        var box = new RoundedRect(new Rect(62, 4, 76, 82), 6);
        c.DrawRectangle(_body, null, box);
        for (var i = 0; i < 3; i++)
        {
            var bay = new Rect(70, 12 + (i * 22), 60, 16);
            c.DrawRectangle(_label, null, new RoundedRect(bay, 2));
            var part = u < 0 ? 0 : Math.Clamp((u * 3) - (2 - i), 0, 1);
            if (part > 0)
            {
                c.DrawRectangle(used, null, new RoundedRect(bay.WithWidth(bay.Width * part), 2));
            }

            c.DrawEllipse(_detail, null, new Point(124, bay.Y + 8), 2, 2);
        }

        var pen = new ImmutablePen(_detail.ToImmutable(), 3);
        c.DrawLine(pen, new Point(100, 86), new Point(100, 98));
        c.DrawLine(pen, new Point(40, 98), new Point(160, 98));
        foreach (var x in new[] { 40, 160 })
        {
            c.DrawRectangle(_detail, null, new RoundedRect(new Rect(x - 10, 94, 20, 12), 2));
        }
    }

    private static void FillLeft(DrawingContext c, RoundedRect body, double u, IBrush used)
    {
        if (u <= 0)
        {
            return;
        }

        using (c.PushClip(body))
        {
            c.DrawRectangle(used, null, body.Rect.WithWidth(body.Rect.Width * u));
        }
    }

    private void Ring(DrawingContext c, Point center, double radius, double thickness, double u, IBrush used)
    {
        c.DrawEllipse(null, new ImmutablePen(_track.ToImmutable(), thickness), center, radius, radius);
        var pen = new ImmutablePen(used.ToImmutable(), thickness);
        if (u >= 0.995)
        {
            c.DrawEllipse(null, pen, center, radius, radius);
        }
        else if (u > 0.005)
        {
            var angle = u * 2 * Math.PI;
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(new Point(center.X, center.Y - radius), false);
                g.ArcTo(new Point(center.X + (radius * Math.Sin(angle)), center.Y - (radius * Math.Cos(angle))), new Size(radius, radius), 0, u > 0.5, SweepDirection.Clockwise);
                g.EndFigure(false);
            }

            c.DrawGeometry(null, pen, arc);
        }
    }

    private void Label(DrawingContext c, string text, Point center, double size)
    {
        var t = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, size, _labelText);
        c.DrawText(t, new Point(center.X - (t.Width / 2), center.Y - (t.Height / 2)));
    }
}
