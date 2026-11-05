// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/GitGraphTests.cs

using Salandual.Core.Git;

namespace Salandual.Core.Tests;

public sealed class GitGraphTests
{
    private static GitCommit C(string sha, params string[] parents) => new(sha, sha, sha, "Ann", "ann@example.invalid", DateTimeOffset.UnixEpoch, parents, [], false);

    [Fact]
    public void AStraightLine_IsOneLane()
    {
        var rows = GitGraph.Layout([C("c", "b"), C("b", "a"), C("a")]);
        Assert.All(rows, r => Assert.Equal(0, r.Lane));
        Assert.Equal([new GitGraphLine(0, 0, 0)], rows[0].Lines);
        Assert.Empty(rows[2].Lines);
        Assert.Equal(1, GitGraph.Width(rows));
    }

    [Fact]
    public void AMerge_ItsSecondParentJoinsFromALaneOfItsOwn_AndEndsWhereItBranched()
    {
        var rows = GitGraph.Layout([C("m", "b", "f"), C("b", "a"), C("f", "a"), C("a")]);
        Assert.Equal([0, 0, 1, 0], rows.Select(r => r.Lane));
        Assert.Contains(new GitGraphLine(0, 0, rows[0].Color), rows[0].Lines);
        Assert.Contains(rows[0].Lines, l => l.From == 0 && l.To == 1);
        Assert.Contains(rows[1].Lines, l => l.From == 1 && l.To == 1);
        Assert.Contains(rows[2].Lines, l => l.From == 1 && l.To == 0);
        Assert.Contains(rows[2].Lines, l => l.From == 0 && l.To == 0);
        Assert.NotEqual(rows[0].Color, rows[2].Color);
        Assert.Equal(2, GitGraph.Width(rows));
    }

    [Fact]
    public void TwoTips_TakeTwoLanes_AndAFreedLaneIsTakenAgain()
    {
        var rows = GitGraph.Layout([C("x", "a"), C("c", "a"), C("a"), C("y"), C("z")]);
        Assert.Equal(0, rows[0].Lane);
        Assert.Equal(1, rows[1].Lane);
        Assert.Equal(0, rows[2].Lane);
        Assert.Contains(rows[1].Lines, l => l.From == 1 && l.To == 0);
        Assert.Equal(0, rows[3].Lane);
        Assert.Equal(0, rows[4].Lane);
    }
}
