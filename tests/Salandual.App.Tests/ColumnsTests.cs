// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ColumnsTests.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.App.Panels;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Tests;

public sealed class ColumnsTests : IDisposable
{
    private const string LongName = "a very long name for the quarterly report that never seems to end at all";
    private static readonly CommandRegistry _commands = CommandRegistry.CreateDefault();
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-columns-").FullName;

    public ColumnsTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Balaton"));
        File.WriteAllBytes(Path.Combine(_dir, "Balaton", "one.txt"), [1]);
        File.WriteAllBytes(Path.Combine(_dir, "Balaton", "two.txt"), [2]);
        Directory.CreateDirectory(Path.Combine(_dir, "Empty"));
        foreach (var (name, size) in new[] { ("árak.json", 2_000), ("jegyzetek.md", 3_000), (LongName + ".docx", 48_000), ("budget 2027.xlsx", 1_258_291) })
        {
            File.WriteAllBytes(Path.Combine(_dir, name), new byte[size]);
        }
    }

    private static App CurrentApp => (App)Application.Current!;

    public void Dispose()
    {
        CurrentApp.Services.Settings.Update(_ => new AppSettings());
        Dispatcher.UIThread.RunJobs();
        Directory.Delete(_dir, recursive: true);
    }

    private async Task<(Window Window, FilePanelView View, FilePanel Panel)> Open(double width = 900, string dateFormat = PanelSettings.DateFormatIso, PanelView view = PanelView.Detailed)
    {
        var panel = new FilePanel(new LocalFileSystemProvider(), new EntrySorter(CultureInfo.GetCultureInfo("en-US"), true), a => Dispatcher.UIThread.Post(a));
        var list = new FilePanelView
        {
            Panel = panel,
            Commands = _commands,
            Format = new DisplayFormat(SizeUnits.Windows, dateFormat, CultureInfo.GetCultureInfo("en-US"), TimeFormat.Hours24),
        };
        var window = new Window { Width = width, Height = 360, Content = list };
        window.Closed += (_, _) => panel.Dispose();
        window.Show();
        panel.SetView(view);
        Assert.True(await panel.NavigateAsync(_dir));
        list.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, list, panel);
    }

    private static int Row(FilePanel panel, string name) => panel.Items.ToList().FindIndex(e => e.Name == name);

    private static IReadOnlyList<ColumnView> With(PanelView view, Func<ColumnView, ColumnView> change) => ColumnViews.With([], change(ColumnViews.For([], view)));

    [AvaloniaFact]
    public async Task Detailed_HasItsColumns_AndANarrowPanelDropsThemFromTheRight()
    {
        var (w, view, _) = await Open(900);
        Assert.Equal([PanelColumn.Extension, PanelColumn.Size, PanelColumn.Modified, PanelColumn.Attributes], view.ShownColumns().Select(c => c.Column));
        var columns = view.ShownColumns();
        for (var i = 1; i < columns.Count; i++)
        {
            Assert.True(columns[i].X >= columns[i - 1].X + columns[i - 1].Width, "in order, apart");
        }

        Assert.True(columns[^1].X + columns[^1].Width <= 900);
        w.Width = 330;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(PanelColumn.Size, view.ShownColumns().Select(c => c.Column));
        Assert.DoesNotContain(PanelColumn.Attributes, view.ShownColumns().Select(c => c.Column));
        Assert.Equal(0, view.NameAndScroll().ScrollMax);
        w.Close();
    }

    [AvaloniaFact]
    public async Task ChosenColumns_ShowTheirValues_ItemsAreCountedInTheBackground()
    {
        var (w, view, panel) = await Open(1200);
        view.ViewColumns = With(PanelView.Detailed, v => ColumnViews.Toggle(ColumnViews.Toggle(ColumnViews.Toggle(ColumnViews.Toggle(v, PanelColumn.Items), PanelColumn.Type), PanelColumn.Created), PanelColumn.DosName));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(PanelColumn.Items, view.ShownColumns().Select(c => c.Column));
        Assert.False(string.IsNullOrEmpty(view.ColumnText(Row(panel, "árak.json"), PanelColumn.Type)));
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d$", view.ColumnText(Row(panel, "árak.json"), PanelColumn.Created));
        Assert.Equal("", view.ColumnText(Row(panel, "árak.json"), PanelColumn.Items));

        string? items = null;
        for (var i = 0; i < 200 && items is not ("2" or "-1"); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
            items = view.ColumnText(Row(panel, "Balaton"), PanelColumn.Items);
        }

        Assert.Equal("2", items);
        Assert.Equal("0", view.ColumnText(Row(panel, "Empty"), PanelColumn.Items));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Types_ShowsTheTypeBesideTheWholeName()
    {
        var (w, view, panel) = await Open(900, view: PanelView.Types);
        Assert.Equal([PanelColumn.Type, PanelColumn.Size, PanelColumn.Modified], view.ShownColumns().Select(c => c.Column));
        Assert.Equal("árak.json", view.RowName(Row(panel, "árak.json")));
        Assert.False(string.IsNullOrEmpty(view.ColumnText(Row(panel, "Balaton"), PanelColumn.Type)));
        w.Close();
    }

    [AvaloniaFact]
    public async Task WithoutSmartNameWidth_NameKeepsItsWidth_AndThePanelScrollsSideways()
    {
        var (w, view, _) = await Open(600);
        view.ViewColumns = With(PanelView.Detailed, v => ColumnViews.SetWidth(ColumnViews.Toggle(v, PanelColumn.Created), PanelColumn.Name, 400));
        Dispatcher.UIThread.RunJobs();
        var (nameRight, max) = view.NameAndScroll();
        Assert.True(max > 0);
        Assert.InRange(nameRight - 400, 0, 60);
        var before = view.ShownColumns()[0].X;
        w.MouseWheel(new Point(300, 100), new Vector(0, -1), RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.ShownColumns()[0].X < before, "Shift+wheel scrolls the columns sideways");
        Assert.Equal(nameRight, view.NameAndScroll().NameRight);
        w.Close();
    }

    [AvaloniaFact]
    public async Task HeaderEdges_SetWidths_ADoubleClickFits_ARightClickAsksForTheChooser()
    {
        var (w, view, _) = await Open(900);
        var changes = new List<ColumnWidthChange>();
        var asked = 0;
        view.ColumnWidthChanged += (_, c) => changes.Add(c);
        view.HeaderMenuRequested += (_, _) => asked++;
        var (nameRight, _) = view.NameAndScroll();
        var ext = view.ShownColumns()[0];
        var edge = (nameRight + ext.X) / 2;
        w.MouseDown(new Point(edge, 10), MouseButton.Left);
        w.MouseMove(new Point(edge - 30, 10));
        w.MouseUp(new Point(edge - 30, 10), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(changes);
        Assert.Equal(PanelColumn.Extension, changes[0].Column);
        Assert.InRange(changes[0].Width, (int)ext.Width + 28, (int)ext.Width + 32);

        w.MouseDown(new Point(edge, 10), MouseButton.Left);
        w.MouseUp(new Point(edge, 10), MouseButton.Left);
        w.MouseDown(new Point(edge, 10), MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        w.MouseUp(new Point(edge, 10), MouseButton.Left);
        Assert.Contains(changes, c => c.Width == 0);

        w.MouseDown(new Point(300, 10), MouseButton.Right);
        w.MouseUp(new Point(300, 10), MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, asked);
        w.Close();
    }

    [AvaloniaFact]
    public async Task RelativeDates_FolderSizeText_AndNamesCutInTheMiddle()
    {
        var (w, view, panel) = await Open(900, PanelSettings.DateFormatRelative);
        var json = view.ColumnText(Row(panel, "árak.json"), PanelColumn.Modified);
        Assert.True(json is "just now" or "1 minute ago" or "2 minutes ago", json);

        Assert.Equal("Folder", view.ColumnText(Row(panel, "Balaton"), PanelColumn.Size));
        view.FolderSize = FolderSizeText.Dir;
        Assert.Equal("<DIR>", view.ColumnText(Row(panel, "Balaton"), PanelColumn.Size));
        view.FolderSize = FolderSizeText.Blank;
        Assert.Equal("", view.ColumnText(Row(panel, "Balaton"), PanelColumn.Size));

        w.Width = 420;
        Dispatcher.UIThread.RunJobs();
        var row = Row(panel, LongName + ".docx");
        Assert.Equal(LongName, view.RowName(row));
        view.LongNames = LongNames.CutMiddle;
        view.ViewColumns = With(PanelView.Detailed, v => ColumnViews.Toggle(v, PanelColumn.Extension));
        Dispatcher.UIThread.RunJobs();
        var cut = view.RowName(row);
        Assert.Contains("…", cut, StringComparison.Ordinal);
        Assert.EndsWith(".docx", cut, StringComparison.Ordinal);
        Assert.StartsWith("a very", cut, StringComparison.Ordinal);
        w.Close();
    }

    [AvaloniaFact]
    public async Task MainWindow_ColumnChooser_CtrlN_Types_AndYourOwnViews()
    {
        var w = new MainWindow { Width = 1200, Height = 700 };
        w.Show();
        for (var i = 0; i < 300 && (w.LeftPanel.Panel.Path.Length == 0 || w.RightPanel.Panel.Path.Length == 0); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        var settings = w.Services.Settings;
        w.LeftPanel.Panel.SetView(PanelView.Detailed);
        w.RightPanel.Panel.SetView(PanelView.Detailed);

        var menu = w.ShowColumnMenu(w.LeftPanel, new Point(200, 10));
        Dispatcher.UIThread.RunJobs();
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(ColumnViews.AllColumns.Count + 2, items.Count);
        Assert.False(items[0].IsEnabled);
        var count = items.First(m => m.Header as string == "Items in a folder");
        Assert.False(count.IsChecked);
        count.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(count.IsChecked);
        Assert.Contains(PanelColumn.Items, w.LeftPanel.View.ShownColumns().Select(c => c.Column));
        Assert.Contains(PanelColumn.Items, w.RightPanel.View.ShownColumns().Select(c => c.Column));
        menu.Close();

        var width = w.LeftPanel.View.NameWidthNow;
        Assert.True(w.Run("Panel.SmartNameWidth", PanelTarget.Left));
        var detailed = ColumnViews.For(settings.Current.Panels.ColumnViews, PanelView.Detailed);
        Assert.False(detailed.SmartName);
        Assert.Equal(width, detailed.NameWidth);
        Assert.True(w.Run("Panel.SmartNameWidth", PanelTarget.Left));
        Assert.True(ColumnViews.For(settings.Current.Panels.ColumnViews, PanelView.Detailed).SmartName);

        Assert.True(w.Run("View.Mode7", PanelTarget.Left));
        Assert.Equal(PanelView.Types, w.LeftPanel.Panel.View);
        Assert.True(w.Run("View.Mode8", PanelTarget.Left));
        Assert.Equal(PanelView.Types, w.LeftPanel.Panel.View);
        settings.Update(s => s with { Panels = s.Panels with { ColumnViews = ColumnViews.With(s.Panels.ColumnViews, ColumnViews.For([], PanelView.Own1) with { Name = "Photos" }) } });
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.Run("View.Mode8", PanelTarget.Left));
        Assert.Equal(PanelView.Own1, w.LeftPanel.Panel.View);
        var viewMenu = w.ShowViewMenu(w.LeftPanel);
        Dispatcher.UIThread.RunJobs();
        var own = viewMenu.Items.OfType<MenuItem>().ToList();
        Assert.Contains(own, m => m.Header as string == "Photos" && m.IsVisible && m.IsChecked);
        Assert.DoesNotContain(own, m => (m.Header as string)?.Replace("_", "") == "Your view 2" && m.IsVisible);
        Assert.Contains(own, m => (m.Header as string)?.Replace("_", "") == "Columns" && m.Items.Count == ColumnViews.AllColumns.Count + 3);
        viewMenu.Close();

        settings.Update(s => s with { Panels = s.Panels with { ColumnViews = ColumnViews.Without(s.Panels.ColumnViews, PanelView.Own1) } });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PanelView.Detailed, w.LeftPanel.Panel.View);
        w.Close();
    }

    [AvaloniaFact]
    public void Options_Views_MakesYourOwnView_AndChoosesItsColumns()
    {
        var settings = CurrentApp.Services.Settings;
        var w = new OptionsWindow();
        w.Show();
        w.ShowCategory("views");
        Dispatcher.UIThread.RunJobs();
        var editor = w.Page.GetLogicalDescendants().OfType<ViewColumnsEditor>().Single();
        editor.Select(PanelView.Own2);
        Dispatcher.UIThread.RunJobs();
        var name = editor.GetLogicalDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Name it to make it");
        name.Text = "Photos";
        name.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = name });
        Dispatcher.UIThread.RunJobs();
        Assert.True(ColumnViews.IsMade(settings.Current.Panels.ColumnViews, PanelView.Own2));

        var created = editor.GetLogicalDescendants().OfType<CheckBox>().First(c => Avalonia.Automation.AutomationProperties.GetName(c) == "Created");
        created.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(ColumnViews.For(settings.Current.Panels.ColumnViews, PanelView.Own2).Columns, c => c.Column == PanelColumn.Created && c.Shown);
        Assert.NotEmpty(w.Find("column chooser"));
        CaptureOptions(w, "options-columns.png");
        w.Close();
    }

    [AvaloniaFact]
    public void Options_Panels_SizeStyle_FolderSize_Stripes_AndSearch()
    {
        var settings = CurrentApp.Services.Settings;
        var w = new OptionsWindow();
        w.Show();
        w.ShowCategory("panels");
        Dispatcher.UIThread.RunJobs();
        Click(w.Page.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(b => (b.Content as string)?.StartsWith("Kilobytes", StringComparison.Ordinal) == true));
        Assert.Equal(SizeStyle.Kilobytes, settings.Current.Panels.SizeStyle);
        Click(w.Page.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(b => b.Content as string == "<DIR>"));
        Assert.Equal(FolderSizeText.Dir, settings.Current.Panels.FolderSize);
        Click(w.Page.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(b => b.Content as string == "Cut in the middle"));
        Assert.Equal(LongNames.CutMiddle, settings.Current.Panels.LongNames);
        foreach (var word in new[] { "stripes", "relative", "bytes", "kib", "hidden", "dir" })
        {
            Assert.NotEmpty(w.Find(word));
        }

        Assert.Contains(w.Find("stripes"), i => i.Title == "Row stripes");
        CaptureOptions(w, "options-sizes.png");
        w.Close();

        static void Click(Avalonia.Controls.Primitives.ToggleButton b)
        {
            b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task Capture_TheColumns()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        var (w, view, _) = await Open(760);
        view.ViewColumns = With(PanelView.Detailed, v => ColumnViews.Toggle(ColumnViews.Toggle(v, PanelColumn.Items), PanelColumn.Attributes));
        view.RowStripes = true;
        for (var i = 0; i < 50; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "columns-detailed.png"), PngBitmapEncoderOptions.Default);
        view.Panel!.SetView(PanelView.Types);
        view.RowStripes = false;
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "columns-types.png"), PngBitmapEncoderOptions.Default);
        view.Panel.SetView(PanelView.Detailed);
        view.ViewColumns = With(PanelView.Detailed, v => ColumnViews.SetWidth(ColumnViews.Toggle(v, PanelColumn.Created), PanelColumn.Name, 360));
        view.Format = new DisplayFormat(SizeUnits.Windows, PanelSettings.DateFormatRelative, CultureInfo.GetCultureInfo("en-US"), TimeFormat.Hours24, SizeStyle.Kilobytes);
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "columns-scroll.png"), PngBitmapEncoderOptions.Default);
        w.Close();
    }

    private static void CaptureOptions(Window w, string file)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            w.Width = 980;
            w.Height = 1000;
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, file), PngBitmapEncoderOptions.Default);
        }
    }
}
