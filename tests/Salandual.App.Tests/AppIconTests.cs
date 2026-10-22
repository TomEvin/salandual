// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/AppIconTests.cs

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Salandual.App.Theme;

namespace Salandual.App.Tests;

public sealed class AppIconTests
{
    private static readonly int[] _sizes = [16, 20, 24, 32, 40, 48, 64, 256];

    [AvaloniaTheory]
    [InlineData(false, "salandual")]
    [InlineData(true, "salandual-dev")]
    public void IconFile_IsDrawnFromTheMark(bool dev, string name)
    {
        var file = Path.Combine(RepoRoot(), "src", "Salandual.App", "Assets", name + ".ico");
        var drawn = _sizes.Select(s => (Size: s, Png: Render(s, dev))).ToList();
        if (Environment.GetEnvironmentVariable("SALANDUAL_WRITE_ICON") == "1")
        {
            File.WriteAllBytes(file, Ico(drawn));
        }

        var entries = ReadIco(File.ReadAllBytes(file));
        Assert.Equal(_sizes, entries.Select(e => e.Size));
        foreach (var (size, png) in drawn)
        {
            var expected = Pixels(png);
            var actual = Pixels(entries.Single(e => e.Size == size).Png);
            Assert.Equal(expected.Length, actual.Length);
            Assert.True(expected.Zip(actual).All(p => Math.Abs(p.First - p.Second) <= 3), $"{size} px differs from the mark: run with SALANDUAL_WRITE_ICON=1");
        }
    }

    [AvaloniaTheory]
    [InlineData(false, "salandual-1024.png")]
    [InlineData(true, "salandual-dev-1024.png")]
    public void BigPicture_IsDrawnFromTheMark(bool dev, string name)
    {
        var file = Path.Combine(RepoRoot(), "src", "Salandual.App", "Assets", name);
        var drawn = Render(1024, dev);
        if (Environment.GetEnvironmentVariable("SALANDUAL_WRITE_ICON") == "1")
        {
            File.WriteAllBytes(file, drawn);
        }

        var expected = Pixels(drawn);
        var actual = Pixels(File.ReadAllBytes(file));
        Assert.Equal(1024 * 1024 * 4, actual.Length);
        Assert.True(expected.Zip(actual).All(p => Math.Abs(p.First - p.Second) <= 3), $"{name} differs from the mark: run with SALANDUAL_WRITE_ICON=1");
    }

    [AvaloniaFact]
    public void Dev_IsVioletWithAPinkMark_TheReleaseBlue()
    {
        foreach (var size in new[] { 16, 256 })
        {
            var release = Pixels(Render(size, dev: false));
            var dev = Pixels(Render(size, dev: true));
            int At(byte[] px, int x, int y, int c) => px[(((y * size) + x) * 4) + c];

            var x0 = size / 8;
            Assert.True(At(release, x0, x0, 0) > At(release, x0, x0, 2) + 60);
            Assert.True(At(dev, x0, x0, 2) > At(dev, x0, x0, 1) + 10);

            var (x1, y1) = size == 16 ? (13, 13) : (234, 230);
            Assert.True(At(dev, x1, y1, 2) > 200 && At(dev, x1, y1, 1) < 120, $"{size}: no pink at the bottom right");
        }
    }

    [AvaloniaFact]
    public void Mark_UsesTheWholeBox_WithSeeThroughCorners()
    {
        foreach (var size in new[] { 16, 256 })
        {
            var px = Pixels(Render(size));
            int Alpha(int x, int y) => px[(((y * size) + x) * 4) + 3];
            Assert.Equal(0, Alpha(0, 0));
            Assert.Equal(255, Alpha(size / 2, size / 2));
        }
    }

    [AvaloniaFact]
    public void Windows_HaveTheIcon()
    {
        var w = new MainWindow();
        w.Show();
        Assert.NotNull(w.Icon);
        w.CloseNow();
    }

    private static byte[] Render(int size, bool dev = false)
    {
        var mark = AppIcons.Mark(size, dev);
        mark.Measure(new Size(size, size));
        mark.Arrange(new Rect(0, 0, size, size));
        using var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        target.Render(mark);
        using var png = new MemoryStream();
        target.Save(png, PngBitmapEncoderOptions.Default);
        return png.ToArray();
    }

    private static byte[] Pixels(byte[] png)
    {
        using var bitmap = new Bitmap(new MemoryStream(png));
        var size = bitmap.PixelSize;
        var pixels = new byte[size.Width * size.Height * 4];
        var buffer = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), buffer, pixels.Length, size.Width * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    private static byte[] Ico(List<(int Size, byte[] Png)> images)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)images.Count);
        var offset = 6 + (16 * images.Count);
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            w.Write(png);
        }

        return ms.ToArray();
    }

    private static List<(int Size, byte[] Png)> ReadIco(byte[] ico)
    {
        var count = BitConverter.ToUInt16(ico, 4);
        var list = new List<(int, byte[])>();
        for (var i = 0; i < count; i++)
        {
            var e = 6 + (16 * i);
            var size = ico[e] == 0 ? 256 : ico[e];
            var length = BitConverter.ToInt32(ico, e + 8);
            var offset = BitConverter.ToInt32(ico, e + 12);
            list.Add((size, ico.AsSpan(offset, length).ToArray()));
        }

        return list;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Salandual.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (Salandual.slnx) not found.");
    }
}
