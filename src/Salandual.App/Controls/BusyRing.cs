// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/BusyRing.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Salandual.App.Controls;

public sealed class BusyRing : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<BusyRing, IBrush?>(nameof(Brush), Brushes.Gray);

    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<BusyRing, double>(nameof(Thickness), 2.5);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DateTime _start = DateTime.UtcNow;

    static BusyRing() => AffectsRender<BusyRing>(BrushProperty, ThicknessProperty);

    public BusyRing() => _timer.Tick += (_, _) => InvalidateVisual();

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= Thickness * 2 || Brush is not { } brush)
        {
            return;
        }

        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (size - Thickness) / 2;
        using (context.PushOpacity(0.25))
        {
            context.DrawEllipse(null, new Pen(brush, Thickness), centre, radius, radius);
        }

        var angle = (DateTime.UtcNow - _start).TotalSeconds % 1 * 2 * Math.PI;
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(centre + new Vector(Math.Cos(angle), Math.Sin(angle)) * radius, false);
            var end = angle + Math.PI / 2;
            g.ArcTo(centre + new Vector(Math.Cos(end), Math.Sin(end)) * radius, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, new Pen(brush, Thickness, lineCap: PenLineCap.Round), arc);
    }
}
