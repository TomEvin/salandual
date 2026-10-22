// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Editing/Indentation.cs

using System.Text;
using Salandual.Core.Settings;

namespace Salandual.Core.Editing;

public static class Indentation
{
    public static string Unit(IndentStyle style) => style switch
    {
        IndentStyle.Spaces2 => "  ",
        IndentStyle.Tabs => "\t",
        _ => "    ",
    };

    public static int Width(IndentStyle style) => style == IndentStyle.Spaces2 ? 2 : 4;

    public static string Convert(string text, IndentStyle from, IndentStyle to)
    {
        var unit = Width(from);
        var output = new StringBuilder(text.Length + (text.Length / 8));
        var at = 0;
        while (at < text.Length)
        {
            var columns = 0;
            var i = at;
            while (i < text.Length && text[i] is ' ' or '\t')
            {
                columns = text[i] == '\t' ? ((columns / 4) + 1) * 4 : columns + 1;
                i++;
            }

            var end = i;
            while (end < text.Length && text[end] is not '\n')
            {
                end++;
            }

            end = Math.Min(text.Length, end + 1);
            if (i == end || text[i] is '\r' or '\n')
            {
                output.Append(text, i, end - i);
            }
            else
            {
                var levels = columns / unit;
                var rest = columns % unit;
                if (to == IndentStyle.Tabs)
                {
                    output.Append('\t', levels);
                }
                else
                {
                    output.Append(' ', levels * Width(to));
                }

                output.Append(' ', rest).Append(text, i, end - i);
            }

            at = end;
        }

        return output.ToString();
    }
}
