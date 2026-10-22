// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Operations/DropRules.cs

using Salandual.Core.Settings;

namespace Salandual.Core.Operations;

[Flags]
public enum DropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
}

public enum DropProblem
{
    None,

    AlreadyThere,

    IntoItself,
}

public static class DropRules
{
    public static DropEffects Choose(bool ctrl, bool shift, bool alt, DropEffects allowed, bool sameDrive, PlainDrag rule)
    {
        var modified = ctrl || shift || alt;
        var wanted = alt || (ctrl && shift) ? DropEffects.Link
            : shift ? DropEffects.Move
            : ctrl ? DropEffects.Copy
            : PlainDefault(sameDrive, rule);
        if ((allowed & wanted) != 0)
        {
            return wanted;
        }

        return modified ? DropEffects.None
            : (allowed & DropEffects.Copy) != 0 ? DropEffects.Copy
            : (allowed & DropEffects.Move) != 0 ? DropEffects.Move
            : allowed & DropEffects.Link;
    }

    public static DropEffects PlainDefault(bool sameDrive, PlainDrag rule)
        => rule == PlainDrag.WindowsRule && sameDrive ? DropEffects.Move : DropEffects.Copy;

    public static bool SameDrive(string a, string b)
        => Path.GetPathRoot(a) is { Length: > 0 } ra && string.Equals(ra.TrimEnd('\\'), Path.GetPathRoot(b)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public static DropProblem Check(IReadOnlyList<string> sources, string folder)
    {
        var target = folder.TrimEnd('\\');
        foreach (var source in sources)
        {
            var s = source.TrimEnd('\\');
            if (string.Equals(s, target, StringComparison.OrdinalIgnoreCase)
                || target.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return DropProblem.IntoItself;
            }
        }

        return sources.Count > 0 && sources.All(s => string.Equals(Path.GetDirectoryName(s.TrimEnd('\\'))?.TrimEnd('\\'), target, StringComparison.OrdinalIgnoreCase))
            ? DropProblem.AlreadyThere
            : DropProblem.None;
    }
}
