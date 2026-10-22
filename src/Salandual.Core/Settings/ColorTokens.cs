// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ColorTokens.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Settings;

public static class ColorTokens
{
    public const string WindowBackground = "WindowBackground";
    public const string PanelBackground = "PanelBackground";
    public const string Text = "Text";
    public const string SecondaryText = "SecondaryText";
    public const string TertiaryText = "TertiaryText";
    public const string SelectedText = "SelectedText";
    public const string SelectionBackground = "SelectionBackground";
    public const string FocusOutline = "FocusOutline";
    public const string ColumnHeader = "ColumnHeader";
    public const string InformationLine = "InformationLine";
    public const string InformationLineText = "InformationLineText";
    public const string Hover = "Hover";
    public const string Pressed = "Pressed";
    public const string Border = "Border";
    public const string BorderStrong = "BorderStrong";
    public const string Accent = "Accent";
    public const string CompressedFiles = "CompressedFiles";
    public const string EncryptedFiles = "EncryptedFiles";
    public const string Danger = "Danger";
    public const string Warning = "Warning";
    public const string Success = "Success";

    public const string HexZero = "HexZero";
    public const string HexText = "HexText";
    public const string HexControl = "HexControl";
    public const string HexHigh = "HexHigh";
    public const string HexFF = "HexFF";
    public const string ViewerOffsets = "ViewerOffsets";
    public const string ViewerCaret = "ViewerCaret";
    public const string ViewerMatch = "ViewerMatch";
    public const string ViewerCurrentMatch = "ViewerCurrentMatch";
    public const string ViewerLineNumbers = "ViewerLineNumbers";

    public const string SyntaxKey = "SyntaxKey";
    public const string SyntaxString = "SyntaxString";
    public const string SyntaxNumber = "SyntaxNumber";
    public const string SyntaxKeyword = "SyntaxKeyword";
    public const string SyntaxPunctuation = "SyntaxPunctuation";
    public const string SyntaxComment = "SyntaxComment";
    public const string SyntaxAttribute = "SyntaxAttribute";
    public const string SyntaxHeading = "SyntaxHeading";
    public const string SyntaxLink = "SyntaxLink";
    public const string SyntaxFunction = "SyntaxFunction";
    public const string SyntaxTag = "SyntaxTag";

    public const string SyntaxBackground = "SyntaxBackground";
    public const string SyntaxText = "SyntaxText";

    public const string CompareRemoved = "CompareRemoved";
    public const string CompareAdded = "CompareAdded";
    public const string CompareChangedText = "CompareChangedText";
    public const string CompareMinorText = "CompareMinorText";
    public const string CompareCopied = "CompareCopied";

    public const string WarningBackground = "WarningBackground";
    public const string DangerBackground = "DangerBackground";
    public const string InfoBackground = "InfoBackground";

    public const string CompareMinorMark = "CompareMinorMark";
    public const string SuccessBackground = "SuccessBackground";
    public const string FocusOutlineInactive = "FocusOutlineInactive";
    public const string OnAccent = "OnAccent";
    public const string AccentHover = "AccentHover";

    public const string InformationLineDimText = "InformationLineDimText";
    public const string InformationLineHover = "InformationLineHover";
    public const string InformationLineAccent = "InformationLineAccent";
    public const string InformationLineDanger = "InformationLineDanger";
    public const string InformationLineWarning = "InformationLineWarning";
    public const string TextSelection = "TextSelection";

