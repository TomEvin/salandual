// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/UserMenu/UserMenuSettings.cs

namespace Salandual.Core.UserMenu;

public enum UserMenuItemKind { Program, Script, Submenu, Separator }

public enum UserMenuShowsFor { Both, Files, Folders }

public enum UserMenuFileKind { Any, Images, Audio, Video, Text, Archives, Programs }

public enum UserMenuCount { Any, One, Two, AtLeastTwo }

public enum UserMenuRuns { EachFile, Once }

public enum UserMenuOnFailure { GoOn, Stop }

public enum UserMenuPowerShell { Pwsh, Windows }

public enum UserMenuContextPlace { Submenu, Top, None }

public enum UserMenuUnfit { Hidden, Greyed }

public sealed record UserMenuItem
{
    public string Name { get; set; } = "";

    public UserMenuItemKind Kind { get; set; } = UserMenuItemKind.Program;

    public string Program { get; set; } = "";

    public string Arguments { get; set; } = "";

    public string StartIn { get; set; } = UserMenuVariables.DefaultStartIn;

    public string Script { get; set; } = "";

    public UserMenuPowerShell PowerShell { get; set; } = UserMenuPowerShell.Pwsh;

    public UserMenuShowsFor ShowsFor { get; set; } = UserMenuShowsFor.Both;

    public string Named { get; set; } = "*";

    public UserMenuFileKind FileKind { get; set; } = UserMenuFileKind.Any;

    public UserMenuCount Count { get; set; } = UserMenuCount.Any;

    public UserMenuRuns Runs { get; set; } = UserMenuRuns.EachFile;

    public UserMenuOnFailure OnFailure { get; set; } = UserMenuOnFailure.GoOn;

    public bool KeepWindowOpen { get; set; } = true;

    public bool AskFirst { get; set; }

    public bool InBar { get; set; }

    public UserMenuContextPlace InContextMenu { get; set; } = UserMenuContextPlace.Submenu;

    public string? Key { get; set; }

    public string? Icon { get; set; }

    public List<UserMenuItem> Items { get; set; } = [];

    public bool IsRunnable => Kind is UserMenuItemKind.Program or UserMenuItemKind.Script;

    public string PlainName => Name.Replace("&&", "\u0001", StringComparison.Ordinal).Replace("&", "", StringComparison.Ordinal).Replace('\u0001', '&');

    internal UserMenuItem Normalize() => this with
    {
        Name = Name ?? "",
        Program = Program ?? "",
        Arguments = Arguments ?? "",
        StartIn = StartIn ?? UserMenuVariables.DefaultStartIn,
        Script = Script ?? "",
        Named = string.IsNullOrWhiteSpace(Named) ? "*" : Named.Trim(),
        Kind = Enum.IsDefined(Kind) ? Kind : UserMenuItemKind.Program,
        PowerShell = Enum.IsDefined(PowerShell) ? PowerShell : UserMenuPowerShell.Pwsh,
        ShowsFor = Enum.IsDefined(ShowsFor) ? ShowsFor : UserMenuShowsFor.Both,
        FileKind = Enum.IsDefined(FileKind) ? FileKind : UserMenuFileKind.Any,
        Count = Enum.IsDefined(Count) ? Count : UserMenuCount.Any,
        Runs = Enum.IsDefined(Runs) ? Runs : UserMenuRuns.EachFile,
        OnFailure = Enum.IsDefined(OnFailure) ? OnFailure : UserMenuOnFailure.GoOn,
        InContextMenu = Enum.IsDefined(InContextMenu) ? InContextMenu : UserMenuContextPlace.Submenu,
        Key = string.IsNullOrWhiteSpace(Key) ? null : Key.Trim(),
        Icon = string.IsNullOrWhiteSpace(Icon) ? null : Icon.Trim(),
        Items = Kind == UserMenuItemKind.Submenu ? [.. (Items ?? []).Where(i => i is not null).Select(i => i.Normalize())] : [],
    };
}

public sealed record UserMenuSettings
{
    public List<UserMenuItem> Items { get; set; } = [];

    public UserMenuUnfit Unfit { get; set; } = UserMenuUnfit.Hidden;

    public IEnumerable<UserMenuItem> All() => Flatten(Items);

    public IReadOnlyList<(UserMenuItem Item, bool SeparatorAfter)> BarItems()
    {
        var buttons = new List<(UserMenuItem Item, bool SeparatorAfter)>();
        var gap = false;
        void Walk(IEnumerable<UserMenuItem> items)
        {
            foreach (var item in items)
            {
                if (item.Kind == UserMenuItemKind.Separator)
                {
                    gap = buttons.Count > 0;
                    continue;
                }

                if (item.InBar)
                {
                    if (gap)
                    {
                        buttons[^1] = (buttons[^1].Item, true);
                    }

                    buttons.Add((item, false));
                    gap = false;
                }

                if (item.Kind == UserMenuItemKind.Submenu)
                {
                    Walk(item.Items);
                }
            }
        }

        Walk(Items);
        return buttons;
    }

    private static IEnumerable<UserMenuItem> Flatten(IEnumerable<UserMenuItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var inner in Flatten(item.Items))
            {
                yield return inner;
            }
        }
    }

    internal UserMenuSettings Normalize() => this with
    {
        Items = [.. (Items ?? []).Where(i => i is not null).Select(i => i.Normalize())],
        Unfit = Enum.IsDefined(Unfit) ? Unfit : UserMenuUnfit.Hidden,
    };
}
