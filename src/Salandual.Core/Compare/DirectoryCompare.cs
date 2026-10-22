// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Compare/DirectoryCompare.cs

using Salandual.Core.Listing;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Core.Compare;

[Flags]
public enum CompareSide
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = Left | Right,
}

public enum DifferenceKind
{
    OnlyLeft,

    OnlyRight,

    NewerOrBigger,

    Different,

    CountedSame,

    Same,
}

public sealed record CompareDifference(string Subfolder, string Name, bool IsDirectory, FileEntry? Left, FileEntry? Right, DifferenceKind Kind, CompareSide Selected, bool Inside = false, bool InOneSided = false);

public readonly record struct CompareProgress(int Folders, int Files, string? CurrentFolder);

public sealed record DirectoryCompareResult(IReadOnlyList<CompareDifference> Differences, int Folders, int Files, int NotRead, IReadOnlyList<string> Problems, bool Cancelled, TimeSpan Elapsed)
{
    public int Count(DifferenceKind kind) => Differences.Count(d => d.Kind == kind && d.Subfolder.Length == 0);

    public IEnumerable<CompareDifference> In(string subfolder) => Differences.Where(d => string.Equals(d.Subfolder, subfolder, StringComparison.OrdinalIgnoreCase));
}

public sealed class DirectoryCompare
{
    public const FileAttributes ComparedAttributes = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;

    private const FileAttributes OnlineOnly = (FileAttributes)0x00400000 | (FileAttributes)0x00040000 | FileAttributes.Offline;
    private static readonly long TwoSeconds = TimeSpan.FromSeconds(2).Ticks;
    private static readonly long OneHour = TimeSpan.FromHours(1).Ticks;

    private readonly IFileSystemProvider _files;
    private readonly Func<string, Stream> _open;
    private readonly CompareDirectoriesOptions _options;
    private readonly bool _showHidden;
    private readonly FileMask? _leaveOutFiles;
    private readonly FileMask? _leaveOutFolders;
    private readonly List<CompareDifference> _differences = [];
    private readonly List<string> _problems = [];
    private int _folders;
    private int _filesSeen;
    private int _notRead;

    public DirectoryCompare(IFileSystemProvider files, Func<string, Stream> open, CompareDirectoriesOptions options, bool showHidden)
    {
        _files = files;
        _open = open;
        _options = options;
        _showHidden = showHidden;
        _leaveOutFiles = options.LeaveOutFiles ? FileMask.Parse(options.LeaveOutFilesMask) : null;
        _leaveOutFolders = options.LeaveOutFolders ? FileMask.Parse(options.LeaveOutFoldersMask) : null;
    }

