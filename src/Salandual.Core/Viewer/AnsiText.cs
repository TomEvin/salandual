// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/AnsiText.cs

using System.Globalization;
using System.Net;
using System.Text;

namespace Salandual.Core.Viewer;

internal sealed class AnsiText(bool dark = false)
{
    private int? _fg;
    private int? _bg;
    private string? _fgRgb;
    private string? _bgRgb;
    private bool _bold;
    private bool _italic;
    private bool _underline;

    public static string Overwrites(string text)
    {
        if (!text.Contains('\r'))
        {
            return text;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var cr = line.LastIndexOf('\r');
            lines[i] = cr < 0 ? line : line[(cr + 1)..];
        }

        return string.Join('\n', lines);
    }

    public string ToHtml(string text)
    {
        var html = new StringBuilder(text.Length + 32);
        var open = false;
        void Open()
        {
            if (Style() is { } style)
            {
                html.Append(style);
                open = true;
            }
        }

        void Close()
        {
            if (open)
            {
                html.Append("</span>");
                open = false;
            }
        }

        Open();
        var i = 0;
        var run = 0;
        while (i < text.Length)
        {
            if (text[i] != '\x1b')
            {
                i++;
                continue;
            }

            html.Append(WebUtility.HtmlEncode(text[run..i]));
            var end = Sequence(text, i, out var sgr);
            if (sgr is not null)
            {
                Close();
                Apply(sgr);
                Open();
            }

            i = end;
            run = end;
        }

        html.Append(WebUtility.HtmlEncode(text[run..]));
        Close();
        return html.ToString();
    }

    private static int Sequence(string text, int at, out string? sgr)
    {
        sgr = null;
        if (at + 1 >= text.Length)
        {
            return text.Length;
        }

        var kind = text[at + 1];
        if (kind == '[')
        {
            var i = at + 2;
            while (i < text.Length && text[i] is >= '0' and <= '?')
            {
                i++;
            }

            while (i < text.Length && text[i] is >= ' ' and <= '/')
            {
                i++;
            }

            if (i >= text.Length)
            {
                return text.Length;
            }

            if (text[i] == 'm')
            {
                sgr = text[(at + 2)..i];
            }

            return i + 1;
        }

        if (kind == ']')
        {
            for (var i = at + 2; i < text.Length; i++)
            {
                if (text[i] == '\a')
                {
                    return i + 1;
                }

                if (text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '\\')
                {
                    return i + 2;
                }
            }

            return text.Length;
        }

        return Math.Min(text.Length, at + 2);
    }

    private void Apply(string sgr)
    {
        var codes = sgr.Length == 0 ? [0] : sgr.Split(';').Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).ToArray();
        for (var k = 0; k < codes.Length; k++)
        {
            var code = codes[k];
            switch (code)
            {
                case 0: _fg = _bg = null; _fgRgb = _bgRgb = null; _bold = _italic = _underline = false; break;
                case 1: _bold = true; break;
                case 3: _italic = true; break;
                case 4: _underline = true; break;
                case 22: _bold = false; break;
                case 23: _italic = false; break;
                case 24: _underline = false; break;
                case >= 30 and <= 37: _fg = code - 30; _fgRgb = null; break;
                case >= 90 and <= 97: _fg = code - 90 + 8; _fgRgb = null; break;
                case 39: _fg = null; _fgRgb = null; break;
                case >= 40 and <= 47: _bg = code - 40; _bgRgb = null; break;
                case >= 100 and <= 107: _bg = code - 100 + 8; _bgRgb = null; break;
                case 49: _bg = null; _bgRgb = null; break;
                case 38 or 48:
                    string? rgb = null;
                    if (k + 2 < codes.Length && codes[k + 1] == 5)
                    {
                        rgb = Color256(codes[k + 2]);
                        k += 2;
                    }
                    else if (k + 4 < codes.Length && codes[k + 1] == 2)
                    {
                        rgb = string.Create(CultureInfo.InvariantCulture, $"#{Math.Clamp(codes[k + 2], 0, 255):X2}{Math.Clamp(codes[k + 3], 0, 255):X2}{Math.Clamp(codes[k + 4], 0, 255):X2}");
                        k += 4;
                    }

                    if (code == 38 && dark && rgb is not null)
                    {
                        rgb = Lighter(rgb);
                    }

                    if (code == 38)
                    {
                        (_fg, _fgRgb) = (null, rgb);
                    }
                    else
                    {
                        (_bg, _bgRgb) = (null, rgb);
                    }

                    break;
            }
        }
    }

    private string? Style()
    {
        var classes = new List<string>();
        if (_fg is { } fg)
        {
            classes.Add("af" + fg.ToString(CultureInfo.InvariantCulture));
        }

        if (_bg is { } bg)
        {
            classes.Add("ab" + bg.ToString(CultureInfo.InvariantCulture));
        }

        if (_bold)
        {
            classes.Add("abo");
        }

        if (_italic)
        {
            classes.Add("ait");
        }

        if (_underline)
        {
            classes.Add("aun");
        }

        var style = (_fgRgb is null ? "" : "color:" + _fgRgb + ";") + (_bgRgb is null ? "" : "background:" + _bgRgb + ";");
        if (classes.Count == 0 && style.Length == 0)
        {
            return null;
        }

        return "<span" + (classes.Count > 0 ? " class=\"" + string.Join(' ', classes) + "\"" : "") + (style.Length > 0 ? " style=\"" + style + "\"" : "") + ">";
    }

    private static string Lighter(string rgb)
    {
        var value = int.Parse(rgb.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int r = (value >> 16) & 0xFF, g = (value >> 8) & 0xFF, b = value & 0xFF;
        if ((0.2126 * r) + (0.7152 * g) + (0.0722 * b) >= 110)
        {
            return rgb;
        }

        int Mix(int c) => c + (int)Math.Round((255 - c) * 0.4);
        return string.Create(CultureInfo.InvariantCulture, $"#{Mix(r):X2}{Mix(g):X2}{Mix(b):X2}");
    }

    private static string? Color256(int n)
    {
        if (n is < 0 or > 255)
        {
            return null;
        }

        if (n < 16)
        {
            return Named[n];
        }

        if (n < 232)
        {
            n -= 16;
            int Level(int v) => v == 0 ? 0 : 55 + (v * 40);
            return string.Create(CultureInfo.InvariantCulture, $"#{Level(n / 36):X2}{Level(n / 6 % 6):X2}{Level(n % 6):X2}");
        }

        var grey = 8 + ((n - 232) * 10);
        return string.Create(CultureInfo.InvariantCulture, $"#{grey:X2}{grey:X2}{grey:X2}");
    }

    internal static readonly string[] Named =
    [
        "#3E424D", "#E75C58", "#00A250", "#DDB62B", "#208FFB", "#D160C4", "#60C6C8", "#C5C1B4",
        "#282C36", "#B22B31", "#007427", "#B27D12", "#0065CA", "#A03196", "#258F8F", "#A1A6B2",
    ];
}
