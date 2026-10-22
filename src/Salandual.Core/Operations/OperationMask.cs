// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Operations/OperationMask.cs

using System.Text;

namespace Salandual.Core.Operations;

public static class OperationMask
{
    public static bool IsIdentity(string? mask) => string.IsNullOrEmpty(mask) || mask is "*.*" or "*";

    public static bool IsMask(string text) => text.AsSpan().IndexOfAny('*', '?') >= 0;

    public static string Apply(string name, string? mask)
    {
        if (IsIdentity(mask))
        {
            return name;
        }

        var result = new StringBuilder(name.Length + mask!.Length);
        var n = 0;
        for (var m = 0; m < mask.Length; m++)
        {
            var c = mask[m];
            if (c == '?')
            {
                if (n < name.Length)
                {
                    result.Append(name[n++]);
                }
            }
            else if (c == '*')
            {
                var end = name.Length;
                if (m + 1 < mask.Length && mask[m + 1] is not ('*' or '?'))
                {
                    var stop = name.LastIndexOf(mask[m + 1]);
                    if (stop >= n)
                    {
                        end = stop;
                    }
                    else if (mask.IndexOf('.', m + 1) >= 0)
                    {
                        var dot = name.LastIndexOf('.');
                        end = dot > n ? dot : name.Length;
                    }
                }

                result.Append(name, n, end - n);
                n = end;
            }
            else if (c == '.')
            {
                result.Append('.');
                var dot = name.LastIndexOf('.');
                n = dot >= n ? dot + 1 : name.Length;
            }
            else
            {
                result.Append(c);
                if (n < name.Length && char.ToUpperInvariant(name[n]) == char.ToUpperInvariant(c))
                {
                    n++;
                }
            }
        }

        if (result.Length > 1 && result[^1] == '.' && !name.EndsWith('.'))
        {
            result.Length--;
        }

        return result.ToString();
    }
}
