// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/VolumeStream.cs

using System.Globalization;

namespace Salandual.Archives;

internal sealed class VolumeStream : Stream
{
    private readonly FileStream[] _parts;
    private readonly long[] _starts;
    private long _position;

    private VolumeStream(FileStream[] parts)
    {
        _parts = parts;
        _starts = new long[parts.Length + 1];
        for (var i = 0; i < parts.Length; i++)
        {
            _starts[i + 1] = _starts[i] + parts[i].Length;
        }
    }

    public static bool IsFirstVolume(string path) => Path.GetExtension(path) == ".001" && Path.GetExtension(Path.GetFileNameWithoutExtension(path)).Length > 1;

    public static Stream Open(string path)
    {
        if (!IsFirstVolume(path))
        {
            return OpenPart(path);
        }

        var stem = path[..^4];
        var parts = new List<FileStream>();
        try
        {
            for (var n = 1; n < 10000; n++)
            {
                var part = stem + "." + n.ToString(n < 1000 ? "000" : "0", CultureInfo.InvariantCulture);
                if (!File.Exists(part))
                {
                    break;
                }

                parts.Add(OpenPart(part));
            }

            return new VolumeStream([.. parts]);
        }
        catch
        {
            foreach (var part in parts)
            {
                part.Dispose();
            }

            throw;
        }
    }

    public static string ArchiveName(string path) => IsFirstVolume(path) ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);

    private static FileStream OpenPart(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _starts[^1];

    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, Length);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var total = 0;
        while (buffer.Length > 0 && _position < Length)
        {
            var part = Array.BinarySearch(_starts, _position);
            part = part >= 0 ? part : ~part - 1;
            if (part >= _parts.Length)
            {
                break;
            }

            var stream = _parts[part];
            stream.Position = _position - _starts[part];
            var read = stream.Read(buffer[..(int)Math.Min(buffer.Length, _starts[part + 1] - _position)]);
            if (read == 0)
            {
                break;
            }

            total += read;
            _position += read;
            buffer = buffer[read..];
        }

        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => Length + offset,
        };
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var part in _parts)
            {
                part.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
