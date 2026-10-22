// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Imaging/ImageFormats.cs

namespace Salandual.Imaging;

public static class ImageFormats
{
    private static readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = "JPEG", ["jpeg"] = "JPEG", ["jpe"] = "JPEG", ["jfif"] = "JPEG",
        ["png"] = "PNG", ["gif"] = "GIF", ["bmp"] = "BMP", ["dib"] = "BMP",
        ["tif"] = "TIFF", ["tiff"] = "TIFF", ["ico"] = "ICO", ["cur"] = "CUR",
        ["webp"] = "WebP", ["avif"] = "AVIF", ["heic"] = "HEIF", ["heif"] = "HEIF",
        ["jxl"] = "JPEG XL", ["jxr"] = "JPEG XR", ["wdp"] = "JPEG XR", ["hdp"] = "JPEG XR",
        ["jp2"] = "JPEG 2000", ["j2k"] = "JPEG 2000",
    };

    public static string? Name(string path)
    {
        if (ImageDecoder.IsRaw(path))
        {
            return null;
        }

        var ext = Path.GetExtension(path).TrimStart('.');
        return _names.TryGetValue(ext, out var name) ? name : ext.ToUpperInvariant();
    }

    public static DecodedImage Turn(DecodedImage image, int quarterTurns)
    {
        var turns = ((quarterTurns % 4) + 4) % 4;
        if (turns == 0)
        {
            return image;
        }

        int w = image.Width, h = image.Height;
        var (outW, outH) = turns == 2 ? (w, h) : (h, w);
        var source = image.Pixels;
        var pixels = GC.AllocateUninitializedArray<byte>(w * h * 4);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (tx, ty) = turns switch
                {
                    1 => (h - 1 - y, x),
                    2 => (w - 1 - x, h - 1 - y),
                    _ => (y, w - 1 - x),
                };
                Buffer.BlockCopy(source, ((y * w) + x) * 4, pixels, ((ty * outW) + tx) * 4, 4);
            }
        }

        var (fullW, fullH) = turns == 2 ? (image.FullWidth, image.FullHeight) : (image.FullHeight, image.FullWidth);
        return new DecodedImage(outW, outH, pixels, fullW, fullH, image.Decoder) { IsPreview = image.IsPreview };
    }

    public static DecodedImage Flip(DecodedImage image, bool horizontally)
    {
        int w = image.Width, h = image.Height;
        var pixels = GC.AllocateUninitializedArray<byte>(w * h * 4);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (tx, ty) = horizontally ? (w - 1 - x, y) : (x, h - 1 - y);
                Buffer.BlockCopy(image.Pixels, ((y * w) + x) * 4, pixels, ((ty * w) + tx) * 4, 4);
            }
        }

        return new DecodedImage(w, h, pixels, image.FullWidth, image.FullHeight, image.Decoder) { IsPreview = image.IsPreview };
    }

    public static byte[] ToDib(DecodedImage image)
    {
        int w = image.Width, h = image.Height;
        var dib = new byte[40 + (w * h * 4)];
        var header = dib.AsSpan();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, 40);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[4..], w);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[8..], h);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(header[12..], 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(header[14..], 32);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[20..], w * h * 4);
        for (var y = 0; y < h; y++)
        {
            var from = y * w * 4;
            var to = 40 + ((h - 1 - y) * w * 4);
            for (var x = 0; x < w * 4; x += 4)
            {
                var a = image.Pixels[from + x + 3];
                for (var c = 0; c < 3; c++)
                {
                    var v = image.Pixels[from + x + c];
                    dib[to + x + c] = a is 0 or 255 ? v : (byte)Math.Min(255, ((v * 255) + (a / 2)) / a);
                }

                dib[to + x + 3] = a;
            }
        }

        return dib;
    }
}
