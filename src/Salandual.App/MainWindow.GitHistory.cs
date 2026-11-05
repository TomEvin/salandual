// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.GitHistory.cs

using Salandual.App.Compare;
using Salandual.App.GitViews;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Git;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Vfs;

namespace Salandual.App;

public partial class MainWindow
{
    private sealed record HistoryShown(string Top, string? Of, Dictionary<string, GitCommit> ByName);

    private sealed record OpenedCommit(string Top, GitCommit Commit, string? Of, Dictionary<string, GitCommitChange> ByPath, Func<Task<bool>>? Back = null);

    private readonly Dictionary<PanelHost, HistoryShown> _histories = [];
    private readonly Dictionary<PanelHost, OpenedCommit> _commits = [];

    internal GitGraphWindow? GraphShown { get; private set; }

    private static string RowName(GitCommit c) => c.Short + " " + c.Summary;

    internal async Task ShowHistoryAsync(PanelHost host, bool ofFocused)
    {
        SetActive(Array.IndexOf(_panels, host), focus: false);
        var top = host.Panel.GitListTop ?? GitTopOrSay(host);
        if (top is null)
        {
            return;
        }

        string? of = null;
        if (ofFocused)
        {
            var picked = host.Panel.IsHistory ? [] : GitPicked(host, top);
            if (host.Panel.IsCommit && host.Panel.FocusedEntry is { } inCommit)
            {
                picked = [inCommit.Name];
            }

            if (picked.Count == 0)
            {
                ShowNotice(AppStrings.Git_History_PickFile);
                return;
            }

            of = picked[^1].Length == 0 ? null : picked[^1];
        }

        await ShowHistoryCoreAsync(host, top, of, focus: null);
    }

    private async Task<bool> ShowHistoryCoreAsync(PanelHost host, string top, string? of, string? focus)
    {
        if (await ReadHistoryAsync(host, top, of) is not { } read)
        {
            ShowNotice(AppStrings.Git_ChangesUnreadable);
            return false;
        }

        host.Panel.ShowGitList(read.What, read.Items, read.Mark, async () => await ReadHistoryAsync(host, top, of) is { } again ? (again.Items, again.Mark) : null, focus);
        UpdateGitListState(host);
        if (read.Items.Count == 0)
        {
            ShowNotice(of is null ? AppStrings.Git_History_NoCommits : Loc.Format(AppStrings.Git_History_NoneOf, of));
        }

        host.View.Focus();
        return true;
    }

    private async Task<(string What, IReadOnlyList<FileEntry> Items, Func<PanelResults, PanelResults> Mark)?> ReadHistoryAsync(PanelHost host, string top, string? of)
    {
        var log = await Task.Run(() => _services.GitReader.Log(top, new GitLogQuery(Path: of)));
        if (log is null)
        {
            return null;
        }

        var items = new List<FileEntry>(log.Commits.Count + 2);
        var byName = new Dictionary<string, GitCommit>(StringComparer.OrdinalIgnoreCase);
        var notPushed = log.Upstream is null ? 0 : log.Commits.Count(c => !c.Pushed);
        void Add(GitCommit c)
        {
            items.Add(new FileEntry(RowName(c), 0, c.When.UtcDateTime.Ticks, FileAttributes.Normal));
            byName[RowName(c)] = c;
        }

        if (log.Upstream is { } upstream && notPushed > 0)
        {
            items.Add(Heading(Loc.Format(AppStrings.Git_History_NotPushed, notPushed)));
            foreach (var c in log.Commits.Where(c => !c.Pushed))
            {
                Add(c);
            }

            if (log.Commits.Any(c => c.Pushed))
            {
                items.Add(Heading(Loc.Format(AppStrings.Git_History_OnUpstream, upstream)));
            }

            foreach (var c in log.Commits.Where(c => c.Pushed))
            {
                Add(c);
            }
        }
        else
        {
            foreach (var c in log.Commits)
            {
                Add(c);
            }
        }

        _histories[host] = new HistoryShown(top, of, byName);
        var history = new PanelHistory(top, of, log.Upstream, notPushed, log.More);
        var name = Path.GetFileName(top);
        var what = of is null ? Loc.Format(AppStrings.Git_History_What, name) : Loc.Format(AppStrings.Git_History_WhatOf, name, of);
        return (what, items, r => r with { History = history });

        static FileEntry Heading(string title) => new(title, 0, 0, FileAttributes.Directory);
    }

