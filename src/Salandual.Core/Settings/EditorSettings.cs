// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/EditorSettings.cs

using Salandual.Core.Listing;
using Salandual.Core.Viewer;

namespace Salandual.Core.Settings;

public enum EditorClose { CtrlF4, CtrlF4OrEsc }

public enum EditorDiskChange { Ask, Reload }

public enum IndentStyle { Spaces2, Spaces4, Tabs }

public enum LineEnding { CrLf, Lf }

public enum FinalNewline { Keep, Always }

public enum FormatIndent { AsFile, Spaces2, Spaces4, Tabs }

public enum PreviewKind { Markdown, Notebook, Svg, Html }

public sealed record PreviewSpot
{
    public PreviewKind Kind { get; set; }

    public bool Below { get; set; }

    public bool Before { get; set; }

    public double Share { get; set; } = 0.5;
}

public sealed record EditorMask
{
    public string Mask { get; set; } = "*.*";

    public SyntaxLanguage Colors { get; set; } = SyntaxLanguage.None;

    public long? ColorsUpToMB { get; set; }

    public long? LargestMB { get; set; }

    public string? Program { get; set; }
}

public sealed record EditorSettings
{
    public static readonly SyntaxLanguage[] ColorChoices = [SyntaxLanguage.Json, SyntaxLanguage.Yaml, SyntaxLanguage.Xml, SyntaxLanguage.Html, SyntaxLanguage.Markdown, SyntaxLanguage.None];

    public static List<EditorMask> DefaultMasks() =>
    [
        new() { Mask = "*.json;*.jsonc;*.ipynb", Colors = SyntaxLanguage.Json },
        new() { Mask = "*.yaml;*.yml", Colors = SyntaxLanguage.Yaml },
        new() { Mask = "*.xml;*.xaml;*.axaml;*.csproj;*.slnx;*.props;*.targets;*.config;*.resx;*.svg", Colors = SyntaxLanguage.Xml },
        new() { Mask = "*.html;*.htm;*.xhtml", Colors = SyntaxLanguage.Html },
        new() { Mask = "*.md;*.markdown", Colors = SyntaxLanguage.Markdown },
        new() { Mask = "*.*" },
    ];

    public static long DefaultColorsUpToMB(SyntaxLanguage colors) => colors == SyntaxLanguage.Yaml ? 10 : 20;

    public const long DefaultLargestMB = 50;

    public List<EditorMask> Masks { get; set; } = DefaultMasks();

    public EditorClose Close { get; set; } = EditorClose.CtrlF4;

    public EditorDiskChange DiskChange { get; set; } = EditorDiskChange.Ask;

    public IndentStyle Indent { get; set; } = IndentStyle.Spaces4;

    public bool Wrap { get; set; }

    public bool LineNumbers { get; set; } = true;

    public bool HighlightCurrentLine { get; set; } = true;

    public bool Strip { get; set; } = true;

    public bool ShowSpaces { get; set; }

    public string NewEncoding { get; set; } = TextEncodings.Utf8;

    public LineEnding NewLineEnding { get; set; } = LineEnding.CrLf;

    public bool TrimOnSave { get; set; }

    public FinalNewline FinalNewline { get; set; } = FinalNewline.Keep;

    public bool Backup { get; set; } = true;

    public bool Check { get; set; } = true;

    public bool Folding { get; set; } = true;

    public FormatIndent FormatIndent { get; set; } = FormatIndent.AsFile;

    public List<PreviewKind> PreviewOpens { get; set; } = [PreviewKind.Markdown];

    public bool PreviewBelow { get; set; }

    public List<PreviewSpot> PreviewSpots { get; set; } = [];

    public bool PreviewSync { get; set; } = true;

    public long PreviewLiveUpToMB { get; set; } = 1;

    public double WindowWidth { get; set; } = 1000;

    public double WindowHeight { get; set; } = 720;

