// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/PerceivedTypes.cs

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Salandual.Platform.Windows.Shell;

public enum PerceivedKind { Other, Picture, Video }

public static partial class PerceivedTypes
{
    private const int PERCEIVED_TYPE_IMAGE = 2;
    private const int PERCEIVED_TYPE_VIDEO = 4;

    private static readonly ConcurrentDictionary<string, PerceivedKind> _known = new(StringComparer.OrdinalIgnoreCase);

    public static PerceivedKind Of(string extension)
        => extension.Length == 0 || extension.Contains('\0') ? PerceivedKind.Other : _known.GetOrAdd(extension, Look);

    private static PerceivedKind Look(string extension)
    {
        try
        {
            if (AssocGetPerceivedType("." + extension, out var type, out _, IntPtr.Zero) < 0)
            {
                return PerceivedKind.Other;
            }

            return type switch
            {
                PERCEIVED_TYPE_IMAGE => PerceivedKind.Picture,
                PERCEIVED_TYPE_VIDEO => PerceivedKind.Video,
                _ => PerceivedKind.Other,
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return PerceivedKind.Other;
        }
    }

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AssocGetPerceivedType(string extension, out int type, out int flags, IntPtr typeName);
}
