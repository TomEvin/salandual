// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/GitSyncTests.cs

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LibGit2Sharp;
using Salandual.Core.Git;
using Salandual.Core.Operations;

namespace Salandual.App.Tests;

public sealed class GitSyncTests : IDisposable
{
    private static readonly Signature _ann = new("Ann Example", "ann@example.invalid", new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-gitsync-").FullName;
    private readonly string _repo;
    private readonly MemoryGitCommands _git = new() { Answer = new GitRun(0, "Already up to date.\n", "") };

    public GitSyncTests()
    {
        _repo = Path.Join(_dir, "garden-planner");
        Repository.Init(_repo);
        using var repo = new Repository(_repo);
        repo.Refs.UpdateTarget("HEAD", "refs/heads/main");
        repo.Config.Set("user.name", "Ann Example", ConfigurationLevel.Local);
        repo.Config.Set("user.email", "ann@example.invalid", ConfigurationLevel.Local);
        File.WriteAllText(Path.Join(_repo, "Planner.cs"), "plan\n");
        Commands.Stage(repo, "*");
        var first = repo.Commit("Plant the first beds", _ann, _ann);
        repo.Network.Remotes.Add("origin", "https://example.invalid/garden.git");
        repo.Refs.Add("refs/remotes/origin/main", first.Id);
        repo.Branches.Update(repo.Head, b => b.Remote = "origin", b => b.UpstreamBranch = "refs/heads/main");
        repo.CreateBranch("frost", first);
    }

    public void Dispose()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_dir, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 50)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 1000 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private async Task<MainWindow> Open(string? folder = null)
    {
        var w = new MainWindow { Width = 1200, Height = 700 };
        w.Services.GitCommands = _git;
        w.Show();
        await w.RightPanel.Panel.NavigateAsync(_dir);
        await w.ActivePanel.Panel.NavigateAsync(folder ?? _repo);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        if (folder is null)
        {
            await Until(() => w.ActivePanel.DirectoryLine.Git is not null);
        }

        return w;
    }

