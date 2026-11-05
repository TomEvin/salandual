// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/GitStates.cs

using Avalonia.Threading;
using Salandual.Core.Git;

namespace Salandual.App.Panels;

public sealed class GitStates : IDisposable
{
    private const int MaxWatched = 4;
    private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(600);

    private readonly IGitReader _reader;
    private readonly Func<bool> _networkDrives;
    private readonly Lazy<IReadOnlyList<string>> _clouds;
    private readonly Dictionary<string, string?> _tops = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Repo> _repos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _localRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private long _asked;
    private bool _changePosted;
    private bool _disposed;

    public GitStates(IGitReader reader, Func<bool> networkDrives, Func<IReadOnlyList<string>> cloudFolders)
    {
        _reader = reader;
        _networkDrives = networkDrives;
        _clouds = new Lazy<IReadOnlyList<string>>(() => [.. cloudFolders().Select(Path.TrimEndingDirectorySeparator)]);
    }

    public event EventHandler? Changed;

    internal int Reads { get; private set; }

    internal bool IsReading
    {
        get
        {
            lock (_lock)
            {
                return _repos.Values.Any(r => r.Reading);
            }
        }
    }

    public GitStatus? StatusOf(string folder)
    {
        if (_disposed || TopFor(folder) is not { } top)
        {
            return null;
        }

        Repo repo;
        lock (_lock)
        {
            if (!_repos.TryGetValue(top, out repo!))
            {
                repo = new Repo(top);
                _repos[top] = repo;
            }

            repo.Asked = ++_asked;
            if (repo.Status is { } known)
            {
                return known;
            }
        }

        Read(repo);
        return null;
    }

    public string? TopFor(string folder)
    {
        lock (_lock)
        {
            if (_tops.TryGetValue(folder, out var known))
            {
                return known;
            }
        }

        var top = Looked(folder) ? GitRepositories.TopOf(folder) : null;
        if (top is not null && InCloud(top))
        {
            top = null;
        }

        lock (_lock)
        {
            if (_tops.Count > 2000)
            {
                _tops.Clear();
            }

            _tops[folder] = top;
        }

        return top;
    }

    public void Relisted(string folder)
    {
        lock (_lock)
        {
            _tops.Remove(folder);
        }

        if (TopFor(folder) is not { } top)
        {
            return;
        }

        Repo? repo;
        lock (_lock)
        {
            if (!_repos.TryGetValue(top, out repo) || repo.Watcher is not null)
            {
                return;
            }
        }

        Read(repo);
    }

    public void Reread(string top)
    {
        Repo? repo;
        lock (_lock)
        {
            _repos.TryGetValue(top, out repo);
        }

        if (repo is not null)
        {
            Read(repo);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            foreach (var repo in _repos.Values)
            {
                repo.Stop();
            }

            _repos.Clear();
        }
    }

    private void Read(Repo repo)
    {
        lock (_lock)
        {
            if (repo.Reading)
            {
                repo.Again = true;
                return;
            }

            repo.Reading = true;
        }

        _ = Task.Run(() =>
        {
            GitStatus? status = null;
            try
            {
                status = _reader.Status(repo.Top);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or DllNotFoundException or TypeInitializationException or BadImageFormatException)
            {
                Core.Diagnostics.AppLog.Warn("Git: a status couldn't be read: " + ex.GetType().Name);
            }

            bool again;
            lock (_lock)
            {
                repo.Status = status;
                repo.Reading = false;
                again = repo.Again;
                repo.Again = false;
                Reads++;
                if (status is not null && !_disposed)
                {
                    Watch(repo);
                }
            }

            PostChanged();
            if (again)
            {
                Read(repo);
            }
        });
    }

    private void Watch(Repo repo)
    {
        if (repo.Watcher is not null)
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(repo.Top)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            FileSystemEventHandler changed = (_, e) => Touched(repo, e.FullPath);
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Renamed += (_, e) => Touched(repo, e.FullPath);
            watcher.Error += (_, _) => Touched(repo, null);
            watcher.EnableRaisingEvents = true;
            repo.Watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        foreach (var old in _repos.Values.Where(r => r.Watcher is not null).OrderByDescending(r => r.Asked).Skip(MaxWatched).ToList())
        {
            old.Stop();
            _repos.Remove(old.Top);
        }
    }

    private void Touched(Repo repo, string? path)
    {
        if (path is not null && !Matters(repo.Top, path))
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed || repo.Watcher is null)
            {
                return;
            }

            repo.Settle ??= new Timer(_ => Read(repo));
            repo.Settle.Change(_settle, Timeout.InfiniteTimeSpan);
        }
    }

    internal static bool Matters(string top, string path)
    {
        var git = Path.Join(top, ".git");
        if (!path.StartsWith(git, StringComparison.OrdinalIgnoreCase) || (path.Length > git.Length && path[git.Length] != '\\'))
        {
            return true;
        }

        var inside = path.Length > git.Length ? path[(git.Length + 1)..] : "";
        return inside is "index" or "HEAD" or "packed-refs" or "MERGE_HEAD" || inside.StartsWith(@"refs\", StringComparison.OrdinalIgnoreCase);
    }

    public bool MayHold(string folder) => Looked(folder) && !InCloud(folder);

    private bool Looked(string folder)
    {
        if (folder.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return _networkDrives() && Path.IsPathFullyQualified(folder);
        }

        if (folder.Length < 3 || !char.IsAsciiLetter(folder[0]) || folder[1] != ':' || folder[2] != '\\')
        {
            return false;
        }

        var root = folder[..3];
        bool local;
        lock (_lock)
        {
            if (_localRoots.TryGetValue(root, out var known))
            {
                return known || _networkDrives();
            }
        }

        try
        {
            local = new DriveInfo(root).DriveType is DriveType.Fixed or DriveType.Removable;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            local = false;
        }

        lock (_lock)
        {
            _localRoots[root] = local;
        }

        return local || _networkDrives();
    }

    private bool InCloud(string top)
        => _clouds.Value.Any(cloud => top.Equals(cloud, StringComparison.OrdinalIgnoreCase)
                                      || (top.StartsWith(cloud, StringComparison.OrdinalIgnoreCase) && top.Length > cloud.Length && top[cloud.Length] == '\\'));

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

    private sealed class Repo(string top)
    {
        public string Top { get; } = top;

        public GitStatus? Status { get; set; }

        public bool Reading { get; set; }

        public bool Again { get; set; }

        public long Asked { get; set; }

        public FileSystemWatcher? Watcher { get; set; }

        public Timer? Settle { get; set; }

        public void Stop()
        {
            Watcher?.Dispose();
            Watcher = null;
            Settle?.Dispose();
            Settle = null;
        }
    }
}
