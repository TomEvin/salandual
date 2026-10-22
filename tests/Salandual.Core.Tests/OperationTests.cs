// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/OperationTests.cs

using Salandual.Core.Operations;

namespace Salandual.Core.Tests;

public sealed class OperationMaskTests
{
    [Theory]
    [InlineData("photo.jpg", "*.bak", "photo.bak")]
    [InlineData("photo.jpg", "*.*", "photo.jpg")]
    [InlineData("photo.jpg", "new_*", "new_photo.jpg")]
    [InlineData("photo.jpg", "*_old.*", "photo_old.jpg")]
    [InlineData("README", "*.*", "README")]
    [InlineData("README", "*.txt", "README.txt")]
    [InlineData("archive.tar.gz", "*.bak", "archive.tar.bak")]
    [InlineData("IMG_0412.HEIC", "????.*", "IMG_.HEIC")]
    [InlineData("report.docx", "*", "report.docx")]
    [InlineData("report.docx", "copy of *", "copy of report.docx")]
    public void Apply(string name, string mask, string expected) => Assert.Equal(expected, OperationMask.Apply(name, mask));

    [Theory]
    [InlineData("*.bak", true)]
    [InlineData("a?c", true)]
    [InlineData("Backup", false)]
    public void IsMask(string text, bool expected) => Assert.Equal(expected, OperationMask.IsMask(text));
}

public sealed class TransferTargetTests
{
    private static readonly FakeOperations _fs = new FakeOperations()
        .Folder(@"D:\Backup")
        .File(@"D:\Backup\a.txt");

    [Fact]
    public void ExistingFolder_CopiesInto()
        => Assert.Equal(new TransferTarget(@"D:\Backup"), TransferTarget.Resolve(_fs, @"D:\Backup", 3));

    [Fact]
    public void Mask_SplitsFolderAndMask()
        => Assert.Equal(new TransferTarget(@"D:\Backup", Mask: "*.bak"), TransferTarget.Resolve(_fs, @"D:\Backup\*.bak", 2));

    [Fact]
    public void IdentityMask_IsNoMask()
        => Assert.Equal(new TransferTarget(@"D:\Backup"), TransferTarget.Resolve(_fs, @"D:\Backup\*.*", 2));

    [Fact]
    public void SingleItemToMissingName_IsANewName()
        => Assert.Equal(new TransferTarget(@"D:\Backup", NewName: "b.txt"), TransferTarget.Resolve(_fs, @"D:\Backup\b.txt", 1));

    [Fact]
    public void SingleItemOntoExistingFile_IsThatName()
        => Assert.Equal(new TransferTarget(@"D:\Backup", NewName: "a.txt"), TransferTarget.Resolve(_fs, @"D:\Backup\a.txt", 1));