    public static readonly IReadOnlyList<(string Token, string Label)> Window =
    [
        (WindowBackground, Strings.Color_WindowBackground),
        (PanelBackground, Strings.Color_PanelBackground),
        (Text, Strings.Color_Text),
        (SecondaryText, Strings.Color_SecondaryText),
        (TertiaryText, Strings.Color_TertiaryText),
        (SelectedText, Strings.Color_SelectedText),
        (SelectionBackground, Strings.Color_SelectionBackground),
        (FocusOutline, Strings.Color_FocusOutline),
        (ColumnHeader, Strings.Color_ColumnHeader),
        (InformationLine, Strings.Color_InformationLine),
        (InformationLineText, Strings.Color_InformationLineText),
        (Hover, Strings.Color_Hover),
        (Pressed, Strings.Color_Pressed),
        (Border, Strings.Color_Border),
        (BorderStrong, Strings.Color_BorderStrong),
        (Accent, Strings.Color_Accent),
        (CompressedFiles, Strings.Color_CompressedFiles),
        (EncryptedFiles, Strings.Color_EncryptedFiles),
        (Danger, Strings.Color_Danger),
        (Warning, Strings.Color_Warning),
        (Success, Strings.Color_Success),
    ];

    public static readonly IReadOnlyList<(string Token, string Label)> Viewer =
    [
        (HexZero, Strings.Color_HexZero),
        (HexText, Strings.Color_HexText),
        (HexControl, Strings.Color_HexControl),
        (HexHigh, Strings.Color_HexHigh),
        (HexFF, Strings.Color_HexFF),
        (ViewerOffsets, Strings.Color_ViewerOffsets),
        (ViewerCaret, Strings.Color_ViewerCaret),
        (ViewerMatch, Strings.Color_ViewerMatch),
        (ViewerCurrentMatch, Strings.Color_ViewerCurrentMatch),
        (ViewerLineNumbers, Strings.Color_ViewerLineNumbers),
    ];

    public static readonly IReadOnlyList<(string Token, string Label)> Syntax =
    [
        (SyntaxBackground, Strings.Color_SyntaxBackground),
        (SyntaxText, Strings.Color_SyntaxText),
        (SyntaxKey, Strings.Color_SyntaxKey),
        (SyntaxTag, Strings.Color_SyntaxTag),
        (SyntaxString, Strings.Color_SyntaxString),
        (SyntaxNumber, Strings.Color_SyntaxNumber),
        (SyntaxKeyword, Strings.Color_SyntaxKeyword),
        (SyntaxPunctuation, Strings.Color_SyntaxPunctuation),
        (SyntaxComment, Strings.Color_SyntaxComment),
        (SyntaxAttribute, Strings.Color_SyntaxAttribute),
        (SyntaxHeading, Strings.Color_SyntaxHeading),
        (SyntaxLink, Strings.Color_SyntaxLink),
        (SyntaxFunction, Strings.Color_SyntaxFunction),
    ];

    public static readonly IReadOnlyList<(string Token, string Label)> Compare =
    [
        (CompareRemoved, Strings.Color_CompareRemoved),
        (CompareAdded, Strings.Color_CompareAdded),
        (CompareChangedText, Strings.Color_CompareChangedText),
        (CompareMinorText, Strings.Color_CompareMinorText),
        (CompareCopied, Strings.Color_CompareCopied),
    ];

    public static readonly IReadOnlyList<(string Token, string Label)> All = [.. Window, .. Viewer, .. Syntax, .. Compare];

    private static readonly HashSet<string> _known = [.. All.Select(t => t.Token)];

    public static bool IsKnown(string token) => _known.Contains(token);

    public static readonly IReadOnlyDictionary<string, string> SalandualLight = new Dictionary<string, string>
    {
        [WindowBackground] = "#F3F3F3", [PanelBackground] = "#FFFFFF", [Text] = "#1B1B1B", [SecondaryText] = "#5C5C5C", [TertiaryText] = "#6E6E6E",
        [ColumnHeader] = "#F9F9F9", [InformationLine] = "#F9F9F9", [CompressedFiles] = "#0063B1", [EncryptedFiles] = "#107C10",
        [Hover] = "#EFEFEF", [Pressed] = "#E6E6E6", [Border] = "#E1E1E1", [BorderStrong] = "#C8C8C8", [Accent] = "#0F6CBD",
        [Danger] = "#C42B1C", [Warning] = "#9D5D00", [Success] = "#0F7B0F",
        [HexZero] = "#8A8A8A", [HexControl] = "#B35900", [HexHigh] = "#0063B1", [HexFF] = "#C42B1C", [ViewerOffsets] = "#8A8A8A", [ViewerLineNumbers] = "#8A8A8A",
        [SyntaxString] = "#0F7B0F", [SyntaxNumber] = "#9D5D00", [SyntaxComment] = "#8A8A8A", [SyntaxAttribute] = "#9D5D00", [SyntaxFunction] = "#8250DF",
        [CompareRemoved] = "#FBE4E2", [CompareAdded] = "#E3F4E1", [CompareChangedText] = "#F5D97A", [CompareMinorText] = "#CBDCFB", [CompareCopied] = "#EAF3FC",
    };

