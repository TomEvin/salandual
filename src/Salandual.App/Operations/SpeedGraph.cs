// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Operations/SpeedGraph.cs

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Salandual.App.Controls;
using Salandual.Core.Operations;

namespace Salandual.App.Operations;

public sealed class SpeedGraph : ThemedControl
{
    private IReadOnlyList<SpeedSample> _samples = [];
    private double _fraction;
    private bool _files, _paused;
    private string _now = "", _average = "";
    private IBrush _accent = null!, _fill = null!, _grey = null!, _greyFill = null!, _bg = null!, _border = null!, _text = null!, _dim = null!;

    public void Update(IReadOnlyList<SpeedSample> samples, double fraction, bool countsFiles, bool paused, string now, string average)
    {
        _samples = samples;
        _fraction = fraction;
        _files = countsFiles;
        _paused = paused;
        _now = now;
        _average = average;
        InvalidateVisual();
    }

    internal int PointCount => _samples.Count;

    protected override void LoadTheme()
    {
        _accent = Brush("Accent");
        _fill = new ImmutableSolidColorBrush((_accent as ISolidColorBrush)?.Color ?? Colors.SteelBlue, 0.22);
        _grey = Brush("TertiaryText");
        _greyFill = new ImmutableSolidColorBrush((_grey as ISolidColorBrush)?.Color ?? Colors.Gray, 0.18);
        _bg = Brush("Hover");
        _border = Brush("Border");
        _text = Brush("Text");
        _dim = Brush("SecondaryText");
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 72);

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var w = Bounds.Width;
        var h = Bounds.Height;
        var frame = new Rect(0, 0, w, h);
        context.DrawRectangle(_bg, new ImmutablePen(_border.ToImmutable(), 1), frame.Deflate(0.5), 4, 4);
        using (context.PushClip(new RoundedRect(frame.Deflate(1), 4)))
        {
            var values = _samples.Select(s => _files ? s.ItemsPerSecond : s.BytesPerSecond).ToList();
            if (values.Count >= 2)
            {
                var max = Math.Max(values.Max() * 1.15, 1);
                var top = 22.0;
                var graphH = h - top - 2;
                Point P(int i) => new(_samples[i].Fraction * w, top + graphH - (values[i] / max * graphH));

                var line = new StreamGeometry();
                using (var g = line.Open())
                {
                    g.BeginFigure(P(0), false);
                    for (var i = 1; i < values.Count; i++)
                    {
                        g.LineTo(P(i));
                    }

                    g.EndFigure(false);
                }

                var area = new StreamGeometry();
                using (var g = area.Open())
                {
                    g.BeginFigure(new Point(_samples[0].Fraction * w, h), true);
                    for (var i = 0; i < values.Count; i++)
                    {
                        g.LineTo(P(i));
                    }

                    g.LineTo(new Point(_samples[^1].Fraction * w, h));
                    g.EndFigure(true);
                }

                var stroke = _paused ? _grey : _accent;
                context.DrawGeometry(_paused ? _greyFill : _fill, null, area);
                context.DrawGeometry(null, new ImmutablePen(stroke.ToImmutable(), 1.6, lineJoin: PenLineJoin.Round), line);

                var avg = values.Average();
                var ay = top + graphH - (avg / max * graphH);
                context.DrawLine(new ImmutablePen(_grey.ToImmutable(), 1, new ImmutableDashStyle([4, 4], 0)), new Point(0, ay), new Point(w, ay));
            }

            var x = _fraction * w;
            context.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(((_accent as ISolidColorBrush)?.Color ?? Colors.SteelBlue), 0.5), 1), new Point(x, 0), new Point(x, h));

            if (_average.Length > 0)
            {
                context.DrawText(Text(_average, _dim, size: FontSize - 2), new Point(8, 4));
            }

            if (_now.Length > 0)
            {
                var now = Text(_now, _text, bold: true, size: FontSize - 1);
                context.DrawText(now, new Point(w - now.Width - 8, 3));
            }
        }
    }
}
