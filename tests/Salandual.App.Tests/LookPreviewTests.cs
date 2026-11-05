// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/LookPreviewTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class LookPreviewTests : IDisposable
{
    private static SettingsService Settings => ((App)Application.Current!).Services.Settings;

    public LookPreviewTests() => Settings.Update(_ => new AppSettings { Appearance = AppearanceSettings.Fixed(ThemeCatalog.SalandualLightId) });

    public void Dispose()
    {
        Settings.RevertLook();
        Settings.Update(_ => new AppSettings());
        Dispatcher.UIThread.RunJobs();
    }

    private static OptionsWindow Open(string category = "themes")
    {
        var w = new OptionsWindow { Width = 1100, Height = 800 };
        w.Show();
        w.ShowCategory(category);
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(Window w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Use(OptionsWindow w, string id)
    {
        w.UseNow(ThemeCatalog.Find(Settings.Current.Appearance, id));
        Dispatcher.UIThread.RunJobs();
    }

    private static string OnScreen => Settings.Current.Appearance.Schedule.ActiveId(false, 0);

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public void NothingChanged_NoBar()
    {
        var w = Open();
        Assert.False(w.LookBar.IsVisible);
        Assert.False(w.HasLookDot("themes"));
        w.Close();
    }

    [AvaloniaFact]
    public void UseOfANewTheme_CountsDown_AndGoesBackAtZero()
    {
        var w = Open();
        Use(w, "nord");
        Assert.Equal("nord", OnScreen);
        Assert.True(w.LookBar.IsVisible);
        Assert.Equal("Nord instead of Salandual Light", w.LookBarTitle);
        var left = w.CountdownLeft!.Value;
        Assert.InRange(left, OptionsWindow.CountdownSeconds - 3, OptionsWindow.CountdownSeconds);
        Assert.Equal($"Keep ({left})", w.KeepButton.Content);
        Assert.Contains($"Salandual Light comes back in {left} s", w.LookBarDetail, StringComparison.Ordinal);
        Assert.True(w.HasLookDot("themes"));
        Assert.False(w.HasLookDot("appearance"));
        Capture(w, "look-countdown.png");

        while (w.CountdownLeft > 1)
        {
            w.CountdownTick();
        }

        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Keep (1)", w.KeepButton.Content);
        Assert.Equal("nord", OnScreen);

        w.CountdownTick();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeCatalog.SalandualLightId, OnScreen);
        Assert.Null(Settings.KeptLook);
        Assert.False(w.LookBar.IsVisible);
        Assert.Null(w.CountdownLeft);
        w.Close();
    }

    [AvaloniaFact]
    public void Keep_SavesIt_AndAKeptThemeHasNoCountdown()
    {
        var w = Open();
        w.KeptShownFor = TimeSpan.FromMinutes(5);
        Use(w, "nord");
        Click(w.KeepButton);
        Assert.Null(Settings.KeptLook);
        Assert.Contains("nord", Settings.Current.Appearance.KeptThemes);
        Assert.True(w.LookBar.IsVisible);
        Assert.Equal("Kept.", w.LookBarTitle);
        Assert.False(w.KeepButton.IsVisible);
        Assert.False(w.HasLookDot("themes"));
        Capture(w, "look-kept.png");

        Use(w, ThemeCatalog.SalandualLightId);
        Assert.True(w.LookBar.IsVisible);
        Assert.Equal("Salandual Light instead of Nord", w.LookBarTitle);
        Assert.Null(w.CountdownLeft);
        Assert.Equal("Shown on the whole screen. Keep it, or Revert to Nord.", w.LookBarDetail);
        Capture(w, "look-use.png");

        Click(w.RevertButton);
        Assert.Equal("nord", OnScreen);
        Assert.False(w.LookBar.IsVisible);

        Use(w, "dracula");
        Use(w, "nord");
        Assert.Null(Settings.KeptLook);
        Assert.False(w.LookBar.IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void AnotherChange_StopsTheCountdown()
    {
        var w = Open();
        Use(w, "nord");
        Settings.TryLook(a => a with { PanelFont = a.PanelFont with { Size = 14 } });
        Dispatcher.UIThread.RunJobs();
        Assert.Null(w.CountdownLeft);
        Assert.Equal("Nord and the panel font", w.LookBarTitle);
        Assert.Equal("Keep all of it, or Revert to the look you had.", w.LookBarDetail);
        Assert.True(w.HasLookDot("themes"));
        Assert.True(w.HasLookDot("appearance"));
        w.RevertLook();
        w.Close();
    }

    [AvaloniaFact]
    public void PanelFont_WaitsUnderTheBar()
    {
        var w = Open("appearance");
        Settings.TryLook(a => a with { PanelFont = new FontSetting { Family = "Segoe UI", Size = 13 } });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Panel font Segoe UI 13 (was System default 12)", w.LookBarTitle);
        Assert.Equal("Shown in the panels now.", w.LookBarDetail);
        Capture(w, "look-fonts.png");

        Settings.TryLook(a => a with { PanelFont = new FontSetting { Family = "Segoe UI", Size = 14 } });
        Settings.KeepLook();
        Settings.TryLook(a => a with { PanelFont = new FontSetting { Family = "Segoe UI", Size = 13 } });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Panel font Segoe UI 13 (was 14)", w.LookBarTitle);
        w.RevertLook();
        w.Close();
    }

    [AvaloniaFact]
    public void Escape_Reverts_CtrlEnter_Keeps()
    {
        var w = Open();
        var closed = false;
        w.Closed += (_, _) => closed = true;
        Use(w, "nord");
        Press(w, Key.Escape);
        Assert.False(closed);
        Assert.Equal(ThemeCatalog.SalandualLightId, OnScreen);
        Assert.Null(Settings.KeptLook);

        Use(w, "dracula");
        Press(w, Key.Enter, RawInputModifiers.Control);
        Assert.Null(Settings.KeptLook);
        Assert.Equal("dracula", OnScreen);
        Assert.False(closed);

        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Closing_Asks_CancelStays_KeepCloses()
    {
        var w = Open();
        var closed = false;
        w.Closed += (_, _) => closed = true;
        Use(w, "nord");
        w.CloseAsking();
        Dispatcher.UIThread.RunJobs();
        Assert.False(closed);
        var ask = Assert.IsType<Salandual.App.Dialogs.ConfirmDialog>(w.LookQuestion);
        Assert.Equal("Keep the new look?", ask.Title);
        Assert.Equal("Nord instead of Salandual Light. Revert puts Salandual Light back.", ask.Message);
        Capture(ask, "look-close.png");

        Click(ask.Third!);
        Assert.False(closed);
        Assert.NotNull(Settings.KeptLook);

        w.CloseAsking();
        Dispatcher.UIThread.RunJobs();
        Click(w.LookQuestion!.No);
        Assert.True(closed);
        Assert.Null(Settings.KeptLook);
        Assert.Equal("nord", OnScreen);
    }

    [AvaloniaFact]
    public void Closing_Revert_PutsItBack()
    {
        var w = Open();
        var closed = false;
        w.Closed += (_, _) => closed = true;
        Use(w, "nord");
        w.CloseAsking();
        Dispatcher.UIThread.RunJobs();
        Click(w.LookQuestion!.Yes);
        Assert.True(closed);
        Assert.Equal(ThemeCatalog.SalandualLightId, OnScreen);
        Assert.Null(Settings.KeptLook);
    }

    [AvaloniaFact]
    public void ThemeColours_InWords()
    {
        var mine = new ColorTheme { Id = "mine-look", Name = "Mine", Colors = new() { [ColorTokens.Text] = "#000000" } };
        var kept = ThemeEditing.Add(new AppearanceSettings(), mine);
        var now = ThemeEditing.Change(kept, mine.Id, t => ThemeEditing.SetColor(ThemeEditing.SetColor(t, ColorTokens.Text, new Rgb(1, 2, 3)), ColorTokens.Accent, new Rgb(4, 5, 6)));
        var changes = LookChanges.Describe(kept, now, s => s.ActiveId(false, 0));
        Assert.Equal("2 colors of Mine changed", changes.Title);
        Assert.Equal("Text and Accent; Mine isn't on screen now.", changes.Detail);
        Assert.Equal(["themes"], changes.Pages);

        now = ThemeEditing.Change(now, mine.Id, t => t with { Background = t.Background with { Opacity = 0.5 } });
        var used = now with { Schedule = now.Schedule with { Mode = ScheduleMode.None, Active = mine.Id } };
        changes = LookChanges.Describe(kept with { Schedule = used.Schedule }, used, s => s.ActiveId(false, 0));
        Assert.Equal("Mine changed", changes.Title);
        Assert.Equal("Text, Accent and the background image; shown on the whole screen while Mine is in use.", changes.Detail);
    }

    [AvaloniaFact]
    public void Rename_IsSavedAtOnce()
    {
        var mine = new ColorTheme { Id = "mine-look2", Name = "Mine" };
        Settings.Update(s => s with { Appearance = ThemeEditing.Add(s.Appearance, mine) });
        Settings.TryLook(a => ThemeEditing.Change(a, mine.Id, t => ThemeEditing.SetColor(t, ColorTokens.Text, new Rgb(1, 2, 3))));
        Settings.Update(s => s with { Appearance = ThemeEditing.Change(s.Appearance, mine.Id, t => t with { Name = "Ours" }) });
        Settings.RevertLook();
        var theme = Assert.Single(Settings.Current.Appearance.Themes);
        Assert.Equal("Ours", theme.Name);
        Assert.False(theme.Colors.ContainsKey(ColorTokens.Text) && theme.Colors[ColorTokens.Text] == "#010203");
    }

    [AvaloniaFact]
    public void UseOfADarkTheme_InWindowsLightMode_ChangesTheSchedule()
    {
        Settings.Update(_ => new AppSettings());
        var w = Open();
        Use(w, "nord");
        Assert.Equal("The theme schedule changed", w.LookBarTitle);
        Assert.Null(w.CountdownLeft);
        Assert.Equal("nord", Settings.Current.Appearance.Schedule.Dark);
        w.RevertLook();
        Assert.Equal(ThemeCatalog.SalandualDarkId, Settings.Current.Appearance.Schedule.Dark);
        w.Close();
    }

    [AvaloniaFact]
    public void CloseFromCode_Reverts_WithoutAsking()
    {
        var w = Open();
        var closed = false;
        w.Closed += (_, _) => closed = true;
        Use(w, "nord");
        w.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
        Assert.Null(w.LookQuestion);
        Assert.Null(Settings.KeptLook);
        Assert.Equal(ThemeCatalog.SalandualLightId, OnScreen);
    }
}
