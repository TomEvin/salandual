// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/FilePanel.Flat.cs

using System.Diagnostics;
using Salandual.Core.Listing;
using Salandual.Core.Vfs;

namespace Salandual.Core.Panels;

public enum FlatFolderShows
{
    Column,

    Headings,

    InName,
}

public sealed record PanelFlat(IReadOnlyList<string> Folders, bool Reading, bool Stopped, int Files, int FoldersRead);

public sealed partial class FilePanel
{
    private CancellationTokenSource? _flatCts;
    private HashSet<string> _flatFolders = new(StringComparer.OrdinalIgnoreCase);

    public bool IsFlat => Results?.Flat is not null;

    public FlatFolderShows FlatFolderShows { get; private set; }

    public int FlatAskAbove { get; set; } = 100_000;

    public Func<int, Task<bool>>? AskToReadOn { get; set; }

    public bool IsHeading(int index) => (IsFlat || (IsGitList && !IsTree)) && (uint)index < (uint)_items.Length && _items[index].IsDirectory;

    public void SetFlatFolderShows(FlatFolderShows shows)
    {
        if (shows != FlatFolderShows)
        {
            FlatFolderShows = shows;
            if (IsFlat)
            {
                SortFlat(_listed);
                Refilter();
            }
        }
    }

    public Task<bool> ToggleFlatAsync(IReadOnlyList<string>? folders = null)
        => IsFlat ? LeaveResultsAsync() : ShowFlatAsync(folders);

    public Task<bool> ShowFlatAsync(IReadOnlyList<string>? folders = null)
    {
        if (Path.Length == 0 || (Results is not null && !IsFlat))
        {
            return Task.FromResult(false);
        }

        var again = IsFlat;
        folders = again ? Results!.Flat!.Folders : folders ?? [];
        _loadCts?.Cancel();
        ++_loadVersion;
        _watch?.Dispose();
        _watch = null;
        if (!again)
        {
            var back = (Path, FocusedEntry is { } f && !IsUp(Focus) ? f.Name : null);
            RememberSelection();
            RememberScreenRow();
            RecordHistory();
            _sizeCts?.Cancel();
            _sizeCts = null;
            _dirSizes.Clear();
            _sizing.Clear();
            _pendingFocus = null;
            _pendingSelect = null;
            _stat = null;
            _keepOrder = false;
            HasUpEntry = false;
            Results = new PanelResults("", back.Path, back.Item2);
            _listed = [];
            _items = [];
            _selected = [];
            Focus = 0;
            _focusScreenRow = null;
            QuickSearchText = null;
            _extendState = null;
        }

        Results = Results! with { Flat = new PanelFlat(folders, Reading: true, Stopped: false, again ? Results.Flat!.Files : 0, 0) };
        Changed?.Invoke(this, PanelChange.Items | PanelChange.Selection | PanelChange.Focus | PanelChange.QuickSearch);
        return ReadFlatAsync(folders, stream: !again);
    }

    public void StopFlat() => _flatCts?.Cancel();

    public static string FlatFolderOf(FileEntry e) => e.IsDirectory ? e.Name : SplitResult(e.Name).Folder;

