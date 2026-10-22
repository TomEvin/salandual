// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/LocalFileSystemProviderTests.cs

using Salandual.Core.Vfs;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Tests;

public sealed class LocalFileSystemProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-listing-").FullName;
    private readonly LocalFileSystemProvider _provider = new();

    public void Dispose() => Directory.Delete(WindowsPath.ToExtended(_dir), recursive: true);

    private string Create(string name, int bytes = 0, string? inDir = null)
    {
        var path = WindowsPath.Join(inDir ?? _dir, name);
        File.WriteAllBytes(WindowsPath.ToExtended(path), new byte[bytes]);
        return path;
    }

    private string CreateDir(string name, string? inDir = null)
    {
        var path = WindowsPath.Join(inDir ?? _dir, name);
        Directory.CreateDirectory(WindowsPath.ToExtended(path));
        return path;
    }

    private DirectoryListing List(string path) => _provider.ListAsync(path, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    public static TheoryData<string> OddNames =>
    [
        "árvíztűrő tükörfúrógép ÁRVÍZTŰRŐ.txt",
        "日本語のファイル名.txt",
        "مرحبا بالعالم.txt",
        "emoji 😀🎉📁.txt",
        "family 👨‍👩‍👧‍👦.txt",
        "caf\u00E9.txt",
        "cafe\u0301.txt",
        "a;b,c&d%e$f#g@h!i'j[k]l{m}n(o)p+q=r~s.txt",
        " leading space.txt",
        "trailing dot.",
        "trailing space.txt ",
        "CON.txt",
        "nul",
        new string('n', 251) + ".txt",
    ];

    [Theory]
    [MemberData(nameof(OddNames))]
    public void Lists_ExactName(string name)
    {
        Create(name, bytes: 3);
        var entry = Assert.Single(List(_dir).Entries);
        Assert.Equal(name, entry.Name);
        Assert.Equal(3, entry.Size);
        Assert.False(entry.IsDirectory);
    }

    [Fact]
    public void Lists_AllEntries_WithMetadata_NoDotEntries()
    {
        Create("file.bin", bytes: 1234);
        CreateDir("sub");
        var hidden = Create("hidden.txt");
        File.SetAttributes(WindowsPath.ToExtended(hidden), FileAttributes.Hidden);

        var listing = List(_dir);

        Assert.Equal(_dir, listing.Path);
        Assert.Equal(["file.bin", "hidden.txt", "sub"], listing.Entries.Select(e => e.Name).Order(StringComparer.Ordinal));
        var file = listing.Entries.Single(e => e.Name == "file.bin");
        Assert.Equal(1234, file.Size);
        Assert.InRange(file.LastWriteUtc, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));
        Assert.True(listing.Entries.Single(e => e.Name == "sub").IsDirectory);
        Assert.True(listing.Entries.Single(e => e.Name == "hidden.txt").IsHidden);
    }

    [Fact]
    public void Lists_FolderWithTrailingDot_AndItsContent()
    {
        var odd = CreateDir("folder with dot.");
        Create("inside.txt", inDir: odd);

        Assert.Equal("inside.txt", Assert.Single(List(odd).Entries).Name);
    }

    [Fact]
    public void Lists_PathLongerThan260()
    {
        var deep = _dir;
        for (var i = 1; i <= 7; i++)
        {
            deep = CreateDir($"{i} Mélyen beágyazott mappa 📁 deeply nested folder " + new string('x', 50), deep);
        }

        Create("mély fájl 😀.txt", bytes: 5, inDir: deep);
        Assert.True(deep.Length > 700);

        var entry = Assert.Single(List(deep).Entries);
        Assert.Equal("mély fájl 😀.txt", entry.Name);
        Assert.Equal(5, entry.Size);
    }

    [Fact]
    public void Lists_ExtendedInput_ReturnsDisplayPath()
        => Assert.Equal(_dir, List(WindowsPath.ToExtended(_dir)).Path);

    [Fact]
    public void Lists_DriveRoot()
    {
        var root = WindowsPath.GetRoot(_dir)!;
        Assert.NotEmpty(List(root).Entries);
    }

    [Fact]
    public void Lists_10000Entries()
    {
        for (var i = 0; i < 10_000; i++)
        {
            Create($"f{i:D5}.dat");
        }

        var listing = List(_dir);
        Assert.Equal(10_000, listing.Entries.Length);
        Assert.Equal(10_000, listing.Entries.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void MissingFolder_Throws_DirectoryNotFound()
        => Assert.Throws<DirectoryNotFoundException>(() => List(WindowsPath.Join(_dir, "does not exist")));

    [Fact]
    public void FilePath_Throws_IOException()
    {
        var file = Create("a.txt");
        Assert.ThrowsAny<IOException>(() => List(file));
    }

    [Fact]
    public async Task Cancelled_Before_Start_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _provider.ListAsync(_dir, cts.Token));
    }

    [Fact]
    public void GetParent_UsesDisplayPaths()
        => Assert.Equal(Path.GetDirectoryName(_dir), _provider.GetParent(_dir));

    [Fact]
    public async Task Watch_ReportsBurst_AsFewChanges()
    {
        var changes = new List<DirectoryChange>();
        var first = new TaskCompletionSource();
        using (_provider.Watch(_dir, c =>
        {
            lock (changes)
            {
                changes.Add(c);
            }

            first.TrySetResult();
        }, ex => first.TrySetException(ex)))
        {
            for (var i = 0; i < 50; i++)
            {
                Create($"burst {i} ő.txt");
            }

            await first.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Task.Delay(700, TestContext.Current.CancellationToken);
        }

        lock (changes)
        {
            Assert.InRange(changes.Count, 1, 3);
            Assert.All(changes, c => Assert.Equal(_dir, c.Path));
            Assert.True(changes.Sum(c => c.ChangeCount) >= 50);
        }
    }

    [Fact]
    public async Task Watch_SeesRename_AndDelete_InOddFolder()
    {
        var odd = CreateDir("watched folder.");
        var file = Create("x.txt", inDir: odd);
        var signal = new SemaphoreSlim(0);
        using var watch = _provider.Watch(odd, _ => signal.Release(), _ => { });

        File.Move(WindowsPath.ToExtended(file), WindowsPath.ToExtended(WindowsPath.Join(odd, "y.txt")));
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        File.Delete(WindowsPath.ToExtended(WindowsPath.Join(odd, "y.txt")));
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Watch_ReportsError_WhenFolderIsDeleted()
    {
        var gone = CreateDir("to be deleted");
        var error = new TaskCompletionSource<Exception>();
        using var watch = _provider.Watch(gone, _ => { }, ex => error.TrySetResult(ex));

        Directory.Delete(WindowsPath.ToExtended(gone));

        var ex = await error.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotNull(ex);
    }
}
