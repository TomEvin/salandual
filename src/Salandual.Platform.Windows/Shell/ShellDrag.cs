// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/ShellDrag.cs

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Salandual.Core.Operations;

namespace Salandual.Platform.Windows.Shell;

[Flags]
public enum DragKeys : uint
{
    None = 0,
    LeftButton = 0x1,
    RightButton = 0x2,
    Shift = 0x4,
    Control = 0x8,
    MiddleButton = 0x10,
    Alt = 0x20,
}

public static unsafe partial class ShellDrag
{
    private const uint DshAllowDropDescriptionText = 0x1;
    private const int DragDropSDrop = 0x00040100;
    private static readonly Guid ClsidDragDropHelper = new("4657278A-411B-11D2-839A-00C04FD918D0");
    private static readonly Guid IidDragSourceHelper2 = new("83E07D0D-0C5F-4163-BF1A-60B274051E40");

    public static bool IsDragging { get; private set; }

    public static DropEffects DragOut(nint owner, string folder, IReadOnlyList<string> names, bool plainCopies, DragPicture? picture = null)
    {
        var data = ShellItems.UIObject(folder, names, ShellItems.IidDataObject, owner);
        try
        {
            if (plainCopies)
            {
                ShellData.SetPreferredEffect(data, DropEffects.Copy);
            }

            SetDragImage(data, picture);
            IsDragging = true;
            uint effect = 0;
            var hr = SHDoDragDrop(owner, data, null, (uint)(DropEffects.Copy | DropEffects.Move | DropEffects.Link), &effect);
            return hr == DragDropSDrop ? (DropEffects)effect : DropEffects.None;
        }
        finally
        {
            IsDragging = false;
            ShellItems.Release(data);
        }
    }

    internal static void* CreateDataObject(string folder, IReadOnlyList<string> names, bool plainCopies)
    {
        var data = ShellItems.UIObject(folder, names, ShellItems.IidDataObject, 0);
        if (plainCopies)
        {
            ShellData.SetPreferredEffect(data, DropEffects.Copy);
        }

        return data;
    }

    internal static void SetDragImage(void* data, DragPicture? picture)
    {
        var clsid = ClsidDragDropHelper;
        var iid = IidDragSourceHelper2;
        void* helper;
        if (CoCreateInstance(&clsid, null, 1, &iid, &helper) < 0 || helper is null)
        {
            return;
        }

        try
        {
            ((delegate* unmanaged[Stdcall]<void*, uint, int>)(*(void***)helper)[5])(helper, DshAllowDropDescriptionText);
            if (picture is not null && FromBitmap(helper, data, picture))
            {
                return;
            }

            Point32 pt;
            GetCursorPos(&pt);
            ((delegate* unmanaged[Stdcall]<void*, nint, Point32*, void*, int>)(*(void***)helper)[4])(helper, 0, &pt, data);
        }
        finally
        {
            ShellItems.Release(helper);
        }
    }

    private static bool FromBitmap(void* helper, void* data, DragPicture picture)
    {
        if (picture.Width <= 0 || picture.Height <= 0 || picture.Bgra.Length != picture.Width * picture.Height * 4)
        {
            return false;
        }

        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = picture.Width,
            Height = -picture.Height,
            Planes = 1,
            BitCount = 32,
        };
        void* bits;
        var bitmap = CreateDIBSection(0, &header, 0, &bits, 0, 0);
        if (bitmap == 0)
        {
            return false;
        }

        picture.Bgra.CopyTo(new Span<byte>(bits, picture.Bgra.Length));
        var image = new ShDragImage
        {
            Width = picture.Width,
            Height = picture.Height,
            Offset = new Point32 { X = picture.PointerX, Y = picture.PointerY },
            Bitmap = bitmap,
            ColorKey = 0xFFFFFFFF,
        };
        if (((delegate* unmanaged[Stdcall]<void*, ShDragImage*, void*, int>)(*(void***)helper)[3])(helper, &image, data) >= 0)
        {
            return true;
        }

        DeleteObject(bitmap);
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShDragImage
    {
        public int Width;
        public int Height;
        public Point32 Offset;
        public nint Bitmap;
        public uint ColorKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, void** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("shell32.dll")]
    private static partial int SHDoDragDrop(nint owner, void* data, void* source, uint okEffects, uint* effect);

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** result);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(Point32* point);
}

public sealed record DragPicture(int Width, int Height, byte[] Bgra, int PointerX, int PointerY);

public sealed record DropFeedback(DropEffects Effect, DropNote? Note = null)
{
    public static DropFeedback Nothing { get; } = new(DropEffects.None);
}

public interface IDropHandler
{
    DropFeedback Enter(DropData data, DragKeys keys, int x, int y, DropEffects allowed);

