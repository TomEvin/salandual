// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/MenuBarTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Options;
using Salandual.Core.Commands;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class MenuBarTests
{
    private static MainWindow Open(bool menuBar, MenuButtonStyle style = MenuButtonStyle.AppIcon)
    {
        var window = new MainWindow { Width = 1100, Height = 700 };
        window.Services.Settings.Update(s => s with { Layout = s.Layout with { MenuButton = style, Bars = s.Layout.Bars with { MenuBar = menuBar } } });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Menu MenuBar(MainWindow w) => w.FindControl<Menu>("MainMenuBar")!;

    private static void Press(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MenuBarOn_MenusInsideOurTitleBar_OrInTheirOwnRow()
    {
        var w = Open(menuBar: true);
        Assert.True(MenuBar(w).IsVisible);
        Assert.NotNull(w.TitleBar);
        Assert.True(w.TitleBar.IsVisible);
        Assert.True(w.ExtendClientAreaToDecorationsHint);
        Assert.True(w.IsMenuInTitleBar);
        Assert.Equal([false, false], w.TitleBar.Children.OfType<Button>().Select(b => b.IsVisible));

        w.Services.Settings.Update(s => s with { Layout = s.Layout with { MenuRow = MenuRow.OwnRow } });
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.IsMenuInTitleBar);
        Assert.True(MenuBar(w).IsVisible);
        Assert.IsType<DockPanel>(MenuBar(w).Parent);

        w.Services.Settings.Update(s => s with { Layout = s.Layout with { MenuRow = MenuRow.InTitleBar } });
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.IsMenuInTitleBar);
        w.CloseNow();
    }

    [AvaloniaFact]
    public void MenuBarOff_TitleBarWithTheChosenButton_AndBackAgain()
    {
        var w = Open(menuBar: false);
        Assert.False(MenuBar(w).IsVisible);
        Assert.NotNull(w.TitleBar);
        Assert.True(w.TitleBar.IsVisible);
        Assert.True(w.ExtendClientAreaToDecorationsHint);
        var buttons = w.TitleBar.Children.OfType<Button>().ToList();
        Assert.Equal([true, false], buttons.Select(b => b.IsVisible));

        w.Services.Settings.Update(s => s with { Layout = s.Layout with { MenuButton = MenuButtonStyle.Hamburger } });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([false, true], buttons.Select(b => b.IsVisible));

        Assert.True(w.Run("View.ToggleMenuBar", PanelTarget.Active));
        Dispatcher.UIThread.RunJobs();
        Assert.True(MenuBar(w).IsVisible);
        Assert.True(w.IsMenuInTitleBar);
        Assert.Equal([false, false], buttons.Select(b => b.IsVisible));
        w.CloseNow();
    }

    [AvaloniaFact]
    public void AppMenu_HoldsAllMenus_ThenShowMenuBar_Configuration_Exit()
    {
        var w = Open(menuBar: false);
        w.OpenAppMenu();
        Dispatcher.UIThread.RunJobs();
        var headers = w.AppMenu!.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Equal(["_Left", "_Files", "_Edit", "_Commands", "_Tools", "_Options", "_Right", "_Help", "Show _Menu Bar", "O_ptions…", "E_xit"], headers);
        Assert.True(w.AppMenu.IsOpen);
        var show = w.AppMenu.Items.OfType<MenuItem>().First(i => (string?)i.Header == "Show _Menu Bar");
        Assert.False(show.IsChecked);
        w.AppMenu.Hide();
        w.CloseNow();
    }

    [AvaloniaFact]
    public void F10_ShowsTheMenuBarForOneUse()
    {
        var w = Open(menuBar: false);
        Press(w, Key.F10);
        Assert.True(MenuBar(w).IsVisible);
        Assert.True(w.IsMenuBarShownOnce);

        MenuBar(w).Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(MenuBar(w).IsVisible);
        Assert.False(w.IsMenuBarShownOnce);
        w.CloseNow();
    }

    [AvaloniaFact]
    public void AltTap_ShowsTheMenuBar_AltLetter_OpensThatMenu()
    {
        var w = Open(menuBar: false);
        Press(w, Key.LeftAlt, RawInputModifiers.Alt);
        Assert.True(MenuBar(w).IsVisible);
        MenuBar(w).Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(MenuBar(w).IsVisible);

        w.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        w.KeyPress(Key.F, RawInputModifiers.Alt, PhysicalKey.F, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(MenuBar(w).IsVisible);
        var files = MenuBar(w).Items.OfType<MenuItem>().Single(i => (string?)i.Header == "_Files");
        Assert.True(files.IsSubMenuOpen);
        w.KeyRelease(Key.F, RawInputModifiers.Alt, PhysicalKey.F, null);
        w.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(MenuBar(w).IsVisible);
        MenuBar(w).Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(MenuBar(w).IsVisible);
        w.CloseNow();
    }

    [AvaloniaFact]
    public void AltWithAnotherKey_IsNotATap()
    {
        var w = Open(menuBar: false);
        w.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        w.KeyPress(Key.F1, RawInputModifiers.Alt, PhysicalKey.F1, null);
        w.KeyRelease(Key.F1, RawInputModifiers.Alt, PhysicalKey.F1, null);
        w.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(MenuBar(w).IsVisible);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task WindowTitle_FollowsTheTemplateAndTheActivePanel()
    {
        var dir = Directory.CreateTempSubdirectory("salandual-title-").FullName;
        try
        {
            var w = Open(menuBar: true);
            await w.ActivePanel.Panel.NavigateAsync(dir);
            Dispatcher.UIThread.RunJobs();
            var name = Path.GetFileName(dir);
            Assert.Equal($"{name} — Salandual (dev)", w.Title);
            Assert.Equal("Salandual (dev)", AppBuild.Name);

            w.Services.Settings.Update(s => s with { Layout = s.Layout with { WindowTitle = "$(App) — $(Path)" } });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal($"Salandual (dev) — {dir}", w.Title);
            Assert.Equal(w.Title, w.TitleBar!.Children.OfType<TextBlock>().Single().Text);
            w.CloseNow();
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [AvaloniaFact]
    public async Task WindowTitle_ShowsTheVersionAfterTheAppName_WhenAsked()
    {
        var dir = Directory.CreateTempSubdirectory("salandual-title-").FullName;
        try
        {
            var w = Open(menuBar: true);
            await w.ActivePanel.Panel.NavigateAsync(dir);
            Dispatcher.UIThread.RunJobs();
            var name = Path.GetFileName(dir);
            Assert.False(w.Services.Settings.Current.Layout.TitleShowsVersion);

            w.Services.Settings.Update(s => s with { Layout = s.Layout with { TitleShowsVersion = true } });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal($"{name} — Salandual (dev) {Salandual.Core.ProductInfo.DisplayVersion}", w.Title);

            w.Services.Settings.Update(s => s with { Layout = s.Layout with { WindowTitle = "$(Folder)" } });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(name, w.Title);

            w.Services.Settings.Update(s => s with { Layout = s.Layout with { TitleShowsVersion = false, WindowTitle = "$(App) — $(Path)" } });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal($"Salandual (dev) — {dir}", w.Title);
            w.CloseNow();
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [AvaloniaFact]
    public void AltSpace_IsTheWindowMenu_HandledByUs()
    {
        var w = Open(menuBar: true);
        Assert.True(w.Run("App.WindowMenu", PanelTarget.Active));
        Press(w, Key.Space, RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
        w.CloseNow();
    }

    [AvaloniaFact]
    public void Dialogs_HaveOurTitleBar_CloseOnly()
    {
        var options = new OptionsWindow();
        options.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(options.ExtendClientAreaToDecorationsHint);
        Assert.False(options.CanMinimize);
        Assert.False(options.CanMaximize);
        options.Close();

        var select = new Dialogs.SelectMaskDialog(select: true, [], includesFolders: false);
        select.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(select.ExtendClientAreaToDecorationsHint);
        Assert.False(select.CanMinimize);
        select.Close();
    }

    [AvaloniaFact]
    public void Options_BarsPreview_FollowsTheBars()
    {
        var settings = ((App)Avalonia.Application.Current!).Services.Settings;
        settings.Update(_ => new AppSettings());
        var options = new OptionsWindow { Width = 1000, Height = 740 };
        options.Show();
        options.ShowCategory("layout");
        Dispatcher.UIThread.RunJobs();
        var preview = options.GetVisualDescendants().OfType<BarsPreview>().Single();
        Assert.Equal(["title", "hot paths bar", "middle toolbar", "panel strips", "directory lines", "column headers", "information lines", "function keys"], preview.Shown);

        settings.Update(s => s with { Layout = s.Layout with { MenuRow = MenuRow.OwnRow, Strip = StripMode.Shared, Bars = s.Layout.Bars with { InformationLine = false, FunctionKeyBar = false } } });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["title", "menu", "hot paths bar", "shared strip", "middle toolbar", "directory lines", "column headers"], preview.Shown);

        var sticky = preview.GetVisualAncestors().OfType<StickyPanel>().Single();
        options.PageScroll.Offset = new Avalonia.Vector(0, options.PageScroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        Assert.True(sticky.ChildOffset > 0);
        var top = Avalonia.VisualExtensions.TranslatePoint(preview, default, options.PageScroll)!.Value.Y;
        Assert.InRange(top, 0, options.PageScroll.Viewport.Height - preview.Bounds.Height);

        var toggle = options.GetVisualDescendants().OfType<ToggleSwitch>().First(t => t.IsEnabled);
        toggle.Focus();
        options.MouseDown(new Avalonia.Point(600, options.Bounds.Height - 120), Avalonia.Input.MouseButton.Left);
        options.MouseUp(new Avalonia.Point(600, options.Bounds.Height - 120), Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(toggle.IsFocused);
        Assert.True(options.PageScroll.IsFocused);

        options.Close();
        settings.Update(_ => new AppSettings());
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Options_MenuButtonChoice_OnlyWhenTheMenuBarIsOff()
    {
        var settings = ((App)Avalonia.Application.Current!).Services.Settings;
        settings.Update(_ => new AppSettings());
        var options = new OptionsWindow { Width = 1000, Height = 740 };
        options.Show();
        options.ShowCategory("layout");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(options.Items, i => i.Title == "Menus");
        Assert.Contains(options.Items, i => i.Title == "Window title");
        Assert.Contains(options.Items, i => i.Title == "Menu button");

        var ownRow = options.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Own row");
        ownRow.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(MenuRow.OwnRow, settings.Current.Layout.MenuRow);
        Assert.True(settings.Current.Layout.Bars.MenuBar);
        settings.Update(s => s with { Appearance = s.Appearance with { Schedule = AppearanceSettings.Fixed(ThemeCatalog.SalandualDarkId).Schedule } });
        Dispatcher.UIThread.RunJobs();
        var inTitle = options.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "In the title bar");
        inTitle.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var center = Avalonia.VisualExtensions.TranslatePoint(inTitle, new Avalonia.Point(inTitle.Bounds.Width / 2, 20), options)!.Value;
        options.MouseMove(center, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(inTitle.IsPointerOver);
        var presenter = inTitle.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
        options.TryFindResource("Brush.PanelBackground", options.ActualThemeVariant, out var panelBg);
        Assert.Equal(((Avalonia.Media.ISolidColorBrush)panelBg!).Color, ((Avalonia.Media.ISolidColorBrush)presenter.Background!).Color);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            options.CaptureRenderedFrame()!.Save(Path.Combine(dir, "options-cards-hover-dark.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        var shared = options.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Shared");
        shared.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(StripMode.Shared, settings.Current.Layout.Strip);

        options.SearchBox.Text = "hamburger";
        Dispatcher.UIThread.RunJobs();
        Assert.True(options.IsSearching);
        options.SearchBox.Text = "";
        options.Close();
        settings.Update(_ => new AppSettings());
        Dispatcher.UIThread.RunJobs();
    }
}
