// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Git.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Salandual.App.Dialogs;
using Salandual.App.Panels;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Git;

namespace Salandual.App;

public partial class MainWindow
{
    private const int MaxBranchesListed = 30;

    private GitStates? GitStates => (Application.Current as App)?.Git;

    internal ContextMenu? GitMenu { get; private set; }

    internal NewBranchDialog? NewBranchShown { get; private set; }

    internal ConfirmDialog? GitMessageShown { get; private set; }

    private void WireGit(PanelHost host)
    {
        host.DirectoryLine.BranchMenuRequested += (_, _) => _ = ShowBranchMenuAsync(host);
        host.GitListText = GitListBarText;
        host.Panel.GitItemOpened = entry => OpenGitItemAsync(host, entry);
        host.Panel.Changed += (_, change) =>
        {
            if ((change & Core.Panels.PanelChange.Items) != 0)
            {
                ChangesLeft(host);
            }
        };
        if (GitStates is { } git)
        {
            git.Changed += (_, _) =>
            {
                UpdateGitState(host);
                if (host.Panel.IsGitList && GitWork.IsCompleted)
                {
                    _ = RereadShownChangesAsync(host);
                }
            };
        }
    }

    private bool GitShownIn(PanelHost host)
        => host.Panel.Path.Length > 0 && (host.Panel.Results is null || host.Panel.IsGitList) && !OnServer(host) && ArchiveAt(host) is null;

    private void UpdateGitState(PanelHost host)
    {
        var status = GitShownIn(host) ? GitStates?.StatusOf(host.Panel.Path) : null;
        host.DirectoryLine.Git = status is null ? null : new GitFacts(status.Label, status.Ahead, status.Behind, status.Upstream, status.Branch is null);
        if (status is null)
        {
            if (host.GitInfo is not null)
            {
                host.GitInfo = null;
            }

            return;
        }

        var folder = host.Panel.Path;
        host.GitInfo = entry =>
        {
            var path = Path.IsPathFullyQualified(entry.Name) ? entry.Name : Path.Join(folder, entry.Name);
            return entry.IsDirectory && status.SubmoduleAt(path) is { } sub
                ? SubmoduleWords(sub)
                : StateWords(status.Of(path, entry.IsDirectory, out var inside), inside);
        };

        if (!host.Panel.IsGitList && status.SubmoduleAt(folder) is { State: GitSubmoduleState.NotCheckedOut } empty)
        {
            if (!string.Equals(_submoduleSaid.GetValueOrDefault(host), folder, StringComparison.OrdinalIgnoreCase))
            {
                _submoduleSaid[host] = folder;
                ShowNotice(Loc.Format(AppStrings.Git_Submodule_EmptyNotice, empty.Path));
            }
        }
        else
        {
            _submoduleSaid.Remove(host);
        }
    }

    private readonly Dictionary<PanelHost, string> _submoduleSaid = [];

    internal static string SubmoduleWords(GitSubmodule sub) => sub.State switch
    {
        GitSubmoduleState.NotCheckedOut => AppStrings.Git_Submodule_NotCheckedOut,
        GitSubmoduleState.OtherCommit => Loc.Format(AppStrings.Git_Submodule_At, sub.Recorded) + " · " + AppStrings.Git_Submodule_OtherCommit,
        GitSubmoduleState.ChangesInside => Loc.Format(AppStrings.Git_Submodule_At, sub.Recorded) + " · " + AppStrings.Git_Submodule_ChangesInside,
        _ => Loc.Format(AppStrings.Git_Submodule_At, sub.Recorded),
    };

    internal static string? StateWords(GitState state, bool inside) => inside && state != GitState.Ignored ? AppStrings.Git_State_Inside : state switch
    {
        GitState.Changed => AppStrings.Git_State_Changed,
        GitState.Staged => AppStrings.Git_State_Staged,
        GitState.Added => AppStrings.Git_State_Added,
        GitState.New => AppStrings.Git_State_New,
        GitState.Deleted => AppStrings.Git_State_Deleted,
        GitState.Conflicted => AppStrings.Git_State_Conflicted,
        GitState.Ignored => AppStrings.Git_State_Ignored,
        _ => null,
    };

