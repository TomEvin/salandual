// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/ShellThread.cs

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Salandual.Platform.Windows.Shell;

internal static unsafe partial class ShellThread
{
    private static readonly ConcurrentQueue<Action> _work = new();
    private static readonly Lock _gate = new();
    private static readonly ManualResetEventSlim _ready = new();
    private static Thread? _thread;
    private static nint _window;

    internal static Func<uint, nint, nint, nint?>? MenuMessages { get; set; }

    internal static nint Window => _window;

    internal static bool IsCurrent => Thread.CurrentThread == _thread;

    public static Task<T> Run<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        return done.Task;
    }

    public static Task Run(Action work) => Run(() =>
    {
        work();
        return true;
    });

    private static void Post(Action work)
    {
        EnsureStarted();
        _work.Enqueue(work);
        PostMessageW(_window, WmWork, 0, 0);
    }

    private static void EnsureStarted()
    {
        lock (_gate)
        {
            if (_thread is null)
            {
                _thread = new Thread(Loop) { IsBackground = true, Name = "Salandual shell menus" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }

        _ready.Wait();
    }

    private static void Loop()
    {
        OleInitialize(null);
        var name = "SalandualShellMenuHost";
        fixed (char* className = name)
        {
            var wc = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WndProc = (delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProc,
                Instance = GetModuleHandleW(null),
                ClassName = className,
            };
            RegisterClassExW(&wc);
            _window = CreateWindowExW(WsExToolWindow, className, className, WsPopup, 0, 0, 0, 0, 0, 0, wc.Instance, null);
        }

        ManagedShellExtensions.BlockCommon();
        _ready.Set();
        Msg msg;
        while (GetMessageW(&msg, 0, 0, 0) > 0)
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmWork)
        {
            while (_work.TryDequeue(out var work))
            {
                try
                {
                    work();
                }
                catch
                {
                }
            }

            return 0;
        }

        if (MenuMessages is { } forward && msg is WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar)
        {
            try
            {
                if (forward(msg, wParam, lParam) is { } result)
                {
                    return result;
                }
            }
            catch
            {
            }
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    internal const uint WmInitMenuPopup = 0x0117;
    internal const uint WmDrawItem = 0x002B;
    internal const uint WmMeasureItem = 0x002C;
    internal const uint WmMenuChar = 0x0120;
    private const uint WmWork = 0x8000 + 0x35;
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolWindow = 0x00000080;

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

    [LibraryImport("ole32.dll")]
    private static partial int OleInitialize(void* reserved);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WndClassEx* wc);

    [LibraryImport("user32.dll")]
    private static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, void* param);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(Msg* msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(Msg* msg);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(Msg* msg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
}
