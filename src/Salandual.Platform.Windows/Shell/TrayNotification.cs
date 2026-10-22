// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/TrayNotification.cs

using System.Runtime.InteropServices;

namespace Salandual.Platform.Windows.Shell;

public static unsafe partial class TrayNotification
{
    public const uint CallbackMessage = 0x8000 + 0x4D;

    public const int BalloonClicked = 0x0400 + 5;

    public const int BalloonGone = 0x0400 + 4;

    public const int BalloonHidden = 0x0400 + 3;

    private const uint Id = 0x4D4C;
    private const uint Add = 0, Modify = 1, Delete = 2, SetVersion = 4;
    private const uint FlagMessage = 0x1, FlagIcon = 0x2, FlagTip = 0x4, FlagInfo = 0x10, FlagShowTip = 0x80;
    private const uint InfoUser = 0x4, InfoLargeIcon = 0x20, InfoRespectQuietTime = 0x80;
    private static bool _shown;

    public static bool Show(nint window, string title, string text)
    {
        if (window == 0)
        {
            return false;
        }

        var icon = WindowIcon(window);
        var data = Data(window);
        data.Flags = FlagMessage | FlagIcon | FlagTip | FlagShowTip;
        data.CallbackMessage = CallbackMessage;
        data.Icon = icon;
        Copy("Salandual", data.Tip, 128);
        if (!_shown)
        {
            if (!ShellNotify(Add, &data))
            {
                return false;
            }

            data.TimeoutOrVersion = 4;
            ShellNotify(SetVersion, &data);
            _shown = true;
        }

        data.Flags = FlagInfo;
        Copy(title, data.InfoTitle, 64);
        Copy(text, data.Info, 256);
        data.InfoFlags = InfoUser | InfoLargeIcon | InfoRespectQuietTime;
        data.BalloonIcon = icon;
        return ShellNotify(Modify, &data);
    }

    public static void Remove(nint window)
    {
        if (!_shown)
        {
            return;
        }

        var data = Data(window);
        ShellNotify(Delete, &data);
        _shown = false;
    }

    private static NotifyIconData Data(nint window) => new() { Size = (uint)sizeof(NotifyIconData), Window = window, Id = Id };

    private static void Copy(string s, char* target, int capacity)
    {
        var n = Math.Min(s.Length, capacity - 1);
        s.AsSpan(0, n).CopyTo(new Span<char>(target, capacity));
        target[n] = '\0';
    }

    private static nint WindowIcon(nint window)
    {
        var icon = SendMessageW(window, 0x007F , 1 , 0);
        if (icon == 0)
        {
            icon = SendMessageW(window, 0x007F, 0 , 0);
        }

        return icon != 0 ? icon : LoadIconW(0, 32512 );
    }

    private static bool ShellNotify(uint message, NotifyIconData* data) => Shell_NotifyIconW(message, data) != 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint TimeoutOrVersion;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [LibraryImport("shell32.dll")]
    private static partial int Shell_NotifyIconW(uint message, NotifyIconData* data);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint LoadIconW(nint instance, nint name);
}
