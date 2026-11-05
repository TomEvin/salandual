// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/EverythingIndex.cs

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Salandual.Core.QuickOpen;

namespace Salandual.Platform.Windows.FileSystem;

public sealed unsafe partial class EverythingIndex : IFileNameIndex
{
    private const string EverythingClass = "EVERYTHING_TASKBAR_NOTIFICATION";
    private const string ReplyClass = "SalandualEverythingReply";
    private const uint ReplyId = 0x53414C44;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);
    private static bool _registered;
    private static readonly Lock _gate = new();

    [ThreadStatic]
    private static byte[]? _answer;

    public FileIndexKind Kind => FileIndexKind.Everything;

    public bool IsAvailable() => FindWindowW(EverythingClass, null) != 0;

    public IReadOnlyList<string> CoveredFolders() => [];

    public IReadOnlyList<WalkedEntry> Find(string typed, int max, CancellationToken cancel)
    {
        var search = FileNamePattern.Wildcards(typed);
        var everything = FindWindowW(EverythingClass, null);
        if (search.Length == 0 || everything == 0 || cancel.IsCancellationRequested)
        {
            return [];
        }

        var instance = GetModuleHandleW(null);
        lock (_gate)
        {
            if (!_registered)
            {
                fixed (char* name = ReplyClass)
                {
                    var wc = new WndClassEx { Size = (uint)sizeof(WndClassEx), WndProc = &WndProc, Instance = instance, ClassName = name };
                    _registered = RegisterClassExW(&wc) != 0;
                }
            }
        }

        nint window;
        fixed (char* name = ReplyClass)
        {
            window = CreateWindowExW(0, name, null, 0, 0, 0, 0, 0, HwndMessage, 0, instance, null);
        }

        if (window == 0)
        {
            return [];
        }

        try
        {
            _answer = null;

            var size = 20 + ((search.Length + 1) * 2);
            var query = new byte[size];
            fixed (byte* q = query)
            {
                var head = (uint*)q;
                head[0] = (uint)window;
                head[1] = ReplyId;
                head[2] = 0;
                head[3] = 0;
                head[4] = (uint)Math.Clamp(max, 1, 5000);
                search.AsSpan().CopyTo(new Span<char>(q + 20, search.Length));
                var data = new CopyDataStruct { Data = QueryW, Size = (uint)size, Pointer = q };
                if (SendMessageTimeoutW(everything, WmCopyData, window, (nint)(&data), SmtoAbortIfHung, 2000, null) == 0)
                {
                    return [];
                }
            }

            var until = Environment.TickCount64 + (long)Wait.TotalMilliseconds;
            Msg msg;
            while (_answer is null && !cancel.IsCancellationRequested)
            {
                var left = until - Environment.TickCount64;
                if (left <= 0)
                {
                    break;
                }

                MsgWaitForMultipleObjects(0, null, 0, (uint)Math.Min(left, 100), QsAllInput);
                while (PeekMessageW(&msg, 0, 0, 0, PmRemove))
                {
                    TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
            }

            return _answer is { } answer ? Parse(answer, max) : [];
        }
        finally
        {
            DestroyWindow(window);
            _answer = null;
        }
    }

    internal static IReadOnlyList<WalkedEntry> Parse(ReadOnlySpan<byte> list, int max)
    {
        const int Head = 28, Item = 12;
        var found = new List<WalkedEntry>();
        if (list.Length < Head)
        {
            return found;
        }

        var count = MemoryMarshal.Read<uint>(list[20..]);
        for (long i = 0; i < count && found.Count < max; i++)
        {
            var at = Head + (i * Item);
            if (at + Item > list.Length)
            {
                break;
            }

            var item = list.Slice((int)at, Item);
            var flags = MemoryMarshal.Read<uint>(item);
            if ((flags & ItemDrive) != 0)
            {
                continue;
            }

            var name = Text(list, MemoryMarshal.Read<uint>(item[4..]));
            var folder = Text(list, MemoryMarshal.Read<uint>(item[8..]));
            if (name is { Length: > 0 } && folder is { Length: >= 2 } && name.IndexOfAny(['\\', '/']) < 0 && Path.IsPathFullyQualified(folder.Length == 2 ? folder + "\\" : folder))
            {
                found.Add(new WalkedEntry(name, folder.Length == 2 ? folder + "\\" : folder, (flags & ItemFolder) != 0));
            }
        }

        return found;
    }

    private static string? Text(ReadOnlySpan<byte> list, uint offset)
    {
        if (offset % 2 != 0 || offset >= list.Length)
        {
            return null;
        }

        var chars = MemoryMarshal.Cast<byte, char>(list[(int)offset..]);
        var end = chars.IndexOf('\0');
        return end < 0 || end > 32_767 ? null : new string(chars[..end]);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmCopyData && lParam != 0)
        {
            var data = (CopyDataStruct*)lParam;
            if (data->Data == ReplyId && data->Pointer is not null && data->Size is > 0 and <= 64 * 1024 * 1024)
            {
                _answer = new ReadOnlySpan<byte>(data->Pointer, (int)data->Size).ToArray();
                return 1;
            }

            return 0;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private const uint WmCopyData = 0x004A, QueryW = 2, SmtoAbortIfHung = 2, QsAllInput = 0x04FF, PmRemove = 1;
    private const uint ItemFolder = 1, ItemDrive = 2;
    private const nint HwndMessage = -3;

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public nuint Data;
        public uint Size;
        public void* Pointer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint FindWindowW(string className, string? windowName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial ushort RegisterClassExW(WndClassEx* wc);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, void* param);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SendMessageTimeoutW(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, nuint* result);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint MsgWaitForMultipleObjects(uint count, nint* handles, int waitAll, uint milliseconds, uint wakeMask);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(Msg* msg, nint hwnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(Msg* msg);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint DispatchMessageW(Msg* msg);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
}
