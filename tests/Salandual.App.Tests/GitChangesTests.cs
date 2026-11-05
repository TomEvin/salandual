// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/GitChangesTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LibGit2Sharp;
using Salandual.App.Panels;
using Salandual.Core.Commands;
using Salandual.Core.Git;

namespace Salandual.App.Tests;

public sealed class GitChangesTests : IDisposable
{
    private static readonly Signature _ann = new("Ann Example", "ann@example.invalid", new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-gitchanges-").FullName;
    private readonly string _repo;
    private readonly MemoryGitCommands _git = new();

    public GitChangesTests()
    {
        _repo = Path.Join(_dir, "garden-planner");
        Repository.Init(_repo);
        using var repo = new Repository(_repo);
        repo.Refs.UpdateTarget("HEAD", "refs/heads/main");
        repo.Config.Set("user.name", "Ann Example", ConfigurationLevel.Local);
        repo.Config.Set("user.email", "ann@example.invalid", ConfigurationLevel.Local);
        Write(@"src\Planner.cs", "plan\nbeds\n");
        Write(@"src\Watering.cs", "water\n");
        Write(@"docs\old-notes.md", "old\nnotes\n");
        Write("Program.cs", "program\n");
        Commands.Stage(repo, "*");
        repo.Commit("Plant the first beds", _ann, _ann);

        Write(@"src\Planner.cs", "plan\nbeds in use\nfrost\n");
        Write(@"src\Watering.cs", "water\nmore\n");
        Commands.Stage(repo, @"src/Watering.cs");
        File.Delete(Path.Join(_repo, @"docs\old-notes.md"));
        Write(@"src\Frost.cs", "frost\ndates\nwarnings\n");
        _git.OnRun = Simulate;
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

    private void Simulate(string top, IReadOnlyList<string> args)
    {
        using var repo = new Repository(top);
        var paths = args.SkipWhile(a => a != "--").Skip(1).ToList();
        if (args.Contains("add"))
        {
            Commands.Stage(repo, paths.Count > 0 ? paths : ["*"]);
        }
        else if (args.Contains("restore") && args.Contains("--source=HEAD"))
        {
            foreach (var path in paths)
            {
                var blob = (Blob)repo.Head.Tip[path].Target;
                File.WriteAllText(Path.Join(top, path), blob.GetContentText());
            }

            Commands.Unstage(repo, paths);
        }
        else if (args.Contains("restore"))
        {
            Commands.Unstage(repo, paths);
        }
        else if (args.Contains("commit"))
        {
            repo.Commit(args[args.ToList().IndexOf("-m") + 1], _ann, _ann, new CommitOptions { AmendPreviousCommit = args.Contains("--amend") });
        }
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

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1200, Height = 700 };
        w.Services.GitCommands = _git;
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(_repo);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        await Until(() => w.ActivePanel.DirectoryLine.Git is not null);
        return w;
    }

    private static async Task<MainWindow> InChanges(MainWindow w)
    {
        await w.ShowChangesAsync(w.ActivePanel);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.ActivePanel.Panel.IsChanges);
        return w;
    }

    private static string[] Names(MainWindow w) => [.. w.ActivePanel.Panel.Items.Select(e => e.Name)];

    private static int Row(MainWindow w, string name) => Array.IndexOf(Names(w), name);

    private static void Focus(MainWindow w, string name)
    {
        w.ActivePanel.Panel.SetFocus(Row(w, name));
        Dispatcher.UIThread.RunJobs();
    }

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

    [AvaloniaFact]
    public async Task CtrlShiftG_ThenC_ShowsTheChangesInTheirGroups_WithTheirLines()
    {
        var w = await Open();
        try
        {
            await w.ShowBranchMenuAsync(w.ActivePanel);
            var menu = w.GitMenu!;
            menu.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = Avalonia.Input.KeyModifiers.None });
            await Until(() => w.ActivePanel.Panel.IsChanges);

