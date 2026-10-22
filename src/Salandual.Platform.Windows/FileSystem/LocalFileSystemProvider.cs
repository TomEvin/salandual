// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/LocalFileSystemProvider.cs

using System.Diagnostics;
using System.IO.Enumeration;
using Salandual.Core.Vfs;

namespace Salandual.Platform.Windows.FileSystem;

public sealed class LocalFileSystemProvider : IFileSystemProvider
{
    private const int EnumerationBufferSize = 64 * 1024;

    private static readonly EnumerationOptions _options = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        BufferSize = EnumerationBufferSize,
    };

    public ProviderCapabilities Capabilities => ProviderCapabilities.List | ProviderCapabilities.Watch;

    public Task<DirectoryListing> ListAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run(() => List(path, cancellationToken), cancellationToken);

    public DirectoryListing List(string path, CancellationToken cancellationToken = default)
    {
        var display = WindowsPath.FromExtended(path);
        var extended = WindowsPath.ToExtended(display);
        var sw = Stopwatch.StartNew();
        var count = 0;
        var enumerable = new FileSystemEnumerable<FileEntry>(extended, (ref FileSystemEntry e) =>
        {
            if ((++count & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new FileEntry(e.FileName.ToString(), e.IsDirectory ? 0 : e.Length, e.LastWriteTimeUtc.UtcTicks, e.Attributes);
        }, _options);

        var entries = enumerable.ToArray();
        return new DirectoryListing(display, entries, sw.Elapsed);
    }

    public string? GetParent(string path) => WindowsPath.GetParent(path);

    public string? GetRoot(string path) => WindowsPath.GetRoot(path);

    public string GetName(string path) => WindowsPath.GetName(path);

    public string Combine(string directory, string name) => WindowsPath.Join(directory, name);

    public IDisposable Watch(string path, Action<DirectoryChange> onChange, Action<Exception> onError)
        => new LocalDirectoryWatcher(WindowsPath.FromExtended(path), onChange, onError);
}

internal sealed class LocalDirectoryWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly ChangeCoalescer _coalescer;

    public LocalDirectoryWatcher(string path, Action<DirectoryChange> onChange, Action<Exception> onError)
    {
        _coalescer = new ChangeCoalescer(ChangeCoalescer.DefaultQuiet, ChangeCoalescer.DefaultMaxDelay,
            (count, overflowed) => onChange(new DirectoryChange(path, count, overflowed)));

        _watcher = new FileSystemWatcher(WindowsPath.ToExtended(path))
        {
            IncludeSubdirectories = false,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size
                | NotifyFilters.LastWrite | NotifyFilters.Attributes | NotifyFilters.CreationTime,
        };

        FileSystemEventHandler changed = (_, _) => _coalescer.Add();
        _watcher.Created += changed;
        _watcher.Deleted += changed;
        _watcher.Changed += changed;
        _watcher.Renamed += (_, _) => _coalescer.Add();
        _watcher.Error += (_, e) =>
        {
            var ex = e.GetException();
            if (ex is InternalBufferOverflowException)
            {
                _coalescer.Add(overflowed: true);
            }
            else
            {
                onError(ex);
            }
        };
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _coalescer.Dispose();
    }
}
