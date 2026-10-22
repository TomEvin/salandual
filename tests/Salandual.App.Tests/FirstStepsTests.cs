// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/FirstStepsTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Wizard;
using Salandual.Core;
using Salandual.Core.Commands;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class FirstStepsTests : IDisposable
{
    private static App CurrentApp => (App)Application.Current!;

    public void Dispose()
    {
        CurrentApp.Theme.Apply(AppearanceSettings.Fixed(ThemeCatalog.SalandualLightId));
        var shared = AppServices.CreateIsolated().DataDirectory;
        foreach (var kept in Directory.Exists(shared) ? Directory.GetFiles(shared, "settings.before-*.json") : [])
        {
            File.Delete(kept);
        }
    }

    private static FirstStepsWindow Open(AppServices services, bool fromHelp = false)
    {
        var w = new FirstStepsWindow(services, fromHelp);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Click(Control c)
    {
        c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static T Tagged<T>(FirstStepsWindow w, object tag)
        where T : Control
        => w.GetLogicalDescendants().OfType<T>().First(c => Equals(c.Tag, tag));

    private static List<string?> Texts(FirstStepsWindow w)
        => [.. w.PageContent!.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Concat(t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? []))];

    [AvaloniaFact]
    public void OpensByItself_OnlyOnAFirstStart()
    {
        Assert.True(MainWindow.OfferFirstSteps(SettingsLoadStatus.NotFound, isolated: false, measuring: false));
        Assert.False(MainWindow.OfferFirstSteps(SettingsLoadStatus.Loaded, isolated: false, measuring: false));
        Assert.False(MainWindow.OfferFirstSteps(SettingsLoadStatus.Corrupt, isolated: false, measuring: false));
        Assert.False(MainWindow.OfferFirstSteps(SettingsLoadStatus.NotFound, isolated: true, measuring: false));
        Assert.False(MainWindow.OfferFirstSteps(SettingsLoadStatus.NotFound, isolated: false, measuring: true));
    }

    [AvaloniaFact]
    public void HelpMenu_HasFirstSteps()
    {
        var help = MainMenu.Create().Last();
        var ids = MainMenu.Flatten(help.Children!).Select(n => n.CommandId).ToList();
        Assert.Equal(["Help.Contents", "Help.WhatIsThis", "Help.FirstSteps", "App.About"], ids);
        Assert.Equal("First steps", CommandRegistry.CreateDefault()["Help.FirstSteps"].Title);
        Assert.True(MainWindow.IsBuilt("Help.FirstSteps"));
    }

    [AvaloniaFact]
    public void Steps_OneSize_NextBackAndTheLastStep()
    {
        var w = Open(AppServices.CreateIsolated());
        Assert.Equal((860, 640), (w.Width, w.Height));
        Assert.False(w.CanResize);
        Assert.Equal(FirstStepsWindow.Step.Welcome, w.Current);
        Assert.False(w.BackButton.IsEnabled);
        Assert.Equal("Skip, use the defaults", w.SkipButton.Content);
        Assert.Contains("Welcome to Salandual", Texts(w));

        var seen = new List<FirstStepsWindow.Step> { w.Current };
        while (w.Current != FirstStepsWindow.Step.Ready)
        {
            Click(w.NextButton);
            seen.Add(w.Current);
        }

        Assert.Equal(Enum.GetValues<FirstStepsWindow.Step>(), seen);
        Assert.Equal("Start Salandual", w.NextButton.Content);
        Assert.False(w.SkipButton.IsVisible);
        Assert.Contains("All set", Texts(w));
        Click(w.BackButton);
        Assert.Equal(FirstStepsWindow.Step.FileManager, w.Current);
        Assert.Contains("comes later", Texts(w));
        w.Close();
    }

    [AvaloniaFact]
    public void Theme_ThreeAndThree_PickedShowsAtOnce_AndTheAccentMakesYourCopy()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services);
        w.Show(FirstStepsWindow.Step.Theme);
        var cards = w.PageContent!.GetLogicalDescendants().OfType<Button>().Select(b => b.Tag as string).Where(t => t?.StartsWith("theme:", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(["theme:salandual-light", "theme:classic-light", "theme:nord-light", "theme:salandual-dark", "theme:graphite", "theme:dracula"], cards);

        Click(Tagged<Button>(w, "theme:dracula"));
        Assert.Equal("dracula", w.Choices.Dark);
        Assert.True(w.Choices.ThemeChanged);
        Click(Tagged<ToggleButton>(w, "accent:#D13438"));
        Assert.Equal(AccentChoice.Colour, w.Choices.Accent);

        var follow = Tagged<ToggleSwitch>(w, "follow");
        follow.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.Choices.FollowWindows);
        Assert.Equal(w.WindowsDark ? "dracula" : ThemeCatalog.SalandualLightId, w.Choices.One);
        Click(Tagged<Button>(w, "theme:nord-light"));
        Assert.Equal("nord-light", w.Choices.One);
        Assert.Equal("Nord Light with your accent", CurrentApp.Theme.ActiveTheme.Name);

        var s = w.Choices.ApplyTo(services.Settings.Current, DateTime.Now);
        Assert.Equal(ScheduleMode.None, s.Appearance.Schedule.Mode);
        var copy = Assert.Single(s.Appearance.Themes);
        Assert.Equal("Nord Light with your accent", copy.Name);
        Assert.Equal((ThemeAccent.Custom, "#D13438"), (copy.Accent, copy.CustomAccent));
        Assert.Equal(copy.Id, s.Appearance.Schedule.Active);

        var again = (w.Choices with { Colour = FirstStepsChoices.Colours[3] }).ApplyTo(s, DateTime.Now);
        Assert.Equal("#107C10", Assert.Single(again.Appearance.Themes).CustomAccent);

        w.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeCatalog.Find(services.Settings.Current.Appearance, services.Settings.Current.Appearance.Schedule.ActiveId(w.WindowsDark, 0)).Id, CurrentApp.Theme.ActiveTheme.Id);
    }

    [AvaloniaFact]
    public void Theme_ItsOwnAccent_IsTheThemeItself()
    {
        var services = AppServices.CreateIsolated();
        var c = FirstStepsChoices.From(services.Settings.Current, startsWithWindows: false, windowsDark: false) with { ThemeChanged = true, Light = "classic-light", Dark = "graphite" };
        var s = c.ApplyTo(services.Settings.Current, DateTime.Now);
        Assert.Empty(s.Appearance.Themes);
        Assert.Equal((ScheduleMode.Windows, "classic-light", "graphite"), (s.Appearance.Schedule.Mode, s.Appearance.Schedule.Light, s.Appearance.Schedule.Dark));

        var custom = services.Settings.Current with { Appearance = services.Settings.Current.Appearance with { Schedule = new ThemeSchedule { Mode = ScheduleMode.Custom } } };
        var kept = FirstStepsChoices.From(custom, false, false).ApplyTo(custom, DateTime.Now);
        Assert.Equal(ScheduleMode.Custom, kept.Appearance.Schedule.Mode);
    }

    [AvaloniaFact]
    public void Keyboard_TotalCommander_WithWhatGoesWithIt_EachUntickable()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services);
        w.Show(FirstStepsWindow.Step.Keyboard);
        Assert.Contains(("Ctrl+F9", "Refresh", false), w.MainKeys(KeyMapName.Salamander));
        Assert.Contains(("Alt+F10", "Calculate occupied space", false), w.MainKeys(KeyMapName.Salamander));

        Assert.Contains(("F2", "Refresh", true), w.MainKeys(KeyMapName.TotalCommander));
        Assert.Contains(("Shift+F6", "Rename", true), w.MainKeys(KeyMapName.TotalCommander));
        Assert.Contains(("Ctrl+Alt+a…z", "Quick search", true), w.MainKeys(KeyMapName.TotalCommander));
        Assert.Contains("Vim", Texts(w));

        Click(Tagged<Button>(w, "keys:TotalCommander"));
        Assert.Equal(KeyMapName.TotalCommander, w.Choices.Keys);
        Assert.Contains("What goes with Total Commander's keys", Texts(w));
        var quick = Tagged<CheckBox>(w, "tc:quick");
        Assert.True(quick.IsChecked);
        Assert.True(Tagged<CheckBox>(w, "tc:right").IsChecked);

        var s = w.Choices.ApplyTo(services.Settings.Current, DateTime.Now);
        Assert.Equal((KeyMapName.TotalCommander, QuickSearchStart.CtrlAltLetter, RightClickUnselected.RightButtonSelects), (s.Keyboard.KeyMap, s.Panels.QuickSearch, s.Mouse.RightClickUnselected));

        quick.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        s = w.Choices.ApplyTo(services.Settings.Current, DateTime.Now);
        Assert.Equal((KeyMapName.TotalCommander, QuickSearchStart.Letters), (s.Keyboard.KeyMap, s.Panels.QuickSearch));

        var back = FirstStepsChoices.From(s, false, false) with { Keys = KeyMapName.Salamander };
        var salamander = back.ApplyTo(s, DateTime.Now);
        Assert.Equal((QuickSearchStart.Letters, RightClickUnselected.MenuForItem), (salamander.Panels.QuickSearch, salamander.Mouse.RightClickUnselected));
        w.Close();
    }

    [AvaloniaFact]
    public void BarsPanelsAndReady_GoIntoTheSettings_OnlyOnStart()
    {
        var services = AppServices.CreateIsolated();
        var w = Open(services);
        w.Show(FirstStepsWindow.Step.Bars);
        Tagged<CheckBox>(w, "bar:middle").IsChecked = false;
        Click(Tagged<ToggleButton>(w, "row2"));
        Assert.Contains("28 px per row", Texts(w));

        w.Show(FirstStepsWindow.Step.Panels);
        Click(Tagged<ToggleButton>(w, "units1"));
        Assert.Contains("25 690 112 · 2 048", Texts(w).Select(t => t?.Replace(' ', ' ').Replace(' ', ' ')));
        Click(Tagged<ToggleButton>(w, "time2"));
        Tagged<RadioButton>(w, "start:Home").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        w.Show(FirstStepsWindow.Step.Ready);
        var summary = w.SummaryLines().ToDictionary(l => l.Label, l => l.Value);
        Assert.Equal("menu bar, drive strip, hot paths bar, F-key bar", summary["Bars"]);
        Assert.Equal("Comfortable", summary["Row height"]);
        Assert.Equal("Bytes", summary["Sizes"]);
        Assert.Equal("ISO · 12-hour", summary["Dates and time"]);
        Assert.Equal("Your user folder", summary["Folders at start"]);
        Assert.Equal("Salamander's", summary["Keys"]);
        Tagged<CheckBox>(w, "startWithWindows").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(24, services.Settings.Current.Layout.RowHeight);

        var main = new MainWindow(services);
        main.Show();
        var result = new FirstStepsResult(w.Choices, null, null);
        w.Close();
        main.ApplyFirstSteps(result, fromHelp: false);
        Dispatcher.UIThread.RunJobs();
        var s = services.Settings.Current;
        Assert.Equal((28, false, SizeUnits.Bytes, TimeFormat.Hours12, StartFolders.Home), (s.Layout.RowHeight, s.Layout.Bars.MiddleToolbar, s.Panels.SizeUnits, s.Panels.TimeFormat, s.General.StartFolders));
        Assert.True(StartWithWindowsState.IsOn(services.StartWithWindows, services.ProgramPath));
        Assert.Null(main.NoticeMessage);
        main.Close();
    }

    [AvaloniaFact]
    public void FromHelp_StartsFromYourSettings_CancelAndDone_WithUndo()
    {
        var services = AppServices.CreateIsolated();
        services.Settings.Update(s => s with { Layout = s.Layout with { RowHeight = 22 }, Keyboard = s.Keyboard with { KeyMap = KeyMapName.TotalCommander } });
        var w = Open(services, fromHelp: true);
        Assert.Equal("Cancel", w.SkipButton.Content);
        Assert.Contains("First steps with Salandual", Texts(w));
        Assert.Equal((22, KeyMapName.TotalCommander), (w.Choices.RowHeight, w.Choices.Keys));
        w.Show(FirstStepsWindow.Step.Ready);
        Assert.Equal("Done", w.NextButton.Content);
        Assert.True(w.SkipButton.IsVisible);
        Assert.Contains("Ready to apply", Texts(w));

        var main = new MainWindow(services);
        main.Show();
        var result = new FirstStepsResult(w.Choices with { RowHeight = 28 }, null, null);
        w.Close();
        main.ApplyFirstSteps(result, fromHelp: true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(28, services.Settings.Current.Layout.RowHeight);
        Assert.Equal("Your choices from First steps are in use.", main.NoticeMessage);
        Click(main.NoticeButtons.First(b => Equals(b.Content, "Undo")));
        Assert.Equal(22, services.Settings.Current.Layout.RowHeight);
        main.Close();
    }

    [AvaloniaFact]
    public async Task ImportSettings_SkipsTheStepsInBetween()
    {
        var services = AppServices.CreateIsolated();
        var dir = Directory.CreateTempSubdirectory("salandual-firststeps-").FullName;
        try
        {
            var file = Path.Combine(dir, "home.saldset");
            var mine = new AppSettings { Panels = new PanelSettings { TimeFormat = TimeFormat.Hours12 }, Layout = new LayoutSettings { RowHeight = 28 } };
            File.WriteAllText(file, SettingsTransfer.Export(mine, new ExportChoice(), new DateTime(2026, 10, 12, 9, 0, 0), "Salandual 0.1"));
            var import = SettingsTransfer.Read(file, services.Settings.Current);
            IReadOnlySet<SettingsPart> parts = import.Changes.Select(c => c.Part).ToHashSet();

            var w = Open(services);
            w.PickImport = () => Task.FromResult<(SettingsImport, IReadOnlySet<SettingsPart>)?>((import, parts));
            Click(Tagged<Button>(w, "import"));
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(w.Imported);
            Assert.Contains(Texts(w), t => t?.StartsWith("Imported from home.saldset:", StringComparison.Ordinal) == true);
            Click(w.NextButton);
            Assert.Equal(FirstStepsWindow.Step.Ready, w.Current);
            Assert.Contains("Window sizes (they belong to this screen)", Texts(w));
            Click(w.BackButton);
            Assert.Equal(FirstStepsWindow.Step.Welcome, w.Current);

            var main = new MainWindow(services);
            main.Show();
            var result = new FirstStepsResult(w.Choices, import, parts);
            w.Close();
            main.ApplyFirstSteps(result, fromHelp: false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((28, TimeFormat.Hours12), (services.Settings.Current.Layout.RowHeight, services.Settings.Current.Panels.TimeFormat));
            Assert.StartsWith("Imported ", main.NoticeMessage, StringComparison.Ordinal);
            main.Close();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Capture_EveryStep()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        void Save(Window w, string name) => w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);

        var w = Open(AppServices.CreateIsolated());
        foreach (var step in Enum.GetValues<FirstStepsWindow.Step>())
        {
            w.Show(step);
            Dispatcher.UIThread.RunJobs();
            Save(w, $"wizard-{(int)step}-{step}.png".ToLowerInvariant());
        }

        w.Show(FirstStepsWindow.Step.Keyboard);
        Click(Tagged<Button>(w, "keys:TotalCommander"));
        Save(w, "wizard-2-tc.png");
        w.Show(FirstStepsWindow.Step.Theme);
        Click(Tagged<Button>(w, "theme:dracula"));
        Tagged<ToggleSwitch>(w, "follow").IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Click(Tagged<Button>(w, "theme:dracula"));
        Save(w, "wizard-1-dracula.png");
        w.Close();

        var help = Open(AppServices.CreateIsolated(), fromHelp: true);
        Save(help, "wizard-help-0.png");
        help.Show(FirstStepsWindow.Step.Ready);
        Save(help, "wizard-help-6.png");
        help.Close();
    }
}
