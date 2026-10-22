// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/SystemMenu.cs

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Salandual.App.Controls;

internal static partial class SystemMenu
{
    private const uint MF_BYCOMMAND = 0x0;
    private const uint MF_ENABLED = 0x0;
    private const uint MF_GRAYED = 0x1;
    private const uint TPM_RETURNCMD = 0x100;
    private const uint TPM_RIGHTBUTTON = 0x2;
    private const uint WM_SYSCOMMAND = 0x112;
    private const uint SC_SIZE = 0xF000;
    private const uint SC_MOVE = 0xF010;
    private const uint SC_MINIMIZE = 0xF020;
    private const uint SC_MAXIMIZE = 0xF030;
    private const uint SC_RESTORE = 0xF120;
    private const uint SC_CLOSE = 0xF060;

    public static bool Show(Window window, Point point)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle)
        {
            return false;
        }

        var hwnd = handle.Handle;
        var menu = GetSystemMenu(hwnd, false);
        if (menu == IntPtr.Zero)
        {
            return false;
        }

        var maximized = window.WindowState == WindowState.Maximized;
        var minimized = window.WindowState == WindowState.Minimized;
        Enable(menu, SC_RESTORE, maximized || minimized);
        Enable(menu, SC_MOVE, !maximized);
        Enable(menu, SC_SIZE, !maximized && window.CanResize);
        Enable(menu, SC_MINIMIZE, !minimized && window.CanMinimize);
        Enable(menu, SC_MAXIMIZE, !maximized && window.CanMaximize && window.CanResize);
        Enable(menu, SC_CLOSE, true);

        var screen = window.PointToScreen(point);
        var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, screen.X, screen.Y, hwnd, IntPtr.Zero);
        if (command != 0)
        {
            PostMessageW(hwnd, WM_SYSCOMMAND, (nint)command, 0);
        }

        return true;
    }

    private static void Enable(IntPtr menu, uint item, bool on) => EnableMenuItem(menu, item, MF_BYCOMMAND | (on ? MF_ENABLED : MF_GRAYED));

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetSystemMenu(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool revert);

    [LibraryImport("user32.dll")]
    private static partial int EnableMenuItem(IntPtr menu, uint item, uint enable);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(IntPtr hwnd, uint message, nint wParam, nint lParam);
}