    private bool RunGitCommand(string id, PanelHost host)
    {
        if (RunInTree(id, host))
        {
            return true;
        }

        switch (id)
        {
            case "Git.Menu":
                _ = ShowBranchMenuAsync(host);
                return true;
            case "Git.Changes":
                _ = ShowChangesAsync(host);
                return true;
            case "Git.Stage":
                _ = StageAsync(host);
                return true;
            case "Git.Unstage":
                _ = UnstageAsync(host);
                return true;
            case "Git.Discard":
                _ = DiscardAsync(host);
                return true;
            case "Git.Commit":
                _ = CommitAsync(host);
                return true;
            case "Git.History":
                _ = ShowHistoryAsync(host, ofFocused: false);
                return true;
            case "Git.HistoryOfFile":
                _ = ShowHistoryAsync(host, ofFocused: true);
                return true;
            case "Git.Fetch":
                Fetch(host);
                return true;
            case "Git.Pull":
                Pull(host);
                return true;
            case "Git.Push":
                Push(host);
                return true;
            case "Git.Clone":
                _ = CloneAsync(host);
                return true;
            case "Git.Browse":
                _ = BrowseVersionAsync(host);
                return true;
            case "Git.Blame":
                _ = BlameAsync(host);
                return true;
            case "Git.Tags":
                _ = ShowTagsAsync(host);
                return true;
            case "File.View" or "File.ViewAlternate" when host.Panel.IsTags:
                _ = BrowseTagAsync(host);
                return true;
            case "Git.Graph":
                _ = ShowGraphAsync(host);
                return true;
            case "File.View" or "File.ViewAlternate" when host.Panel.IsHistory && host.Panel.FocusedEntry is { IsDirectory: false } commit:
                _ = OpenCommitAsync(host, commit);
                return true;
            case "File.View" when host.Panel.IsCommit:
                _ = ShowCommitDiffAsync(host);
                return true;
            case "File.ViewAlternate" or "Panel.OpenInExplorer" when host.Panel.IsCommit:
                _ = ViewAsItWasAsync(host);
                return true;
            case "File.View" when host.Panel.IsChanges:
                _ = ShowGitDiffAsync(host);
                return true;
            case "Git.NewBranch":
                _ = NewBranchAsync(host);
                return true;
            default:
                return false;
        }
    }

    private string? GitTopOrSay(PanelHost host)
    {
        if (GitShownIn(host) && GitStates?.TopFor(host.Panel.Path) is { } top)
        {
            return top;
        }

        ShowNotice(AppStrings.Git_NotInRepository);
        return null;
    }

