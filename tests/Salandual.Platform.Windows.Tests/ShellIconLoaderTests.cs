// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/ShellIconLoaderTests.cs

using Salandual.Platform.Windows.Shell;

namespace Salandual.Platform.Windows.Tests;

public sealed class ShellIconLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-icons-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        var thread = new Thread(() => result = work());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void Load_FileAndFolder_GivesVisiblePixelsAtTheAskedSize(int size)
    {
        var file = Path.Join(_dir, "jegyzet ő.txt");
        File.WriteAllText(file, "x");
        foreach (var path in new[] { file, _dir })
        {
            var icon = OnSta(() => ShellIconLoader.Load(path, size));
            Assert.NotNull(icon);
            Assert.Equal(size, icon.Width);
            Assert.Equal(size, icon.Height);
            Assert.Equal(size * size * 4, icon.Bgra.Length);
            Assert.Contains(icon.Bgra.Where((_, i) => i % 4 == 3), a => a > 0);
            Assert.Contains(icon.Bgra.Where((_, i) => i % 4 == 3), a => a == 0);
        }
    }

    [Fact]
    public void Load_MissingPath_IsNull() => Assert.Null(OnSta(() => ShellIconLoader.Load(Path.Join(_dir, "nincs.txt"), 16)));

    [Fact]
    public void Request_RaisesLoaded_OnceForMergedRequests()
    {
        var file = Path.Join(_dir, "a.json");
        File.WriteAllText(file, "{}");
        using var loader = new ShellIconLoader();
        var results = new List<(string Key, int Size, IconPixels? Icon)>();
        using var done = new ManualResetEventSlim();
        loader.Loaded += (key, size, icon) =>
        {
            lock (results)
            {
                results.Add((key, size, icon));
            }

            done.Set();
        };

        loader.Request("s:json", file, 20);
        loader.Request("s:json", file, 20);
        Assert.True(done.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Thread.Sleep(200);

        lock (results)
        {
            var (key, size, icon) = Assert.Single(results);
            Assert.Equal("s:json", key);
            Assert.Equal(20, size);
            Assert.NotNull(icon);
        }
    }

    [Fact]
    public async Task Thumbnail_IsThePictureItself_AndNothingForAFileWithout()
    {
        var bmp = Path.Join(_dir, "red.bmp");
        const int w = 200, h = 100;
        var pixels = new byte[w * h * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i + (i / 3 / w < h / 2 ? 0 : 2)] = 255;
        }

        using (var s = File.Create(bmp))
        using (var b = new BinaryWriter(s))
        {
            b.Write((ushort)0x4D42);
            b.Write(54 + pixels.Length);
            b.Write(0);
            b.Write(54);
            b.Write(40);
            b.Write(w);
            b.Write(h);
            b.Write((ushort)1);
            b.Write((ushort)24);
            b.Write(new byte[24]);
            b.Write(pixels);
        }

        var thumbnail = await ShellIconLoader.LoadThumbnailAsync(bmp, 64);
        Assert.NotNull(thumbnail);
        Assert.Equal(64, thumbnail.Width);
        Assert.Equal(32, thumbnail.Height);
        var top = (4 * 64 + 32) * 4;
        var bottom = (28 * 64 + 32) * 4;
        Assert.True(thumbnail.Bgra[top + 2] > 200 && thumbnail.Bgra[top] < 50 && thumbnail.Bgra[top + 3] == 255, "red on top");
        Assert.True(thumbnail.Bgra[bottom] > 200 && thumbnail.Bgra[bottom + 2] < 50, "blue below");

        var text = Path.Join(_dir, "plain.txt");
        File.WriteAllText(text, "no picture here");
        Assert.Null(await ShellIconLoader.LoadThumbnailAsync(text, 64));
        Assert.Null(await ShellIconLoader.LoadThumbnailAsync(Path.Join(_dir, "missing.mp4"), 64));
    }
}
