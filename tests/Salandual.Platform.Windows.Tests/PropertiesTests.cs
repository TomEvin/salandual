// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/PropertiesTests.cs

using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.Platform.Windows.Tests;

public sealed class PropertiesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-props-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_dir, recursive: true);
    }

    private string Write(string name, string text = "hello")
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void General_ReadsTypeSizesDatesAndAttributes()
    {
        var path = Write("notes.txt", new string('x', 5000));
        var d = FileDetailsReader.Read(path);

        Assert.False(d.IsDirectory);
        Assert.Equal(5000, d.Size);
        Assert.True(d.SizeOnDisk >= 5000);
        Assert.False(string.IsNullOrEmpty(d.TypeName));
        Assert.True(d.Modified > DateTime.Now.AddMinutes(-5));
        Assert.False(d.FromInternet);

        var folder = FileDetailsReader.Read(_dir);
        Assert.True(folder.IsDirectory);
        Assert.Null(folder.OpensWith);
    }

    [Fact]
    public void General_MarkOfTheWeb_AndUnblock()
    {
        var path = Write("setup.exe", "not a program");
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        Assert.True(FileDetailsReader.IsFromInternet(path));

        FileDetailsReader.Unblock(path);
        Assert.False(FileDetailsReader.IsFromInternet(path));
        Assert.Equal("not a program", File.ReadAllText(path));
    }

    [Fact]
    public void General_SimpleAttributes_KeepTheOthers()
    {
        var path = Write("a.txt");
        File.SetAttributes(path, FileAttributes.Archive | FileAttributes.NotContentIndexed);
        FileDetailsReader.SetSimpleAttributes(path, FileAttributes.ReadOnly | FileAttributes.Hidden);

        var now = File.GetAttributes(path);
        Assert.True(now.HasFlag(FileAttributes.ReadOnly));
        Assert.True(now.HasFlag(FileAttributes.Hidden));
        Assert.False(now.HasFlag(FileAttributes.Archive));
        Assert.True(now.HasFlag(FileAttributes.NotContentIndexed));
    }

    [Fact]
    public void General_CountsAFolder()
    {
        Write(@"f\a.txt", "12345");
        Write(@"f\sub\b.txt", "123");
        var updates = new List<FolderCount>();
        var count = FileDetailsReader.CountFolder(Path.Combine(_dir, "f"), updates.Add, TestContext.Current.CancellationToken);

        Assert.Equal(2, count.Files);
        Assert.Equal(1, count.Folders);
        Assert.Equal(8, count.Bytes);
        Assert.True(count.Done);
        Assert.True(updates[^1].Done);
    }

    [Fact]
    public void Signature_SignedProgram_IsValid()
    {
        var dotnet = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", "..", "dotnet.exe");
        Assert.SkipUnless(File.Exists(dotnet), "dotnet.exe not found next to the runtime");

        var s = FileSignature.Check(dotnet);
        Assert.Equal(SignatureState.Valid, s.State);
        Assert.False(s.ThroughCatalog);
        Assert.False(string.IsNullOrEmpty(s.SignedBy));
        Assert.Contains("Microsoft", s.IssuedBy);
        Assert.NotNull(s.Certificate);
        Assert.True(s.ValidTo > s.ValidFrom);
        Assert.StartsWith("SHA", s.Algorithm);
    }

    [Fact]
    public void Signature_WindowsFile_IsSignedThroughACatalog()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Assert.SkipUnless(File.Exists(notepad), "no notepad.exe");

        var s = FileSignature.Check(notepad);
        Assert.Equal(SignatureState.Valid, s.State);
        Assert.NotNull(s.SignedBy);
    }

    [Fact]
    public void Signature_ChangedCopy_AndUnsigned()
    {
        var dotnet = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", "..", "dotnet.exe");
        Assert.SkipUnless(File.Exists(dotnet), "dotnet.exe not found next to the runtime");

        var copy = Path.Combine(_dir, "changed.exe");
        File.Copy(dotnet, copy);
        var bytes = File.ReadAllBytes(copy);
        bytes[bytes.Length / 3] ^= 0xFF;
        File.WriteAllBytes(copy, bytes);
        Assert.Equal(SignatureState.Changed, FileSignature.Check(copy).State);

        var plain = Write("plain.exe", "MZ not really");
        Assert.Equal(SignatureState.NotSigned, FileSignature.Check(plain).State);
        Assert.True(FileSignature.CanBeSigned(plain));
        Assert.False(FileSignature.CanBeSigned(Write("a.txt")));
    }

    [Fact]
    public void Security_ReadsOwnerPermissionsAndWhatIMayDo()
    {
        var path = Write("mine.txt");
        var a = FileAccessReader.Read(path);

        Assert.False(string.IsNullOrEmpty(a.Owner));
        Assert.NotEmpty(a.Entries);
        Assert.True(a.CanRead);
        Assert.True(a.CanChange);
        Assert.True(a.CanDelete);

        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.True(FileAccessReader.Read(path).CanRead);
    }

    [Theory]
    [InlineData(System.Security.AccessControl.FileSystemRights.FullControl, AccessLevel.FullControl)]
    [InlineData(System.Security.AccessControl.FileSystemRights.Modify | System.Security.AccessControl.FileSystemRights.Synchronize, AccessLevel.Change)]
    [InlineData(System.Security.AccessControl.FileSystemRights.ReadAndExecute | System.Security.AccessControl.FileSystemRights.Synchronize, AccessLevel.ReadAndRun)]
    [InlineData(System.Security.AccessControl.FileSystemRights.Read, AccessLevel.Read)]
    [InlineData(System.Security.AccessControl.FileSystemRights.Write, AccessLevel.Write)]
    [InlineData(System.Security.AccessControl.FileSystemRights.Delete, AccessLevel.Special)]
    [InlineData(unchecked((System.Security.AccessControl.FileSystemRights)0x10000000), AccessLevel.FullControl)]
    public void Security_Levels(System.Security.AccessControl.FileSystemRights rights, AccessLevel level)
        => Assert.Equal(level, FileAccessReader.Level(rights));

    [Fact]
    public void PreviousVersions_Tokens_AndSharePaths()
    {
        var taken = PreviousVersions.ParseToken("@GMT-2026.10.12-20.00.00");
        Assert.Equal(new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc).ToLocalTime(), taken);
        Assert.Null(PreviousVersions.ParseToken("@GMT-nonsense"));

        Assert.Equal((@"\\localhost\C$", @"\Users\a.txt"), PreviousVersions.SharePath(@"C:\Users\a.txt"));
        Assert.Equal((@"\\server\share", @"\dir\b.txt"), PreviousVersions.SharePath(@"\\server\share\dir\b.txt"));
        Assert.Equal((@"\\server\share", ""), PreviousVersions.SharePath(@"\\server\share"));
        Assert.Null(PreviousVersions.SharePath(@"\\server"));
    }

    [Fact]
    public void PreviousVersions_ListDoesNotThrow()
    {
        var path = Write("a.txt");
        var list = PreviousVersions.List(path, TestContext.Current.CancellationToken);
        Assert.True(list is null || list.All(v => v.Path.Contains("@GMT-", StringComparison.Ordinal)));
    }
}
