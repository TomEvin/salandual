// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Search/FileSearch.cs

using System.Diagnostics;
using System.IO.Compression;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Vfs;

namespace Salandual.Core.Search;

public sealed record SearchSummary(int Folders, int Files, TimeSpan Elapsed, bool Cancelled);

public sealed class SearchCallbacks
{
    public Action<SearchHit>? Found { get; init; }

    public Action<SearchProgress>? Progress { get; init; }

    public Action<SearchLogEntry>? Log { get; init; }
}

public sealed class FileSearch
{
    public const long ZipEntryLimit = 64L * 1024 * 1024;

    private const FileAttributes OnlineOnly = (FileAttributes)0x00400000 | (FileAttributes)0x00040000 | FileAttributes.Offline;

    private readonly IFileSystemProvider _files;
    private readonly Func<string, Stream> _open;

    public FileSearch(IFileSystemProvider files, Func<string, Stream> open)
    {
        _files = files;
        _open = open;
    }

    public async Task<SearchSummary> RunAsync(SearchCriteria criteria, SearchCallbacks callbacks, CancellationToken token = default)
    {
        var clock = Stopwatch.StartNew();
        var run = new Run(this, criteria, callbacks, token);
        try
        {
            foreach (var root in criteria.LookIn.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await run.WalkAsync(root).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return new SearchSummary(run.Folders, run.FileCount, clock.Elapsed, Cancelled: true);
        }

        run.Report(force: true, "");
        return new SearchSummary(run.Folders, run.FileCount, clock.Elapsed, Cancelled: false);
    }

    public Stream? OpenContent(SearchHit hit)
    {
        if (hit.Archive is null)
        {
            return (hit.Attributes & OnlineOnly) != 0 ? null : _open(hit.FullPath);
        }

        using var stream = _open(hit.Archive);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var inner = hit.FullPath[(hit.Archive.Length + 1)..].Replace('\\', '/');
        return zip.GetEntry(inner) is { } entry && entry.Length <= ZipEntryLimit ? Run.Unpack(entry) : null;
    }

    public IReadOnlyList<SearchHit> Filter(SearchCriteria criteria, IEnumerable<SearchHit> items, SearchCallbacks callbacks, CancellationToken token = default)
    {
        var run = new Run(this, criteria, callbacks, token);
        var result = new List<SearchHit>();
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            if (run.Test(item) is { } hit)
            {
                result.Add(hit);
            }
        }

        return result;
    }

    private sealed class Run
    {
        private readonly FileSearch _search;
        private readonly SearchCriteria _criteria;
        private readonly SearchCallbacks _callbacks;
        private readonly CancellationToken _token;
        private readonly FileMask _mask;
        private readonly SkipRules _skip;
        private readonly ContentMatcher? _content;
        private readonly Stopwatch _sinceReport = Stopwatch.StartNew();

        public Run(FileSearch search, SearchCriteria criteria, SearchCallbacks callbacks, CancellationToken token)
        {
            _search = search;
            _criteria = criteria;
            _callbacks = callbacks;
            _token = token;
            _mask = NameRule.ToMask(criteria.Named);
            _skip = new SkipRules(criteria.SkippedFolders);
            _content = criteria.HasTexts ? ContentMatcher.Create(criteria, out _) : null;
        }

        public int Folders { get; private set; }

        public int FileCount { get; private set; }

        public void Report(bool force, string folder)
        {
            if (force || _sinceReport.ElapsedMilliseconds >= 100)
            {
                _sinceReport.Restart();
                _callbacks.Progress?.Invoke(new SearchProgress(Folders, FileCount, folder));
            }
        }

        private void Log(SearchLogKind kind, string path, string message) => _callbacks.Log?.Invoke(new SearchLogEntry(kind, path, message));

        public async Task WalkAsync(string root)
        {
            var stack = new Stack<(string Path, int Level)>();
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                _token.ThrowIfCancellationRequested();
                var (folder, level) = stack.Pop();
                DirectoryListing listing;
                try
                {
                    listing = await _search._files.ListAsync(folder, _token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    Log(SearchLogKind.Error, folder, ex.Message);
                    continue;
                }

                Folders++;
                Report(force: false, folder);
                var root0 = _search._files.GetRoot(folder);
                var below = new List<string>();
                foreach (var entry in listing.Entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                {
                    _token.ThrowIfCancellationRequested();
                    var path = _search._files.Combine(folder, entry.Name);
                    if (entry.IsDirectory)
                    {
                        if (_skip.IsSkipped(path, entry.Name, folder, root0))
                        {
                            Log(SearchLogKind.Skipped, path, Strings.Find_LogSkippedFolder);
                            continue;
                        }

                        if (!_criteria.HasTexts && _mask.IsMatch(entry.Name) && Passes(entry.Size, entry.LastWriteUtcTicks, entry.Attributes, isDirectory: true))
                        {
                            _callbacks.Found?.Invoke(new SearchHit(folder, entry.Name, 0, entry.LastWriteUtcTicks, entry.Attributes));
                        }

                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            Log(SearchLogKind.Skipped, path, Strings.Find_LogLink);
                        }
                        else if (_criteria.Depth is null || level + 1 <= _criteria.Depth)
                        {
                            below.Add(path);
                        }

                        continue;
                    }

                    FileCount++;
                    if (_mask.IsMatch(entry.Name))
                    {
                        if (Test(new SearchHit(folder, entry.Name, entry.Size, entry.LastWriteUtcTicks, entry.Attributes)) is { } hit)
                        {
                            _callbacks.Found?.Invoke(hit);
                        }
                    }

                    if (_criteria.InsideZip && entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && (entry.Attributes & OnlineOnly) == 0)
                    {
                        SearchZip(path);
                    }
                }

                for (var i = below.Count - 1; i >= 0; i--)
                {
                    stack.Push((below[i], level + 1));
                }
            }
        }

