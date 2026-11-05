// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/UserMenu/UserMenuRunner.cs

using System.Text;
using Salandual.Core.Operations;
using Salandual.Core.Resources;

namespace Salandual.Core.UserMenu;

public sealed record UserMenuStep(ProgramStart Start, string Subject, IReadOnlyList<string>? ListLines = null, bool Console = false);

public sealed record UserMenuPlan(UserMenuItem Item, IReadOnlyList<UserMenuStep> Steps, string ProgramName);

public static class UserMenuRunner
{
    public const string ListVariable = "SALANDUAL_FILES";

    public const int MaxCommandLine = 32000;

    private const string ReadList =
        "$__list = @([System.IO.File]::ReadAllLines($env:" + ListVariable + ", [System.Text.Encoding]::UTF8)); "
        + "Remove-Item -LiteralPath $env:" + ListVariable + " -ErrorAction SilentlyContinue; Remove-Item Env:\\" + ListVariable + " -ErrorAction SilentlyContinue\n";

    private const string ScriptPrelude = ReadList + "$Files = $__list; Remove-Variable __list\n";

    private const string ConsolePrelude = ReadList + "$__program = $__list[0]; $__arguments = @($__list | Select-Object -Skip 1); Remove-Variable __list\n& $__program @__arguments\n";

    public static UserMenuPlan? Plan(UserMenuItem item, UserMenuContext context, IProgramStarter starter, out string? error, string? arguments = null)
    {
        error = null;
        var takesAll = UserMenuVariables.TakesAll(item);
        var once = item.Runs == UserMenuRuns.Once || takesAll
            || item.Kind == UserMenuItemKind.Program && !UserMenuVariables.NamesAFile(item)
            || context.Selected.Count <= 1;
        var one = once && context.HasSelection && context.Selected.Count == 1 ? context.Selected[0] : context.Focused ?? context.Selected.FirstOrDefault();
        List<UserMenuEntry?> runFor = once ? [one] : [.. context.Selected];

        var steps = new List<UserMenuStep>();
        var programName = "";
        foreach (var file in runFor)
        {
            var folder = UserMenuVariables.Expand(item.StartIn, context, file, out error);
            if (error is not null)
            {
                return null;
            }

            folder = string.IsNullOrWhiteSpace(folder) ? context.ActivePath : folder;
            var subject = file?.Name ?? item.PlainName;
            UserMenuStep step;
            if (item.Kind == UserMenuItemKind.Script)
            {
                if (PowerShellFor(item.PowerShell, starter) is not { } shell)
                {
                    error = Strings.UserMenu_NoPowerShell;
                    return null;
                }

                programName = Path.GetFileNameWithoutExtension(shell);
                var files = once ? context.Selected.Select(e => e.FullName).ToList() : [file!.FullName];
                step = new UserMenuStep(PowerShellStart(shell, ScriptPrelude + item.Script, item.KeepWindowOpen, folder), subject, files);
            }
            else
            {
                var programText = UserMenuVariables.Expand(item.Program, context, file, out error);
                if (error is not null)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(programText))
                {
                    error = Strings.UserMenu_NoProgram;
                    return null;
                }

                var path = starter.Find(programText, out var failure);
                if (path is null)
                {
                    error = Loc.Format(failure == ProgramStartFailure.NotAProgram ? Strings.UserMenu_NotAProgram : Strings.UserMenu_NotFound, programText.Trim().Trim('"'));
                    return null;
                }

                var args = UserMenuVariables.ExpandArguments(arguments ?? item.Arguments, context, file, out error);
                if (args is null)
                {
                    return null;
                }

                programName = Path.GetFileNameWithoutExtension(path);
                step = item.KeepWindowOpen && starter.IsConsoleProgram(path) && PowerShellFor(UserMenuPowerShell.Pwsh, starter) is { } shell
                    ? new UserMenuStep(PowerShellStart(shell, ConsolePrelude, keepOpen: true, folder), subject, [path, .. args], Console: true)
                    : new UserMenuStep(new ProgramStart(path, args, folder), subject);
            }

            if (step.Start.Program.Length + step.Start.Arguments.Sum(a => a.Length + 3) > MaxCommandLine)
            {
                error = item.Kind == UserMenuItemKind.Script ? Strings.UserMenu_ScriptTooLong : Strings.UserMenu_TooLong;
                return null;
            }

            steps.Add(step);
        }

        return new UserMenuPlan(item, steps, programName);
    }

    public static IReadOnlyList<string> Shown(UserMenuStep step)
    {
        if (step.Console && step.ListLines is { } lines)
        {
            return lines;
        }

        var encoded = step.Start.Arguments.ToList().IndexOf("-EncodedCommand");
        return encoded < 0 ? [step.Start.Program, .. step.Start.Arguments] : [step.Start.Program, .. step.Start.Arguments.Take(encoded)];
    }

    private static string? PowerShellFor(UserMenuPowerShell which, IProgramStarter starter)
    {
        if (which == UserMenuPowerShell.Pwsh && starter.Find("pwsh.exe", out _) is { } pwsh)
        {
            return pwsh;
        }

        return starter.Find(starter.WindowsPowerShell, out _);
    }

    private static ProgramStart PowerShellStart(string shell, string script, bool keepOpen, string folder)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        List<string> args = ["-NoLogo", "-NoProfile"];
        if (keepOpen)
        {
            args.Add("-NoExit");
        }

        args.AddRange(["-EncodedCommand", encoded]);
        return new ProgramStart(shell, args, folder);
    }

    public static IRunningProgram? Start(UserMenuStep step, IProgramStarter starter, Func<string> newListFile, out ProgramStartFailure failure, out string? detail)
    {
        var start = step.Start;
        if (step.ListLines is { } lines)
        {
            var file = newListFile();
            try
            {
                File.WriteAllText(file, string.Concat(lines.Select(l => l + "\n")), new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failure = ProgramStartFailure.Failed;
                detail = e.Message;
                return null;
            }

            start = start with { Environment = new Dictionary<string, string> { [ListVariable] = file } };
        }

        return starter.Start(start, out failure, out detail);
    }

    public static OperationJob Job(UserMenuPlan plan, IProgramStarter starter, Func<string> newListFile)
    {
        var steps = plan.Steps;
        return new OperationJob(OperationKind.Run, plan.Item.PlainName, plan.ProgramName, [], job =>
        {
            for (var i = 0; i < steps.Count; i++)
            {
                job.Checkpoint();
                var step = steps[i];
                job.Report(OperationPhase.Working, 0, 0, i, steps.Count, step.Subject, force: true);
                using var running = Start(step, starter, newListFile, out var failure, out var detail);
                string? problem = null;
                if (running is null)
                {
                    problem = Loc.Format(Strings.UserMenu_DidNotStart, detail ?? failure.ToString());
                }
                else if (running.WaitForExit(job.CancellationToken) is { } code)
                {
                    if (code != 0)
                    {
                        problem = Loc.Format(Strings.UserMenu_ExitCode, code);
                    }
                }
                else
                {
                    job.Checkpoint();
                }

                if (problem is not null)
                {
                    job.AddIssue(step.Subject, problem);
                    if (plan.Item.OnFailure == UserMenuOnFailure.Stop)
                    {
                        break;
                    }
                }
            }

            job.Report(OperationPhase.Working, 0, 0, steps.Count, steps.Count, null, force: true);
        })
        {
            Subject = Loc.Plural(steps.Count, Strings.UserMenu_Files_One, Strings.UserMenu_Files_Other),
        };
    }
}
