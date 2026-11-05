// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/QuickOpen/DriveWalk.cs

using System.Diagnostics;
using System.IO.Enumeration;

namespace Salandual.Core.QuickOpen;

public sealed class DriveWalk
{
    private const int Chunk = 4096;

    private const FileAttributes Online = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;

    private readonly WalkFilter _filter;
    private readonly HashSet<string> _covered;
    private readonly List<string> _roots;
    private readonly Stack<string> _pending = new();
    private readonly List<WalkedEntry[]> _chunks = [];
    private readonly Lock _gate = new();
    private WalkedEntry[]? _current;
    private int _rootAt;
    private volatile int _count;

    public DriveWalk(IEnumerable<string> roots, WalkFilter filter, IEnumerable<string>? covered = null)
    {
        _filter = filter;
        _roots = [.. roots.Select(r => r.Length > 3 ? r.TrimEnd('\\') : r).Distinct(StringComparer.OrdinalIgnoreCase)];
        _covered = new HashSet<string>((covered ?? []).Select(c => c.Length > 3 ? c.TrimEnd('\\') : c), StringComparer.OrdinalIgnoreCase);
        StartedUtc = DateTime.UtcNow;
    }

    public DateTime StartedUtc { get; }

    public IReadOnlyList<string> Roots => _roots;

    public int Count => _count;

    public bool Finished { get; private set; }

    public IReadOnlyList<string> Unfinished => Finished ? [] : [.. _roots.Skip(Math.Max(0, _rootAt - 1))];

    public IEnumerable<WalkedEntry> Entries
    {
        get
        {
            var count = _count;
            WalkedEntry[][] chunks;
            lock (_gate)
            {
                chunks = [.. _chunks];
            }

            for (var i = 0; i < count; i++)
            {
                yield return chunks[i / Chunk][i % Chunk];
            }
        }
    }

    public bool Run(int max, TimeSpan time, CancellationToken cancel, Action<int>? found = null)
    {
        var clock = Stopwatch.StartNew();
        var limit = _count + max;
        var told = clock.Elapsed;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = _filter.Attributes,
        };

        while (!Finished)
        {
            if (_pending.Count == 0)
            {
                if (_rootAt >= _roots.Count)
                {
                    Finished = true;
                    break;
                }

                var root = _roots[_rootAt++];
                if (!_covered.Contains(root))
                {
                    _pending.Push(root);
                }

                continue;
            }

            if (cancel.IsCancellationRequested || _count >= limit || clock.Elapsed > time)
            {
                return true;
            }

            var folder = _pending.Pop();
            try
            {
                var entries = new FileSystemEnumerable<(string Name, FileAttributes Attributes, bool IsFolder)>(folder,
                    (ref FileSystemEntry e) => (e.FileName.ToString(), e.Attributes, e.IsDirectory), options);
                foreach (var (name, attributes, isFolder) in entries)
                {
                    var full = folder.EndsWith('\\') ? folder + name : folder + "\\" + name;
                    var online = (attributes & Online) != 0;
                    if (isFolder)
                    {
                        if (_filter.SkipsFolder(full, name))
                        {
                            continue;
                        }

                        if (!online && !_covered.Contains(full))
                        {
                            _pending.Push(full);
                        }
                    }

                    Add(new WalkedEntry(name, folder, isFolder, online));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
            }

            if (found is not null && clock.Elapsed - told > TimeSpan.FromMilliseconds(750))
            {
                told = clock.Elapsed;
                found(_count);
            }
        }

        return false;
    }

    private void Add(WalkedEntry entry)
    {
        var at = _count;
        if (at % Chunk == 0)
        {
            _current = new WalkedEntry[Chunk];
            lock (_gate)
            {
                _chunks.Add(_current);
            }
        }

        _current![at % Chunk] = entry;
        _count = at + 1;
    }
}
