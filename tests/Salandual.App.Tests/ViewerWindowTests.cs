// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ViewerWindowTests.cs

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Viewer;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class ViewerWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-viewer-").FullName;

    public ViewerWindowTests()
    {
        File.WriteAllText(Path.Join(_dir, "a-notes.txt"), "first\r\nMagyarán line two\r\nthird magyarán\r\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Join(_dir, "b-build.log"), string.Concat(Enumerable.Range(1, 20000).Select(i => $"2026-08-14 line {i} Salandual\n")));
        File.WriteAllBytes(Path.Join(_dir, "c-data.bin"), [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xB8, 0x00, 0x41, 0x42]);
        File.WriteAllText(Path.Join(_dir, "d-readme.md"), "# Title\n");
        Directory.CreateDirectory(Path.Join(_dir, "e-folder"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open(string focus)
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir, focus);
        await w.RightPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(TopLevel w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
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

    private static async Task<ViewerWindow> Viewer(MainWindow w, Key key = Key.F3, RawInputModifiers mods = RawInputModifiers.None)
    {
        Press(w, key, mods);
        await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Document is not null);
        var v = w.Viewers[^1];
        v.Width = 1100;
        v.Height = 700;
        v.CaptureRenderedFrame();
        return v;
    }

    [AvaloniaFact]
    public async Task F3_OpensTheText_WithTheTitleCounterAndStatus()
    {
        var w = await Open("a-notes.txt");
        var v = await Viewer(w);

        Assert.False(v.IsHex);
        Assert.Equal($"a-notes.txt — {_dir}", v.Title);
        Assert.Equal(TextEncodings.Utf8, v.Document!.Encoding.Id);
        await v.Document.Index.Completion;
        Assert.Equal(3, v.Document.Index.Count);
        Assert.Equal("CRLF", v.Document.LineEnding);

        Assert.Equal("1 / 3 text files", v.Counter.Text);
        await v.StepFileAsync(1);
        Assert.Equal(Path.Join(_dir, "b-build.log"), v.FilePath);
        Press(v, Key.Back);
        await Until(() => v.FilePath.EndsWith("a-notes.txt", StringComparison.Ordinal));

        v.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        await Until(() => w.Viewers.Count == 0);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ABinaryFile_OpensInHex_AndF5F4Switch()
    {
        var w = await Open("c-data.bin");
        var v = await Viewer(w);
        Assert.True(v.Document!.Guess.IsBinary);
        Assert.True(v.IsHex);

        Press(v, Key.F5);
        Assert.False(v.IsHex);
        Press(v, Key.F4);
        Assert.True(v.IsHex);
        Assert.Equal(0, v.HexView.OffsetAt(new Avalonia.Point(150, v.HexView.RowHeight + 2)) is { } o ? o / 16 : -1);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AltF3_OpensHex_AndCtrlShiftF3_AsksWhich()
    {
        var w = await Open("a-notes.txt");
        var hex = await Viewer(w, Key.F3, RawInputModifiers.Alt);
        Assert.True(hex.IsHex);
        hex.Close();
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();

        Press(w, Key.F3, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Contains(w.ViewWithMenu!.Items.OfType<MenuItem>(), m => (string?)m.Header == Resources.AppStrings.ViewerKind_Hex);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task From_Find_the_viewer_opens_at_the_first_match_and_F3_goes_on()
    {
        var w = await Open("a-notes.txt");
        var notes = Path.Join(_dir, "a-notes.txt");
        var v = w.OpenViewer(w.LeftPanel, notes, ViewerKind.Text, siblings: [notes], findText: new FindQuery("magyarán"));
        await v.Loading;
        await Until(() => v.HitsText == "1 of 2");
        Assert.Equal("Magyarán", v.Document!.Text(v.Current.SelectionStart, v.Current.SelectionEnd));
        Assert.True(v.FindBox.IsEffectivelyVisible);
        Assert.Equal("magyarán", v.FindBox.Text);

        Press(v, Key.F3);
        await v.Finding;
        await Until(() => v.HitsText == "2 of 2");
        v.Close();

        w.Services.Settings.Update(s => s with { Find = s.Find with { ViewerKeepsText = false } });
        v = w.OpenViewer(w.LeftPanel, notes, ViewerKind.Text, siblings: [notes], findText: new FindQuery("third"));
        await v.Loading;
        Assert.Equal("third", v.Document!.Text(v.Current.SelectionStart, v.Current.SelectionEnd));
        Assert.False(v.FindBox.IsEffectivelyVisible);
        Assert.Equal("", v.FindBox.Text);
        v.Close();

        w.Services.Settings.Update(s => s with { Find = s.Find with { ViewAtMatch = false } });
        v = w.OpenViewer(w.LeftPanel, notes, ViewerKind.Text, siblings: [notes], findText: new FindQuery("third"));
        await v.Loading;
        Assert.False(v.Current.HasSelection);
        Assert.False(v.FindBox.IsEffectivelyVisible);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Find_LightsUpTheMatch_CountsThem_AndF3GoesOn()
    {
        var w = await Open("a-notes.txt");
        var v = await Viewer(w);
        Press(v, Key.F, RawInputModifiers.Control);
        v.FindBox.Text = "magyarán";
        await v.FindAsync(forward: true, from: 0);
        await Until(() => v.HitsText == "1 of 2");
        var d = v.Document!;
        Assert.Equal("Magyarán", d.Text(v.Current.SelectionStart, v.Current.SelectionEnd));

        Press(v, Key.F3);
        await v.Finding;
        await Until(() => v.HitsText == "2 of 2");
        Press(v, Key.F3);
        await v.Finding;
        await Until(() => v.HitsText == "1 of 2");

        v.FindBox.Text = "nothing like this";
        await v.FindAsync(forward: true, from: 0);
        Assert.Equal(Resources.AppStrings.Viewer_NoHits, v.HitsText);

        Press(v, Key.Escape);
        Assert.False(v.FindBox.IsEffectivelyVisible);
        Assert.Single(w.Viewers);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task GoTo_ALineDeepInAFile_AndAnOffsetInHex()
    {
        var w = await Open("b-build.log");
        var v = await Viewer(w);
        Assert.True(await v.GoToAsync("15000"));
        var d = v.Document!;
        Assert.Equal(14999, d.Index.NumberOf(v.TextView.CaretLineStart));
        Assert.StartsWith("2026-08-14 line 15000 ", d.Text(v.TextView.CaretLineStart, v.TextView.CaretLineStart + 30), StringComparison.Ordinal);
        Assert.False(await v.GoToAsync("line?"));

        v.SetMode(true);
        Assert.True(await v.GoToAsync("0x20"));
        Assert.Equal(0x20, v.Current.Caret);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Copy_PutsTheTextOrTheHexOnTheClipboard()
    {
        var w = await Open("a-notes.txt");
        var clipboard = new FakeClipboard();
        w.FileClipboardAccess = clipboard;
        var v = await Viewer(w);
        v.Current.Select(7, 16);
        Press(v, Key.C, RawInputModifiers.Control);
        Assert.Equal("Magyarán", clipboard.Text);

        v.SetMode(true);
        v.Current.Select(7, 10);
        Press(v, Key.C, RawInputModifiers.Control);
        Assert.Equal("4D 61 67", clipboard.Text);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Keys_ChangeTheSharedSettings_AndTheEncoding()
    {
        var w = await Open("a-notes.txt");
        var v = await Viewer(w);
        var wrap = w.Services.Settings.Current.Viewers.Wrap;
        Press(v, Key.F2);
        await Until(() => w.Services.Settings.Current.Viewers.Wrap != wrap);
        Assert.Equal(!wrap, v.TextView.Wrap);

        var before = v.Document!.Encoding;
        Press(v, Key.F8);
        Assert.NotEqual(before.Id, v.Document.Encoding.Id);
        Assert.False(v.Document.IsDetected);
        Press(v, Key.F8, RawInputModifiers.Shift);
        Assert.Equal(before.Id, v.Document.Encoding.Id);

        w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { FindBar = FindBarPosition.Bottom } });
        await Until(() => v.FindBarAtBottom);
        Assert.Contains(v.BuildMoreMenu().Items.OfType<MenuItem>(), m => (string?)m.Header == Resources.AppStrings.Viewer_FindAtBottom && m.IsChecked);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheNextViewer_OpensAsTheLastOneClosed_MaximizedOrSized()
    {
        var w = await Open("a-notes.txt");
        var v = await Viewer(w);
        v.WindowState = WindowState.Maximized;
        Dispatcher.UIThread.RunJobs();
        v.Close();
        Assert.True(w.Services.Settings.Current.Viewers.WindowMaximized);

        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        Press(w, Key.F3);
        await Until(() => w.Viewers.Count > 0);
        Assert.Equal(WindowState.Maximized, w.Viewers[^1].WindowState);
        w.Viewers[^1].WindowState = WindowState.Normal;
        w.Viewers[^1].Close();
        Assert.False(w.Services.Settings.Current.Viewers.WindowMaximized);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task HexOffsets_SwitchToDecimal_WithAClickOnOffset()
    {
        var w = await Open("c-data.bin");
        var v = await Viewer(w);
        Assert.False(v.HexView.DecimalOffsets);
        var at = v.HexView.TranslatePoint(new Avalonia.Point(20, 5), v)!.Value;
        v.MouseDown(at, MouseButton.Left);
        v.MouseUp(at, MouseButton.Left);
        await Until(() => w.Services.Settings.Current.Viewers.DecimalOffsets);
        Assert.True(v.HexView.DecimalOffsets);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task HexStatus_EachValueHexOrDecimal_ClickedWithTheMouse_AndCopiedEitherWay()
    {
        var w = await Open("c-data.bin");
        var clipboard = new FakeClipboard();
        w.FileClipboardAccess = clipboard;
        var v = await Viewer(w);
        v.Current.Select(2, 16);
        Dispatcher.UIThread.RunJobs();
        string[] Texts() => [.. v.StatusCells.Select(c => ((TextBlock)c.Child!).Text ?? "")];
        Border Cell(string start) => v.StatusCells.First(c => ((TextBlock)c.Child!).Text!.StartsWith(start, StringComparison.Ordinal));
        Assert.Contains("Offset 0x00000010", Texts());
        Assert.Contains("0xE bytes selected", Texts());
        Assert.Contains("0x12 bytes", Texts());

        var offsetCell = Cell("Offset");
        await Task.Delay(500);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(offsetCell, Cell("Offset"));

        var at = offsetCell.TranslatePoint(new Point(10, offsetCell.Bounds.Height / 2), v)!.Value;
        v.MouseMove(at);
        v.MouseDown(at, MouseButton.Left);
        v.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(v.LastStatusMenu?.IsOpen);
        var items = v.LastStatusMenu!.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Hex: 0x10", "Decimal: 16", Resources.AppStrings.Viewer_CopyValue], items.Select(i => (string?)i.Header));
        items[2].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("0x10", clipboard.Text);

        items[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => Texts().Contains("Offset 16"));
        Assert.Contains("0xE bytes selected", Texts());
        Assert.Contains("0x12 bytes", Texts());
        Assert.True(v.HexView.DecimalOffsets);

        var sizeCell = v.StatusCells.First(c => ((TextBlock)c.Child!).Text == "0x12 bytes");
        ((Action<Control>)sizeCell.Tag!)(sizeCell);
        Assert.Equal(4, v.LastStatusMenu!.Items.OfType<MenuItem>().Count());

        var all = v.BuildMoreMenu().Items.OfType<MenuItem>().Single(m => (string?)m.Header == Resources.AppStrings.Viewer_Numbers)
            .Items.OfType<MenuItem>().First(m => (string?)m.Header == Resources.AppStrings.Viewer_AllDecimal);
        all.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => Texts().Contains("14 bytes selected"));
        Assert.Contains("18 bytes", Texts());
        Assert.Contains("Offset 16", Texts());

        var numbers = v.BuildMoreMenu().Items.OfType<MenuItem>().Single(m => (string?)m.Header == Resources.AppStrings.Viewer_Numbers);
        Assert.Equal(6, numbers.Items.Count);
        Assert.DoesNotContain(v.BuildMoreMenu().Items.OfType<MenuItem>(), m => (string?)m.Header is { } h && (h == Resources.AppStrings.Viewer_BytesTip || h == Resources.AppStrings.Viewer_GroupsTip));
        Assert.Contains(v.StatusCells, c => ((TextBlock)c.Child!).Text == "16 bytes per row" && c.Tag is Action<Control>);
        Assert.Contains(v.StatusCells, c => ((TextBlock)c.Child!).Text == "groups of 8" && c.Tag is Action<Control>);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ByteColours_FromTheMoreMenu_ColourTheHexViewByKind()
    {
        var w = await Open("c-data.bin");
        var v = await Viewer(w);
        Assert.Equal(ByteColors.Two, v.HexView.ByteColors);

        var colours = v.BuildMoreMenu().Items.OfType<MenuItem>().Single(m => (string?)m.Header == Resources.AppStrings.Viewer_ByteColors);
        var items = colours.Items.OfType<MenuItem>().ToList();
        Assert.Equal([Resources.AppStrings.Viewer_ByteColors_Two, Resources.AppStrings.Viewer_ByteColors_ByKind, Resources.AppStrings.Viewer_ByteColors_Change], items.Select(m => (string?)m.Header));
        Assert.True(items[0].IsChecked);
        items[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => v.HexView.ByteColors == ByteColors.ByKind);
        Assert.Equal(ByteColors.ByKind, w.Services.Settings.Current.Viewers.HexByteColors);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task OptionsViewers_SaysWhenARowAboveTakesAMask()
    {
        var w = new MainWindow();
        var options = new Options.OptionsWindow(w.Services) { Width = 1000, Height = 760 };
        w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { Masks = [.. ViewerSettings.DefaultMasks().Take(3), new ViewerMask { Mask = "*.svg;*.txt" }, new ViewerMask()] } });
        options.Show();
        options.ShowCategory("viewers");
        Dispatcher.UIThread.RunJobs();
        var note = options.GetVisualDescendants().OfType<TextBlock>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == Resources.AppStrings.Opt_ViewerMask_TakenName);
        Assert.Equal("*.svg already goes to row 1 above, so this row never gets it.", note.Text);
        options.Close();
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Viewers_AreTheirOwnWindows_BesideSalandual_NotOnTopOfIt()
    {
        var w = await Open("a-notes.txt");
        var first = await Viewer(w);
        Assert.Null(first.Owner);
        Assert.True(first.ShowInTaskbar);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        var second = await Viewer(w);
        Assert.NotEqual(first.Position, second.Position);
        second.Close();
        first.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task RightClick_MovesTheCaretOutsideTheSelection_AndTheMenuCopiesAndFinds()
    {
        var w = await Open("c-data.bin");
        var clipboard = new FakeClipboard();
        w.FileClipboardAccess = clipboard;
        var v = await Viewer(w);
        v.Current.Select(0, 2);
        var menu = v.BuildContextMenu();
        var copyAs = menu.Items.OfType<MenuItem>().First(m => (string?)m.Header == Resources.AppStrings.Viewer_Menu_CopyAs);
        copyAs.Items.OfType<MenuItem>().First(m => (string?)m.Header == Resources.AppStrings.Viewer_CopyAs_C).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("{ 0x4D, 0x5A }", clipboard.Text);
        copyAs.Items.OfType<MenuItem>().First(m => (string?)m.Header == Resources.AppStrings.Viewer_CopyAs_Base64).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("TVo=", clipboard.Text);

        v.Current.Select(5, 7);
        v.FindSelectedBytes();
        await v.Finding;
        Assert.Equal("00 00", v.FindBox.Text);
        Assert.Equal(6, v.Current.SelectionStart);

        var at = v.HexView.TranslatePoint(new Point(150, v.HexView.RowHeight * 2.5), v)!.Value;
        v.MouseDown(at, MouseButton.Right);
        v.MouseUp(at, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.False(v.Current.HasSelection);
        Assert.Equal(16, v.Current.Caret / 16 * 16);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task SelectFromHereTo_ExtendsFromWhereTheSelectionStarted()
    {
        var w = await Open("b-build.log");
        var v = await Viewer(w);
        v.SetMode(true);
        v.Current.Select(0x10, 0x10);
        Assert.True(await v.GoToAsync("0x40", extend: true));
        Assert.Equal((0x10L, 0x40L), (v.Current.SelectionStart, v.Current.SelectionEnd));
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task GoToInHex_HasAHexDecSwitch_ThatConvertsWhatIsTyped()
    {
        var w = await Open("b-build.log");
        var v = await Viewer(w);
        v.SetMode(true);
        v.OpenGoTo();
        Dispatcher.UIThread.RunJobs();
        var (hex, dec) = v.GoToBase!.Value;
        Assert.True(hex.IsChecked);
        Assert.Equal("0x", Assert.IsType<TextBlock>(v.GoToBox!.InnerLeftContent).Text);

        v.GoToBox.Text = "40";
        dec.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("64", v.GoToBox.Text);
        Assert.True(dec.IsChecked);
        Assert.False(((TextBlock)v.GoToBox.InnerLeftContent!).IsVisible);
        hex.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("40", v.GoToBox.Text);

        Assert.True(await v.GoToAsync("40", hexDigits: true));
        Assert.Equal(0x40, v.Current.Caret);
        Assert.True(await v.GoToAsync("40", hexDigits: false));
        Assert.Equal(40, v.Current.Caret);
        Assert.False(await v.GoToAsync("4A", hexDigits: false));
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task OptionsViewers_ShowsTheMaskTable()
    {
        var options = new Options.OptionsWindow { Width = 1000, Height = 760 };
        options.Show();
        options.ShowCategory("viewers");
        Dispatcher.UIThread.RunJobs();
        var boxes = options.GetVisualDescendants().OfType<TextBox>().Where(t => Avalonia.Automation.AutomationProperties.GetName(t) == Resources.AppStrings.Opt_ViewerMask_Mask).ToList();
        Assert.Equal(ViewerSettings.DefaultMasks().Count, boxes.Count);
        Assert.Equal("*.md;*.markdown", boxes[2].Text);
        options.Close();
        await Task.CompletedTask;
    }

    private sealed class FakeClipboard : IClipboardAccess
    {
        public string? Text { get; private set; }

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut) => true;

        public bool SetText(string text)
        {
            Text = text;
            SequenceNumber++;
            return true;
        }

        public void Clear() => Text = null;

        public ClipboardContent? Read() => new() { Text = Text };
    }
}
