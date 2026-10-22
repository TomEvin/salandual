// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/OptionsWindowTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.App.Theme;
using Salandual.Core.Commands;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class OptionsWindowTests : IDisposable
{
    private static App CurrentApp => (App)Application.Current!;

    private static SettingsService Settings => CurrentApp.Services.Settings;

    public OptionsWindowTests() => Settings.Update(_ => new AppSettings());

    public void Dispose()
    {
        Settings.Update(_ => new AppSettings());
        Dispatcher.UIThread.RunJobs();
    }

    private static OptionsWindow Open(string category = "appearance")
    {
        var w = new OptionsWindow();
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

    private static IEnumerable<T> Find<T>(Control root) => root.GetLogicalDescendants().OfType<T>();

    private static ToggleButton Segment(Control root, string label) => Find<ToggleButton>(root).First(b => b.Content as string == label);

    [AvaloniaFact]
    public void Compare_and_search_holds_Searchs_settings()
    {
        Settings.Update(s => s with { Find = s.Find with { Named = ["*.md"], Texts = ["deploy"] } });
        var w = Open("compare");
        var focus = RowControl<ToggleSwitch>(w.Page, "Go to the results when something is found");
        Assert.True(focus.IsChecked);
        focus.IsChecked = false;
        RowControl<ToggleSwitch>(w.Page, "F3 on a result opens at the first match").IsChecked = false;
        RowControl<ToggleSwitch>(w.Page, "The viewer's F3 looks for Search's text").IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(Settings.Current.Find.FocusResults);
        Assert.False(Settings.Current.Find.ViewAtMatch);
        Assert.False(Settings.Current.Find.ViewerKeepsText);

        var add = Find<TextBox>(w.Page).Single(b => b.PlaceholderText == Resources.AppStrings.Find_AddSkipped);
        add.Text = "node_modules";
        add.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = add });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["System Volume Information", "$Recycle.Bin", "node_modules"], Settings.Current.Find.SkippedFolders);
        Find<Button>(w.Page).First(b => (b.Content as string)?.StartsWith("$Recycle.Bin", StringComparison.Ordinal) == true).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["System Volume Information", "node_modules"], Settings.Current.Find.SkippedFolders);

        Find<Button>(w.Page).Single(b => b.Content as string == "Forget them").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Settings.Current.Find.Named);
        Assert.Empty(Settings.Current.Find.Texts);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Compare_and_search_sets_the_defaults_a_new_Search_starts_with()
    {
        var w = Open("compare");
        Assert.Contains(Find<TextBlock>(w.Page), t => t.Text == "*.* · The active panel's folder · All levels · No advanced settings");
        var builtIn = Find<Button>(w.Page).Single(b => b.Content as string == "Built-in");
        Assert.False(builtIn.IsEnabled);

        Find<Button>(w.Page).Single(b => b.Content as string == "Change…").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 100 && w.SearchDefaultsWindow is null; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        var defaults = w.SearchDefaultsWindow!;
        Assert.True(defaults.EditsDefaults);
        defaults.NamedBox.Text = "*.md";
        defaults.TextRows[0].Box.Text = "deploy";
        defaults.MatchCaseBox.IsChecked = true;
        defaults.MinSizeBox.Text = "30";
        Assert.True(defaults.SaveDefaults());
        for (var i = 0; i < 100 && w.SearchDefaultsWindow is not null; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("*.md", Settings.Current.Find.Defaults.Named);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Find<TextBlock>(w.Page), t => t.Text == "*.md · The active panel's folder · All levels · containing “deploy” · Match case · Size ≥ 30 KB");

        builtIn = Find<Button>(w.Page).Single(b => b.Content as string == "Built-in");
        Assert.True(builtIn.IsEnabled);
        builtIn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(Settings.Current.Find.Defaults.IsBuiltIn());
        w.Close();
    }

    [AvaloniaFact]
    public void Confirmations_has_Closing_Search()
    {
        var w = Open("confirm");
        var close = RowControl<ToggleSwitch>(w.Page, "Closing Search");
        Assert.False(close.IsChecked);
        close.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Settings.Current.Confirmations.CloseSearch);
        Assert.DoesNotContain(Find<TextBlock>(w.Page), t => t.Text == "Closing Find, stopping a search");
        w.Close();
    }

    [AvaloniaFact]
    public void ACustomMapSite_IsUsedOnlyOnceItHasANameAndAnAddress()
    {
        var w = Open("viewers");
        TextBox Box(bool address) => Find<TextBox>(w.Page).Single(b => address ? b.PlaceholderText?.Contains("{lat}", StringComparison.Ordinal) == true : b.PlaceholderText?.StartsWith("e.g.", StringComparison.Ordinal) == true);
        void Click(string label)
        {
            Segment(w.Page, label).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        Click("Google Maps");
        Click("Custom");
        Assert.True(Segment(w.Page, "Custom").IsChecked);
        Assert.Equal(MapSite.GoogleMaps, Settings.Current.Viewers.MapSite);

        Box(address: true).Text = "https://maps.example/{lat},{lon}";
        Assert.Equal(MapSite.GoogleMaps, Settings.Current.Viewers.MapSite);
        Box(address: false).Text = "Mapy";
        Assert.Equal(MapSite.Custom, Settings.Current.Viewers.MapSite);

        Box(address: false).Text = "";
        Assert.Equal(MapSite.GoogleMaps, Settings.Current.Viewers.MapSite);

        w.ShowCategory("panels");
        w.ShowCategory("viewers");
        Dispatcher.UIThread.RunJobs();
        Assert.True(Segment(w.Page, "Google Maps").IsChecked);
        Assert.Equal("https://maps.example/{lat},{lon}", Settings.Current.Viewers.MapCustomUrl);
        w.Close();
    }

    [AvaloniaFact]
    public void EveryCategory_Builds()
    {
        var w = Open();
        foreach (var id in new[] { "appearance", "layout", "panels", "keyboard", "hotpaths", "operations", "confirm", "viewers", "editors", "compare", "general", "advanced" })
        {
            w.ShowCategory(id);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(id, w.CurrentCategory);
            Assert.True(w.Page.Children.Count >= 2, id);
        }

        w.Close();
    }

    [AvaloniaFact]
    public void ViewerMaskTable_Reflows_WhenNarrow()
    {
        var w = Open("viewers");
        void NoOverlaps()
        {
            var boxes = w.ViewerMaskGrid!.Children.Where(c => c.IsVisible && c is not TextBlock).Select(c => c.Bounds).ToList();
            for (var i = 0; i < boxes.Count; i++)
            {
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    Assert.False(boxes[i].Intersects(boxes[j]), $"{boxes[i]} and {boxes[j]} overlap");
                }
            }
        }

        w.Width = 1100;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, w.ViewerMaskGrid!.ColumnDefinitions.Count);
        NoOverlaps();

        w.Width = 760;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, w.ViewerMaskGrid!.ColumnDefinitions.Count);
        NoOverlaps();
        w.Close();
    }

    [AvaloniaFact]
    public void ThemeCard_Previews_AndUseAppliesIt()
    {
        Settings.Update(s => s with { Appearance = AppearanceSettings.Fixed(ThemeCatalog.SalandualLightId) });
        var w = Open("themes");
        Assert.Equal(44, w.ThemeCards.Count);

        var dracula = w.ThemeCards.First(c => (string?)c.Tag == "dracula");
        dracula.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ColorMath.ParseHex("#F8F8F2"), w.ThemeBigPreview!.Colors![ColorTokens.Text]);
        Assert.Equal(ThemeCatalog.SalandualLightId, Settings.Current.Appearance.Schedule.Active);

        Find<Button>(w.Page).First(b => b.Content as string == "Use").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("dracula", Settings.Current.Appearance.Schedule.Active);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        Assert.Equal("dracula", CurrentApp.Theme.ActiveTheme.Id);
        w.Close();
    }

    [AvaloniaFact]
    public void Search_FindsAcrossCategories_AndEnterOpensThePage()
    {
        var w = Open();
        w.SearchBox.Text = "row";
        w.SearchNow();

        Assert.True(w.IsSearching);
        Assert.Contains(w.Find("row"), i => i.Title == "Row height");
        Assert.Contains(w.Find("hidden"), i => i.Title == "Show hidden and system files");
        Assert.Contains(w.Find("F5"), i => i.SearchOnly && i.Keywords.Contains("F5", StringComparison.Ordinal));

        w.SearchBox.Focus();
        Press(w, Key.Enter);
        Assert.False(w.IsSearching);
        Assert.Equal("layout", w.CurrentCategory);
        w.Close();
    }

    [AvaloniaFact]
    public void Escape_ClearsTheSearchFirst_ThenCloses()
    {
        var w = Open();
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.SearchBox.Text = "font";
        w.SearchNow();

        Press(w, Key.Escape);
        Assert.False(w.IsSearching);
        Assert.False(closed);

        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void CtrlF_FocusesTheSearch()
    {
        var w = Open();
        Press(w, Key.F, RawInputModifiers.Control);
        Assert.True(w.SearchBox.IsFocused);
        w.Close();
    }

    [AvaloniaFact]
    public void RowHeightSlider_ChangesTheSetting()
    {
        var w = Open("layout");
        var slider = Find<Slider>(w.Page).First();
        slider.Value = 28;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(28, Settings.Current.Layout.RowHeight);
        Assert.Contains(Find<TextBlock>(w.Page), t => t.Text == "28 px · Comfortable");
        w.Close();
    }

    [AvaloniaFact]
    public void SizeUnits_AndDateFormat_Apply()
    {
        var w = Open("panels");
        Segment(w.Page, "KiB, MiB").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var dates = Find<ComboBox>(w.Page).First(c => c.Items.OfType<ComboBoxItem>().Any(i => i.Content is string s && s.StartsWith("magyar", StringComparison.Ordinal)));
        dates.SelectedIndex = dates.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => ((string)i.Content!).StartsWith("magyar", StringComparison.Ordinal));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(SizeUnits.Binary, Settings.Current.Panels.SizeUnits);
        Assert.Equal("hu-HU", Settings.Current.Panels.DateFormat);
        w.Close();
    }

    [AvaloniaFact]
    public void Schedule_Modes_AndResetThemes()
    {
        Settings.Update(s => s with { Appearance = new AppearanceSettings() });
        var w = Open("themes");
        Find<Button>(w.Page).First(b => b.Content is TextBlock { Text: "Schedule" }).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var radios = Find<RadioButton>(w.Page).Where(r => r.GroupName == "themeSchedule").ToList();
        Assert.Equal(3, radios.Count);
        Assert.True(radios[1].IsChecked);

        radios[2].IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ScheduleMode.Custom, Settings.Current.Appearance.Schedule.Mode);
        Assert.Contains(Find<TimePicker>(w.Page), t => t.SelectedTime == TimeSpan.FromHours(7));
        Button DefaultTimes() => Find<Button>(w.Page).First(b => b.Content as string == "Back to the default times");
        Assert.False(DefaultTimes().IsEnabled);

        Settings.Update(s => s with { Appearance = s.Appearance with { Schedule = s.Appearance.Schedule with { Changes = [new() { Minute = 600, Theme = "nord" }] } } });
        w.ShowCategory("themes");
        Find<Button>(w.Page).First(b => b.Content is TextBlock { Text: "Schedule" }).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        DefaultTimes().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new ThemeSchedule().Changes, Settings.Current.Appearance.Schedule.Changes);
        Assert.Equal(ScheduleMode.Custom, Settings.Current.Appearance.Schedule.Mode);

        Settings.Update(s => s with { Appearance = s.Appearance with { Schedule = new() } });
        Assert.Equal(ScheduleMode.Windows, Settings.Current.Appearance.Schedule.Mode);
        w.Close();
    }

    [AvaloniaFact]
    public void Rebuild_KeepsTheScrollPosition_RightAway()
    {
        var w = Open("viewers");
        w.PageScroll.Offset = new Avalonia.Vector(0, 300);
        Dispatcher.UIThread.RunJobs();
        w.Rebuild();
        Assert.Equal(300, w.PageScroll.Offset.Y);
        w.Close();
    }

    [AvaloniaFact]
    public void Keyboard_ListsEveryCommand_AndFilters()
    {
        var w = Open("keyboard");
        var filter = Find<TextBox>(w.Page).First();
        var count = CurrentApp.Services.Commands.Commands.Count;
        Assert.Contains(Find<TextBlock>(w.Page), t => t.Text == $"{count} commands");

        filter.Text = "F5";
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Find<TextBlock>(w.Page), t => t.Text == $"{count} commands");
        w.Close();
    }

    [AvaloniaFact]
    public async Task OptionsCommand_OpensTheDialog_FromTheMainWindow()
    {
        var main = new MainWindow { Width = 1100, Height = 700 };
        main.Show();
        Assert.True(main.Run("App.Options", PanelTarget.Active));
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(main.OpenOptions);
        Assert.True(main.OpenOptions!.IsVisible);
        main.OpenOptions.Close();
        await Task.Delay(10);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(main.OpenOptions);
        main.Close();
    }

    [AvaloniaFact]
    public void ThemePreview_DrawsEachView()
    {
        var w = Open("themes");
        w.Width = 1100;
        w.Height = 900;
        Dispatcher.UIThread.RunJobs();
        foreach (var id in new[] { "dracula", "far", "catppuccin-latte" })
        {
            w.ThemeCards.First(c => (string?)c.Tag == id).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            foreach (var view in new[] { "Window", "Viewer", "Dialog" })
            {
                Segment(w.Page, view).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Enum.Parse<ThemePreviewView>(view), w.ThemeBigPreview!.View);
                if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
                {
                    Directory.CreateDirectory(dir);
                    w.CaptureRenderedFrame()!.Save(Path.Combine(dir, $"themes-{id}-{view.ToLowerInvariant()}.png"), PngBitmapEncoderOptions.Default);
                }
            }
        }

        Segment(w.Page, "Window").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Segment(w.Page, "Dark").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.All(w.ThemeCards.Where(c => c.IsVisible), c => Assert.True(ThemeCatalog.Find(Settings.Current.Appearance, (string?)c.Tag).Dark));

        var search = Find<TextBox>(w.Page).First(t => t.InnerRightContent == w.ThemeSearchClear);
        Assert.False(w.ThemeSearchClear!.IsVisible);
        search.Text = "nord";
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.ThemeSearchClear.IsVisible);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } searched)
        {
            w.CaptureRenderedFrame()!.Save(Path.Combine(searched, "themes-search.png"), PngBitmapEncoderOptions.Default);
        }

        Assert.All(w.ThemeCards.Where(c => c.IsVisible), c => Assert.Contains("Nord", ThemeCatalog.Find(Settings.Current.Appearance, (string?)c.Tag).Name));
        w.ThemeSearchClear.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", search.Text);
        Assert.False(w.ThemeSearchClear.IsVisible);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } cards)
        {
            Segment(w.Page, "All").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            w.ThemeCards[10].BringIntoView();
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame()!.Save(Path.Combine(cards, "themes-cards.png"), PngBitmapEncoderOptions.Default);
            Find<Button>(w.Page).First(b => b.Content is TextBlock { Text: "Schedule" }).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Find<RadioButton>(w.Page).Last(r => r.GroupName == "themeSchedule").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame()!.Save(Path.Combine(cards, "themes-schedule.png"), PngBitmapEncoderOptions.Default);
        }

        w.Close();
    }

    [AvaloniaFact]
    public void Compare_directories_remember_greys_the_starts_at_once()
    {
        var w = Open("compare");
        var start = RowControl<ComboBox>(w.Page, "The list starts with");
        var kind = RowControl<ComboBox>(w.Page, "Different starts with");
        var names = RowControl<TextBox>(w.Page, "Filter names to start with");
        Assert.True(start.IsEnabled && kind.IsEnabled && names.IsEnabled);
        RowControl<ToggleSwitch>(w.Page, "Remember the list's last view").IsChecked = true;
        Assert.False(start.IsEnabled || kind.IsEnabled || names.IsEnabled);
        Assert.True(Settings.Current.Compare.ListRemember);
        RowControl<ToggleSwitch>(w.Page, "Remember the list's last view").IsChecked = false;
        Assert.True(start.IsEnabled && kind.IsEnabled && names.IsEnabled);
    }

    [AvaloniaFact]
    public void Compare_files_with_another_program_and_the_comparators_defaults()
    {
        var w = Open("compare");
        var arguments = RowControl<TextBox>(w.Page, "Arguments");
        Assert.False(arguments.IsEffectivelyEnabled);
        Segment(w.Page, "Another program").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(ToggleButton.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(Settings.Current.Compare.Files.UseProgram);
        arguments = RowControl<TextBox>(w.Page, "Arguments");
        Assert.True(arguments.IsEffectivelyEnabled);
        Assert.Contains("Choose a program…", RowControl<ComboBox>(w.Page, "Program").Items.OfType<ComboBoxItem>().Select(i => i.Content as string));
        arguments.Text = "--diff \"$(Left)\" \"$(Right)\"";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("--diff \"$(Left)\" \"$(Right)\"", Settings.Current.Compare.Files.ProgramArguments);

        RowControl<ToggleSwitch>(w.Page, "Text in code colours").IsChecked = false;
        RowControl<ToggleSwitch>(w.Page, "Ignore case").IsChecked = true;
        RowControl<ToggleSwitch>(w.Page, "Start with only the differences").IsChecked = true;
        var context = RowControl<TextBox>(w.Page, "Start with only the differences");
        context.Text = "80";
        context.Focus();
        arguments.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new FileCompareSettings { CodeColors = false, IgnoreCase = true, OnlyDifferences = true, Context = 50, UseProgram = true, Program = Settings.Current.Compare.Files.Program, ProgramArguments = "--diff \"$(Left)\" \"$(Right)\"" }, Settings.Current.Compare.Files);
        Assert.Equal("50", context.Text);
        Assert.NotNull(Find<TextBlock>(w.Page).FirstOrDefault(t => t.Text == "Spacing or case"));
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            w.Width = 1100;
            w.Height = 1000;
            Dispatcher.UIThread.RunJobs();
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "options-compare.png"), PngBitmapEncoderOptions.Default);
        }

        w.Close();
    }

    private static T RowControl<T>(Control page, string title)
        where T : Control
        => Find<TextBlock>(page).First(t => t.Text == title).GetLogicalAncestors().OfType<Grid>().First().GetLogicalDescendants().OfType<T>().First();

    [AvaloniaFact]
    public void OperationsConfirmationsAndGeneral_ChangeTheirSettings()
    {
        var w = Open("operations");
        Segment(w.Page, "Keep both").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Segment(w.Page, "nowhere (permanently)").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        RowControl<ToggleSwitch>(w.Page, "Check the copies").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((Core.Operations.ExistingFilePolicy.KeepBoth, false, true), (Settings.Current.Operations.IfExists, Settings.Current.Operations.RecycleByDefault, Settings.Current.Operations.Verify));

        w.ShowCategory("confirm");
        Dispatcher.UIThread.RunJobs();
        var create = RowControl<ToggleSwitch>(w.Page, "Creating a target folder that doesn't exist");
        Assert.False(create.IsChecked);
        Assert.False(RowControl<ToggleSwitch>(w.Page, "Closing Salandual").IsChecked);
        Assert.False(RowControl<ToggleSwitch>(w.Page, "Adding files to an existing archive").IsEnabled);
        Assert.DoesNotContain(Find<TextBlock>(w.Page), t => t.Text == "Replacing files");
        create.IsChecked = true;
        RowControl<ToggleSwitch>(w.Page, "Deleting files and folders").IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Settings.Current.Confirmations.CreateTargetFolder);
        Assert.False(Settings.Current.Confirmations.Delete);

        w.ShowCategory("general");
        Dispatcher.UIThread.RunJobs();
        var single = RowControl<ToggleSwitch>(w.Page, "One Salandual at a time");
        Assert.True(single.IsChecked);
        single.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(Settings.Current.General.SingleInstance);

        Assert.Contains(w.Find("merge"), i => i.Category == "confirm");
        Assert.Contains(w.Find("single instance"), i => i.Category == "general");
        w.Close();
    }
}

