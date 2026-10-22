// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Compare/DiffFind.cs

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Salandual.Core.Compare;

public enum FindSides : byte
{
    Both,
    Left,
    Right,
}

public readonly record struct DiffHit(long Row, int Side, long Start, int Length);

public sealed record DiffFindQuery(string Text, bool MatchCase = false, bool WholeWords = false, bool Regex = false, FindSides Sides = FindSides.Both, bool OnlyDifferences = false, bool HexBytes = false)
{
    public bool Looks(int side) => Sides == FindSides.Both || (int)Sides - 1 == side;
}

public static class DiffFind
{
    public const int MaxHits = 100_000;

    public static List<DiffHit>? Text(IReadOnlyList<DiffRow> rows, IReadOnlyList<string> left, IReadOnlyList<string> right, DiffFindQuery query, CancellationToken cancel = default)
    {
        if (query.Text.Length == 0)
        {
            return [];
        }

        Regex pattern;
        try
        {
            var source = query.Regex ? query.Text : System.Text.RegularExpressions.Regex.Escape(query.Text);
            if (query.WholeWords)
            {
                source = @"\b(?:" + source + @")\b";
            }

            pattern = new Regex(source, (query.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return null;
        }

        var hits = new List<DiffHit>();
        try
        {
            for (var r = 0; r < rows.Count && hits.Count < MaxHits; r++)
            {
                var row = rows[r];
                if (query.OnlyDifferences && row.Change == LineChange.Same)
                {
                    continue;
                }

                if ((r & 1023) == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                }

                for (var side = 0; side < 2; side++)
                {
                    var line = side == 0 ? row.Left : row.Right;
                    if (line < 0 || !query.Looks(side))
                    {
                        continue;
                    }

                    for (var m = pattern.Match((side == 0 ? left : right)[line]); m.Success && hits.Count < MaxHits; m = m.NextMatch())
                    {
                        if (m.Length > 0)
                        {
                            hits.Add(new DiffHit(r, side, m.Index, m.Length));
                        }
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return hits;
    }

    public static byte[]? Pattern(DiffFindQuery query)
    {
        if (!query.HexBytes)
        {
            return query.Text.Length == 0 ? [] : Encoding.UTF8.GetBytes(query.Text);
        }

        var digits = string.Concat(query.Text.Where(c => !char.IsWhiteSpace(c)));
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[2..];
        }

        if (digits.Length % 2 != 0)
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

    public static List<DiffHit>? Bytes(Stream left, Stream right, BinaryDiff diff, DiffFindQuery query, CancellationToken cancel = default)
    {
        if (Pattern(query) is not { } pattern)
        {
            return null;
        }

        if (pattern.Length == 0)
        {
            return [];
        }

        var fold = !query.HexBytes;
        var found = new List<DiffHit>();
        for (var side = 0; side < 2; side++)
        {
            if (query.Looks(side))
            {
                Scan(side == 0 ? left : right, side);
            }
        }

        found.Sort((a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Side != b.Side ? a.Side.CompareTo(b.Side) : a.Start.CompareTo(b.Start));
        return found.Count > MaxHits ? found[..MaxHits] : found;

        void Scan(Stream stream, int side)
        {
            const int Piece = 1 << 20;
            var buffer = new byte[Piece + pattern.Length];
            var kept = 0;
            long offset = 0;
            stream.Position = 0;
            int read;
            var count = 0;
            while ((read = stream.Read(buffer, kept, Piece)) > 0 && count < MaxHits)
            {
                cancel.ThrowIfCancellationRequested();
                var length = kept + read;
                for (var i = 0; i + pattern.Length <= length && count < MaxHits; i++)
                {
                    if (Matches(buffer, i))
                    {
                        var at = offset + i;
                        var row = at / BinaryDiff.RowBytes;
                        if (!query.OnlyDifferences || diff.BlockOf(row) >= 0)
                        {
                            found.Add(new DiffHit(row, side, at, pattern.Length));
                            count++;
                        }
                    }
                }

                kept = Math.Min(pattern.Length - 1, length);
                Array.Copy(buffer, length - kept, buffer, 0, kept);
                offset += length - kept;
            }
        }

        bool Matches(byte[] buffer, int at)
        {
            for (var j = 0; j < pattern.Length; j++)
            {
                var (a, b) = (buffer[at + j], pattern[j]);
                if (a != b && !(fold && Lower(a) == Lower(b)))
                {
                    return false;
                }
            }

            return true;
        }

        static byte Lower(byte b) => b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b;
    }
}
