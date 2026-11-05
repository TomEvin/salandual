// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/QuickOpenUiTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.Core.QuickOpen;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class QuickOpenUiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-quick-").FullName;

    public QuickOpenUiTests()
    {
        Directory.CreateDirectory(Path.Join(_dir, "Reports", "Old"));
        Directory.CreateDirectory(Path.Join(_dir, "Meetings"));
        File.WriteAllText(Path.Join(_dir, "Reports", "report-q3.pdf"), "");
        File.WriteAllText(Path.Join(_dir, "Reports", "Old", "budget-2025.xlsx"), "");
        File.WriteAllText(Path.Join(_dir, "Meetings", "weekly-report.md"), "# Week");
        File.WriteAllText(Path.Join(_dir, "plan.txt"), "plan");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 640 };
        w.ShellAccess = new NoShellAccess();
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(_dir);
        await w.RightPanel.Panel.NavigateAsync(Path.Join(_dir, "Meetings"));
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None)
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

    private static void Type(MainWindow w, string text)
    {
        w.QuickOpen!.Input.Text = text;
        w.QuickOpen.Input.CaretIndex = text.Length;
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitForWalk(MainWindow w)
    {
        await Until(() => w.QuickWalk is { IsCompleted: true });
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task CtrlShiftP_Opens_WhatYouUseInGroups_EscCloses()
    {
        var w = await Open();
        var now = DateTime.UtcNow;
        w.Services.Settings.Update(s => s with
        {
            History = s.History with
            {
                QuickCommands = [new UsageEntry("Compare.Directories", 4, now), new UsageEntry("File.MakeList", 1, now.AddDays(-2))],
                QuickFolders = [new UsageEntry(Path.Join(_dir, "Reports"), 3, now)],
                QuickFiles = [new UsageEntry(Path.Join(_dir, "plan.txt"), 1, now.AddDays(-1))],
            },
        });
        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        var box = w.QuickOpen!;
        Assert.True(box.IsVisible);
        Assert.True(box.Input.IsFocused);
        Assert.Equal(["Commands you run most", "Folders lately", "Files lately", "Tabs"], box.Headings);
        Assert.Equal("Compare directories", box.Items[0].Name);
        Assert.Equal(0, box.SelectedIndex);
        Assert.Equal("Ctrl+Shift+P", box.KeyText);
        Assert.Contains(box.Items, i => i is { Kind: QuickKind.Folder, Name: "Reports", Key: "today" });
        Assert.Contains(box.Items, i => i is { Kind: QuickKind.File, Name: "plan.txt", Key: "yesterday" });
        Capture(w, "quick-open.png");

        Press(w, Key.Escape);
        Assert.False(box.IsVisible);
        Assert.True(w.ActivePanel.View.IsFocused);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Typing_FindsFoldersAndFilesBelow_EnterGoesToTheFile()
    {
        var w = await Open();
        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Type(w, "rep");
        await WaitForWalk(w);
        var box = w.QuickOpen!;
        Assert.Contains(box.Items, i => i is { Kind: QuickKind.Folder, Name: "Reports" });
        Assert.Contains(box.Items, i => i is { Kind: QuickKind.File, Name: "report-q3.pdf", Detail: "Reports" });
        Assert.Contains(box.Items, i => i is { Kind: QuickKind.File, Name: "weekly-report.md", Detail: "Meetings" });
        Assert.Contains("Files in " + _dir, box.Headings);
        Assert.StartsWith("Read ", box.Note);
        Capture(w, "quick-open-typing.png");

        box.SelectedIndex = box.Items.ToList().FindIndex(i => i.Name == "report-q3.pdf");
        Assert.StartsWith("Enter go to it", box.FootText);
        Press(w, Key.Enter);
        await Until(() => w.ActivePanel.Panel.Path == Path.Join(_dir, "Reports"));
        Assert.Equal("report-q3.pdf", w.ActivePanel.Panel.FocusedEntry?.Name);
        Assert.False(box.IsVisible);
        Assert.Empty(((NoShellAccess)w.ShellAccess).Opened);
        w.Close();
    }

    [AvaloniaFact]
    public async Task AFolder_OpensHere_CtrlEnterInTheOtherPanel_AltEnterInANewTab()
    {
        var w = await Open();
        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Type(w, "old");
        await WaitForWalk(w);
        var box = w.QuickOpen!;
        Assert.Equal("Old", box.SelectedItem?.Name);
        Assert.StartsWith("Enter open", box.FootText);
        Press(w, Key.Enter, RawInputModifiers.Control);
        await Until(() => w.RightPanel.Panel.Path == Path.Join(_dir, "Reports", "Old"));
        Assert.Equal(_dir, w.LeftPanel.Panel.Path);

        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Type(w, "meet");
        await WaitForWalk(w);
        var tabs = w.TabsOf(w.ActivePanel).Count;
        Press(w, Key.Enter, RawInputModifiers.Alt);
        await Until(() => w.TabsOf(w.ActivePanel).Count == tabs + 1 && w.ActivePanel.Panel.Path == Path.Join(_dir, "Meetings"));
        w.Close();
    }

    [AvaloniaFact]
    public async Task AGreaterThan_FindsCommandsWithTheirKeys_EnterRunsOne()
    {
        var w = await Open();
        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Type(w, ">sel all");
        var box = w.QuickOpen!;
        Assert.Equal(QuickScope.Commands, box.ChosenScope);
        Assert.Equal(["Commands"], box.Headings);
        var all = box.Items.First(i => i.Target == "Selection.SelectAll");
        Assert.Equal("Ctrl+A", all.Key);
        Assert.True(all.Enabled);

        Type(w, ">save sel");
        var later = box.Items.First(i => i.Target == "Selection.Save");
        Assert.False(later.Enabled);
        Assert.StartsWith("arrives in", later.Detail);

        Type(w, ">select all");
        box.SelectedIndex = box.Items.ToList().FindIndex(i => i.Target == "Selection.SelectAll");
        Press(w, Key.Enter);
        await Until(() => w.ActivePanel.Panel.FindSelected(0, 1) >= 0);
        Assert.Contains(w.Services.Settings.Current.History.QuickCommands, e => e.Item == "Selection.SelectAll");
        w.Close();
    }

    [AvaloniaFact]
    public async Task Tab_GoesThroughTheKinds_AndAPathCompletes()
    {
        var w = await Open();
        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        var box = w.QuickOpen!;
        Type(w, "rep");
        Press(w, Key.Tab);
        Assert.Equal(">rep", box.Input.Text);
        Press(w, Key.Tab);
        Assert.Equal("/rep", box.Input.Text);
        Press(w, Key.Tab);
        Assert.Equal(QuickScope.Path, box.ChosenScope);
        Press(w, Key.Tab, RawInputModifiers.Shift);
        Assert.Equal(QuickScope.Files, box.ChosenScope);

        Type(w, Path.Join(_dir, "Re"));
        await Until(() => box.Items.Any(i => i.Target == Path.Join(_dir, "Reports")));
        Assert.Equal(["Path"], box.Headings);
        Press(w, Key.Tab);
        Assert.Equal(Path.Join(_dir, "Reports") + "\\", box.Input.Text);
        await Until(() => box.SelectedItem?.Target == Path.Join(_dir, "Reports"));
        Press(w, Key.Enter);
        await Until(() => w.ActivePanel.Panel.Path == Path.Join(_dir, "Reports"));
        w.Close();
    }

    [AvaloniaFact]
    public async Task TabsAndSettings_AreFound()
    {
        var w = await Open();
        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Type(w, "@meet");
        var box = w.QuickOpen!;
        Assert.Contains(box.Items, i => i is { Kind: QuickKind.Tab, Name: "Meetings", Key: "right" } && i.Detail == Path.Join(_dir, "Meetings"));
        Press(w, Key.Enter);
        Assert.Same(w.RightPanel, w.ActivePanel);

        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Type(w, ",start with");
        Assert.Contains(box.Items, i => i is { Kind: QuickKind.Setting, Name: "Start with Windows" });
        Type(w, ",repeat last");
        Assert.DoesNotContain(box.Items, i => i.Kind == QuickKind.Setting && i.Name == "Repeat last tool command");
        Type(w, ",start with");
        box.SelectedIndex = box.Items.ToList().FindIndex(i => i.Name == "Start with Windows");
        Press(w, Key.Enter);
        await Until(() => w.OpenOptions is not null);
        Assert.Equal("general", w.OpenOptions!.CurrentCategory);
        w.OpenOptions.Close();
        Dispatcher.UIThread.RunJobs();
        w.Close();
    }

    [AvaloniaFact]
    public async Task WhatYouOpen_IsRemembered_UnlessSwitchedOff_AndAFileOpensWhenOptionsSaySo()
    {
        var w = await Open();
        await w.ActivePanel.Panel.NavigateAsync(Path.Join(_dir, "Meetings"));
        Assert.Equal(Path.Join(_dir, "Meetings"), w.Services.Settings.Current.History.QuickFolders[0].Item);

        w.Services.Settings.Update(s => s with { QuickOpen = s.QuickOpen with { Remember = false } });
        await w.ActivePanel.Panel.NavigateAsync(Path.Join(_dir, "Reports"));
        Assert.DoesNotContain(w.Services.Settings.Current.History.QuickFolders, e => e.Item == Path.Join(_dir, "Reports"));

        w.Services.Settings.Update(s => s with { QuickOpen = new QuickOpenSettings { Remember = true, FileEnter = QuickOpenFileEnter.Open } });
        await w.ActivePanel.Panel.NavigateAsync(_dir);
        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Type(w, "plan.txt");
        await WaitForWalk(w);
        var box = w.QuickOpen!;
        box.SelectedIndex = box.Items.ToList().FindIndex(i => i is { Kind: QuickKind.File, Name: "plan.txt" });
        Assert.StartsWith("Enter open it", box.FootText);
        Press(w, Key.Enter);
        await Until(() => ((NoShellAccess)w.ShellAccess).Opened.Count == 1);
        Assert.Equal(Path.Join(_dir, "plan.txt"), ((NoShellAccess)w.ShellAccess).Opened[0]);
        Assert.Equal(Path.Join(_dir, "plan.txt"), w.Services.Settings.Current.History.QuickFiles[0].Item);
        w.Close();
    }

    [AvaloniaFact]
    public async Task AClickElsewhere_ClosesTheBox()
    {
        var w = await Open();
        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.True(w.QuickOpen!.IsVisible);
        w.RightPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.QuickOpen.IsVisible);
        w.Close();
    }
}
