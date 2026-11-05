// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Git/GitHistory.cs

namespace Salandual.Core.Git;

public enum GitRefKind
{
    Branch,

    Remote,

    Tag,
}

public sealed record GitRef(string Name, GitRefKind Kind, bool IsCurrent = false);

public sealed record GitCommit(string Sha, string Summary, string Message, string Author, string Email, DateTimeOffset When, IReadOnlyList<string> Parents, IReadOnlyList<GitRef> Refs, bool Pushed)
{
    public string Short => Sha.Length > 7 ? Sha[..7] : Sha;

    public bool IsMerge => Parents.Count > 1;
}

public sealed record GitLogQuery(bool AllBranches = false, bool FirstParentOnly = false, string? Path = null, int Limit = 2000);

public sealed record GitLog(IReadOnlyList<GitCommit> Commits, string? Upstream, bool More);

public sealed record GitCommitChange(string Path, GitState State, int? Added, int? Removed);

public sealed record GitTreeItem(string Name, bool IsFolder, long Size, string? Submodule = null);

public sealed record GitBlameRun(int Start, int Count, GitCommit? Commit);

public sealed record GitBlame(IReadOnlyList<GitBlameRun> Runs, int Lines, DateTimeOffset Oldest, DateTimeOffset Newest)
{
    public const int MaxLines = 20_000;

    public GitBlameRun? RunAt(int line)
    {
        int lo = 0, hi = Runs.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var run = Runs[mid];
            if (line < run.Start)
            {
                hi = mid - 1;
            }
            else if (line >= run.Start + run.Count)
            {
                lo = mid + 1;
            }
            else
            {
                return run;
            }
        }

        return null;
    }

    public double Age(GitCommit? commit)
    {
        var span = (Newest - Oldest).TotalSeconds;
        return commit is null || span <= 0 ? 0 : Math.Clamp((Newest - commit.When).TotalSeconds / span, 0, 1);
    }
}

public enum GitBlameRefusal
{
    None,

    TooBig,

    Binary,

    NotInGit,

    Unreadable,
}

public sealed record GitTag(string Name, GitCommit Commit, string Message, bool IsAnnotated, string By, DateTimeOffset When);

public enum GitSubmoduleState
{
    Same,

    OtherCommit,

    ChangesInside,

    NotCheckedOut,
}

public sealed record GitSubmodule(string Path, string Recorded, GitSubmoduleState State);
