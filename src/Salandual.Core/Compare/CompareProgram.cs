// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Compare/CompareProgram.cs

using System.Text.RegularExpressions;
using Salandual.Core.Editing;

namespace Salandual.Core.Compare;

public sealed partial record CompareProgram(string Name, string Path, string Arguments)
{
    public static readonly string[] Variables = ["$(Left)", "$(Right)", "$(LeftName)", "$(RightName)"];

    public const string DefaultArguments = "\"$(Left)\" \"$(Right)\"";

    private static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "powershell_ise.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "bash.exe", "wsl.exe", "conhost.exe",
    };

    public static IReadOnlyList<(CompareProgram Program, bool Found)> Known(Func<string, bool> exists, string programFiles, string programFilesX86, string localAppData)
    {
        (string Name, string Arguments, string[] Places)[] known =
        [
            ("WinMerge", "/u /e \"$(Left)\" \"$(Right)\"",
                [Join(programFiles, @"WinMerge\WinMergeU.exe"), Join(programFilesX86, @"WinMerge\WinMergeU.exe"), Join(localAppData, @"Programs\WinMerge\WinMergeU.exe")]),
            ("Beyond Compare", DefaultArguments,
                [Join(programFiles, @"Beyond Compare 5\BComp.exe"), Join(programFiles, @"Beyond Compare 4\BComp.exe"), Join(programFilesX86, @"Beyond Compare 4\BComp.exe"), Join(localAppData, @"Programs\Beyond Compare 5\BComp.exe")]),
            ("Visual Studio Code", "--diff \"$(Left)\" \"$(Right)\"",
                [Join(localAppData, @"Programs\Microsoft VS Code\Code.exe"), Join(programFiles, @"Microsoft VS Code\Code.exe")]),
        ];
        return [.. known.Select(k =>
        {
            var found = k.Places.FirstOrDefault(p => p.Length > 0 && exists(p));
            return (new CompareProgram(k.Name, found ?? k.Places.First(p => p.Length > 0), k.Arguments), found is not null);
        })];

        static string Join(string folder, string path) => folder.Length == 0 ? "" : System.IO.Path.Combine(folder, path);
    }

    public static bool IsShell(string program) => Shells.Contains(System.IO.Path.GetFileName(program.Trim().Trim('"')));

    public static IReadOnlyList<string> ArgumentsFor(string arguments, string left, string right)
        => [.. ExternalCommand.Split((arguments ?? "").Trim()).Select(a => VariablePattern().Replace(a, m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "left" => left,
            "right" => right,
            "leftname" => System.IO.Path.GetFileName(left),
            _ => System.IO.Path.GetFileName(right),
        }))];

    [GeneratedRegex(@"\$\((Left|Right|LeftName|RightName)\)", RegexOptions.IgnoreCase)]
    private static partial Regex VariablePattern();
}
