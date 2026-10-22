// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/TextLines.cs

using System.Text;

namespace Salandual.Core.Viewer;

public sealed record DecodedLine(string Text, long[] ByteOf)
{
    public int ColumnOf(long offset)
    {
        var i = Array.BinarySearch(ByteOf, offset);
        if (i >= 0)
        {
            while (i > 0 && ByteOf[i - 1] == offset)
            {
                i--;
            }

            return i;
        }

        return Math.Max(0, ~i - 1);
    }
}

public sealed class TextLines(CachedBytes bytes, TextEncoding encoding, long start)
{
    public const int MaxLineBytes = 16 * 1024;

    private const int MaxBackScan = 4 * 1024 * 1024;

    public CachedBytes Bytes => bytes;

    public TextEncoding Encoding => encoding;

    public long Start => start;

    public long Length => bytes.Length;

    public static (int Content, int Break) Split(ReadOnlySpan<byte> span, TextEncoding encoding)
    {
        var limit = Math.Min(span.Length, MaxLineBytes);
        if (encoding.Unit == 1)
        {
            var i = span[..limit].IndexOfAny((byte)'\r', (byte)'\n');
            if (i >= 0)
            {
                return (i, span[i] == '\r' && i + 1 < span.Length && span[i + 1] == '\n' ? 2 : 1);
            }

            if (span.Length <= MaxLineBytes)
            {
                return (span.Length, 0);
            }

            var cut = MaxLineBytes;
            for (var back = 0; encoding.IsUtf8 && back < 3 && cut > 1 && (span[cut] & 0xC0) == 0x80; back++)
            {
                cut--;
            }

            return (cut, 0);
        }

        var big = encoding.BigEndian;
        int Unit(ReadOnlySpan<byte> s, int at) => big ? (s[at] << 8) | s[at + 1] : s[at] | (s[at + 1] << 8);
        for (var i = 0; i + 1 < limit; i += 2)
        {
            var u = Unit(span, i);
            if (u == '\n')
            {
                return (i, 2);
            }

            if (u == '\r')
            {
                return (i, i + 3 < span.Length && Unit(span, i + 2) == '\n' ? 4 : 2);
            }
        }

        if (span.Length <= MaxLineBytes)
        {
            return (span.Length, 0);
        }

        var end = MaxLineBytes;
        return (char.IsHighSurrogate((char)Unit(span, end - 2)) ? end - 2 : end, 0);
    }

