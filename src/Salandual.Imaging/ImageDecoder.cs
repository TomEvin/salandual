// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Imaging/ImageDecoder.cs

using System.Runtime.InteropServices;

namespace Salandual.Imaging;

public static class ImageDecoder
{
    public const int MaxSide = 16384;

    public const long MaxPixels = 256L * 1024 * 1024;

    private static readonly HashSet<string> _wicFirst = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "jpe", "jfif", "png", "gif", "bmp", "dib", "tif", "tiff", "ico", "cur", "jxr", "wdp", "hdp", "webp", "heic", "heif", "avif",
    };

    private static readonly HashSet<string> _raw = new(StringComparer.OrdinalIgnoreCase)
    {
        "3fr", "ari", "arw", "bay", "cr2", "cr3", "crw", "dcr", "dng", "erf", "fff", "iiq", "k25", "kdc", "mef", "mos", "mrw", "nef", "nrw",
        "orf", "pef", "raf", "raw", "rw2", "rwl", "sr2", "srf", "srw", "x3f",
    };

    private static readonly HashSet<string> _turnsItself = new(StringComparer.OrdinalIgnoreCase) { "heic", "heif", "avif" };

    private static string Ext(string path) => Path.GetExtension(path).TrimStart('.');

    public static bool IsRaw(string path) => _raw.Contains(Ext(path));

    public static DecodedImage Decode(string path, int maxWidth = 0, int maxHeight = 0, CancellationToken token = default)
    {
        var ext = Ext(path);
        if (_raw.Contains(ext))
        {
            return RawPreview(path, maxWidth, maxHeight, token) ?? DecodeFull(path, maxWidth, maxHeight, token);
        }

        var errors = new List<Exception>();
        foreach (var decoder in Chain(ext))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var image = decoder(path, maxWidth, maxHeight);
                return image.Width > maxWidth && maxWidth > 0 || image.Height > maxHeight && maxHeight > 0 || image.Width > MaxSide || image.Height > MaxSide
                    ? Wic.Scale(image, maxWidth, maxHeight)
                    : image;
            }
            catch (Exception e) when (IsDecodeFailure(e))
            {
                errors.Add(e);
            }
        }

        throw Failure(errors);
    }

    public static DecodedImage DecodeFull(string path, int maxWidth = 0, int maxHeight = 0, CancellationToken token = default)
    {
        if (!IsRaw(path))
        {
            return Decode(path, maxWidth, maxHeight, token);
        }

        var errors = new List<Exception>();
        try
        {
            return Wic.DecodeFile(path, maxWidth, maxHeight, exifOrientation: true, largestFrame: false);
        }
        catch (Exception e) when (IsDecodeFailure(e))
        {
            errors.Add(e);
        }

        token.ThrowIfCancellationRequested();
        try
        {
            return MagickDecoder.Decode(path, maxWidth, maxHeight, fastRaw: true);
        }
        catch (Exception e) when (IsDecodeFailure(e))
        {
            errors.Add(e);
        }

        throw Failure(errors);
    }

    private static DecodedImage? RawPreview(string path, int maxWidth, int maxHeight, CancellationToken token)
    {
        try
        {
            if (MagickDecoder.RawPreview(path) is not { } preview)
            {
                return null;
            }

            token.ThrowIfCancellationRequested();
            var image = Wic.DecodeBytes(preview.Jpeg, maxWidth, maxHeight, preview.Orientation);
            var swapped = preview.Orientation is >= 5 and <= 8;
            var (fullW, fullH) = swapped ? (preview.Height, preview.Width) : (preview.Width, preview.Height);
            return new DecodedImage(image.Width, image.Height, image.Pixels, Math.Max(fullW, image.FullWidth), Math.Max(fullH, image.FullHeight), "Embedded preview, " + image.Decoder)
            {
                IsPreview = true,
            };
        }
        catch (Exception e) when (IsDecodeFailure(e))
        {
            return null;
        }
    }

    private delegate DecodedImage Decoder(string path, int maxWidth, int maxHeight);

    private static IEnumerable<Decoder> Chain(string ext)
    {
        Decoder wic = (p, w, h) => Wic.DecodeFile(p, w, h, exifOrientation: !_turnsItself.Contains(ext), largestFrame: ext is "ico" or "cur");
        Decoder magick = (p, w, h) => MagickDecoder.Decode(p, w, h);
        if (ext.Equals("dds", StringComparison.OrdinalIgnoreCase))
        {
            return [wic, (p, _, _) => Dds.Blocks(p), (p, _, _) => Dds.Uncompressed(p), magick];
        }

        return _wicFirst.Contains(ext) ? [wic, magick] : [magick, wic];
    }

    private static bool IsDecodeFailure(Exception e) => e is not OperationCanceledException and not OutOfMemoryException
        && e is COMException or ImageMagick.MagickException or IOException or InvalidDataException or NotSupportedException or ArgumentException
            or UnauthorizedAccessException or ImageDecodeException or IndexOutOfRangeException or InvalidOperationException or FormatException or OverflowException;

    private static Exception Failure(List<Exception> errors)
    {
        if (errors.OfType<ImageDecodeException>().FirstOrDefault() is { } refused)
        {
            return refused;
        }

        if (errors.FirstOrDefault(e => e is IOException or UnauthorizedAccessException) is { } io)
        {
            return io;
        }

        var last = errors.LastOrDefault();
        return new ImageDecodeException(last is null ? Salandual.Core.Resources.Strings.Image_NoDecoder : Salandual.Core.Resources.Strings.Image_NoDecoder + " " + Short(last.Message), last);
    }

    private static string Short(string message)
    {
        var line = message.Split('\n')[0].Trim();
        return line.Length > 200 ? line[..200] + "…" : line;
    }
}
