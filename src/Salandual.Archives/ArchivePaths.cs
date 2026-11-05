// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/ArchivePaths.cs

namespace Salandual.Archives;

public static class ArchivePaths
{
    public const string DotDot = "(..)";

    private static readonly HashSet<string> _archiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "zipx", "7z", "rar", "tar", "tgz", "tbz", "tbz2", "txz", "tzst", "tlz", "taz", "gz", "gzip", "bz2", "xz", "zst",
        "lzma", "lz", "z", "cab", "iso", "wim", "swm", "esd", "dmg", "arj", "lzh", "lha", "cpio", "rpm", "deb", "squashfs", "001",
    };

    public static bool IsArchiveName(string name) => Path.GetExtension(name) is { Length: > 1 } ext && _archiveExtensions.Contains(ext[1..]);

    public static string Shown(string raw)
    {
        var parts = raw.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var shown = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].TrimEnd();
            if (i == 0 && part.Length == 2 && part[1] == ':' && char.IsAsciiLetter(part[0]))
            {
                continue;
            }

            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            shown.Add(part == ".." ? DotDot : part);
        }

        return string.Join('\\', shown);
    }

    public static string SingleName(string archiveFileName)
    {
        var extension = Path.GetExtension(archiveFileName);
        var stem = Path.GetFileNameWithoutExtension(archiveFileName);
        return extension.ToLowerInvariant() switch
        {
            ".tgz" or ".tbz" or ".tbz2" or ".txz" or ".tzst" or ".tlz" or ".taz" => stem + ".tar",
            _ when stem.Length > 0 => stem,
            _ => archiveFileName + "~",
        };
    }

    public static string SafeFileName(string name)
    {
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] < 32 || chars[i] is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            {
                chars[i] = '_';
            }
        }

        var safe = new string(chars).TrimEnd('.', ' ');
        if (safe.Length == 0)
        {
            return "_";
        }

        return IsDeviceName(safe) ? "_" + safe : safe;
    }

    public static bool IsDeviceName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9' or '¹' or '²' or '³');
    }

    public static string FolderNameFor(string archiveFileName)
    {
        var name = archiveFileName;
        for (var i = 0; i < 3; i++)
        {
            var extension = Path.GetExtension(name);
            var bare = extension.TrimStart('.');
            var isPart = bare.Length > 0 && bare.All(char.IsAsciiDigit)
                || (bare.StartsWith("part", StringComparison.OrdinalIgnoreCase) && bare.Length > 4 && bare[4..].All(char.IsAsciiDigit));
            if (extension.Length < 2 || !(isPart || _archiveExtensions.Contains(bare)) || name.Length == extension.Length)
            {
                break;
            }

            name = name[..^extension.Length];
        }

        return SafeFileName(name);
    }

    public static string? Inside(string path, string archivePath)
    {
        if (path.Length == archivePath.Length)
        {
            return string.Equals(path, archivePath, StringComparison.OrdinalIgnoreCase) ? "" : null;
        }

        return path.Length > archivePath.Length && path[archivePath.Length] == '\\'
            && path.StartsWith(archivePath, StringComparison.OrdinalIgnoreCase)
            ? path[(archivePath.Length + 1)..].TrimEnd('\\')
            : null;
    }
}
