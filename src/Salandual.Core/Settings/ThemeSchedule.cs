// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ThemeSchedule.cs

namespace Salandual.Core.Settings;

public enum ScheduleMode
{
    None,

    Windows,

    Custom,
}

public sealed record ThemeChange
{
    public int Minute { get; set; }

    public string Theme { get; set; } = ThemeCatalog.SalandualLightId;
}

public sealed record ThemeSchedule
{
    public ScheduleMode Mode { get; set; } = ScheduleMode.Windows;

    public string Active { get; set; } = ThemeCatalog.SalandualLightId;

    public string Light { get; set; } = ThemeCatalog.SalandualLightId;

    public string Dark { get; set; } = ThemeCatalog.SalandualDarkId;

    public List<ThemeChange> Changes { get; set; } =
    [
        new() { Minute = 7 * 60, Theme = ThemeCatalog.SalandualLightId },
        new() { Minute = (19 * 60) + 30, Theme = ThemeCatalog.SalandualDarkId },
    ];

    public string ActiveId(bool windowsDark, int minuteOfDay) => Mode switch
    {
        ScheduleMode.None => Active,
        ScheduleMode.Windows => windowsDark ? Dark : Light,
        _ => At(minuteOfDay),
    };

    public string At(int minuteOfDay)
    {
        var sorted = Sorted();
        var theme = sorted[^1].Theme;
        foreach (var change in sorted)
        {
            if (change.Minute <= minuteOfDay)
            {
                theme = change.Theme;
            }
        }

        return theme;
    }

    public int? MinutesToNextChange(int minuteOfDay)
    {
        if (Mode != ScheduleMode.Custom)
        {
            return null;
        }

        var next = Sorted().Select(c => c.Minute).FirstOrDefault(m => m > minuteOfDay, Sorted()[0].Minute + 1440);
        return next - minuteOfDay;
    }

    public IReadOnlyList<ThemeChange> Sorted() => [.. Changes.OrderBy(c => c.Minute)];

    public bool Uses(string themeId) => Active == themeId || Light == themeId || Dark == themeId || Changes.Any(c => c.Theme == themeId);

    internal ThemeSchedule Normalize(AppearanceSettings appearance)
    {
        string Valid(string? id, bool dark) => ThemeCatalog.Find(appearance, id, dark).Id;
        var changes = (Changes ?? [])
            .Where(c => c is not null)
            .Select(c => c with { Minute = Math.Clamp(c.Minute, 0, 1439), Theme = Valid(c.Theme, dark: false) })
            .GroupBy(c => c.Minute).Select(g => g.Last())
            .OrderBy(c => c.Minute)
            .ToList();
        return this with
        {
            Mode = Enum.IsDefined(Mode) ? Mode : ScheduleMode.Windows,
            Active = Valid(Active, dark: false),
            Light = Valid(Light, dark: false),
            Dark = Valid(Dark, dark: true),
            Changes = changes.Count > 0 ? changes : new ThemeSchedule().Changes,
        };
    }
}
