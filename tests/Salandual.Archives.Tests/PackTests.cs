// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Archives.Tests/PackTests.cs

using System.Text;
using Salandual.Core.Operations;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Archives.Tests;

public sealed class PackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "salandual-pack-tests-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _src;
    private readonly NoRecycleBin _fs = new();

    public PackTests()
    {
        _src = Path.Combine(_dir, "Photos");
        Directory.CreateDirectory(Path.Combine(_src, "Summer", "empty"));
        File.WriteAllText(Path.Combine(_src, "Summer", "notes.txt"), string.Concat(Enumerable.Repeat("Hello from the lake. ", 400)));
        File.WriteAllBytes(Path.Combine(_src, "Summer", "IMG_4021.jpg"), Noise(40_000, 1));
        File.WriteAllText(Path.Combine(_src, "route.gpx"), "<gpx>" + string.Concat(Enumerable.Repeat("<pt/>", 300)) + "</gpx>");
        File.SetLastWriteTimeUtc(Path.Combine(_src, "route.gpx"), new DateTime(2024, 7, 15, 8, 0, 0, DateTimeKind.Utc));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(PackFormat.Zip, "zip")]
    [InlineData(PackFormat.SevenZip, "7z")]
    [InlineData(PackFormat.Tar, "tar")]
    public async Task EachFormatHoldsWhatWasPacked(PackFormat format, string formatName)
    {
        var target = Path.Combine(_dir, "Photos" + ArchiveWriter.Extension(format));

        var job = await RunAsync(Request(target, new PackSettings(format)));

        Assert.True(job.State == JobState.Done, job.State + ": " + job.Error + string.Join("; ", job.Issues));
        var contents = OpenArchive(target, "Photos", null);
        Assert.Equal(formatName, contents.FormatName);
        Assert.Equal(["Summer", "Summer\\IMG_4021.jpg", "Summer\\empty", "Summer\\notes.txt", "route.gpx"],contents.Items.Select(i => i.Path).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new DateTime(2024, 7, 15, 8, 0, 0, DateTimeKind.Utc), contents.Find("route.gpx")!.ModifiedUtc);
        AssertSameFiles(contents, target);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Theory]
    [InlineData(PackFormat.TarGz, "gzip")]
    [InlineData(PackFormat.TarXz, "xz")]
    public async Task ACompressedTarHoldsATarWithTheFiles(PackFormat format, string formatName)
    {
        var target = Path.Combine(_dir, "Photos" + ArchiveWriter.Extension(format));

        var job = await RunAsync(Request(target, new PackSettings(format)));

        Assert.True(job.State == JobState.Done, job.State + ": " + job.Error + string.Join("; ", job.Issues));
        var outer = OpenArchive(target, "Photos", null);
        Assert.Equal(formatName, outer.FormatName);
        Assert.Equal("Photos.tar", Assert.Single(outer.Items).Path);
        var tar = Path.Combine(_dir, "inner.tar");
        using (var file = File.Create(tar))
        {
            Extract(outer, [outer.Items[0].Index], _ => file, null, "Photos");
        }

        var contents = OpenArchive(tar, "inner", null);
        Assert.Equal("tar", contents.FormatName);
        AssertSameFiles(contents, tar);
    }

    [Fact]
    public async Task A7zWithHiddenNamesNeedsThePasswordToBeListed()
    {
        var target = Path.Combine(_dir, "Photos.7z");

        await RunAsync(Request(target, new PackSettings(PackFormat.SevenZip, Password: "secret", HideNames: true)), password: "secret");

        var refused = Assert.Throws<ArchiveException>(() => OpenArchive(target, "Photos.7z", (_, _) => null));
        Assert.Equal(ArchiveProblem.PasswordCancelled, refused.Problem);
        var contents = OpenArchive(target, "Photos.7z", (_, _) => "secret");
        Assert.True(contents.NamesHidden);
        Assert.All(contents.Items.Where(i => !i.IsDirectory), i => Assert.True(i.Encrypted));
        AssertSameFiles(contents, target);
    }

    [Fact]
    public async Task AnAesZipIsTestedWithThePasswordAskedAgain()
    {
        var target = Path.Combine(_dir, "Photos.zip");
        var asked = 0;

        var job = await RunAsync(Request(target, new PackSettings(PackFormat.Zip, Password: "secret")), password: "secret", asked: () => asked++);

        Assert.True(job.State == JobState.Done, job.State + ": " + job.Error + string.Join("; ", job.Issues));
        Assert.True(asked >= 1);
        var contents = OpenArchive(target, "Photos.zip", null);
        Assert.All(contents.Items.Where(i => !i.IsDirectory), i => Assert.True(i.Encrypted));
        contents.Password = "secret";
        AssertSameFiles(contents, target);
    }

    [Fact]
    public async Task AWrongPasswordAtTheTestKeepsTheFiles()
    {
        var target = Path.Combine(_dir, "Photos.zip");

        var job = await RunAsync(Request(target, new PackSettings(PackFormat.Zip, Password: "secret")) with { DeleteAfter = true }, password: null);

        Assert.Equal(JobState.DoneWithIssues, job.State);
        Assert.True(File.Exists(target));
        Assert.True(Directory.Exists(Path.Combine(_src, "Summer")));
        Assert.Empty(_fs.Recycled);
    }

    [Fact]
    public async Task FilesAlreadyCompressedAreStored()
    {
        var target = Path.Combine(_dir, "Photos.zip");

        await RunAsync(Request(target, new PackSettings(PackFormat.Zip, PackLevel.Maximum)));

        var contents = OpenArchive(target, "Photos.zip", null);
        var jpg = contents.Find("Summer\\IMG_4021.jpg")!;
        Assert.Equal(jpg.Size, jpg.PackedSize);
        var text = contents.Find("Summer\\notes.txt")!;
        Assert.True(text.PackedSize < text.Size / 4);
        AssertSameFiles(contents, target);
    }

    [Fact]
    public async Task VolumesAreWrittenAndReadFromTheFirst()
    {
        File.WriteAllBytes(Path.Combine(_src, "big.bin"), Noise(300_000, 2));
        var target = Path.Combine(_dir, "Photos.7z");

        var job = await RunAsync(Request(target, new PackSettings(PackFormat.SevenZip, VolumeSize: 100_000), "Summer", "route.gpx", "big.bin"));

        Assert.True(job.State == JobState.Done, job.State + ": " + job.Error + string.Join("; ", job.Issues));
        Assert.False(File.Exists(target));
        var parts = Directory.GetFiles(_dir, "Photos.7z.*").Order().ToArray();
        Assert.True(parts.Length >= 3);
        Assert.All(parts[..^1], p => Assert.Equal(100_000, new FileInfo(p).Length));
        var contents = OpenArchive(parts[0], "Photos.7z", null);
        AssertSameFiles(contents, parts[0]);
    }

    [Fact]
    public async Task ReplacingSendsTheOldArchiveToTheRecycleBinOnlyWhenTheNewOneIsComplete()
    {
        var target = Path.Combine(_dir, "Photos.zip");
        File.WriteAllText(target, "the old one");

        var job = await RunAsync(Request(target, new PackSettings(PackFormat.Zip)) with { IfThere = PackExisting.Replace });

        Assert.True(job.State == JobState.Done, job.State + ": " + job.Error + string.Join("; ", job.Issues));
        Assert.Equal([target], _fs.Recycled);
        AssertSameFiles(OpenArchive(target, "Photos.zip", null), target);
    }

    [Fact]
    public async Task AddingAsksAboutAFileOfTheSameName()
    {
        var target = Path.Combine(_dir, "Photos.zip");
        await RunAsync(Request(target, new PackSettings(PackFormat.Zip)));
        File.WriteAllText(Path.Combine(_src, "route.gpx"), "<gpx>newer</gpx>");
        File.WriteAllText(Path.Combine(_src, "new.txt"), "a new file");
        var questions = 0;

        var job = await RunAsync(Request(target, new PackSettings(PackFormat.Zip), "route.gpx", "new.txt") with { IfThere = PackExisting.Add },
            answer: q => q is ConflictQuestion ? new OperationAnswer(AnswerKind.KeepBoth).Also(_ => questions++) : null);

        Assert.True(job.State == JobState.Done, job.State + ": " + job.Error + string.Join("; ", job.Issues));
        Assert.Equal(1, questions);
        Assert.Empty(_fs.Recycled);
        var contents = OpenArchive(target, "Photos.zip", null);
        Assert.NotNull(contents.Find("Summer\\notes.txt"));
        Assert.Equal("a new file", Read(contents, "new.txt"));
        Assert.Equal("<gpx>newer</gpx>", Read(contents, "route (2).gpx"));
        Assert.StartsWith("<gpx><pt/>", Read(contents, "route.gpx"));
    }

    [Theory]
    [InlineData(PackFormat.Zip)]
    [InlineData(PackFormat.SevenZip)]
    [InlineData(PackFormat.Tar)]
    public void AnArchiveIsChangedIntoANewFile(PackFormat format)
    {
        var target = Path.Combine(_dir, "Photos" + ArchiveWriter.Extension(format));
        var inputs = ArchivePacker.Collect(_fs, _dir, ["Photos"], _ => false, (_, _) => { }, TestContext.Current.CancellationToken);
        ArchiveWriter.Create(target, inputs, new PackSettings(format), new WriteCallbacks(null, TestContext.Current.CancellationToken));
        var contents = OpenArchive(target, "Photos", null);
        var notes = contents.Find("Photos\\Summer\\notes.txt")!;
        var gpx = contents.Find("Photos\\route.gpx")!;
        File.WriteAllText(Path.Combine(_dir, "added.txt"), "added");
        var added = new PackInput(Path.Combine(_dir, "added.txt"), "Photos\\Summer\\added.txt", false, 5, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal);
        var changed = Path.Combine(_dir, "changed" + ArchiveWriter.Extension(format));

        ArchiveWriter.Update(contents, changed, new ArchiveEdit(new HashSet<int> { notes.Index }, new Dictionary<int, string> { [gpx.Index] = "Photos\\track.gpx" }, [added]),
            new PackSettings(format), new WriteCallbacks(null, TestContext.Current.CancellationToken), null, "Photos");

        var after = OpenArchive(changed, "changed", null);
        Assert.Null(after.Find("Photos\\Summer\\notes.txt"));
        Assert.Null(after.Find("Photos\\route.gpx"));
        Assert.StartsWith("<gpx>", Read(after, "Photos\\track.gpx"));
        Assert.Equal("added", Read(after, "Photos\\Summer\\added.txt"));
        Assert.NotNull(after.Find("Photos\\Summer\\IMG_4021.jpg"));
        Assert.Equal(new FileInfo(target).Length, contents.FileLength);
    }

    [Fact]
    public async Task StoppingLeavesNothingBehind()
    {
        File.WriteAllBytes(Path.Combine(_src, "big.bin"), Noise(20_000_000, 3));
        var target = Path.Combine(_dir, "Photos.7z");
        var job = ArchivePacker.Pack(_fs, Request(target, new PackSettings(PackFormat.SevenZip, PackLevel.Maximum)), null);
        job.Changed += (_, _) =>
        {
            if (job.Progress.Phase == OperationPhase.Working)
            {
                job.Cancel();
            }
        };

        await RunAsync(job);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Empty(Directory.GetFiles(_dir, "Photos.7z*"));
    }

    [Fact]
    public async Task DeletingAfterwardsRecyclesThePackedItemsOnly()
    {
        var target = Path.Combine(_dir, "Photos", "Photos.zip");

        var job = await RunAsync(new PackRequest(_src, ["Summer", "route.gpx"], target, new PackSettings(PackFormat.Zip), DeleteAfter: true));

        Assert.True(job.State == JobState.Done, job.State + ": " + job.Error + string.Join("; ", job.Issues));
        Assert.Equal([Path.Combine(_src, "Summer"), Path.Combine(_src, "route.gpx")], _fs.Recycled);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task ThePackedArchiveIsNeverPackedIntoItself()
    {
        File.WriteAllText(Path.Combine(_src, "Photos.zip"), "old");
        File.WriteAllText(Path.Combine(_src, "Photos.zip.001"), "old part");
        File.WriteAllText(Path.Combine(_src, "Photos.zip.notes"), "a file of its own");
        var target = Path.Combine(_src, "Photos.zip");

        var job = await RunAsync(new PackRequest(_src, ["Photos.zip", "Photos.zip.001", "Photos.zip.notes", "route.gpx"], target, new PackSettings(PackFormat.Zip), IfThere: PackExisting.Replace));

        Assert.True(job.State == JobState.Done, job.State + ": " + job.Error + string.Join("; ", job.Issues));
        Assert.Equal(["Photos.zip.notes", "route.gpx"], OpenArchive(target, "Photos.zip", null).Items.Select(i => i.Path).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ANewNameIsFoundBesideTheOldOne()
    {
        File.WriteAllText(Path.Combine(_dir, "Backup.tar.gz"), "x");
        File.WriteAllText(Path.Combine(_dir, "Backup (2).tar.gz"), "x");

        Assert.Equal(Path.Combine(_dir, "Backup (3).tar.gz"), ArchivePacker.NewName(_fs, Path.Combine(_dir, "Backup.tar.gz")));
    }

    private ArchiveTarget Target(string archive, string inner = "")
        => new(archive, inner, ct => ArchiveReader.Open(archive, Path.GetFileName(archive), (_, _) => "secret", cancellation: ct));

    private async Task<string> MakeZip(params string[] names)
    {
        var target = Path.Combine(_dir, "2024.zip");
        await RunAsync(Request(target, new PackSettings(PackFormat.Zip), names));
        return target;
    }

    [Fact]
    public async Task FilesCopiedIntoAnArchiveGoBelowItsFolder()
    {
        var archive = await MakeZip("Summer");
        File.WriteAllText(Path.Combine(_src, "beach.txt"), "Sand");

        var job = await RunAsync(new PackRequest(_src, ["beach.txt", "route.gpx"], archive, new PackSettings(PackFormat.Zip), PackExisting.Add, Into: Target(archive, "Summer")));

        Assert.True(job.State == JobState.Done, job.State + ": " + job.Error);
        var contents = OpenArchive(archive, "2024.zip", null);
        Assert.Equal("Sand", Read(contents, "Summer\\beach.txt"));
        Assert.NotNull(contents.Find("Summer\\route.gpx"));
        Assert.NotNull(contents.Find("Summer\\notes.txt"));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task PuttingAnEditedFileBack_ReplacesItsEntryWithoutAsking()
    {
        var archive = await MakeZip("Summer");
        var copy = Directory.CreateDirectory(Path.Combine(_dir, "copy")).FullName;
        File.WriteAllText(Path.Combine(copy, "notes.txt"), "edited");

        var job = await RunAsync(new PackRequest(copy, ["notes.txt"], archive, new PackSettings(PackFormat.Zip), PackExisting.Add, Into: Target(archive, "Summer"), IfInside: ExistingFilePolicy.Overwrite),
            answer: _ => throw new InvalidOperationException("It must not ask."));

        Assert.Equal(JobState.Done, job.State);
        var contents = OpenArchive(archive, "2024.zip", null);
        Assert.Equal("edited", Read(contents, "Summer\\notes.txt"));
        Assert.Single(contents.Items, i => i.Path == "Summer\\notes.txt");
    }

    [Fact]
    public async Task DeletingRenamingAndANewFolder_AreJobsOnTheArchive()
    {
        var archive = await MakeZip("Summer", "route.gpx");

        var delete = await RunAsync(ArchivePacker.Change(_fs, Target(archive), "delete", c => new ArchiveEdit(ArchivePacker.EntriesOf(c, "", ["Summer"]), new Dictionary<int, string>(), []), null));
        var rename = await RunAsync(ArchivePacker.Change(_fs, Target(archive), "rename", c => ArchivePacker.Renaming(c, "", "route.gpx", "track.gpx"), null));
        var folder = await RunAsync(ArchivePacker.Change(_fs, Target(archive), "new folder", _ => ArchivePacker.NewFolder("", "Winter"), null));

        Assert.All([delete, rename, folder], j => Assert.Equal(JobState.Done, j.State));
        Assert.Equal(["Winter", "track.gpx"], OpenArchive(archive, "2024.zip", null).Items.Select(i => i.Path).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void AFolderWithoutAnEntryOfItsOwn_IsRenamedThroughThePathsBelowIt()
    {
        var archive = Path.Combine(_dir, "files.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntry("Summer/a.txt");
            zip.CreateEntry("Summer/b/c.txt");
        }

        var contents = OpenArchive(archive, "files.zip", null);
        var edit = ArchivePacker.Renaming(contents, "", "Summer", "Winter");

        Assert.Equal(["Winter\\a.txt", "Winter\\b\\c.txt"], edit.Rename.Values.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task AnEncryptedArchive_IsChangedWithItsPassword_AndNewFilesGetIt()
    {
        var archive = Path.Combine(_dir, "secret.7z");
        await RunAsync(Request(archive, new PackSettings(PackFormat.SevenZip, Password: "secret")), password: "secret");
        File.WriteAllText(Path.Combine(_dir, "added.txt"), "added");

        var job = await RunAsync(new PackRequest(_dir, ["added.txt"], archive, new PackSettings(PackFormat.SevenZip), PackExisting.Add, Into: new ArchiveTarget(archive, "",
            ct => ArchiveReader.Open(archive, "secret.7z", null, cancellation: ct))), password: "secret");

        Assert.True(job.State == JobState.Done, job.State + ": " + job.Error);
        var contents = OpenArchive(archive, "secret.7z", null);
        Assert.True(contents.Find("added.txt")!.Encrypted);
    }

    [Fact]
    public void WhatCantBeChanged_SaysWhy()
    {
        var contents = OpenArchive(ArchiveReaderTests.Fixture("plain.7z"), "plain.7z", null);
        var plain = ArchiveReaderTests.Fixture("plain.7z");

        Assert.Null(ArchivePacker.WhyNotChangeable(new ArchiveLocation(plain, "", contents) { FileOnDisk = plain }));
        Assert.Contains("inside another archive", ArchivePacker.WhyNotChangeable(new ArchiveLocation(plain + "\\inner.zip", "", contents) { FileOnDisk = plain }), StringComparison.Ordinal);
        var split = ArchiveReaderTests.Fixture("split.7z.001");
        Assert.Contains("volumes", ArchivePacker.WhyNotChangeable(new ArchiveLocation(split, "", OpenArchive(split, "split.7z", null)) { FileOnDisk = split }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AQueuedChange_ReadsTheArchiveAsTheChangeBeforeItLeftIt()
    {
        var archive = await MakeZip("Summer", "route.gpx");
        var queue = new OperationQueue();
        var first = ArchivePacker.Change(_fs, Target(archive), "one", _ => ArchivePacker.NewFolder("", "One"), null);
        var second = ArchivePacker.Change(_fs, Target(archive), "two", _ => ArchivePacker.NewFolder("", "Two"), null);

        queue.Add(first);
        queue.Add(second, toQueue: true);
        await second.Completion.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        Assert.Equal(JobState.Done, second.State);
        var names = OpenArchive(archive, "2024.zip", null).Items.Select(i => i.Path).ToList();
        Assert.Contains("One", names);
        Assert.Contains("Two", names);
    }

    private PackRequest Request(string target, PackSettings settings, params string[] names)
        => new(_src, names.Length > 0 ? names : ["Summer", "route.gpx"], target, settings);

    private Task<OperationJob> RunAsync(PackRequest request, string? password = "", Action? asked = null, Func<OperationQuestion, OperationAnswer?>? answer = null)
    {
        var job = ArchivePacker.Pack(_fs, request, (_, _) =>
        {
            asked?.Invoke();
            return password;
        });
        if (answer is not null)
        {
            job.Changed += (_, _) =>
            {
                if (job.Question is { } q && answer(q) is { } a)
                {
                    job.Answer(a);
                }
            };
        }

        return RunAsync(job);
    }

    private static async Task<OperationJob> RunAsync(OperationJob job)
    {
        new OperationQueue().Add(job);
        await job.Completion.WaitAsync(TimeSpan.FromSeconds(120), TestContext.Current.CancellationToken);
        return job;
    }

    private void AssertSameFiles(ArchiveContents contents, string archive)
    {
        var files = contents.Items.Where(i => !i.IsDirectory).ToList();
        Assert.NotEmpty(files);
        var buffers = files.ToDictionary(f => f.Index, _ => new MemoryStream());
        var results = Extract(contents, [.. files.Select(f => f.Index)], i => buffers[i], (_, _) => contents.Password ?? "secret", Path.GetFileName(archive));
        foreach (var file in files)
        {
            Assert.Equal(ExtractResult.Ok, results.GetValueOrDefault(file.Index, ExtractResult.Ok));
            var path = file.Path.StartsWith("Photos\\", StringComparison.Ordinal) ? Path.Combine(_dir, file.Path) : Path.Combine(_src, file.Path);
            Assert.Equal(File.ReadAllBytes(path), buffers[file.Index].ToArray());
        }
    }

    private static string Read(ArchiveContents contents, string path)
    {
        var item = contents.Find(path) ?? throw new InvalidOperationException("Not in the archive: " + path);
        using var buffer = new MemoryStream();
        Extract(contents, [item.Index], _ => buffer, null, "test");
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static ArchiveContents OpenArchive(string path, string name, ArchivePasswordQuestion? ask)
        => ArchiveReader.Open(path, name, ask, cancellation: TestContext.Current.CancellationToken);

    private static IReadOnlyDictionary<int, ExtractResult> Extract(ArchiveContents contents, IReadOnlyList<int> indices, Func<int, Stream?> target, ArchivePasswordQuestion? ask, string name)
        => ArchiveReader.Extract(contents, indices, target, ask, name, cancellation: TestContext.Current.CancellationToken);

    private static byte[] Noise(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private sealed class NoRecycleBin : IOperationFileSystem
    {
        private readonly LocalOperations _local = new();

        public List<string> Recycled { get; } = [];

        public string Combine(string directory, string name) => _local.Combine(directory, name);

        public string GetName(string path) => _local.GetName(path);

        public string? GetParent(string path) => _local.GetParent(path);

        public string VolumeOf(string path) => _local.VolumeOf(path);

        public ItemInfo? Stat(string path) => _local.Stat(path);

        public VolumeFacts Describe(string path) => _local.Describe(path);

        public IReadOnlyList<ItemInfo> List(string directory, CancellationToken cancellationToken) => _local.List(directory, cancellationToken);

        public void CreateDirectory(string path) => _local.CreateDirectory(path);

        public void CopyFile(string source, string target, bool overwrite, Action<long> progress) => _local.CopyFile(source, target, overwrite, progress);

        public void Move(string source, string target, bool replace) => _local.Move(source, target, replace);

        public void DeleteFile(string path) => _local.DeleteFile(path);

        public void DeleteDirectory(string path) => _local.DeleteDirectory(path);

        public void SetAttributes(string path, FileAttributes attributes) => _local.SetAttributes(path, attributes);

        public void SetTimes(string path, DateTime createdUtc, DateTime lastWriteUtc) => _local.SetTimes(path, createdUtc, lastWriteUtc);

        public void RemoveStreams(string path) => _local.RemoveStreams(path);

        public bool ContentEquals(string a, string b, CancellationToken cancellationToken) => _local.ContentEquals(a, b, cancellationToken);

        public bool CanRecycle(string path, out string reason)
        {
            reason = "";
            return true;
        }

        public RecycleResult Recycle(string path)
        {
            Recycled.Add(path);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }

            return RecycleResult.Recycled;
        }
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
