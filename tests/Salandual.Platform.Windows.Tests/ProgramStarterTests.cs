// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/ProgramStarterTests.cs

using Salandual.Core.UserMenu;
using Salandual.Platform.Windows.Shell;

namespace Salandual.Platform.Windows.Tests;

public sealed class ProgramStarterTests
{
    private readonly WindowsProgramStarter _starter = new();

    [Fact]
    public void Finds_ProgramsByName_NeverAScriptToRun()
    {
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), _starter.Find("cmd.exe", out var failure), ignoreCase: true);
        Assert.Equal(ProgramStartFailure.None, failure);
        Assert.Null(_starter.Find("surely-not-a-program-here.exe", out failure));
        Assert.Equal(ProgramStartFailure.NotFound, failure);

        var dir = Directory.CreateTempSubdirectory("salandual-starter-").FullName;
        try
        {
            var script = Path.Combine(dir, "run.cmd");
            File.WriteAllText(script, "@echo off");
            Assert.Null(_starter.Find(script, out failure));
            Assert.Equal(ProgramStartFailure.NotAProgram, failure);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConsolePrograms_ByTheirHeader()
    {
        Assert.True(_starter.IsConsoleProgram(Path.Combine(Environment.SystemDirectory, "cmd.exe")));
        Assert.False(_starter.IsConsoleProgram(Path.Combine(Environment.SystemDirectory, "notepad.exe")));
        Assert.False(_starter.IsConsoleProgram(Path.Combine(Environment.SystemDirectory, "no-such.exe")));
        Assert.True(File.Exists(_starter.WindowsPowerShell));
    }
}
