// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/ArchiveSafety.cs

using Salandual.Core.Resources;

namespace Salandual.Archives;

public enum Refusal
{
    LeavesTarget,
    FullPath,
    DeviceName,
    Stream,
    LinkOutside,
}

public static class ArchiveSafety
{
    public static Refusal? Check(string raw, out string part)
    {
        part = raw;
        if (raw.StartsWith('\\') || raw.StartsWith('/') || (raw.Length >= 2 && raw[1] == ':' && char.IsAsciiLetter(raw[0])))
        {
            return Refusal.FullPath;
        }

        foreach (var piece in raw.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            part = piece;
            if (piece == ".")
            {
                continue;
            }

            if (piece.TrimEnd('.', ' ').Length == 0)
            {
                return Refusal.LeavesTarget;
            }

            if (piece.Contains(':', StringComparison.Ordinal))
            {
                return Refusal.Stream;
            }

            if (ArchivePaths.IsDeviceName(piece))
            {
                return Refusal.DeviceName;
            }
        }

        return null;
    }

    public static bool IsInside(string path, string root)
    {
        var full = Path.GetFullPath(path);
        var top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return full.Length > top.Length + 1 && full.StartsWith(top, StringComparison.OrdinalIgnoreCase) && full[top.Length] == '\\';
    }

    public static string? LinkTargetInside(string linkTarget, string linkPath, string root)
    {
        var target = linkTarget.Replace('/', '\\');
        if (target.StartsWith('\\') || (target.Length >= 2 && target[1] == ':') || target.Split('\\').Any(p => p.Contains(':', StringComparison.Ordinal)))
        {
            return null;
        }

        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(linkPath) ?? root, target));
        return IsInside(resolved, root) ? target : null;
    }

    public static string Text(Refusal refusal, string part) => refusal switch
    {
        Refusal.LeavesTarget => Strings.Unpack_Refused_LeavesTarget,
        Refusal.FullPath => Strings.Unpack_Refused_FullPath,
        Refusal.DeviceName => Loc.Format(Strings.Unpack_Refused_Device, part.Split('.')[0]),
        Refusal.Stream => Strings.Unpack_Refused_Stream,
        _ => Strings.Unpack_Refused_LinkOutside,
    };

    public static string Text(ExtractResult result) => result switch
    {
        ExtractResult.ChecksumError => Strings.Unpack_Checksum,
        ExtractResult.Truncated => Strings.Unpack_Truncated,
        ExtractResult.WrongPassword => Strings.Unpack_WrongPassword,
        ExtractResult.UnsupportedMethod => Strings.Unpack_Unsupported,
        _ => Strings.Unpack_Damaged,
    };
}
