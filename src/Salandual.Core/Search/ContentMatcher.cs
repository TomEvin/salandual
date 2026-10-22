// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Search/ContentMatcher.cs

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Salandual.Core.Viewer;

namespace Salandual.Core.Search;

public sealed record TextProblemInRow(int Row, string Message);

public readonly record struct ContentResult(bool IsMatch, MatchLine? Line);

public sealed class ContentMatcher
{
    private const int CharBlock = 64 * 1024;
    private const int LongestLine = 64 * 1024;

    public const long CountLimit = 32L * 1024 * 1024;

    private readonly Row[] _rows;
    private readonly bool _all;
    private readonly SearchEncodings _encodings;

    private sealed record Row(SearchText Text, Regex? Pattern, byte[]? Bytes);

    private ContentMatcher(Row[] rows, bool all, SearchEncodings encodings)
    {
        _rows = rows;
        _all = all;
        _encodings = encodings == SearchEncodings.None ? SearchEncodings.Auto : encodings;
    }

    public static ContentMatcher? Create(SearchCriteria criteria, out TextProblemInRow? problem)
    {
        problem = null;
        var rows = new List<Row>();
        for (var i = 0; i < criteria.Texts.Count; i++)
        {
            var text = criteria.Texts[i];
            if (text.Text.Length == 0)
            {
                continue;
            }

            if (text.Kind == TextKind.Hex)
            {
                if (HexPattern.Parse(text.Text, out var error) is not { Length: > 0 } bytes)
                {
                    problem = new(i, error ?? "");
                    return null;
                }

                rows.Add(new(text, null, bytes));
                continue;
            }

            var body = text.Kind == TextKind.Regex ? text.Text : Regex.Escape(text.Text);
            if (criteria.WholeWords)
            {
                body = $@"(?<!\w)(?:{body})(?!\w)";
            }

            try
            {
                var options = RegexOptions.CultureInvariant | (criteria.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
                rows.Add(new(text, new Regex(body, options, TimeSpan.FromSeconds(2)), null));
            }
            catch (ArgumentException ex)
            {
                problem = new(i, ex.Message);
                return null;
            }
        }

        return rows.Count == 0 ? null : new ContentMatcher([.. rows], criteria.AllTexts, criteria.Encodings);
    }

    public ContentResult Match(Stream stream, CancellationToken token = default)
    {
        var found = new bool[_rows.Length];
        MatchLine? line = null;
        var show = Array.FindIndex(_rows, r => r.Text.Contains && r.Pattern is not null);

        if (_rows.Any(r => r.Bytes is not null))
        {
            var offset = ScanBytes(stream, found, token);
            if (show < 0 && offset is { } at)
            {
                var hexRow = Array.FindIndex(_rows, r => r.Bytes is not null && r.Text.Contains);
                if (hexRow >= 0)
                {
                    line = new MatchLine(0, "", 0, 0, 0, Offset: at);
                }
            }

            Rewind(stream);
        }

        if (_rows.Any(r => r.Pattern is not null))
        {
            foreach (var encoding in EncodingsFor(stream))
            {
                var (decided, first) = ScanText(stream, encoding, found, show, token);
                line ??= first;
                Rewind(stream);
                if (decided)
                {
                    break;
                }
            }
        }

        var match = Decide(found) ?? false;
        return new ContentResult(match, match ? line : null);
    }

    private bool? Decide(bool[] found, bool atEnd = true)
    {
        var anyUnknown = false;
        var anyTrue = false;
        var anyFalse = false;
        for (var i = 0; i < _rows.Length; i++)
        {
            bool? value = _rows[i].Text.Contains
                ? found[i] ? true : atEnd ? false : null
                : found[i] ? false : atEnd ? true : null;
            anyUnknown |= value is null;
            anyTrue |= value == true;
            anyFalse |= value == false;
        }

        if (_all)
        {
            return anyFalse ? false : anyUnknown ? null : true;
        }

        return anyTrue ? true : anyUnknown ? null : false;
    }

    private static void Rewind(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }
    }

