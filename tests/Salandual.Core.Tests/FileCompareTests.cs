// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/FileCompareTests.cs

using System.Text;
using Salandual.Core.Compare;

namespace Salandual.Core.Tests;

public sealed class FileCompareTests
{
    private static TextDiff Diff(IReadOnlyList<string> left, IReadOnlyList<string> right, TextDiffOptions? options = null)
        => TextDiff.Compare(left, right, options, cancel: TestContext.Current.CancellationToken);

    private static BinaryDiff Bytes(Stream left, Stream right) => BinaryDiff.Compare(left, right, cancel: TestContext.Current.CancellationToken);

    private static string Shape(TextDiff diff) => string.Join(" ", diff.Rows.Select(r => r.Change switch
    {
        LineChange.Same => "=",
        LineChange.Changed => "~",
        LineChange.SpacingOrCase => "s",
        LineChange.Added => "+",
        _ => "-",
    }));

    [Fact]
    public void TheSameLines_AreAllSame()
    {
        var diff = Diff(["a", "b", "c"], ["a", "b", "c"]);
        Assert.Equal("= = =", Shape(diff));
        Assert.Empty(diff.Blocks);
    }

    [Fact]
    public void TheMockupsPackageJson_LinesUpAsInTheMockup()
    {
        string[] left =
        [
            "{", "  \"name\": \"website\",", "  \"version\": \"1.4.0\",", "  \"private\": true,", "  \"scripts\": {", "    \"build\": \"vite build\",",
            "    \"deploy\": \"pwsh ./deploy.ps1\",", "    \"test\": \"vitest\"", "  },", "  \"dependencies\": {", "    \"marked\": \"12.0.2\",", "    \"vite\": \"5.4.8\"", "  },", "  \"license\": \"MIT\"", "}",
        ];
        string[] right =
        [
            "{", "  \"name\": \"website\",", "  \"version\": \"1.5.0\",", "  \"private\": true,", "  \"scripts\": {", "    \"build\": \"vite build\",",
            "    \"test\": \"vitest run\",", "    \"lint\": \"eslint .\"", "  },", "  \"dependencies\": {", "    \"marked\": \"12.0.2\",", "    \"vite\": \"5.4.10\"", "  },", "  \"license\": \"MIT\"", "}",
        ];
        var diff = Diff(left, right);

        Assert.Equal("= = ~ = = = - ~ + = = = ~ = = =", Shape(diff));
        Assert.Equal(3, diff.Blocks.Count);
        Assert.Equal((3, 1, 1), (diff.Changed, diff.Added, diff.Removed));
        Assert.Equal(new DiffRow(7, 6, LineChange.Changed), diff.Rows[7]);
        Assert.Equal(1, diff.BlockOf(7));
        Assert.Equal(-1, diff.BlockOf(0));
    }

    [Fact]
    public void LinesOnOneSide_AreAddedOrRemoved()
    {
        Assert.Equal("= + + =", Shape(Diff(["a", "d"], ["a", "b", "c", "d"])));
        Assert.Equal("- = -", Shape(Diff(["x", "a", "y"], ["a"])));
        Assert.Equal("+ +", Shape(Diff([], ["a", "b"])));
    }

    [Fact]
    public void WhiteSpaceAndCase_CanBeIgnored()
    {
        string[] left = ["int  x = 1;", "Return x;"];
        string[] right = ["int x=1;", "return x;"];
        Assert.Equal("s s", Shape(Diff(left, right)));
        Assert.Equal("= s", Shape(Diff(left, right, new TextDiffOptions(IgnoreWhiteSpace: true))));
        Assert.Equal("= =", Shape(Diff(left, right, new TextDiffOptions(IgnoreWhiteSpace: true, IgnoreCase: true))));
    }

    [Fact]
    public void ALongFile_WithAFewChanges_IsLinedUpExactly()
    {
        var left = Enumerable.Range(0, 20_000).Select(i => "line " + i).ToList();
        var right = left.ToList();
        right[100] = "changed";
        right.RemoveAt(5000);
        right.Insert(15_000, "new line");
        var diff = Diff(left, right);
        Assert.True(diff.Finished);
        Assert.Equal(3, diff.Blocks.Count);
        Assert.Equal(1, diff.Removed);
        Assert.Equal(1, diff.Added);
        Assert.Equal(1, diff.Changed);
    }

