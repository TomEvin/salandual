// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Diagnostics/PersonalPaths.cs

namespace Salandual.Core.Diagnostics;

public readonly record struct TextPart(string Text, bool Hidden);

public static class PersonalPaths
{
    public const string Placeholder = "%USERPROFILE%";

    public static IReadOnlyList<TextPart> Split(string text, string userFolder)
    {
        var folder = userFolder.TrimEnd('\\', '/');
        if (folder.Length < 3)
        {
            return [new TextPart(text, false)];
        }

        var parts = new List<TextPart>();
        var start = 0;
        while (start < text.Length)
        {
            var at = text.IndexOf(folder, start, StringComparison.OrdinalIgnoreCase);
            while (at >= 0 && at + folder.Length < text.Length && char.IsLetterOrDigit(text[at + folder.Length]))
            {
                at = text.IndexOf(folder, at + 1, StringComparison.OrdinalIgnoreCase);
            }

            if (at < 0)
            {
                break;
            }

            if (at > start)
            {
                parts.Add(new TextPart(text[start..at], false));
            }

            parts.Add(new TextPart(Placeholder, true));
            start = at + folder.Length;
        }

        if (start < text.Length || parts.Count == 0)
        {
            parts.Add(new TextPart(text[start..], false));
        }

        return parts;
    }

    public static string Hide(string text, string userFolder) => string.Concat(Split(text, userFolder).Select(p => p.Text));
}