        private bool Passes(long size, long ticks, FileAttributes attributes, bool isDirectory)
        {
            if (_criteria.MinSize is { } min && (isDirectory || size < min))
            {
                return false;
            }

            if (_criteria.MaxSize is { } max && (isDirectory || size > max))
            {
                return false;
            }

            if (_criteria.ModifiedFrom is { } from && ticks < from.Ticks)
            {
                return false;
            }

            if (_criteria.ModifiedTo is { } to && ticks >= to.Ticks)
            {
                return false;
            }

            foreach (var (attribute, rule) in _criteria.Attributes)
            {
                var has = (attributes & attribute) != 0;
                if ((rule == AttributeRule.Must && !has) || (rule == AttributeRule.MustNot && has))
                {
                    return false;
                }
            }

            return true;
        }

        public SearchHit? Test(SearchHit item)
        {
            if (!Passes(item.Size, item.LastWriteUtcTicks, item.Attributes, item.IsDirectory))
            {
                return null;
            }

            if (_content is null)
            {
                return _criteria.HasTexts ? null : item;
            }

            if (item.IsDirectory)
            {
                return null;
            }

            if (item.Archive is null && (item.Attributes & OnlineOnly) != 0)
            {
                Log(SearchLogKind.NotRead, item.FullPath, Strings.Find_LogOnlineOnly);
                return null;
            }

            try
            {
                using var stream = item.Archive is null ? _search._open(item.FullPath) : OpenInZip(item);
                if (stream is null)
                {
                    return null;
                }

                var result = _content.Match(stream, _token);
                return result.IsMatch ? item with { Line = result.Line } : null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.SecurityException)
            {
                Log(SearchLogKind.Error, item.FullPath, ex.Message);
                return null;
            }
        }

        private void SearchZip(string zipPath)
        {
            try
            {
                using var stream = _search._open(zipPath);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (var entry in zip.Entries)
                {
                    _token.ThrowIfCancellationRequested();
                    var inner = entry.FullName.Replace('/', '\\').TrimEnd('\\');
                    if (inner.Length == 0 || inner.Split('\\').Contains(".."))
                    {
                        continue;
                    }

                    var isDirectory = entry.FullName.EndsWith('/');
                    var slash = inner.LastIndexOf('\\');
                    var name = slash < 0 ? inner : inner[(slash + 1)..];
                    var folder = slash < 0 ? zipPath : zipPath + "\\" + inner[..slash];
                    if (!_mask.IsMatch(name))
                    {
                        continue;
                    }

                    var attributes = isDirectory ? FileAttributes.Directory : FileAttributes.Archive;
                    var hit = new SearchHit(folder, name, isDirectory ? 0 : entry.Length, entry.LastWriteTime.UtcDateTime.Ticks, attributes, zipPath);
                    if (isDirectory)
                    {
                        if (!_criteria.HasTexts && Passes(0, hit.LastWriteUtcTicks, attributes, isDirectory: true))
                        {
                            _callbacks.Found?.Invoke(hit);
                        }

                        continue;
                    }

                    if (_content is not null && entry.Length > ZipEntryLimit)
                    {
                        Log(SearchLogKind.NotRead, hit.FullPath, Strings.Find_LogTooBigInZip);
                        continue;
                    }

                    if (TestEntry(hit, entry) is { } found)
                    {
                        _callbacks.Found?.Invoke(found);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                Log(SearchLogKind.Error, zipPath, ex.Message);
            }
        }

        private SearchHit? TestEntry(SearchHit hit, ZipArchiveEntry entry)
        {
            if (!Passes(hit.Size, hit.LastWriteUtcTicks, hit.Attributes, isDirectory: false))
            {
                return null;
            }

            if (_content is null)
            {
                return hit;
            }

            using var memory = Unpack(entry);
            var result = _content.Match(memory, _token);
            return result.IsMatch ? hit with { Line = result.Line } : null;
        }

        private MemoryStream? OpenInZip(SearchHit item)
        {
            using var stream = _search._open(item.Archive!);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var inner = item.FullPath[(item.Archive!.Length + 1)..].Replace('\\', '/');
            return zip.GetEntry(inner) is { } entry && entry.Length <= ZipEntryLimit ? Unpack(entry) : null;
        }

        internal static MemoryStream Unpack(ZipArchiveEntry entry)
        {
            using var source = entry.Open();
            var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                memory.Write(buffer, 0, read);
                if (memory.Length > ZipEntryLimit)
                {
                    throw new InvalidDataException(Strings.Find_LogTooBigInZip);
                }
            }

            memory.Position = 0;
            return memory;
        }
    }
}