    DropFeedback Over(DragKeys keys, int x, int y, DropEffects allowed);

    void Leave();

    DropEffects Drop(DragKeys keys, int x, int y, DropEffects allowed);
}

public sealed unsafe partial class ShellDropTarget : IDisposable
{
    private static readonly Guid ClsidDragDropHelper = new("4657278A-411B-11D2-839A-00C04FD918D0");
    private static readonly Guid IidDropTargetHelper = new("4657278B-411B-11D2-839A-00C04FD918D0");
    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IidDropTarget = new("00000122-0000-0000-C000-000000000046");
    private static readonly void** _vtable = CreateVtable();

    private readonly nint _hwnd;
    private readonly IDropHandler _handler;
    private readonly bool _registered;
    private Native* _native;
    private GCHandle _self;
    private void* _helper;
    private void* _data;
    private DropNote? _note;

    private ShellDropTarget(nint hwnd, IDropHandler handler, bool register)
    {
        _hwnd = hwnd;
        _handler = handler;
        _self = GCHandle.Alloc(this);
        _native = (Native*)NativeMemory.AllocZeroed((nuint)sizeof(Native));
        _native->Vtable = _vtable;
        _native->Self = GCHandle.ToIntPtr(_self);
        var clsid = ClsidDragDropHelper;
        var iid = IidDropTargetHelper;
        void* helper;
        _helper = ShellDrag.CoCreateInstance(&clsid, null, 1, &iid, &helper) >= 0 ? helper : null;
        if (register)
        {
            _ = RevokeDragDrop(hwnd);
            ShellItems.Check(RegisterDragDrop(hwnd, _native));
            _registered = true;
        }
    }

    public static ShellDropTarget Register(nint hwnd, IDropHandler handler) => new(hwnd, handler, register: true);

    internal static ShellDropTarget CreateUnregistered(IDropHandler handler) => new(0, handler, register: false);

    internal void* Pointer => _native;

    public void Dispose()
    {
        if (_native is null)
        {
            return;
        }

        if (_registered)
        {
            _ = RevokeDragDrop(_hwnd);
        }

        ReleaseData();
        ShellItems.Release(_helper);
        _helper = null;
        NativeMemory.Free(_native);
        _native = null;
        _self.Free();
    }

    private int Enter(void* data, uint keys, ShellDrag.Point32 pt, uint* effect)
    {
        var allowed = (DropEffects)(*effect);
        *effect = 0;
        try
        {
            ReleaseData();
            _data = data;
            ShellItems.AddRef(data);
            Apply(_handler.Enter(ShellData.Read(data), (DragKeys)keys, pt.X, pt.Y, allowed), allowed, effect);
        }
        catch (Exception)
        {
            *effect = 0;
        }

        if (_helper is not null)
        {
            ((delegate* unmanaged[Stdcall]<void*, nint, void*, ShellDrag.Point32*, uint, int>)(*(void***)_helper)[3])(_helper, _hwnd, data, &pt, *effect);
        }

        return 0;
    }

    private int Over(uint keys, ShellDrag.Point32 pt, uint* effect)
    {
        var allowed = (DropEffects)(*effect);
        *effect = 0;
        try
        {
            Apply(_handler.Over((DragKeys)keys, pt.X, pt.Y, allowed), allowed, effect);
        }
        catch (Exception)
        {
            *effect = 0;
        }

        if (_helper is not null)
        {
            ((delegate* unmanaged[Stdcall]<void*, ShellDrag.Point32*, uint, int>)(*(void***)_helper)[5])(_helper, &pt, *effect);
        }

        return 0;
    }

    private int Leave()
    {
        try
        {
            _handler.Leave();
        }
        catch (Exception)
        {
        }

        ClearNote();
        if (_helper is not null)
        {
            ((delegate* unmanaged[Stdcall]<void*, int>)(*(void***)_helper)[4])(_helper);
        }

        ReleaseData();
        return 0;
    }

    private int Drop(void* data, uint keys, ShellDrag.Point32 pt, uint* effect)
    {
        var allowed = (DropEffects)(*effect);
        var logical = DropEffects.None;
        try
        {
            logical = _handler.Drop((DragKeys)keys, pt.X, pt.Y, allowed) & allowed;
        }
        catch (Exception)
        {
            logical = DropEffects.None;
        }

        var performed = logical == DropEffects.Move ? DropEffects.None : logical;
        *effect = (uint)performed;
        ClearNote();
        ShellData.SetPerformed(data, performed, logical);
        if (_helper is not null)
        {
            ((delegate* unmanaged[Stdcall]<void*, void*, ShellDrag.Point32*, uint, int>)(*(void***)_helper)[6])(_helper, data, &pt, *effect);
        }

        ReleaseData();
        return 0;
    }

