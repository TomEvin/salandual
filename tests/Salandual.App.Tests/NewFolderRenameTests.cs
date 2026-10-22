// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/NewFolderRenameTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.Core.Commands;
using Salandual.Core.Operations;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class NewFolderRenameTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-m33-").FullName;
    private readonly string _left;
    private readonly string _right;

    public NewFolderRenameTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Left")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Right")).FullName;
        File.WriteAllText(Path.Join(_left, "IMG_0413.HEIC"), "photo");
        File.WriteAllText(Path.Join(_left, "notes.txt"), "n");
        File.WriteAllText(Path.Join(_left, "todo.txt"), "t");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

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

    private static void Press(TopLevel w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        try
        {
            w.KeyRelease(key, mods, PhysicalKey.None, null);
        }
        catch (ObjectDisposedException)
        {
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition, int tries = 500)
    {
        for (var i = 0; i < tries && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static void Focus(MainWindow w, string name)
    {
        var panel = w.LeftPanel.Panel;
        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == name));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Select(MainWindow w, params string[] names)
    {
        var panel = w.LeftPanel.Panel;
        foreach (var name in names)
        {
            panel.Toggle(panel.Items.ToList().FindIndex(e => e.Name == name));
        }

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task F7_MakesTheWholePath_AndFocusesTheNewFolder()
    {
        var w = await Open();
        Press(w, Key.F7);
        await Until(() => w.OpenOperationDialog is NewFolderDialog);
        var dialog = (NewFolderDialog)w.OpenOperationDialog!;
        Assert.Null(dialog.MoveBox);
        Assert.Contains("dialog", dialog.Classes);

        dialog.NameBox.Text = "a|b";
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.ErrorText.IsVisible);

        dialog.NameBox.Text = @"2026\Augusztus\Válogatás";
        Dispatcher.UIThread.RunJobs();
        Assert.False(dialog.ErrorText.IsVisible);
        Press(dialog, Key.Enter);

        await Until(() => w.OpenOperationDialog is null);
        Assert.True(Directory.Exists(Path.Join(_left, @"2026\Augusztus\Válogatás")));
        await Until(() => w.LeftPanel.Panel.FocusedEntry?.Name == "2026");
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F7_AnExistingName_KeepsTheDialogOpenWithTheReason()
    {
        Directory.CreateDirectory(Path.Join(_left, "Balaton"));
        var w = await Open();
        Press(w, Key.F7);
        await Until(() => w.OpenOperationDialog is NewFolderDialog);
        var dialog = (NewFolderDialog)w.OpenOperationDialog!;
        dialog.NameBox.Text = "Balaton";
        await dialog.AcceptAsync();
        Assert.Equal("A folder named “Balaton” is already here.", dialog.ErrorText.Text);
        Assert.Same(dialog, w.OpenOperationDialog);
        Press(dialog, Key.Escape);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F7_WithASelection_MovesTheItemsIntoTheNewFolder_AndRemembersTheChoice()
    {
        var w = await Open();
        Select(w, "notes.txt", "todo.txt");
        Press(w, Key.F7);
        await Until(() => w.OpenOperationDialog is NewFolderDialog);
        var dialog = (NewFolderDialog)w.OpenOperationDialog!;
        Assert.Equal("Move the 2 selected items into it", dialog.MoveBox?.Content);
        Assert.True(dialog.MoveBox!.IsChecked);
        dialog.NameBox.Text = "Jegyzetek";
        Press(dialog, Key.Enter);

        await Until(() => File.Exists(Path.Join(_left, @"Jegyzetek\notes.txt")) && File.Exists(Path.Join(_left, @"Jegyzetek\todo.txt")));
        Assert.False(File.Exists(Path.Join(_left, "notes.txt")));
        await Until(() => w.LeftPanel.Panel.FocusedEntry?.Name == "Jegyzetek");

        Select(w, "IMG_0413.HEIC");
        Press(w, Key.F7);
        await Until(() => w.OpenOperationDialog is NewFolderDialog);
        dialog = (NewFolderDialog)w.OpenOperationDialog!;
        dialog.MoveBox!.IsChecked = false;
        Press(dialog, Key.Escape);
        await Until(() => w.OpenOperationDialog is null);
        Assert.False(w.Services.Settings.Current.Operations.MoveIntoNewFolder);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F2_SelectsTheNameWithoutExtension_EnterRenames_AndKeepsTheFocus()
    {
        var w = await Open();
        Focus(w, "notes.txt");
        Press(w, Key.F2);
        var rename = w.LeftPanel.Rename!;
        Assert.True(w.LeftPanel.IsRenaming);
        Assert.Equal("notes.txt", rename.Box.Text);
        Assert.Equal((0, 5), (rename.Box.SelectionStart, rename.Box.SelectionEnd));
        Assert.True(rename.Box.IsFocused);

        rename.Box.Text = "Jegyzet.txt";
        Press(w, Key.Enter);
        await rename.Finished;
        await Until(() => w.LeftPanel.Panel.FocusedEntry?.Name == "Jegyzet.txt");
        Assert.True(File.Exists(Path.Join(_left, "Jegyzet.txt")));
        Assert.False(w.LeftPanel.IsRenaming);
        Assert.True(w.LeftPanel.View.IsFocused);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F2_ChangingTheExtension_Warns_EscBringsItBack_EscAgainCancels()
    {
        var w = await Open();
        Focus(w, "IMG_0413.HEIC");
        Press(w, Key.F2);
        var rename = w.LeftPanel.Rename!;
        rename.Box.Text = "IMG_0413.jpg";
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Changing .HEIC to .jpg doesn't convert the file", rename.Message, StringComparison.Ordinal);

        Press(w, Key.Escape);
        Assert.Equal("IMG_0413.HEIC", rename.Box.Text);
        Assert.Null(rename.Message);
        Assert.True(w.LeftPanel.IsRenaming);

        Press(w, Key.Escape);
        Assert.False(w.LeftPanel.IsRenaming);
        Assert.True(File.Exists(Path.Join(_left, "IMG_0413.HEIC")));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F2_ATakenName_IsExplainedUnderTheRow_AndNothingChanges()
    {
        var w = await Open();
        Focus(w, "notes.txt");
        Press(w, Key.F2);
        var rename = w.LeftPanel.Rename!;
        rename.Box.Text = "todo.txt";
        Press(w, Key.Enter);
        await rename.Finished;
        Assert.Equal("A file named “todo.txt” is already here.", rename.Message);
        Assert.True(w.LeftPanel.IsRenaming);
        Assert.Equal("n", await File.ReadAllTextAsync(Path.Join(_left, "notes.txt"), TestContext.Current.CancellationToken));

        rename.Box.Text = "bad:name.txt";
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("A name can't contain :", rename.Message, StringComparison.Ordinal);
        Press(w, Key.Escape);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task SlowSecondClick_OnTheFocusedName_StartsRename_DoubleClickDoesNot()
    {
        var w = await Open();
        Focus(w, "notes.txt");
        var view = w.LeftPanel.View;
        var row = view.NameBounds(w.LeftPanel.Panel.Focus)!.Value;
        var at = view.TranslatePoint(new Point(row.X + 12, row.Center.Y), w)!.Value;
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        await Until(() => w.LeftPanel.IsRenaming, tries: 150);
        Assert.Equal("notes.txt", w.LeftPanel.Rename!.Box.Text);
        Press(w, Key.Escape);

        w.Services.Settings.Update(s => s with { Panels = s.Panels with { ClickToRename = false } });
        Dispatcher.UIThread.RunJobs();
        view.Focus();
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        await Task.Delay(800, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.LeftPanel.IsRenaming);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Duplicate_MakesName2_FocusedNextToTheOriginal()
    {
        var w = await Open();
        Focus(w, "notes.txt");
        w.Run("File.Duplicate", PanelTarget.Active);
        await Until(() => File.Exists(Path.Join(_left, "notes (2).txt")));
        await Until(() => w.LeftPanel.Panel.FocusedEntry?.Name == "notes (2).txt");
        Assert.Equal("n", await File.ReadAllTextAsync(Path.Join(_left, "notes (2).txt"), TestContext.Current.CancellationToken));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task MiddleToolbar_ButtonsRunTheirCommands_LaterOnesAreGreyed_ArrowsFollowTheActivePanel()
    {
        var w = await Open();
        var bar = w.Splitter;
        Assert.True(bar.ShowButtons);
        Assert.Equal(Controls.PanelSplitter.ToolbarWidth, bar.Bounds.Width);
        Assert.Equal(16, Controls.PanelSplitter.Buttons.Count);

        var newFolder = Controls.PanelSplitter.Buttons.ToList().FindIndex(b => b.CommandId == "File.CreateDirectory");
        bar.Click(newFolder);
        await Until(() => w.OpenOperationDialog is NewFolderDialog);
        Press(w.OpenOperationDialog!, Key.Escape);
        await Until(() => w.OpenOperationDialog is null);

        var view = Controls.PanelSplitter.Buttons.ToList().FindIndex(b => b.CommandId == "File.View");
        bar.Click(view);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(w.NoticeMessage);

        Assert.False(bar.RightActive);
        Press(w, Key.Tab);
        Assert.True(bar.RightActive);

        w.Run("View.ToggleMiddleToolbar", PanelTarget.Active);
        Dispatcher.UIThread.RunJobs();
        Assert.False(bar.ShowButtons);
        Assert.Equal(Controls.PanelSplitter.PlainWidth, bar.Bounds.Width);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task MiddleToolbar_TooShort_PutsTheLastButtonsUnderMore()
    {
        var w = await Open();
        w.Height = 420;
        Dispatcher.UIThread.RunJobs();
        Assert.InRange(w.Splitter.VisibleButtons, 1, Controls.PanelSplitter.Buttons.Count - 1);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CloudDrive_ShowsCloudDrive_NotAMadeUpFreeSpace()
    {
        Assert.True(Platform.Windows.FileSystem.DiskSpace.IsMadeUp(10L << 50));
        Assert.False(Platform.Windows.FileSystem.DiskSpace.IsMadeUp(20L << 40));
        var w = await Open();
        var line = w.LeftPanel.DirectoryLine;
        line.SetSpace(_left, 10L << 50, 10L << 50);
        w.CaptureRenderedFrame();
        Assert.True(line.FreeSpaceBounds.Width is > 0 and < 140);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task WhenTheQueueIsDone_Sleep_CountsDownThenSleeps_OnlyOnce()
    {
        var w = await Open();
        var done = new List<WhenDone>();
        w.RunPowerAction = a =>
        {
            done.Add(a);
            return true;
        };
        w.Operations.WhenDone = WhenDone.Sleep;
        Focus(w, "notes.txt");
        w.Run("File.Duplicate", PanelTarget.Active);

        await Until(() => w.OpenCountdown is not null);
        Assert.Contains("The PC goes to sleep in", w.OpenCountdown!.Message, StringComparison.Ordinal);
        Assert.Equal(WhenDone.Nothing, w.Operations.WhenDone);
        for (var i = 0; i < 59; i++)
        {
            w.OpenCountdown.Tick();
        }

        Assert.Contains("in 1 s", w.OpenCountdown.Message, StringComparison.Ordinal);
        w.OpenCountdown.Tick();
        await Until(() => done.Count == 1);
        Assert.Equal([WhenDone.Sleep], done);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task WhenTheQueueIsDone_Cancel_KeepsThePcOn()
    {
        var w = await Open();
        var done = new List<WhenDone>();
        w.RunPowerAction = a =>
        {
            done.Add(a);
            return true;
        };
        w.Operations.WhenDone = WhenDone.ShutDown;
        Focus(w, "notes.txt");
        w.Run("File.Duplicate", PanelTarget.Active);

        await Until(() => w.OpenCountdown is not null);
        Assert.Contains("shuts down in 60 s", w.OpenCountdown!.Message, StringComparison.Ordinal);
        Press(w.OpenCountdown, Key.Escape);
        await Until(() => w.OpenCountdown is null);
        Assert.Empty(done);
        Assert.Equal("Cancelled: the PC stays on.", w.NoticeMessage);
        w.CloseNow();
    }
}
