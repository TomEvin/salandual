// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Operations/QuickRename.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Operations;

public static class QuickRename
{
    public static int StemLength(string name, bool isDirectory)
    {
        if (isDirectory)
        {
            return name.Length;
        }

        var dot = name.LastIndexOf('.');
        return dot > 0 ? dot : name.Length;
    }

    public static (string From, string To)? ExtensionChange(string oldName, string newName, bool isDirectory)
    {
        if (isDirectory)
        {
            return null;
        }

        var from = Extension(oldName);
        if (from.Length == 0)
        {
            return null;
        }

        var to = Extension(newName.TrimEnd());
        return string.Equals(from, to, StringComparison.OrdinalIgnoreCase) ? null : (from, to);
    }

    public static string? Problem(IOperationFileSystem fs, string folder, string oldName, string newName)
    {
        if (FileNames.Problem(newName) is { } problem)
        {
            return problem;
        }

        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fs.Stat(fs.Combine(folder, newName)) switch
        {
            { IsDirectory: true } => Loc.Format(Strings.Name_FolderExists, newName),
            not null => Loc.Format(Strings.Name_FileExists, newName),
            null => null,
        };
    }

    private static string Extension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[dot..] : "";
    }
}