    private void Apply(DropFeedback feedback, DropEffects allowed, uint* effect)
    {
        *effect = (uint)(feedback.Effect & allowed);
        var note = feedback.Note ?? DropNote.Clear;
        if (_data is not null && note != _note)
        {
            ShellData.SetNote(_data, note);
            _note = note;
        }
    }

    private void ClearNote()
    {
        if (_data is not null && _note is not null && _note != DropNote.Clear)
        {
            ShellData.SetNote(_data, DropNote.Clear);
        }

        _note = null;
    }

    private void ReleaseData()
    {
        if (_data is not null)
        {
            ShellItems.Release(_data);
            _data = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Native
    {
        public void** Vtable;
        public nint Self;
    }

    private static ShellDropTarget Of(Native* native) => (ShellDropTarget)GCHandle.FromIntPtr(native->Self).Target!;

    private static void** CreateVtable()
    {
        var v = (void**)NativeMemory.Alloc((nuint)(7 * sizeof(void*)));
        v[0] = (delegate* unmanaged[Stdcall]<Native*, Guid*, void**, int>)&QueryInterface;
        v[1] = (delegate* unmanaged[Stdcall]<Native*, uint>)&AddRef;
        v[2] = (delegate* unmanaged[Stdcall]<Native*, uint>)&Release;
        v[3] = (delegate* unmanaged[Stdcall]<Native*, void*, uint, ShellDrag.Point32, uint*, int>)&DragEnter;
        v[4] = (delegate* unmanaged[Stdcall]<Native*, uint, ShellDrag.Point32, uint*, int>)&DragOver;
        v[5] = (delegate* unmanaged[Stdcall]<Native*, int>)&DragLeave;
        v[6] = (delegate* unmanaged[Stdcall]<Native*, void*, uint, ShellDrag.Point32, uint*, int>)&DropCall;
        return v;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(Native* self, Guid* iid, void** result)
    {
        if (*iid == IidUnknown || *iid == IidDropTarget)
        {
            *result = self;
            return 0;
        }

        *result = null;
        return unchecked((int)0x80004002);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(Native* self) => 1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(Native* self) => 1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DragEnter(Native* self, void* data, uint keys, ShellDrag.Point32 pt, uint* effect) => Of(self).Enter(data, keys, pt, effect);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DragOver(Native* self, uint keys, ShellDrag.Point32 pt, uint* effect) => Of(self).Over(keys, pt, effect);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DragLeave(Native* self) => Of(self).Leave();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DropCall(Native* self, void* data, uint keys, ShellDrag.Point32 pt, uint* effect) => Of(self).Drop(data, keys, pt, effect);

    [LibraryImport("ole32.dll")]
    private static partial int RegisterDragDrop(nint hwnd, void* target);

    [LibraryImport("ole32.dll")]
    private static partial int RevokeDragDrop(nint hwnd);
}

public static unsafe partial class ShellLinks
{
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IidShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IidPersistFile = new("0000010B-0000-0000-C000-000000000046");

    public static Task<string> CreateAsync(string target, string folder) => ShellThread.Run(() => Create(target, folder));

    private static string Create(string target, string folder)
    {
        var name = stackalloc char[260];
        int mustCopy;
        if (!SHGetNewLinkInfoW(target, folder, name, &mustCopy, 0))
        {
            throw new IOException(target);
        }

        var path = new string(name);
        var clsid = ClsidShellLink;
        var iid = IidShellLinkW;
        void* link = null;
        void* file = null;
        try
        {
            ShellItems.Check(ShellDrag.CoCreateInstance(&clsid, null, 1, &iid, &link));
            fixed (char* t = target)
            {
                ShellItems.Check(((delegate* unmanaged[Stdcall]<void*, char*, int>)(*(void***)link)[20])(link, t));
            }

            if (Path.GetDirectoryName(target) is { Length: > 0 } dir && File.Exists(target))
            {
                fixed (char* d = dir)
                {
                    ((delegate* unmanaged[Stdcall]<void*, char*, int>)(*(void***)link)[9])(link, d);
                }
            }

            var iidFile = IidPersistFile;
            ShellItems.Check(((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)(*(void***)link)[0])(link, &iidFile, &file));
            fixed (char* p = path)
            {
                ShellItems.Check(((delegate* unmanaged[Stdcall]<void*, char*, int, int>)(*(void***)file)[6])(file, p, 1));
            }

            return path;
        }
        finally
        {
            ShellItems.Release(file);
            ShellItems.Release(link);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHGetNewLinkInfoW(string linkTo, string folder, char* name, int* mustCopy, uint flags);
}
