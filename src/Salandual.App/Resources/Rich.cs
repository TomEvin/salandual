// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Resources/Rich.cs

using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls.Documents;

namespace Salandual.App.Resources;

internal static partial class Rich
{
    public static void Add(InlineCollection inlines, string format, Func<string, Inline>? plain, params Inline[] args)
        => inlines.AddRange(Build(format, plain, args));

    public static Inline[] Build(string format, Func<string, Inline>? plain, params Inline[] args)
    {
        var parts = new List<Inline>();
        var at = 0;
        foreach (Match m in Placeholder().Matches(format))
        {
            AddPlain(format[at..m.Index]);
            parts.Add(args[int.Parse(m.Groups[1].ValueSpan, provider: CultureInfo.InvariantCulture)]);
            at = m.Index + m.Length;
        }

        AddPlain(format[at..]);
        return [.. parts];

        void AddPlain(string text)
        {
            if (text.Length > 0)
            {
                parts.Add(plain?.Invoke(text) ?? new Run(text));
            }
        }
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();
}
