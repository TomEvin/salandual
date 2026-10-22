// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/MainWindowTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.Core;
using Salandual.Core.Commands;

namespace Salandual.App.Tests;

public sealed class MainWindowTests
{
    private static async Task<MainWindow> Open()
    {
        var window = new MainWindow { Width = 1100, Height = 700 };
        window.Show();
        for (var i = 0; i < 300 && (window.LeftPanel.Panel.Path.Length == 0 || window.RightPanel.Panel.Path.Length == 0); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

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

    [AvaloniaFact]
    public async Task Opens_WithTwoListedPanels_AndPathInTitle()
    {
        var w = await Open();
        Assert.NotEmpty(w.LeftPanel.Panel.Path);
        Assert.NotEmpty(w.RightPanel.Panel.Path);
        Assert.Equal(Salandual.Core.Panels.WindowTitle.Expand(Salandual.Core.Panels.WindowTitle.Default, w.ActivePanel.Panel.Path, AppBuild.Name), w.Title);
        Assert.True(w.LeftPanel.IsActive);
        Assert.False(w.RightPanel.IsActive);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Tab_SwitchesTheActivePanel()
    {
        var w = await Open();
        Press(w, Key.Tab);
        Assert.True(w.RightPanel.IsActive);
        Assert.Equal(Salandual.Core.Panels.WindowTitle.Expand(Salandual.Core.Panels.WindowTitle.Default, w.RightPanel.Panel.Path, AppBuild.Name), w.Title);
        Press(w, Key.Tab);
        Assert.True(w.LeftPanel.IsActive);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Menu_HasSalamanderMenus_AndEveryItemIsACommand()
    {
        var w = await Open();
        Assert.Equal(MainMenu.Create().Count, w.MainMenuBar.Items.Count);
        Assert.Equal(MainMenu.Flatten(MainMenu.Create()).Count(), w.MenuItems.Count);
        Assert.Contains(w.MenuItems, m => m.Node.CommandId == "Panel.SortBySize" && m.Item.InputGesture?.ToString() == "Ctrl+F6");
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task LeftMenuCommand_ActsOnTheLeftPanel_EvenWhenRightIsActive()
    {
        var w = await Open();
        Press(w, Key.Tab);
        Assert.True(w.Run("Panel.SortBySize", PanelTarget.Left));
        Assert.Equal(Core.Listing.SortColumn.Size, w.LeftPanel.Panel.Sort.Column);
        Assert.Equal(Core.Listing.SortColumn.Name, w.RightPanel.Panel.Sort.Column);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlShiftO_OpensOptions()
    {
        var w = await Open();
        Press(w, Key.O, RawInputModifiers.Control | RawInputModifiers.Shift);
        for (var i = 0; i < 200 && w.OpenOptions is null; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.NotNull(w.OpenOptions);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task NotYetBuiltCommand_ShowsWhenItArrives()
    {
        var w = await Open();
        Press(w, Key.F10, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.True(w.Notice.IsVisible);
        Assert.Contains("Phase 3", w.NoticeText.Text, StringComparison.Ordinal);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task BarToggles_HideAndShowBars()
    {
        var w = await Open();
        Assert.True(w.Run("View.ToggleInformationLine", PanelTarget.Active));
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.LeftPanel.Info.IsVisible);
        w.Run("View.ToggleInformationLine", PanelTarget.Active);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.LeftPanel.Info.IsVisible);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Splitter_DoubleClick_ResetsTo50Percent()
    {
        var w = await Open();
        w.PanelsGrid.ColumnDefinitions[0].Width = new GridLength(0.3, GridUnitType.Star);
        w.PanelsGrid.ColumnDefinitions[2].Width = new GridLength(0.7, GridUnitType.Star);
        Dispatcher.UIThread.RunJobs();

        var center = w.Splitter.TranslatePoint(new Point(w.Splitter.Bounds.Width / 2, w.Splitter.Bounds.Height - 3), w)!.Value;
        w.MouseDown(center, MouseButton.Left);
        w.MouseUp(center, MouseButton.Left);
        w.MouseDown(center, MouseButton.Left);
        w.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0.5, w.PanelsGrid.ColumnDefinitions[0].Width.Value, 3);
        w.CloseNow();
    }
}

public sealed class MainWindowCaptureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("salandual-main-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string name, params (string Name, int Size)[] files)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var (file, size) in files)
        {
            if (file.EndsWith('/'))
            {
                Directory.CreateDirectory(Path.Combine(dir, file.TrimEnd('/')));
            }
            else
            {
                File.WriteAllBytes(Path.Combine(dir, file), new byte[size]);
            }
        }

        return dir;
    }

    [AvaloniaFact]
    public async Task Capture_LightAndDark()
    {
        var left = Folder("Nyaralás 2026", ("Balaton/", 0), ("Tihany – drón/", 0), ("DSC09231.ARW", 25_690_112), ("IMG_0412.HEIC", 3_565_158), ("IMG_0413.HEIC", 3_250_585),
            ("IMG_0414.HEIC", 3_879_731), ("panoráma.avif", 1_258_291), ("térkép.jxl", 880_640), ("árak.json", 2_048), ("jegyzetek.md", 3_072), ("日本語メモ.txt", 1_024));
        var right = Folder("Photos", ("2024/", 0), ("2025/", 0), ("Nyaralás 2025/", 0), ("IMG_0413.HEIC", 3_040_870), ("index.csv", 18_432));

        var w = new MainWindow { Width = 1280, Height = 720 };
        w.Show();
        Assert.True(await w.LeftPanel.Panel.NavigateAsync(left));
        Assert.True(await w.RightPanel.Panel.NavigateAsync(right));
        var l = w.LeftPanel.Panel;
        for (var i = 0; i < l.Count; i++)
        {
            if (l.Items[i].Name is "IMG_0412.HEIC" or "IMG_0413.HEIC" or "IMG_0414.HEIC")
            {
                l.Toggle(i);
            }
        }

        l.SetFocus(Enumerable.Range(0, l.Count).First(i => l.Items[i].Name == "IMG_0414.HEIC"));
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();

        Save(w, "main-light.png");
        ((App)Avalonia.Application.Current!).Theme.Apply(Core.Settings.AppearanceSettings.Fixed(Core.Settings.ThemeCatalog.SalandualDarkId));
        Dispatcher.UIThread.RunJobs();
        Save(w, "main-dark.png");
        ((App)Avalonia.Application.Current!).Theme.Apply(new Core.Settings.AppearanceSettings());
        w.CloseNow();
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
