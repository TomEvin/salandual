// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/QuickOpen/QuickOpenSearch.cs

using System.Diagnostics;

namespace Salandual.Core.QuickOpen;

public enum QuickKind { Command, Folder, File, Tab, Setting, Path, More }

public sealed record QuickItem(QuickKind Kind, string Name, string Detail, string Target, string Key = "", bool Enabled = true)
{
    public IReadOnlyList<int> Hits { get; init; } = [];

    public double Score { get; init; }

    public bool OnlineOnly { get; init; }
}

public sealed record WalkedEntry(string Name, string Folder, bool IsFolder, bool OnlineOnly = false);

public sealed record WalkResult(IReadOnlyList<WalkedEntry> Entries, bool CutShort);

public static class QuickOpenSearch
{
    public const int MaxWalked = 50_000;

    public static readonly TimeSpan WalkTime = TimeSpan.FromSeconds(3);

    public static IReadOnlyList<QuickItem> Rank(string typed, IEnumerable<QuickItem> candidates, int max, Func<QuickItem, double>? boost = null)
    {
        var found = new List<QuickItem>();
        foreach (var item in candidates)
        {
            var extra = boost?.Invoke(item) ?? 0;
            if (QuickMatch.Match(typed, item.Name) is { } m)
            {
                found.Add(item with { Hits = m.Positions, Score = m.Score + extra });
            }
            else if (item.Kind is QuickKind.Folder or QuickKind.File && item.Detail.Length > 0
                     && QuickMatch.Match(typed, item.Detail + "\\" + item.Name) is { } inPath)
            {
                found.Add(item with { Hits = [], Score = inPath.Score - 30 + extra });
            }
        }

        return [.. found.OrderByDescending(i => i.Score).ThenBy(i => i.Name.Length).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).Take(max)];
    }

    public static double Boost(double frecency) => frecency <= 0 ? 0 : Math.Min(12, 3 * Math.Log2(1 + frecency));

    public static WalkResult Walk(string root, bool showHidden, CancellationToken cancel, int max = MaxWalked, TimeSpan? time = null, WalkFilter? filter = null)
    {
        filter ??= WalkFilter.None;
        var entries = new List<WalkedEntry>();
        var limit = time ?? WalkTime;
        var clock = Stopwatch.StartNew();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint | (showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System) | filter.Attributes,
        };
        var rootFull = Path.TrimEndingDirectorySeparator(root);
        try
        {
            var walk = new System.IO.Enumeration.FileSystemEnumerable<WalkedEntry>(rootFull,
                (ref System.IO.Enumeration.FileSystemEntry e) => new WalkedEntry(e.FileName.ToString(), e.Directory.ToString(), e.IsDirectory), options)
            {
                ShouldIncludePredicate = (ref System.IO.Enumeration.FileSystemEntry e) => !e.IsDirectory || !SkipsFolder(filter, ref e),
                ShouldRecursePredicate = (ref System.IO.Enumeration.FileSystemEntry e) => !SkipsFolder(filter, ref e),
            };
            foreach (var entry in walk)
            {
                if (cancel.IsCancellationRequested)
                {
                    return new WalkResult(entries, true);
                }

                entries.Add(entry);
                if (entries.Count >= max || clock.Elapsed > limit)
                {
                    return new WalkResult(entries, true);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new WalkResult(entries, true);
        }

        return new WalkResult(entries, false);
    }

    private static bool SkipsFolder(WalkFilter filter, ref System.IO.Enumeration.FileSystemEntry e)
    {
        var name = e.FileName.ToString();
        return filter.SkipsFolder(Path.Join(e.Directory, name), name);
    }
}
