// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/ColorTokensTests.cs

using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class ColorTokensTests
{
    private static Rgb Hex(string hex) => ColorMath.ParseHex(hex);

    private static ColorTheme Theme(string id) => ThemeCatalog.Find(new AppearanceSettings(), id);

    [Theory]
    [InlineData("#0F6CBD", 0x0F, 0x6C, 0xBD)]
    [InlineData("0f6cbd", 0x0F, 0x6C, 0xBD)]
    [InlineData(" #FFFFFF ", 255, 255, 255)]
    public void ParseHex_Accepts(string text, byte r, byte g, byte b)
        => Assert.Equal(new Rgb(r, g, b), ColorMath.ParseHex(text));

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void TryParseHex_Rejects(string text) => Assert.False(ColorMath.TryParseHex(text, out _));

    [Fact]
    public void Mix_IsLinear() => Assert.Equal(new Rgb(128, 128, 128), ColorMath.Mix(new Rgb(0, 0, 0), new Rgb(255, 255, 255), 0.5));

    [Fact]
    public void SalandualLight_LooksAsBefore_SelectionAndFocusFromTheAccent()
    {
        var light = ColorTokens.Resolve(Theme(ThemeCatalog.SalandualLightId));
        var blue = Hex("#0F6CBD");
        Assert.Equal(blue, light[ColorTokens.Accent]);
        Assert.Equal(blue, light[ColorTokens.FocusOutline]);
        Assert.Equal(ColorMath.Mix(Hex("#FFFFFF"), blue, 0.16), light[ColorTokens.SelectionBackground]);
        Assert.Equal(Hex("#EFEFEF"), light[ColorTokens.Hover]);
        Assert.Equal(Hex("#FFF4CE"), light[ColorTokens.WarningBackground]);
        Assert.Equal(Hex("#8A8A8A"), light[ColorTokens.HexZero]);
        Assert.Equal(blue, light[ColorTokens.SyntaxKey]);

        var dark = ColorTokens.Resolve(Theme(ThemeCatalog.SalandualDarkId));
        Assert.Equal(Hex("#272727"), dark[ColorTokens.PanelBackground]);
        Assert.Equal(Hex("#479EF5"), dark[ColorTokens.Accent]);
        Assert.Equal(Hex("#433519"), dark[ColorTokens.WarningBackground]);
    }

    [Fact]
    public void Catalog_TenBuiltIn_FifteenShippedAndVsCodes_UniqueIds_EveryTokenResolved()
    {
        Assert.Equal(10, ThemeCatalog.BuiltIn.Count);
        Assert.Equal(
            ["far", "total-commander", "dracula", "nord", "nord-light", "gruvbox-dark", "gruvbox-light", "catppuccin-latte", "catppuccin-mocha", "tokyo-night", "one-light", "github-light", "github-dark", "amber-terminal", "green-terminal"],
            ThemeCatalog.Shipped.Select(t => t.Id).Where(id => !id.StartsWith("code-", StringComparison.Ordinal)));
        Assert.Equal(19, ThemeCatalog.Shipped.Count(t => t.Id.StartsWith("code-", StringComparison.Ordinal)));
        var all = ThemeCatalog.All(new AppearanceSettings()).ToList();
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        Assert.All(all, t =>
        {
            Assert.True(t.ReadOnly, t.Id);
            Assert.All(t.Colors, kv => Assert.True(ColorTokens.IsKnown(kv.Key) && ColorMath.TryParseHex(kv.Value, out _), t.Id + " " + kv.Key));
            var resolved = ColorTokens.Resolve(t);
            Assert.All(ColorTokens.All, token => Assert.True(resolved.ContainsKey(token.Token), t.Id + " " + token.Token));
        });
    }

    [Fact]
    public void EveryTheme_TextIsReadableOnItsPanels()
    {
        static double Contrast(Rgb a, Rgb b)
        {
            var (x, y) = (ColorMath.Luminance(a) + 0.05, ColorMath.Luminance(b) + 0.05);
            return Math.Max(x, y) / Math.Min(x, y);
        }

        foreach (var t in ThemeCatalog.BuiltIn.Concat(ThemeCatalog.Shipped))
        {
            var c = ColorTokens.Resolve(t);
            Assert.True(Contrast(c[ColorTokens.Text], c[ColorTokens.PanelBackground]) >= 4.5, $"{t.Id}: text");
            Assert.True(Contrast(c[ColorTokens.SelectedText], c[ColorTokens.SelectionBackground]) >= 3, $"{t.Id}: selected files");
            Assert.True(t.Dark == ColorMath.Luminance(c[ColorTokens.PanelBackground]) < 0.2, $"{t.Id}: light or dark");
            foreach (var token in (ReadOnlySpan<string>)[ColorTokens.SyntaxKey, ColorTokens.SyntaxString, ColorTokens.SyntaxNumber, ColorTokens.SyntaxKeyword])
            {
                Assert.True(Contrast(c[token], c[ColorTokens.SyntaxBackground]) >= 2.5, $"{t.Id}: {token}");
            }
        }
    }

    [Fact]
    public void EveryTheme_EveryTextColourReadsOnItsBackground()
    {
        var pairs = new (string Fore, string Back, double Ratio)[]
        {
            (ColorTokens.Text, ColorTokens.PanelBackground, 4.5), (ColorTokens.Text, ColorTokens.WindowBackground, 4.5),
            (ColorTokens.Text, ColorTokens.ColumnHeader, 4.5), (ColorTokens.Text, ColorTokens.Hover, 4.5), (ColorTokens.Text, ColorTokens.Pressed, 4.5),
            (ColorTokens.SecondaryText, ColorTokens.PanelBackground, 4.5), (ColorTokens.SecondaryText, ColorTokens.WindowBackground, 4.5),
            (ColorTokens.SecondaryText, ColorTokens.ColumnHeader, 4.5), (ColorTokens.SecondaryText, ColorTokens.InfoBackground, 4.5),
            (ColorTokens.TertiaryText, ColorTokens.PanelBackground, 4.5), (ColorTokens.TertiaryText, ColorTokens.WindowBackground, 4.5),
            (ColorTokens.TertiaryText, ColorTokens.ColumnHeader, 4.5),
            (ColorTokens.InformationLineText, ColorTokens.InformationLine, 4.5), (ColorTokens.InformationLineDimText, ColorTokens.InformationLine, 4.5),
            (ColorTokens.InformationLineAccent, ColorTokens.InformationLine, 4.5), (ColorTokens.InformationLineDanger, ColorTokens.InformationLine, 4.5),
            (ColorTokens.InformationLineWarning, ColorTokens.InformationLine, 4.5),
            (ColorTokens.SelectedText, ColorTokens.SelectionBackground, 4.5),
            (ColorTokens.Text, ColorTokens.TextSelection, 3), (ColorTokens.SyntaxText, ColorTokens.TextSelection, 3),
            (ColorTokens.CompressedFiles, ColorTokens.PanelBackground, 4.5), (ColorTokens.EncryptedFiles, ColorTokens.PanelBackground, 4.5),
            (ColorTokens.Danger, ColorTokens.PanelBackground, 4.5), (ColorTokens.Warning, ColorTokens.PanelBackground, 4.5), (ColorTokens.Success, ColorTokens.PanelBackground, 4.5),
            (ColorTokens.Danger, ColorTokens.WindowBackground, 4.5), (ColorTokens.Warning, ColorTokens.WindowBackground, 4.5), (ColorTokens.Success, ColorTokens.WindowBackground, 4.5),
            (ColorTokens.Danger, ColorTokens.DangerBackground, 4.5), (ColorTokens.Warning, ColorTokens.WarningBackground, 4.5), (ColorTokens.Success, ColorTokens.SuccessBackground, 4.5),
            (ColorTokens.Text, ColorTokens.DangerBackground, 4.5), (ColorTokens.Text, ColorTokens.WarningBackground, 4.5),
            (ColorTokens.Text, ColorTokens.SuccessBackground, 4.5), (ColorTokens.Text, ColorTokens.InfoBackground, 4.5),
            (ColorTokens.Accent, ColorTokens.PanelBackground, 3), (ColorTokens.Accent, ColorTokens.WindowBackground, 3),
            (ColorTokens.FocusOutline, ColorTokens.PanelBackground, 3), (ColorTokens.FocusOutline, ColorTokens.SelectionBackground, 3),
            (ColorTokens.OnAccent, ColorTokens.Accent, 4.4),
            (ColorTokens.HexZero, ColorTokens.PanelBackground, 3), (ColorTokens.ViewerOffsets, ColorTokens.PanelBackground, 3), (ColorTokens.ViewerLineNumbers, ColorTokens.PanelBackground, 3),
            (ColorTokens.Text, ColorTokens.ViewerMatch, 3), (ColorTokens.Text, ColorTokens.ViewerCurrentMatch, 3),
            (ColorTokens.SyntaxText, ColorTokens.ViewerMatch, 3), (ColorTokens.SyntaxText, ColorTokens.ViewerCurrentMatch, 3),
            (ColorTokens.SyntaxText, ColorTokens.SyntaxBackground, 4.5),
            (ColorTokens.SyntaxText, ColorTokens.CompareRemoved, 3), (ColorTokens.SyntaxText, ColorTokens.CompareAdded, 3), (ColorTokens.SyntaxText, ColorTokens.CompareChangedText, 3),
            (ColorTokens.SyntaxText, ColorTokens.CompareMinorText, 3), (ColorTokens.SyntaxText, ColorTokens.CompareCopied, 3),
        };

        foreach (var t in ThemeCatalog.BuiltIn.Concat(ThemeCatalog.Shipped))
        {
            var c = ColorTokens.Resolve(t);
            foreach (var (fore, back, ratio) in pairs)
            {
                Assert.True(ColorMath.Contrast(c[fore], c[back]) >= ratio - 0.005, $"{t.Id}: {fore} on {back} is {ColorMath.Contrast(c[fore], c[back]):F2} : 1, needs {ratio}");
            }
        }
    }

    [Fact]
    public void FindMatches_TheCurrentOneIsToldApart()
    {
        foreach (var t in ThemeCatalog.BuiltIn.Concat(ThemeCatalog.Shipped))
        {
            var c = ColorTokens.Resolve(t);
            var apart = ColorMath.Contrast(c[ColorTokens.ViewerMatch], c[ColorTokens.ViewerCurrentMatch]);
            Assert.True(apart >= (t.Id.StartsWith("code-", StringComparison.Ordinal) ? 1.05 : 1.25), $"{t.Id}: {apart:F2} : 1");
        }
    }

    [Fact]
    public void AColourThatFallsShort_MovesTowardsReadable_KeepingItsHue_AndOneThatReadsStaysPut()
    {
        var dracula = ColorTokens.Resolve(Theme("dracula"));
        Assert.Equal(Hex("#F8F8F2"), dracula[ColorTokens.Text]);
        var hint = dracula[ColorTokens.TertiaryText];
        Assert.NotEqual(Hex("#6272A4"), hint);
        Assert.True(hint.B > hint.R && hint.B > hint.G, "still bluish");

        foreach (var id in new[] { ThemeCatalog.SalandualLightId, ThemeCatalog.SalandualDarkId })
        {
            var t = Theme(id);
            var c = ColorTokens.Resolve(t);
            foreach (var (token, hex) in t.Colors)
            {
                Assert.True(c[token] == Hex(hex) || token is ColorTokens.OnAccent, $"{id}: {token} {hex} was moved to {ColorMath.ToHex(c[token])}");
            }
        }
    }

    [Fact]
    public void EnsureContrast_KeepsAReadableColour_AndMovesOtherwiseTowardsTheFarSide()
    {
        var white = new Rgb(255, 255, 255);
        var grey = Hex("#8A8A8A");
        Assert.Equal(grey, ColorMath.EnsureContrast(grey, 3.0, white));

        var darker = ColorMath.EnsureContrast(grey, 4.5, white);
        Assert.True(ColorMath.Contrast(darker, white) >= 4.5);
        Assert.True(darker.R < grey.R, "towards black on a light background");

        var black = new Rgb(0, 0, 0);
        var lighter = ColorMath.EnsureContrast(Hex("#555555"), 4.5, black);
        Assert.True(ColorMath.Contrast(lighter, black) >= 4.5);

        var served = ColorMath.EnsureContrast(Hex("#777777"), 12, black, Hex("#808080"));
        Assert.Equal(ColorMath.EnsureContrast(Hex("#777777"), 12, black), served);
        Assert.True(ColorMath.Contrast(served, black) >= 12);

        Assert.Equal(new Rgb(0, 0, 0), ColorMath.EnsureContrast(Hex("#777777"), 7, Hex("#808080")));
    }

    [Fact]
    public void ALightWindowBehindADarkPanel_TextKeepsToThePanel()
    {
        var dark = Theme(ThemeCatalog.SalandualLightId) with
        {
            Id = "half-made", Origin = ThemeOrigin.Mine, Dark = true,
            Colors = new() { [ColorTokens.PanelBackground] = "#202028", [ColorTokens.Text] = "#EEEEEE" },
        };
        var c = ColorTokens.Resolve(dark);
        Assert.Equal(Hex("#202028"), c[ColorTokens.PanelBackground]);
        Assert.Equal(Hex("#EEEEEE"), c[ColorTokens.Text]);
        Assert.True(ColorMath.Contrast(c[ColorTokens.SecondaryText], c[ColorTokens.PanelBackground]) >= 4.5);
        Assert.True(ColorMath.Contrast(c[ColorTokens.TertiaryText], c[ColorTokens.PanelBackground]) >= 4.5);
    }

    [Fact]
    public void OnColor_IsWhiteOrNearBlack_WhicheverStandsOutMore()
    {
        Assert.Equal(new Rgb(255, 255, 255), ColorMath.OnColor(Hex("#0F6CBD")));
        Assert.Equal(new Rgb(10, 10, 10), ColorMath.OnColor(Hex("#479EF5")));
        Assert.Equal(new Rgb(10, 10, 10), ColorMath.OnColor(Hex("#33FF66")));
    }

    [Fact]
    public void EveryTheme_AccentControlsReadInEveryState()
    {
        foreach (var t in ThemeCatalog.BuiltIn.Concat(ThemeCatalog.Shipped))
        {
            var c = ColorTokens.Resolve(t);
            var a = ControlColors.From(c).Accent;
            foreach (var (state, fill) in new[] { ("rest", a.Fill), ("hover", a.FillHover), ("pressed", a.FillPressed) })
            {
                Assert.True(ColorMath.Contrast(a.Label, fill) >= 4.5, $"{t.Id}: label on the {state} fill is {ColorMath.Contrast(a.Label, fill):F2} : 1");
            }

            foreach (var surface in new[] { c[ColorTokens.PanelBackground], c[ColorTokens.WindowBackground] })
            {
                Assert.True(ColorMath.Contrast(a.Edge, surface) >= 3, $"{t.Id}: edge");
                foreach (var (state, mark) in new[] { ("rest", a.Mark), ("hover", a.MarkHover), ("pressed", a.MarkPressed) })
                {
                    Assert.True(ColorMath.Contrast(mark, surface) >= 3, $"{t.Id}: {state} mark is {ColorMath.Contrast(mark, surface):F2} : 1");
                }
            }
        }

        Assert.Equal(Hex("#0F6CBD"), ControlColors.From(ColorTokens.Resolve(Theme(ThemeCatalog.SalandualLightId))).Accent.Fill);
        Assert.Equal(Hex("#326FAC"), ControlColors.From(ColorTokens.Resolve(Theme(ThemeCatalog.SalandualDarkId))).Accent.Fill);
    }

    [Fact]
    public void EveryTheme_ControlsStandOut_AndDisabledTextIsReadable()
    {
        foreach (var t in ThemeCatalog.BuiltIn.Concat(ThemeCatalog.Shipped))
        {
            var c = ColorTokens.Resolve(t);
            var k = ControlColors.From(c);
            foreach (var surface in new[] { c[ColorTokens.PanelBackground], c[ColorTokens.WindowBackground] })
            {
                Assert.True(ColorMath.Contrast(k.Edge, surface) >= 3, $"{t.Id}: edge");
                Assert.True(ColorMath.Contrast(k.DisabledText, surface) >= 3, $"{t.Id}: disabled text");
            }

            Assert.True(ColorMath.Contrast(c[ColorTokens.Text], k.Fill) >= 4.5, $"{t.Id}: button text");
            Assert.True(ColorMath.Contrast(c[ColorTokens.Text], k.FillPressed) >= 4.5, $"{t.Id}: pressed button text");
            Assert.True(ColorMath.Contrast(k.DisabledText, c[ColorTokens.PanelBackground]) < ColorMath.Contrast(c[ColorTokens.Text], c[ColorTokens.PanelBackground]), $"{t.Id}: disabled is dimmer");
        }

        var light = ControlColors.From(ColorTokens.Resolve(Theme(ThemeCatalog.SalandualLightId)));
        Assert.InRange(ColorMath.Contrast(light.Edge, new Rgb(255, 255, 255)), 3, 3.6);
    }

    [Fact]
    public void AThemeLeavingColoursOut_GetsThemFromItsOwnPanelAndText_AndItsAccent()
    {
        var mine = new ColorTheme { Id = "mine", Dark = true, Colors = new() { [ColorTokens.PanelBackground] = "#000000", [ColorTokens.Text] = "#FFFFFF", [ColorTokens.Accent] = "#FFD000" } };
        var c = ColorTokens.Resolve(mine);
        Assert.Equal(ColorMath.Mix(new Rgb(0, 0, 0), Hex("#FFD000"), 0.22), c[ColorTokens.SelectionBackground]);
        Assert.Equal(ColorMath.Mix(new Rgb(0, 0, 0), new Rgb(255, 255, 255), 0.14), c[ColorTokens.Border]);
        Assert.Equal(Hex("#FFD000"), c[ColorTokens.SyntaxKey]);
        Assert.Equal(new Rgb(10, 10, 10), c[ColorTokens.OnAccent]);

        Assert.Equal(ColorMath.Lighten(Hex("#0078D4"), 0.35), ColorTokens.Resolve(mine with { Accent = ThemeAccent.Windows }, Hex("#0078D4"))[ColorTokens.Accent]);
        Assert.Equal(Hex("#C239B3"), ColorTokens.Resolve(mine with { Accent = ThemeAccent.Custom, CustomAccent = "#C239B3" })[ColorTokens.Accent]);
        Assert.Null(ColorTokens.OwnSurface(mine, c));
        var own = mine with { CodeBackground = CodeBackground.Own, Colors = new(mine.Colors) { [ColorTokens.SyntaxBackground] = "#101820" } };
        Assert.Equal(Hex("#101820"), ColorTokens.OwnSurface(own, ColorTokens.Resolve(own))!.Background);
    }

    [Fact]
    public void Schedule_NoSchedule_WindowsMode_AndYourOwnTimes()
    {
        var s = new ThemeSchedule { Active = "dracula", Light = "one-light", Dark = "nord" };
        Assert.Equal("one-light", s.ActiveId(windowsDark: false, 600));
        Assert.Equal("nord", s.ActiveId(windowsDark: true, 600));
        Assert.Equal("dracula", (s with { Mode = ScheduleMode.None }).ActiveId(true, 600));

        var times = s with
        {
            Mode = ScheduleMode.Custom,
            Changes = [new() { Minute = 19 * 60, Theme = "nord" }, new() { Minute = 7 * 60, Theme = "one-light" }],
        };
        Assert.Equal("nord", times.ActiveId(false, 3 * 60));
        Assert.Equal("one-light", times.ActiveId(false, 7 * 60));
        Assert.Equal("nord", times.ActiveId(false, 23 * 60));
        Assert.Equal(60, times.MinutesToNextChange(6 * 60));
        Assert.Equal((7 * 60) + 60, times.MinutesToNextChange(23 * 60));
        Assert.Null(s.MinutesToNextChange(600));
    }

    [Fact]
    public void Normalize_KeepsYourThemes_DropsBadOnes_AndRepairsTheSchedule()
    {
        var a = new AppSettings
        {
            Appearance = new AppearanceSettings
            {
                Themes =
                [
                    new() { Id = "mine", Name = " Mine ", Origin = ThemeOrigin.Shipped, Colors = new() { [ColorTokens.Text] = "#12345", ["NoSuchToken"] = "#000000", [ColorTokens.PanelBackground] = "#abcdef" } },
                    new() { Id = "mine", Name = "Twice" },
                    new() { Id = "dracula", Name = "Not the real one" },
                    new() { Id = "", Name = "No id" },
                ],
                Schedule = new ThemeSchedule { Mode = ScheduleMode.Custom, Active = "gone", Dark = "mine", Changes = [new() { Minute = 5000, Theme = "gone" }] },
            },
        }.Normalize().Appearance;

        var mine = Assert.Single(a.Themes);
        Assert.Equal("Mine", mine.Name);
        Assert.Equal(ThemeOrigin.Mine, mine.Origin);
        Assert.Equal(new Dictionary<string, string> { [ColorTokens.PanelBackground] = "#ABCDEF" }, mine.Colors);
        Assert.Equal(ThemeCatalog.SalandualLightId, a.Schedule.Active);
        Assert.Equal("mine", a.Schedule.Dark);
        var change = Assert.Single(a.Schedule.Changes);
        Assert.Equal((1439, ThemeCatalog.SalandualLightId), (change.Minute, change.Theme));
    }

    [Fact]
    public void Equivalence_ComparesThemesAndTheSchedule()
    {
        var a = AppearanceSettings.Fixed("nord").Normalize();
        Assert.True(a.IsEquivalentTo(AppearanceSettings.Fixed("nord").Normalize()));
        Assert.False(a.IsEquivalentTo(AppearanceSettings.Fixed("dracula").Normalize()));
        var withTheme = a with { Themes = [new() { Id = "mine", Colors = new() { [ColorTokens.Text] = "#111111" } }] };
        Assert.False(withTheme.IsEquivalentTo(a with { Themes = [new() { Id = "mine", Colors = new() { [ColorTokens.Text] = "#222222" } }] }));
    }
}
