// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Vfs/ChangeCoalescer.cs

namespace Salandual.Core.Vfs;

public sealed class ChangeCoalescer : IDisposable
{
    private readonly Lock _lock = new();
    private readonly Timer _timer;
    private readonly TimeSpan _quiet;
    private readonly TimeSpan _maxDelay;
    private readonly Action<int, bool> _fired;
    private long _firstTicks;
    private int _count;
    private bool _overflowed;
    private bool _disposed;

    public ChangeCoalescer(TimeSpan quiet, TimeSpan maxDelay, Action<int, bool> fired)
    {
        _quiet = quiet;
        _maxDelay = maxDelay;
        _fired = fired;
        _timer = new Timer(_ => Fire());
    }

    public static TimeSpan DefaultQuiet => TimeSpan.FromMilliseconds(100);

    public static TimeSpan DefaultMaxDelay => TimeSpan.FromMilliseconds(500);

    public void Add(bool overflowed = false)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            var now = Environment.TickCount64;
            if (_count == 0)
            {
                _firstTicks = now;
            }

            _count++;
            _overflowed |= overflowed;
            var untilMax = _maxDelay - TimeSpan.FromMilliseconds(now - _firstTicks);
            var due = untilMax < _quiet ? untilMax : _quiet;
            _timer.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        int count;
        bool overflowed;
        lock (_lock)
        {
            if (_disposed || _count == 0)
            {
                return;
            }

            count = _count;
            overflowed = _overflowed;
            _count = 0;
            _overflowed = false;
        }

        _fired(count, overflowed);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }

        _timer.Dispose();
    }
}