    public static readonly IReadOnlyDictionary<string, string> SalandualDark = new Dictionary<string, string>
    {
        [WindowBackground] = "#1F1F1F", [PanelBackground] = "#272727", [Text] = "#F2F2F2", [SecondaryText] = "#B8B8B8", [TertiaryText] = "#949494",
        [ColumnHeader] = "#2D2D2D", [InformationLine] = "#2D2D2D", [CompressedFiles] = "#60CDFF", [EncryptedFiles] = "#6CCB5F",
        [Hover] = "#333333", [Pressed] = "#3A3A3A", [Border] = "#3A3A3A", [BorderStrong] = "#4D4D4D", [Accent] = "#479EF5",
        [Danger] = "#FF99A4", [Warning] = "#F7C04A", [Success] = "#6CCB5F",
        [HexZero] = "#8C8C8C", [HexControl] = "#F7A35C", [HexHigh] = "#60CDFF", [HexFF] = "#FF99A4", [ViewerOffsets] = "#8C8C8C", [ViewerLineNumbers] = "#8C8C8C",
        [SyntaxString] = "#6CCB5F", [SyntaxNumber] = "#F7C04A", [SyntaxComment] = "#8C8C8C", [SyntaxAttribute] = "#F7C04A", [SyntaxFunction] = "#D2A8FF",
        [CompareRemoved] = "#4A2426", [CompareAdded] = "#1F3A22", [CompareChangedText] = "#6E5C1C", [CompareMinorText] = "#2C4472", [CompareCopied] = "#1E3247",
    };

    private const double Body = 4.5;
    private const double Mark = 3.0;
    private const double Seen = 1.12;
    private const double Apart = 1.25;

    private static readonly Rgb _blueLight = ColorMath.ParseHex("#0F6CBD");
    private static readonly Rgb _blueDark = ColorMath.ParseHex("#479EF5");

