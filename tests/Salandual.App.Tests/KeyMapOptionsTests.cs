// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/KeyMapOptionsTests.cs

using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.App.Panels;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Tests;

public sealed class KeyMapOptionsTests
{
    private static readonly HashSet<string> _both = [SettingsTransferDialog.QuickSearchTick, SettingsTransferDialog.RightButtonTick];

    private static OptionsWindow Open(AppServices services)
    {
        var w = new OptionsWindow(services) { Width = 1000, Height = 740 };
        w.Show();
        w.ShowCategory("keyboard");
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    [AvaloniaFact]
    public void KeyMap_TwoCards_TotalCommanderWithWhatGoesWithIt_UndoBringsYourKeysBack()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services);
        Assert.Equal([KeyMapName.Salamander, KeyMapName.TotalCommander], w.KeyMapCards.Select(c => (KeyMapName)c.Tag!));
        Assert.Null(w.OnlyChangedKeys);
        Assert.Contains(w.Find("total commander"), i => i.Title == "Key map");

        w.UseKeyMap(KeyMapName.TotalCommander, _both);
        Dispatcher.UIThread.RunJobs();
        var s = services.Settings.Current;
        Assert.Equal(KeyMapName.TotalCommander, s.Keyboard.KeyMap);
        Assert.Equal(QuickSearchStart.CtrlAltLetter, s.Panels.QuickSearch);
        Assert.Equal(RightClickUnselected.RightButtonSelects, s.Mouse.RightClickUnselected);
        Assert.Equal(KeyMapName.TotalCommander, services.Commands.KeyMap);
        Assert.Matches(@"^You're using Total Commander's keys: \d+ keys changed, with Quick search: Ctrl\+Alt\+letter and Right-click: The right button selects\.$", w.KeyMapNoticeText!.Text);
        Assert.Equal("→ New tab (Total Commander), not yet in Salandual", w.Answer(KeyChord.Parse("Ctrl+T")));
        Assert.Equal("→ Refresh (Panel)", w.Answer(KeyChord.Parse("F2")));
        Assert.Contains("(Total Commander's key map)", w.CheatSheet(), StringComparison.Ordinal);
        Assert.Contains("Total Commander's keys not yet in Salandual", w.CheatSheet(), StringComparison.Ordinal);

        Assert.NotNull(w.OnlyChangedKeys);
        w.OnlyChangedKeys!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var texts = w.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("was F2", texts);
        Assert.Contains("New tab", texts);
        Assert.DoesNotContain("Move", texts);
        Assert.Contains(texts, t => t is not null && Regex.IsMatch(t, @"^\d+ of \d+ commands$"));