    private Task OpenCommitAsync(PanelHost host, FileEntry entry)
        => _histories.TryGetValue(host, out var shown) && shown.ByName.TryGetValue(entry.Name, out var commit)
            ? ShowCommitAsync(host, shown.Top, commit, shown.Of)
            : Task.CompletedTask;

    internal async Task ShowCommitAsync(PanelHost host, string top, GitCommit commit, string? of, Func<Task<bool>>? back = null)
    {
        if (await ReadCommitAsync(host, top, commit, of, back) is not { } read)
        {
            ShowNotice(AppStrings.Git_CommitUnreadable);
            return;
        }

        var mark = (Func<PanelResults, PanelResults>)(r => r with { Commit = new PanelCommit(top, commit.Sha, commit.Summary) });
        host.Panel.ShowGitList(
            Loc.Format(AppStrings.Git_CommitView_What, Path.GetFileName(top), commit.Short, commit.Summary),
            read,
            mark,
            async () => await ReadCommitAsync(host, top, commit, of, back) is { } again ? (again, mark) : null,
            back: back ?? (() => ShowHistoryCoreAsync(host, top, of, RowName(commit))));
        UpdateGitListState(host);
        host.View.Focus();
    }

    private async Task<IReadOnlyList<FileEntry>?> ReadCommitAsync(PanelHost host, string top, GitCommit commit, string? of, Func<Task<bool>>? back)
    {
        var changes = await Task.Run(() => _services.GitReader.CommitChanges(top, commit.Sha));
        if (changes is null)
        {
            return null;
        }

        var ticks = commit.When.UtcDateTime.Ticks;
        _commits[host] = new OpenedCommit(top, commit, of, changes.ToDictionary(c => c.Path, StringComparer.OrdinalIgnoreCase), back);
        return [.. changes.OrderBy(c => c.Path, StringComparer.CurrentCultureIgnoreCase).Select(c => new FileEntry(c.Path, 0, ticks, FileAttributes.Normal)),
            new FileEntry(AppStrings.Git_Tree_Heading, 0, 0, FileAttributes.Directory),
            new FileEntry(TreeRowName(commit), 0, ticks, FileAttributes.Normal)];
    }