    private async Task<bool> ReadFlatAsync(IReadOnlyList<string> folders, bool stream)
    {
        _flatCts?.Cancel();
        var cts = _flatCts = new CancellationTokenSource();
        var version = _loadVersion;
        var root = Path;
        var showHidden = ShowHidden;
        var ask = FlatAskAbove > 0 ? AskToReadOn : null;
        var askAbove = FlatAskAbove;
        var found = new List<FileEntry>();
        var batch = new List<FileEntry>();
        var read = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stopped = false;

        void Flush()
        {
            if (batch.Count == 0)
            {
                return;
            }

            var part = batch.ToArray();
            var count = (Files: found.Count, Folders: read.Count);
            batch.Clear();
            _post(() => AddFlat(part, count.Files, count.Folders, version));
        }

        try
        {
            await Task.Run(async () =>
            {
                var clock = Stopwatch.StartNew();
                var interval = 150L;
                var asked = false;
                var stack = new Stack<string>();
                foreach (var folder in (folders.Count == 0 ? [""] : folders).Reverse())
                {
                    stack.Push(folder);
                }

                while (stack.Count > 0)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var rel = stack.Pop();
                    if (await ListFlatFolderAsync(root, rel, showHidden, cts.Token).ConfigureAwait(false) is not { } listed)
                    {
                        continue;
                    }

                    read.Add(rel);
                    found.AddRange(listed.Files);
                    if (stream)
                    {
                        batch.AddRange(listed.Files);
                    }

                    for (var i = listed.Below.Count - 1; i >= 0; i--)
                    {
                        stack.Push(listed.Below[i]);
                    }

                    if (stream && clock.ElapsedMilliseconds >= interval)
                    {
                        Flush();
                        clock.Restart();
                        interval = Math.Min(1000, 150 + (found.Count / 200));
                    }

                    if (!asked && ask is not null && found.Count >= askAbove && stack.Count > 0)
                    {
                        asked = true;
                        if (stream)
                        {
                            Flush();
                        }

                        if (!await OnPanelThread(() => ask(found.Count)).ConfigureAwait(false))
                        {
                            stopped = true;
                            break;
                        }
                    }
                }
            }, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }

        var whole = found.ToArray();
        var folderCount = read.Count;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _post(() =>
        {
            if (version != _loadVersion || !IsFlat)
            {
                done.SetResult(false);
                return;
            }

            _flatFolders = read;
            SortFlat(whole);
            _listed = whole;
            Results = Results! with { Flat = Results.Flat! with { Reading = false, Stopped = stopped, Files = whole.Length, FoldersRead = folderCount } };
            Refilter();
            WatchFlat(root, version);
            done.SetResult(true);
        });
        return await done.Task.ConfigureAwait(false);
    }

    private void AddFlat(FileEntry[] part, int files, int folders, int version)
    {
        if (version != _loadVersion || !IsFlat)
        {
            return;
        }

        var all = new FileEntry[_listed.Length + part.Length];
        _listed.CopyTo(all, 0);
        part.CopyTo(all, _listed.Length);
        SortFlat(all);
        _listed = all;
        Results = Results! with { Flat = Results.Flat! with { Files = files, FoldersRead = folders } };
        Refilter();
    }

