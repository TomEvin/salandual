// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/ProgramStarter.cs

using System.ComponentModel;
using System.Diagnostics;
using Salandual.Core.UserMenu;

namespace Salandual.Platform.Windows.Shell;

public sealed class WindowsProgramStarter : IProgramStarter
{
    public string WindowsPowerShell { get; } = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    public string? Find(string program, out ProgramStartFailure failure)
    {
        var path = Programs.Resolve(program, out var problem);
        failure = problem switch
        {
            StartProblem.None => ProgramStartFailure.None,
            StartProblem.NotAProgram => ProgramStartFailure.NotAProgram,
            _ => ProgramStartFailure.NotFound,
        };
        return path;
    }

    public bool IsConsoleProgram(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var reader = new BinaryReader(file);
            if (file.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
            {
                return false;
            }

            file.Position = 0x3C;
            var pe = reader.ReadInt32();
            if (pe <= 0 || pe + 24 + 70 > file.Length)
            {
                return false;
            }

            file.Position = pe;
            if (reader.ReadUInt32() != 0x00004550)
            {
                return false;
            }

            file.Position = pe + 24 + 68;
            return reader.ReadUInt16() == 3;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IRunningProgram? Start(ProgramStart start, out ProgramStartFailure failure, out string? detail)
    {
        detail = null;
        if (Find(start.Program, out failure) is not { } path)
        {
            return null;
        }

        var info = new ProcessStartInfo(path) { UseShellExecute = false };
        foreach (var argument in start.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (start.Folder is { Length: > 0 } folder && Directory.Exists(folder))
        {
            info.WorkingDirectory = folder;
        }

        foreach (var (name, value) in start.Environment ?? new Dictionary<string, string>())
        {
            info.Environment[name] = value;
        }

        try
        {
            if (Process.Start(info) is { } process)
            {
                return new Running(process);
            }

            failure = ProgramStartFailure.Failed;
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            failure = ProgramStartFailure.Failed;
            detail = e.Message;
            return null;
        }
    }

    private sealed class Running(Process process) : IRunningProgram
    {
        public int? WaitForExit(CancellationToken token)
        {
            try
            {
                process.WaitForExitAsync(token).GetAwaiter().GetResult();
                return process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public void Dispose() => process.Dispose();
    }
}
