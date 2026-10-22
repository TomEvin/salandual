// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/CompareDirectoriesTests.cs

using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Input;
using Salandual.App.Compare;
using Salandual.App.Panels;
using Salandual.Core.Compare;

namespace Salandual.App.Tests;

public sealed class CompareDirectoriesTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-compare-").FullName;
    private readonly string _left;
    private readonly string _right;

    public CompareDirectoriesTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Website")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Backup")).FullName;
        Directory.CreateDirectory(Path.Join(_left, "drafts"));
        Directory.CreateDirectory(Path.Join(_right, "old"));
        Write(_left, "CHANGELOG.md", "longer text", Noon.AddDays(4));
        Write(_right, "CHANGELOG.md", "short", Noon);
        Write(_left, "same.txt", "x", Noon);
        Write(_right, "same.txt", "x", Noon.AddSeconds(1));
        Write(_left, "notes.txt", "n", Noon);
        Write(_right, "sitemap.xml", "s", Noon);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static void Write(string folder, string name, string text, DateTime modified)
    {
        var path = Path.Join(folder, name);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, modified);
    }

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_left);
        await w.RightPanel.Panel.NavigateAsync(_right);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static string[] Selected(PanelHost host)
    {
        var p = host.Panel;
        return [.. p.Items.Where((_, i) => p.IsSelected(i)).Select(e => e.Name).Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static async Task<CompareDirectoriesDialogRun> Compare(MainWindow w)
    {
        Assert.True(w.Run("Compare.Directories", Salandual.Core.Commands.PanelTarget.Active));
        await Until(() => w.CompareDialog is not null);
        return new CompareDirectoriesDialogRun(w.CompareDialog!);
    }

    private sealed record CompareDirectoriesDialogRun(Dialogs.CompareDirectoriesDialog Dialog);

    [AvaloniaFact]
    public async Task CtrlF10_selects_the_differences_in_both_panels_with_marks_and_the_bar_counts_them()
    {
        var w = await Open();
        var run = await Compare(w);
        Assert.True(run.Dialog.NewerBox.IsChecked);
        Assert.True(run.Dialog.BiggerBox.IsChecked);
        Assert.False(run.Dialog.ContentBox.IsChecked);
        run.Dialog.Accept();
        await Until(() => w.Comparison is not null);

        Assert.Equal(["CHANGELOG.md", "drafts", "notes.txt"], Selected(w.LeftPanel));
        Assert.Equal(["old", "sitemap.xml"], Selected(w.RightPanel));
        var left = w.LeftPanel.Panel.CompareMarks!;
        Assert.Equal(DifferenceKind.NewerOrBigger, left["CHANGELOG.md"]);
        Assert.Equal(DifferenceKind.OnlyLeft, left["drafts"]);
        Assert.False(left.ContainsKey("same.txt"));
        Assert.Equal(DifferenceKind.OnlyRight, w.RightPanel.Panel.CompareMarks!["sitemap.xml"]);
        Assert.True(w.CompareBar!.IsVisible);
        Assert.Equal("Compared: ← 2 only here  ·  → 2 only there  ·  ▲ 1 newer or bigger  ·  1 time close enough counted as the same", w.CompareBarText);
        Assert.NotNull(w.DifferencesList);
        Assert.True(w.CompareListButton.IsVisible && w.CompareRefreshButton.IsVisible);

        var lp = w.LeftPanel.Panel;
        lp.SetFocus(Enumerable.Range(0, lp.Count).First(i => lp.Items[i].Name == "CHANGELOG.md"));
        Assert.Equal((Path.Join(_left, "CHANGELOG.md"), Path.Join(_right, "CHANGELOG.md")), w.FilesToCompare());

        w.CompareHideButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.Null(w.LeftPanel.Panel.CompareMarks);
        Assert.False(w.CompareBar.IsVisible);
        Assert.Equal(["CHANGELOG.md", "drafts", "notes.txt"], Selected(w.LeftPanel));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task The_marks_go_when_a_panel_shows_another_folder_or_the_panels_swap()
    {
        var w = await Open();
        (await Compare(w)).Dialog.Accept();
        await Until(() => w.Comparison is not null);
        await w.RightPanel.Panel.RefreshAsync();
        Assert.NotNull(w.LeftPanel.Panel.CompareMarks);

        await w.RightPanel.Panel.NavigateAsync(_dir);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(w.LeftPanel.Panel.CompareMarks);
        Assert.Null(w.RightPanel.Panel.CompareMarks);
        Assert.False(w.CompareBar!.IsVisible);

        await w.RightPanel.Panel.NavigateAsync(_right);
        (await Compare(w)).Dialog.Accept();
        await Until(() => w.Comparison is not null);
        w.SwapPanels();
        Assert.Null(w.LeftPanel.Panel.CompareMarks);
        Assert.False(w.CompareBar.IsVisible);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Content_and_subfolders_and_save_as_my_defaults()
    {
        Directory.CreateDirectory(Path.Join(_left, "src"));
        Directory.CreateDirectory(Path.Join(_right, "src"));
        Write(Path.Join(_left, "src"), "app.cs", "abcd", Noon);
        Write(Path.Join(_right, "src"), "app.cs", "abce", Noon);
        var w = await Open();
        var run = await Compare(w);
        run.Dialog.NewerBox.IsChecked = false;
        run.Dialog.BiggerBox.IsChecked = false;
        run.Dialog.ContentBox.IsChecked = true;
        run.Dialog.SubfoldersBox.IsChecked = true;
        run.Dialog.SaveDefaultsBox.IsChecked = true;
        run.Dialog.Accept();
        await Until(() => w.Comparison is not null);

        Assert.Equal(DifferenceKind.Different, w.LeftPanel.Panel.CompareMarks!["src"]);
        Assert.Equal(DifferenceKind.Different, w.LeftPanel.Panel.CompareMarks["CHANGELOG.md"]);
        Assert.Contains("src", Selected(w.RightPanel));
        Assert.Contains("≠ 2 different", w.CompareBarText, StringComparison.Ordinal);
        var saved = w.Services.Settings.Current.Compare.Directories;
        Assert.True(saved.Content && saved.Subfolders && !saved.Newer);

        var again = await Compare(w);
        Assert.True(again.Dialog.ContentBox.IsChecked);
        again.Dialog.Close();
        await Until(() => w.CompareDialog is null);
        w.CloseNow();
    }

    private static void Click(Avalonia.Controls.Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(DifferencesWindow list, Key key, KeyModifiers mods = KeyModifiers.None)
    {
        list.List.RaiseEvent(new KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = list.List });
        Dispatcher.UIThread.RunJobs();
    }

    private static string[] RowPaths(DifferencesWindow list) => [.. list.Rows.Select(r => r.Node.Path)];

    private static string FocusedName(PanelHost host) => host.Panel.FocusedEntry?.Name ?? "";

    [AvaloniaFact]
    public async Task Two_folders_selected_in_a_panel_are_compared_in_the_list_only()
    {
        var w = await Open();
        await w.LeftPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.Panel.SelectWhere(e => e.IsDirectory, select: true);
        var run = await Compare(w);
        Assert.Equal(Path.Join(_dir, "Backup"), run.Dialog.LeftBox.Text);
        Assert.Equal(Path.Join(_dir, "Website"), run.Dialog.RightBox.Text);
        Assert.Equal("Filled in from the two folders selected in the left panel.", run.Dialog.From.Text);
        Assert.False(run.Dialog.SelectBox.IsEnabled);
        Assert.False(run.Dialog.SelectBox.IsChecked);
        Assert.True(run.Dialog.ListBox.IsChecked);
        Assert.False(run.Dialog.ListBox.IsEnabled);
        run.Dialog.Accept();
        await Until(() => w.DifferencesList?.Rows.Count > 0);
        var list = w.DifferencesList!;
        Assert.Equal(Path.Join(_dir, "Backup"), list.LeftRoot);
        Assert.Null(w.Comparison);
        Assert.Null(w.LeftPanel.Panel.CompareMarks);
        Assert.False(list.SelectInPanelsButton.IsVisible);
        Assert.Equal(["drafts", "old", "CHANGELOG.md", "notes.txt", "sitemap.xml"], RowPaths(list));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task One_folder_selected_in_each_panel_compares_those_two()
    {
        Directory.CreateDirectory(Path.Join(_left, "src"));
        Directory.CreateDirectory(Path.Join(_right, "src"));
        var w = await Open();
        w.LeftPanel.Panel.SelectWhere(e => e.Name == "src", select: true);
        w.RightPanel.Panel.SelectWhere(e => e.Name == "src", select: true);
        var run = await Compare(w);
        Assert.Equal(Path.Join(_left, "src"), run.Dialog.LeftBox.Text);
        Assert.Equal(Path.Join(_right, "src"), run.Dialog.RightBox.Text);
        Assert.Equal("Filled in from the folder selected in each panel.", run.Dialog.From.Text);
        run.Dialog.Close();
        await Until(() => w.CompareDialog is null);

        w.RightPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        run = await Compare(w);
        Assert.Equal(Path.Join(_left, "src"), run.Dialog.LeftBox.Text);
        Assert.Equal(Path.Join(_right, "src"), run.Dialog.RightBox.Text);
        run.Dialog.Close();
        await Until(() => w.CompareDialog is null);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task One_folder_selected_goes_with_the_other_panels_folder_and_the_dialog_checks_the_folders()
    {
        var w = await Open();
        await w.RightPanel.Panel.NavigateAsync(_dir);
        w.RightPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        w.RightPanel.Panel.SelectWhere(e => e.Name == "Backup", select: true);
        var run = await Compare(w);
        Assert.Equal(_left, run.Dialog.LeftBox.Text);
        Assert.Equal(_right, run.Dialog.RightBox.Text);
        Assert.Equal("Filled in from the left panel's folder and the folder selected in the right panel.", run.Dialog.From.Text);

        run.Dialog.Swap();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(_right, run.Dialog.LeftBox.Text);
        run.Dialog.RightBox.Text = Path.Join(_dir, "nothing-here");
        Dispatcher.UIThread.RunJobs();
        run.Dialog.Accept();
        Assert.Equal($"There is no folder {Path.Join(_dir, "nothing-here")}.", run.Dialog.Problem);
        run.Dialog.RightBox.Text = _right + "\\";
        Dispatcher.UIThread.RunJobs();
        run.Dialog.Accept();
        Assert.Equal("The two folders are the same.", run.Dialog.Problem);
        run.Dialog.Close();
        await Until(() => w.CompareDialog is null);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task The_list_side_by_side_filters_expands_and_shows_a_row_in_the_panels()
    {
        Directory.CreateDirectory(Path.Join(_left, "src"));
        Directory.CreateDirectory(Path.Join(_right, "src"));
        Write(Path.Join(_left, "src"), "app.cs", "new code", Noon.AddDays(1));
        Write(Path.Join(_right, "src"), "app.cs", "old", Noon);
        Write(Path.Join(_left, "src"), "same.cs", "x", Noon);
        Write(Path.Join(_right, "src"), "same.cs", "x", Noon);
        Write(Path.Join(_left, "drafts"), "post.md", "p", Noon);
        var w = await Open();
        var run = await Compare(w);
        run.Dialog.SubfoldersBox.IsChecked = true;
        run.Dialog.Accept();
        await Until(() => w.Comparison is not null && w.DifferencesList?.Rows.Count > 0);
        var list = w.DifferencesList!;
        Assert.Equal(["drafts", "old", "src", "CHANGELOG.md", "notes.txt", "sitemap.xml"], RowPaths(list));
        Assert.Equal("7 differences", list.CountText);
        Assert.True(list.SelectInPanelsButton.IsVisible);

        Assert.Equal("▸ Expand all", list.ExpandButton.Content);
        Click(list.ExpandButton);
        Assert.Equal(["drafts", @"drafts\post.md", "old", "src", @"src\app.cs", "CHANGELOG.md", "notes.txt", "sitemap.xml"], RowPaths(list));
        Assert.Equal("▾ Collapse all", list.ExpandButton.Content);
        Click(list.ExpandButton);
        Assert.Equal(6, list.Rows.Count);

        Click(list.SameChip);
        Assert.Equal(["src", "same.txt"], RowPaths(list));
        Assert.Equal("= Same 2", list.SameChip.Content);
        Click(list.DifferentChip);

        list.List.SelectedIndex = 2;
        Press(list, Key.Right);
        Assert.Equal(@"src\app.cs", list.Rows[3].Node.Path);
        Press(list, Key.Right);
        Assert.Equal(@"src\app.cs", list.Focused!.Node.Path);
        Assert.Equal("Compare the two files", list.EnterText);
        Press(list, Key.Enter);
        await Until(() => w.FileCompare is not null);
        Assert.Equal((Path.Join(_left, "src", "app.cs"), Path.Join(_right, "src", "app.cs")), (w.FileCompare!.LeftPath, w.FileCompare.RightPath));
        w.FileCompare.Close();
        Press(list, Key.Left);
        Assert.Equal("src", list.Focused!.Node.Path);
        Press(list, Key.Left);
        Assert.Equal(6, list.Rows.Count);

        list.ExpandOrCollapseAll();
        list.List.SelectedItem = list.Rows.Single(r => r.Node.Path == @"src\app.cs");
        Press(list, Key.J, KeyModifiers.Control);
        await Until(() => w.ComparedLevel == "src");
        Assert.Equal(Path.Join(_left, "src"), w.LeftPanel.Panel.Path);
        Assert.Equal(Path.Join(_right, "src"), w.RightPanel.Panel.Path);
        Assert.Equal("app.cs", FocusedName(w.LeftPanel));
        Assert.Equal("app.cs", FocusedName(w.RightPanel));
        Assert.Equal(DifferenceKind.NewerOrBigger, w.LeftPanel.Panel.CompareMarks!["app.cs"]);
        Assert.Equal("Compared: ▲ 1 newer or bigger", w.CompareBarText);

        list.List.SelectedItem = list.Rows.Single(r => r.Node.Path == @"drafts\post.md");
        Press(list, Key.J, KeyModifiers.Control);
        await Until(() => w.LeftPanel.Panel.Path == Path.Join(_left, "drafts") && w.RightPanel.Panel.Path == _right);
        Assert.Equal("post.md", FocusedName(w.LeftPanel));
        Assert.Null(w.Comparison);

        await w.LeftPanel.Panel.NavigateAsync(_left);
        list.UpdatePanelsButton();
        Assert.True(list.SelectInPanelsButton.IsVisible);
        Click(list.SelectInPanelsButton);
        Assert.Equal(["CHANGELOG.md", "drafts", "notes.txt", "src"], Selected(w.LeftPanel));
        Assert.Equal("", w.ComparedLevel);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task The_bar_opens_the_list_again_and_refreshes()
    {
        var w = await Open();
        (await Compare(w)).Dialog.Accept();
        await Until(() => w.Comparison is not null && w.DifferencesList is not null);
        w.DifferencesList!.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(w.DifferencesList);
        Click(w.CompareListButton);
        Assert.NotNull(w.DifferencesList);
        Assert.Equal(5, w.DifferencesList!.Rows.Count);

        Write(_right, "notes.txt", "n", Noon);
        Click(w.CompareRefreshButton);
        await w.Comparing!;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Compared: ← 1 only here  ·  → 2 only there  ·  ▲ 1 newer or bigger  ·  1 time close enough counted as the same", w.CompareBarText);
        Assert.Equal(4, w.DifferencesList!.Rows.Count);
        Assert.Equal(["CHANGELOG.md", "drafts"], Selected(w.LeftPanel));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Without_the_list_and_the_marks_in_Options_only_the_selection_shows_them()
    {
        var w = await Open();
        w.Services.Settings.Update(s => s with { Compare = s.Compare with { ShowList = false, ShowMarks = false } });
        var run = await Compare(w);
        Assert.False(run.Dialog.ListBox.IsChecked);
        run.Dialog.Accept();
        await Until(() => w.Comparison is not null);
        Assert.Null(w.DifferencesList);
        Assert.Null(w.LeftPanel.Panel.CompareMarks);
        Assert.Equal(["CHANGELOG.md", "drafts", "notes.txt"], Selected(w.LeftPanel));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Different_has_ten_kinds_with_their_keys_and_the_names_filter_has_not_and_clear()
    {
        Write(Path.Join(_left, "drafts"), "post.md", "p", Noon);
        Write(_left, "index.html", "<p>", Noon);
        Write(_right, "index.html", "<p>", Noon);
        var w = await Open();
        var run = await Compare(w);
        run.Dialog.SubfoldersBox.IsChecked = true;
        run.Dialog.Accept();
        await Until(() => w.DifferencesList?.Rows.Count > 0);
        var list = w.DifferencesList!;
        Assert.Equal("All 8", list.AllChip.Content);
        Assert.Equal("≠ Differences 6", list.DifferentChip.Content);
        Assert.Equal("= Same 2", list.SameChip.Content);

        var menu = list.OpenKindMenu();
        Dispatcher.UIThread.RunJobs();
        Assert.True(menu.IsOpen);
        var items = menu.Items.OfType<Avalonia.Controls.MenuItem>().ToList();
        Assert.Equal(10, items.Count);
        Assert.Equal("Ctrl+0", items[^1].InputGesture?.ToString("p", null));
        items.Single(i => Equals(i.Tag, DifferenceFilter.RightOrphans)).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("≠ Right orphans 2", list.DifferentChip.Content);
        Assert.Equal(["old", "sitemap.xml"], RowPaths(list));
        menu.Hide();

        Press(list, Key.D9, KeyModifiers.Control);
        Assert.Equal("≠ Left orphans 3", list.DifferentChip.Content);
        Assert.Equal(["drafts", "notes.txt"], RowPaths(list));
        Press(list, Key.D0, KeyModifiers.Control);
        Assert.Equal(["old", "sitemap.xml"], RowPaths(list));
        Press(list, Key.D5, KeyModifiers.Control);
        Assert.Equal(["CHANGELOG.md"], RowPaths(list));
        Press(list, Key.D3, KeyModifiers.Control);
        Assert.Equal(["CHANGELOG.md"], RowPaths(list));
        Press(list, Key.D2, KeyModifiers.Control);
        Assert.Equal(["CHANGELOG.md", "index.html", "same.txt"], RowPaths(list));
        Click(list.AllChip);
        Assert.Equal(["drafts", "old", "CHANGELOG.md", "index.html", "notes.txt", "same.txt", "sitemap.xml"], RowPaths(list));

        list.NamesBox.Text = "*.md";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["drafts", "CHANGELOG.md"], RowPaths(list));
        Assert.Equal("All 2", list.AllChip.Content);
        Assert.True(list.ClearNamesButton.IsVisible);
        Click(list.NotChip);
        Assert.Equal(["index.html", "notes.txt", "same.txt", "sitemap.xml"], RowPaths(list));
        Click(list.ClearNamesButton);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", list.NamesBox.Text);
        Assert.False(list.ClearNamesButton.IsVisible);
        Assert.Equal(7, list.Rows.Count);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task The_list_starts_as_Options_says()
    {
        var w = await Open();
        w.Services.Settings.Update(s => s with { Compare = s.Compare with { ListView = CompareView.Different, ListKind = DifferenceFilter.RightOrphans, ListNames = "*.xml" } });
        (await Compare(w)).Dialog.Accept();
        await Until(() => w.DifferencesList?.Rows.Count > 0);
        var list = w.DifferencesList!;
        Assert.Equal("≠ Right orphans 1", list.DifferentChip.Content);
        Assert.Equal("*.xml", list.NamesBox.Text);
        Assert.Equal(["sitemap.xml"], RowPaths(list));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Remembering_the_list_view_opens_it_as_you_left_it()
    {
        var w = await Open();
        w.Services.Settings.Update(s => s with { Compare = s.Compare with { ListRemember = true } });
        (await Compare(w)).Dialog.Accept();
        await Until(() => w.DifferencesList?.Rows.Count > 0);
        var list = w.DifferencesList!;
        list.SetKind(DifferenceFilter.LeftOrphans);
        list.NamesBox.Text = "*.txt";
        Dispatcher.UIThread.RunJobs();
        list.Close();
        Dispatcher.UIThread.RunJobs();
        var saved = w.Services.Settings.Current.Compare;
        Assert.Equal((CompareView.Different, DifferenceFilter.LeftOrphans, "*.txt"), (saved.ListView, saved.ListKind, saved.ListNames));

        Click(w.CompareListButton);
        list = w.DifferencesList!;
        Assert.Equal("≠ Left orphans 1", list.DifferentChip.Content);
        Assert.Equal(["notes.txt"], RowPaths(list));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task The_arrow_keys_stay_in_the_list_CtrlShiftE_expands_all_and_CtrlShiftO_opens_Options()
    {
        Write(Path.Join(_left, "drafts"), "post.md", "p", Noon);
        var w = await Open();
        var run = await Compare(w);
        run.Dialog.SubfoldersBox.IsChecked = true;
        run.Dialog.Accept();
        await Until(() => w.DifferencesList?.Rows.Count > 0);
        var list = w.DifferencesList!;
        Assert.False(list.ExpandButton.Focusable || list.AllChip.Focusable || list.KindArrow.Focusable);
        list.List.SelectedIndex = 0;
        Press(list, Key.Down);
        Press(list, Key.Down);
        Assert.Equal(2, list.List.SelectedIndex);
        Press(list, Key.Right);
        Assert.Equal(2, list.List.SelectedIndex);
        Press(list, Key.End);
        Assert.Equal(list.Rows.Count - 1, list.List.SelectedIndex);
        Press(list, Key.Home);
        Press(list, Key.Up);
        Assert.Equal(0, list.List.SelectedIndex);

        Press(list, Key.E, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal("▾ Collapse all", list.ExpandButton.Content);
        Press(list, Key.E, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal("▸ Expand all", list.ExpandButton.Content);
        Press(list, Key.O, KeyModifiers.Control | KeyModifiers.Shift);
        await Until(() => w.OpenOptions is not null);
        w.CloseNow();
    }

    [Fact]
    public void Each_mark_says_what_it_means_and_up_on_both_sides_is_one_newer_and_the_other_bigger()
    {
        var older = new Salandual.Core.Vfs.FileEntry("a.txt", 900, Noon.Ticks, FileAttributes.Archive);
        var newer = new Salandual.Core.Vfs.FileEntry("a.txt", 100, Noon.AddDays(1).Ticks, FileAttributes.Archive);
        var both = new CompareDifference("", "a.txt", false, older, newer, DifferenceKind.NewerOrBigger, CompareSide.Both);
        Assert.Equal("▲", DifferencesWindow.Mark(both, CompareSide.Left));
        Assert.Equal("Bigger than the other side", DifferencesWindow.MarkTip(both, CompareSide.Left));
        Assert.Equal("Newer than the other side", DifferencesWindow.MarkTip(both, CompareSide.Right));
        var orphan = new CompareDifference("", "x", false, older, null, DifferenceKind.OnlyLeft, CompareSide.Left);
        Assert.Equal("Only on the left: the right side hasn't it", DifferencesWindow.MarkTip(orphan, CompareSide.Left));
        Assert.Null(DifferencesWindow.MarkTip(orphan, CompareSide.Right));
    }

    [AvaloniaFact]
    public async Task Both_panels_need_a_folder_of_their_own()
    {
        var w = await Open();
        await w.RightPanel.Panel.NavigateAsync(_left);
        Assert.True(w.Run("Compare.Directories", Salandual.Core.Commands.PanelTarget.Active));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(w.CompareDialog);
        Assert.Equal("Both panels show the same folder.", w.NoticeMessage);
        w.CloseNow();
    }
}
