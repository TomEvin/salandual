// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/FileCompareTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Salandual.App.Compare;
using Salandual.Core.Compare;

namespace Salandual.App.Tests;

public sealed class FileCompareTests : IDisposable
{
    private static readonly string[] Left =
    [
        "{", "  \"name\": \"website\",", "  \"version\": \"1.4.0\",", "  \"private\": true,", "  \"scripts\": {", "    \"build\": \"vite build\",",
        "    \"deploy\": \"pwsh ./deploy.ps1\",", "    \"test\": \"vitest\"", "  },", "  \"dependencies\": {", "    \"marked\": \"12.0.2\",", "    \"vite\": \"5.4.8\"", "  },", "  \"license\": \"MIT\"", "}",
    ];

    private static readonly string[] Right =
    [
        "{", "  \"name\": \"website\",", "  \"version\": \"1.5.0\",", "  \"private\": true,", "  \"scripts\": {", "    \"build\": \"vite build\",",
        "    \"test\": \"vitest run\",", "    \"lint\": \"eslint .\"", "  },", "  \"dependencies\": {", "    \"marked\": \"12.0.2\",", "    \"vite\": \"5.4.10\"", "  },", "  \"license\": \"MIT\"", "}",
    ];

    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-filecompare-").FullName;
    private readonly string _left;
    private readonly string _right;

