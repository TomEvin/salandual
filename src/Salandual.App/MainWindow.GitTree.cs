// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.GitTree.cs

using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Git;
using Salandual.Core.Operations;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Vfs;

namespace Salandual.App;

public partial class MainWindow
{
    private const int MaxFilesCopiedOut = 20_000;

    private const long MaxBytesCopiedOut = 4L << 30;

    private sealed record TreeShown(string Top, GitCommit Commit, string Folder, Dictionary<string, GitTreeItem> ByName, IReadOnlyDictionary<string, GitCommitChange> Changes, Func<Task<bool>> Out)
    {
        public string Inner(string name) => Folder.Length == 0 ? name : Folder + "\\" + name;
    }

    private readonly Dictionary<PanelHost, TreeShown> _trees = [];

    private static string TreeRowName(GitCommit c) => Loc.Format(AppStrings.Git_Tree_Row, c.Short);

    internal async Task BrowseVersionAsync(PanelHost host)
    {
        SetActive(Array.IndexOf(_panels, host), focus: false);
        if (host.Panel.IsTree)
        {
            return;
        }

        if (host.Panel.IsTags)
        {
            await BrowseTagAsync(host);
            return;
        }

        if (host.Panel.IsHistory && _histories.TryGetValue(host, out var history) && host.Panel.FocusedEntry is { } row && history.ByName.TryGetValue(row.Name, out var focused))
        {
            await BrowseFromAsync(host, history.Top, focused, () => ShowHistoryCoreAsync(host, history.Top, history.Of, RowName(focused)));
            return;
        }

        if (host.Panel.IsCommit && _commits.TryGetValue(host, out var opened))
        {
            await BrowseFromAsync(host, opened.Top, opened.Commit, async () =>
            {
                await ShowCommitAsync(host, opened.Top, opened.Commit, opened.Of, opened.Back);
                return true;
            });
            return;
        }

        if ((host.Panel.GitListTop ?? GitTopOrSay(host)) is not { } top)
        {
            return;
        }

        var log = await Task.Run(() => _services.GitReader.Log(top, new GitLogQuery(Limit: 1)));
        if (log is not { Commits.Count: > 0 })
        {
            ShowNotice(AppStrings.Git_History_NoCommits);
            return;
        }

        var head = log.Commits[0];
        var from = host.Panel.Results is null ? host.Panel.Path : null;
        var focus = host.Panel.Results is null && host.Panel.FocusedEntry is { } f && !host.Panel.IsUp(host.Panel.Focus) ? f.Name : null;
        await BrowseFromAsync(host, top, head, async () => from is not null && await host.Panel.NavigateAsync(from, focus));
    }

    private async Task BrowseFromAsync(PanelHost host, string top, GitCommit commit, Func<Task<bool>> back)
    {
        if (!await ShowTreeAsync(host, top, commit, "", null, back, null))
        {
            ShowNotice(Loc.Format(AppStrings.Git_Tree_Unreadable, commit.Short));
        }
    }

    private async Task<bool> ShowTreeAsync(PanelHost host, string top, GitCommit commit, string folder, string? focus, Func<Task<bool>> back, IReadOnlyDictionary<string, GitCommitChange>? changes)
    {
        if (await ReadTreeAsync(host, top, commit, folder, back, changes) is not { } read)
        {
            return false;
        }

        var name = Path.GetFileName(top);
        var mark = (Func<PanelResults, PanelResults>)(r => r with { Tree = new PanelTree(top, commit.Sha, folder) });
        var known = _trees[host].Changes;
        host.Panel.ShowGitList(
            folder.Length == 0 ? Loc.Format(AppStrings.Git_Tree_What, name, commit.Short) : Loc.Format(AppStrings.Git_Tree_WhatIn, name, commit.Short, folder),
            read,
            mark,
            async () => await ReadTreeAsync(host, top, commit, folder, back, known) is { } again ? (again, mark) : null,
            focus,
            back: folder.Length == 0 ? back : () => ShowTreeAsync(host, top, commit, ParentOf(folder), Path.GetFileName(folder), back, known));
        UpdateGitListState(host);
        host.View.Focus();
        return true;

        static string ParentOf(string inner) => inner.LastIndexOf('\\') is var cut and > 0 ? inner[..cut] : "";
    }