public sealed class BackgroundImageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-bg-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(BackgroundFit.Stretch, BackgroundPosition.Center, 0, 0, 400, 200)]
    [InlineData(BackgroundFit.Fill, BackgroundPosition.Center, 0, -100, 400, 400)]
    [InlineData(BackgroundFit.Fit, BackgroundPosition.Center, 100, 0, 200, 200)]
    [InlineData(BackgroundFit.Fit, BackgroundPosition.TopLeft, 0, 0, 200, 200)]
    [InlineData(BackgroundFit.Center, BackgroundPosition.BottomRight, 300, 100, 100, 100)]
    public void Place_FitsAndPositions(BackgroundFit fit, BackgroundPosition position, double x, double y, double w, double h)
        => Assert.Equal(new Rect(x, y, w, h), BackgroundImageService.Place(new Size(100, 100), new Rect(0, 0, 400, 200), fit, position));

    [AvaloniaFact]
    public async Task Apply_DecodesInTheBackground_AndReportsBadFiles()
    {
        var png = Path.Combine(_dir, "kép.png");
        using (var bitmap = new RenderTargetBitmap(new PixelSize(8, 4)))
        {
            using (var ctx = bitmap.CreateDrawingContext())
            {
                ctx.FillRectangle(Brushes.Orange, new Rect(0, 0, 8, 4));
            }

            bitmap.Save(png, PngBitmapEncoderOptions.Default);
        }

        var bad = Path.Combine(_dir, "not-an-image.png");
        await File.WriteAllTextAsync(bad, "hello");

        var service = new BackgroundImageService();
        service.Apply(new BackgroundImageSettings { Path = png });
        Assert.True(service.IsLoading);
        await WaitUntil(() => !service.IsLoading);
        Assert.NotNull(service.Image);
        Assert.Equal(new PixelSize(8, 4), service.Image!.PixelSize);
        Assert.Null(service.Error);

        service.Apply(new BackgroundImageSettings { Path = bad });
        await WaitUntil(() => !service.IsLoading);
        Assert.Null(service.Image);
        Assert.NotNull(service.Error);

        service.Apply(new BackgroundImageSettings());
        Assert.Null(service.Image);
        Assert.Null(service.Error);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }
}

