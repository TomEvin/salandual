// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/QuickOpen/QuickMatch.cs

namespace Salandual.Core.QuickOpen;

public sealed record MatchResult(int Score, IReadOnlyList<int> Positions);

public static class QuickMatch
{
    private const int Letter = 1, WordStart = 8, NameStart = 6, Next = 5, Gap = 1, WholeWord = 12;

    public static MatchResult? Match(string typed, string text)
    {
        var pattern = typed.Replace(" ", "", StringComparison.Ordinal);
        if (pattern.Length == 0)
        {
            return new MatchResult(0, []);
        }

        if (pattern.Length > text.Length)
        {
            return null;
        }

        var positions = new int[pattern.Length];
        var at = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            var plain = -1;
            var start = -1;
            for (var j = at; j < text.Length; j++)
            {
                if (!Same(text[j], pattern[i]))
                {
                    continue;
                }

                if (plain < 0)
                {
                    plain = j;
                }

                if (i > 0 && j == positions[i - 1] + 1)
                {
                    start = -1;
                    break;
                }

                if (IsWordStart(text, j) && Fits(pattern, i + 1, text, j + 1))
                {
                    start = j;
                    break;
                }
            }

            if (plain < 0)
            {
                return null;
            }

            positions[i] = start >= 0 ? start : plain;
            at = positions[i] + 1;
        }

        var score = 0;
        for (var i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            score += Letter;
            if (p == 0)
            {
                score += NameStart;
            }

            if (IsWordStart(text, p))
            {
                score += WordStart;
            }

            if (i > 0)
            {
                score += p == positions[i - 1] + 1 ? Next : -Math.Min(3, (p - positions[i - 1] - 1) * Gap);
            }
        }

        if (text.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
        {
            score += WholeWord;
        }

        score -= Math.Min(10, (text.Length - pattern.Length) / 8);
        return new MatchResult(score, positions);
    }

    private static bool Fits(string pattern, int from, string text, int at)
    {
        for (var i = from; i < pattern.Length; i++, at++)
        {
            while (at < text.Length && !Same(text[at], pattern[i]))
            {
                at++;
            }

            if (at >= text.Length)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Same(char a, char b) => char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

    public static bool IsWordStart(string text, int i)
    {
        if (i == 0)
        {
            return true;
        }

        var before = text[i - 1];
        var c = text[i];
        return before is ' ' or '\\' or '/' or '-' or '_' or '.' or '(' or ')' or '›' or ','
            || (char.IsLower(before) && char.IsUpper(c))
            || (char.IsDigit(before) && char.IsLetter(c));
    }
}
