// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Imaging.Tests/ImageDecoderTests.cs

using System.Buffers.Binary;
using System.Diagnostics;

namespace Salandual.Imaging.Tests;

public sealed class ImageDecoderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-imaging-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string? Samples
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Salandual.slnx")))
                {
                    var samples = Path.Combine(dir.FullName, "_samples", "images");
                    return Directory.Exists(samples) ? samples : null;
                }
            }

            return null;
        }
    }

    private string Bmp(string name, int width, int height, Func<int, int, (byte R, byte G, byte B)> color)
    {
        var stride = ((width * 3) + 3) & ~3;
        var data = new byte[54 + (stride * height)];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), 24);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = color(x, y);
                var at = 54 + ((height - 1 - y) * stride) + (x * 3);
                (data[at], data[at + 1], data[at + 2]) = (b, g, r);
            }
        }

        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static (byte B, byte G, byte R, byte A) Pixel(DecodedImage image, int x, int y)
    {
        var at = ((y * image.Width) + x) * 4;
        return (image.Pixels[at], image.Pixels[at + 1], image.Pixels[at + 2], image.Pixels[at + 3]);
    }

    [Fact]
    public void Bmp_DecodesThroughWic_AsPremultipliedBgra()
    {
        var path = Bmp("three.bmp", 3, 2, (x, y) => y == 1 ? ((byte)0, (byte)0, (byte)255) : x switch { 0 => ((byte)255, (byte)0, (byte)0), 1 => ((byte)0, (byte)255, (byte)0), _ => ((byte)10, (byte)20, (byte)30) });

        var image = ImageDecoder.Decode(path, token: TestContext.Current.CancellationToken);

        Assert.StartsWith("WIC", image.Decoder, StringComparison.Ordinal);
        Assert.Equal((3, 2), (image.Width, image.Height));
        Assert.False(image.IsReduced);
        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), Pixel(image, 0, 0));
        Assert.Equal(((byte)0, (byte)255, (byte)0, (byte)255), Pixel(image, 1, 0));
        Assert.Equal(((byte)30, (byte)20, (byte)10, (byte)255), Pixel(image, 2, 0));
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), Pixel(image, 1, 1));
    }

    [Fact]
    public void ABigImage_IsDecodedToFit_AndKnowsItsOwnSize()
    {
        var path = Bmp("wide.bmp", 400, 200, (x, _) => x < 200 ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0));

        var image = ImageDecoder.Decode(path, 100, 100, token: TestContext.Current.CancellationToken);

        Assert.Equal((100, 50), (image.Width, image.Height));
        Assert.Equal((400, 200), (image.FullWidth, image.FullHeight));
        Assert.True(image.IsReduced);
        Assert.Equal(0.25, image.Density);
        Assert.True(Pixel(image, 10, 25).R > 200);
        Assert.True(Pixel(image, 90, 25).R < 50);
    }

    [Fact]
    public void TurnAndFlip_MoveThePixelsWhereTheScreenShowsThem()
    {
        var image = new DecodedImage(2, 1, [0, 0, 255, 255, 0, 255, 0, 255], 2, 1, "test");

        var turned = ImageFormats.Turn(image, 1);
        Assert.Equal((1, 2), (turned.Width, turned.Height));
        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), Pixel(turned, 0, 0));

        var mirrored = ImageFormats.Flip(image, horizontally: true);
        Assert.Equal(((byte)0, (byte)255, (byte)0, (byte)255), Pixel(mirrored, 0, 0));
        var upsideDown = ImageFormats.Flip(turned, horizontally: false);
        Assert.Equal(((byte)0, (byte)255, (byte)0, (byte)255), Pixel(upsideDown, 0, 0));
    }

    [Fact]
    public void AFileThatIsNoImage_SaysNoDecoderCouldReadIt()
    {
        var path = Path.Combine(_dir, "fake.png");
        File.WriteAllText(path, "not an image at all");

        var e = Assert.Throws<ImageDecodeException>(() => ImageDecoder.Decode(path, token: TestContext.Current.CancellationToken));

        Assert.StartsWith(Salandual.Core.Resources.Strings.Image_NoDecoder, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUncompressedDdsTexture_ThatOnlyOurReaderKnows_Decodes()
    {
        var data = new byte[128 + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x20534444);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(76), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(80), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(88), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(92), 0x7C00);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(96), 0x03E0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(100), 0x001F);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(128), 0x7C00);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(130), 0x001F);
        var path = Path.Combine(_dir, "bgr15.dds");
        File.WriteAllBytes(path, data);

        var image = ImageDecoder.Decode(path, token: TestContext.Current.CancellationToken);

        Assert.Equal((2, 1), (image.Width, image.Height));
        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), Pixel(image, 0, 0));
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), Pixel(image, 1, 0));
    }

    [Fact]
    public void ExifOrientation_TurnsEveryLandscapeSampleUpright()
    {
        if (Samples is not { } samples)
        {
            Assert.Skip("The local _samples folder is not here.");
            return;
        }

        var folder = Path.Combine(samples, "jpeg-orientation");
        var reference = ImageDecoder.Decode(Path.Combine(folder, "Landscape_1.jpg"), 60, 60, token: TestContext.Current.CancellationToken);
        for (var n = 0; n <= 8; n++)
        {
            var image = ImageDecoder.Decode(Path.Combine(folder, $"Landscape_{n}.jpg"), 60, 60, token: TestContext.Current.CancellationToken);
            Assert.Equal((reference.Width, reference.Height), (image.Width, image.Height));
            Assert.True(Difference(reference, image) < 12, $"Landscape_{n} is not upright (difference {Difference(reference, image):F1}).");
        }
    }

    private static double Difference(DecodedImage a, DecodedImage b)
    {
        long sum = 0;
        for (var i = 0; i < a.Pixels.Length; i++)
        {
            sum += Math.Abs(a.Pixels[i] - b.Pixels[i]);
        }

        return (double)sum / a.Pixels.Length;
    }

    public static TheoryData<string> SampleFolders() =>
        ["avif", "bmp", "dds", "dds-variants", "gif", "heic", "ico", "jpeg", "jxl", "png", "psd", "raw", "svg", "tga", "tiff", "webp"];

    [Theory]
    [MemberData(nameof(SampleFolders))]
    public void EverySample_Decodes(string folder)
    {
        if (Samples is not { } samples)
        {
            Assert.Skip("The local _samples folder is not here.");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(samples, folder)))
        {
            var clock = Stopwatch.StartNew();
            var image = ImageDecoder.Decode(file, 1920, 1080, token: TestContext.Current.CancellationToken);
            TestContext.Current.TestOutputHelper?.WriteLine($"{Path.GetFileName(file)}: {image.Width}×{image.Height} of {image.FullWidth}×{image.FullHeight}, {image.Decoder}, {clock.ElapsedMilliseconds} ms");
            Assert.True(image.Width <= 1920 && image.Height <= 1080);
            Assert.Equal(ImageDecoder.IsRaw(file), image.IsPreview);
            Assert.Contains(image.Pixels, b => b != 0);
        }
    }

    [Fact]
    public void ImagesDeeperThan8Bits_ThroughMagick_KeepTheirPixelsAndAlpha()
    {
        if (Samples is not { } samples)
        {
            Assert.Skip("The local _samples folder is not here.");
            return;
        }

        var jxl = ImageDecoder.Decode(Path.Combine(samples, "jxl", "alpha_triangles.jxl"), token: TestContext.Current.CancellationToken);
        Assert.Equal("Magick.NET", jxl.Decoder);
        Assert.Equal(0, Pixel(jxl, 10, 10).A);
        Assert.Equal(255, Pixel(jxl, 1000, 20).A);
        Assert.True(Pixel(jxl, 1000, 20).G > 200);
        Assert.Equal(255, Pixel(jxl, 20, 1000).A);
        Assert.True(Pixel(jxl, 20, 1000).R > 200);

        foreach (var name in new[] { "psdtools-16bit5x5.psd", "psdtools-32bit.psd" })
        {
            var psd = ImageDecoder.Decode(Path.Combine(samples, "adobe", name), token: TestContext.Current.CancellationToken);
            Assert.Equal("Magick.NET", psd.Decoder);
            Assert.Contains(psd.Pixels, b => b != 0);
        }
    }

    [Fact]
    public void ARawFile_ShowsItsPreviewFirst_AndItsSensorDataOnRequest()
    {
        if (Samples is not { } samples)
        {
            Assert.Skip("The local _samples folder is not here.");
            return;
        }

        var file = Path.Combine(samples, "raw", "Nikon_D750.NEF");
        var preview = ImageDecoder.Decode(file, 1000, 1000, token: TestContext.Current.CancellationToken);
        Assert.True(preview.IsPreview);
        Assert.InRange(preview.FullWidth, 6000, 6100);
        Assert.InRange(preview.FullHeight, 4000, 4100);

        var full = ImageDecoder.DecodeFull(file, 1000, 1000, token: TestContext.Current.CancellationToken);
        Assert.False(full.IsPreview);
        Assert.InRange(full.Width, preview.Width - 2, preview.Width + 2);
        Assert.InRange(full.Height, preview.Height - 2, preview.Height + 2);
    }
}
