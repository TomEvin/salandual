// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Git/GitChanges.cs

namespace Salandual.Core.Git;

public enum GitChangeGroup
{
    Staged,

    Changed,

    New,

    Conflicted,
}

public sealed record GitChange(string Path, GitChangeGroup Group, GitState State, int? Added, int? Removed, bool InHead, bool HasStaged, bool OnDisk)
{
    public string Letter => State switch
    {
        GitState.Conflicted => "U",
        GitState.New => "?",
        GitState.Added => "A",
        GitState.Deleted => "D",
        _ => !OnDisk ? "D" : "M",
    };
}

public sealed record GitHeadFacts(string? Sha, string? Message, bool Pushed, string? Author, IReadOnlyList<string> Recent);

public enum GitVersion
{
    Head,

    Index,
}
