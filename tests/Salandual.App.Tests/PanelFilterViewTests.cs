// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/PanelFilterViewTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.App.Wizard;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class PanelFilterViewTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-filter-").FullName;

    public PanelFilterViewTests()
    {
        foreach (var name in new[] { "IMG_0410.jpg", "IMG_0412.heic", "notes.txt", "route.gpx", ".DS_Store" })
        {
            File.WriteAllText(Path.Join(_dir, name), "x");
        }

        Directory.CreateDirectory(Path.Join(_dir, "raw"));
        Directory.CreateDirectory(Path.Join(_dir, ".git"));
        File.WriteAllText(Path.Join(_dir, "raw", "IMG_0411.heic"), "x");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Salandual.Core.Settings.SettingsService Settings => ((App)Application.Current!).Services.Settings;

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

    private static void Type(MainWindow w, string text)
    {
        w.KeyPress(Key.None, RawInputModifiers.None, PhysicalKey.None, null);
        w.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    private static string[] Names(MainWindow w) => [.. w.ActivePanel.Panel.Items.Skip(w.ActivePanel.Panel.HasUpEntry ? 1 : 0).Select(e => e.Name)];

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
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

    [AvaloniaFact]
    public async Task CtrlF12_TheDialog_TheChip_ItsMenu_AndItsX()
    {
        var w = await Open();
        try
        {
            Press(w, Key.F12, RawInputModifiers.Control);
            var dialog = w.OpenFilterDialog;
            Assert.NotNull(dialog);
            Assert.Equal("Filter – left panel", dialog.Title);
            Assert.Equal("No filter", dialog.NoFilter.Content);
            Assert.True(dialog.NoFilter.IsChecked);
            Assert.False(dialog.AdvancedPanel.IsVisible);
            dialog.AdvancedButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialog.AdvancedPanel.IsVisible);
            Assert.Contains(dialog.GetLogicalDescendants().OfType<Button>(), b => b.Content as string == "Pictures");
            Capture(dialog, "filter-dialog.png");

            dialog.MaskBox.Text = "*.jpg;*.heic";
            Assert.True(dialog.UseFilter.IsChecked);
            dialog.NoFilter.IsChecked = true;
            dialog.Accept();
            Assert.Equal("Masks", Avalonia.Automation.AutomationProperties.GetName(dialog.MaskBox));
            await Until(() => w.OpenFilterDialog is null);
            Assert.Null(w.ActivePanel.Panel.Filter);

            Press(w, Key.F12, RawInputModifiers.Control);
            dialog = w.OpenFilterDialog!;
            dialog.UseFilter.IsChecked = true;
            dialog.MaskBox.Text = "*.jpg;*.heic";
            dialog.Accept();
            await Until(() => w.OpenFilterDialog is null);
            Assert.Equal([".git", "raw", "IMG_0410.jpg", "IMG_0412.heic"], Names(w));
            Assert.Equal("*.jpg;*.heic", w.Services.Settings.Current.History.FilterMasks[0]);
            Assert.Equal("*.jpg;*.heic", w.ActivePanel.DirectoryLine.FilterChip);
            Assert.Contains("Filter: 4 of 7 shown, 3 hidden", w.ActivePanel.Info.RightText);
            Capture(w, "filter-chip.png");

            w.CaptureRenderedFrame();
            var chip = w.ActivePanel.DirectoryLine.FilterChipBounds;
            Assert.True(chip.Width > 40, chip.ToString());
            w.ActivePanel.DirectoryLine.Click(chip.TopLeft + new Point(8, 8));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(w.FilterMenu);
            Assert.Equal(["Change the filter…", "Turn the filter off", "Show hidden names", "Show hidden and system files", "Hide dot files and folders"],
                w.FilterMenu.Items.OfType<MenuItem>().Select(i => i.Header as string));
            Capture(w, "filter-menu.png");
            w.FilterMenu.Close();
            Dispatcher.UIThread.RunJobs();

            var off = w.ActivePanel.DirectoryLine.FilterOffBounds;
            w.ActivePanel.DirectoryLine.Click(off.Center);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(w.ActivePanel.Panel.Filter);
            Assert.Null(w.ActivePanel.DirectoryLine.FilterChip);
            Assert.Equal(7, Names(w).Length);

            Press(w, Key.F12, RawInputModifiers.Control);
            dialog = w.OpenFilterDialog!;
            Assert.Equal("*.jpg;*.heic", dialog.MaskBox.Text);
            dialog.DotFilesBox.IsChecked = true;
            dialog.Accept();
            await Until(() => w.OpenFilterDialog is null);
            Assert.True(w.Services.Settings.Current.Panels.HideDotFiles);
            await Until(() => !Names(w).Contains(".git"));
            Assert.Null(w.ActivePanel.Panel.Filter);
        }
        finally
        {
            w.Services.Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task QuickFilter_CtrlSInTheBox_EnterKeepsTheChip_EscBringsAllBack()
    {
        var w = await Open();
        try
        {
            var view = w.ActivePanel.View;
            view.Focus();
            Type(w, "I");
            Assert.Equal("I", w.ActivePanel.Panel.QuickSearchText);
            Assert.False(w.ActivePanel.Panel.QuickFilterMode);
            Press(w, Key.S, RawInputModifiers.Control);
            Assert.True(w.ActivePanel.Panel.IsQuickFiltering);
            Type(w, "MG_041");
            Assert.Equal(["IMG_0410.jpg", "IMG_0412.heic"], Names(w));
            Assert.Contains("Quick filter: 2 of 7 shown", w.ActivePanel.Info.RightText);
            Assert.True(view.QuickSearchBox.Width > 100);
            Capture(w, "filter-quick.png");

            Press(w, Key.Escape);
            Assert.Null(w.ActivePanel.Panel.QuickSearchText);
            Assert.Equal(7, Names(w).Length);
            Assert.StartsWith("IMG_041", w.ActivePanel.Panel.FocusedEntry?.Name);

            Type(w, "n");
            w.CaptureRenderedFrame();
            var box = view.QuickSearchBox;
            Assert.Equal("n", w.ActivePanel.Panel.QuickSearchText);
            Assert.True(box.Width > 100, box.ToString());
            var at = view.TranslatePoint(box.Center, w)!.Value;
            w.MouseDown(at, MouseButton.Left);
            w.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.ActivePanel.Panel.IsQuickFiltering);
            Press(w, Key.Enter);
            Assert.Null(w.ActivePanel.Panel.QuickSearchText);
            Assert.Equal("*n*", w.ActivePanel.DirectoryLine.FilterChip);
            Assert.Equal(["notes.txt"], Names(w));
            Assert.Equal(_dir, w.ActivePanel.Panel.Path);

            await w.ActivePanel.Panel.NavigateAsync(Path.Join(_dir, "raw"));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(w.ActivePanel.DirectoryLine.FilterChip);
        }
        finally
        {
            w.Services.Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task HideSelectedNames_FromTheEditMenu_TheChipBringsThemBack()
    {
        var w = await Open();
        try
        {
            var panel = w.ActivePanel.Panel;
            panel.SelectWhere(e => e.Name is "notes.txt" or "route.gpx", true);
            Assert.True(w.Run("Panel.HideSelectedNames", Core.Commands.PanelTarget.Active));
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("notes.txt", Names(w));
            Assert.Equal("2 names hidden", w.ActivePanel.DirectoryLine.NamesChip);
            Assert.Contains("2 names hidden", w.ActivePanel.Info.RightText);
            Capture(w, "filter-names.png");
            w.CaptureRenderedFrame();
            Assert.True(w.ActivePanel.DirectoryLine.NamesChipBounds.Width > 40);
            w.ActivePanel.DirectoryLine.Click(w.ActivePanel.DirectoryLine.NamesChipBounds.Center);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("notes.txt", Names(w));
            Assert.Null(w.ActivePanel.DirectoryLine.NamesChip);
            Assert.Empty(Core.Commands.DefaultCommands.All().Single(c => c.Id == "Panel.HideSelectedNames").DefaultChords);
        }
        finally
        {
            w.Services.Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public void Options_TheFilter_AndTheDotFiles_FirstSteps()
    {
        try
        {
            var w = new OptionsWindow { Width = 980, Height = 1000 };
            w.Show();
            Assert.Contains(w.Find("dot files"), h => h.Title == "Hide dot files and folders");
            Assert.Contains(w.Find("quick filter"), h => h.Title == "Typing in a panel");
            var hit = w.Find("filter");
            Assert.Contains(hit, h => h.Title == "When you change folders");
            Assert.Contains(w.Find("hide names"), h => h.Title == "Hidden names");
            w.ShowCategory("panels", hit.First(h => h.Title == "When you change folders"));
            Dispatcher.UIThread.RunJobs();
            var only = w.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single(b => b.Content as string == "Only this folder");
            only.IsChecked = true;
            only.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(Settings.Current.Panels.FilterThisFolderOnly);
            Capture(w, "filter-options.png");
            w.Close();

            Assert.False(FirstStepsChoices.From(new AppSettings(), false, false).HideDotFiles);
            var s = (FirstStepsChoices.From(new AppSettings(), false, false) with { HideDotFiles = true }).ApplyTo(new AppSettings(), DateTime.Now);
            Assert.True(s.Panels.HideDotFiles);
        }
        finally
        {
            Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task QuickSearch_TheFirstHitAtTheBottom_StaysClearOfTheChip()
    {
        var many = Path.Join(_dir, "many");
        Directory.CreateDirectory(many);
        for (var i = 0; i < 120; i++)
        {
            File.WriteAllText(Path.Join(many, $"a{i:000}.txt"), "x");
            File.WriteAllText(Path.Join(many, $"z{i:000}.txt"), "x");
        }

        File.WriteAllText(Path.Join(many, "m.txt"), "x");
        var w = await Open();
        try
        {
            await w.ActivePanel.Panel.NavigateAsync(many);
            Dispatcher.UIThread.RunJobs();
            var view = w.ActivePanel.View;

            Type(w, "m");
            Assert.Equal("m.txt", w.ActivePanel.Panel.FocusedEntry?.Name);
            w.CaptureRenderedFrame();
            Assert.True(view.QuickSearchBox.Width > 100);
            Assert.False(view.FocusedCellRect.Intersects(view.QuickSearchBox), view.FocusedCellRect + " under " + view.QuickSearchBox);
            Assert.True(view.QuickSearchBox.Y > view.Bounds.Height / 2, view.QuickSearchBox.ToString());
            Press(w, Key.Escape);

            Press(w, Key.End);
            Type(w, "z119");
            Assert.Equal("z119.txt", w.ActivePanel.Panel.FocusedEntry?.Name);
            w.CaptureRenderedFrame();
            Assert.False(view.FocusedCellRect.Intersects(view.QuickSearchBox), view.FocusedCellRect + " under " + view.QuickSearchBox);
            Assert.True(view.QuickSearchBox.Y < view.Bounds.Height / 2, view.QuickSearchBox.ToString());
        }
        finally
        {
            w.CloseNow();
        }
    }
}
