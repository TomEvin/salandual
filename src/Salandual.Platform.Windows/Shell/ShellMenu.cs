// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/ShellMenu.cs

using System.Runtime.InteropServices;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Shell;

public sealed record ShellMenuItem(
    int Id,
    string Text,
    string? Verb,
    bool IsSeparator,
    bool IsEnabled,
    bool IsChecked,
    bool IsDefault,
    IconPixels? Icon,
    IReadOnlyList<ShellMenuItem> Children)
{
    public bool HasChildren => Children.Count > 0;

    public string PlainText => ShellMenu.StripAccessKey(Text);
}

public sealed unsafe partial class ShellMenu : IDisposable
{
    private const int FirstId = 1;
    private const int LastId = 0x7FFF;
    private void* _menu;
    private void* _menu2;
    private void* _menu3;
    private nint _hmenu;
    private bool _disposed;

    private ShellMenu(string folder, IReadOnlyList<string> names, bool extended)
    {
        Folder = folder;
        Names = names;
        Extended = extended;
    }

    public string Folder { get; }

    public IReadOnlyList<string> Names { get; }

    public bool Extended { get; }

    public IReadOnlyList<ShellMenuItem> Items { get; private set; } = [];

    public bool IsFor(string folder, IReadOnlyList<string> names, bool extended)
        => !_disposed && extended == Extended && string.Equals(folder, Folder, StringComparison.OrdinalIgnoreCase)
           && names.Count == Names.Count && names.Zip(Names).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));

    public static Task<ShellMenu> BuildAsync(string folder, IReadOnlyList<string> names, bool extended)
    {
        var copy = names.ToArray();
        return ShellThread.Run(() =>
        {
            var menu = new ShellMenu(folder, copy, extended);
            try
            {
                menu.Build();
                return menu;
            }
            catch
            {
                menu.ReleaseAll();
                throw;
            }
        });
    }

    private void Build()
    {
        ManagedShellExtensions.BlockFor(Names);
        _menu = Names.Count == 0 ? BackgroundMenu(Folder) : ItemsMenu(Folder, Names);
        _hmenu = CreatePopupMenu();
        var flags = CmfNormal | CmfExplore | (Extended ? CmfExtendedVerbs : 0);
        Check(((delegate* unmanaged[Stdcall]<void*, nint, uint, uint, uint, uint, int>)(*(void***)_menu)[3])(_menu, _hmenu, 0, FirstId, LastId, flags));
        void* p;
        var iid2 = IidContextMenu2;
        if (((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)(*(void***)_menu)[0])(_menu, &iid2, &p) >= 0)
        {
            _menu2 = p;
        }

        var iid3 = IidContextMenu3;
        if (((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)(*(void***)_menu)[0])(_menu, &iid3, &p) >= 0)
        {
            _menu3 = p;
        }

        Items = ReadMenu(_hmenu, 0);
    }

    public Task InvokeAsync(int id, nint owner, int screenX = 0, int screenY = 0)
        => ShellThread.Run(() => Invoke(id - FirstId, null, owner, screenX, screenY));

    public Task InvokeVerbAsync(string verb, nint owner)
        => ShellThread.Run(() => Invoke(0, verb, owner, 0, 0));

    public Task<string?> ShowNativeAsync(nint owner, int screenX, int screenY)
        => ShellThread.Run(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ShellThread.MenuMessages = HandleMenuMessage;
            try
            {
                var host = ShellThread.Window;
                SetForegroundWindow(host);
                var id = TrackPopupMenuEx(_hmenu, TpmReturnCmd | TpmRightButton, screenX, screenY, host, null);
                ShellThread.PostMessageW(host, 0, 0, 0);
                if (id <= 0)
                {
                    return null;
                }

                var verb = Verb(id - FirstId);
                Invoke(id - FirstId, null, owner, screenX, screenY);
                return verb ?? "#" + (id - FirstId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            finally
            {
                ShellThread.MenuMessages = null;
            }
        });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ShellThread.IsCurrent)
        {
            ReleaseAll();
        }
        else
        {
            _ = ShellThread.Run(ReleaseAll);
        }
    }

    public static string StripAccessKey(string text)
    {
        var tab = text.IndexOf('\t');
        if (tab >= 0)
        {
            text = text[..tab];
        }

        return text.Replace("&&", "\u0001", StringComparison.Ordinal).Replace("&", "", StringComparison.Ordinal).Replace('\u0001', '&').Trim();
    }

    private IReadOnlyList<ShellMenuItem> ReadMenu(nint hmenu, int depth)
    {
        var count = GetMenuItemCount(hmenu);
        var items = new List<ShellMenuItem>(Math.Max(0, count));
        for (var i = 0; i < count; i++)
        {
            var info = new MenuItemInfo { Size = (uint)sizeof(MenuItemInfo), Mask = MiimFType | MiimState | MiimId | MiimSubmenu | MiimString | MiimBitmap };
            if (!GetMenuItemInfoW(hmenu, (uint)i, true, &info))
            {
                continue;
            }

            if ((info.Type & MftSeparator) != 0)
            {
                items.Add(new ShellMenuItem(0, "", null, true, false, false, false, null, []));
                continue;
            }

            var (subMenu, itemId, state, bitmap) = (info.SubMenu, info.Id, info.State, info.BitmapItem);
            var text = "";
            if (info.Cch > 0)
            {
                var buffer = new char[info.Cch + 1];
                fixed (char* b = buffer)
                {
                    info.TypeData = b;
                    info.Cch++;
                    info.Mask = MiimString;
                    if (GetMenuItemInfoW(hmenu, (uint)i, true, &info))
                    {
                        text = new string(b);
                    }
                }
            }

            IReadOnlyList<ShellMenuItem> children = [];
            if (subMenu != 0 && depth < 4)
            {
                SendMenuMessage(ShellThread.WmInitMenuPopup, subMenu, i);
                children = ReadMenu(subMenu, depth + 1);
            }

            var id = subMenu != 0 ? 0 : (int)itemId;
            if (text.Length == 0 && id >= FirstId)
            {
                text = HelpText(id - FirstId) ?? "";
            }

            if (text.Length == 0)
            {
                continue;
            }

            var verb = id >= FirstId ? Verb(id - FirstId) : null;
            items.Add(new ShellMenuItem(
                id, text, verb, false,
                (state & (MfsDisabled | MfsGrayed)) == 0,
                (state & MfsChecked) != 0,
                (state & MfsDefault) != 0,
                MenuBitmap(bitmap),
                children));
        }

        return items;
    }

    private void SendMenuMessage(uint msg, nint wParam, int index)
    {
        try
        {
            if (_menu3 is not null)
            {
                nint result;
                ((delegate* unmanaged[Stdcall]<void*, uint, nint, nint, nint*, int>)(*(void***)_menu3)[7])(_menu3, msg, wParam, index, &result);
            }
            else if (_menu2 is not null)
            {
                ((delegate* unmanaged[Stdcall]<void*, uint, nint, nint, int>)(*(void***)_menu2)[6])(_menu2, msg, wParam, index);
            }
        }
        catch (SEHException)
        {
        }
    }

    private nint? HandleMenuMessage(uint msg, nint wParam, nint lParam)
    {
        if (_menu3 is not null)
        {
            nint result;
            ((delegate* unmanaged[Stdcall]<void*, uint, nint, nint, nint*, int>)(*(void***)_menu3)[7])(_menu3, msg, wParam, lParam, &result);
            return result;
        }

        if (_menu2 is not null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint, nint, nint, int>)(*(void***)_menu2)[6])(_menu2, msg, wParam, lParam);
            return 0;
        }

        return null;
    }

    private string? Verb(int offset) => CommandString(offset, GcsVerbW, 128);

    private string? HelpText(int offset) => CommandString(offset, GcsHelpTextW, 260);

    private string? CommandString(int offset, uint type, int size)
    {
        var buffer = stackalloc char[size];
        buffer[0] = '\0';
        var hr = ((delegate* unmanaged[Stdcall]<void*, nuint, uint, uint*, char*, uint, int>)(*(void***)_menu)[5])(_menu, (nuint)offset, type, null, buffer, (uint)size);
        if (hr < 0)
        {
            return null;
        }

        var s = new string(buffer);
        return s.Length > 0 ? s : null;
    }

    private void Invoke(int offset, string? verb, nint owner, int x, int y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var verbAnsi = verb is null ? 0 : Marshal.StringToHGlobalAnsi(verb);
        var verbWide = verb is null ? 0 : Marshal.StringToHGlobalUni(verb);
        try
        {
            var info = new InvokeCommandInfoEx
            {
                Size = (uint)sizeof(InvokeCommandInfoEx),
                Mask = CmicMaskUnicode | (verb is null ? CmicMaskPtInvoke : 0) | CmicMaskAsyncOk,
                Hwnd = owner,
                Verb = verb is null ? offset : verbAnsi,
                VerbW = verb is null ? offset : verbWide,
                Show = 1,
                X = x,
                Y = y,
            };
            Check(((delegate* unmanaged[Stdcall]<void*, InvokeCommandInfoEx*, int>)(*(void***)_menu)[4])(_menu, &info));
        }
        finally
        {
            if (verbAnsi != 0)
            {
                Marshal.FreeHGlobal(verbAnsi);
                Marshal.FreeHGlobal(verbWide);
            }
        }
    }

    private static IconPixels? MenuBitmap(nint hbitmap)
    {
        if (hbitmap is >= -1 and <= 11)
        {
            return null;
        }

        DibSection dib;
        if (GetObjectW(hbitmap, sizeof(DibSection), &dib) < sizeof(Bitmap) || dib.Bm.Width is <= 0 or > 64 || dib.Bm.Height is <= 0 or > 64)
        {
            return null;
        }

        int w = dib.Bm.Width, h = dib.Bm.Height;
        var pixels = new byte[w * h * 4];
        var header = new BitmapInfoHeader { Size = (uint)sizeof(BitmapInfoHeader), Width = w, Height = -h, Planes = 1, BitCount = 32 };
        var dc = GetDC(0);
        try
        {
            fixed (byte* p = pixels)
            {
                if (GetDIBits(dc, hbitmap, 0, (uint)h, p, &header, 0) == 0)
                {
                    return null;
                }
            }
        }
        finally
        {
            _ = ReleaseDC(0, dc);
        }

        var anyAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                anyAlpha = true;
                break;
            }
        }

        if (!anyAlpha)
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }

        return new IconPixels(w, h, pixels);
    }

    private static void* ItemsMenu(string folder, IReadOnlyList<string> names)
        => ShellItems.UIObject(folder, names, IidContextMenu, ShellThread.Window);

    private static void* BackgroundMenu(string folder)
    {
        nint pidl = 0;
        void* shellFolder = null;
        try
        {
            Check(SHParseDisplayName(folder, null, &pidl, 0, null));
            var iid = IidShellFolder;
            Check(SHBindToObject(null, pidl, null, &iid, &shellFolder));
            void* menu;
            var iidMenu = IidContextMenu;
            Check(((delegate* unmanaged[Stdcall]<void*, nint, Guid*, void**, int>)(*(void***)shellFolder)[8])(shellFolder, ShellThread.Window, &iidMenu, &menu));
            return menu;
        }
        finally
        {
            Release(shellFolder);
            if (pidl != 0)
            {
                CoTaskMemFree(pidl);
            }
        }
    }

    private void ReleaseAll()
    {
        Release(_menu3);
        Release(_menu2);
        Release(_menu);
        _menu3 = _menu2 = _menu = null;
        if (_hmenu != 0)
        {
            DestroyMenu(_hmenu);
            _hmenu = 0;
        }
    }

    private static void Release(void* com)
    {
        if (com is not null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)com)[2])(com);
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0)
        {
            throw Marshal.GetExceptionForHR(hr) ?? new IOException("0x" + hr.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private const uint CmfNormal = 0x0;
    private const uint CmfExplore = 0x4;
    private const uint CmfExtendedVerbs = 0x100;
    private const uint CmicMaskAsyncOk = 0x00100000;
    private const uint CmicMaskUnicode = 0x4000;
    private const uint CmicMaskPtInvoke = 0x20000000;
    private const uint GcsVerbW = 0x4;
    private const uint GcsHelpTextW = 0x5;
    private const uint MiimState = 0x1;
    private const uint MiimId = 0x2;
    private const uint MiimSubmenu = 0x4;
    private const uint MiimString = 0x40;
    private const uint MiimBitmap = 0x80;
    private const uint MiimFType = 0x100;
    private const uint MftSeparator = 0x800;
    private const uint MfsGrayed = 0x1;
    private const uint MfsDisabled = 0x2;
    private const uint MfsChecked = 0x8;
    private const uint MfsDefault = 0x1000;
    private const uint TpmRightButton = 0x2;
    private const uint TpmReturnCmd = 0x100;

    private static readonly Guid IidShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IidContextMenu = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid IidContextMenu2 = new("000214F4-0000-0000-C000-000000000046");
    private static readonly Guid IidContextMenu3 = new("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuItemInfo
    {
        public uint Size;
        public uint Mask;
        public uint Type;
        public uint State;
        public uint Id;
        public nint SubMenu;
        public nint BitmapChecked;
        public nint BitmapUnchecked;
        public nuint ItemData;
        public char* TypeData;
        public uint Cch;
        public nint BitmapItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeCommandInfoEx
    {
        public uint Size;
        public uint Mask;
        public nint Hwnd;
        public nint Verb;
        public nint Parameters;
        public nint Directory;
        public int Show;
        public uint HotKey;
        public nint Icon;
        public nint Title;
        public nint VerbW;
        public nint ParametersW;
        public nint DirectoryW;
        public nint TitleW;
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public void* Bits;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct DibSection
    {
        public Bitmap Bm;
        public BitmapInfoHeader Bmih;
        public fixed uint BitFields[3];
        public nint Section;
        public uint Offset;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, void* bindContext, nint* pidl, uint sfgaoIn, uint* sfgaoOut);

    [LibraryImport("shell32.dll")]
    private static partial int SHBindToObject(void* folder, nint pidl, void* bindContext, Guid* iid, void** result);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint p);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    private static partial int GetMenuItemCount(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMenuItemInfoW(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, MenuItemInfo* info);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, void* tpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint handle, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, void* bits, BitmapInfoHeader* info, uint usage);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);
}
