// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Git/GitProgram.cs

using System.Diagnostics;
using System.Text;
using Salandual.Core.Git;

namespace Salandual.Git;

public sealed class GitProgram : IGitCommands
{
    private static readonly TimeSpan _longest = TimeSpan.FromMinutes(2);
    private readonly Lazy<string?> _program;

    public GitProgram()
        : this(Find)
    {
    }

    internal GitProgram(Func<string?> find) => _program = new Lazy<string?>(find);

    public string? Program => _program.Value;

    public async Task<GitRun> RunAsync(string top, IReadOnlyList<string> arguments, CancellationToken token = default)
    {
        if (Program is not { } program)
        {
            return new GitRun(-1, "", "Git isn't installed.");
        }

        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = top,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = new Process { StartInfo = info };
        process.Start();
        process.StandardInput.Close();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(_longest);
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var errors = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return new GitRun(process.ExitCode, await output.ConfigureAwait(false), await errors.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }
    }

    public async Task<GitRun> RunLongAsync(string folder, IReadOnlyList<string> arguments, Action<string> line, CancellationToken token)
    {
        if (Program is not { } program)
        {
            return new GitRun(-1, "", "Git isn't installed.");
        }

        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_MERGE_AUTOEDIT"] = "no";
        using var process = new Process { StartInfo = info };
        process.Start();
        process.StandardInput.Close();
        var output = ReadLinesAsync(process.StandardOutput, line, token);
        var errors = ReadLinesAsync(process.StandardError, line, token);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return new GitRun(process.ExitCode, await output.ConfigureAwait(false), await errors.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }
    }

    internal static async Task<string> ReadLinesAsync(StreamReader reader, Action<string> line, CancellationToken token)
    {
        var all = new StringBuilder();
        var current = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                all.Append(c);
                if (c is '\r' or '\n')
                {
                    if (current.Length > 0)
                    {
                        line(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        if (current.Length > 0)
        {
            line(current.ToString());
        }

        return all.ToString();
    }

    internal static string? Find() => Find(Environment.GetEnvironmentVariable("PATH"), [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
    ], File.Exists);

    internal static string? Find(string? pathVariable, IEnumerable<string> programFolders, Func<string, bool> exists)
    {
        foreach (var folder in programFolders.Where(f => f.Length > 0))
        {
            var git = Path.Combine(folder, "Git", "cmd", "git.exe");
            if (exists(git))
            {
                return git;
            }
        }

        foreach (var part in (pathVariable ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var folder = part.Trim('"');
            if (!Path.IsPathFullyQualified(folder))
            {
                continue;
            }

            var git = Path.Combine(folder, "git.exe");
            if (exists(git))
            {
                return git;
            }
        }

        return null;
    }
}
