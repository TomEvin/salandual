// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Search/NameRule.cs

using System.Text;
using Salandual.Core.Listing;

namespace Salandual.Core.Search;

public static class NameRule
{
    public static FileMask ToMask(string? named)
    {
        if (string.IsNullOrWhiteSpace(named))
        {
            return FileMask.All;
        }

        var result = new StringBuilder();
        var part = new StringBuilder();
        for (var i = 0; i < named.Length; i++)
        {
            var c = named[i];
            if (c == ';' && i + 1 < named.Length && named[i + 1] == ';')
            {
                part.Append(";;");
                i++;
            }
            else if (c is ';' or '|')
            {
                Flush();
                result.Append(c);
            }
            else
            {
                part.Append(c);
            }
        }

        Flush();
        return FileMask.Parse(result.ToString()) ?? FileMask.All;

        void Flush()
        {
            var mask = part.ToString();
            var trimmed = mask.Trim();
            result.Append(trimmed.Length > 0 && trimmed.IndexOfAny(['*', '?', '.']) < 0 ? "*" + trimmed + "*" : mask);
            part.Clear();
        }
    }
}

public sealed class SkipRules
{
    private readonly List<string> _names = [];
    private readonly List<string> _atRoot = [];
    private readonly List<string> _endings = [];
    private readonly List<string> _paths = [];

    public SkipRules(IEnumerable<string> rules)
    {
        foreach (var raw in rules)
        {
            var rule = raw.Trim().TrimEnd('\\');
            if (rule.Length == 0)
            {
                continue;
            }

            if (rule.StartsWith(@"\\", StringComparison.Ordinal) || (rule.Length >= 2 && rule[1] == ':'))
            {
                _paths.Add(rule);
            }
            else if (rule[0] == '\\')
            {
                _atRoot.Add(rule[1..]);
            }
            else if (rule.Contains('\\'))
            {
                _endings.Add("\\" + rule);
            }
            else
            {
                _names.Add(rule);
            }
        }
    }

    public bool IsSkipped(string path, string name, string? parent, string? root)
    {
        foreach (var n in _names)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (parent is not null && root is not null && string.Equals(parent.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            foreach (var n in _atRoot)
            {
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        foreach (var ending in _endings)
        {
            if (path.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var p in _paths)
        {
            if (string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