    public static IReadOnlyDictionary<string, Rgb> Resolve(ColorTheme theme, Rgb? windowsAccent = null)
    {
        var dark = theme.Dark;
        var result = new Dictionary<string, Rgb>(StringComparer.Ordinal);
        Apply(result, theme.Colors);

        void Floor(string token, double ratio, params ReadOnlySpan<string> on)
        {
            Span<Rgb> backgrounds = stackalloc Rgb[on.Length];
            var count = 0;
            foreach (var background in on)
            {
                if (result.TryGetValue(background, out var color))
                {
                    backgrounds[count++] = color;
                }
            }

            result[token] = ColorMath.EnsureContrast(result[token], ratio, backgrounds[..count]);
        }

        var basis = dark ? SalandualDark : SalandualLight;
        foreach (var token in (ReadOnlySpan<string>)[WindowBackground, PanelBackground, Text, Danger, Warning, Success])
        {
            result.TryAdd(token, ColorMath.ParseHex(basis[token]));
        }

        var accent = theme.Accent switch
        {
            ThemeAccent.Windows when windowsAccent is { } w => dark ? ColorMath.Lighten(w, 0.35) : w,
            ThemeAccent.Custom when ColorMath.TryParseHex(theme.CustomAccent, out var c) => c,
            _ => result.TryGetValue(Accent, out var own) ? own : dark ? _blueDark : _blueLight,
        };
        result[Accent] = accent;
        Floor(Accent, Mark, PanelBackground, WindowBackground);
        accent = result[Accent];

        var surface = result[PanelBackground];
        var window = result[WindowBackground];
        var salandual = theme.Origin == ThemeOrigin.BuiltIn;
        result[WarningBackground] = Soft(dark, salandual, "#FFF4CE", "#433519", result[Warning], surface);
        result[DangerBackground] = Soft(dark, salandual, "#FDE7E9", "#442726", result[Danger], surface);
        result[InfoBackground] = Soft(dark, salandual, "#EAF3FC", "#1E3247", accent, surface);
        result[SuccessBackground] = Soft(dark, salandual, "#DFF6DD", "#1F3A1D", result[Success], surface);

        Floor(Text, Body, PanelBackground, WindowBackground, ColumnHeader, Hover, Pressed, WarningBackground, DangerBackground, InfoBackground, SuccessBackground);
        Floor(Danger, Body, PanelBackground, WindowBackground, DangerBackground);
        Floor(Warning, Body, PanelBackground, WindowBackground, WarningBackground);
        Floor(Success, Body, PanelBackground, WindowBackground, SuccessBackground);

        var text = result[Text];
        result.TryAdd(SecondaryText, ColorMath.Mix(text, surface, 0.32));
        result.TryAdd(TertiaryText, ColorMath.Mix(text, surface, 0.50));
        result.TryAdd(ColumnHeader, ColorMath.Mix(surface, window, 0.5));
        result.TryAdd(InformationLine, result[ColumnHeader]);
        result.TryAdd(InformationLineText, text);
        result.TryAdd(Hover, ColorMath.Mix(surface, text, dark ? 0.08 : 0.06));
        result.TryAdd(Pressed, ColorMath.Mix(surface, text, dark ? 0.12 : 0.10));
        result.TryAdd(Border, ColorMath.Mix(surface, text, dark ? 0.14 : 0.12));
        result.TryAdd(BorderStrong, ColorMath.Mix(surface, text, dark ? 0.24 : 0.22));
        result.TryAdd(SelectionBackground, ColorMath.Mix(surface, accent, dark ? 0.22 : 0.16));
        result.TryAdd(SelectedText, ColorMath.Mix(text, accent, dark ? 0.70 : 0.75));
        result.TryAdd(FocusOutline, accent);

        Floor(SecondaryText, Body, PanelBackground, WindowBackground, ColumnHeader, Hover, WarningBackground, DangerBackground, InfoBackground, SuccessBackground);
        Floor(TertiaryText, Body, PanelBackground, WindowBackground, ColumnHeader);
        Floor(InformationLineText, Body, InformationLine);
        Floor(SelectedText, Body, SelectionBackground);
        Floor(FocusOutline, Mark, PanelBackground, SelectionBackground);

        var info = result[InformationLine];
        var infoText = result[InformationLineText];
        result[InformationLineDimText] = ColorMath.EnsureContrast(ColorMath.Mix(infoText, info, 0.25), Body, info);
        result[InformationLineHover] = ColorMath.Mix(info, infoText, 0.14);
        result[InformationLineAccent] = ColorMath.EnsureContrast(accent, Body, info);
        result[InformationLineDanger] = ColorMath.EnsureContrast(result[Danger], Body, info);
        result[InformationLineWarning] = ColorMath.EnsureContrast(result[Warning], Body, info);

        result.TryAdd(SyntaxKey, accent);
        result.TryAdd(SyntaxKeyword, accent);
        result.TryAdd(SyntaxHeading, accent);
        result.TryAdd(SyntaxLink, accent);
        result.TryAdd(SyntaxString, result[Success]);
        result.TryAdd(SyntaxNumber, result[Warning]);
        result.TryAdd(SyntaxComment, result[TertiaryText]);
        result.TryAdd(SyntaxAttribute, result[SyntaxNumber]);
        result.TryAdd(SyntaxFunction, result[SyntaxKey]);
        result.TryAdd(SyntaxPunctuation, result[SecondaryText]);
        result.TryAdd(SyntaxTag, result[SyntaxKey]);
        result.TryAdd(SyntaxBackground, surface);
        result.TryAdd(SyntaxText, text);
        result.TryAdd(CompressedFiles, result[SyntaxKey]);
        result.TryAdd(EncryptedFiles, result[Success]);

        Floor(CompressedFiles, Body, PanelBackground);
        Floor(EncryptedFiles, Body, PanelBackground);
        Floor(SyntaxText, Body, SyntaxBackground);

        result.TryAdd(HexZero, result[TertiaryText]);
        result.TryAdd(HexText, text);
        result.TryAdd(HexControl, result[SyntaxNumber]);
        result.TryAdd(HexHigh, result[SyntaxKey]);
        result.TryAdd(HexFF, result[Danger]);
        result.TryAdd(ViewerOffsets, result[TertiaryText]);
        result.TryAdd(ViewerLineNumbers, result[ViewerOffsets]);
        result.TryAdd(ViewerCaret, accent);
        var warmMatch = dark ? ColorMath.Mix(surface, new Rgb(0xC8, 0x9B, 0x1E), 0.44) : ColorMath.Mix(surface, new Rgb(0xFF, 0xE0, 0x7A), 0.56);
        var warmCurrent = dark ? ColorMath.Mix(surface, new Rgb(0xD9, 0x8C, 0x10), 0.75) : ColorMath.Mix(surface, new Rgb(0xF7, 0xA8, 0x32), 0.75);
        result.TryAdd(ViewerMatch, warmMatch);
        result.TryAdd(ViewerCurrentMatch, warmCurrent);

        Floor(HexZero, Mark, PanelBackground);
        Floor(ViewerOffsets, Mark, PanelBackground);
        Floor(ViewerLineNumbers, Mark, PanelBackground);

        var code = result[SyntaxBackground];
        Rgb[] inks = [result[Text], result[SyntaxText]];
        var match = Highlight(result[ViewerMatch], inks, surface);
        var current = Highlight(result[ViewerCurrentMatch], inks, surface);
        if (ColorMath.Contrast(match, surface) < Seen)
        {
            match = Highlight(warmMatch, inks, surface);
        }

        if (ColorMath.Contrast(current, surface) < Seen || ColorMath.Contrast(current, match) < Apart)
        {
            current = Highlight(warmCurrent, inks, surface);
        }

        for (var step = 0; step < 10 && ColorMath.Contrast(current, match) < Apart && ColorMath.Contrast(match, surface) > Seen; step++)
        {
            match = ColorMath.Mix(match, surface, 0.08);
        }

        (result[ViewerMatch], result[ViewerCurrentMatch]) = (match, current);

        result[TextSelection] = Highlight(result[SelectionBackground], inks, surface);

        result.TryAdd(CompareRemoved, ColorMath.Mix(code, result[Danger], dark ? 0.24 : 0.13));
        result.TryAdd(CompareAdded, ColorMath.Mix(code, result[Success], dark ? 0.24 : 0.13));
        result.TryAdd(CompareChangedText, dark ? ColorMath.Mix(code, new Rgb(0xC8, 0x9B, 0x1E), 0.48) : ColorMath.Mix(code, new Rgb(0xF0, 0xC0, 0x30), 0.62));
        result.TryAdd(CompareMinorText, dark ? ColorMath.Mix(code, new Rgb(0x4A, 0x7F, 0xE0), 0.42) : ColorMath.Mix(code, new Rgb(0x4A, 0x7F, 0xE0), 0.30));
        result.TryAdd(CompareCopied, ColorMath.Mix(code, accent, dark ? 0.22 : 0.10));
        Rgb[] codeInk = [result[SyntaxText]];
        foreach (var token in (ReadOnlySpan<string>)[CompareRemoved, CompareAdded, CompareChangedText, CompareMinorText, CompareCopied])
        {
            result[token] = Highlight(result[token], codeInk, code);
        }

        result[FocusOutlineInactive] = salandual ? ColorMath.ParseHex(dark ? "#5A5A5A" : "#B0B0B0") : ColorMath.Mix(surface, text, 0.35);
        result[OnAccent] = ColorMath.OnColor(accent);
        result[CompareMinorMark] = ColorMath.Mix(result[CompareMinorText], dark ? new Rgb(0xA9, 0xC1, 0xF5) : new Rgb(0x3A, 0x66, 0xC4), 0.7);
        result[AccentHover] = dark ? ColorMath.Lighten(accent, 0.12) : ColorMath.Mix(accent, new Rgb(0, 0, 0), 0.12);
        return result;
    }

