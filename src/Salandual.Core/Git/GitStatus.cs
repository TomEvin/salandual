// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Git/GitStatus.cs

namespace Salandual.Core.Git;

public enum GitState
{
    None,

    Changed,

    Staged,

    Added,

    New,

    Deleted,

    Conflicted,

    Ignored,
}

public sealed record GitBranch(string Name, bool IsRemote, bool IsCurrent, int Ahead = 0, int Behind = 0);

public sealed class GitStatus
{
    private readonly Dictionary<string, GitState> _files;
    private readonly Dictionary<string, GitState> _folders;
    private readonly Dictionary<string, GitState> _whole;

    private GitStatus(string top, Dictionary<string, GitState> files, Dictionary<string, GitState> folders, Dictionary<string, GitState> whole)
    {
        Top = top;
        _files = files;
        _folders = folders;
        _whole = whole;
    }

    public string Top { get; }

    public string? Branch { get; init; }

    public string? DetachedAt { get; init; }

    public string? Upstream { get; init; }

    public int Ahead { get; init; }

    public int Behind { get; init; }

    public int ChangedCount { get; private init; }

    public IReadOnlyDictionary<string, GitSubmodule> Submodules { get; init; } = new Dictionary<string, GitSubmodule>();

    public GitSubmodule? SubmoduleAt(string fullPath)
        => Submodules.Count > 0 && Relative(fullPath) is { Length: > 0 } relative && Submodules.TryGetValue(relative, out var sub) ? sub : null;

    public string Label => Branch ?? (DetachedAt is { } at ? "(" + at + ")" : "");

    public GitState Of(string fullPath, bool isDirectory, out bool inside)
    {
        inside = false;
        if (Relative(fullPath) is not { } relative)
        {
            return GitState.None;
        }

        if (relative.Length == 0)
        {
            inside = ChangedCount > 0;
            return inside ? Strongest(_folders.Values) : GitState.None;
        }

        if (_files.TryGetValue(relative, out var state))
        {
            return state;
        }

        if (_whole.TryGetValue(relative, out state))
        {
            return state;
        }

        for (var cut = relative.LastIndexOf('\\'); cut > 0; cut = relative.LastIndexOf('\\', cut - 1))
        {
            if (_whole.TryGetValue(relative[..cut], out state))
            {
                return state;
            }
        }

        if (isDirectory && _folders.TryGetValue(relative, out state))
        {
            inside = true;
            return state;
        }

        return GitState.None;
    }

    public GitState Of(string fullPath, bool isDirectory) => Of(fullPath, isDirectory, out _);

    public static GitStatus From(string top, IEnumerable<(string Path, GitState State)> entries, string? branch = null, string? detachedAt = null, string? upstream = null, int ahead = 0, int behind = 0, IEnumerable<GitSubmodule>? submodules = null)
    {
        var files = new Dictionary<string, GitState>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, GitState>(StringComparer.OrdinalIgnoreCase);
        var whole = new Dictionary<string, GitState>(StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        foreach (var (path, state) in entries)
        {
            if (state == GitState.None || path.Length == 0)
            {
                continue;
            }

            var isFolder = path[^1] is '/' or '\\';
            var relative = path.TrimEnd('/', '\\').Replace('/', '\\');
            if (relative.Length == 0)
            {
                continue;
            }

            (isFolder ? whole : files)[relative] = state;
            if (state == GitState.Ignored)
            {
                continue;
            }

            changed++;
            for (var cut = relative.LastIndexOf('\\'); cut > 0; cut = relative.LastIndexOf('\\', cut - 1))
            {
                var folder = relative[..cut];
                folders[folder] = folders.TryGetValue(folder, out var was) ? Stronger(was, state) : state;
            }
        }

        return new GitStatus(Path.TrimEndingDirectorySeparator(top), files, folders, whole)
        {
            Branch = branch,
            DetachedAt = detachedAt,
            Upstream = upstream,
            Ahead = ahead,
            Behind = behind,
            ChangedCount = changed,
            Submodules = (submodules ?? []).ToDictionary(s => s.Path.Replace('/', '\\').TrimEnd('\\'), StringComparer.OrdinalIgnoreCase),
        };
    }

    public static GitState Stronger(GitState a, GitState b) => Rank(a) >= Rank(b) ? a : b;

    private static GitState Strongest(IEnumerable<GitState> states) => states.Aggregate(GitState.None, Stronger);

    private static int Rank(GitState state) => state switch
    {
        GitState.Conflicted => 6,
        GitState.Changed => 5,
        GitState.Deleted => 4,
        GitState.Staged or GitState.Added => 3,
        GitState.New => 2,
        GitState.Ignored => 1,
        _ => 0,
    };

    private string? Relative(string fullPath)
    {
        var path = Path.TrimEndingDirectorySeparator(fullPath);
        if (path.Equals(Top, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return path.Length > Top.Length + 1 && path.StartsWith(Top, StringComparison.OrdinalIgnoreCase) && path[Top.Length] == '\\'
            ? path[(Top.Length + 1)..]
            : null;
    }
}
