// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.GitChanges.cs

using Salandual.App.Compare;
using Salandual.App.Dialogs;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Git;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Vfs;

namespace Salandual.App;

public partial class MainWindow
{
    private const long MaxDiffBytes = 64L << 20;

    private readonly Dictionary<PanelHost, Dictionary<string, GitChange>> _changes = [];

    internal CommitDialog? CommitShown { get; private set; }

    internal ConfirmDialog? DiscardQuestion { get; private set; }

    internal Task GitWork { get; private set; } = Task.CompletedTask;

    internal async Task ShowChangesAsync(PanelHost host)
    {
        SetActive(Array.IndexOf(_panels, host), focus: false);
        if (GitTopOrSay(host) is not { } top)
        {
            return;
        }

        if (await ReadChangesAsync(host, top) is not { } read)
        {
            ShowNotice(AppStrings.Git_ChangesUnreadable);
            return;
        }

        host.Panel.ShowChanges(Loc.Format(AppStrings.Git_Changes_What, Path.GetFileName(top)), read.Items, read.Changes, () => ReadChangesAsync(host, top));
        UpdateChangesState(host);
        if (read.Changes.Staged + read.Changes.NotStaged == 0)
        {
            ShowNotice(AppStrings.Git_NoChanges);
        }

        host.View.Focus();
    }

    private async Task<(IReadOnlyList<FileEntry> Items, PanelChanges Changes)?> ReadChangesAsync(PanelHost host, string top)
    {
        var read = await Task.Run(() =>
        {
            var list = _services.GitReader.Changes(top);
            return list is null ? null : list.Select(c => (Change: c, Entry: EntryOf(top, c))).ToList();
        });
        if (read is null)
        {
            return null;
        }

        var items = new List<FileEntry>(read.Count + 4);
        var map = new Dictionary<string, GitChange>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Enum.GetValues<GitChangeGroup>())
        {
            var inGroup = read.Where(r => r.Change.Group == group).OrderBy(r => r.Change.Path, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (inGroup.Count == 0)
            {
                continue;
            }

            items.Add(new FileEntry(GroupTitle(group, inGroup.Count), 0, 0, FileAttributes.Directory));
            foreach (var (change, entry) in inGroup)
            {
                items.Add(entry);
                map[change.Path] = change;
            }
        }

        _changes[host] = map;
        var changes = new PanelChanges(top, read.Count(r => r.Change.HasStaged), read.Count(r => r.Change.Group != GitChangeGroup.Staged));
        return (items, changes);
    }

