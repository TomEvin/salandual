// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/QuickOpen/QuickOpenQuery.cs

namespace Salandual.Core.QuickOpen;

public enum QuickScope { All, Commands, Files, Path, Tabs, Settings }

public sealed record QuickOpenQuery(QuickScope Scope, string Text)
{
    public static QuickOpenQuery Parse(string? typed)
    {
        var text = (typed ?? "").TrimStart();
        if (text.Length == 0)
        {
            return new(QuickScope.All, "");
        }

        return text[0] switch
        {
            '>' => new(QuickScope.Commands, text[1..].Trim()),
            '/' => new(QuickScope.Files, text[1..].Trim()),
            '@' => new(QuickScope.Tabs, text[1..].Trim()),
            ',' => new(QuickScope.Settings, text[1..].Trim()),
            '\\' or '~' => new(QuickScope.Path, text),
            _ when text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':' => new(QuickScope.Path, text),
            _ => new(QuickScope.All, text.Trim()),
        };
    }

    public static string PrefixOf(QuickScope scope) => scope switch
    {
        QuickScope.Commands => ">",
        QuickScope.Files => "/",
        QuickScope.Tabs => "@",
        QuickScope.Settings => ",",
        _ => "",
    };

    public string In(QuickScope scope) => scope == QuickScope.Path
        ? (Scope == QuickScope.Path ? Text : "\\")
        : PrefixOf(scope) + (Scope == QuickScope.Path ? "" : Text);
}
