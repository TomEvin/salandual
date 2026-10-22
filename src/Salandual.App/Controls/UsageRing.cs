// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/UsageRing.cs

using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Salandual.App.Resources;

namespace Salandual.App.Controls;

public enum SpaceLevel { Normal, GettingFull, AlmostFull }

public sealed class UsageRing : ThemedControl
{
    private const double Thickness = 10;
    private double _fraction;
    private string _center = "";
    private SpaceLevel _level;
    private IBrush _accent = null!, _warning = null!, _danger = null!, _track = null!, _text = null!, _dim = null!;

    public double Fraction
    {
        get => _fraction;
        set
        {
            _fraction = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            InvalidateVisual();
        }
    }

    public string CenterText
    {
        get => _center;
        set
        {
            _center = value;
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

    public static SpaceLevel LevelOf(long free, long total)
    {
        if (total <= 0)
        {
            return SpaceLevel.Normal;
        }

        var share = (double)free / total;
        return share < 0.03 ? SpaceLevel.AlmostFull : share < 0.10 ? SpaceLevel.GettingFull : SpaceLevel.Normal;
    }

    public static string BrushKey(SpaceLevel level) => level switch
    {
        SpaceLevel.AlmostFull => "Brush.Danger",
        SpaceLevel.GettingFull => "Brush.Warning",
        _ => "Brush.Accent",
    };

    protected override void LoadTheme()
    {
        _accent = Brush("Accent");
        _warning = Brush("Warning");
        _danger = Brush("Danger");
        _track = Brush("Pressed");
        _text = Brush("Text");
        _dim = Brush("SecondaryText");
    }

    protected override Size MeasureOverride(Size availableSize) => new(116, 116);

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (size - Thickness) / 2;
        context.DrawEllipse(null, new ImmutablePen(_track.ToImmutable(), Thickness), center, radius, radius);
        var fill = _level switch { SpaceLevel.AlmostFull => _danger, SpaceLevel.GettingFull => _warning, _ => _accent };
        var pen = new ImmutablePen(fill.ToImmutable(), Thickness, lineCap: PenLineCap.Round);
        if (_fraction >= 0.995)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
        }
        else if (_fraction > 0.005)
        {
            var angle = _fraction * 2 * Math.PI;
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(new Point(center.X, center.Y - radius), false);
                g.ArcTo(new Point(center.X + (radius * Math.Sin(angle)), center.Y - (radius * Math.Cos(angle))), new Size(radius, radius), 0, _fraction > 0.5, SweepDirection.Clockwise);
                g.EndFigure(false);
            }

            context.DrawGeometry(null, pen, arc);
        }

        var value = new FormattedText(_center, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Bold, 17, _text);
        var caption = new FormattedText(AppStrings.Ring_Free, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 11, _dim);
        var top = center.Y - ((value.Height + caption.Height) / 2);
        context.DrawText(value, new Point(center.X - (value.Width / 2), top));
        context.DrawText(caption, new Point(center.X - (caption.Width / 2), top + value.Height));
    }
}
