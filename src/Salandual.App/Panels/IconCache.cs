// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/IconCache.cs

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Salandual.Archives;
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Panels;

public sealed class IconCache : IDisposable
{
    private const int MaxOwnIcons = 2000;
    private const string FolderKey = "/";
    private const int MaxSizes = 4;

    private static readonly HashSet<string> _perFile = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "lnk", "ico", "url", "cur", "ani", "scr", "cpl", "appref-ms",
    };

    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _perFileLookup = _perFile.GetAlternateLookup<ReadOnlySpan<char>>();

    private readonly Dictionary<string, bool> _bareArchives = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<int, SizeStore> _sizes = [];
    private readonly ShellIconLoader _loader = new();
    private bool _changePosted;
    private bool _started;

    public IconCache()
    {
        _loader.Loaded += (key, size, pixels) => Dispatcher.UIThread.Post(() => Store(key, size, pixels));
    }

    public void Start()
    {
        if (!_started)
        {
            _started = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;

    public Bitmap? Get(string folder, in FileEntry entry, int sizePx)
    {
        if (Remote.ServerAddress.IsServerPath(folder))
        {
            folder = ServerItems;
        }

        if (!_sizes.TryGetValue(sizePx, out var store))
        {
            if (_sizes.Count >= MaxSizes)
            {
                _sizes.Clear();
            }

            _sizes[sizePx] = store = new SizeStore();
        }

        if (!HasOwnIcon(entry))
        {
            var ext = entry.IsDirectory ? FolderKey.AsSpan() : entry.Extension;
            if (store.SharedLookup.TryGetValue(ext, out var icon))
            {
                return icon;
            }

            if (!entry.IsDirectory && IsBareArchive(entry.Name))
            {
                Request("s:" + ext.ToString(), folder, "archive.zip", sizePx, ThumbnailRequest.ByName);
                return null;
            }

            Request("s:" + ext.ToString(), folder, entry.Name, sizePx);
            return null;
        }

        var path = Path.Join(folder, entry.Name);
        if (store.Own.TryGetValue(path, out var own))
        {
            return own;
        }

        Request("p:" + path, folder, entry.Name, sizePx);
        return null;
    }

    public Bitmap? GetAt(string path, in FileEntry entry, int sizePx)
    {
        if (!_sizes.TryGetValue(sizePx, out var store))
        {
            return Get(Path.GetDirectoryName(path) ?? path, entry, sizePx);
        }

        if (store.Own.TryGetValue(path, out var own))
        {
            return own;
        }

        if (_started && Path.IsPathFullyQualified(path))
        {
            _loader.Request("p:" + path, path, sizePx, ThumbnailRequest.None);
        }

        return null;
    }

    private static readonly string ServerItems = Path.Combine(Path.GetTempPath(), "salandual-server-items");

    internal static bool HasOwnIcon(in FileEntry entry)
        => entry.IsDirectory
            ? (entry.Attributes & (FileAttributes.ReadOnly | FileAttributes.System)) != 0
            : _perFileLookup.Contains(entry.Extension);

    private void Request(string key, string folder, string name, int sizePx, ThumbnailRequest how = ThumbnailRequest.None)
    {
        if (_started && Path.IsPathFullyQualified(folder))
        {
            _loader.Request(key, Path.Join(folder, name), sizePx, how);
        }
    }

    private bool IsBareArchive(string name)
    {
        if (!_started || !ArchivePaths.IsArchiveName(name))
        {
            return false;
        }

        var ext = Path.GetExtension(name);
        if (!_bareArchives.TryGetValue(ext, out var bare))
        {
            _bareArchives[ext] = bare = FileDetailsReader.IsUnknownType(ext);
        }

        return bare;
    }

    private void Store(string key, int size, IconPixels? pixels)
    {
        if (!_sizes.TryGetValue(size, out var store))
        {
            return;
        }

        var shared = key.StartsWith("s:", StringComparison.Ordinal);
        if (pixels is null && shared)
        {
            return;
        }

        var icon = pixels is null ? null : ToBitmap(pixels);
        if (shared)
        {
            store.Shared[key[2..]] = icon;
        }
        else
        {
            if (store.Own.Count >= MaxOwnIcons)
            {
                store.Own.Clear();
            }

            store.Own[key[2..]] = icon;
        }

        if (!_changePosted)
        {
            _changePosted = true;
            Dispatcher.UIThread.Post(() =>
            {
                _changePosted = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Background);
        }
    }

    internal static WriteableBitmap ToBitmap(IconPixels icon)
    {
        var bitmap = new WriteableBitmap(new PixelSize(icon.Width, icon.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bitmap.Lock();
        for (var y = 0; y < icon.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(icon.Bgra, y * icon.Width * 4, fb.Address + (y * fb.RowBytes), icon.Width * 4);
        }

        return bitmap;
    }

    public void Dispose() => _loader.Dispose();

    private sealed class SizeStore
    {
        public SizeStore() => SharedLookup = Shared.GetAlternateLookup<ReadOnlySpan<char>>();

        public Dictionary<string, Bitmap?> Shared { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Bitmap?>.AlternateLookup<ReadOnlySpan<char>> SharedLookup { get; }

        public Dictionary<string, Bitmap?> Own { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
