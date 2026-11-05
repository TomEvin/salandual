// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/ThumbnailCache.cs

using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Panels;

public sealed record Thumbnail(Bitmap Image, TimeSpan? Length);

public sealed class ThumbnailCache : IDisposable
{
    private const long MaxBytes = 192L * 1024 * 1024;

    private const int MaxNone = 20000;

    private readonly Dictionary<string, LinkedListNode<(string Key, Thumbnail Thumb)>> _ready = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string Key, Thumbnail Thumb)> _recent = new();
    private readonly HashSet<string> _none = new(StringComparer.OrdinalIgnoreCase);
    private readonly ShellIconLoader _loader = new(thumbnails: true, maxWaiting: 400);
    private long _bytes;
    private bool _changePosted;
    private bool _started;

    public ThumbnailCache()
    {
        _loader.Loaded += (key, _, pixels) => Dispatcher.UIThread.Post(() => Store(key, pixels));
    }

    public event EventHandler? Changed;

    public ThumbnailFiles Files { get; set; } = ThumbnailFiles.All;

    public void Start() => _started = true;

    public static bool IsOnlineOnly(in FileEntry entry)
        => ((int)entry.Attributes & (OfflineAttribute | RecallOnOpenAttribute | RecallOnDataAccessAttribute)) != 0;

    private const int OfflineAttribute = 0x1000, RecallOnOpenAttribute = 0x40000, RecallOnDataAccessAttribute = 0x400000;

    public bool Wants(in FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            return false;
        }

        return Files == ThumbnailFiles.All || PerceivedTypes.Of(entry.Extension.ToString()) is PerceivedKind.Picture or PerceivedKind.Video;
    }

    public Thumbnail? Get(string folder, in FileEntry entry, int sizePx, out bool none)
    {
        none = false;
        if (!Wants(entry) || !Path.IsPathFullyQualified(folder))
        {
            none = true;
            return null;
        }

        var path = Path.Join(folder, entry.Name);
        var key = string.Concat(path, "|", sizePx.ToString(System.Globalization.CultureInfo.InvariantCulture), "|", entry.LastWriteUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (_ready.TryGetValue(key, out var node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Thumb;
        }

        if (_none.Contains(key))
        {
            none = true;
            return null;
        }

        if (_started)
        {
            var how = IsOnlineOnly(entry) ? ThumbnailRequest.CachedOnly
                : PerceivedTypes.Of(entry.Extension.ToString()) == PerceivedKind.Video ? ThumbnailRequest.Length
                : ThumbnailRequest.None;
            _loader.Request(key, path, sizePx, how);
        }

        return null;
    }

    private void Store(string key, IconPixels? pixels)
    {
        if (pixels is null)
        {
            if (_none.Count >= MaxNone)
            {
                _none.Clear();
            }

            _none.Add(key);
        }
        else if (!_ready.ContainsKey(key))
        {
            var thumb = new Thumbnail(IconCache.ToBitmap(pixels), pixels.Length);
            _ready[key] = _recent.AddFirst((key, thumb));
            _bytes += pixels.Bgra.Length;
            while (_bytes > MaxBytes && _recent.Last is { } oldest && oldest != _recent.First)
            {
                _recent.RemoveLast();
                _ready.Remove(oldest.Value.Key);
                _bytes -= (long)oldest.Value.Thumb.Image.PixelSize.Width * oldest.Value.Thumb.Image.PixelSize.Height * 4;
                oldest.Value.Thumb.Image.Dispose();
            }
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

    public void Dispose() => _loader.Dispose();
}
