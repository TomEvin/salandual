// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Theme/ThemeService.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Salandual.Core.Settings;

namespace Salandual.App.Theme;

public sealed class ThemeService
{
    private readonly Application _app;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _scheduleTimer = new();

    public ThemeService(Application app, SettingsService settings)
    {
        _app = app;
        _settings = settings;
        _settings.Changed += (_, e) =>
        {
            if (!e.Old.Appearance.IsEquivalentTo(e.New.Appearance))
            {
                Dispatcher.UIThread.Post(() => Apply(e.New.Appearance));
            }
        };
        _scheduleTimer.Tick += (_, _) => Apply(_settings.Current.Appearance);
    }

    public event EventHandler? Applied;

    public ColorTheme ActiveTheme { get; private set; } = ThemeCatalog.BuiltIn[0];

    public void Start()
    {
        if (_app.PlatformSettings is { } platform)
        {
            platform.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(() => Apply(_settings.Current.Appearance));
        }

        Apply(_settings.Current.Appearance);
    }

    public bool WindowsDark => _app.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;

    public Rgb? WindowsAccent => _app.PlatformSettings?.GetColorValues() is { } v ? new Rgb(v.AccentColor1.R, v.AccentColor1.G, v.AccentColor1.B) : null;

    public IReadOnlyDictionary<string, Rgb> Resolve(ColorTheme theme) => ColorTokens.Resolve(theme, WindowsAccent);