    public bool WindowMaximized { get; set; }

    public int RowFor(string name)
    {
        for (var i = 0; i < Masks.Count; i++)
        {
            if (FileMask.Parse(Masks[i].Mask)?.IsMatch(name) == true)
            {
                return i;
            }
        }

        return -1;
    }

    public string? ProgramFor(string name) => RowFor(name) is >= 0 and var row ? Masks[row].Program : null;

    public IReadOnlyList<string> ExternalPrograms() => [.. Masks.Select(m => m.Program).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];

    public PreviewSpot SpotFor(PreviewKind kind) => PreviewSpots.FirstOrDefault(s => s.Kind == kind) ?? new PreviewSpot { Kind = kind, Below = PreviewBelow };

    public SyntaxLanguage ColorsFor(string name) => RowFor(name) is >= 0 and var row ? Masks[row].Colors : SyntaxLanguage.None;

    public long ColorsUpToBytes(string name)
    {
        var row = RowFor(name);
        if (row < 0 || Masks[row].Colors == SyntaxLanguage.None)
        {
            return 0;
        }

        return (Masks[row].ColorsUpToMB ?? DefaultColorsUpToMB(Masks[row].Colors)) * 1024 * 1024;
    }

    public long LargestBytes(string name)
    {
        var row = RowFor(name);
        var mb = row >= 0 && Masks[row].LargestMB is { } set ? set : DefaultLargestMB;
        return mb <= 0 ? 0 : mb * 1024 * 1024;
    }

    internal EditorSettings Normalize() => this with
    {
        Masks = Masks is { Count: > 0 }
            ? [.. Masks.Where(m => m is not null).Select(m => m with
            {
                Mask = string.IsNullOrWhiteSpace(m.Mask) ? "*.*" : m.Mask.Trim(),
                Colors = ColorChoices.Contains(m.Colors) ? m.Colors : SyntaxLanguage.None,
                ColorsUpToMB = m.ColorsUpToMB is { } c ? Math.Clamp(c, 1, 4096) : null,
                LargestMB = m.LargestMB is { } l ? Math.Clamp(l, 0, 65536) : null,
                Program = string.IsNullOrWhiteSpace(m.Program) ? null : m.Program.Trim(),
            })]
            : DefaultMasks(),
        Close = Enum.IsDefined(Close) ? Close : EditorClose.CtrlF4,
        DiskChange = Enum.IsDefined(DiskChange) ? DiskChange : EditorDiskChange.Ask,
        Indent = Enum.IsDefined(Indent) ? Indent : IndentStyle.Spaces4,
        NewEncoding = TextEncodings.Find(NewEncoding)?.Id ?? TextEncodings.Utf8,
        NewLineEnding = Enum.IsDefined(NewLineEnding) ? NewLineEnding : LineEnding.CrLf,
        FinalNewline = Enum.IsDefined(FinalNewline) ? FinalNewline : FinalNewline.Keep,
        FormatIndent = Enum.IsDefined(FormatIndent) ? FormatIndent : FormatIndent.AsFile,
        PreviewOpens = PreviewOpens is null ? [PreviewKind.Markdown] : [.. PreviewOpens.Where(Enum.IsDefined).Distinct()],
        PreviewSpots = PreviewSpots is null ? [] : [.. PreviewSpots.Where(s => s is not null && Enum.IsDefined(s.Kind)).DistinctBy(s => s.Kind).Select(s => s with { Share = Math.Clamp(double.IsFinite(s.Share) ? s.Share : 0.5, 0.2, 0.8) })],
        PreviewLiveUpToMB = Math.Clamp(PreviewLiveUpToMB, 0, 1024),
        WindowWidth = Math.Clamp(double.IsFinite(WindowWidth) ? WindowWidth : 1000, 520, 10000),
        WindowHeight = Math.Clamp(double.IsFinite(WindowHeight) ? WindowHeight : 720, 300, 10000),
    };
}
