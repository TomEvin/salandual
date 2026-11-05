// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/QuickOpenDrivesTests.cs

using Salandual.Core.QuickOpen;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class QuickOpenDrivesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-quick-drives-").FullName;

    public void Dispose()
    {
        foreach (var d in Directory.EnumerateDirectories(_dir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(d, FileAttributes.Directory);
        }

        Directory.Delete(_dir, recursive: true);
    }

    private string Write(params string[] parts)
    {
        var path = Path.Join([_dir, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Theory]
    [InlineData("node_modules", "node_modules")]
    [InlineData("  \"D:\\Backups\\\"  ", @"D:\Backups")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData("bad*name", null)]
    [InlineData("x:relative", null)]
    [InlineData(@"\\server\share", null)]
    [InlineData("..", null)]
    [InlineData("", null)]
    public void AListLine_IsANameOrAFullPath(string line, string? kept) => Assert.Equal(kept, WalkFilter.Clean(line));

    [Fact]
    public void TheFilter_LeavesOutByNameAnywhere_AndByPathOnlyThere()
    {
        var filter = new WalkFilter(true, true, true, ["node_modules", @"D:\Backups"]);
        Assert.True(filter.SkipsFolder(@"E:\Code\app\node_modules", "node_modules"));
        Assert.True(filter.SkipsFolder(@"D:\Backups", "Backups"));
        Assert.False(filter.SkipsFolder(@"E:\Backups", "Backups"));
        Assert.True(filter.SkipsFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Windows"));
        Assert.False(new WalkFilter(true, true, false, []).SkipsFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Windows"));
        Assert.Equal(FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System, filter.Attributes);
        Assert.Equal(FileAttributes.ReparsePoint, new WalkFilter(false, false, false, []).Attributes);
    }

    [Fact]
    public void TheWalk_GoesOverEveryDrive_LeavesOut_AndGoesOnWhereItStopped()
    {
        var a = Path.Join(_dir, "A");
        var b = Path.Join(_dir, "B");
        Write("A", "Docs", "plan.pdf");
        Write("A", "node_modules", "lib.js");
        Write("A", "Hidden", "secret.txt");
        File.SetAttributes(Path.Join(a, "Hidden"), FileAttributes.Directory | FileAttributes.Hidden);
        Write("B", "Indexed", "found-by-index.txt");
        Write("B", "Photos", "one.jpg");
        Write("B", "Photos", "two.jpg");

        var walk = new DriveWalk([a, b], new WalkFilter(true, true, false, ["node_modules"]), covered: [Path.Join(b, "Indexed")]);
        Assert.True(walk.Run(2, TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.Equal(2, walk.Count);
        Assert.Equal([b], walk.Unfinished);

        var told = 0;
        Assert.False(walk.Run(100, TimeSpan.FromSeconds(10), CancellationToken.None, n => told = n));
        Assert.True(walk.Finished);
        Assert.Empty(walk.Unfinished);
        var names = walk.Entries.Select(e => e.Name).ToList();
        Assert.Equal(["Docs", "plan.pdf", "Indexed", "Photos", "one.jpg", "two.jpg"], names);
        Assert.DoesNotContain(walk.Entries, e => e.Folder.EndsWith("Indexed", StringComparison.Ordinal));
        Assert.Contains(walk.Entries, e => e is { Name: "Photos", IsFolder: true } && e.Folder == b);
        Assert.False(walk.Run(10, TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.True(told is 0 or 6);
    }

    [Fact]
    public void TheWalk_StopsWhenCancelled_AndOnACoveredDrive_ReadsNothing()
    {
        Write("A", "x.txt");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var walk = new DriveWalk([Path.Join(_dir, "A")], WalkFilter.None);
        Assert.True(walk.Run(10, TimeSpan.FromSeconds(10), cancel.Token));
        Assert.Equal(0, walk.Count);

        var covered = new DriveWalk([Path.Join(_dir, "A")], WalkFilter.None, covered: [Path.Join(_dir, "A")]);
        Assert.False(covered.Run(10, TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.Equal(0, covered.Count);
    }

    [Fact]
    public void TheWalkBelowAFolder_LeavesOutTheListedFolders()
    {
        Write("Code", "node_modules", "lib.js");
        Write("Code", "app.cs");
        var walked = QuickOpenSearch.Walk(Path.Join(_dir, "Code"), showHidden: true, CancellationToken.None, filter: new WalkFilter(false, false, false, ["node_modules"]));
        Assert.Equal(["app.cs"], walked.Entries.Select(e => e.Name));
    }

    [Theory]
    [InlineData("budget", "*b*u*d*g*e*t*", "'%b%u%d%g%e%t%'")]
    [InlineData("a b", "*a*b*", "'%a%b%'")]
    [InlineData("50%_[x]'", "*5*0*%*_*[*x*]*'*", "'%5%0%[%]%[_]%[[]%x%]%''%'")]
    [InlineData("*?\"", "", "'%*%?%\"%'")]
    public void TheIndexes_GetTheLettersInOrder_Quoted(string typed, string wildcards, string like)
    {
        Assert.Equal(wildcards, FileNamePattern.Wildcards(typed));
        Assert.Equal(like, FileNamePattern.Like(typed));
    }

    [Fact]
    public void ASlashFirst_IsFilesOnly()
    {
        Assert.Equal(new QuickOpenQuery(QuickScope.Files, "budget"), QuickOpenQuery.Parse("/budget"));
        Assert.Equal("/rep", QuickOpenQuery.Parse("rep").In(QuickScope.Files));
    }

    [Fact]
    public void TheSettings_AreKeptInTheirRanges()
    {
        var q = new AppSettings
        {
            QuickOpen = new QuickOpenSettings
            {
                FilesFrom = (QuickOpenFilesFrom)42,
                WalkEntries = 1,
                WalkSeconds = 1000,
                SkipDrives = ["e:", "E:", "bad", "F"],
                SkipFolders = ["node_modules", "NODE_MODULES", "a|b"],
            },
        }.Normalize().QuickOpen;
        Assert.Equal(QuickOpenFilesFrom.Best, q.FilesFrom);
        Assert.Equal(QuickOpenSettings.MinWalkEntries, q.WalkEntries);
        Assert.Equal(QuickOpenSettings.MaxWalkSeconds, q.WalkSeconds);
        Assert.Equal(["E:"], q.SkipDrives);
        Assert.Equal(["node_modules"], q.SkipFolders);
        Assert.True(new QuickOpenSettings().SkipHidden && new QuickOpenSettings().SkipSystem && new QuickOpenSettings().SkipWindowsFolders);
    }
}
