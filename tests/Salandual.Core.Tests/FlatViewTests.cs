// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/FlatViewTests.cs

using System.Collections.Concurrent;
using System.Globalization;
using Salandual.Core.Listing;
using Salandual.Core.Panels;

namespace Salandual.Core.Tests;

public sealed class FlatViewTests
{
    private readonly FakeFileSystem _fs = new FakeFileSystem()
        .Dir(@"C:\", "Projects/")
        .Dir(@"C:\Projects", "Clients/", "Website/", ".git/", "~Secret/", "@Link/", "README.md:4")
        .Dir(@"C:\Projects\Clients", "Acme/", "Bolt/")
        .Dir(@"C:\Projects\Clients\Acme", "budget.xlsx:84", "contract.pdf:412")
        .Dir(@"C:\Projects\Clients\Bolt", "offer.pdf:220")
        .Dir(@"C:\Projects\Website", "docs/", "index.html:9")
        .Dir(@"C:\Projects\Website\docs", "notes.txt:6", "deploy.md:2")
        .Dir(@"C:\Projects\.git", "config:1")
        .Dir(@"C:\Projects\Secret", "key.txt:1")
        .Dir(@"C:\Projects\Link", "elsewhere.txt:1");

    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly FilePanel _panel;

    public FlatViewTests()
        => _panel = new FilePanel(_fs, new EntrySorter(CultureInfo.GetCultureInfo("en-US"), numericSort: true), _posted.Enqueue);

    private string[] Names => [.. _panel.Items.Skip(_panel.HasUpEntry ? 1 : 0).Select(e => e.Name)];

    private async Task<T> Pump<T>(Task<T> task)
    {
        while (!task.IsCompleted)
        {
            Drain();
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }

        Drain();
        return await task;
    }

    private void Drain()
    {
        while (_posted.TryDequeue(out var a))
        {
            a();
        }
    }

    private async Task Open(string path) => Assert.True(await Pump(_panel.NavigateAsync(path)));

    [Fact]
    public async Task Flat_ListsEveryFileUnderTheFolder_NoFolders_NoLinksFollowed_HiddenOnlyWhenShown_AndBackspaceGoesBack()
    {
        await Open(@"C:\Projects");
        Assert.True(await Pump(_panel.ToggleFlatAsync()));
        Assert.True(_panel.IsFlat);
        Assert.Equal(@"C:\Projects", _panel.Path);
        Assert.False(_panel.HasUpEntry);
        Assert.Equal(
            [@"Clients\Acme\budget.xlsx", @".git\config", @"Clients\Acme\contract.pdf", @"Website\docs\deploy.md", @"Website\index.html", @"Website\docs\notes.txt", @"Clients\Bolt\offer.pdf", "README.md"],
            Names);
        Assert.Equal(new PanelFlat([], Reading: false, Stopped: false, Files: 8, FoldersRead: 7), _panel.Results!.Flat);
        Assert.Equal(1, _fs.TreeWatchCount);

        _panel.SetShowHidden(true);
        await Pump(Task.Run(async () => { while (_panel.Results!.Flat!.Reading || _panel.Count == 8) { await Task.Delay(2, TestContext.Current.CancellationToken); } return true; }));
        Assert.Contains(@"Secret\key.txt", Names);
        Assert.DoesNotContain(Names, n => n.StartsWith("Link", StringComparison.Ordinal));

        Assert.True(await Pump(_panel.ToggleFlatAsync()));
        Assert.False(_panel.IsFlat);
        Assert.Contains("Clients", Names);
        Assert.Equal(0, _fs.TreeWatchCount);
    }

    [Fact]
    public async Task SelectedFolders_OnlyWhatIsUnderThem()
    {
        await Open(@"C:\Projects");
        Assert.True(await Pump(_panel.ShowFlatAsync(["Clients", ".git"])));
        Assert.Equal([@"Clients\Acme\budget.xlsx", @".git\config", @"Clients\Acme\contract.pdf", @"Clients\Bolt\offer.pdf"], Names);
        Assert.Equal(["Clients", ".git"], _panel.Results!.Flat!.Folders);
    }

    [Fact]
    public async Task TheFilter_TheQuickFilter_AndDotFolders_LookAtTheNameWithoutItsFolder()
    {
        await Open(@"C:\Projects");
        await Pump(_panel.ShowFlatAsync());
        _panel.SetFilter(new PanelFilter(FileMask.Parse("*.pdf")!));
        Assert.Equal([@"Clients\Acme\contract.pdf", @"Clients\Bolt\offer.pdf"], Names);
        Assert.Equal((2, 8), (_panel.ShownCount, _panel.ListedCount));
        _panel.ClearFilter();

        _panel.SetHideDotFiles(true);
        Assert.DoesNotContain(@".git\config", Names);

        Assert.True(_panel.QuickSearchType('n'));
        Assert.True(_panel.QuickSearchType('o'));
        _panel.ToggleQuickFilter();
        Assert.True(_panel.QuickFilterMode);
        Assert.Equal([@"Website\docs\notes.txt"], Names);
    }

    [Fact]
    public async Task Headings_AFolderOverItsFiles_NeverSelected_AndThePathInFrontSortsByIt()
    {
        await Open(@"C:\Projects");
        _panel.SetFlatFolderShows(FlatFolderShows.Headings);
        await Pump(_panel.ShowFlatAsync());
        Assert.Equal(
            ["README.md", ".git", @".git\config", @"Clients\Acme", @"Clients\Acme\budget.xlsx", @"Clients\Acme\contract.pdf", @"Clients\Bolt", @"Clients\Bolt\offer.pdf", "Website", @"Website\index.html", @"Website\docs", @"Website\docs\deploy.md", @"Website\docs\notes.txt"],
            Names);
        Assert.True(_panel.IsHeading(1));
        Assert.False(_panel.IsHeading(2));
        _panel.Execute("Selection.SelectAll", 10);
        Assert.Equal(new SelectionSummary(8, 0, 4 + 1 + 84 + 412 + 220 + 9 + 2 + 6), _panel.GetSelectionSummary());
        _panel.Toggle(1);
        Assert.False(_panel.IsSelected(1));

        _panel.SetFlatFolderShows(FlatFolderShows.InName);
        Assert.Equal([".git\\config", @"Clients\Acme\budget.xlsx", @"Clients\Acme\contract.pdf", @"Clients\Bolt\offer.pdf", "README.md", @"Website\docs\deploy.md", @"Website\docs\notes.txt", @"Website\index.html"], Names);
        Assert.True(_panel.IsSelected(0));
    }

    [Fact]
    public async Task ABigTree_AsksBeforeReadingOn_StopHereKeepsWhatWasFound()
    {
        await Open(@"C:\Projects");
        var asked = 0;
        _panel.FlatAskAbove = 3;
        _panel.AskToReadOn = files =>
        {
            asked = files;
            return Task.FromResult(false);
        };
        await Pump(_panel.ShowFlatAsync());
        Assert.True(asked >= 3);
        Assert.Equal(asked, _panel.Count);
        Assert.True(_panel.Results!.Flat!.Stopped);

        _panel.AskToReadOn = _ => Task.FromResult(true);
        await Pump(_panel.RefreshAsync());
        Assert.Equal(8, _panel.Count);
        Assert.False(_panel.Results!.Flat!.Stopped);
    }

    [Fact]
    public async Task ChangesAnywhereInTheTree_ReadThatFolderAgain_ANewFolderWhole_AGoneFolderWithWhatWasUnderIt()
    {
        await Open(@"C:\Projects");
        await Pump(_panel.ShowFlatAsync());
        _fs.Dir(@"C:\Projects\Website\docs", "faq.md:3");
        _fs.Dir(@"C:\Projects\Clients", "Cora/");
        _fs.Dir(@"C:\Projects\Clients\Cora", "brief.docx:5");
        _fs.DeleteDir(@"C:\Projects\Clients\Bolt");
        _fs.Remove(@"C:\Projects\Clients", "Bolt");
        _fs.FireTreeChange(@"C:\Projects", @"C:\Projects\Website\docs", @"C:\Projects\Clients", @"C:\Projects\Clients\Cora", @"C:\Projects\Clients\Bolt");
        await Pump(Task.Run(async () => { while (!_posted.IsEmpty || _panel.Count == 8) { await Task.Delay(2, TestContext.Current.CancellationToken); } return true; }));
        await Task.Delay(20, TestContext.Current.CancellationToken);
        Drain();
        Assert.Contains(@"Website\docs\faq.md", Names);
        Assert.Contains(@"Clients\Cora\brief.docx", Names);
        Assert.DoesNotContain(@"Clients\Bolt\offer.pdf", Names);
        Assert.Equal(9, _panel.Results!.Flat!.Files);

        _fs.Dir(@"C:\Elsewhere", "x.txt:1");
        _fs.FireTreeChange(@"C:\Projects", @"C:\Elsewhere");
        Drain();
        Assert.Equal(9, _panel.Count);
    }
}
