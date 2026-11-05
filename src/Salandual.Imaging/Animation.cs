// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Imaging/Animation.cs

using System.Buffers.Binary;
using ImageMagick;

namespace Salandual.Imaging;

public sealed class AnimatedImage
{
    public AnimatedImage(IReadOnlyList<DecodedImage> frames, IReadOnlyList<int> delays, int plays)
    {
        if (frames.Count < 2 || delays.Count != frames.Count)
        {
            throw new ArgumentException("An animation has two frames or more, each with its time.", nameof(frames));
        }

        Frames = frames;
        Delays = delays;
        Plays = Math.Max(0, plays);
    }

    public IReadOnlyList<DecodedImage> Frames { get; }

    public IReadOnlyList<int> Delays { get; }

    public int Plays { get; }

    public static int DelayOf(long ticks, long ticksPerSecond)
    {
        var ms = ticksPerSecond > 0 ? ticks * 1000 / ticksPerSecond : ticks * 10;
        return ms <= 10 ? 100 : (int)Math.Min(ms, 60_000);
    }
}

public static class Animation
{
    public const long MaxBytes = 512L * 1024 * 1024;

    public const int MaxFrames = 5000;

    public static bool MayMove(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant() is "gif" or "webp" or "png" or "apng" or "jxl";

    public static AnimatedImage? Decode(string path, int maxWidth = 0, int maxHeight = 0, CancellationToken token = default)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        MagickFormat? format = ext switch
        {
            "gif" when GifFrames(path) > 1 => MagickFormat.Gif,
            "webp" when WebPIsAnimated(path) => MagickFormat.WebP,
            "jxl" => MagickFormat.Jxl,
            _ => null,
        };
        if (ext is "png" or "apng")
        {
            return PngIsAnimated(path) ? Apng.Decode(path, maxWidth, maxHeight, token) : null;
        }

        if (format is not { } magickFormat)
        {
            return null;
        }

        token.ThrowIfCancellationRequested();
        MagickDecoder.Configure();
        var settings = new MagickReadSettings { Format = magickFormat };
        using var collection = new MagickImageCollection();
        if (magickFormat == MagickFormat.Jxl)
        {
            collection.Ping(path, settings);
            if (collection.Count < 2)
            {
                return null;
            }

            collection.Clear();
        }

        collection.Read(path, settings);
        if (collection.Count < 2)
        {
            return null;
        }

        token.ThrowIfCancellationRequested();
        var plays = (int)Math.Min(collection[0].AnimationIterations, int.MaxValue);
        collection.Coalesce();
        var count = Math.Min(collection.Count, MaxFrames);
        var (fullW, fullH) = ((int)collection[0].Width, (int)collection[0].Height);
        var (w, h) = (fullW, fullH);
        if ((long)w * h * 4 * count > MaxBytes)
        {
            (w, h) = Wic.Fit(fullW, fullH, maxWidth, maxHeight);
            var over = (double)w * h * 4 * count / MaxBytes;
            if (over > 1)
            {
                var scale = Math.Sqrt(1 / over);
                (w, h) = (Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale)));
            }
        }

        var frames = new List<DecodedImage>(count);
        var delays = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var frame = collection[i];
            delays.Add(AnimatedImage.DelayOf(frame.AnimationDelay, frame.AnimationTicksPerSecond));
            if (frame.Width != w || frame.Height != h)
            {
                frame.Resize(new MagickGeometry((uint)w, (uint)h) { IgnoreAspectRatio = true });
            }

            frame.Depth = 8;
            var pixels = frame.ToByteArray(MagickFormat.Bgra);
            MagickDecoder.Premultiply(pixels);
            frames.Add(new DecodedImage(w, h, pixels, fullW, fullH, "Magick.NET, animated"));
        }

        return new AnimatedImage(frames, delays, plays);
    }

    public static AnimatedImage? TryDecode(string path, int maxWidth = 0, int maxHeight = 0, CancellationToken token = default)
    {
        try
        {
            return Decode(path, maxWidth, maxHeight, token);
        }
        catch (Exception e) when (ImageDecoder.IsDecodeFailure(e))
        {
            return null;
        }
    }

    internal static int GifFrames(string path)
    {
        try
        {
            using var stream = new BufferedStream(File.OpenRead(path), 64 * 1024);
            Span<byte> head = stackalloc byte[13];
            if (stream.ReadAtLeast(head, 13, throwOnEndOfStream: false) < 13 || head[0] != 'G' || head[1] != 'I' || head[2] != 'F')
            {
                return 0;
            }

            if ((head[10] & 0x80) != 0)
            {
                stream.Seek(3 * (1 << ((head[10] & 7) + 1)), SeekOrigin.Current);
            }

            Span<byte> descriptor = stackalloc byte[9];
            var frames = 0;
            while (frames < 2)
            {
                switch (stream.ReadByte())
                {
                    case 0x21:
                        stream.ReadByte();
                        SkipSubBlocks(stream);
                        break;
                    case 0x2C:
                        if (stream.ReadAtLeast(descriptor, 9, throwOnEndOfStream: false) < 9)
                        {
                            return frames;
                        }

                        if ((descriptor[8] & 0x80) != 0)
                        {
                            stream.Seek(3 * (1 << ((descriptor[8] & 7) + 1)), SeekOrigin.Current);
                        }

                        stream.ReadByte();
                        SkipSubBlocks(stream);
                        frames++;
                        break;
                    default:
                        return frames;
                }
            }

            return frames;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void SkipSubBlocks(Stream stream)
    {
        int size;
        while ((size = stream.ReadByte()) > 0)
        {
            stream.Seek(size, SeekOrigin.Current);
        }
    }

    internal static bool WebPIsAnimated(string path)
    {
        Span<byte> head = stackalloc byte[21];
        return Read(path, head) && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)
            && head[12..16].SequenceEqual("VP8X"u8) && (head[20] & 0x02) != 0;
    }

    internal static bool PngIsAnimated(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> chunk = stackalloc byte[8];
            if (stream.ReadAtLeast(chunk, 8, throwOnEndOfStream: false) < 8 || chunk[0] != 0x89 || chunk[1] != 'P' || chunk[2] != 'N' || chunk[3] != 'G')
            {
                return false;
            }

            while (stream.ReadAtLeast(chunk, 8, throwOnEndOfStream: false) == 8)
            {
                var type = chunk[4..8];
                if (type.SequenceEqual("acTL"u8))
                {
                    return true;
                }

                if (type.SequenceEqual("IDAT"u8))
                {
                    return false;
                }

                stream.Seek(BinaryPrimitives.ReadUInt32BigEndian(chunk) + 4L, SeekOrigin.Current);
            }

            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Read(string path, Span<byte> head)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
