// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/LookChanges.cs

using System.Globalization;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Options;

internal sealed record LookChanges(string Title, string Detail, IReadOnlySet<string> Pages, ColorTheme Before, ColorTheme After)
{
    public bool ThemeSwitched => Before.Id != After.Id;

    public static LookChanges Describe(AppearanceSettings kept, AppearanceSettings now, Func<ThemeSchedule, string> activeId)
    {
        var before = ThemeCatalog.Find(kept, activeId(kept.Schedule));
        var after = ThemeCatalog.Find(now, activeId(now.Schedule));
        var parts = new List<(string Title, string Short, string Detail)>();
        var pages = new HashSet<string>(StringComparer.Ordinal);

        if (before.Id != after.Id)
        {
            parts.Add((Loc.Format(AppStrings.Look_Instead, after.Name, before.Name), after.Name, Loc.Format(AppStrings.Look_Instead_Desc, before.Name)));
        }
        else if (!Same(kept.Schedule, now.Schedule))
        {
            parts.Add((AppStrings.Look_Schedule, AppStrings.Look_Schedule_Short, AppStrings.Look_Schedule_Desc));
        }

        if (before.Id != after.Id || !Same(kept.Schedule, now.Schedule))
        {
            pages.Add("themes");
        }

        foreach (var theme in now.Themes)
        {
            if (kept.Themes.FirstOrDefault(t => t.Id == theme.Id) is { } old && ThemePart(old, theme, theme.Id == after.Id) is { } part)
            {
                parts.Add(part);
                pages.Add("themes");
            }
        }

        if (kept.PanelFont != now.PanelFont)
        {
            parts.Add((Loc.Format(AppStrings.Look_PanelFont, Font(now.PanelFont), Was(kept.PanelFont, now.PanelFont)), AppStrings.Look_PanelFont_Short, AppStrings.Look_PanelFont_Desc));
            pages.Add("appearance");
        }

        if (kept.MonoFont != now.MonoFont)
        {
            parts.Add((Loc.Format(AppStrings.Look_MonoFont, Font(now.MonoFont), Was(kept.MonoFont, now.MonoFont)), AppStrings.Look_MonoFont_Short, AppStrings.Look_MonoFont_Desc));
            pages.Add("appearance");
        }

        return parts switch
        {
            [] => new(AppStrings.Look_Schedule, AppStrings.Look_Schedule_Desc, pages, before, after),
            [var one] => new(one.Title, one.Detail, pages, before, after),
            _ => new(Capitalise(List(parts.Select(p => p.Short))), AppStrings.Look_Many_Desc, pages, before, after),
        };
    }

    private static (string, string, string)? ThemePart(ColorTheme old, ColorTheme now, bool inUse)
    {
        var colours = ColorTokens.All.Where(t => Color(old, t.Token) != Color(now, t.Token)).Select(t => t.Label).ToList();
        var background = old.Background != now.Background;
        var settings = old.Dark != now.Dark || old.Accent != now.Accent || old.CustomAccent != now.CustomAccent || old.CodeBackground != now.CodeBackground;
        var kinds = (colours.Count > 0 ? 1 : 0) + (background ? 1 : 0) + (settings ? 1 : 0);
        if (kinds == 0)
        {
            return null;
        }

        var title = kinds > 1 ? Loc.Format(AppStrings.Look_Theme, now.Name)
            : colours.Count > 0 ? Loc.Plural(colours.Count, AppStrings.Look_Colours_One, AppStrings.Look_Colours_Other, now.Name)
            : background ? Loc.Format(AppStrings.Look_Background, now.Name)
            : Loc.Format(AppStrings.Look_ThemeSettings, now.Name);

        var what = colours.Take(colours.Count > 3 ? 2 : 3).ToList();
        if (colours.Count > 3)
        {
            what.Add(Loc.Plural(colours.Count - 2, AppStrings.Look_More_One, AppStrings.Look_More_Other));
        }

        if (background)
        {
            what.Add(AppStrings.Look_BackgroundPart);
        }

        if (settings)
        {
            what.Add(AppStrings.Look_SettingsPart);
        }

        var detail = Loc.Format(inUse ? AppStrings.Look_InUse_Desc : AppStrings.Look_NotInUse_Desc, Capitalise(List(what)), now.Name);
        return (title, Loc.Format(AppStrings.Look_Theme_Short, now.Name), detail);
    }

    private static string? Color(ColorTheme theme, string token) => theme.Colors.GetValueOrDefault(token);

    private static bool Same(ThemeSchedule a, ThemeSchedule b)
        => a.Mode == b.Mode && a.Active == b.Active && a.Light == b.Light && a.Dark == b.Dark
           && a.Changes.Count == b.Changes.Count && a.Sorted().SequenceEqual(b.Sorted());

    private static string Font(FontSetting font)
        => (font.Family ?? AppStrings.Look_SystemFont) + " " + font.Size.ToString(CultureInfo.CurrentCulture);

    private static string Was(FontSetting old, FontSetting now)
        => string.Equals(old.Family, now.Family, StringComparison.Ordinal) ? old.Size.ToString(CultureInfo.CurrentCulture) : Font(old);

    private static string List(IEnumerable<string> items)
    {
        var all = items.ToList();
        return all.Count < 2 ? string.Concat(all) : Loc.Format(AppStrings.Look_And, string.Join(", ", all.Take(all.Count - 1)), all[^1]);
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];
}
