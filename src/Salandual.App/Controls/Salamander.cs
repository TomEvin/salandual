// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/Salamander.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Salandual.App.Theme;

namespace Salandual.App.Controls;

internal sealed class Salamander : Control
{
    public const double Step = 0.26;

    public static readonly StyledProperty<double> TimeProperty = AvaloniaProperty.Register<Salamander, double>(nameof(Time));

    public static readonly StyledProperty<bool> DarkProperty = AvaloniaProperty.Register<Salamander, bool>(nameof(Dark));

    static Salamander() => AffectsRender<Salamander>(TimeProperty, DarkProperty);

    public Salamander()
    {
        Width = 84;
        Height = 28;
        IsHitTestVisible = false;
    }

    public double Time
    {
        get => GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public bool Dark
    {
        get => GetValue(DarkProperty);
        set => SetValue(DarkProperty, value);
    }

    internal bool FirstStep => Time / Step % 1 < 0.5;

    private double Sway
    {
        get
        {
            var half = Time / Step % 2;
            var p = half < 1 ? half : 2 - half;
            return -Math.Cos(p * Math.PI);
        }
    }

    public override void Render(DrawingContext context) => AppIcons.DrawSalamander(context, Bounds.Size, Sway, FirstStep, Dark);

    public static (double Left, double Opacity) RunAt(double time, double length, double width)
    {
        var p = Math.Clamp(time / length, 0, 1);
        p = p < 0.5 ? 4 * p * p * p : 1 - (Math.Pow(-2 * p + 2, 3) / 2);
        (double At, double Left)[] keys = [(0, -90), (0.44, 0.40 * width), (0.52, 0.42 * width), (0.60, 0.46 * width), (1, width - 40)];
        var left = keys[^1].Left;
        for (var i = 1; i < keys.Length; i++)
        {
            if (p <= keys[i].At)
            {
                var (a, b) = (keys[i - 1], keys[i]);
                left = a.Left + ((b.Left - a.Left) * (p - a.At) / (b.At - a.At));
                break;
            }
        }

        var opacity = p < 0.92 ? 1 : (1 - p) / 0.08;
        return (left, opacity);
    }
}
