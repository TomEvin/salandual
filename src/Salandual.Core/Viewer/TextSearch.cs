// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/TextSearch.cs

using System.Text.RegularExpressions;

namespace Salandual.Core.Viewer;

public sealed record TextQuery(string Text, bool MatchCase, bool WholeWords, bool Regex)
{
    public Regex? Pattern(out string? error)
    {
        error = null;
        if (Text.Length == 0)
        {
            return null;
        }

        var body = Regex ? Text : System.Text.RegularExpressions.Regex.Escape(Text);
        if (WholeWords)
        {
            body = $@"(?<!\w)(?:{body})(?!\w)";
        }

        var options = RegexOptions.Multiline | RegexOptions.CultureInvariant | (MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        try
        {
            return new Regex(body, options, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return null;
        }
    }
}

public static class TextSearch
{
    public static List<(int Start, int Length)> All(string text, Regex pattern, int limit = 100_000)
    {
        var result = new List<(int, int)>();
        try
        {
            for (var m = pattern.Match(text); m.Success && result.Count < limit; m = m.NextMatch())
            {
                if (m.Length > 0)
                {
                    result.Add((m.Index, m.Length));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return result;
    }

    public static int Next(IReadOnlyList<(int Start, int Length)> matches, int caret, bool forward)
    {
        if (matches.Count == 0)
        {
            return -1;
        }

        if (forward)
        {
            for (var i = 0; i < matches.Count; i++)
            {
                if (matches[i].Start >= caret)
                {
                    return i;
                }
            }

            return 0;
        }

        for (var i = matches.Count - 1; i >= 0; i--)
        {
            if (matches[i].Start < caret)
            {
                return i;
            }
        }

        return matches.Count - 1;
    }

    public static string Replacement(Regex pattern, string text, (int Start, int Length) match, string replacement, bool regex)
    {
        if (!regex)
        {
            return replacement;
        }

        var m = pattern.Match(text, match.Start);
        return m.Success && m.Index == match.Start ? m.Result(replacement) : replacement;
    }

    public static (string Text, int Count) ReplaceAll(string text, Regex pattern, string replacement, bool regex, int from = 0, int to = int.MaxValue)
    {
        var count = 0;
        var end = Math.Min(to, text.Length);
        var result = pattern.Replace(text, m =>
        {
            if (m.Index < from || m.Index + m.Length > end || m.Length == 0)
            {
                return m.Value;
            }

            count++;
            return regex ? m.Result(replacement) : replacement;
        });
        return (result, count);
    }
}
