// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Archives.Tests/ArchiveReaderTests.cs

using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Salandual.Archives.SevenZip;

namespace Salandual.Archives.Tests;

public sealed class ArchiveReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "salandual-archive-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public ArchiveReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    internal static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void SevenZipLoadsWithItsFormats()
    {
        Assert.True(ArchiveReader.IsAvailable);
        var names = SevenZipLibrary.Formats.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in new[] { "7z", "zip", "Rar", "Rar5", "tar", "gzip", "xz", "zstd", "Iso", "Cab", "wim", "Dmg" })
        {
            Assert.Contains(expected, names);
        }

        Assert.Contains("zip", ArchiveReader.KnownExtensions);
        Assert.Contains("dmg", ArchiveReader.KnownExtensions);
    }

    [Theory]
    [InlineData("2026-12-01T09:00:00Z")]
    [InlineData("2026-07-20T09:00:00Z")]
    public void ADosTime_IsTheWallClockOfItsOwnDate(string now)
    {
        var cet = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
        var at = DateTime.Parse(now, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var wall = new DateTime(2026, 7, 10, 12, 0, 0);
        var fromSevenZip = DateTime.SpecifyKind(wall - cet.GetUtcOffset(at), DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc), ArchiveReader.FromDosTime(fromSevenZip, cet, at));
        Assert.Null(ArchiveReader.FromDosTime(null, cet, at));
    }

    [Fact]
    public void A7zListsItsFoldersAndUnpacks()
    {
        var contents = Open(Fixture("plain.7z"), "plain.7z", null);
        Assert.Equal("7z", contents.FormatName);
        Assert.Equal(["docs", "top.txt"], Names(contents.List("")!));
        Assert.Equal(["deeper", "readme.txt"], Names(contents.List("docs")!));
        Assert.Equal(["notes.txt"], Names(contents.List(@"docs\deeper")!));
        Assert.True(contents.List("")!.Single(e => e.Name == "docs").IsDirectory);
        Assert.Null(contents.List("nothing"));

        var readme = contents.Find(@"docs\readme.txt")!;
        Assert.Equal(16, readme.Size);
        Assert.Equal("Hello from 7-Zip", Unpack(contents, readme));
    }

    [Fact]
    public void AZipWithOnlyFilesGetsItsFolders()
    {
        var zip = Path.Combine(_dir, "files.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Write(archive, "a/b/c.txt", "C");
            Write(archive, "a/d.txt", "D");
        }

        var contents = Open(zip, "files.zip", null);
        Assert.Equal(["a"], Names(contents.List("")!));
        Assert.Equal(["b", "d.txt"], Names(contents.List("a")!));
        Assert.Equal("C", Unpack(contents, contents.Find(@"A\B\C.TXT")!));
    }

    [Fact]
    public void NamesThatLeaveTheArchiveAreShownInside()
    {
        var tar = Path.Combine(_dir, "names.tar");
        using (var stream = File.Create(tar))
        using (var writer = new TarWriter(stream))
        {
            foreach (var name in new[] { "../evil.txt", "/etc/passwd", "ok/./fine.txt" })
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream("x"u8.ToArray()) });
            }
        }

        var contents = Open(tar, "names.tar", null);
        var paths = contents.Items.Select(i => i.Path).Order().ToList();
        Assert.Equal([@"(..)\evil.txt", @"etc\passwd", @"ok\fine.txt"], paths);
        Assert.Equal(@"..\evil.txt", contents.Items.Single(i => i.Path.StartsWith("(..)", StringComparison.Ordinal)).RawPath);
        Assert.DoesNotContain(contents.List("")!, e => e.Name == "..");
    }

    [Fact]
    public void AGzipHoldsTheFileItsNameSays()
    {
        var gz = Path.Combine(_dir, "notes.txt.gz");
        using (var stream = File.Create(gz))
        using (var zip = new GZipStream(stream, CompressionLevel.Fastest))
        {
            zip.Write("gzipped"u8);
        }

        var contents = Open(gz, "notes.txt.gz", null);
        var item = Assert.Single(contents.Items);
        Assert.Equal("notes.txt", item.Path);
        Assert.Equal("gzipped", Unpack(contents, item));
        Assert.Equal("backup.tar", ArchivePaths.SingleName("backup.tgz"));
    }

    [Fact]
    public void HiddenNamesAskForThePasswordAgainUntilItFits()
    {
        var asked = new List<bool>();
        var answers = new Queue<string>(["wrong", "secret"]);
        var contents = Open(Fixture("hidden-names.7z"), "hidden-names.7z", (_, wrong) =>
        {
            asked.Add(wrong);
            return answers.Dequeue();
        });

        Assert.Equal([false, true], asked);
        Assert.Equal(["docs", "top.txt"], Names(contents.List("")!));
        Assert.Equal("Top file", Unpack(contents, contents.Find("top.txt")!, (_, _) => throw new InvalidOperationException("asked again")));
    }

    [Fact]
    public void CancellingThePasswordSaysSo()
    {
        var ex = Assert.Throws<ArchiveException>(() => Open(Fixture("hidden-names.7z"), "hidden-names.7z", (_, _) => null));
        Assert.Equal(ArchiveProblem.PasswordCancelled, ex.Problem);
        Assert.Contains("hidden-names.7z", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAesZipListsWithoutAPasswordAndAsksWhenUnpacking()
    {
        var contents = Open(Fixture("aes.zip"), "aes.zip", (_, _) => throw new InvalidOperationException("asked to list"));
        Assert.True(contents.HasEncryptedItems);
        var readme = contents.Find(@"docs\readme.txt")!;
        Assert.True(readme.Encrypted);
        Assert.True((contents.List("docs")!.Single(e => e.Name == "readme.txt").Attributes & FileAttributes.Encrypted) != 0);

        var asked = new List<bool>();
        var answers = new Queue<string>(["wrong", "secret"]);
        Assert.Equal("Hello from 7-Zip", Unpack(contents, readme, (_, wrong) =>
        {
            asked.Add(wrong);
            return answers.Dequeue();
        }));
        Assert.Equal([false, true], asked);
    }

    [Fact]
    public void ASplitArchiveReadsItsNextVolumes()
    {
        var contents = Open(Fixture("split.7z.001"), "split.7z.001", null);
        var big = contents.Find("big.bin")!;
        Assert.Equal(3000, big.Size);
        using var target = new MemoryStream();
        var results = ArchiveReader.Extract(contents, [big.Index], _ => target, null, "split.7z.001", cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(ExtractResult.Ok, results[big.Index]);
        var bytes = target.ToArray();
        Assert.Equal(3000, bytes.Length);
        Assert.All(Enumerable.Range(0, 3000), i => Assert.Equal((byte)((i * 37 + 11) % 251), bytes[i]));
    }

    [Fact]
    public void AFileThatIsNoArchiveSaysWhy()
    {
        var text = Path.Combine(_dir, "plain.txt");
        File.WriteAllText(text, "just text");
        Assert.Equal(ArchiveProblem.NotAnArchive, Assert.Throws<ArchiveException>(() => Open(text, "plain.txt", null)).Problem);

        var fake = Path.Combine(_dir, "fake.zip");
        File.WriteAllText(fake, "not a zip at all");
        Assert.Equal(ArchiveProblem.Damaged, Assert.Throws<ArchiveException>(() => Open(fake, "fake.zip", null)).Problem);
    }

    [Theory]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("nul", "_nul")]
    [InlineData("COM1", "_COM1")]
    [InlineData("a:b?.txt", "a_b_.txt")]
    [InlineData("name. ", "name")]
    [InlineData("console.txt", "console.txt")]
    public void CopiesGetNamesWindowsAccepts(string name, string safe) => Assert.Equal(safe, ArchivePaths.SafeFileName(name));

    private static ArchiveContents Open(string path, string name, ArchivePasswordQuestion? ask)
        => ArchiveReader.Open(path, name, ask, cancellation: TestContext.Current.CancellationToken);

    private static string[] Names(Core.Vfs.FileEntry[] entries) => [.. entries.Select(e => e.Name).Order(StringComparer.OrdinalIgnoreCase)];

    private static void Write(ZipArchive archive, string name, string text)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    private static string Unpack(ArchiveContents contents, ArchiveItem item, ArchivePasswordQuestion? ask = null)
    {
        using var target = new MemoryStream();
        var results = ArchiveReader.Extract(contents, [item.Index], _ => target, ask, "test", cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(ExtractResult.Ok, results[item.Index]);
        return Encoding.UTF8.GetString(target.ToArray());
    }
}