    public void Apply(AppearanceSettings appearance)
    {
        var now = DateTime.Now;
        var minute = (now.Hour * 60) + now.Minute;
        var theme = ThemeCatalog.Find(appearance, appearance.Schedule.ActiveId(WindowsDark, minute));
        ActiveTheme = theme;
        _app.RequestedThemeVariant = theme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var colors = Resolve(theme);
        ApplyVariant(ThemeVariant.Light, theme, colors);
        ApplyVariant(ThemeVariant.Dark, theme, colors);
        ApplyFluent(theme, colors);
        ApplyFonts(appearance);
        ScheduleNext(appearance.Schedule, minute, now.Second);
        Applied?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleNext(ThemeSchedule schedule, int minute, int second)
    {
        _scheduleTimer.Stop();
        if (schedule.MinutesToNextChange(minute) is { } minutes)
        {
            _scheduleTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, (minutes * 60) - second + 2));
            _scheduleTimer.Start();
        }
    }

    private void ApplyVariant(ThemeVariant variant, ColorTheme theme, IReadOnlyDictionary<string, Rgb> colors)
    {
        var dict = VariantDictionary(variant);
        foreach (var (token, color) in colors)
        {
            dict["Brush." + token] = Brush(color);
        }

        var own = ColorTokens.OwnSurface(theme, colors);
        dict["Brush.CodeSurface"] = own is null ? dict["Brush.PanelBackground"] : Brush(own.Background);
        dict["Brush.CodeInk"] = own is null ? dict["Brush.Text"] : Brush(own.Text);
        dict["Brush.CodeCurrentLine"] = own is null ? dict["Brush.Hover"] : Brush(own.CurrentLine);
        dict["Brush.CodeLineNumbers"] = own is null ? dict["Brush.ViewerLineNumbers"] : Brush(own.LineNumbers);
        dict["Brush.CodeSelection"] = own is null ? dict["Brush.TextSelection"] : Brush(own.Selection);
        ApplyControls(dict, theme, colors);
    }

    private static void ApplyControls(ResourceDictionary dict, ColorTheme theme, IReadOnlyDictionary<string, Rgb> colors)
    {
        var k = ControlColors.From(colors);
        var text = theme.Origin == ThemeOrigin.BuiltIn ? Brush(theme.Dark ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0)) : Brush(colors[ColorTokens.Text]);
        var (fill, hover, pressed) = (Brush(k.Fill), Brush(k.FillHover), Brush(k.FillPressed));
        var (edge, edgeHover) = (Brush(k.Edge), Brush(k.EdgeHover));
        var (disabledFill, disabledEdge, disabledText) = (Brush(k.DisabledFill), Brush(k.DisabledEdge), Brush(k.DisabledText));

        foreach (var button in (ReadOnlySpan<string>)["Button", "RepeatButton", "ToggleButton"])
        {
            dict[button + "Background"] = fill;
            dict[button + "BackgroundPointerOver"] = hover;
            dict[button + "BackgroundPressed"] = pressed;
            dict[button + "BackgroundDisabled"] = disabledFill;
            dict[button + "BorderBrush"] = edge;
            dict[button + "BorderBrushPointerOver"] = edgeHover;
            dict[button + "BorderBrushPressed"] = edgeHover;
            dict[button + "BorderBrushDisabled"] = disabledEdge;
            dict[button + "Foreground"] = text;
            dict[button + "ForegroundPointerOver"] = text;
            dict[button + "ForegroundPressed"] = text;
            dict[button + "ForegroundDisabled"] = disabledText;
        }

        foreach (var field in (ReadOnlySpan<string>)["TextControl", "ComboBox", "TimePickerButton"])
        {
            dict[field + "BorderBrush"] = edge;
            dict[field + "BorderBrushPointerOver"] = edgeHover;
            dict[field + "BorderBrushDisabled"] = disabledEdge;
            dict[field + "BackgroundDisabled"] = disabledFill;
            dict[field + "ForegroundDisabled"] = disabledText;
        }

        dict["TextControlPlaceholderForegroundDisabled"] = disabledText;
        dict["ComboBoxDropDownGlyphForegroundDisabled"] = disabledText;
        dict["CheckBoxCheckBackgroundStrokeUnchecked"] = edge;
        dict["CheckBoxCheckBackgroundStrokeUncheckedPointerOver"] = edgeHover;
        dict["CheckBoxCheckBackgroundStrokeUncheckedDisabled"] = disabledEdge;
        dict["RadioButtonOuterEllipseStroke"] = edge;
        dict["RadioButtonOuterEllipseStrokePointerOver"] = edgeHover;
        dict["RadioButtonOuterEllipseStrokeDisabled"] = disabledEdge;
        dict["ToggleSwitchStrokeOff"] = edge;
        dict["ToggleSwitchStrokeOffPointerOver"] = edgeHover;
        dict["ToggleSwitchStrokeOffDisabled"] = disabledEdge;
        foreach (var key in (ReadOnlySpan<string>)["CheckBoxForegroundUncheckedDisabled", "CheckBoxForegroundCheckedDisabled", "CheckBoxForegroundIndeterminateDisabled", "RadioButtonForegroundDisabled", "ToggleSwitchHeaderForegroundDisabled", "TabItemHeaderForegroundDisabled"])
        {
            dict[key] = disabledText;
        }

        dict["ScrollBarThumbBackgroundColor"] = ToColor(k.ScrollThumb);
        ApplyAccentControls(dict, k);
    }

    private static void ApplyAccentControls(ResourceDictionary dict, ControlColors k)
    {
        var a = k.Accent;
        var (label, edge) = (Brush(a.Label), Brush(a.Edge));
        var states = new (string State, ImmutableSolidColorBrush Fill, ImmutableSolidColorBrush Mark)[]
        {
            ("", Brush(a.Fill), Brush(a.Mark)),
            ("PointerOver", Brush(a.FillHover), Brush(a.MarkHover)),
            ("Pressed", Brush(a.FillPressed), Brush(a.MarkPressed)),
        };
        foreach (var (state, fill, mark) in states)
        {
            dict["AccentButtonBackground" + state] = fill;
            dict["AccentButtonForeground" + state] = label;
            dict["CheckBoxCheckBackgroundFillChecked" + state] = fill;
            dict["CheckBoxCheckBackgroundStrokeChecked" + state] = edge;
            dict["CheckBoxCheckGlyphForegroundChecked" + state] = label;
            dict["CheckBoxCheckBackgroundFillIndeterminate" + state] = fill;
            dict["CheckBoxCheckBackgroundStrokeIndeterminate" + state] = edge;
            dict["CheckBoxCheckGlyphForegroundIndeterminate" + state] = label;
            dict["ToggleSwitchFillOn" + state] = fill;
            dict["ToggleSwitchStrokeOn" + state] = edge;
            dict["ToggleSwitchKnobFillOn" + state] = label;
            dict["ToggleButtonBackgroundChecked" + state] = fill;
            dict["ToggleButtonForegroundChecked" + state] = label;
            dict["ComboBoxItemBackgroundSelected" + state] = fill;
            dict["ComboBoxItemForegroundSelected" + state] = label;
            dict["SliderThumbBackground" + state] = mark;
            dict["SliderTrackValueFill" + state] = mark;
        }

        dict["AccentButtonBackgroundDisabled"] = Brush(k.DisabledFill);
        dict["AccentButtonBorderBrushDisabled"] = Brush(k.DisabledEdge);
        dict["AccentButtonForegroundDisabled"] = Brush(k.DisabledText);
    }

    private void ApplyFluent(ColorTheme theme, IReadOnlyDictionary<string, Rgb> colors)
    {
        var accent = colors[ColorTokens.Accent];
        var light = theme.Dark ? ColorMath.Mix(accent, new Rgb(0, 0, 0), 0.3) : accent;
        var dark = theme.Dark ? accent : ColorMath.Lighten(accent, 0.35);
        var r = _app.Resources;
        r["SystemAccentColor"] = ToColor(light);
        r["SystemAccentColorDark1"] = ToColor(ColorMath.Mix(light, new Rgb(0, 0, 0), 0.15));
        r["SystemAccentColorDark2"] = ToColor(ColorMath.Mix(light, new Rgb(0, 0, 0), 0.30));
        r["SystemAccentColorDark3"] = ToColor(ColorMath.Mix(light, new Rgb(0, 0, 0), 0.45));
        r["SystemAccentColorLight1"] = ToColor(dark);
        r["SystemAccentColorLight2"] = ToColor(ColorMath.Lighten(dark, 0.15));
        r["SystemAccentColorLight3"] = ToColor(ColorMath.Lighten(dark, 0.30));

        if (_app.Styles.OfType<FluentTheme>().FirstOrDefault() is not { } fluent)
        {
            return;
        }

        foreach (var variant in (ReadOnlySpan<ThemeVariant>)[ThemeVariant.Light, ThemeVariant.Dark])
        {
            if (theme.Origin == ThemeOrigin.BuiltIn)
            {
                fluent.Palettes.Remove(variant);
            }
            else
            {
                fluent.Palettes[variant] = Palette(theme, colors, accent);
            }
        }
    }

    private static ColorPaletteResources Palette(ColorTheme theme, IReadOnlyDictionary<string, Rgb> c, Rgb accent)
    {
        var surface = c[ColorTokens.PanelBackground];
        var text = c[ColorTokens.Text];
        Color A(Rgb rgb, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), rgb.R, rgb.G, rgb.B);
        return new ColorPaletteResources
        {
            Accent = ToColor(accent),
            RegionColor = ToColor(c[ColorTokens.WindowBackground]),
            AltHigh = ToColor(surface),
            AltMediumHigh = A(surface, 0.8),
            AltMedium = A(surface, 0.6),
            AltMediumLow = A(surface, 0.4),
            AltLow = A(surface, 0.2),
            BaseHigh = ToColor(text),
            BaseMediumHigh = A(text, 0.8),
            BaseMedium = A(text, 0.6),
            BaseMediumLow = A(text, 0.4),
            BaseLow = A(text, 0.2),
            ChromeAltLow = ToColor(text),
            ChromeBlackHigh = Colors.Black,
            ChromeBlackMedium = Color.FromArgb(theme.Dark ? (byte)0xCC : (byte)0x99, 0, 0, 0),
            ChromeBlackMediumLow = Color.FromArgb(0x66, 0, 0, 0),
            ChromeBlackLow = Color.FromArgb(0x33, 0, 0, 0),
            ChromeDisabledHigh = ToColor(c[ColorTokens.Border]),
            ChromeDisabledLow = ToColor(c[ColorTokens.TertiaryText]),
            ChromeGray = ToColor(c[ColorTokens.SecondaryText]),
            ChromeHigh = ToColor(c[ColorTokens.BorderStrong]),
            ChromeLow = ToColor(c[ColorTokens.WindowBackground]),
            ChromeMedium = ToColor(ColorMath.Mix(c[ColorTokens.WindowBackground], surface, 0.5)),
            ChromeMediumLow = ToColor(surface),
            ChromeWhite = Colors.White,
            ListLow = A(text, 0.1),
            ListMedium = A(text, 0.2),
            ErrorText = ToColor(c[ColorTokens.Danger]),
        };
    }

    private void ApplyFonts(AppearanceSettings appearance)
    {
        var r = _app.Resources;
        r["Font.Panel"] = new FontFamily(appearance.PanelFont.Family ?? "Segoe UI Variable Text, Segoe UI");
        r["Font.PanelSize"] = appearance.PanelFont.Size;
        r["Font.Mono"] = new FontFamily((appearance.MonoFont.Family ?? "Cascadia Mono") + ", Consolas");
        r["Font.MonoSize"] = appearance.MonoFont.Size;
    }

    private ResourceDictionary VariantDictionary(ThemeVariant variant)
    {
        if (_app.Resources.ThemeDictionaries.TryGetValue(variant, out var existing) && existing is ResourceDictionary dict)
        {
            return dict;
        }

        var created = new ResourceDictionary();
        _app.Resources.ThemeDictionaries[variant] = created;
        return created;
    }

    private static ImmutableSolidColorBrush Brush(Rgb c) => new(ToColor(c));

    private static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);
}
