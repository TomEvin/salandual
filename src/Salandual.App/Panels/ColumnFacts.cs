// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/ColumnFacts.cs

using System.IO.Enumeration;
using Avalonia.Threading;
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Panels;

public sealed class ColumnFacts : IDisposable
{
    private const int MaxKept = 50000, MaxWaiting = 400;
    private const int OfflineAttribute = 0x1000, RecallOnOpenAttribute = 0x40000, RecallOnDataAccessAttribute = 0x400000;

    private readonly Dictionary<string, long> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _dosNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string Path, bool Items, bool ShowHidden)> _waiting = new();
    private readonly HashSet<string> _asked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _localRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _work = new(0);
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;
    private bool _changePosted;

    public event EventHandler? Changed;

    public long? Items(string folder, in FileEntry entry, bool showHidden, out bool counting)
    {
        counting = false;
        if (!entry.IsDirectory || !IsCountable(folder, entry))
        {
            return null;
        }

        var path = Path.Join(folder, entry.Name);
        var key = (showHidden ? "+" : "-") + path;
        lock (_lock)
        {
            if (_items.TryGetValue(key, out var n))
            {
                return n;
            }
        }

        counting = true;
        Ask(path, items: true, showHidden, key);
        return null;
    }

    public string? DosName(string folder, in FileEntry entry)
    {
        if (!IsOnLocalOrNetworkDrive(folder))
        {
            return "";
        }

        var path = Path.Join(folder, entry.Name);
        lock (_lock)
        {
            if (_dosNames.TryGetValue(path, out var name))
            {
                return name;
            }
        }

        Ask(path, items: false, showHidden: false, "?" + path);
        return null;
    }

    public void Forget(string folder)
    {
        var prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        lock (_lock)
        {
            foreach (var key in _items.Keys.Where(k => k.AsSpan(1).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _items.Remove(key);
            }

            foreach (var key in _dosNames.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _dosNames.Remove(key);
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _work.Release();
    }

    private bool IsCountable(string folder, in FileEntry entry)
        => ((int)entry.Attributes & ((int)FileAttributes.ReparsePoint | OfflineAttribute | RecallOnOpenAttribute | RecallOnDataAccessAttribute)) == 0
           && IsLocal(folder);

    private bool IsLocal(string folder) => Root(folder) is { } root && LocalRoot(root);

    private static bool IsOnLocalOrNetworkDrive(string folder) => Root(folder) is not null || folder.StartsWith(@"\\", StringComparison.Ordinal);

    private static string? Root(string folder)
        => folder.Length >= 3 && char.IsAsciiLetter(folder[0]) && folder[1] == ':' && folder[2] == '\\' ? folder[..3] : null;

    private bool LocalRoot(string root)
    {
        lock (_lock)
        {
            if (_localRoots.TryGetValue(root, out var local))
            {
                return local;
            }
        }

        bool found;
        try
        {
            found = new DriveInfo(root).DriveType is DriveType.Fixed or DriveType.Removable;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            found = false;
        }

        lock (_lock)
        {
            _localRoots[root] = found;
        }

        return found;
    }

    private void Ask(string path, bool items, bool showHidden, string key)
    {
        lock (_lock)
        {
            if (!_asked.Add(key))
            {
                var node = _waiting.First;
                while (node is not null && !(node.Value.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && node.Value.Items == items))
                {
                    node = node.Next;
                }

                if (node is not null && node != _waiting.First)
                {
                    _waiting.Remove(node);
                    _waiting.AddFirst(node);
                }

                return;
            }

            _waiting.AddFirst((path, items, showHidden));
            while (_waiting.Count > MaxWaiting)
            {
                var last = _waiting.Last!.Value;
                _asked.Remove(KeyOf(last));
                _waiting.RemoveLast();
            }

            _thread ??= StartThread();
        }

        _work.Release();
    }

    private static string KeyOf((string Path, bool Items, bool ShowHidden) w) => w.Items ? (w.ShowHidden ? "+" : "-") + w.Path : "?" + w.Path;

    private Thread StartThread()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Column facts", Priority = ThreadPriority.BelowNormal };
        thread.Start();
        return thread;
    }

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            _work.Wait();
            (string Path, bool Items, bool ShowHidden) next;
            lock (_lock)
            {
                if (_waiting.First is not { } first)
                {
                    continue;
                }

                next = first.Value;
                _waiting.RemoveFirst();
            }

            var key = KeyOf(next);
            if (next.Items)
            {
                var n = Count(next.Path, next.ShowHidden);
                lock (_lock)
                {
                    if (_items.Count >= MaxKept)
                    {
                        _items.Clear();
                    }

                    _items[key] = n;
                    _asked.Remove(key);
                }
            }
            else
            {
                var name = Path.GetFileName(ShortNames.Of(next.Path));
                lock (_lock)
                {
                    if (_dosNames.Count >= MaxKept)
                    {
                        _dosNames.Clear();
                    }

                    _dosNames[next.Path] = name.Equals(Path.GetFileName(next.Path), StringComparison.Ordinal) ? "" : name;
                    _asked.Remove(key);
                }
            }

            PostChanged();
        }
    }

    private static long Count(string path, bool showHidden)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
            };
            var count = 0L;
            var all = new FileSystemEnumerable<bool>(WindowsPath.ToExtended(path), (ref FileSystemEntry _) => true, options);
            foreach (var _ in all)
            {
                count++;
            }

            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return -1;
        }
    }

    private void PostChanged()
    {
        lock (_lock)
        {
            if (_changePosted)
            {
                return;
            }

            _changePosted = true;
        }

        Dispatcher.UIThread.Post(() =>
        {
            lock (_lock)
            {
                _changePosted = false;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Background);
    }
}
