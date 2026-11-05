// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/ActivityChart.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Salandual.App.Resources;
using Salandual.Core;
using Salandual.Core.Listing;
using Salandual.Core.Resources;

namespace Salandual.App.Controls;

public sealed class ActivityChart : ThemedControl
{
    private IBrush _bg = null!, _border = null!, _accent = null!, _dim = null!;
    private IReadOnlyList<DiskActivityPoint> _points = [];
    private DateTime _now = DateTime.Now;
    private int _hover = -1;

    public bool Speeds { get; init; }

    public DisplayFormat Format { get; init; } = DisplayFormat.Default;

    public TimeSpan Span { get; init; } = DiskActivityHistory.Kept;

    public void Show(IReadOnlyList<DiskActivityPoint> points, DateTime now)
    {
        _points = points;
        _now = now;
        SpeedMax = Math.Max(1, points.Count == 0 ? 0 : points.Max(p => Math.Max(p.Sample.ReadPerSecond, p.Sample.WritePerSecond)));
        if (_hover >= points.Count)
        {
            _hover = -1;
        }

        InvalidateVisual();
    }

    internal IReadOnlyList<DiskActivityPoint> Points => _points;

    internal double SpeedMax { get; private set; } = 1;

    internal IReadOnlyList<(double From, double To)> Gaps()
    {
        var gaps = new List<(double, double)>();
        var start = _now - Span;
        var previous = start;
        foreach (var point in _points)
        {
            if (point.Time - previous > DiskActivityHistory.GapAfter)
            {
                gaps.Add((Fraction(previous), Fraction(point.Time)));
            }

            previous = point.Time;
        }

        if (_now - previous > DiskActivityHistory.GapAfter)
        {
            gaps.Add((Fraction(previous), 1));
        }

        return gaps;
    }

    private double Fraction(DateTime time) => Math.Clamp(1 - ((_now - time).TotalSeconds / Span.TotalSeconds), 0, 1);

    protected override void LoadTheme()
    {
        _bg = Brush("WindowBackground");
        _border = Brush("Border");
        _accent = Brush("Accent");
        _dim = Brush("SecondaryText");
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, Speeds ? 64 : 84);

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var box = new Rect(Bounds.Size).Deflate(0.5);
        context.DrawRectangle(_bg, new ImmutablePen(_border.ToImmutable(), 1), box, 2, 2);
        var inner = box.Deflate(1);
        var gridPen = new ImmutablePen(_border.ToImmutable(), 1);
        foreach (var part in new[] { 0.25, 0.5, 0.75 })
        {
            var y = Math.Round(inner.Top + (inner.Height * part)) + 0.5;
            context.DrawLine(gridPen, new Point(inner.Left, y), new Point(inner.Right, y));
        }

        using (context.PushClip(inner))
        {
            var hatch = new ImmutablePen(_border.ToImmutable(), 1);
            foreach (var (from, to) in Gaps())
            {
                var gap = new Rect(inner.Left + (inner.Width * from), inner.Top, Math.Max(1, inner.Width * (to - from)), inner.Height);
                using (context.PushClip(gap))
                {
                    for (var x = gap.Left - inner.Height; x < gap.Right; x += 6)
                    {
                        context.DrawLine(hatch, new Point(x, inner.Bottom), new Point(x + inner.Height, inner.Top));
                    }
                }
            }

            if (Speeds)
            {
                DrawRuns(context, inner, p => p.ReadPerSecond / SpeedMax, null, new ImmutablePen(_accent.ToImmutable(), 1.5));
                DrawRuns(context, inner, p => p.WritePerSecond / SpeedMax, null, new ImmutablePen(_dim.ToImmutable(), 1.5, new ImmutableDashStyle([3, 2], 0)));
            }
            else
            {
                var accent = (_accent as ISolidColorBrush)?.Color ?? Colors.SteelBlue;
                DrawRuns(context, inner, p => p.Busy / 100, new ImmutableSolidColorBrush(new Color(56, accent.R, accent.G, accent.B)), new ImmutablePen(_accent.ToImmutable(), 1.5));
            }

            if (_hover >= 0 && _hover < _points.Count)
            {
                var x = X(inner, _points[_hover].Time);
                context.DrawLine(new ImmutablePen(_dim.ToImmutable(), 1), new Point(x, inner.Top), new Point(x, inner.Bottom));
            }
        }
    }

    private double X(Rect inner, DateTime time) => inner.Left + (inner.Width * Fraction(time));

    private void DrawRuns(DrawingContext context, Rect inner, Func<DiskActivitySample, double> value, IBrush? fill, IPen pen)
    {
        double Y(DiskActivityPoint p) => inner.Bottom - (inner.Height * Math.Clamp(value(p.Sample), 0, 1));
        var start = 0;
        while (start < _points.Count)
        {
            var end = start + 1;
            while (end < _points.Count && _points[end].Time - _points[end - 1].Time <= DiskActivityHistory.GapAfter)
            {
                end++;
            }

            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(new Point(X(inner, _points[start].Time), Y(_points[start])), false);
                for (var i = start + 1; i < end; i++)
                {
                    g.LineTo(new Point(X(inner, _points[i].Time), Y(_points[i])));
                }

                g.EndFigure(false);
            }

            if (fill is not null && end - start > 1)
            {
                var area = new StreamGeometry();
                using (var g = area.Open())
                {
                    g.BeginFigure(new Point(X(inner, _points[start].Time), inner.Bottom), true);
                    for (var i = start; i < end; i++)
                    {
                        g.LineTo(new Point(X(inner, _points[i].Time), Y(_points[i])));
                    }

                    g.LineTo(new Point(X(inner, _points[end - 1].Time), inner.Bottom));
                    g.EndFigure(true);
                }

                context.DrawGeometry(fill, null, area);
            }

            context.DrawGeometry(null, pen, line);
            start = end;
        }
    }

    internal string? TipAt(double x)
    {
        _hover = -1;
        if (_points.Count == 0 || Bounds.Width <= 2)
        {
            return null;
        }

        var time = _now - TimeSpan.FromSeconds(Span.TotalSeconds * (1 - Math.Clamp((x - 1) / (Bounds.Width - 2), 0, 1)));
        var nearest = 0;
        for (var i = 1; i < _points.Count; i++)
        {
            if (Math.Abs((_points[i].Time - time).Ticks) < Math.Abs((_points[nearest].Time - time).Ticks))
            {
                nearest = i;
            }
        }

        if (Math.Abs((_points[nearest].Time - time).TotalSeconds) > Span.TotalSeconds / 60)
        {
            return null;
        }

        _hover = nearest;
        var p = _points[nearest];
        return Loc.Format(AppStrings.Drive_Activity_Point, p.Time.ToString("T", CultureInfo.CurrentCulture), p.Sample.Percent,
            Format.Size((long)p.Sample.ReadPerSecond), Format.Size((long)p.Sample.WritePerSecond));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        ToolTip.SetTip(this, TipAt(e.GetPosition(this).X));
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }
}
