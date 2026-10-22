// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/SettingsService.cs

namespace Salandual.Core.Settings;

public sealed class SettingsChangedEventArgs(AppSettings oldSettings, AppSettings newSettings) : EventArgs
{
    public AppSettings Old { get; } = oldSettings;
    public AppSettings New { get; } = newSettings;
}

public sealed class SettingsService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly Timer _saveTimer;
    private readonly TimeSpan _debounce;
    private readonly Lock _lock = new();
    private readonly Lock _saveLock = new();
    private bool _dirty;

    public SettingsService(SettingsStore store, SettingsLoadResult loaded, TimeSpan? debounce = null)
    {
        _store = store;
        Current = loaded.Settings;
        LoadResult = loaded;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
        _saveTimer = new Timer(_ => SaveNow());
        if (loaded.MigratedFrom is not null && loaded.CanSave)
        {
            _dirty = true;
            _saveTimer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public AppSettings Current { get; private set; }
    public SettingsLoadResult LoadResult { get; }
    public string FilePath => _store.FilePath;

    public Exception? LastSaveError { get; private set; }

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public event EventHandler? SaveFailed;

    public static SettingsService Open(string dataDirectory, TimeSpan? debounce = null)
    {
        var store = SettingsStore.InDirectory(dataDirectory);
        return new SettingsService(store, store.Load(), debounce);
    }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        AppSettings old, next;
        lock (_lock)
        {
            old = Current;
            next = change(old).Normalize();
            if (next == old)
            {
                return;
            }

            Current = next;
            _dirty = true;
            if (LoadResult.CanSave)
            {
                _saveTimer.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
        }

        Changed?.Invoke(this, new SettingsChangedEventArgs(old, next));
    }

    public void SaveSoon()
    {
        lock (_lock)
        {
            _dirty = true;
            if (LoadResult.CanSave)
            {
                _saveTimer.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Flush()
    {
        _saveTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        SaveNow();
    }

    public void Dispose()
    {
        Flush();
        _saveTimer.Dispose();
    }

    private void SaveNow()
    {
        lock (_saveLock)
        {
            SaveLocked();
        }
    }

    private void SaveLocked()
    {
        AppSettings snapshot;
        lock (_lock)
        {
            if (!_dirty || !LoadResult.CanSave)
            {
                return;
            }

            snapshot = Current;
            _dirty = false;
        }

        try
        {
            _store.Save(snapshot);
            LastSaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var first = LastSaveError is null;
            LastSaveError = ex;
            lock (_lock)
            {
                _dirty = true;
            }

            if (first)
            {
                SaveFailed?.Invoke(this, EventArgs.Empty);
            }
        }

    }
}
