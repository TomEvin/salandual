// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/DiskSpace.cs

using System.Runtime.InteropServices;

namespace Salandual.Platform.Windows.FileSystem;

public static partial class DiskSpace
{
    public static bool TryGet(string path, out long freeForUser, out long total)
    {
        var directory = WindowsPath.ToExtended(path);
        if (!directory.EndsWith('\\'))
        {
            directory += "\\";
        }

        if (GetDiskFreeSpaceExW(directory, out var free, out var size, out _))
        {
            freeForUser = (long)free;
            total = (long)size;
            return true;
        }

        freeForUser = total = 0;
        return false;
    }

    public static bool IsMadeUp(long total) => total >= CloudSizeHint;

    private const long CloudSizeHint = 1L << 50;

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceExW(string directoryName, out ulong freeBytesAvailableToCaller, out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);
}