    public FileCompareTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Website")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Backup")).FullName;
        File.WriteAllLines(Path.Join(_left, "package.json"), Left);
        File.WriteAllLines(Path.Join(_right, "package.json"), Right);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private async Task<FileCompareWindow> OpenWindow(string left, string right, FileCompareHost? host = null)
    {
        var services = ((App)Avalonia.Application.Current!).Services;
        var window = new FileCompareWindow(services.Settings, left, right) { Width = 1200, Height = 700, Host = host };
        window.Show();
        await Until(() => window.View.RowCount > 0 || window.SummaryText.Length > 0 && window.SummaryText != "Comparing…");
        await window.Comparing;
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Press(FileCompareWindow w, Key key, KeyModifiers mods = KeyModifiers.None)
    {
        w.View.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = w.View });
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task Two_files_side_by_side_with_the_differences_counted_and_gone_through()
    {
        var w = await OpenWindow(Path.Join(_left, "package.json"), Path.Join(_right, "package.json"));
        Assert.False(w.View.IsBinary);
        Assert.Equal(3, w.View.Blocks.Count);
        Assert.Equal("3 differences, 5 lines: 3 changed, 1 added, 1 removed", w.SummaryText);
        Assert.Equal("JSON", w.KindText);
        Assert.Equal("Compare — package.json · package.json", w.Title);

        Assert.Equal("Difference 1 of 3", w.PositionText);
        Assert.Equal(2, w.View.CursorRow);
        Assert.False(w.PreviousButton.IsEnabled);
        Press(w, Key.Down, KeyModifiers.Alt);
        Assert.Equal("Difference 2 of 3", w.PositionText);
        Assert.Equal(6, w.View.CursorRow);
        Press(w, Key.Down, KeyModifiers.Alt);
        Press(w, Key.Down, KeyModifiers.Alt);
        Assert.Equal("Difference 3 of 3", w.PositionText);
        Assert.False(w.NextButton.IsEnabled);
        Press(w, Key.Up, KeyModifiers.Alt);
        Assert.Equal("Difference 2 of 3", w.PositionText);
        Press(w, Key.Up);
        Assert.Equal(5, w.View.CursorRow);
        Assert.Equal("3 differences", w.PositionText);
        Assert.Equal("Ln 6", w.LineText);

        var rows = w.View.Text!.Rows;
        Assert.Equal(new DiffRow(6, -1, LineChange.Removed), rows[6]);
        Assert.Equal(new DiffRow(7, 6, LineChange.Changed), rows[7]);
        Assert.Equal(new DiffRow(-1, 7, LineChange.Added), rows[8]);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Only_the_differences_folds_the_same_lines_and_a_click_opens_a_fold()
    {
        var w = await OpenWindow(Path.Join(_left, "package.json"), Path.Join(_right, "package.json"));
        Press(w, Key.D, KeyModifiers.Control);
        Assert.True(w.View.OnlyDifferences);
        Assert.Contains("on", w.OnlyButton.Classes);

        var items = Enumerable.Range(0, (int)w.View.ItemCount).Select(i => w.View.ItemAt(i)).ToList();
        Assert.DoesNotContain(items, i => i.IsFold);
        Press(w, Key.D, KeyModifiers.Control);

        var longLeft = Enumerable.Range(0, 100).Select(i => "line " + i).ToList();
        var longRight = longLeft.ToList();
        longRight[50] = "line fifty";
        File.WriteAllLines(Path.Join(_left, "long.txt"), longLeft);
        File.WriteAllLines(Path.Join(_right, "long.txt"), longRight);
        w.Close();
        w = await OpenWindow(Path.Join(_left, "long.txt"), Path.Join(_right, "long.txt"));
        w.SetOnlyDifferences(true);
        Assert.Equal(new DiffItem(0, 47), w.View.ItemAt(0));
        Assert.Equal(new DiffItem(47), w.View.ItemAt(1));
        Assert.Equal(new DiffItem(54, 46), w.View.ItemAt(8));
        Assert.Equal(9, w.View.ItemCount);
        Assert.Equal(50, w.View.CursorRow);
        w.View.Unfold(0);
        Assert.Equal(55, w.View.ItemCount);
        Assert.Equal(0, w.View.CursorRow);
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_difference_is_copied_across_with_its_arrows_and_undone()
    {
        var w = await OpenWindow(Path.Join(_left, "package.json"), Path.Join(_right, "package.json"));
        Assert.Equal(58, w.View.Gap);
        Assert.Equal("Click a line to copy only it; Shift+click for several", w.HintText);

        var side = (w.View.Bounds.Width - w.View.Gap) / 2;
        var y = ((w.View.ItemOfRow(6) - w.View.Top) * w.View.RowHeight) + 4;
        var x = side + ((w.View.Gap - 44) / 2);
        Assert.Equal((1, true), w.View.ArrowAt(new Point(x + 5, y)));
        Assert.Equal((1, false), w.View.ArrowAt(new Point(x + 30, y)));
        Assert.Null(w.View.ArrowAt(new Point(x + 5, y + w.View.RowHeight)));

        w.CopyAcross(1, toRight: true);
        await w.Comparing;
        Assert.Equal(2, w.View.Blocks.Count);
        Assert.Equal(Left[3..10], w.LinesOf(1).Skip(3).Take(7));
        Assert.Equal("● 3 lines copied in, not saved", w.PendingText(1));
        Assert.Equal("", w.PendingText(0));
        Assert.True(w.IsModified);
        Assert.False(w.BinaryButton.IsEnabled);
        Assert.Equal("Difference 1 of 2", w.PositionText);

        Press(w, Key.Z, KeyModifiers.Control);
        await w.Comparing;
        Assert.Equal(3, w.View.Blocks.Count);
        Assert.Equal(Right, w.LinesOf(1));
        Assert.False(w.IsModified);
        Assert.Equal("", w.PendingText(1));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Only_the_lines_clicked_are_copied_and_Ctrl_Alt_arrows_copy_the_current_difference()
    {
        var w = await OpenWindow(Path.Join(_left, "package.json"), Path.Join(_right, "package.json"));

        w.View.SelectLine(8, extend: false);
        Assert.Equal("1 of 3 lines selected (Esc: the whole difference)", w.HintText);
        Assert.Equal((8, 1), w.View.TargetRows(1));
        w.View.SelectLine(7, extend: true);
        Assert.Equal((7, 2), w.View.TargetRows(1));
        Press(w, Key.Escape);
        Assert.Null(w.View.Selection);
        Assert.True(w.IsVisible);
        w.View.SelectLine(8, extend: false);
        w.CopyAcross(1, toRight: false);
        await w.Comparing;
        Assert.Equal("    \"lint\": \"eslint .\"", w.LinesOf(0)[8]);
        Assert.Equal("● 1 line copied in, not saved", w.PendingText(0));
        Assert.Equal(3, w.View.Blocks.Count);

        Press(w, Key.Home);
        Press(w, Key.Down, KeyModifiers.Alt);
        Press(w, Key.Right, KeyModifiers.Control | KeyModifiers.Alt);
        await w.Comparing;
        Assert.Equal("  \"version\": \"1.4.0\",", w.LinesOf(1)[2]);
        Assert.Equal(2, w.View.Blocks.Count);
        Assert.Equal("Difference 1 of 2", w.PositionText);

        Press(w, Key.Z, KeyModifiers.Control);
        await w.Comparing;
        Assert.Equal(Right, w.LinesOf(1));
        Assert.True(w.IsModified);
        w.Undo(0);
        await w.Comparing;
        Assert.False(w.IsModified);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Save_writes_the_file_as_it_was_written_and_keeps_a_bak()
    {
        var right = Path.Join(_right, "package.json");
        var before = File.ReadAllText(right);
        var w = await OpenWindow(Path.Join(_left, "package.json"), right);
        w.CopyAcross(0, toRight: true);
        await w.Comparing;
        Assert.True(await w.SaveAsync(1));
        Assert.False(w.IsModified);
        Assert.Equal("", w.PendingText(1));
        Assert.Equal(before, File.ReadAllText(right + ".bak"));
        var expected = string.Join(Environment.NewLine, Right.Select((l, i) => i == 2 ? Left[2] : l)) + Environment.NewLine;
        Assert.Equal(expected, File.ReadAllText(right));
        Assert.Equal(2, w.View.Blocks.Count);

        w.CopyAcross(1, toRight: true);
        await w.Comparing;
        Assert.True(await w.SaveAsync(1));
        Assert.Equal(expected, File.ReadAllText(right + ".bak"));
        Assert.Single(w.View.Blocks);

        var settings = ((App)Avalonia.Application.Current!).Services.Settings;
        settings.Update(s => s with { Editors = s.Editors with { Backup = false } });
        try
        {
            File.Delete(right + ".bak");
            w.CopyAcross(0, toRight: true);
            await w.Comparing;
            Assert.True(await w.SaveAsync(1));
            Assert.False(File.Exists(right + ".bak"));
        }
        finally
        {
            settings.Update(s => s with { Editors = s.Editors with { Backup = true } });
        }
        Assert.Equal("Compare — package.json · package.json", w.Title);
        w.Close();
        Assert.False(w.IsVisible);
    }

    [AvaloniaFact]
    public async Task Closing_with_lines_not_saved_asks_first()
    {
        var right = Path.Join(_right, "package.json");
        var before = File.ReadAllText(right);
        var w = await OpenWindow(Path.Join(_left, "package.json"), right);
        w.CopyAcross(2, toRight: true);
        await w.Comparing;
        w.Close();
        await Until(() => w.CloseQuestion is not null);
        var question = w.CloseQuestion!;
        Assert.Equal("Save the lines copied into package.json?", question.Title);
        Click(question.Buttons[2]);
        await Until(() => w.CloseQuestion is null);
        Assert.True(w.IsVisible);

        w.Close();
        await Until(() => w.CloseQuestion is not null);
        Click(w.CloseQuestion!.Buttons[1]);
        await Until(() => !w.IsVisible);
        Assert.Equal(before, File.ReadAllText(right));
        Assert.False(File.Exists(right + ".bak"));
    }

    [AvaloniaFact]
    public async Task Find_lights_the_matches_in_both_files_and_goes_from_one_to_the_next()
    {
        var w = await OpenWindow(Path.Join(_left, "package.json"), Path.Join(_right, "package.json"));
        Press(w, Key.F, KeyModifiers.Control);
        Assert.True(w.FindOpen);
        w.FindBox.Text = "vite";
        Dispatcher.UIThread.RunJobs();
        await w.Finding;
        Assert.Equal(6, w.View.Hits.Count);
        Assert.Equal("1 of 6", w.FindCountText);
        Assert.Equal(5, w.View.CursorRow);

        Press(w, Key.F3);
        Assert.Equal("2 of 6", w.FindCountText);
        Assert.Equal(1, w.View.Side);
        Press(w, Key.Enter);
        Assert.Equal("3 of 6", w.FindCountText);
        Press(w, Key.F3, KeyModifiers.Shift);
        Press(w, Key.F3, KeyModifiers.Shift);
        Press(w, Key.F3, KeyModifiers.Shift);
        Assert.Equal("6 of 6", w.FindCountText);

        w.FindInButtons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await w.Finding;
        Assert.Equal(3, w.View.Hits.Count);
        Assert.All(w.View.Hits, h => Assert.Equal(1, h.Side));
        w.FindInButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click(w.FindOnlyButton);
        await w.Finding;
        Assert.Equal(4, w.View.Hits.Count);
        Click(w.FindOnlyButton);
        w.WholeWordsButton.IsChecked = true;
        await w.Finding;
        Assert.Equal(4, w.View.Hits.Count);
        w.RegexButton.IsChecked = true;
        w.FindBox.Text = "(";
        Dispatcher.UIThread.RunJobs();
        await w.Finding;
        Assert.Equal("Can't search for this", w.FindCountText);

        w.RegexButton.IsChecked = false;
        w.WholeWordsButton.IsChecked = false;
        w.FindBox.Text = "deploy";
        Dispatcher.UIThread.RunJobs();
        await w.Finding;
        Assert.Equal(2, w.View.Hits.Count);
        w.CopyAcross(1, toRight: true);
        await w.Comparing;
        await w.Finding;
        Assert.Equal(4, w.View.Hits.Count);
        w.Undo();
        await w.Comparing;
        await w.Finding;

        w.FindBox.Focus();
        Press(w, Key.Escape);
        Assert.False(w.FindOpen);
        Assert.Empty(w.View.Hits);
        Assert.True(w.IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Find_in_bytes_as_text_or_hex()
    {
        var w = await OpenWindow(Path.Join(_left, "package.json"), Path.Join(_right, "package.json"));
        w.SetBinary(true);
        await w.Comparing;
        Press(w, Key.F7);
        Assert.False(w.MatchCaseButton.IsVisible);
        Assert.True(w.HexBytesButton.IsEffectivelyVisible);
        w.FindBox.Text = "LINT";
        Dispatcher.UIThread.RunJobs();
        await w.Finding;
        Assert.Equal(2, w.View.Hits.Count);
        Assert.All(w.View.Hits, h => Assert.Equal(1, h.Side));
        w.HexBytesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        w.FindBox.Text = "7B 0D 0A";
        Dispatcher.UIThread.RunJobs();
        await w.Finding;
        Assert.Equal(6, w.View.Hits.Count);
        Assert.Equal("5 of 6", w.FindCountText);
        w.FindBox.Text = "7B 0";
        Dispatcher.UIThread.RunJobs();
        await w.Finding;
        Assert.Equal("Can't search for this", w.FindCountText);
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_more_menu_copies_everything_across_and_reaches_the_rest_of_Salandual()
    {
        var clipboard = new TextClipboard();
        List<string> edited = [], shown = [];
        var options = 0;
        var compared = new List<(string, string)>();
        var host = new FileCompareHost
        {
            Clipboard = clipboard,
            Edit = edited.Add,
            ShowInPanel = shown.Add,
            OpenOptions = () => options++,
            Program = () => ("WinMerge", (l, r) => compared.Add((l, r))),
        };
        var left = Path.Join(_left, "package.json");
        var right = Path.Join(_right, "package.json");
        var w = await OpenWindow(left, right, host);
        var menu = (MenuFlyout)w.MoreButton.Flyout!;
        menu.ShowAt(w.MoreButton);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            ["Copy all the differences to the right file", "Copy all the differences to the left file", "Open the left file in the editor", "Open the right file in the editor", "Show the left file in the panel", "Show the right file in the panel", "Copy the line", "Copy both paths", "Compare in WinMerge", "Options › Compare…"],
            menu.Items.OfType<MenuItem>().Select(i => i.Header as string));
        menu.Hide();
        Dispatcher.UIThread.RunJobs();

        void Run(string header)
        {
            menu.ShowAt(w.MoreButton);
            Dispatcher.UIThread.RunJobs();
            menu.Items.OfType<MenuItem>().First(i => i.Header as string == header).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
        }

        Run("Open the right file in the editor");
        Run("Show the left file in the panel");
        Run("Copy both paths");
        Assert.Equal(left + Environment.NewLine + right, clipboard.Text);
        Run("Compare in WinMerge");
        Run("Options › Compare…");
        Assert.Equal([right], edited);
        Assert.Equal([left], shown);
        Assert.Equal([(left, right)], compared);
        Assert.Equal(1, options);
        Press(w, Key.C, KeyModifiers.Control);
        Assert.Equal(Left[2], clipboard.Text);

        Press(w, Key.Right, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);
        await w.Comparing;
        Assert.Empty(w.View.Blocks);
        Assert.Equal(Left, w.LinesOf(1));
        menu.ShowAt(w.MoreButton);
        Dispatcher.UIThread.RunJobs();
        Assert.False(menu.Items.OfType<MenuItem>().First(i => i.Header as string == "Open the left file in the editor").IsEnabled);
        Assert.False(menu.Items.OfType<MenuItem>().First(i => i.Header as string == "Copy all the differences to the right file").IsEnabled);
        menu.Hide();
        Dispatcher.UIThread.RunJobs();
        Press(w, Key.Z, KeyModifiers.Control);
        await w.Comparing;
        Assert.Equal(Right, w.LinesOf(1));
        Assert.False(w.IsModified);
        w.Close();
    }

    private sealed class TextClipboard : IClipboardAccess
    {
        public string? Text { get; private set; }

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut) => false;

        public bool SetText(string text)
        {
            Text = text;
            SequenceNumber++;
            return true;
        }

        public void Clear() => Text = null;

        public Platform.Windows.Shell.ClipboardContent? Read() => new() { Text = Text };
    }

    [AvaloniaFact]
    public async Task White_space_and_case_can_be_ignored()
    {
        File.WriteAllLines(Path.Join(_left, "code.cs"), ["int  x = 1;", "Return x;"]);
        File.WriteAllLines(Path.Join(_right, "code.cs"), ["int x=1;", "return x;"]);
        var w = await OpenWindow(Path.Join(_left, "code.cs"), Path.Join(_right, "code.cs"));
        Assert.Single(w.View.Blocks);
        Assert.Equal((0, 2), (w.View.Text!.Changed, w.View.Text.SpacingOrCase));
        Assert.Equal("1 difference, 2 lines: 2 only in spacing or case", w.SummaryText);
        Click(w.SpacesButton);
        await w.Comparing;
        Assert.Equal(1, w.View.Text!.SpacingOrCase);
        Assert.Contains("on", w.SpacesButton.Classes);
        Click(w.CaseButton);
        await w.Comparing;
        Assert.Empty(w.View.Blocks);
        Assert.Equal("No differences, with white space or case ignored.", w.SummaryText);
        Assert.Equal("No differences", w.PositionText);
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_binary_file_is_compared_as_hex_and_text_can_be_chosen()
    {
        var a = new byte[160];
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = (byte)(i % 7 == 0 ? 0 : i);
        }

        var b = a.Concat(new byte[] { 1, 2, 3, 4 }).ToArray();
        b[19] = 0x80;
        b[100] = 0x55;
        File.WriteAllBytes(Path.Join(_left, "icon.png"), a);
        File.WriteAllBytes(Path.Join(_right, "icon.png"), b);
        var w = await OpenWindow(Path.Join(_left, "icon.png"), Path.Join(_right, "icon.png"));
        Assert.True(w.View.IsBinary);
        Assert.Equal("Binary", w.KindText);
        Assert.Equal("3 differences, 2 bytes; the right file is 4 bytes longer", w.SummaryText);
        Assert.Contains("on", w.BinaryButton.Classes);
        Assert.False(w.SpacesButton.IsVisible);
        Assert.Equal("Offset 00000010", w.LineText);
        Press(w, Key.Down, KeyModifiers.Alt);
        Assert.Equal(6, w.View.CursorRow);

        Click(w.TextButton);
        await w.Comparing;
        Assert.False(w.View.IsBinary);
        Assert.Contains("on", w.TextButton.Classes);
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_same_files_say_so_and_swap_and_escape_work()
    {
        File.WriteAllText(Path.Join(_left, "same.txt"), "a\r\nb\r\n");
        File.WriteAllText(Path.Join(_right, "same.txt"), "a\nb\n");
        var w = await OpenWindow(Path.Join(_left, "same.txt"), Path.Join(_right, "same.txt"));
        Assert.Equal("The text is the same; the encoding or the line breaks differ.", w.SummaryText);
        w.Swap();
        await w.Comparing;
        Assert.Equal(Path.Join(_right, "same.txt"), w.LeftPath);
        Press(w, Key.Escape);
        Assert.False(w.IsVisible);
    }

    [AvaloniaFact]
    public async Task CtrlShiftC_compares_two_selected_files_the_same_name_in_the_other_panel_or_the_two_cursors()
    {
        File.WriteAllText(Path.Join(_left, "a.txt"), "a");
        File.WriteAllText(Path.Join(_left, "b.txt"), "b");
        File.WriteAllText(Path.Join(_right, "z.txt"), "z");
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_left);
        await w.RightPanel.Panel.NavigateAsync(_right);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        var l = w.LeftPanel.Panel;
        int Index(Core.Panels.FilePanel p, string name) => Enumerable.Range(0, p.Count).First(i => p.Items[i].Name == name);

        l.Toggle(Index(l, "a.txt"));
        l.Toggle(Index(l, "b.txt"));
        Assert.Equal((Path.Join(_left, "a.txt"), Path.Join(_left, "b.txt")), w.FilesToCompare());
        l.Toggle(Index(l, "a.txt"));
        l.Toggle(Index(l, "b.txt"));

        l.SetFocus(Index(l, "package.json"));
        Assert.Equal((Path.Join(_left, "package.json"), Path.Join(_right, "package.json")), w.FilesToCompare());

        l.SetFocus(Index(l, "a.txt"));
        var r = w.RightPanel.Panel;
        r.SetFocus(Index(r, "z.txt"));
        Assert.Equal((Path.Join(_left, "a.txt"), Path.Join(_right, "z.txt")), w.FilesToCompare());

        w.LeftPanel.View.Focus();
        w.KeyPress(Key.C, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
        w.KeyRelease(Key.C, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
        await Until(() => w.FileCompare is not null);
        Assert.Equal(Path.Join(_right, "z.txt"), w.FileCompare!.RightPath);
        w.FileCompare.Close();

        var settings = w.Services.Settings;
        List<(string Program, IReadOnlyList<string> Arguments)> started = [];
        w.StartProgramForTests = (program, arguments) =>
        {
            started.Add((program, arguments));
            return Platform.Windows.Shell.StartProblem.None;
        };
        try
        {
            settings.Update(s => s with { Compare = s.Compare with { Files = s.Compare.Files with { UseProgram = true, Program = @"C:\Tools\WinMerge\WinMergeU.exe", ProgramArguments = "/u /e \"$(Left)\" \"$(Right)\" /dl $(LeftName)" } } });
            w.CompareFiles();
            Assert.Equal(@"C:\Tools\WinMerge\WinMergeU.exe", Assert.Single(started).Program);
            Assert.Equal(["/u", "/e", Path.Join(_left, "a.txt"), Path.Join(_right, "z.txt"), "/dl", "a.txt"], started[0].Arguments);
            Assert.Null(w.FileCompare);

            settings.Update(s => s with { Compare = s.Compare with { Files = s.Compare.Files with { Program = @"C:\Windows\System32\cmd.exe", ProgramArguments = "/c \"$(Left)\"" } } });
            w.CompareFiles();
            Assert.Single(started);
            Assert.StartsWith("cmd.exe is a command shell", w.NoticeText.Text, StringComparison.Ordinal);
        }
        finally
        {
            settings.Update(s => s with { Compare = s.Compare with { Files = new() } });
            w.StartProgramForTests = null;
        }

        r.SetFocus(0);
        l.SetFocus(0);
        Assert.Null(w.FilesToCompare());
        Assert.True(w.Run("Compare.Files", Core.Commands.PanelTarget.Active));
        Assert.Equal("Select two files, or put the cursor on a file in each panel.", w.NoticeText.Text);
        w.CloseNow();
    }
}

public sealed class FileCompareCaptureTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-filecompare-cap-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [AvaloniaFact]
    public async Task Capture_TextAndBinary_LightAndDark()
    {
        var left = Path.Join(Directory.CreateDirectory(Path.Join(_dir, "Website")).FullName, "package.json");
        var right = Path.Join(Directory.CreateDirectory(Path.Join(_dir, "Backup")).FullName, "package.json");
        File.WriteAllLines(left,
        [
            "{", "  \"name\": \"website\",", "  \"version\": \"1.4.0\",", "  \"private\": true,", "  \"scripts\": {", "    \"build\": \"vite build\",",
            "    \"deploy\": \"pwsh ./deploy.ps1\",", "    \"test\": \"vitest\"", "  },", "  \"dependencies\": {", "    \"marked\":  \"12.0.2\",", "    \"vite\": \"5.4.8\"", "  },", "  \"License\": \"MIT\"", "}",
        ]);
        File.WriteAllLines(right,
        [
            "{", "  \"name\": \"website\",", "  \"version\": \"1.5.0\",", "  \"private\": true,", "  \"scripts\": {", "    \"build\": \"vite build\",",
            "    \"test\": \"vitest run\",", "    \"lint\": \"eslint .\"", "  },", "  \"dependencies\": {", "    \"marked\": \"12.0.2\",", "    \"vite\": \"5.4.10\"", "  },", "  \"license\": \"MIT\"", "}",
        ]);
        var services = ((App)Avalonia.Application.Current!).Services;
        var w = new FileCompareWindow(services.Settings, left, right) { Width = 1240, Height = 560 };
        w.Show();
        for (var i = 0; i < 300 && w.View.RowCount == 0; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        await w.Comparing;
        Dispatcher.UIThread.RunJobs();
        Save(w, "filecompare-light.png");
        w.CopyAcross(1, toRight: true);
        await w.Comparing;
        w.View.SelectLine(w.View.Blocks[1].Row, extend: false);
        Dispatcher.UIThread.RunJobs();
        Save(w, "filecompare-copied.png");
        w.View.ClearSelection();
        w.OpenFind();
        w.FindBox.Text = "vite";
        Dispatcher.UIThread.RunJobs();
        await w.Finding;
        Dispatcher.UIThread.RunJobs();
        Save(w, "filecompare-find.png");
        w.CloseFind();
        w.Undo();
        await w.Comparing;
        Dispatcher.UIThread.RunJobs();
        ((App)Avalonia.Application.Current!).Theme.Apply(Core.Settings.AppearanceSettings.Fixed(Core.Settings.ThemeCatalog.SalandualDarkId));
        Dispatcher.UIThread.RunJobs();
        Save(w, "filecompare-dark.png");
        ((App)Avalonia.Application.Current!).Theme.Apply(Core.Settings.AppearanceSettings.Fixed("github-dark"));
        Dispatcher.UIThread.RunJobs();
        Save(w, "filecompare-github-dark.png");
        ((App)Avalonia.Application.Current!).Theme.Apply(new Core.Settings.AppearanceSettings());
        w.SetBinary(true);
        await w.Comparing;
        Dispatcher.UIThread.RunJobs();
        Save(w, "filecompare-binary.png");
        w.Close();
    }

    private static void Save(Window w, string name)
    {
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
}
