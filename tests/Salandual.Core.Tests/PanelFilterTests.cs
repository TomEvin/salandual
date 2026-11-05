// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/PanelFilterTests.cs

using System.Collections.Concurrent;
using System.Globalization;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Vfs;

namespace Salandual.Core.Tests;

public sealed class PanelFilterTests
{
    private readonly FakeFileSystem _fs = new FakeFileSystem()
        .Dir(@"C:\", "Photos/")
        .Dir(@"C:\Photos", "raw/", ".git/", "IMG_0410.jpg:10", "IMG_0412.heic:20", "IMG_9001.jpg:30", "notes.txt:5", ".DS_Store:1", "route.gpx:4")
        .Dir(@"C:\Photos\raw", "IMG_0411.heic:7", "IMG_0413.arw:9", "x.txt:1");

    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly FilePanel _panel;

    public PanelFilterTests()
        => _panel = new FilePanel(_fs, new EntrySorter(CultureInfo.GetCultureInfo("en-US"), numericSort: true), _posted.Enqueue);

    private string[] Names => [.. _panel.Items.Skip(_panel.HasUpEntry ? 1 : 0).Select(e => e.Name)];

    private async Task Open(string path)
    {
        Assert.True(await _panel.NavigateAsync(path));
        while (_posted.TryDequeue(out var a))
        {
            a();
        }
    }

    private void Focus(string name) => _panel.SetFocus(_panel.Items.ToList().FindIndex(e => e.Name == name));

    [Fact]
    public async Task TheFilter_ShowsTheMatchingFiles_FoldersStay_AndStaysInTheSubfolder()
    {
        await Open(@"C:\Photos");
        Focus("IMG_0412.heic");
        _panel.SetFilter(new PanelFilter(FileMask.Parse("*.jpg;*.heic | IMG_9*")!));
        Assert.Equal([".git", "raw", "IMG_0410.jpg", "IMG_0412.heic"], Names);
        Assert.Equal("IMG_0412.heic", _panel.FocusedEntry?.Name);
        Assert.Equal((4, 8), (_panel.ShownCount, _panel.ListedCount));
        Assert.True(_panel.IsFiltered);
        Assert.Equal("Filter: 4 of 8 shown, 4 hidden", InformationLine.FilterSummary(_panel, CultureInfo.GetCultureInfo("en-US")));

        await Open(@"C:\Photos\raw");
        Assert.Equal(["IMG_0411.heic"], Names);

        _panel.SetFilter(new PanelFilter(FileMask.Parse("*.heic")!, Folders: true));
        await Open(@"C:\Photos");
        Assert.Equal(["IMG_0412.heic"], Names);

        _panel.ClearFilter();
        Assert.Equal(8, _panel.ShownCount);
        Assert.Null(InformationLine.FilterSummary(_panel, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public async Task OnlyThisFolder_TheFilterGoesWhenThePanelLeaves()
    {
        _panel.FilterThisFolderOnly = true;
        await Open(@"C:\Photos");
        _panel.SetFilter(new PanelFilter(FileMask.Parse("*.jpg")!));
        await Open(@"C:\Photos\raw");
        Assert.Null(_panel.Filter);
        Assert.Equal(3, _panel.ShownCount);
    }

    [Fact]
    public async Task DotFiles_OutOfSightWithoutListingAgain_AndARefreshKeepsTheFilter()
    {
        await Open(@"C:\Photos");
        _panel.SetHideDotFiles(true);
        Assert.DoesNotContain(".git", Names);
        Assert.DoesNotContain(".DS_Store", Names);
        Assert.Null(InformationLine.FilterSummary(_panel, CultureInfo.GetCultureInfo("en-US")));

        _panel.SetFilter(new PanelFilter(FileMask.Parse("*.txt")!));
        Assert.True(await _panel.RefreshAsync());
        Assert.Equal(["raw", "notes.txt"], Names);
    }

    [Fact]
    public async Task HideSelectedNames_UntilTheFolderIsLeft_UnselectedKeepsTheSelection()
    {
        await Open(@"C:\Photos");
        _panel.SelectWhere(e => e.Name is "notes.txt" or "route.gpx", true);
        Assert.True(_panel.Execute("Panel.HideSelectedNames", 10));
        Assert.DoesNotContain("notes.txt", Names);
        Assert.Equal(2, _panel.HiddenNameCount);
        Assert.Equal("2 names hidden", InformationLine.FilterSummary(_panel, CultureInfo.GetCultureInfo("en-US")));
        Assert.True(await _panel.RefreshAsync());
        Assert.DoesNotContain("route.gpx", Names);

        _panel.ShowHiddenNames();
        Assert.Contains("notes.txt", Names);

        _panel.SelectWhere(e => e.Name == "IMG_0410.jpg", true);
        Assert.True(_panel.HideNames(selected: false));
        Assert.Equal(["IMG_0410.jpg"], Names);
        Assert.True(_panel.IsSelected(1));

        await Open(@"C:\Photos\raw");
        await Open(@"C:\Photos");
        Assert.Equal(8, _panel.ShownCount);
        Assert.False(_panel.HideNames(selected: false));
    }

    [Fact]
    public async Task HiddenNamesStay_InEveryFolderUntilShown()
    {
        _panel.HiddenNamesStay = true;
        await Open(@"C:\Photos");
        Focus("notes.txt");
        Assert.True(_panel.HideNames(selected: true));
        await Open(@"C:\Photos\raw");
        await Open(@"C:\Photos");
        Assert.DoesNotContain("notes.txt", Names);
    }

    [Fact]
    public async Task QuickFilter_HidesWhatDoesNotHoldTheText_EscBringsAllBack_EnterKeepsIt()
    {
        await Open(@"C:\Photos");
        Focus("route.gpx");
        Assert.True(_panel.QuickSearchType('I'));
        Assert.False(_panel.QuickFilterMode);
        _panel.ToggleQuickFilter();
        Assert.True(_panel.IsQuickFiltering);
        Assert.Equal([".git", "IMG_0410.jpg", "IMG_0412.heic", "IMG_9001.jpg"], Names);
        foreach (var c in "MG_041")
        {
            Assert.True(_panel.QuickSearchType(c));
        }

        Assert.Equal(["IMG_0410.jpg", "IMG_0412.heic"], Names);
        Assert.Equal("Quick filter: 2 of 8 shown", InformationLine.FilterSummary(_panel, CultureInfo.GetCultureInfo("en-US")));
        Assert.False(_panel.QuickSearchType('9'));

        Focus("IMG_0412.heic");
        _panel.EndQuickSearch();
        Assert.Equal(8, _panel.ShownCount);
        Assert.Equal("IMG_0412.heic", _panel.FocusedEntry?.Name);

        _panel.QuickFilterFirst = true;
        Assert.True(_panel.QuickSearchType('x'));
        Assert.True(_panel.IsQuickFiltering);
        Assert.Equal(["notes.txt", "route.gpx"], Names);
        _panel.EndQuickSearch(keepFilter: true);
        Assert.Equal(["x"], _panel.KeptQuickFilters);
        Assert.True(_panel.IsFiltered);
        Assert.Equal(["notes.txt", "route.gpx"], Names);

        await Open(@"C:\Photos\raw");
        Assert.Empty(_panel.KeptQuickFilters);
        Assert.Equal(3, _panel.ShownCount);
    }

    [Fact]
    public async Task Sorting_KeepsWhatIsOutOfSightInOrderForWhenItComesBack()
    {
        await Open(@"C:\Photos");
        _panel.SetFilter(new PanelFilter(FileMask.Parse("*.jpg")!));
        _panel.ToggleSort(SortColumn.Size);
        _panel.ClearFilter();
        var sizes = _panel.Items.Where(e => !e.IsDirectory).Select(e => e.Size).ToList();
        Assert.True(sizes.SequenceEqual(sizes.Order()) || sizes.SequenceEqual(sizes.OrderDescending()), string.Join(",", sizes));
    }
}
