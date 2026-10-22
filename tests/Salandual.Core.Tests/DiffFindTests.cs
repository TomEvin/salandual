// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/DiffFindTests.cs

using System.Text;
using Salandual.Core.Compare;

namespace Salandual.Core.Tests;

public sealed class DiffFindTests
{
    private static readonly string[] Left = ["{", "  \"build\": \"vite build\",", "  \"test\": \"vitest\"", "  \"vite\": \"5.4.8\"", "}"];
    private static readonly string[] Right = ["{", "  \"build\": \"vite build\",", "  \"test\": \"vitest run\",", "  \"lint\": \"eslint .\"", "  \"vite\": \"5.4.10\"", "}"];

    private static TextDiff Diff() => TextDiff.Compare(Left, Right, cancel: TestContext.Current.CancellationToken);

    [Fact]
    public void Matches_InBothFiles_InTheOrderOfTheRows()
    {
        var diff = Diff();
        var hits = DiffFind.Text(diff.Rows, Left, Right, new DiffFindQuery("vite"), TestContext.Current.CancellationToken)!;
        Assert.Equal(6, hits.Count);
        Assert.Equal(new DiffHit(1, 0, 12, 4), hits[0]);
        Assert.Equal(new DiffHit(1, 1, 12, 4), hits[1]);
        Assert.True(hits.Zip(hits.Skip(1)).All(p => p.First.Row < p.Second.Row || (p.First.Row == p.Second.Row && p.First.Side <= p.Second.Side)));

        Assert.Equal(4, DiffFind.Text(diff.Rows, Left, Right, new DiffFindQuery("vite", WholeWords: true), TestContext.Current.CancellationToken)!.Count);
        Assert.All(DiffFind.Text(diff.Rows, Left, Right, new DiffFindQuery("vite", Sides: FindSides.Right), TestContext.Current.CancellationToken)!, h => Assert.Equal(1, h.Side));
        var only = DiffFind.Text(diff.Rows, Left, Right, new DiffFindQuery("vite", OnlyDifferences: true), TestContext.Current.CancellationToken)!;
        Assert.All(only, h => Assert.NotEqual(LineChange.Same, diff.Rows[(int)h.Row].Change));
        Assert.Equal(4, only.Count);
    }

    [Fact]
    public void Case_RegularExpressions_AndABrokenOne()
    {
        var diff = Diff();
        Assert.Empty(DiffFind.Text(diff.Rows, Left, Right, new DiffFindQuery("VITE", MatchCase: true), TestContext.Current.CancellationToken)!);
        Assert.Equal(2, DiffFind.Text(diff.Rows, Left, Right, new DiffFindQuery(@"5\.4\.\d+", Regex: true), TestContext.Current.CancellationToken)!.Count);
        Assert.Null(DiffFind.Text(diff.Rows, Left, Right, new DiffFindQuery("(", Regex: true), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Bytes_TextOrHex_AcrossPieces()
    {
        var a = new byte[3_000_000];
        new Random(3).NextBytes(a);
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] is (byte)'I' or (byte)'i')
            {
                a[i] = 0;
            }
        }

        var b = a.ToArray();
        Encoding.ASCII.GetBytes("IHDR").CopyTo(a, 12);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(a, (1 << 20) - 2);
        Encoding.ASCII.GetBytes("ihdr").CopyTo(b, 100);
        b[5000] ^= 1;
        using var left = new MemoryStream(a);
        using var right = new MemoryStream(b);
        var diff = BinaryDiff.Compare(left, right, cancel: TestContext.Current.CancellationToken);

        var text = DiffFind.Bytes(left, right, diff, new DiffFindQuery("ihdr"), TestContext.Current.CancellationToken)!;
        Assert.Equal([(0L, 12L), (0, (1 << 20) - 2), (1, 100)], text.Select(h => ((long)h.Side, h.Start)).OrderBy(x => x.Item1).ThenBy(x => x.Item2));
        Assert.Equal(new DiffHit(0, 0, 12, 4), text[0]);

        var hex = DiffFind.Bytes(left, right, diff, new DiffFindQuery("49 48 44 52", HexBytes: true), TestContext.Current.CancellationToken)!;
        Assert.Equal(2, hex.Count);
        Assert.Null(DiffFind.Bytes(left, right, diff, new DiffFindQuery("4 9 4", HexBytes: true), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 0x49, 0xAB }, DiffFind.Pattern(new DiffFindQuery("0x49ab", HexBytes: true)));
    }
}
