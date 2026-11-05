// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Git/LibGit2Reader.cs

using LibGit2Sharp;
using Salandual.Core.Git;

namespace Salandual.Git;

public sealed class LibGit2Reader : IGitReader
{
    private static readonly Lock _initLock = new();
    private static bool? _ready;

    public GitStatus? Status(string top)
    {
        if (!Ready())
        {
            return null;
        }

        try
        {
            using var repo = new Repository(top);
            if (repo.Info.IsBare || repo.Info.WorkingDirectory is null)
            {
                return null;
            }

            var options = new StatusOptions
            {
                Show = StatusShowOption.IndexAndWorkDir,
                IncludeUntracked = true,
                RecurseUntrackedDirs = false,
                IncludeIgnored = true,
                RecurseIgnoredDirs = false,
                DetectRenamesInIndex = false,
                DetectRenamesInWorkDir = false,
                ExcludeSubmodules = true,
                IncludeUnaltered = false,
            };
            var entries = new List<(string, GitState)>();
            foreach (var entry in repo.RetrieveStatus(options))
            {
                entries.Add((entry.FilePath, StateOf(entry.State)));
            }

            var (branch, detached, upstream, ahead, behind) = HeadOf(repo);
            var subs = repo.Submodules.Any() ? SubmodulesOf(repo) : [];
            foreach (var sub in subs.Where(s => s.State is GitSubmoduleState.OtherCommit or GitSubmoduleState.ChangesInside))
            {
                entries.Add((sub.Path, GitState.Changed));
            }

            return GitStatus.From(Path.TrimEndingDirectorySeparator(repo.Info.WorkingDirectory), entries, branch, detached, upstream, ahead, behind, subs);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Warn("Git: A repository couldn't be read: " + ex.GetType().Name);
            return null;
        }
    }

    public IReadOnlyList<string> Remotes(string top)
    {
        if (!Ready())
        {
            return [];
        }

        try
        {
            using var repo = new Repository(top);
            return [.. repo.Network.Remotes.Select(r => r.Name)];
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return [];
        }
    }

