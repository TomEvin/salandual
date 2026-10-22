// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/ListingTests.cs

using System.Globalization;
using Salandual.Core.Listing;
using Salandual.Core.Vfs;

namespace Salandual.Core.Tests;

public sealed class ListingTests
{
    private static readonly EntrySorter _hu = new(CultureInfo.GetCultureInfo("hu-HU"), numericSort: true);
    private static readonly EntrySorter _en = new(CultureInfo.GetCultureInfo("en-US"), numericSort: true);

    private static FileEntry F(string name, long size = 0, int day = 1) => new(name, size, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc).Ticks, FileAttributes.Normal);

    private static FileEntry D(string name, int day = 1) => new(name, 0, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc).Ticks, FileAttributes.Directory);

    private static string[] Names(FileEntry[] e) => [.. e.Select(x => x.Name)];

    [Theory]
    [InlineData("report.pdf", "report", "pdf")]
    [InlineData("archive.tar.gz", "archive.tar", "gz")]
    [InlineData(".gitignore", ".gitignore", "")]
    [InlineData("noext", "noext", "")]
    [InlineData("trailing dot.", "trailing dot.", "")]
    [InlineData("ÁRVÍZTŰRŐ.TXT", "ÁRVÍZTŰRŐ", "TXT")]
    public void Extension_SplitsAtLastDot(string name, string baseName, string ext)
    {
        var e = F(name);
        Assert.Equal(baseName, e.BaseName.ToString());
        Assert.Equal(ext, e.Extension.ToString());
    }

    [Fact]
    public void Folders_HaveNoExtension()
    {
        var d = D("folder.v2");
        Assert.Equal("", d.Extension.ToString());
        Assert.Equal("folder.v2", d.BaseName.ToString());
    }

    [Fact]
    public void ByName_FoldersFirst_NumericAware_CaseInsensitive()
    {
        FileEntry[] e = [F("IMG_10.jpg"), F("img_2.jpg"), D("zeta"), F("IMG_1.jpg"), D("Alpha")];
        _en.Sort(e, SortSpec.Default);
        Assert.Equal(["Alpha", "zeta", "IMG_1.jpg", "img_2.jpg", "IMG_10.jpg"], Names(e));
    }

    [Fact]
    public void ByName_Descending_KeepsFoldersFirst()
    {
        FileEntry[] e = [F("a"), D("x"), F("b"), D("y")];
        _en.Sort(e, new SortSpec(SortColumn.Name, Descending: true));
        Assert.Equal(["y", "x", "b", "a"], Names(e));
    }

    [Fact]
    public void Hungarian_Collation_PutsAccentedLettersInPlace()
    {
        FileEntry[] e = [F("csiga"), F("cukor"), F("öreg"), F("ló"), F("orr")];
        _hu.Sort(e, SortSpec.Default);
        Assert.Equal(["cukor", "csiga", "ló", "orr", "öreg"], Names(e));
    }

    [Fact]
    public void EqualByCulture_TieBreaksOrdinally_AndIsStable()
    {
        var nfc = F("café.txt");
        var nfd = F("café.txt");
        FileEntry[] a = [nfc, nfd];
        FileEntry[] b = [nfd, nfc];
        _en.Sort(a, SortSpec.Default);
        _en.Sort(b, SortSpec.Default);
        Assert.Equal(Names(a), Names(b));
        Assert.Equal("café.txt", a[0].Name);
    }

    [Fact]
    public void BySize_FilesBySize_FoldersByName()
    {
        FileEntry[] e = [F("big", 900), D("b"), F("small", 10), D("a"), F("mid", 50)];
        _en.Sort(e, new SortSpec(SortColumn.Size));
        Assert.Equal(["a", "b", "small", "mid", "big"], Names(e));
    }

    [Fact]
    public void ByExtension_ThenName()
    {
        FileEntry[] e = [F("b.txt"), F("a.zip"), F("noext"), F("a.txt"), F("c.JPG")];
        _en.Sort(e, new SortSpec(SortColumn.Extension));
        Assert.Equal(["noext", "c.JPG", "a.txt", "b.txt", "a.zip"], Names(e));
    }

    [Fact]
    public void ByTime_ThenName()
    {
        FileEntry[] e = [F("new", day: 9), F("old", day: 2), F("b-same", day: 5), F("a-same", day: 5)];
        _en.Sort(e, new SortSpec(SortColumn.Time));
        Assert.Equal(["old", "a-same", "b-same", "new"], Names(e));
    }

    [Theory]
    [InlineData(SortColumn.Name, false)]
    [InlineData(SortColumn.Name, true)]
    [InlineData(SortColumn.Extension, false)]
    [InlineData(SortColumn.Size, true)]
    [InlineData(SortColumn.Time, false)]
    public void SortKeyPath_GivesSameOrder_AsDirectComparison(SortColumn column, bool descending)
    {
        var random = new Random(12345);
        string[] parts = ["IMG_", "árvíz", "Árvíz", "café", "café", "日本", "😀", "doc", "Doc", "a.b", "x"];
        string[] exts = ["", ".txt", ".TXT", ".jpg", ".tar.gz", "."];
        var entries = new FileEntry[3000];
        for (var i = 0; i < entries.Length; i++)
        {
            var name = parts[random.Next(parts.Length)] + random.Next(200) + exts[random.Next(exts.Length)];
            var attributes = random.Next(8) == 0 ? FileAttributes.Directory : FileAttributes.Archive;
            entries[i] = new FileEntry(name, random.Next(5) * 100, random.Next(3) * TimeSpan.TicksPerDay, attributes);
        }

        var spec = new SortSpec(column, descending);
        var direct = (FileEntry[])entries.Clone();
        var keyed = (FileEntry[])entries.Clone();
        _hu.Sort(direct, spec, sortKeyThreshold: int.MaxValue);
        _hu.Sort(keyed, spec, sortKeyThreshold: 0);

        Assert.Equal(direct, keyed);
    }

    [Fact]
    public void SortSpec_Toggle_ReversesSameColumn_ResetsOther()
    {
        var s = SortSpec.Default.Toggle(SortColumn.Name);
        Assert.True(s.Descending);
        Assert.Equal(new SortSpec(SortColumn.Size), s.Toggle(SortColumn.Size));
    }

    [Fact]
    public void Filter_HidesHiddenAndSystem_UnlessShown()
    {
        FileEntry[] e = [F("a"), new("h", 0, 0, FileAttributes.Hidden), new("s", 0, 0, FileAttributes.System | FileAttributes.Directory)];
        Assert.Equal(["a"], Names(EntryFilter.Apply(e, showHidden: false)));
        Assert.Equal(3, EntryFilter.Apply(e, showHidden: true).Length);
    }

    [Fact]
    public async Task Coalescer_CombinesBurst_IntoOneSignal()
    {
        var fired = new List<(int Count, bool Overflow)>();
        var done = new TaskCompletionSource();
        using var c = new ChangeCoalescer(TimeSpan.FromMilliseconds(80), TimeSpan.FromSeconds(5), (n, o) =>
        {
            lock (fired)
            {
                fired.Add((n, o));
            }

            done.TrySetResult();
        });

        for (var i = 0; i < 20; i++)
        {
            c.Add(overflowed: i == 7);
        }

        await done.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        lock (fired)
        {
            Assert.Equal([(20, true)], fired);
        }
    }

    [Fact]
    public async Task Coalescer_ContinuousChanges_StillFireByMaxDelay()
    {
        var firedAt = new TaskCompletionSource<long>();
        using var c = new ChangeCoalescer(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(300), (_, _) => firedAt.TrySetResult(Environment.TickCount64));
        var start = Environment.TickCount64;

        while (!firedAt.Task.IsCompleted && Environment.TickCount64 - start < 3000)
        {
            c.Add();
            await Task.Delay(30, TestContext.Current.CancellationToken);
        }

        var elapsed = await firedAt.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken) - start;
        Assert.InRange(elapsed, 250, 1500);
    }
}
