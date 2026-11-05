// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/GitHistoryTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LibGit2Sharp;
using Salandual.App.Panels;
using Salandual.Core.Commands;
using Salandual.Core.Git;
using Salandual.Core.Panels;

namespace Salandual.App.Tests;

public sealed class GitHistoryTests : IDisposable
{
    private static readonly Signature _ann = new("Ann Example", "ann@example.invalid", new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
    private static readonly Signature _sam = new("Sam Sample", "sam@example.invalid", new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.Zero));
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-githistory-").FullName;
    private readonly string _repo;
    private readonly string _first;
    private readonly string _water;
    private readonly string _merge;

    public GitHistoryTests()
    {
        _repo = Path.Join(_dir, "garden-planner");
        Repository.Init(_repo);
        using var repo = new Repository(_repo);
        repo.Refs.UpdateTarget("HEAD", "refs/heads/main");
        Write(@"src\Planner.cs", "plan\n");
        Write(@"src\Watering.cs", "water\n");
        Commands.Stage(repo, "*");
        var first = repo.Commit("Plant the first beds", _ann, _ann);
        _first = first.Sha;
        repo.ApplyTag("v0.1");

        var frost = repo.CreateBranch("frost", first);
        Commands.Checkout(repo, frost);
        Write(@"src\Frost.cs", "frost\ndates\n");
        Commands.Stage(repo, "*");
        var frostCommit = repo.Commit("Frost dates", _sam, _sam);
        Commands.Checkout(repo, repo.Branches["main"]);
        Write(@"src\Watering.cs", "water\nless in October\n");
        Commands.Stage(repo, "*");
        _water = repo.Commit("Water version 2.1 beds less", _ann, _ann).Sha;
        _merge = repo.Merge(frostCommit, _ann, new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastForward, CommitOnSuccess = true }).Commit.Sha;

