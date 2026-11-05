// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/WriteStreams.cs

namespace Salandual.Archives;

internal sealed class PackFileStream(string path, long length, Action<int>? read = null) : Stream
{
    private FileStream? _file;
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(Span<byte> buffer)
    {
        if (_file is null)
        {
            if (_position >= length && length > 0)
            {
                return 0;
            }

            _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            _file.Position = _position;
        }

        var count = _file.Read(buffer);
        _position += count;
        read?.Invoke(count);
        if (count == 0)
        {
            Close();
        }

        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };
        if (_file is not null)
        {
            _file.Position = _position;
        }

        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Close()
    {
        _file?.Dispose();
        _file = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }

        base.Dispose(disposing);
    }
}

internal sealed class VolumeWriter(Func<int, string> part, long volumeSize) : Stream
{
    private readonly List<FileStream> _volumes = [];
    private long _position, _length;

    public IReadOnlyList<string> Files => [.. _volumes.Select(v => v.Name)];

    public override bool CanRead => false;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        while (buffer.Length > 0)
        {
            var index = (int)(_position / volumeSize);
            var at = _position % volumeSize;
            var volume = Volume(index);
            var count = (int)Math.Min(buffer.Length, volumeSize - at);
            volume.Position = at;
            volume.Write(buffer[..count]);
            buffer = buffer[count..];
            _position += count;
            _length = Math.Max(_length, _position);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => _length + offset,
        };
        return _position;
    }

    public override void SetLength(long value)
    {
        _length = value;
        var last = value == 0 ? 0 : (int)((value - 1) / volumeSize);
        for (var i = 0; i < _volumes.Count; i++)
        {
            _volumes[i].SetLength(i < last ? volumeSize : i == last ? value - (long)last * volumeSize : 0);
        }
    }

    public override void Flush()
    {
        foreach (var volume in _volumes)
        {
            volume.Flush();
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private FileStream Volume(int index)
    {
        while (_volumes.Count <= index)
        {
            _volumes.Add(new FileStream(part(_volumes.Count + 1), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16));
        }

        return _volumes[index];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var volume in _volumes)
            {
                volume.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}

internal sealed class Pipe
{
    public sealed class PipeStoppedException : IOException;

    private const int MaxBlocks = 16;

    private readonly Queue<byte[]> _blocks = new();
    private readonly object _gate = new();
    private byte[]? _current;
    private int _offset;
    private bool _ended, _failed;

    public Stream Writer => new End(this, write: true);

    public Stream Reader => new End(this, write: false);

    public void Complete()
    {
        lock (_gate)
        {
            _ended = true;
            Monitor.PulseAll(_gate);
        }
    }

    public void Fail()
    {
        lock (_gate)
        {
            _failed = true;
            Monitor.PulseAll(_gate);
        }
    }

    private void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            while (_blocks.Count >= MaxBlocks && !_failed)
            {
                Monitor.Wait(_gate);
            }

            if (_failed)
            {
                throw new PipeStoppedException();
            }

            _blocks.Enqueue(data.ToArray());
            Monitor.PulseAll(_gate);
        }
    }

    private int Read(Span<byte> buffer)
    {
        if (_current is null || _offset == _current.Length)
        {
            lock (_gate)
            {
                while (_blocks.Count == 0 && !_ended && !_failed)
                {
                    Monitor.Wait(_gate);
                }

                if (_failed)
                {
                    throw new PipeStoppedException();
                }

                if (_blocks.Count == 0)
                {
                    return 0;
                }

                _current = _blocks.Dequeue();
                _offset = 0;
                Monitor.PulseAll(_gate);
            }
        }

        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsSpan(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    private sealed class End(Pipe pipe, bool write) : Stream
    {
        public override bool CanRead => !write;

        public override bool CanSeek => false;

        public override bool CanWrite => write;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(Span<byte> buffer) => pipe.Read(buffer);

        public override int Read(byte[] buffer, int offset, int count) => pipe.Read(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => pipe.Write(buffer);

        public override void Write(byte[] buffer, int offset, int count) => pipe.Write(buffer.AsSpan(offset, count));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