    private void UpdateGitListState(PanelHost host)
    {
        FunctionKeys.InvalidateVisual();
        host.View.SubmoduleFor = null;
        var panel = host.Panel;
        if (panel.IsChanges)
        {
            UpdateChangesState(host);
            return;
        }

        var format = CurrentFormat();
        if (panel.IsHistory && _histories.TryGetValue(host, out var history))
        {
            host.View.Place = new PlaceColumns(AppStrings.Git_AuthorHead, e => history.ByName.TryGetValue(e.Name, out var c) ? c.Author : null)
            {
                Hide = [PanelColumn.Extension, PanelColumn.Size, PanelColumn.Attributes, PanelColumn.Git],
                Share = 0.3,
                Glyph = e => e.IsDirectory ? null : "Icon.GitCommit",
            };
            host.View.GitStateFor = _ => GitState.None;
            UpdateGitState(host);
            host.GitInfo = null;
            host.InfoInstead = e => history.ByName.TryGetValue(e.Name, out var c) ? c.Short + " · " + format.Date(c.When.LocalDateTime) + " " + format.Time(c.When.LocalDateTime) + " · " + CommitWords(c) : null;
        }
        else if (panel.IsTags && _tagLists.TryGetValue(host, out var tags))
        {
            UpdateTagsState(host, tags);
        }
        else if (panel.IsCommit && _commits.TryGetValue(host, out var commit))
        {
            host.View.Place = new PlaceColumns(AppStrings.Git_LinesHead, e => commit.ByPath.TryGetValue(e.Name, out var c) ? LinesText(new GitChange(c.Path, GitChangeGroup.Changed, c.State, c.Added, c.Removed, true, false, true), format) : null)
            {
                Hide = [PanelColumn.Size, PanelColumn.Attributes],
                Share = 0.25,
                Glyph = e => e.Name == TreeRowName(commit.Commit) ? "Icon.Folder" : null,
            };
            host.View.GitStateFor = e => commit.ByPath.TryGetValue(e.Name, out var c) ? c.State : GitState.None;
            UpdateGitState(host);
            host.GitInfo = null;
            host.InfoInstead = e => commit.ByPath.TryGetValue(e.Name, out var c) ? c.Path + " · " + Loc.Format(c.State switch
            {
                GitState.Added => AppStrings.Git_CommitState_Added,
                GitState.Deleted => AppStrings.Git_CommitState_Deleted,
                _ => AppStrings.Git_CommitState_Changed,
            }, commit.Commit.Short) + (LinesText(new GitChange(c.Path, GitChangeGroup.Changed, c.State, c.Added, c.Removed, true, false, true), format) is { Length: > 0 } lines ? " · " + lines : "") : null;
        }
        else if (panel.IsTree && _trees.TryGetValue(host, out var tree))
        {
            GitCommitChange? Changed(FileEntry e) => !e.IsDirectory && tree.Changes.TryGetValue(tree.Inner(e.Name), out var c) ? c : null;
            host.View.Place = new PlaceColumns(AppStrings.Git_LinesHead, e => Changed(e) is { } c ? LinesText(new GitChange(c.Path, GitChangeGroup.Changed, c.State, c.Added, c.Removed, true, false, true), format) : null)
            {
                Hide = [PanelColumn.Attributes],
                Share = 0.2,
            };
            GitTreeItem? Linked(FileEntry e) => tree.ByName.TryGetValue(e.Name, out var i) && i.Submodule is not null ? i : null;
            host.View.GitStateFor = e => Linked(e) is not null ? GitState.Ignored : Changed(e)?.State ?? GitState.None;
            host.View.SubmoduleFor = e => Linked(e) is not null;
            UpdateGitState(host);
            host.GitInfo = null;
            host.InfoInstead = e => tree.Inner(e.Name) + " · " + (Linked(e) is { } sub ? Loc.Format(AppStrings.Git_Tree_SubmoduleInfo, sub.Submodule) : Changed(e) is { } c
                ? Loc.Format(c.State == GitState.Added ? AppStrings.Git_CommitState_Added : AppStrings.Git_CommitState_Changed, tree.Commit.Short)
                    + (LinesText(new GitChange(c.Path, GitChangeGroup.Changed, c.State, c.Added, c.Removed, true, false, true), format) is { Length: > 0 } lines ? " · " + lines : "")
                : Loc.Format(AppStrings.Git_Tree_AsIn, tree.Commit.Short));
        }
    }

    private static string CommitWords(GitCommit c)
    {
        var parents = c.Parents.Count == 0 ? AppStrings.Git_NoParent
            : c.Parents.Count == 1 ? Loc.Format(AppStrings.Git_Parent_One, c.Parents[0][..Math.Min(7, c.Parents[0].Length)])
            : Loc.Format(AppStrings.Git_Parents_Other, string.Join(", ", c.Parents.Select(p => p[..Math.Min(7, p.Length)])));
        var parts = new List<string> { c.Author, parents };
        if (c.Refs.Count > 0)
        {
            parts.Add(string.Join(", ", c.Refs.Select(r => r.Name)));
        }

        return string.Join(" · ", parts);
    }

    private string GitListBarText(PanelResults results)
    {
        if (results.Changes is { } changes)
        {
            return ChangesBarText(changes);
        }

        if (results.History is { } history)
        {
            var count = ActivePanelCount(results);
            return history.More ? Loc.Format(AppStrings.Git_History_BarMore, count) : Loc.Plural(count, AppStrings.Git_History_Bar_One, AppStrings.Git_History_Bar_Other);
        }

        if (results.Tags is not null)
        {
            var tags = ActivePanelCount(results);
            return Loc.Plural(tags, AppStrings.Git_Tags_Bar_One, AppStrings.Git_Tags_Bar_Other) + " · " + AppStrings.Git_Tags_Keys;
        }

        return results.Commit is not null ? AppStrings.Git_CommitView_Keys : results.Tree is not null ? AppStrings.Git_Tree_Keys : "";
    }

    private int ActivePanelCount(PanelResults results)
    {
        foreach (var host in _panels)
        {
            if (ReferenceEquals(host.Panel.Results, results) && _tagLists.TryGetValue(host, out var tags) && results.Tags is not null)
            {
                return tags.ByName.Count;
            }

            if (ReferenceEquals(host.Panel.Results, results) && _histories.TryGetValue(host, out var shown))
            {
                return shown.ByName.Count;
            }
        }

        return 0;
    }

