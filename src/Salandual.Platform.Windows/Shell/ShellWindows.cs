// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/ShellWindows.cs

using System.Runtime.InteropServices;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Shell;

public static partial class ShellWindows
{
    public static Task<int> OpenAsync(nint owner, string path, string? verb = null)
        => ShellThread.Run(() => Execute(owner, WindowsPath.FromExtended(path), verb));

    private static unsafe int Execute(nint owner, string path, string? verb)
    {
        var folder = WindowsPath.GetParent(path);
        fixed (char* file = path)
        fixed (char* dir = folder)
        fixed (char* v = verb)
        {
            var info = new ShellExecuteInfo
            {
                Size = (uint)sizeof(ShellExecuteInfo),
                Mask = SeeMaskFlagNoUi | SeeMaskNoAsync | SeeMaskInvokeIdList,
                Hwnd = owner,
                Verb = v,
                File = file,
                Directory = dir,
                Show = 1,
            };
            return ShellExecuteExW(&info) ? 0 : Marshal.GetLastPInvokeError();
        }
    }

    public static unsafe void OpenWith(nint owner, string path)
    {
        fixed (char* file = WindowsPath.FromExtended(path))
        {
            var info = new OpenAsInfo { File = file, Flags = OaifAllowRegistration | OaifRegisterExt | OaifExec };
            _ = SHOpenWithDialog(owner, &info);
        }
    }

    public static async Task ShowPropertiesAsync(nint owner, string folder, IReadOnlyList<string> names)
    {
        if (names.Count == 1 || names.Count == 0)
        {
            var path = names.Count == 0 ? folder : WindowsPath.Join(folder, names[0]);
            await ShellThread.Run(() => ShowPropertiesPage(owner, path, null));
            return;
        }

        var paths = names.Select(n => WindowsPath.Join(folder, n)).ToList();
        if (paths.Select(WindowsPath.GetParent).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
        {
            await ShellThread.Run(() => ShowMultiFolderProperties(paths));
            return;
        }

        using var menu = await ShellMenu.BuildAsync(folder, names, extended: false);
        await menu.InvokeVerbAsync("properties", owner);
    }

    private static unsafe void ShowMultiFolderProperties(IReadOnlyList<string> paths)
    {
        var data = ShellItems.DataObject(paths);
        try
        {
            ShellItems.Check(SHMultiFileProperties(data, 0));
        }
        finally
        {
            ShellItems.Release(data);
        }
    }

    public static Task ShowPropertiesAsync(nint owner, string path, string? page)
        => ShellThread.Run(() => ShowPropertiesPage(owner, path, page));

    public static string SecurityPage => _securityPage ??= LoadSecurityPageName() ?? "Security";

    private static string? _securityPage;

    private static void ShowPropertiesPage(nint owner, string path, string? page)
    {
        var target = WindowsPath.FromExtended(path);
        if (target.Length == 2 && target[1] == ':')
        {
            target += "\\";
        }

        _ = SHObjectProperties(owner, ShopFilePath, target, page);
    }

    private static unsafe string? LoadSecurityPageName()
    {
        var module = LoadLibraryExW("rshx32.dll", 0, LoadLibraryAsDataFile);
        if (module == 0)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[128];
            var length = LoadStringW(module, 2, buffer, 128);
            return length > 0 ? new string(buffer, 0, length) : null;
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private const uint OaifAllowRegistration = 0x1;
    private const uint OaifRegisterExt = 0x2;
    private const uint OaifExec = 0x4;
    private const uint ShopFilePath = 0x2;
    private const uint LoadLibraryAsDataFile = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct OpenAsInfo
    {
        public char* File;
        public char* Class;
        public uint Flags;
    }

    private const uint SeeMaskInvokeIdList = 0xC;
    private const uint SeeMaskNoAsync = 0x100;
    private const uint SeeMaskFlagNoUi = 0x400;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ShellExecuteInfo
    {
        public uint Size;
        public uint Mask;
        public nint Hwnd;
        public char* Verb;
        public char* File;
        public char* Parameters;
        public char* Directory;
        public int Show;
        public nint InstApp;
        public nint IdList;
        public char* Class;
        public nint KeyClass;
        public uint HotKey;
        public nint IconOrMonitor;
        public nint Process;
    }

    [LibraryImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ShellExecuteExW(ShellExecuteInfo* info);

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHMultiFileProperties(void* data, uint flags);

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHOpenWithDialog(nint owner, OpenAsInfo* info);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHObjectProperties(nint owner, uint type, string name, string? page);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryExW(string name, nint file, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(nint module);

    [LibraryImport("user32.dll")]
    private static unsafe partial int LoadStringW(nint module, uint id, char* buffer, int size);
}