    private async Task<IReadOnlyList<FileEntry>?> ReadTreeAsync(PanelHost host, string top, GitCommit commit, string folder, Func<Task<bool>> back, IReadOnlyDictionary<string, GitCommitChange>? changes)
    {
        var (items, changed) = await Task.Run(() => (
            _services.GitReader.Tree(top, commit.Sha, folder),
            changes ?? (IReadOnlyDictionary<string, GitCommitChange>?)_services.GitReader.CommitChanges(top, commit.Sha)?.ToDictionary(c => c.Path, StringComparer.OrdinalIgnoreCase)));
        if (items is null)
        {
            return null;
        }

        var usable = items.Where(i => WindowsCanHold(i.Name)).ToList();
        _trees[host] = new TreeShown(top, commit, folder, usable.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase), changed ?? new Dictionary<string, GitCommitChange>(), back);
        var ticks = commit.When.UtcDateTime.Ticks;
        return [.. usable.OrderBy(i => !i.IsFolder).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(i => new FileEntry(i.Name, i.Size, ticks, i.IsFolder ? FileAttributes.Directory : FileAttributes.Normal))];
    }

    internal static bool WindowsCanHold(string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
        {
            return false;
        }

        var stem = (name.IndexOf('.', StringComparison.Ordinal) is var dot and >= 0 ? name[..dot] : name).TrimEnd();
        return stem.ToUpperInvariant() switch
        {
            "CON" or "PRN" or "AUX" or "NUL" => false,
            var s when s.Length == 4 && (s.StartsWith("COM", StringComparison.Ordinal) || s.StartsWith("LPT", StringComparison.Ordinal)) && s[3] is >= '0' and <= '9' or '¹' or '²' or '³' => false,
            _ => true,
        };
    }

    private async Task<bool> OpenGitItemAsync(PanelHost host, FileEntry entry)
    {
        var panel = host.Panel;
        if (panel.IsTags)
        {
            await OpenTagAsync(host);
            return true;
        }

        if (panel.IsHistory)
        {
            await OpenCommitAsync(host, entry);
            return true;
        }

        if (panel.IsCommit && _commits.TryGetValue(host, out var opened) && entry.Name == TreeRowName(opened.Commit))
        {
            await BrowseVersionAsync(host);
            return true;
        }

        if (!panel.IsTree || !_trees.TryGetValue(host, out var shown) || !shown.ByName.TryGetValue(entry.Name, out var item))
        {
            return false;
        }

        if (item.IsFolder)
        {
            if (!await ShowTreeAsync(host, shown.Top, shown.Commit, shown.Inner(item.Name), null, shown.Out, shown.Changes))
            {
                ShowNotice(Loc.Format(AppStrings.Git_Tree_Unreadable, shown.Commit.Short));
            }
        }
        else
        {
            await ViewInTreeAsync(host, alternate: false);
        }

        return true;
    }

    internal async Task ViewInTreeAsync(PanelHost host, bool alternate)
    {
        if (!_trees.TryGetValue(host, out var shown) || host.Panel.FocusedEntry is not { } entry || !shown.ByName.TryGetValue(entry.Name, out var item))
        {
            return;
        }

        if (item.Submodule is { } linked)
        {
            ShowNotice(Loc.Format(AppStrings.Git_Tree_SubmoduleNotice, item.Name, linked));
            return;
        }

        if (item.IsFolder)
        {
            await OpenGitItemAsync(host, entry);
            return;
        }

        var folder = Path.Combine(_services.Temp.NewFolder("git"), shown.Commit.Short);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, item.Name);
        if (!await Task.Run(() => _services.GitReader.SaveVersionAt(shown.Top, shown.Inner(item.Name), shown.Commit.Sha, path)))
        {
            ShowNotice(Loc.Format(AppStrings.Git_Tree_Unreadable, shown.Commit.Short));
            return;
        }

        File.SetAttributes(path, FileAttributes.ReadOnly);
        OpenViewer(host, path, _services.Settings.Current.Viewers.KindFor(item.Name, alternate), siblings: [path]);
    }

    private static bool ChangesInTree(string id)
        => (id.StartsWith("File.", StringComparison.Ordinal) || id.StartsWith("Clipboard.", StringComparison.Ordinal) || id.StartsWith("Archive.", StringComparison.Ordinal)
                || id.StartsWith("Compare.", StringComparison.Ordinal) || id.StartsWith("Find.", StringComparison.Ordinal)
                || id is "Panel.OpenInExplorer" or "Panel.DirectoryContextMenu" or "Git.Stage" or "Git.Unstage" or "Git.Discard" or "Git.HistoryOfFile")
            && id is not "Clipboard.CopyName";

    private bool RunInTree(string id, PanelHost host)
    {
        if (host.Panel.IsTags && ChangesInTree(id) && id is not ("File.View" or "File.ViewAlternate"))
        {
            ShowNotice(AppStrings.Git_Tags_ReadOnly, 7, (AppStrings.Common_OK, () => { }));
            return true;
        }

        if (!host.Panel.IsTree || !_trees.TryGetValue(host, out var shown))
        {
            if (id is "File.Copy" or "File.Move" && OtherPanel(host).Panel.IsTree)
            {
                ShowNotice(Loc.Format(AppStrings.Git_Tree_NoCopyInto, _trees.TryGetValue(OtherPanel(host), out var into) ? into.Commit.Short : ""), 7, (AppStrings.Common_OK, () => { }));
                return true;
            }

            return false;
        }

        switch (id)
        {
            case "File.View" or "File.ViewAlternate":
                _ = ViewInTreeAsync(host, alternate: id == "File.ViewAlternate");
                return true;
            case "File.Copy":
                _ = CopyOutOfTreeAsync(host);
                return true;
        }

        if (!ChangesInTree(id))
        {
            return false;
        }

        ShowNotice(Loc.Format(AppStrings.Git_Tree_ReadOnly, shown.Commit.Short), 7, (AppStrings.Common_OK, () => { }));
        return true;
    }

    private bool GreyedInTree(string id)
        => _panels is not null && ((ActivePanel.Panel.IsTree && id is not ("File.View" or "File.ViewAlternate" or "File.Copy")) || (ActivePanel.Panel.IsTags && id is not ("File.View" or "File.ViewAlternate"))) && ChangesInTree(id);

    internal async Task CopyOutOfTreeAsync(PanelHost host)
    {
        if (_operationDialog is not null)
        {
            _operationDialog.Activate();
            return;
        }

        if (!_trees.TryGetValue(host, out var shown))
        {
            return;
        }

        var picked = host.Panel.GetOperationNames().Where(shown.ByName.ContainsKey).Select(n => shown.ByName[n]).Where(i => i.Submodule is null).ToList();
        if (picked.Count == 0)
        {
            ShowNotice(AppStrings.Notice_NothingToCopy);
            return;
        }

        ShowNotice(Loc.Format(AppStrings.Git_Tree_Reading, shown.Commit.Short), 3600);
        var staged = await Task.Run(() => StageTree(shown, picked));
        if (staged.Problem is { } problem)
        {
            ShowNotice(problem, 10, (AppStrings.Common_OK, () => { }));
            return;
        }

        ShowNotice(staged.Left > 0 ? Loc.Plural(staged.Left, AppStrings.Git_Tree_Left_One, AppStrings.Git_Tree_Left_Other) : Loc.Format(AppStrings.Git_Tree_Ready, shown.Commit.Short), staged.Left > 0 ? 10 : 3);
        var other = OtherPanel(host);
        var target = OnDrive(other) ? other.Panel.Path : "";
        var sources = staged.Paths.Select(p => new OperationSource(p, Directory.Exists(p))).ToList();
        if (await CopyMoveDialogAsync(host, OperationKind.Copy, sources, staged.Folder, staged.Bytes, target) is not null)
        {
            host.Panel.SelectWhere(_ => true, select: false);
        }
    }

    private (string Folder, List<string> Paths, long Bytes, int Left, string? Problem) StageTree(TreeShown shown, IReadOnlyList<GitTreeItem> picked)
    {
        var reader = _services.GitReader;
        var format = CurrentFormat();
        var plan = new List<(string Inner, string Relative, bool IsFolder)>();
        long bytes = 0;
        var files = 0;
        var left = 0;
        string TooMuch() => Loc.Format(AppStrings.Git_Tree_TooMuch, format.Number(MaxFilesCopiedOut), format.Size(MaxBytesCopiedOut));

        bool Walk(string inner, string relative, GitTreeItem item)
        {
            if (item.Submodule is not null)
            {
                return true;
            }

            if (!item.IsFolder)
            {
                plan.Add((inner, relative, false));
                bytes += item.Size;
                return ++files <= MaxFilesCopiedOut && bytes <= MaxBytesCopiedOut;
            }

            plan.Add((inner, relative, true));
            if (reader.Tree(shown.Top, shown.Commit.Sha, inner) is not { } below)
            {
                return false;
            }

            foreach (var child in below)
            {
                if (!WindowsCanHold(child.Name))
                {
                    left++;
                    continue;
                }

                if (!Walk(inner + "\\" + child.Name, relative + "\\" + child.Name, child))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var item in picked)
        {
            if (!Walk(shown.Inner(item.Name), item.Name, item))
            {
                return ("", [], 0, 0, files > MaxFilesCopiedOut || bytes > MaxBytesCopiedOut ? TooMuch() : Loc.Format(AppStrings.Git_Tree_Unreadable, shown.Commit.Short));
            }
        }

        var folder = Path.Combine(_services.Temp.NewFolder("git"), shown.Commit.Short);
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var (inner, relative, isFolder) in plan)
            {
                var path = Path.Join(folder, relative);
                if (isFolder)
                {
                    Directory.CreateDirectory(path);
                }
                else if (!reader.SaveVersionAt(shown.Top, inner, shown.Commit.Sha, path))
                {
                    return ("", [], 0, 0, Loc.Format(AppStrings.Git_Tree_Unreadable, shown.Commit.Short));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ("", [], 0, 0, ex.Message);
        }

        return (folder, [.. picked.Select(i => Path.Join(folder, i.Name))], bytes, left, null);
    }
}
