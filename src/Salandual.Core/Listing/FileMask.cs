// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Listing/FileMask.cs

namespace Salandual.Core.Listing;

public sealed class FileMask
{
    public const string RegexPrefix = "regex:";

    private readonly string[] _include;
    private readonly string[] _exclude;
    private readonly System.Text.RegularExpressions.Regex? _regex;

    private FileMask(string text, string[] include, string[] exclude, System.Text.RegularExpressions.Regex? regex = null)
    {
        Text = text;
        _include = include;
        _exclude = exclude;
        _regex = regex;
    }

    public string Text { get; }

    public bool IsRegex => _regex is not null;

    public static FileMask? ParseRegex(string? text, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var regex = new System.Text.RegularExpressions.Regex("^(?:" + text + ")$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(200));
            return new FileMask(RegexPrefix + text, [], [], regex);
        }
        catch (ArgumentException ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    public static FileMask? FromHistory(string? text)
        => text is not null && text.StartsWith(RegexPrefix, StringComparison.Ordinal) ? ParseRegex(text[RegexPrefix.Length..], out _) : Parse(text);

    public static FileMask All { get; } = Parse("*.*")!;

    public static FileMask? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var bar = text.IndexOf('|');
        var include = Split(bar < 0 ? text : text[..bar]);
        var exclude = bar < 0 ? [] : Split(text[(bar + 1)..]);
        if (include.Length == 0)
        {
            if (exclude.Length == 0)
            {
                return null;
            }

            include = ["*"];
        }

        return new FileMask(text.Trim(), include, exclude);
    }

    public bool IsMatch(ReadOnlySpan<char> name)
    {
        if (_regex is not null)
        {
            try
            {
                return _regex.IsMatch(name);
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                return false;
            }
        }

        foreach (var mask in _exclude)
        {
            if (MatchOne(name, mask))
            {
                return false;
            }
        }

        foreach (var mask in _include)
        {
            if (MatchOne(name, mask))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] Split(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != ';')
            {
                current.Append(text[i]);
            }
            else if (i + 1 < text.Length && text[i + 1] == ';')
            {
                current.Append(';');
                i++;
            }
            else
            {
                Add();
            }
        }

        Add();
        return [.. parts];

        void Add()
        {
            var mask = current.ToString().Trim();
            if (mask.Length > 0)
            {
                parts.Add(mask);
            }

            current.Clear();
        }
    }

    private static bool MatchOne(ReadOnlySpan<char> name, ReadOnlySpan<char> mask)
    {
        if (Glob(name, mask))
        {
            return true;
        }

        var hasExtension = name.LastIndexOf('.') is var dot && dot > 0 && dot < name.Length - 1;
        if (hasExtension)
        {
            return false;
        }

        if (mask.EndsWith(".*"))
        {
            return Glob(name, mask[..^2]);
        }

        return mask.Length > 1 && mask[^1] == '.' && Glob(name.TrimEnd('.'), mask[..^1]);
    }

    private static bool Glob(ReadOnlySpan<char> name, ReadOnlySpan<char> mask)
    {
        int n = 0, m = 0, starM = -1, starN = 0;
        while (n < name.Length)
        {
            if (m < mask.Length && mask[m] == '*')
            {
                starM = m++;
                starN = n;
            }
            else if (m < mask.Length && (mask[m] == '?' || SameLetter(name[n], mask[m])))
            {
                n++;
                m++;
            }
            else if (starM >= 0)
            {
                m = starM + 1;
                n = ++starN;
            }
            else
            {
                return false;
            }
        }

        while (m < mask.Length && mask[m] == '*')
        {
            m++;
        }

        return m == mask.Length;
    }

    private static bool SameLetter(char a, char b) => a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}
