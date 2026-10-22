// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/ViewerTests.cs

using System.Text;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.Core.Tests;

public sealed class ViewerTests
{
    private static TextLines Lines(byte[] bytes, TextEncoding? encoding = null, int start = 0)
        => new(new CachedBytes(new MemoryByteSource(bytes)), encoding ?? TextEncodings.Find(TextEncodings.Utf8)!, start);

    private static List<long> Forward(TextLines lines)
    {
        var starts = new List<long>();
        for (var at = lines.Start; at < lines.Length;)
        {
            starts.Add(at);
            var next = lines.LineAt(at).Next;
            Assert.True(next > at);
            at = next;
        }

        return starts;
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' }, TextEncodings.Utf8, 3, false)]
    [InlineData(new byte[] { 0xFF, 0xFE, (byte)'a', 0 }, TextEncodings.Utf16LE, 2, false)]
    [InlineData(new byte[] { (byte)'a', 0, (byte)'b', 0, (byte)'c', 0, (byte)'\n', 0 }, TextEncodings.Utf16LE, 0, false)]
    [InlineData(new byte[] { 0, (byte)'a', 0, (byte)'b', 0, (byte)'c', 0, (byte)'\n' }, TextEncodings.Utf16BE, 0, false)]
    [InlineData(new byte[] { (byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0, 0xFF, 0xFF, 0, 0 }, null, 0, true)]
    public void Detection_ReadsBomsUtf16AndBinary(byte[] start, string? id, int bom, bool binary)
    {
        var guess = TextEncodings.Detect(start);
        Assert.Equal(binary, guess.IsBinary);
        Assert.Equal(bom, guess.BomLength);
        if (id is not null)
        {
            Assert.Equal(id, guess.Encoding.Id);
        }
    }

    [Fact]
    public void Detection_TellsUtf8FromTheAnsiCodePage()
    {
        Assert.Equal(TextEncodings.Utf8, TextEncodings.Detect(Encoding.UTF8.GetBytes("árvíztűrő tükörfúrógép\r\n")).Encoding.Id);
        var ansi = TextEncodings.Detect([(byte)'a', 0xE1, (byte)'r', (byte)'v', 0xED, (byte)'z']);
        Assert.False(ansi.IsBinary);
        Assert.Equal(TextEncodings.Ansi.Id, ansi.Encoding.Id);
        Assert.Equal("CRLF", TextEncodings.LineEnding("a\r\nb"u8, TextEncodings.Default));
        Assert.Equal("LF", TextEncodings.LineEnding("a\nb"u8, TextEncodings.Default));
    }

    [Fact]
    public void Encodings_StepAroundWithF8()
    {
        var first = TextEncodings.All[0];
        Assert.Equal(TextEncodings.All[1], TextEncodings.Step(first, 1));
        Assert.Equal(TextEncodings.All[^1], TextEncodings.Step(first, -1));
        Assert.All(TextEncodings.All.Skip(3), e => Assert.True(e.SingleByte, e.Name));
    }

    [Theory]
    [InlineData("one\r\ntwo\nthree\rfour", 4)]
    [InlineData("one\r\n\r\n\r\nlast\r\n", 4)]
    [InlineData("", 0)]
    [InlineData("\n", 1)]
    public void Lines_EndAtLfCrLfAndCr(string text, int count)
    {
        var lines = Lines(Encoding.UTF8.GetBytes(text));
        Assert.Equal(count, Forward(lines).Count);
    }

    [Theory]
    [InlineData(TextEncodings.Utf8)]
    [InlineData(TextEncodings.Utf16LE)]
    [InlineData(TextEncodings.Utf16BE)]
    public void Previous_FindsTheSameStarts_AsReadingForward_EvenAcrossLongLines(string id)
    {
        var encoding = TextEncodings.Find(id)!;
        var text = new StringBuilder();
        var random = new Random(7);
        for (var i = 0; i < 300; i++)
        {
            var length = i % 50 == 0 ? 40_000 : random.Next(0, 80);
            text.Append(new string('é', length / 2)).Append(new string('x', length - (length / 2)));
            text.Append((i % 3) switch { 0 => "\r\n", 1 => "\n", _ => "\r" });
        }

        var lines = Lines(encoding.Encoding.GetBytes(text.ToString()), encoding);
        var forward = Forward(lines);
        var backward = new List<long>();
        for (long? at = lines.Previous(lines.Length); at is not null; at = lines.Previous(at.Value))
        {
            backward.Add(at.Value);
        }

        backward.Reverse();
        Assert.Equal(forward, backward);
        Assert.All(forward, s => Assert.Equal(s, lines.LineStartOf(s)));
        Assert.Equal(forward[5], lines.LineStartOf(forward[6] - 1));
    }

    [Fact]
    public void LineStartOf_InsideACrLf_IsThatLinesStart()
    {
        var lines = Lines("ab\r\ncd"u8.ToArray());
        Assert.Equal(0, lines.LineStartOf(3));
        Assert.Equal(4, lines.LineStartOf(4));
    }

    [Fact]
    public void Decode_ExpandsTabs_HidesControls_AndMapsColumnsToBytes()
    {
        var lines = Lines(Encoding.UTF8.GetBytes("a\tűb\u0001"));
        var line = lines.Decode(0, lines.Length, 4);
        Assert.Equal("a   űb·", line.Text);
        Assert.Equal(1, line.ColumnOf(1));
        Assert.Equal(4, line.ColumnOf(2));
        Assert.Equal(5, line.ColumnOf(4));
        Assert.Equal(lines.Length, line.ByteOf[^1]);
    }

    [Fact]
    public async Task LineIndex_CountsInTheBackground_AndMapsBothWays()
    {
        var text = string.Concat(Enumerable.Range(1, 5000).Select(i => $"line {i}\n"));
        var lines = Lines(Encoding.UTF8.GetBytes(text));
        using var index = new LineIndex(lines);
        index.Start();
        await index.Completion;

        Assert.True(index.IsComplete);
        Assert.Equal(5000, index.Count);
        var start = index.StartOf(3000)!.Value;
        Assert.Equal("line 3001", lines.Decode(start, lines.LineAt(start).End, 8).Text);
        Assert.Equal(3000, index.NumberOf(start));
        Assert.Equal(1.0, index.Fraction);
    }

    [Fact]
    public void Find_NextPreviousAndCount_InTextAndHex()
    {
        var lines = Lines(Encoding.UTF8.GetBytes("Magyarán one\nmagyarán two\nMAGYARÁNS three\n"));
        var plain = new FindQuery("magyarán");
        var first = ViewerFind.Next(lines, plain, 0, token: TestContext.Current.CancellationToken)!.Value;
        Assert.Equal(0, first.Start);
        Assert.Equal(9, first.End);

        var second = ViewerFind.Next(lines, plain, first.Start + 1, token: TestContext.Current.CancellationToken)!.Value;
        Assert.Equal(14, second.Start);
        Assert.Equal(second, ViewerFind.Previous(lines, plain, 28, token: TestContext.Current.CancellationToken));
        Assert.Equal((3L, 1L, false), ViewerFind.Count(lines, plain, second.Start, token: TestContext.Current.CancellationToken));

        Assert.Equal((1L, 0L, false), ViewerFind.Count(lines, new FindQuery("magyarán", MatchCase: true), 0, token: TestContext.Current.CancellationToken));
        Assert.Equal((2L, 0L, false), ViewerFind.Count(lines, new FindQuery("magyarán", WholeWords: true), 0, token: TestContext.Current.CancellationToken));
        Assert.Equal((2L, 0L, false), ViewerFind.Count(lines, new FindQuery(@"^m\w+ \w{3}$", IsRegex: true), 0, token: TestContext.Current.CancellationToken));
        Assert.Null(ViewerFind.Next(lines, new FindQuery("(", IsRegex: true), 0, token: TestContext.Current.CancellationToken));

        var hex = new FindQuery("72 C3 A1 6E", HexBytes: true);
        Assert.Equal(new FindHit(5, 9), ViewerFind.Next(lines, hex, 0, token: TestContext.Current.CancellationToken));
        Assert.Equal(new FindHit(19, 23), ViewerFind.Previous(lines, hex, 30, token: TestContext.Current.CancellationToken));
        Assert.Null(new FindQuery("6C C", HexBytes: true).Bytes);
    }

    [Fact]
    public void SelectionCopy_HexCArrayBase64()
    {
        byte[] bytes = [0x4D, 0x5A, 0x00];
        Assert.Equal("4D 5A 00", SelectionCopy.Format(bytes, CopyFormat.HexBytes));
        Assert.Equal("{ 0x4D, 0x5A, 0x00 }", SelectionCopy.Format(bytes, CopyFormat.CArray));
        Assert.Equal("TVoA", SelectionCopy.Format(bytes, CopyFormat.Base64));
        Assert.Contains(",\n  0x10", SelectionCopy.Format([.. Enumerable.Range(0, 17).Select(i => (byte)i)], CopyFormat.CArray), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1245", false, 1245L, false)]
    [InlineData("0x4A3F0", false, 0x4A3F0L, true)]
    [InlineData("$ff", false, 255L, true)]
    [InlineData("10h", true, 16L, true)]
    [InlineData("100", true, 100L, true)]
    [InlineData("4A3F0", true, 0x4A3F0L, true)]
    public void GoTo_TakesLinesAndOffsets(string text, bool hexView, long value, bool isOffset)
        => Assert.Equal(new GoToTarget(value, isOffset), GoToTarget.Parse(text, hexView));

    [Fact]
    public void GoTo_HexOrDecimalSwitch_ReadsPlainDigits_AndRebasesInPlace()
    {
        Assert.Equal(new GoToTarget(0x100, true), GoToTarget.Parse("100", hexView: true, hexDigits: true));
        Assert.Equal(new GoToTarget(100, true), GoToTarget.Parse("100", hexView: true, hexDigits: false));
        Assert.Null(GoToTarget.Parse("4A", hexView: true, hexDigits: false));
        Assert.Equal(new GoToTarget(0x4A, true), GoToTarget.Parse("0x4A", hexView: true, hexDigits: false));

        Assert.Equal("304112", GoToTarget.Rebase("4A3F0", toHex: false));
        Assert.Equal("4A3F0", GoToTarget.Rebase("304112", toHex: true));
        Assert.Equal("4A3F0", GoToTarget.Rebase("0x4A3F0", toHex: true));
        Assert.Equal("", GoToTarget.Rebase("", toHex: true));
        Assert.Equal("4G", GoToTarget.Rebase("4G", toHex: false));
    }

    [Fact]
    public void MaskTable_FirstFitWins_AndNormalizes()
    {
        var settings = new ViewerSettings();
        Assert.Equal(ViewerKind.Image, settings.KindFor("DSC04218.ARW", alternate: false));
        Assert.Equal(ViewerKind.Hex, settings.KindFor("DSC04218.ARW", alternate: true));
        Assert.Equal(ViewerKind.Markdown, settings.KindFor("README.md", alternate: false));
        Assert.Equal(ViewerKind.Text, settings.KindFor("README.md", alternate: true));
        Assert.Equal(ViewerKind.Automatic, settings.KindFor("build.log", alternate: false));
        Assert.Equal(settings.RowFor("a.mp4"), settings.RowFor("b.flac"));

        var overlapping = new ViewerSettings { Masks = [.. ViewerSettings.DefaultMasks().Take(3), new ViewerMask { Mask = "*.svg;*.txt;*.*" }, new ViewerMask { Mask = "*.*" }] };
        Assert.Equal([("*.svg", 0)], overlapping.TakenAbove(3));
        Assert.Empty(overlapping.TakenAbove(0));
        Assert.Empty(overlapping.TakenAbove(4));

        var odd = new ViewerSettings { Masks = [], HexBytesPerRow = 17, HexGroup = 3, TabWidth = 5 }.Normalize();
        Assert.Equal(ViewerSettings.DefaultMasks().Count, odd.Masks.Count);
        Assert.Equal((16, 8, 8), (odd.HexBytesPerRow, odd.HexGroup, odd.TabWidth));
    }

    [Fact]
    public void ByteKinds_TwoColours_AsBefore_ByKind_FiveKinds()
    {
        Assert.Equal(ByteKind.Zero, ByteKinds.Of(0x00, ByteColors.Two));
        Assert.Equal(ByteKind.Zero, ByteKinds.Of(0x0A, ByteColors.Two));
        Assert.Equal(ByteKind.Zero, ByteKinds.Of(0x0D, ByteColors.Two));
        Assert.All(new byte[] { 0x01, 0x09, 0x41, 0x7F, 0x80, 0xFE, 0xFF }, b => Assert.Equal(ByteKind.Text, ByteKinds.Of(b, ByteColors.Two)));

        Assert.Equal(ByteKind.Zero, ByteKinds.Of(0x00, ByteColors.ByKind));
        Assert.All(new byte[] { 0x20, 0x41, 0x7E }, b => Assert.Equal(ByteKind.Text, ByteKinds.Of(b, ByteColors.ByKind)));
        Assert.All(new byte[] { 0x01, 0x09, 0x0A, 0x0D, 0x1F, 0x7F }, b => Assert.Equal(ByteKind.Control, ByteKinds.Of(b, ByteColors.ByKind)));
        Assert.All(new byte[] { 0x80, 0xA1, 0xFE }, b => Assert.Equal(ByteKind.High, ByteKinds.Of(b, ByteColors.ByKind)));
        Assert.Equal(ByteKind.FF, ByteKinds.Of(0xFF, ByteColors.ByKind));
        Assert.Equal(ByteColors.Two, new ViewerSettings().HexByteColors);
    }
}
