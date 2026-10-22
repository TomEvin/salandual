// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/LocalOperationsTests.cs

using System.Diagnostics;
using Salandual.Core.Operations;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Tests;

public sealed class LocalOperationsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-ops-").FullName;
    private readonly LocalOperations _fs = new();
    private readonly OperationQueue _queue = new();

    public LocalOperationsTests()
    {
        Source = Folder("src");
        Target = Folder("dst");
    }

    private string Source { get; }

    private string Target { get; }

    public void Dispose() => Clean(WindowsPath.ToExtended(_dir));

    private static void Clean(string folder)
    {
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            entry.Attributes &= ~FileAttributes.ReadOnly;
            if (entry is DirectoryInfo dir && !dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Clean(dir.FullName);
            }
            else
            {
                entry.Delete();
            }
        }

        Directory.Delete(folder);
    }

    private string Folder(string name, string? inDir = null)
    {
        var path = WindowsPath.Join(inDir ?? _dir, name);
        Directory.CreateDirectory(WindowsPath.ToExtended(path));
        return path;
    }

    private static string Write(string folder, string name, string text = "hello", DateTime? written = null)
    {
        var path = WindowsPath.Join(folder, name);
        File.WriteAllText(WindowsPath.ToExtended(path), text);
        if (written is { } time)
        {
            File.SetLastWriteTimeUtc(WindowsPath.ToExtended(path), time);
        }

        return path;
    }

    private static string Read(string path) => File.ReadAllText(WindowsPath.ToExtended(path));

    private static bool Exists(string path) => Path.Exists(WindowsPath.ToExtended(path));

    private static OperationSource Item(string path) => new(path, Directory.Exists(WindowsPath.ToExtended(path)));

    private async Task<List<OperationQuestion>> Run(OperationJob job, Func<OperationQuestion, OperationAnswer>? answer = null)
    {
        var asked = new List<OperationQuestion>();
        job.Changed += (_, _) =>
        {
            if (job.Question is { } q && !asked.Contains(q))
            {
                asked.Add(q);
                job.Answer(answer?.Invoke(q) ?? new OperationAnswer(AnswerKind.Cancel));
            }
        };
        _queue.Add(job);
        await job.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.NotEqual(JobState.Failed, job.State);
        return asked;
    }

    [Fact]
    public async Task Copy_Tree_OddAndLongNames_KeepsContentTimesAndFolderTimes()
    {
        var photos = Folder("Nyaralás 2026", Source);
        var deep = Folder(new string('d', 120), Folder(new string('c', 120), photos));
        Write(photos, "árvíztűrő 😀.txt", "a");
        Write(photos, "trailing dot.", "b");
        Write(photos, "trailing space ", "c");
        Write(deep, new string('n', 200) + ".txt", "d");
        var then = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        Write(photos, "old.txt", "e", then);
        Directory.SetLastWriteTimeUtc(WindowsPath.ToExtended(photos), then);
        File.SetAttributes(WindowsPath.ToExtended(WindowsPath.Join(photos, "old.txt")), FileAttributes.ReadOnly);

        var job = FileOperations.Copy(_fs, [Item(photos)], TransferTarget.Resolve(_fs, Target, 1));
        Assert.Equal("Copying 1 folder", job.Title);
        await Run(job);

        Assert.Equal(JobState.Done, job.State);
        var copied = WindowsPath.Join(Target, "Nyaralás 2026");
        Assert.Equal("a", Read(WindowsPath.Join(copied, "árvíztűrő 😀.txt")));
        Assert.Equal("b", Read(WindowsPath.Join(copied, "trailing dot.")));
        Assert.Equal("c", Read(WindowsPath.Join(copied, "trailing space ")));
        Assert.Equal("d", Read(WindowsPath.Join(WindowsPath.Join(WindowsPath.Join(copied, new string('c', 120)), new string('d', 120)), new string('n', 200) + ".txt")));
        var old = WindowsPath.ToExtended(WindowsPath.Join(copied, "old.txt"));
        Assert.Equal(then, File.GetLastWriteTimeUtc(old));
        Assert.True(File.GetAttributes(old).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal(then, Directory.GetLastWriteTimeUtc(WindowsPath.ToExtended(copied)));
        Assert.Equal(job.Progress.TotalBytes, job.Progress.DoneBytes);
        Assert.True(Exists(photos));
    }

    [Fact]
    public async Task Copy_Conflicts_Ask_Overwrite_Skip_KeepBoth_ThenForAll()
    {
        var older = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt", "e.txt" })
        {
            Write(Source, name, "new " + name);
            Write(Target, name, "old " + name, older);
        }

        File.SetAttributes(WindowsPath.ToExtended(WindowsPath.Join(Target, "a.txt")), FileAttributes.ReadOnly);
        var sources = new[] { "a.txt", "b.txt", "c.txt", "d.txt", "e.txt" }.Select(n => Item(WindowsPath.Join(Source, n))).ToList();
        var scan = FileOperations.FindConflicts(_fs, sources, new TransferTarget(Target), null, TestContext.Current.CancellationToken);
        Assert.Equal(5, scan.Count);
        Assert.True(scan.Complete);
        Assert.True(scan.Samples[0].SourceIsNewer);

        var answers = new Queue<OperationAnswer>([
            new(AnswerKind.Overwrite),
            new(AnswerKind.Skip),
            new(AnswerKind.KeepBoth),
            new(AnswerKind.Overwrite, ForAll: true),
        ]);
        var asked = await Run(FileOperations.Copy(_fs, sources, new TransferTarget(Target)), _ => answers.Dequeue());

        Assert.Equal(4, asked.Count);
        var first = Assert.IsType<ConflictQuestion>(asked[0]);
        Assert.Equal(WindowsPath.Join(Target, "a.txt"), first.Target);
        Assert.Equal("new a.txt", Read(WindowsPath.Join(Target, "a.txt")));
        Assert.Equal("old b.txt", Read(WindowsPath.Join(Target, "b.txt")));
        Assert.Equal("old c.txt", Read(WindowsPath.Join(Target, "c.txt")));
        Assert.Equal("new c.txt", Read(WindowsPath.Join(Target, "c (2).txt")));
        Assert.Equal("new d.txt", Read(WindowsPath.Join(Target, "d.txt")));
        Assert.Equal("new e.txt", Read(WindowsPath.Join(Target, "e.txt")));
    }

    [Fact]
    public async Task Copy_OverwriteOlderPolicy_OnlyReplacesOlderFiles()
    {
        Write(Source, "newer.txt", "source", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Write(Target, "newer.txt", "target", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Write(Source, "older.txt", "source", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Write(Target, "older.txt", "target", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var options = new TransferOptions { IfExists = ExistingFilePolicy.OverwriteOlder };
        var asked = await Run(FileOperations.Copy(_fs, [Item(WindowsPath.Join(Source, "newer.txt")), Item(WindowsPath.Join(Source, "older.txt"))], new TransferTarget(Target), options));

        Assert.Empty(asked);
        Assert.Equal("source", Read(WindowsPath.Join(Target, "newer.txt")));
        Assert.Equal("target", Read(WindowsPath.Join(Target, "older.txt")));
    }

    [Fact]
    public async Task Copy_MaskRenamesFiles_FilterAndSkipEmptyFolders()
    {
        var sub = Folder("sub", Source);
        Folder("empty", Source);
        Write(Source, "a.heic");
        Write(Source, "b.jpg");
        Write(sub, "c.heic");
        Write(sub, "d.txt");

        var options = new TransferOptions { Filter = "*.heic", SkipEmptyFolders = true };
        var target = TransferTarget.Resolve(_fs, WindowsPath.Join(Target, "*.bak"), 4);
        var job = FileOperations.Copy(_fs, [Item(WindowsPath.Join(Source, "a.heic")), Item(WindowsPath.Join(Source, "b.jpg")), Item(sub), Item(WindowsPath.Join(Source, "empty"))], target, options);
        await Run(job);

        Assert.True(Exists(WindowsPath.Join(Target, "a.bak")));
        Assert.False(Exists(WindowsPath.Join(Target, "b.bak")));
        Assert.True(Exists(WindowsPath.Join(WindowsPath.Join(Target, "sub"), "c.heic")));
        Assert.False(Exists(WindowsPath.Join(WindowsPath.Join(Target, "sub"), "d.txt")));
        Assert.False(Exists(WindowsPath.Join(Target, "empty")));

        var inside = Folder("inside", Target);
        await Run(FileOperations.Copy(_fs, [Item(sub)], TransferTarget.Resolve(_fs, WindowsPath.Join(inside, "*.bak"), 1) with { MaskInSubfolders = true }));
        Assert.True(Exists(WindowsPath.Join(WindowsPath.Join(inside, "sub"), "c.bak")));
        Assert.True(Exists(WindowsPath.Join(WindowsPath.Join(inside, "sub"), "d.bak")));
    }

    [Fact]
    public async Task Copy_IntoItself_IsRefused_OntoItself_MakesASecondCopy()
    {
        var folder = Folder("Photos", Source);
        var file = Write(Source, "note.txt", "x");

        var job = FileOperations.Copy(_fs, [Item(folder)], new TransferTarget(WindowsPath.Join(folder, "inner"), CreatesFolder: true));
        await Run(job);
        Assert.Equal(JobState.DoneWithIssues, job.State);
        Assert.Contains("into itself", Assert.Single(job.Issues).Message, StringComparison.Ordinal);

        var refused = FileOperations.Copy(_fs, [Item(file)], new TransferTarget(Source));
        await Run(refused);
        Assert.Equal("You can't copy a file onto itself.", Assert.Single(refused.Issues).Message);
        Assert.False(Exists(WindowsPath.Join(Source, "note (2).txt")));

        await Run(FileOperations.Copy(_fs, [Item(file)], new TransferTarget(Source) { RenameOntoItself = true }));
        Assert.Equal("x", Read(WindowsPath.Join(Source, "note (2).txt")));
    }

    [Fact]
    public async Task Copy_Streams_KeptOrRemoved_Verify()
    {
        var file = Write(Source, "download.zip", "zip");
        File.WriteAllText(WindowsPath.ToExtended(file) + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3");

        await Run(FileOperations.Copy(_fs, [Item(file)], new TransferTarget(Folder("kept", Target)), new TransferOptions { Verify = true }));
        Assert.Equal("[ZoneTransfer]\r\nZoneId=3", File.ReadAllText(WindowsPath.ToExtended(WindowsPath.Join(WindowsPath.Join(Target, "kept"), "download.zip")) + ":Zone.Identifier"));

        await Run(FileOperations.Copy(_fs, [Item(file)], new TransferTarget(Folder("plain", Target)), new TransferOptions { KeepStreams = false }));
        var plain = WindowsPath.Join(WindowsPath.Join(Target, "plain"), "download.zip");
        Assert.Equal("zip", Read(plain));
        Assert.False(File.Exists(WindowsPath.ToExtended(plain) + ":Zone.Identifier"));
    }

    [Fact]
    public async Task Copy_LockedFile_RetryThenSucceeds_SkipLeavesAnIssue()
    {
        var file = Write(Source, "locked.txt", "data");
        var other = Write(Source, "other.txt", "data");
        var lockStream = new FileStream(WindowsPath.ToExtended(file), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var asked = await Run(FileOperations.Copy(_fs, [Item(file)], new TransferTarget(Target)), q =>
        {
            lockStream.Dispose();
            return new OperationAnswer(AnswerKind.Retry);
        });
        var error = Assert.IsType<ErrorQuestion>(Assert.Single(asked));
        Assert.StartsWith("It is in use by ", error.Message, StringComparison.Ordinal);
        Assert.Equal("data", Read(WindowsPath.Join(Target, "locked.txt")));

        using (new FileStream(WindowsPath.ToExtended(other), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var job = FileOperations.Copy(_fs, [Item(other)], new TransferTarget(Target));
            await Run(job, _ => new OperationAnswer(AnswerKind.Skip));
            Assert.Equal(JobState.DoneWithIssues, job.State);
            Assert.Equal(other, Assert.Single(job.Issues).Path);
        }
    }

    [Fact]
    public async Task Copy_Cancel_LeavesNoPartialFile_SpeedLimitSlows()
    {
        var big = WindowsPath.Join(Source, "big.bin");
        await File.WriteAllBytesAsync(WindowsPath.ToExtended(big), new byte[8 * 1024 * 1024], TestContext.Current.CancellationToken);
        var job = FileOperations.Copy(_fs, [Item(big)], new TransferTarget(Target), new TransferOptions { SpeedLimit = 2 * 1024 * 1024 });
        _queue.Add(job);

        var clock = Stopwatch.StartNew();
        while (job.Progress.DoneBytes == 0 && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(job.Progress.DoneBytes < job.Progress.TotalBytes);
        job.Pause();
        Assert.Equal(JobState.Paused, job.State);
        job.Cancel();
        await job.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.False(Exists(WindowsPath.Join(Target, "big.bin")));
    }

    [Fact]
    public async Task Move_SameVolume_RenamesWholeFolders_MergesIntoExisting_CaseOnlyRename()
    {
        var photos = Folder("Photos", Source);
        Write(photos, "a.jpg", "new a");
        Write(photos, "b.jpg", "b");
        var existing = Folder("Photos", Target);
        Write(existing, "a.jpg", "old a");
        Write(existing, "c.jpg", "c");
        var plain = Folder("Plain", Source);
        Write(plain, "x.txt");

        var asked = await Run(FileOperations.Move(_fs, [Item(photos), Item(plain)], new TransferTarget(Target)), _ => new OperationAnswer(AnswerKind.Overwrite));

        Assert.IsType<ConflictQuestion>(Assert.Single(asked));
        Assert.False(Exists(photos));
        Assert.False(Exists(plain));
        Assert.Equal("new a", Read(WindowsPath.Join(existing, "a.jpg")));
        Assert.Equal("b", Read(WindowsPath.Join(existing, "b.jpg")));
        Assert.Equal("c", Read(WindowsPath.Join(existing, "c.jpg")));
        Assert.True(Exists(WindowsPath.Join(WindowsPath.Join(Target, "Plain"), "x.txt")));

        var lower = Write(Source, "readme.md");
        await Run(FileOperations.Move(_fs, [Item(lower)], new TransferTarget(Source, NewName: "README.md")));
        Assert.Equal(["README.md"], Directory.GetFiles(WindowsPath.ToExtended(Source)).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Move_AcrossVolumes_DeletesTheSourceOnlyAfterTheCopy()
    {
        var fs = new TwoVolumes(_fs, Target);
        var folder = Folder("Docs", Source);
        Write(folder, "a.txt", "a");
        var locked = Write(folder, "b.txt", "b");
        File.SetAttributes(WindowsPath.ToExtended(WindowsPath.Join(folder, "a.txt")), FileAttributes.ReadOnly);

        using (new FileStream(WindowsPath.ToExtended(locked), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var job = FileOperations.Move(fs, [Item(folder)], new TransferTarget(Target));
            await Run(job, _ => new OperationAnswer(AnswerKind.Skip));
            Assert.Equal(JobState.DoneWithIssues, job.State);
        }

        Assert.False(Exists(WindowsPath.Join(folder, "a.txt")));
        Assert.True(Exists(locked));
        Assert.Equal("a", Read(WindowsPath.Join(WindowsPath.Join(Target, "Docs"), "a.txt")));
        Assert.Equal("b", Read(WindowsPath.Join(WindowsPath.Join(Target, "Docs"), "b.txt")));
    }

    [Fact]
    public async Task DeletePermanently_Tree_ReadOnlyFiles_JunctionTargetSurvives()
    {
        var outside = Folder("outside");
        Write(outside, "keep me.txt", "precious");
        var tree = Folder("tree", Source);
        var ro = Write(Folder("sub", tree), "ro.txt");
        File.SetAttributes(WindowsPath.ToExtended(ro), FileAttributes.ReadOnly | FileAttributes.Hidden);
        Write(tree, "trailing dot.");
        var link = WindowsPath.Join(tree, "link");
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false })!)
        {
            await mklink.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(WindowsPath.Join(link, "keep me.txt")));

        var job = FileOperations.Delete(_fs, [Item(tree)], recycle: false);
        Assert.Equal("Deleting permanently 1 folder", job.Title);
        await Run(job);

        Assert.Equal(JobState.Done, job.State);
        Assert.False(Exists(tree));
        Assert.Equal("precious", Read(WindowsPath.Join(outside, "keep me.txt")));
    }

    [Fact]
    public async Task Copy_FolderLinksAreNotFollowed()
    {
        var tree = Folder("tree", Source);
        Write(tree, "a.txt");
        var link = WindowsPath.Join(tree, "loop");
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{tree}\"") { CreateNoWindow = true, UseShellExecute = false })!)
        {
            await mklink.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        var job = FileOperations.Copy(_fs, [Item(tree)], new TransferTarget(Target));
        await Run(job);
        Assert.Equal(JobState.DoneWithIssues, job.State);
        var issue = Assert.Single(job.Issues);
        Assert.Equal(link, issue.Path);
        Assert.Contains("copied as an empty folder", issue.Message, StringComparison.Ordinal);
        Assert.True(Exists(WindowsPath.Join(WindowsPath.Join(Target, "tree"), "a.txt")));

        var copied = WindowsPath.ToExtended(WindowsPath.Join(WindowsPath.Join(Target, "tree"), "loop"));
        Assert.True(Directory.Exists(copied));
        Assert.False(File.GetAttributes(copied).HasFlag(FileAttributes.ReparsePoint));
        Assert.Empty(Directory.EnumerateFileSystemEntries(copied));
    }

    [Fact]
    public async Task Preview_ConflictsOntoItselfMaskLinksOnlineOnlyAndFat32()
    {
        var sub = Folder("Edited", Source);
        Write(sub, "inner.jpg", "i");
        Write(Source, "a.jpg", "new");
        Write(Target, "a.bak", "old");
        Write(Source, "cloud.jpg", new string('c', 100));
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{WindowsPath.Join(sub, "link")}\" \"{Target}\"") { CreateNoWindow = true, UseShellExecute = false })!)
        {
            await mklink.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        var fs = new FakeTarget(_fs, onlineOnly: "cloud.jpg", fileSystem: "FAT32");
        var sources = new[] { Item(sub), Item(WindowsPath.Join(Source, "a.jpg")), Item(WindowsPath.Join(Source, "cloud.jpg")) };
        var preview = TransferPreview.Scan(fs, OperationKind.Copy, sources, new TransferTarget(Target, Mask: "*.bak"), null, TestContext.Current.CancellationToken);

        Assert.True(preview.Complete);
        Assert.Equal(1, preview.Conflicts.Count);
        Assert.Equal([("a.jpg", "a.bak"), ("cloud.jpg", "cloud.bak")], preview.MaskRenames);
        Assert.Equal(1, preview.MaskInsideCount);
        Assert.Equal(WindowsPath.Join(sub, "link"), Assert.Single(preview.Links));
        Assert.Equal(1, preview.OnlineOnlyFiles);
        Assert.Equal(100, preview.OnlineOnlyBytes);
        Assert.Equal("FAT32", preview.Target.FileSystem);

        var onto = TransferPreview.Scan(_fs, OperationKind.Copy, [Item(WindowsPath.Join(Source, "a.jpg"))], new TransferTarget(Source), null, TestContext.Current.CancellationToken);
        Assert.Equal(WindowsPath.Join(Source, "a.jpg"), Assert.Single(onto.OntoItself));
    }

    [Fact]
    public void Describe_RealDisk()
    {
        var facts = _fs.Describe(_dir);
        Assert.NotNull(facts.FreeBytes);
        Assert.True(facts.FreeBytes > 0);
        Assert.False(string.IsNullOrEmpty(facts.FileSystem));
        Assert.False(facts.ReadOnly);
    }

    [Fact]
    public async Task InUse_NamesTheProgram()
    {
        var file = Write(Source, "open.txt", "x");
        using var open = new FileStream(WindowsPath.ToExtended(file), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var job = FileOperations.Delete(_fs, [Item(file)], recycle: false);
        await Run(job, _ => new OperationAnswer(AnswerKind.Skip));
        var message = Assert.Single(job.Issues).Message;
        Assert.StartsWith("It is in use by ", message, StringComparison.Ordinal);
        Assert.NotEqual("It is in use by another program.", message);
    }

    [Fact]
    public async Task SpeedLimit_ChangesWhileRunning_ProgressHasCurrentFileAndGraphPoints()
    {
        var big = WindowsPath.Join(Source, "big.bin");
        await File.WriteAllBytesAsync(WindowsPath.ToExtended(big), new byte[6 * 1024 * 1024], TestContext.Current.CancellationToken);
        var job = FileOperations.Copy(_fs, [Item(big)], new TransferTarget(Target), new TransferOptions { SpeedLimit = 1024 * 1024 });
        Assert.Equal(1024 * 1024, job.SpeedLimit);
        var sawCurrent = false;
        job.Changed += (_, _) => sawCurrent |= job.Progress.CurrentTotal == 6 * 1024 * 1024 && job.Progress.CurrentDone > 0;
        _queue.Add(job);

        var clock = Stopwatch.StartNew();
        while (job.Progress.DoneBytes < 1024 * 1024 && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        job.SpeedLimit = 0;
        await job.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Done, job.State);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        Assert.True(sawCurrent);
        Assert.NotEmpty(job.SpeedHistory);
        Assert.True(job.Progress.AverageBytesPerSecond > 0);
    }

    [Fact]
    public async Task Delete_RefusedItems_FollowTheDialogsAnswer()
    {
        var odd = Write(Source, "dot.");
        var kept = FileOperations.Delete(_fs, [Item(odd)], recycle: true, refusedPermanently: false);
        Assert.Empty(await Run(kept));
        Assert.True(Exists(odd));

        Assert.Empty(await Run(FileOperations.Delete(_fs, [Item(odd)], recycle: true, refusedPermanently: true)));
        Assert.False(Exists(odd));
    }

    [Fact]
    public void NewFolder_MakesEveryFolderOnTheWay_AndNamesTheFirstNewOne()
    {
        Folder("2026", Source);
        var plan = NewFolderPlan.Make(_fs, Source, @"2026\Augusztus/Válogatás ", out var problem);
        Assert.Null(problem);
        Assert.NotNull(plan);
        Assert.Equal(WindowsPath.Join(Source, @"2026\Augusztus"), plan.First);
        Assert.Equal(WindowsPath.Join(Source, @"2026\Augusztus\Válogatás"), plan.Deepest);
        plan.Create(_fs);
        Assert.True(Directory.Exists(WindowsPath.ToExtended(plan.Deepest)));
    }

    [Theory]
    [InlineData("Balaton", "A folder named “Balaton” is already here.")]
    [InlineData("notes.txt", "A file named “notes.txt” is already here.")]
    [InlineData(@"notes.txt\x", "A file named “notes.txt” is already here.")]
    [InlineData("a<b", "A name can't contain <  (none of < > : \" / \\ | ? * are allowed).")]
    [InlineData(@"ok\CON", "“CON”: “CON” is reserved by Windows.")]
    [InlineData(@"  \ ", "Type a name.")]
    public void NewFolder_ExplainsWhatCantBeMade(string typed, string expected)
    {
        Folder("Balaton", Source);
        Write(Source, "notes.txt");
        Assert.Null(NewFolderPlan.Make(_fs, Source, typed, out var problem));
        Assert.Equal(expected, problem);
    }

    [Fact]
    public void NewFolder_TakesAFullPath()
    {
        var full = WindowsPath.Join(Target, @"a\b");
        var plan = NewFolderPlan.Make(_fs, Source, full, out _)!;
        Assert.Equal(full, plan.Deepest);
        Assert.Equal(WindowsPath.Join(Target, "a"), plan.First);
    }

    [Theory]
    [InlineData("IMG_0413.HEIC", false, 8)]
    [InlineData("archive.tar.gz", false, 11)]
    [InlineData("README", false, 6)]
    [InlineData(".gitignore", false, 10)]
    [InlineData("Nyaralás 2026.v2", true, 16)]
    public void QuickRename_SelectsTheNameWithoutItsExtension(string name, bool folder, int stem)
        => Assert.Equal(stem, QuickRename.StemLength(name, folder));

    [Theory]
    [InlineData("IMG_0413.HEIC", "IMG_0413.jpg", false, ".HEIC", ".jpg")]
    [InlineData("IMG_0413.HEIC", "IMG_0413", false, ".HEIC", "")]
    [InlineData("IMG_0413.HEIC", "Balaton.heic", false, null, null)]
    [InlineData("README", "README.md", false, null, null)]
    [InlineData("Photos.2026", "Photos.2027", true, null, null)]
    public void QuickRename_WarnsOnlyWhenTheExtensionReallyChanges(string from, string to, bool folder, string? oldExt, string? newExt)
    {
        var change = QuickRename.ExtensionChange(from, to, folder);
        Assert.Equal(oldExt, change?.From);
        Assert.Equal(newExt, change?.To);
    }

    [Fact]
    public void QuickRename_RefusesATakenName_ButAllowsACaseChange()
    {
        Write(Source, "a.txt");
        Write(Source, "b.txt");
        Folder("Képek", Source);
        Assert.Equal("A file named “b.txt” is already here.", QuickRename.Problem(_fs, Source, "a.txt", "b.txt"));
        Assert.Equal("A folder named “képek” is already here.", QuickRename.Problem(_fs, Source, "a.txt", "képek"));
        Assert.Null(QuickRename.Problem(_fs, Source, "a.txt", "A.TXT"));
        Assert.Null(QuickRename.Problem(_fs, Source, "a.txt", "c.txt"));
        Assert.Equal("Type a name.", QuickRename.Problem(_fs, Source, "a.txt", ""));
    }

    [Fact]
    public async Task Duplicate_CopiesNextToTheOriginals_AsName2()
    {
        var file = Write(Source, "report.docx", "v1");
        Write(Source, "report (2).docx", "older copy");
        var folder = Folder("Album", Source);
        Write(folder, "x.jpg");

        var job = FileOperations.Duplicate(_fs, [Item(file), Item(folder)], Source);
        Assert.Equal("Duplicating 1 folder and 1 file", job.Title);
        await Run(job);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal("v1", Read(WindowsPath.Join(Source, "report (3).docx")));
        Assert.Equal("older copy", Read(WindowsPath.Join(Source, "report (2).docx")));
        Assert.True(Exists(WindowsPath.Join(Source, @"Album (2)\x.jpg")));
        Assert.Equal([WindowsPath.Join(Source, "report (3).docx"), WindowsPath.Join(Source, "Album (2)")], job.Targets);
    }

    private sealed class FakeTarget(LocalOperations inner, string onlineOnly, string fileSystem) : IOperationFileSystem
    {
        private ItemInfo Mark(ItemInfo info) => info.Name == onlineOnly ? info with { Attributes = info.Attributes | (FileAttributes)0x00400000 } : info;

        public VolumeFacts Describe(string path) => new(1L << 40, fileSystem, false, 32000);

        public string Combine(string directory, string name) => inner.Combine(directory, name);

        public string GetName(string path) => inner.GetName(path);

        public string? GetParent(string path) => inner.GetParent(path);

        public string VolumeOf(string path) => inner.VolumeOf(path);

        public ItemInfo? Stat(string path) => inner.Stat(path) is { } i ? Mark(i) : null;

        public IReadOnlyList<ItemInfo> List(string directory, CancellationToken cancellationToken) => [.. inner.List(directory, cancellationToken).Select(Mark)];

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void CopyFile(string source, string target, bool overwrite, Action<long> progress) => inner.CopyFile(source, target, overwrite, progress);

        public void Move(string source, string target, bool replace) => inner.Move(source, target, replace);

        public void DeleteFile(string path) => inner.DeleteFile(path);

        public void DeleteDirectory(string path) => inner.DeleteDirectory(path);

        public void SetAttributes(string path, FileAttributes attributes) => inner.SetAttributes(path, attributes);

        public void SetTimes(string path, DateTime createdUtc, DateTime lastWriteUtc) => inner.SetTimes(path, createdUtc, lastWriteUtc);

        public void RemoveStreams(string path) => inner.RemoveStreams(path);

        public bool ContentEquals(string a, string b, CancellationToken cancellationToken) => inner.ContentEquals(a, b, cancellationToken);

        public bool CanRecycle(string path, out string reason) => inner.CanRecycle(path, out reason);

        public RecycleResult Recycle(string path) => inner.Recycle(path);
    }

    [Fact]
    public async Task Recycle_NamesTheShellCantTake_AreAskedAbout()
    {
        var odd = Write(Source, "trailing dot.");
        var job = FileOperations.Delete(_fs, [Item(odd)], recycle: true);
        var asked = await Run(job, _ => new OperationAnswer(AnswerKind.Skip));

        var question = Assert.IsType<PermanentDeleteQuestion>(Assert.Single(asked));
        Assert.Equal("Its name ends with a dot or a space.", Assert.Single(question.Items).Reason);
        Assert.True(Exists(odd));

        await Run(FileOperations.Delete(_fs, [Item(odd)], recycle: true), _ => new OperationAnswer(AnswerKind.DeletePermanently));
        Assert.False(Exists(odd));
    }

    [Fact]
    public void CanRecycle_Reasons()
    {
        Assert.True(_fs.CanRecycle(WindowsPath.Join(_dir, "normal.txt"), out _));
        Assert.False(_fs.CanRecycle(WindowsPath.Join(_dir, new string('n', 250) + ".txt"), out var reason));
        Assert.Equal("Its path is longer than 260 characters.", reason);
        Assert.False(_fs.CanRecycle(@"\\server\share\a.txt", out reason));
        Assert.Equal("Network drives have no Recycle Bin.", reason);
        Assert.False(_fs.CanRecycle(WindowsPath.Join(_dir, "nul"), out reason));
    }

    [Fact]
    public async Task Recycle_ReallyGoesToTheBin()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("SALANDUAL_TEST_RECYCLE") == "1", "Set SALANDUAL_TEST_RECYCLE=1 to send a test file to the Recycle Bin.");
        var file = Write(Source, $"salandual recycle test {Guid.NewGuid():N}.txt");
        var job = FileOperations.Delete(_fs, [Item(file)], recycle: true);
        await Run(job);
        Assert.Equal(JobState.Done, job.State);
        Assert.False(Exists(file));
    }

    private sealed class TwoVolumes(LocalOperations inner, string otherDisk) : IOperationFileSystem
    {
        public string VolumeOf(string path) => path.StartsWith(otherDisk, StringComparison.OrdinalIgnoreCase) ? "Y:\\" : "X:\\";

        public string Combine(string directory, string name) => inner.Combine(directory, name);

        public string GetName(string path) => inner.GetName(path);

        public string? GetParent(string path) => inner.GetParent(path);

        public ItemInfo? Stat(string path) => inner.Stat(path);

        public VolumeFacts Describe(string path) => inner.Describe(path);

        public IReadOnlyList<ItemInfo> List(string directory, CancellationToken cancellationToken) => inner.List(directory, cancellationToken);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void CopyFile(string source, string target, bool overwrite, Action<long> progress) => inner.CopyFile(source, target, overwrite, progress);

        public void Move(string source, string target, bool replace) => throw new IOException("Not the same disk.");

        public void DeleteFile(string path) => inner.DeleteFile(path);

        public void DeleteDirectory(string path) => inner.DeleteDirectory(path);

        public void SetAttributes(string path, FileAttributes attributes) => inner.SetAttributes(path, attributes);

        public void SetTimes(string path, DateTime createdUtc, DateTime lastWriteUtc) => inner.SetTimes(path, createdUtc, lastWriteUtc);

        public void RemoveStreams(string path) => inner.RemoveStreams(path);

        public bool ContentEquals(string a, string b, CancellationToken cancellationToken) => inner.ContentEquals(a, b, cancellationToken);

        public bool CanRecycle(string path, out string reason) => inner.CanRecycle(path, out reason);

        public RecycleResult Recycle(string path) => inner.Recycle(path);
    }

    [Fact]
    public async Task DeleteChecks_AskAboutFoldersThatArentEmpty_AndHiddenItems()
    {
        var full = Folder("Full", Source);
        Write(full, "inside.txt");
        var empty = Folder("Empty", Source);
        var hidden = Write(Source, "desktop.ini");
        File.SetAttributes(WindowsPath.ToExtended(hidden), FileAttributes.Hidden);
        var plain = Write(Source, "plain.txt");

        var job = FileOperations.Delete(_fs, [Item(full), Item(empty), Item(hidden), Item(plain)], recycle: false, checks: new DeleteChecks(NonEmptyFolders: true, HiddenOrSystem: true));
        var asked = await Run(job, q => new OperationAnswer(((CheckQuestion)q).Kind == CheckKind.DeleteHiddenOrSystem ? AnswerKind.Yes : AnswerKind.Skip));

        Assert.Equal([(CheckKind.DeleteNonEmptyFolder, full), (CheckKind.DeleteHiddenOrSystem, hidden)], asked.Cast<CheckQuestion>().Select(q => (q.Kind, q.Path)));
        Assert.True(Exists(full));
        Assert.False(Exists(empty) || Exists(hidden) || Exists(plain));
        Assert.Empty(job.Issues);
    }

    [Fact]
    public async Task DeleteChecks_YesForTheRest_AsksOnce()
    {
        var a = Folder("A", Source);
        Write(a, "1.txt");
        var b = Folder("B", Source);
        Write(b, "2.txt");
        var asked = await Run(FileOperations.Delete(_fs, [Item(a), Item(b)], recycle: false, checks: new DeleteChecks(true, false)), _ => new OperationAnswer(AnswerKind.Yes, ForAll: true));
        Assert.Single(asked);
        Assert.False(Exists(a) || Exists(b));
    }

    [Fact]
    public async Task ConfirmMerge_AsksBeforeAFolderGoesIntoOneOfTheSameName()
    {
        var album = Folder("Album", Source);
        Write(album, "new.jpg");
        var there = Folder("Album", Target);
        Write(there, "old.jpg");
        var options = TransferOptions.Default with { ConfirmMerge = true };

        var asked = await Run(FileOperations.Copy(_fs, [Item(album)], new TransferTarget(Target), options), _ => new OperationAnswer(AnswerKind.Skip));
        Assert.Equal(CheckKind.MergeFolder, Assert.IsType<CheckQuestion>(Assert.Single(asked)).Kind);
        Assert.False(Exists(WindowsPath.Join(there, "new.jpg")));

        await Run(FileOperations.Copy(_fs, [Item(album)], new TransferTarget(Target), options), _ => new OperationAnswer(AnswerKind.Yes));
        Assert.True(Exists(WindowsPath.Join(there, "new.jpg")) && Exists(WindowsPath.Join(there, "old.jpg")));

        var other = Folder("Other", Source);
        Assert.Empty(await Run(FileOperations.Copy(_fs, [Item(other)], new TransferTarget(Target), options)));
    }

    [Fact]
    public async Task ConfirmReplaceHidden_AsksEvenWhenFilesAreOverwrittenWithoutAsking()
    {
        var file = Write(Source, "thumbs.db", "new");
        var there = Write(Target, "thumbs.db", "old");
        File.SetAttributes(WindowsPath.ToExtended(there), FileAttributes.Hidden);
        var options = TransferOptions.Default with { IfExists = ExistingFilePolicy.Overwrite, ConfirmReplaceHiddenOrSystem = true };

        var asked = await Run(FileOperations.Copy(_fs, [Item(file)], new TransferTarget(Target), options), _ => new OperationAnswer(AnswerKind.Skip));
        Assert.Equal(CheckKind.ReplaceHiddenOrSystem, Assert.IsType<CheckQuestion>(Assert.Single(asked)).Kind);
        Assert.Equal("old", Read(there));

        await Run(FileOperations.Copy(_fs, [Item(file)], new TransferTarget(Target), options), _ => new OperationAnswer(AnswerKind.Yes));
        Assert.Equal("new", Read(there));

        Write(Source, "thumbs.db", "newer");
        File.SetAttributes(WindowsPath.ToExtended(there), FileAttributes.Hidden);
        var asks = await Run(FileOperations.Copy(_fs, [Item(file)], new TransferTarget(Target), options with { IfExists = ExistingFilePolicy.Ask }), _ => new OperationAnswer(AnswerKind.Overwrite));
        Assert.IsType<ConflictQuestion>(Assert.Single(asks));
        Assert.Equal("newer", Read(there));
    }
}
