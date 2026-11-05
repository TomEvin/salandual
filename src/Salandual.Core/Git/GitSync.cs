// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Git/GitSync.cs

using System.Globalization;
using System.Text.RegularExpressions;
using Salandual.Core.Operations;

namespace Salandual.Core.Git;

public enum GitSyncKind { Fetch, Pull, Push, Clone }

public enum GitAddressProblem
{
    Empty,

    Password,

    NotSupported,
}

public readonly record struct GitProgress(string Line, int? Percent);

public static partial class GitSync
{
    public static IReadOnlyList<string> Fetch() => ["fetch", "--progress"];

    public static IReadOnlyList<string> Pull() => ["pull", "--progress"];

    public static IReadOnlyList<string> Push(bool hasUpstream, string? remote)
        => hasUpstream || remote is null ? ["push", "--progress"] : ["push", "--progress", "--set-upstream", remote, "HEAD"];

    public static IReadOnlyList<string> Clone(string address, string folder)
        => ["-c", "protocol.ext.allow=never", "clone", "--progress", "--", address.Trim(), folder];

    public static string? PushRemote(IReadOnlyList<string> remotes)
        => remotes.Contains("origin", StringComparer.Ordinal) ? "origin" : remotes.Count == 1 ? remotes[0] : null;

    public static GitAddressProblem? CheckAddress(string? address)
    {
        var a = (address ?? "").Trim();
        if (a.Length == 0)
        {
            return GitAddressProblem.Empty;
        }

        if (a.StartsWith('-') || a.Contains("::", StringComparison.Ordinal) || a.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            return GitAddressProblem.NotSupported;
        }

        if (Uri.TryCreate(a, UriKind.Absolute, out var uri) && a.Contains("://", StringComparison.Ordinal))
        {
            if (uri.Scheme is not ("https" or "http" or "ssh" or "git") || uri.Host.Length == 0)
            {
                return GitAddressProblem.NotSupported;
            }

            return uri.UserInfo.Contains(':', StringComparison.Ordinal) ? GitAddressProblem.Password : null;
        }

        return ScpLike().IsMatch(a) ? null : GitAddressProblem.NotSupported;
    }

    public static string? FolderNameOf(string? address)
    {
        var a = (address ?? "").Trim().TrimEnd('/', '\\');
        if (a.EndsWith("/.git", StringComparison.OrdinalIgnoreCase))
        {
            a = a[..^5];
        }

        var cut = a.LastIndexOfAny(['/', ':', '\\']);
        var name = cut >= 0 ? a[(cut + 1)..] : a;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or ".." ? null : name;
    }

    public static GitProgress? ParseProgress(string line)
    {
        var text = line.Trim();
        if (text.StartsWith("remote: ", StringComparison.Ordinal))
        {
            text = text[8..].Trim();
        }

        var m = ProgressLine().Match(text);
        if (!m.Success)
        {
            return null;
        }

        var percent = int.Parse(m.Groups["p"].Value, CultureInfo.InvariantCulture);
        return new GitProgress(text.TrimEnd(',', ' ').Replace(", done.", "", StringComparison.Ordinal), Math.Clamp(percent, 0, 100));
    }

    public static string WithoutProgress(string text)
        => string.Join("\n", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => ParseProgress(l) is null)).Trim();

    public static string Gist(string message)
    {
        var lines = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault(l => l.StartsWith("fatal:", StringComparison.Ordinal) || l.StartsWith("error:", StringComparison.Ordinal))
            ?? (lines.Length > 0 ? lines[^1] : "");
    }

    public static bool StoppedOnConflict(GitRun run)
        => !run.Succeeded && (run.Output.Contains("CONFLICT", StringComparison.Ordinal) || run.Errors.Contains("CONFLICT", StringComparison.Ordinal));

    public static string LastWords(GitRun run)
    {
        foreach (var text in new[] { run.Output, run.Errors })
        {
            var words = WithoutProgress(text);
            if (words.Length > 0)
            {
                return words.Split('\n')[^1];
            }
        }

        return "";
    }

    public static OperationJob Job(GitSyncKind kind, string title, string route, string folder, IReadOnlyList<string> arguments, IGitCommands git,
        Action<GitRun>? ended = null, OperationJob? before = null, string? waitWords = null)
    {
        return new OperationJob(OperationKind.Git, title, route, [], job =>
        {
            if (before is { IsFinished: false })
            {
                job.Report(OperationPhase.Working, 0, 0, 0, 100, waitWords, force: true);
                before.Completion.Wait(job.CancellationToken);
            }

            job.Checkpoint();
            job.Report(OperationPhase.Working, 0, 0, 0, 100, null, force: true);
            string? phase = null;
            var run = git.RunLongAsync(folder, arguments, line =>
            {
                if (ParseProgress(line) is { } p)
                {
                    var now = p.Line[..p.Line.IndexOf(':', StringComparison.Ordinal)];
                    job.Report(OperationPhase.Working, 0, 0, p.Percent ?? 0, 100, p.Line, force: now != phase);
                    phase = now;
                }
            }, job.CancellationToken).GetAwaiter().GetResult();
            ended?.Invoke(run);
            if (!run.Succeeded)
            {
                throw new GitRefusedException(WithoutProgress(run.Message) is { Length: > 0 } said ? said : run.Message);
            }

            job.Outcome = LastWords(run);
            job.Report(OperationPhase.Working, 0, 0, 100, 100, null, force: true);
        })
        {
            Subject = title,
            GitKind = kind,
        };
    }

    [GeneratedRegex(@"^[^\s@:/\\]+@[^\s@:/\\]+:[^\s]+$")]
    private static partial Regex ScpLike();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z ]*:\s+(?<p>\d{1,3})%")]
    private static partial Regex ProgressLine();
}

public sealed class GitRefusedException(string message) : Exception(message);
