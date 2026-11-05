// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Git/GitGraph.cs

namespace Salandual.Core.Git;

public readonly record struct GitGraphLine(int From, int To, int Color);

public sealed record GitGraphRow(int Lane, int Color, IReadOnlyList<GitGraphLine> Lines);

public static class GitGraph
{
    public const int Colors = 6;

    public static IReadOnlyList<GitGraphRow> Layout(IReadOnlyList<GitCommit> commits)
    {
        var rows = new GitGraphRow[commits.Count];
        var lanes = new List<string?>();
        var colors = new List<int>();
        var nextColor = 0;
        var pending = new List<(int From, int To)>();
        var fresh = new HashSet<int>();


        int Free()
        {
            var i = lanes.IndexOf(null);
            if (i < 0)
            {
                lanes.Add(null);
                colors.Add(0);
                i = lanes.Count - 1;
            }

            colors[i] = nextColor++ % Colors;
            return i;
        }

        for (var r = 0; r < commits.Count; r++)
        {
            var commit = commits[r];

            var at = lanes.IndexOf(commit.Sha);
            if (at < 0)
            {
                at = Free();
            }

            if (r > 0)
            {
                rows[r - 1] = rows[r - 1] with { Lines = Lines(lanes, colors, commit.Sha, at, pending, fresh) };
            }

            pending.Clear();
            fresh.Clear();
            for (var i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] == commit.Sha)
                {
                    lanes[i] = null;
                }
            }



            rows[r] = new GitGraphRow(at, colors[at], []);

            if (commit.Parents.Count > 0)
            {
                lanes[at] = commit.Parents[0];
            }

            for (var p = 1; p < commit.Parents.Count; p++)
            {
                var parent = commit.Parents[p];
                var waiting = lanes.IndexOf(parent);
                if (waiting < 0)
                {
                    waiting = Free();
                    lanes[waiting] = parent;
                    fresh.Add(waiting);
                }

                pending.Add((at, waiting));
            }
        }

        if (commits.Count > 0)
        {
            rows[^1] = rows[^1] with { Lines = Lines(lanes, colors, null, -1, pending, fresh) };
        }

        return rows;
    }

    private static List<GitGraphLine> Lines(List<string?> lanes, List<int> colors, string? next, int nextLane, List<(int From, int To)> pending, HashSet<int> fresh)
    {
        var lines = new List<GitGraphLine>();
        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] is not { } waits || fresh.Contains(i))
            {
                continue;
            }

            lines.Add(new GitGraphLine(i, waits == next ? nextLane : i, colors[i]));
        }

        foreach (var (from, to) in pending)
        {
            lines.Add(new GitGraphLine(from, lanes[to] == next && next is not null ? nextLane : to, colors[to]));
        }

        return lines;
    }

    public static int Width(IReadOnlyList<GitGraphRow> rows)
    {
        var most = 0;
        foreach (var row in rows)
        {
            most = Math.Max(most, row.Lane + 1);
            foreach (var line in row.Lines)
            {
                most = Math.Max(most, Math.Max(line.From, line.To) + 1);
            }
        }

        return most;
    }
}