    public IReadOnlyList<GitBranch> Branches(string top)
    {
        if (!Ready())
        {
            return [];
        }

        try
        {
            using var repo = new Repository(top);
            var local = new List<GitBranch>();
            var remote = new List<GitBranch>();
            var tracked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in repo.Branches.Where(b => !b.IsRemote))
            {
                var details = b.IsTracking ? b.TrackingDetails : null;
                local.Add(new GitBranch(b.FriendlyName, false, b.IsCurrentRepositoryHead, details?.AheadBy ?? 0, details?.BehindBy ?? 0));
                if (b.IsTracking && b.TrackedBranch is { } up)
                {
                    tracked.Add(up.FriendlyName);
                }
            }

            var localNames = local.Select(b => b.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var b in repo.Branches.Where(b => b.IsRemote))
            {
                var name = b.FriendlyName;
                var cut = name.IndexOf('/', StringComparison.Ordinal);
                var shortName = cut > 0 ? name[(cut + 1)..] : name;
                if (b.CanonicalName.EndsWith("/HEAD", StringComparison.Ordinal) || tracked.Contains(name) || localNames.Contains(shortName))
                {
                    continue;
                }

                remote.Add(new GitBranch(name, true, false));
            }

            return [.. local.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase), .. remote.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Warn("Git: A repository's branches couldn't be read: " + ex.GetType().Name);
            return [];
        }
    }

    internal const int MaxChanges = 5000;

    internal const int MaxCounted = 400;

    private const FileStatus InIndex = FileStatus.NewInIndex | FileStatus.ModifiedInIndex | FileStatus.DeletedFromIndex | FileStatus.RenamedInIndex | FileStatus.TypeChangeInIndex;
    private const FileStatus InWorkdir = FileStatus.ModifiedInWorkdir | FileStatus.DeletedFromWorkdir | FileStatus.TypeChangeInWorkdir | FileStatus.RenamedInWorkdir;

    public IReadOnlyList<GitChange>? Changes(string top)
    {
        if (!Ready())
        {
            return null;
        }

        try
        {
            using var repo = new Repository(top);
            if (repo.Info.IsBare || repo.Info.WorkingDirectory is null)
            {
                return null;
            }

            var options = new StatusOptions
            {
                Show = StatusShowOption.IndexAndWorkDir,
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
                IncludeIgnored = false,
                DetectRenamesInIndex = false,
                DetectRenamesInWorkDir = false,
                ExcludeSubmodules = true,
                IncludeUnaltered = false,
            };
            var root = Path.TrimEndingDirectorySeparator(repo.Info.WorkingDirectory);
            var tip = repo.Head.Tip;
            var list = new List<GitChange>();
            foreach (var entry in repo.RetrieveStatus(options))
            {
                var s = entry.State;
                if ((s & (FileStatus.Ignored | FileStatus.Nonexistent)) != 0 || s == FileStatus.Unaltered)
                {
                    continue;
                }

                var path = entry.FilePath.Replace('/', '\\');
                var staged = (s & InIndex) != 0;
                var group = (s & FileStatus.Conflicted) != 0 ? GitChangeGroup.Conflicted
                    : (s & FileStatus.NewInWorkdir) != 0 ? GitChangeGroup.New
                    : (s & InWorkdir) != 0 ? GitChangeGroup.Changed
                    : GitChangeGroup.Staged;
                var onDisk = File.Exists(Path.Join(root, path));
                list.Add(new GitChange(path, group, StateOf(s), null, null, tip?[entry.FilePath] is not null, staged, onDisk));
                if (list.Count >= MaxChanges)
                {
                    break;
                }
            }

            return list.Count <= MaxCounted ? Counted(repo, root, list) : list;
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Warn("Git: A repository's changes couldn't be read: " + ex.GetType().Name);
            return null;
        }
    }

    private static List<GitChange> Counted(Repository repo, string root, List<GitChange> list)
    {
        var tree = repo.Head.Tip?.Tree;
        var exact = new ExplicitPathsOptions { ShouldFailOnUnmatchedPath = false };
        PatchStats? Stats(GitChangeGroup group, DiffTargets targets)
        {
            var paths = list.Where(c => c.Group == group).Select(c => c.Path.Replace('\\', '/')).ToList();
            if (paths.Count == 0)
            {
                return null;
            }

            try
            {
                return repo.Diff.Compare<PatchStats>(tree, targets, paths, exact, new CompareOptions { ContextLines = 0 });
            }
            catch (Exception ex) when (ex is LibGit2SharpException or ArgumentException or InvalidOperationException)
            {
                return null;
            }
        }

        var staged = Stats(GitChangeGroup.Staged, DiffTargets.Index);
        var changed = Stats(GitChangeGroup.Changed, DiffTargets.Index | DiffTargets.WorkingDirectory);
        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            var stats = c.Group switch { GitChangeGroup.Staged => staged, GitChangeGroup.Changed => changed, _ => null };
            if ((stats?[c.Path] ?? stats?[c.Path.Replace('\\', '/')]) is { } line && line.LinesAdded + line.LinesDeleted > 0)
            {
                list[i] = c with { Added = line.LinesAdded, Removed = line.LinesDeleted };
            }
            else if (c.Group == GitChangeGroup.New && LinesOf(Path.Join(root, c.Path)) is { } lines)
            {
                list[i] = c with { Added = lines, Removed = 0 };
            }
        }

        return list;
    }

    internal static int? LinesOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 8 << 20 || (info.Attributes & (FileAttributes.Offline | FileAttributes.ReparsePoint)) != 0 || ((int)info.Attributes & 0x00400000) != 0)
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            if (Array.IndexOf(bytes, (byte)0) >= 0)
            {
                return null;
            }

            var lines = bytes.Count(b => b == (byte)'\n');
            return bytes.Length > 0 && bytes[^1] != (byte)'\n' ? lines + 1 : lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public byte[]? Version(string top, string path, GitVersion version, long limit)
    {
        if (!Ready())
        {
            return null;
        }

        try
        {
            using var repo = new Repository(top);
            var gitPath = path.Replace('\\', '/');
            var blob = version == GitVersion.Head
                ? repo.Head.Tip?[gitPath]?.Target as Blob
                : repo.Index[gitPath] is { } entry ? repo.Lookup<Blob>(entry.Id) : null;
            if (blob is null || blob.Size > limit)
            {
                return null;
            }

            using var content = blob.GetContentStream(new FilteringOptions(gitPath));
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            return copy.Length > limit ? null : copy.ToArray();
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Warn("Git: A file's committed version couldn't be read: " + ex.GetType().Name);
            return null;
        }
    }

    public GitHeadFacts? Head(string top)
    {
        if (!Ready())
        {
            return null;
        }

        try
        {
            using var repo = new Repository(top);
            var name = repo.Config.Get<string>("user.name")?.Value;
            var email = repo.Config.Get<string>("user.email")?.Value;
            var author = string.IsNullOrWhiteSpace(name) ? null : string.IsNullOrWhiteSpace(email) ? name.Trim() : name.Trim() + " <" + email.Trim() + ">";
            if (repo.Info.IsHeadUnborn || repo.Head.Tip is not { } tip)
            {
                return new GitHeadFacts(null, null, false, author, []);
            }

            var head = repo.Head;
            var pushed = head.IsTracking && head.TrackedBranch?.Tip is { } up
                && (up.Sha == tip.Sha || repo.ObjectDatabase.FindMergeBase(up, tip)?.Sha == tip.Sha);
            var recent = repo.Commits.Take(30).Select(c => c.Message.TrimEnd()).Where(m => m.Length > 0).Distinct(StringComparer.Ordinal).Take(10).ToList();
            return new GitHeadFacts(tip.Sha[..7], tip.Message.TrimEnd(), pushed, author, recent);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Warn("Git: The last commit couldn't be read: " + ex.GetType().Name);
            return null;
        }
    }

    internal const int MaxWalked = 50_000;

    internal static bool IsSha(string? sha) => sha is { Length: >= 7 and <= 64 } && sha.All(Uri.IsHexDigit);

    public GitLog? Log(string top, GitLogQuery query)
    {
        if (!Ready())
        {
            return null;
        }

        try
        {
            using var repo = new Repository(top);
            if (repo.Info.IsHeadUnborn || repo.Head.Tip is not { } tip)
            {
                return new GitLog([], null, false);
            }

            var refs = RefsOf(repo);
            var head = repo.Head;
            var upstream = head.IsTracking ? head.TrackedBranch?.Tip : null;
            var notPushed = new HashSet<string>(StringComparer.Ordinal);
            if (upstream is not null && !query.AllBranches)
            {
                foreach (var c in repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = tip, ExcludeReachableFrom = upstream }).Take(query.Limit + 1))
                {
                    notPushed.Add(c.Sha);
                }
            }

            object from = query.AllBranches ? Tips(repo) : tip;
            var filter = new CommitFilter
            {
                IncludeReachableFrom = from,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
                FirstParentOnly = query.FirstParentOnly,
            };
            var gitPath = query.Path is { Length: > 0 } p ? p.Replace('\\', '/').TrimEnd('/') : null;
            var list = new List<GitCommit>();
            var walked = 0;
            var more = false;
            foreach (var c in repo.Commits.QueryBy(filter))
            {
                if (++walked > MaxWalked)
                {
                    more = true;
                    break;
                }

                if (gitPath is not null && !Touches(c, gitPath))
                {
                    continue;
                }

                if (list.Count == query.Limit)
                {
                    more = true;
                    break;
                }

                list.Add(new GitCommit(
                    c.Sha,
                    c.MessageShort,
                    c.Message.TrimEnd(),
                    c.Author.Name,
                    c.Author.Email,
                    c.Author.When,
                    [.. c.Parents.Select(parent => parent.Sha)],
                    refs.TryGetValue(c.Sha, out var named) ? named : [],
                    upstream is not null && !query.AllBranches && !notPushed.Contains(c.Sha)));
            }

            return new GitLog(list, head.IsTracking ? head.TrackedBranch?.FriendlyName : null, more);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Warn("Git: A repository's history couldn't be read: " + ex.GetType().Name);
            return null;
        }
    }

    private static bool Touches(Commit c, string gitPath)
    {
        var mine = c[gitPath]?.Target.Id;
        var parent = c.Parents.FirstOrDefault();
        return parent is null ? mine is not null : !Equals(mine, parent[gitPath]?.Target.Id);
    }

    private static List<Commit> Tips(Repository repo)
    {
        var tips = new List<Commit>();
        foreach (var b in repo.Branches)
        {
            if (b.Tip is { } t && !b.CanonicalName.EndsWith("/HEAD", StringComparison.Ordinal))
            {
                tips.Add(t);
            }
        }

        foreach (var tag in repo.Tags)
        {
            if (tag.PeeledTarget is Commit c)
            {
                tips.Add(c);
            }
        }

        return [.. tips.DistinctBy(c => c.Sha)];
    }

    private static Dictionary<string, IReadOnlyList<GitRef>> RefsOf(Repository repo)
    {
        var all = new List<(string Sha, GitRef Ref)>();
        foreach (var b in repo.Branches)
        {
            if (b.Tip is { } t && !b.CanonicalName.EndsWith("/HEAD", StringComparison.Ordinal))
            {
                all.Add((t.Sha, new GitRef(b.FriendlyName, b.IsRemote ? GitRefKind.Remote : GitRefKind.Branch, b.IsCurrentRepositoryHead)));
            }
        }

        foreach (var tag in repo.Tags)
        {
            if (tag.PeeledTarget is Commit c)
            {
                all.Add((c.Sha, new GitRef(tag.FriendlyName, GitRefKind.Tag)));
            }
        }

        return all.GroupBy(r => r.Sha, StringComparer.Ordinal).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<GitRef>)[.. g.Select(r => r.Ref).OrderByDescending(r => r.IsCurrent).ThenBy(r => r.Kind).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)],
            StringComparer.Ordinal);
    }

    public IReadOnlyList<GitCommitChange>? CommitChanges(string top, string sha)
    {
        if (!Ready() || !IsSha(sha))
        {
            return null;
        }

        try
        {
            using var repo = new Repository(top);
            if (repo.Lookup<Commit>(sha) is not { } commit)
            {
                return null;
            }

            var before = commit.Parents.FirstOrDefault()?.Tree;
            var changes = repo.Diff.Compare<TreeChanges>(before, commit.Tree);
            var stats = changes.Count <= MaxCounted ? repo.Diff.Compare<PatchStats>(before, commit.Tree) : null;
            var list = new List<GitCommitChange>();
            foreach (var change in changes)
            {
                var state = change.Status switch
                {
                    ChangeKind.Added or ChangeKind.Copied => GitState.Added,
                    ChangeKind.Deleted => GitState.Deleted,
                    ChangeKind.Unmodified or ChangeKind.Ignored or ChangeKind.Untracked => GitState.None,
                    _ => GitState.Changed,
                };
                if (state == GitState.None)
                {
                    continue;
                }

                var path = change.Path.Replace('/', '\\');
                var line = stats?[change.Path] ?? stats?[change.Path.Replace('\\', '/')];
                list.Add(line is { } l && l.LinesAdded + l.LinesDeleted > 0 ? new GitCommitChange(path, state, l.LinesAdded, l.LinesDeleted) : new GitCommitChange(path, state, null, null));
            }

            return list;
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Warn("Git: A commit's changes couldn't be read: " + ex.GetType().Name);
            return null;
        }
    }

    public byte[]? VersionAt(string top, string path, string sha, long limit)
    {
        if (!Ready() || !IsSha(sha))
        {
            return null;
        }

        try
        {
            using var repo = new Repository(top);
            var gitPath = path.Replace('\\', '/');
            if (repo.Lookup<Commit>(sha)?[gitPath]?.Target is not Blob blob || blob.Size > limit)
            {
                return null;
            }

            using var content = blob.GetContentStream(new FilteringOptions(gitPath));
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            return copy.Length > limit ? null : copy.ToArray();
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Warn("Git: A file of a commit couldn't be read: " + ex.GetType().Name);
            return null;
        }
    }

    public IReadOnlyList<GitTreeItem>? Tree(string top, string sha, string folder)
    {
        if (!Ready() || !IsSha(sha))
        {
            return null;
        }

        try
        {
            using var repo = new Repository(top);
            if (repo.Lookup<Commit>(sha) is not { } commit)
            {
                return null;
            }

            var tree = folder.Length == 0 ? commit.Tree : commit[folder.Replace('\\', '/')]?.Target as Tree;
            if (tree is null)
            {
                return null;
            }

            var list = new List<GitTreeItem>(tree.Count);
            foreach (var entry in tree)
            {
                switch (entry.TargetType)
                {
                    case TreeEntryTargetType.Tree:
                        list.Add(new GitTreeItem(entry.Name, true, 0));
                        break;
                    case TreeEntryTargetType.Blob:
                        list.Add(new GitTreeItem(entry.Name, false, ((Blob)entry.Target).Size));
                        break;
                    case TreeEntryTargetType.GitLink:
                        var linked = entry.Target.Sha;
                        list.Add(new GitTreeItem(entry.Name, false, 0, linked[..Math.Min(7, linked.Length)]));
                        break;
                }
            }

            return list;
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Warn("Git: A commit's tree couldn't be read: " + ex.GetType().Name);
            return null;
        }
    }

    public bool SaveVersionAt(string top, string path, string sha, string target)
    {
        if (!Ready() || !IsSha(sha))
        {
            return false;
        }

        try
        {
            using var repo = new Repository(top);
            var gitPath = path.Replace('\\', '/');
            if (repo.Lookup<Commit>(sha)?[gitPath]?.Target is not Blob blob)
            {
                return false;
            }

            using var content = blob.GetContentStream(new FilteringOptions(gitPath));
            using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            content.CopyTo(file);
            return true;
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Core.Diagnostics.AppLog.Warn("Git: A file of a commit couldn't be written out: " + ex.GetType().Name);
            return false;
        }
    }


    internal const int MaxSubmodules = 64;

    private const long MaxBlamedBytes = 16L << 20;

    public (GitBlame? Blame, GitBlameRefusal Refusal) Blame(string top, string path, string? sha)
    {
        if (!Ready() || (sha is not null && !IsSha(sha)))
        {
            return (null, GitBlameRefusal.Unreadable);
        }

        try
        {
            using var repo = new Repository(top);
            var gitPath = path.Replace('\\', '/');
            Commit? at;
            if (sha is null)
            {
                at = repo.Info.IsHeadUnborn ? null : repo.Head.Tip;
            }
            else if (repo.Lookup<Commit>(sha) is { } found)
            {
                at = found;
            }
            else
            {
                return (null, GitBlameRefusal.Unreadable);
            }

            if (at?[gitPath]?.Target is not Blob blob)
            {
                return (null, GitBlameRefusal.NotInGit);
            }

            if (blob.Size > MaxBlamedBytes)
            {
                return (null, GitBlameRefusal.TooBig);
            }

            byte[] committedBytes;
            using (var content = blob.GetContentStream())
            using (var copy = new MemoryStream())
            {
                content.CopyTo(copy);
                committedBytes = copy.ToArray();
            }

            if (Array.IndexOf(committedBytes, (byte)0) >= 0)
            {
                return (null, GitBlameRefusal.Binary);
            }

            var committed = LinesOf(committedBytes);
            var shown = committed;
            if (sha is null)
            {
                var file = Path.Join(top, path);
                var info = new FileInfo(file);
                if (!info.Exists)
                {
                    return (null, GitBlameRefusal.NotInGit);
                }

                if (info.Length > MaxBlamedBytes)
                {
                    return (null, GitBlameRefusal.TooBig);
                }

                var bytes = File.ReadAllBytes(file);
                if (Array.IndexOf(bytes, (byte)0) >= 0)
                {
                    return (null, GitBlameRefusal.Binary);
                }

                shown = LinesOf(bytes);
            }

            if (shown.Length > GitBlame.MaxLines || committed.Length > GitBlame.MaxLines)
            {
                return (null, GitBlameRefusal.TooBig);
            }

            var byLine = new GitCommit?[committed.Length];
            var commits = new Dictionary<string, GitCommit>(StringComparer.Ordinal);
            foreach (var hunk in repo.Blame(gitPath, new BlameOptions { StartingAt = at }))
            {
                var c = hunk.FinalCommit;
                if (!commits.TryGetValue(c.Sha, out var commit))
                {
                    commit = ToCommit(c, []);
                    commits[c.Sha] = commit;
                }

                for (var i = hunk.FinalStartLineNumber; i < hunk.FinalStartLineNumber + hunk.LineCount && i < byLine.Length; i++)
                {
                    byLine[i] = commit;
                }
            }

            var lines = new GitCommit?[shown.Length];
            if (sha is not null)
            {
                Array.Copy(byLine, lines, lines.Length);
            }
            else
            {
                var diff = Core.Compare.TextDiff.Compare(committed, shown, limit: TimeSpan.FromSeconds(5));
                if (diff.Finished)
                {
                    foreach (var row in diff.Rows)
                    {
                        if (row.Right >= 0 && row.Left >= 0 && row.Change == Core.Compare.LineChange.Same)
                        {
                            lines[row.Right] = byLine[row.Left];
                        }
                    }
                }
            }

            var runs = new List<GitBlameRun>();
            for (var i = 0; i < lines.Length;)
            {
                var start = i;
                var commit = lines[i];
                while (i < lines.Length && ReferenceEquals(lines[i], commit))
                {
                    i++;
                }

                runs.Add(new GitBlameRun(start, i - start, commit));
            }

            var times = commits.Values.Select(c => c.When).ToList();
            return (new GitBlame(runs, lines.Length, times.Count == 0 ? default : times.Min(), times.Count == 0 ? default : times.Max()), GitBlameRefusal.None);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Core.Diagnostics.AppLog.Warn("Git: Who changed a file's lines couldn't be read: " + ex.GetType().Name);
            return (null, GitBlameRefusal.Unreadable);
        }
    }

    internal static string[] LinesOf(byte[] bytes)
    {
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        if (text.Length == 0)
        {
            return [];
        }

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].EndsWith('\r'))
            {
                lines[i] = lines[i][..^1];
            }
        }

        return text.EndsWith('\n') ? lines[..^1] : lines;
    }

    public IReadOnlyList<GitTag>? Tags(string top)
    {
        if (!Ready())
        {
            return null;
        }

        try
        {
            using var repo = new Repository(top);
            var refs = RefsOf(repo);
            var list = new List<GitTag>();
            foreach (var tag in repo.Tags)
            {
                if (tag.PeeledTarget is not Commit c)
                {
                    continue;
                }

                var commit = ToCommit(c, refs.TryGetValue(c.Sha, out var named) ? named : []);
                list.Add(tag.IsAnnotated && tag.Annotation is { } a
                    ? new GitTag(tag.FriendlyName, commit, a.Message.TrimEnd(), true, a.Tagger?.Name ?? commit.Author, a.Tagger?.When ?? commit.When)
                    : new GitTag(tag.FriendlyName, commit, commit.Summary, false, commit.Author, commit.When));
            }

            return [.. list.OrderByDescending(t => t.When).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Warn("Git: A repository's tags couldn't be read: " + ex.GetType().Name);
            return null;
        }
    }

    public IReadOnlyList<GitSubmodule> Submodules(string top)
    {
        if (!Ready())
        {
            return [];
        }

        try
        {
            using var repo = new Repository(top);
            return SubmodulesOf(repo);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Warn("Git: A repository's submodules couldn't be read: " + ex.GetType().Name);
            return [];
        }
    }

    private static List<GitSubmodule> SubmodulesOf(Repository repo)
    {
        var list = new List<GitSubmodule>();
        foreach (var sm in repo.Submodules.Take(MaxSubmodules))
        {
            var recorded = (sm.IndexCommitId ?? sm.HeadCommitId)?.Sha ?? "";
            GitSubmoduleState state;
            try
            {
                var s = sm.RetrieveStatus();
                state = (s & SubmoduleStatus.WorkDirUninitialized) != 0 || sm.WorkDirCommitId is null ? GitSubmoduleState.NotCheckedOut
                    : (s & SubmoduleStatus.WorkDirModified) != 0 ? GitSubmoduleState.OtherCommit
                    : (s & (SubmoduleStatus.WorkDirFilesModified | SubmoduleStatus.WorkDirFilesIndexDirty | SubmoduleStatus.WorkDirFilesUntracked)) != 0 ? GitSubmoduleState.ChangesInside
                    : GitSubmoduleState.Same;
            }
            catch (LibGit2SharpException)
            {
                state = GitSubmoduleState.NotCheckedOut;
            }

            list.Add(new GitSubmodule(sm.Path.Replace('/', '\\'), recorded[..Math.Min(7, recorded.Length)], state));
        }

        return list;
    }

    private static GitCommit ToCommit(Commit c, IReadOnlyList<GitRef> refs)
        => new(c.Sha, c.MessageShort, c.Message.TrimEnd(), c.Author.Name, c.Author.Email, c.Author.When, [.. c.Parents.Select(p => p.Sha)], refs, false);

    internal static GitState StateOf(FileStatus s)
    {
        if ((s & FileStatus.Conflicted) != 0)
        {
            return GitState.Conflicted;
        }

        if ((s & FileStatus.Ignored) != 0)
        {
            return GitState.Ignored;
        }

        if ((s & (FileStatus.ModifiedInWorkdir | FileStatus.TypeChangeInWorkdir | FileStatus.RenamedInWorkdir)) != 0)
        {
            return GitState.Changed;
        }

        if ((s & FileStatus.DeletedFromWorkdir) != 0 || (s & FileStatus.DeletedFromIndex) != 0)
        {
            return GitState.Deleted;
        }

        if ((s & FileStatus.NewInIndex) != 0)
        {
            return GitState.Added;
        }

        if ((s & (FileStatus.ModifiedInIndex | FileStatus.RenamedInIndex | FileStatus.TypeChangeInIndex)) != 0)
        {
            return GitState.Staged;
        }

        return (s & FileStatus.NewInWorkdir) != 0 ? GitState.New : GitState.None;
    }

    private static (string? Branch, string? Detached, string? Upstream, int Ahead, int Behind) HeadOf(Repository repo)
    {
        if (repo.Info.IsHeadUnborn)
        {
            var target = repo.Refs.Head?.TargetIdentifier ?? "";
            return (target.StartsWith("refs/heads/", StringComparison.Ordinal) ? target["refs/heads/".Length..] : target, null, null, 0, 0);
        }

        if (repo.Info.IsHeadDetached)
        {
            var sha = repo.Head.Tip?.Sha;
            return (null, sha is null ? "?" : sha[..Math.Min(7, sha.Length)], null, 0, 0);
        }

        var head = repo.Head;
        if (!head.IsTracking || head.TrackedBranch is null)
        {
            return (head.FriendlyName, null, null, 0, 0);
        }

        var details = head.TrackingDetails;
        return (head.FriendlyName, null, head.TrackedBranch.FriendlyName, details.AheadBy ?? 0, details.BehindBy ?? 0);
    }

    internal static bool Ready()
    {
        lock (_initLock)
        {
            if (_ready is { } ready)
            {
                return ready;
            }

            var dir = NativeFolder();
            if (dir is not null)
            {
                try
                {
                    GlobalSettings.NativeLibraryPath = dir;
                }
                catch (LibGit2SharpException)
                {
                }
            }

            _ready = dir is not null;
            return _ready.Value;
        }
    }

    internal static string? NativeFolder()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var dir in new[] { baseDir, Path.Combine(baseDir, "runtimes", "win-x64", "native") })
        {
            try
            {
                if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "git2-*.dll").Any())
                {
                    return Path.TrimEndingDirectorySeparator(dir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }
}
