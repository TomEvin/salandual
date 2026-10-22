// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ThemeCatalog.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Settings;

public static partial class ThemeCatalog
{
    public const string SalandualLightId = "salandual-light";
    public const string SalandualDarkId = "salandual-dark";

    private static readonly Dictionary<string, string[]> _aliases = new(StringComparer.Ordinal)
    {
        ["win"] = [ColorTokens.WindowBackground], ["panel"] = [ColorTokens.PanelBackground], ["text"] = [ColorTokens.Text],
        ["text2"] = [ColorTokens.SecondaryText], ["text3"] = [ColorTokens.TertiaryText], ["hdr"] = [ColorTokens.ColumnHeader],
        ["info"] = [ColorTokens.InformationLine], ["infoText"] = [ColorTokens.InformationLineText], ["sel"] = [ColorTokens.SelectionBackground],
        ["selText"] = [ColorTokens.SelectedText], ["hover"] = [ColorTokens.Hover], ["focus"] = [ColorTokens.FocusOutline],
        ["border"] = [ColorTokens.Border], ["border2"] = [ColorTokens.BorderStrong], ["accent"] = [ColorTokens.Accent],
        ["lineNo"] = [ColorTokens.ViewerLineNumbers], ["offsets"] = [ColorTokens.ViewerOffsets], ["match"] = [ColorTokens.ViewerMatch],
        ["cmatch"] = [ColorTokens.ViewerCurrentMatch], ["code"] = [ColorTokens.SyntaxText], ["key"] = [ColorTokens.SyntaxKey],
        ["tag"] = [ColorTokens.SyntaxTag], ["str"] = [ColorTokens.SyntaxString], ["num"] = [ColorTokens.SyntaxNumber],
        ["kw"] = [ColorTokens.SyntaxKeyword], ["punct"] = [ColorTokens.SyntaxPunctuation], ["com"] = [ColorTokens.SyntaxComment],
        ["attr"] = [ColorTokens.SyntaxAttribute], ["head"] = [ColorTokens.SyntaxHeading], ["link"] = [ColorTokens.SyntaxLink],
        ["func"] = [ColorTokens.SyntaxFunction], ["danger"] = [ColorTokens.Danger], ["warn"] = [ColorTokens.Warning], ["ok"] = [ColorTokens.Success],
        ["zip"] = [ColorTokens.CompressedFiles], ["enc"] = [ColorTokens.EncryptedFiles], ["hex0"] = [ColorTokens.HexZero],
        ["hexCtl"] = [ColorTokens.HexControl], ["hexHigh"] = [ColorTokens.HexHigh], ["hexFF"] = [ColorTokens.HexFF],
    };

