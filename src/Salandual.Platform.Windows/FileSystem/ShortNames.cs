// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/ShortNames.cs

using System.Runtime.InteropServices;

namespace Salandual.Platform.Windows.FileSystem;

public static partial class ShortNames
{
    public static string Of(string path)
    {
        var extended = WindowsPath.ToExtended(path);
        var buffer = new char[Math.Max(extended.Length + 1, 300)];
        var length = GetShortPathNameW(extended, buffer, (uint)buffer.Length);
        if (length > buffer.Length)
        {
            buffer = new char[length];
            length = GetShortPathNameW(extended, buffer, (uint)buffer.Length);
        }

        return length == 0 || length > buffer.Length ? path : WindowsPath.FromExtended(new string(buffer, 0, (int)length));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetShortPathNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint bufferLength);
}
