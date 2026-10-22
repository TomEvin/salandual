// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/ManagedShellExtensions.cs

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Salandual.Platform.Windows.Shell;

internal static unsafe partial class ManagedShellExtensions
{
    private static readonly string[] _commonKeys = ["*", "AllFilesystemObjects", "Directory", "Directory\\Background", "Folder", "Drive"];

    private static readonly HashSet<Guid> _blocked = [];
    private static readonly HashSet<string> _scanned = new(StringComparer.OrdinalIgnoreCase);
    private static void** _factory;

    public static void BlockCommon()
    {
        foreach (var key in _commonKeys)
        {
            BlockHandlersOf(key);
        }
    }

    public static void BlockFor(IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            var ext = Path.GetExtension(name);
            if (ext.Length < 2)
            {
                continue;
            }

            BlockHandlersOf(ext);
            BlockHandlersOf("SystemFileAssociations\\" + ext);
            using var extKey = Registry.ClassesRoot.OpenSubKey(ext);
            if (extKey?.GetValue("") is string progId && progId.Length > 0)
            {
                BlockHandlersOf(progId);
            }

            if (extKey?.GetValue("PerceivedType") is string perceived && perceived.Length > 0)
            {
                BlockHandlersOf("SystemFileAssociations\\" + perceived);
            }
        }
    }

    private static void BlockHandlersOf(string classKey)
    {
        if (!_scanned.Add(classKey))
        {
            return;
        }

        try
        {
            using var handlers = Registry.ClassesRoot.OpenSubKey(classKey + "\\shellex\\ContextMenuHandlers");
            if (handlers is null)
            {
                return;
            }

            foreach (var name in handlers.GetSubKeyNames())
            {
                using var handler = handlers.OpenSubKey(name);
                var value = handler?.GetValue("") as string;
                if (Guid.TryParse(string.IsNullOrEmpty(value) ? name : value, out var clsid) && IsManaged(clsid))
                {
                    Block(clsid);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }

    internal static bool IsManaged(Guid clsid)
    {
        using var server = Registry.ClassesRoot.OpenSubKey("CLSID\\" + clsid.ToString("B") + "\\InprocServer32");
        return server?.GetValue("") is string dll
               && Path.GetFileName(dll.Trim('"')).Equals("mscoree.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static void Block(Guid clsid)
    {
        if (!_blocked.Add(clsid))
        {
            return;
        }

        uint cookie;
        _ = CoRegisterClassObject(&clsid, Factory(), ClsctxInprocServer, RegclsMultipleUse, &cookie);
    }

    private static void* Factory()
    {
        if (_factory is null)
        {
            var vtable = (void**)NativeMemory.Alloc(5, (nuint)sizeof(void*));
            vtable[0] = (delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)&QueryInterface;
            vtable[1] = (delegate* unmanaged[Stdcall]<void*, uint>)&AddRef;
            vtable[2] = (delegate* unmanaged[Stdcall]<void*, uint>)&Release;
            vtable[3] = (delegate* unmanaged[Stdcall]<void*, void*, Guid*, void**, int>)&CreateInstance;
            vtable[4] = (delegate* unmanaged[Stdcall]<void*, int, int>)&LockServer;
            _factory = (void**)NativeMemory.Alloc(1, (nuint)sizeof(void*));
            _factory[0] = vtable;
        }

        return _factory;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(void* self, Guid* iid, void** result)
    {
        if (*iid == IidUnknown || *iid == IidClassFactory)
        {
            *result = self;
            return 0;
        }

        *result = null;
        return ENoInterface;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(void* self) => 2;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(void* self) => 1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CreateInstance(void* self, void* outer, Guid* iid, void** result)
    {
        *result = null;
        return ClassENotAvailable;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int LockServer(void* self, int locking) => 0;

    private const uint ClsctxInprocServer = 0x1;
    private const uint RegclsMultipleUse = 0x1;
    private const int ENoInterface = unchecked((int)0x80004002);
    private const int ClassENotAvailable = unchecked((int)0x80040111);
    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IidClassFactory = new("00000001-0000-0000-C000-000000000046");

    [LibraryImport("ole32.dll")]
    private static partial int CoRegisterClassObject(Guid* clsid, void* unknown, uint context, uint flags, uint* cookie);
}
