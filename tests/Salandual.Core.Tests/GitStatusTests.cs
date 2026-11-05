// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/GitStatusTests.cs

using Salandual.Core.Git;

namespace Salandual.Core.Tests;

public sealed class GitStatusTests
{
    private const string Top = @"D:\Projects\garden-planner";

    private static GitStatus Garden() => GitStatus.From(Top + @"\", [
        ("Planner.cs", GitState.Changed),
        ("src/Beds/Soil.cs", GitState.Staged),
        ("src/Beds/Frost.cs", GitState.Conflicted),
        ("src/Seeds/", GitState.New),
        ("bin/", GitState.Ignored),
        ("debug.log", GitState.Ignored),
    ], branch: "main", upstream: "origin/main", ahead: 2, behind: 1);

    [Fact]
    public void AFile_ItsOwnState_AFolder_TheStrongestInside_AFolderListedWhole_ForEverythingInIt()
    {
        var s = Garden();
        Assert.Equal(GitState.Changed, s.Of(Top + @"\Planner.cs", false));
        Assert.Equal(GitState.Conflicted, s.Of(Top + @"\src", true, out var inside));
        Assert.True(inside);
        Assert.Equal(GitState.Conflicted, s.Of(Top + @"\src\Beds", true));
        Assert.Equal(GitState.New, s.Of(Top + @"\src\Seeds", true, out inside));
        Assert.False(inside);
        Assert.Equal(GitState.New, s.Of(Top + @"\src\Seeds\Tomato\Red.cs", false));
        Assert.Equal(GitState.Ignored, s.Of(Top + @"\bin\Debug", true));
        Assert.Equal(GitState.None, s.Of(Top + @"\Program.cs", false));
        Assert.Equal(GitState.None, s.Of(@"D:\Projects\garden-planner-old\Planner.cs", false));
        Assert.Equal(GitState.Conflicted, s.Of(Top, true, out inside));
        Assert.True(inside);
        Assert.Equal(4, s.ChangedCount);
        Assert.Equal("main", s.Label);
    }

    [Fact]
    public void AFolderTakes_AConflictFirst_ThenAChange_ADeletion_Staged_New()
    {
        Assert.Equal(GitState.Conflicted, GitStatus.Stronger(GitState.Changed, GitState.Conflicted));
        Assert.Equal(GitState.Changed, GitStatus.Stronger(GitState.Staged, GitState.Changed));
        Assert.Equal(GitState.Deleted, GitStatus.Stronger(GitState.Deleted, GitState.Added));
        Assert.Equal(GitState.Staged, GitStatus.Stronger(GitState.Staged, GitState.New));
        Assert.Equal(GitState.New, GitStatus.Stronger(GitState.Ignored, GitState.New));
    }

    [Fact]
    public void TheRepository_IsFoundByItsGitFolderOrFile_InTheFolderOrAbove_NeverInsideGit()
    {
        var dir = Directory.CreateTempSubdirectory("salandual-gitfind-").FullName;
        try
        {
            var repo = Path.Join(dir, "garden");
            Directory.CreateDirectory(Path.Join(repo, ".git"));
            Directory.CreateDirectory(Path.Join(repo, "src", "Beds"));
            var worktree = Path.Join(dir, "worktree");
            Directory.CreateDirectory(worktree);
            File.WriteAllText(Path.Join(worktree, ".git"), "gitdir: elsewhere");

            Assert.Equal(repo, GitRepositories.TopOf(Path.Join(repo, "src", "Beds")));
            Assert.Equal(repo, GitRepositories.TopOf(repo + @"\"));
            Assert.Equal(worktree, GitRepositories.TopOf(worktree));
            Assert.Null(GitRepositories.TopOf(Path.Join(repo, ".git")));
            Assert.Null(GitRepositories.TopOf(Path.Join(repo, ".GIT", "refs")));
            Assert.Null(GitRepositories.TopOf(dir));
            Assert.Null(GitRepositories.TopOf(@"relative\folder"));
            Assert.Null(GitRepositories.TopOf(""));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("frost-warnings", true)]
    [InlineData("feature/frost", true)]
    [InlineData("v0.1", true)]
    [InlineData("", false)]
    [InlineData("@", false)]
    [InlineData("-f", false)]
    [InlineData("frost warnings", false)]
    [InlineData("a..b", false)]
    [InlineData("a/", false)]
    [InlineData("/a", false)]
    [InlineData("a//b", false)]
    [InlineData("a.", false)]
    [InlineData(".hidden", false)]
    [InlineData("a/.b", false)]
    [InlineData("a.lock", false)]
    [InlineData("a@{1}", false)]
    [InlineData("a~1", false)]
    [InlineData("a^", false)]
    [InlineData("a:b", false)]
    [InlineData("a?", false)]
    [InlineData("a*", false)]
    [InlineData("a[b", false)]
    [InlineData(@"a\b", false)]
    [InlineData("a\tb", false)]
    public void ABranchsName_FollowsGitsRules(string name, bool valid) => Assert.Equal(valid, GitNames.IsValidBranch(name));

    [Fact]
    public void TheCommands_ArgumentsAsAList()
    {
        Assert.Equal(["switch", "frost"], GitCommandLines.Switch(new GitBranch("frost", false, false)));
        Assert.Equal(["switch", "--track", "origin/compost"], GitCommandLines.Switch(new GitBranch("origin/compost", true, false)));
        Assert.Equal(["switch", "-c", "seeds"], GitCommandLines.NewBranch("seeds", switchTo: true));
        Assert.Equal(["branch", "seeds"], GitCommandLines.NewBranch("seeds", switchTo: false));
    }

    [Fact]
    public void Staging_Discarding_AndCommitting_PathsTakenLiterally_AfterTheirDashes()
    {
        Assert.Equal([["--literal-pathspecs", "add", "-A", "--", "src/Planner.cs", "-rf", "a[1]*.cs"]], GitCommandLines.Stage([@"src\Planner.cs", "-rf", "a[1]*.cs"]));
        Assert.Equal([["--literal-pathspecs", "restore", "--staged", "--", "Watering.cs"]], GitCommandLines.Unstage(["Watering.cs"], noCommitYet: false));
        Assert.Equal([["--literal-pathspecs", "rm", "--cached", "-q", "--", "Watering.cs"]], GitCommandLines.Unstage(["Watering.cs"], noCommitYet: true));
        Assert.Equal([["--literal-pathspecs", "restore", "--source=HEAD", "--staged", "--worktree", "--", "Planner.cs"]], GitCommandLines.DiscardToHead(["Planner.cs"]));
        Assert.Equal([["--literal-pathspecs", "rm", "--cached", "-q", "--ignore-unmatch", "--", "New.cs"]], GitCommandLines.DiscardAdded(["New.cs"]));
        Assert.Equal(["commit", "-m", "-m looks like an option\n\nIt isn't."], GitCommandLines.Commit("-m looks like an option\n\nIt isn't.", amend: false));
        Assert.Equal(["commit", "--amend", "-m", "Plan beds"], GitCommandLines.Commit("Plan beds", amend: true));
        Assert.Equal(["add", "-A"], GitCommandLines.StageAll());
    }

    [Fact]
    public void ALongListOfPaths_IsSplitIntoSeveralCommands()
    {
        var paths = Enumerable.Range(0, 2000).Select(i => @"src\folder\file-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".cs").ToList();
        var runs = GitCommandLines.Stage(paths);
        Assert.True(runs.Count > 1);
        Assert.All(runs, run => Assert.True(run.Skip(4).Sum(p => p.Length + 3) <= GitCommandLines.MaxPathChars));
        Assert.Equal(paths.Select(p => p.Replace('\\', '/')), runs.SelectMany(run => run.Skip(4)));
    }

    [Fact]
    public void AChangesLetter_AsGitWritesIt()
    {
        Assert.Equal("M", new GitChange("a.cs", GitChangeGroup.Changed, GitState.Changed, 1, 0, true, false, true).Letter);
        Assert.Equal("D", new GitChange("a.cs", GitChangeGroup.Changed, GitState.Deleted, null, null, true, false, false).Letter);
        Assert.Equal("A", new GitChange("a.cs", GitChangeGroup.Staged, GitState.Added, 3, 0, false, true, true).Letter);
        Assert.Equal("?", new GitChange("a.cs", GitChangeGroup.New, GitState.New, 3, 0, false, false, true).Letter);
        Assert.Equal("U", new GitChange("a.cs", GitChangeGroup.Conflicted, GitState.Conflicted, null, null, true, true, true).Letter);
    }
}
