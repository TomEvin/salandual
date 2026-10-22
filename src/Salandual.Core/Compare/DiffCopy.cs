// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Compare/DiffCopy.cs

namespace Salandual.Core.Compare;

public static class DiffCopy
{
    public static int Apply(IReadOnlyList<DiffRow> rows, IEnumerable<int> which, bool toRight, IReadOnlyList<string> source, List<string> target, List<bool> copied)
    {
        var count = 0;

        foreach (var r in which.Where(r => r >= 0 && r < rows.Count && rows[r].Change != LineChange.Same).Distinct().OrderDescending())
        {
            var row = rows[r];
            var (from, to) = toRight ? (row.Left, row.Right) : (row.Right, row.Left);
            if (from >= 0 && to >= 0)
            {
                target[to] = source[from];
                copied[to] = true;
            }
            else if (from >= 0)
            {
                var at = 0;
                for (var p = r - 1; p >= 0; p--)
                {
                    var before = toRight ? rows[p].Right : rows[p].Left;
                    if (before >= 0)
                    {
                        at = before + 1;
                        break;
                    }
                }

                target.Insert(at, source[from]);
                copied.Insert(at, true);
            }
            else
            {
                target.RemoveAt(to);
                copied.RemoveAt(to);
            }

            count++;
        }

        return count;
    }

    public static string Join(IReadOnlyList<string> lines, string? lineEnding, bool endsWithBreak)
    {
        var eol = lineEnding switch { "LF" => "\n", "CR" => "\r", _ => "\r\n" };
        var text = string.Join(eol, lines);
        return endsWithBreak && lines.Count > 0 ? text + eol : text;
    }
}
