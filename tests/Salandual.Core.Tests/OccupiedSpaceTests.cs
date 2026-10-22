// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/OccupiedSpaceTests.cs

using Salandual.Core.Listing;

namespace Salandual.Core.Tests;

public sealed class OccupiedSpaceTests : IDisposable
{
    private static readonly DateTime _when = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-space-").FullName;

    public OccupiedSpaceTests()
    {
        Write("a.txt", 10);
        Write("b.bin", 5000);
        Write(Path.Join("big", "one.bin"), 9000);
        Write(Path.Join("big", "deep", "two.bin"), 4097);
        Write(Path.Join("small", "x.txt"), 1);
        Directory.CreateDirectory(Path.Join(_dir, "empty"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string name, int bytes)
    {
        var path = Path.Join(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private FileListItem Item(string name)
    {
        var path = Path.Join(_dir, name);
        return Directory.Exists(path)
            ? new FileListItem(path, 0, _when, FileAttributes.Directory)
            : new FileListItem(path, new FileInfo(path).Length, _when, FileAttributes.Archive);
    }

    [Fact]
    public void TheSelection_FilesAndWhatIsInsideTheFolders()
    {
        var result = OccupiedSpace.Count([Item("a.txt"), Item("big"), Item("empty")], 4096, null, null, CancellationToken.None);
        var t = result.Totals;
        Assert.Equal(3, t.Files);
        Assert.Equal(3, t.Folders);
        Assert.Equal(10 + 9000 + 4097, t.Bytes);
        Assert.Equal(4096 + 12288 + 8192, t.OnDisk);
        Assert.Null(result.Inside);
        Assert.Equal(["a.txt|10", "big|13097", "empty|0"], result.Parts.Select(p => p.Name + "|" + p.Bytes));
    }

    [Fact]
    public void OneFolder_TheChartShowsWhatIsInIt()
    {
        var result = OccupiedSpace.Count([new FileListItem(_dir, 0, _when, FileAttributes.Directory)], 4096, null, null, CancellationToken.None);
        Assert.Equal(Path.GetFileName(_dir), result.Inside);
        Assert.Equal(5, result.Totals.Files);
        Assert.Equal(5, result.Totals.Folders);
        Assert.Equal(["a.txt", "b.bin", "big", "empty", "small"], result.Parts.Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheChart_TheFiveBiggest_ThenTheRest()
    {
        var parts = new SpacePart[] { new("f1", true, 600), new("f2", true, 300), new("f3", true, 100), new("f4", true, 90), new("f5", true, 40), new("x.txt", false, 30), new("y.txt", false, 20), new("zero", true, 0) };
        var (biggest, rest, restIsFiles, restCount) = new SpaceResult(SpaceTotals.None, parts, null).Slices();
        Assert.Equal(["f1", "f2", "f3", "f4", "f5"], biggest.Select(p => p.Name));
        Assert.Equal(50, rest);
        Assert.True(restIsFiles);
        Assert.Equal(2, restCount);

        parts[6] = new("g", true, 20);
        Assert.False(new SpaceResult(SpaceTotals.None, parts, null).Slices().RestIsFiles);
    }

    [Fact]
    public void AnotherClusterSize_WhatItWouldTake()
    {
        var t = OccupiedSpace.Count([Item("a.txt"), Item("b.bin")], 4096, null, null, CancellationToken.None).Totals;
        Assert.Equal(512 + 5120, t.EstimateFor(512));
        Assert.Equal(4096 + 8192, t.EstimateFor(4096));
        Assert.Equal(2 * 131072, t.EstimateFor(131072));
        Assert.Equal(0, t.EstimateFor(3000));
    }

    [Fact]
    public void Compressed_WhatItReallyTakes_AndWhatItSaves()
    {
        var packed = new FileListItem(@"C:\x\packed.log", 100_000, _when, FileAttributes.Compressed);
        var t = OccupiedSpace.Count([packed, new FileListItem(@"C:\x\plain.txt", 100, _when, FileAttributes.Archive)], 4096, p => p.EndsWith("packed.log", StringComparison.Ordinal) ? 30_000 : -1, null, CancellationToken.None).Totals;
        Assert.Equal(32768 + 4096, t.OnDisk);
        Assert.Equal(1, t.CompressedFiles);
        Assert.Equal(70_000, t.CompressionSaves);
    }

    [Fact]
    public void OnlyInTheCloud_NothingOnThisPc_NeverOpened()
    {
        var asked = new List<string>();
        var online = new FileListItem(@"C:\OneDrive\video.mp4", 340_000_000, _when, (FileAttributes)0x00400000 | FileAttributes.Compressed);
        var t = OccupiedSpace.Count([online], 4096, p => { asked.Add(p); return 0; }, null, CancellationToken.None).Totals;
        Assert.Equal((1, 340_000_000L, 0L), (t.OnlineFiles, t.OnlineBytes, t.OnDisk));
        Assert.Equal(340_000_000, t.Bytes);
        Assert.Empty(asked);
    }

    [Fact]
    public void ClosedWhileCounting_Stops()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => OccupiedSpace.Count([Item("big")], 4096, null, null, cancel.Token));
    }

    [Theory]
    [InlineData(4096, 4096)]
    [InlineData(0, 4096)]
    [InlineData(2000, 2048)]
    [InlineData(1L << 20, 131072)]
    public void TheClusterSizeToStartWith_TheNearest(long cluster, long nearest) => Assert.Equal(nearest, OccupiedSpace.Nearest(cluster));
}
