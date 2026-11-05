// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/DiskActivityCounters.cs

using System.Runtime.InteropServices;
using Salandual.Core;

namespace Salandual.Platform.Windows.FileSystem;

public sealed partial class DiskActivityCounters : IDiskActivitySource
{
    private const uint FormatDouble = 0x200, NoCap100 = 0x8000;
    private const uint ValidData = 0, NewData = 1;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (nint Idle, nint Read, nint Write)> _counters = new(StringComparer.OrdinalIgnoreCase);
    private nint _query;
    private bool _failed;

    public IReadOnlyDictionary<string, DiskActivitySample> Sample(IReadOnlyCollection<string> drives)
    {
        var result = new Dictionary<string, DiskActivitySample>(StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            if (_failed || !Open())
            {
                return result;
            }

            foreach (var gone in _counters.Keys.Where(d => !drives.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                Remove(gone);
            }

            foreach (var drive in drives.Where(d => DiskActivity.DriveOf(d) == d && !_counters.ContainsKey(d)))
            {
                Add(drive);
            }

            if (_counters.Count == 0 || PdhCollectQueryData(_query) != 0)
            {
                return result;
            }

            foreach (var (drive, c) in _counters)
            {
                if (Read(c.Idle) is { } idle && Read(c.Read) is { } read && Read(c.Write) is { } write)
                {
                    result[drive] = new DiskActivitySample(DiskActivity.BusyFromIdle(idle), Math.Max(0, read), Math.Max(0, write));
                }
            }
        }

        return result;
    }

    private bool Open()
    {
        if (_query != 0)
        {
            return true;
        }

        if (PdhOpenQueryW(null, 0, out _query) != 0)
        {
            _query = 0;
            _failed = true;
            return false;
        }

        return true;
    }

    private void Add(string drive)
    {
        nint Counter(string name) => PdhAddEnglishCounterW(_query, $@"\LogicalDisk({drive})\{name}", 0, out var counter) == 0 ? counter : 0;

        var idle = Counter("% Idle Time");
        var read = Counter("Disk Read Bytes/sec");
        var write = Counter("Disk Write Bytes/sec");
        if (idle == 0 || read == 0 || write == 0)
        {
            foreach (var c in new[] { idle, read, write }.Where(c => c != 0))
            {
                _ = PdhRemoveCounter(c);
            }

            return;
        }

        _counters[drive] = (idle, read, write);
    }

    private void Remove(string drive)
    {
        var (idle, read, write) = _counters[drive];
        _ = PdhRemoveCounter(idle);
        _ = PdhRemoveCounter(read);
        _ = PdhRemoveCounter(write);
        _counters.Remove(drive);
    }

    private static double? Read(nint counter)
        => PdhGetFormattedCounterValue(counter, FormatDouble | NoCap100, 0, out var value) == 0 && value.Status is ValidData or NewData
            ? value.Value
            : null;

    public void Dispose()
    {
        lock (_lock)
        {
            if (_query != 0)
            {
                _ = PdhCloseQuery(_query);
                _query = 0;
            }

            _counters.Clear();
            _failed = true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValue
    {
        public uint Status;
        public double Value;
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhRemoveCounter(nint counter);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhGetFormattedCounterValue(nint counter, uint format, nint type, out CounterValue value);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhCloseQuery(nint query);
}
