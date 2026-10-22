// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Compare/BinaryDiff.cs

namespace Salandual.Core.Compare;

public sealed class BinaryDiff
{
    public const int RowBytes = 16;

    public const int MaxBlocks = 100_000;

    private BinaryDiff(long leftLength, long rightLength, List<DiffBlock> blocks, long bytes, bool cancelled)
    {
        LeftLength = leftLength;
        RightLength = rightLength;
        Blocks = blocks;
        DifferentBytes = bytes;
        Cancelled = cancelled;
    }

    public long LeftLength { get; }

    public long RightLength { get; }

    public IReadOnlyList<DiffBlock> Blocks { get; }

    public long DifferentBytes { get; }

    public bool Cancelled { get; }

    public long RowCount => (Math.Max(LeftLength, RightLength) + RowBytes - 1) / RowBytes;

    public bool Same => DifferentBytes == 0 && LeftLength == RightLength;

    public int BlockOf(long row)
    {
        int lo = 0, hi = Blocks.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var b = Blocks[mid];
            if (row < b.Row)
            {
                hi = mid - 1;
            }
            else if (row >= (long)b.Row + b.Count)
            {
                lo = mid + 1;
            }
            else
            {
                return mid;
            }
        }

        return -1;
    }

    public static BinaryDiff Compare(Stream left, Stream right, Action<long>? progress = null, CancellationToken cancel = default)
    {
        const int chunk = 1 << 20;
        var a = new byte[chunk];
        var b = new byte[chunk];
        var blocks = new List<DiffBlock>();
        long bytes = 0, at = 0, lastRow = -2;
        var cancelled = false;
        while (true)
        {
            if (cancel.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var na = Fill(left, a);
            var nb = Fill(right, b);
            var both = Math.Min(na, nb);
            var sa = a.AsSpan(0, both);
            var sb = b.AsSpan(0, both);
            var from = 0;
            while (from < both)
            {
                var same = sa[from..].CommonPrefixLength(sb[from..]);
                from += same;
                if (from >= both)
                {
                    break;
                }

                bytes++;
                AddRow((at + from) / RowBytes);
                from++;
            }

            at += both;
            progress?.Invoke(at);
            if (na != nb || na < chunk)
            {
                break;
            }
        }

        long leftLength = left.CanSeek ? left.Length : at, rightLength = right.CanSeek ? right.Length : at;
        if (!cancelled && leftLength != rightLength)
        {
            var shorter = Math.Min(leftLength, rightLength);
            var first = shorter / RowBytes;
            var last = (Math.Max(leftLength, rightLength) - 1) / RowBytes;
            if (blocks.Count > 0 && (long)blocks[^1].Row + blocks[^1].Count >= first)
            {
                blocks[^1] = blocks[^1] with { Count = (int)Math.Min(int.MaxValue, last - blocks[^1].Row + 1) };
            }
            else if (blocks.Count < MaxBlocks)
            {
                blocks.Add(new DiffBlock((int)Math.Min(int.MaxValue, first), (int)Math.Min(int.MaxValue, last - first + 1)));
            }
        }

        return new BinaryDiff(leftLength, rightLength, blocks, bytes, cancelled);

        void AddRow(long row)
        {
            if (row == lastRow)
            {
                return;
            }

            if (row == lastRow + 1 && blocks.Count > 0)
            {
                blocks[^1] = blocks[^1] with { Count = blocks[^1].Count + 1 };
            }
            else if (blocks.Count < MaxBlocks)
            {
                blocks.Add(new DiffBlock((int)Math.Min(int.MaxValue, row), 1));
            }

            lastRow = row;
        }
    }

    private static int Fill(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = stream.Read(buffer, total, buffer.Length - total);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }
}
