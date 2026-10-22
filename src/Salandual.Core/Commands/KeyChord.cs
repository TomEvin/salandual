// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Commands/KeyChord.cs

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Salandual.Core.Commands;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
}

public readonly record struct KeyChord(KeyModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PgUp"] = "PageUp", ["Page Up"] = "PageUp", ["PgDn"] = "PageDown", ["Page Down"] = "PageDown",
        ["Del"] = "Delete", ["Ins"] = "Insert", ["Esc"] = "Escape", ["Return"] = "Enter",
        ["Num +"] = "Num+", ["Num -"] = "Num-", ["Num *"] = "Num*", ["Num /"] = "Num/",
        ["Grave"] = "`", ["Backslash"] = "\\",
    };

    private static readonly HashSet<string> _named = new(StringComparer.OrdinalIgnoreCase)
    {
        "Up", "Down", "Left", "Right", "Home", "End", "PageUp", "PageDown", "Insert", "Delete", "Backspace",
        "Enter", "Tab", "Space", "Escape", "Num+", "Num-", "Num*", "Num/", "Apps",
    };

    public override string ToString()
    {
        var sb = new StringBuilder();
        if ((Modifiers & KeyModifiers.Ctrl) != 0)
        {
            sb.Append("Ctrl+");
        }

        if ((Modifiers & KeyModifiers.Alt) != 0)
        {
            sb.Append("Alt+");
        }

        if ((Modifiers & KeyModifiers.Shift) != 0)
        {
            sb.Append("Shift+");
        }

        return sb.Append(Key).ToString();
    }

    public static KeyChord Parse(string text)
        => TryParse(text, out var chord) ? chord : throw new FormatException($"Not a key chord: '{text}'.");

    public static bool TryParse(string? text, [NotNullWhen(true)] out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var rest = text.Trim();
        if (rest.EndsWith(" (+)", StringComparison.Ordinal))
        {
            rest = rest[..^4];
        }

        var mods = KeyModifiers.None;
        while (true)
        {
            var next = StripModifier(rest, "Ctrl+", KeyModifiers.Ctrl, ref mods)
                ?? StripModifier(rest, "Alt+", KeyModifiers.Alt, ref mods)
                ?? StripModifier(rest, "Shift+", KeyModifiers.Shift, ref mods);
            if (next is null)
            {
                break;
            }

            rest = next;
        }

        var key = NormalizeKey(rest);
        if (key is null)
        {
            return false;
        }

        chord = new KeyChord(mods, key);
        return true;
    }

    private static string? StripModifier(string text, string prefix, KeyModifiers flag, ref KeyModifiers mods)
    {
        if (text.Length > prefix.Length && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            mods |= flag;
            return text[prefix.Length..];
        }

        return null;
    }

    private static string? NormalizeKey(string key)
    {
        if (_aliases.TryGetValue(key, out var alias))
        {
            return alias;
        }

        if (_named.TryGetValue(key, out var named))
        {
            return named;
        }

        if (key.Length == 1)
        {
            return char.IsLetter(key[0]) ? key.ToUpperInvariant() : key;
        }

        if (key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 24)
        {
            return "F" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return null;
    }
}