    public static readonly IReadOnlyList<ColorTheme> BuiltIn =
    [
        Own(SalandualLightId, Strings.Theme_SalandualLight, dark: false, ColorTokens.SalandualLight),
        Own(SalandualDarkId, Strings.Theme_SalandualDark, dark: true, ColorTokens.SalandualDark),
        Own("classic-light", Strings.Theme_ClassicLight, dark: false, With(ColorTokens.SalandualLight,
            "panel=#FFFFFF text=#000000 text2=#000000 selText=#FF0000 sel=#FFFFFF focus=#808080 hdr=#F0F0F0 info=#F0F0F0 zip=#0000FF enc=#008000 hex0=#808080 offsets=#808080 lineNo=#808080")),
        Own("classic-dark", Strings.Theme_ClassicDark, dark: true, With(ColorTokens.SalandualDark,
            "panel=#1E1E1E text=#E8E8E8 selText=#FF6060 sel=#1E1E1E focus=#A0A0A0")),
        Own("contrast-light", Strings.Theme_ContrastLight, dark: false, With(ColorTokens.SalandualLight,
            "panel=#FFFFFF text=#000000 text2=#000000 selText=#FFFFFF sel=#000080 focus=#000000 hdr=#FFFFFF info=#FFFFFF " +
            "hex0=#595959 hexCtl=#8B4000 hexHigh=#000080 hexFF=#A00000 offsets=#000000 lineNo=#000000 match=#FFFF00 cmatch=#00FFFF " +
            "key=#000080 str=#006400 num=#8B4000 kw=#000080 punct=#000000 com=#595959 attr=#8B4000 head=#000080 link=#000080 func=#5C2E00")),
        Own("contrast-dark", Strings.Theme_ContrastDark, dark: true, With(ColorTokens.SalandualDark,
            "panel=#000000 text=#FFFFFF text2=#FFFFFF selText=#000000 sel=#FFFF00 focus=#00FFFF hdr=#000000 info=#000000 zip=#00FFFF enc=#00FF00 " +
            "hex0=#C0C0C0 hexCtl=#FFB000 hexHigh=#00FFFF hexFF=#FF6060 offsets=#FFFFFF lineNo=#FFFFFF match=#0000C0 cmatch=#008080 " +
            "key=#00FFFF str=#00FF00 num=#FFB000 kw=#00FFFF punct=#FFFFFF com=#C0C0C0 attr=#FFB000 head=#00FFFF link=#00FFFF func=#FFFF80")),

        Own("soft-grey", Strings.Theme_SoftGrey, dark: false, With(ColorTokens.SalandualLight,
            "win=#E4E4E4 panel=#EDEDED text=#1E1E1E text2=#555555 text3=#6E6E6E hdr=#E4E4E4 info=#E4E4E4 infoText=#1E1E1E sel=#C9D3DE selText=#12324F " +
            "hover=#E2E2E2 focus=#3E6A93 border=#C8C8C8 border2=#A8A8A8 accent=#3E6A93 lineNo=#6E6E6E offsets=#6E6E6E hex0=#7A7A7A match=#F0E2A8 code=#1E1E1E " +
            "key=#3E6A93 tag=#3E6A93 str=#3D7A3D num=#8A5A00 kw=#3E6A93 punct=#555555 com=#6E6E6E attr=#8A5A00 head=#3E6A93 link=#3E6A93 func=#5A3F7A")),
        Own("graphite", Strings.Theme_Graphite, dark: true, With(ColorTokens.SalandualDark,
            "win=#2E2E30 panel=#3A3A3C text=#E8E8E8 text2=#B0B0B2 text3=#949496 hdr=#333335 info=#333335 infoText=#E8E8E8 sel=#4D5A68 selText=#FFFFFF " +
            "hover=#444446 focus=#8FB0D0 border=#4C4C4E border2=#626264 accent=#8FB0D0 lineNo=#949496 offsets=#949496 match=#5A4E2A code=#E8E8E8 " +
            "key=#8FB0D0 tag=#8FB0D0 str=#9CCB8A num=#E0B870 kw=#B4A0E0 punct=#B0B0B2 com=#949496 attr=#E0B870 head=#8FB0D0 link=#8FB0D0 func=#9CCB8A")),
        Own("steel", Strings.Theme_Steel, dark: false, With(ColorTokens.SalandualLight,
            "win=#C9CFD6 panel=#DDE2E7 text=#1C2329 text2=#4A545E text3=#5E6873 hdr=#D2D8DE info=#CBD2D9 infoText=#1C2329 sel=#A9B8C8 selText=#0E1A26 " +
            "hover=#D0D6DC focus=#2F5878 border=#B4BCC5 border2=#8E98A3 accent=#2F5878 lineNo=#5E6873 offsets=#5E6873 hex0=#66717C match=#E8D9A0 code=#1C2329 " +
            "key=#2F5878 tag=#2F5878 str=#3F6E3F num=#80551A kw=#5A3F7A punct=#4A545E com=#5E6873 attr=#80551A head=#2F5878 link=#2F5878 func=#3F6E3F")),
        Own("gunmetal", Strings.Theme_Gunmetal, dark: true, With(ColorTokens.SalandualDark,
            "win=#22272C panel=#2C3238 text=#DCE2E8 text2=#9AA5B0 text3=#808B96 hdr=#262B31 info=#262B31 infoText=#DCE2E8 sel=#3E5266 selText=#FFFFFF " +
            "hover=#343B42 focus=#7FA7CC border=#3C444C border2=#525C66 accent=#7FA7CC lineNo=#808B96 offsets=#808B96 match=#54482A code=#DCE2E8 " +
            "key=#7FA7CC tag=#7FA7CC str=#94C48A num=#D9B070 kw=#B8A0D8 punct=#9AA5B0 com=#808B96 attr=#D9B070 head=#7FA7CC link=#7FA7CC func=#94C48A")),
    ];

