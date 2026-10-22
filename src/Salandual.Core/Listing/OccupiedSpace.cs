// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Listing/OccupiedSpace.cs

using System.Diagnostics;

namespace Salandual.Core.Listing;

public sealed record SpaceTotals(long Files, long Folders, long Bytes, long OnDisk, long CompressedFiles, long CompressionSaves, long OnlineFiles, long OnlineBytes, IReadOnlyList<long> Estimates)
{
    public static SpaceTotals None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, new long[OccupiedSpace.Clusters.Count]);

    public long EstimateFor(long cluster) => OccupiedSpace.IndexOf(cluster) is var at and >= 0 ? Estimates[at] : 0;
}

public sealed record SpacePart(string Name, bool IsDirectory, long Bytes);

public sealed record SpaceResult(SpaceTotals Totals, IReadOnlyList<SpacePart> Parts, string? Inside)
{
    public (IReadOnlyList<SpacePart> Biggest, long RestBytes, bool RestIsFiles, int RestCount) Slices(int count = 5)
    {
        var ordered = Parts.Where(p => p.Bytes > 0).OrderByDescending(p => p.Bytes).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var rest = ordered.Skip(count).ToList();
        return (ordered.Take(count).ToList(), rest.Sum(p => p.Bytes), rest.All(p => !p.IsDirectory), rest.Count);
    }
}

public static class OccupiedSpace
{
    public static IReadOnlyList<long> Clusters { get; } = [512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072];

    public const FileAttributes OnlineOnly = (FileAttributes)0x00400000 | (FileAttributes)0x00040000 | FileAttributes.Offline;

    public static int IndexOf(long cluster)
    {
        for (var i = 0; i < Clusters.Count; i++)
        {
            if (Clusters[i] == cluster)
            {
                return i;
            }
        }

        return -1;
    }

    public static long Nearest(long cluster) => cluster <= 0 ? 4096 : Clusters.MinBy(c => Math.Abs(Math.Log2(c) - Math.Log2(cluster)));

    public static long RoundUp(long size, long cluster) => cluster <= 0 ? size : (size + cluster - 1) / cluster * cluster;

    public static SpaceResult Count(IReadOnlyList<FileListItem> selected, long cluster, Func<string, long>? allocated, Action<SpaceTotals>? progress, CancellationToken cancel)
    {
        var counter = new Counter(cluster, allocated, progress);
        string? inside = null;
        var top = selected;
        if (selected.Count == 1 && selected[0].IsDirectory && (selected[0].Attributes & FileAttributes.ReparsePoint) == 0)
        {
            inside = selected[0].Name;
            counter.Folders++;
            top = Entries(selected[0].FullName);
        }

        var parts = new List<SpacePart>();
        foreach (var item in top)
        {
            cancel.ThrowIfCancellationRequested();
            var before = counter.Bytes;
            counter.Add(item, cancel);
            parts.Add(new SpacePart(item.Name, item.IsDirectory, counter.Bytes - before));
        }

        return new SpaceResult(counter.Totals(), parts, inside);
    }

    private static List<FileListItem> Entries(string folder)
    {
        try
        {
            return [.. new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false })
                .Select(e => new FileListItem(e.FullName, e is FileInfo f ? f.Length : 0, e.LastWriteTimeUtc, e.Attributes))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private sealed class Counter(long cluster, Func<string, long>? allocated, Action<SpaceTotals>? progress)
    {
        private readonly long[] _estimates = new long[Clusters.Count];
        private readonly Stopwatch _last = Stopwatch.StartNew();
        private long _files, _onDisk, _compressedFiles, _saves, _onlineFiles, _onlineBytes;

        public long Folders { get; set; }

        public long Bytes { get; private set; }

        public SpaceTotals Totals() => new(_files, Folders, Bytes, _onDisk, _compressedFiles, _saves, _onlineFiles, _onlineBytes, [.. _estimates]);

        public void Add(FileListItem item, CancellationToken cancel)
        {
            if (!item.IsDirectory)
            {
                File(item.FullName, item.Size, item.Attributes);
                return;
            }

            Folders++;
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            var pending = new Stack<string>();
            pending.Push(item.FullName);
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
            while (pending.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos("*", options);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    continue;
                }

                try
                {
                    foreach (var entry in entries)
                    {
                        if (entry is FileInfo f)
                        {
                            File(f.FullName, f.Length, f.Attributes);
                        }
                        else
                        {
                            Folders++;
                            if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                pending.Push(entry.FullName);
                            }
                        }

                        if (_last.ElapsedMilliseconds >= 150)
                        {
                            progress?.Invoke(Totals());
                            _last.Restart();
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                }
            }
        }

        private void File(string path, long size, FileAttributes attributes)
        {
            _files++;
            Bytes += size;
            for (var i = 0; i < _estimates.Length; i++)
            {
                _estimates[i] += RoundUp(size, Clusters[i]);
            }

            if ((attributes & OnlineOnly) != 0)
            {
                _onlineFiles++;
                _onlineBytes += size;
                return;
            }

            var taken = (attributes & (FileAttributes.Compressed | FileAttributes.SparseFile)) != 0 && allocated?.Invoke(path) is long real && real >= 0 ? real : size;
            _onDisk += RoundUp(taken, cluster);
            if ((attributes & FileAttributes.Compressed) != 0)
            {
                _compressedFiles++;
                _saves += Math.Max(0, size - taken);
            }
        }
    }
}
