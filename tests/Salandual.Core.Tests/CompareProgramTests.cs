// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/CompareProgramTests.cs

using Salandual.Core.Compare;

namespace Salandual.Core.Tests;

public sealed class CompareProgramTests
{
    [Fact]
    public void TheVariables_AreFilledIntoTheirOwnArguments()
    {
        var left = @"C:\Projects\My Site\a ""b"".json";
        var right = @"D:\Backup\$(Right) & calc.json";
        Assert.Equal(["/u", "/e", left, right], CompareProgram.ArgumentsFor("/u /e \"$(Left)\" \"$(Right)\"", left, right));
        Assert.Equal(["--diff", left, right], CompareProgram.ArgumentsFor("--diff $(Left) $(Right)", left, right));
        Assert.Equal(["/title1=a \"b\".json", "/title2=$(Right) & calc.json"], CompareProgram.ArgumentsFor("\"/title1=$(LeftName)\" \"/title2=$(RightName)\"", left, right));
        Assert.Equal([left, right], CompareProgram.ArgumentsFor(CompareProgram.DefaultArguments, left, right));
    }

    [Fact]
    public void TheKnownPrograms_AreFoundWhereTheyInstall()
    {
        string[] there = [@"C:\Program Files (x86)\WinMerge\WinMergeU.exe", @"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe"];
        var known = CompareProgram.Known(p => there.Contains(p), @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\x\AppData\Local");
        Assert.Equal(["WinMerge", "Beyond Compare", "Visual Studio Code"], known.Select(k => k.Program.Name));
        Assert.Equal([true, false, true], known.Select(k => k.Found));
        Assert.Equal(there[0], known[0].Program.Path);
        Assert.Equal(@"C:\Program Files\Beyond Compare 5\BComp.exe", known[1].Program.Path);
        Assert.StartsWith("--diff ", known[2].Program.Arguments, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe", true)]
    [InlineData(@"""C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe""", true)]
    [InlineData(@"C:\Program Files\PowerShell\7\PWSH.EXE", true)]
    [InlineData(@"C:\Program Files\WinMerge\WinMergeU.exe", false)]
    public void Shells_AreTold(string program, bool shell) => Assert.Equal(shell, CompareProgram.IsShell(program));
}
