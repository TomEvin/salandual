// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Commands/CommandRegistry.cs

namespace Salandual.Core.Commands;

public sealed record CommandDefinition(string Id, string Title, string Category, IReadOnlyList<KeyChord> DefaultChords, string PlannedFor)
{
    public string? ShortLabel { get; init; }
}

public sealed class CommandRegistry
{
    private readonly Dictionary<string, CommandDefinition> _commands = new(StringComparer.Ordinal);
    private readonly List<CommandDefinition> _order = [];
    private Dictionary<KeyChord, string> _keyMap = [];
    private Dictionary<string, List<KeyChord>> _chords = new(StringComparer.Ordinal);
    private List<NotYetKey> _notYet = [];

    public IReadOnlyCollection<CommandDefinition> Commands => _order;

    public CommandDefinition this[string id] => _commands[id];

    public KeyMapName KeyMap { get; private set; } = KeyMapName.Salamander;

    public event EventHandler? KeyMapChanged;

    public IReadOnlyList<NotYetKey> NotYetKeys => _notYet;

    public void Register(CommandDefinition command)
    {
        if (!_commands.TryAdd(command.Id, command))
        {
            throw new InvalidOperationException($"Command '{command.Id}' is registered twice.");
        }

        _order.Add(command);
        _chords[command.Id] = [];
        foreach (var chord in command.DefaultChords)
        {
            if (!_keyMap.TryAdd(chord, command.Id))
            {
                throw new InvalidOperationException($"{chord} is bound to both '{_keyMap[chord]}' and '{command.Id}'.");
            }

            _chords[command.Id].Add(chord);
        }
    }

    public void UseKeyMap(KeyMapName name)
    {
        if (!Enum.IsDefined(name))
        {
            name = KeyMapName.Salamander;
        }

        (_keyMap, _chords, _notYet) = KeyMaps.Build(name, _order);
        var changed = name != KeyMap;
        KeyMap = name;
        if (changed)
        {
            KeyMapChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool TryGet(string id, out CommandDefinition command) => _commands.TryGetValue(id, out command!);

    public string? Resolve(KeyChord chord) => _keyMap.TryGetValue(chord, out var id) ? id : null;

    public NotYetKey? NotYet(KeyChord chord) => _notYet.FirstOrDefault(k => k.Chord == chord);

    public IEnumerable<KeyChord> ChordsFor(string id) => _chords.TryGetValue(id, out var chords) ? chords : [];

    public KeyChord? FirstChord(string id) => _chords.TryGetValue(id, out var chords) && chords.Count > 0 ? chords[0] : null;

    public (IReadOnlyDictionary<KeyChord, string> Map, IReadOnlyList<NotYetKey> NotYet) KeysOf(KeyMapName name)
    {
        var (map, _, notYet) = KeyMaps.Build(name, _order);
        return (map, notYet);
    }

    public IReadOnlyList<(KeyChord Chord, string? Before, string? After)> Differences(KeyMapName from, KeyMapName to)
    {
        var (a, aNotYet) = KeysOf(from);
        var (b, bNotYet) = KeysOf(to);
        string? Target(IReadOnlyDictionary<KeyChord, string> m, IReadOnlyList<NotYetKey> n, KeyChord c)
            => m.TryGetValue(c, out var id) ? id : n.Any(k => k.Chord == c) ? "" : null;
        return [.. a.Keys.Concat(aNotYet.Select(k => k.Chord)).Concat(b.Keys).Concat(bNotYet.Select(k => k.Chord)).Distinct()
            .Select(c => (Chord: c, Before: Target(a, aNotYet, c), After: Target(b, bNotYet, c)))

            .Where(d => d.Before != d.After)
            .OrderBy(d => d.Chord.Modifiers).ThenBy(d => FunctionKey(d.Chord.Key)).ThenBy(d => d.Chord.Key, StringComparer.Ordinal)];
    }

    private static int FunctionKey(string key)
        => key.Length > 1 && key[0] == 'F' && int.TryParse(key.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 100;

    public static CommandRegistry CreateDefault()
    {
        var registry = new CommandRegistry();
        foreach (var command in DefaultCommands.All())
        {
            registry.Register(command);
        }

        return registry;
    }
}