    [Fact]
    public void TwoFilesWithNothingInCommon_StopLookingInTime_AndFaceEachOther()
    {
        var left = Enumerable.Range(0, 60_000).Select(i => "left " + i).ToList();
        var right = Enumerable.Range(0, 60_000).Select(i => "right " + (i * 7)).ToList();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var diff = TextDiff.Compare(left, right, limit: TimeSpan.FromMilliseconds(200), cancel: TestContext.Current.CancellationToken);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), clock.Elapsed.ToString());
        Assert.Equal(60_000, diff.Changed);
        Assert.Single(diff.Blocks);
    }

    [Fact]
    public void RandomEdits_KeepEveryLineOnce_AndInOrder()
    {
        var random = new Random(7);
        for (var round = 0; round < 200; round++)
        {
            var left = Enumerable.Range(0, random.Next(0, 40)).Select(_ => "l" + random.Next(0, 8)).ToList();
            var right = Enumerable.Range(0, random.Next(0, 40)).Select(_ => "l" + random.Next(0, 8)).ToList();
            var diff = Diff(left, right);
            Assert.Equal(Enumerable.Range(0, left.Count), diff.Rows.Where(r => r.Left >= 0).Select(r => r.Left));
            Assert.Equal(Enumerable.Range(0, right.Count), diff.Rows.Where(r => r.Right >= 0).Select(r => r.Right));
            Assert.All(diff.Rows.Where(r => r.Change == LineChange.Same), r => Assert.Equal(left[r.Left], right[r.Right]));

            var same = diff.Rows.Count(r => r.Change == LineChange.Same);
            Assert.Equal(Lcs(left, right), same);
        }

        static int Lcs(List<string> a, List<string> b)
        {
            var t = new int[a.Count + 1, b.Count + 1];
            for (var i = 1; i <= a.Count; i++)
            {
                for (var j = 1; j <= b.Count; j++)
                {
                    t[i, j] = a[i - 1] == b[j - 1] ? t[i - 1, j - 1] + 1 : Math.Max(t[i - 1, j], t[i, j - 1]);
                }
            }

            return t[a.Count, b.Count];
        }
    }

    [Fact]
    public void AChangedLine_LightsTheChangedWords()
    {
        var (left, right) = TextDiff.Inline("    \"test\": \"vitest\"", "    \"test\": \"vitest run\",");
        Assert.Empty(left);
        Assert.Equal([" run", ","], right.Select(p => "    \"test\": \"vitest run\",".Substring(p.Start, p.Length)));

        var (l2, r2) = TextDiff.Inline("\"version\": \"1.4.0\",", "\"version\": \"1.5.0\",");
        Assert.Equal("4", "\"version\": \"1.4.0\",".Substring(l2[0].Start, l2[0].Length));
        Assert.Equal("5", "\"version\": \"1.5.0\",".Substring(r2[0].Start, r2[0].Length));

        var (l3, r3) = TextDiff.Inline("a  =  B", "a = b", new TextDiffOptions(IgnoreWhiteSpace: true, IgnoreCase: true));
        Assert.Empty(l3);
        Assert.Empty(r3);
        Assert.All(right, p => Assert.False(p.SpacingOrCase));
    }

    [Fact]
    public void LinesThatDifferOnlyInSpacingOrCase_AreToldApart()
    {
        string[] left = ["{", "    \"marked\":  \"12.0.2\",", "    \"vite\": \"5.4.8\"", "  \"License\": \"MIT\"", "}"];
        string[] right = ["{", "    \"marked\": \"12.0.2\",", "    \"vite\": \"5.4.10\"", "  \"license\": \"MIT\"", "}"];
        var diff = Diff(left, right);
        Assert.Equal("= s ~ s =", Shape(diff));
        Assert.Equal((1, 2), (diff.Changed, diff.SpacingOrCase));
        Assert.Single(diff.Blocks);

        Assert.Equal("= = ~ s =", Shape(Diff(left, right, new TextDiffOptions(IgnoreWhiteSpace: true))));
        Assert.Equal("= s ~ = =", Shape(Diff(left, right, new TextDiffOptions(IgnoreCase: true))));
        Assert.Equal("= = ~ = =", Shape(Diff(left, right, new TextDiffOptions(IgnoreWhiteSpace: true, IgnoreCase: true))));
    }

    [Fact]
    public void SpacingOrCase_IsLitWholeOnBothSides_InTheSecondColour()
    {
        var (l, r) = TextDiff.Inline("    \"marked\":  \"12.0.2\",", "    \"marked\": \"12.0.2\",");
        Assert.Equal(new InlinePiece(13, 2, true), Assert.Single(l));
        Assert.Equal(new InlinePiece(13, 1, true), Assert.Single(r));

        var (l2, r2) = TextDiff.Inline("  \"License\": \"MIT\"", "  \"license\": \"MIT\"");
        Assert.Equal(new InlinePiece(3, 7, true), Assert.Single(l2));
        Assert.Equal(new InlinePiece(3, 7, true), Assert.Single(r2));

        const string a = "Name = foo;", b = "name = bar;";
        var (l3, r3) = TextDiff.Inline(a, b);
        Assert.Equal([("Name", true), ("foo", false)], l3.Select(p => (a.Substring(p.Start, p.Length), p.SpacingOrCase)));
        Assert.Equal([("name", true), ("bar", false)], r3.Select(p => (b.Substring(p.Start, p.Length), p.SpacingOrCase)));
    }

    [Fact]
    public void CopyingADifferenceAcross_MakesThatPartTheSame()
    {
        string[] left = ["{", "  \"deploy\": 1,", "  \"test\": \"vitest\"", "  },", "x"];
        string[] right = ["{", "  \"test\": \"vitest run\",", "  \"lint\": 2", "  },", "y"];
        var diff = Diff(left, right);
        Assert.Equal("= - ~ + = ~", Shape(diff));
        var block = diff.Blocks[0];

        var target = right.ToList();
        var copied = target.Select(_ => false).ToList();
        Assert.Equal(3, DiffCopy.Apply(diff.Rows, Enumerable.Range(block.Row, block.Count), toRight: true, left, target, copied));
        Assert.Equal(["{", "  \"deploy\": 1,", "  \"test\": \"vitest\"", "  },", "y"], target);
        Assert.Equal([false, true, true, false, false], copied);
        Assert.Equal("= = = = ~", Shape(Diff(left, target)));

        var back = left.ToList();
        var marks = back.Select(_ => false).ToList();
        Assert.Equal(1, DiffCopy.Apply(diff.Rows, [3], toRight: false, right, back, marks));
        Assert.Equal(["{", "  \"deploy\": 1,", "  \"test\": \"vitest\"", "  \"lint\": 2", "  },", "x"], back);
        Assert.Equal([false, false, false, true, false, false], marks);

        var less = right.ToList();
        var none = less.Select(_ => false).ToList();
        Assert.Equal(1, DiffCopy.Apply(diff.Rows, [0, 3], toRight: true, left, less, none));
        Assert.Equal(["{", "  \"test\": \"vitest run\",", "  },", "y"], less);
    }

    [Fact]
    public void CopyingEveryDifference_MakesTheFilesTheSame()
    {
        var random = new Random(7);
        for (var round = 0; round < 50; round++)
        {
            var left = Enumerable.Range(0, random.Next(0, 30)).Select(_ => "l" + random.Next(8)).ToList();
            var right = Enumerable.Range(0, random.Next(0, 30)).Select(_ => "l" + random.Next(8)).ToList();
            var diff = Diff(left, right);
            var toRight = round % 2 == 0;
            var target = (toRight ? right : left).ToList();
            var copied = target.Select(_ => false).ToList();
            DiffCopy.Apply(diff.Rows, Enumerable.Range(0, diff.Rows.Count), toRight, toRight ? left : right, target, copied);
            Assert.Equal(toRight ? left : right, target);
            Assert.Equal(target.Count, copied.Count);
        }
    }

    [Fact]
    public void LinesAreWritten_WithTheFilesLineBreak()
    {
        Assert.Equal("a\r\nb\r\n", DiffCopy.Join(["a", "b"], "CRLF", endsWithBreak: true));
        Assert.Equal("a\nb", DiffCopy.Join(["a", "b"], "LF", endsWithBreak: false));
        Assert.Equal("a\r\nb", DiffCopy.Join(["a", "b"], null, endsWithBreak: false));
        Assert.Equal("", DiffCopy.Join([], "LF", endsWithBreak: true));
    }

    [Fact]
    public void Bytes_DifferingRowsNextToEachOther_AreOneDifference()
    {
        var a = new byte[160];
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = (byte)((i * 37) + 11);
        }

        var b = a.ToArray();
        b[19] ^= 1;
        b[23] ^= 1;
        b[31] ^= 1;
        b[100] ^= 1;
        b[101] ^= 1;
        var bigger = b.Concat(new byte[] { 1, 2, 3, 4 }).ToArray();
        var diff = Bytes(new MemoryStream(a), new MemoryStream(bigger));
        Assert.Equal(5, diff.DifferentBytes);
        Assert.Equal([new DiffBlock(1, 1), new DiffBlock(6, 1), new DiffBlock(10, 1)], diff.Blocks);
        Assert.Equal(11, diff.RowCount);
        Assert.False(diff.Same);
        Assert.Equal(1, diff.BlockOf(6));

        Assert.True(Bytes(new MemoryStream(a), new MemoryStream(a.ToArray())).Same);
    }

    [Fact]
    public void Bytes_ADifferenceRunningIntoTheLongerEnd_IsOne()
    {
        var a = new byte[40];
        var b = new byte[70];
        a[39] = 1;
        var diff = Bytes(new MemoryStream(a), new MemoryStream(b));
        Assert.Equal([new DiffBlock(2, 3)], diff.Blocks);
    }

    [Fact]
    public void AFile_IsReadAsLines_WithItsEncodingAndLineBreak()
    {
        var path = Path.Combine(Path.GetTempPath(), "salandual-cmp-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{\r\n  \"név\": 1\r\n}\r\n")]);
            var file = CompareFile.Read(path, TestContext.Current.CancellationToken);
            Assert.Equal(["{", "  \"név\": 1", "}"], file.Lines);
            Assert.Equal("CRLF", file.LineEnding);
            Assert.Equal(3, file.BomLength);
            Assert.False(file.LooksBinary);
            Assert.Equal(Salandual.Core.Viewer.SyntaxLanguage.Json, file.Language);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Equal(["a", "", "b"], CompareFile.SplitLines("a\n\rb"));
        Assert.Equal(["a", ""], CompareFile.SplitLines("a\r\n\r\n"));
    }
}
