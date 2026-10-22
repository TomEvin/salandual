// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/SpaceChart.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Salandual.App.Dialogs;

internal sealed record SpaceSlice(double Value, Color Color, string Tip);

internal sealed class SpaceChart : Control
{
    private const double Cx = 115, Cy = 112, OuterFrom = 84, OuterTo = 106, InnerFrom = 46, InnerTo = 78;

    internal const double OuterMinDegrees = 4;

    private IReadOnlyList<SpaceSlice> _inner = [];
    private IReadOnlyList<SpaceSlice> _outer = [];
    private List<(double From, double To)> _innerAngles = [];
    private List<(double From, double To)> _outerAngles = [];
    private string _big = "", _small = "";
    private (bool Outer, int Index)? _hover;

    public SpaceChart()
    {
        Width = 230;
        Height = 236;
        ResourcesChanged += (_, _) => InvalidateVisual();
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public static (Color[] Parts, Color Others, Color Selection, Color Other, Color Free) Palette(bool dark) => dark
        ? ([Color.Parse("#3987E5"), Color.Parse("#D95926"), Color.Parse("#199E70"), Color.Parse("#C98500"), Color.Parse("#D55181")], Color.Parse("#6A6A6A"), Color.Parse("#9085E9"), Color.Parse("#8A8A8A"), Color.Parse("#3A3A3A"))
        : ([Color.Parse("#2A78D6"), Color.Parse("#EB6834"), Color.Parse("#1BAF7A"), Color.Parse("#EDA100"), Color.Parse("#E87BA4")], Color.Parse("#B9B9B9"), Color.Parse("#6250D6"), Color.Parse("#8F8F8F"), Color.Parse("#E4E4E4"));

    public void Show(IReadOnlyList<SpaceSlice> inner, IReadOnlyList<SpaceSlice> outer, string big, string small)
    {
        (_inner, _outer, _big, _small) = (inner, outer, big, small);
        _innerAngles = Angles(inner, 0);
        _outerAngles = Angles(outer, OuterMinDegrees);
        _hover = null;
        InvalidateVisual();
    }

    internal string? HoverTip => _hover is { } h ? (h.Outer ? _outer : _inner)[h.Index].Tip : null;

    internal static List<(double From, double To)> Angles(IReadOnlyList<SpaceSlice> slices, double minDegrees)
    {
        var total = slices.Sum(s => s.Value);
        if (total <= 0)
        {
            return [];
        }

        var min = minDegrees * Math.PI / 180;
        var sweep = slices.Select(s => s.Value > 0 ? Math.Max(s.Value / total * 2 * Math.PI, min) : 0).ToArray();
        var biggest = Array.IndexOf(sweep, sweep.Max());
        sweep[biggest] -= sweep.Sum() - 2 * Math.PI;
        var at = 0.0;
        var result = new List<(double, double)>();
        foreach (var s in sweep)
        {
            result.Add((at, at + s));
            at += s;
        }

        return result;
    }

    public override void Render(DrawingContext context)
    {
        if (_innerAngles.Count == 0)
        {
            return;
        }

        var surface = Brush("Brush.PanelBackground");
        var gap = new Pen(surface, 2);

        for (var i = 1; i <= 4; i++)
        {
            using (context.PushTransform(Matrix.CreateTranslation(0, 2 + i * 1.5)))
            using (context.PushOpacity(0.07))
            {
                context.DrawGeometry(Brushes.Black, null, Rings());
            }
        }

        using (context.PushTransform(Matrix.CreateTranslation(0, 6)))
        using (context.PushOpacity(0.35))
        {
            Draw(context, null, dim: false);
        }

        Draw(context, gap, dim: true);

        using (context.PushGeometryClip(Rings()))
        {
            var sheen = new RadialGradientBrush
            {
                Center = new RelativePoint(0.4, 0.28, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.4, 0.28, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(97, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(15, 255, 255, 255), 0.55),
                    new GradientStop(Color.FromArgb(41, 0, 0, 0), 1),
                },
            };
            context.FillRectangle(sheen, new Rect(0, 0, 230, 236));
        }

        Text(context, _big, 17, FontWeight.SemiBold, "Brush.Text", Cy - 3 - 12);
        Text(context, _small, 11, FontWeight.Normal, "Brush.SecondaryText", Cy + 14 - 9);
    }

    private void Draw(DrawingContext context, IPen? gap, bool dim)
    {
        for (var i = 0; i < _outerAngles.Count; i++)
        {
            Slice(context, _outer[i].Color, gap, OuterFrom, OuterTo, _outerAngles[i], dim && _hover is { } h && h != (true, i));
        }

        for (var i = 0; i < _innerAngles.Count; i++)
        {
            Slice(context, _inner[i].Color, gap, InnerFrom, InnerTo, _innerAngles[i], dim && _hover is { } h && h != (false, i));
        }
    }

    private static void Slice(DrawingContext context, Color color, IPen? gap, double r0, double r1, (double From, double To) a, bool faded)
    {
        if (a.To - a.From <= 0)
        {
            return;
        }

        using (context.PushOpacity(faded ? 0.55 : 1))
        {
            context.DrawGeometry(new SolidColorBrush(color), gap, Sector(r0, r1, a.From, a.To));
        }
    }

    private static StreamGeometry Sector(double r0, double r1, double a0, double a1)
    {
        a1 = Math.Min(a1, a0 + 2 * Math.PI - 0.0001);
        var big = a1 - a0 > Math.PI;
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(At(r1, a0), true);
        g.ArcTo(At(r1, a1), new Size(r1, r1), 0, big, SweepDirection.Clockwise);
        g.LineTo(At(r0, a1));
        g.ArcTo(At(r0, a0), new Size(r0, r0), 0, big, SweepDirection.CounterClockwise);
        g.EndFigure(true);
        return geometry;
    }

    private static Point At(double r, double a) => new(Cx + r * Math.Sin(a), Cy - r * Math.Cos(a));

    private Geometry Rings()
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        foreach (var r in (double[])[OuterTo, OuterFrom, InnerTo, InnerFrom])
        {
            if (_outerAngles.Count > 0 || r <= InnerTo)
            {
                group.Children.Add(new EllipseGeometry(new Rect(Cx - r, Cy - r, 2 * r, 2 * r)));
            }
        }

        return group;
    }

    private void Text(DrawingContext context, string text, double size, FontWeight weight, string brush, double y)
    {
        if (text.Length == 0)
        {
            return;
        }

        var layout = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, Brush(brush));
        context.DrawText(layout, new Point(Cx - layout.Width / 2, y));
    }

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var b) && b is IBrush brush ? brush : Brushes.Gray;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        HoverAt(e.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(null);
    }

    internal void HoverAt(Point p)
    {
        var (dx, dy) = (p.X - Cx, p.Y - Cy);
        var r = Math.Sqrt(dx * dx + dy * dy);
        var a = Math.Atan2(dx, -dy);
        if (a < 0)
        {
            a += 2 * Math.PI;
        }

        var angles = r is >= OuterFrom and <= OuterTo ? _outerAngles : r is >= InnerFrom and <= InnerTo ? _innerAngles : null;
        var index = angles?.FindIndex(x => a >= x.From && a < x.To) ?? -1;
        SetHover(index >= 0 ? (ReferenceEquals(angles, _outerAngles), index) : null);
    }

    private void SetHover((bool Outer, int Index)? hover)
    {
        if (hover == _hover)
        {
            return;
        }

        _hover = hover;
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, HoverTip);
        if (HoverTip is not null && IsPointerOver)
        {
            ToolTip.SetIsOpen(this, true);
        }

        InvalidateVisual();
    }
}
