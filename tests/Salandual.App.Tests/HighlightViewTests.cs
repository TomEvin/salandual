// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/HighlightViewTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.Core.Panels;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class HighlightViewTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-highlight-").FullName;
    private readonly NoShellAccess _shell = new();

    public HighlightViewTests()
    {
        foreach (var name in new[] { "backup.7z", "notes.txt", "report.bak", "secret.txt" })
        {
            File.WriteAllText(Path.Join(_dir, name), "x");
        }

        File.SetAttributes(Path.Join(_dir, "secret.txt"), FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Join(_dir, "Clients"));
        Directory.CreateDirectory(Path.Join(_dir, "Photos"));
    }

    public void Dispose()
    {
        File.SetAttributes(Path.Join(_dir, "secret.txt"), FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    private static SettingsService Settings => ((App)Application.Current!).Services.Settings;

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.ShellAccess = _shell;
        w.Services.Settings.Update(s => s with { Panels = s.Panels with { ShowHiddenFiles = true } });
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(_dir);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        w.ActivePanel.Panel.SetShowHidden(true);
        await Until(() => Row(w, "secret.txt") >= 0);
        return w;
    }

    private static int Row(MainWindow w, string name) => w.ActivePanel.Panel.Items.ToList().FindIndex(e => e.Name == name);

    private static Color Ink(MainWindow w, string name) => ((ISolidColorBrush)w.ActivePanel.View.NameLookOf(Row(w, name)).Brush).Color;

    private static Color Resource(Control c, string key) => c.TryFindResource(key, c.ActualThemeVariant, out var v) && v is ISolidColorBrush b ? b.Color : throw new InvalidOperationException(key);

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static string Text(object? header) => header as string is { } s ? s.Replace("_", "", StringComparison.Ordinal) : "";

    private static MenuItem Item(ItemsControl menu, string header)
        => menu.Items.OfType<MenuItem>().First(m => Text(m.Header) == header);

    private static void Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
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

    private static void CapturePopup(Control inside, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir && TopLevel.GetTopLevel(inside) is { } top)
        {
            Dispatcher.UIThread.RunJobs();
            top.CaptureRenderedFrame()?.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task Rules_ColourTheNames_TheIdeasWhenOn_TheSelectionKeepsItsColour()
    {
        var w = await Open();
        try
        {
            var view = w.ActivePanel.View;
            Assert.Equal(Resource(view, "Brush.SecondaryText"), Ink(w, "secret.txt"));
            Assert.Equal(Resource(view, "Brush.Text"), Ink(w, "backup.7z"));
            Assert.Equal(FontStyle.Normal, view.NameLookOf(Row(w, "report.bak")).Face.Style);

            w.Services.Settings.Update(s => s with { Highlighting = s.Highlighting with { Rules = [.. HighlightSettings.Defaults().Select(r => r with { On = true })] } });
            await Until(() => Ink(w, "backup.7z") != Resource(view, "Brush.Text"));
            var background = Resource(view, "Brush.PanelBackground");
            var purple = Highlighter.Colour(NameColour.Purple, new Rgb(background.R, background.G, background.B), _ => new Rgb(0, 0, 0))!.Value;
            Assert.Equal(Color.FromRgb(purple.R, purple.G, purple.B), Ink(w, "backup.7z"));
            Assert.Equal(FontStyle.Italic, view.NameLookOf(Row(w, "report.bak")).Face.Style);
            Assert.Equal(Resource(view, "Brush.Text"), Ink(w, "notes.txt"));
            Capture(w, "highlight-rules.png");

            w.ActivePanel.Panel.SelectWhere(e => e.Name == "backup.7z", true);
            Assert.Equal(Resource(view, "Brush.SelectedText"), Ink(w, "backup.7z"));
        }
        finally
        {
            w.Services.Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task FolderColour_FromTheRightClickMenu_ToSeveralFolders_FollowsARename()
    {
        var w = await Open();
        try
        {
            var clients = Path.Join(_dir, "Clients");
            var photos = Path.Join(_dir, "Photos");
            var panel = w.ActivePanel.Panel;
            panel.SelectWhere(e => e.Name is "Clients" or "Photos", true);
            panel.SetFocus(Row(w, "Clients"));
            var view = w.ActivePanel.View;
            var r = view.NameBounds(Row(w, "Clients"))!.Value;
            var p = view.TranslatePoint(new Point(r.X + 20, r.Y + (r.Height / 2)), w)!.Value;
            w.MouseDown(p, MouseButton.Right);
            w.MouseUp(p, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            var menu = w.QuickMenu!;
            Click(Item(menu, "Color and icon…"));
            await Until(() => w.FolderLookPicker is { IsVisible: true });
            var picker = w.FolderLookPicker!;
            Assert.Equal(33, picker.ColourButtons.Count);
            Assert.Equal(["No color", "Red", "Coral", "Orange"], picker.ColourButtons.Take(4).Select(b => Avalonia.Automation.AutomationProperties.GetName(b)));
            var orange = 1 + Array.IndexOf(MainWindow.FolderColours, NameColour.Orange);
            Assert.Equal(33, picker.MarkButtons.Count);
            Assert.False(picker.ResetButton.IsEnabled);
            CapturePopup(picker, "highlight-picker.png");
            picker.ColourButtons[orange].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var folders = w.Services.Settings.Current.Highlighting.Folders;
            Assert.Equal(NameColour.Orange, folders[clients].Colour);
            Assert.Equal(NameColour.Orange, folders[photos].Colour);
            Assert.Same(picker, w.FolderLookPicker);
            Assert.Equal(orange, picker.ColourButtons.ToList().FindIndex(b => b.IsChecked == true));
            Assert.Equal(1, picker.ColourButtons.Count(b => b.IsChecked == true));
            Assert.True(picker.ResetButton.IsEnabled);
            picker.MarkButtons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, picker.MarkButtons.Count(b => b.IsChecked == true));
            Assert.Equal(NameColour.Orange, w.Services.Settings.Current.Highlighting.Folders[photos].Colour);
            Assert.Equal(FolderMarks.All[1], w.Services.Settings.Current.Highlighting.Folders[photos].Mark);
            picker.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await Until(() => w.FolderLookPicker is null);
            w.ShowFolderLookPicker(w.ActivePanel, [clients, photos], new Point(40, 60));
            await Until(() => w.FolderLookPicker is { IsVisible: true });
            Assert.True(w.FolderLookPicker!.ColourButtons[orange].IsChecked);
            Assert.True(w.FolderLookPicker.ResetButton.IsEnabled);
            w.FolderLookPicker.MarkButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("★", w.Services.Settings.Current.Highlighting.Folders[clients].Mark);
            Assert.Equal(NameColour.Orange, w.Services.Settings.Current.Highlighting.Folders[clients].Colour);
            w.FolderLookPicker!.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => w.FolderLookPicker is null);
            menu.Close();
            Dispatcher.UIThread.RunJobs();
            Capture(w, "highlight-folders.png");

            panel.SelectWhere(e => e.Name == "notes.txt", true);
            w.MouseDown(p, MouseButton.Right);
            w.MouseUp(p, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(w.QuickMenu!.Items.OfType<MenuItem>(), m => Text(m.Header) == "Color and icon…");
            w.QuickMenu!.Close();
            Dispatcher.UIThread.RunJobs();

            w.FolderMoved(clients, Path.Join(_dir, "Customers"));
            Assert.False(w.Services.Settings.Current.Highlighting.Folders.ContainsKey(clients));
            Assert.Equal("★", w.Services.Settings.Current.Highlighting.Folders[Path.Join(_dir, "Customers")].Mark);

            w.SetFolderLooks([photos], _ => null);
            Assert.False(w.Services.Settings.Current.Highlighting.Folders.ContainsKey(photos));
        }
        finally
        {
            w.Services.Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Options_TheRules_InOrder_AddEditMoveDelete_AndTheFolders()
    {
        try
        {
            Settings.Update(s => s with { Highlighting = s.Highlighting.WithFolder(@"Q:\Gone\Old projects", new FolderLook { Colour = NameColour.Red, Mark = "★" }) });
            var w = new OptionsWindow { Width = 980, Height = 1000 };
            w.Show();
            Assert.Contains(w.Find("highlighting"), h => h.Section == "Rules");
            Assert.Contains(w.Find("folder color"), h => h.Section == "Folder colors and icons");
            Assert.DoesNotContain(w.Find("dimmed"), h => h.Title == "Hidden and system files dimmed");
            w.ShowCategory("highlight");
            Dispatcher.UIThread.RunJobs();
            var table = w.GetLogicalDescendants().OfType<HighlightTable>().Single();
            Assert.Equal(6, table.List.ItemCount);
            Assert.Equal(["Add a rule…", "Edit the rule…", "Copy the rule", "Delete the rule", "Move up (Alt+↑)", "Move down (Alt+↓)", "Back to the defaults"],
                table.Tools.Select(b => ToolTip.GetTip(b) as string));
            Assert.Equal(((ISolidColorBrush)Panels.FolderLooks.ThemeBrush("EncryptedFiles")!).Color, ((ISolidColorBrush)table.SampleOf!.Foreground!).Color);
            Assert.Equal("Files · *.zip;*.7z;*.rar;*.tar;*.gz", HighlightTable.Summary(HighlightSettings.Defaults()[3]));
            Assert.Equal("Files and folders · Hidden or System", HighlightTable.Summary(HighlightSettings.Defaults()[2]));
            var folders = w.GetLogicalDescendants().OfType<FolderLookTable>().Single();
            Assert.Equal(1, folders.RowCount);
            Capture(w, "highlight-options.png");

            table.List.SelectedIndex = 3;
            table.Move(-1);
            Assert.Equal("Archives", Settings.Current.Highlighting.Rules![2].Name);

            HighlightRuleDialog? dialog = null;
            table.DialogOpened = d => dialog = d;
            table.List.SelectedIndex = 0;
            var adding = table.EditAsync(-1);
            await Until(() => dialog is { IsVisible: true });
            dialog!.Accept();
            Assert.Equal("Give the rule a name.", dialog.ErrorText.Text);
            dialog.NameBox.Text = "Photos";
            dialog.MasksBox.Text = "*.jpg;*.heic";
            dialog.PickFor(RuleTarget.Files);
            dialog.PickStyle(NameStyle.Bold);
            dialog.Attributes[0].Rule = Core.Search.AttributeRule.MustNot;
            Dispatcher.UIThread.RunJobs();
            var height = dialog.Bounds.Height;
            Assert.False(dialog.AnyOfBox.IsEnabled);
            dialog.Attributes[1].Rule = Core.Search.AttributeRule.Must;
            dialog.Attributes[2].Rule = Core.Search.AttributeRule.Must;
            Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.AnyOfBox.IsEnabled);
            Assert.Equal(height, dialog.Bounds.Height);
            dialog.Attributes[1].Rule = Core.Search.AttributeRule.Either;
            dialog.Attributes[2].Rule = Core.Search.AttributeRule.Either;
            dialog.PickOwn(new(0x12, 0x34, 0x56), new(0xAB, 0xCD, 0xEF));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(height, dialog.Bounds.Height);
            Assert.Equal(Color.Parse("#123456"), ((ISolidColorBrush)dialog.SampleLight.Foreground!).Color);
            Assert.Equal("name.jpg", dialog.SampleLight.Text);
            Assert.Equal(FontWeight.SemiBold, dialog.SampleLight.FontWeight);
            Capture(dialog, "highlight-rule-own.png");
            dialog.PickColour(NameColour.Teal);
            Capture(dialog, "highlight-rule.png");
            dialog.Accept();
            await adding;
            var added = Settings.Current.Highlighting.Rules![1];
            Assert.Equal(("Photos", "*.jpg;*.heic", NameColour.Teal, NameStyle.Bold, FileAttributes.ReadOnly), (added.Name, added.Masks, added.Colour, added.Style, added.MustNotHave));
            Assert.Equal(7, table.List.ItemCount);

            table.List.SelectedIndex = 1;
            table.Delete();
            Assert.Equal(6, Settings.Current.Highlighting.Rules!.Count);
            w.Close();
        }
        finally
        {
            Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
        }
    }
}
