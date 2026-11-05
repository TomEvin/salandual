// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Archives.Tests/ArchiveFileSystemTests.cs

using System.IO.Compression;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Archives.Tests;

public sealed class ArchiveFileSystemTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "salandual-archive-fs-tests-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _temp;
    private readonly ArchiveFileSystem _files;

    public ArchiveFileSystemTests()
    {
        Directory.CreateDirectory(_dir);
        _temp = Path.Combine(_dir, "temp");
        _files = new ArchiveFileSystem(new LocalFileSystemProvider(), () => _temp);
    }

    public void Dispose()
    {
        _files.Forget();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task AnArchiveListsLikeAFolder()
    {
        var archive = Path.Combine(_dir, "plain.7z");
        File.Copy(ArchiveReaderTests.Fixture("plain.7z"), archive);

        var folder = await _files.ListAsync(_dir, TestContext.Current.CancellationToken);
        Assert.Contains(folder.Entries, e => e.Name == "plain.7z" && !e.IsDirectory);

        var root = await _files.ListAsync(archive, TestContext.Current.CancellationToken);
        Assert.Equal(archive, root.Path);
        Assert.Equal(["docs", "top.txt"], root.Entries.Select(e => e.Name).Order().ToArray());

        var docs = await _files.ListAsync(archive + @"\docs\", TestContext.Current.CancellationToken);
        Assert.Equal(archive + @"\docs", docs.Path);
        Assert.Contains(docs.Entries, e => e.Name == "readme.txt");

        Assert.Equal(_dir, _files.GetParent(archive));
        Assert.Equal(archive, _files.GetParent(archive + @"\docs"));

        var location = _files.LocateCached(archive + @"\docs");
        Assert.NotNull(location);
        Assert.Equal(archive, location.ArchivePath);
        Assert.Equal("docs", location.Inner);
        Assert.Null(_files.LocateCached(_dir));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _files.ListAsync(archive + @"\missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileOrFolderThatIsNotThereIsStillAnError()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _files.ListAsync(Path.Combine(_dir, "nothing", "here"), TestContext.Current.CancellationToken));
        var text = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(text, "text");
        var ex = await Assert.ThrowsAsync<ArchiveException>(() => _files.ListAsync(text, TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveProblem.NotAnArchive, ex.Problem);
    }

    [Fact]
    public async Task AnArchiveInsideAnArchiveOpensToo()
    {
        var inner = Path.Combine(_dir, "inner.zip");
        using (var zip = ZipFile.Open(inner, ZipArchiveMode.Create))
        using (var s = zip.CreateEntry("deep/file.txt").Open())
        {
            s.Write("deep text"u8);
        }

        var outer = Path.Combine(_dir, "outer.zip");
        using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(inner, "packed/inner.zip");
        }

        var listing = await _files.ListAsync(outer + @"\packed\inner.zip\deep", TestContext.Current.CancellationToken);
        Assert.Equal("file.txt", Assert.Single(listing.Entries).Name);
        Assert.Equal(outer, _files.LocateCached(outer + @"\packed\inner.zip\deep")!.FileOnDisk);
        Assert.Single(Directory.GetDirectories(Path.Combine(_temp, "Archives")));

        var copy = await _files.CopyOutAsync(outer + @"\packed\inner.zip\deep\file.txt", Path.Combine(_dir, "view"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("deep text", File.ReadAllText(copy));

        _files.Forget();
        Assert.Empty(Directory.GetDirectories(Path.Combine(_temp, "Archives")));
    }

    [Fact]
    public async Task AChangedArchiveIsReadAgain()
    {
        var zip = Path.Combine(_dir, "changing.zip");
        Make(zip, "first.txt");
        Assert.Equal("first.txt", Assert.Single((await _files.ListAsync(zip, TestContext.Current.CancellationToken)).Entries).Name);

        File.Delete(zip);
        Make(zip, "second.txt");
        File.SetLastWriteTimeUtc(zip, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal("second.txt", Assert.Single((await _files.ListAsync(zip, TestContext.Current.CancellationToken)).Entries).Name);

        static void Make(string path, string name)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            using var s = archive.CreateEntry(name).Open();
            s.Write("x"u8);
        }
    }

    [Fact]
    public async Task ViewingAnEntryCopiesItUnderASafeName()
    {
        var zip = Path.Combine(_dir, "names.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var s = archive.CreateEntry("CON.txt").Open())
        {
            s.Write("device name"u8);
        }

        var copy = await _files.CopyOutAsync(zip + @"\CON.txt", Path.Combine(_dir, "view"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("_CON.txt", Path.GetFileName(copy));
        Assert.Equal("device name", File.ReadAllText(copy));
    }
}
