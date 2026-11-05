// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/ArchiveSearch.cs

using System.Text.RegularExpressions;
using Salandual.Core.Resources;
using Salandual.Core.Search;

namespace Salandual.Archives;

public sealed partial class ArchiveSearch : IArchiveSearch
{
    public bool IsArchive(string name) => ArchivePaths.IsArchiveName(name) && !LaterRarPart().IsMatch(name);

    public IArchiveSearchReader Open(string path, CancellationToken token)
    {
        try
        {
            return new Reader(ArchiveReader.Open(path, Path.GetFileName(path), askPassword: null, searchInside: false, token));
        }
        catch (ArchiveException ex) when (ex.Problem == ArchiveProblem.PasswordCancelled)
        {
            throw new IOException(Strings.Find_LogNamesHidden, ex);
        }
    }

    [GeneratedRegex(@"\.part0*([2-9]|\d{2,})\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LaterRarPart();

    private sealed class Reader(ArchiveContents contents) : IArchiveSearchReader
    {
        public IReadOnlyList<ArchiveSearchEntry> Entries { get; } =
            [.. contents.Items.Select(i => new ArchiveSearchEntry(i.Index, i.Path, i.IsDirectory, i.Size, i.ModifiedUtc?.Ticks ?? contents.FileWriteUtc.Ticks, i.Encrypted))];

        public void Read(IReadOnlyList<int> indices, long limit, Action<int, Stream> found, Action<int, string> failed, CancellationToken token)
        {
            var streams = new Dictionary<int, LimitedMemoryStream>();
            var done = new HashSet<int>();
            try
            {
                var name = Path.GetFileName(contents.ArchivePath);
                ArchiveReader.Extract(contents, indices, i => streams[i] = new LimitedMemoryStream(limit), askPassword: null, name, cancellation: token, entryDone: (i, ok) =>
                {
                    done.Add(i);
                    if (!streams.Remove(i, out var memory))
                    {
                        return;
                    }

                    using (memory)
                    {
                        if (!ok)
                        {
                            failed(i, Strings.Find_LogNotUnpacked);
                        }
                        else if (memory.Overflow)
                        {
                            failed(i, Strings.Find_LogTooBigInArchive);
                        }
                        else
                        {
                            memory.Position = 0;
                            found(i, memory);
                        }
                    }
                });
            }
            finally
            {
                foreach (var memory in streams.Values)
                {
                    memory.Dispose();
                }
            }

            foreach (var index in indices.Where(i => !done.Contains(i)))
            {
                failed(index, Strings.Find_LogNotUnpacked);
            }
        }

        public void Dispose()
        {
        }
    }
}
