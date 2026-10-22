// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Search/Duplicates.cs

using System.Security.Cryptography;
using Salandual.Core.Settings;

namespace Salandual.Core.Search;

public sealed record DuplicateGroup(IReadOnlyList<SearchHit> Files, string? Name, long? Size, bool SameContent)
{
    public long Freeable => Size is { } size ? size * (Files.Count - 1) : 0;
}

public readonly record struct DuplicateProgress(int Read, int ToRead);

public static class Duplicates
{
    private const int HeadBytes = 64 * 1024;

    public static async Task<(IReadOnlyList<DuplicateGroup> Groups, SearchSummary Summary)> FindAsync(
        FileSearch search, SearchCriteria criteria, DuplicateKeys keys, SearchCallbacks callbacks, Action<DuplicateProgress>? comparing, CancellationToken token = default)
    {
        var files = new List<SearchHit>();
        var walk = new SearchCallbacks
        {
            Found = hit =>
            {
                if (!hit.IsDirectory)
                {
                    lock (files)
                    {
                        files.Add(hit);
                    }
                }
            },
            Progress = callbacks.Progress,
            Log = callbacks.Log,
        };
        var summary = await search.RunAsync(criteria with { Texts = [] }, walk, token).ConfigureAwait(false);
        var groups = Group(files, keys & ~DuplicateKeys.Content);
        if (keys.HasFlag(DuplicateKeys.Content))
        {
            if (summary.Cancelled)
            {
                return ([], summary);
            }

            try
            {
                groups = await Task.Run(() => ByContent(search, groups, callbacks, comparing, token), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ([], summary with { Cancelled = true });
            }
        }

        return (Order(groups), summary);
    }

    public static List<DuplicateGroup> Group(IEnumerable<SearchHit> files, DuplicateKeys keys)
    {
        var byName = keys.HasFlag(DuplicateKeys.Name);
        var bySize = keys.HasFlag(DuplicateKeys.Size) || keys.HasFlag(DuplicateKeys.Content);
        if (!byName && !bySize)
        {
            return [];
        }

        return [.. files.Where(f => !f.IsDirectory)
            .GroupBy(f => (Name: byName ? f.Name.ToUpperInvariant() : "", Size: bySize ? f.Size : -1))
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup([.. g], byName ? g.First().Name : null, bySize ? g.Key.Size : null, false))];
    }

    public static List<DuplicateGroup> Order(IEnumerable<DuplicateGroup> groups)
        => [.. groups
            .Select(g => g with { Files = [.. g.Files.OrderByDescending(f => f.LastWriteUtcTicks).ThenBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)] })
            .OrderByDescending(g => g.Freeable)
            .ThenByDescending(g => g.Files.Count)
            .ThenBy(g => g.Files[0].Name, StringComparer.CurrentCultureIgnoreCase)];

    private static List<DuplicateGroup> ByContent(FileSearch search, List<DuplicateGroup> groups, SearchCallbacks callbacks, Action<DuplicateProgress>? comparing, CancellationToken token)
    {
        var toRead = groups.Sum(g => g.Files.Count);
        var read = 0;
        var result = new List<DuplicateGroup>();
        foreach (var group in groups)
        {
            if (group.Size == 0)
            {
                result.Add(group with { SameContent = true });
                read += group.Files.Count;
                comparing?.Invoke(new DuplicateProgress(read, toRead));
                continue;
            }

            var heads = Split(group.Files, f => Hash(search, f, HeadBytes, callbacks, token));
            foreach (var same in heads)
            {
                var whole = same.Count > 1 && group.Size > HeadBytes ? Split(same, f => Hash(search, f, long.MaxValue, callbacks, token)) : [same];
                foreach (var files in whole.Where(w => w.Count > 1))
                {
                    result.Add(group with { Files = files, SameContent = true });
                }
            }

            read += group.Files.Count;
            comparing?.Invoke(new DuplicateProgress(read, toRead));
        }

        return result;
    }

    private static List<List<SearchHit>> Split(IReadOnlyList<SearchHit> files, Func<SearchHit, string?> key)
    {
        var parts = new Dictionary<string, List<SearchHit>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (key(file) is { } k)
            {
                if (!parts.TryGetValue(k, out var list))
                {
                    parts[k] = list = [];
                }

                list.Add(file);
            }
        }

        return [.. parts.Values];
    }

    private static string? Hash(FileSearch search, SearchHit file, long limit, SearchCallbacks callbacks, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var stream = search.OpenContent(file);
            if (stream is null)
            {
                callbacks.Log?.Invoke(new SearchLogEntry(SearchLogKind.NotRead, file.FullPath, file.Archive is null ? Resources.Strings.Find_LogOnlineOnly : Resources.Strings.Find_LogTooBigInZip));
                return null;
            }

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int got;
            while (total < limit && (got = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, limit - total))) > 0)
            {
                token.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, got);
                total += got;
            }

            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.SecurityException)
        {
            callbacks.Log?.Invoke(new SearchLogEntry(SearchLogKind.Error, file.FullPath, ex.Message));
            return null;
        }
    }
}