            var view = w.ActivePanel.View;
            Assert.Equal(["Staged · 1", @"src\Watering.cs", "Changed · 2", @"docs\old-notes.md", @"src\Planner.cs", "New · 1", @"src\Frost.cs"], Names(w));
            Assert.True(w.ActivePanel.Panel.IsHeading(0));
            Assert.Equal(@"src\Watering.cs", w.ActivePanel.Panel.FocusedEntry!.Value.Name);
            Assert.Equal(@"src\Planner.cs", view.RowName(Row(w, @"src\Planner.cs")) + ".cs");
            Assert.Equal("+1", view.FolderColumnText(Row(w, @"src\Watering.cs")));
            Assert.Equal("+2 −1", view.FolderColumnText(Row(w, @"src\Planner.cs")));
            Assert.Equal("−2", view.FolderColumnText(Row(w, @"docs\old-notes.md")));
            Assert.Equal("+3", view.FolderColumnText(Row(w, @"src\Frost.cs")));
            Assert.Equal("", view.ColumnText(Row(w, @"docs\old-notes.md"), Salandual.Core.Panels.PanelColumn.Size));
            Assert.Equal("D", view.ColumnText(Row(w, @"docs\old-notes.md"), Salandual.Core.Panels.PanelColumn.Git));
            Assert.Equal(GitState.New, view.GitStateOf(Row(w, @"src\Frost.cs")));
            Assert.NotNull(w.ActivePanel.DirectoryLine.Git);
            Capture(w, "git-changes.png");

