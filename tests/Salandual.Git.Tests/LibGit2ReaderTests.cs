// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Git.Tests/LibGit2ReaderTests.cs

using System.Diagnostics;
using LibGit2Sharp;
using Salandual.Core.Git;

namespace Salandual.Git.Tests;

public sealed class LibGit2ReaderTests : IDisposable
{
    private static readonly Signature _ann = new("Ann Example", "ann@example.invalid", new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-git-").FullName;
    private readonly LibGit2Reader _reader = new();

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
        var path = Path.Join(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private Commit CommitAll(Repository repo, string message)
    {
        Commands.Stage(repo, "*");
        return repo.Commit(message, _ann, _ann);
    }

    private Repository Garden()
    {
        Repository.Init(_dir);
        var repo = new Repository(_dir);
        repo.Refs.UpdateTarget("HEAD", "refs/heads/main");
        Write("Planner.cs", "plan\n");
        Write("Watering.cs", "water\n");
        Write(@"Beds\Soil.cs", "soil\n");
        Write("Harvest.cs", "harvest\n");
        Write(".gitignore", "bin/\n*.log\n");
        var first = CommitAll(repo, "First");
        repo.CreateBranch("frost-warnings", first);
        Write("Program.cs", "program\n");
        var second = CommitAll(repo, "Second");

        var tree = repo.ObjectDatabase.CreateTree(new TreeDefinition().Add("Program.cs", repo.ObjectDatabase.CreateBlob(new MemoryStream("theirs\n"u8.ToArray())), Mode.NonExecutableFile));
        var theirs = repo.ObjectDatabase.CreateCommit(_ann, _ann, "Theirs", tree, [first], prettifyMessage: false);
        repo.Network.Remotes.Add("origin", "https://example.invalid/garden.git");
        repo.Refs.Add("refs/remotes/origin/main", theirs.Id);
        repo.Refs.Add("refs/remotes/origin/compost", first.Id);
        repo.Branches.Update(repo.Head, b => b.Remote = "origin", b => b.UpstreamBranch = "refs/heads/main");
        Assert.NotNull(second);
        return repo;
    }

    [Fact]
    public void Status_SaysEachFilesState_AndTheBranchWithAheadAndBehind()
    {
        using (var repo = Garden())
        {
            Write("Planner.cs", "plan changed\n");
            Write("Watering.cs", "water changed\n");
            Commands.Stage(repo, "Watering.cs");
            Write("Frost.cs", "frost\n");
            Write("Staged.cs", "staged\n");
            Commands.Stage(repo, "Staged.cs");
            Write(@"bin\Debug\app.dll", "dll");
            Write("debug.log", "log");
            Write(@"Seeds\Tomato.cs", "tomato\n");
            Write(@"Beds\Soil.cs", "soil changed\n");
        }

        var status = _reader.Status(_dir)!;
        Assert.Equal(Path.TrimEndingDirectorySeparator(_dir), status.Top);
        Assert.Equal("main", status.Branch);
        Assert.Equal("origin/main", status.Upstream);
        Assert.Equal((1, 1), (status.Ahead, status.Behind));
        Assert.Equal(GitState.Changed, status.Of(Path.Join(_dir, "Planner.cs"), false));
        Assert.Equal(GitState.Staged, status.Of(Path.Join(_dir, "Watering.cs"), false));
        Assert.Equal(GitState.New, status.Of(Path.Join(_dir, "Frost.cs"), false));
        Assert.Equal(GitState.Added, status.Of(Path.Join(_dir, "Staged.cs"), false));
        Assert.Equal(GitState.Ignored, status.Of(Path.Join(_dir, "debug.log"), false));
        Assert.Equal(GitState.Ignored, status.Of(Path.Join(_dir, "bin"), true));
        Assert.Equal(GitState.Ignored, status.Of(Path.Join(_dir, @"bin\Debug\app.dll"), false));
        Assert.Equal(GitState.New, status.Of(Path.Join(_dir, "Seeds"), true));
        Assert.Equal(GitState.New, status.Of(Path.Join(_dir, @"Seeds\Tomato.cs"), false));
        Assert.Equal(GitState.None, status.Of(Path.Join(_dir, "Harvest.cs"), false));
        Assert.Equal(GitState.Changed, status.Of(Path.Join(_dir, "Beds"), true, out var inside));
        Assert.True(inside);
        Assert.Equal(GitState.None, status.Of(Path.Join(_dir, "Harvest.cs").ToUpperInvariant(), false));
        Assert.Equal(GitState.Changed, status.Of(Path.Join(_dir, "PLANNER.CS"), false));
    }

    [Fact]
    public void Status_OfAConflict_AndOfACommitCheckedOut()
    {
        using (var repo = Garden())
        {
            Commands.Checkout(repo, "frost-warnings");
            Write("Harvest.cs", "harvest on frost\n");
            CommitAll(repo, "Frost");
            Commands.Checkout(repo, "main");
            Write("Harvest.cs", "harvest on main\n");
            CommitAll(repo, "Main");
            var merged = repo.Merge(repo.Branches["frost-warnings"], _ann, new MergeOptions { CommitOnSuccess = false });
            Assert.Equal(MergeStatus.Conflicts, merged.Status);
        }

        Assert.Equal(GitState.Conflicted, _reader.Status(_dir)!.Of(Path.Join(_dir, "Harvest.cs"), false));

        using (var repo = new Repository(_dir))
        {
            repo.Reset(ResetMode.Hard);
            Commands.Checkout(repo, repo.Head.Tip.Parents.First());
        }

        var detached = _reader.Status(_dir)!;
        Assert.Null(detached.Branch);
        Assert.Equal(7, detached.DetachedAt!.Length);
        Assert.Equal("(" + detached.DetachedAt + ")", detached.Label);
    }

    [Fact]
    public void Branches_TheOnesHere_ThenTheRemotesOnesNotHereYet()
    {
        Garden().Dispose();
        var branches = _reader.Branches(_dir);
        Assert.Equal(
            [new GitBranch("frost-warnings", false, false), new GitBranch("main", false, true, 1, 1), new GitBranch("origin/compost", true, false)],
            branches);
    }

    [Fact]
    public void Remotes_ByName_NoneOutsideARepository()
    {
        Garden().Dispose();
        Assert.Equal(["origin"], _reader.Remotes(_dir));
        Assert.Empty(_reader.Remotes(Path.Join(_dir, "nowhere")));
    }

    [Fact]
    public async Task GitsOutput_IsGivenLineByLine_ItsProgressToo_AsItComes()
    {
        var lines = new List<string>();
        using var reader = new StreamReader(new MemoryStream("Receiving objects:  10% (1/10)\rReceiving objects: 100% (10/10), done.\nAlready up to date.\n\nlast"u8.ToArray()));
        var all = await GitProgram.ReadLinesAsync(reader, lines.Add, TestContext.Current.CancellationToken);
        Assert.Equal(["Receiving objects:  10% (1/10)", "Receiving objects: 100% (10/10), done.", "Already up to date.", "last"], lines);
        Assert.Equal("Receiving objects:  10% (1/10)\rReceiving objects: 100% (10/10), done.\nAlready up to date.\n\nlast", all);
    }

    [Fact]
    public void ANewRepositoryWithoutACommit_HasItsBranch_AndNotARepository_IsNothing()
    {
        Repository.Init(_dir);
        Write("a.txt", "a");
        var status = _reader.Status(_dir)!;
        Assert.False(string.IsNullOrEmpty(status.Branch));
        Assert.Equal(GitState.New, status.Of(Path.Join(_dir, "a.txt"), false));

        var plain = Directory.CreateTempSubdirectory("salandual-nogit-").FullName;
        try
        {
            Assert.Null(_reader.Status(plain));
        }
        finally
        {
            Directory.Delete(plain);
        }
    }

    [Fact]
    public void Libgit2_IsLoadedOnlyFromSalandualsOwnFolder()
    {
        Garden().Dispose();
        Assert.NotNull(_reader.Status(_dir));
        var loaded = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(m => m.ModuleName.StartsWith("git2-", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(loaded);
        Assert.StartsWith(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), loaded[0].FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitExe_IsFoundInGitsFolders_OrInAFolderOfPathGivenInFull_NeverARelativeOne()
    {
        var there = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Tools\git.exe", @".\git.exe", @"D:\Apps\Git\cmd\git.exe" };
        Assert.Equal(@"D:\Apps\Git\cmd\git.exe", GitProgram.Find(@".;C:\Tools", [@"C:\Program Files", @"D:\Apps"], there.Contains));
        Assert.Equal(@"C:\Tools\git.exe", GitProgram.Find(@".;""C:\Tools""", [@"C:\Program Files"], there.Contains));
        Assert.Null(GitProgram.Find(@".;bin", [@"C:\Program Files"], there.Contains));
    }

    [Fact]
    public void Changes_InTheirGroups_WithTheLinesAddedAndRemoved()
    {
        using (var repo = Garden())
        {
            Write("Planner.cs", "plan\nmore\nand more\n");
            Write("Watering.cs", "water changed\n");
            Commands.Stage(repo, "Watering.cs");
            Write("Harvest.cs", "harvest staged\n");
            Commands.Stage(repo, "Harvest.cs");
            Write("Harvest.cs", "harvest staged\nand changed again\n");
            File.Delete(Path.Join(_dir, "Program.cs"));
            Write(@"Frost\Dates.cs", "a\nb\nc");
            Write(@"Frost\[1].cs", "x\n");
            Write(@"bin\app.dll", "dll");
        }

        var changes = _reader.Changes(_dir)!.ToDictionary(c => c.Path);
        Assert.Equal([@"Frost\Dates.cs", @"Frost\[1].cs", "Harvest.cs", "Planner.cs", "Program.cs", "Watering.cs"], changes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((GitChangeGroup.Changed, 2, 0), (changes["Planner.cs"].Group, changes["Planner.cs"].Added, changes["Planner.cs"].Removed));
        Assert.Equal((GitChangeGroup.Staged, 1, 1), (changes["Watering.cs"].Group, changes["Watering.cs"].Added, changes["Watering.cs"].Removed));
        Assert.True(changes["Watering.cs"].HasStaged);
        Assert.Equal(GitChangeGroup.Changed, changes["Harvest.cs"].Group);
        Assert.True(changes["Harvest.cs"].HasStaged);
        Assert.Equal((2, 1), (changes["Harvest.cs"].Added, changes["Harvest.cs"].Removed));
        Assert.Equal((GitChangeGroup.Changed, false, "D"), (changes["Program.cs"].Group, changes["Program.cs"].OnDisk, changes["Program.cs"].Letter));
        Assert.Equal((GitChangeGroup.New, 3, false), (changes[@"Frost\Dates.cs"].Group, changes[@"Frost\Dates.cs"].Added, changes[@"Frost\Dates.cs"].InHead));
        Assert.Equal("?", changes[@"Frost\[1].cs"].Letter);
        Assert.True(changes["Planner.cs"].InHead);
    }

    private static string Text(byte[] bytes) => System.Text.Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n");

    [Fact]
    public void Version_IsTheCommittedOrStagedFile_AsItWouldBeCheckedOut()
    {
        using (var repo = Garden())
        {
            Write("Watering.cs", "water staged\n");
            Commands.Stage(repo, "Watering.cs");
            Write("Watering.cs", "water on the disk\n");
        }

        Assert.Equal("water\n", Text(_reader.Version(_dir, "Watering.cs", GitVersion.Head, 1 << 20)!));
        Assert.Equal("water staged\n", Text(_reader.Version(_dir, "Watering.cs", GitVersion.Index, 1 << 20)!));
        Assert.Equal("soil\n", Text(_reader.Version(_dir, @"Beds\Soil.cs", GitVersion.Head, 1 << 20)!));
        Assert.Null(_reader.Version(_dir, "Nothing.cs", GitVersion.Head, 1 << 20));
        Assert.Null(_reader.Version(_dir, "Watering.cs", GitVersion.Head, 3));
    }

    [Fact]
    public void Head_SaysTheLastCommit_WhetherItIsPushed_TheAuthor_AndTheLastMessages()
    {
        using (var repo = Garden())
        {
            repo.Config.Set("user.name", "Ann Example", ConfigurationLevel.Local);
            repo.Config.Set("user.email", "ann@example.invalid", ConfigurationLevel.Local);
        }

        var head = _reader.Head(_dir)!;
        Assert.Equal(7, head.Sha!.Length);
        Assert.Equal("Second", head.Message);
        Assert.False(head.Pushed);
        Assert.Equal("Ann Example <ann@example.invalid>", head.Author);
        Assert.Equal(["Second", "First"], head.Recent);

        using (var repo = new Repository(_dir))
        {
            repo.Refs.UpdateTarget("refs/remotes/origin/main", repo.Head.Tip.Id.Sha);
        }

        Assert.True(_reader.Head(_dir)!.Pushed);
    }

    [Fact]
    public void Head_InANewRepository_HasNoCommit()
    {
        Repository.Init(_dir);
        var head = _reader.Head(_dir)!;
        Assert.Null(head.Sha);
        Assert.Empty(head.Recent);
    }

    [Fact]
    public void Changes_InAFolder_AreCountedToo()
    {
        using (var repo = Garden())
        {
            Write(@"Beds\Soil.cs", "soil\nmore\n");
            Commands.Stage(repo, "Beds/Soil.cs");
            Write(@"Beds\Raised.cs", "raised\n");
            Commands.Stage(repo, "Beds/Raised.cs");
            Commands.Stage(repo, "*");
            repo.Commit("Beds", _ann, _ann);
            Write(@"Beds\Soil.cs", "soil\nmore\nand more\n");
            Write(@"Beds\Raised.cs", "raised\nhigh\n");
            Commands.Stage(repo, "Beds/Raised.cs");
        }

        var changes = _reader.Changes(_dir)!.ToDictionary(c => c.Path);
        Assert.Equal((1, 0), (changes[@"Beds\Soil.cs"].Added, changes[@"Beds\Soil.cs"].Removed));
        Assert.Equal((1, 0), (changes[@"Beds\Raised.cs"].Added, changes[@"Beds\Raised.cs"].Removed));
    }

    [Fact]
    public void Changes_WithADeletedFile_AreCountedToo()
    {
        Repository.Init(_dir);
        using (var repo = new Repository(_dir))
        {
            Write(@"src\Planner.cs", "plan\nbeds\n");
            Write(@"src\Watering.cs", "water\n");
            Write(@"docs\old-notes.md", "old\nnotes\n");
            CommitAll(repo, "First");
            Write(@"src\Planner.cs", "plan\nbeds in use\nfrost\n");
            Write(@"src\Watering.cs", "water\nmore\n");
            Commands.Stage(repo, "src/Watering.cs");
            File.Delete(Path.Join(_dir, @"docs\old-notes.md"));
            Write(@"src\Frost.cs", "frost\ndates\nwarnings\n");
        }

        var changes = _reader.Changes(_dir)!.ToDictionary(c => c.Path);
        Assert.Equal((1, 0), (changes[@"src\Watering.cs"].Added, changes[@"src\Watering.cs"].Removed));
        Assert.Equal((2, 1), (changes[@"src\Planner.cs"].Added, changes[@"src\Planner.cs"].Removed));
        Assert.Equal((0, 2), (changes[@"docs\old-notes.md"].Added, changes[@"docs\old-notes.md"].Removed));
        Assert.Equal((3, 0), (changes[@"src\Frost.cs"].Added, changes[@"src\Frost.cs"].Removed));
    }

    [Fact]
    public void Log_NewestFirst_WithBranchAndTagNames_AndWhatIsntPushed()
    {
        string first;
        using (var repo = Garden())
        {
            first = repo.Head.Tip.Parents.Single().Sha;
            repo.ApplyTag("v0.1", first);
        }

        var log = _reader.Log(_dir, new GitLogQuery())!;
        Assert.Equal(["Second", "First"], log.Commits.Select(c => c.Summary));
        Assert.Equal("origin/main", log.Upstream);
        Assert.False(log.Commits[0].Pushed);
        Assert.True(log.Commits[1].Pushed);
        Assert.Equal(new GitRef("main", GitRefKind.Branch, true), log.Commits[0].Refs[0]);
        Assert.Contains(new GitRef("v0.1", GitRefKind.Tag), log.Commits[1].Refs);
        Assert.Contains(new GitRef("frost-warnings", GitRefKind.Branch), log.Commits[1].Refs);
        Assert.Contains(new GitRef("origin/compost", GitRefKind.Remote), log.Commits[1].Refs);
        Assert.Equal([first], log.Commits[0].Parents);
        Assert.Equal("Ann Example", log.Commits[0].Author);

        var all = _reader.Log(_dir, new GitLogQuery(AllBranches: true))!;
        Assert.Equal(3, all.Commits.Count);
        Assert.Contains(all.Commits, c => c.Summary == "Theirs");
        Assert.Equal(["Second"], _reader.Log(_dir, new GitLogQuery(Path: "Program.cs"))!.Commits.Select(c => c.Summary));
        Assert.Equal(["First"], _reader.Log(_dir, new GitLogQuery(Path: "Beds"))!.Commits.Select(c => c.Summary));
        var one = _reader.Log(_dir, new GitLogQuery(Limit: 1))!;
        Assert.Single(one.Commits);
        Assert.True(one.More);
    }

    [Fact]
    public void ACommitsChanges_AgainstItsFirstParent_AndAFileAsItWasThen()
    {
        string third, first;
        using (var repo = Garden())
        {
            Write("Planner.cs", "plan\nbetter\n");
            File.Delete(Path.Join(_dir, "Harvest.cs"));
            Commands.Stage(repo, "*");
            third = repo.Commit("Third", _ann, _ann).Sha;
            first = repo.Head.Tip.Parents.Single().Parents.Single().Sha;
        }

        var changes = _reader.CommitChanges(_dir, third)!.ToDictionary(c => c.Path);
        Assert.Equal((GitState.Changed, 1, 0), (changes["Planner.cs"].State, changes["Planner.cs"].Added, changes["Planner.cs"].Removed));
        Assert.Equal((GitState.Deleted, 0, 1), (changes["Harvest.cs"].State, changes["Harvest.cs"].Added, changes["Harvest.cs"].Removed));
        Assert.Equal(2, changes.Count);

        var firstChanges = _reader.CommitChanges(_dir, first)!;
        Assert.Contains(firstChanges, c => c.Path == @"Beds\Soil.cs" && c.State == GitState.Added && c.Added == 1);

        Assert.Equal("plan\n", Text(_reader.VersionAt(_dir, "Planner.cs", first, 1 << 20)!));
        Assert.Null(_reader.VersionAt(_dir, "Harvest.cs", third, 1 << 20));
        Assert.Null(_reader.CommitChanges(_dir, "not a hash; something"));
    }

    [Fact]
    public void ACommitsTree_FolderByFolder_AndAFileWrittenOutAsItWas()
    {
        string third, first;
        using (var repo = Garden())
        {
            Write("Planner.cs", "plan\nbetter\n");
            File.Delete(Path.Join(_dir, "Harvest.cs"));
            Commands.Stage(repo, "*");
            third = repo.Commit("Third", _ann, _ann).Sha;
            first = repo.Head.Tip.Parents.Single().Parents.Single().Sha;
        }

        var top = _reader.Tree(_dir, first, "")!;
        Assert.Contains(new GitTreeItem("Beds", true, 0), top);
        Assert.Contains(new GitTreeItem("Planner.cs", false, 5), top);
        Assert.Contains(top, i => i.Name == "Harvest.cs");
        Assert.DoesNotContain(_reader.Tree(_dir, third, "")!, i => i.Name == "Harvest.cs");
        Assert.Contains(_reader.Tree(_dir, first, "Beds")!, i => i.Name == "Soil.cs" && !i.IsFolder);
        Assert.Null(_reader.Tree(_dir, first, "No such folder"));
        Assert.Null(_reader.Tree(_dir, "not a hash; something", ""));

        var target = Path.Join(Directory.CreateTempSubdirectory("salandual-gittree-").FullName, "Planner.cs");
        try
        {
            Assert.True(_reader.SaveVersionAt(_dir, "Planner.cs", first, target));
            Assert.Equal("plan\n", File.ReadAllText(target).ReplaceLineEndings("\n"));
            Assert.False(_reader.SaveVersionAt(_dir, "Planner.cs", third, target));
            Assert.False(_reader.SaveVersionAt(_dir, "Harvest.cs", third, target + ".2"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(target)!, recursive: true);
        }
    }

    private static readonly Signature _sam = new("Sam Sample", "sam@example.invalid", new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Blame_SaysWhichCommitLastChangedEachLine_AndLinesNotCommitted()
    {
        string first, second;
        using (var repo = Garden())
        {
            Write("Planner.cs", "one\ntwo\nthree\n");
            first = CommitAll(repo, "Plan by Ann").Sha;
            Write("Planner.cs", "one\nTWO\nthree\n");
            Commands.Stage(repo, "*");
            second = repo.Commit("Plan by Sam", _sam, _sam).Sha;
        }

        Write("Planner.cs", "one\nTWO\nthree\nfour, not committed\n");
        var (blame, refusal) = _reader.Blame(_dir, "Planner.cs", null);
        Assert.Equal(GitBlameRefusal.None, refusal);
        Assert.NotNull(blame);
        Assert.Equal(4, blame.Lines);
        Assert.Equal(first, blame.RunAt(0)!.Commit!.Sha);
        Assert.Equal((second, "Sam Sample"), (blame.RunAt(1)!.Commit!.Sha, blame.RunAt(1)!.Commit!.Author));
        Assert.Equal(first, blame.RunAt(2)!.Commit!.Sha);
        Assert.Null(blame.RunAt(3)!.Commit);
        Assert.Equal([0, 1, 2, 3], blame.Runs.Select(r => r.Start));
        Assert.Equal(0, blame.Age(blame.RunAt(1)!.Commit));
        Assert.Equal(1, blame.Age(blame.RunAt(0)!.Commit));

        var (before, _) = _reader.Blame(_dir, "Planner.cs", first);
        Assert.Single(before!.Runs);
        Assert.Equal((0, 3, first), (before.Runs[0].Start, before.Runs[0].Count, before.Runs[0].Commit!.Sha));

        Write("Frost.cs", "new\n");
        Assert.Equal(GitBlameRefusal.NotInGit, _reader.Blame(_dir, "Frost.cs", null).Refusal);
        Assert.Equal(GitBlameRefusal.Unreadable, _reader.Blame(_dir, "Planner.cs", "not a hash; something").Refusal);
        using (var repo = new Repository(_dir))
        {
            File.WriteAllBytes(Path.Join(_dir, "seed.bin"), [1, 0, 2]);
            CommitAll(repo, "Binary");
        }

        Assert.Equal(GitBlameRefusal.Binary, _reader.Blame(_dir, "seed.bin", null).Refusal);
    }

    [Fact]
    public void LinesOf_LeavesNoLastEmptyLine_AndTakesCrLf()
    {
        Assert.Equal(["a", "b"], LibGit2Reader.LinesOf("a\r\nb\r\n"u8.ToArray()));
        Assert.Equal(["a", "", "b"], LibGit2Reader.LinesOf("a\n\nb"u8.ToArray()));
        Assert.Empty(LibGit2Reader.LinesOf([]));
    }

    [Fact]
    public void Tags_NewestFirst_AnAnnotatedOnesOwnMessage_ALightweightOnesCommits()
    {
        string first, second;
        using (var repo = Garden())
        {
            second = repo.Head.Tip.Sha;
            first = repo.Head.Tip.Parents.Single().Sha;
            repo.ApplyTag("v0.1", first);
            repo.ApplyTag("v0.2", second, _sam, "Harvest dates\n\nThe second release.");
        }

        var tags = _reader.Tags(_dir)!;
        Assert.Equal(["v0.2", "v0.1"], tags.Select(t => t.Name));
        Assert.Equal((second, true, "Sam Sample"), (tags[0].Commit.Sha, tags[0].IsAnnotated, tags[0].By));
        Assert.StartsWith("Harvest dates", tags[0].Message, StringComparison.Ordinal);
        Assert.Equal((first, false, "First", "Ann Example"), (tags[1].Commit.Sha, tags[1].IsAnnotated, tags[1].Message, tags[1].By));
        Assert.Contains(new GitRef("v0.1", GitRefKind.Tag), tags[1].Commit.Refs);
        Assert.Null(new LibGit2Reader().Tags(Path.Join(_dir, "not a repository")));
    }

    private string WithSubmodules()
    {
        using var repo = Garden();
        var seeds = Path.Join(_dir, "vendor", "seeds");
        Directory.CreateDirectory(seeds);
        Repository.Init(seeds);
        string recorded;
        using (var sub = new Repository(seeds))
        {
            File.WriteAllText(Path.Join(seeds, "seeds.csv"), "carrot\n");
            Commands.Stage(sub, "*");
            recorded = sub.Commit("Seeds", _ann, _ann).Sha;
        }

        Directory.CreateDirectory(Path.Join(_dir, "vendor", "weather"));
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
        return commit.Sha;
    }

    [Fact]
    public void Submodules_TheirRecordedCommit_AndWhatTheirFoldersHold()
    {
        var sha = WithSubmodules();
        var subs = _reader.Submodules(_dir).ToDictionary(s => s.Path);
        Assert.Equal(GitSubmoduleState.Same, subs[@"vendor\seeds"].State);
        Assert.Equal(7, subs[@"vendor\seeds"].Recorded.Length);
        Assert.Equal(GitSubmoduleState.NotCheckedOut, subs[@"vendor\weather"].State);

        var status = _reader.Status(_dir)!;
        Assert.NotNull(status.SubmoduleAt(Path.Join(_dir, "vendor", "seeds")));
        Assert.Null(status.SubmoduleAt(Path.Join(_dir, "vendor")));
        Assert.Equal(GitState.None, status.Of(Path.Join(_dir, "vendor", "seeds"), isDirectory: true));

        File.WriteAllText(Path.Join(_dir, "vendor", "seeds", "seeds.csv"), "carrot\nleek\n");
        Assert.Equal(GitSubmoduleState.ChangesInside, _reader.Submodules(_dir).Single(s => s.Path == @"vendor\seeds").State);
        Assert.Equal(GitState.Changed, _reader.Status(_dir)!.Of(Path.Join(_dir, "vendor", "seeds"), isDirectory: true));
        using (var sub = new Repository(Path.Join(_dir, "vendor", "seeds")))
        {
            Commands.Stage(sub, "*");
            sub.Commit("Leeks", _ann, _ann);
        }

        Assert.Equal(GitSubmoduleState.OtherCommit, _reader.Submodules(_dir).Single(s => s.Path == @"vendor\seeds").State);

        var vendor = _reader.Tree(_dir, sha, "vendor")!;
        Assert.All(vendor, i => Assert.Equal(7, i.Submodule?.Length));
        Assert.Equal(["seeds", "weather"], vendor.Select(i => i.Name).Order());
    }
}
