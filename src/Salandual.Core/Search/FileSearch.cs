// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Search/FileSearch.cs

using System.Diagnostics;
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
    public const long ArchiveEntryLimit = 64L * 1024 * 1024;

    private const FileAttributes OnlineOnly = (FileAttributes)0x00400000 | (FileAttributes)0x00040000 | FileAttributes.Offline;

    private readonly IFileSystemProvider _files;
    private readonly Func<string, Stream> _open;
    private readonly IArchiveSearch _archives;

    public FileSearch(IFileSystemProvider files, Func<string, Stream> open, IArchiveSearch? archives = null)
    {
        _files = files;
        _open = open;
        _archives = archives ?? new ZipSearch(open);
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

        return ReadEntry(hit, CancellationToken.None, out _);
    }

    private MemoryStream? ReadEntry(SearchHit hit, CancellationToken token, out string? why)
    {
        using var reader = _archives.Open(hit.Archive!, token);
        var inner = hit.FullPath[(hit.Archive!.Length + 1)..];
        if (reader.Entries.FirstOrDefault(e => !e.IsDirectory && string.Equals(e.Path, inner, StringComparison.OrdinalIgnoreCase)) is not { } entry)
        {
            throw new FileNotFoundException(hit.FullPath);
        }

        if (entry.Encrypted || entry.Size > ArchiveEntryLimit)
        {
            why = entry.Encrypted ? Strings.Find_LogEncrypted : Strings.Find_LogTooBigInArchive;
            return null;
        }

        MemoryStream? content = null;
        string? problem = null;
        reader.Read([entry.Index], ArchiveEntryLimit, (_, stream) =>
        {
            content = new MemoryStream();
            stream.CopyTo(content);
            content.Position = 0;
        }, (_, message) => problem = message, token);
        why = problem;
        return content;
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

                    if (_criteria.InsideZip && _search._archives.IsArchive(entry.Name) && (entry.Attributes & OnlineOnly) == 0)
                    {
                        SearchArchive(path);
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
                string? why = null;
                using var stream = item.Archive is null ? _search._open(item.FullPath) : _search.ReadEntry(item, _token, out why);
                if (stream is null)
                {
                    Log(SearchLogKind.NotRead, item.FullPath, why ?? Strings.Find_LogNotUnpacked);
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

        private void SearchArchive(string archivePath)
        {
            try
            {
                using var reader = _search._archives.Open(archivePath, _token);
                var toRead = new Dictionary<int, SearchHit>();
                foreach (var entry in reader.Entries)
                {
                    _token.ThrowIfCancellationRequested();
                    var inner = entry.Path;
                    if (inner.Length == 0 || inner.Split('\\').Contains(".."))
                    {
                        continue;
                    }

                    var slash = inner.LastIndexOf('\\');
                    var name = slash < 0 ? inner : inner[(slash + 1)..];
                    if (!_mask.IsMatch(name))
                    {
                        continue;
                    }

                    var folder = slash < 0 ? archivePath : archivePath + "\\" + inner[..slash];
                    var attributes = entry.IsDirectory ? FileAttributes.Directory : FileAttributes.Archive;
                    var hit = new SearchHit(folder, name, entry.IsDirectory ? 0 : entry.Size, entry.LastWriteUtcTicks, attributes, archivePath);
                    if ((entry.IsDirectory && _criteria.HasTexts) || !Passes(hit.Size, hit.LastWriteUtcTicks, attributes, entry.IsDirectory))
                    {
                        continue;
                    }

                    if (entry.IsDirectory || _content is null)
                    {
                        _callbacks.Found?.Invoke(hit);
                    }
                    else if (entry.Encrypted)
                    {
                        Log(SearchLogKind.NotRead, hit.FullPath, Strings.Find_LogEncrypted);
                    }
                    else if (entry.Size > ArchiveEntryLimit)
                    {
                        Log(SearchLogKind.NotRead, hit.FullPath, Strings.Find_LogTooBigInArchive);
                    }
                    else
                    {
                        toRead[entry.Index] = hit;
                    }
                }

                if (toRead.Count > 0)
                {
                    reader.Read([.. toRead.Keys.Order()], ArchiveEntryLimit, (index, stream) =>
                    {
                        var result = _content!.Match(stream, _token);
                        if (result.IsMatch)
                        {
                            _callbacks.Found?.Invoke(toRead[index] with { Line = result.Line });
                        }
                    }, (index, message) => Log(SearchLogKind.NotRead, toRead[index].FullPath, message), _token);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                Log(SearchLogKind.Error, archivePath, ex.Message);
            }
        }
    }
}
