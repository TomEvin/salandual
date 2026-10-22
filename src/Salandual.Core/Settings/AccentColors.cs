// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/AccentColors.cs

namespace Salandual.Core.Settings;

public sealed record AccentColors(Rgb Fill, Rgb FillHover, Rgb FillPressed, Rgb Label, Rgb Edge, Rgb Mark, Rgb MarkHover, Rgb MarkPressed)
{
    private const double LabelRatio = 4.5;
    private const double EdgeRatio = 3.0;

    public static AccentColors From(IReadOnlyDictionary<string, Rgb> c, bool dark, Rgb controlEdge)
    {
        var accent = c[ColorTokens.Accent];
        var surface = c[ColorTokens.PanelBackground];
        var window = c[ColorTokens.WindowBackground];
        var black = new Rgb(0, 0, 0);

        var rest = dark ? ColorMath.Mix(accent, black, 0.3) : accent;
        var label = ColorMath.OnColor(rest);
        var away = label == ColorMath.White ? black : ColorMath.White;
        var fill = rest;
        for (var t = 0.0; t <= 1.0 && ColorMath.Contrast(label, fill) < LabelRatio; t += 0.01)
        {
            fill = ColorMath.Mix(rest, away, t);
        }

        var seen = ColorMath.Contrast(fill, surface) >= EdgeRatio && ColorMath.Contrast(fill, window) >= EdgeRatio;
        var text = c[ColorTokens.Text];
        return new AccentColors(
            Fill: fill,
            FillHover: ColorMath.Mix(fill, away, 0.12),
            FillPressed: ColorMath.Mix(fill, away, 0.22),
            Label: label,
            Edge: seen ? fill : controlEdge,
            Mark: accent,
            MarkHover: ColorMath.Mix(accent, text, 0.18),
            MarkPressed: ColorMath.Mix(accent, text, 0.32));
    }
}
