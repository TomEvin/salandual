// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/ViewerFind.cs

using System.Globalization;
using System.Text.RegularExpressions;

namespace Salandual.Core.Viewer;

public sealed record FindQuery(string Text, bool HexBytes = false, bool MatchCase = false, bool WholeWords = false, bool IsRegex = false)
{
    public byte[]? Bytes => HexBytes ? ViewerFind.ParseHex(Text) : null;

    public Regex? ToRegex()
    {
        if (HexBytes || string.IsNullOrEmpty(Text))
        {
            return null;
        }

        var pattern = IsRegex ? Text : Regex.Escape(Text);
        if (WholeWords)
        {
            pattern = $@"(?<!\w)(?:{pattern})(?!\w)";
        }

        try
        {
            return new Regex(pattern, (MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) | RegexOptions.Multiline, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public bool IsValid => HexBytes ? Bytes is { Length: > 0 } : ToRegex() is not null;
}

public readonly record struct FindHit(long Start, long End);

public static class ViewerFind
{
    private const int Chunk = 1024 * 1024;

    public static byte[]? ParseHex(string text)
    {
        var digits = new string([.. text.Where(c => !char.IsWhiteSpace(c) && c != ',' && c != '-')]);
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[2..];
        }

        if (digits.Length == 0 || digits.Length % 2 != 0)
        {
            return null;
        }

        var bytes = new byte[digits.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(digits.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return null;
            }
        }

        return bytes;
    }

    public static FindHit? Next(TextLines lines, FindQuery query, long from, CancellationToken token = default)
    {
        if (query.Bytes is { } pattern)
        {
            return NextBytes(lines.Bytes.Source, pattern, from, token);
        }

        if (query.ToRegex() is not { } regex)
        {
            return null;
        }

        var at = lines.LineStartOf(from);
        while (at < lines.Length && !token.IsCancellationRequested)
        {
            var (text, map, end) = ReadChunk(lines, at);
            for (var m = regex.Match(text); m.Success; m = m.NextMatch())
            {
                if (m.Length > 0 && map[m.Index] >= from)
                {
                    return new FindHit(map[m.Index], map[m.Index + m.Length]);
                }
            }

            at = end;
        }

        return null;
    }

    public static FindHit? Previous(TextLines lines, FindQuery query, long before, CancellationToken token = default)
    {
        if (query.Bytes is { } pattern)
        {
            return PreviousBytes(lines.Bytes.Source, pattern, lines.Start, before, token);
        }

        if (query.ToRegex() is not { } regex)
        {
            return null;
        }

        var end = Math.Min(lines.Length, lines.LineAt(lines.LineStartOf(Math.Max(lines.Start, before))).Next);
        while (end > lines.Start && !token.IsCancellationRequested)
        {
            var from = lines.LineStartOf(Math.Max(lines.Start, end - Chunk));
            if (from >= end)
            {
                from = lines.Previous(end) ?? lines.Start;
            }

            FindHit? last = null;
            var at = from;
            while (at < end)
            {
                var (text, map, chunkEnd) = ReadChunk(lines, at, end);
                for (var m = regex.Match(text); m.Success; m = m.NextMatch())
                {
                    if (m.Length > 0 && map[m.Index] < before)
                    {
                        last = new FindHit(map[m.Index], map[m.Index + m.Length]);
                    }
                }

                at = chunkEnd;
            }

            if (last is not null)
            {
                return last;
            }

            end = from;
        }

        return null;
    }

    public static (long Total, long Before, bool Capped) Count(TextLines lines, FindQuery query, long current, IProgress<double>? progress = null, long limit = 1_000_000, CancellationToken token = default)
    {
        long total = 0, before = 0;
        var at = lines.Start;
        while (!token.IsCancellationRequested)
        {
            var hit = Next(lines, query, at, token);
            if (hit is not { } h)
            {
                break;
            }

            total++;
            before += h.Start < current ? 1 : 0;
            if (total >= limit)
            {
                return (total, before, true);
            }

            at = Math.Max(h.End, h.Start + 1);
            if (total % 256 == 0)
            {
                progress?.Report((double)at / Math.Max(1, lines.Length));
            }
        }

        return (total, before, false);
    }

    public static IEnumerable<FindHit> InLine(DecodedLine line, Regex regex)
    {
        foreach (Match m in regex.Matches(line.Text))
        {
            if (m.Length > 0)
            {
                yield return new FindHit(line.ByteOf[m.Index], line.ByteOf[m.Index + m.Length]);
            }
        }
    }

    private static (string Text, long[] Map, long End) ReadChunk(TextLines lines, long at, long stop = long.MaxValue)
    {
        var limit = Math.Min(lines.Length, stop);
        var end = at;
        while (end < limit && end - at < Chunk)
        {
            var next = lines.LineAt(end).Next;
            if (next == end)
            {
                break;
            }

            end = Math.Min(next, limit);
        }

        var decoded = Raw(lines, at, end);
        return (decoded.Text, decoded.Map, end);
    }

    private static (string Text, long[] Map) Raw(TextLines lines, long from, long to)
    {
        var raw = new byte[(int)(to - from)];
        var n = lines.Bytes.Read(from, raw);
        var encoding = lines.Encoding;
        if (encoding.IsUtf8)
        {
            var text = new System.Text.StringBuilder(n);
            var map = new List<long>(n + 1);
            Span<char> pair = stackalloc char[2];
            var i = 0;
            while (i < n)
            {
                System.Text.Rune.DecodeFromUtf8(raw.AsSpan(i, n - i), out var rune, out var consumed);
                var count = rune.EncodeToUtf16(pair);
                for (var k = 0; k < count; k++)
                {
                    text.Append(pair[k]);
                    map.Add(from + i);
                }

                i += Math.Max(1, consumed);
            }

            map.Add(from + n);
            return (text.ToString(), [.. map]);
        }

        var chars = encoding.Encoding.GetChars(raw, 0, n);
        var offsets = new long[chars.Length + 1];
        for (var k = 0; k <= chars.Length; k++)
        {
            offsets[k] = from + (k * encoding.Unit);
        }

        return (new string(chars), offsets);
    }

    private static FindHit? NextBytes(IByteSource source, byte[] pattern, long from, CancellationToken token)
    {
        var buffer = new byte[Chunk + pattern.Length];
        for (var at = from; at < source.Length && !token.IsCancellationRequested; at += Chunk)
        {
            var n = source.Read(at, buffer);
            var i = buffer.AsSpan(0, n).IndexOf(pattern);
            if (i >= 0)
            {
                return new FindHit(at + i, at + i + pattern.Length);
            }
        }

        return null;
    }

    private static FindHit? PreviousBytes(IByteSource source, byte[] pattern, long start, long before, CancellationToken token)
    {
        var buffer = new byte[Chunk + pattern.Length];
        var end = Math.Min(source.Length, before + pattern.Length - 1);
        while (end > start && !token.IsCancellationRequested)
        {
            var from = Math.Max(start, end - buffer.Length);
            var n = source.Read(from, buffer.AsSpan(0, (int)(end - from)));
            var i = buffer.AsSpan(0, n).LastIndexOf(pattern);
            if (i >= 0)
            {
                return new FindHit(from + i, from + i + pattern.Length);
            }

            if (from == start)
            {
                break;
            }

            end = from + pattern.Length - 1;
        }

        return null;
    }
}

public readonly record struct GoToTarget(long Value, bool IsOffset)
{
    public static GoToTarget? Parse(string text, bool hexView, bool? hexDigits = null)
    {
        var t = text.Trim().Replace(" ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (t.Length == 0)
        {
            return null;
        }

        string? hex = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? t[2..]
            : t.StartsWith('$') ? t[1..]
            : t.EndsWith('h') || t.EndsWith('H') ? t[..^1]
            : hexView && hexDigits == true ? t
            : null;
        if (hex is not null)
        {
            return long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var o) ? new GoToTarget(o, true) : null;
        }

        if (!long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            return hexView && hexDigits is null && long.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? new GoToTarget(h, true) : null;
        }

        return hexView ? new GoToTarget(n, true) : new GoToTarget(Math.Max(1, n), false);
    }

    public static string Rebase(string text, bool toHex)
    {
        if (Parse(text, hexView: true, hexDigits: !toHex) is not { } target)
        {
            return text;
        }

        return toHex ? target.Value.ToString("X", CultureInfo.InvariantCulture) : target.Value.ToString(CultureInfo.InvariantCulture);
    }
}
