// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/HighlightSettings.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Settings;

public enum RuleTarget
{
    Files,
    Folders,
    Both,
}

public enum NameColour
{
    Normal,
    Dimmed,
    Blue,
    Green,
    Purple,
    Orange,
    Red,
    Teal,
    Pink,
    Grey,

    Own,

    Yellow,
    Amber,
    Gold,
    Brown,
    Coral,
    Maroon,
    Rose,
    Magenta,
    Plum,
    Violet,
    Indigo,
    Navy,
    Sky,
    Cyan,
    Aqua,
    Mint,
    Forest,
    Lime,
    Olive,
    Sand,
    Peach,
    Lavender,
    Slate,
    Charcoal,
}

public enum NameStyle
{
    Normal,
    Bold,
    Italic,
}

public sealed record HighlightRule
{
    public string Name { get; set; } = "";

    public bool On { get; set; } = true;

    public RuleTarget For { get; set; } = RuleTarget.Both;

    public string Masks { get; set; } = "";

    public FileAttributes MustHave { get; set; }

    public FileAttributes MustNotHave { get; set; }

    public bool AnyOf { get; set; }

    public NameColour Colour { get; set; }

    public string? OwnLight { get; set; }

    public string? OwnDark { get; set; }

    public NameStyle Style { get; set; }

    internal HighlightRule? Normalize()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return null;
        }

        var own = Colour == NameColour.Own && ColorMath.TryParseHex(OwnLight, out _) && ColorMath.TryParseHex(OwnDark, out _);
        return this with
        {
            Name = Name.Trim(),
            Masks = (Masks ?? "").Trim(),
            For = Enum.IsDefined(For) ? For : RuleTarget.Both,
            Colour = Enum.IsDefined(Colour) && (Colour != NameColour.Own || own) ? Colour : NameColour.Normal,
            OwnLight = own ? OwnLight : null,
            OwnDark = own ? OwnDark : null,
            Style = Enum.IsDefined(Style) ? Style : NameStyle.Normal,
            MustNotHave = MustNotHave & ~MustHave,
        };
    }
}

public sealed record FolderLook
{
    public NameColour Colour { get; set; }

    public string? Mark { get; set; }

    public string? Image { get; set; }

    public bool IsEmpty => Colour == NameColour.Normal && Mark is null && Image is null;

    internal FolderLook Normalize() => this with
    {
        Colour = Enum.IsDefined(Colour) && Colour is not (NameColour.Own or NameColour.Dimmed) ? Colour : NameColour.Normal,
        Mark = Mark is not null && FolderMarks.All.Contains(Mark) ? Mark : null,
        Image = Image is not null && FolderMarks.IsImageName(Image) ? Image : null,
    };
}

public static class FolderMarks
{
    public static readonly IReadOnlyList<string> All =
    [
        "★", "♥", "✓", "✕", "⚑", "!", "?", "⚠", "⚡", "</>", "#",
        "♪", "▶", "⚙", "☁", "☀", "☾", "✿", "♣", "✎", "✉", "☎",
        "$", "€", "@", "⌂", "⌛", "☺", "◆", "●", "▲", "■",
    ];

    public const string ImageFolder = "folder-images";

    public static readonly IReadOnlyList<string> ImageExtensions = [".png", ".ico", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    public const long MaxImageBytes = 4 * 1024 * 1024;

    public static bool IsImageName(string name)
    {
        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        return stem.Length is > 0 and <= 64 && stem.All(char.IsAsciiHexDigit) && ImageExtensions.Contains(ext.ToLowerInvariant()) && name == stem + ext;
    }
}

public sealed record HighlightSettings
{
    public const int MaxRules = 100;

    public List<HighlightRule>? Rules { get; set; }

    public Dictionary<string, FolderLook> Folders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<HighlightRule> Effective => Rules ?? Defaults();

    public static List<HighlightRule> Defaults(bool dimHidden = true) =>
    [
        new() { Name = Strings.Highlight_Encrypted, MustHave = FileAttributes.Encrypted, Colour = NameColour.Green },
        new() { Name = Strings.Highlight_Compressed, MustHave = FileAttributes.Compressed, Colour = NameColour.Blue },
        new() { Name = Strings.Highlight_Hidden, On = dimHidden, MustHave = FileAttributes.Hidden | FileAttributes.System, AnyOf = true, Colour = NameColour.Dimmed },
        new() { Name = Strings.Highlight_Archives, On = false, For = RuleTarget.Files, Masks = "*.zip;*.7z;*.rar;*.tar;*.gz", Colour = NameColour.Purple },
        new() { Name = Strings.Highlight_Programs, On = false, For = RuleTarget.Files, Masks = "*.exe;*.msi;*.bat;*.cmd;*.ps1", Colour = NameColour.Orange },
        new() { Name = Strings.Highlight_Temporary, On = false, For = RuleTarget.Files, Masks = "*.tmp;*.bak;~*", Colour = NameColour.Grey, Style = NameStyle.Italic },
    ];

    public static string Key(string path)
    {
        var p = path.Trim();
        return p.Length > 3 ? p.TrimEnd('\\') : p;
    }

    public HighlightSettings? Moved(string from, string to)
    {
        var (f, t) = (Key(from), Key(to));
        var moved = Folders.Where(p => string.Equals(p.Key, f, StringComparison.OrdinalIgnoreCase)
            || p.Key.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase)).ToList();
        if (moved.Count == 0 || string.Equals(f, t, StringComparison.Ordinal))
        {
            return null;
        }

        var folders = new Dictionary<string, FolderLook>(Folders, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, look) in moved)
        {
            folders.Remove(key);
            folders[t + key[f.Length..]] = look;
        }

        return this with { Folders = folders };
    }

    public HighlightSettings WithFolder(string path, FolderLook? look)
    {
        var folders = new Dictionary<string, FolderLook>(Folders, StringComparer.OrdinalIgnoreCase);
        if (look is null || look.IsEmpty)
        {
            folders.Remove(Key(path));
        }
        else
        {
            folders[Key(path)] = look;
        }

        return this with { Folders = folders };
    }

    internal HighlightSettings Normalize(bool dimHidden)
    {
        var rules = Rules?.Select(r => r?.Normalize()).OfType<HighlightRule>().Take(MaxRules).ToList();
        var folders = new Dictionary<string, FolderLook>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, look) in Folders ?? [])
        {
            if (!string.IsNullOrWhiteSpace(path) && look?.Normalize() is { IsEmpty: false } kept)
            {
                folders[Key(path)] = kept;
            }
        }

        if (dimHidden && Rules is not null == rules is not null && (Rules is null || Rules.SequenceEqual(rules!))
            && Folders is not null && Folders.Comparer == StringComparer.OrdinalIgnoreCase && Folders.Count == folders.Count
            && folders.All(f => Folders.TryGetValue(f.Key, out var l) && l == f.Value))
        {
            return this;
        }

        return this with { Rules = rules ?? (dimHidden ? null : Defaults(dimHidden: false)), Folders = folders };
    }
}
