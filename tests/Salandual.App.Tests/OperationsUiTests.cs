// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/OperationsUiTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.Core.Operations;

namespace Salandual.App.Tests;

public sealed class OperationsUiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-ops-ui-").FullName;
    private readonly string _left;
    private readonly string _right;

    public OperationsUiTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Left")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Right")).FullName;
        File.WriteAllText(Path.Join(_left, "a.txt"), "new a");
        File.WriteAllText(Path.Join(_left, "b.txt"), "b");
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

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Focus(MainWindow w, string name)
    {
        var panel = w.LeftPanel.Panel;
        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == name));
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<CopyMoveDialog> CopyDialog(MainWindow w, Key key = Key.F5)
    {
        Press(w, key);
        await Until(() => w.OpenOperationDialog is CopyMoveDialog);
        var dialog = (CopyMoveDialog)w.OpenOperationDialog!;
        await Until(() => dialog.Preview is not null);
        return dialog;
    }

    private static string Text(StackPanel checks) => string.Join(" | ", checks.Children.OfType<Border>().Select(b => b.Child)
        .OfType<DockPanel>().SelectMany(d => d.Children.OfType<StackPanel>()).Select(s => s.Children.OfType<TextBlock>().First())
        .Select(t => string.Concat(t.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text))));

    [AvaloniaFact]
    public async Task F5_ShowsTheOtherPanelAndTheChecks_EnterCopies_ChipSaysDone()
    {
        var w = await Open();
        Focus(w, "a.txt");
        var dialog = await CopyDialog(w);

        Assert.Equal(_right + "\\", dialog.To.Text);
        Assert.Contains("Copy 1 file", string.Concat(dialog.Heading.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)), StringComparison.Ordinal);
        await Until(() => Text(dialog.Checks).Contains("Nothing there has the same name.", StringComparison.Ordinal));
        Assert.True(dialog.Go.IsEnabled);
        Assert.Contains("dialog", dialog.Classes);

        Press(dialog, Key.Enter);
        await Until(() => File.Exists(Path.Join(_right, "a.txt")));
        await Until(() => w.Chip?.Text == "✓ All done");
        Assert.True(w.Chip!.IsVisible);
        Assert.Equal("new a", await File.ReadAllTextAsync(Path.Join(_right, "a.txt"), TestContext.Current.CancellationToken));
        Assert.Contains(w.Services.Settings.Current.History.CopyTargets, t => t == _right + "\\");
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Conflict_Dialog_Minimize_WaitsInThePanel_ChipBringsItBack_OOverwrites()
    {
        File.WriteAllText(Path.Join(_right, "a.txt"), "old a");
        File.SetLastWriteTimeUtc(Path.Join(_right, "a.txt"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var w = await Open();
        Focus(w, "a.txt");
        var dialog = await CopyDialog(w);
        Assert.Contains("1 file already exists", Text(dialog.Checks), StringComparison.Ordinal);
        Press(dialog, Key.Enter);

        await Until(() => w.OpenConflictDialog is not null);
        var conflict = w.OpenConflictDialog!;
        Assert.Contains("Do the same for the rest", conflict.ForAll.Content as string ?? "", StringComparison.Ordinal);
        Click(conflict.MinimizeButton);
        await Until(() => w.OpenConflictDialog is null);
        await Until(() => w.Chip!.Look == "ask");
        Assert.StartsWith("A file exists:", w.Chip!.Text, StringComparison.Ordinal);

        w.ToggleOperationsPanel();
        var view = Assert.Single(w.OpsPanel!.Views.Values);
        Assert.NotNull(view.QuestionCard);

        w.Chip.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(w.Chip, new Pointer(1, PointerType.Mouse, true), w.Chip, default, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        await Until(() => w.OpenConflictDialog is not null);
        Press(w.OpenConflictDialog!, Key.O);
        await Until(() => File.ReadAllText(Path.Join(_right, "a.txt")) == "new a");
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CopyIntoItsOwnFolder_IsBlocked_UntilNewNamesAreTicked()
    {
        var w = await Open();
        Focus(w, "a.txt");
        var dialog = await CopyDialog(w);
        dialog.To.Text = _left + "\\";
        await Until(() => dialog.OntoItselfBox is not null);
        Assert.Contains("You can't copy a file onto itself.", Text(dialog.Checks), StringComparison.Ordinal);
        Assert.False(dialog.Go.IsEnabled);

        dialog.OntoItselfBox!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.Go.IsEnabled);
        Press(dialog, Key.Enter);
        await Until(() => File.Exists(Path.Join(_left, "a (2).txt")));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F6_Moves_ShiftEnterQueues()
    {
        var w = await Open();
        Focus(w, "b.txt");
        var dialog = await CopyDialog(w, Key.F6);
        Assert.Contains("Move", dialog.Title, StringComparison.Ordinal);
        Press(dialog, Key.Enter, RawInputModifiers.Shift);
        await Until(() => File.Exists(Path.Join(_right, "b.txt")) && !File.Exists(Path.Join(_left, "b.txt")));
        Assert.Contains("Added to the queue", w.NoticeMessage ?? "", StringComparison.Ordinal);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ShiftDel_DeletesPermanently_F8StartsOnTheRecycleBin()
    {
        var w = await Open();
        Focus(w, "b.txt");
        Press(w, Key.F8);
        await Until(() => w.OpenOperationDialog is DeleteDialog);
        var recycle = (DeleteDialog)w.OpenOperationDialog!;
        Assert.False(recycle.Permanent);
        Press(recycle, Key.Escape);
        await Until(() => w.OpenOperationDialog is null);
        Assert.True(File.Exists(Path.Join(_left, "b.txt")));

        Press(w, Key.Delete, RawInputModifiers.Shift);
        await Until(() => w.OpenOperationDialog is DeleteDialog);
        var permanent = (DeleteDialog)w.OpenOperationDialog!;
        Assert.True(permanent.Permanent);
        Assert.Contains("Delete permanently", permanent.Go.Content as string ?? "", StringComparison.Ordinal);
        Press(permanent, Key.Enter);
        await Until(() => !File.Exists(Path.Join(_left, "b.txt")));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Delete_NamesItemsTheRecycleBinCantTake()
    {
        var odd = Path.Join(_left, "dot.");
        File.WriteAllText(@"\\?\" + odd, "x");
        var w = await Open();
        Focus(w, "dot.");
        Press(w, Key.F8);
        await Until(() => w.OpenOperationDialog is DeleteDialog);
        var dialog = (DeleteDialog)w.OpenOperationDialog!;
        Assert.Equal("Its name ends with a dot or a space.", Assert.Single(dialog.Refused).Reason);
        dialog.SetRefusedPermanently(false);
        Press(dialog, Key.Enter);
        await Until(() => w.Chip?.Text?.Contains("not done", StringComparison.Ordinal) == true);
        Assert.True(File.Exists(@"\\?\" + odd));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Panel_ShowsTheGraph_SpeedLimitChangesWhileRunning()
    {
        await File.WriteAllBytesAsync(Path.Join(_left, "big.bin"), new byte[5 * 1024 * 1024], TestContext.Current.CancellationToken);
        var w = await Open();
        w.Services.Settings.Update(s => s with { Operations = s.Operations with { LimitSpeed = true, SpeedLimitMb = 1 } });
        Focus(w, "big.bin");
        var dialog = await CopyDialog(w);
        Press(dialog, Key.Enter);
        await Until(() => w.Operations.Jobs.Any(j => j.State == JobState.Running));
        w.ToggleOperationsPanel();
        var view = Assert.Single(w.OpsPanel!.Views.Values);
        Assert.True(view.IsExpanded);
        Assert.True(w.OpsPanel.PauseQueueLink.IsVisible);
        await Until(() => view.Graph.PointCount > 0);
        Assert.Equal("None", view.LimitBox.Items[0]);

        view.LimitBox.SelectedIndex = 0;
        await Until(() => File.Exists(Path.Join(_right, "big.bin")) && w.Operations.Jobs.All(j => j.IsFinished), tries: 600);
        Assert.Equal(JobState.Done, w.Operations.Jobs[0].State);
        await Until(() => !w.OpsPanel.PauseQueueLink.IsVisible);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DeleteConfirmationOff_F8GoesStraightToTheJob_PermanentStillAsks()
    {
        var odd = Path.Join(_left, "dot.");
        File.WriteAllText(@"\\?\" + odd, "x");
        var w = await Open();
        w.Services.Settings.Update(s => s with { Confirmations = s.Confirmations with { Delete = false } });
        Focus(w, "dot.");
        Press(w, Key.F8);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(w.OpenOperationDialog);
        await Until(() => w.Operations.Jobs.Any(j => j.Question is PermanentDeleteQuestion));
        w.Operations.Jobs.First(j => j.Question is not null).Answer(new OperationAnswer(AnswerKind.Skip));
        await Until(() => w.Operations.Jobs.All(j => j.IsFinished));
        Assert.True(File.Exists(@"\\?\" + odd));

        Focus(w, "b.txt");
        Press(w, Key.Delete, RawInputModifiers.Shift);
        await Until(() => w.OpenOperationDialog is DeleteDialog);
        Press(w.OpenOperationDialog!, Key.Escape);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task DeletingAFolderThatIsntEmpty_AsksInADialog_SkipKeepsIt()
    {
        var full =Directory.CreateDirectory(Path.Join(_left, "Full")).FullName;
        File.WriteAllText(Path.Join(full, "inside.txt"), "x");
        var w = await Open();
        Focus(w, "Full");
        Press(w, Key.Delete, RawInputModifiers.Shift);
        await Until(() => w.OpenOperationDialog is DeleteDialog);
        Press(w.OpenOperationDialog!, Key.Enter);

        await Until(() => w.OpenCheckDialog is not null);
        var check = w.OpenCheckDialog!;
        Assert.Equal("Delete", check.Yes.Content);
        Click(check.Skip);
        await Until(() => w.Operations.Jobs.All(j => j.IsFinished));
        Assert.True(Directory.Exists(full));

        Focus(w, "Full");
        Press(w, Key.Delete, RawInputModifiers.Shift);
        await Until(() => w.OpenOperationDialog is DeleteDialog);
        Press(w.OpenOperationDialog!, Key.Enter);
        await Until(() => w.OpenCheckDialog is not null);
        Press(w.OpenCheckDialog!, Key.Escape);
        await Until(() => w.Chip!.Look == "ask");
        Assert.StartsWith("A question:", w.Chip!.Text, StringComparison.Ordinal);
        w.Operations.BringToFront(w.Operations.Jobs.First(j => j.Question is CheckQuestion));
        await Until(() => w.OpenCheckDialog is not null);
        Click(w.OpenCheckDialog!.Yes);
        await Until(() => !Directory.Exists(full));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ClosingSalandual_AsksOnlyWhenTurnedOn()
    {
        var w = await Open();
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.Services.Settings.Update(s => s with { Confirmations = s.Confirmations with { CloseApp = true } });
        w.Close();
        await Until(() => w.OpenCloseQuestion is not null);
        Assert.False(closed);
        Click(w.OpenCloseQuestion!.No);
        await Until(() => w.OpenCloseQuestion is null);
        Assert.False(closed);

        w.Services.Settings.Update(s => s with { Confirmations = new() });
        w.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public async Task ClosingSalandual_WithAnotherWindowOpen_AlwaysAsks()
    {
        var w = await Open();
        var closed = false;
        w.Closed += (_, _) => closed = true;
        var child = new Window { Title = "notes.txt — Viewer", Width = 200, Height = 100 };
        child.Show(w);
        Assert.Equal(["notes.txt"], w.OpenChildWindows());
        w.Close();
        await Until(() => w.OpenCloseQuestion is not null);
        var text = string.Join("\n", w.OpenCloseQuestion!.GetLogicalDescendants().OfType<TextBlock>().Select(b => b.Text));
        Assert.Contains("This window is open and closes too:", text, StringComparison.Ordinal);
        Assert.Contains("• notes.txt", text, StringComparison.Ordinal);
        Click(w.OpenCloseQuestion!.No);
        await Until(() => w.OpenCloseQuestion is null);
        Assert.False(closed);
        Assert.True(child.IsVisible);

        w.Close();
        await Until(() => w.OpenCloseQuestion is not null);
        Click(w.OpenCloseQuestion!.Yes);
        await Until(() => closed);
    }

    [AvaloniaFact]
    public async Task ClosingSalandual_ClosesSearchAndTheEditorsToo()
    {
        var w = await Open();
        w.Services.Settings.Update(s => s with { Confirmations = s.Confirmations with { CloseSearch = true } });
        var closed = false;
        w.Closed += (_, _) => closed = true;
        var find = w.OpenFind(w.LeftPanel);
        var editor = await w.OpenEditorAsync(w.LeftPanel, Path.Join(_left, "a.txt"), internalEditor: true);
        Assert.NotNull(editor);
        await Until(() => editor!.IsLoaded);
        Assert.Equal(2, w.OpenChildWindows().Count);

        w.Close();
        await Until(() => w.OpenCloseQuestion is not null);
        Click(w.OpenCloseQuestion!.Yes);
        await Until(() => closed);
        await Until(() => !find.IsVisible && !editor!.IsVisible);
        Assert.Null(find.CloseQuestion);
        Assert.Empty(w.FindWindows);
        Assert.Empty(w.Editors);
    }
}
