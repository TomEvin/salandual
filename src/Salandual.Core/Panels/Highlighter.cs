// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/Highlighter.cs

using Salandual.Core.Listing;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Core.Panels;

public sealed class Highlighter
{
    private readonly (HighlightRule Rule, FileMask? Mask)[] _rules;

    public Highlighter(IEnumerable<HighlightRule> rules)
        => _rules = [.. rules.Where(r => r.On).Select(r => (r, string.IsNullOrWhiteSpace(r.Masks) ? null : FileMask.FromHistory(r.Masks)))];

    public static Highlighter None { get; } = new([]);

    public bool IsEmpty => _rules.Length == 0;

    public HighlightRule? Match(in FileEntry entry)
    {
        foreach (var (rule, mask) in _rules)
        {
            if (Matches(rule, mask, entry))
            {
                return rule;
            }
        }

        return null;
    }

    public static bool Matches(HighlightRule rule, in FileEntry entry)
        => Matches(rule, string.IsNullOrWhiteSpace(rule.Masks) ? null : FileMask.FromHistory(rule.Masks), entry);

    private static bool Matches(HighlightRule rule, FileMask? mask, in FileEntry entry)
    {
        if (rule.For == (entry.IsDirectory ? RuleTarget.Files : RuleTarget.Folders))
        {
            return false;
        }

        var attributes = entry.Attributes;
        if ((attributes & rule.MustNotHave) != 0)
        {
            return false;
        }

        if (rule.MustHave != 0 && (rule.AnyOf ? (attributes & rule.MustHave) == 0 : (attributes & rule.MustHave) != rule.MustHave))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(rule.Masks) || (mask is not null && mask.IsMatch(entry.Name));
    }

    public static Rgb? Colour(NameColour colour, Rgb background, Func<string, Rgb> token, string? ownLight = null, string? ownDark = null)
    {
        var dark = ColorMath.Luminance(background) < 0.2;
        Rgb shade;
        switch (colour)
        {
            case NameColour.Normal:
                return null;
            case NameColour.Dimmed:
                return token(ColorTokens.SecondaryText);
            case NameColour.Blue:
                return token(ColorTokens.CompressedFiles);
            case NameColour.Green:
                return token(ColorTokens.EncryptedFiles);
            case NameColour.Own:
                if (!ColorMath.TryParseHex(dark ? ownDark : ownLight, out shade))
                {
                    return null;
                }

                return shade;
            default:
                shade = ColorMath.ParseHex(Shades(colour) is var (l, d) ? (dark ? d : l) : "#808080");
                break;
        }

        return ColorMath.EnsureContrast(shade, 4.5, background);
    }

    public static (string Light, string Dark)? Shades(NameColour colour) => colour switch
    {
        NameColour.Blue => ("#0F6CBD", "#479EF5"),
        NameColour.Green => ("#0F7B0F", "#6CCB5F"),
        NameColour.Purple => ("#7E57C2", "#B39DDB"),
        NameColour.Orange => ("#B85C00", "#F7A24A"),
        NameColour.Red => ("#C42B1C", "#FF99A4"),
        NameColour.Teal => ("#00838F", "#4DD0E1"),
        NameColour.Pink => ("#C2185B", "#F06292"),
        NameColour.Grey => ("#6E6E6E", "#9E9E9E"),
        NameColour.Yellow => ("#A68A00", "#F2D64B"),
        NameColour.Amber => ("#B07A00", "#FFC24D"),
        NameColour.Gold => ("#9C7A1A", "#E6C35C"),
        NameColour.Brown => ("#795548", "#BCA59A"),
        NameColour.Coral => ("#D2563C", "#FF9E85"),
        NameColour.Maroon => ("#8E2433", "#E07A88"),
        NameColour.Rose => ("#B03A64", "#F48FB1"),
        NameColour.Magenta => ("#A0168F", "#E57FD6"),
        NameColour.Plum => ("#6A2C70", "#C792CC"),
        NameColour.Violet => ("#5E35B1", "#9F8BE8"),
        NameColour.Indigo => ("#3949AB", "#8C9EFF"),
        NameColour.Navy => ("#1A3A7A", "#7FA6E6"),
        NameColour.Sky => ("#0277BD", "#81D4FA"),
        NameColour.Cyan => ("#007C99", "#5CD6F0"),
        NameColour.Aqua => ("#00897B", "#64DFCF"),
        NameColour.Mint => ("#2E7D5B", "#7FDCB0"),
        NameColour.Forest => ("#1B5E20", "#81C784"),
        NameColour.Lime => ("#5F7F00", "#C0E35A"),
        NameColour.Olive => ("#6B6B1F", "#C7C77A"),
        NameColour.Sand => ("#8A7350", "#D9C59E"),
        NameColour.Peach => ("#B5653A", "#FFC09A"),
        NameColour.Lavender => ("#7C6BB0", "#C5B8F0"),
        NameColour.Slate => ("#4F6475", "#A3B5C4"),
        NameColour.Charcoal => ("#3C3C3C", "#C8C8C8"),
        _ => null,
    };
}
