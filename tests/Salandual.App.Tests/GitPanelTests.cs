// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/GitPanelTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using LibGit2Sharp;
using Salandual.App.Panels;
using Salandual.Core.Git;
using Salandual.Core.Panels;

namespace Salandual.App.Tests;

public sealed class GitPanelTests : IDisposable
{
    private static readonly Signature _ann = new("Ann Example", "ann@example.invalid", new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-gitpanel-").FullName;
    private readonly string _repo;
    private readonly MemoryGitCommands _git = new();

    public GitPanelTests()
    {
        _repo = Path.Join(_dir, "garden-planner");
        Repository.Init(_repo);
        using var repo = new Repository(_repo);
        repo.Refs.UpdateTarget("HEAD", "refs/heads/main");
        Write("Planner.cs", "plan\n");
        Write("Watering.cs", "water\n");
        Write(@"Beds\Soil.cs", "soil\n");
        Write("Program.cs", "program\n");
        Write(".gitignore", "bin/\n*.log\n");
        Commands.Stage(repo, "*");
        var first = repo.Commit("First", _ann, _ann);
        repo.CreateBranch("frost-warnings", first);
        Write("Harvest.cs", "harvest\n");
        Commands.Stage(repo, "*");
        repo.Commit("Second", _ann, _ann);
        var tree = repo.ObjectDatabase.CreateTree(new TreeDefinition().Add("Theirs.cs", repo.ObjectDatabase.CreateBlob(new MemoryStream("theirs\n"u8.ToArray())), Mode.NonExecutableFile));
        var theirs = repo.ObjectDatabase.CreateCommit(_ann, _ann, "Theirs", tree, [first], prettifyMessage: false);
        repo.Network.Remotes.Add("origin", "https://example.invalid/garden.git");
        repo.Refs.Add("refs/remotes/origin/main", theirs.Id);
        repo.Refs.Add("refs/remotes/origin/compost", first.Id);
        repo.Branches.Update(repo.Head, b => b.Remote = "origin", b => b.UpstreamBranch = "refs/heads/main");

        Write("Planner.cs", "plan changed\n");
        Write("Watering.cs", "water changed\n");
        Commands.Stage(repo, "Watering.cs");
        Write("Frost.cs", "frost\n");
        Write(@"bin\app.dll", "dll");
        Write(@"Beds\Soil.cs", "soil changed\n");
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_dir, recursive: true);
    }

