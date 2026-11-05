// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/ShellIcons.cs

using System.Runtime.InteropServices;

namespace Salandual.Platform.Windows.Shell;

public sealed record IconPixels(int Width, int Height, byte[] Bgra)
{
    public TimeSpan? Length { get; init; }
}

[Flags]
public enum ThumbnailRequest
{
    None = 0,

    CachedOnly = 1,

    Length = 2,

    ByName = 4,
}

public sealed partial class ShellIconLoader : IDisposable
{
    private readonly LinkedList<(string Key, string Path, int Size, ThumbnailRequest How)> _queue = new();
    private readonly bool _thumbnails;
    private readonly int _maxWaiting;
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _signal = new(0);
    private Thread? _thread;
    private volatile bool _disposed;

    public ShellIconLoader(bool thumbnails = false, int maxWaiting = 0)
    {
        _thumbnails = thumbnails;
        _maxWaiting = maxWaiting;
    }

    public event Action<string, int, IconPixels?>? Loaded;

    public void Request(string key, string path, int sizePx, ThumbnailRequest how = ThumbnailRequest.None)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            if (!_pending.Add(PendingKey(key, sizePx)))
            {
                return;
            }

            _queue.AddFirst((key, path, sizePx, how));
            if (_maxWaiting > 0 && _queue.Count > _maxWaiting)
            {
                var oldest = _queue.Last!.Value;
                _queue.RemoveLast();
                _pending.Remove(PendingKey(oldest.Key, oldest.Size));
            }

            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = _thumbnails ? "Shell thumbnails" : "Shell icons", Priority = ThreadPriority.BelowNormal };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }

        _signal.Release();
    }

    private static string PendingKey(string key, int size) => string.Concat(key, "|", size.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void Run()
    {
        while (!_disposed)
        {
            _signal.Wait();
            (string Key, string Path, int Size, ThumbnailRequest How) item;
            lock (_lock)
            {
                if (_queue.Count == 0)
                {
                    continue;
                }

                item = _queue.First!.Value;
                _queue.RemoveFirst();
            }

            IconPixels? icon = null;
            try
            {
                icon = _thumbnails ? LoadThumbnail(item.Path, item.Size, item.How)
                    : (item.How & ThumbnailRequest.ByName) != 0 ? LoadByName(item.Path, item.Size)
                    : Load(item.Path, item.Size);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
            }

            lock (_lock)
            {
                _pending.Remove(PendingKey(item.Key, item.Size));
            }

            if (!_disposed)
            {
                Loaded?.Invoke(item.Key, item.Size, icon);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _signal.Release();
    }

    private static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private const int SIIGBF_ICONONLY = 0x4;
    private const int SIIGBF_THUMBNAILONLY = 0x8;
    private const int SIIGBF_INCACHEONLY = 0x10;

    private static readonly Guid IID_IShellItem2 = new("7e9fb0d3-919f-4307-ab2e-9b1860310c93");

    private static readonly PropertyKey PKEY_Media_Duration = new(new Guid("64440490-4c8b-11d1-8b70-080036b11a03"), 3);

    public static IconPixels? Load(string path, int sizePx) => GetImage(path, sizePx, SIIGBF_ICONONLY);

    public static unsafe IconPixels? LoadByName(string path, int sizePx)
    {
        var list = SHSimpleIDListFromPath(path);
        if (list == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var iid = IID_IShellItemImageFactory;
            void* factory;
            return SHCreateItemFromIDList(list, &iid, &factory) < 0 || factory is null ? null : FromFactory(factory, sizePx, SIIGBF_ICONONLY);
        }
        finally
        {
            ILFree(list);
        }
    }

    public static Task<IconPixels?> LoadThumbnailAsync(string path, int sizePx)
        => ShellThread.Run(() =>
        {
            try
            {
                return Opaque(Upright(GetImage(FileSystem.WindowsPath.FromExtended(path), sizePx, SIIGBF_THUMBNAILONLY)));
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                return null;
            }
        });

    public static IconPixels? LoadThumbnail(string path, int sizePx, ThumbnailRequest how = ThumbnailRequest.None)
    {
        var cachedOnly = (how & ThumbnailRequest.CachedOnly) != 0;
        var windowsPath = FileSystem.WindowsPath.FromExtended(path);
        var image = Opaque(Upright(GetImage(windowsPath, sizePx, SIIGBF_THUMBNAILONLY | (cachedOnly ? SIIGBF_INCACHEONLY : 0))));
        return image is not null && !cachedOnly && (how & ThumbnailRequest.Length) != 0 && MediaLength(windowsPath) is { } length ? image with { Length = length } : image;
    }

    private static unsafe TimeSpan? MediaLength(string path)
    {
        var iid = IID_IShellItem2;
        void* item;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, &iid, &item) < 0 || item is null)
        {
            return null;
        }

        try
        {
            var getUInt64 = (delegate* unmanaged[Stdcall]<void*, PropertyKey*, ulong*, int>)(*(void***)item)[19];
            var key = PKEY_Media_Duration;
            ulong ticks;
            return getUInt64(item, &key, &ticks) >= 0 && ticks is > 0 and < long.MaxValue ? TimeSpan.FromTicks((long)ticks) : null;
        }
        finally
        {
            var release = (delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)item)[2];
            release(item);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

    private static IconPixels? Upright(IconPixels? image)
    {
        if (image is null)
        {
            return null;
        }

        var stride = image.Width * 4;
        var turned = new byte[image.Bgra.Length];
        for (var y = 0; y < image.Height; y++)
        {
            image.Bgra.AsSpan(y * stride, stride).CopyTo(turned.AsSpan((image.Height - 1 - y) * stride));
        }

        return image with { Bgra = turned };
    }

    private static IconPixels? Opaque(IconPixels? image)
    {
        if (image is null)
        {
            return null;
        }

        for (var i = 3; i < image.Bgra.Length; i += 4)
        {
            if (image.Bgra[i] != 0)
            {
                return image;
            }
        }

        for (var i = 3; i < image.Bgra.Length; i += 4)
        {
            image.Bgra[i] = 255;
        }

        return image;
    }

    private static unsafe IconPixels? GetImage(string path, int sizePx, int flags)
    {
        var iid = IID_IShellItemImageFactory;
        void* factory;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, &iid, &factory) < 0 || factory is null)
        {
            return null;
        }

        return FromFactory(factory, sizePx, flags);
    }

    private static unsafe IconPixels? FromFactory(void* factory, int sizePx, int flags)
    {
        try
        {
            var getImage = (delegate* unmanaged[Stdcall]<void*, Size, int, IntPtr*, int>)(*(void***)factory)[3];
            IntPtr hbm;
            if (getImage(factory, new Size(sizePx, sizePx), flags, &hbm) < 0 || hbm == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return ToPixels(hbm);
            }
            finally
            {
                DeleteObject(hbm);
            }
        }
        finally
        {
            var release = (delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)factory)[2];
            release(factory);
        }
    }

    private static unsafe IconPixels? ToPixels(IntPtr hbm)
    {
        DibSection dib;
        if (GetObjectW(hbm, sizeof(DibSection), &dib) == 0 || dib.Bits == null || dib.BitsPixel != 32)
        {
            return null;
        }

        var w = dib.Width;
        var h = Math.Abs(dib.Height);
        var stride = dib.WidthBytes;
        var bottomUp = dib.HeaderHeight > 0;
        var result = new byte[w * h * 4];
        var src = (byte*)dib.Bits;
        for (var y = 0; y < h; y++)
        {
            var row = bottomUp ? h - 1 - y : y;
            new ReadOnlySpan<byte>(src + (row * stride), w * 4).CopyTo(result.AsSpan(y * w * 4));
        }

        return new IconPixels(w, h, result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Size(int Cx, int Cy);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DibSection
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public void* Bits;
        public uint HeaderSize;
        public int HeaderWidth;
        public int HeaderHeight;
        public ushort HeaderPlanes;
        public ushort HeaderBitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
        public fixed uint Bitfields[3];
        public IntPtr Section;
        public uint Offset;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHCreateItemFromParsingName(string path, IntPtr bindContext, Guid* riid, void** item);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SHSimpleIDListFromPath(string path);

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHCreateItemFromIDList(IntPtr list, Guid* riid, void** item);

    [LibraryImport("shell32.dll")]
    private static partial void ILFree(IntPtr list);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetObjectW(IntPtr handle, int size, void* buffer);
}
