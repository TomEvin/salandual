// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.GitSync.cs

using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Git;
using Salandual.Core.Operations;
using Salandual.Core.Resources;

namespace Salandual.App;

public partial class MainWindow
{
    private readonly Dictionary<string, OperationJob> _gitJobs = new(StringComparer.OrdinalIgnoreCase);

    internal CloneDialog? CloneShown { get; private set; }

    internal OperationJob? LastGitJob { get; private set; }

    internal void Fetch(PanelHost host)
    {
        if (SyncTop(host) is not { } at)
        {
            return;
        }

        var upstream = at.Status?.Upstream;
        var route = upstream is not null && upstream.IndexOf('/', StringComparison.Ordinal) is > 0 and var cut ? upstream[..cut] : GitSync.PushRemote(at.Remotes) ?? at.Remotes[0];
        StartGitJob(host, GitSyncKind.Fetch, at.Top, at.Top, GitSync.Fetch(), route);
    }

    internal void Pull(PanelHost host)
    {
        if (SyncTop(host) is not { } at)
        {
            return;
        }

        var route = at.Status is { Branch: { } branch, Upstream: { } up } ? Loc.Format(AppStrings.Git_Job_PullRoute, branch, up) : at.Status?.Label ?? "";
        StartGitJob(host, GitSyncKind.Pull, at.Top, at.Top, GitSync.Pull(), route);
    }

    internal void Push(PanelHost host)
    {
        if (SyncTop(host) is { } at)
        {
            StartPush(host, at.Top, at.Status, at.Remotes);
        }
    }

    private void StartPush(PanelHost host, string top)
    {
        var remotes = _services.GitReader.Remotes(top);
        StartPush(host, top, _services.GitReader.Status(top), remotes);
    }

    private void StartPush(PanelHost host, string top, GitStatus? status, IReadOnlyList<string> remotes)
    {
        var upstream = status?.Upstream;
        var remote = upstream is null ? GitSync.PushRemote(remotes) : null;
        var branch = status?.Label ?? "";
        var route = upstream is not null ? Loc.Format(AppStrings.Git_Job_PushRoute, branch, upstream)
            : remote is not null ? Loc.Format(AppStrings.Git_Job_PushNew, branch, remote)
            : branch;
        StartGitJob(host, GitSyncKind.Push, top, top, GitSync.Push(upstream is not null, remote), route);
    }

    private (string Top, GitStatus? Status, IReadOnlyList<string> Remotes)? SyncTop(PanelHost host)
    {
        if ((host.Panel.GitListTop ?? GitTopOrSay(host)) is not { } top)
        {
            return null;
        }

        if (_services.GitCommands.Program is null)
        {
            ShowNotice(AppStrings.Git_NotInstalled);
            return null;
        }

        var remotes = _services.GitReader.Remotes(top);
        if (remotes.Count == 0)
        {
            ShowNotice(Loc.Format(AppStrings.Git_NoRemote, Path.GetFileName(top)));
            return null;
        }

        return (top, GitStates?.StatusOf(top) ?? _services.GitReader.Status(top), remotes);
    }

    internal async Task CloneAsync(PanelHost host)
    {
        if (_services.GitCommands.Program is null)
        {
            ShowNotice(AppStrings.Git_NotInstalled);
            return;
        }

        var other = OtherPanel(host);
        var offered = OnDrive(other) ? other.Panel.Path : OnDrive(host) ? host.Panel.Path : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dialog = CloneShown = new CloneDialog(offered, CloneFolderProblem);
        CloneRequest? request;
        try
        {
            request = await dialog.ShowDialog<CloneRequest?>(this);
        }
        finally
        {
            CloneShown = null;
        }

        host.View.Focus();
        if (request is null)
        {
            return;
        }

        var parent = Path.GetDirectoryName(request.Folder)!;
        var name = Path.GetFileName(request.Folder);
        StartGitJob(host, GitSyncKind.Clone, request.Folder, parent, GitSync.Clone(request.Address, request.Folder),
            Loc.Format(AppStrings.Git_Job_CloneRoute, request.Address, request.Folder), name, request.OpenWhenDone);
    }

    private bool OnDrive(PanelHost host)
        => host.Panel.Path.Length > 2 && host.Panel.Results is null && !OnServer(host) && ArchiveAt(host) is null && Path.IsPathFullyQualified(host.Panel.Path);

