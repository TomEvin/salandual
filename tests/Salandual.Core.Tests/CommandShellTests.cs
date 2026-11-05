// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/CommandShellTests.cs

using System.Text;
using Salandual.Core.CommandLine;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class CommandShellTests
{
    private const string Wt = @"C:\Users\me\AppData\Local\Microsoft\WindowsApps\wt.exe";
    private const string Pwsh = @"C:\Program Files\PowerShell\7\pwsh.exe";
    private const string WinPs = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    private const string Cmd = @"C:\Windows\System32\cmd.exe";

    private static readonly ShellsFound All = new(Wt, Pwsh, WinPs, Cmd);
    private static readonly ShellsFound NoTerminal = new(null, Pwsh, WinPs, Cmd);
    private static readonly ShellsFound WindowsOnly = new(null, null, WinPs, Cmd);

    private static string Decode(ShellStart start)
    {
        var at = start.Arguments.ToList().IndexOf("-EncodedCommand");
        return Encoding.Unicode.GetString(Convert.FromBase64String(start.Arguments[at + 1]));
    }

    [Fact]
    public void TheShell_IsWhatIsFound_UnlessOneThatIsThereIsChosen()
    {
        var auto = new CommandLineSettings();
        Assert.Equal(ShellKind.Terminal, CommandShell.Effective(auto, All));
        Assert.Equal(ShellKind.PowerShell7, CommandShell.Effective(auto, NoTerminal));
        Assert.Equal(ShellKind.WindowsPowerShell, CommandShell.Effective(auto, WindowsOnly));
        Assert.Equal(ShellKind.CommandPrompt, CommandShell.Effective(auto with { Shell = ShellKind.CommandPrompt }, All));

        Assert.Equal(ShellKind.PowerShell7, CommandShell.Effective(auto with { Shell = ShellKind.Terminal }, NoTerminal));
        Assert.Equal(ShellKind.Terminal, CommandShell.Effective(auto with { Shell = ShellKind.Program }, All));
        Assert.Equal(ShellKind.Program, CommandShell.Effective(auto with { Shell = ShellKind.Program, Program = @"C:\Git\git-bash.exe" }, All));
    }

    [Fact]
    public void ATypedCommand_RunsInPowerShellForTheTerminal_OrInTheShellChosen()
    {
        var auto = new CommandLineSettings();
        Assert.Equal(CommandLineShell.PowerShell7, CommandShell.RunsIn(auto, All));
        Assert.Equal(CommandLineShell.WindowsPowerShell, CommandShell.RunsIn(auto, WindowsOnly));
        Assert.Equal(CommandLineShell.CommandPrompt, CommandShell.RunsIn(auto with { Shell = ShellKind.CommandPrompt }, All));
        Assert.Equal(CommandLineShell.WindowsPowerShell, CommandShell.RunsIn(auto with { Shell = ShellKind.WindowsPowerShell }, All));
        Assert.Equal(CommandLineShell.CommandPrompt, CommandShell.RunsIn(auto with { RunIn = CommandLineShell.CommandPrompt }, All));
        Assert.Equal(CommandLineShell.WindowsPowerShell, CommandShell.RunsIn(auto with { RunIn = CommandLineShell.PowerShell7 }, WindowsOnly));
    }

    [Fact]
    public void ATypedCommand_ReachesTheShellWhole_AsTyped()
    {
        const string typed = "ffprobe -hide_banner \"day2 swim.mp4\" | Select-String 'Duration' ; echo $env:TEMP `n %PATH% & dir";
        var ps = CommandShell.Run(CommandLineShell.PowerShell7, All, typed, @"D:\Video", keepOpen: true);
        Assert.Equal(Pwsh, ps.Program);
        Assert.Equal(@"D:\Video", ps.Folder);
        Assert.Equal(["-NoLogo", "-NoExit", "-EncodedCommand"], ps.Arguments.Take(3));
        Assert.Equal(typed, Decode(ps));
        Assert.Null(ps.RawArguments);
        Assert.False(ps.Elevated);

        var closes = CommandShell.Run(CommandLineShell.WindowsPowerShell, All, "dir", @"D:\Video", keepOpen: false);
        Assert.Equal(WinPs, closes.Program);
        Assert.DoesNotContain("-NoExit", closes.Arguments);

        var cmd = CommandShell.Run(CommandLineShell.CommandPrompt, All, typed, @"D:\Video", keepOpen: true);
        Assert.Equal(Cmd, cmd.Program);
        Assert.Equal("/s /k \"" + typed + "\"", cmd.RawArguments);
        Assert.Empty(cmd.Arguments);
        Assert.StartsWith("/s /c \"", CommandShell.Run(CommandLineShell.CommandPrompt, All, "dir", @"D:\", keepOpen: false).RawArguments, StringComparison.Ordinal);
    }

    [Fact]
    public void NumSlash_OpensTheShellInTheFolder_AndAsAdministratorGivesItTheFolderItself()
    {
        var folder = @"D:\Video\Lake;2024 it's";
        var settings = new CommandLineSettings();

        var wt = CommandShell.Open(settings, All, folder, elevated: false, out var note);
        Assert.Equal(Wt, wt.Program);
        Assert.Equal(folder, wt.Folder);
        Assert.Equal(["-d", "."], wt.Arguments);
        Assert.Equal(ShellNote.None, note);

        var profile = CommandShell.Open(settings with { TerminalProfile = "{0caa0dad-35be-5f56-a8ff-afceeeaa6101}" }, All, folder, elevated: false, out _);
        Assert.Equal(["-p", "{0caa0dad-35be-5f56-a8ff-afceeeaa6101}", "-d", "."], profile.Arguments);

        var wtAdmin = CommandShell.Open(settings, All, folder, elevated: true, out _);
        Assert.True(wtAdmin.Elevated);
        Assert.Equal(["-d", @"D:\Video\Lake\;2024 it's"], wtAdmin.Arguments);

        var pwsh = CommandShell.Open(settings with { Shell = ShellKind.PowerShell7 }, All, folder, elevated: false, out _);
        Assert.Equal(Pwsh, pwsh.Program);
        Assert.Empty(pwsh.Arguments);
        Assert.Equal(["-NoExit", "-WorkingDirectory", folder], CommandShell.Open(settings with { Shell = ShellKind.PowerShell7 }, All, folder, elevated: true, out _).Arguments);

        var winPs = CommandShell.Open(settings with { Shell = ShellKind.WindowsPowerShell }, All, folder, elevated: true, out _);
        Assert.Equal(@"Set-Location -LiteralPath 'D:\Video\Lake;2024 it''s'", Decode(winPs));

        var cmd = CommandShell.Open(settings with { Shell = ShellKind.CommandPrompt }, All, folder, elevated: false, out _);
        Assert.Null(cmd.RawArguments);
        Assert.Equal("/k cd /d \"" + folder + "\"", CommandShell.Open(settings with { Shell = ShellKind.CommandPrompt }, All, folder, elevated: true, out _).RawArguments);

        var percent = CommandShell.Open(settings with { Shell = ShellKind.CommandPrompt }, All, @"D:\100%", elevated: true, out note);
        Assert.Null(percent.RawArguments);
        Assert.Equal(ShellNote.OwnFolder, note);

        var own = CommandShell.Open(settings with { Shell = ShellKind.Program, Program = @"C:\Git\git-bash.exe" }, All, folder, elevated: false, out note);
        Assert.Equal(@"C:\Git\git-bash.exe", own.Program);
        Assert.Empty(own.Arguments);
        Assert.Equal(ShellNote.None, note);
        CommandShell.Open(settings with { Shell = ShellKind.Program, Program = @"C:\Git\git-bash.exe" }, All, folder, elevated: true, out note);
        Assert.Equal(ShellNote.OwnFolder, note);
    }

    [Theory]
    [InlineData("day2-swim.mp4", CommandLineShell.CommandPrompt, "day2-swim.mp4")]
    [InlineData("day2 swim.mp4", CommandLineShell.CommandPrompt, "\"day2 swim.mp4\"")]
    [InlineData("Tom&Jerry.mkv", CommandLineShell.CommandPrompt, "\"Tom&Jerry.mkv\"")]
    [InlineData("a(1).txt", CommandLineShell.CommandPrompt, "\"a(1).txt\"")]
    [InlineData(@"C:\Program Files", CommandLineShell.CommandPrompt, "\"C:\\Program Files\"")]
    [InlineData("day2-swim.mp4", CommandLineShell.PowerShell7, "day2-swim.mp4")]
    [InlineData(@"D:\Video\x.mp4", CommandLineShell.PowerShell7, @"D:\Video\x.mp4")]
    [InlineData("day2 swim.mp4", CommandLineShell.PowerShell7, "'day2 swim.mp4'")]
    [InlineData("it's.txt", CommandLineShell.PowerShell7, "'it''s.txt'")]
    [InlineData("it\u2019s.txt", CommandLineShell.WindowsPowerShell, "'it\u2019\u2019s.txt'")]
    [InlineData("$env.txt", CommandLineShell.PowerShell7, "'$env.txt'")]
    [InlineData("a`b.txt", CommandLineShell.PowerShell7, "'a`b.txt'")]
    [InlineData("-rf.txt", CommandLineShell.PowerShell7, "'-rf.txt'")]
    [InlineData("a;b.txt", CommandLineShell.PowerShell7, "'a;b.txt'")]
    [InlineData("árvíztűrő.txt", CommandLineShell.PowerShell7, "árvíztűrő.txt")]
    public void AName_IsQuotedForTheShell(string name, CommandLineShell shell, string expected)
        => Assert.Equal(expected, CommandShell.Quote(name, shell));

    [Fact]
    public void ANameTheShellWouldStillRead_IsRisky()
    {
        Assert.True(CommandShell.IsRisky("100%.txt", CommandLineShell.CommandPrompt));
        Assert.False(CommandShell.IsRisky("100%.txt", CommandLineShell.PowerShell7));
        Assert.True(CommandShell.IsRisky("photo[1].jpg", CommandLineShell.PowerShell7));
        Assert.False(CommandShell.IsRisky("photo[1].jpg", CommandLineShell.CommandPrompt));
        Assert.False(CommandShell.IsRisky("$it's.txt", CommandLineShell.WindowsPowerShell));
    }

    [Theory]
    [InlineData("cd ..", @"C:\a\b", @"C:\a")]
    [InlineData("cd..", @"C:\a\b", @"C:\a")]
    [InlineData(@"cd\", @"C:\a\b", @"C:\")]
    [InlineData("cd sub", @"C:\a", @"C:\a\sub")]
    [InlineData("chdir sub", @"C:\a", @"C:\a\sub")]
    [InlineData("CD /d D:\\Video", @"C:\a", @"D:\Video")]
    [InlineData("cd \"C:\\Program Files\"", @"D:\", @"C:\Program Files")]
    [InlineData("cd 'C:\\Program Files'", @"D:\", @"C:\Program Files")]
    [InlineData("cd ../x", @"C:\a\b", @"C:\a\x")]
    public void Cd_MovesThePanel(string text, string folder, string expected)
    {
        Assert.True(CommandShell.TryChangeDirectory(text, folder, out var target, out var drive));
        Assert.Equal(expected, target);
        Assert.False(drive);
    }

    [Fact]
    public void ADriveAlone_GoesToThatDrive_AndAnythingElseRunsInTheShell()
    {
        Assert.True(CommandShell.TryChangeDirectory(" d: ", @"C:\a", out var target, out var drive));
        Assert.Equal(@"D:\", target);
        Assert.True(drive);

        foreach (var text in new[] { "cd", "cdx", "cd x & dir", "cd %TEMP%", "cd $HOME", "cd *", "dir", "code .", "cd \"a\" \"b\"" })
        {
            Assert.False(CommandShell.TryChangeDirectory(text, @"C:\a", out _, out _));
        }
    }

    [Fact]
    public void TerminalProfiles_AreReadFromItsSettings_TheHiddenLeftOut()
    {
        const string json = """
            // Windows Terminal's settings: comments and trailing commas are allowed
            {
                "defaultProfile": "{574e775e-4f2a-5b96-ac1e-a2962a402336}",
                "profiles": {
                    "defaults": {},
                    "list": [
                        { "guid": "{574e775e-4f2a-5b96-ac1e-a2962a402336}", "name": "PowerShell", "source": "Windows.Terminal.PowershellCore" },
                        { "guid": "{0caa0dad-35be-5f56-a8ff-afceeeaa6101}", "name": "Command Prompt", "hidden": false },
                        { "guid": "{b453ae62-4e3d-5e58-b989-0a998ec441b8}", "name": "Azure Cloud Shell", "hidden": true },
                        { "name": "No id" },
                    ],
                },
            }
            """;
        var (profiles, defaultId) = TerminalSettings.Parse(json);
        Assert.Equal(["PowerShell", "Command Prompt"], profiles.Select(p => p.Name));
        Assert.Equal("{574e775e-4f2a-5b96-ac1e-a2962a402336}", defaultId);

        var (old, _) = TerminalSettings.Parse("""{ "profiles": [ { "guid": "{1}", "name": "Ubuntu" } ] }""");
        Assert.Equal("Ubuntu", Assert.Single(old).Name);
        Assert.Empty(TerminalSettings.Parse("not json").Profiles);
        Assert.Empty(TerminalSettings.Parse("[1, 2]").Profiles);
    }

    [Fact]
    public void TheSettings_AreKeptInRange_AndTheCommandsRememberedNewestFirst()
    {
        var odd = new AppSettings { CommandLine = new CommandLineSettings { Shell = (ShellKind)42, RunIn = (CommandLineShell)9, Remembered = 5000, Typing = (PanelTyping)7, TerminalProfile = " " } }.Normalize();
        Assert.Null(odd.CommandLine.Shell);
        Assert.Null(odd.CommandLine.RunIn);
        Assert.Equal(CommandLineSettings.MaxRemembered, odd.CommandLine.Remembered);
        Assert.Equal(PanelTyping.QuickSearch, odd.CommandLine.Typing);
        Assert.Null(odd.CommandLine.TerminalProfile);

        var list = HistorySettings.Push([new("dir", @"C:\"), new("git status", @"E:\Code")], new("dir", @"D:\"), max: 30);
        Assert.Equal([new("dir", @"D:\"), new("git status", @"E:\Code")], list);
        Assert.Equal([new CommandHistoryEntry("a", "")], HistorySettings.Push([new("b", "")], new("a", ""), max: 1));
        Assert.Empty(HistorySettings.Push([], new("a", ""), max: 0));
    }

    [Fact]
    public void YourOwnShellProgram_IsTakenFromAnImportOnlyWithPrograms_TheHistoryIsHistory()
    {
        Assert.Equal(SettingsPart.Programs, SettingsTransfer.PartOf("commandLine.program"));
        Assert.Equal(SettingsPart.General, SettingsTransfer.PartOf("commandLine.shell"));
        Assert.Equal(SettingsPart.General, SettingsTransfer.PartOf("commandLine.typing"));
        Assert.Equal(SettingsPart.History, SettingsTransfer.PartOf("history.commands"));
    }

    [Fact]
    public void TheSettings_GoThroughTheFile()
    {
        var dir = Directory.CreateTempSubdirectory("salandual-cmdline-").FullName;
        try
        {
            var store = SettingsStore.InDirectory(dir);
            var settings = new AppSettings
            {
                CommandLine = new CommandLineSettings { Shell = ShellKind.CommandPrompt, RunIn = CommandLineShell.WindowsPowerShell, CloseWhenDone = true, Remembered = 50, Typing = PanelTyping.CommandLine },
                History = new HistorySettings { Commands = [new("git status", @"E:\Code")] },
            };
            store.Save(settings);
            var loaded = store.Load().Settings;
            Assert.Equal(settings.CommandLine, loaded.CommandLine);
            Assert.Equal(settings.History.Commands, loaded.History.Commands);
            Assert.Contains("\"commandPrompt\"", File.ReadAllText(Directory.GetFiles(dir, "*.json")[0]), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
