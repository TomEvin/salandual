// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ColorTheme.cs

namespace Salandual.Core.Settings;

public enum ThemeOrigin
{
    BuiltIn,

    Shipped,

    Mine,

    VsCode,
}

public enum ThemeAccent { Theme, Windows, Custom }

public enum CodeBackground { Panels, Own }

public sealed record CodeSurfaceColors(Rgb Background, Rgb Text, Rgb CurrentLine, Rgb LineNumbers, Rgb Selection);

public sealed record ColorTheme
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public bool Dark { get; set; }

    public ThemeOrigin Origin { get; set; } = ThemeOrigin.Mine;

    public Dictionary<string, string> Colors { get; set; } = [];

    public ThemeAccent Accent { get; set; } = ThemeAccent.Theme;

    public string? CustomAccent { get; set; }

    public CodeBackground CodeBackground { get; set; } = CodeBackground.Panels;

    public string? CopiedFrom { get; set; }

    public DateTime? CopiedOn { get; set; }

    public string? ImportedFrom { get; set; }

    public string? SourcePath { get; set; }

    public BackgroundImageSettings Background { get; set; } = new();

    public Dictionary<string, string> Original { get; set; } = [];

    public bool ReadOnly => Origin != ThemeOrigin.Mine;

    internal ColorTheme Normalize()
    {
        var background = (Background ?? new()).Normalize();
        return this with
        {
            Id = (Id ?? "").Trim(),
            Name = string.IsNullOrWhiteSpace(Name) ? Id ?? "" : Name.Trim(),
            Origin = Origin is ThemeOrigin.VsCode ? ThemeOrigin.VsCode : ThemeOrigin.Mine,
            Colors = Known(Colors),
            Original = Known(Original),
            Background = background with { Path = ThemeFiles.SafeFileName(background.Path) },
            Accent = Enum.IsDefined(Accent) ? Accent : ThemeAccent.Theme,
            CustomAccent = ColorMath.TryParseHex(CustomAccent, out var a) ? ColorMath.ToHex(a) : null,
            CodeBackground = Enum.IsDefined(CodeBackground) ? CodeBackground : CodeBackground.Panels,
            CopiedFrom = string.IsNullOrWhiteSpace(CopiedFrom) ? null : CopiedFrom.Trim(),
            ImportedFrom = string.IsNullOrWhiteSpace(ImportedFrom) ? null : ImportedFrom.Trim(),
            SourcePath = string.IsNullOrWhiteSpace(SourcePath) ? null : SourcePath,
        };
    }

    private static Dictionary<string, string> Known(Dictionary<string, string>? source)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (token, value) in source ?? [])
        {
            if (ColorTokens.IsKnown(token) && ColorMath.TryParseHex(value, out var c))
            {
                colors[token] = ColorMath.ToHex(c);
            }
        }

        return colors;
    }
}
