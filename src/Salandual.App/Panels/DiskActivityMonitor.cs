// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/DiskActivityMonitor.cs

using Avalonia.Threading;
using Salandual.Core;

namespace Salandual.App.Panels;

internal sealed class DiskActivityMonitor(IDiskActivitySource source) : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan FirstInterval = TimeSpan.FromMilliseconds(300);

    private const int MissesKept = 2;

    private readonly Dictionary<string, int> _misses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hurried = new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _stop = new();
    private IReadOnlyDictionary<string, DiskActivitySample> _values = new Dictionary<string, DiskActivitySample>();
    private string[] _wanted = [];
    private bool _started;
    private bool _paused;
    private bool _behind;
    private bool _stale;

    public event EventHandler? Updated;

    public event EventHandler? Sampled;

    public Func<bool>? IsMinimized { get; set; }

    public Func<bool>? InFront { get; set; }

    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    public DiskActivityHistory History { get; } = new();

    public IReadOnlyDictionary<string, DiskActivitySample> Values => _values;

    public IReadOnlyList<string> Wanted => _wanted;

    public bool IsPaused => _paused;

    public bool IsBehind => _behind;

    public DiskActivitySample? Get(string? drive)
        => drive is not null && _values.TryGetValue(drive, out var sample) ? sample : null;

    public void SetWanted(IEnumerable<string> drives)
    {
        var wanted = drives.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (wanted.SequenceEqual(_wanted, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _wanted = wanted;
        _hurried.IntersectWith(wanted);
        if (_values.Keys.Any(d => !wanted.Contains(d, StringComparer.OrdinalIgnoreCase)))
        {
            Apply(_values.Where(v => wanted.Contains(v.Key, StringComparer.OrdinalIgnoreCase)).ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase));
        }
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        _paused = paused;
        _misses.Clear();
        if (paused && _values.Count > 0)
        {
            Apply(new Dictionary<string, DiskActivitySample>());
        }
    }

    public void SetBehind(bool behind)
    {
        if (_behind == behind)
        {
            return;
        }

        _behind = behind;
        if (!behind)
        {
            if (_stale)
            {
                _stale = false;
                Updated?.Invoke(this, EventArgs.Empty);
            }

            Sampled?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _ = RunAsync(_stop.Token);
    }

    private async Task RunAsync(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var interval = Interval;
                CheckInFront();
                if (!_paused && _wanted.Length > 0)
                {
                    var drives = _wanted;
                    var values = await Task.Run(() => source.Sample(drives), stop);
                    if (!stop.IsCancellationRequested && !_paused && ReferenceEquals(drives, _wanted))
                    {
                        ApplySample(values);
                        if (drives.Count(d => !values.ContainsKey(d) && _hurried.Add(d)) > 0)
                        {
                            interval = FirstInterval;
                        }
                    }
                }

                await Task.Delay(interval, stop);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal void SampleNow() => ApplySample(source.Sample(_wanted));

    internal void CheckInFront()
    {
        if (IsMinimized is { } minimized)
        {
            SetPaused(minimized());
        }

        if (InFront is { } inFront)
        {
            SetBehind(!inFront());
        }
    }

    private void ApplySample(IReadOnlyDictionary<string, DiskActivitySample> values)
    {
        var now = Now();
        foreach (var (drive, sample) in values)
        {
            History.Add(drive, now, sample);
        }

        var merged = new Dictionary<string, DiskActivitySample>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var drive in _wanted)
        {
            if (values.ContainsKey(drive))
            {
                _misses.Remove(drive);
            }
            else if (_values.TryGetValue(drive, out var last) && _misses.GetValueOrDefault(drive) < MissesKept)
            {
                _misses[drive] = _misses.GetValueOrDefault(drive) + 1;
                merged[drive] = last;
            }
            else
            {
                _misses.Remove(drive);
            }
        }

        Apply(merged);
        if (!_behind)
        {
            Sampled?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Apply(IReadOnlyDictionary<string, DiskActivitySample> values)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (values.Count == _values.Count && values.All(v => _values.TryGetValue(v.Key, out var old) && old == v.Value))
        {
            return;
        }

        _values = values;
        if (_behind)
        {
            _stale = true;
            return;
        }

        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _stop.Cancel();
        source.Dispose();
    }
}
