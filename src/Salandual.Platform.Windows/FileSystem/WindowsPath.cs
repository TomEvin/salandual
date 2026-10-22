// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/WindowsPath.cs

namespace Salandual.Platform.Windows.FileSystem;

public static class WindowsPath
{
    public const string ExtendedPrefix = @"\\?\";
    public const string ExtendedUncPrefix = @"\\?\UNC\";

    public static string ToExtended(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
        {
            return path;
        }

        path = path.Replace('/', '\\');
        if (IsDrivePath(path))
        {
            return ExtendedPrefix + (path.Length == 2 ? path + "\\" : path);
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal) && path.Length > 2 && path[2] != '\\' && path[2] != '?' && path[2] != '.')
        {
            return ExtendedUncPrefix + path[2..];
        }

        throw new ArgumentException($"Not a fully qualified local or network path: '{path}'.", nameof(path));
    }

    public static string FromExtended(string path)
    {
        if (path.StartsWith(ExtendedUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[ExtendedUncPrefix.Length..];
        }

        return path.StartsWith(ExtendedPrefix, StringComparison.Ordinal) ? path[ExtendedPrefix.Length..] : path;
    }

    public static string Join(string directory, string name)
        => directory.Length == 0 ? name : directory.EndsWith('\\') ? directory + name : directory + "\\" + name;

    public static string? GetRoot(string path)
    {
        path = FromExtended(path);
        if (IsDrivePath(path))
        {
            return path[..2] + "\\";
        }

        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        var server = path.IndexOf('\\', 2);
        if (server < 0)
        {
            return null;
        }

        var share = path.IndexOf('\\', server + 1);
        return share < 0 ? path : path[..share];
    }

    public static string? GetParent(string path)
    {
        path = FromExtended(path);
        var root = GetRoot(path);
        if (root is null)
        {
            return null;
        }

        var trimmed = path.TrimEnd('\\');
        if (trimmed.Length <= root.TrimEnd('\\').Length)
        {
            return null;
        }

        var cut = trimmed.LastIndexOf('\\');
        return cut < root.Length ? root : trimmed[..cut];
    }

    public static string GetName(string path)
    {
        path = FromExtended(path);
        var root = GetRoot(path);
        if (root is not null && path.TrimEnd('\\').Length <= root.TrimEnd('\\').Length)
        {
            return root;
        }

        var trimmed = path.TrimEnd('\\');
        return trimmed[(trimmed.LastIndexOf('\\') + 1)..];
    }

    private static bool IsDrivePath(string path)
        => path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path.Length == 2 || path[2] == '\\');
}
