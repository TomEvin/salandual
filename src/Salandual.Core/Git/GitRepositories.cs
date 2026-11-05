// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Git/GitRepositories.cs

namespace Salandual.Core.Git;

public static class GitRepositories
{
    private const int MaxLevels = 64;

    public static string? TopOf(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Path.IsPathFullyQualified(folder))
        {
            return null;
        }

        var dir = Path.TrimEndingDirectorySeparator(folder);
        if (dir.Split('\\').Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        for (var level = 0; level < MaxLevels && dir.Length > 0; level++)
        {
            var git = Path.Join(dir, ".git");
            try
            {
                if (Directory.Exists(git) || File.Exists(git))
                {
                    return dir;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            var up = Path.GetDirectoryName(dir);
            if (up is null || up.Length >= dir.Length)
            {
                break;
            }

            dir = Path.TrimEndingDirectorySeparator(up);
        }

        return null;
    }
}

public static class GitNames
{
    public static bool IsValidBranch(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "@" || name.Length > 250)
        {
            return false;
        }

        if (name[0] is '-' or '/' || name[^1] is '/' or '.' || name.Contains("..", StringComparison.Ordinal)
            || name.Contains("//", StringComparison.Ordinal) || name.Contains("@{", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (c < 0x20 || c == 0x7F || c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\')
            {
                return false;
            }
        }

        return name.Split('/').All(part => part.Length > 0 && part[0] != '.' && !part.EndsWith(".lock", StringComparison.OrdinalIgnoreCase));
    }
}

public interface IGitReader
{
    GitStatus? Status(string top);

    IReadOnlyList<GitBranch> Branches(string top);

    IReadOnlyList<string> Remotes(string top);

    IReadOnlyList<GitChange>? Changes(string top);

    byte[]? Version(string top, string path, GitVersion version, long limit);

    GitHeadFacts? Head(string top);

    GitLog? Log(string top, GitLogQuery query);

    IReadOnlyList<GitCommitChange>? CommitChanges(string top, string sha);

    byte[]? VersionAt(string top, string path, string sha, long limit);

    IReadOnlyList<GitTreeItem>? Tree(string top, string sha, string folder);

    bool SaveVersionAt(string top, string path, string sha, string target);

    (GitBlame? Blame, GitBlameRefusal Refusal) Blame(string top, string path, string? sha);

    IReadOnlyList<GitTag>? Tags(string top);

    IReadOnlyList<GitSubmodule> Submodules(string top);
}

public sealed record GitRun(int ExitCode, string Output, string Errors)
{
    public bool Succeeded => ExitCode == 0;

    public string Message => (Errors.Trim().Length > 0 ? Errors : Output).Trim();
}

public interface IGitCommands
{
    string? Program { get; }

    Task<GitRun> RunAsync(string top, IReadOnlyList<string> arguments, CancellationToken token = default);

    Task<GitRun> RunLongAsync(string folder, IReadOnlyList<string> arguments, Action<string> line, CancellationToken token);
}

public static class GitCommandLines
{
    public static IReadOnlyList<string> Switch(GitBranch branch)
        => branch.IsRemote ? ["switch", "--track", branch.Name] : ["switch", branch.Name];

    public static IReadOnlyList<string> NewBranch(string name, bool switchTo)
        => switchTo ? ["switch", "-c", name] : ["branch", name];

    public const int MaxPathChars = 20_000;

    public static IReadOnlyList<IReadOnlyList<string>> Stage(IEnumerable<string> paths) => WithPaths(["add", "-A"], paths);

    public static IReadOnlyList<string> StageAll() => ["add", "-A"];

    public static IReadOnlyList<IReadOnlyList<string>> Unstage(IEnumerable<string> paths, bool noCommitYet)
        => WithPaths(noCommitYet ? ["rm", "--cached", "-q"] : ["restore", "--staged"], paths);

    public static IReadOnlyList<IReadOnlyList<string>> DiscardToHead(IEnumerable<string> paths)
        => WithPaths(["restore", "--source=HEAD", "--staged", "--worktree"], paths);

    public static IReadOnlyList<IReadOnlyList<string>> DiscardAdded(IEnumerable<string> paths)
        => WithPaths(["rm", "--cached", "-q", "--ignore-unmatch"], paths);

    public static IReadOnlyList<string> Commit(string message, bool amend)
        => amend ? ["commit", "--amend", "-m", message] : ["commit", "-m", message];

    private static IReadOnlyList<IReadOnlyList<string>> WithPaths(string[] command, IEnumerable<string> paths)
    {
        var runs = new List<IReadOnlyList<string>>();
        List<string>? run = null;
        var chars = 0;
        foreach (var path in paths.Select(p => p.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (run is null || chars + path.Length + 3 > MaxPathChars)
            {
                run = ["--literal-pathspecs", .. command, "--"];
                runs.Add(run);
                chars = 0;
            }

            run.Add(path);
            chars += path.Length + 3;
        }

        return runs;
    }
}

public sealed class MemoryGitCommands(string? program = @"C:\Program Files\Git\cmd\git.exe") : IGitCommands
{
    private readonly List<(string Top, IReadOnlyList<string> Arguments)> _runs = [];

    public string? Program { get; set; } = program;

    public GitRun Answer { get; set; } = new(0, "", "");

    public Action<string, IReadOnlyList<string>>? OnRun { get; set; }

    public IReadOnlyList<(string Top, IReadOnlyList<string> Arguments)> Runs
    {
        get
        {
            lock (_runs)
            {
                return [.. _runs];
            }
        }
    }

    public Task<GitRun> RunAsync(string top, IReadOnlyList<string> arguments, CancellationToken token = default)
    {
        lock (_runs)
        {
            _runs.Add((top, arguments));
        }

        OnRun?.Invoke(top, arguments);
        return Task.FromResult(Answer);
    }

    public IReadOnlyList<string> Lines { get; set; } = [];

    public Task? Hold { get; set; }

    public async Task<GitRun> RunLongAsync(string folder, IReadOnlyList<string> arguments, Action<string> line, CancellationToken token)
    {
        lock (_runs)
        {
            _runs.Add((folder, arguments));
        }

        foreach (var l in Lines)
        {
            line(l);
        }

        if (Hold is { } hold)
        {
            await hold.WaitAsync(token).ConfigureAwait(false);
        }

        token.ThrowIfCancellationRequested();
        OnRun?.Invoke(folder, arguments);
        return Answer;
    }
}