    internal async Task ShowBranchMenuAsync(PanelHost host)
    {
        SetActive(Array.IndexOf(_panels, host), focus: false);
        if (GitShownIn(host) && !host.Panel.IsGitList && GitStates is { } states && states.TopFor(host.Panel.Path) is null && states.MayHold(host.Panel.Path)
            && _services.GitCommands.Program is not null && OnDrive(host))
        {
            await CloneAsync(host);
            return;
        }

        if (GitTopOrSay(host) is not { } top)
        {
            return;
        }

        var status = GitStates?.StatusOf(host.Panel.Path);
        var (branches, remotes) = await Task.Run(() => (_services.GitReader.Branches(top), _services.GitReader.Remotes(top)));
        var git = _services.GitCommands.Program is not null;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = Loc.Format(AppStrings.Git_MenuHead, Path.GetFileName(top), status is null ? "" : Summary(status)), IsEnabled = false });
        menu.Items.Add(new Separator());
        var changes = new MenuItem { Header = BranchRow(AppStrings.Git_Menu_Changes, "C"), Tag = "Git.Changes" };
        changes.Click += (_, _) => _ = ShowChangesAsync(host);
        menu.Items.Add(changes);
        var commit = new MenuItem { Header = BranchRow(AppStrings.Git_Menu_Commit, "M"), Tag = "Git.Commit", IsEnabled = git };
        commit.Click += (_, _) => _ = CommitAsync(host);
        menu.Items.Add(commit);
        var history = new MenuItem { Header = BranchRow(AppStrings.Git_Menu_History, "H"), Tag = "Git.History" };
        history.Click += (_, _) => _ = ShowHistoryAsync(host, ofFocused: false);
        menu.Items.Add(history);
        var historyOf = new MenuItem { Header = BranchRow(AppStrings.Git_Menu_HistoryOfFile, "F"), Tag = "Git.HistoryOfFile" };
        historyOf.Click += (_, _) => _ = ShowHistoryAsync(host, ofFocused: true);
        menu.Items.Add(historyOf);
        var graph = new MenuItem { Header = BranchRow(AppStrings.Git_Menu_Graph, "G"), Tag = "Git.Graph" };
        graph.Click += (_, _) => _ = ShowGraphAsync(host);
        menu.Items.Add(graph);
        var browse = new MenuItem { Header = BranchRow(AppStrings.Git_Menu_Browse, "B"), Tag = "Git.Browse" };
        browse.Click += (_, _) => _ = BrowseVersionAsync(host);
        menu.Items.Add(browse);
        var blame = new MenuItem { Header = BranchRow(AppStrings.Git_Menu_Blame, "W"), Tag = "Git.Blame" };
        blame.Click += (_, _) => _ = BlameAsync(host);
        menu.Items.Add(blame);
        var tags = new MenuItem { Header = BranchRow(AppStrings.Git_Menu_Tags, "A"), Tag = "Git.Tags" };
        tags.Click += (_, _) => _ = ShowTagsAsync(host);
        menu.Items.Add(tags);
        menu.Items.Add(new Separator());
        var fetch = new MenuItem { Header = BranchRow(AppStrings.Git_Menu_Fetch, "T"), Tag = "Git.Fetch", IsEnabled = git && remotes.Count > 0 };
        fetch.Click += (_, _) => Fetch(host);
        menu.Items.Add(fetch);
        var pull = new MenuItem { Header = BranchRow(AppStrings.Git_Menu_Pull, "L"), Tag = "Git.Pull", IsEnabled = git && remotes.Count > 0 };
        pull.Click += (_, _) => Pull(host);
        menu.Items.Add(pull);
        var push = new MenuItem { Header = BranchRow(AppStrings.Git_Menu_Push, "P"), Tag = "Git.Push", IsEnabled = git && remotes.Count > 0 };
        push.Click += (_, _) => Push(host);
        menu.Items.Add(push);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = AppStrings.Git_SwitchTo, IsEnabled = false });
        foreach (var branch in branches.Where(b => !b.IsRemote).Take(MaxBranchesListed).Concat(branches.Where(b => b.IsRemote).Take(MaxBranchesListed)))
        {
            var note = branch.IsRemote ? AppStrings.Git_Remote : DirectoryLine.AheadBehind(new GitFacts(branch.Name, branch.Ahead, branch.Behind, null, false));
            var item = new MenuItem
            {
                Header = BranchRow(branch.Name, note.Length > 0 ? note : null),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = branch.IsCurrent,
                IsEnabled = git || branch.IsCurrent,
                Tag = branch.Name,
            };
            var chosen = branch;
            item.Click += (_, _) =>
            {
                if (!chosen.IsCurrent)
                {
                    _ = SwitchBranchAsync(host, top, chosen);
                }
            };
            menu.Items.Add(item);
        }

        var hidden = branches.Count(b => !b.IsRemote) - MaxBranchesListed + branches.Count(b => b.IsRemote) - MaxBranchesListed;
        if (hidden > 0)
        {
            menu.Items.Add(new MenuItem { Header = Loc.Plural(hidden, AppStrings.Git_MoreBranches_One, AppStrings.Git_MoreBranches_Other), IsEnabled = false });
        }

        var create = new MenuItem { Header = BranchRow(AppStrings.Git_NewBranchMenu, "N"), IsEnabled = git, InputGesture = GestureOf("Git.NewBranch"), Tag = "Git.NewBranch" };
        create.Click += (_, _) => _ = NewBranchAsync(host);
        menu.Items.Add(create);
        if (!git)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = AppStrings.Git_NotInstalled, IsEnabled = false });
        }

        menu.PlacementTarget = host.DirectoryLine;
        menu.Placement = PlacementMode.AnchorAndGravity;
        menu.PlacementAnchor = PopupAnchor.BottomRight;
        menu.PlacementGravity = PopupGravity.BottomLeft;
        menu.PlacementRect = host.DirectoryLine.BranchBounds.Width > 0 ? host.DirectoryLine.BranchBounds : new Rect(host.DirectoryLine.Bounds.Size).WithHeight(host.DirectoryLine.Bounds.Height);
        menu.AddHandler(KeyDownEvent, (_, e) =>
        {
            var item = e.KeyModifiers == Avalonia.Input.KeyModifiers.None ? e.Key switch
            {
                Avalonia.Input.Key.C => changes,
                Avalonia.Input.Key.M => commit,
                Avalonia.Input.Key.H => history,
                Avalonia.Input.Key.F => historyOf,
                Avalonia.Input.Key.G => graph,
                Avalonia.Input.Key.B => browse,
                Avalonia.Input.Key.W => blame,
                Avalonia.Input.Key.A => tags,
                Avalonia.Input.Key.T => fetch,
                Avalonia.Input.Key.L => pull,
                Avalonia.Input.Key.P => push,
                Avalonia.Input.Key.N => create,
                _ => null,
            } : null;
            if (item is { IsEnabled: true })
            {
                e.Handled = true;
                menu.Close();
                item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        menu.Closed += (_, _) => host.View.Focus();
        GitMenu = menu;
        menu.Open(host.DirectoryLine);
    }

    private static string Summary(GitStatus status)
        => status.Branch is null ? Loc.Format(AppStrings.Git_Summary_Detached, status.DetachedAt ?? "")
            : status.Upstream is null ? Loc.Format(AppStrings.Git_Summary_NoUpstream, status.Branch)
            : status.Ahead == 0 && status.Behind == 0 ? Loc.Format(AppStrings.Git_Summary_Even, status.Branch, status.Upstream)
            : Loc.Format(AppStrings.Git_Summary, status.Branch, string.Join(", ", new[]
            {
                status.Ahead > 0 ? Loc.Plural(status.Ahead, AppStrings.Git_ToPush_One, AppStrings.Git_ToPush_Other) : "",
                status.Behind > 0 ? Loc.Plural(status.Behind, AppStrings.Git_ToPull_One, AppStrings.Git_ToPull_Other) : "",
            }.Where(s => s.Length > 0)));

    private static Control BranchRow(string name, string? note)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinWidth = 260 };
        grid.Children.Add(new TextBlock { Text = name, MaxWidth = 360, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        if (note is not null)
        {
            var right = new TextBlock { Text = note, FontSize = 12, Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }

        return grid;
    }

    internal async Task SwitchBranchAsync(PanelHost host, string top, GitBranch branch)
    {
        var run = await RunGitAsync(top, GitCommandLines.Switch(branch));
        GitStates?.Reread(top);
        if (run.Succeeded)
        {
            ShowNotice(Loc.Format(AppStrings.Git_Switched, branch.IsRemote ? branch.Name[(branch.Name.IndexOf('/', StringComparison.Ordinal) + 1)..] : branch.Name));
            return;
        }

        await ShowGitMessageAsync(Loc.Format(AppStrings.Git_SwitchFailed, branch.Name), run.Message);
        host.View.Focus();
    }

    internal async Task NewBranchAsync(PanelHost host)
    {
        if (GitTopOrSay(host) is not { } top)
        {
            return;
        }

        if (_services.GitCommands.Program is null)
        {
            ShowNotice(AppStrings.Git_NotInstalled);
            return;
        }

        var status = GitStates?.StatusOf(host.Panel.Path) ?? await Task.Run(() => _services.GitReader.Status(top));
        var branches = await Task.Run(() => _services.GitReader.Branches(top));
        var dialog = NewBranchShown = new NewBranchDialog(Path.GetFileName(top), status?.Label ?? "HEAD", branches.Where(b => !b.IsRemote).Select(b => b.Name),
            (name, switchTo) => RunGitAsync(top, GitCommandLines.NewBranch(name, switchTo)));
        string? made;
        try
        {
            made = await dialog.ShowDialog<string?>(this);
        }
        finally
        {
            NewBranchShown = null;
        }

        GitStates?.Reread(top);
        if (made is not null)
        {
            ShowNotice(Loc.Format(dialog.SwitchBox.IsChecked == true ? AppStrings.Git_MadeAndSwitched : AppStrings.Git_Made, made));
        }

        host.View.Focus();
    }

    private async Task<GitRun> RunGitAsync(string top, IReadOnlyList<string> arguments)
    {
        try
        {
            return await _services.GitCommands.RunAsync(top, arguments);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or OperationCanceledException)
        {
            return new GitRun(-1, "", ex.Message);
        }
    }

    private async Task ShowGitMessageAsync(string title, string message)
    {
        var dialog = GitMessageShown = new ConfirmDialog(title, message, AppStrings.Common_OK, AppStrings.Common_OK);
        dialog.Yes.IsVisible = false;
        try
        {
            await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            GitMessageShown = null;
        }
    }
}
