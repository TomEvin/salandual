// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/FilePanelTests.cs

using System.Globalization;
using Salandual.Core.Listing;
using Salandual.Core.Panels;

namespace Salandual.Core.Tests;

public sealed class FilePanelTests
{
    private readonly FakeFileSystem _fs = new FakeFileSystem()
        .Dir(@"C:\", "Data/", "Windows/", "boot.ini:10")
        .Dir(@"C:\Data", "Photos/", "Docs/", "IMG_0412.HEIC:3000", "IMG_0412.JPG:900", "IMG_0413.HEIC:3100", "notes.md:40", "README:5", "~secret.txt:1", "árak.json:2")
        .Dir(@"C:\Data\Photos", "a.jpg:1");

    private readonly FilePanel _panel;

    public FilePanelTests() => _panel = new FilePanel(_fs, new EntrySorter(CultureInfo.GetCultureInfo("en-US"), numericSort: true), a => a());

    private string[] Names => [.. _panel.Items.Select(e => e.Name)];

    private string[] SelectedNames => [.. _panel.Items.Where((_, i) => _panel.IsSelected(i)).Select(e => e.Name)];

    private async Task OpenData()
    {
        Assert.True(await _panel.NavigateAsync(@"C:\Data"));
    }

    private void Focus(string name) => _panel.SetFocus(Array.IndexOf(Names, name));

    private void Run(params string[] commands)
    {
        foreach (var c in commands)
        {
            Assert.True(_panel.Execute(c, pageRows: 4), c);
        }
    }

    [Fact]
    public async Task Navigate_UpEntryFirst_FoldersThenFiles_HiddenFilteredOut()
    {
        await OpenData();
        Assert.Equal(["..", "Docs", "Photos", "árak.json", "IMG_0412.HEIC", "IMG_0412.JPG", "IMG_0413.HEIC", "notes.md", "README"], Names);
        Assert.True(_panel.IsUp(0));
        Assert.Equal(0, _panel.Focus);
    }

    [Fact]
    public async Task RefreshWithAName_FocusesTheNewItem_AndSelectsTheGivenNames()
    {
        await OpenData();
        Focus("notes.md");
        _panel.Toggle(Array.IndexOf(Names, "README"));
        _fs.Remove(@"C:\Data", "notes.md");
        _fs.Dir(@"C:\Data", "Jegyzetek.md:40", "notes (2).md:40");

        await _panel.RefreshAsync("Jegyzetek.md");
        Assert.Equal("Jegyzetek.md", _panel.FocusedEntry?.Name);
        Assert.Equal(["README"], SelectedNames);

        await _panel.RefreshAsync("notes (2).md", ["notes (2).md", "Jegyzetek.md"]);
        Assert.Equal("notes (2).md", _panel.FocusedEntry?.Name);
        Assert.Equal(["Jegyzetek.md", "notes (2).md"], SelectedNames);

        await _panel.RefreshAsync("Docs", []);
        Assert.Empty(SelectedNames);
    }

    [Fact]
    public async Task SetSorter_ResortsInPlace_KeepingFocusAndSelection()
    {
        var fs = new FakeFileSystem().Dir(@"C:\N", "IMG_10.jpg:1", "IMG_2.jpg:1", "IMG_1.jpg:1");
        var panel = new FilePanel(fs, new EntrySorter(CultureInfo.GetCultureInfo("en-US"), numericSort: true), a => a());
        await panel.NavigateAsync(@"C:\N");
        Assert.Equal(["..", "IMG_1.jpg", "IMG_2.jpg", "IMG_10.jpg"], panel.Items.Select(e => e.Name));
        panel.Toggle(2);
        panel.SetFocus(2);

        panel.SetSorter(new EntrySorter(CultureInfo.GetCultureInfo("en-US"), numericSort: false));

        Assert.Equal(["..", "IMG_1.jpg", "IMG_10.jpg", "IMG_2.jpg"], panel.Items.Select(e => e.Name));
        Assert.Equal("IMG_2.jpg", panel.FocusedEntry?.Name);
        Assert.True(panel.IsSelected(3));
    }

    [Fact]
    public async Task Root_HasNoUpEntry()
    {
        await _panel.NavigateAsync(@"C:\");
        Assert.False(_panel.HasUpEntry);
        Assert.Equal("Data", Names[0]);
    }

    [Fact]
    public async Task Parent_FocusesTheFolderWeCameFrom()
    {
        await _panel.NavigateAsync(@"C:\Data\Photos");
        await _panel.GoParentAsync();
        Assert.Equal(@"C:\Data", _panel.Path);
        Assert.Equal("Photos", _panel.FocusedEntry?.Name);
    }

    [Fact]
    public async Task Parent_OffersTheScreenRowTheFolderHadWhenWeLeft()
    {
        await OpenData();
        Focus("Photos");
        _panel.TopIndex = 1;
        Assert.Null(_panel.TakeFocusScreenRow());

        await _panel.OpenFocusedAsync(foldersOnly: true);
        Assert.Equal(@"C:\Data\Photos", _panel.Path);
        Assert.Null(_panel.TakeFocusScreenRow());

        await _panel.GoParentAsync();
        Assert.Equal("Photos", _panel.FocusedEntry?.Name);
        Assert.Equal(_panel.Focus - 1, _panel.TakeFocusScreenRow());
        Assert.Null(_panel.TakeFocusScreenRow());

        await _panel.RefreshAsync();
        Assert.Null(_panel.TakeFocusScreenRow());
    }

    [Fact]
    public async Task Enter_OpensFolder_UpGoesUp_FileAsksToRun()
    {
        await OpenData();
        Focus("Photos");
        await _panel.OpenFocusedAsync();
        Assert.Equal(@"C:\Data\Photos", _panel.Path);

        await _panel.OpenFocusedAsync();
        Assert.Equal(@"C:\Data", _panel.Path);

        string? run = null;
        _panel.ExecuteRequested += (_, p) => run = p;
        Focus("notes.md");
        await _panel.OpenFocusedAsync();
        Assert.Equal(@"C:\Data\notes.md", run);
    }

    [Fact]
    public async Task Root_Command_GoesToDriveRoot()
    {
        await _panel.NavigateAsync(@"C:\Data\Photos");
        await _panel.GoRootAsync();
        Assert.Equal(@"C:\", _panel.Path);
    }

    [Fact]
    public async Task FailedNavigation_KeepsPanel_AndReportsError()
    {
        await OpenData();
        string? error = null;
        _panel.Error += (_, m) => error = m;
        Assert.False(await _panel.NavigateAsync(@"C:\Missing"));
        Assert.Equal(@"C:\Data", _panel.Path);
        Assert.Contains("Missing", error);
    }

    [Fact]
    public async Task Refresh_KeepsSelectionAndFocusByName()
    {
        await OpenData();
        Focus("notes.md");
        _panel.Toggle(Array.IndexOf(Names, "IMG_0413.HEIC"));
        _fs.Dir(@"C:\Data", "AAA.txt:1");

        await _panel.RefreshAsync();

        Assert.Equal("notes.md", _panel.FocusedEntry?.Name);
        Assert.Equal(["IMG_0413.HEIC"], SelectedNames);
    }

    [Fact]
    public async Task Refresh_FocusedItemDeleted_FocusStaysAtSamePosition()
    {
        await OpenData();
        Focus("IMG_0412.JPG");
        var position = _panel.Focus;
        _fs.Remove(@"C:\Data", "IMG_0412.JPG");

        await _panel.RefreshAsync();

        Assert.Equal(position, _panel.Focus);
        Assert.Equal("IMG_0413.HEIC", _panel.FocusedEntry?.Name);
    }

    [Fact]
    public async Task WatcherChange_RefreshesTheList()
    {
        await OpenData();
        var refreshed = new TaskCompletionSource();
        _panel.Changed += (_, c) =>
        {
            if (c.HasFlag(PanelChange.Items))
            {
                refreshed.TrySetResult();
            }
        };
        _fs.Dir(@"C:\Data", "new.txt:1");

        _fs.FireChange(@"C:\Data");

        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains("new.txt", Names);
    }

    [Fact]
    public async Task WatcherError_FolderDeleted_GoesUpToNearestExisting()
    {
        await _panel.NavigateAsync(@"C:\Data\Photos");
        var moved = new TaskCompletionSource();
        _panel.Changed += (_, c) =>
        {
            if (c.HasFlag(PanelChange.Items))
            {
                moved.TrySetResult();
            }
        };
        _fs.DeleteDir(@"C:\Data");

        _fs.FireError(@"C:\Data\Photos");

        await moved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(@"C:\", _panel.Path);
    }

    [Fact]
    public async Task ShiftDown_SelectsPassedItems_FirstItemDecides()
    {
        await OpenData();
        Focus("IMG_0412.HEIC");
        Run("Selection.ExtendDown", "Selection.ExtendDown");
        Assert.Equal(["IMG_0412.HEIC", "IMG_0412.JPG"], SelectedNames);
        Assert.Equal("IMG_0413.HEIC", _panel.FocusedEntry?.Name);

        Focus("IMG_0412.HEIC");
        Run("Selection.ExtendDown");
        Assert.Equal(["IMG_0412.JPG"], SelectedNames);
    }

    [Fact]
    public async Task ShiftEnd_IncludesLast_ShiftHome_NeverSelectsUp()
    {
        await OpenData();
        Focus("notes.md");
        Run("Selection.ExtendToLast");
        Assert.Equal(["notes.md", "README"], SelectedNames);

        Run("Selection.UnselectAll");
        Focus("Docs");
        Run("Selection.ExtendToFirst");
        Assert.Equal(["Docs"], SelectedNames);
        Assert.Equal(0, _panel.Focus);
    }

    [Fact]
    public async Task Insert_And_Space_ToggleAndMoveDown()
    {
        await OpenData();
        Focus("Docs");
        Run("Selection.ToggleAndNext");
        Assert.Equal("Photos", _panel.FocusedEntry?.Name);
        Run("Selection.ToggleAndSize");
        Assert.Equal(["Docs", "Photos"], SelectedNames);
        Assert.Equal("árak.json", _panel.FocusedEntry?.Name);

        _panel.SetFocus(0);
        Run("Selection.ToggleAndSize");
        Assert.False(_panel.IsSelected(0));
        Assert.Equal(1, _panel.Focus);
    }

    [Fact]
    public async Task SelectAll_Invert_FilesOnlyUnlessSetting()
    {
        await OpenData();
        Run("Selection.SelectAll");
        Assert.Equal(8, _panel.GetSelectionSummary().Count);

        Run("Selection.UnselectAll", "Selection.InvertFiles");
        Assert.Equal(new SelectionSummary(6, 0, 3000 + 900 + 3100 + 40 + 5 + 2), _panel.GetSelectionSummary());

        _panel.SelectFoldersWithNumPlus = true;
        Run("Selection.UnselectAll", "Selection.InvertFiles");
        Assert.Equal(8, _panel.GetSelectionSummary().Count);

        Run("Selection.InvertAll");
        Assert.Equal(0, _panel.GetSelectionSummary().Count);
    }

    [Fact]
    public async Task SameExtension_AndSameName()
    {
        await OpenData();
        Focus("IMG_0412.HEIC");
        Run("Selection.SameExtension");
        Assert.Equal(["IMG_0412.HEIC", "IMG_0413.HEIC"], SelectedNames);
        Run("Selection.UnselectSameExtension", "Selection.SameName");
        Assert.Equal(["IMG_0412.HEIC", "IMG_0412.JPG"], SelectedNames);
    }

    [Fact]
    public async Task OperationNames_SelectionOrFocusedItem()
    {
        await OpenData();
        Focus("notes.md");
        Assert.Equal(["notes.md"], _panel.GetOperationNames());
        _panel.SetFocus(0);
        Assert.Empty(_panel.GetOperationNames());
        Run("Selection.SelectAll");
        Assert.Equal(8, _panel.GetOperationNames().Count);
    }

    [Fact]
    public async Task Sort_KeepsFocusAndSelection_SecondPressReverses()
    {
        await OpenData();
        Focus("notes.md");
        _panel.Toggle(Array.IndexOf(Names, "README"));
        Run("Panel.SortBySize");
        Assert.Equal(["..", "Docs", "Photos", "árak.json", "README", "notes.md", "IMG_0412.JPG", "IMG_0412.HEIC", "IMG_0413.HEIC"], Names);
        Assert.Equal("notes.md", _panel.FocusedEntry?.Name);
        Assert.Equal(["README"], SelectedNames);

        Run("Panel.SortBySize");
        Assert.Equal(new SortSpec(SortColumn.Size, Descending: true), _panel.Sort);
        Assert.Equal("..", Names[0]);
        Assert.Equal("IMG_0413.HEIC", Names[3]);
    }

    [Fact]
    public async Task ToggleHidden_ListsAgain()
    {
        await OpenData();
        var listed = new TaskCompletionSource();
        _panel.Changed += (_, c) =>
        {
            if (c.HasFlag(PanelChange.Items))
            {
                listed.TrySetResult();
            }
        };
        Run("Panel.ToggleHidden");
        await listed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains("secret.txt", Names);
    }

    [Fact]
    public async Task SelectedNavigation_AltKeys()
    {
        await OpenData();
        _panel.Toggle(Array.IndexOf(Names, "Docs"));
        _panel.Toggle(Array.IndexOf(Names, "notes.md"));
        Run("Focus.FirstSelected");
        Assert.Equal("Docs", _panel.FocusedEntry?.Name);
        Run("Focus.NextSelected");
        Assert.Equal("notes.md", _panel.FocusedEntry?.Name);
        Run("Focus.NextSelected");
        Assert.Equal("notes.md", _panel.FocusedEntry?.Name);
        Run("Focus.FirstFile");
        Assert.Equal("árak.json", _panel.FocusedEntry?.Name);
    }

    [Fact]
    public void UnknownCommand_IsLeftToTheWindow() => Assert.False(_panel.Execute("File.Copy", 10));

    [Fact]
    public async Task QuickSearch_JumpsToFirstMatch_RefusesNonMatching()
    {
        await OpenData();
        Assert.True(_panel.QuickSearchType('i'));
        Assert.Equal("IMG_0412.HEIC", _panel.FocusedEntry?.Name);
        Assert.False(_panel.QuickSearchType('x'));
        Assert.Equal("i", _panel.QuickSearchText);
        Assert.True(_panel.QuickSearchType('m'));
        Assert.Equal("IMG_0412.HEIC", _panel.FocusedEntry?.Name);
    }

    [Fact]
    public async Task QuickSearch_UpDown_MoveBetweenMatches_ShiftSelects()
    {
        await OpenData();
        _panel.QuickSearchType('i');
        _panel.QuickSearchMove(1);
        Assert.Equal("IMG_0412.JPG", _panel.FocusedEntry?.Name);
        _panel.QuickSearchMove(int.MaxValue);
        Assert.Equal("IMG_0413.HEIC", _panel.FocusedEntry?.Name);
        _panel.QuickSearchMove(int.MinValue);
        Assert.Equal("IMG_0412.HEIC", _panel.FocusedEntry?.Name);

        _panel.QuickSearchMove(1, select: true);
        _panel.QuickSearchMove(1, select: true);
        Assert.Equal(["IMG_0412.HEIC", "IMG_0412.JPG"], SelectedNames);
    }

    [Fact]
    public async Task QuickSearch_Wildcards_FromSalamanderHelp()
    {
        await OpenData();
        foreach (var c in "/.md")
        {
            Assert.True(_panel.QuickSearchType(c));
        }

        Assert.Equal("notes.md", _panel.FocusedEntry?.Name);
    }

    [Fact]
    public async Task QuickSearch_Back_Extend_End()
    {
        await OpenData();
        _panel.QuickSearchType('n');
        _panel.QuickSearchExtend();
        _panel.QuickSearchExtend();
        Assert.Equal("not", _panel.QuickSearchText);
        _panel.QuickSearchBack();
        Assert.Equal("no", _panel.QuickSearchText);
        _panel.QuickSearchBack();
        _panel.QuickSearchBack();
        Assert.Null(_panel.QuickSearchText);
        _panel.QuickSearchType('r');
        _panel.EndQuickSearch();
        Assert.Null(_panel.QuickSearchText);
        Assert.Equal("README", _panel.FocusedEntry?.Name);
    }

    [Theory]
    [InlineData("file.txt", "/le", true)]
    [InlineData("less.exe", "/le", true)]
    [InlineData("bmp.rle", "/le", true)]
    [InlineData("promote.html", "pro/.htm", true)]
    [InlineData("prototype.htm", "pro\\.htm", true)]
    [InlineData("index.html", "/.htm", true)]
    [InlineData("flag.txt", "/g.", true)]
    [InlineData("pic.jpg", "/g.", false)]
    [InlineData("ÁRVÍZ.txt", "árv", true)]
    [InlineData("README", "readme.", true)]
    [InlineData("readme2", "readme.", false)]
    [InlineData("readme.txt", "readme.", true)]
    [InlineData("abc", "b", false)]
    [InlineData("abc", "a<c", true)]
    [InlineData("anything", "", true)]
    public void QuickSearchPattern_Matches(string name, string pattern, bool expected)
        => Assert.Equal(expected, QuickSearchPattern.IsMatch(name, pattern));
}

public sealed class FilePanelCommandListTests
{
    [Fact]
    public void HandledCommands_MatchExecute_AndAreRegistered()
    {
        var registry = Salandual.Core.Commands.CommandRegistry.CreateDefault();
        using var panel = new FilePanel(new FakeFileSystem(), new EntrySorter(CultureInfo.InvariantCulture, true), a => a());
        foreach (var id in FilePanel.HandledCommands)
        {
            Assert.True(registry.TryGet(id, out _), id);
            Assert.True(panel.Execute(id, 10), id);
        }

        foreach (var definition in registry.Commands.Where(c => !FilePanel.HandledCommands.Contains(c.Id)))
        {
            Assert.False(panel.Execute(definition.Id, 10), definition.Id);
        }
    }

}

public sealed class SearchResultsPanelTests
{
    private readonly FakeFileSystem _fs = new FakeFileSystem()
        .Dir(@"C:\", "Data/")
        .Dir(@"C:\Data", "Photos/", "Docs/", "notes.md:40", "README:5");

    private readonly FilePanel _panel;

    public SearchResultsPanelTests() => _panel = new FilePanel(_fs, new EntrySorter(CultureInfo.GetCultureInfo("en-US"), numericSort: true), a => a());

    private string[] Names => [.. _panel.Items.Select(e => e.Name)];

    private string[] SelectedNames => [.. _panel.Items.Where((_, i) => _panel.IsSelected(i)).Select(e => e.Name)];

    private async Task OpenData() => Assert.True(await _panel.NavigateAsync(@"C:\Data"));

    private void Focus(string name) => _panel.SetFocus(Array.IndexOf(Names, name));

    private static Salandual.Core.Vfs.FileEntry Found(string name, long size = 1) => new(name, size, 0, FileAttributes.Archive);

    [Fact]
    public async Task Search_results_in_a_panel_named_by_their_path_without_up_and_back_to_the_folder()
    {
        await OpenData();
        Focus("notes.md");
        var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _panel.ShowResults(@"C:\Data", [Found(@"Photos\a.jpg"), Found("notes.md"), Found(@"Docs.old\README")], "*.* containing “x”", path => gone.Contains(path) ? null : Found(path[(path.LastIndexOf('\\') + 1)..], 7));
        Assert.Equal(@"C:\Data", _panel.Path);
        Assert.False(_panel.HasUpEntry);
        Assert.Equal("*.* containing “x”", _panel.Results!.What);
        Assert.Equal(@"C:\Data", _panel.Results.ReturnPath);
        Assert.Equal([@"Photos\a.jpg", "notes.md", @"Docs.old\README"], Names);
        Assert.Equal(@"Docs.old\README".Length, _panel.Items[2].ExtensionStart);
        Assert.Equal(("Docs.old", "README"), FilePanel.SplitResult(@"Docs.old\README"));

        _panel.SelectByMask(FileMask.Parse("a.*")!, select: true);
        Assert.Equal([@"Photos\a.jpg"], SelectedNames);
        gone.Add(@"C:\Data\notes.md");
        Assert.True(await _panel.RefreshAsync());
        Assert.Equal([@"Photos\a.jpg", @"Docs.old\README"], Names);
        Assert.Equal(7, _panel.Items[0].Size);
        Assert.Equal([@"Photos\a.jpg"], SelectedNames);

        _panel.RenameResult(@"Photos\a.jpg", "b.jpg");
        Assert.Equal(@"Photos\b.jpg", _panel.FocusedEntry?.Name);

        await _panel.GoParentAsync();
        Assert.Null(_panel.Results);
        Assert.Equal(@"C:\Data", _panel.Path);
        Assert.True(_panel.HasUpEntry);
        Assert.Equal("notes.md", _panel.FocusedEntry?.Name);
    }

    [Fact]
    public async Task Search_results_keep_their_order_when_asked_and_alt_left_goes_back()
    {
        await OpenData();
        _panel.ShowResults(@"C:\Data", [Found("z.txt"), Found("a.txt")], "duplicates", _ => null, keepOrder: true);
        Assert.Equal(["z.txt", "a.txt"], Names);
        _panel.ToggleSort(SortColumn.Name);
        Assert.Equal(2, _panel.Count);
        Assert.True(await _panel.GoBackAsync());
        Assert.Null(_panel.Results);
        Assert.Equal(@"C:\Data", _panel.Path);
    }
}
