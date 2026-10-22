// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Imaging/MagickDecoder.cs

using ImageMagick;

namespace Salandual.Imaging;

internal static class MagickDecoder
{
    private static int _configured;

    public static DecodedImage Decode(string path, int maxWidth, int maxHeight, bool fastRaw = false)
    {
        Configure();
        var settings = Settings(path);
        if (fastRaw)
        {
            settings.SetDefine(MagickFormat.Dng, "interpolation-quality", "0");
        }

        using var image = new MagickImage(path, settings);
        image.AutoOrient();
        var (fullW, fullH) = ((int)image.Width, (int)image.Height);
        var (w, h) = Wic.Fit(fullW, fullH, maxWidth, maxHeight);
        if ((long)w * h > ImageDecoder.MaxPixels)
        {
            throw new ImageDecodeException(Salandual.Core.Resources.Strings.Image_TooBig);
        }

        if (w != fullW || h != fullH)
        {
            image.Resize(new MagickGeometry((uint)w, (uint)h) { IgnoreAspectRatio = true });
        }

        image.Depth = 8;
        var pixels = image.ToByteArray(MagickFormat.Bgra);
        Premultiply(pixels);
        return new DecodedImage((int)image.Width, (int)image.Height, pixels, fullW, fullH, "Magick.NET");
    }

    public static (byte[] Jpeg, int Width, int Height, int Orientation)? RawPreview(string path)
    {
        Configure();
        var settings = Settings(path);
        settings.SetDefine(MagickFormat.Dng, "read-thumbnail", "true");
        using var image = new MagickImage();
        image.Ping(path, settings);
        var orientation = (int)image.Orientation is var o and >= 1 and <= 8 ? o : 1;
        return image.GetProfile("dng:thumbnail")?.ToByteArray() is { Length: > 0 } jpeg
            ? (jpeg, (int)image.Width, (int)image.Height, orientation)
            : null;
    }

    private static MagickReadSettings Settings(string path)
    {
        var settings = new MagickReadSettings { FrameIndex = 0, FrameCount = 1 };
        if (FormatOf(path) is { } format)
        {
            settings.Format = format;
        }

        return settings;
    }

    internal static MagickFormat? FormatOf(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        return ext.Length > 0 && Enum.TryParse<MagickFormat>(ext, ignoreCase: true, out var format) && format != MagickFormat.Unknown ? format : null;
    }

    private static void Configure()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 0)
        {
            ResourceLimits.Width = 100_000;
            ResourceLimits.Height = 100_000;
            ResourceLimits.Area = (ulong)ImageDecoder.MaxPixels * 4;
        }
    }

    internal static void Premultiply(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a == 255)
            {
                continue;
            }

            bgra[i] = (byte)((bgra[i] * a + 127) / 255);
            bgra[i + 1] = (byte)((bgra[i + 1] * a + 127) / 255);
            bgra[i + 2] = (byte)((bgra[i + 2] * a + 127) / 255);
        }
    }
}
