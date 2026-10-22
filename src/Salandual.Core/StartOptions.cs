// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/StartOptions.cs

namespace Salandual.Core;

public sealed record StartOptions(string? Left, string? Right, string? Active, int Panel)
{
    public static StartOptions None { get; } = new(null, null, null, 0);

    public static StartOptions Parse(IReadOnlyList<string> args, string currentDirectory)
    {
        string? left = null, right = null, active = null, dropped = null;
        var panel = 0;
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            if (dropped is null && name.Length > 2 && name[0] is not ('-' or '/') && Path.IsPathFullyQualified(name.Trim().Trim('"')))
            {
                dropped = Full(name);
                continue;
            }

            if (name.Length != 2 || name[0] is not ('-' or '/') || i + 1 >= args.Count)
            {
                continue;
            }

            var value = args[++i];
            switch (char.ToUpperInvariant(name[1]))
            {
                case 'L': left = Full(value); break;
                case 'R': right = Full(value); break;
                case 'A': active = Full(value); break;
                case 'P': panel = value is "1" or "2" ? value[0] - '0' : 0; break;
                default: i--; break;
            }
        }

        return new StartOptions(left, right, left is null && right is null ? active ?? dropped : null, panel);

        string? Full(string path)
        {
            path = path.Trim().Trim('"');
            if (path.Length == 0)
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path, currentDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }
    }

    public int ActiveIndex => Panel == 2 ? 1 : 0;

    public (string Left, string Right) Resolve(string defaultLeft, string defaultRight)
    {
        var left = Left ?? defaultLeft;
        var right = Right ?? defaultRight;
        if (Active is { } a)
        {
            if (ActiveIndex == 1)
            {
                right = a;
            }
            else
            {
                left = a;
            }
        }

        return (left, right);
    }
}
