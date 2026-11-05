// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Imaging/Apng.cs

using System.Buffers.Binary;

namespace Salandual.Imaging;

internal static class Apng
{
    private static readonly byte[] _signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly HashSet<string> _shared = ["PLTE", "tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT"];

    private sealed class Frame
    {
        public int Width, Height, X, Y, Dispose, Blend, Delay;
        public readonly MemoryStream Data = new();
    }

    public static AnimatedImage? Decode(string path, int maxWidth, int maxHeight, CancellationToken token)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(_signature))
        {
            return null;
        }

        byte[]? header = null;
        var shared = new List<(string Type, byte[] Data)>();
        var frames = new List<Frame>();
        var plays = 0;
        Frame? current = null;
        for (var at = 8; at + 12 <= bytes.Length;)
        {
            var length = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at)), int.MaxValue);
            var type = System.Text.Encoding.ASCII.GetString(bytes, at + 4, 4);
            if (length > bytes.Length - at - 12)
            {
                throw new InvalidDataException("A chunk runs past the end of the file.");
            }

            var data = bytes.AsSpan(at + 8, length);
            at += 12 + length;
            switch (type)
            {
                case "IHDR" when length == 13:
                    header = data.ToArray();
                    break;
                case "acTL" when length == 8:
                    plays = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data[4..]), int.MaxValue);
                    break;
                case "fcTL" when length == 26:
                    if (frames.Count >= Animation.MaxFrames)
                    {
                        at = bytes.Length;
                        break;
                    }

                    current = new Frame
                    {
                        Width = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]),
                        Height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[8..]),
                        X = (int)BinaryPrimitives.ReadUInt32BigEndian(data[12..]),
                        Y = (int)BinaryPrimitives.ReadUInt32BigEndian(data[16..]),
                        Delay = AnimatedImage.DelayOf(BinaryPrimitives.ReadUInt16BigEndian(data[20..]), BinaryPrimitives.ReadUInt16BigEndian(data[22..]) is var den and > 0 ? den : 100),
                        Dispose = data[24],
                        Blend = data[25],
                    };
                    frames.Add(current);
                    break;
                case "IDAT":
                    current?.Data.Write(data);
                    break;
                case "fdAT" when length >= 4:
                    current?.Data.Write(data[4..]);
                    break;
                case "IEND":
                    at = bytes.Length;
                    break;
                default:
                    if (_shared.Contains(type) && frames.Count == 0)
                    {
                        shared.Add((type, data.ToArray()));
                    }

                    break;
            }
        }

        frames.RemoveAll(f => f.Data.Length == 0);
        if (header is null || frames.Count < 2)
        {
            return null;
        }

        var width = (int)BinaryPrimitives.ReadUInt32BigEndian(header);
        var height = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
        if (width <= 0 || height <= 0 || (long)width * height > ImageDecoder.MaxPixels)
        {
            throw new ImageDecodeException(Salandual.Core.Resources.Strings.Image_TooBig);
        }

        var (w, h) = (long)width * height * 4 * frames.Count > Animation.MaxBytes ? Wic.Fit(width, height, maxWidth, maxHeight) : (width, height);
        var canvas = new byte[width * height * 4];
        var pictures = new List<DecodedImage>(frames.Count);
        foreach (var frame in frames)
        {
            token.ThrowIfCancellationRequested();
            if (frame.Width <= 0 || frame.Height <= 0 || frame.X < 0 || frame.Y < 0 || frame.X + frame.Width > width || frame.Y + frame.Height > height)
            {
                throw new InvalidDataException("A frame lies outside the picture.");
            }

            var picture = Wic.DecodeBytes(Png(header, frame, shared), 0, 0, 1);
            var before = frame.Dispose == 2 ? (byte[])canvas.Clone() : null;
            Lay(canvas, width, picture, frame.X, frame.Y, over: frame.Blend == 1);
            var whole = new DecodedImage(width, height, (byte[])canvas.Clone(), width, height, "WIC, APNG");
            pictures.Add(w != width || h != height ? Wic.Scale(whole, w, h) : whole);
            if (frame.Dispose == 1)
            {
                Clear(canvas, width, frame);
            }
            else if (before is not null)
            {
                canvas = before;
            }
        }

        return new AnimatedImage(pictures, [.. frames.Select(f => f.Delay)], plays);
    }

    private static byte[] Png(byte[] header, Frame frame, List<(string Type, byte[] Data)> shared)
    {
        using var png = new MemoryStream();
        png.Write(_signature);
        var ihdr = (byte[])header.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)frame.Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)frame.Height);
        Chunk(png, "IHDR", ihdr);
        foreach (var (type, data) in shared)
        {
            Chunk(png, type, data);
        }

        Chunk(png, "IDAT", frame.Data.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        png.Write(number);
        var typed = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, typed);
        data.CopyTo(typed, 4);
        png.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typed));
        png.Write(number);
    }

    private static uint Crc(byte[] bytes)
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

    private static void Lay(byte[] canvas, int width, DecodedImage picture, int x, int y, bool over)
    {
        for (var row = 0; row < picture.Height; row++)
        {
            var from = row * picture.Width * 4;
            var to = (((y + row) * width) + x) * 4;
            if (!over)
            {
                Buffer.BlockCopy(picture.Pixels, from, canvas, to, picture.Width * 4);
                continue;
            }

            for (var i = 0; i < picture.Width * 4; i += 4)
            {
                var keep = 255 - picture.Pixels[from + i + 3];
                for (var c = 0; c < 4; c++)
                {
                    canvas[to + i + c] = (byte)(picture.Pixels[from + i + c] + ((canvas[to + i + c] * keep) + 127) / 255);
                }
            }
        }
    }

    private static void Clear(byte[] canvas, int width, Frame frame)
    {
        for (var row = 0; row < frame.Height; row++)
        {
            Array.Clear(canvas, (((frame.Y + row) * width) + frame.X) * 4, frame.Width * 4);
        }
    }
}
