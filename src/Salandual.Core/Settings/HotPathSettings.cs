// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/HotPathSettings.cs

using System.Text;

namespace Salandual.Core.Settings;

public sealed record HotPath(int Slot, string Name, string Path, bool InStrip = true, bool InMenu = true);

public sealed record HotPathSettings
{
    public const int SlotCount = 30;

    public List<HotPath> Paths { get; set; } = [];

    public bool ShiftDigits { get; set; }

    public bool AskForName { get; set; }

    public HotPath? this[int slot] => Paths.Find(p => p.Slot == slot);

    public static string? KeyOf(int slot) => slot is >= 1 and <= 10 ? "Ctrl+" + (slot % 10).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    public static int SlotOfDigit(int digit) => digit == 0 ? 10 : digit;

    public int? NextFreeSlot()
    {
        for (var slot = 1; slot <= SlotCount; slot++)
        {
            if (this[slot] is null)
            {
                return slot;
            }
        }

        return null;
    }

    public HotPathSettings With(HotPath path) => this with { Paths = Sorted(Paths.Where(p => p.Slot != path.Slot).Append(path)) };

    public HotPathSettings Without(int slot) => this with { Paths = [.. Paths.Where(p => p.Slot != slot)] };

    public HotPathSettings Swap(int a, int b)
    {
        if (a == b || a is < 1 or > SlotCount || b is < 1 or > SlotCount)
        {
            return this;
        }

        return this with { Paths = Sorted(Paths.Select(p => p.Slot == a ? p with { Slot = b } : p.Slot == b ? p with { Slot = a } : p)) };
    }

    public static string DefaultName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = System.IO.Path.GetFileName(trimmed);
        if (name.Length == 0 && trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            name = trimmed[(trimmed.LastIndexOf('\\') + 1)..];
        }

        return name.Length > 0 ? name : trimmed.Length > 0 ? trimmed : path;
    }

    public static string Expand(string path)
    {
        var sb = new StringBuilder(path.Length + 32);
        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] != '$' || i + 1 >= path.Length)
            {
                sb.Append(path[i]);
                continue;
            }

            var next = path[i + 1];
            if (next == '$')
            {
                sb.Append('$');
                i++;
            }
            else if (next == '[' && path.IndexOf(']', i + 2) is var end and > 0)
            {
                sb.Append(Environment.GetEnvironmentVariable(path[(i + 2)..end]) ?? path[i..(end + 1)]);
                i = end;
            }
            else if (next == '(' && path.IndexOf(')', i + 2) is var close and > 0)
            {
                var variable = path[(i + 2)..close];
                var value = variable.ToUpperInvariant() switch
                {
                    "WINDIR" => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "SYSDIR" => Environment.SystemDirectory,
                    _ => null,
                };
                sb.Append(value ?? path[i..(close + 1)]);
                i = close;
            }
            else
            {
                sb.Append('$');
            }
        }

        return Environment.ExpandEnvironmentVariables(sb.ToString());
    }

    internal HotPathSettings Normalize()
    {
        var seen = new HashSet<int>();
        var paths = new List<HotPath>();
        foreach (var p in Paths ?? [])
        {
            if (p is null || p.Slot is < 1 or > SlotCount || string.IsNullOrWhiteSpace(p.Path) || !seen.Add(p.Slot))
            {
                continue;
            }

            var path = p.Path.Trim();
            paths.Add(p with { Path = path, Name = string.IsNullOrWhiteSpace(p.Name) ? DefaultName(path) : p.Name.Trim() });
        }

        return this with { Paths = Sorted(paths) };
    }

    private static List<HotPath> Sorted(IEnumerable<HotPath> paths) => [.. paths.OrderBy(p => p.Slot)];
}

public sealed record PlaceSetting(string Id, bool InMenu, bool InStrip);

public sealed record DriveMenuSettings
{
    public const string Desktop = "Desktop", Documents = "Documents", Downloads = "Downloads", Music = "Music",
        Pictures = "Pictures", Videos = "Videos", Cloud = "Cloud", OtherPanel = "OtherPanel", Network = "Network", Registry = "Registry";

    public static IReadOnlyList<PlaceSetting> DefaultPlaces { get; } =
    [
        new(Desktop, true, false), new(Documents, true, false), new(Downloads, true, false), new(Music, false, false),
        new(Pictures, true, false), new(Videos, false, false), new(Cloud, true, false), new(OtherPanel, true, false),
        new(Network, true, false), new(Registry, true, false),
    ];

    public string HiddenDrives { get; set; } = "";

    public string SeparatorsAfter { get; set; } = "";

    public bool FreeSpace { get; set; } = true;

    public List<PlaceSetting> Places { get; set; } = [.. DefaultPlaces];

    public bool IsDriveShown(char letter) => !HiddenDrives.Contains(char.ToUpperInvariant(letter));

    public bool HasSeparatorAfter(char letter) => SeparatorsAfter.Contains(char.ToUpperInvariant(letter));

    public PlaceSetting Place(string id) => Places.Find(p => p.Id == id) ?? DefaultPlaces.First(p => p.Id == id);

    public static string Toggle(string letters, char letter, bool on)
    {
        letter = char.ToUpperInvariant(letter);
        var set = new SortedSet<char>(letters.Where(char.IsAsciiLetterUpper));
        if (on)
        {
            set.Add(letter);
        }
        else
        {
            set.Remove(letter);
        }

        return new string([.. set]);
    }

    internal DriveMenuSettings Normalize()
    {
        static string Letters(string? s) => new([.. new SortedSet<char>((s ?? "").ToUpperInvariant().Where(char.IsAsciiLetterUpper))]);
        var places = DefaultPlaces.Select(d => (Places ?? []).Find(p => p?.Id == d.Id) is { } p ? p : d).ToList();
        return this with { HiddenDrives = Letters(HiddenDrives), SeparatorsAfter = Letters(SeparatorsAfter), Places = places };
    }
}
