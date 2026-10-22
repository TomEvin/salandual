// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Wizard/FirstStepsChoices.cs

using Salandual.App.Dialogs;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Wizard;

internal enum AccentChoice { Theme, Windows, Colour }

internal sealed record FirstStepsChoices
{
    public static readonly IReadOnlyList<string> LightThemes = [ThemeCatalog.SalandualLightId, "classic-light", "nord-light"];

    public static readonly IReadOnlyList<string> DarkThemes = [ThemeCatalog.SalandualDarkId, "graphite", "dracula"];

    public static readonly IReadOnlyList<Rgb> Colours = [new(0x0F, 0x6C, 0xBD), new(0x87, 0x64, 0xB8), new(0xD1, 0x34, 0x38), new(0x10, 0x7C, 0x10), new(0xCA, 0x50, 0x10)];

    public bool ThemeChanged { get; init; }

    public bool FollowWindows { get; init; } = true;

    public string Light { get; init; } = ThemeCatalog.SalandualLightId;

    public string Dark { get; init; } = ThemeCatalog.SalandualDarkId;

    public string One { get; init; } = ThemeCatalog.SalandualLightId;

    public AccentChoice Accent { get; init; }

    public Rgb Colour { get; init; } = Colours[0];

    public KeyMapName Keys { get; init; }

    public bool TcQuickSearch { get; init; } = true;

    public bool TcRightButton { get; init; } = true;

    public BarVisibility Bars { get; init; } = new();

    public int RowHeight { get; init; } = 24;

    public SizeUnits Units { get; init; }

    public string DateFormat { get; init; } = PanelSettings.DateFormatIso;

    public TimeFormat Time { get; init; }

    public StartFolders Start { get; init; }

    public string StartLeft { get; init; } = "";

    public string StartRight { get; init; } = "";

    public bool StartWithWindows { get; init; }

    public IEnumerable<string> ThemeIds => FollowWindows ? [Light, Dark] : [One];

    public static FirstStepsChoices From(AppSettings s, bool startsWithWindows, bool windowsDark)
    {
        var schedule = s.Appearance.Schedule;
        var follow = schedule.Mode != ScheduleMode.None;
        var onScreen = ThemeCatalog.Find(s.Appearance, schedule.ActiveId(windowsDark, (DateTime.Now.Hour * 60) + DateTime.Now.Minute));
        var accent = onScreen.Accent switch
        {
            ThemeAccent.Windows => AccentChoice.Windows,
            ThemeAccent.Custom when ColorMath.TryParseHex(onScreen.CustomAccent, out _) => AccentChoice.Colour,
            _ => AccentChoice.Theme,
        };
        return new FirstStepsChoices
        {
            FollowWindows = follow,
            Light = schedule.Light,
            Dark = schedule.Dark,
            One = follow ? onScreen.Id : schedule.Active,
            Accent = accent,
            Colour = accent == AccentChoice.Colour && ColorMath.TryParseHex(onScreen.CustomAccent, out var c) ? c : Colours[0],
            Keys = s.Keyboard.KeyMap,
            Bars = s.Layout.Bars,
            RowHeight = s.Layout.RowHeight,
            Units = s.Panels.SizeUnits,
            DateFormat = s.Panels.DateFormat,
            Time = s.Panels.TimeFormat,
            Start = s.General.StartFolders,
            StartLeft = s.General.StartLeft,
            StartRight = s.General.StartRight,
            StartWithWindows = startsWithWindows,
        };
    }

    public AppSettings ApplyTo(AppSettings s, DateTime now)
    {
        var before = s.Keyboard.KeyMap;
        s = s with
        {
            Keyboard = s.Keyboard with { KeyMap = Keys },
            Layout = s.Layout with { Bars = Bars, RowHeight = RowHeight },
            Panels = s.Panels with { SizeUnits = Units, DateFormat = DateFormat, TimeFormat = Time },
            General = s.General with { StartFolders = Start, StartLeft = StartLeft.Trim(), StartRight = StartRight.Trim() },
        };

        if (Keys == KeyMapName.TotalCommander || before == KeyMapName.TotalCommander)
        {
            var (quick, right) = SettingsTransferDialog.GoesWith(Keys, s);
            var takeQuick = Keys == KeyMapName.Salamander || TcQuickSearch;
            var takeRight = Keys == KeyMapName.Salamander || TcRightButton;
            s = s with
            {
                Panels = takeQuick ? s.Panels with { QuickSearch = quick } : s.Panels,
                Mouse = takeRight ? s.Mouse with { RightClickUnselected = right } : s.Mouse,
            };
        }

        return ThemeChanged ? s with { Appearance = AppearanceWith(s.Appearance, now) } : s;
    }

    public AppearanceSettings AppearanceWith(AppearanceSettings a, DateTime now)
    {
        string WithAccent(string id)
        {
            var theme = ThemeCatalog.Find(a, id);
            var mode = Accent switch { AccentChoice.Windows => ThemeAccent.Windows, AccentChoice.Colour => ThemeAccent.Custom, _ => ThemeAccent.Theme };
            var custom = Accent == AccentChoice.Colour ? ColorMath.ToHex(Colour) : theme.CustomAccent;
            if (!theme.ReadOnly)
            {
                a = ThemeEditing.Change(a, theme.Id, t => t with { Accent = mode, CustomAccent = custom });
                return theme.Id;
            }

            if (mode == ThemeAccent.Theme)
            {
                return theme.Id;
            }

            var name = Loc.Format(AppStrings.Wizard_AccentThemeName, theme.Name);
            if (a.Themes.FirstOrDefault(t => !t.ReadOnly && t.Name == name && t.CopiedFrom == theme.Name) is { } copy)
            {
                a = ThemeEditing.Change(a, copy.Id, t => t with { Accent = mode, CustomAccent = custom });
                return copy.Id;
            }

            var made = ThemeEditing.Duplicate(theme, a, now, name) with { Accent = mode, CustomAccent = custom };
            a = ThemeEditing.Add(a, made);
            return made.Id;
        }

        var schedule = FollowWindows
            ? a.Schedule with { Mode = ScheduleMode.Windows, Light = WithAccent(Light), Dark = WithAccent(Dark) }
            : a.Schedule with { Mode = ScheduleMode.None, Active = WithAccent(One) };
        return a with { Schedule = schedule };
    }
}
