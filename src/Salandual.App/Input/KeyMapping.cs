// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Input/KeyMapping.cs

using Avalonia.Input;
using Salandual.Core.Commands;
using CoreModifiers = Salandual.Core.Commands.KeyModifiers;

namespace Salandual.App.Input;

public static class KeyMapping
{
    public static bool TryGetChord(Key key, Avalonia.Input.KeyModifiers modifiers, out KeyChord chord)
    {
        chord = default;
        var name = KeyName(key);
        if (name is null)
        {
            return false;
        }

        var mods = CoreModifiers.None;
        if ((modifiers & Avalonia.Input.KeyModifiers.Control) != 0)
        {
            mods |= CoreModifiers.Ctrl;
        }

        if ((modifiers & Avalonia.Input.KeyModifiers.Alt) != 0)
        {
            mods |= CoreModifiers.Alt;
        }

        if ((modifiers & Avalonia.Input.KeyModifiers.Shift) != 0)
        {
            mods |= CoreModifiers.Shift;
        }

        chord = new KeyChord(mods, name);
        return true;
    }

    public static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F24 => "F" + (key - Key.F1 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.Back => "Backspace",
        Key.Enter => "Enter",
        Key.Tab => "Tab",
        Key.Space => "Space",
        Key.Escape => "Escape",
        Key.Apps => "Apps",
        Key.Add => "Num+",
        Key.Subtract => "Num-",
        Key.Multiply => "Num*",
        Key.Divide => "Num/",
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe or Key.OemBackslash => "\\",
        Key.OemTilde => "`",
        _ => null,
    };

    private static Dictionary<string, Key>? _keysByName;

    public static KeyGesture? ToGesture(KeyChord chord)
    {
        _keysByName ??= BuildReverseMap();
        if (!_keysByName.TryGetValue(chord.Key, out var key))
        {
            return null;
        }

        var mods = Avalonia.Input.KeyModifiers.None;
        if ((chord.Modifiers & CoreModifiers.Ctrl) != 0)
        {
            mods |= Avalonia.Input.KeyModifiers.Control;
        }

        if ((chord.Modifiers & CoreModifiers.Alt) != 0)
        {
            mods |= Avalonia.Input.KeyModifiers.Alt;
        }

        if ((chord.Modifiers & CoreModifiers.Shift) != 0)
        {
            mods |= Avalonia.Input.KeyModifiers.Shift;
        }

        return new KeyGesture(key, mods);
    }

    private static Dictionary<string, Key> BuildReverseMap()
    {
        var map = new Dictionary<string, Key>(StringComparer.Ordinal);
        foreach (var key in Enum.GetValues<Key>())
        {
            if (KeyName(key) is { } name)
            {
                map.TryAdd(name, key);
            }
        }

        return map;
    }
}
