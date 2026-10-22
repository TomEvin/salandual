// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/NativeFocus.cs

using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Salandual.App.Viewer;

internal static partial class NativeFocus
{
    public static void TakeBack(TopLevel window)
    {
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != 0 && GetFocus() != hwnd)
        {
            SetFocus(hwnd);
        }
    }

    public static bool IsOn(TopLevel window)
        => OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.Handle is { } hwnd && GetFocus() == hwnd;

    public static bool IsInFront(TopLevel window)
        => (window as Window)?.IsActive == true
            || (OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != 0 && GetForegroundWindow() == hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetFocus(nint hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetFocus();
}
