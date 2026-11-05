// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.GitBlame.cs

using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.App.Viewer;
using Salandual.Core.Git;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App;

public partial class MainWindow
{
    internal ViewerWindow? BlameShown { get; private set; }

    internal async Task BlameAsync(PanelHost host)
    {
        SetActive(Array.IndexOf(_panels, host), focus: false);
        var panel = host.Panel;
        if (panel.IsCommit && _commits.TryGetValue(host, out var opened) && panel.FocusedEntry is { } inCommit && opened.ByPath.TryGetValue(inCommit.Name, out var change))
        {
            var sha = change.State == GitState.Deleted ? opened.Commit.Parents.FirstOrDefault() : opened.Commit.Sha;
            if (sha is not null)
            {
                await BlameAtAsync(host, opened.Top, change.Path, sha);
            }

            return;
        }

        if (panel.IsTree && _trees.TryGetValue(host, out var tree) && panel.FocusedEntry is { IsDirectory: false } inTree && tree.ByName.TryGetValue(inTree.Name, out var item) && item.Submodule is null)
        {
            await BlameAtAsync(host, tree.Top, tree.Inner(item.Name), tree.Commit.Sha);
            return;
        }

        if (panel.IsHistory || panel.IsTags || panel.IsTree)
        {
            ShowNotice(AppStrings.Git_Blame_PickFile);
            return;
        }

        if ((panel.GitListTop ?? GitTopOrSay(host)) is not { } top)
        {
            return;
        }

        if (panel.FocusedEntry is not { IsDirectory: false } focused || panel.IsUp(panel.Focus) || panel.IsHeading(panel.Focus))
        {
            ShowNotice(AppStrings.Git_Blame_PickFile);
            return;
        }

        var full = Path.IsPathFullyQualified(focused.Name) ? focused.Name : Path.Join(panel.Path, focused.Name);
        var relative = Path.GetRelativePath(top, full);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative) || !File.Exists(full))
        {
            ShowNotice(AppStrings.Git_Blame_PickFile);
            return;
        }

        var (blame, refusal) = await Task.Run(() => _services.GitReader.Blame(top, relative, null));
        if (blame is null)
        {
            ShowNotice(Refused(refusal, Path.GetFileName(full)));
            return;
        }

        BlameShown = OpenViewer(host, full, ViewerKind.Text, siblings: [full], blame: BlameFor(host, top, relative, blame));
    }

    private async Task BlameAtAsync(PanelHost host, string top, string path, string sha)
    {
        var name = Path.GetFileName(path);
        var (blame, content) = await Task.Run(() => (_services.GitReader.Blame(top, path, sha), _services.GitReader.VersionAt(top, path, sha, MaxDiffBytes)));
        if (blame.Blame is not { } found)
        {
            ShowNotice(Refused(blame.Refusal, name));
            return;
        }

        if (content is null)
        {
            ShowNotice(Loc.Format(AppStrings.Git_Diff_TooBig, name));
            return;
        }

        var file = WriteVersion(Path.Combine(_services.Temp.NewFolder("git"), sha[..Math.Min(7, sha.Length)]), name, content);
        BlameShown = OpenViewer(host, file, ViewerKind.Text, siblings: [file], blame: BlameFor(host, top, path, found));
    }

    private static string Refused(GitBlameRefusal refusal, string name) => refusal switch
    {
        GitBlameRefusal.TooBig => Loc.Format(AppStrings.Git_Blame_TooBig, name, GitBlame.MaxLines.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)),
        GitBlameRefusal.Binary => Loc.Format(AppStrings.Git_Blame_Binary, name),
        GitBlameRefusal.NotInGit => Loc.Format(AppStrings.Git_Blame_NotInGit, name),
        _ => AppStrings.Git_ChangesUnreadable,
    };

    private ViewerBlame BlameFor(PanelHost host, string top, string path, GitBlame blame)
    {
        var format = CurrentFormat();
        return new ViewerBlame
        {
            Column = new ViewerBlameColumn(blame, run => run.Commit is { } c
                ? c.Short + " " + ShortAuthor(c.Author) + " · " + format.Date(c.When.LocalDateTime)
                : "– " + AppStrings.Git_Blame_NotCommitted),
            Words = run => run.Commit is { } c
                ? string.Join(" · ", c.Short, c.Summary, c.Author, format.Date(c.When.LocalDateTime) + " " + format.Time(c.When.LocalDateTime))
                : AppStrings.Git_Blame_NotCommitted,
            OpenCommit = commit =>
            {
                Activate();
                _ = ShowCommitAsync(host, top, commit, null);
            },
            Before = commit =>
            {
                _ = BlameAtAsync(host, top, path, commit.Parents[0]);
            },
        };
    }

    internal static string ShortAuthor(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? name : parts[0] + " " + char.ToUpper(parts[^1][0], System.Globalization.CultureInfo.CurrentCulture) + ".";
    }
}