    public static readonly IReadOnlyList<ColorTheme> Shipped =
    [
        Ship("far", Strings.Theme_Far, dark: true,
            "win=#000000 panel=#000080 text=#00FFFF text2=#00C0C0 text3=#008B8B hdr=#000080 info=#000080 infoText=#00FFFF sel=#000080 selText=#FFFF00 " +
            "hover=#0000B0 focus=#00A0A0 border=#00A0A0 border2=#00FFFF accent=#008080 lineNo=#00C0C0 match=#808000 code=#00FFFF " +
            "key=#FFFF00 tag=#FFFF00 str=#00FF00 num=#FF00FF kw=#FFFFFF punct=#00FFFF com=#808080 attr=#FF00FF head=#FFFFFF link=#00FF00 func=#FFFFFF " +
            "danger=#FF5555 warn=#FFFF00 ok=#00FF00 zip=#FF00FF enc=#00FF00"),
        Ship("total-commander", Strings.Theme_TotalCommander, dark: false,
            "win=#F0F0F0 panel=#FFFFFF text=#000000 text2=#404040 hdr=#F0F0F0 info=#F0F0F0 infoText=#000000 sel=#FFFFFF selText=#FF0000 " +
            "hover=#E5F3FF focus=#000000 border=#A0A0A0 border2=#808080 accent=#0078D7 lineNo=#808080 match=#FFFF80 code=#000000 " +
            "key=#000080 tag=#800000 str=#A31515 num=#008080 kw=#0000FF punct=#000000 com=#008000 attr=#FF0000 head=#000080 link=#0000FF func=#795E26 " +
            "danger=#C00000 warn=#9D5D00 ok=#008000 zip=#0000FF enc=#008000"),
        Ship("dracula", "Dracula", dark: true,
            "win=#21222C panel=#282A36 text=#F8F8F2 text2=#C0C0BC text3=#6272A4 hdr=#21222C info=#191A21 infoText=#F8F8F2 sel=#44475A selText=#F8F8F2 " +
            "hover=#343746 focus=#BD93F9 border=#44475A border2=#6272A4 accent=#BD93F9 lineNo=#6272A4 match=#71614F code=#F8F8F2 " +
            "key=#8BE9FD tag=#FF79C6 str=#F1FA8C num=#BD93F9 kw=#FF79C6 punct=#F8F8F2 com=#6272A4 attr=#50FA7B head=#BD93F9 link=#8BE9FD func=#50FA7B " +
            "danger=#FF5555 warn=#FFB86C ok=#50FA7B zip=#8BE9FD enc=#50FA7B"),
        Ship("nord", "Nord", dark: true,
            "win=#3B4252 panel=#2E3440 text=#D8DEE9 text2=#A4ABB7 text3=#7B88A1 hdr=#3B4252 info=#3B4252 infoText=#D8DEE9 sel=#434C5E selText=#ECEFF4 " +
            "hover=#3B4252 focus=#88C0D0 border=#3B4252 border2=#4C566A accent=#88C0D0 lineNo=#616E88 match=#52707F code=#D8DEE9 " +
            "key=#8FBCBB tag=#81A1C1 str=#A3BE8C num=#B48EAD kw=#81A1C1 punct=#ECEFF4 com=#616E88 attr=#8FBCBB head=#88C0D0 link=#88C0D0 func=#88C0D0 " +
            "danger=#BF616A warn=#EBCB8B ok=#A3BE8C zip=#88C0D0 enc=#A3BE8C"),
        Ship("nord-light", Strings.Theme_NordLight, dark: false,
            "win=#E5E9F0 panel=#ECEFF4 text=#2E3440 text2=#4C566A text3=#7B88A1 hdr=#E5E9F0 info=#D8DEE9 infoText=#2E3440 sel=#D8DEE9 selText=#2E3440 " +
            "hover=#E5E9F0 focus=#5E81AC border=#D8DEE9 border2=#C2CAD8 accent=#5E81AC lineNo=#7B88A1 match=#EBDDB5 code=#2E3440 " +
            "key=#5E81AC tag=#5E81AC str=#5D7B3A num=#9A5F94 kw=#5E81AC punct=#4C566A com=#7B88A1 attr=#A15C31 head=#5E81AC link=#5E81AC func=#3B7C8C " +
            "danger=#BF616A warn=#A06A00 ok=#5D7B3A zip=#5E81AC enc=#5D7B3A"),
        Ship("gruvbox-dark", Strings.Theme_GruvboxDark, dark: true,
            "win=#1D2021 panel=#282828 text=#EBDBB2 text2=#BDAE93 text3=#928374 hdr=#32302F info=#32302F infoText=#EBDBB2 sel=#504945 selText=#FBF1C7 " +
            "hover=#3C3836 focus=#FABD2F border=#3C3836 border2=#504945 accent=#D79921 lineNo=#7C6F64 match=#5B4D24 code=#EBDBB2 " +
            "key=#83A598 tag=#8EC07C str=#B8BB26 num=#D3869B kw=#FB4934 punct=#A89984 com=#928374 attr=#FABD2F head=#FABD2F link=#83A598 func=#8EC07C " +
            "danger=#FB4934 warn=#FABD2F ok=#B8BB26 zip=#83A598 enc=#B8BB26"),
        Ship("gruvbox-light", Strings.Theme_GruvboxLight, dark: false,
            "win=#EBDBB2 panel=#FBF1C7 text=#3C3836 text2=#665C54 text3=#928374 hdr=#F2E5BC info=#EBDBB2 infoText=#3C3836 sel=#D5C4A1 selText=#282828 " +
            "hover=#F2E5BC focus=#B57614 border=#D5C4A1 border2=#BDAE93 accent=#B57614 lineNo=#A89984 match=#F2D98C code=#3C3836 " +
            "key=#076678 tag=#427B58 str=#79740E num=#8F3F71 kw=#9D0006 punct=#7C6F64 com=#928374 attr=#B57614 head=#B57614 link=#076678 func=#427B58 " +
            "danger=#9D0006 warn=#B57614 ok=#79740E zip=#076678 enc=#79740E"),
        Ship("catppuccin-latte", "Catppuccin Latte", dark: false,
            "win=#E6E9EF panel=#EFF1F5 text=#4C4F69 text2=#6C6F85 text3=#8C8FA1 hdr=#E6E9EF info=#DCE0E8 infoText=#4C4F69 sel=#CCD0DA selText=#4C4F69 " +
            "hover=#DCE0E8 focus=#8839EF border=#CCD0DA border2=#BCC0CC accent=#8839EF lineNo=#8C8FA1 match=#F0DDB8 code=#4C4F69 " +
            "key=#1E66F5 tag=#1E66F5 str=#40A02B num=#FE640B kw=#8839EF punct=#7C7F93 com=#8C8FA1 attr=#DF8E1D head=#D20F39 link=#1E66F5 func=#1E66F5 " +
            "danger=#D20F39 warn=#DF8E1D ok=#40A02B zip=#1E66F5 enc=#40A02B"),
        Ship("catppuccin-mocha", "Catppuccin Mocha", dark: true,
            "win=#181825 panel=#1E1E2E text=#CDD6F4 text2=#A6ADC8 text3=#7F849C hdr=#181825 info=#11111B infoText=#CDD6F4 sel=#313244 selText=#CDD6F4 " +
            "hover=#262637 focus=#CBA6F7 border=#313244 border2=#45475A accent=#CBA6F7 lineNo=#7F849C match=#5E4E3A code=#CDD6F4 " +
            "key=#89B4FA tag=#89B4FA str=#A6E3A1 num=#FAB387 kw=#CBA6F7 punct=#9399B2 com=#7F849C attr=#F9E2AF head=#F38BA8 link=#89B4FA func=#89B4FA " +
            "danger=#F38BA8 warn=#F9E2AF ok=#A6E3A1 zip=#89B4FA enc=#A6E3A1"),
        Ship("tokyo-night", "Tokyo Night", dark: true,
            "win=#16161E panel=#1A1B26 text=#A9B1D6 text2=#787C99 text3=#565F89 hdr=#16161E info=#16161E infoText=#787C99 sel=#283457 selText=#C0CAF5 " +
            "hover=#202330 focus=#7AA2F7 border=#232433 border2=#3B4261 accent=#3D59A1 lineNo=#4A5072 match=#2E3A5E code=#A9B1D6 " +
            "key=#7AA2F7 tag=#F7768E str=#9ECE6A num=#FF9E64 kw=#BB9AF7 punct=#89DDFF com=#565F89 attr=#BB9AF7 head=#89DDFF link=#73DACA func=#7AA2F7 " +
            "danger=#F7768E warn=#E0AF68 ok=#9ECE6A zip=#7DCFFF enc=#9ECE6A"),
        Ship("one-light", "One Light", dark: false,
            "win=#EAEAEB panel=#FAFAFA text=#383A42 text2=#696C77 text3=#A0A1A7 hdr=#EAEAEB info=#EAEAEB infoText=#383A42 sel=#DBDBDC selText=#232324 " +
            "hover=#F0F0F1 focus=#526FFF border=#DBDBDC border2=#C8C8CA accent=#526FFF lineNo=#9D9D9F match=#F5E2A8 code=#383A42 " +
            "key=#E45649 tag=#E45649 str=#50A14F num=#986801 kw=#A626A4 punct=#383A42 com=#A0A1A7 attr=#986801 head=#E45649 link=#4078F2 func=#4078F2 " +
            "danger=#E45649 warn=#C18401 ok=#50A14F zip=#4078F2 enc=#50A14F"),
        Ship("github-light", "GitHub Light", dark: false,
            "win=#F6F8FA panel=#FFFFFF text=#1F2328 text2=#59636E text3=#818B98 hdr=#F6F8FA info=#F6F8FA infoText=#1F2328 sel=#DDF4FF selText=#1F2328 " +
            "hover=#EFF2F5 focus=#0969DA border=#D1D9E0 border2=#B7C0CA accent=#0969DA lineNo=#8C959F match=#FFF8C5 code=#1F2328 " +
            "key=#0550AE tag=#116329 str=#0A3069 num=#0550AE kw=#CF222E punct=#1F2328 com=#59636E attr=#0550AE head=#0550AE link=#0A3069 func=#8250DF " +
            "danger=#D1242F warn=#9A6700 ok=#1A7F37 zip=#0969DA enc=#1A7F37"),
        Ship("github-dark", "GitHub Dark", dark: true,
            "win=#010409 panel=#0D1117 text=#E6EDF3 text2=#9198A1 text3=#6E7681 hdr=#010409 info=#010409 infoText=#9198A1 sel=#12233A selText=#E6EDF3 " +
            "hover=#161B22 focus=#1F6FEB border=#30363D border2=#3D444D accent=#1F6FEB lineNo=#6E7681 match=#5A4A16 code=#E6EDF3 " +
            "key=#79C0FF tag=#7EE787 str=#A5D6FF num=#79C0FF kw=#FF7B72 punct=#E6EDF3 com=#8B949E attr=#79C0FF head=#1F6FEB link=#A5D6FF func=#D2A8FF " +
            "danger=#F85149 warn=#D29922 ok=#3FB950 zip=#58A6FF enc=#3FB950"),
        Ship("amber-terminal", Strings.Theme_AmberTerminal, dark: true,
            "win=#0D0800 panel=#140C00 text=#FFB000 text2=#CC8C00 text3=#8A5E00 hdr=#1C1200 info=#1C1200 infoText=#FFB000 sel=#4D3500 selText=#FFD580 " +
            "hover=#261A00 focus=#FFB000 border=#3D2A00 border2=#5C4000 accent=#FFB000 lineNo=#8A5E00 match=#6B4A00 code=#FFB000 " +
            "key=#FFCC4D tag=#FFCC4D str=#FFD580 num=#FFE0A0 kw=#FFC21A punct=#CC8C00 com=#8A5E00 attr=#FFE0A0 head=#FFD580 link=#FFCC4D func=#FFC21A " +
            "danger=#FF6A3D warn=#FFD580 ok=#FFCC4D zip=#FFCC4D enc=#FFE0A0"),
        Ship("green-terminal", Strings.Theme_GreenTerminal, dark: true,
            "win=#000A00 panel=#001100 text=#33FF66 text2=#26C24D text3=#1A8033 hdr=#001A00 info=#001A00 infoText=#33FF66 sel=#0B4D1E selText=#99FFB3 " +
            "hover=#002600 focus=#33FF66 border=#0B3D17 border2=#14662B accent=#33FF66 lineNo=#1A8033 match=#1E6B2E code=#33FF66 " +
            "key=#66FF8C tag=#66FF8C str=#99FFB3 num=#CCFFD9 kw=#4DFF79 punct=#26C24D com=#1A8033 attr=#CCFFD9 head=#99FFB3 link=#66FF8C func=#4DFF79 " +
            "danger=#FF5C5C warn=#E5FF66 ok=#66FF8C zip=#66FF8C enc=#CCFFD9"),

        .. VsCodeOwn(),
    ];