    public (long End, long Next) LineAt(long offset)
    {
        if (offset >= Length)
        {
            return (Length, Length);
        }

        Span<byte> small = stackalloc byte[512];
        var n = bytes.Read(offset, small);
        var (content, brk) = Split(small[..n], encoding);
        if (offset + n >= Length || (brk > 0 && content + brk + 2 <= n))
        {
            return (offset + content, offset + content + brk);
        }

        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent((int)Math.Min(MaxLineBytes + 4, Length - offset));
        try
        {
            n = bytes.Read(offset, buffer.AsSpan(0, (int)Math.Min(MaxLineBytes + 4, Length - offset)));
            (content, brk) = Split(buffer.AsSpan(0, n), encoding);
            return (offset + content, offset + content + brk);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public long? Previous(long lineStart)
    {
        if (lineStart <= start)
        {
            return null;
        }

        var from = lineStart - BreakBefore(lineStart);
        var candidate = LastBreakEndBefore(from) ?? start;

        var at = candidate;
        while (true)
        {
            var (_, next) = LineAt(at);
            if (next >= lineStart || next == at)
            {
                return at;
            }

            at = next;
        }
    }

    public long LineStartOf(long offset)
    {
        offset = Math.Clamp(offset, start, Math.Max(start, Length));
        var at = LastBreakEndBefore(offset) ?? start;
        while (true)
        {
            var (_, next) = LineAt(at);
            if (next > offset || next == at || next >= Length)
            {
                return at;
            }

            at = next;
        }
    }

    public DecodedLine Decode(long from, long to, int tabWidth)
    {
        var length = (int)Math.Max(0, to - from);
        var raw = new byte[length];
        length = bytes.Read(from, raw);
        var text = new StringBuilder(length + 16);
        var map = new List<long>(length + 1);
        var span = raw.AsSpan(0, length);
        var column = 0;

        void Add(char c, long at)
        {
            if (c == '\t')
            {
                var spaces = tabWidth - (column % tabWidth);
                for (var k = 0; k < spaces; k++)
                {
                    text.Append(' ');
                    map.Add(at);
                }

                column += spaces;
                return;
            }

            text.Append(c < 0x20 || c == 0x7F ? '·' : c);
            map.Add(at);
            column++;
        }

        if (encoding.IsUtf8)
        {
            var i = 0;
            Span<char> pair = stackalloc char[2];
            while (i < span.Length)
            {
                System.Text.Rune.DecodeFromUtf8(span[i..], out var rune, out var consumed);
                var n = rune.EncodeToUtf16(pair);
                for (var k = 0; k < n; k++)
                {
                    Add(pair[k], from + i);
                }

                i += Math.Max(1, consumed);
            }
        }
        else
        {
            var chars = encoding.Encoding.GetChars(raw, 0, length);
            for (var i = 0; i < chars.Length; i++)
            {
                Add(chars[i], from + (i * encoding.Unit));
            }
        }

        map.Add(to);
        return new DecodedLine(text.ToString(), [.. map]);
    }

    private int BreakBefore(long offset)
    {
        if (encoding.Unit == 1)
        {
            var last = bytes.ByteAt(offset - 1);
            if (last == '\n')
            {
                return offset - 2 >= start && bytes.ByteAt(offset - 2) == '\r' ? 2 : 1;
            }

            return last == '\r' ? 1 : 0;
        }

        var unit = UnitAt(offset - 2);
        if (unit == '\n')
        {
            return offset - 4 >= start && UnitAt(offset - 4) == '\r' ? 4 : 2;
        }

        return unit == '\r' ? 2 : 0;
    }

    private int UnitAt(long offset)
    {
        if (offset < start)
        {
            return -1;
        }

        int a = bytes.ByteAt(offset), b = bytes.ByteAt(offset + 1);
        return a < 0 || b < 0 ? -1 : encoding.BigEndian ? (a << 8) | b : a | (b << 8);
    }

    private long? LastBreakEndBefore(long offset)
    {
        var buffer = new byte[1024];
        var end = offset;
        var lowest = Math.Max(start, offset - MaxBackScan);
        while (end > lowest)
        {
            var from = Math.Max(lowest, end - buffer.Length);
            if (encoding.Unit == 2)
            {
                from += (from - start) % 2;
            }

            if (from >= end)
            {
                break;
            }

            var n = bytes.Read(from, buffer.AsSpan(0, (int)(end - from)));
            var span = buffer.AsSpan(0, n);
            if (encoding.Unit == 1)
            {
                for (var i = span.LastIndexOfAny((byte)'\r', (byte)'\n'); i >= 0; i = span[..i].LastIndexOfAny((byte)'\r', (byte)'\n'))
                {
                    if (span[i] == '\r' && from + i + 1 == offset && bytes.ByteAt(offset) == '\n')
                    {
                        continue;
                    }

                    return from + i + 1;
                }
            }
            else
            {
                for (var i = (n & ~1) - 2; i >= 0; i -= 2)
                {
                    var u = encoding.BigEndian ? (span[i] << 8) | span[i + 1] : span[i] | (span[i + 1] << 8);
                    if (u == '\n' || (u == '\r' && !(from + i + 2 == offset && UnitAt(offset) == '\n')))
                    {
                        return from + i + 2;
                    }
                }
            }

            end = from;
            if (buffer.Length < 64 * 1024)
            {
                buffer = new byte[buffer.Length * 8];
            }
        }

        return lowest > start ? lowest - ((lowest - start) % Math.Max(1, encoding.Unit)) : null;
    }
}