    public async Task<DirectoryCompareResult> RunAsync(string left, string right, Action<CompareProgress>? progress = null, CancellationToken token = default)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var cancelled = false;
        try
        {
            await Task.Run(() => CompareAsync(left, right, "", progress, token), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        return new DirectoryCompareResult([.. _differences], _folders, _filesSeen, _notRead, [.. _problems], cancelled, clock.Elapsed);
    }

    private async Task<bool> CompareAsync(string left, string right, string subfolder, Action<CompareProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        progress?.Invoke(new CompareProgress(_folders, _filesSeen, left));
        var leftItems = await ListAsync(left, token).ConfigureAwait(false);
        var rightItems = await ListAsync(right, token).ConfigureAwait(false);
        if (leftItems is null || rightItems is null)
        {
            return false;
        }

        _folders++;
        var byName = rightItems.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        var any = false;
        foreach (var l in leftItems.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (LeftOut(l))
            {
                byName.Remove(l.Name);
                continue;
            }

            if (!byName.Remove(l.Name, out var r) || LeftOut(r) || r.IsDirectory != l.IsDirectory)
            {
                any |= await OnlyOnOneSideAsync(subfolder, l, DifferenceKind.OnlyLeft, left, token).ConfigureAwait(false);
                if (r.Name is not null && r.IsDirectory != l.IsDirectory && !LeftOut(r))
                {
                    any |= await OnlyOnOneSideAsync(subfolder, r, DifferenceKind.OnlyRight, right, token).ConfigureAwait(false);
                }

                continue;
            }

            if (l.IsDirectory)
            {
                any |= await FoldersAsync(left, right, subfolder, l, r, progress, token).ConfigureAwait(false);
            }
            else
            {
                _filesSeen++;
                any |= Files(left, right, subfolder, l, r, token);
            }
        }

        foreach (var r in byName.Values.Where(e => !LeftOut(e)).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            any |= await OnlyOnOneSideAsync(subfolder, r, DifferenceKind.OnlyRight, right, token).ConfigureAwait(false);
        }

        return any;
    }

    private async Task<FileEntry[]?> ListAsync(string path, CancellationToken token)
    {
        try
        {
            var listing = await _files.ListAsync(path, token).ConfigureAwait(false);
            return EntryFilter.Apply(listing.Entries, _showHidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _problems.Add($"{path}: {ex.Message}");

            return null;
        }
    }

    private bool LeftOut(FileEntry entry)
        => entry.IsDirectory ? _leaveOutFolders?.IsMatch(entry.Name) == true : _leaveOutFiles?.IsMatch(entry.Name) == true;

    private async Task<bool> OnlyOnOneSideAsync(string subfolder, FileEntry entry, DifferenceKind kind, string folder, CancellationToken token)
    {
        if (entry.IsDirectory && !_options.FoldersOnOneSide)
        {
            return false;
        }

        Add(subfolder, entry, kind, inOneSided: false);
        if (entry.IsDirectory && _options.Subfolders)
        {
            await AddInsideAsync(_files.Combine(folder, entry.Name), Sub(subfolder, entry.Name), kind, token).ConfigureAwait(false);
        }

        return true;
    }

    private void Add(string subfolder, FileEntry entry, DifferenceKind kind, bool inOneSided)
    {
        var left = kind == DifferenceKind.OnlyLeft;
        _differences.Add(new CompareDifference(subfolder, entry.Name, entry.IsDirectory, left ? entry : null, left ? null : entry, kind, left ? CompareSide.Left : CompareSide.Right, InOneSided: inOneSided));
    }

    private async Task AddInsideAsync(string path, string subfolder, DifferenceKind kind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (await ListAsync(path, token).ConfigureAwait(false) is not { } items)
        {
            return;
        }

        _folders++;
        foreach (var e in items.Where(e => !LeftOut(e)).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            Add(subfolder, e, kind, inOneSided: true);
            if (e.IsDirectory)
            {
                await AddInsideAsync(_files.Combine(path, e.Name), Sub(subfolder, e.Name), kind, token).ConfigureAwait(false);
            }
            else
            {
                _filesSeen++;
            }
        }
    }

    private static string Sub(string subfolder, string name) => subfolder.Length == 0 ? name : subfolder + "\\" + name;

    private async Task<bool> FoldersAsync(string left, string right, string subfolder, FileEntry l, FileEntry r, Action<CompareProgress>? progress, CancellationToken token)
    {
        var attributes = _options.SubfolderAttributes && (l.Attributes & ComparedAttributes) != (r.Attributes & ComparedAttributes);
        var inside = _options.Subfolders
            && await CompareAsync(_files.Combine(left, l.Name), _files.Combine(right, r.Name), Sub(subfolder, l.Name), progress, token).ConfigureAwait(false);
        if (!attributes && !inside)
        {
            _differences.Add(new CompareDifference(subfolder, l.Name, true, l, r, DifferenceKind.Same, CompareSide.None));
            return false;
        }

        _differences.Add(new CompareDifference(subfolder, l.Name, true, l, r, DifferenceKind.Different, CompareSide.Both, Inside: inside));
        return true;
    }

    private bool Files(string left, string right, string subfolder, FileEntry l, FileEntry r, CancellationToken token)
    {
        var side = CompareSide.None;
        var countedSame = false;
        if (_options.Newer && l.LastWriteUtcTicks != r.LastWriteUtcTicks)
        {
            if (CloseEnough(l.LastWriteUtcTicks, r.LastWriteUtcTicks))
            {
                countedSame = true;
            }
            else
            {
                side |= l.LastWriteUtcTicks > r.LastWriteUtcTicks ? CompareSide.Left : CompareSide.Right;
            }
        }

        if (_options.Bigger && l.Size != r.Size)
        {
            side |= l.Size > r.Size ? CompareSide.Left : CompareSide.Right;
        }

        var kind = side != CompareSide.None ? DifferenceKind.NewerOrBigger : (DifferenceKind?)null;
        if (kind is null && _options.Attributes && (l.Attributes & ComparedAttributes) != (r.Attributes & ComparedAttributes))
        {
            kind = DifferenceKind.Different;
        }

        if (kind is null && _options.Content && !SameContent(_files.Combine(left, l.Name), _files.Combine(right, r.Name), l, r, token))
        {
            kind = DifferenceKind.Different;
        }

        if (kind == DifferenceKind.Different)
        {
            side = CompareSide.Both;
        }
        else if (kind is null && countedSame)
        {
            kind = DifferenceKind.CountedSame;
        }

        if (kind is not { } k)
        {
            _differences.Add(new CompareDifference(subfolder, l.Name, false, l, r, DifferenceKind.Same, CompareSide.None));
            return false;
        }

        _differences.Add(new CompareDifference(subfolder, l.Name, false, l, r, k, side));
        return k != DifferenceKind.CountedSame;
    }

    private bool CloseEnough(long a, long b)
    {
        var apart = Math.Abs(a - b);
        var slack = _options.IgnoreTwoSeconds ? TwoSeconds : 0;
        return (_options.IgnoreTwoSeconds && apart <= TwoSeconds) || (_options.IgnoreOneHour && Math.Abs(apart - OneHour) <= slack);
    }

    private bool SameContent(string leftPath, string rightPath, FileEntry l, FileEntry r, CancellationToken token)
    {
        if (l.Size != r.Size)
        {
            return false;
        }

        if (((l.Attributes | r.Attributes) & OnlineOnly) != 0)
        {
            _notRead++;
            return true;
        }

        try
        {
            using var a = _open(leftPath);
            using var b = _open(rightPath);
            var x = new byte[81920];
            var y = new byte[81920];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var got = a.ReadAtLeast(x, x.Length, throwOnEndOfStream: false);
                var other = got == 0 ? b.Read(y, 0, 1) : b.ReadAtLeast(y.AsSpan(0, got), got, throwOnEndOfStream: false);
                if (got != other || !x.AsSpan(0, got).SequenceEqual(y.AsSpan(0, other)))
                {
                    return false;
                }

                if (got < x.Length)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _notRead++;
            _problems.Add($"{leftPath}: {ex.Message}");
            return true;
        }
    }
}
