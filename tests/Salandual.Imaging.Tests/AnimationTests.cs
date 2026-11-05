// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Imaging.Tests/AnimationTests.cs

using System.Buffers.Binary;
using ImageMagick;

namespace Salandual.Imaging.Tests;

public sealed class AnimationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-animation-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Made(string name, MagickFormat format, int frames = 3, uint loops = 0)
    {
        using var collection = new MagickImageCollection();
        int[] delays = [20, 0, 70];
        for (var i = 0; i < frames; i++)
        {
            var frame = new MagickImage(MagickColors.Transparent, 30, 10);
            using (var square = new MagickImage(MagickColors.Red, 10, 10))
            {
                frame.Composite(square, i * 10, 0, CompositeOperator.Over);
            }

            frame.AnimationDelay = (uint)delays[i % 3];
            frame.AnimationIterations = loops;
            frame.GifDisposeMethod = GifDisposeMethod.Background;
            collection.Add(frame);
        }

        var path = Path.Combine(_dir, name);
        collection.Write(path, format);
        return path;
    }

    private static byte AlphaAt(DecodedImage image, int x, int y) => image.Pixels[(((y * image.Width) + x) * 4) + 3];

    [Fact]
    public void AnAnimatedGif_GivesEveryFrame_WithItsTime_AndHowOftenItPlays()
    {
        var path = Made("moving.gif", MagickFormat.Gif, loops: 2);

        Assert.Equal(2, Animation.GifFrames(path));
        var animation = Animation.Decode(path, token: TestContext.Current.CancellationToken);

        Assert.NotNull(animation);
        Assert.Equal(3, animation.Frames.Count);
        Assert.Equal([200, 100, 700], animation.Delays);
        Assert.Equal(2, animation.Plays);
        Assert.All(animation.Frames, f => Assert.Equal((30, 10), (f.Width, f.Height)));
        Assert.Equal(255, AlphaAt(animation.Frames[1], 15, 5));
        Assert.Equal(0, AlphaAt(animation.Frames[1], 25, 5));
        Assert.True(animation.Frames[0].HasTransparency);
    }

    [Fact]
    public void AnAnimatedWebP_IsFound_AndPlaysForEver()
    {
        var path = Made("moving.webp", MagickFormat.WebP);

        Assert.True(Animation.WebPIsAnimated(path));
        var animation = Animation.Decode(path, token: TestContext.Current.CancellationToken);

        Assert.NotNull(animation);
        Assert.Equal(3, animation.Frames.Count);
        Assert.Equal(0, animation.Plays);
    }

    [Fact]
    public void StillImages_AreNotDecodedAgain()
    {
        var gif = Made("still.gif", MagickFormat.Gif, frames: 1);
        var png = Path.Combine(_dir, "still.png");
        using (var image = new MagickImage(MagickColors.Blue, 4, 4))
        {
            image.Write(png, MagickFormat.Png);
        }

        Assert.Equal(1, Animation.GifFrames(gif));
        Assert.Null(Animation.Decode(gif, token: TestContext.Current.CancellationToken));
        Assert.False(Animation.PngIsAnimated(png));
        Assert.Null(Animation.Decode(png, token: TestContext.Current.CancellationToken));
        Assert.False(Animation.MayMove(Path.Combine(_dir, "photo.jpg")));
        Assert.False(new DecodedImage(1, 1, [0, 0, 255, 255], 1, 1, "test").HasTransparency);
    }

    private string Apng()
    {
        using var file = new MemoryStream();
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string type, byte[] data)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
            file.Write(number);
            var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            file.Write(typed);
            BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typed));
            file.Write(number);
        }

        static uint Crc(byte[] bytes)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }

            return ~crc;
        }

        byte[] BigEndian(params uint[] values) => values.SelectMany(v => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }).ToArray();
        byte[] Pixels(byte r, byte g, byte b)
        {
            using var packed = new MemoryStream();
            using (var z = new System.IO.Compression.ZLibStream(packed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                for (var y = 0; y < 2; y++)
                {
                    z.WriteByte(0);
                    for (var x = 0; x < 4; x++)
                    {
                        z.Write([r, g, b, 255]);
                    }
                }
            }

            return packed.ToArray();
        }

        byte[] Control(uint sequence) => [.. BigEndian(sequence, 4, 2, 0, 0), 0, 5, 0, 100, 0, 0];
        Chunk("IHDR", [.. BigEndian(4, 2), 8, 6, 0, 0, 0]);
        Chunk("acTL", BigEndian(2, 0));
        Chunk("fcTL", Control(0));
        Chunk("IDAT", Pixels(255, 0, 0));
        Chunk("fcTL", Control(1));
        Chunk("fdAT", [.. BigEndian(2), .. Pixels(0, 0, 255)]);
        Chunk("IEND", []);
        var path = Path.Combine(_dir, "moving.png");
        File.WriteAllBytes(path, file.ToArray());
        return path;
    }

    [Fact]
    public void AnApng_IsFound_AndPlays()
    {
        var path = Apng();

        Assert.True(Animation.PngIsAnimated(path));
        var animation = Animation.Decode(path, token: TestContext.Current.CancellationToken);

        Assert.NotNull(animation);
        Assert.Equal(2, animation.Frames.Count);
        Assert.Equal([50, 50], animation.Delays);
        Assert.Equal(255, animation.Frames[1].Pixels[0]);
    }

    [Fact]
    public void ADelay_IsTakenAsBrowsersTakeIt()
    {
        Assert.Equal(40, AnimatedImage.DelayOf(4, 100));
        Assert.Equal(100, AnimatedImage.DelayOf(0, 100));
        Assert.Equal(100, AnimatedImage.DelayOf(1, 100));
        Assert.Equal(50, AnimatedImage.DelayOf(50, 1000));
    }

    [Theory]
    [InlineData("gif/animated-iss634.gif")]
    [InlineData("webp/animated.webp")]
    [InlineData("jxl/animation_icos4d.jxl")]
    public void TheSamples_Play(string sample)
    {
        string? root = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && root is null; dir = dir.Parent)
        {
            root = File.Exists(Path.Combine(dir.FullName, "Salandual.slnx")) ? dir.FullName : null;
        }

        var path = Path.Combine(root ?? "", "_samples", "images", sample);
        Assert.SkipUnless(File.Exists(path), "No local samples.");

        var animation = Animation.Decode(path, token: TestContext.Current.CancellationToken);

        Assert.NotNull(animation);
        Assert.True(animation.Frames.Count > 1);
        Assert.All(animation.Delays, d => Assert.InRange(d, 11, 60_000));
    }
}