        repo.Network.Remotes.Add("origin", "https://example.invalid/garden.git");
        repo.Refs.Add("refs/remotes/origin/main", first.Id);
        repo.Branches.Update(repo.Head, b => b.Remote = "origin", b => b.UpstreamBranch = "refs/heads/main");
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
        w.Services.GitCommands = new MemoryGitCommands();
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(_repo);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        await Until(() => w.ActivePanel.DirectoryLine.Git is not null);
        return w;
    }

    private static string[] Names(MainWindow w) => [.. w.ActivePanel.Panel.Items.Select(e => e.Name)];

    private static int Row(MainWindow w, string name) => Array.IndexOf(Names(w), name);

    private static string Short(string sha) => sha[..7];

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
    public async Task CtrlShiftG_ThenH_ShowsHistory_NotPushedFirst_WithAuthors()
    {
        var w = await Open();
        try
        {
            await w.ShowBranchMenuAsync(w.ActivePanel);
            w.GitMenu!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.H, KeyModifiers = Avalonia.Input.KeyModifiers.None });
            await Until(() => w.ActivePanel.Panel.IsHistory);

            var names = Names(w);
            Assert.Equal("Not pushed · 3", names[0]);
            Assert.StartsWith(Short(_merge) + " Merge", names[1], StringComparison.Ordinal);
            Assert.Contains(Short(_water) + " Water version 2.1 beds less", names);
            Assert.Equal("On origin/main", names[^2]);
            Assert.Equal(Short(_first) + " Plant the first beds", names[^1]);
            Assert.Equal(1, w.ActivePanel.Panel.Focus);

            var view = w.ActivePanel.View;
            var water = Row(w, Short(_water) + " Water version 2.1 beds less");
            Assert.Equal("Ann Example", view.FolderColumnText(water));
            Assert.Null(view.ColumnText(water, PanelColumn.Extension));
            Assert.Null(view.ColumnText(water, PanelColumn.Size));
            Assert.Equal(Short(_water) + " Water version 2.1 beds less", view.RowName(water));
            Assert.Equal(GitState.None, view.GitStateOf(water));
            w.ActivePanel.Panel.SetFocus(water);
            Dispatcher.UIThread.RunJobs();
            await Until(() => w.ActivePanel.Info.LeftText.Contains("Ann Example · parent", StringComparison.Ordinal));
            Capture(w, "git-history.png");
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task EnterOnACommit_OpensItsFiles_F3TheirDiff_AltF3TheFileAsItWas_BackspaceBackToHistory()
    {
        var w = await Open();
        try
        {
            await w.ShowHistoryAsync(w.ActivePanel, ofFocused: false);
            var water = Short(_water) + " Water version 2.1 beds less";
            w.ActivePanel.Panel.SetFocus(Row(w, water));
            await w.ActivePanel.Panel.OpenFocusedAsync();
            await Until(() => w.ActivePanel.Panel.IsCommit);
            Assert.Equal([@"src\Watering.cs", "Browse this version", "The whole tree at " + Short(_water)], Names(w));
            var view = w.ActivePanel.View;
            Assert.Equal("+1", view.FolderColumnText(0));
            Assert.Equal("M", view.ColumnText(0, PanelColumn.Git));
            Assert.Equal(GitState.Changed, view.GitStateOf(0));
            await Until(() => w.ActivePanel.Info.LeftText == @"src\Watering.cs · changed in " + Short(_water) + " · +1");

            Assert.True(w.Run("File.View", PanelTarget.Active));
            await Until(() => w.FileCompare is not null);
            var compare = w.FileCompare!;
            await compare.Comparing;
            Assert.Equal($"Watering.cs: before {Short(_water)} ↔ {Short(_water)}", compare.Title);
            Assert.Equal(["water"], compare.LinesOf(0));
            Assert.Equal(["water", "less in October"], compare.LinesOf(1));
            Assert.StartsWith(w.Services.Temp.Root, compare.RightPath, StringComparison.OrdinalIgnoreCase);
            compare.CopyAll(toRight: true);
            Assert.Equal(["water", "less in October"], compare.LinesOf(1));
            compare.Close();

            Assert.True(w.Run("File.ViewAlternate", PanelTarget.Active));
            await Until(() => w.Viewers.Count == 1);
            Assert.Equal("water\nless in October\n", File.ReadAllText(w.Viewers[0].FilePath).ReplaceLineEndings("\n"));
            Assert.True(File.GetAttributes(w.Viewers[0].FilePath).HasFlag(FileAttributes.ReadOnly));
            w.Viewers[0].Close();

            await w.ActivePanel.Panel.LeaveResultsAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.ActivePanel.Panel.IsHistory);
            Assert.Equal(water, w.ActivePanel.Panel.FocusedEntry!.Value.Name);
            await w.ActivePanel.Panel.LeaveResultsAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(w.ActivePanel.Panel.IsGitList);
            Assert.Equal(_repo, w.ActivePanel.Panel.Path);
            Assert.Null(w.ActivePanel.View.FolderColumnText(0));
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task BrowseThisVersion_TheWholeTreeAsItWas_ReadOnly_F3ViewsAFile_F5CopiesAFolderOut_BackspaceGoesUp()
    {
        var w = await Open();
        var output = Directory.CreateDirectory(Path.Join(_dir, "out")).FullName;
        try
        {
            await w.RightPanel.Panel.NavigateAsync(output);
            await w.ShowHistoryAsync(w.ActivePanel, ofFocused: false);
            var water = Short(_water) + " Water version 2.1 beds less";
            w.ActivePanel.Panel.SetFocus(Row(w, water));
            await w.ActivePanel.Panel.OpenFocusedAsync();
            await Until(() => w.ActivePanel.Panel.IsCommit);

            var row = "The whole tree at " + Short(_water);
            Assert.Equal([@"src\Watering.cs", "Browse this version", row], Names(w));
            Assert.True(w.ActivePanel.Panel.IsHeading(1));
            w.ActivePanel.Panel.SetFocus(2);
            await w.ActivePanel.Panel.OpenFocusedAsync();
            await Until(() => w.ActivePanel.Panel.IsTree);
            Assert.Equal(["src"], Names(w));
            Assert.False(w.ActivePanel.Panel.IsHeading(0));
            Assert.Equal("garden-planner at " + Short(_water), w.ActivePanel.Panel.Results!.What);

            await w.ActivePanel.Panel.OpenFocusedAsync();
            await Until(() => Names(w).Length == 2);
            Assert.Equal(["Planner.cs", "Watering.cs"], Names(w));
            Assert.Equal(@"garden-planner at " + Short(_water) + @" › src", w.ActivePanel.Panel.Results!.What);
            var view = w.ActivePanel.View;
            Assert.Equal("+1", view.FolderColumnText(1));
            Assert.Equal(GitState.Changed, view.GitStateOf(1));
            Assert.Equal("", view.FolderColumnText(0));
            w.ActivePanel.Panel.SetFocus(1);
            Dispatcher.UIThread.RunJobs();
            await Until(() => w.ActivePanel.Info.LeftText == @"src\Watering.cs · changed in " + Short(_water) + " · +1");
            Capture(w, "git-tree.png");

            Assert.True(w.Run("File.View", PanelTarget.Active));
            await Until(() => w.Viewers.Count == 1);
            Assert.Equal("water\nless in October\n", File.ReadAllText(w.Viewers[0].FilePath).ReplaceLineEndings("\n"));
            Assert.True(File.GetAttributes(w.Viewers[0].FilePath).HasFlag(FileAttributes.ReadOnly));
            w.Viewers[0].Close();

            Assert.True(w.Run("File.Delete", PanelTarget.Active));
            Assert.Equal("This is the tree as it was in " + Short(_water) + ", read-only. F3 views a file and F5 copies it out.", w.NoticeMessage);
            Assert.True(File.Exists(Path.Join(_repo, @"src\Watering.cs")));

            Assert.True(w.Run("File.Copy", PanelTarget.Right));
            Assert.StartsWith("The other panel shows the tree as it was in " + Short(_water), w.NoticeMessage, StringComparison.Ordinal);

            await w.ActivePanel.Panel.LeaveResultsAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["src"], Names(w));
            Assert.Equal("src", w.ActivePanel.Panel.FocusedEntry!.Value.Name);
            Assert.True(w.Run("File.Copy", PanelTarget.Active));
            await Until(() => w.OpenOperationDialog is Dialogs.CopyMoveDialog);
            var copy = (Dialogs.CopyMoveDialog)w.OpenOperationDialog!;
            await Until(() => copy.Preview is not null);
            copy.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            await Until(() => File.Exists(Path.Join(output, @"src\Watering.cs")) && w.Operations.Jobs.All(j => j.IsFinished));
            Assert.Equal("water\nless in October\n", File.ReadAllText(Path.Join(output, @"src\Watering.cs")).ReplaceLineEndings("\n"));
            Assert.Equal("plan\n", File.ReadAllText(Path.Join(output, @"src\Planner.cs")).ReplaceLineEndings("\n"));
            Assert.False(File.Exists(Path.Join(output, @"src\Frost.cs")));

            await w.ActivePanel.Panel.LeaveResultsAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.ActivePanel.Panel.IsCommit);
            await w.ActivePanel.Panel.LeaveResultsAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.ActivePanel.Panel.IsHistory);

            w.ActivePanel.Panel.SetFocus(Row(w, Short(_first) + " Plant the first beds"));
            await w.ShowBranchMenuAsync(w.ActivePanel);
            w.GitMenu!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.B, KeyModifiers = Avalonia.Input.KeyModifiers.None });
            await Until(() => w.ActivePanel.Panel.IsTree);
            Assert.Equal("garden-planner at " + Short(_first), w.ActivePanel.Panel.Results!.What);
        }
        finally
        {
            w.Close();
        }
    }

    [Fact]
    public void ANameWindowsCantHold_IsLeftOut()
    {
        Assert.True(MainWindow.WindowsCanHold("Watering.cs"));
        Assert.True(MainWindow.WindowsCanHold("CONFIG.md"));
        Assert.True(MainWindow.WindowsCanHold("COMA.txt"));
        Assert.False(MainWindow.WindowsCanHold("CON"));
        Assert.False(MainWindow.WindowsCanHold("nul.txt"));
        Assert.False(MainWindow.WindowsCanHold("COM1"));
        Assert.False(MainWindow.WindowsCanHold("lpt9.log"));
        Assert.False(MainWindow.WindowsCanHold("a:b"));
        Assert.False(MainWindow.WindowsCanHold("ends."));
        Assert.False(MainWindow.WindowsCanHold(".."));
    }

    [AvaloniaFact]
    public async Task HistoryOfAFile_OnlyTheCommitsThatChangedIt()
    {
        var w = await Open();
        try
        {
            await w.ActivePanel.Panel.NavigateAsync(Path.Join(_repo, "src"));
            Dispatcher.UIThread.RunJobs();
            w.ActivePanel.Panel.SetFocus(Row(w, "Watering.cs"));
            await w.ShowHistoryAsync(w.ActivePanel, ofFocused: true);
            Assert.True(w.ActivePanel.Panel.IsHistory);
            Assert.Equal(@"src\Watering.cs", w.ActivePanel.Panel.Results!.History!.Of);
            Assert.Equal(["Not pushed · 1", Short(_water) + " Water version 2.1 beds less", "On origin/main", Short(_first) + " Plant the first beds"], Names(w));
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheGraph_InAWindowOfItsOwn_LanesForTheMerge_TheCommitsFiles_AndEnterOpensItInThePanel()
    {
        var w = await Open();
        try
        {
            await w.ShowGraphAsync(w.ActivePanel);
            var graph = w.GraphShown!;
            await graph.Loading;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Graph · garden-planner", graph.Title);
            Assert.Equal(4, graph.Items.Count);
            Assert.Equal(2, graph.Items[0].Lanes);
            Assert.True(graph.Items[0].IsHead);
            Assert.Contains(graph.Items, i => i.Row.Lane == 1);
            Assert.Contains(graph.Items[^1].Commit.Refs, r => r is { Name: "v0.1", Kind: GitRefKind.Tag });
            Assert.Contains(graph.Items[0].Commit.Refs, r => r is { Name: "main", IsCurrent: true });

            graph.CommitList.SelectedIndex = graph.Items.ToList().FindIndex(i => i.Commit.Sha == _water);
            await Until(() => graph.FileList.ItemCount == 1);
            await graph.ShowingCommit;
            Assert.Contains("Water version 2.1 beds less", graph.DetailsText, StringComparison.Ordinal);
            Assert.Contains("Ann Example <ann@example.invalid>", graph.DetailsText, StringComparison.Ordinal);
            Assert.Contains("1 file changed, +1 −0", graph.DetailsText, StringComparison.Ordinal);
            Capture(graph, "git-graph.png");

            graph.BranchesBox.SelectedIndex = 1;
            await graph.Loading;
            Assert.Equal(4, graph.Items.Count);
            graph.FirstParentBox.IsChecked = true;
            await Until(() => graph.Items.Count == 3);
            graph.FirstParentBox.IsChecked = false;
            await Until(() => graph.Items.Count == 4);
            await graph.Loading;
            await Until(() => graph.FileList.ItemCount == 1);

            graph.FileList.SelectedIndex = 0;
            graph.DiffSelected();
            await Until(() => w.FileCompare is not null);
            w.FileCompare!.Close();
            graph.OpenSelected();
            await Until(() => w.ActivePanel.Panel.IsCommit);
            Assert.Equal(@"src\Watering.cs", Names(w)[0]);
            graph.Close();
        }
        finally
        {
            w.Close();
        }
    }

    private static void Press(Window w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static List<(string Text, double Left, double Right)> RowTexts(GitViews.GitGraphWindow graph, int index)
    {
        var row = (Control)graph.CommitList.ContainerFromIndex(index)!;
        return [.. row.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)).Select(t =>
        {
            var at = t.TranslatePoint(new Avalonia.Point(0, 0), row)!.Value;
            return (t.Text!, at.X, at.X + t.Bounds.Width);
        })];
    }

    [AvaloniaFact]
    public async Task TheGraph_TheAuthorInAColumnOfItsOwn_TheSummaryCutBeforeIt_AndGoToATag()
    {
        var w = await Open();
        try
        {
            await w.ShowGraphAsync(w.ActivePanel);
            var graph = w.GraphShown!;
            await graph.Loading;
            graph.Width = 700;
            for (var k = 0; k < 5; k++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            var water = graph.Items.ToList().FindIndex(i => i.Commit.Sha == _water);
            graph.CommitList.ScrollIntoView(water);
            Dispatcher.UIThread.RunJobs();
            var texts = RowTexts(graph, water);
            var summary = texts.Single(t => t.Text == "Water version 2.1 beds less");
            var author = texts.Single(t => t.Text == "Ann Example");
            var date = texts.Last();
            Assert.True(summary.Right <= author.Left, $"the summary ({summary.Right}) runs into the author ({author.Left})");
            Assert.True(author.Right <= date.Left, $"the author ({author.Right}) runs into the date ({date.Left})");

            var (authorWidth, dateWidth) = graph.ColumnWidthsShown;
            Assert.InRange(authorWidth, 50, 120);
            Assert.True(dateWidth > 0);
            for (var i = 0; i < graph.Items.Count; i++)
            {
                graph.CommitList.ScrollIntoView(i);
                Dispatcher.UIThread.RunJobs();
                var other = RowTexts(graph, i);
                Assert.Equal(author.Left, other.Single(t => t.Text is "Ann Example" or "Sam Sample").Left, 1);
                Assert.Equal(date.Left, other.Last().Left, 1);
            }

            graph.BranchesBox.SelectedIndex = 2;
            await Until(() => graph.TagMenu is not null);
            Assert.Equal(0, graph.BranchesBox.SelectedIndex);
            var tag = graph.TagMenu!.Items.OfType<MenuItem>().Single();
            Assert.StartsWith("v0.1", (string)tag.Header!, StringComparison.Ordinal);
            tag.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => (graph.CommitList.SelectedItem as GitViews.GraphItem)?.Commit.Sha == _first);
            Capture(graph, "git-graph-author.png");
            graph.Close();
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task CtrlShiftG_ThenA_ListsTheTags_EnterOpensTheCommit_F3BrowsesTheTree_BackspaceComesBack_NothingWritten()
    {
        using (var repo = new Repository(_repo))
        {
            repo.ApplyTag("v0.2", _water, _sam, "Water less in October\n\nThe second release.");
        }

        var w = await Open();
        try
        {
            await w.ShowBranchMenuAsync(w.ActivePanel);
            w.GitMenu!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = Avalonia.Input.KeyModifiers.None });
            await Until(() => w.ActivePanel.Panel.IsTags);
            Assert.Equal(["v0.2", "v0.1"], Names(w));
            await Until(() => w.ActivePanel.ResultsText == "garden-planner › Tags · 2 tags · Enter opens the commit · F3 browses the files as they were");
            var view = w.ActivePanel.View;
            Assert.Equal("Water less in October", view.FolderColumnText(0));
            Assert.Equal("Plant the first beds", view.FolderColumnText(1));
            Assert.Equal("Sam Sample", view.ColumnText(0, PanelColumn.Size));
            Assert.Null(view.ColumnText(0, PanelColumn.Extension));
            Assert.Equal("v0.2", view.RowName(0));
            await Until(() => w.ActivePanel.Info.LeftText.Contains("v0.2 · on " + Short(_water) + " · Sam Sample", StringComparison.Ordinal));
            Capture(w, "git-tags.png");

            Assert.True(w.Run("File.Delete", PanelTarget.Active));
            Assert.True(w.Run("File.Move", PanelTarget.Active));
            Assert.True(w.ActivePanel.Panel.IsTags);

            await w.ActivePanel.Panel.OpenFocusedAsync();
            await Until(() => w.ActivePanel.Panel.IsCommit);
            Assert.Equal(@"src\Watering.cs", Names(w)[0]);
            await w.ActivePanel.Panel.LeaveResultsAsync();
            await Until(() => w.ActivePanel.Panel.IsTags);
            Assert.Equal("v0.2", w.ActivePanel.Panel.FocusedEntry!.Value.Name);

            w.ActivePanel.Panel.SetFocus(1);
            Assert.True(w.Run("File.View", PanelTarget.Active));
            await Until(() => w.ActivePanel.Panel.IsTree);
            Assert.Equal(["src"], Names(w));
            await w.ActivePanel.Panel.LeaveResultsAsync();
            await Until(() => w.ActivePanel.Panel.IsTags);
            Assert.Equal("v0.1", w.ActivePanel.Panel.FocusedEntry!.Value.Name);
            await w.ActivePanel.Panel.LeaveResultsAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(w.ActivePanel.Panel.IsGitList);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task CtrlShiftG_ThenW_WhoChangedEachLine_EnterOpensTheCommit_AltLeftTheFileBeforeIt()
    {
        Write(@"src\Watering.cs", "water\nless in October\nnot yet committed\n");
        var w = await Open();
        try
        {
            await w.ActivePanel.Panel.NavigateAsync(Path.Join(_repo, "src"));
            w.ActivePanel.Panel.SetFocus(Row(w, "Watering.cs"));
            await w.ShowBranchMenuAsync(w.ActivePanel);
            w.GitMenu!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.W, KeyModifiers = Avalonia.Input.KeyModifiers.None });
            await Until(() => w.BlameShown is not null);
            var viewer = w.BlameShown!;
            await Until(() => viewer.BlameShown && viewer.CaretRun is not null);
            Assert.Equal(_first, viewer.CaretRun!.Commit!.Sha);
            Capture(viewer, "git-blame.png");

            Press(viewer, Key.Down);
            Assert.Equal(_water, viewer.CaretRun!.Commit!.Sha);
            Press(viewer, Key.Down);
            Assert.Null(viewer.CaretRun!.Commit);
            Press(viewer, Key.Up);

            Press(viewer, Key.G, RawInputModifiers.Control | RawInputModifiers.Shift);
            Press(viewer, Key.W);
            Assert.False(viewer.BlameShown);
            Press(viewer, Key.G, RawInputModifiers.Control | RawInputModifiers.Shift);
            Press(viewer, Key.W);
            Assert.True(viewer.BlameShown);

            var before = w.Viewers.Count;
            Press(viewer, Key.Left, RawInputModifiers.Alt);
            await Until(() => w.Viewers.Count == before + 1);
            var older = w.BlameShown!;
            Assert.NotSame(viewer, older);
            Assert.Equal("water\n", File.ReadAllText(older.FilePath).ReplaceLineEndings("\n"));
            await Until(() => older.CaretRun is not null);
            Assert.Equal(_first, older.CaretRun!.Commit!.Sha);
            older.Close();

            viewer.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(_water, viewer.CaretRun!.Commit!.Sha);

            Press(viewer, Key.Enter);
            await Until(() => w.ActivePanel.Panel.IsCommit);
            Assert.Equal(@"src\Watering.cs", Names(w)[0]);
            viewer.Close();
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task ASubmodule_CarriesS_SaysWhatItHolds_AndIsAGreyedRowInBrowseThisVersion()
    {
        var seeds = Path.Join(_repo, "vendor", "seeds");
        Directory.CreateDirectory(seeds);
        Repository.Init(seeds);
        string recorded;
        using (var sub = new Repository(seeds))
        {
            sub.Refs.UpdateTarget("HEAD", "refs/heads/seeds-main");
            File.WriteAllText(Path.Join(seeds, "seeds.csv"), "carrot\n");
            Commands.Stage(sub, "*");
            recorded = sub.Commit("Seeds", _ann, _ann).Sha;
        }

        Directory.CreateDirectory(Path.Join(_repo, "vendor", "weather"));
        using (var repo = new Repository(_repo))
        {
            const string modules = "[submodule \"vendor/seeds\"]\n\tpath = vendor/seeds\n\turl = https://example.invalid/seeds.git\n"
                + "[submodule \"vendor/weather\"]\n\tpath = vendor/weather\n\turl = https://example.invalid/weather.git\n";
            Write(".gitmodules", modules);
            var definition = TreeDefinition.From(repo.Head.Tip)
                .Add(".gitmodules", repo.ObjectDatabase.CreateBlob(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(modules))), Mode.NonExecutableFile)
                .AddGitLink("vendor/seeds", new ObjectId(recorded))
                .AddGitLink("vendor/weather", new ObjectId(recorded));
            var commit = repo.ObjectDatabase.CreateCommit(_ann, _ann, "Submodules", repo.ObjectDatabase.CreateTree(definition), [repo.Head.Tip], prettifyMessage: false);
            repo.Refs.UpdateTarget("refs/heads/main", commit.Id.Sha);
            repo.Reset(ResetMode.Mixed);
        }

        File.WriteAllText(Path.Join(seeds, "seeds.csv"), "carrot\nleek\n");
        var w = await Open();
        try
        {
            await w.ActivePanel.Panel.NavigateAsync(Path.Join(_repo, "vendor"));
            await Until(() => Row(w, "seeds") >= 0 && w.ActivePanel.View.GitLetterAt(Row(w, "seeds")) == "S");
            var view = w.ActivePanel.View;
            Assert.Equal("S", view.GitLetterAt(Row(w, "weather")));
            Assert.Equal(GitState.Changed, view.GitStateOf(Row(w, "seeds")));
            w.ActivePanel.Panel.SetFocus(Row(w, "seeds"));
            await Until(() => w.ActivePanel.Info.LeftText.Contains("submodule at " + Short(recorded) + " · its own changes inside", StringComparison.Ordinal));
            w.ActivePanel.Panel.SetFocus(Row(w, "weather"));
            await Until(() => w.ActivePanel.Info.LeftText.Contains("submodule not checked out", StringComparison.Ordinal));
            Capture(w, "git-submodule.png");

            await w.ActivePanel.Panel.NavigateAsync(seeds);
            await Until(() => w.ActivePanel.DirectoryLine.Git?.Label == "seeds-main");

            await w.ActivePanel.Panel.NavigateAsync(_repo);
            await w.BrowseVersionAsync(w.ActivePanel);
            await Until(() => w.ActivePanel.Panel.IsTree);
            w.ActivePanel.Panel.SetFocus(Row(w, "vendor"));
            await w.ActivePanel.Panel.OpenFocusedAsync();
            await Until(() => Names(w).Contains("seeds") && Row(w, "seeds") >= 0);
            Assert.Equal("S", view.GitLetterAt(Row(w, "seeds")));
            Assert.Equal(GitState.Ignored, view.GitStateOf(Row(w, "seeds")));
            w.ActivePanel.Panel.SetFocus(Row(w, "seeds"));
            await Until(() => w.ActivePanel.Info.LeftText.Contains(@"vendor\seeds · submodule at " + Short(recorded) + ", not part of this tree", StringComparison.Ordinal));
        }
        finally
        {
            w.Close();
        }
    }
}
