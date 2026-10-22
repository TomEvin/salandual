// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/MemoryFiles.cs

using System.Text;
using Salandual.Core.Vfs;

namespace Salandual.Core.Tests;

internal sealed class MemoryFiles : IFileSystemProvider
{
    private readonly Dictionary<string, List<FileEntry>> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _content = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Opened { get; } = [];

    public MemoryFiles Folder(string path, FileAttributes extra = 0)
    {
        if (!_dirs.ContainsKey(path))
        {
            _dirs[path] = [];
        }

        if (GetParent(path) is { } parent)
        {
            Folder(parent);
            var name = GetName(path);
            if (!_dirs[parent].Any(e => e.Name == name))
            {
                _dirs[parent].Add(new FileEntry(name, 0, new DateTime(2026, 1, 1).Ticks, FileAttributes.Directory | extra));
            }
        }

        return this;
    }

    public MemoryFiles File(string path, string text, DateTime? modified = null, FileAttributes attributes = FileAttributes.Archive)
        => File(path, Encoding.UTF8.GetBytes(text), modified, attributes);

    public MemoryFiles File(string path, byte[] bytes, DateTime? modified = null, FileAttributes attributes = FileAttributes.Archive)
    {
        var parent = GetParent(path)!;
        Folder(parent);
        _dirs[parent].Add(new FileEntry(GetName(path), bytes.Length, (modified ?? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks, attributes));
        _content[path] = bytes;
        return this;
    }

    public Stream Open(string path)
    {
        Opened.Add(path);
        return new MemoryStream(_content[path], writable: false);
    }

    public ProviderCapabilities Capabilities => ProviderCapabilities.List | ProviderCapabilities.Read;

    public Task<DirectoryListing> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Unreadable.Contains(path))
        {
            throw new UnauthorizedAccessException($"Access to {path} is denied.");
        }

        return Task.FromResult(new DirectoryListing(path, [.. _dirs[path]], TimeSpan.Zero));
    }

    public string? GetParent(string path)
    {
        var i = path.TrimEnd('\\').LastIndexOf('\\');
        return i < 0 || path.Length <= 3 ? null : i == 2 ? path[..3] : path[..i];
    }

    public string? GetRoot(string path) => path.Length >= 3 ? path[..3] : null;

    public string GetName(string path) => path.TrimEnd('\\')[(path.TrimEnd('\\').LastIndexOf('\\') + 1)..];

    public string Combine(string directory, string name) => directory.EndsWith('\\') ? directory + name : directory + "\\" + name;

    public IDisposable Watch(string path, Action<DirectoryChange> onChange, Action<Exception> onError) => throw new NotSupportedException();
}
