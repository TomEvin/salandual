// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Compare/CompareTree.cs

using Salandual.Core.Listing;

namespace Salandual.Core.Compare;

public enum CompareView
{
    All,
    Different,
    Same,
}

public enum DifferenceFilter
{
    Differences,
    NoOrphans,
    DifferencesButNoOrphans,
    Orphans,
    LeftNewer,
    RightNewer,
    LeftNewerAndLeftOrphans,
    RightNewerAndRightOrphans,
    LeftOrphans,
    RightOrphans,
}

public sealed record CompareListFilter(CompareView View, DifferenceFilter Kind, FileMask? Names = null, bool Not = false)
{
    public static CompareListFilter Differences { get; } = new(CompareView.Different, DifferenceFilter.Differences);
}

public sealed class CompareNode
{
    internal CompareNode(CompareDifference item, string path)
    {
        Item = item;
        Path = path;
    }

    public CompareDifference Item { get; }

    public string Path { get; }

    public List<CompareNode> Children { get; } = [];

    public bool IsDirectory => Item.IsDirectory;
}

public sealed class CompareTree
{
    private CompareTree(IReadOnlyList<CompareNode> roots) => Roots = roots;

    public IReadOnlyList<CompareNode> Roots { get; }

    public static CompareTree From(DirectoryCompareResult result)
    {
        var folders = new Dictionary<string, CompareNode>(StringComparer.OrdinalIgnoreCase);
        var nodes = result.Differences.Select(d => new CompareNode(d, d.Subfolder.Length == 0 ? d.Name : d.Subfolder + "\\" + d.Name)).ToList();
        foreach (var node in nodes.Where(n => n.IsDirectory))
        {
            folders.TryAdd(node.Path, node);
        }

        var roots = new List<CompareNode>();
        foreach (var node in nodes)
        {
            if (node.Item.Subfolder.Length == 0)
            {
                roots.Add(node);
            }
            else if (folders.TryGetValue(node.Item.Subfolder, out var parent))
            {
                parent.Children.Add(node);
            }
        }

        Sort(roots);
        return new CompareTree(roots);

        static void Sort(List<CompareNode> list)
        {
            list.Sort((a, b) => a.IsDirectory != b.IsDirectory ? (a.IsDirectory ? -1 : 1) : StringComparer.OrdinalIgnoreCase.Compare(a.Item.Name, b.Item.Name));
            foreach (var node in list)
            {
                Sort(node.Children);
            }
        }
    }

    public static bool Matches(CompareNode node, CompareListFilter filter)
    {
        var d = node.Item;
        if (d.Inside)
        {
            return false;
        }

        if (filter.Names is { } names && (d.IsDirectory || names.IsMatch(d.Name) == filter.Not))
        {
            return false;
        }

        var orphan = d.Kind is DifferenceKind.OnlyLeft or DifferenceKind.OnlyRight;
        var leftNewer = d.Kind == DifferenceKind.NewerOrBigger && d.Selected.HasFlag(CompareSide.Left);
        var rightNewer = d.Kind == DifferenceKind.NewerOrBigger && d.Selected.HasFlag(CompareSide.Right);
        return filter.View switch
        {
            CompareView.All => true,
            CompareView.Same => d.Kind is DifferenceKind.Same or DifferenceKind.CountedSame,
            _ => filter.Kind switch
            {
                DifferenceFilter.Differences => d.Kind is not (DifferenceKind.Same or DifferenceKind.CountedSame),
                DifferenceFilter.NoOrphans => !orphan,
                DifferenceFilter.DifferencesButNoOrphans => d.Kind is DifferenceKind.NewerOrBigger or DifferenceKind.Different,
                DifferenceFilter.Orphans => orphan,
                DifferenceFilter.LeftNewer => leftNewer,
                DifferenceFilter.RightNewer => rightNewer,
                DifferenceFilter.LeftNewerAndLeftOrphans => leftNewer || d.Kind == DifferenceKind.OnlyLeft,
                DifferenceFilter.RightNewerAndRightOrphans => rightNewer || d.Kind == DifferenceKind.OnlyRight,
                DifferenceFilter.LeftOrphans => d.Kind == DifferenceKind.OnlyLeft,
                _ => d.Kind == DifferenceKind.OnlyRight,
            },
        };
    }

    public static bool Shows(CompareNode node, CompareListFilter filter) => Matches(node, filter) || node.Children.Any(c => Shows(c, filter));

    public int Count(CompareListFilter filter)
    {
        var count = 0;
        var stack = new Stack<CompareNode>(Roots);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (Matches(node, filter))
            {
                count++;
            }

            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }

        return count;
    }

    public IEnumerable<CompareNode> Folders(CompareListFilter filter)
    {
        var stack = new Stack<CompareNode>(Roots);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Children.Any(c => Shows(c, filter)))
            {
                yield return node;
                foreach (var child in node.Children)
                {
                    stack.Push(child);
                }
            }
        }
    }

    public CompareNode? Find(string path)
    {
        var list = Roots;
        CompareNode? found = null;
        foreach (var part in path.Split('\\'))
        {
            found = list.FirstOrDefault(n => string.Equals(n.Item.Name, part, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                return null;
            }

            list = found.Children;
        }

        return found;
    }
}
