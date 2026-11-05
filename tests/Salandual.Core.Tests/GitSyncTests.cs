// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/GitSyncTests.cs

using Salandual.Core.Git;
using Salandual.Core.Operations;

namespace Salandual.Core.Tests;

public sealed class GitSyncTests
{
    [Theory]
    [InlineData("https://example.com/sam/compost-tools.git")]
    [InlineData("http://example.com/sam/compost-tools")]
    [InlineData("ssh://git@example.com/sam/compost-tools.git")]
    [InlineData("git://example.com/sam/compost-tools.git")]
    [InlineData("git@example.com:sam/compost-tools.git")]
    [InlineData("https://sam@example.com/sam/compost-tools.git")]
    [InlineData("  https://example.com/x.git  ")]
    public void CheckAddress_TakesTheUsualAddresses(string address) => Assert.Null(GitSync.CheckAddress(address));

    [Theory]
    [InlineData("", GitAddressProblem.Empty)]
    [InlineData("   ", GitAddressProblem.Empty)]
    [InlineData("https://sam:secret@example.com/x.git", GitAddressProblem.Password)]
    [InlineData("https://sam:ghp_token@example.com/x.git", GitAddressProblem.Password)]
    [InlineData("file:///C:/Projects/x", GitAddressProblem.NotSupported)]
    [InlineData("ext::sh -c touch% /tmp/x", GitAddressProblem.NotSupported)]
    [InlineData("--upload-pack=calc.exe", GitAddressProblem.NotSupported)]
    [InlineData("-u", GitAddressProblem.NotSupported)]
    [InlineData(@"C:\Projects\x", GitAddressProblem.NotSupported)]
    [InlineData("ftp://example.com/x.git", GitAddressProblem.NotSupported)]
    [InlineData("https://example.com/a b.git", GitAddressProblem.NotSupported)]
    [InlineData("example.com/x.git", GitAddressProblem.NotSupported)]
    public void CheckAddress_RefusesPasswordsAndOtherKinds(string address, GitAddressProblem problem) => Assert.Equal(problem, GitSync.CheckAddress(address));

    [Theory]
    [InlineData("https://example.com/sam/compost-tools.git", "compost-tools")]
    [InlineData("https://example.com/sam/compost-tools/", "compost-tools")]
    [InlineData("git@example.com:sam/compost-tools.git", "compost-tools")]
    [InlineData("https://example.com/sam/compost-tools/.git", "compost-tools")]
    [InlineData("https://example.com/", "example.com")]
    [InlineData("", null)]
    public void FolderNameOf_IsWhatGitWouldCallIt(string address, string? name) => Assert.Equal(name, GitSync.FolderNameOf(address));

    [Fact]
    public void CommandLines_AsAList_TheAddressAfterTheDashes_NoExtTransport()
    {
        Assert.Equal(["fetch", "--progress"], GitSync.Fetch());
        Assert.Equal(["pull", "--progress"], GitSync.Pull());
        Assert.Equal(["push", "--progress"], GitSync.Push(hasUpstream: true, "origin"));
        Assert.Equal(["push", "--progress", "--set-upstream", "origin", "HEAD"], GitSync.Push(hasUpstream: false, "origin"));
        Assert.Equal(["push", "--progress"], GitSync.Push(hasUpstream: false, null));
        Assert.Equal(["-c", "protocol.ext.allow=never", "clone", "--progress", "--", "https://example.com/x.git", @"D:\Projects\x"],
            GitSync.Clone(" https://example.com/x.git ", @"D:\Projects\x"));
        Assert.Equal("origin", GitSync.PushRemote(["upstream", "origin"]));
        Assert.Equal("fork", GitSync.PushRemote(["fork"]));
        Assert.Null(GitSync.PushRemote(["fork", "upstream"]));
        Assert.Null(GitSync.PushRemote([]));
    }

