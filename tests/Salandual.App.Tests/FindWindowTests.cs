// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/FindWindowTests.cs

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Find;
using Salandual.Core.Search;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class FindWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-find-").FullName;
    private readonly List<string> _calls = [];
    private readonly TextClipboard _clip = new();
    private readonly HashSet<string> _gone = new(StringComparer.OrdinalIgnoreCase);

    public FindWindowTests()
    {
        Write("README.md", "# Site\nRun deploy.ps1 to publish.\nAgain: deploy\n");
        Write("notes.txt", "Friday: deploy to the test server first\n");
        Write("draft.md", "deploy, but only a draft\n");
        Write(@"docs\hosting.md", "## Deploying with a script\n");
        Write(@"docs\ops\deep.txt", "deploy from the deep folder\n");
        Write("other.log", "nothing to see\n");
        File.WriteAllBytes(Path.Join(_dir, "wide.txt"), Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("a deploy in UTF-16")).ToArray());
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Join(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private FindWindow Open(IFileSystemProvider? files = null, SettingsService? settings = null, bool editDefaults = false)
    {
        var request = new FindRequest(_dir)
        {
            Files = files ?? new LocalFileSystemProvider(),
            OtherPanelFolder = () => @"D:\Other",
            LocalDrives = () => [@"C:\", @"D:\"],
            ShowInPanel = hit => _calls.Add("panel " + hit.FullPath),
            OpenFolder = path => _calls.Add("folder " + path),
            OpenFile = path => _calls.Add("open " + path),
            View = (path, siblings, text) => _calls.Add($"view {path} [{siblings.Count}]" + (text is null ? "" : $" “{text.Text}”")),
            Edit = path => _calls.Add("edit " + path),
            Properties = (folder, names, windows, _) => _calls.Add($"properties {folder} {string.Join(",", names)}" + (windows ? " windows" : "")),
            OpenWith = path => _calls.Add("open with " + path),
            CopyOrMove = (kind, hits, _) =>
            {
                _calls.Add($"{kind} {string.Join(",", hits.Select(h => h.Name))}");
                return Task.FromResult<Salandual.Core.Operations.OperationJob?>(null);
            },
            Delete = (hits, inverted, _) =>
            {
                _calls.Add($"delete {string.Join(",", hits.Select(h => h.Name))}" + (inverted ? " inverted" : ""));
                return Task.FromResult<Salandual.Core.Operations.OperationJob?>(null);
            },
            Rename = hit => _calls.Add("rename " + hit.Name),
            OpenOptions = category => _calls.Add("options " + category),
            Exists = path => !_gone.Contains(path) && (File.Exists(path) || Directory.Exists(path)),
            Clipboard = _clip,
        };
        var window = new FindWindow(settings ?? AppServices.CreateIsolated().Settings, request, editDefaults) { Width = 1000 };
        if (!editDefaults)
        {
            window.Height = 700;
        }

        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void The_clear_buttons_sit_in_the_middle_of_their_boxes()
    {
        var w = Open();
        w.NamedBox.Text = "*.md;*.txt";
        w.TextRows[0].Box.Text = "deploy";
        Dispatcher.UIThread.RunJobs();
        foreach (var box in new[] { w.NamedBox, w.TextRows[0].Box })
        {
            var clear = box.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Clear");
            Assert.True(clear.IsVisible);
            var cross = (Avalonia.Controls.Shapes.Path)clear.Content!;
            var drawn = cross.Data!.Bounds.Center;
            Assert.InRange(cross.TranslatePoint(drawn, box)!.Value.Y, (box.Bounds.Height / 2) - 1.5, (box.Bounds.Height / 2) + 1.5);
            Assert.InRange(cross.TranslatePoint(drawn, clear)!.Value.X, (clear.Bounds.Width / 2) - 1, (clear.Bounds.Width / 2) + 1);
        }

        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "find-clear.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        w.Close();
    }

    private static string[] Names(FindWindow w) => [.. w.Shown.Select(h => h.Name).Order(StringComparer.OrdinalIgnoreCase)];

    private static void Press(Control target, Key key, KeyModifiers mods = KeyModifiers.None)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = target });
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task Alt_F7_opens_Find_on_the_active_panels_folder_and_again_brings_it_forward()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(_dir);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();

        w.KeyPress(Key.F7, RawInputModifiers.Alt, PhysicalKey.None, null);
        w.KeyRelease(Key.F7, RawInputModifiers.Alt, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        var find = Assert.Single(w.FindWindows);
        Assert.Equal(_dir, find.LookInBox.Text);
        Assert.StartsWith("Search – ", find.Title);

        find.LookInBox.Text = @"C:\Elsewhere";
        Assert.True(w.Run("Find.Files", Salandual.Core.Commands.PanelTarget.Active));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(find, Assert.Single(w.FindWindows));
        Assert.Equal(_dir, find.LookInBox.Text);
        find.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(w.FindWindows);

        w.ActivePanel.View.Focus();
        w.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.None, null);
        w.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(w.FindWindows).Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Finds_by_name_and_content_with_the_matching_line_and_how_many_more()
    {
        var w = Open();
        w.NamedBox.Text = "*.md;*.txt";
        w.TextRows[0].Box.Text = "deploy";
        Assert.Equal("Search – *.md;*.txt containing “deploy”", w.TitleText);
        await w.StartAsync(ResultMode.New);

        Assert.Equal(["deep.txt", "draft.md", "hosting.md", "notes.txt", "README.md", "wide.txt"], Names(w));
        var readme = w.Shown.Single(h => h.Name == "README.md");
        Assert.Equal(2, readme.Line!.LineNumber);
        Assert.Equal("Run deploy.ps1 to publish.", readme.Line.Text);
        Assert.Equal(1, readme.Line.MoreLines);
        Assert.StartsWith("6 found", w.HeaderText, StringComparison.Ordinal);
        Assert.Contains("files in", w.HeaderText, StringComparison.Ordinal);
        Assert.Equal(_dir, w.Services().Find.LookIn[0]);
        Assert.Equal("deploy", w.Services().Find.Texts[0]);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Doesnt_contain_and_any_of_them()
    {
        var w = Open();
        w.NamedBox.Text = "*.md";
        w.TextRows[0].Box.Text = "deploy";
        w.AddTextRow(new SearchText("draft", Contains: false));
        Assert.Equal("Search – *.md containing “deploy”, not “draft”", w.TitleText);
        Assert.True(w.AllOrAnyBox.IsEffectivelyVisible);
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["hosting.md", "README.md"], Names(w));

        w.RemoveTextRow(w.TextRows[1]);
        Assert.False(w.AllOrAnyBox.IsEffectivelyVisible);
        w.TextRows[0].Box.Text = "publish";
        w.AddTextRow(new SearchText("script"));
        w.AllOrAnyBox.SelectedIndex = 1;
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["hosting.md", "README.md"], Names(w));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Subfolders_all_none_or_down_to_one_level()
    {
        var w = Open();
        w.NamedBox.Text = "*.txt";
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["deep.txt", "notes.txt", "wide.txt"], Names(w));

        w.DepthBox.SelectedIndex = 1;
        Assert.False(w.LevelsBox.IsVisible);
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["notes.txt", "wide.txt"], Names(w));

        w.DepthBox.SelectedIndex = 2;
        Assert.True(w.LevelsBox.IsVisible);
        w.LevelsBox.Value = 1;
        w.NamedBox.Text = "*.md";
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["draft.md", "hosting.md", "README.md"], Names(w));
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_broken_regular_expression_says_so_and_nothing_is_searched()
    {
        var w = Open();
        w.TextRows[0].SetKind(TextKind.Regex, w);
        Assert.True(w.TextRows[0].Pieces.IsVisible);
        w.TextRows[0].Box.Text = "(deploy";
        await w.StartAsync(ResultMode.New);
        Assert.True(w.Problem.IsVisible);
        Assert.False(w.IsSearching);
        Assert.Empty(w.Shown);

        w.TextRows[0].Box.Text = @"deploy\.ps\d";
        await w.StartAsync(ResultMode.New);
        Assert.False(w.Problem.IsVisible);
        Assert.Equal(["README.md"], Names(w));
        w.Close();
    }

    [AvaloniaFact]
    public void Regex_pieces_go_in_at_the_caret()
    {
        var w = Open();
        var row = w.TextRows[0];
        row.SetKind(TextKind.Regex, w);
        row.Box.Text = "deploy";
        Dispatcher.UIThread.RunJobs();
        var box = FindWindow.TextBoxIn(row.Box)!;
        box.CaretIndex = 6;
        box.SelectionStart = box.SelectionEnd = 6;
        RegexMenu.Insert(row.Box, RegexPieces.All.First(p => p.Pattern == @"\d+"));
        Assert.Equal(@"deploy\d+", row.Box.Text);
        var menu = Assert.IsType<MenuFlyout>(row.Pieces.Flyout);
        Assert.Contains(menu.Items.OfType<MenuItem>(), i => i.Tag is RegexPiece { Pattern: "^" });
        w.Close();
    }

    [AvaloniaFact]
    public async Task Encodings_any_or_only_the_ones_ticked()
    {
        var w = Open();
        w.NamedBox.Text = "wide.txt";
        w.TextRows[0].Box.Text = "deploy";
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["wide.txt"], Names(w));
        Assert.Equal("Encodings: any  ▾", w.EncodingChip.Content);

        w.Encodings = SearchEncodings.Utf8;
        Assert.Equal("Encodings: UTF-8  ▾", w.EncodingChip.Content);
        await w.StartAsync(ResultMode.New);
        Assert.Empty(w.Shown);

        var boxes = w.EncodingBoxes;
        boxes[0].IsChecked = true;
        Assert.Equal(SearchEncodings.Auto, w.Encodings);
        boxes[1].IsChecked = true;
        boxes[3].IsChecked = true;
        Assert.Equal(SearchEncodings.Ansi | SearchEncodings.Utf8, w.Encodings);
        Assert.False(boxes[0].IsChecked);
        Assert.Equal("Encodings: ANSI, UTF-8  ▾", w.EncodingChip.Content);
        boxes[4].IsChecked = true;
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["wide.txt"], Names(w));
        boxes[3].IsChecked = false;
        Assert.Equal(SearchEncodings.Ansi | SearchEncodings.Utf16LE, w.Encodings);
        boxes[0].IsChecked = true;
        Assert.Equal(SearchEncodings.Auto, w.Encodings);
        Assert.All(boxes.Skip(1), b => Assert.False(b.IsChecked));
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_results_keys_open_show_view_edit_hide_and_copy()
    {
        var w = Open();
        w.NamedBox.Text = "*.md";
        await w.StartAsync(ResultMode.New);
        var list = w.ResultList;
        list.Focus();
        list.SelectedItem = w.Shown.Single(h => h.Name == "README.md");
        var readme = Path.Join(_dir, "README.md");

        Press(list, Key.Enter);
        Press(list, Key.Space);
        Press(list, Key.F3);
        Press(list, Key.F4);
        Press(list, Key.Enter, KeyModifiers.Alt);
        Press(list, Key.Enter, KeyModifiers.Alt | KeyModifiers.Shift);
        Assert.Equal(["open " + readme, "panel " + readme, $"view {readme} [3]", "edit " + readme, $"properties {_dir} README.md", $"properties {_dir} README.md windows"], _calls);

        Press(list, Key.C, KeyModifiers.Control);
        Assert.Equal([readme], _clip.Files);
        Press(list, Key.Insert, KeyModifiers.Alt | KeyModifiers.Shift);
        Assert.Equal("README.md", _clip.Text);

        Press(list, Key.H, KeyModifiers.Control);
        Assert.DoesNotContain(w.Shown, h => h.Name == "README.md");
        Assert.StartsWith("2 found", w.HeaderText, StringComparison.Ordinal);

        _calls.Clear();
        w.NamedBox.Text = "docs";
        await w.StartAsync(ResultMode.New);
        list.SelectedItem = w.Shown.Single(h => h.IsDirectory);
        Press(list, Key.Enter);
        Assert.Equal(["folder " + Path.Join(_dir, "docs")], _calls);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Keep_remove_and_add_to_the_results()
    {
        var w = Open();
        w.NamedBox.Text = "*.md";
        w.TextRows[0].Box.Text = "deploy";
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["draft.md", "hosting.md", "README.md"], Names(w));

        w.TextRows[0].Box.Text = "draft";
        await w.StartAsync(ResultMode.Remove);
        Assert.Equal(["hosting.md", "README.md"], Names(w));

        w.TextRows[0].Box.Text = "publish";
        await w.StartAsync(ResultMode.Keep);
        Assert.Equal(["README.md"], Names(w));

        w.NamedBox.Text = "*.log";
        w.TextRows[0].Box.Text = "";
        await w.StartAsync(ResultMode.Add);
        Assert.Equal(["other.log", "README.md"], Names(w));
        w.Close();
    }

    private sealed class SlowFiles(TaskCompletionSource gate) : IFileSystemProvider
    {
        private readonly LocalFileSystemProvider _local = new();

        public ProviderCapabilities Capabilities => _local.Capabilities;

        public async Task<DirectoryListing> ListAsync(string path, CancellationToken cancellationToken = default)
        {
            await gate.Task.WaitAsync(cancellationToken);
            return await _local.ListAsync(path, cancellationToken);
        }

        public string? GetParent(string path) => _local.GetParent(path);

        public string? GetRoot(string path) => _local.GetRoot(path);

        public string GetName(string path) => _local.GetName(path);

        public string Combine(string directory, string name) => _local.Combine(directory, name);

        public IDisposable Watch(string path, Action<DirectoryChange> onChange, Action<Exception> onError) => _local.Watch(path, onChange, onError);
    }

    [AvaloniaFact]
    public async Task While_searching_the_progress_line_shows_in_its_kept_place_and_Esc_stops()
    {
        var gate = new TaskCompletionSource();
        var w = Open(new SlowFiles(gate));
        Assert.Equal(3, w.ProgressStrip.Height);
        Assert.Equal(0, w.ProgressStrip.Opacity);
        var top = Avalonia.VisualExtensions.TranslatePoint(w.ResultList, new Avalonia.Point(0, 0), w);

        var running = w.StartAsync(ResultMode.New);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.IsSearching);
        Assert.Equal(1, w.ProgressStrip.Opacity);
        Assert.True(w.StopButton.IsVisible);
        Assert.Same(w.FindButton.Parent, w.StopButton.Parent);
        Assert.All(((Panel)w.StopButton.Parent!).Children, c => Assert.Equal(c == w.StopButton, c.IsVisible));
        Assert.Equal(top, Avalonia.VisualExtensions.TranslatePoint(w.ResultList, new Avalonia.Point(0, 0), w));

        Press(w, Key.Escape);
        await running;
        Assert.False(w.IsSearching);
        Assert.True(w.IsVisible);
        Assert.Contains("stopped", w.HeaderText, StringComparison.Ordinal);
        Assert.Equal(0, w.ProgressStrip.Opacity);
        Assert.All(((Panel)w.StopButton.Parent!).Children, c => Assert.Equal(c != w.StopButton, c.IsVisible));

        Press(w, Key.Escape);
        Assert.False(w.IsVisible);
    }

    [AvaloniaFact]
    public async Task Stop_in_Searchs_place_stops_the_search()
    {
        var gate = new TaskCompletionSource();
        var w = Open(new SlowFiles(gate));
        var running = w.StartAsync(ResultMode.New);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Stop  Esc", w.StopButton.Content);
        Assert.False(w.FindButton.IsVisible);
        Assert.False(w.NamedBox.IsEffectivelyEnabled);
        Assert.False(w.LookInBox.IsEffectivelyEnabled);
        Assert.False(w.MatchCaseBox.IsEffectivelyEnabled);
        Assert.False(w.AdvancedButton.IsEffectivelyEnabled);
        Assert.Equal(0.45, w.MatchCaseBox.Opacity);
        Assert.Equal(0.45, w.AdvancedButton.Opacity);
        Assert.True(w.StopButton.IsEffectivelyEnabled);

        w.StopButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await running;
        Assert.False(w.IsSearching);
        Assert.True(w.FindButton.IsVisible);
        Assert.False(w.StopButton.IsVisible);
        Assert.True(w.NamedBox.IsEffectivelyEnabled);
        Assert.True(w.LookInBox.IsEffectivelyEnabled);
        Assert.True(w.MatchCaseBox.IsEffectivelyEnabled);
        Assert.True(w.AdvancedButton.IsEffectivelyEnabled);
        Assert.Contains("stopped", w.HeaderText, StringComparison.Ordinal);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Advanced_sets_size_modified_and_attributes_and_the_summary_says_what()
    {
        var w = Open();
        Assert.Equal("No advanced settings · skipped folders: 2", w.SummaryText);
        Assert.False(w.ClearAdvancedButton.IsVisible);
        Assert.False(w.AdvancedOpen);
        w.SummaryTapped(w.ClearAdvancedButton);
        Assert.False(w.AdvancedOpen);
        w.SummaryTapped(w.SummaryField);
        Assert.True(w.AdvancedOpen);
        Assert.False(w.WithinCount.IsVisible);

        w.UnitBox.SelectedIndex = 0;
        w.MinSizeBox.Text = "30";
        w.AttributeBoxes[FileAttributes.Hidden].Rule = AttributeRule.MustNot;
        w.ModifiedBox.SelectedIndex = 1;
        w.WithinCount.Value = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.WithinCount.IsVisible);
        Assert.Equal("✕ Hidden", w.AttributeBoxes[FileAttributes.Hidden].Text);
        Assert.Equal("Size ≥ 30 bytes · Last 2 days · Not Hidden · skipped folders: 2", w.SummaryText);
        Assert.True(w.ClearAdvancedButton.IsVisible);

        w.NamedBox.Text = "*.*";
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["notes.txt", "README.md", "wide.txt"], Names(w));
        Assert.DoesNotContain(w.Shown, h => h.IsDirectory);

        w.ModifiedBox.SelectedIndex = 2;
        Assert.False(w.WithinCount.IsVisible);
        Assert.True(w.ToDate.IsVisible);
        w.ToDate.SelectedDate = DateTime.Today.AddYears(-1);
        await w.StartAsync(ResultMode.New);
        Assert.Empty(w.Shown);

        w.ClearAdvancedButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("No advanced settings · skipped folders: 2", w.SummaryText);
        Assert.Equal("Hidden", w.AttributeBoxes[FileAttributes.Hidden].Text);
        Assert.Equal(0, w.ModifiedBox.SelectedIndex);

        w.AttributeBoxes[FileAttributes.System].View.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("✓ System", w.AttributeBoxes[FileAttributes.System].Text);
        w.AttributeBoxes[FileAttributes.System].View.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(AttributeRule.MustNot, w.AttributeBoxes[FileAttributes.System].Rule);
        w.AttributeBoxes[FileAttributes.System].View.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(AttributeRule.Either, w.AttributeBoxes[FileAttributes.System].Rule);

        w.AttributeBoxes[FileAttributes.System].Rule = AttributeRule.MustNot;
        w.AttributeBoxes[FileAttributes.Archive].Rule = AttributeRule.Must;
        Assert.Equal("Archive, Not System · skipped folders: 2", w.SummaryText);

        var folder = w.AttributeBoxes[FileAttributes.Directory];
        var widths = new List<double>();
        foreach (var rule in new[] { AttributeRule.Either, AttributeRule.Must, AttributeRule.MustNot })
        {
            folder.Rule = rule;
            folder.View.Measure(Avalonia.Size.Infinity);
            widths.Add(folder.View.DesiredSize.Width);
        }

        Assert.Single(widths.Distinct());
        Assert.Equal("Archive, Not Folder, Not System · skipped folders: 2", w.SummaryText);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Skipped_folders_are_kept_in_the_settings_and_logged()
    {
        var settings = AppServices.CreateIsolated().Settings;
        var w = Open(settings: settings);
        w.AdvancedOpen = true;
        w.SkippedInput.Text = "ops";
        Press(w.SkippedInput, Key.Enter);
        Assert.Equal(["System Volume Information", "$Recycle.Bin", "ops"], settings.Current.Find.SkippedFolders);
        Assert.Equal("No advanced settings · skipped folders: 3", w.SummaryText);

        w.NamedBox.Text = "*.txt";
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["notes.txt", "wide.txt"], Names(w));
        Assert.Single(w.Log, e => e.Kind == SearchLogKind.Skipped);
        Assert.True(w.LogButton.IsVisible);
        Assert.Equal("Log  1", w.LogButton.Content);

        w.ShowLog();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(w.LogWindow!.Lines, l => l.Contains(Path.Join(_dir, "docs", "ops"), StringComparison.Ordinal));
        w.LogWindow.Close();
        w.Close();
    }

    [AvaloniaFact]
    public async Task Inside_zip_files_when_ticked()
    {
        using (var zip = System.IO.Compression.ZipFile.Open(Path.Join(_dir, "pack.zip"), System.IO.Compression.ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("inner/invoice.txt").Open());
            writer.Write("deploy the invoice");
        }

        var w = Open();
        w.NamedBox.Text = "invoice";
        await w.StartAsync(ResultMode.New);
        Assert.Empty(w.Shown);

        w.InsideZipBox.IsChecked = true;
        await w.StartAsync(ResultMode.New);
        var hit = Assert.Single(w.Shown);
        Assert.Equal(Path.Join(_dir, "pack.zip", "inner", "invoice.txt"), hit.FullPath);

        w.ResultList.SelectedItem = hit;
        Press(w.ResultList, Key.F3);
        Assert.Empty(_calls);
        Assert.Contains("ZIP", w.NoticeText, StringComparison.Ordinal);
        Press(w.ResultList, Key.Space);
        Assert.Equal(["panel " + Path.Join(_dir, "pack.zip")], _calls);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Results_sort_by_a_column_and_keep_folders_first()
    {
        var w = Open();
        w.NamedBox.Text = "*";
        w.DepthBox.SelectedIndex = 1;
        await w.StartAsync(ResultMode.New);
        Assert.True(w.Shown[0].IsDirectory);
        w.Sort(ResultColumn.Size, toggle: true);
        Assert.True(w.Shown[0].IsDirectory);
        Assert.Equal("README.md", w.Shown[1].Name);
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_results_take_the_keyboard_with_the_first_one_found_but_not_when_nothing_is()
    {
        var w = Open();
        w.NamedBox.Text = "nothing-like-this";
        w.NamedBox.Focus();
        await w.StartAsync(ResultMode.New);
        Assert.Empty(w.Shown);
        Assert.True(w.NamedBox.IsKeyboardFocusWithin);

        w.NamedBox.Text = "*.md";
        await w.StartAsync(ResultMode.New);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.ResultList.IsKeyboardFocusWithin);
        Assert.Equal(0, w.ResultList.SelectedIndex);

        w.Settings.Update(s => s with { Find = s.Find with { FocusResults = false } });
        w.NamedBox.Focus();
        await w.StartAsync(ResultMode.New);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.NamedBox.IsKeyboardFocusWithin);
        w.Close();
    }

    [AvaloniaFact]
    public async Task F3_gives_the_viewer_Finds_first_text()
    {
        var w = Open();
        w.NamedBox.Text = "README.md";
        w.TextRows[0].Box.Text = "publish";
        w.AddTextRow(new SearchText("draft", Contains: false));
        w.MatchCaseBox.IsChecked = true;
        await w.StartAsync(ResultMode.New);
        Assert.Equal(new Salandual.Core.Viewer.FindQuery("publish", MatchCase: true), w.ViewerQuery);
        w.ResultList.SelectedIndex = 0;
        Press(w.ResultList, Key.F3);
        Assert.Equal([$"view {Path.Join(_dir, "README.md")} [1] “publish”"], _calls);

        w.RemoveTextRow(w.TextRows[1]);
        w.TextRows[0].SetKind(TextKind.Hex, w);
        await w.StartAsync(ResultMode.New);
        Assert.Equal(new Salandual.Core.Viewer.FindQuery("70 75 62 6C 69 73 68", HexBytes: true, MatchCase: true), w.ViewerQuery);
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_drop_downs_get_what_was_just_searched_for()
    {
        var w = Open();
        Assert.Empty(w.NamedBox.Items);
        w.NamedBox.Text = "*.md";
        w.TextRows[0].Box.Text = "deploy";
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["*.md"], w.NamedBox.Items.OfType<string>());
        Assert.Equal([_dir], w.LookInBox.Items.OfType<string>());
        Assert.Equal(["deploy"], w.TextRows[0].Box.Items.OfType<string>());
        Assert.Equal("*.md", w.NamedBox.Text);

        w.NamedBox.Text = "*.txt";
        await w.StartAsync(ResultMode.New);
        Assert.Equal(["*.txt", "*.md"], w.NamedBox.Items.OfType<string>());
        w.Close();
    }

    [AvaloniaFact]
    public void Hex_takes_hex_digits_only_in_pairs()
    {
        var w = Open();
        var row = w.TextRows[0];
        row.Box.Text = "MZ";
        row.SetKind(TextKind.Hex, w);
        Assert.Equal("4D 5A", row.Box.Text);

        row.Box.Text = "4d5a9 zz";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("4D 5A 9", row.Box.Text);

        row.SetKind(TextKind.Text, w);
        Assert.Equal("4D 5A 9", row.Box.Text);
        row.Box.Text = "4D5A";
        row.SetKind(TextKind.Hex, w);
        Assert.Equal("4D 5A", row.Box.Text);
        row.SetKind(TextKind.Text, w);
        Assert.Equal("MZ", row.Box.Text);
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_results_menu_and_keys_work_as_in_a_panel()
    {
        var w = Open();
        w.NamedBox.Text = "*.md";
        await w.StartAsync(ResultMode.New);
        var list = w.ResultList;
        list.SelectedItem = w.Shown.Single(h => h.Name == "README.md");

        var menu = w.ShowResultMenu(new Avalonia.Point(10, 10))!;
        var headers = menu.Items.OfType<MenuItem>().Select(i => i.Header as string).OfType<string>().Select(h => h.Replace("_", "", StringComparison.Ordinal)).ToList();
        Assert.Equal(["Open", "Open with…", "Jump to the file", "View", "Edit", "Cut", "Copy", "Copy to other panel…", "Move to other panel…", "Rename", "Delete", "Copy path", "Hide from the list", "Windows commands", "Properties"], headers);
        menu.Close();

        Press(list, Key.F5);
        Press(list, Key.F6);
        Press(list, Key.F2);
        Press(list, Key.F8);
        Press(list, Key.Delete, KeyModifiers.Shift);
        Assert.Equal(["Copy README.md", "Move README.md", "rename README.md", "delete README.md", "delete README.md inverted"], _calls);
        Press(list, Key.X, KeyModifiers.Control);
        Assert.True(_clip.Cut);

        list.SelectAll();
        Dispatcher.UIThread.RunJobs();
        menu = w.ShowResultMenu(new Avalonia.Point(10, 10))!;
        Assert.Contains(menu.Items.OfType<MenuItem>(), i => (i.Header as string)?.Replace("_", "", StringComparison.Ordinal) == "Open all 3");
        Assert.Contains(menu.Items.OfType<MenuItem>(), i => (i.Header as string)?.Replace("_", "", StringComparison.Ordinal) == "Delete 3 items");
        Assert.False(menu.Items.OfType<MenuItem>().Single(i => (i.Header as string)?.Replace("_", "", StringComparison.Ordinal) == "Windows commands").IsEnabled);
        menu.Close();

        _calls.Clear();
        Press(list, Key.Enter, KeyModifiers.Alt);
        var properties = Assert.Single(_calls);
        Assert.StartsWith($"properties {_dir} ", properties, StringComparison.Ordinal);
        Assert.Equal(3, properties[$"properties {_dir} ".Length..].Split(',').Length);

        _gone.Add(Path.Join(_dir, "draft.md"));
        w.DropMissing([.. w.Shown]);
        Assert.Equal(["hosting.md", "README.md"], Names(w));
        w.Close();
    }

    [AvaloniaFact]
    public void The_calendar_marks_today_without_choosing_it()
    {
        var w = Open();
        w.AdvancedOpen = true;
        w.ModifiedBox.SelectedIndex = 2;
        w.FromDate.SelectedDate = DateTime.Today.AddDays(-3);
        w.FromDate.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var calendar = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w.FromDate).OfType<Avalonia.Controls.Primitives.Popup>().Single().Child!;
        var days = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(calendar).OfType<Avalonia.Controls.Primitives.CalendarDayButton>().ToList();
        var today = DateTime.Today.Day.ToString(System.Globalization.CultureInfo.CurrentCulture);
        Assert.Contains(days, d => d.Classes.Contains(":today") && d.Content as string == today);
        Assert.Equal(DateTime.Today.AddDays(-3), w.FromDate.SelectedDate);
        w.FromDate.IsDropDownOpen = false;
        w.Close();
    }

    [AvaloniaFact]
    public void Search_options_open_from_the_gear()
    {
        var w = Open();
        w.OptionsButton().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["options compare"], _calls);
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_closed_Search_starts_the_next_one_from_the_defaults_and_keeps_the_history()
    {
        var settings = AppServices.CreateIsolated().Settings;
        var w = Open(settings: settings);
        Assert.Equal("*.*", w.NamedBox.Text);
        Assert.Equal(_dir, w.LookInBox.Text);
        w.NamedBox.Text = "*.md";
        w.TextRows[0].Box.Text = "deploy";
        w.AddTextRow(new SearchText("draft", Contains: false));
        w.DepthBox.SelectedIndex = 1;
        w.InsideZipBox.IsChecked = true;
        w.MatchCaseBox.IsChecked = true;
        w.Encodings = SearchEncodings.Utf8;
        w.MinSizeBox.Text = "1";
        await w.StartAsync(ResultMode.New);
        w.Close();
        Dispatcher.UIThread.RunJobs();

        w = Open(settings: settings);
        Assert.Equal("*.*", w.NamedBox.Text);
        Assert.Equal(_dir, w.LookInBox.Text);
        Assert.Equal("", Assert.Single(w.TextRows).Text);
        Assert.Equal(0, w.DepthBox.SelectedIndex);
        Assert.False(w.InsideZipBox.IsChecked);
        Assert.False(w.MatchCaseBox.IsChecked);
        Assert.Equal(SearchEncodings.Auto, w.Encodings);
        Assert.Equal("No advanced settings · skipped folders: 2", w.SummaryText);
        Assert.Equal(["*.md"], w.NamedBox.Items.OfType<string>());
        Assert.Equal(["deploy", "draft"], w.TextRows[0].Box.Items.OfType<string>());
        w.Close();
    }

    [AvaloniaFact]
    public void A_new_Search_starts_with_the_defaults_from_Options()
    {
        var settings = AppServices.CreateIsolated().Settings;
        settings.Update(s => s with
        {
            Find = s.Find with
            {
                Defaults = new SearchDefaults
                {
                    Named = "*.md", Texts = [new("deploy"), new("4D 5A", TextKind.Hex, Contains: false)], AllTexts = false, Depth = 3, WholeWords = true,
                    Encodings = SearchEncodings.Ansi | SearchEncodings.Utf16LE, MinSize = "2", SizeUnit = 2, Modified = ModifiedRule.Within, WithinCount = 7, MustNotHave = FileAttributes.System,
                },
            },
        });
        var w = Open(settings: settings);
        Assert.Equal("*.md", w.NamedBox.Text);
        Assert.Equal(_dir, w.LookInBox.Text);
        Assert.Equal(["deploy", "4D 5A"], w.TextRows.Select(r => r.Text));
        Assert.Equal(TextKind.Hex, w.TextRows[1].Kind);
        Assert.False(w.TextRows[1].Contains);
        Assert.Equal(1, w.AllOrAnyBox.SelectedIndex);
        Assert.Equal(2, w.DepthBox.SelectedIndex);
        Assert.Equal(3, (int)w.LevelsBox.Value!);
        Assert.True(w.WholeWordsBox.IsChecked);
        Assert.Equal("Encodings: ANSI, UTF-16 LE  ▾", w.EncodingChip.Content);
        Assert.Equal("Size ≥ 2 MB · Last 7 days · Not System · skipped folders: 2", w.SummaryText);
        Assert.Equal("Search – *.md containing “deploy”, not “4D 5A”", w.TitleText);
        w.Close();

        settings.Update(s => s with { Find = s.Find with { Defaults = new SearchDefaults { LookIn = @"D:Fixed" } } });
        w = Open(settings: settings);
        Assert.Equal(@"D:Fixed", w.LookInBox.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void The_defaults_window_saves_every_box_and_has_the_built_in_ones()
    {
        var settings = AppServices.CreateIsolated().Settings;
        var w = Open(settings: settings, editDefaults: true);
        Assert.True(w.EditsDefaults);
        Assert.Equal("Search defaults – what a new Search starts with", w.Title);
        Assert.Equal("", w.LookInBox.Text);
        Assert.Equal("The active panel's folder", w.LookInBox.PlaceholderText);
        w.NamedBox.Text = "*.txt";
        w.LookInBox.Text = @"C:Projects";
        w.TextRows[0].Box.Text = "TODO";
        w.MatchCaseBox.IsChecked = true;
        w.AttributeBoxes[FileAttributes.Archive].Rule = AttributeRule.Must;
        w.Encodings = SearchEncodings.Utf8;
        Assert.True(w.SaveDefaults());
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.IsVisible);
        var saved = settings.Current.Find.Defaults;
        Assert.Equal("*.txt", saved.Named);
        Assert.Equal(@"C:Projects", saved.LookIn);
        Assert.Equal([new SearchText("TODO")], saved.Texts);
        Assert.True(saved.MatchCase);
        Assert.Equal(FileAttributes.Archive, saved.MustHave);
        Assert.Equal(SearchEncodings.Utf8, saved.Encodings);
        Assert.Empty(settings.Current.Find.Named);

        w = Open(settings: settings, editDefaults: true);
        Assert.Equal("*.txt", w.NamedBox.Text);
        w.TextRows[0].SetKind(TextKind.Regex, w);
        w.TextRows[0].Box.Text = "(broken";
        Assert.False(w.SaveDefaults());
        Assert.True(w.Problem.IsVisible);
        Assert.Equal("*.txt", settings.Current.Find.Defaults.Named);
        w.ApplyDefaults(SearchDefaults.BuiltIn);
        Assert.Equal("*.*", w.NamedBox.Text);
        Assert.Equal("", w.LookInBox.Text);
        Assert.True(w.SaveDefaults());
        Assert.True(settings.Current.Find.Defaults.IsBuiltIn());
    }

    [AvaloniaFact]
    public void Named_and_Containing_have_a_clear_button_while_they_have_text()
    {
        var w = Open();
        Assert.True(w.NamedClear.IsVisible);
        w.NamedClear.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("", w.NamedBox.Text);
        Assert.False(w.NamedClear.IsVisible);

        var row = w.TextRows[0];
        Assert.False(row.Clear.IsVisible);
        row.Box.Text = "deploy";
        Assert.True(row.Clear.IsVisible);
        row.Clear.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("", row.Box.Text);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(w.NamedClear, FindWindow.TextBoxIn(w.NamedBox)!.InnerRightContent);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Closing_Search_asks_first_when_Options_says_so()
    {
        var settings = AppServices.CreateIsolated().Settings;
        var w = Open(settings: settings);
        w.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.IsVisible);

        settings.Update(s => s with { Confirmations = s.Confirmations with { CloseSearch = true } });
        w = Open(settings: settings);
        Press(w, Key.Escape);
        await Until(() => w.CloseQuestion is not null);
        Assert.True(w.IsVisible);
        Assert.Equal("Close Search?", w.CloseQuestion!.Title);
        w.CloseQuestion.No.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Until(() => w.CloseQuestion is null);
        Assert.True(w.IsVisible);

        w.Close();
        await Until(() => w.CloseQuestion is not null);
        w.CloseQuestion!.Yes.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Until(() => !w.IsVisible);
    }

    private sealed class TextClipboard : IClipboardAccess
    {
        public IReadOnlyList<string> Files { get; private set; } = [];

        public bool Cut { get; private set; }

        public string? Text { get; private set; }

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut)
        {
            Files = [.. paths];
            Cut = cut;
            SequenceNumber++;
            return true;
        }

        public bool SetText(string text)
        {
            Text = text;
            SequenceNumber++;
            return true;
        }

        public void Clear()
        {
            Files = [];
            Text = null;
        }

        public ClipboardContent? Read() => new() { Files = [.. Files], Text = Text };
    }
}

internal static class FindWindowTestExtensions
{
    public static AppSettings Services(this FindWindow window) => window.Settings.Current;
}
