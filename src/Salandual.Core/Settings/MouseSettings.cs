// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/MouseSettings.cs

namespace Salandual.Core.Settings;

public enum RightClickMenu
{
    Quick,

    Windows,
}

public enum RightClickUnselected
{
    MenuForItem,

    SelectOnlyItem,

    RightButtonSelects,
}

public enum PropertiesWindow { Salandual, Windows }

public enum PlainDrag
{
    AlwaysCopies,

    WindowsRule,
}

public enum AfterDrop
{
    StartAtOnce,

    ShowDialog,
}

public enum DriveChipDrop
{
    FolderItOpens,

    DriveRoot,
}

public sealed record PinnedShellCommand
{
    public string? Verb { get; set; }

    public List<string> Path { get; set; } = [];

    public string Title => Path.Count > 0 ? Path[^1] : Verb ?? "";

    public bool Matches(string? verb, IReadOnlyList<string> path)
        => !string.IsNullOrEmpty(Verb) && !string.IsNullOrEmpty(verb)
            ? string.Equals(Verb, verb, StringComparison.OrdinalIgnoreCase)
            : string.IsNullOrEmpty(Verb) && string.IsNullOrEmpty(verb) && Path.SequenceEqual(path, StringComparer.CurrentCultureIgnoreCase);
}

public sealed record MouseSettings
{
    public const int MaxPinned = 20;

    public RightClickMenu RightClick { get; set; } = RightClickMenu.Quick;

    public RightClickUnselected RightClickUnselected { get; set; } = RightClickUnselected.MenuForItem;

    public PropertiesWindow AltEnter { get; set; } = PropertiesWindow.Salandual;

    public List<PinnedShellCommand> Pinned { get; set; } = [];

    public PlainDrag PlainDrag { get; set; } = PlainDrag.AlwaysCopies;

    public AfterDrop AfterDrop { get; set; } = AfterDrop.StartAtOnce;

    public DriveChipDrop DriveChipDrop { get; set; } = DriveChipDrop.FolderItOpens;

    internal MouseSettings Normalize() => this with
    {
        PlainDrag = Enum.IsDefined(PlainDrag) ? PlainDrag : PlainDrag.AlwaysCopies,
        AfterDrop = Enum.IsDefined(AfterDrop) ? AfterDrop : AfterDrop.StartAtOnce,
        DriveChipDrop = Enum.IsDefined(DriveChipDrop) ? DriveChipDrop : DriveChipDrop.FolderItOpens,
        RightClick = Enum.IsDefined(RightClick) ? RightClick : RightClickMenu.Quick,
        RightClickUnselected = Enum.IsDefined(RightClickUnselected) ? RightClickUnselected : RightClickUnselected.MenuForItem,
        AltEnter = Enum.IsDefined(AltEnter) ? AltEnter : PropertiesWindow.Salandual,
        Pinned = [.. (Pinned ?? [])
            .Where(p => p is not null && (!string.IsNullOrWhiteSpace(p.Verb) || p.Path is { Count: > 0 }))
            .Select(p => p with { Path = [.. (p.Path ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())] })
            .Take(MaxPinned)],
    };
}
