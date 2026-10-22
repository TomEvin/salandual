// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/QuickSearchPattern.cs

namespace Salandual.Core.Panels;

public static class QuickSearchPattern
{
    public static bool IsWildcard(char c) => c is '/' or '\\' or '<';

    public static bool HasWildcard(ReadOnlySpan<char> pattern) => pattern.IndexOfAny('/', '\\', '<') >= 0;

    public static bool IsMatch(ReadOnlySpan<char> name, ReadOnlySpan<char> pattern)
    {
        if (pattern.IsEmpty)
        {
            return true;
        }

        if (Glob(name, pattern, openEnd: true))
        {
            return true;
        }

        return pattern[^1] == '.' && name.LastIndexOf('.') <= 0 && Glob(name, pattern[..^1], openEnd: false);
    }

    private static bool Glob(ReadOnlySpan<char> name, ReadOnlySpan<char> pattern, bool openEnd)
    {
        int n = 0, p = 0, starP = -1, starN = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && IsWildcard(pattern[p]))
            {
                starP = p++;
                starN = n;
            }
            else if (p < pattern.Length && SameLetter(name[n], pattern[p]))
            {
                n++;
                p++;
            }
            else if (p == pattern.Length && openEnd)
            {
                return true;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                n = ++starN;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && IsWildcard(pattern[p]))
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool SameLetter(char a, char b) => a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}
