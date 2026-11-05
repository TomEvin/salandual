// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ArchiveIconTests.cs

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Tests;

public sealed class ArchiveIconTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-archive-icon-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static async Task<Bitmap> IconOf(IconCache cache, string folder, string name)
    {
        var entry = new FileEntry(name, 1, 0, FileAttributes.Archive);
        for (var i = 0; i < 500; i++)
        {
            if (cache.Get(folder, entry, 32) is { } icon)
            {
                return icon;
            }

            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        throw new TimeoutException(name);
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var buffer = new byte[size.Width * size.Height * 4];
        var p = System.Runtime.InteropServices.Marshal.AllocHGlobal(buffer.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), p, buffer.Length, size.Width * 4);
            System.Runtime.InteropServices.Marshal.Copy(p, buffer, 0, buffer.Length);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(p);
        }

        return buffer;
    }

    [AvaloniaFact]
    public async Task AnArchiveWindowsDoesntKnow_HasTheZipIcon_AnUnknownFileKeepsTheBlankPage()
    {
        Assert.SkipUnless(FileDetailsReader.IsUnknownType(".squashfs"), "Something on this PC opens .squashfs files.");
        foreach (var name in new[] { "a.zip", "b.squashfs", "c.salandual-nothing-opens-this" })
        {
            File.WriteAllBytes(Path.Join(_dir, name), [0]);
        }

        using var cache = new IconCache();
        cache.Start();
        var zip = Pixels(await IconOf(cache, _dir, "a.zip"));
        Assert.Equal(zip, Pixels(await IconOf(cache, _dir, "b.squashfs")));
        Assert.NotEqual(zip, Pixels(await IconOf(cache, _dir, "c.salandual-nothing-opens-this")));
    }
}
