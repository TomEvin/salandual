// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Appearance.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Salandual.App.Theme;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

using Salandual.App.Resources;
using Salandual.Core.Resources;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    private const string Appearance = "appearance";

    private static readonly string[] _monoFirst = ["Cascadia Mono", "Cascadia Code", "Consolas", "Courier New", "Lucida Console"];

    private static IReadOnlyList<string>? _fontFamilies;

    private App? AppInstance => Application.Current as App;

    private void AddAppearance()
    {
        Add(Appearance, AppStrings.OptSec_Preview, null, null, () => new PreviewPanel(Settings), wide: true, keywords: AppStrings.Opt_Preview_Kw);

        Add(Appearance, AppStrings.OptSec_Fonts, AppStrings.Opt_PanelFont, AppStrings.Opt_PanelFont_Desc, () => FontPicker(Current.Appearance.PanelFont, mono: false,
            f => UpdateAppearance(a => a with { PanelFont = f })), keywords: AppStrings.Opt_PanelFont_Kw);
        Add(Appearance, AppStrings.OptSec_Fonts, AppStrings.Opt_MonoFont, AppStrings.Opt_MonoFont_Desc, MonoFontPicker, wide: true, keywords: AppStrings.Opt_MonoFont_Kw);
    }

    private static string Percent(double value) => string.Create(CultureInfo.CurrentCulture, $"{Math.Round(value * 100)} %");

    private static IReadOnlyList<string> FontFamilies => _fontFamilies ??=
        [.. FontManager.Current.SystemFonts.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)];

    private static Control FontPicker(FontSetting current, bool mono, Action<FontSetting> changed)
    {
        var families = new List<(string? Value, string Label)> { (null, mono ? AppStrings.Opt_Font_Default : AppStrings.Opt_Font_SystemDefault) };
        var installed = FontFamilies;
        if (mono)
        {
            families.AddRange(_monoFirst.Where(m => installed.Contains(m, StringComparer.OrdinalIgnoreCase)).Select(m => ((string?)m, m)));
        }

        families.AddRange(installed.Where(f => !mono || !_monoFirst.Contains(f, StringComparer.OrdinalIgnoreCase)).Select(f => ((string?)f, f)));
        if (current.Family is { } own && !families.Any(f => string.Equals(f.Value, own, StringComparison.OrdinalIgnoreCase)))
        {
            families.Add((own, Loc.Format(AppStrings.Opt_Font_NotInstalled, own)));
        }

        var selectedFamily = families.FirstOrDefault(f => string.Equals(f.Value, current.Family, StringComparison.OrdinalIgnoreCase)).Value;
        var family = Combo(families, selectedFamily, f => changed(current = current with { Family = f }), 240);
        var sizes = Enumerable.Range(9, 12).Select(s => ((double)s, s.ToString(CultureInfo.CurrentCulture))).ToList();
        if (!sizes.Any(s => s.Item1 == current.Size))
        {
            sizes.Add((current.Size, current.Size.ToString(CultureInfo.CurrentCulture)));
        }

        var size = Combo(sizes, current.Size, s => changed(current = current with { Size = s }), 72);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(family);
        panel.Children.Add(size);
        return panel;
    }

    private Control MonoFontPicker()
    {
        var sample = new TextBlock { Text = AppStrings.Sample_MonoFont, Margin = new Thickness(0, 4, 0, 0) }
            .Res(TextBlock.FontFamilyProperty, "Font.Mono").Res(TextBlock.FontSizeProperty, "Font.MonoSize").Res(TextBlock.ForegroundProperty, "Brush.Text");
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(FontPicker(Current.Appearance.MonoFont, mono: true, f => UpdateAppearance(a => a with { MonoFont = f })));
        panel.Children.Add(sample);
        return panel;
    }
}
