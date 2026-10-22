// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ColorMath.cs

using System.Globalization;

namespace Salandual.Core.Settings;

public readonly record struct Rgb(byte R, byte G, byte B);

public static class ColorMath
{
    public static bool TryParseHex(string? text, out Rgb color)
    {
        color = default;
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        if (s[0] == '#')
        {
            s = s[1..];
        }

        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }

        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Rgb ParseHex(string text)
        => TryParseHex(text, out var c) ? c : throw new FormatException($"Not a #RRGGBB color: '{text}'.");

    public static string ToHex(Rgb c) => string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");

    public static Rgb Mix(Rgb a, Rgb b, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return new Rgb(Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));
    }

    public static double Luminance(Rgb c)
    {
        static double Lin(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Lin(c.R)) + (0.7152 * Lin(c.G)) + (0.0722 * Lin(c.B));
    }

    public static double Contrast(Rgb a, Rgb b)
    {
        var (x, y) = (Luminance(a) + 0.05, Luminance(b) + 0.05);
        return Math.Max(x, y) / Math.Min(x, y);
    }

    public static Rgb Lighten(Rgb c, double amount) => Mix(c, new Rgb(255, 255, 255), amount);

    public static readonly Rgb White = new(255, 255, 255);

    public static readonly Rgb Ink = new(10, 10, 10);

    public static Rgb EnsureContrast(Rgb color, double ratio, params ReadOnlySpan<Rgb> backgrounds)
    {
        if (backgrounds.IsEmpty)
        {
            return color;
        }

        for (var count = backgrounds.Length; count >= 1; count--)
        {
            if (Nearest(color, ratio, backgrounds[..count]) is { } found)
            {
                return found;
            }
        }

        return Contrast(White, backgrounds[0]) >= Contrast(new Rgb(0, 0, 0), backgrounds[0]) ? White : new Rgb(0, 0, 0);
    }

    private static Rgb? Nearest(Rgb color, double ratio, ReadOnlySpan<Rgb> backgrounds)
    {
        if (Reaches(color, ratio, backgrounds))
        {
            return color;
        }

        Rgb? nearest = null;
        var nearestStep = int.MaxValue;
        foreach (var toward in (ReadOnlySpan<Rgb>)[new Rgb(0, 0, 0), White])
        {
            for (var step = 1; step <= 100; step++)
            {
                var candidate = Mix(color, toward, step / 100.0);
                if (Reaches(candidate, ratio, backgrounds))
                {
                    if (step < nearestStep)
                    {
                        (nearest, nearestStep) = (candidate, step);
                    }

                    break;
                }
            }
        }

        return nearest;
    }

    public static Rgb OnColor(Rgb fill) => Contrast(White, fill) >= Contrast(Ink, fill) ? White : Ink;

    private static bool Reaches(Rgb color, double ratio, ReadOnlySpan<Rgb> backgrounds)
        => WorstContrast(color, backgrounds) >= ratio;

    private static double WorstContrast(Rgb color, ReadOnlySpan<Rgb> backgrounds)
    {
        var worst = double.MaxValue;
        foreach (var background in backgrounds)
        {
            worst = Math.Min(worst, Contrast(color, background));
        }

        return worst;
    }

    private static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));
}