            Focus(w, "Changed · 2");
            await w.ActivePanel.Panel.OpenFocusedAsync();
            Assert.True(w.ActivePanel.Panel.IsChanges);
            await w.ActivePanel.Panel.LeaveResultsAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(w.ActivePanel.Panel.IsChanges);
            Assert.Null(w.ActivePanel.View.FolderColumnText(0));
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task Stage_AndUnstage_TheSelectedOrFocusedFiles_ThenTheGroupsFollow()
    {
        var w = await InChanges(await Open());
        try
        {
            Focus(w, @"src\Planner.cs");
            await w.StageAsync(w.ActivePanel);
            Assert.Equal(["--literal-pathspecs", "add", "-A", "--", "src/Planner.cs"], _git.Runs[^1].Arguments);
            Assert.Equal(_repo, _git.Runs[^1].Top);
            await Until(() => Row(w, "Staged · 2") == 0);
            Assert.Equal(@"src\Planner.cs", w.ActivePanel.Panel.FocusedEntry!.Value.Name);
            Assert.Equal("1 file staged.", w.NoticeMessage);

            w.ActivePanel.Panel.Toggle(Row(w, @"src\Watering.cs"));
            w.ActivePanel.Panel.Toggle(Row(w, @"src\Frost.cs"));
            await w.UnstageAsync(w.ActivePanel);
            Assert.Equal(["--literal-pathspecs", "restore", "--staged", "--", "src/Watering.cs"], _git.Runs[^1].Arguments);

            _git.Answer = new GitRun(128, "", "fatal: Unable to create 'index.lock': File exists.");
            Focus(w, @"src\Frost.cs");
            var staging = w.StageAsync(w.ActivePanel);
            await Until(() => w.GitMessageShown is not null);
            Assert.Contains("index.lock", w.GitMessageShown!.Message, StringComparison.Ordinal);
            w.GitMessageShown.Close(false);
            await staging;
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task Stage_InAnOrdinaryFolderOfTheRepository_TakesAFolderWithWhatIsInIt()
    {
        var w = await Open();
        try
        {
            w.ActivePanel.Panel.SetFocus(Row(w, "src"));
            Assert.True(w.Run("Git.Stage", PanelTarget.Active, new KeyChord(Salandual.Core.Commands.KeyModifiers.Ctrl | Salandual.Core.Commands.KeyModifiers.Shift, "S")));
            await Until(() => _git.Runs.Count == 1);
            await w.GitWork;
            Assert.Equal(["--literal-pathspecs", "add", "-A", "--", "src/Frost.cs", "src/Planner.cs"], _git.Runs[0].Arguments.Take(4).Concat(_git.Runs[0].Arguments.Skip(4).Order(StringComparer.Ordinal)));
            Assert.Equal("Ctrl+Shift+S", string.Join(" ", w.Services.Commands.ChordsFor("Git.Stage")));
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task Discard_AlwaysAsks_ThenTheFileGoesToTheRecycleBin_AndGitPutsTheLastCommitsBack()
    {
        var w = await InChanges(await Open());
        try
        {
            Focus(w, @"src\Planner.cs");
            var discarding = w.DiscardAsync(w.ActivePanel);
            await Until(() => w.DiscardQuestion is not null);
            Assert.Equal("Discard the changes of Planner.cs?", w.DiscardQuestion!.Title);
            Assert.Contains("Recycle Bin first", w.DiscardQuestion.Message, StringComparison.Ordinal);
            w.DiscardQuestion.Close(false);
            await discarding;
            Assert.Empty(_git.Runs);
            Assert.Empty(w.Services.RecycledInTests);

            discarding = w.DiscardAsync(w.ActivePanel);
            await Until(() => w.DiscardQuestion is not null);
            w.DiscardQuestion!.Close(true);
            await discarding;
            Assert.Equal([Path.Join(_repo, @"src\Planner.cs")], w.Services.RecycledInTests);
            Assert.Equal(["--literal-pathspecs", "restore", "--source=HEAD", "--staged", "--worktree", "--", "src/Planner.cs"], _git.Runs[^1].Arguments);
            Assert.Equal("plan\nbeds\n", File.ReadAllText(Path.Join(_repo, @"src\Planner.cs")).ReplaceLineEndings("\n"));
            await Until(() => Row(w, @"src\Planner.cs") < 0);

            var runs = _git.Runs.Count;
            Focus(w, @"src\Frost.cs");
            discarding = w.DiscardAsync(w.ActivePanel);
            await Until(() => w.DiscardQuestion is not null);
            w.DiscardQuestion!.Close(true);
            await discarding;
            Assert.False(File.Exists(Path.Join(_repo, @"src\Frost.cs")));
            Assert.Equal(runs, _git.Runs.Count);
            Assert.Equal("The changes of 1 file were discarded; the file as it was is in the Recycle Bin.", w.NoticeMessage);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task F3_ComparesWithTheLastCommit_ReadOnlyOnTheLeft_AndStagesThisFile()
    {
        var w = await InChanges(await Open());
        try
        {
            Focus(w, @"src\Planner.cs");
            Assert.True(w.Run("File.View", PanelTarget.Active));
            await Until(() => w.FileCompare is not null);
            var compare = w.FileCompare!;
            await compare.Comparing;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Planner.cs: the last commit ↔ your copy", compare.Title);
            Assert.StartsWith(w.Services.Temp.Root, compare.LeftPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.GetAttributes(compare.LeftPath).HasFlag(FileAttributes.ReadOnly));
            Assert.Equal(Path.Join(_repo, @"src\Planner.cs"), compare.RightPath);
            Assert.Equal(["plan", "beds"], compare.LinesOf(0));
            Assert.Contains("stage this file", compare.HintText, StringComparison.Ordinal);

            compare.CopyAll(toRight: false);
            Assert.Equal(["plan", "beds"], compare.LinesOf(0));
            Assert.Equal("This side is Git's, read-only: nothing is copied into it.", compare.HintText);
            Capture(compare, "git-diff.png");

            compare.KeyPress(Key.S, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
            compare.KeyRelease(Key.S, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
            await Until(() => _git.Runs.Count == 1);
            await w.GitWork;
            Assert.Equal(["--literal-pathspecs", "add", "-A", "--", "src/Planner.cs"], _git.Runs[0].Arguments);
            compare.Close();

            Focus(w, @"src\Watering.cs");
            w.Run("File.View", PanelTarget.Active);
            await Until(() => w.FileCompare is not null && w.FileCompare != compare);
            var staged = w.FileCompare!;
            await staged.Comparing;
            Assert.Equal("Watering.cs: the last commit ↔ staged", staged.Title);
            Assert.Equal(["water", "more"], staged.LinesOf(1));
            staged.Close();
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task Commit_TakesWhatIsStaged_SaysWhoCommits_AndWithNothingStagedTakesAll()
    {
        var w = await InChanges(await Open());
        try
        {
            var committing = w.CommitAsync(w.ActivePanel);
            await Until(() => w.CommitShown is not null);
            var dialog = w.CommitShown!;
            Assert.Equal("Commit to main", dialog.Title);
            Assert.Contains(@"src\Watering.cs (staged)", Text(dialog.GoesInText), StringComparison.Ordinal);
            Assert.Contains("3 files not staged stay as they are", Text(dialog.GoesInText), StringComparison.Ordinal);
            Assert.Contains("Ann Example <ann@example.invalid>", ((TextBlock)((StackPanel)dialog.AmendBox.Parent!).Children[^1]).Text, StringComparison.Ordinal);
            Assert.True(dialog.AmendBox.IsVisible);

            await dialog.AcceptAsync();
            Assert.Equal("Write a message first.", dialog.ErrorText.Text);
            dialog.MessageBox.Text = "Water the beds twice\n\nIn summer.";
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("The first line is the summary (20 of 72 letters).", dialog.SummaryText.Text, StringComparison.Ordinal);
            Capture(dialog, "git-commit.png");

            dialog.MessageBox.Text = "";
            dialog.AmendBox.IsChecked = true;
            Assert.Equal("Plant the first beds", dialog.MessageBox.Text);
            dialog.AmendBox.IsChecked = false;
            dialog.MessageBox.Text = "Water the beds twice\n\nIn summer.";

            await dialog.AcceptAsync();
            await committing;
            Assert.Equal(["commit", "-m", "Water the beds twice\n\nIn summer."], _git.Runs[^1].Arguments);
            Assert.Equal("Committed to main: Water the beds twice", w.NoticeMessage);
            await Until(() => Row(w, @"src\Watering.cs") < 0);

            committing = w.CommitAsync(w.ActivePanel);
            await Until(() => w.CommitShown is not null);
            dialog = w.CommitShown!;
            Assert.Equal("Nothing is staged, so all the changes go in: 3 files.", Text(dialog.GoesInText));
            dialog.MessageBox.Text = "Frost dates";
            await dialog.AcceptAsync();
            await committing;
            Assert.Equal(["add", "-A"], _git.Runs[^2].Arguments);
            Assert.Equal(["commit", "-m", "Frost dates"], _git.Runs[^1].Arguments);
        }
        finally
        {
            w.Close();
        }

        static string Text(TextBlock block) => string.Concat(block.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text));
    }

    [AvaloniaFact]
    public async Task Commit_WhenGitRefuses_SaysWhatItSaid_AndStaysOpen()
    {
        var w = await Open();
        try
        {
            var committing = w.CommitAsync(w.ActivePanel);
            await Until(() => w.CommitShown is not null);
            var dialog = w.CommitShown!;
            dialog.MessageBox.Text = "Plan";
            _git.OnRun = null;
            _git.Answer = new GitRun(1, "", "pre-commit hook failed");
            await dialog.AcceptAsync();
            Assert.Equal("Git said: pre-commit hook failed", dialog.ErrorText.Text);
            Assert.True(dialog.IsVisible);
            dialog.Close(false);
            await committing;
        }
        finally
        {
            w.Close();
        }
    }
}