    private async Task<(List<FileEntry> Files, List<string> Below)?> ListFlatFolderAsync(string root, string rel, bool showHidden, CancellationToken token)
    {
        DirectoryListing listing;
        try
        {
            listing = await _provider.ListAsync(rel.Length == 0 ? root : _provider.Combine(root, rel), token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }

        var files = new List<FileEntry>();
        var below = new List<string>();
        foreach (var e in EntryFilter.Apply(listing.Entries, showHidden).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            var name = rel.Length == 0 ? e.Name : rel + "\\" + e.Name;
            if (!e.IsDirectory)
            {
                files.Add(e with { Name = name });
            }
            else if ((e.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                below.Add(name);
            }
        }

        return (files, below);
    }

    private Task<bool> OnPanelThread(Func<Task<bool>> ask)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _post(async () =>
        {
            try
            {
                answer.SetResult(await ask());
            }
            catch (Exception ex)
            {
                answer.SetException(ex);
            }
        });
        return answer.Task;
    }

    private void WatchFlat(string root, int version)
    {
        _watch?.Dispose();
        _watch = null;
        if ((_provider.Capabilities & ProviderCapabilities.Watch) == 0)
        {
            return;
        }

        try
        {
            _watch = _provider.WatchTree(root,
                changed => _post(() => _ = FlatChangedAsync(changed, version)),
                error => _post(() =>
                {
                    if (version == _loadVersion && IsFlat)
                    {
                        _ = LeaveResultsAsync();
                    }
                }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private async Task FlatChangedAsync(IReadOnlyCollection<string>? changed, int version)
    {
        if (version != _loadVersion || !IsFlat || Results!.Flat!.Reading)
        {
            return;
        }

        if (changed is null)
        {
            await ShowFlatAsync();
            return;
        }

        var root = Path;
        var showHidden = ShowHidden;
        var known = _flatFolders;
        var scope = Results.Flat.Folders;
        var rels = new List<string>();
        foreach (var path in changed)
        {
            if (Relative(root, path) is { } rel && InScope(rel, scope) && !rels.Contains(rel, StringComparer.OrdinalIgnoreCase)
                && (known.Contains(rel) || known.Contains(ParentOf(rel))))
            {
                rels.Add(rel);
            }
        }

        if (rels.Count == 0)
        {
            return;
        }

        var (replaced, gone, added, nowRead) = await Task.Run(async () =>
        {
            var replacedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var goneFolders = new List<string>();
            var files = new List<FileEntry>();
            var read = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>(rels);
            while (stack.Count > 0)
            {
                var rel = stack.Pop();
                if (await ListFlatFolderAsync(root, rel, showHidden, CancellationToken.None).ConfigureAwait(false) is not { } listed)
                {
                    goneFolders.Add(rel);
                    read.RemoveWhere(r => Under(r, rel));
                    continue;
                }

                read.Add(rel);
                replacedFolders.Add(rel);
                files.AddRange(listed.Files);
                foreach (var below in listed.Below.Where(b => !read.Contains(b)))
                {
                    stack.Push(below);
                }
            }

            return (replacedFolders, goneFolders, files, read);
        }).ConfigureAwait(true);

        if (version != _loadVersion || !IsFlat)
        {
            return;
        }

        var kept = _listed.Where(e =>
        {
            var folder = SplitResult(e.Name).Folder;
            return !replaced.Contains(folder) && !gone.Any(g => Under(folder, g));
        });
        var all = kept.Concat(added).DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        SortFlat(all);
        _listed = all;
        _flatFolders = nowRead;
        Results = Results! with { Flat = Results.Flat! with { Files = all.Length, FoldersRead = nowRead.Count } };
        Refilter();
    }

    private static bool InScope(string rel, IReadOnlyList<string> scope)
        => scope.Count == 0 || scope.Any(s => Under(rel, s));

    private static bool Under(string rel, string folder)
        => folder.Length == 0
            || string.Equals(rel, folder, StringComparison.OrdinalIgnoreCase)
            || (rel.Length > folder.Length && rel[folder.Length] == '\\' && rel.StartsWith(folder, StringComparison.OrdinalIgnoreCase));

    private static string ParentOf(string rel) => rel.LastIndexOf('\\') is var slash and >= 0 ? rel[..slash] : "";

    private static string? Relative(string root, string path)
    {
        var r = root.TrimEnd('\\');
        var p = path.TrimEnd('\\');
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return p.Length > r.Length + 1 && p[r.Length] == '\\' && p.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? p[(r.Length + 1)..] : null;
    }

    private void SortFlat(FileEntry[] items)
    {
        if (FlatFolderShows == FlatFolderShows.Column)
        {
            SortResults(items);
            return;
        }

        var sort = Sort;
        var names = StringComparer.CurrentCultureIgnoreCase;
        var headings = FlatFolderShows == FlatFolderShows.Headings;
        Array.Sort(items, (a, b) =>
        {
            var (folderA, nameA) = SplitResult(a.Name);
            var (folderB, nameB) = SplitResult(b.Name);
            if (headings && names.Compare(folderA, folderB) is var byFolder and not 0)
            {
                return byFolder;
            }

            var c = sort.Column switch
            {
                SortColumn.Size => a.Size.CompareTo(b.Size),
                SortColumn.Time => a.LastWriteUtcTicks.CompareTo(b.LastWriteUtcTicks),
                SortColumn.Extension => names.Compare(a.Extension.ToString(), b.Extension.ToString()),
                _ => 0,
            };
            if (c == 0)
            {
                c = headings ? names.Compare(nameA, nameB) : names.Compare(a.Name, b.Name);
            }

            return sort.Descending ? -c : c;
        });
    }

    private FileEntry[] FlatItems(FileEntry[] visible)
    {
        if (FlatFolderShows != FlatFolderShows.Headings)
        {
            return visible;
        }

        var items = new List<FileEntry>(visible.Length + 16);
        string? last = null;
        foreach (var e in visible)
        {
            var folder = SplitResult(e.Name).Folder;
            if (folder.Length > 0 && !string.Equals(folder, last, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new FileEntry(folder, 0, 0, FileAttributes.Directory));
            }

            last = folder;
            items.Add(e);
        }

        return [.. items];
    }
}
