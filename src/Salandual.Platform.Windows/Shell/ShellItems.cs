// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/ShellItems.cs

using System.Runtime.InteropServices;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Shell;

internal static unsafe partial class ShellItems
{
    public static readonly Guid IidContextMenu = new("000214E4-0000-0000-C000-000000000046");
    public static readonly Guid IidDataObject = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Guid IidShellFolder = new("000214E6-0000-0000-C000-000000000046");

    public static void* UIObject(string folder, IReadOnlyList<string> names, Guid iid, nint owner)
    {
        var absolute = new nint[names.Count];
        var children = new nint[names.Count];
        void* parent = null;
        try
        {
            for (var i = 0; i < names.Count; i++)
            {
                nint pidl;
                Check(SHParseDisplayName(WindowsPath.Join(folder, names[i]), null, &pidl, 0, null));
                absolute[i] = pidl;
                if (i == 0)
                {
                    var iidFolder = IidShellFolder;
                    nint child;
                    Check(SHBindToParent(pidl, &iidFolder, &parent, &child));
                    children[i] = child;
                }
                else
                {
                    children[i] = ILFindLastID(pidl);
                }
            }

            void* result;
            fixed (nint* c = children)
            {
                Check(((delegate* unmanaged[Stdcall]<void*, nint, uint, nint*, Guid*, uint*, void**, int>)(*(void***)parent)[10])(parent, owner, (uint)children.Length, c, &iid, null, &result));
            }

            return result;
        }
        finally
        {
            Release(parent);
            foreach (var pidl in absolute)
            {
                if (pidl != 0)
                {
                    CoTaskMemFree(pidl);
                }
            }
        }
    }

    public static void* DataObject(IReadOnlyList<string> paths)
    {
        var absolute = new nint[paths.Count];
        try
        {
            for (var i = 0; i < paths.Count; i++)
            {
                nint pidl;
                Check(SHParseDisplayName(WindowsPath.FromExtended(paths[i]), null, &pidl, 0, null));
                absolute[i] = pidl;
            }

            void* result;
            var iid = IidDataObject;
            fixed (nint* items = absolute)
            {
                Check(SHCreateDataObject(0, (uint)absolute.Length, items, null, &iid, &result));
            }

            return result;
        }
        finally
        {
            foreach (var pidl in absolute)
            {
                if (pidl != 0)
                {
                    CoTaskMemFree(pidl);
                }
            }
        }
    }

    public static void Release(void* com)
    {
        if (com is not null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)com)[2])(com);
        }
    }

    public static void AddRef(void* com) => ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)com)[1])(com);

    public static void Check(int hr)
    {
        if (hr < 0)
        {
            throw Marshal.GetExceptionForHR(hr) ?? new IOException("0x" + hr.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, void* bindContext, nint* pidl, uint sfgaoIn, uint* sfgaoOut);

    [LibraryImport("shell32.dll")]
    private static partial int SHBindToParent(nint pidl, Guid* iid, void** result, nint* last);

    [LibraryImport("shell32.dll")]
    private static partial nint ILFindLastID(nint pidl);

    [LibraryImport("shell32.dll")]
    private static partial int SHCreateDataObject(nint folder, uint count, nint* items, void* inner, Guid* iid, void** result);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint p);
}
