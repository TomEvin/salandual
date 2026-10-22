// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Editing/ExternalCommand.cs

using System.Text;
using System.Text.RegularExpressions;

namespace Salandual.Core.Editing;

public sealed partial record ExternalCommand(string Program, IReadOnlyList<string> Arguments)
{
    public static readonly string[] Variables = ["$(FullName)", "$(Name)", "$(FullPath)", "$(NameNoExt)"];

    public static ExternalCommand? Parse(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var parts = Split(commandLine.Trim());
        return parts.Count == 0 || parts[0].Length == 0 ? null : new ExternalCommand(parts[0], parts.Skip(1).ToList());
    }

    public string DisplayName => Path.GetFileNameWithoutExtension(Program.Replace('/', '\\').Split('\\')[^1]);

    public IReadOnlyList<string> ArgumentsFor(string file)
    {
        var folder = Path.GetDirectoryName(file) ?? "";
        return [.. Arguments.Select(a => VariablePattern().Replace(a, m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "fullname" => file,
            "name" => Path.GetFileName(file),
            "namenoext" => Path.GetFileNameWithoutExtension(file),
            _ => folder,
        }))];
    }

    [GeneratedRegex(@"\$\((FullName|NameNoExt|Name|FullPath)\)", RegexOptions.IgnoreCase)]
    private static partial Regex VariablePattern();

    internal static List<string> Split(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var any = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\')
            {
                var slashes = 0;
                while (i < line.Length && line[i] == '\\')
                {
                    slashes++;
                    i++;
                }

                if (i < line.Length && line[i] == '"')
                {
                    current.Append('\\', slashes / 2);
                    if (slashes % 2 == 1)
                    {
                        current.Append('"');
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else
                {
                    current.Append('\\', slashes);
                    i--;
                }

                any = true;
                continue;
            }

            if (c == '"')
            {
                quoted = !quoted;
                any = true;
                continue;
            }

            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    any = false;
                }

                continue;
            }

            current.Append(c);
            any = true;
        }

        if (any)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }
}
