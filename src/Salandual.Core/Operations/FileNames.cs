// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Operations/FileNames.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Operations;

public static class FileNames
{
    private static readonly string[] _devices =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    public static bool IsReserved(string name) => _devices.Contains(name.Split('.')[0].TrimEnd(), StringComparer.OrdinalIgnoreCase);

    public static string? Problem(string name)
    {
        if (name.Length == 0 || name.Trim().Length == 0)
        {
            return Strings.Name_Empty;
        }

        if (name is "." or "..")
        {
            return Strings.Name_Dots;
        }

        foreach (var c in name)
        {
            if (c < ' ')
            {
                return Strings.Name_ControlCharacters;
            }

            if (c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            {
                return Loc.Format(Strings.Name_BadCharacter, c);
            }
        }

        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            return Strings.Name_TrailingDotOrSpace;
        }

        if (IsReserved(name))
        {
            return Loc.Format(Strings.Name_Reserved, name.Split('.')[0].TrimEnd());
        }

        return name.Length > 255 ? Strings.Name_TooLong : null;
    }

    public static string FreeName(IOperationFileSystem fs, string folder, string name)
    {
        var path = fs.Combine(folder, name);
        return fs.Stat(path) is null ? name : fs.GetName(FileOperations.UniqueName(fs, path, isDirectory: false));
    }
}
