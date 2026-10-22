// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ThemeAndInputTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Salandual.App.Input;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class ThemeAndInputTests
{
    private static App CurrentApp => (App)Application.Current!;

    private static Color BrushColor(ThemeVariant variant, string key)
    {
        Assert.True(Application.Current!.Resources.ThemeDictionaries.TryGetValue(variant, out var provider));
        Assert.True(((ResourceDictionary)provider).TryGetValue(key, out var value), key);
        return ((ISolidColorBrush)value!).Color;
    }

    [AvaloniaFact]
    public void Apply_SetsThemeVariant_AndEveryColorToken()
    {
        CurrentApp.Theme.Apply(AppearanceSettings.Fixed(ThemeCatalog.SalandualDarkId));

        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        foreach (var (token, _) in ColorTokens.All)
        {
            _ = BrushColor(ThemeVariant.Light, "Brush." + token);
            _ = BrushColor(ThemeVariant.Dark, "Brush." + token);
        }

        Assert.Equal(Color.Parse("#272727"), BrushColor(ThemeVariant.Dark, "Brush.PanelBackground"));
        Assert.Equal(Color.Parse("#272727"), BrushColor(ThemeVariant.Light, "Brush.PanelBackground"));
    }

    [AvaloniaFact]
    public void AThemesCustomAccent_ReachesOurBrushes_AndFluentControls()
    {
        CurrentApp.Theme.Apply(new AppearanceSettings
        {
            Themes = [new ColorTheme { Id = "mine", Name = "Mine", Accent = ThemeAccent.Custom, CustomAccent = "#C239B3" }],
            Schedule = new ThemeSchedule { Mode = ScheduleMode.None, Active = "mine" },
        });

        Assert.Equal(Color.Parse("#C239B3"), BrushColor(ThemeVariant.Light, "Brush.Accent"));
        Assert.Equal(Color.Parse("#C239B3"), BrushColor(ThemeVariant.Light, "Brush.FocusOutline"));
        Assert.Equal(Color.Parse("#C239B3"), (Color)Application.Current!.Resources["SystemAccentColor"]!);
    }

    [AvaloniaFact]
    public void AShippedTheme_ColoursEverything_AndBackToSalandual()
    {
        var fluent = Application.Current!.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>().Single();
        CurrentApp.Theme.Apply(AppearanceSettings.Fixed("dracula"));
        Assert.Equal(ThemeVariant.Dark, Application.Current.RequestedThemeVariant);
        Assert.Equal(Color.Parse("#F8F8F2"), BrushColor(ThemeVariant.Dark, "Brush.Text"));
        Assert.Equal(Color.Parse("#282A36"), fluent.Palettes[ThemeVariant.Dark].AltHigh);

        CurrentApp.Theme.Apply(AppearanceSettings.Fixed(ThemeCatalog.SalandualLightId));
        Assert.Equal(Color.Parse("#1B1B1B"), BrushColor(ThemeVariant.Light, "Brush.Text"));
        Assert.False(fluent.Palettes.ContainsKey(ThemeVariant.Light));

        Assert.True(Application.Current.TryGetResource("SystemBaseLowColor", ThemeVariant.Light, out var baseLow));
        Assert.Equal(Color.Parse("#33000000"), (Color)baseLow!);
        Assert.True(Application.Current.TryGetResource("SystemAltHighColor", ThemeVariant.Light, out var altHigh));
        Assert.Equal(Colors.White, (Color)altHigh!);
    }

    [AvaloniaFact]
    public void Fonts_AreResources()
    {
        CurrentApp.Theme.Apply(new AppearanceSettings { PanelFont = new FontSetting { Family = "Calibri", Size = 15 } });

        Assert.Equal("Calibri", ((FontFamily)Application.Current!.Resources["Font.Panel"]!).Name);
        Assert.Equal(15d, Application.Current.Resources["Font.PanelSize"]);
    }

    [Theory]
    [InlineData(Key.F5, KeyModifiers.None, "F5")]
    [InlineData(Key.Add, KeyModifiers.Shift, "Shift+Num+")]
    [InlineData(Key.Multiply, KeyModifiers.None, "Num*")]
    [InlineData(Key.D1, KeyModifiers.Shift, "Shift+1")]
    [InlineData(Key.OemPlus, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+=")]
    [InlineData(Key.Back, KeyModifiers.None, "Backspace")]
    [InlineData(Key.PageDown, KeyModifiers.Control, "Ctrl+PageDown")]
    [InlineData(Key.F4, KeyModifiers.Alt, "Alt+F4")]
    public void KeyMapping_ProducesSalamanderChords(Key key, KeyModifiers mods, string expected)
    {
        Assert.True(KeyMapping.TryGetChord(key, mods, out var chord));
        Assert.Equal(expected, chord.ToString());
    }

    [Fact]
    public void KeyMapping_IgnoresModifiersAlone() => Assert.False(KeyMapping.TryGetChord(Key.LeftShift, KeyModifiers.Shift, out _));
}
