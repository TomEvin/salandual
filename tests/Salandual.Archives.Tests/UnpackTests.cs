// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Archives.Tests/UnpackTests.cs

using System.IO.Compression;
using System.Text;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Archives.Tests;

public sealed class UnpackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "salandual-unpack-tests-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _out;
    private readonly LocalOperations _fs = new();

    public UnpackTests()
    {
        _out = Path.Combine(_dir, "a", "b", "out");
        Directory.CreateDirectory(_out);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    [InlineData("tar")]
    [InlineData("rar")]
    public async Task UnsafeNamesAreRefusedAndTheRestIsUnpacked(string kind)
    {
        var archive = Path.Combine(_dir, "crafted." + kind);
        var entries = CraftedArchives.Names.Select(n => (n, Encoding.UTF8.GetBytes("data of " + n))).ToList();
        switch (kind)
        {
            case "zip":
                CraftedArchives.Zip(archive, entries);
                break;
            case "tar":
                CraftedArchives.Tar(archive, CraftedArchives.Names.Select(n => CraftedArchives.TarFile(n, "data of " + n)));
                break;
            case "rar":
                CraftedArchives.Rar5(archive, entries);
                break;
            default:
                File.Copy(ArchiveReaderTests.Fixture("crafted.7z"), archive);
                break;
        }

        var job = await RunAsync(Source(archive), new UnpackOptions { IfExists = ExistingFilePolicy.Overwrite });

        Assert.Equal(JobState.DoneWithIssues, job.State);
        Assert.True(File.Exists(Path.Combine(_out, "Summer", "ok.txt")));
        Assert.Equal([Path.Combine(_out, "Summer", "ok.txt")], Directory.GetFiles(_dir, "*", SearchOption.AllDirectories).Where(f => f != archive).ToArray());
        var reasons = job.Issues.Select(i => i.Message).ToList();
        Assert.Equal(4, reasons.Count);
        Assert.Contains(Strings.Unpack_Refused_LeavesTarget, reasons);
        Assert.Contains(Strings.Unpack_Refused_FullPath, reasons);
        Assert.Contains(Loc.Format(Strings.Unpack_Refused_Device, "CON"), reasons);
        Assert.Contains(Strings.Unpack_Refused_Stream, reasons);
    }

    [Fact]
    public async Task LinksAreMadeOnlyInsideTheTarget()
    {
        var archive = Path.Combine(_dir, "links.tar");
        CraftedArchives.Tar(archive,
        [
            CraftedArchives.TarFile("Summer/ok.txt", "fine"),
            CraftedArchives.TarLink("Summer/docs", "../../../../outside"),
            CraftedArchives.TarLink("Summer/abs", "C:/Users"),
            CraftedArchives.TarLink("Summer/again.txt", "Summer/ok.txt", hard: true),
            CraftedArchives.TarLink("Summer/near.txt", "ok.txt"),
        ]);

        var job = await RunAsync(Source(archive), new UnpackOptions());

        var summer = Path.Combine(_out, "Summer");
        Assert.Equal("fine", File.ReadAllText(Path.Combine(summer, "again.txt")));
        Assert.False(Path.Exists(Path.Combine(summer, "docs")));
        Assert.False(Path.Exists(Path.Combine(summer, "abs")));
        var issues = job.Issues.ToDictionary(i => i.Path, i => i.Message);
        Assert.Equal(Strings.Unpack_Refused_LinkOutside, issues["Summer\\docs → ../../../../outside"]);
        Assert.Equal(Strings.Unpack_Refused_LinkOutside, issues["Summer\\abs → C:/Users"]);

        var near = Path.Combine(summer, "near.txt");
        if (File.Exists(near))
        {
            Assert.Equal("ok.txt", new FileInfo(near).LinkTarget);
        }
        else
        {
            Assert.Equal(Strings.Unpack_LinkNotAllowed, issues["Summer\\near.txt → ok.txt"]);
        }
    }

    [Fact]
    public async Task UnpackMakesAFolderForTheArchiveOnlyWhenItIsNeeded()
    {
        var plain = Path.Combine(_dir, "plain.7z");
        File.Copy(ArchiveReaderTests.Fixture("plain.7z"), plain);
        var one = Path.Combine(_dir, "one.zip");
        CraftedArchives.Zip(one, [("Photos/a.txt", [1]), ("Photos/b.txt", [2])]);

        var plan = ArchiveUnpacker.Plan(Source(plain), new UnpackOptions());
        Assert.True(plan.NeedsFolder);
        Assert.Equal("plain", ArchiveUnpacker.FolderFor(plan, UnpackFolderRule.WhenNeeded, "plain.7z"));
        Assert.Null(ArchiveUnpacker.FolderFor(plan, UnpackFolderRule.Never, "plain.7z"));
        Assert.Null(ArchiveUnpacker.FolderFor(ArchiveUnpacker.Plan(Source(one), new UnpackOptions()), UnpackFolderRule.WhenNeeded, "one.zip"));
        Assert.Equal("one", ArchiveUnpacker.FolderFor(ArchiveUnpacker.Plan(Source(one), new UnpackOptions()), UnpackFolderRule.Always, "one.zip"));

        await RunAsync(Source(plain), new UnpackOptions { Folder = UnpackFolderRule.WhenNeeded });
        await RunAsync(Source(one), new UnpackOptions { Folder = UnpackFolderRule.WhenNeeded });

        Assert.True(File.Exists(Path.Combine(_out, "plain", "top.txt")));
        Assert.True(File.Exists(Path.Combine(_out, "plain", "docs", "readme.txt")));
        Assert.True(File.Exists(Path.Combine(_out, "Photos", "a.txt")));
    }

    [Fact]
    public async Task TheSelectedEntriesOfAFolderGoStraightIntoTheTarget()
    {
        var plain = Path.Combine(_dir, "plain.7z");
        File.Copy(ArchiveReaderTests.Fixture("plain.7z"), plain);
        var contents = ArchiveReader.Open(plain, "plain.7z", null, cancellation: TestContext.Current.CancellationToken);

        await RunAsync(new UnpackSource(contents, plain, plain, "", ["docs"]), new UnpackOptions());
        Assert.True(File.Exists(Path.Combine(_out, "docs", "readme.txt")));
        Assert.False(File.Exists(Path.Combine(_out, "top.txt")));

        await RunAsync(new UnpackSource(contents, plain, plain, "docs", ["readme.txt"]), new UnpackOptions { IfExists = ExistingFilePolicy.KeepBoth });
        Assert.True(File.Exists(Path.Combine(_out, "readme.txt")));

        await RunAsync(new UnpackSource(contents, plain, plain, "docs", ["readme.txt"]), new UnpackOptions { IfExists = ExistingFilePolicy.KeepBoth });
        Assert.True(File.Exists(Path.Combine(_out, "readme (2).txt")));

        var flat = Path.Combine(_out, "flat");
        await RunAsync(new UnpackSource(contents, plain, plain, "", []), new UnpackOptions { KeepFolders = false, Mask = "*.txt" }, flat);
        Assert.Equal(contents.Items.Where(i => !i.IsDirectory && i.Name.EndsWith(".txt", StringComparison.Ordinal)).Select(i => i.Name).Order().ToArray(), Directory.GetFiles(flat).Select(f => Path.GetFileName(f)).Order().ToArray());
    }

    [Fact]
    public async Task AFileThatGrowsPastItsSizeStopsTheJobAndIsNotLeft()
    {
        var archive = Path.Combine(_dir, "lies.zip");
        CraftedArchives.Zip(archive, [("big.bin", new byte[3 << 20])], CompressionLevel.NoCompression);
        var bytes = File.ReadAllBytes(archive);
        Patch(bytes, [0x50, 0x4B, 0x03, 0x04], 22);
        Patch(bytes, [0x50, 0x4B, 0x01, 0x02], 24);
        File.WriteAllBytes(archive, bytes);

        var job = await RunAsync(Source(archive), new UnpackOptions());

        Assert.False(File.Exists(Path.Combine(_out, "big.bin")));
        Assert.True(job.State == JobState.Failed && job.Error!.Message == Loc.Format(Strings.Unpack_Grew, "big.bin")
            || job.Issues.Any(i => i.Path == "big.bin"), $"{job.State}: {job.Error?.Message}");

        static void Patch(byte[] data, byte[] signature, int offset)
        {
            var at = data.AsSpan().IndexOf(signature);
            BitConverter.GetBytes(10).CopyTo(data, at + offset);
        }
    }

    [Fact]
    public void ASuspiciousArchiveAsksFirst()
    {
        var plan = new UnpackPlan([], 5L << 30, 3, 0, 3);
        Assert.NotNull(ArchiveUnpacker.Suspicion(plan, 42 << 10, ratio: 100, megabytes: 1024, freeBytes: null));
        Assert.Null(ArchiveUnpacker.Suspicion(plan, 3L << 30, ratio: 100, megabytes: 1024, freeBytes: null));
        Assert.Null(ArchiveUnpacker.Suspicion(plan, 42 << 10, ratio: 100, megabytes: 8192, freeBytes: null));
        Assert.True(ArchiveUnpacker.Suspicion(plan, 3L << 30, ratio: 100, megabytes: 1024, freeBytes: 1L << 30)!.MoreThanFree);
    }

    [Fact]
    public async Task TestArchiveFindsDamagedEntriesAndWritesNothing()
    {
        var good = Path.Combine(_dir, "good.zip");
        CraftedArchives.Zip(good, [("a.txt", Encoding.UTF8.GetBytes(new string('a', 5000))), ("b.txt", Encoding.UTF8.GetBytes(new string('b', 5000)))], CompressionLevel.NoCompression);
        var bad = Path.Combine(_dir, "bad.zip");
        var bytes = File.ReadAllBytes(good);
        bytes[100] ^= 0xFF;
        File.WriteAllBytes(bad, bytes);

        var ok = await RunAsync(ArchiveUnpacker.Test(_fs, Open(good), "good.zip", good, null));
        Assert.Equal(JobState.Done, ok.State);
        var damaged = await RunAsync(ArchiveUnpacker.Test(_fs, Open(bad), "bad.zip", bad, null));
        Assert.Equal(JobState.DoneWithIssues, damaged.State);
        Assert.Equal([("a.txt", Strings.Unpack_Checksum)], damaged.Issues.Select(i => (i.Path, i.Message)).ToArray());
        Assert.Empty(Directory.GetFiles(_out, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(@"..\evil.txt", Refusal.LeavesTarget)]
    [InlineData(@"a\...\b", Refusal.LeavesTarget)]
    [InlineData(@"C:\Windows\win.ini", Refusal.FullPath)]
    [InlineData(@"\\server\share\x", Refusal.FullPath)]
    [InlineData(@"\etc\passwd", Refusal.FullPath)]
    [InlineData(@"docs\NUL", Refusal.DeviceName)]
    [InlineData(@"com1.txt", Refusal.DeviceName)]
    [InlineData(@"a.txt:Zone.Identifier", Refusal.Stream)]
    [InlineData(@"docs\a..b.txt", null)]
    [InlineData(@".\docs\console.txt", null)]
    public void NamesAreCheckedAsTheArchiveGivesThem(string raw, Refusal? expected) => Assert.Equal(expected, ArchiveSafety.Check(raw, out _));

    [Theory]
    [InlineData("2024.zip", "2024")]
    [InlineData("backup.tar.gz", "backup")]
    [InlineData("photos.7z.001", "photos")]
    [InlineData("photos.part1.rar", "photos")]
    [InlineData("notes.txt.gz", "notes.txt")]
    [InlineData("setup.exe", "setup.exe")]
    public void UnpacksFolderIsTheArchivesNameWithoutItsExtensions(string name, string folder) => Assert.Equal(folder, ArchivePaths.FolderNameFor(name));

    private UnpackSource Source(string archive) => new(Open(archive), archive, archive, "", []);

    private static ArchiveContents Open(string path) => ArchiveReader.Open(path, Path.GetFileName(path), null, cancellation: TestContext.Current.CancellationToken);

    private Task<OperationJob> RunAsync(UnpackSource source, UnpackOptions options, string? target = null)
        => RunAsync(ArchiveUnpacker.Unpack(_fs, source, target ?? _out, options, null));

    private static async Task<OperationJob> RunAsync(OperationJob job)
    {
        new OperationQueue().Add(job);
        await job.Completion.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        return job;
    }
}
