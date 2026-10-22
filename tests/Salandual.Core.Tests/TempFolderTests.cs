// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/TempFolderTests.cs

using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class TempFolderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("salandual-temp-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Crashed(string name, params (string Name, int Bytes)[] files)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        foreach (var (file, bytes) in files)
        {
            File.WriteAllBytes(Path.Combine(folder, file), new byte[bytes]);
        }

        Directory.SetCreationTimeUtc(folder, DateTime.UtcNow.AddHours(-1));
        return folder;
    }

    [Fact]
    public void ItsOwnFolder_MadeOnFirstUse_GoesWhenItCloses()
    {
        var temp = new TempFolder(_root);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
        var file = temp.NewFile("page", ".html");
        File.WriteAllText(file, "<p>");
        var list = temp.NewFolder("list");
        File.WriteAllText(Path.Combine(list, "File list.txt"), "a");
        Assert.True(File.Exists(Path.Combine(temp.Own, TempFolder.LockName)));
        var own = temp.Own;

        temp.Dispose();
        Assert.False(Directory.Exists(own));
    }

    [Fact]
    public void AnotherRunningSalandual_ItsFilesAreNotLeftovers()
    {
        using var other = new TempFolder(_root);
        File.WriteAllText(other.NewFile("page", ".html"), "<p>");
        Directory.SetCreationTimeUtc(other.Own, DateTime.UtcNow.AddHours(-1));
        using var temp = new TempFolder(_root);
        File.WriteAllText(temp.NewFile("page", ".html"), "<p>");

        Assert.Equal(TempLeftovers.None.Entries, temp.FindLeftovers().Entries);
        Assert.Equal(0, temp.Delete(new TempLeftovers([other.Own], 1, 3)));
        Assert.True(Directory.Exists(other.Own));
    }

    [Fact]
    public void WhatACrashLeft_CountedAndDeleted_AStartingOneIsNot()
    {
        Crashed("run-1-aaaaaaaa", ("page-1.html", 1000), ("image-2.png", 24));
        File.WriteAllBytes(Path.Combine(_root, "page-old.html"), new byte[100]);
        Directory.CreateDirectory(Path.Combine(_root, "run-2-bbbbbbbb"));

        using var temp = new TempFolder(_root);
        var left = temp.FindLeftovers();
        Assert.Equal((3L, 1124L, 2), (left.Files, left.Bytes, left.Entries.Count));

        Assert.Equal(0, temp.Delete(left));
        Assert.Equal([Path.Combine(_root, "run-2-bbbbbbbb")], Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void AFileInUse_Stays_AndIsCounted()
    {
        var folder = Crashed("run-3-cccccccc", ("a.txt", 5), ("held.txt", 7));
        using var temp = new TempFolder(_root);
        var left = temp.FindLeftovers();
        using (new FileStream(Path.Combine(folder, "held.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(1, temp.Delete(left));
        }

        Assert.Equal([Path.Combine(folder, "held.txt")], Directory.EnumerateFileSystemEntries(folder));
    }

    [Fact]
    public void ClosedWithAFileInUse_GoesAtTheNextStart_WithoutAsking()
    {
        var temp = new TempFolder(_root);
        var held = temp.NewFile("page", ".html");
        File.WriteAllText(held, "<p>");
        var own = temp.Own;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            temp.Dispose();
        }

        Assert.True(File.Exists(Path.Combine(own, TempFolder.ClosedName)));
        Assert.False(File.Exists(Path.Combine(own, TempFolder.LockName)));

        using var next = new TempFolder(_root);
        next.DeleteClosed();
        Assert.False(Directory.Exists(own));
        Assert.Empty(next.FindLeftovers().Entries);
    }
}
