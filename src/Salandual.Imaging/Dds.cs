// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Imaging/Dds.cs

using System.Buffers.Binary;
using System.Numerics;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;

namespace Salandual.Imaging;

internal static class Dds
{
    private const uint AlphaPixels = 0x1, Alpha = 0x2, FourCc = 0x4, Palette8 = 0x20, Rgb = 0x40, Luminance = 0x20000;

    public static DecodedImage Blocks(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new BcDecoder();
        var dds = DdsFile.Load(stream);
        var format = dds.header.ddsPixelFormat.IsDxt10Format ? dds.dx10Header.dxgiFormat.ToString() : dds.header.ddsPixelFormat.dwFourCc.ToString();
        if (format.Contains("BC6H", StringComparison.OrdinalIgnoreCase))
        {
            var hdr = decoder.DecodeHdr2D(dds).Span;
            var px = new byte[hdr.Width * hdr.Height * 4];
            var i = 0;
            for (var y = 0; y < hdr.Height; y++)
            {
                for (var x = 0; x < hdr.Width; x++)
                {
                    var c = hdr[y, x];
                    px[i++] = Gamma(c.b);
                    px[i++] = Gamma(c.g);
                    px[i++] = Gamma(c.r);
                    px[i++] = 255;
                }
            }

            return new DecodedImage(hdr.Width, hdr.Height, px, hdr.Width, hdr.Height, "BCnEncoder (HDR)");
        }

        var single = format.Contains("Bc4", StringComparison.OrdinalIgnoreCase) || dds.header.ddsPixelFormat.dwFourCc.ToString() == "826889281";
        var image = decoder.Decode2D(dds).Span;
        var pixels = new byte[image.Width * image.Height * 4];
        var at = 0;
        for (var y = 0; y < image.Height; y++)
        {
            foreach (var c in image.GetRowSpan(y))
            {
                pixels[at++] = single ? c.r : c.b;
                pixels[at++] = single ? c.r : c.g;
                pixels[at++] = c.r;
                pixels[at++] = c.a;
            }
        }

        MagickDecoder.Premultiply(pixels);
        return new DecodedImage(image.Width, image.Height, pixels, image.Width, image.Height, "BCnEncoder");
    }

    public static DecodedImage Uncompressed(string path)
    {
        var d = File.ReadAllBytes(path);
        if (d.Length < 128 || BinaryPrimitives.ReadUInt32LittleEndian(d) != 0x20534444)
        {
            throw new InvalidDataException("Not a DDS file.");
        }

        var h = (int)U32(d, 12);
        var w = (int)U32(d, 16);
        if (w <= 0 || h <= 0 || (long)w * h > ImageDecoder.MaxPixels)
        {
            throw new InvalidDataException("Bad DDS size.");
        }

        var flags = U32(d, 80);
        var px = new byte[w * h * 4];
        if ((flags & FourCc) != 0 && U32(d, 84) == 0x30315844 )
        {
            var dxgi = U32(d, 128);
            if (dxgi is not (28 or 29 or 87 or 88 or 91 or 93))
            {
                throw new NotSupportedException($"DXGI format {dxgi}.");
            }

            var bgra = dxgi is 87 or 88 or 91 or 93;
            var noAlpha = dxgi is 88 or 93;
            Need(d, 148, (long)w * h * 4);
            for (int i = 0, src = 148; i < w * h; i++, src += 4)
            {
                px[i * 4] = d[src + (bgra ? 0 : 2)];
                px[(i * 4) + 1] = d[src + 1];
                px[(i * 4) + 2] = d[src + (bgra ? 2 : 0)];
                px[(i * 4) + 3] = noAlpha ? (byte)255 : d[src + 3];
            }

            return Done(w, h, px);
        }

        var bits = (int)U32(d, 88);
        if ((flags & Palette8) != 0)
        {
            Need(d, 128 + 1024, (long)w * h);
            var palette = d.AsSpan(128, 1024);
            for (var i = 0; i < w * h; i++)
            {
                var p = palette.Slice(d[128 + 1024 + i] * 4, 4);
                px[i * 4] = p[2];
                px[(i * 4) + 1] = p[1];
                px[(i * 4) + 2] = p[0];
                px[(i * 4) + 3] = 255;
            }

            return Done(w, h, px);
        }

        if ((flags & (Rgb | Luminance | Alpha)) == 0 || bits is not (8 or 16 or 24 or 32))
        {
            throw new NotSupportedException($"Pixel format flags 0x{flags:X}, {bits} bpp.");
        }

        uint rMask = FitMask(U32(d, 92), bits), gMask = FitMask(U32(d, 96), bits), bMask = FitMask(U32(d, 100), bits), aMask = (flags & (AlphaPixels | Alpha)) != 0 ? FitMask(U32(d, 104), bits) : 0;
        var bytesPerPixel = bits / 8;
        Need(d, 128, (long)w * h * bytesPerPixel);
        for (int i = 0, at = 128; i < w * h; i++, at += bytesPerPixel)
        {
            uint v = 0;
            for (var b = 0; b < bytesPerPixel; b++)
            {
                v |= (uint)d[at + b] << (8 * b);
            }

            byte r, g, bl;
            if ((flags & Luminance) != 0)
            {
                r = g = bl = Channel(v, rMask);
            }
            else if ((flags & Alpha) != 0 && (flags & Rgb) == 0)
            {
                r = g = bl = 255;
            }
            else
            {
                (r, g, bl) = (Channel(v, rMask), Channel(v, gMask), Channel(v, bMask));
            }

            px[i * 4] = bl;
            px[(i * 4) + 1] = g;
            px[(i * 4) + 2] = r;
            px[(i * 4) + 3] = aMask == 0 ? (byte)255 : Channel(v, aMask);
        }

        return Done(w, h, px);
    }

    private static DecodedImage Done(int w, int h, byte[] px)
    {
        MagickDecoder.Premultiply(px);
        return new DecodedImage(w, h, px, w, h, "DDS reader");
    }

    private static void Need(byte[] d, int at, long length)
    {
        if (at + length > d.Length)
        {
            throw new InvalidDataException("The DDS file is cut short.");
        }
    }

    private static byte Gamma(float v) => (byte)Math.Clamp(MathF.Round(MathF.Pow(Math.Clamp(v, 0, 1), 1 / 2.2f) * 255), 0, 255);

    private static uint FitMask(uint mask, int bits)
        => bits < 32 && mask != 0 && (mask & ((1u << bits) - 1)) == 0 ? mask >> (32 - bits) : mask;

    private static byte Channel(uint value, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        var shift = BitOperations.TrailingZeroCount(mask);
        var max = mask >> shift;
        return (byte)(((value & mask) >> shift) * 255 / max);
    }

    private static uint U32(byte[] d, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(offset));
}
