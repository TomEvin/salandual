// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/AccentControlsTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class AccentControlsTests
{
    private static Rgb Over(Color c, Rgb under)
    {
        var a = c.A / 255.0;
        return new Rgb((byte)Math.Round((c.R * a) + (under.R * (1 - a))), (byte)Math.Round((c.G * a) + (under.G * (1 - a))), (byte)Math.Round((c.B * a) + (under.B * (1 - a))));
    }

    [AvaloniaFact]
    public void EveryTheme_FluentsAccentControls_ReadInEveryState()
    {
        var app = (App)Application.Current!;
        string[] states = ["", "PointerOver", "Pressed"];
        (string Fore, string Back)[] pairs =
        [
            ("AccentButtonForeground{0}", "AccentButtonBackground{0}"),
            ("CheckBoxCheckGlyphForegroundChecked{0}", "CheckBoxCheckBackgroundFillChecked{0}"),
            ("CheckBoxCheckGlyphForegroundIndeterminate{0}", "CheckBoxCheckBackgroundFillIndeterminate{0}"),
            ("ToggleSwitchKnobFillOn{0}", "ToggleSwitchFillOn{0}"),
            ("ToggleButtonForegroundChecked{0}", "ToggleButtonBackgroundChecked{0}"),
            ("ComboBoxItemForegroundSelected{0}", "ComboBoxItemBackgroundSelected{0}"),
        ];

        try
        {
            foreach (var t in ThemeCatalog.BuiltIn.Concat(ThemeCatalog.Shipped))
            {
                app.Theme.Apply(AppearanceSettings.Fixed(t.Id));
                var variant = app.ActualThemeVariant;
                var panel = app.Theme.Resolve(t)[ColorTokens.PanelBackground];

                Rgb? Get(string key)
                    => app.TryGetResource(key, variant, out var v) ? v switch { ISolidColorBrush b => Over(b.Color, panel), Color c => Over(c, panel), _ => null } : null;

                foreach (var (fore, back) in pairs)
                {
                    foreach (var state in states)
                    {
                        var (f, b) = (Get(string.Format(fore, state)), Get(string.Format(back, state)));
                        Assert.True(f is not null && b is not null, $"{t.Id}: {string.Format(fore, state)} / {string.Format(back, state)} is not a resource");
                        Assert.True(ColorMath.Contrast(f!.Value, b!.Value) >= 4.5, $"{t.Id}: {string.Format(fore, state)} is {ColorMath.Contrast(f.Value, b.Value):F2} : 1 on {ColorMath.ToHex(b.Value)}");
                    }
                }
            }
        }
        finally
        {
            app.Theme.Apply(AppearanceSettings.Fixed(ThemeCatalog.SalandualLightId));
        }
    }

    [AvaloniaFact]
    public void EveryTheme_APrimaryButton_AsDrawn_ReadsAtRest()
    {
        var app = (App)Application.Current!;
        var accent = new Button { Content = "OK" };
        accent.Classes.Add("accent");
        var window = new Window { Content = accent, Width = 200, Height = 100 };
        try
        {
            window.Show();
            foreach (var t in ThemeCatalog.BuiltIn.Concat(ThemeCatalog.Shipped))
            {
                app.Theme.Apply(AppearanceSettings.Fixed(t.Id));
                Dispatcher.UIThread.RunJobs();
                var presenter = accent.GetVisualDescendants().OfType<ContentPresenter>().First();
                var (fore, back) = ((ISolidColorBrush)presenter.Foreground!, (ISolidColorBrush)presenter.Background!);
                var ratio = ColorMath.Contrast(new Rgb(fore.Color.R, fore.Color.G, fore.Color.B), new Rgb(back.Color.R, back.Color.G, back.Color.B));
                Assert.True(ratio >= 4.5, $"{t.Id}: the OK button is {ratio:F2} : 1");
            }
        }
        finally
        {
            window.Close();
            app.Theme.Apply(AppearanceSettings.Fixed(ThemeCatalog.SalandualLightId));
        }
    }
}
