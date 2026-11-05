// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/KeyEditorTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.Core.Commands;
using Salandual.Core.QuickOpen;
using Salandual.Core.UserMenu;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace Salandual.App.Tests;

public sealed class KeyEditorTests
{
    private static OptionsWindow Open(AppServices services)
    {
        var w = new OptionsWindow(services) { Width = 1000, Height = 740 };
        w.Show();
        w.ShowCategory("keyboard");
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(Control target, Key key, KeyModifiers mods = KeyModifiers.None)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = target });
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static List<string?> Texts(Window w)
        => [.. w.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text)];

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            for (var k = 0; k < 10; k++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    private static KeyChord K(string text) => KeyChord.Parse(text);

    [AvaloniaFact]
    public void ARowOpens_AFreeKeyIsAssigned_AndShowsAsYours()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services);
        w.OpenKeyRow("File.CreateDirectory");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("File.CreateDirectory", w.OpenKeyRowCommand);
        Assert.NotNull(w.KeyCapture);

        Press(w.KeyCapture!, Key.F7, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal("Ctrl+Shift+F7 is not assigned. Press Enter to assign it.", w.KeyCaptureMessage!.Text);
        Assert.Equal("Assign", w.KeyCapturePrimary!.Content);
        Capture(w, "keys-free.png");
        Press(w.KeyCapture!, Key.Enter);

        Assert.Equal("File.CreateDirectory", services.Commands.Resolve(K("Ctrl+Shift+F7")));
        var mine = Assert.Single(services.Settings.Current.Keyboard.Keys);
        Assert.Equal(["F7", "Ctrl+Shift+F7"], mine.Keys);
        Assert.Equal("File.CreateDirectory", w.OpenKeyRowCommand);
        Press(w.KeyCapture!, Key.Escape);
        Assert.Null(w.OpenKeyRowCommand);
        Assert.Contains("Customised · Salamander's keys: F7", Texts(w));
        Assert.Contains("1 command has keys of your own", Texts(w));
        w.Close();
    }

    [AvaloniaFact]
    public void AKeyAnotherCommandHas_IsReassignedOnlyWhenAsked_AndUndoGivesItBack()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services);
        w.OpenKeyRow("Compare.Directories");
        Dispatcher.UIThread.RunJobs();
        Press(w.KeyCapture!, Key.F7, KeyModifiers.Alt);
        Assert.Equal("Alt+F7 is already assigned to Search for files (Commands).", w.KeyCaptureMessage!.Text);
        Assert.Equal("Reassign to Compare directories", w.KeyCapturePrimary!.Content);
        Assert.Equal("Find.Files", services.Commands.Resolve(K("Alt+F7")));
        Capture(w, "keys-taken.png");

        Click(w.KeyCapturePrimary!);
        Assert.Equal("Compare.Directories", services.Commands.Resolve(K("Alt+F7")));
        Assert.Equal([K("Ctrl+F")], services.Commands.ChordsFor("Find.Files"));
        Assert.Contains("Alt+F7 reassigned to Compare directories", Texts(w));
        Capture(w, "keys-reassigned.png");

        var undo = w.GetLogicalDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Undo Search for files");
        Click(undo);
        Assert.Equal("Find.Files", services.Commands.Resolve(K("Alt+F7")));
        Assert.Empty(services.Settings.Current.Keyboard.Keys);
        w.Close();
    }

    [AvaloniaFact]
    public void KeysThatCantBeGiven_SayWhy_AndHaveNoAssign()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services);
        w.OpenKeyRow("File.View");
        Dispatcher.UIThread.RunJobs();
        Press(w.KeyCapture!, Key.V);
        Assert.Equal("V on its own starts quick search in a panel. Use it with Ctrl, Alt or Shift.", w.KeyCaptureMessage!.Text);
        Assert.Null(w.KeyCapturePrimary);
        Capture(w, "keys-refused.png");
        Press(w.KeyCapture!, Key.Down);
        Assert.Equal("Down on its own moves in panels and lists. Use it with Ctrl, Alt or Shift.", w.KeyCaptureMessage!.Text);
        Press(w.KeyCapture!, Key.F, KeyModifiers.Alt);
        Assert.Equal("Alt+F opens the Files menu. Use another letter, or add Ctrl or Shift.", w.KeyCaptureMessage!.Text);
        Press(w.KeyCapture!, Key.F3);
        Assert.Equal("F3 is already assigned to this command.", w.KeyCaptureMessage!.Text);

        Press(w.KeyCapture!, Key.Escape);
        Assert.Equal("File.View", w.OpenKeyRowCommand);
        Press(w.KeyCapture!, Key.Escape);
        Assert.Null(w.OpenKeyRowCommand);
        Assert.Empty(services.Settings.Current.Keyboard.Keys);
        w.Close();
    }

    [AvaloniaFact]
    public void AKeyRemoved_ThenRestored_ForOneAndForAll()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services);
        w.OpenKeyRow("Find.Files");
        Dispatcher.UIThread.RunJobs();
        var remove = w.GetLogicalDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Remove Ctrl+F");
        Click(remove);
        Assert.Null(services.Commands.Resolve(K("Ctrl+F")));
        var restore = w.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Restore Salamander's keys");
        Assert.True(restore.IsEnabled);
        Click(restore);
        Assert.Equal("Find.Files", services.Commands.Resolve(K("Ctrl+F")));
        Assert.Empty(services.Settings.Current.Keyboard.Keys);

        services.Settings.Update(s => s with { Keyboard = s.Keyboard with { Keys = [new CommandKeys("File.Copy", [K("F5"), K("Ctrl+Alt+C")]), new CommandKeys("File.Move", [])] } });
        w.Close();
        w = Open(services);
        Assert.Contains("2 commands have keys of your own", Texts(w));
        Assert.Null(services.Commands.Resolve(K("F6")));
        Capture(w, "keys-yours.png");
        w.RestoreAllKeys();
        Assert.Empty(services.Settings.Current.Keyboard.Keys);
        Assert.Equal("File.Move", services.Commands.Resolve(K("F6")));
        Assert.Contains("Salamander's keys restored for 2 commands.", Texts(w));
        Click(w.KeyMapNoticeUndo!);
        Assert.Equal(2, services.Settings.Current.Keyboard.Keys.Count);
        Assert.Equal("File.Copy", services.Commands.Resolve(K("Ctrl+Alt+C")));
        w.Close();
    }

    [AvaloniaFact]
    public void OnlyYourKeys_ShowsTheirRows()
    {
        var services = AppServices.CreateIsolated();
        services.Settings.Update(s => s with { Keyboard = s.Keyboard with { Keys = [new CommandKeys("File.Copy", [K("F5"), K("Ctrl+Alt+C")])] } });
        var w = Open(services);
        w.OnlyYourKeys!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(w);
        Assert.Contains("Copy", texts);
        Assert.DoesNotContain("Move", texts);
        Assert.Contains("1 of", string.Join('|', texts.Where(t => t is not null && t.EndsWith(" commands", StringComparison.Ordinal))), StringComparison.Ordinal);
        w.Close();
    }

    [AvaloniaFact]
    public void AUserMenuItemsKey_IsReassignedOnlyWhenAsked()
    {
        var services = AppServices.CreateIsolated();
        services.Settings.Update(s => s with { UserMenu = s.UserMenu with { Items = [new UserMenuItem { Name = "Code", Program = @"C:\Tools\code.exe", Key = "Ctrl+Alt+K" }] } });
        var w = Open(services);
        w.OpenKeyRow("File.Copy");
        Dispatcher.UIThread.RunJobs();
        Press(w.KeyCapture!, Key.K, KeyModifiers.Control | KeyModifiers.Alt);
        Assert.Equal("Ctrl+Alt+K is assigned to the user menu item Code.", w.KeyCaptureMessage!.Text);
        Click(w.KeyCapturePrimary!);
        Assert.Null(services.Settings.Current.UserMenu.Items[0].Key);
        Assert.Equal("File.Copy", services.Commands.Resolve(K("Ctrl+Alt+K")));
        w.Close();
    }

    [AvaloniaFact]
    public void WhichCommandHasAKey_AnswersWithChange()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services);
        w.KeyBox!.Focus();
        Press(w.KeyBox!, Key.R, KeyModifiers.Control);
        Assert.True(w.WhichKeyChange!.IsVisible);
        Click(w.WhichKeyChange!);
        Assert.Equal("Panel.Refresh", w.OpenKeyRowCommand);
        w.Close();
    }

    [AvaloniaFact]
    public void TheMapDialog_ListsYourKeysKept()
    {
        var services = AppServices.CreateIsolated();
        services.Commands.UseKeyMap(KeyMapName.Salamander, [new CommandKeys("File.Copy", [K("F5"), K("Ctrl+Alt+C")])]);
        var d = Dialogs.SettingsTransferDialog.ForKeyMap(services.Commands, KeyMapName.TotalCommander, services.Settings.Current, MainWindow.IsBuilt);
        d.Show();
        Dispatcher.UIThread.RunJobs();
        var texts = d.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Your keys, kept", texts);
        Assert.Contains("Copy: F5  Ctrl+Alt+C", texts);
        d.Close();
    }

    [AvaloniaFact]
    public async Task QuickOpen_CtrlK_OnACommand_OpensItsRow()
    {
        var w = new MainWindow { Width = 1100, Height = 640 };
        w.Show();
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        w.KeyPress(Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        var box = w.QuickOpen!;
        box.Input.Text = ">refresh";
        Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 200 && box.SelectedItem?.Kind != QuickKind.Command; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("Panel.Refresh", box.SelectedItem!.Target);
        Assert.Contains("Ctrl+K change its key", box.FootText, StringComparison.Ordinal);
        w.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.None, null);
        for (var i = 0; i < 200 && w.OpenOptions?.OpenKeyRowCommand is null; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("Panel.Refresh", w.OpenOptions!.OpenKeyRowCommand);
        w.OpenOptions.Close();
        Dispatcher.UIThread.RunJobs();
        w.Close();
    }
}
