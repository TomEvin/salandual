// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ControlColors.cs

namespace Salandual.Core.Settings;

public sealed record ControlColors(Rgb Fill, Rgb FillHover, Rgb FillPressed, Rgb Edge, Rgb EdgeHover, Rgb DisabledFill, Rgb DisabledEdge, Rgb DisabledText, Rgb ScrollThumb, AccentColors Accent)
{
    public static ControlColors From(IReadOnlyDictionary<string, Rgb> c)
    {
        var surface = c[ColorTokens.PanelBackground];
        var window = c[ColorTokens.WindowBackground];
        var text = c[ColorTokens.Text];
        var dark = ColorMath.Luminance(surface) < 0.2;
        var disabledFill = ColorMath.Mix(surface, text, 0.03);
        var edge = Reach(surface, text, 3.0, surface, window);
        return new ControlColors(
            Fill: Shade(surface, text, dark ? 0.12 : 0.07),
            FillHover: Shade(surface, text, dark ? 0.18 : 0.12),
            FillPressed: Shade(surface, text, dark ? 0.24 : 0.18),
            Edge: edge,
            EdgeHover: Reach(surface, text, 4.5, surface, window),
            DisabledFill: disabledFill,
            DisabledEdge: Reach(surface, text, 1.6, surface, window),
            DisabledText: Reach(surface, text, 3.0, surface, window, disabledFill),
            ScrollThumb: Reach(surface, text, 2.0, surface, window),
            Accent: AccentColors.From(c, dark, edge));
    }

    private static Rgb Shade(Rgb surface, Rgb text, double amount)
    {
        var fill = ColorMath.Mix(surface, text, amount);
        for (var t = amount; t > 0.03 && ColorMath.Contrast(text, fill) < 4.5; t -= 0.01)
        {
            fill = ColorMath.Mix(surface, text, t);
        }

        return fill;
    }

    private static Rgb Reach(Rgb from, Rgb to, double ratio, params ReadOnlySpan<Rgb> backgrounds)
    {
        for (var step = 0; step <= 100; step++)
        {
            var color = ColorMath.Mix(from, to, step / 100.0);
            var all = true;
            foreach (var background in backgrounds)
            {
                all &= ColorMath.Contrast(color, background) >= ratio;
            }

            if (all)
            {
                return color;
            }
        }

        return to;
    }
}
