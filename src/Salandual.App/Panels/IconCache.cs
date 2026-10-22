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
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Panels;

public sealed class IconCache : IDisposable
{
    private const int MaxOwnIcons = 2000;
    private const string FolderKey = "/";

    private static readonly HashSet<string> _perFile = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "lnk", "ico", "url", "cur", "ani", "scr", "cpl", "appref-ms",
    };

    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _perFileLookup = _perFile.GetAlternateLookup<ReadOnlySpan<char>>();

    private readonly Dictionary<string, Bitmap?> _shared = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap?>.AlternateLookup<ReadOnlySpan<char>> _sharedLookup;

    private readonly Dictionary<string, Bitmap?> _own = new(StringComparer.OrdinalIgnoreCase);
    private readonly ShellIconLoader _loader = new();
    private int _size;
    private bool _changePosted;
    private bool _started;

    public IconCache()
    {
        _sharedLookup = _shared.GetAlternateLookup<ReadOnlySpan<char>>();
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
        if (sizePx != _size)
        {
            _shared.Clear();
            _own.Clear();
            _size = sizePx;
        }

        if (!HasOwnIcon(entry))
        {
            var ext = entry.IsDirectory ? FolderKey.AsSpan() : entry.Extension;
            if (_sharedLookup.TryGetValue(ext, out var icon))
            {
                return icon;
            }

            Request("s:" + ext.ToString(), folder, entry.Name, sizePx);
            return null;
        }

        var path = Path.Join(folder, entry.Name);
        if (_own.TryGetValue(path, out var own))
        {
            return own;
        }

        Request("p:" + path, folder, entry.Name, sizePx);
        return null;
    }

    internal static bool HasOwnIcon(in FileEntry entry)
        => entry.IsDirectory
            ? (entry.Attributes & (FileAttributes.ReadOnly | FileAttributes.System)) != 0
            : _perFileLookup.Contains(entry.Extension);

    private void Request(string key, string folder, string name, int sizePx)
    {
        if (_started && Path.IsPathFullyQualified(folder))
        {
            _loader.Request(key, Path.Join(folder, name), sizePx);
        }
    }

    private void Store(string key, int size, IconPixels? pixels)
    {
        if (size != _size)
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
            _shared[key[2..]] = icon;
        }
        else
        {
            if (_own.Count >= MaxOwnIcons)
            {
                _own.Clear();
            }

            _own[key[2..]] = icon;
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
}