    private static Rgb Highlight(Rgb background, Rgb[] inks, Rgb under)
    {
        foreach (var ratio in (ReadOnlySpan<double>)[Body, 3.5, Mark])
        {
            var candidate = Soften(background, inks, under, ratio);
            if (ColorMath.Contrast(candidate, under) >= Seen)
            {
                return candidate;
            }
        }

        return Soften(background, inks, under, Mark);
    }

    private static Rgb Soften(Rgb background, Rgb[] inks, Rgb under, double ratio)
    {
        for (var step = 0; step <= 50; step++)
        {
            var candidate = ColorMath.Mix(background, under, step / 50.0);
            var readable = true;
            foreach (var ink in inks)
            {
                readable &= ColorMath.Contrast(ink, candidate) >= ratio;
            }

            if (readable)
            {
                return candidate;
            }
        }

        return under;
    }

    private static Rgb Soft(bool dark, bool salandual, string light, string darkHex, Rgb color, Rgb surface)
        => salandual ? ColorMath.ParseHex(dark ? darkHex : light) : ColorMath.Mix(surface, color, dark ? 0.15 : 0.14);

    public static string TokenFor(Salandual.Core.Viewer.SyntaxKind kind) => kind switch
    {
        Salandual.Core.Viewer.SyntaxKind.Key => SyntaxKey,
        Salandual.Core.Viewer.SyntaxKind.Tag => SyntaxTag,
        Salandual.Core.Viewer.SyntaxKind.String or Salandual.Core.Viewer.SyntaxKind.Code => SyntaxString,
        Salandual.Core.Viewer.SyntaxKind.Number or Salandual.Core.Viewer.SyntaxKind.Marker => SyntaxNumber,
        Salandual.Core.Viewer.SyntaxKind.Keyword or Salandual.Core.Viewer.SyntaxKind.Escape => SyntaxKeyword,
        Salandual.Core.Viewer.SyntaxKind.Punctuation or Salandual.Core.Viewer.SyntaxKind.Quote => SyntaxPunctuation,
        Salandual.Core.Viewer.SyntaxKind.Comment => SyntaxComment,
        Salandual.Core.Viewer.SyntaxKind.Attribute => SyntaxAttribute,
        Salandual.Core.Viewer.SyntaxKind.Heading => SyntaxHeading,
        Salandual.Core.Viewer.SyntaxKind.Link => SyntaxLink,
        Salandual.Core.Viewer.SyntaxKind.Function => SyntaxFunction,
        _ => SyntaxText,
    };

    public static CodeSurfaceColors? OwnSurface(ColorTheme theme, IReadOnlyDictionary<string, Rgb> resolved)
    {
        var bg = resolved[SyntaxBackground];
        if (theme.CodeBackground != CodeBackground.Own || bg == resolved[PanelBackground])
        {
            return null;
        }

        var fg = resolved[SyntaxText];
        var accent = resolved[Accent];
        var darkCode = ColorMath.Luminance(bg) < 0.2;
        var selection = Highlight(ColorMath.Mix(bg, accent, darkCode ? 0.35 : 0.22), [fg], bg);
        var lineNumbers = ColorMath.EnsureContrast(ColorMath.Mix(bg, fg, 0.45), Mark, bg);
        return new CodeSurfaceColors(bg, fg, ColorMath.Mix(bg, fg, darkCode ? 0.07 : 0.05), lineNumbers, selection);
    }

    private static void Apply(Dictionary<string, Rgb> target, IReadOnlyDictionary<string, string> colors)
    {
        foreach (var (token, hex) in colors)
        {
            if (IsKnown(token) && ColorMath.TryParseHex(hex, out var c))
            {
                target[token] = c;
            }
        }
    }
}