public sealed class OptionsCaptureTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-optcap-").FullName;

    private static SettingsService Settings => ((App)Application.Current!).Services.Settings;

    public void Dispose()
    {
        Settings.Update(_ => new AppSettings());
        Dispatcher.UIThread.RunJobs();
        Directory.Delete(_dir, recursive: true);
    }

    [AvaloniaFact]
    public async Task Capture_Pages()
    {
        Settings.Update(_ => new AppSettings());
        var w = new OptionsWindow { Width = 1000, Height = 760 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        foreach (var dark in new[] { false, true })
        {
            Settings.Update(s => s with { Appearance = s.Appearance with { Schedule = AppearanceSettings.Fixed(dark ? ThemeCatalog.SalandualDarkId : ThemeCatalog.SalandualLightId).Schedule } });
            Dispatcher.UIThread.RunJobs();
            var suffix = dark ? "dark" : "light";
            foreach (var id in new[] { "appearance", "layout", "panels", "keyboard", "operations", "advanced" })
            {
                w.ShowCategory(id);
                Dispatcher.UIThread.RunJobs();
                Save(w, $"options-{id}-{suffix}.png");
            }

            w.ShowCategory("layout");
            Dispatcher.UIThread.RunJobs();
            w.Page.Children[^2].BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Save(w, $"options-layout-titlebar-{suffix}.png");
            w.Page.Children[^1].BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Save(w, $"options-layout-bars-{suffix}.png");
            w.PageScroll.Offset = new Vector(0, w.PageScroll.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            Save(w, $"options-layout-bars-end-{suffix}.png");
            w.Page.Children[^2].BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Save(w, $"options-layout-title-{suffix}.png");

            w.ShowCategory("appearance");
            Dispatcher.UIThread.RunJobs();
            w.Page.Children[^3].BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Save(w, $"options-appearance-colors-{suffix}.png");
            w.Page.Children[^1].BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Save(w, $"options-appearance-bottom-{suffix}.png");

            w.ShowCategory("viewers");
            Dispatcher.UIThread.RunJobs();
            w.PageScroll.Offset = new Vector(0, w.PageScroll.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            Save(w, $"options-viewers-end-{suffix}.png");

            w.SearchBox.Text = "row";
            w.SearchNow();
            Save(w, $"options-search-{suffix}.png");
            w.SearchBox.Text = "";
        }

        w.Close();

        var image = Path.Combine(_dir, "gradient.png");
        using (var bitmap = new RenderTargetBitmap(new PixelSize(1200, 800)))
        {
            using (var ctx = bitmap.CreateDrawingContext())
            {
                ctx.FillRectangle(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Colors.DeepSkyBlue, 0), new GradientStop(Colors.MediumPurple, 0.5), new GradientStop(Colors.Orange, 1) },
                }, new Rect(0, 0, 1200, 800));
                ctx.DrawEllipse(Brushes.White, null, new Point(800, 300), 180, 180);
            }

            bitmap.Save(image, PngBitmapEncoderOptions.Default);
        }

        var data = ((App)Application.Current!).Services.DataDirectory;
        var theme = ThemeEditing.Duplicate(ThemeCatalog.BuiltIn[0], Settings.Current.Appearance, DateTime.Now) with { Id = "capture-background" };
        theme = theme with { Background = new BackgroundImageSettings { Path = ThemeFiles.CopyImage(data, theme.Id, image), Opacity = 0.2 } };
        Settings.Update(s => s with { Appearance = ThemeEditing.Add(s.Appearance, theme) with { Schedule = AppearanceSettings.Fixed(theme.Id).Schedule } });
        var main = new MainWindow { Width = 1280, Height = 720 };
        main.Show();
        var left = Directory.CreateDirectory(Path.Combine(_dir, "Nyaralás 2026")).FullName;
        foreach (var name in new[] { "Balaton", "Tihany" })
        {
            Directory.CreateDirectory(Path.Combine(left, name));
        }

        foreach (var name in new[] { "IMG_0412.HEIC", "IMG_0413.HEIC", "panoráma.avif", "árak.json", "jegyzetek.md" })
        {
            await File.WriteAllBytesAsync(Path.Combine(left, name), new byte[2048]);
        }

        Assert.True(await main.LeftPanel.Panel.NavigateAsync(left));
        Assert.True(await main.RightPanel.Panel.NavigateAsync(_dir));
        for (var i = 0; i < 300 && (((App)Application.Current!).BackgroundImage.Image is null || main.LeftPanel.Panel.Path.Length == 0); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Save(main, "main-background-panels.png");
        Settings.Update(s => s with { Appearance = ThemeEditing.Change(s.Appearance, theme.Id, t => t with { Background = t.Background with { Placement = BackgroundPlacement.Window, Fit = BackgroundFit.Fit } }) });
        Dispatcher.UIThread.RunJobs();
        Save(main, "main-background-window.png");
        main.Close();
        ThemeFiles.DeleteFolder(data, theme.Id);
    }

    private static void Save(Window w, string name)
    {
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }
}
