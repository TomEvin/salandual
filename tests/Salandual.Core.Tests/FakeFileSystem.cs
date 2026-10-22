// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/FakeFileSystem.cs

using Salandual.Core.Vfs;

namespace Salandual.Core.Tests;

internal sealed class FakeFileSystem : IFileSystemProvider
{
    private readonly Dictionary<string, List<FileEntry>> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Path, Action<DirectoryChange> Change, Action<Exception> Error)> _watches = [];

    public ProviderCapabilities Capabilities => ProviderCapabilities.List | ProviderCapabilities.Watch;

    public int ListCalls { get; private set; }

    public FakeFileSystem Dir(string path, params string[] children)
    {
        var list = _dirs.TryGetValue(path, out var l) ? l : _dirs[path] = [];
        foreach (var child in children)
        {
            var hidden = child.StartsWith('~');
            var c = hidden ? child[1..] : child;
            var isDir = c.EndsWith('/');
            var parts = c.TrimEnd('/').Split(':');
            var attributes = (isDir ? FileAttributes.Directory : FileAttributes.Archive) | (hidden ? FileAttributes.Hidden : 0);
            list.Add(new FileEntry(parts[0], parts.Length > 1 ? long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0, 0, attributes));
            if (isDir)
            {
                var sub = Combine(path, parts[0]);
                if (!_dirs.ContainsKey(sub))
                {
                    _dirs[sub] = [];
                }
            }
        }

        return this;
    }

    public void Remove(string dir, string name) => _dirs[dir].RemoveAll(e => e.Name == name);

    public void DeleteDir(string path)
    {
        foreach (var key in _dirs.Keys.Where(k => k.StartsWith(path, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _dirs.Remove(key);
        }
    }

    public void FireChange(string path)
    {
        foreach (var w in _watches.Where(w => w.Path == path).ToList())
        {
            w.Change(new DirectoryChange(path, 1, false));
        }
    }

    public void FireError(string path)
    {
        foreach (var w in _watches.Where(w => w.Path == path).ToList())
        {
            w.Error(new IOException("gone"));
        }
    }

    public Task<DirectoryListing> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        ListCalls++;
        if (!_dirs.TryGetValue(path, out var list))
        {
            throw new DirectoryNotFoundException($"Could not find {path}.");
        }

        return Task.FromResult(new DirectoryListing(path, [.. list], TimeSpan.FromMilliseconds(1)));
    }

    public string? GetParent(string path)
    {
        var trimmed = path.TrimEnd('\\');
        var cut = trimmed.LastIndexOf('\\');
        return cut < 0 ? null : cut == 2 ? trimmed[..3] : trimmed[..cut];
    }

    public string? GetRoot(string path) => path.Length >= 3 ? path[..3] : null;

    public string GetName(string path) => path.TrimEnd('\\')[(path.TrimEnd('\\').LastIndexOf('\\') + 1)..];

    public string Combine(string directory, string name) => directory.EndsWith('\\') ? directory + name : directory + "\\" + name;

    public IDisposable Watch(string path, Action<DirectoryChange> onChange, Action<Exception> onError)
    {
        var w = (path, onChange, onError);
        _watches.Add(w);
        return new Unwatch(() => _watches.Remove(w));
    }

    private sealed class Unwatch(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
