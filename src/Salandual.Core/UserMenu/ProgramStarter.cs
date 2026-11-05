// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/UserMenu/ProgramStarter.cs

namespace Salandual.Core.UserMenu;

public sealed record ProgramStart(string Program, IReadOnlyList<string> Arguments, string? Folder)
{
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

public enum ProgramStartFailure { None, NotFound, NotAProgram, Failed }

public interface IRunningProgram : IDisposable
{
    int? WaitForExit(CancellationToken token);
}

public interface IProgramStarter
{
    string? Find(string program, out ProgramStartFailure failure);

    string WindowsPowerShell { get; }

    bool IsConsoleProgram(string path);

    IRunningProgram? Start(ProgramStart start, out ProgramStartFailure failure, out string? detail);
}

public sealed class MemoryProgramStarter : IProgramStarter
{
    private readonly object _gate = new();
    private readonly List<ProgramStart> _started = [];

    public IReadOnlyList<ProgramStart> Started
    {
        get { lock (_gate) { return [.. _started]; } }
    }

    public Dictionary<string, string> Programs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ConsolePrograms { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Queue<int> ExitCodes { get; } = new();

    public ManualResetEventSlim? Hold { get; set; }

    public string WindowsPowerShell { get; set; } = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

    public string? Find(string program, out ProgramStartFailure failure)
    {
        var name = program.Trim().Trim('"');
        var found = Programs.TryGetValue(name, out var path) ? path
            : Path.IsPathFullyQualified(name) && (File.Exists(name) || Programs.ContainsValue(name)) ? name
            : null;
        failure = found is null ? ProgramStartFailure.NotFound
            : Path.GetExtension(found).ToLowerInvariant() is ".exe" or ".com" ? ProgramStartFailure.None : ProgramStartFailure.NotAProgram;
        return failure == ProgramStartFailure.None ? found : null;
    }

    public bool IsConsoleProgram(string path) => ConsolePrograms.Contains(path);

    public IRunningProgram? Start(ProgramStart start, out ProgramStartFailure failure, out string? detail)
    {
        failure = ProgramStartFailure.None;
        detail = null;
        int code;
        lock (_gate)
        {
            _started.Add(start);
            code = ExitCodes.Count > 0 ? ExitCodes.Dequeue() : 0;
        }

        return new Finished(code, Hold);
    }

    private sealed class Finished(int code, ManualResetEventSlim? hold) : IRunningProgram
    {
        public int? WaitForExit(CancellationToken token)
        {
            try
            {
                hold?.Wait(token);
                return code;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public void Dispose()
        {
        }
    }
}
