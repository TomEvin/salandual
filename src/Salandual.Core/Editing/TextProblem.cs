// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Editing/TextProblem.cs

namespace Salandual.Core.Editing;

public sealed record TextProblem(int Offset, int Length, int Line, int Column, string Message)
{
    public static TextProblem At(string text, int offset, int length, string message)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var (line, column) = LineColumn(text, offset);
        return new TextProblem(offset, Math.Max(0, Math.Min(length, text.Length - offset)), line, column, message);
    }

    public static TextProblem AtLine(string text, int line, int column, string message)
    {
        var offset = OffsetOf(text, line, column);
        var end = offset;
        while (end < text.Length && end - offset < 40 && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        if (end == offset && end < text.Length && text[end] is not ('\r' or '\n'))
        {
            end++;
        }

        return new TextProblem(offset, end - offset, Math.Max(1, line), Math.Max(1, column), message);
    }

    public static (int Line, int Column) LineColumn(string text, int offset)
    {
        var line = 1;
        var start = 0;
        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
            {
                line++;
                start = i + 1;
            }
        }

        return (line, offset - start + 1);
    }

    public static int OffsetOf(string text, int line, int column)
    {
        var offset = 0;
        for (var l = 1; l < line && offset < text.Length; offset++)
        {
            if (text[offset] == '\n' || (text[offset] == '\r' && (offset + 1 >= text.Length || text[offset + 1] != '\n')))
            {
                l++;
            }
        }

        for (var c = 1; c < column && offset < text.Length && text[offset] is not ('\r' or '\n'); c++)
        {
            offset++;
        }

        return offset;
    }
}

public readonly record struct FoldRange(int Start, int End, int Lines);

public sealed record CheckResult(IReadOnlyList<TextProblem> Problems, IReadOnlyList<FoldRange> Folds)
{
    public static CheckResult Empty { get; } = new([], []);
}