    internal async Task ShowCommitDiffAsync(PanelHost host)
    {
        if (host.Panel.IsCommit && _commits.TryGetValue(host, out var opened) && host.Panel.FocusedEntry is { } row && row.Name == TreeRowName(opened.Commit))
        {
            await BrowseVersionAsync(host);
            return;
        }

        if (host.Panel.IsCommit && _commits.TryGetValue(host, out var shown) && host.Panel.FocusedEntry is { } entry
            && shown.ByPath.TryGetValue(entry.Name, out var change))
        {
            await DiffInCommitAsync(shown.Top, shown.Commit, change);
        }
    }

    internal async Task ViewAsItWasAsync(PanelHost host)
    {
        if (!host.Panel.IsCommit || !_commits.TryGetValue(host, out var shown) || host.Panel.FocusedEntry is not { } entry
            || !shown.ByPath.TryGetValue(entry.Name, out var change))
        {
            return;
        }

        var commit = shown.Commit;
        var name = Path.GetFileName(change.Path);
        var sha = change.State == GitState.Deleted ? commit.Parents.FirstOrDefault() : commit.Sha;
        var content = sha is null ? null : await Task.Run(() => _services.GitReader.VersionAt(shown.Top, change.Path, sha, MaxDiffBytes));
        if (content is null)
        {
            ShowNotice(Loc.Format(AppStrings.Git_Diff_TooBig, name));
            return;
        }

        var path = WriteVersion(Path.Combine(_services.Temp.NewFolder("git"), commit.Short), name, content);
        OpenViewer(host, path, _services.Settings.Current.Viewers.KindFor(name, alternate: false), siblings: [path]);
    }

    internal async Task ShowGraphAsync(PanelHost host)
    {
        var top = host.Panel.GitListTop ?? GitTopOrSay(host);
        if (top is null)
        {
            return;
        }

        var window = GraphShown = new GitGraphWindow(
            _services.Settings,
            Path.GetFileName(top),
            query => Task.Run(() => _services.GitReader.Log(top, query)),
            sha => Task.Run(() => _services.GitReader.CommitChanges(top, sha)),
            commit =>
            {
                Activate();
                _ = ShowCommitAsync(ActivePanel, top, commit, null);
            },
            (commit, change) => _ = DiffInCommitAsync(top, commit, change),
            () => Task.Run(() => _services.GitReader.Tags(top)));
        Controls.WindowMemory.Remember(window, _services.Settings, Core.Settings.WindowSettings.GitGraph, centreOn: this);
        window.Closed += (_, _) =>
        {
            if (GraphShown == window)
            {
                GraphShown = null;
            }
        };
        window.Show();
        await window.Loading;
    }

    private async Task DiffInCommitAsync(string top, GitCommit commit, GitCommitChange change)
    {

        var parent = commit.Parents.FirstOrDefault();
        var name = Path.GetFileName(change.Path);
        var (before, after) = await Task.Run(() => (
            parent is null || change.State == GitState.Added ? [] : _services.GitReader.VersionAt(top, change.Path, parent, MaxDiffBytes),
            change.State == GitState.Deleted ? [] : _services.GitReader.VersionAt(top, change.Path, commit.Sha, MaxDiffBytes)));
        if (before is null || after is null)
        {
            ShowNotice(Loc.Format(AppStrings.Git_Diff_TooBig, name));
            return;
        }

        var folder = _services.Temp.NewFolder("git");
        var parentShort = parent is null ? "" : parent[..Math.Min(7, parent.Length)];
        OpenFileCompare(
            WriteVersion(Path.Combine(folder, "before"), name, before),
            WriteVersion(Path.Combine(folder, commit.Short), name, after),
            new GitCompare(
                Loc.Format(AppStrings.Git_CommitDiff_Title, name, commit.Short),
                change.State == GitState.Added || parent is null ? Loc.Format(AppStrings.Git_CommitDiff_NotBefore, commit.Short) : Loc.Format(AppStrings.Git_CommitDiff_Side, parentShort),
                change.State == GitState.Deleted ? Loc.Format(AppStrings.Git_CommitDiff_Deleted, commit.Short) : Loc.Format(AppStrings.Git_CommitDiff_Side, commit.Short),
                RightReadOnly: true,
                AppStrings.Git_CommitDiff_Keys,
                null,
                null));
    }
}
