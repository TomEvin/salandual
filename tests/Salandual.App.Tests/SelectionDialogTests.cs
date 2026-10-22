// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/SelectionDialogTests.cs

using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.Core.Vfs;

namespace Salandual.App.Tests;

public sealed class SelectionDialogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-select-").FullName;

    public SelectionDialogTests()
    {
        foreach (var name in new[] { "a.jpg", "b.png", "c.txt", "IMG_1.JPG" })
        {
            File.WriteAllText(Path.Join(_dir, name), "x");
        }

        Directory.CreateDirectory(Path.Join(_dir, "Sub", "Deeper"));
        File.WriteAllBytes(Path.Join(_dir, "Sub", "one.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Join(_dir, "Sub", "Deeper", "two.bin"), new byte[2000]);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open()
    {
        var window = new MainWindow { Width = 1100, Height = 700 };
        window.Show();
        await window.ActivePanel.Panel.NavigateAsync(_dir);
        window.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Press(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static string[] Selected(MainWindow w)
    {
        var p = w.ActivePanel.Panel;
        return [.. p.Items.Where((_, i) => p.IsSelected(i)).Select(e => e.Name)];
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
    public async Task NumPlus_AsksForMask_SelectsMatches_AndRemembersTheMask()
    {
        var w = await Open();
        Press(w, Key.Add);
        var dialog = w.OpenMaskDialog;
        Assert.NotNull(dialog);
        Assert.Equal("Select", dialog.Title);
        Assert.Equal("*.*", dialog.MaskBox.Text);

        dialog.MaskBox.Text = "*.jpg;*.png";
        dialog.Accept();
        await Until(() => w.OpenMaskDialog is null);

        Assert.Equal(["a.jpg", "b.png", "IMG_1.JPG"], Selected(w));
        Assert.Equal("*.jpg;*.png", w.Services.Settings.Current.History.SelectMasks[0]);

        Press(w, Key.Add);
        Assert.Equal("*.jpg;*.png", w.OpenMaskDialog!.MaskBox.Text);
        w.OpenMaskDialog.Close();
        await Until(() => w.OpenMaskDialog is null);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task NumMinus_Unselects_EmptyMaskIsRefused_EscCancels()
    {
        var w = await Open();
        Press(w, Key.A, RawInputModifiers.Control);
        Assert.Equal(5, Selected(w).Length);

        Press(w, Key.Subtract);
        var dialog = w.OpenMaskDialog!;
        Assert.Equal("Unselect", dialog.Title);
        dialog.MaskBox.Text = "  ";
        dialog.Accept();
        Assert.True(dialog.ErrorText.IsVisible);
        Assert.Same(dialog, w.OpenMaskDialog);

        dialog.MaskBox.Text = "*.jpg";
        dialog.Accept();
        await Until(() => w.OpenMaskDialog is null);
        Assert.Equal(["Sub", "b.png", "c.txt"], Selected(w));

        Press(w, Key.Subtract);
        w.OpenMaskDialog!.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await Until(() => w.OpenMaskDialog is null);
        Assert.Equal(["Sub", "b.png", "c.txt"], Selected(w));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Regex_SelectsAndUnselectsByTheWholeName_CaseIgnored()
    {
        var w = await Open();
        Press(w, Key.Add);
        var dialog = w.OpenMaskDialog!;
        dialog.SetRegex(true);
        dialog.MaskBox.Text = "img";
        dialog.Accept();
        await Until(() => w.OpenMaskDialog is null);
        Assert.Empty(Selected(w));

        Press(w, Key.Add);
        Assert.True(w.OpenMaskDialog!.IsRegex);
        w.OpenMaskDialog.MaskBox.Text = @"img_\d+\.jpg";
        w.OpenMaskDialog.Accept();
        await Until(() => w.OpenMaskDialog is null);
        Assert.Equal(["IMG_1.JPG"], Selected(w));
        Assert.Equal(@"regex:img_\d+\.jpg", w.Services.Settings.Current.History.SelectMasks[0]);

        Press(w, Key.A, RawInputModifiers.Control);
        Press(w, Key.Subtract);
        dialog = w.OpenMaskDialog!;
        dialog.SetRegex(true);
        dialog.MaskBox.Text = @".*\.(JPG|png)";
        dialog.Accept();
        await Until(() => w.OpenMaskDialog is null);
        Assert.Equal(["Sub", "c.txt"], Selected(w));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlW_BringsBackTheSelection()
    {
        var w = await Open();
        Press(w, Key.A, RawInputModifiers.Control);
        Press(w, Key.D, RawInputModifiers.Control);
        Assert.Empty(Selected(w));

        Press(w, Key.W, RawInputModifiers.Control);
        Assert.Equal(5, Selected(w).Length);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Space_OnFolder_ShowsItsSizeInTheInformationLine()
    {
        var w = await Open();
        var panel = w.ActivePanel.Panel;
        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == "Sub"));
        Press(w, Key.Space);
        Assert.Equal(["Sub"], Selected(w));

        Assert.Equal("a.jpg", panel.FocusedEntry?.Name);
        var sub = panel.Items.First(e => e.Name == "Sub");
        await Until(() => panel.TryGetDirectorySize(sub, out _));
        panel.TryGetDirectorySize(sub, out var size);
        Assert.Equal(3000, size);
        Press(w, Key.Up);
        Assert.StartsWith("Sub: 3 KB", w.ActivePanel.Info.LeftText);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task BackInTheParent_TheFolderIsOnTheSameScreenRow()
    {
        for (var i = 0; i < 80; i++)
        {
            File.WriteAllText(Path.Join(_dir, $"file{i:00}.txt"), "x");
        }

        Directory.CreateDirectory(Path.Join(_dir, "Sub", "Deeper", "Even"));
        var w = await Open();
        var view = w.ActivePanel.View;
        var panel = w.ActivePanel.Panel;
        Assert.True(view.PageRows < 40);

        var sub = panel.Items.ToList().FindIndex(e => e.Name == "Sub");
        view.ScrollTo(0);
        panel.SetFocus(sub);
        Dispatcher.UIThread.RunJobs();
        var rowBefore = sub - view.Top;

        Press(w, Key.Enter);
        await Until(() => panel.Path.EndsWith("Sub", StringComparison.Ordinal));
        Press(w, Key.Back);
        await Until(() => !panel.Path.EndsWith("Sub", StringComparison.Ordinal) && panel.FocusedEntry?.Name == "Sub");
        Assert.Equal(rowBefore, panel.Focus - view.Top);

        Press(w, Key.Enter);
        await Until(() => panel.Path.EndsWith("Sub", StringComparison.Ordinal));
        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == "Deeper"));
        Dispatcher.UIThread.RunJobs();
        Press(w, Key.Enter);
        await Until(() => panel.Path.EndsWith("Deeper", StringComparison.Ordinal));
        Press(w, Key.Back);
        await Until(() => panel.FocusedEntry?.Name == "Deeper");
        Assert.Equal(1, panel.Focus - view.Top);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task BackInTheParent_ScrolledList_KeepsTheRow()
    {
        Directory.CreateDirectory(Path.Join(_dir, "zz-last"));
        for (var i = 0; i < 80; i++)
        {
            Directory.CreateDirectory(Path.Join(_dir, $"dir{i:00}"));
        }

        var w = await Open();
        var view = w.ActivePanel.View;
        var panel = w.ActivePanel.Panel;
        var target = panel.Items.ToList().FindIndex(e => e.Name == "dir50");
        view.ScrollTo(target - 7);
        panel.SetFocus(target);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(7, panel.Focus - view.Top);

        Press(w, Key.Enter);
        await Until(() => panel.Path.EndsWith("dir50", StringComparison.Ordinal));
        Press(w, Key.Back);
        await Until(() => panel.FocusedEntry?.Name == "dir50");
        Assert.Equal(7, panel.Focus - view.Top);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Capture_IconsSizesAndDialog()
    {
        foreach (var name in new[] { "árak.json", "jegyzetek.md", "útlevél.pdf", "régi képek.zip", "setup.exe", "index.html", "napló.log", "táblázat.csv" })
        {
            File.WriteAllText(Path.Join(_dir, name), "x");
        }

        var right = Directory.CreateTempSubdirectory("salandual-select-right-").FullName;
        try
        {
            foreach (var name in new[] { "2024", "2025" })
            {
                Directory.CreateDirectory(Path.Join(right, name));
            }

            File.WriteAllText(Path.Join(right, "index.csv"), "x");
            var w = await Open();
            Assert.True(await w.RightPanel.Panel.NavigateAsync(right));
            var panel = w.ActivePanel.Panel;
            panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == "Sub"));
            Press(w, Key.Space);
            await Until(() => panel.TryGetDirectorySize(panel.Items.First(e => e.Name == "Sub"), out _));
            await Task.Delay(1500);
            Dispatcher.UIThread.RunJobs();
            Save(w, "m15-main-icons.png");

            Press(w, Key.Add);
            w.OpenMaskDialog!.MaskBox.Text = "*.jpg;*.png|thumb*";
            Dispatcher.UIThread.RunJobs();
            Save(w.OpenMaskDialog, "m15-select-dialog.png");
            w.OpenMaskDialog.Accept();
            await Until(() => w.OpenMaskDialog is null);
            Save(w, "m15-main-selected.png");
            w.CloseNow();
        }
        finally
        {
            Directory.Delete(right, recursive: true);
        }
    }

    private static void Save(Avalonia.Controls.Window w, string name)
    {
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    [Theory]
    [InlineData("photo.jpg", FileAttributes.Archive, false)]
    [InlineData("setup.EXE", FileAttributes.Archive, true)]
    [InlineData("Salandual.lnk", FileAttributes.Archive, true)]
    [InlineData("README", FileAttributes.Archive, false)]
    [InlineData("Photos", FileAttributes.Directory, false)]
    [InlineData("Desktop", FileAttributes.Directory | FileAttributes.ReadOnly, true)]
    public void OwnIcons_ForProgramsShortcutsAndCustomizedFolders(string name, FileAttributes attributes, bool own)
        => Assert.Equal(own, IconCache.HasOwnIcon(new FileEntry(name, 0, 0, attributes)));
}
