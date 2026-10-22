// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ThemeEditing.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Settings;

public static class ThemeEditing
{
    public static readonly IReadOnlyList<(string Token, string Label)> Main =
    [
        (ColorTokens.WindowBackground, Strings.ThemeMain_Window),
        (ColorTokens.PanelBackground, Strings.ThemeMain_Panels),
        (ColorTokens.Text, Strings.ThemeMain_Text),
        (ColorTokens.Accent, Strings.ThemeMain_Accent),
        (ColorTokens.SelectionBackground, Strings.ThemeMain_Selection),
    ];

    public static ColorTheme Duplicate(ColorTheme source, AppearanceSettings appearance, DateTime now, string? name = null)
    {
        var colors = new Dictionary<string, string>(source.Colors, StringComparer.Ordinal);
        return new ColorTheme
        {
            Id = NewId(appearance),
            Name = UniqueName(name ?? Loc.Format(Strings.Theme_CopyName, source.Name), appearance),
            Dark = source.Dark,
            Origin = ThemeOrigin.Mine,
            Colors = colors,
            Original = new Dictionary<string, string>(colors, StringComparer.Ordinal),
            Accent = source.Accent,
            CustomAccent = source.CustomAccent,
            CodeBackground = source.CodeBackground,
            Background = source.Background,
            CopiedFrom = source.Name,
            CopiedOn = now,
        };
    }

    public static ColorTheme New(AppearanceSettings appearance, DateTime now)
        => Duplicate(ThemeCatalog.BuiltIn[0], appearance, now, Strings.Theme_NewName);

    public static AppearanceSettings Add(AppearanceSettings appearance, ColorTheme theme)
        => appearance with { Themes = [.. appearance.Themes, theme] };

    public static AppearanceSettings Change(AppearanceSettings appearance, string id, Func<ColorTheme, ColorTheme> change)
        => appearance with { Themes = [.. appearance.Themes.Select(t => t.Id == id && !t.ReadOnly ? change(t) : t)] };

    public static AppearanceSettings Remove(AppearanceSettings appearance, string id)
        => appearance with { Themes = [.. appearance.Themes.Where(t => t.Id != id || t.ReadOnly)] };

    public static ColorTheme Rename(ColorTheme theme, string name, AppearanceSettings appearance)
    {
        name = name.Trim();
        return name.Length == 0 || name == theme.Name ? theme : theme with { Name = UniqueName(name, appearance, theme.Id) };
    }

    public static ColorTheme SetColor(ColorTheme theme, string token, Rgb color)
        => theme with { Colors = new(theme.Colors, StringComparer.Ordinal) { [token] = ColorMath.ToHex(color) } };

    public static ColorTheme ResetColor(ColorTheme theme, string token)
    {
        var colors = new Dictionary<string, string>(theme.Colors, StringComparer.Ordinal);
        if (theme.Original.TryGetValue(token, out var original))
        {
            colors[token] = original;
        }
        else
        {
            colors.Remove(token);
        }

        return theme with { Colors = colors };
    }

    public static bool IsChanged(ColorTheme theme, string token)
        => theme.Colors.GetValueOrDefault(token) != theme.Original.GetValueOrDefault(token);

    public static ColorTheme FillFromMain(ColorTheme theme, IReadOnlyDictionary<string, Rgb> resolved)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (token, _) in Main)
        {
            colors[token] = ColorMath.ToHex(resolved[token]);
        }

        return theme with { Colors = colors };
    }

    public static string UniqueName(string name, AppearanceSettings appearance, string? exceptId = null)
    {
        var taken = ThemeCatalog.All(appearance).Where(t => t.Id != exceptId).Select(t => t.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var candidate = name;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{name} {n}";
        }

        return candidate;
    }

    internal static string NewId(AppearanceSettings appearance)
    {
        var taken = ThemeCatalog.All(appearance).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        string id;
        do
        {
            id = "mine-" + Guid.NewGuid().ToString("N")[..8];
        }
        while (taken.Contains(id));
        return id;
    }
}
