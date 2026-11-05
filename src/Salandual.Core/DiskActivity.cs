// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/DiskActivity.cs

namespace Salandual.Core;

public readonly record struct DiskActivitySample(double Busy, double ReadPerSecond, double WritePerSecond)
{
    public int Percent => (int)Math.Round(Math.Clamp(Busy, 0, 100));
}

public interface IDiskActivitySource : IDisposable
{
    IReadOnlyDictionary<string, DiskActivitySample> Sample(IReadOnlyCollection<string> drives);
}

public sealed class MemoryDiskActivitySource : IDiskActivitySource
{
    public Dictionary<string, DiskActivitySample> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string[]> Asked { get; } = [];

    public IReadOnlyDictionary<string, DiskActivitySample> Sample(IReadOnlyCollection<string> drives)
    {
        lock (Values)
        {
            Asked.Add([.. drives]);
            return drives.Where(Values.ContainsKey).ToDictionary(d => d, d => Values[d], StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Dispose()
    {
    }
}

public static class DiskActivity
{
    public static string? DriveOf(string? path)
    {
        if (path is null)
        {
            return null;
        }

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            path = path[4..];
        }

        return path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path.Length == 2 || path[2] == '\\')
            ? char.ToUpperInvariant(path[0]) + ":"
            : null;
    }

    public static double BusyFromIdle(double idlePercent) => Math.Clamp(100 - idlePercent, 0, 100);
}

public enum ActivityLook
{
    Bar,

    Columns,

    Line,
}

public readonly record struct DiskActivityPoint(DateTime Time, DiskActivitySample Sample);

public sealed class DiskActivityHistory
{
    public static readonly TimeSpan Kept = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan GapAfter = TimeSpan.FromSeconds(2.5);

    private readonly Dictionary<string, List<DiskActivityPoint>> _drives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public void Add(string drive, DateTime time, DiskActivitySample sample)
    {
        lock (_lock)
        {
            if (!_drives.TryGetValue(drive, out var points))
            {
                _drives[drive] = points = [];
            }

            points.Add(new DiskActivityPoint(time, sample));
            var old = points.FindIndex(p => p.Time >= time - Kept);
            if (old > 0)
            {
                points.RemoveRange(0, old);
            }
        }
    }

    public IReadOnlyList<DiskActivityPoint> Since(string drive, DateTime from)
    {
        lock (_lock)
        {
            return _drives.TryGetValue(drive, out var points) ? [.. points.Where(p => p.Time >= from)] : [];
        }
    }

    public IReadOnlyList<double?> LastSeconds(string drive, DateTime now, int seconds)
    {
        var result = new double?[seconds];
        foreach (var point in Since(drive, now - TimeSpan.FromSeconds(seconds)))
        {
            var slot = seconds - 1 - (int)Math.Floor((now - point.Time).TotalSeconds);
            if (slot >= 0 && slot < seconds)
            {
                result[slot] = point.Sample.Busy;
            }
        }

        return result;
    }
}