    public static IEnumerable<ColorTheme> All(AppearanceSettings appearance) => [.. BuiltIn, .. Shipped, .. appearance.Themes];

    public static ColorTheme Find(AppearanceSettings appearance, string? id, bool fallbackDark = false)
        => TryFind(appearance, id) ?? BuiltIn[fallbackDark ? 1 : 0];

    public static ColorTheme? TryFind(AppearanceSettings appearance, string? id)
        => id is null ? null : All(appearance).FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    private static ColorTheme Own(string id, string name, bool dark, IReadOnlyDictionary<string, string> colors)
        => new() { Id = id, Name = name, Dark = dark, Origin = ThemeOrigin.BuiltIn, Colors = new(colors, StringComparer.Ordinal) };

    private static ColorTheme Ship(string id, string name, bool dark, string colors)
    {
        var parsed = Parse(colors);
        parsed.TryAdd(ColorTokens.ViewerOffsets, parsed.GetValueOrDefault(ColorTokens.ViewerLineNumbers) ?? "");
        return new() { Id = id, Name = name, Dark = dark, Origin = ThemeOrigin.Shipped, Colors = parsed };
    }

    private static ColorTheme VsShip(string id, string name, bool dark, string colors)
        => new()
        {
            Id = id,
            Name = name,
            Dark = dark,
            Origin = ThemeOrigin.Shipped,
            Colors = colors.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal),
        };

    private static Dictionary<string, string> With(IReadOnlyDictionary<string, string> basis, string changes)
    {
        var result = new Dictionary<string, string>(basis, StringComparer.Ordinal);
        foreach (var (token, hex) in Parse(changes))
        {
            result[token] = hex;
        }

        return result;
    }

    private static Dictionary<string, string> Parse(string colors)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in colors.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var (alias, hex) = (pair[..pair.IndexOf('=')], pair[(pair.IndexOf('=') + 1)..]);
            foreach (var token in _aliases[alias])
            {
                result[token] = hex;
            }
        }

        return result;
    }
}