    [Fact]
    public void TrailingBackslash_IsAFolderToMake()
        => Assert.Equal(new TransferTarget(@"D:\Backup\New", CreatesFolder: true), TransferTarget.Resolve(_fs, @"D:\Backup\New\", 1));

    [Fact]
    public void SeveralItemsToMissingPath_MakeThatFolder()
        => Assert.Equal(new TransferTarget(@"D:\Backup\New", CreatesFolder: true), TransferTarget.Resolve(_fs, @"D:\Backup\New", 2));

    [Fact]
    public void NameFor_MaskOnlyRenamesFiles_InsideFoldersOnlyWhenAsked()
    {
        var target = new TransferTarget(@"D:\Backup", Mask: "*.bak");
        Assert.Equal("a.bak", target.NameFor("a.txt", isDirectory: false));
        Assert.Equal("Photos", target.NameFor("Photos", isDirectory: true));
        Assert.Equal("c.txt", target.NameFor("c.txt", isDirectory: false, selected: false));
        Assert.Equal("c.bak", (target with { MaskInSubfolders = true }).NameFor("c.txt", isDirectory: false, selected: false));

        var renamed = new TransferTarget(@"D:\Backup", NewName: "new.txt");
        Assert.Equal("new.txt", renamed.NameFor("old.txt", isDirectory: false));
        Assert.Equal("inner.txt", renamed.NameFor("inner.txt", isDirectory: false, selected: false));
    }

    [Fact]
    public void Progress_TimeLeftFromTheAverage_FilesPerSecondForSmallFiles()
    {
        const long Mb = 1024 * 1024;
        var big = new OperationProgress(OperationPhase.Working, 100 * Mb, 1000 * Mb, 1, 10, null, BytesPerSecond: 1, AverageBytesPerSecond: 100 * Mb);
        Assert.False(big.CountsFiles);
        Assert.Equal(TimeSpan.FromSeconds(9), big.Remaining);

        var small = new OperationProgress(OperationPhase.Working, 1000, 2000, 500, 1000, null, 0, AverageItemsPerSecond: 100);
        Assert.True(small.CountsFiles);
        Assert.Equal(TimeSpan.FromSeconds(5), small.Remaining);
    }

    [Fact]
    public void VolumeFacts_Fat32HoldsFilesUnder4GB()
    {
        Assert.Equal(uint.MaxValue, new VolumeFacts(0, "FAT32", false, 260).MaxFileSize);
        Assert.Null(new VolumeFacts(0, "exFAT", false, 260).MaxFileSize);
        Assert.Null(new VolumeFacts(0, "NTFS", false, 260).MaxFileSize);
    }

    [Fact]
    public void Count_Words()
    {
        Assert.Equal("1 file", FileOperations.Count([new(@"C:\a", false)]));
        Assert.Equal("3 files", FileOperations.Count([new(@"C:\a", false), new(@"C:\b", false), new(@"C:\c", false)]));
        Assert.Equal("2 folders and 1 file", FileOperations.Count([new(@"C:\a", true), new(@"C:\b", true), new(@"C:\c", false)]));
    }
}

public sealed class FileNamesTests
{
    [Theory]
    [InlineData("Report 2026.docx")]
    [InlineData("árvíztűrő tükörfúrógép")]
    [InlineData("日本語")]
    [InlineData(".gitignore")]
    [InlineData("CONSOLE.txt")]
    public void Fine(string name) => Assert.Null(FileNames.Problem(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("a:b")]
    [InlineData("what?")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("com1")]
    public void Problems(string name) => Assert.NotNull(FileNames.Problem(name));
}

public sealed class OperationQueueTests
{
    private static (OperationJob Job, ManualResetEventSlim Gate) Blocked(string title, params string[] volumes)
    {
        var gate = new ManualResetEventSlim();
        var job = new OperationJob(OperationKind.Copy, title, "", volumes, j =>
        {
            while (!gate.Wait(10))
            {
                j.Checkpoint();
            }
        });
        return (job, gate);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task StartedNow_SameDiskWaits_OtherDiskRuns()
    {
        var queue = new OperationQueue();
        var (a, gateA) = Blocked("a", @"C:\", @"D:\");
        var (b, gateB) = Blocked("b", @"D:\");
        var (c, gateC) = Blocked("c", @"E:\");
        queue.Add(a);
        queue.Add(b);
        queue.Add(c);

        await Until(() => a.State == JobState.Running && c.State == JobState.Running);
        Assert.Equal(JobState.Waiting, b.State);

        gateA.Set();
        await Until(() => b.State == JobState.Running);
        gateB.Set();
        gateC.Set();
        await Task.WhenAll(a.Completion, b.Completion, c.Completion);
        Assert.All([a, b, c], j => Assert.Equal(JobState.Done, j.State));
    }

    [Fact]
    public async Task Queued_RunOneAfterAnother_InTheOrderSet_AndPauseHolds()
    {
        var queue = new OperationQueue();
        var (a, gateA) = Blocked("a", @"C:\");
        var (b, gateB) = Blocked("b", @"E:\");
        var (c, gateC) = Blocked("c", @"F:\");
        queue.Add(a, toQueue: true);
        queue.Add(b, toQueue: true);
        queue.Add(c, toQueue: true);
        await Until(() => a.State == JobState.Running);
        Assert.Equal(JobState.Queued, b.State);

        queue.MoveUp(c);
        Assert.Equal([c, b], queue.Queued);

        queue.SetQueuePaused(true);
        gateA.Set();
        await a.Completion;
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Queued, c.State);

        queue.SetQueuePaused(false);
        await Until(() => c.State == JobState.Running);
        Assert.Equal(JobState.Queued, b.State);

        queue.StartNow(b);
        await Until(() => b.State == JobState.Running);
        gateB.Set();
        gateC.Set();
        await Task.WhenAll(b.Completion, c.Completion);
    }

    [Fact]
    public async Task Remove_CancelsAWaitingJob_PauseStopsTheWork()
    {
        var queue = new OperationQueue();
        var (a, gateA) = Blocked("a", @"C:\");
        var (b, _) = Blocked("b", @"C:\");
        queue.Add(a);
        queue.Add(b);
        await Until(() => a.State == JobState.Running);

        queue.Remove(b);
        Assert.Equal(JobState.Cancelled, b.State);
        Assert.DoesNotContain(b, queue.Jobs);

        a.Pause();
        Assert.Equal(JobState.Paused, a.State);
        a.Resume();
        a.Cancel();
        await a.Completion;
        Assert.Equal(JobState.Cancelled, a.State);
        gateA.Set();
    }

    [Fact]
    public async Task Ask_WaitsForTheAnswer()
    {
        OperationAnswer? got = null;
        var job = new OperationJob(OperationKind.Delete, "x", "", [@"C:\"], j => got = j.Ask(new ErrorQuestion(@"C:\x", "Delete", "It is in use.")));
        var queue = new OperationQueue();
        queue.Add(job);
        await Until(() => job.Question is ErrorQuestion);
        job.Answer(new OperationAnswer(AnswerKind.Skip, ForAll: true));
        await job.Completion;
        Assert.Equal(new OperationAnswer(AnswerKind.Skip, true), got);
    }
}

internal sealed class FakeOperations : IOperationFileSystem
{
    private readonly Dictionary<string, ItemInfo> _items = new(StringComparer.OrdinalIgnoreCase);

    public FakeOperations Folder(string path)
    {
        _items[path] = new ItemInfo(GetName(path), FileAttributes.Directory, 0, default, default);
        return this;
    }

    public FakeOperations File(string path)
    {
        _items[path] = new ItemInfo(GetName(path), FileAttributes.Normal, 1, default, default);
        return this;
    }

    public string Combine(string directory, string name) => directory.TrimEnd('\\') + "\\" + name;

    public string GetName(string path) => path.TrimEnd('\\')[(path.TrimEnd('\\').LastIndexOf('\\') + 1)..];

    public string? GetParent(string path)
    {
        var trimmed = path.TrimEnd('\\');
        var cut = trimmed.LastIndexOf('\\');
        return cut < 0 ? null : cut == 2 ? trimmed[..3] : trimmed[..cut];
    }

    public string VolumeOf(string path) => path[..3];

    public ItemInfo? Stat(string path) => _items.TryGetValue(path, out var info) ? info : null;

    public VolumeFacts Describe(string path) => VolumeFacts.Unknown;

    public IReadOnlyList<ItemInfo> List(string directory, CancellationToken cancellationToken) => [];

    public void CreateDirectory(string path) => throw new NotSupportedException();

    public void CopyFile(string source, string target, bool overwrite, Action<long> progress) => throw new NotSupportedException();

    public void Move(string source, string target, bool replace) => throw new NotSupportedException();

    public void DeleteFile(string path) => throw new NotSupportedException();

    public void DeleteDirectory(string path) => throw new NotSupportedException();

    public void SetAttributes(string path, FileAttributes attributes) => throw new NotSupportedException();

    public void SetTimes(string path, DateTime createdUtc, DateTime lastWriteUtc) => throw new NotSupportedException();

    public void RemoveStreams(string path) => throw new NotSupportedException();

    public bool ContentEquals(string a, string b, CancellationToken cancellationToken) => throw new NotSupportedException();

    public bool CanRecycle(string path, out string reason) => throw new NotSupportedException();

    public RecycleResult Recycle(string path) => throw new NotSupportedException();
}