    [Fact]
    public void Progress_IsReadFromGitsLines_AndLeftOutOfItsWords()
    {
        Assert.Equal(new GitProgress("Receiving objects:  64% (210/328), 1.20 MiB | 2.10 MiB/s", 64), GitSync.ParseProgress("Receiving objects:  64% (210/328), 1.20 MiB | 2.10 MiB/s"));
        Assert.Equal(new GitProgress("Counting objects: 100% (5/5)", 100), GitSync.ParseProgress("remote: Counting objects: 100% (5/5), done."));
        Assert.Null(GitSync.ParseProgress("Already up to date."));
        Assert.Null(GitSync.ParseProgress("To https://example.com/x.git"));

        var errors = "remote: Counting objects:  50% (1/2)\rremote: Counting objects: 100% (2/2), done.\nTo https://example.com/x.git\n   a41f2c9..7c03e1b  main -> main\n";
        Assert.Equal("To https://example.com/x.git\na41f2c9..7c03e1b  main -> main", GitSync.WithoutProgress(errors));
        Assert.Equal("a41f2c9..7c03e1b  main -> main", GitSync.LastWords(new GitRun(0, "", errors)));
        Assert.Equal("Already up to date.", GitSync.LastWords(new GitRun(0, "Already up to date.\n", "")));
        Assert.Equal("fatal: Authentication failed for 'https://example.com/x.git/'", GitSync.Gist("remote: Invalid username or token.\nfatal: Authentication failed for 'https://example.com/x.git/'\n"));
        Assert.Equal("hint: see git help", GitSync.Gist("something\nhint: see git help"));
        Assert.True(GitSync.StoppedOnConflict(new GitRun(1, "Auto-merging src/Planner.cs\nCONFLICT (content): Merge conflict in src/Planner.cs\n", "")));
        Assert.False(GitSync.StoppedOnConflict(new GitRun(0, "CONFLICT", "")));
        Assert.False(GitSync.StoppedOnConflict(new GitRun(1, "", "fatal: not a git repository")));
    }

    [Fact]
    public async Task Job_ShowsGitsProgress_EndsWithItsWords_AndStopsWithItsMessageWhenItRefuses()
    {
        var git = new MemoryGitCommands { Lines = ["Receiving objects:  64% (210/328)"], Answer = new GitRun(0, "Already up to date.\n", "") };
        var seen = new List<string?>();
        var job = GitSync.Job(GitSyncKind.Pull, "Pull garden-planner", "main ← origin/main", @"D:\garden-planner", GitSync.Pull(), git);
        job.Changed += (_, _) => seen.Add(job.Progress.Current);
        Assert.Equal(OperationKind.Git, job.Kind);
        Assert.Equal(GitSyncKind.Pull, job.GitKind);
        job.RunHere();
        await job.Completion;
        Assert.Equal(JobState.Done, job.State);
        Assert.Equal("Already up to date.", job.Outcome);
        Assert.Contains("Receiving objects:  64% (210/328)", seen);
        Assert.Equal(@"D:\garden-planner", git.Runs[0].Top);
        Assert.Equal(["pull", "--progress"], git.Runs[0].Arguments);

        git.Answer = new GitRun(128, "", "Receiving objects:  10% (1/10)\rfatal: Authentication failed\n");
        GitRun? said = null;
        var refused = GitSync.Job(GitSyncKind.Push, "Push garden-planner", "main → origin/main", @"D:\garden-planner", GitSync.Push(true, null), git, run => said = run);
        refused.RunHere();
        await refused.Completion;
        Assert.Equal(JobState.Failed, refused.State);
        Assert.Equal("fatal: Authentication failed", refused.Error!.Message);
        Assert.Equal(128, said!.ExitCode);
    }

    [Fact]
    public async Task Job_OnTheSameRepository_WaitsForTheOneBefore_AndStopStopsIt()
    {
        var hold = new TaskCompletionSource();
        var git = new MemoryGitCommands { Hold = hold.Task };
        var first = GitSync.Job(GitSyncKind.Pull, "Pull garden-planner", "", @"D:\garden-planner", GitSync.Pull(), git);
        var second = GitSync.Job(GitSyncKind.Push, "Push garden-planner", "", @"D:\garden-planner", GitSync.Push(true, null), git, before: first, waitWords: "Starts after Pull garden-planner, the same repository.");
        var queue = new OperationQueue();
        queue.Add(first, false);
        queue.Add(second, false);
        for (var i = 0; i < 200 && (second.Progress.Current is null || git.Runs.Count == 0); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal("Starts after Pull garden-planner, the same repository.", second.Progress.Current);
        Assert.Single(git.Runs);
        hold.SetResult();
        await first.Completion;
        await second.Completion;
        Assert.Equal(JobState.Done, second.State);
        Assert.Equal(2, git.Runs.Count);

        var never = new TaskCompletionSource();
        git.Hold = never.Task;
        var stopped = GitSync.Job(GitSyncKind.Fetch, "Fetch garden-planner", "", @"D:\garden-planner", GitSync.Fetch(), git);
        queue.Add(stopped, false);
        for (var i = 0; i < 200 && git.Runs.Count < 3; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        stopped.Cancel();
        await stopped.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Cancelled, stopped.State);
    }
}