    private void Write(string relative, string text)
    {
        var path = Path.Join(_repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
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
        await w.ActivePanel.Panel.NavigateAsync(folder ?? _repo);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        if (folder is null)
        {
            await Until(() => w.ActivePanel.DirectoryLine.Git is not null);
        }

        return w;
    }

    private static int Row(MainWindow w, string name) => w.ActivePanel.Panel.Items.ToList().FindIndex(e => e.Name == name);

    private static Color Ink(MainWindow w, string name) => ((ISolidColorBrush)w.ActivePanel.View.NameLookOf(Row(w, name)).Brush).Color;

    private static Color RowInk(MainWindow w, string name) => ((ISolidColorBrush)w.ActivePanel.View.RowInkOf(Row(w, name))).Color;

    private static Color Token(MainWindow w, string token)
        => w.ActivePanel.View.TryFindResource("Brush." + token, w.ActivePanel.View.ActualThemeVariant, out var r) && r is ISolidColorBrush b ? b.Color : default;

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            for (var k = 0; k < 10; k++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    private static IReadOnlyList<ColumnView> WithGit() => ColumnViews.With([], ColumnViews.Toggle(ColumnViews.For([], PanelView.Detailed), PanelColumn.Git));

    [AvaloniaFact]
    public async Task InARepository_TheDirectoryLineHasTheBranch_TheNamesTheirStatesColors_AndTheGitColumnItsLetters()
    {
        var w = await Open();
        try
        {
            var line = w.ActivePanel.DirectoryLine;
            Assert.Equal(new GitFacts("main", 1, 1, "origin/main", false), line.Git);
            Assert.Equal("↑1 ↓1", DirectoryLine.AheadBehind(line.Git!));
            Assert.Contains("origin/main", line.GitTip(), StringComparison.Ordinal);
            w.CaptureRenderedFrame();
            Assert.True(line.BranchBounds.Width > 0);
            Assert.Equal(default, line.FreeSpaceBounds);

            var view = w.ActivePanel.View;
            Assert.Equal(GitState.Changed, view.GitStateOf(Row(w, "Planner.cs")));
            Assert.Equal(GitState.Staged, view.GitStateOf(Row(w, "Watering.cs")));
            Assert.Equal(GitState.New, view.GitStateOf(Row(w, "Frost.cs")));
            Assert.Equal(GitState.Ignored, view.GitStateOf(Row(w, "bin")));
            Assert.Equal(GitState.Changed, view.GitStateOf(Row(w, "Beds")));
            Assert.Equal(GitState.None, view.GitStateOf(Row(w, "Program.cs")));
            Assert.Equal(Token(w, "Warning"), Ink(w, "Planner.cs"));
            Assert.Equal(Token(w, "Success"), Ink(w, "Watering.cs"));
            Assert.Equal(Token(w, "Accent"), Ink(w, "Frost.cs"));
            Assert.Equal(Token(w, "TertiaryText"), Ink(w, "bin"));
            Assert.Equal(Token(w, "Text"), Ink(w, "Program.cs"));

            Assert.Equal(Token(w, "Warning"), RowInk(w, "Planner.cs"));
            Assert.Equal(Token(w, "Accent"), RowInk(w, "Frost.cs"));
            Assert.Equal(Token(w, "SecondaryText"), RowInk(w, "Program.cs"));

            Assert.Equal(PanelColumn.Git, view.ShownColumns().Last().Column);
            w.Services.Settings.Update(s => s with { Panels = s.Panels with { Git = s.Panels.Git with { ColumnInRepositories = false } } });
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(PanelColumn.Git, view.ShownColumns().Select(c => c.Column));
            w.Services.Settings.Update(s => s with { Panels = s.Panels with { ColumnViews = WithGit() } });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("M", view.ColumnText(Row(w, "Planner.cs"), PanelColumn.Git));
            Assert.Equal("?", view.ColumnText(Row(w, "Frost.cs"), PanelColumn.Git));
            Assert.Equal("!", view.ColumnText(Row(w, "bin"), PanelColumn.Git));
            Assert.Equal("•", view.ColumnText(Row(w, "Beds"), PanelColumn.Git));
            Assert.Equal("", view.ColumnText(Row(w, "Program.cs"), PanelColumn.Git));

            Assert.Equal(Token(w, "Warning"), view.GitTagOf(Row(w, "Planner.cs")));
            Assert.Equal(Token(w, "Accent"), view.GitTagOf(Row(w, "Frost.cs")));
            Assert.Null(view.GitTagOf(Row(w, "Program.cs")));

            w.ActivePanel.Panel.SetFocus(Row(w, "Planner.cs"));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("changed, not staged", w.ActivePanel.Info.LeftText, StringComparison.Ordinal);
            Capture(w, "git-panel.png");

            w.Services.Settings.Update(s => s with { Panels = s.Panels with { Git = s.Panels.Git with { ColorNames = false } } });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Token(w, "Text"), Ink(w, "Planner.cs"));
            Assert.Equal(Token(w, "SecondaryText"), RowInk(w, "Planner.cs"));
            Assert.Equal("M", view.ColumnText(Row(w, "Planner.cs"), PanelColumn.Git));
        }
        finally
        {
            w.Services.Settings.Update(s => s with { Panels = s.Panels with { Git = new(), ColumnViews = [] } });
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task AChange_IsSeen_WithoutListingAgain()
    {
        var w = await Open();
        try
        {
            Assert.Equal(GitState.None, w.ActivePanel.View.GitStateOf(Row(w, "Program.cs")));
            File.WriteAllText(Path.Join(_repo, "Program.cs"), "program changed\n");
            await Until(() => Row(w, "Program.cs") >= 0 && w.ActivePanel.View.GitStateOf(Row(w, "Program.cs")) == GitState.Changed);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheBranchMenu_SaysWhereYouAre_SwitchesWithGit_AndOffersTheRemotesBranches()
    {
        var w = await Open();
        try
        {
            w.ActivePanel.DirectoryLine.ClickBranch();
            await Until(() => w.GitMenu is not null);
            var items = w.GitMenu!.Items.OfType<MenuItem>().ToList();
            Assert.Equal("garden-planner · main, 1 commit to push, 1 commit to pull", items[0].Header);
            var branches = items.Where(i => i.Tag is string t && !t.StartsWith("Git.", StringComparison.Ordinal)).ToList();
            Assert.Equal(["Git.Changes", "Git.Commit"], items.Select(i => i.Tag).OfType<string>().Where(t => t is "Git.Changes" or "Git.Commit"));
            Assert.Equal(["frost-warnings", "main", "origin/compost"], branches.Select(i => (string)i.Tag!));
            Assert.True(branches.Single(i => (string)i.Tag! == "main").IsChecked);
            Assert.True(items.Single(i => (string?)i.Tag == "Git.NewBranch").IsEnabled);

            branches.Single(i => (string)i.Tag! == "frost-warnings").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => _git.Runs.Count == 1);
            Assert.Equal(_repo, _git.Runs[0].Top);
            Assert.Equal(["switch", "frost-warnings"], _git.Runs[0].Arguments);
            w.GitMenu.Close();

            await w.SwitchBranchAsync(w.ActivePanel, _repo, new GitBranch("origin/compost", true, false));
            Assert.Equal(["switch", "--track", "origin/compost"], _git.Runs[1].Arguments);

            _git.Answer = new GitRun(1, "", "error: Your local changes to the following files would be overwritten by checkout:\n\tPlanner.cs");
            var switching = w.SwitchBranchAsync(w.ActivePanel, _repo, new GitBranch("frost-warnings", false, false));
            await Until(() => w.GitMessageShown is not null);
            Assert.Contains("would be overwritten", w.GitMessageShown!.Message, StringComparison.Ordinal);
            Assert.False(w.GitMessageShown.Yes.IsVisible);
            w.GitMessageShown.Close(false);
            await switching;
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task NewBranch_ChecksTheName_ThenGitMakesIt()
    {
        var w = await Open();
        try
        {
            var making = w.NewBranchAsync(w.ActivePanel);
            await Until(() => w.NewBranchShown is not null);
            var dialog = w.NewBranchShown!;
            Assert.Equal("New branch in garden-planner", dialog.Title);
            Assert.True(dialog.SwitchBox.IsChecked);

            dialog.NameBox.Text = "frost warnings";
            Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.ErrorText.IsVisible);
            Capture(dialog, "git-newbranch.png");
            dialog.NameBox.Text = "Frost-Warnings";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("There's already a branch called Frost-Warnings.", dialog.ErrorText.Text);
            await dialog.AcceptAsync();
            Assert.Empty(_git.Runs);

            dialog.NameBox.Text = "seeds";
            _git.Answer = new GitRun(128, "", "fatal: cannot lock ref 'refs/heads/seeds'");
            await dialog.AcceptAsync();
            Assert.Equal("Git said: fatal: cannot lock ref 'refs/heads/seeds'", dialog.ErrorText.Text);
            Assert.True(dialog.IsVisible);

            _git.Answer = new GitRun(0, "", "Switched to a new branch 'seeds'");
            await dialog.AcceptAsync();
            await making;
            Assert.Equal(["switch", "-c", "seeds"], _git.Runs[^1].Arguments);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task WithoutGitInstalled_SwitchingAndNewBranchAreGreyed_SayingWhy()
    {
        _git.Program = null;
        var w = await Open();
        try
        {
            await w.ShowBranchMenuAsync(w.ActivePanel);
            var items = w.GitMenu!.Items.OfType<MenuItem>().ToList();
            Assert.False(items.Single(i => (string?)i.Tag == "frost-warnings").IsEnabled);
            Assert.False(items.Single(i => (string?)i.Tag == "Git.NewBranch").IsEnabled);
            Assert.Contains(items, i => (i.Header as string)?.Contains("isn't installed", StringComparison.Ordinal) == true);
            w.GitMenu.Close();
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task OutsideARepository_NothingOfGit()
    {
        var plain = Path.Join(_dir, "plain");
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Join(plain, "a.txt"), "a");
        var w = await Open(plain);
        try
        {
            Assert.Null(w.ActivePanel.DirectoryLine.Git);
            Assert.Equal(GitState.None, w.ActivePanel.View.GitStateOf(Row(w, "a.txt")));
            Assert.DoesNotContain(PanelColumn.Git, w.ActivePanel.View.ShownColumns().Select(c => c.Column));
            var opening = w.ShowBranchMenuAsync(w.ActivePanel);
            await Until(() => w.CloneShown is not null);
            w.CloneShown!.Close(null);
            await opening;
            Assert.Null(w.GitMenu);
            Assert.Null(w.LastGitJob);
            w.Services.GitCommands = new MemoryGitCommands(program: null);
            await w.ShowBranchMenuAsync(w.ActivePanel);
            Assert.Null(w.GitMenu);
            Assert.Equal("This folder isn't inside a Git repository.", w.NoticeMessage);

            await w.ActivePanel.Panel.NavigateAsync(Path.Join(_repo, ".git"));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(w.ActivePanel.DirectoryLine.Git);
        }
        finally
        {
            w.Close();
        }
    }
}
