// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/EditorWindowTests.cs

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Editor;
using Salandual.App.Options;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.App.Tests;

public sealed class EditorWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-editor-").FullName;

    public EditorWindowTests()
    {
        File.WriteAllText(Path.Join(_dir, "a-settings.json"), "{\r\n  \"name\": \"Salandual\",\r\n  \"rows\": 24\r\n}\r\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Join(_dir, "b-notes.txt"), "alpha beta\nalpha gamma\nalpha\n", new UTF8Encoding(false));
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

    private static async Task<EditorWindow> Editor(MainWindow w)
    {
        Press(w, Key.F4);
        await Until(() => w.Editors.Count > 0 && w.Editors[^1].IsLoaded);
        var e = w.Editors[^1];
        await e.Loading;
        Dispatcher.UIThread.RunJobs();
        return e;
    }

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static SettingsService SettingsOf(MainWindow w) => w.Services.Settings;

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task F4_OpensTheFile_WithItsColoursAndStatus_AndAgainBringsItForward()
    {
        var w = await Open("a-settings.json");
        var e = await Editor(w);
        e.Width = 1000;
        e.Height = 600;

        Assert.Equal($"a-settings.json — {_dir}", e.Title);
        Assert.Equal(SyntaxLanguage.Json, e.Language);
        Assert.Equal(0, e.Editor.TextArea.Caret.Offset);
        Assert.True(e.Editor.TextArea.IsFocused);
        Assert.Equal("{\r\n  \"name\": \"Salandual\",\r\n  \"rows\": 24\r\n}\r\n", e.Editor.Document.Text);
        Assert.Equal((TextEncodings.Utf8, false, LineEnding.CrLf, IndentStyle.Spaces2), (e.File.Encoding.Id, e.File.Bom, e.File.LineEnding, e.File.Indent));
        var status = e.StatusTexts.Select(t => t.Text).ToList();
        Assert.Contains("Ln 1, Col 1", status);
        Assert.Contains("JSON", status);
        Assert.Contains("Spaces: 2", status);
        Assert.Contains("UTF-8", status);
        Assert.Contains("CRLF", status);
        Assert.False(e.IsModified);
        Capture(e, "editor-json.png");

        w.LeftPanel.View.Focus();
        Press(w, Key.F4);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(w.Editors);

        await w.LeftPanel.Panel.NavigateAsync(_dir, "b-notes.txt");
        w.LeftPanel.View.Focus();
        var plain = await Editor(w);
        Assert.Equal(SyntaxLanguage.None, plain.Language);
        Assert.Equal(LineEnding.Lf, plain.File.LineEnding);
        Assert.Equal(2, w.Editors.Count);
        plain.Close();
        e.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Save_WritesTheFile_KeepsABackupOnce_AndTheTitleShowsChanges()
    {
        var w = await Open("a-settings.json");
        var e = await Editor(w);
        var path = Path.Join(_dir, "a-settings.json");
        var original = File.ReadAllText(path);

        e.Editor.Document.Insert(e.Editor.Document.Text.IndexOf("24", StringComparison.Ordinal), "1");
        Dispatcher.UIThread.RunJobs();
        Assert.True(e.IsModified);
        Assert.StartsWith("● ", e.Title, StringComparison.Ordinal);
        Assert.Contains("Modified", e.StatusTexts.Select(t => t.Text));

        Press(e, Key.S, RawInputModifiers.Control);
        await Until(() => !e.IsModified);
        Assert.Contains("\"rows\": 124", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(path + ".bak"));
        Assert.DoesNotContain("●", e.Title, StringComparison.Ordinal);

        var first = File.ReadAllText(path);
        e.Editor.Document.Insert(0, " ");
        Assert.True(await e.SaveAsync());
        Assert.Equal(first, File.ReadAllText(path + ".bak"));
        Assert.Empty(Directory.GetFiles(_dir, "*.saving"));

        e.SetLineEnding(LineEnding.Lf);
        Assert.True(e.IsModified);
        Assert.True(await e.SaveAsync());
        Assert.DoesNotContain("\r\n", File.ReadAllText(path), StringComparison.Ordinal);
        e.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ClosingWithChanges_Asks_DontSaveLeavesTheFile_CancelKeepsTheWindow()
    {
        var w = await Open("b-notes.txt");
        var e = await Editor(w);
        var path = Path.Join(_dir, "b-notes.txt");
        e.Editor.Document.Insert(0, "changed ");
        Dispatcher.UIThread.RunJobs();

        Press(e, Key.F4, RawInputModifiers.Control);
        await Until(() => e.CloseQuestion is not null);
        var question = e.CloseQuestion!;
        Assert.Equal("Save the changes to b-notes.txt?", question.Title);
        Assert.Equal(3, question.Buttons.Count);
        await Until(() => question.Buttons[0].IsFocused);
        Press(question, Key.Right);
        Press(question, Key.Right);
        Press(question, Key.Right);
        Assert.True(question.Buttons[2].IsFocused);
        Press(question, Key.Left);
        Assert.True(question.Buttons[1].IsFocused);
        Press(question, Key.Right);
        question.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        await Until(() => e.CloseQuestion is null);
        Assert.Single(w.Editors);

        e.Close();
        await Until(() => e.CloseQuestion is not null);
        Click(e.CloseQuestion!.Buttons[1]);
        await Until(() => w.Editors.Count == 0);
        Assert.Equal("alpha beta\nalpha gamma\nalpha\n", File.ReadAllText(path));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ClosingSalandual_AsksForAnEditorWithChanges()
    {
        var w = await Open("b-notes.txt");
        var e = await Editor(w);
        e.Editor.Document.Insert(0, "changed ");
        Dispatcher.UIThread.RunJobs();

        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.CloseNow();
        await Until(() => e.CloseQuestion is not null);
        Click(e.CloseQuestion!.Buttons[2]);
        await Until(() => e.CloseQuestion is null);
        Assert.False(closed);
        Assert.Single(w.Editors);

        w.CloseNow();
        await Until(() => e.CloseQuestion is not null);
        Click(e.CloseQuestion!.Buttons[1]);
        await Until(() => closed);
        Assert.Empty(w.Editors);
        Assert.Equal("alpha beta\nalpha gamma\nalpha\n", File.ReadAllText(Path.Join(_dir, "b-notes.txt")));
    }

    [AvaloniaFact]
    public async Task AnotherProgramsChange_ShowsABar_ReloadBringsItIn_UndoGoesBack()
    {
        var w = await Open("b-notes.txt");
        var e = await Editor(w);
        var path = Path.Join(_dir, "b-notes.txt");
        File.WriteAllText(path, "from another program\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        e.CheckDiskNow();
        Assert.True(e.DiskBar.IsVisible);
        await e.ReloadAsync();
        Assert.Equal("from another program\n", e.Editor.Document.Text);
        Assert.False(e.DiskBar.IsVisible);
        Assert.False(e.IsModified);

        e.Editor.Undo();
        Assert.Equal("alpha beta\nalpha gamma\nalpha\n", e.Editor.Document.Text);
        Assert.True(e.IsModified);

        File.Delete(path);
        e.CheckDiskNow();
        Assert.True(e.DiskBar.IsVisible);
        Assert.True(await e.SaveAsync());
        Assert.True(File.Exists(path));
        e.Close();
        await Until(() => e.CloseQuestion is null && w.Editors.Count == 0);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task FindAndReplace_CountsMatches_ReplaceAllIsOneUndo()
    {
        var w = await Open("b-notes.txt");
        var e = await Editor(w);

        Press(e, Key.H, RawInputModifiers.Control);
        Assert.True(e.FindBarOpen);
        Assert.True(e.ReplaceOpen);
        e.FindBox.Text = "alpha";
        e.Search(select: true);
        Assert.Equal(3, e.Matches.Count);
        Assert.Equal("1 of 3", e.HitsText);
        e.FindNext(forward: true);
        Assert.Equal("2 of 3", e.HitsText);
        Assert.Equal("alpha", e.Editor.SelectedText);
        Capture(e, "editor-replace.png");

        e.ReplaceBox.Text = "omega";
        e.ReplaceAll();
        Assert.Equal("omega beta\nomega gamma\nomega\n", e.Editor.Document.Text);
        Assert.Equal("3 replaced", e.HitsText);
        e.Editor.Undo();
        Assert.Equal("alpha beta\nalpha gamma\nalpha\n", e.Editor.Document.Text);

        e.Editor.Select(0, 0);
        e.FindBox.Text = @"alpha (\w+)";
        e.Search(select: false);
        Assert.Empty(e.Matches);
        e.RegexFlag.IsChecked = true;
        e.Search(select: true);
        Assert.Equal(2, e.Matches.Count);
        Assert.Equal("alpha beta", e.Editor.SelectedText);
        Press(e, Key.Escape);
        Assert.False(e.FindBarOpen);
        e.Editor.Select(0, 10);
        Press(e, Key.H, RawInputModifiers.Control);
        Assert.Equal(@"alpha (\w+)", e.FindBox.Text);
        e.ReplaceBox.Text = "$1";
        e.ReplaceAll();
        Assert.Equal("beta\ngamma\nalpha\n", e.Editor.Document.Text);
        e.Editor.Undo();

        e.FindBox.Text = @"(?<first>alpha) (\w+)";
        e.ReplaceBox.Text = "${first}:$1 $$5 [$0]";
        e.ReplaceAll();
        Assert.Equal("alpha:beta $5 [alpha beta]\nalpha:gamma $5 [alpha gamma]\nalpha\n", e.Editor.Document.Text);
        e.Editor.Undo();
        Assert.Equal("alpha beta\nalpha gamma\nalpha\n", e.Editor.Document.Text);
        e.FindBox.Text = @"alpha (\w+)";

        Press(e, Key.Escape);
        e.Editor.Select(6, 4);
        Press(e, Key.F, RawInputModifiers.Control);
        Assert.Equal("beta", e.FindBox.Text);
        Press(e, Key.Escape);
        Assert.False(e.FindBarOpen);

        Assert.Null(e.Editor.FocusAdorner);
        Assert.Null(e.Editor.TextArea.FocusAdorner);

        Assert.True(e.GoTo("2:7"));
        Assert.Equal((2, 7), (e.Editor.TextArea.Caret.Line, e.Editor.TextArea.Caret.Column));
        Assert.False(e.GoTo("two"));
        e.Close();
        await Until(() => e.CloseQuestion is not null || w.Editors.Count == 0);
        if (e.CloseQuestion is { } q)
        {
            Click(q.Buttons[1]);
        }

        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task SizeLimits_PlainTextOverTheColourLimit_AndTooBigAsksFirst()
    {
        var w = await Open("a-settings.json");
        var big = Path.Join(_dir, "c-big.json");
        File.WriteAllText(big, "[" + string.Concat(Enumerable.Repeat("\"0123456789\",\n", 120_000)) + "1]");
        SettingsOf(w).Update(s => s with
        {
            Editors = s.Editors with { Masks = [new EditorMask { Mask = "*.json", Colors = SyntaxLanguage.Json, ColorsUpToMB = 1, LargestMB = 3 }, new EditorMask()] },
        });

        await w.LeftPanel.Panel.NavigateAsync(_dir, "c-big.json");
        w.LeftPanel.View.Focus();
        var e = await Editor(w);
        Assert.Equal(SyntaxLanguage.None, e.Language);
        Assert.True(e.SizeNotice.IsVisible);
        e.Close();
        await Until(() => w.Editors.Count == 0);

        SettingsOf(w).Update(s => s with { Editors = s.Editors with { Masks = [s.Editors.Masks[0] with { LargestMB = 1 }, new EditorMask()] } });
        w.LeftPanel.View.Focus();
        Press(w, Key.F4);
        await Until(() => w.TooBigQuestion is not null);
        Assert.Equal("c-big.json is too big to edit here", w.TooBigQuestion!.Title);
        Click(w.TooBigQuestion.Buttons.First(b => (string?)b.Tag == "anyway"));
        await Until(() => w.Editors.Count == 1 && w.Editors[0].IsLoaded);
        await w.Editors[0].Loading;
        Assert.Equal(SyntaxLanguage.None, w.Editors[0].Language);
        w.Editors[0].Close();
        await Until(() => w.Editors.Count == 0);

        Press(w, Key.F4);
        await Until(() => w.TooBigQuestion is not null);
        Click(w.TooBigQuestion!.Buttons.First(b => (string?)b.Tag == "cancel"));
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(w.Editors);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task OptionsEditors_HasTheTable_AndItsSettingsReachTheEditor()
    {
        var o = new OptionsWindow { Width = 1100, Height = 900 };
        o.Show();
        o.ShowCategory("editors");
        Dispatcher.UIThread.RunJobs();
        var grid = o.EditorMaskGrid!;
        Assert.Equal(6, grid.Children.OfType<ComboBox>().Count());
        Assert.Contains(grid.Children.OfType<ComboBox>(), c => (c.SelectedItem as ComboBoxItem)?.Content as string == "Internal · YAML");
        Capture(o, "options-editors.png");

        o.Width = 760;
        Dispatcher.UIThread.RunJobs();
        o.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        var boxes = grid.Children.OfType<Control>().Where(c => c.IsVisible).Select(c => new Rect(c.TranslatePoint(default, grid) ?? default, c.Bounds.Size)).ToList();
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                Assert.False(boxes[i].Intersects(boxes[j]) && boxes[i].Intersect(boxes[j]).Width > 1 && boxes[i].Intersect(boxes[j]).Height > 1, $"{i} and {j} overlap");
            }
        }

        o.Close();
    }
}
