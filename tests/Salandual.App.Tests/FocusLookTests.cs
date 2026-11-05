// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/FocusLookTests.cs

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.App.Panels;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Tests;

public sealed class FocusLookTests : IDisposable
{
    private static readonly CommandRegistry _commands = CommandRegistry.CreateDefault();
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-focus-").FullName;

    public FocusLookTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Balaton"));
        foreach (var name in new[] { "budget 2027.xlsx", "notes.txt", "IMG_0412.jpg" })
        {
            File.WriteAllBytes(Path.Combine(_dir, name), [1, 2, 3]);
        }
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<(Window Window, FilePanelView View, FilePanel Panel)> Open(PanelView view = PanelView.Detailed)
    {
        var panel = new FilePanel(new LocalFileSystemProvider(), new EntrySorter(CultureInfo.GetCultureInfo("en-US"), true), a => Dispatcher.UIThread.Post(a));
        var list = new FilePanelView
        {
            Panel = panel,
            Commands = _commands,
            Format = new DisplayFormat(SizeUnits.Windows, PanelSettings.DateFormatIso, CultureInfo.GetCultureInfo("en-US"), TimeFormat.Hours24),
        };
        var window = new Window { Width = 900, Height = 360, Content = list };
        window.Closed += (_, _) => panel.Dispose();
        window.Show();
        panel.SetView(view);
        Assert.True(await panel.NavigateAsync(_dir));
        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == "budget 2027.xlsx"));
        Dispatcher.UIThread.RunJobs();
        return (window, list, panel);
    }

    [AvaloniaFact]
    public async Task Detailed_WholeRowAtFirst_NameOnlyReachesTheExtension()
    {
        var (w, view, _) = await Open();
        Assert.Equal(MarkLook.WholeRow, new PanelSettings().FocusLook);
        Assert.Equal(MarkLook.WholeRow, new PanelSettings().SelectionLook);
        Assert.True(new PanelSettings().FocusBackground);
        Assert.True(view.FocusMarkRect().Width > 800);

        view.FocusLook = MarkLook.NameOnly;
        var mark = view.FocusMarkRect();
        var ext = view.ShownColumns()[0];
        Assert.Equal(PanelColumn.Extension, ext.Column);
        Assert.True(mark.Right > ext.X + 10 && mark.Right < ext.X + ext.Width + 6, $"{mark} {ext}");
        Assert.True(mark.Right < view.ShownColumns()[1].X);

        view.ViewColumns = ColumnViews.With([], ColumnViews.Toggle(ColumnViews.For([], PanelView.Detailed), PanelColumn.Extension));
        Dispatcher.UIThread.RunJobs();
        var whole = view.FocusMarkRect();
        Assert.Equal(PanelColumn.Size, view.ShownColumns()[0].Column);
        Assert.True(whole.Width is > 60 and < 250, whole.ToString());
        w.Close();
    }

    [AvaloniaFact]
    public async Task TheFocusBackground_ReachesApartFromTheOutline()
    {
        var (w, view, _) = await Open();
        Assert.Equal(MarkLook.WholeRow, new PanelSettings().FocusBackgroundLook);
        view.FocusLook = MarkLook.NameOnly;
        Assert.True(view.FocusMarkRect(background: true).Width > 800);
        Assert.True(view.FocusMarkRect().Width < view.FocusMarkRect(background: true).Width - 100);
        (view.FocusLook, view.FocusBackgroundLook) = (MarkLook.WholeRow, MarkLook.NameOnly);
        Assert.True(view.FocusMarkRect().Width > 800);
        Assert.True(view.FocusMarkRect(background: true).Width < view.FocusMarkRect().Width - 100);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Cells_NameOnly_MarkTheNameUnderThePicture()
    {
        var (w, view, _) = await Open(PanelView.Thumbnails);
        var cell = view.FocusMarkRect();
        view.FocusLook = MarkLook.NameOnly;
        var name = view.FocusMarkRect();
        Assert.True(name.Height < cell.Height / 3 && name.Bottom <= cell.Bottom + 0.5 && name.Width <= cell.Width + 0.5, $"{name} in {cell}");
        w.Close();
    }

    [AvaloniaFact]
    public async Task ADateColumnTooNarrowForTheTime_ShowsTheDateOnly()
    {
        var (w, view, panel) = await Open();
        var row = panel.Items.ToList().FindIndex(e => e.Name == "notes.txt");
        Assert.Contains(":", view.ColumnText(row, PanelColumn.Modified));
        view.ViewColumns = ColumnViews.With([], ColumnViews.SetWidth(ColumnViews.For([], PanelView.Detailed), PanelColumn.Modified, 80));
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(":", view.ColumnText(row, PanelColumn.Modified));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Capture_TheLooks()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        var (w, view, panel) = await Open();
        w.Width = 560;
        panel.Toggle(panel.Items.ToList().FindIndex(e => e.Name == "notes.txt"));
        panel.Toggle(panel.Items.ToList().FindIndex(e => e.Name == "Balaton"));
        foreach (var (focus, selection) in new[] { (MarkLook.WholeRow, MarkLook.WholeRow), (MarkLook.WholeRow, MarkLook.NameOnly), (MarkLook.NameOnly, MarkLook.NameOnly) })
        {
            (view.FocusLook, view.SelectionLook) = (focus, selection);
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, $"focus-{focus}-{selection}.png"), PngBitmapEncoderOptions.Default);
        }

        view.IsActivePanel = false;
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "focus-inactive.png"), PngBitmapEncoderOptions.Default);
        view.IsActivePanel = true;
        view.ViewColumns = ColumnViews.With([], ColumnViews.SetWidth(ColumnViews.For([], PanelView.Detailed), PanelColumn.Modified, 80));
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "focus-narrow-date.png"), PngBitmapEncoderOptions.Default);
        panel.SetView(PanelView.Thumbnails);
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "focus-thumbnails.png"), PngBitmapEncoderOptions.Default);
        panel.SetView(PanelView.Brief);
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "focus-brief.png"), PngBitmapEncoderOptions.Default);
        w.Close();
    }

    [AvaloniaFact]
    public void Options_Panels_FocusAndSelection()
    {
        var settings = ((App)Avalonia.Application.Current!).Services.Settings;
        try
        {
            var w = new OptionsWindow { Width = 980, Height = 1000 };
            w.Show();
            var hit = w.Find("name only");
            Assert.Contains(hit, h => h.Title == "The focused item");
            Assert.Contains(hit, h => h.Title == "Selected items");
            Assert.Contains(w.Find("cursor"), h => h.Title == "Focus background");
            w.ShowCategory("panels", hit.First(h => h.Title == "The focused item"));
            Dispatcher.UIThread.RunJobs();
            var names = w.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Where(b => b.Content as string == "Name only").ToList();
            Assert.Equal(3, names.Count);
            names[0].IsChecked = true;
            names[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MarkLook.NameOnly, settings.Current.Panels.FocusLook);
            Assert.Equal(MarkLook.WholeRow, settings.Current.Panels.FocusBackgroundLook);
            if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
            {
                for (var k = 0; k < 20; k++)
                {
                    Dispatcher.UIThread.RunJobs();
                }

                w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "focus-options.png"), PngBitmapEncoderOptions.Default);
            }

            var off = w.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single(b => b.Content as string == "Off" && b.Parent == names[2].Parent);
            off.IsChecked = true;
            off.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(settings.Current.Panels.FocusBackground);
            w.Close();
        }
        finally
        {
            settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
        }
    }
}
