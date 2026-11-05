// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/Bencode.cs

using System.Text;
using Salandual.Core.Resources;

namespace Salandual.Core.Viewer;

public enum BencodeKind : byte { Dictionary, List, Integer, Bytes }

public sealed class BencodeValue
{
    private readonly byte[] _source;

    internal BencodeValue(byte[] source, BencodeKind kind, int start)
    {
        _source = source;
        Kind = kind;
        Start = start;
    }

    public BencodeKind Kind { get; }

    public int Start { get; }

    public int End { get; internal set; }

    public long Integer { get; internal set; }

    internal int BytesStart;

    internal int BytesLength;

    internal List<BencodeValue>? Items;

    internal List<(string Key, BencodeValue Value)>? Entries;

    internal List<BencodeValue>? KeyValues;

    public ReadOnlySpan<byte> KeyBytes(int index) => KeyValues is { } keys ? keys[index].Bytes : default;

    public ReadOnlySpan<byte> Bytes => Kind == BencodeKind.Bytes ? _source.AsSpan(BytesStart, BytesLength) : default;

    public ReadOnlySpan<byte> Raw => _source.AsSpan(Start, End - Start);

    public IReadOnlyList<BencodeValue> List => (IReadOnlyList<BencodeValue>?)Items ?? [];

    public IReadOnlyList<(string Key, BencodeValue Value)> Dictionary => (IReadOnlyList<(string, BencodeValue)>?)Entries ?? [];

    public BencodeValue? this[string key]
    {
        get
        {
            if (Entries is { } entries)
            {
                foreach (var (k, v) in entries)
                {
                    if (string.Equals(k, key, StringComparison.Ordinal))
                    {
                        return v;
                    }
                }
            }

            return null;
        }
    }

    public string? Text => Kind == BencodeKind.Bytes ? Encoding.UTF8.GetString(Bytes) : null;

    public bool IsText
    {
        get
        {
            if (Kind != BencodeKind.Bytes)
            {
                return false;
            }

            var bytes = Bytes;
            if (bytes.IndexOfAnyInRange((byte)0, (byte)8) >= 0 || bytes.IndexOfAnyInRange((byte)14, (byte)31) >= 0 || bytes.IndexOf((byte)11) >= 0 || bytes.IndexOf((byte)12) >= 0 || bytes.IndexOf((byte)127) >= 0)
            {
                return false;
            }

            return System.Text.Unicode.Utf8.IsValid(bytes);
        }
    }
}

public sealed class BencodeException(string message, int offset) : Exception(message)
{
    public int Offset { get; } = offset;
}

public static class Bencode
{
    public const int MaxDepth = 64;

    public static BencodeValue Read(byte[] data, CancellationToken token = default)
    {
        var at = 0;
        var value = ReadValue(data, ref at, 0, token);
        if (at != data.Length)
        {
            throw new BencodeException(Strings.Torrent_Bad_After, at);
        }

        return value;
    }

    private static BencodeValue ReadValue(byte[] data, ref int at, int depth, CancellationToken token)
    {
        if (depth > MaxDepth)
        {
            throw new BencodeException(Strings.Torrent_Bad_Deep, at);
        }

        if (at >= data.Length)
        {
            throw new BencodeException(Strings.Torrent_Bad_Short, at);
        }

        var start = at;
        switch (data[at])
        {
            case (byte)'d':
            {
                at++;
                var value = new BencodeValue(data, BencodeKind.Dictionary, start) { Entries = [], KeyValues = [] };
                while (true)
                {
                    if (at >= data.Length)
                    {
                        throw new BencodeException(Strings.Torrent_Bad_Short, at);
                    }

                    if (data[at] == (byte)'e')
                    {
                        at++;
                        break;
                    }

                    if ((value.Entries.Count & 0x3FF) == 0x3FF)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    var keyAt = at;
                    var key = ReadValue(data, ref at, depth + 1, token);
                    if (key.Kind != BencodeKind.Bytes)
                    {
                        throw new BencodeException(Strings.Torrent_Bad_Key, keyAt);
                    }

                    var item = ReadValue(data, ref at, depth + 1, token);
                    value.Entries.Add((Encoding.UTF8.GetString(key.Bytes), item));
                    value.KeyValues.Add(key);
                }

                value.End = at;
                return value;
            }

            case (byte)'l':
            {
                at++;
                var value = new BencodeValue(data, BencodeKind.List, start) { Items = [] };
                while (true)
                {
                    if (at >= data.Length)
                    {
                        throw new BencodeException(Strings.Torrent_Bad_Short, at);
                    }

                    if (data[at] == (byte)'e')
                    {
                        at++;
                        break;
                    }

                    if ((value.Items.Count & 0x3FF) == 0x3FF)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    value.Items.Add(ReadValue(data, ref at, depth + 1, token));
                }

                value.End = at;
                return value;
            }

            case (byte)'i':
            {
                at++;
                var end = Array.IndexOf(data, (byte)'e', at, Math.Min(data.Length - at, 22));
                if (end < 0 || end == at)
                {
                    throw new BencodeException(Strings.Torrent_Bad_Number, start);
                }

                var digits = data.AsSpan(at, end - at);
                var negative = digits[0] == (byte)'-';
                var body = negative ? digits[1..] : digits;
                if (body.Length == 0 || (body.Length > 1 && body[0] == (byte)'0') || (negative && body is [(byte)'0']))
                {
                    throw new BencodeException(Strings.Torrent_Bad_Number, start);
                }

                long n = 0;
                foreach (var b in body)
                {
                    if (b is < (byte)'0' or > (byte)'9' || n > (long.MaxValue - 9) / 10)
                    {
                        throw new BencodeException(Strings.Torrent_Bad_Number, start);
                    }

                    n = (n * 10) + (b - '0');
                }

                at = end + 1;
                return new BencodeValue(data, BencodeKind.Integer, start) { Integer = negative ? -n : n, End = at };
            }

            case >= (byte)'0' and <= (byte)'9':
            {
                long length = 0;
                while (at < data.Length && data[at] is >= (byte)'0' and <= (byte)'9')
                {
                    length = (length * 10) + (data[at] - '0');
                    if (length > data.Length)
                    {
                        throw new BencodeException(Strings.Torrent_Bad_Length, start);
                    }

                    at++;
                }

                if (at >= data.Length || data[at] != (byte)':')
                {
                    throw new BencodeException(Strings.Torrent_Bad_Length, start);
                }

                at++;
                if (length > data.Length - at)
                {
                    throw new BencodeException(Strings.Torrent_Bad_Length, start);
                }

                var value = new BencodeValue(data, BencodeKind.Bytes, start) { BytesStart = at, BytesLength = (int)length };
                at += (int)length;
                value.End = at;
                return value;
            }

            default:
                throw new BencodeException(Strings.Torrent_Bad_Unknown, at);
        }
    }
}
