// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Archives.Tests/ArchiveSearchTests.cs

using System.Formats.Tar;
using Salandual.Core.Resources;
using Salandual.Core.Search;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Archives.Tests;

public sealed class ArchiveSearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "salandual-archive-search-" + Guid.NewGuid().ToString("N")[..8]);

    public ArchiveSearchTests()
    {
        Directory.CreateDirectory(_dir);
        foreach (var name in new[] { "plain.7z", "aes.zip", "hidden-names.7z", "split.7z.001", "split.7z.002", "split.7z.003", "split.7z.004" })
        {
            File.Copy(ArchiveReaderTests.Fixture(name), Path.Combine(_dir, name));
        }

        File.WriteAllText(Path.Combine(_dir, "loose.txt"), "Hello from the disk");
        Directory.CreateDirectory(Path.Combine(_dir, "src", "Summer"));
        File.WriteAllText(Path.Combine(_dir, "src", "Summer", "beach.txt"), "Hello from a TAR");
        TarFile.CreateFromDirectory(Path.Combine(_dir, "src"), Path.Combine(_dir, "backup.tar"), includeBaseDirectory: false);
        Directory.Delete(Path.Combine(_dir, "src"), recursive: true);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<(List<SearchHit> Hits, List<SearchLogEntry> Log)> Find(SearchCriteria criteria)
    {
        var hits = new List<SearchHit>();
        var log = new List<SearchLogEntry>();
        var search = new FileSearch(new LocalFileSystemProvider(), File.OpenRead, new ArchiveSearch());
        await search.RunAsync(criteria with { LookIn = [_dir], InsideZip = true }, new SearchCallbacks { Found = hits.Add, Log = log.Add }, TestContext.Current.CancellationToken);
        return (hits, log);
    }

    [Fact]
    public async Task NamesAreFoundInsideEveryKind()
    {
        var (hits, _) = await Find(new SearchCriteria { Named = "*.txt" });

        var inside = hits.Where(h => h.Archive is not null).Select(h => h.FullPath[(_dir.Length + 1)..]).Order(StringComparer.Ordinal).ToList();
        Assert.Contains(@"plain.7z\docs\readme.txt", inside);
        Assert.Contains(@"plain.7z\docs\deeper\notes.txt", inside);
        Assert.Contains(@"backup.tar\Summer\beach.txt", inside);
        Assert.Contains(@"split.7z.001\top.txt", inside);
        Assert.Contains(@"aes.zip\docs\readme.txt", inside);
        Assert.DoesNotContain(inside, p => p.StartsWith("split.7z.002", StringComparison.Ordinal));
        var readme = hits.Single(h => h.FullPath.EndsWith(@"plain.7z\docs\readme.txt", StringComparison.Ordinal));
        Assert.Equal(Path.Combine(_dir, "plain.7z"), readme.Archive);
        Assert.Equal(Path.Combine(_dir, "plain.7z", "docs"), readme.Folder);
        Assert.Equal(16, readme.Size);
    }

    [Fact]
    public async Task AFolderInsideAnArchive_IsFoundByName()
    {
        var (hits, _) = await Find(new SearchCriteria { Named = "deeper" });

        var folder = Assert.Single(hits, h => h.Archive!.EndsWith("plain.7z", StringComparison.Ordinal));
        Assert.True(folder.IsDirectory);
        Assert.Equal(Path.Combine(_dir, "plain.7z", "docs", "deeper"), folder.FullPath);
    }

    [Fact]
    public async Task ContentsAreReadInsideArchives_ButNotEncryptedOnes()
    {
        var (hits, log) = await Find(new SearchCriteria { Texts = [new("Hello from")] });

        var found = hits.Select(h => h.FullPath[(_dir.Length + 1)..]).Order(StringComparer.Ordinal).ToList();
        Assert.Contains("loose.txt", found);
        Assert.Contains(@"plain.7z\docs\readme.txt", found);
        Assert.Contains(@"backup.tar\Summer\beach.txt", found);
        Assert.DoesNotContain(@"aes.zip\docs\readme.txt", found);
        Assert.Equal("Hello from 7-Zip", hits.Single(h => h.Name == "readme.txt").Line!.Text);
        Assert.Contains(log, e => e.Kind == SearchLogKind.NotRead && e.Path.EndsWith(@"aes.zip\docs\readme.txt", StringComparison.Ordinal) && e.Message == Strings.Find_LogEncrypted);
    }

    [Fact]
    public async Task AnArchiveWithHiddenNames_IsNotLookedInto_AndNothingAsks()
    {
        var (hits, log) = await Find(new SearchCriteria { Named = "*" });

        Assert.DoesNotContain(hits, h => h.Archive is not null && h.Archive.EndsWith("hidden-names.7z", StringComparison.Ordinal));
        Assert.Contains(log, e => e.Kind == SearchLogKind.Error && e.Path.EndsWith("hidden-names.7z", StringComparison.Ordinal) && e.Message == Strings.Find_LogNamesHidden);
    }

    [Fact]
    public async Task AFoundEntry_OpensForFindDuplicates()
    {
        var (hits, _) = await Find(new SearchCriteria { Named = "readme.txt" });
        var search = new FileSearch(new LocalFileSystemProvider(), File.OpenRead, new ArchiveSearch());

        using var content = search.OpenContent(hits.Single(h => h.Archive!.EndsWith("plain.7z", StringComparison.Ordinal)))!;
        Assert.Equal("Hello from 7-Zip", new StreamReader(content).ReadToEnd());
        Assert.Null(search.OpenContent(hits.Single(h => h.Archive!.EndsWith("aes.zip", StringComparison.Ordinal))));
    }

    [Theory]
    [InlineData("photos.zip", true)]
    [InlineData("photos.7z.001", true)]
    [InlineData("photos.7z.002", false)]
    [InlineData("photos.part1.rar", true)]
    [InlineData("photos.part2.rar", false)]
    [InlineData("photos.part10.rar", false)]
    [InlineData("report.docx", false)]
    public void WhichFilesAreLookedInside(string name, bool archive) => Assert.Equal(archive, new ArchiveSearch().IsArchive(name));

    [Fact]
    public async Task CompareReadsAFileInsideAnOpenArchive_FromACopyThatGoesWhenClosed()
    {
        var temp = Path.Combine(_dir, "temp");
        var files = new ArchiveFileSystem(new LocalFileSystemProvider(), () => temp);
        try
        {
            await files.ListAsync(Path.Combine(_dir, "plain.7z", "docs"), TestContext.Current.CancellationToken);

            using (var stream = files.OpenRead(Path.Combine(_dir, "plain.7z", "docs", "readme.txt"), TestContext.Current.CancellationToken))
            {
                Assert.Equal("Hello from 7-Zip", new StreamReader(stream).ReadToEnd());
            }

            Assert.Empty(Directory.GetFiles(temp));
            using var loose = files.OpenRead(Path.Combine(_dir, "loose.txt"), TestContext.Current.CancellationToken);
            Assert.Equal("Hello from the disk", new StreamReader(loose).ReadToEnd());
        }
        finally
        {
            files.Forget();
        }
    }

    [Fact]
    public async Task CompareDirectories_ReadsAnArchiveSide()
    {
        var temp = Path.Combine(_dir, "temp");
        var files = new ArchiveFileSystem(new LocalFileSystemProvider(), () => temp);
        var left = Directory.CreateDirectory(Path.Combine(_dir, "left")).FullName;
        File.WriteAllText(Path.Combine(left, "top.txt"), "Not the same");
        try
        {
            await files.ListAsync(Path.Combine(_dir, "plain.7z"), TestContext.Current.CancellationToken);
            var compare = new Salandual.Core.Compare.DirectoryCompare(files, path => files.OpenRead(path), new Salandual.Core.Settings.CompareDirectoriesOptions { Content = true, Newer = false, Bigger = false }, showHidden: true);
            var result = await compare.RunAsync(left, Path.Combine(_dir, "plain.7z"), token: TestContext.Current.CancellationToken);

            Assert.Empty(result.Problems);
            Assert.Contains(result.Differences, d => d.Name == "docs" && d.Kind == Salandual.Core.Compare.DifferenceKind.OnlyRight);
            Assert.Contains(result.Differences, d => d.Name == "top.txt" && d.Kind == Salandual.Core.Compare.DifferenceKind.Different);
        }
        finally
        {
            files.Forget();
        }
    }
}
