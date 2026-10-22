// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/FindStep1bTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Find;
using Salandual.Core.Search;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Tests;

public sealed class FindStep1bTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-find1b-").FullName;
    private readonly List<string> _calls = [];
    private readonly List<(IReadOnlyList<SearchHit> Hits, string What)> _shown = [];

    public FindStep1bTests()
    {
        Write(@"a\photo.jpg", "the same bytes");
        Write(@"b\IMG_1.jpg", "the same bytes");
        Write(@"b\other.jpg", "other bytes!!!");
        Write(@"a\notes.txt", "short");
        Write(@"b\notes.txt", "a longer note");
        File.SetLastWriteTimeUtc(Path.Join(_dir, @"a\photo.jpg"), new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Join(_dir, @"b\IMG_1.jpg"), new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Join(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private FindWindow Open(SettingsService settings, bool duplicates = false)
    {
        var request = new FindRequest(_dir)
        {
            Files = new LocalFileSystemProvider(),
            Delete = (hits, _, _) =>
            {
                _calls.Add("delete " + string.Join(",", hits.Select(h => h.Name)));
                return Task.FromResult<Salandual.Core.Operations.OperationJob?>(null);
            },
            OpenOptions = category => _calls.Add("options " + category),
            OpenDuplicates = (named, lookIn) => _calls.Add($"duplicates {named} {lookIn}"),
            ShowResults = (hits, what) => _shown.Add((hits, what)),
        };
        var window = new FindWindow(settings, request, duplicates: duplicates) { Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Press(Control target, Key key, KeyModifiers mods = KeyModifiers.None)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = target });
        Dispatcher.UIThread.RunJobs();
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

    [AvaloniaFact]
    public async Task A_search_is_saved_with_a_name_and_where_it_looks_and_comes_back_with_its_boxes()
    {
        var settings = AppServices.CreateIsolated().Settings;
        var w = Open(settings);
        Assert.NotNull(w.SavedButton);
        w.NamedBox.Text = "*.jpg";
        w.TextRows[0].Box.Text = "same";
        w.MatchCaseBox.IsChecked = true;

        Press(w, Key.D, KeyModifiers.Control);
        var dialog = w.SaveDialog!;
        Assert.Equal("*.jpg with “same”", dialog.NameBox.Text);
        Assert.True(dialog.KeepsFolders);
        dialog.NameBox.Text = "Photos";
        dialog.Accept();
        await Until(() => settings.Current.Find.Saved.Count == 1);
        var saved = settings.Current.Find.Saved[0];
        Assert.Equal("Photos", saved.Name);
        Assert.Equal(_dir, saved.Search.LookIn);
        Assert.True(saved.Search.MatchCase);

        w.NamedBox.Text = "*.txt";
        var saving = w.SaveSearchAsync();
        Dispatcher.UIThread.RunJobs();
        w.SaveDialog!.NameBox.Text = "Texts";
        w.SaveDialog.ActivePanel!.IsChecked = true;
        w.SaveDialog.Accept();
        await saving;
        Assert.Equal(["Photos", "Texts"], settings.Current.Find.Saved.Select(s => s.Name));
        Assert.Equal("", settings.Current.Find.Saved[1].Search.LookIn);

        w.NamedBox.Text = "*.*";
        w.TextRows[0].Box.Text = "";
        w.MatchCaseBox.IsChecked = false;
        Press(w, Key.D1, KeyModifiers.Control);
        Assert.Equal("*.jpg", w.NamedBox.Text);
        Assert.Equal("same", w.TextRows[0].Text);
        Assert.True(w.MatchCaseBox.IsChecked);
        Assert.False(w.IsSearching);
        Assert.Empty(w.Shown);

        var flyout = (MenuFlyout)w.SavedButton!.Flyout!;
        flyout.ShowAt(w.SavedButton);
        Dispatcher.UIThread.RunJobs();
        var items = flyout.Items.OfType<MenuItem>().Where(i => i.Tag is string).ToList();
        Assert.Equal(["Photos", "Texts"], items.Select(i => (string)i.Tag!));
        Assert.Equal(new KeyGesture(Key.D2, KeyModifiers.Control), items[1].InputGesture);
        Assert.Contains("The active panel's folder", FindWindow.SavedText(settings.Current.Find.Saved[1].Search), StringComparison.Ordinal);
        flyout.Hide();

        FindWindow.RenameSaved(settings, "Texts", "Notes");
        FindWindow.RemoveSaved(settings, "Photos");
        Assert.Equal(["Notes"], settings.Current.Find.Saved.Select(s => s.Name));
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_size_from_bigger_than_to_is_marked_and_neither_searched_nor_saved()
    {
        var settings = AppServices.CreateIsolated().Settings;
        var w = Open(settings);
        w.MinSizeBox.Text = "5";
        w.MaxSizeBox.Text = "2";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("error", w.MinSizeBox.Classes);
        Assert.Contains("error", w.MaxSizeBox.Classes);

        await w.StartAsync(ResultMode.New);
        Assert.True(w.Problem.IsVisible);
        Assert.Equal("Size: the first value is bigger than the second.", w.Problem.Text);
        Assert.Empty(w.Shown);
        await w.SaveSearchAsync();
        Assert.Null(w.SaveDialog);
        Assert.Empty(settings.Current.Find.Saved);

        w.MaxSizeBox.Text = "x";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", w.MaxSizeBox.Text);
        Assert.Null(w.SizeProblem());
        w.MaxSizeBox.Text = "12a3456789";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("123456", w.MaxSizeBox.Text);
        Assert.Equal(6, w.MinSizeBox.MaxLength);
        w.MaxSizeBox.Text = "5";
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("error", w.MaxSizeBox.Classes);
        Assert.Null(w.SizeProblem());

        var clear = w.ClearAdvancedButton;
        Assert.True(clear.IsVisible);
        Assert.Contains("clearadv", clear.Classes);
        var cross = Assert.IsType<Avalonia.Controls.Shapes.Path>(clear.Content);
        var middle = Avalonia.VisualExtensions.TranslatePoint(cross, new Avalonia.Point(cross.Bounds.Width / 2, cross.Bounds.Height / 2), clear)!.Value;
        Assert.Equal(clear.Bounds.Width / 2, middle.X, 0.5);
        Assert.Equal(clear.Bounds.Height / 2, middle.Y, 0.5);
        Assert.True(w.TryFindResource("Brush.Danger", w.ActualThemeVariant, out var danger));
        Assert.Equal(((Avalonia.Media.ISolidColorBrush)danger!).Color, ((Avalonia.Media.ISolidColorBrush)cross.Stroke!).Color);

        w.AdvancedOpen = true;
        w.ModifiedBox.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var days = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w.WithinCount).OfType<TextBox>().Single();
        Assert.Equal(3, days.MaxLength);
        days.Text = "12x45";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("124", days.Text);
        Assert.Equal(999, w.WithinCount.Maximum);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Dates_from_later_than_to_are_refused_and_both_calendars_tint_the_days_between()
    {
        var settings = AppServices.CreateIsolated().Settings;
        var w = Open(settings);
        w.AdvancedOpen = true;
        w.ModifiedBox.SelectedIndex = 2;
        var day = new DateTime(2026, 10, 10);
        w.FromDate.SelectedDate = day;
        w.ToDate.SelectedDate = day.AddDays(-4);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Modified: the first date is later than the second.", w.AdvancedProblem());
        Assert.Contains("error", w.FromDate.Classes);
        await w.StartAsync(ResultMode.New);
        Assert.Equal("Modified: the first date is later than the second.", w.Problem.Text);
        Assert.Empty(w.Shown);

        w.ToDate.SelectedDate = day.AddDays(8);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(w.AdvancedProblem());
        Assert.DoesNotContain("error", w.FromDate.Classes);
        foreach (var picker in new[] { w.FromDate, w.ToDate })
        {
            picker.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var calendar = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(picker).OfType<Avalonia.Controls.Primitives.Popup>().Single().Child!;
            var days = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(calendar).OfType<Avalonia.Controls.Primitives.CalendarDayButton>().ToList();
            var tinted = days.Where(d => d.Classes.Contains("inrange")).Select(d => (DateTime)d.DataContext!).ToList();
            Assert.Equal(Enumerable.Range(0, 9).Select(i => day.AddDays(i)), tinted);
            Assert.Equal([day, day.AddDays(8)], days.Where(d => d.Classes.Contains("rangeend")).Select(d => (DateTime)d.DataContext!));

            var range = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(w.Resources["Brush.FindRange"]);
            Assert.True(range.Opacity is > 0.4 and < 0.8);
            picker.IsDropDownOpen = false;
        }

        w.Close();
    }

    [AvaloniaFact]
    public void Select_in_Find_duplicates_shows_its_items()
    {
        var w = Open(AppServices.CreateIsolated().Settings, duplicates: true);
        var flyout = (MenuFlyout)w.SelectButton!.Flyout!;
        flyout.ShowAt(w.SelectButton);
        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(flyout.Items.OfType<MenuItem>());
        Assert.All(flyout.Items.OfType<MenuItem>(), i => Assert.NotNull(TopLevel.GetTopLevel(i)));
        flyout.Hide();
        w.Close();
    }

    [AvaloniaFact]
    public void With_nothing_saved_the_list_still_opens_and_says_so_with_Save_this_search()
    {
        var settings = AppServices.CreateIsolated().Settings;
        var w = Open(settings);
        var flyout = (MenuFlyout)w.SavedButton!.Flyout!;
        flyout.ShowAt(w.SavedButton);
        Dispatcher.UIThread.RunJobs();
        Assert.True(flyout.IsOpen);
        Assert.All(flyout.Items.OfType<MenuItem>(), i => Assert.NotNull(TopLevel.GetTopLevel(i)));
        Assert.Contains(flyout.Items.OfType<MenuItem>(), i => i.Header is TextBlock { Text: "No saved searches yet" });
        Assert.Contains(flyout.Items.OfType<MenuItem>(), i => i.Header is string s && s.StartsWith("Save this search", StringComparison.Ordinal));
        flyout.Hide();
        w.Close();
    }

    [AvaloniaFact]
    public async Task Find_duplicates_groups_by_what_is_lit_selects_all_but_the_newest_and_deletes_them()
    {
        var settings = AppServices.CreateIsolated().Settings;
        var w = Open(settings, duplicates: true);
        Assert.True(w.FindsDuplicates);
        Assert.Null(w.SavedButton);
        Assert.StartsWith("Find duplicates – ", w.Title);
        Assert.True(w.SameButtons[Salandual.Core.Settings.DuplicateKeys.Size].IsChecked);
        Assert.True(w.SameButtons[Salandual.Core.Settings.DuplicateKeys.Content].IsChecked);
        Assert.False(w.SameButtons[Salandual.Core.Settings.DuplicateKeys.Name].IsChecked);

        await w.StartAsync(ResultMode.New);
        var group = Assert.Single(w.Groups);
        Assert.Equal(["photo.jpg", "IMG_1.jpg"], group.Files.Select(f => f.Name));
        Assert.Equal("2 files · 14 B each · same content", w.GroupText(group));
        Assert.StartsWith("2 files in 1 groups", w.HeaderText, StringComparison.Ordinal);

        w.SelectAllBut(DuplicateKeep.Newest);
        Assert.Equal(["IMG_1.jpg"], w.ResultList.SelectedItems!.OfType<SearchHit>().Select(h => h.Name));
        Press(w.ResultList, Key.F8);
        Assert.Contains("delete IMG_1.jpg", _calls);

        w.SameButtons[Salandual.Core.Settings.DuplicateKeys.Size].IsChecked = false;
        w.SameButtons[Salandual.Core.Settings.DuplicateKeys.Content].IsChecked = false;
        w.SameButtons[Salandual.Core.Settings.DuplicateKeys.Name].IsChecked = true;
        await w.StartAsync(ResultMode.New);
        Assert.Equal("notes.txt · 2 files · same name", w.GroupText(Assert.Single(w.Groups)));
        Assert.Equal(Salandual.Core.Settings.DuplicateKeys.Name, settings.Current.Find.DuplicateKeys);
        var before = w.Shown.ToList();
        w.Sort(ResultColumn.Size, toggle: true);
        Assert.Equal(before, w.Shown);

        w.SameButtons[Salandual.Core.Settings.DuplicateKeys.Name].IsChecked = false;
        await w.StartAsync(ResultMode.New);
        Assert.True(w.Problem.IsVisible);

        w.SameButtons[Salandual.Core.Settings.DuplicateKeys.Size].IsChecked = true;
        await w.StartAsync(ResultMode.New);
        Press(w, Key.P, KeyModifiers.Control);
        var (hits, what) = Assert.Single(_shown);
        Assert.Equal(w.Shown.Count, hits.Count);
        Assert.StartsWith("duplicates in ", what, StringComparison.Ordinal);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Search_offers_Find_duplicates_with_its_boxes_and_shows_its_results_in_a_panel()
    {
        var w = Open(AppServices.CreateIsolated().Settings);
        w.NamedBox.Text = "*.jpg";
        await w.StartAsync(ResultMode.New);
        Assert.Equal(3, w.Shown.Count);
        Assert.True(w.ShowInPanelButton.IsEnabled);
        w.ShowResultsInPanel();
        Assert.Equal("*.jpg", Assert.Single(_shown).What);
        Assert.Equal(3, _shown[0].Hits.Count);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Show_in_a_panel_lists_the_results_with_their_folder_and_Back_returns()
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.Show();
        var settings = w.Services.Settings;
        await w.LeftPanel.Panel.NavigateAsync(_dir, "b");
        await w.RightPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        var hits = new[]
        {
            new SearchHit(Path.Join(_dir, "a"), "photo.jpg", 14, 0, FileAttributes.Archive),
            new SearchHit(Path.Join(_dir, "b"), "IMG_1.jpg", 14, 0, FileAttributes.Archive),
        };

        var host = w.ShowResultsInPanel(hits, "*.jpg", keepOrder: false);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(w.LeftPanel, host);
        Assert.Same(w.LeftPanel, w.ActivePanel);
        Assert.Equal(_dir, host.Panel.Path);
        Assert.Equal([@"b\IMG_1.jpg", @"a\photo.jpg"], host.Panel.Items.Select(e => e.Name));
        Assert.True(host.ResultsBar!.IsVisible);
        Assert.Equal("Search's results · *.jpg · 2 items", host.ResultsText);

        File.Delete(Path.Join(_dir, @"b\IMG_1.jpg"));
        await host.Panel.RefreshAsync();
        Assert.Equal([@"a\photo.jpg"], host.Panel.Items.Select(e => e.Name));

        host.ResultsBack!.Command?.Execute(null);
        host.ResultsBack.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Until(() => host.Panel.Results is null);
        Assert.False(host.ResultsBar.IsVisible);
        Assert.Equal("b", host.Panel.FocusedEntry?.Name);
        Assert.Same(w.LeftPanel, w.ActivePanel);

        settings.Update(s => s with { Find = s.Find with { ResultsPanel = ResultsPanel.Other } });
        try
        {
            Assert.Same(w.RightPanel, w.ShowResultsInPanel(hits.Take(1).ToList(), "x", keepOrder: false));
            Assert.NotNull(w.RightPanel.Panel.Results);
        }
        finally
        {
            settings.Update(s => s with { Find = s.Find with { ResultsPanel = ResultsPanel.Active } });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Jump_to_the_file_takes_the_panel_to_the_file_under_the_cursor_in_its_folder()
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir, "b");
        await w.RightPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        var hits = new[]
        {
            new SearchHit(Path.Join(_dir, "a"), "photo.jpg", 14, 0, FileAttributes.Archive),
            new SearchHit(Path.Join(_dir, "b"), "IMG_1.jpg", 14, 0, FileAttributes.Archive),
        };
        var host = w.ShowResultsInPanel(hits, "*.jpg", keepOrder: false);
        Dispatcher.UIThread.RunJobs();
        Assert.True(host.ResultsJump!.IsEnabled);
        Assert.Equal("↗ Jump to the file", host.ResultsJump.Content);

        var panel = host.Panel;
        panel.Toggle(0);
        panel.Toggle(1);
        panel.SetFocus(1);
        Press(host.View, Key.F10, KeyModifiers.Shift);
        Assert.Contains(w.QuickMenu!.Items.OfType<MenuItem>(), i => i.Header is string s && s.Replace("_", "", StringComparison.Ordinal) == "Jump to the file");
        w.QuickMenu.Close();
        Press(host.View, Key.J, KeyModifiers.Control);
        await Until(() => panel.Results is null);
        Assert.Equal(Path.Join(_dir, "a"), panel.Path);
        Assert.Equal("photo.jpg", panel.FocusedEntry?.Name);
        Assert.False(host.ResultsBar!.IsVisible);

        host = w.ShowResultsInPanel(hits, "*.jpg", keepOrder: false);
        Dispatcher.UIThread.RunJobs();
        host.ResultsJump!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Until(() => host.Panel.Results is null);
        Assert.Equal(Path.Join(_dir, "b"), host.Panel.Path);
        Assert.Equal("IMG_1.jpg", host.Panel.FocusedEntry?.Name);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Alt_Shift_F7_opens_Find_duplicates_and_Commands_lists_the_saved_searches()
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        var settings = w.Services.Settings;
        settings.Update(s => s with { Find = s.Find with { Saved = [new SavedSearch("Photos", new SearchDefaults { Named = "*.jpg" })] } });
        try
        {
            w.KeyPress(Key.F7, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.None, null);
            w.KeyRelease(Key.F7, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            var duplicates = Assert.Single(w.FindWindows);
            Assert.True(duplicates.FindsDuplicates);
            Assert.Equal(_dir, duplicates.LookInBox.Text);

            var search = w.OpenSavedSearch("Photos");
            Assert.False(search.FindsDuplicates);
            Assert.Equal("*.jpg", search.NamedBox.Text);
            Assert.Equal(_dir, search.LookInBox.Text);
            Assert.Contains(Salandual.Core.Commands.MainMenu.Create().Single(m => m.Header == "_Commands").Children!, n => n.Dynamic == "Find.Saved");
            search.Close();
            duplicates.Close();
        }
        finally
        {
            settings.Update(s => s with { Find = s.Find with { Saved = [] } });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task The_viewer_and_the_editor_offer_the_pieces_while_regex_is_lit()
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(Path.Join(_dir, "a"), "notes.txt");
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();

        w.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.None, null);
        w.KeyRelease(Key.F3, RawInputModifiers.None, PhysicalKey.None, null);
        await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Document is not null);
        var viewer = w.Viewers[^1];
        Assert.False(viewer.FindPieces.IsVisible);
        viewer.RegexFlag.IsChecked = true;
        Assert.True(viewer.FindPieces.IsVisible);
        var pieces = (MenuFlyout)viewer.FindPieces.Flyout!;
        viewer.FindBox.Text = "ab";
        viewer.FindBox.SelectionStart = viewer.FindBox.SelectionEnd = viewer.FindBox.CaretIndex = 2;
        ((MenuItem)pieces.Items.OfType<MenuItem>().First(i => i.Tag is RegexPiece { Pattern: "+" })).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("ab+", viewer.FindBox.Text);
        viewer.FindBox.Text = @"^S\w{3}T$";
        await viewer.FindAsync(forward: true, from: 0);
        await Until(() => viewer.HitsText == "1 of 1");
        Assert.Equal("short", viewer.Document!.Text(viewer.Current.SelectionStart, viewer.Current.SelectionEnd));
        viewer.Close();

        w.KeyPress(Key.F4, RawInputModifiers.None, PhysicalKey.None, null);
        w.KeyRelease(Key.F4, RawInputModifiers.None, PhysicalKey.None, null);
        await Until(() => w.Editors.Count > 0 && w.Editors[^1].IsLoaded);
        var editor = w.Editors[^1];
        await editor.Loading;
        Assert.False(editor.ReplacePieces.IsVisible);
        editor.RegexFlag.IsChecked = true;
        Assert.True(editor.FindPieces.IsVisible);
        Assert.True(editor.ReplacePieces.IsVisible);
        var replace = (MenuFlyout)editor.ReplacePieces.Flyout!;
        Assert.Equal(["$0", "$1", "$2", "$3", "${name}", "$$"], replace.Items.OfType<MenuItem>().Select(i => ((RegexPiece)i.Tag!).Pattern));
        editor.ReplaceBox.Text = "";
        replace.Items.OfType<MenuItem>().ElementAt(1).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("$1", editor.ReplaceBox.Text);
        editor.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public void Select_with_a_regular_expression_matches_the_whole_name_and_the_history_keeps_which_it_was()
    {
        var dialog = new SelectMaskDialog(select: true, [@"regex:IMG_\d+\.jpg", "*.txt"], includesFolders: false);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.IsRegex);
        Assert.Equal(@"IMG_\d+\.jpg", dialog.MaskBox.Text);
        Assert.True(dialog.PiecesButton.IsVisible);
        Assert.Contains("whole name", dialog.HelpText.Text, StringComparison.Ordinal);
        dialog.Accept();
        Assert.True(dialog.Result!.IsRegex);
        Assert.Equal(@"regex:IMG_\d+\.jpg", dialog.Result.Text);
        Assert.True(dialog.Result.IsMatch("img_2041.JPG"));

        var other = new SelectMaskDialog(select: false, ["*.txt"], includesFolders: false);
        other.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(other.IsRegex);
        Assert.False(other.PiecesButton.IsVisible);
        other.SetRegex(true);
        other.MaskBox.Text = "(";
        other.Accept();
        Assert.True(other.ErrorText.IsVisible);
        Assert.Null(other.Result);
        other.SetRegex(false);
        Assert.False(other.ErrorText.IsVisible);
        other.Close();
    }
}
