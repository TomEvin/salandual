// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Search/HexInput.cs

using System.Text;

namespace Salandual.Core.Search;

public static class HexInput
{
    public static string Format(string text)
    {
        var result = new StringBuilder(text.Length + (text.Length / 2));
        var digits = 0;
        foreach (var c in text)
        {
            if (!Uri.IsHexDigit(c))
            {
                continue;
            }

            if (digits > 0 && digits % 2 == 0)
            {
                result.Append(' ');
            }

            result.Append(char.ToUpperInvariant(c));
            digits++;
        }

        return result.ToString();
    }

    public static int CaretAfter(int digits) => digits <= 0 ? 0 : digits + ((digits - 1) / 2);

    public static int DigitsBefore(string text, int caret)
    {
        var count = 0;
        for (var i = 0; i < Math.Min(caret, text.Length); i++)
        {
            if (Uri.IsHexDigit(text[i]))
            {
                count++;
            }
        }

        return count;
    }

    public static bool IsHex(string text) => text.All(c => Uri.IsHexDigit(c) || char.IsWhiteSpace(c));

    public static string FromText(string text)
        => IsHex(text) ? Format(text) : Format(Convert.ToHexString(Encoding.UTF8.GetBytes(text)));

    public static string? ToText(string hex)
    {
        var digits = Format(hex).Replace(" ", "", StringComparison.Ordinal);
        if (digits.Length == 0 || digits.Length % 2 != 0)
        {
            return null;
        }

        try
        {
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(Convert.FromHexString(digits));
            return text.Any(c => char.IsControl(c) && c != '\t') ? null : text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
