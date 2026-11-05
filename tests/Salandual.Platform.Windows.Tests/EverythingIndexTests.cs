// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/EverythingIndexTests.cs

using System.Text;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Tests;

public sealed class EverythingIndexTests
{
    private static byte[] Answer(params (uint Flags, string Name, string Folder)[] items)
    {
        var head = 28 + (items.Length * 12);
        var texts = new MemoryStream();
        var offsets = new List<(uint Name, uint Folder)>();
        foreach (var (_, name, folder) in items)
        {
            var n = (uint)(head + texts.Length);
            texts.Write(Encoding.Unicode.GetBytes(name + "\0"));
            var f = (uint)(head + texts.Length);
            texts.Write(Encoding.Unicode.GetBytes(folder + "\0"));
            offsets.Add((n, f));
        }

        var all = new MemoryStream();
        var w = new BinaryWriter(all);
        foreach (var v in new uint[] { 0, 0, (uint)items.Length, 0, 0, (uint)items.Length, 0 })
        {
            w.Write(v);
        }

        for (var i = 0; i < items.Length; i++)
        {
            w.Write(items[i].Flags);
            w.Write(offsets[i].Name);
            w.Write(offsets[i].Folder);
        }

        w.Write(texts.ToArray());
        return all.ToArray();
    }

    [Fact]
    public void AnAnswer_GivesNamesFoldersAndFolderFlags_NotDrives()
    {
        var found = EverythingIndex.Parse(Answer((0, "budget.xlsx", @"D:\Work"), (1, "Budget", @"E:\Archive"), (2, "C:", ""), (0, "top.txt", "F:")), 10);
        Assert.Equal(3, found.Count);
        Assert.Equal(("budget.xlsx", @"D:\Work", false), (found[0].Name, found[0].Folder, found[0].IsFolder));
        Assert.True(found[1].IsFolder);
        Assert.Equal(@"F:\", found[2].Folder);
        Assert.Single(EverythingIndex.Parse(Answer((0, "a.txt", @"D:\"), (0, "b.txt", @"D:\")), 1));
    }

    [Fact]
    public void ABrokenAnswer_IsReadOnlyWhereItHolds()
    {
        var answer = Answer((0, "good.txt", @"D:\Work"), (0, "bad.txt", @"D:\Work"));
        BitConverter.GetBytes(int.MaxValue).CopyTo(answer, 28 + 12 + 4);
        Assert.Equal(["good.txt"], EverythingIndex.Parse(answer, 10).Select(e => e.Name));

        BitConverter.GetBytes(1_000_000u).CopyTo(answer, 20);
        Assert.Single(EverythingIndex.Parse(answer, 10));
        Assert.Empty(EverythingIndex.Parse(answer.AsSpan(0, 10), 10));
        Assert.Empty(EverythingIndex.Parse(Answer((0, @"..\x", @"D:\"), (0, "x", "relative")), 10));
    }
}