    private IEnumerable<Encoding> EncodingsFor(Stream stream)
    {
        if (_encodings.HasFlag(SearchEncodings.Auto))
        {
            var start = new byte[TextEncodings.SampleSize];
            var read = stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
            Rewind(stream);
            yield return TextEncodings.Detect(start.AsSpan(0, read)).Encoding.Encoding;
            yield break;
        }

        foreach (var (flag, id) in new (SearchEncodings, string)[]
        {
            (SearchEncodings.Ansi, "cp" + CultureInfo.CurrentCulture.TextInfo.ANSICodePage),
            (SearchEncodings.Oem, "cp" + CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            (SearchEncodings.Utf8, TextEncodings.Utf8),
            (SearchEncodings.Utf16LE, TextEncodings.Utf16LE),
            (SearchEncodings.Utf16BE, TextEncodings.Utf16BE),
        })
        {
            if (_encodings.HasFlag(flag))
            {
                yield return (TextEncodings.Find(id) ?? TextEncodings.Ansi).Encoding;
            }
        }
    }

    private (bool Decided, MatchLine? First) ScanText(Stream stream, Encoding encoding, bool[] found, int show, CancellationToken token)
    {
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false, CharBlock, leaveOpen: true);
        var block = new char[CharBlock];
        var pending = new StringBuilder();
        long lineNumber = 1;
        MatchLine? first = null;
        var more = 0;
        var known = false;
        long chars = 0;
        int read;
        while ((read = reader.Read(block, 0, block.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            chars += read;
            var start = 0;
            for (var i = 0; i < read; i++)
            {
                if (block[i] == '\n')
                {
                    pending.Append(block, start, i - start);
                    if (Line(pending, lineNumber))
                    {
                        return (true, first);
                    }

                    pending.Clear();
                    lineNumber++;
                    start = i + 1;
                }
                else if (pending.Length + (i - start) >= LongestLine)
                {
                    pending.Append(block, start, i - start);
                    if (Line(pending, lineNumber))
                    {
                        return (true, first);
                    }

                    pending.Clear();
                    start = i;
                }
            }

            pending.Append(block, start, read - start);
            if (known && (stream.CanSeek ? stream.Position : chars) > CountLimit)
            {
                if (first is not null)
                {
                    first = first with { MoreLines = more, AtLeast = true };
                }

                return (true, first);
            }
        }

        if (pending.Length > 0)
        {
            Line(pending, lineNumber);
        }

        if (first is not null)
        {
            first = first with { MoreLines = more };
        }

        return (Decide(found) is not null && first is not null, first);

        bool Line(StringBuilder text, long number)
        {
            if (text.Length > 0 && text[^1] == '\r')
            {
                text.Length--;
            }

            var line = text.ToString();
            if (number == 1 && line.Length > 0 && line[0] == '﻿')
            {
                line = line[1..];
            }
            for (var r = 0; r < _rows.Length; r++)
            {
                if (_rows[r].Pattern is not { } pattern || (found[r] && r != show))
                {
                    continue;
                }

                Match m;
                try
                {
                    m = pattern.Match(line);
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }

                if (!m.Success)
                {
                    continue;
                }

                found[r] = true;
                if (r == show)
                {
                    if (first is null)
                    {
                        first = Excerpt(line, number, m.Index, m.Length);
                    }
                    else
                    {
                        more++;
                    }
                }
            }

            var decided = Decide(found, atEnd: false);
            known = decided is not null;
            if (decided == false)
            {
                return true;
            }

            return decided == true && show < 0;
        }
    }

    private static MatchLine Excerpt(string line, long number, int start, int length)
    {
        const int Before = 60;
        const int Width = 240;
        var from = Math.Max(0, start - Before);
        var to = Math.Min(line.Length, Math.Max(start + length, from + Width));
        var text = line[from..to].Replace('\t', ' ');
        var lead = text.Length - text.TrimStart().Length;
        text = text.Trim();
        return new MatchLine(number, text, Math.Max(0, start - from - lead), Math.Min(length, Math.Max(0, text.Length - (start - from - lead))), 0);
    }

    private long? ScanBytes(Stream stream, bool[] found, CancellationToken token)
    {
        var longest = _rows.Where(r => r.Bytes is not null).Max(r => r.Bytes!.Length);
        var buffer = new byte[1024 * 1024 + longest];
        var kept = 0;
        long baseOffset = 0;
        long? first = null;
        int read;
        while ((read = stream.Read(buffer, kept, buffer.Length - kept)) > 0)
        {
            token.ThrowIfCancellationRequested();
            var span = buffer.AsSpan(0, kept + read);
            for (var r = 0; r < _rows.Length; r++)
            {
                if (_rows[r].Bytes is not { } bytes || found[r])
                {
                    continue;
                }

                var at = span.IndexOf(bytes);
                if (at >= 0)
                {
                    found[r] = true;
                    if (_rows[r].Text.Contains && first is null)
                    {
                        first = baseOffset + at;
                    }
                }
            }

            if (Enumerable.Range(0, _rows.Length).All(i => _rows[i].Bytes is null || found[i]))
            {
                break;
            }

            kept = Math.Min(longest - 1, span.Length);
            span[^kept..].CopyTo(buffer);
            baseOffset += span.Length - kept;
        }

        return first;
    }
}

public static class HexPattern
{
    public static byte[]? Parse(string text, out string? error)
    {
        error = null;
        var bytes = new List<byte>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || c == ',')
            {
                i++;
            }
            else if (c == '"')
            {
                var end = text.IndexOf('"', i + 1);
                if (end < 0)
                {
                    error = Resources.Strings.Find_HexQuote;
                    return null;
                }

                bytes.AddRange(TextEncodings.Ansi.Encoding.GetBytes(text[(i + 1)..end]));
                i = end + 1;
            }
            else if (i + 1 < text.Length && Uri.IsHexDigit(c) && Uri.IsHexDigit(text[i + 1]))
            {
                bytes.Add(byte.Parse(text.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 2;
            }
            else
            {
                error = Resources.Strings.Find_HexDigits;
                return null;
            }
        }

        if (bytes.Count == 0)
        {
            error = Resources.Strings.Find_HexDigits;
            return null;
        }

        return [.. bytes];
    }
}