    private static FileEntry EntryOf(string top, GitChange change)
    {
        if (change.OnDisk)
        {
            try
            {
                var info = new FileInfo(Path.Join(top, change.Path));
                if (info.Exists)
                {
                    return new FileEntry(change.Path, info.Length, info.LastWriteTimeUtc.Ticks, info.Attributes, info.CreationTimeUtc.Ticks, info.LastAccessTimeUtc.Ticks);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return new FileEntry(change.Path, 0, 0, FileAttributes.Normal);
    }

    private static string GroupTitle(GitChangeGroup group, int count) => Loc.Format(group switch
    {
        GitChangeGroup.Staged => AppStrings.Git_Group_Staged,
        GitChangeGroup.Changed => AppStrings.Git_Group_Changed,
        GitChangeGroup.New => AppStrings.Git_Group_New,
        _ => AppStrings.Git_Group_Conflicted,
    }, count);

    private void UpdateChangesState(PanelHost host)
    {
        if (!host.Panel.IsChanges)
        {
            return;
        }

        var format = CurrentFormat();
        host.View.Place = new PlaceColumns(AppStrings.Git_LinesHead, e => ChangeOf(host, e) is { } c ? LinesText(c, format) : null)
        {
            Size = e => ChangeOf(host, e) is { OnDisk: false } ? "" : null,
            Share = 0.25,
        };
        host.View.GitStateFor = null;
        host.InfoInstead = null;
        UpdateGitState(host);
    }

    private GitChange? ChangeOf(PanelHost host, FileEntry e)
        => !e.IsDirectory && _changes.TryGetValue(host, out var map) && map.TryGetValue(e.Name, out var c) ? c : null;

    internal static string LinesText(GitChange c, Core.Listing.DisplayFormat format)
    {
        var added = c.Added ?? 0;
        var removed = c.Removed ?? 0;
        var parts = new List<string>(2);
        if (added > 0)
        {
            parts.Add("+" + format.Number(added));
        }

        if (removed > 0)
        {
            parts.Add("−" + format.Number(removed));
        }

        return string.Join(" ", parts);
    }

    private string ChangesBarText(PanelChanges changes)
    {
        var keys = new List<string> { Loc.Format(AppStrings.Git_Changes_Counts, changes.Staged, changes.NotStaged) };
        if (_services.Commands.ChordsFor("Git.Stage").FirstOrDefault() is { Key.Length: > 0 } stage)
        {
            keys.Add(Loc.Format(AppStrings.Git_Changes_Stage, stage));
        }

        if (_services.Commands.ChordsFor("Git.Unstage").FirstOrDefault() is { Key.Length: > 0 } unstage)
        {
            keys.Add(Loc.Format(AppStrings.Git_Changes_Unstage, unstage));
        }

        if (_services.Commands.ChordsFor("Git.Menu").FirstOrDefault() is { Key.Length: > 0 } menu)
        {
            keys.Add(Loc.Format(AppStrings.Git_Changes_Commit, menu));
        }

        return string.Join(" · ", keys);
    }

    private void ChangesLeft(PanelHost host)
    {
        var left = (!host.Panel.IsChanges && _changes.Remove(host)) | (!host.Panel.IsHistory && _histories.Remove(host)) | (!host.Panel.IsCommit && _commits.Remove(host)) | (!host.Panel.IsTree && _trees.Remove(host)) | (!host.Panel.IsTags && _tagLists.Remove(host));
        FunctionKeys.InvalidateVisual();
        if (left && !host.Panel.IsGitList)
        {
            host.View.GitStateFor = null;
            host.View.SubmoduleFor = null;
            host.InfoInstead = null;
            UpdatePlaceState(host);
            UpdateServerState(host);
            UpdateGitState(host);
        }
    }

    private List<string> GitPicked(PanelHost host, string top)
    {
        var panel = host.Panel;
        var picked = new List<string>();
        void Add(FileEntry e)
        {
            var full = Path.IsPathFullyQualified(e.Name) ? e.Name : Path.Join(panel.Path, e.Name);
            var relative = Path.GetRelativePath(top, full);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathFullyQualified(relative))
            {
                picked.Add(relative == "." ? "" : relative);
            }
        }

        for (var i = 0; i < panel.Count; i++)
        {
            if (panel.IsSelected(i) && !panel.IsUp(i) && !panel.IsHeading(i))
            {
                Add(panel.Items[i]);
            }
        }

        if (picked.Count == 0 && panel.FocusedEntry is { } focused && !panel.IsUp(panel.Focus) && !panel.IsHeading(panel.Focus))
        {
            Add(focused);
        }

        return picked;
    }

    private async Task<(string Top, List<GitChange> Changes, GitHeadFacts? Head)?> PickedChangesAsync(PanelHost host)
    {
        var top = host.Panel.GitListTop ?? GitTopOrSay(host);
        if (top is null)
        {
            return null;
        }

        if (_services.GitCommands.Program is null)
        {
            ShowNotice(AppStrings.Git_NotInstalled);
            return null;
        }

        var picked = GitPicked(host, top);
        var (all, head) = await Task.Run(() => (_services.GitReader.Changes(top), _services.GitReader.Head(top)));
        if (all is null)
        {
            ShowNotice(AppStrings.Git_ChangesUnreadable);
            return null;
        }

        var chosen = all.Where(c => picked.Any(p => Under(c.Path, p))).ToList();
        return (top, chosen, head);

        static bool Under(string path, string picked)
            => picked.Length == 0
                || path.Equals(picked, StringComparison.OrdinalIgnoreCase)
                || (path.Length > picked.Length && path[picked.Length] == '\\' && path.StartsWith(picked, StringComparison.OrdinalIgnoreCase));
    }

    internal Task StageAsync(PanelHost host) => GitWork = StageCoreAsync(host);

    private async Task StageCoreAsync(PanelHost host)
    {
        if (await PickedChangesAsync(host) is not { } picked)
        {
            return;
        }

        var paths = picked.Changes.Where(c => c.Group != GitChangeGroup.Staged).Select(c => c.Path).ToList();
        await StagePathsAsync(host, picked.Top, paths);
    }

    private async Task StagePathsAsync(PanelHost host, string top, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            ShowNotice(AppStrings.Git_NothingToStage);
            return;
        }

        if (await RunAllAsync(top, GitCommandLines.Stage(paths)) is { } failed)
        {
            await ShowGitMessageAsync(AppStrings.Git_StageFailed, failed.Message);
        }
        else
        {
            ShowNotice(Loc.Plural(paths.Count, AppStrings.Git_Staged_One, AppStrings.Git_Staged_Other));
        }

        await AfterGitAsync(host, top);
    }

    internal Task UnstageAsync(PanelHost host) => GitWork = UnstageCoreAsync(host);

    private async Task UnstageCoreAsync(PanelHost host)
    {
        if (await PickedChangesAsync(host) is not { } picked)
        {
            return;
        }

        var paths = picked.Changes.Where(c => c.HasStaged).Select(c => c.Path).ToList();
        if (paths.Count == 0)
        {
            ShowNotice(AppStrings.Git_NothingToUnstage);
            return;
        }

        if (await RunAllAsync(picked.Top, GitCommandLines.Unstage(paths, noCommitYet: picked.Head?.Sha is null)) is { } failed)
        {
            await ShowGitMessageAsync(AppStrings.Git_UnstageFailed, failed.Message);
        }
        else
        {
            ShowNotice(Loc.Plural(paths.Count, AppStrings.Git_Unstaged_One, AppStrings.Git_Unstaged_Other));
        }

        await AfterGitAsync(host, picked.Top);
    }

    internal Task DiscardAsync(PanelHost host) => GitWork = DiscardCoreAsync(host);

    private async Task DiscardCoreAsync(PanelHost host)
    {
        if (await PickedChangesAsync(host) is not { } picked)
        {
            return;
        }

        var changes = picked.Changes.Where(c => c.Group != GitChangeGroup.Conflicted).ToList();
        if (changes.Count == 0)
        {
            ShowNotice(picked.Changes.Count > 0 ? AppStrings.Git_DiscardConflicted : AppStrings.Git_NothingToDiscard);
            return;
        }

        var title = changes.Count == 1 ? Loc.Format(AppStrings.Git_Discard_Title_One, Path.GetFileName(changes[0].Path)) : Loc.Format(AppStrings.Git_Discard_Title_Other, changes.Count);
        var question = DiscardQuestion = new ConfirmDialog(title, AppStrings.Git_Discard_Text, AppStrings.Git_Discard_Yes, AppStrings.Common_Cancel);
        bool yes;
        try
        {
            yes = await question.ShowDialog<bool>(this);
        }
        finally
        {
            DiscardQuestion = null;
        }

        if (!yes)
        {
            host.View.Focus();
            return;
        }

        var top = picked.Top;
        var (gone, stayed) = await Task.Run(() =>
        {
            var recycled = new List<GitChange>();
            string? why = null;
            foreach (var c in changes)
            {
                if (!c.OnDisk)
                {
                    recycled.Add(c);
                    continue;
                }

                var (done, reason) = _services.RecycleFile(Path.Join(top, c.Path));
                if (done)
                {
                    recycled.Add(c);
                }
                else
                {
                    why ??= reason is null ? Loc.Format(AppStrings.Git_Discard_Kept, c.Path) : Loc.Format(AppStrings.Git_Discard_NotRecycled, c.Path, reason);
                }
            }

            return (recycled, why);
        });

        GitRun? failed = null;
        var back = gone.Where(c => c.InHead).Select(c => c.Path).ToList();
        if (back.Count > 0)
        {
            failed = await RunAllAsync(top, GitCommandLines.DiscardToHead(back));
        }

        var added = gone.Where(c => !c.InHead && c.HasStaged).Select(c => c.Path).ToList();
        if (failed is null && added.Count > 0)
        {
            failed = await RunAllAsync(top, GitCommandLines.DiscardAdded(added));
        }

        if (failed is not null)
        {
            await ShowGitMessageAsync(AppStrings.Git_DiscardFailed, failed.Message);
        }
        else if (gone.Count > 0)
        {
            ShowNotice(Loc.Plural(gone.Count, AppStrings.Git_Discarded_One, AppStrings.Git_Discarded_Other) + (stayed is null ? "" : " " + stayed));
        }
        else if (stayed is not null)
        {
            ShowNotice(stayed);
        }

        await AfterGitAsync(host, top);
    }

    internal Task CommitAsync(PanelHost host) => GitWork = CommitCoreAsync(host);

    private async Task CommitCoreAsync(PanelHost host)
    {
        var top = host.Panel.GitListTop ?? GitTopOrSay(host);
        if (top is null)
        {
            return;
        }

        if (_services.GitCommands.Program is null)
        {
            ShowNotice(AppStrings.Git_NotInstalled);
            return;
        }

        var (all, head, status, remotes) = await Task.Run(() => (_services.GitReader.Changes(top), _services.GitReader.Head(top), _services.GitReader.Status(top), _services.GitReader.Remotes(top)));
        if (all is null || head is null)
        {
            ShowNotice(AppStrings.Git_ChangesUnreadable);
            return;
        }

        var staged = all.Where(c => c.HasStaged).Select(c => c.Path).ToList();
        var notStaged = all.Count(c => c.Group != GitChangeGroup.Staged);
        if (staged.Count == 0 && notStaged == 0 && head.Sha is null)
        {
            ShowNotice(AppStrings.Git_NothingToCommit);
            return;
        }

        var contents = new CommitContents(staged, notStaged);
        var branch = status?.Label ?? "HEAD";
        var dialog = CommitShown = new CommitDialog(branch, contents, head, async (message, amend) =>
        {
            if (contents.TakesAll && !amend)
            {
                if (notStaged == 0)
                {
                    return new GitRun(1, "", AppStrings.Git_NothingToCommit);
                }

                var add = await RunGitAsync(top, GitCommandLines.StageAll());
                if (!add.Succeeded)
                {
                    return add;
                }
            }

            return await RunGitAsync(top, GitCommandLines.Commit(message, amend));
        }, canPush: remotes.Count > 0 && status?.Branch is not null);
        bool done;
        try
        {
            done = await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            CommitShown = null;
        }

        if (done)
        {
            var summary = (dialog.MessageBox.Text ?? "").Split('\n')[0].Trim();
            ShowNotice(Loc.Format(dialog.AmendBox.IsChecked == true ? AppStrings.Git_Amended : AppStrings.Git_Committed, branch, summary));
        }

        await AfterGitAsync(host, top);
        if (done && dialog.PushBox is { IsVisible: true, IsChecked: true })
        {
            StartPush(host, top);
        }
    }

    internal async Task ShowGitDiffAsync(PanelHost host)
    {
        if (!host.Panel.IsChanges || host.Panel.FocusedEntry is not { } entry || ChangeOf(host, entry) is not { } change)
        {
            return;
        }

        if (change.Group == GitChangeGroup.Conflicted)
        {
            ShowNotice(AppStrings.Git_Diff_Conflicted);
            return;
        }

        var top = host.Panel.Results!.Changes!.Top;
        var staged = change.Group == GitChangeGroup.Staged;
        var name = Path.GetFileName(change.Path);
        var (committed, inIndex, head) = await Task.Run(() => (
            change.InHead ? _services.GitReader.Version(top, change.Path, GitVersion.Head, MaxDiffBytes) : [],
            staged ? _services.GitReader.Version(top, change.Path, GitVersion.Index, MaxDiffBytes) : null,
            _services.GitReader.Head(top)));
        if (committed is null)
        {
            ShowNotice(Loc.Format(AppStrings.Git_Diff_TooBig, name));
            return;
        }

        var folder = _services.Temp.NewFolder("git");
        var left = WriteVersion(Path.Combine(folder, "commit"), name, committed);
        var rightLabel = staged ? AppStrings.Git_Diff_Staged : !change.OnDisk ? AppStrings.Git_Diff_Deleted : null;
        var right = staged ? WriteVersion(Path.Combine(folder, "staged"), name, inIndex ?? [])
            : !change.OnDisk ? WriteVersion(Path.Combine(folder, "deleted"), name, [])
            : Path.Join(top, change.Path);
        var branch = GitStates?.StatusOf(top)?.Label ?? "HEAD";
        var stageChord = _services.Commands.ChordsFor("Git.Stage").FirstOrDefault();
        var hasStageKey = stageChord.Key is { Length: > 0 };
        var git = new GitCompare(
            Loc.Format(staged ? AppStrings.Git_Diff_TitleStaged : AppStrings.Git_Diff_Title, name),
            change.InHead ? Loc.Format(AppStrings.Git_Diff_Head, branch, head?.Sha ?? "") : AppStrings.Git_Diff_NotInHead,
            rightLabel,
            RightReadOnly: rightLabel is not null,
            Loc.Format(AppStrings.Git_Diff_Keys, hasStageKey ? stageChord.ToString() : "—"),
            hasStageKey ? stageChord : null,
            staged ? null : () => GitWork = StagePathsAsync(host, top, [change.Path]));
        OpenFileCompare(left, right, git);
    }

    private static string WriteVersion(string folder, string name, byte[] content)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, content);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        return path;
    }

    private async Task<GitRun?> RunAllAsync(string top, IReadOnlyList<IReadOnlyList<string>> runs)
    {
        foreach (var arguments in runs)
        {
            var run = await RunGitAsync(top, arguments);
            if (!run.Succeeded)
            {
                return run;
            }
        }

        return null;
    }

    private async Task RereadShownChangesAsync(PanelHost host)
    {
        if (await host.Panel.RereadGitListAsync())
        {
            UpdateGitListState(host);
        }
    }

    private async Task AfterGitAsync(PanelHost host, string top)
    {
        GitStates?.Reread(top);
        foreach (var panel in _panels.Where(p => p.Panel.IsGitList && string.Equals(p.Panel.GitListTop, top, StringComparison.OrdinalIgnoreCase)))
        {
            await panel.Panel.RereadGitListAsync();
            UpdateGitListState(panel);
        }

        host.View.Focus();
    }
}