    private string? CloneFolderProblem(string folder)
    {
        if (folder.Length == 0 || !Path.IsPathFullyQualified(folder) || Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder)) is not { Length: > 0 } parent
            || Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)).Length == 0)
        {
            return AppStrings.Git_Clone_NotAFolder;
        }

        if (GitStates is { } git && !git.MayHold(folder))
        {
            return AppStrings.Git_Clone_NotHere;
        }

        try
        {
            if (!Directory.Exists(parent))
            {
                return Loc.Format(AppStrings.Git_Clone_NoParent, parent);
            }

            if (File.Exists(folder) || (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()))
            {
                return Loc.Format(AppStrings.Git_Clone_NotEmpty, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        return null;
    }

    private void StartGitJob(PanelHost host, GitSyncKind kind, string key, string folder, IReadOnlyList<string> arguments, string route, string? name = null, bool openWhenDone = false)
    {
        name ??= Path.GetFileName(key);
        var title = Loc.Format(kind switch
        {
            GitSyncKind.Fetch => AppStrings.Git_Job_Fetch,
            GitSyncKind.Pull => AppStrings.Git_Job_Pull,
            GitSyncKind.Push => AppStrings.Git_Job_Push,
            _ => AppStrings.Git_Job_Clone,
        }, name);
        _gitJobs.TryGetValue(key, out var before);
        before = before is { IsFinished: false } ? before : null;
        GitRun? said = null;
        var job = GitSync.Job(kind, title, route, folder, arguments, _services.GitCommands, run => said = run, before,
            before is null ? null : Loc.Format(AppStrings.Git_Job_Waits, before.Title));
        _gitJobs[key] = job;
        LastGitJob = job;
        Operations.Add(job, toQueue: false);
        ShowGitRunning(job);
        job.Changed += (_, _) => Dispatcher.UIThread.Post(() => GitRunningChanged(job));
        _ = job.Completion.ContinueWith(_ => Dispatcher.UIThread.Post(() => GitJobEnded(host, kind, key, job, said, openWhenDone)), TaskScheduler.Default);
    }

    private OperationJob? _gitNoticeJob;

    private void ShowGitRunning(OperationJob job)
    {
        _gitNoticeJob = job;
        ShowNotice(Loc.Format(AppStrings.Git_Job_Running, job.Title), 3600, (AppStrings.Git_Job_Stop, job.Cancel), (AppStrings.Common_Show, () => ToggleOperationsPanel()));
        GitRunningChanged(job);
    }

    private void GitRunningChanged(OperationJob job)
    {
        if (!ReferenceEquals(_gitNoticeJob, job) || job.IsFinished || NoticeMessage != Loc.Format(AppStrings.Git_Job_Running, job.Title))
        {
            return;
        }

        var p = job.Progress;
        ShowNoticeProgress(p.Fraction > 0 ? p.Fraction : null, p.Current ?? AppStrings.Ops_GitStarting);
    }

    private void GitJobEnded(PanelHost host, GitSyncKind kind, string key, OperationJob job, GitRun? run, bool openWhenDone)
    {
        if (ReferenceEquals(_gitNoticeJob, job))
        {
            _gitNoticeJob = null;
        }

        if (_gitJobs.TryGetValue(key, out var last) && ReferenceEquals(last, job))
        {
            _gitJobs.Remove(key);
        }

        if (kind != GitSyncKind.Clone)
        {
            GitStates?.Reread(key);
            if (host.Panel.IsGitList)
            {
                _ = RereadShownChangesAsync(host);
            }
        }

        switch (job.State)
        {
            case JobState.Done when kind == GitSyncKind.Clone:
                if (openWhenDone && Directory.Exists(key))
                {
                    _ = host.Panel.NavigateAsync(key);
                }

                ShowNotice(Loc.Format(AppStrings.Git_Job_Cloned, Path.GetFileName(key), key));
                break;
            case JobState.Done:
                ShowNotice(job.Outcome is { Length: > 0 } words ? Loc.Format(AppStrings.Git_Job_DoneSaid, job.Title, words) : Loc.Format(AppStrings.Git_Job_Done, job.Title));
                break;
            case JobState.Failed when kind == GitSyncKind.Pull && run is not null && GitSync.StoppedOnConflict(run):
                ShowNotice(Loc.Format(AppStrings.Git_Job_Conflict, job.Title), (AppStrings.Git_Job_ShowChanges, () => _ = ShowChangesAsync(host)));
                break;
            case JobState.Cancelled:
                ShowNotice(Loc.Format(AppStrings.Git_Job_Stopped, job.Title));
                break;
            case JobState.Failed:
                ShowNotice(Loc.Format(AppStrings.Git_Job_Failed, job.Title, GitSync.Gist(job.Error?.Message ?? "")),(AppStrings.Common_Show, () => ToggleOperationsPanel()));
                break;
        }
    }
}
