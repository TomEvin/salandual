// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/UncPath.cs

using System.Runtime.InteropServices;

namespace Salandual.Platform.Windows.FileSystem;

public static unsafe partial class UncPath
{
    private const uint UniversalNameInfoLevel = 1;

    public static string For(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return path;
        }

        uint size = 1024;
        var buffer = stackalloc byte[(int)size];
        if (WNetGetUniversalNameW(path, UniversalNameInfoLevel, buffer, ref size) == 0)
        {
            var name = Marshal.PtrToStringUni(*(nint*)buffer);
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        return path;
    }

    [LibraryImport("mpr.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WNetGetUniversalNameW(string localPath, uint infoLevel, byte* buffer, ref uint bufferSize);
}
