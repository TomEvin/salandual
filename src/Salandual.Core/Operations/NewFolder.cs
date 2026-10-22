// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Operations/NewFolder.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Operations;

public sealed record NewFolderPlan(string Deepest, string First, IReadOnlyList<string> Missing)
{
    public static NewFolderPlan? Make(IOperationFileSystem fs, string folder, string typed, out string? problem)
    {
        var text = typed.Trim().Replace('/', '\\');
        string start;
        if (Path.IsPathFullyQualified(text))
        {
            var root = Path.GetPathRoot(text)!;
            start = root;
            text = text[root.Length..];
        }
        else
        {
            start = folder;
        }

        var parts = text.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            problem = Strings.Name_Empty;
            return null;
        }

        foreach (var part in parts)
        {
            if (FileNames.Problem(part) is { } p)
            {
                problem = parts.Length == 1 ? p : Loc.Format(Strings.Name_PartProblem, part, p);
                return null;
            }
        }

        var path = start;
        var missing = new List<string>();
        for (var i = 0; i < parts.Length; i++)
        {
            path = fs.Combine(path, parts[i]);
            if (missing.Count > 0)
            {
                missing.Add(path);
                continue;
            }

            var info = fs.Stat(path);
            if (info is null)
            {
                missing.Add(path);
            }
            else if (!info.Value.IsDirectory)
            {
                problem = Loc.Format(i == 0 ? Strings.NewFolder_FileHere : Strings.NewFolder_FileOnTheWay, parts[i]);
                return null;
            }
        }

        if (missing.Count == 0)
        {
            problem = parts.Length == 1 ? Loc.Format(Strings.Name_FolderExists, parts[0]) : Strings.NewFolder_AllExist;
            return null;
        }

        problem = null;
        return new NewFolderPlan(path, missing[0], missing);
    }

    public void Create(IOperationFileSystem fs)
    {
        foreach (var path in Missing)
        {
            fs.CreateDirectory(path);
        }
    }
}
