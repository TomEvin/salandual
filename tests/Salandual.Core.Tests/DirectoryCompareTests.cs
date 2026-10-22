// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/DirectoryCompareTests.cs

using Salandual.Core.Compare;
using Salandual.Core.Listing;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class DirectoryCompareTests
{
    private static readonly DateTime Noon = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<DirectoryCompareResult> Compare(MemoryFiles files, CompareDirectoriesOptions? options = null, bool showHidden = true)
        => await new DirectoryCompare(files, files.Open, options ?? new CompareDirectoriesOptions(), showHidden).RunAsync(@"C:\L", @"D:\R");

    private static string Line(CompareDifference d) => $"{(d.Subfolder.Length > 0 ? d.Subfolder + "\\" : "")}{d.Name} {d.Kind} {d.Selected}";

    [Fact]
    public async Task By_date_and_size_the_newer_or_bigger_is_selected_and_one_side_only_on_its_side()
    {
        var files = new MemoryFiles()
            .Folder(@"C:\L\drafts").Folder(@"D:\R\old").Folder(@"C:\L\assets").Folder(@"D:\R\assets")
            .File(@"C:\L\CHANGELOG.md", "longer text", Noon.AddDays(4)).File(@"D:\R\CHANGELOG.md", "short", Noon)
            .File(@"C:\L\same.txt", "x", Noon).File(@"D:\R\same.txt", "x", Noon)
            .File(@"C:\L\index.html", "aaa", Noon).File(@"D:\R\index.html", "a", Noon.AddDays(1))
            .File(@"C:\L\notes.txt", "n", Noon)
            .File(@"D:\R\sitemap.xml", "s", Noon);
        var result = await Compare(files);
        Assert.Equal(
            ["CHANGELOG.md NewerOrBigger Left", "drafts OnlyLeft Left", "index.html NewerOrBigger Both", "notes.txt OnlyLeft Left", "old OnlyRight Right", "sitemap.xml OnlyRight Right"],
            result.Differences.Where(d => d.Kind != DifferenceKind.Same).Select(Line).Order(StringComparer.Ordinal));
        Assert.Equal(2, result.Count(DifferenceKind.OnlyLeft));
        Assert.Equal(2, result.Count(DifferenceKind.OnlyRight));
        Assert.Equal(2, result.Count(DifferenceKind.NewerOrBigger));
        Assert.False(result.Cancelled);
    }

    [Fact]
    public async Task Times_close_enough_are_counted_as_the_same()
    {
        var files = new MemoryFiles()
            .File(@"C:\L\usb.txt", "x", Noon).File(@"D:\R\usb.txt", "x", Noon.AddSeconds(2))
            .File(@"C:\L\summer.txt", "x", Noon).File(@"D:\R\summer.txt", "x", Noon.AddHours(1).AddSeconds(1))
            .File(@"C:\L\later.txt", "x", Noon).File(@"D:\R\later.txt", "x", Noon.AddSeconds(3));

        var result = await Compare(files);
        Assert.Equal(["later.txt NewerOrBigger Right", "summer.txt NewerOrBigger Right", "usb.txt CountedSame None"], result.Differences.Where(d => d.Kind != DifferenceKind.Same).Select(Line).Order(StringComparer.Ordinal));

        result = await Compare(files, new CompareDirectoriesOptions { IgnoreOneHour = true });
        Assert.Equal(["later.txt NewerOrBigger Right", "summer.txt CountedSame None", "usb.txt CountedSame None"], result.Differences.Where(d => d.Kind != DifferenceKind.Same).Select(Line).Order(StringComparer.Ordinal));

        result = await Compare(files, new CompareDirectoriesOptions { IgnoreTwoSeconds = false });
        Assert.Equal(3, result.Count(DifferenceKind.NewerOrBigger));
    }

    [Fact]
    public async Task Attributes_and_contents_select_both_and_cloud_files_are_not_read()
    {
        var files = new MemoryFiles()
            .File(@"C:\L\a.txt", "same", Noon).File(@"D:\R\a.txt", "same", Noon, System.IO.FileAttributes.Archive | System.IO.FileAttributes.ReadOnly)
            .File(@"C:\L\b.txt", "abcd", Noon).File(@"D:\R\b.txt", "abce", Noon)
            .File(@"C:\L\c.txt", "same", Noon).File(@"D:\R\c.txt", "same", Noon)
            .File(@"C:\L\cloud.txt", "abcd", Noon, System.IO.FileAttributes.Archive | (System.IO.FileAttributes)0x00400000).File(@"D:\R\cloud.txt", "abce", Noon);
        var options = new CompareDirectoriesOptions { Newer = false, Bigger = false, Attributes = true, Content = true };
        var result = await Compare(files, options);
        Assert.Equal(["a.txt Different Both", "b.txt Different Both"], result.Differences.Where(d => d.Kind != DifferenceKind.Same).Select(Line).Order(StringComparer.Ordinal));
        Assert.Equal(1, result.NotRead);
        Assert.DoesNotContain(@"C:\L\cloud.txt", files.Opened);
        Assert.DoesNotContain(@"C:\L\a.txt", files.Opened);
    }

    [Fact]
    public async Task Inside_subfolders_with_masks_left_out_and_folders_only_on_one_side()
    {
        var files = new MemoryFiles()
            .File(@"C:\L\src\app.cs", "new code", Noon.AddDays(1)).File(@"D:\R\src\app.cs", "old", Noon)
            .File(@"C:\L\src\same.cs", "x", Noon).File(@"D:\R\src\same.cs", "x", Noon)
            .File(@"C:\L\src\deep\x.tmp", "t", Noon)
            .File(@"C:\L\docs\a.md", "x", Noon).File(@"D:\R\docs\a.md", "x", Noon)
            .File(@"C:\L\.git\HEAD", "a", Noon).File(@"D:\R\.git\HEAD", "b", Noon.AddDays(1))
            .Folder(@"C:\L\empty");

        var result = await Compare(files);
        Assert.Equal(["empty OnlyLeft Left"], result.Differences.Where(d => d.Kind != DifferenceKind.Same).Select(Line));

        var options = new CompareDirectoriesOptions { Subfolders = true, FoldersOnOneSide = false, LeaveOutFiles = true };
        result = await Compare(files, options);
        Assert.Equal(["src Different Both", @"src\app.cs NewerOrBigger Left"], result.Differences.Where(d => d.Kind != DifferenceKind.Same).Select(Line).Order(StringComparer.Ordinal));
        Assert.True(result.Differences.Single(d => d.Name == "src").Inside);
        Assert.Equal(1, result.Count(DifferenceKind.Different));
        Assert.Equal(0, result.Count(DifferenceKind.NewerOrBigger));

        result = await Compare(files, options with { FoldersOnOneSide = true });
        Assert.Contains(@"src\deep OnlyLeft Left", result.Differences.Where(d => d.Kind != DifferenceKind.Same).Select(Line));
        Assert.Contains("empty OnlyLeft Left", result.Differences.Where(d => d.Kind != DifferenceKind.Same).Select(Line));
    }

    [Fact]
    public async Task Only_these_with_a_bar_and_hidden_items_only_when_the_panels_show_them()
    {
        var files = new MemoryFiles()
            .File(@"C:\L\a.cs", "x", Noon).File(@"C:\L\b.md", "x", Noon).File(@"C:\L\c.txt", "x", Noon)
            .File(@"C:\L\secret.ini", "x", Noon, System.IO.FileAttributes.Hidden).Folder(@"D:\R");
        var result = await Compare(files, new CompareDirectoriesOptions { LeaveOutFiles = true, LeaveOutFilesMask = "|*.cs;*.md" }, showHidden: false);
        Assert.Equal(["a.cs OnlyLeft Left", "b.md OnlyLeft Left"], result.Differences.Where(d => d.Kind != DifferenceKind.Same).Select(Line).Order(StringComparer.Ordinal));
    }

    private static MemoryFiles Website() => new MemoryFiles()
        .File(@"C:\L\assets\icons\home.svg", "h", Noon).File(@"D:\R\assets\icons\home.svg", "h", Noon)
        .File(@"C:\L\assets\icons\mail.svg", "m", Noon)
        .File(@"C:\L\assets\logo.svg", "x", Noon).File(@"D:\R\assets\logo.svg", "x", Noon, System.IO.FileAttributes.Archive | System.IO.FileAttributes.ReadOnly)
        .File(@"C:\L\assets\site.css", "longer", Noon.AddDays(8)).File(@"D:\R\assets\site.css", "short", Noon)
        .File(@"C:\L\drafts\post-1.md", "1", Noon).File(@"C:\L\drafts\post-2.md", "2", Noon).Folder(@"C:\L\drafts\pics")
        .File(@"D:\R\old\index-2025.html", "o", Noon)
        .File(@"C:\L\config.json", "c", Noon).File(@"D:\R\config.json", "c", Noon)
        .File(@"C:\L\robots.txt", "r", Noon).File(@"D:\R\robots.txt", "r", Noon.AddSeconds(2))
        .File(@"C:\L\notes.txt", "n", Noon);

    [Fact]
    public async Task What_is_the_same_and_what_is_in_a_folder_on_one_side_are_kept_for_the_list()
    {
        var result = await Compare(Website(), new CompareDirectoriesOptions { Subfolders = true, Attributes = true });
        var lines = result.Differences.Select(d => Line(d) + (d.InOneSided ? " in" : "")).ToList();
        Assert.Contains("config.json Same None", lines);
        Assert.Contains(@"assets\icons\home.svg Same None", lines);
        Assert.Contains(@"drafts\post-1.md OnlyLeft Left in", lines);
        Assert.Contains(@"drafts\pics OnlyLeft Left in", lines);
        Assert.Contains(@"old\index-2025.html OnlyRight Right in", lines);
        Assert.Equal(3, result.Count(DifferenceKind.OnlyLeft) + result.Count(DifferenceKind.OnlyRight));
        Assert.Equal(["assets", "config.json", "notes.txt", "robots.txt"], result.In("").Where(d => d.Left is not null && !d.IsDirectory || d.Name == "assets").Select(d => d.Name).Order(StringComparer.Ordinal));

        result = await Compare(Website());
        Assert.DoesNotContain(result.Differences, d => d.Subfolder.Length > 0);
    }

    [Fact]
    public async Task The_list_side_by_side_folders_first_its_filters_and_counts()
    {
        var tree = CompareTree.From(await Compare(Website(), new CompareDirectoriesOptions { Subfolders = true, Attributes = true }));
        Assert.Equal(["assets", "drafts", "old", "config.json", "notes.txt", "robots.txt"], tree.Roots.Select(n => n.Item.Name));
        Assert.Equal(["icons", "logo.svg", "site.css"], tree.Find("assets")!.Children.Select(n => n.Item.Name));
        Assert.Equal(@"assets\icons\mail.svg", tree.Find(@"assets\icons\mail.svg")!.Path);

        int Count(DifferenceFilter kind, string? names = null, bool not = false) => tree.Count(new CompareListFilter(CompareView.Different, kind, FileMask.Parse(names), not));
        Assert.Equal(8, Count(DifferenceFilter.Orphans));
        Assert.Equal(6, Count(DifferenceFilter.LeftOrphans));
        Assert.Equal(2, Count(DifferenceFilter.RightOrphans));
        Assert.Equal(1, Count(DifferenceFilter.LeftNewer));
        Assert.Equal(0, Count(DifferenceFilter.RightNewer));
        Assert.Equal(7, Count(DifferenceFilter.LeftNewerAndLeftOrphans));
        Assert.Equal(2, Count(DifferenceFilter.RightNewerAndRightOrphans));
        Assert.Equal(2, Count(DifferenceFilter.DifferencesButNoOrphans));
        Assert.Equal(5, Count(DifferenceFilter.NoOrphans));
        Assert.Equal(10, Count(DifferenceFilter.Differences));
        Assert.Equal(13, tree.Count(new CompareListFilter(CompareView.All, DifferenceFilter.Differences)));
        Assert.Equal(3, tree.Count(new CompareListFilter(CompareView.Same, DifferenceFilter.Differences)));

        Assert.Equal(2, Count(DifferenceFilter.Differences, "*.md"));
        Assert.Equal(5, Count(DifferenceFilter.Differences, "*.md", not: true));

        var differences = CompareListFilter.Differences;
        var same = new CompareListFilter(CompareView.Same, DifferenceFilter.Differences);
        var assets = tree.Find("assets")!;
        Assert.False(CompareTree.Matches(assets, differences));
        Assert.True(CompareTree.Shows(assets, differences));
        Assert.False(CompareTree.Shows(tree.Find("config.json")!, differences));
        Assert.True(CompareTree.Shows(tree.Find("config.json")!, same));
        Assert.False(CompareTree.Shows(tree.Find("drafts")!, new CompareListFilter(CompareView.Different, DifferenceFilter.LeftNewer)));
        Assert.True(CompareTree.Shows(tree.Find("drafts")!, differences with { Names = FileMask.Parse("*.md") }));
        Assert.False(CompareTree.Matches(tree.Find("drafts")!, differences with { Names = FileMask.Parse("*.md") }));
        Assert.Equal(["assets", "assets\\icons", "drafts", "old"], tree.Folders(differences).Select(n => n.Path).Order(StringComparer.Ordinal));
        Assert.Equal(["assets", "assets\\icons"], tree.Folders(same).Select(n => n.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_folder_that_cant_be_read_is_named_and_stopping_says_so()
    {
        var files = new MemoryFiles().File(@"C:\L\a.txt", "x", Noon).Folder(@"D:\R");
        files.Unreadable.Add(@"D:\R");
        var result = await Compare(files);
        Assert.Empty(result.Differences);
        Assert.Contains(result.Problems, p => p.StartsWith(@"D:\R:", StringComparison.Ordinal));

        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        result = await new DirectoryCompare(files, files.Open, new CompareDirectoriesOptions(), true).RunAsync(@"C:\L", @"D:\R", null, stop.Token);
        Assert.True(result.Cancelled);
    }
}