        w.KeyMapNoticeUndo!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        s = services.Settings.Current;
        Assert.Equal(KeyMapName.Salamander, s.Keyboard.KeyMap);
        Assert.Equal(QuickSearchStart.Letters, s.Panels.QuickSearch);
        Assert.Equal(RightClickUnselected.MenuForItem, s.Mouse.RightClickUnselected);
        Assert.Equal(KeyMapName.Salamander, services.Commands.KeyMap);
        Assert.Null(w.KeyMapNoticeText);
        w.Close();
    }

    [AvaloniaFact]
    public void KeyMap_UntickedSettings_StayAsTheyAre()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services);
        w.UseKeyMap(KeyMapName.TotalCommander, new HashSet<string>());
        Assert.Equal(QuickSearchStart.Letters, services.Settings.Current.Panels.QuickSearch);
        Assert.Equal(RightClickUnselected.MenuForItem, services.Settings.Current.Mouse.RightClickUnselected);
        Assert.Matches(@"^You're using Total Commander's keys: \d+ keys changed\.$", w.KeyMapNoticeText!.Text);
        w.Close();

        var again = Open(services);
        Assert.Equal(KeyMapName.TotalCommander, services.Commands.KeyMap);
        Assert.NotNull(again.OnlyChangedKeys);
        again.Close();
    }

    [AvaloniaFact]
    public void Dialog_ShowsTheKeysThatChange_TheSettingsTicked_AndWhatIsntBuilt()
    {
        var services = AppServices.CreateIsolated();
        var d = SettingsTransferDialog.ForKeyMap(services.Commands, KeyMapName.TotalCommander, services.Settings.Current, MainWindow.IsBuilt);
        d.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Use Total Commander's keys?", d.Title);
        Assert.Equal("Use Total Commander's keys", d.Primary.Content);
        Assert.Equal([SettingsTransferDialog.QuickSearchTick, SettingsTransferDialog.RightButtonTick], d.Ticks.Select(t => t.Value));
        Assert.All(d.Ticks, t => Assert.True(t.Box.IsChecked));
        var texts = d.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Now (Salamander)", texts);
        Assert.Contains("Then (Total Commander)", texts);
        Assert.Contains("F2", texts);
        Assert.Contains("Rename", texts);
        Assert.Contains("Refresh", texts);
        Assert.Contains("What goes with Total Commander's keys", texts);
        Assert.Contains("Not yet in Salandual", texts);
        Assert.Contains(texts, t => t is not null && t.StartsWith("Undo brings", StringComparison.Ordinal));
        d.Close();

        services.Settings.Update(s => s with { Keyboard = new KeyboardSettings { KeyMap = KeyMapName.TotalCommander }, Panels = s.Panels with { QuickSearch = QuickSearchStart.CtrlAltLetter } });
        services.Commands.UseKeyMap(KeyMapName.TotalCommander);
        var back = SettingsTransferDialog.ForKeyMap(services.Commands, KeyMapName.Salamander, services.Settings.Current, MainWindow.IsBuilt);
        Assert.Equal("Use Salamander's keys?", back.Title);
        Assert.Equal([SettingsTransferDialog.QuickSearchTick], back.Ticks.Select(t => t.Value));
    }

    [AvaloniaFact]
    public async Task MainWindow_MenusAndFKeyBar_FollowTheKeyMap()
    {
        var w = new MainWindow();
        w.Show();
        string? Gesture(string id) => w.MenuItems.First(m => m.Node.CommandId == id).Item.InputGesture?.ToString();
        Assert.Equal("F2", Gesture("File.QuickRename"));

        w.Services.Settings.Update(s => s with { Keyboard = new KeyboardSettings { KeyMap = KeyMapName.TotalCommander } });
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(KeyMapName.TotalCommander, w.Services.Commands.KeyMap);
        Assert.Equal("Shift+F6", Gesture("File.QuickRename"));
        Assert.Equal("Panel.Refresh", w.Services.Commands.Resolve(KeyChord.Parse("F2")));

        w.Services.Settings.Update(s => s with { Keyboard = new KeyboardSettings { KeyMap = KeyMapName.Salamander } });
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("F2", Gesture("File.QuickRename"));
        w.Close();
    }

    [AvaloniaFact]
    public void Pictures()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services);
        Capture(w, "keymap-salamander.png");
        var d = SettingsTransferDialog.ForKeyMap(services.Commands, KeyMapName.TotalCommander, services.Settings.Current, MainWindow.IsBuilt);
        d.Show();
        Dispatcher.UIThread.RunJobs();
        Capture(d, "keymap-dialog.png");
        d.Close();
        w.UseKeyMap(KeyMapName.TotalCommander, _both);
        Dispatcher.UIThread.RunJobs();
        Capture(w, "keymap-tc.png");
        w.OnlyChangedKeys!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        w.ShowCategory("keyboard");
        Dispatcher.UIThread.RunJobs();
        w.ShowCategory("panels");
        Dispatcher.UIThread.RunJobs();
        Capture(w, "keymap-panels.png");
        w.Close();
    }

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task QuickSearch_CtrlAltLetter_StartsIt_LettersAloneThenGoOn()

    {
        var dir = Directory.CreateTempSubdirectory("salandual-keymap-").FullName;
        try
        {
            foreach (var name in new[] { "alma.txt", "jegyzetek.md", "jelszo.txt" })
            {
                File.WriteAllText(Path.Combine(dir, name), "x");
            }

            var panel = new FilePanel(new LocalFileSystemProvider(), new EntrySorter(CultureInfo.GetCultureInfo("en-US"), true), a => Dispatcher.UIThread.Post(a));
            var view = new FilePanelView { Panel = panel, Commands = CommandRegistry.CreateDefault(), QuickSearchWithCtrlAlt = true };
            var window = new Window { Width = 600, Height = 300, Content = view };
            window.Closed += (_, _) => panel.Dispose();
            window.Show();
            Assert.True(await panel.NavigateAsync(dir));
            view.Focus();
            Dispatcher.UIThread.RunJobs();

            window.KeyTextInput("j");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(panel.QuickSearchText);

            window.KeyPress(Key.J, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.None, null);
            window.KeyRelease(Key.J, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("j", panel.QuickSearchText);

            foreach (var (key, text) in new[] { (Key.E, "e"), (Key.G, "g") })
            {
                window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyTextInput(text);
                window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal("jeg", panel.QuickSearchText);
            Assert.Equal("jegyzetek.md", panel.FocusedEntry?.Name);
            window.Close();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
