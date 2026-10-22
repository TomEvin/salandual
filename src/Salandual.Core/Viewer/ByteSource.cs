// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/ByteSource.cs

using Microsoft.Win32.SafeHandles;

namespace Salandual.Core.Viewer;

public interface IByteSource : IDisposable
{
    long Length { get; }

    int Read(long offset, Span<byte> buffer);
}

public sealed class FileByteSource : IByteSource
{
    private readonly SafeFileHandle _handle;

    public FileByteSource(string path)
    {
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        Length = RandomAccess.GetLength(_handle);
    }

    public long Length { get; }

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset >= Length || buffer.IsEmpty)
        {
            return 0;
        }

        var wanted = (int)Math.Min(buffer.Length, Length - offset);
        var total = 0;
        while (total < wanted)
        {
            var n = RandomAccess.Read(_handle, buffer[total..wanted], offset + total);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }

    public void Dispose() => _handle.Dispose();
}

public sealed class MemoryByteSource(byte[] bytes) : IByteSource
{
    public long Length => bytes.Length;

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset >= bytes.Length)
        {
            return 0;
        }

        var n = (int)Math.Min(buffer.Length, bytes.Length - offset);
        bytes.AsSpan((int)offset, n).CopyTo(buffer);
        return n;
    }

    public void Dispose()
    {
    }
}

public sealed class CachedBytes(IByteSource source)
{
    public const int PageSize = 64 * 1024;
    private const int MaxPages = 32;

    private readonly LinkedList<(long Page, byte[] Bytes, int Length)> _pages = new();
    private readonly Lock _lock = new();

    public IByteSource Source => source;

    public long Length => source.Length;

    public int Read(long offset, Span<byte> buffer)
    {
        var total = 0;
        lock (_lock)
        {
            while (total < buffer.Length && offset + total < Length)
            {
                var at = offset + total;
                var (bytes, length) = Page(at / PageSize);
                var inPage = (int)(at % PageSize);
                if (inPage >= length)
                {
                    break;
                }

                var n = Math.Min(length - inPage, buffer.Length - total);
                bytes.AsSpan(inPage, n).CopyTo(buffer[total..]);
                total += n;
            }
        }

        return total;
    }

    public int ByteAt(long offset)
    {
        Span<byte> one = stackalloc byte[1];
        return Read(offset, one) == 1 ? one[0] : -1;
    }

    private (byte[] Bytes, int Length) Page(long page)
    {
        for (var node = _pages.First; node is not null; node = node.Next)
        {
            if (node.Value.Page == page)
            {
                _pages.Remove(node);
                _pages.AddFirst(node);
                return (node.Value.Bytes, node.Value.Length);
            }
        }

        var bytes = _pages.Count >= MaxPages ? _pages.Last!.Value.Bytes : new byte[PageSize];
        if (_pages.Count >= MaxPages)
        {
            _pages.RemoveLast();
        }

        var length = source.Read(page * PageSize, bytes);
        _pages.AddFirst((page, bytes, length));
        return (bytes, length);
    }
}
