// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/QuickOpen/Usage.cs

namespace Salandual.Core.QuickOpen;

public sealed record UsageEntry(string Item, int Count, DateTime LastUtc);

public static class Usage
{
    public const int MaxFolders = 300, MaxFiles = 200, MaxCommands = 100;

    public static double Frecency(UsageEntry entry, DateTime nowUtc)
    {
        var age = nowUtc - entry.LastUtc;
        var weight = age < TimeSpan.FromHours(4) ? 4.0
            : age < TimeSpan.FromDays(1) ? 2.0
            : age < TimeSpan.FromDays(7) ? 1.0
            : age < TimeSpan.FromDays(30) ? 0.5
            : 0.25;
        return entry.Count * weight;
    }

    public static List<UsageEntry> Used(IEnumerable<UsageEntry>? list, string item, DateTime nowUtc, int max)
    {
        var all = (list ?? []).ToList();
        var old = all.FindIndex(e => string.Equals(e.Item, item, StringComparison.OrdinalIgnoreCase));
        var count = old >= 0 ? all[old].Count + 1 : 1;
        if (old >= 0)
        {
            all.RemoveAt(old);
        }

        all.Insert(0, new UsageEntry(item, Math.Min(count, 10_000), nowUtc));
        if (all.Count > max)
        {
            all = [.. all.OrderByDescending(e => e == all[0] ? double.MaxValue : Frecency(e, nowUtc)).Take(max).OrderByDescending(e => e.LastUtc)];
        }

        return all;
    }

    public static List<UsageEntry> Clean(IEnumerable<UsageEntry?>? list, int max)
        => [.. (list ?? [])
            .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Item) && e.Count > 0)
            .Select(e => e!)
            .DistinctBy(e => e.Item, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(e => e.LastUtc)
            .Take(max)];

    public static IEnumerable<UsageEntry> Latest(IEnumerable<UsageEntry> list) => list.OrderByDescending(e => e.LastUtc);

    public static IEnumerable<UsageEntry> Most(IEnumerable<UsageEntry> list, DateTime nowUtc) => list.OrderByDescending(e => Frecency(e, nowUtc)).ThenByDescending(e => e.LastUtc);
}
