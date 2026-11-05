// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Commands/YourKeys.cs

namespace Salandual.Core.Commands;

public sealed record CommandKeys
{
    public const int MaxKeys = 20;

    public string Command { get; set; } = "";

    public List<string> Keys { get; set; } = [];

    public CommandKeys()
    {
    }

    public CommandKeys(string command, IEnumerable<KeyChord> keys)
    {
        Command = command;
        Keys = [.. keys.Select(k => k.ToString())];
    }

    public IEnumerable<KeyChord> Chords()
        => (Keys ?? []).Select(k => KeyChord.TryParse(k, out var c) ? c : (KeyChord?)null).OfType<KeyChord>().Distinct();

    internal static List<CommandKeys> Clean(IEnumerable<CommandKeys?>? list)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<CommandKeys>();
        foreach (var entry in (list ?? []).Reverse())
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Command) || !seen.Add(entry.Command.Trim()))
            {
                continue;
            }

            kept.Add(new CommandKeys(entry.Command.Trim(), entry.Chords().Take(MaxKeys)));
        }

        kept.Reverse();
        return kept.Count > Settings.KeyboardSettings.MaxYourKeys ? kept[^Settings.KeyboardSettings.MaxYourKeys..] : kept;
    }
}

public enum KeyRefusal
{
    None,

    Types,

    Selects,

    Moves,

    Opens,

    Closes,

    Switches,

    Deletes,

    OpensMenu,
}

public sealed record KeyHolder(string? Command, NotYetKey? NotYet)
{
    public bool IsFree => Command is null && NotYet is null;
}

public static class YourKeys
{
    private static readonly HashSet<string> _moves = new(StringComparer.Ordinal) { "Up", "Down", "Left", "Right", "Home", "End", "PageUp", "PageDown" };

    public static KeyRefusal WhyNot(CommandRegistry registry, string command, KeyChord chord)
    {
        if (registry.MapsGive(chord, command))
        {
            return KeyRefusal.None;
        }

        if (chord.Modifiers == KeyModifiers.Alt && MenuLetters.Contains(chord.Key))
        {
            return KeyRefusal.OpensMenu;
        }

        if (chord.Modifiers != KeyModifiers.None)
        {
            return KeyRefusal.None;
        }

        return chord.Key switch
        {
            "Space" => KeyRefusal.Selects,
            "Enter" => KeyRefusal.Opens,
            "Escape" => KeyRefusal.Closes,
            "Tab" => KeyRefusal.Switches,
            "Backspace" => KeyRefusal.Deletes,
            _ when _moves.Contains(chord.Key) => KeyRefusal.Moves,
            _ when chord.Key.Length == 1 => KeyRefusal.Types,
            _ => KeyRefusal.None,
        };
    }

    public static IReadOnlySet<string> MenuLetters { get; } = MainMenu.Create()
        .Select(m => m.Header is { } h && h.IndexOf('_', StringComparison.Ordinal) is var i and >= 0 && i + 1 < h.Length ? char.ToUpperInvariant(h[i + 1]).ToString() : null)
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);

    public static string? MenuOf(string letter)
        => MainMenu.Create().Select(m => m.Header).OfType<string>()
            .FirstOrDefault(h => h.IndexOf('_', StringComparison.Ordinal) is var i and >= 0 && i + 1 < h.Length && string.Equals(h[i + 1].ToString(), letter, StringComparison.OrdinalIgnoreCase))
            ?.Replace("_", "", StringComparison.Ordinal);

    public static KeyHolder Holder(CommandRegistry registry, string command, KeyChord chord)
        => registry.Resolve(chord) is { } id && id != command ? new KeyHolder(id, null) : new KeyHolder(null, registry.NotYet(chord));

    public static (List<CommandKeys> Keys, string? From) Assign(CommandRegistry registry, IReadOnlyList<CommandKeys> keys, string command, KeyChord chord)
    {
        var from = registry.Resolve(chord) is { } id && id != command ? id : null;
        var list = keys.ToList();
        if (from is not null)
        {
            list = Set(registry, list, from, registry.ChordsFor(from).Where(c => c != chord));
        }

        var mine = registry.ChordsFor(command).ToList();
        if (!mine.Contains(chord))
        {
            mine.Add(chord);
        }

        return (Set(registry, list, command, mine), from);
    }

    public static List<CommandKeys> Remove(CommandRegistry registry, IReadOnlyList<CommandKeys> keys, string command, KeyChord chord)
        => Set(registry, keys.ToList(), command, registry.ChordsFor(command).Where(c => c != chord));

    public static List<CommandKeys> Reset(IReadOnlyList<CommandKeys> keys, string command)
        => [.. keys.Where(k => k.Command != command)];

    private static List<CommandKeys> Set(CommandRegistry registry, List<CommandKeys> list, string command, IEnumerable<KeyChord> chords)
    {
        var wanted = chords.Distinct().Take(CommandKeys.MaxKeys).ToList();
        list.RemoveAll(k => k.Command == command);
        if (!wanted.SequenceEqual(registry.MapChordsFor(command)))
        {
            list.Add(new CommandKeys(command, wanted));
        }

        return list;
    }
}
