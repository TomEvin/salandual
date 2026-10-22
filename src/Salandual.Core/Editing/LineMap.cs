// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Editing/LineMap.cs

namespace Salandual.Core.Editing;

internal sealed class LineMap
{
    private readonly List<int> _starts = [0];

    public LineMap(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n' || (c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
            {
                _starts.Add(i + 1);
            }
        }
    }

    public int Count => _starts.Count;

    public int Line(int offset)
    {
        var index = _starts.BinarySearch(offset);
        return (index >= 0 ? index : ~index - 1) + 1;
    }

    public int Start(int line) => _starts[Math.Clamp(line - 1, 0, _starts.Count - 1)];
}
