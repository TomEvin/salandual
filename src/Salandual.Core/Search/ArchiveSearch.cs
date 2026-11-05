// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Search/ArchiveSearch.cs

using System.IO.Compression;
using Salandual.Core.Resources;

namespace Salandual.Core.Search;

public sealed record ArchiveSearchEntry(int Index, string Path, bool IsDirectory, long Size, long LastWriteUtcTicks, bool Encrypted = false);

public interface IArchiveSearch
{
    bool IsArchive(string name);

    IArchiveSearchReader Open(string path, CancellationToken token);
}

public interface IArchiveSearchReader : IDisposable
{
    IReadOnlyList<ArchiveSearchEntry> Entries { get; }

    void Read(IReadOnlyList<int> indices, long limit, Action<int, Stream> found, Action<int, string> failed, CancellationToken token);
}

public sealed class ZipSearch(Func<string, Stream> open) : IArchiveSearch
{
    public bool IsArchive(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public IArchiveSearchReader Open(string path, CancellationToken token) => new Reader(open(path));

    private sealed class Reader : IArchiveSearchReader
    {
        private readonly Stream _stream;
        private readonly ZipArchive _zip;
        private readonly List<ZipArchiveEntry> _byIndex;

        public Reader(Stream stream)
        {
            _stream = stream;
            try
            {
                _zip = new ZipArchive(stream, ZipArchiveMode.Read);
            }
            catch
            {
                stream.Dispose();
                throw;
            }

            _byIndex = [.. _zip.Entries];
            Entries = [.. _byIndex.Select((e, i) => new ArchiveSearchEntry(i, e.FullName.Replace('/', '\\').TrimEnd('\\'), e.FullName.EndsWith('/'), e.Length, e.LastWriteTime.UtcDateTime.Ticks))];
        }

        public IReadOnlyList<ArchiveSearchEntry> Entries { get; }

        public void Read(IReadOnlyList<int> indices, long limit, Action<int, Stream> found, Action<int, string> failed, CancellationToken token)
        {
            foreach (var index in indices)
            {
                token.ThrowIfCancellationRequested();
                using var memory = new LimitedMemoryStream(limit);
                using (var source = _byIndex[index].Open())
                {
                    source.CopyTo(memory);
                }

                if (memory.Overflow)
                {
                    failed(index, Strings.Find_LogTooBigInArchive);
                    continue;
                }

                memory.Position = 0;
                found(index, memory);
            }
        }

        public void Dispose()
        {
            _zip.Dispose();
            _stream.Dispose();
        }
    }
}

public sealed class LimitedMemoryStream(long limit) : MemoryStream
{
    public bool Overflow { get; private set; }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (Overflow || Length + count > limit)
        {
            Overflow = true;
            return;
        }

        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

    public override void WriteByte(byte value) => Write([value], 0, 1);
}
