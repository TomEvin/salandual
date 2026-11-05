// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/UserMenuTests.cs

using System.Text;
using Salandual.Core.Operations;
using Salandual.Core.Settings;
using Salandual.Core.UserMenu;

namespace Salandual.Core.Tests;

public sealed class UserMenuTests
{
    private static UserMenuEntry F(string fullName) => new(fullName, false);

    private static UserMenuEntry D(string fullName) => new(fullName, true);

    private static UserMenuContext Context(params UserMenuEntry[] selected) => new()
    {
        ActivePath = @"D:\Video\Lake 2024",
        OtherPath = @"E:\Edits",
        Selected = selected,
        HasSelection = selected.Length > 1,
        Focused = selected.FirstOrDefault(),
        WindowsFolder = @"C:\Windows",
        SystemFolder = @"C:\Windows\system32",
        AppFolder = @"C:\Apps\Salandual\",
        EnvironmentVariable = n => n == "USERNAME" ? "me" : null,
        ShortPath = p => p.Replace("Lake 2024", "LAKE20~1", StringComparison.Ordinal),
    };

    private static MemoryProgramStarter Starter() => new()
    {
        Programs =
        {
            ["mpc-be64.exe"] = @"C:\Program Files\MPC-BE\mpc-be64.exe",
            [@"C:\Program Files\WinMerge\WinMergeU.exe"] = @"C:\Program Files\WinMerge\WinMergeU.exe",
            ["ffmpeg.exe"] = @"C:\Tools\ffmpeg.exe",
            ["pwsh.exe"] = @"C:\Program Files\PowerShell\7\pwsh.exe",
            [@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"] = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            ["notes.cmd"] = @"C:\Tools\notes.cmd",
        },
        ConsolePrograms = { @"C:\Tools\ffmpeg.exe" },
    };

    [Fact]
    public void TheFile_AsSalamanderNamesIt()
    {
        var file = F(@"D:\Video\Lake 2024\day2-swim.mp4");
        var context = Context(file);
        string Fill(string text) => UserMenuVariables.Expand(text, context, file, out var error) ?? error!;
        Assert.Equal(@"D:\Video\Lake 2024\day2-swim.mp4", Fill("$(FullName)"));
        Assert.Equal("day2-swim.mp4", Fill("$(Name)"));
        Assert.Equal("day2-swim", Fill("$(NamePart)"));
        Assert.Equal("mp4", Fill("$(ExtPart)"));
        Assert.Equal(@"D:\Video\Lake 2024\", Fill("$(FullPath)"));
        Assert.Equal("D:", Fill("$(Drive)"));
        Assert.Equal(@"\Video\Lake 2024\", Fill("$(Path)"));
        Assert.Equal(@"D:\Video\LAKE20~1\day2-swim.mp4", Fill("$(DOSFullName)"));
        Assert.Equal(@"\Video\LAKE20~1\", Fill("$(DOSPath)"));
        Assert.Equal(@"E:\Edits\", Fill("$(FullPathInactive)"));
        Assert.Equal(@"D:\Video\Lake 2024\", Fill("$(FullPathLeft)"));
        Assert.Equal(@"E:\Edits\", Fill("$(FullPathRight)"));
        Assert.Equal(@"C:\Windows\", Fill("$(WinDir)"));
        Assert.Equal(@"C:\Windows\system32\", Fill("$(SysDir)"));
        Assert.Equal(@"C:\Apps\Salandual\", Fill("$(SalDir)"));
        Assert.Equal("me", Fill("$[USERNAME]"));
        Assert.Equal(".gitignore", UserMenuVariables.Expand("$(NamePart)", context, F(@"D:\a\.gitignore"), out _));
        Assert.Equal("", UserMenuVariables.Expand("$(ExtPart)", context, F(@"D:\a\README"), out _));
    }

    [Fact]
    public void Arguments_AreSplitFirst_SoANameStaysOneArgument()
    {
        var tricky = F(@"D:\Video\it's ""quoted"" & a; rm -r.mp4");
        var args = UserMenuVariables.ExpandArguments("/u \"$(FullName)\" --title=$(NamePart)", Context(tricky), tricky, out var error);
        Assert.Null(error);
        Assert.Equal(["/u", tricky.FullName, "--title=it's \"quoted\" & a; rm -r"], args);

        var odd = F(@"D:\a\$(SysDir).txt");
        Assert.Equal([odd.FullName], UserMenuVariables.ExpandArguments("$(FullName)", Context(odd), odd, out _));
    }

    [Fact]
    public void Lists_GiveOneArgumentEach_AndMustStandAlone()
    {
        var context = Context(F(@"D:\v\a b.mp4"), F(@"D:\v\c.mp4"));
        Assert.Equal(["-x", @"D:\v\a b.mp4", @"D:\v\c.mp4"], UserMenuVariables.ExpandArguments("-x \"$(ListOfSelectedFullNames)\"", context, null, out _));
        Assert.Equal(["a b.mp4", "c.mp4"], UserMenuVariables.ExpandArguments("$(ListOfSelectedNames)", context, null, out _));
        Assert.Null(UserMenuVariables.ExpandArguments("--files=$(ListOfSelectedNames)", context, null, out var error));
        Assert.Equal("$(ListOfSelectedNames) has to be an argument of its own: it gives one argument for each selected item.", error);
        Assert.Equal("$(ListOfSelectedNames) can only be in the arguments.", UserMenuVariables.Check("$(ListOfSelectedNames)", arguments: false));
    }

    [Fact]
    public void Mistakes_AreNamed()
    {
        Assert.Equal("There is no variable $(FileNme).", UserMenuVariables.Check("\"$(FileNme)\"", true));
        Assert.Equal("A $( has no ) after it.", UserMenuVariables.Check("$(FullName", true));
        Assert.Null(UserMenuVariables.Check("$(FullName) $[ANY] $(FileOrDirToCompareInactive) costs $5", true));
        Assert.Null(UserMenuVariables.Expand("$[NOPE]", Context(), null, out var error));
        Assert.Equal("There is no environment variable NOPE.", error);
        Assert.Null(UserMenuVariables.Expand("$(FullName)", Context(), null, out error));
        Assert.Equal("$(FullName) needs a file or folder: focus one first.", error);
    }

    [Fact]
    public void TwoToCompare_BySalamandersRules()
    {
        var a = F(@"D:\v\a.txt");
        var b = F(@"D:\v\b.txt");
        var other = F(@"E:\x\a.txt");
        string Fill(UserMenuContext c, string v) => UserMenuVariables.Expand(v, c, null, out var error) ?? error!;

        var two = Context(a, b);
        Assert.Equal(a.FullName, Fill(two, "$(FileToCompareLeft)"));
        Assert.Equal(b.FullName, Fill(two, "$(FileToCompareRight)"));

        var cross = Context(a) with { OtherFocused = other };
        Assert.Equal(a.FullName, Fill(cross, "$(FileToCompareLeft)"));
        Assert.Equal(other.FullName, Fill(cross, "$(FileToCompareRight)"));
        Assert.Equal(other.FullName, Fill(cross with { ActiveIsLeft = false }, "$(FileToCompareLeft)"));
        Assert.Equal(a.FullName, Fill(cross with { ActiveIsLeft = false }, "$(FileToCompareActive)"));
        Assert.Equal(other.FullName, Fill(cross with { ActiveIsLeft = false }, "$(FileToCompareInactive)"));

        var selectedAndFocused = Context(a) with { HasSelection = true, Focused = b, OtherFocused = other };
        Assert.Equal(b.FullName, Fill(selectedAndFocused, "$(FileToCompareRight)"));

        var sameName = Context(a) with { OtherNamed = n => n == "a.txt" ? other : null };
        Assert.Equal(other.FullName, Fill(sameName, "$(FileOrDirToCompareRight)"));

        Assert.Equal("Two items to compare are needed: select two here, or focus one here and one in the other panel.", Fill(Context(a), "$(FileToCompareLeft)"));
        Assert.Equal("Two folders to compare are needed, not files.", Fill(two, "$(DirToCompareLeft)"));
        Assert.Equal(@"D:\v\x", Fill(Context(D(@"D:\v\x"), D(@"D:\v\y")), "$(DirToCompareLeft)"));
    }

    [Fact]
    public void ShownFor_FilesFoldersNamesKindsAndHowMany()
    {
        var video = new UserMenuItem { Program = "ffmpeg.exe", ShowsFor = UserMenuShowsFor.Files, FileKind = UserMenuFileKind.Video };
        Assert.Null(UserMenuFit.WhyNot(video, [F(@"D:\a.mp4"), F(@"D:\b.MKV")]));
        Assert.Equal("For video only.", UserMenuFit.WhyNot(video, [F(@"D:\a.mp4"), F(@"D:\c.txt")]));
        Assert.Equal("For files only.", UserMenuFit.WhyNot(video, [D(@"D:\Stills")]));
        Assert.Equal("For files or folders: none is focused.", UserMenuFit.WhyNot(video, []));

        var named = new UserMenuItem { Program = "x.exe", Named = "*.mp4;*.mkv" };
        Assert.Null(UserMenuFit.WhyNot(named, [F(@"D:\a.mp4")]));
        Assert.Equal("For *.mp4;*.mkv only.", UserMenuFit.WhyNot(named, [F(@"D:\a.avi")]));

        var pair = new UserMenuItem { Program = "x.exe", Count = UserMenuCount.Two };
        Assert.Equal("For exactly two items.", UserMenuFit.WhyNot(pair, [F(@"D:\a")]));
        Assert.Null(UserMenuFit.WhyNot(pair, [F(@"D:\a"), F(@"D:\b")]));

        Assert.Null(UserMenuFit.WhyNot(new UserMenuItem { Program = "wt.exe" }, []));
        Assert.Equal("For folders only.", UserMenuFit.WhyNot(new UserMenuItem { ShowsFor = UserMenuShowsFor.Folders }, [F(@"D:\a")]));
    }

    [Fact]
    public void AProgram_RunsOnceForEachFile_AsSalamander()
    {
        var item = new UserMenuItem { Name = "&Play", Program = "mpc-be64.exe", Arguments = "\"$(FullName)\" /play" };
        var a = F(@"D:\v\a.mp4");
        var b = F(@"D:\v\b.mp4");
        var plan = UserMenuRunner.Plan(item, Context(a, b), Starter(), out var error);
        Assert.Null(error);
        Assert.Equal("mpc-be64", plan!.ProgramName);
        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(@"C:\Program Files\MPC-BE\mpc-be64.exe", plan.Steps[0].Start.Program);
        Assert.Equal([a.FullName, "/play"], plan.Steps[0].Start.Arguments);
        Assert.Equal([b.FullName, "/play"], plan.Steps[1].Start.Arguments);
        Assert.Equal(@"D:\v\", plan.Steps[0].Start.Folder);
        Assert.Equal("b.mp4", plan.Steps[1].Subject);

        Assert.Single(UserMenuRunner.Plan(item with { Runs = UserMenuRuns.Once }, Context(a, b), Starter(), out _)!.Steps);
        var list = UserMenuRunner.Plan(item with { Arguments = "$(ListOfSelectedFullNames)" }, Context(a, b), Starter(), out _)!;
        Assert.Equal([a.FullName, b.FullName], Assert.Single(list.Steps).Start.Arguments);

        var terminal = UserMenuRunner.Plan(new UserMenuItem { Program = "mpc-be64.exe", StartIn = "" }, Context(a, b), Starter(), out _)!;
        Assert.Equal(@"D:\Video\Lake 2024", Assert.Single(terminal.Steps).Start.Folder);
    }

    [Fact]
    public void AProgram_FoundOnlyAsAProgram()
    {
        var a = F(@"D:\v\a.mp4");
        Assert.Null(UserMenuRunner.Plan(new UserMenuItem { Program = "nothere.exe" }, Context(a), Starter(), out var error));
        Assert.Equal("nothere.exe wasn't found. Give its full path in Options › User menu.", error);
        Assert.Null(UserMenuRunner.Plan(new UserMenuItem { Program = "notes.cmd", Arguments = "$(FullName)" }, Context(a), Starter(), out error));
        Assert.Equal("notes.cmd isn't a program: only .exe and .com files are started. A script goes in as a PowerShell script.", error);
        Assert.Null(UserMenuRunner.Plan(new UserMenuItem(), Context(a), Starter(), out error));
        Assert.Equal("The item has no program.", error);
    }

    [Fact]
    public void AScript_GetsTheFilesInAList_NeverInItsText()
    {
        var item = new UserMenuItem { Kind = UserMenuItemKind.Script, Runs = UserMenuRuns.Once, Script = "Get-FileHash -LiteralPath $Files", KeepWindowOpen = true };
        var a = F(@"D:\v\a'; rm -r *.txt");
        var b = F(@"D:\v\b.mp4");
        var plan = UserMenuRunner.Plan(item, Context(a, b), Starter(), out var error)!;
        Assert.Null(error);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", step.Start.Program);
        Assert.Equal(["-NoLogo", "-NoProfile", "-NoExit", "-EncodedCommand"], step.Start.Arguments.Take(4));
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(step.Start.Arguments[4]));
        Assert.EndsWith("\nGet-FileHash -LiteralPath $Files", script, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -r", script, StringComparison.Ordinal);
        Assert.Equal([a.FullName, b.FullName], step.ListLines);
        Assert.Equal([step.Start.Program, "-NoLogo", "-NoProfile", "-NoExit"], UserMenuRunner.Shown(step));

        var starter = Starter();
        starter.Programs.Remove("pwsh.exe");
        var each = UserMenuRunner.Plan(item with { Runs = UserMenuRuns.EachFile, KeepWindowOpen = false }, Context(a, b), starter, out _)!;
        Assert.Equal(2, each.Steps.Count);
        Assert.Equal(starter.WindowsPowerShell, each.Steps[0].Start.Program);
        Assert.DoesNotContain("-NoExit", each.Steps[0].Start.Arguments);
        Assert.Equal([b.FullName], each.Steps[1].ListLines);
    }

    [Fact]
    public void AConsoleProgramKeptOpen_StartsInPowerShell_WithItselfInTheList()
    {
        var a = F(@"D:\v\a.mp4");
        var plan = UserMenuRunner.Plan(new UserMenuItem { Program = "ffmpeg.exe", Arguments = "-i \"$(FullName)\"", KeepWindowOpen = true }, Context(a), Starter(), out _)!;
        var step = Assert.Single(plan.Steps);
        Assert.True(step.Console);
        Assert.EndsWith("pwsh.exe", step.Start.Program, StringComparison.Ordinal);
        Assert.Equal([@"C:\Tools\ffmpeg.exe", "-i", a.FullName], step.ListLines);
        Assert.Equal([@"C:\Tools\ffmpeg.exe", "-i", a.FullName], UserMenuRunner.Shown(step));

        var closed = UserMenuRunner.Plan(new UserMenuItem { Program = "ffmpeg.exe", Arguments = "-i \"$(FullName)\"", KeepWindowOpen = false }, Context(a), Starter(), out _)!;
        Assert.Equal(@"C:\Tools\ffmpeg.exe", Assert.Single(closed.Steps).Start.Program);
    }

    [Fact]
    public void Start_WritesTheListFile_AndNamesItInTheEnvironment()
    {
        var dir = Directory.CreateTempSubdirectory("salandual-usermenu-").FullName;
        try
        {
            var starter = Starter();
            var step = new UserMenuStep(new ProgramStart(@"C:\Program Files\PowerShell\7\pwsh.exe", ["-NoProfile"], @"D:\"), "a", [@"D:\ä b.txt", ""]);
            using var running = UserMenuRunner.Start(step, starter, () => Path.Combine(dir, "list.txt"), out var failure, out _);
            Assert.Equal(ProgramStartFailure.None, failure);
            var started = Assert.Single(starter.Started);
            var list = started.Environment![UserMenuRunner.ListVariable];
            Assert.Equal([@"D:\ä b.txt", ""], File.ReadAllLines(list, Encoding.UTF8));
            Assert.False(File.ReadAllBytes(list).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ForEachFile_AJob_IssuesForExitCodes_StopsWhenAsked()
    {
        var item = new UserMenuItem { Name = "Remux", Program = "ffmpeg.exe", Arguments = "\"$(FullName)\"", KeepWindowOpen = false };
        var files = new[] { F(@"D:\v\a.mp4"), F(@"D:\v\b.mp4"), F(@"D:\v\c.mp4") };
        var starter = Starter();
        starter.ExitCodes.Enqueue(1);
        var job = UserMenuRunner.Job(UserMenuRunner.Plan(item, Context(files), starter, out _)!, starter, () => throw new InvalidOperationException());
        Assert.Equal(OperationKind.Run, job.Kind);
        Assert.Equal("Remux", job.Title);
        job.RunHere();
        await job.Completion;
        Assert.Equal(JobState.DoneWithIssues, job.State);
        Assert.Equal(3, starter.Started.Count);
        var issue = Assert.Single(job.Issues);
        Assert.Equal(("a.mp4", "Ended with exit code 1."), (issue.Path, issue.Message));

        var stopping = Starter();
        stopping.ExitCodes.Enqueue(2);
        var stopJob = UserMenuRunner.Job(UserMenuRunner.Plan(item with { OnFailure = UserMenuOnFailure.Stop }, Context(files), stopping, out _)!, stopping, () => "");
        stopJob.RunHere();
        await stopJob.Completion;
        Assert.Single(stopping.Started);
    }

    [Fact]
    public async Task Stop_StartsNoMore_AndLeavesTheRunningOneAlone()
    {
        var item = new UserMenuItem { Program = "ffmpeg.exe", Arguments = "\"$(FullName)\"", KeepWindowOpen = false };
        var starter = Starter();
        using var hold = new ManualResetEventSlim(false);
        starter.Hold = hold;
        var queue = new OperationQueue();
        var job = UserMenuRunner.Job(UserMenuRunner.Plan(item, Context(F(@"D:\a.mp4"), F(@"D:\b.mp4")), starter, out _)!, starter, () => "");
        queue.Add(job);
        for (var i = 0; i < 200 && starter.Started.Count == 0; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        job.Cancel();
        await job.Completion;
        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Single(starter.Started);
    }

    [Fact]
    public void Settings_KeepTheTree_AndAreProgramsForImport()
    {
        var menu = new UserMenuSettings
        {
            Items =
            [
                new UserMenuItem { Name = "&Play", Program = "mpc-be64.exe", Arguments = "\"$(FullName)\"", Key = " Ctrl+Alt+P " },
                new UserMenuItem { Kind = UserMenuItemKind.Separator },
                new UserMenuItem { Name = "Video", Kind = UserMenuItemKind.Submenu, Items = [new UserMenuItem { Name = "Remux", Program = "ffmpeg.exe" }] },
                new UserMenuItem { Name = "Not a submenu", Items = [new UserMenuItem { Name = "lost" }] },
            ],
        };
        var settings = (new AppSettings { UserMenu = menu }).Normalize();
        Assert.Equal("Ctrl+Alt+P", settings.UserMenu.Items[0].Key);
        Assert.Equal("Play", settings.UserMenu.Items[0].PlainName);
        Assert.Equal("Remux", Assert.Single(settings.UserMenu.Items[2].Items).Name);
        Assert.Empty(settings.UserMenu.Items[3].Items);
        Assert.Equal(["&Play", "", "Video", "Remux", "Not a submenu"], settings.UserMenu.All().Select(i => i.Name));

        Assert.Equal(SettingsPart.Programs, SettingsTransfer.PartOf("userMenu.items"));
        Assert.Equal(SettingsPart.Programs, SettingsTransfer.PartOf("userMenu.unfit"));
    }

    [Fact]
    public void AnImportedUserMenu_ComesOnlyWithItsProgramsTicked()
    {
        var dir = Directory.CreateTempSubdirectory("salandual-usermenu-import-").FullName;
        try
        {
            var theirs = new AppSettings { UserMenu = new UserMenuSettings { Items = [new UserMenuItem { Name = "Run me", Program = @"C:\Their\tool.exe" }] } }.Normalize();
            var file = Path.Combine(dir, "theirs" + SettingsTransfer.Extension);
            File.WriteAllText(file, SettingsTransfer.Export(theirs, new ExportChoice(), new DateTime(2026, 10, 27), "test"));
            var import = SettingsTransfer.Read(file, new AppSettings().Normalize());
            var change = Assert.Single(import.Changes, c => c.Part == SettingsPart.Programs);
            Assert.Equal("User menu", change.Name);
            Assert.DoesNotContain(SettingsPart.Programs, import.Same);

            Assert.Empty(import.Apply(new HashSet<SettingsPart> { SettingsPart.Appearance, SettingsPart.Advanced }).UserMenu.Items);
            Assert.Equal("Run me", Assert.Single(import.Apply(new HashSet<SettingsPart> { SettingsPart.Programs }).UserMenu.Items).Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