    private static async Task<OperationJob> Key(MainWindow w, Key key, OperationJob? before)
    {
        await w.ShowBranchMenuAsync(w.ActivePanel);
        w.GitMenu!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.None });
        await Until(() => w.LastGitJob is { IsFinished: true } job && !ReferenceEquals(job, before));
        Dispatcher.UIThread.RunJobs();
        return w.LastGitJob!;
    }

    [AvaloniaFact]
    public async Task BranchMenu_TLP_FetchPullAndPush_AsJobs_WithANoticeOfGitsWords()
    {
        var w = await Open();
        try
        {
            await w.ShowBranchMenuAsync(w.ActivePanel);
            var items = w.GitMenu!.Items.OfType<MenuItem>().ToList();
            Assert.All(new[] { "Git.Fetch", "Git.Pull", "Git.Push" }, tag => Assert.True(items.Single(i => (string?)i.Tag == tag).IsEnabled, tag));
            w.GitMenu.Close();

            var pull = await Key(w, Avalonia.Input.Key.L, null);
            Assert.Equal(OperationKind.Git, pull.Kind);
            Assert.Equal("Pull garden-planner", pull.Title);
            Assert.Equal("main ← origin/main", pull.Route);
            Assert.Equal(JobState.Done, pull.State);
            Assert.Contains(pull, w.Operations.Jobs);
            Assert.Equal(_repo, _git.Runs[^1].Top);
            Assert.Equal(["pull", "--progress"], _git.Runs[^1].Arguments);
            Assert.Equal("Pull garden-planner: Already up to date.", w.NoticeMessage);

            var fetch = await Key(w, Avalonia.Input.Key.T, pull);
            Assert.Equal(["fetch", "--progress"], _git.Runs[^1].Arguments);
            Assert.Equal("origin", fetch.Route);

            _git.Answer = new GitRun(0, "", "To https://example.invalid/garden.git\n   a41f2c9..7c03e1b  main -> main\n");
            var push = await Key(w, Avalonia.Input.Key.P, fetch);
            Assert.Equal(["push", "--progress"], _git.Runs[^1].Arguments);
            Assert.Equal("main → origin/main", push.Route);
            Assert.Equal("Push garden-planner: a41f2c9..7c03e1b  main -> main", w.NoticeMessage);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task WhileGitRuns_ANoticeSaysSo_WithItsProgress_AndStopEndsIt()
    {
        var w = await Open();
        var hold = new TaskCompletionSource();
        _git.Hold = hold.Task;
        _git.Lines = ["Receiving objects:  64% (210/328)"];
        try
        {
            await w.ShowBranchMenuAsync(w.ActivePanel);
            w.GitMenu!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Avalonia.Input.Key.T, KeyModifiers = KeyModifiers.None });
            await Until(() => w.NoticeProgress == "Receiving objects:  64% (210/328)");
            Assert.Equal("Fetch garden-planner…", w.NoticeMessage);
            Assert.Equal(["Stop", "Show"], w.NoticeButtons.Select(b => b.Content as string));
            Assert.False(w.LastGitJob!.IsFinished);

            w.NoticeButtons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Until(() => w.LastGitJob.IsFinished);
            Dispatcher.UIThread.RunJobs();
            await Until(() => w.NoticeMessage == "Fetch garden-planner was stopped.");
            Assert.Equal(JobState.Cancelled, w.LastGitJob.State);
        }
        finally
        {
            hold.TrySetResult();
            _git.Hold = null;
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task Push_ABranchWithoutAnUpstream_GoesToOriginAndGetsOne()
    {
        using (var repo = new Repository(_repo))
        {
            Commands.Checkout(repo, repo.Branches["frost"]);
        }

        var w = await Open();
        try
        {
            var push = await Key(w, Avalonia.Input.Key.P, null);
            Assert.Equal(["push", "--progress", "--set-upstream", "origin", "HEAD"], _git.Runs[^1].Arguments);
            Assert.Equal("frost → origin, new there", push.Route);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task APullThatStopsOnAConflict_SaysSo_AndARefusal_SaysGitsWords()
    {
        var w = await Open();
        try
        {
            _git.Answer = new GitRun(1, "Auto-merging Planner.cs\nCONFLICT (content): Merge conflict in Planner.cs\nAutomatic merge failed; fix conflicts and then commit the result.\n", "");
            var pull = await Key(w, Avalonia.Input.Key.L, null);
            Assert.Equal(JobState.Failed, pull.State);
            Assert.Equal("Pull garden-planner stopped on a conflict: the conflicted files are in Changes. Edit them, stage them, then commit.", w.NoticeMessage);

            _git.Answer = new GitRun(128, "", "remote: Invalid username or token.\nfatal: Authentication failed for 'https://example.invalid/garden.git/'\n");
            var push = await Key(w, Avalonia.Input.Key.P, pull);
            Assert.Equal(JobState.Failed, push.State);
            Assert.Equal("Push garden-planner didn't work: fatal: Authentication failed for 'https://example.invalid/garden.git/'", w.NoticeMessage);
            Assert.Contains("Authentication failed", push.Error!.Message, StringComparison.Ordinal);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task ARepositoryWithoutARemote_HasFetchPullAndPushGreyed_AndSaysWhy()
    {
        using (var repo = new Repository(_repo))
        {
            repo.Network.Remotes.Remove("origin");
        }

        var w = await Open();
        try
        {
            await w.ShowBranchMenuAsync(w.ActivePanel);
            Assert.False(w.GitMenu!.Items.OfType<MenuItem>().Single(i => (string?)i.Tag == "Git.Pull").IsEnabled);
            w.GitMenu.Close();
            w.Pull(w.ActivePanel);
            Assert.Equal("garden-planner has no remote to fetch from or push to.", w.NoticeMessage);
            Assert.Empty(_git.Runs);
            Assert.Null(w.LastGitJob);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task Commit_PushAfterwards_StartsAPushJob()
    {
        File.WriteAllText(Path.Join(_repo, "Planner.cs"), "plan\nmore\n");
        using (var repo = new Repository(_repo))
        {
            Commands.Stage(repo, "Planner.cs");
        }

        var w = await Open();
        try
        {
            var committing = w.CommitAsync(w.ActivePanel);
            await Until(() => w.CommitShown is not null);
            var dialog = w.CommitShown!;
            Assert.True(dialog.PushBox.IsVisible);
            Assert.Equal("Push afterwards", dialog.PushBox.Content);
            dialog.PushBox.IsChecked = true;
            dialog.MessageBox.Text = "More plans";
            await dialog.AcceptAsync();
            await committing;
            await Until(() => w.LastGitJob is { IsFinished: true });
            Assert.Equal(["commit", "-m", "More plans"], _git.Runs[^2].Arguments);
            Assert.Equal(["push", "--progress"], _git.Runs[^1].Arguments);
            Assert.Equal("Push garden-planner", w.LastGitJob!.Title);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task CtrlShiftG_OutsideARepository_Clones_IntoANewFolder_AndOpensIt()
    {
        File.WriteAllText(Path.Join(_dir, "notes.txt"), "a file outside any repository");
        Directory.CreateDirectory(Path.Join(_dir, "full"));
        File.WriteAllText(Path.Join(_dir, "full", "kept.txt"), "kept");
        _git.OnRun = (folder, args) =>
        {
            if (args.Contains("clone"))
            {
                Repository.Init(args[^1]);
            }
        };
        var w = await Open(_dir);
        try
        {
            var opening = w.ShowBranchMenuAsync(w.ActivePanel);
            await Until(() => w.CloneShown is not null);
            var dialog = w.CloneShown!;
            Assert.Equal("Clone a repository", dialog.Title);
            Assert.Equal(_dir, dialog.FolderBox.Text);
            Assert.True(dialog.OpenBox.IsChecked);

            dialog.AddressBox.Text = "https://sam:secret@example.invalid/sam/compost-tools.git";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Path.Join(_dir, "compost-tools"), dialog.FolderBox.Text);
            dialog.Accept();
            Assert.Equal("This address has a password in it. Leave it out: Git's credential manager asks for it.", dialog.ErrorText.Text);
            dialog.AddressBox.Text = "ext::sh -c calc";
            Dispatcher.UIThread.RunJobs();
            dialog.Accept();
            Assert.StartsWith("Only addresses starting with https://", dialog.ErrorText.Text, StringComparison.Ordinal);

            dialog.AddressBox.Text = "https://example.invalid/sam/compost-tools.git";
            dialog.FolderBox.Text = Path.Join(_dir, "full");
            Dispatcher.UIThread.RunJobs();
            dialog.Accept();
            Assert.Equal("full is already there and isn't empty.", dialog.ErrorText.Text);
            Assert.True(dialog.IsVisible);

            dialog.FolderBox.Text = Path.Join(_dir, "compost-tools");
            Dispatcher.UIThread.RunJobs();
            dialog.Accept();
            await opening;
            await Until(() => w.LastGitJob is { IsFinished: true });
            var job = w.LastGitJob!;
            Assert.Equal("Clone compost-tools", job.Title);
            Assert.Equal(JobState.Done, job.State);
            Assert.Equal(_dir, _git.Runs[^1].Top);
            Assert.Equal(["-c", "protocol.ext.allow=never", "clone", "--progress", "--", "https://example.invalid/sam/compost-tools.git", Path.Join(_dir, "compost-tools")], _git.Runs[^1].Arguments);
            await Until(() => w.ActivePanel.Panel.Path == Path.Join(_dir, "compost-tools"));
            Assert.Equal($"Cloned compost-tools into {Path.Join(_dir, "compost-tools")}.", w.NoticeMessage);
        }
        finally
        {
            w.Close();
        }
    }
}
