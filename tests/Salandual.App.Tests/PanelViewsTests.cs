// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/PanelViewsTests.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Tests;

public sealed class PanelViewsTests : IDisposable
{
    private static readonly CommandRegistry _commands = CommandRegistry.CreateDefault();
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-views-").FullName;

    public PanelViewsTests()
    {
        foreach (var folder in new[] { "Edited", "Raw", "Video" })
        {
            Directory.CreateDirectory(Path.Combine(_dir, folder));
        }

        for (var i = 1; i <= 60; i++)
        {
            File.WriteAllBytes(Path.Combine(_dir, string.Create(CultureInfo.InvariantCulture, $"day{i:00} photo.txt")), new byte[i * 100]);
        }
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<(Window Window, FilePanelView View, FilePanel Panel)> Open(PanelView view, double width = 720, double height = 360)
    {
        var panel = new FilePanel(new LocalFileSystemProvider(), new EntrySorter(CultureInfo.GetCultureInfo("en-US"), true), a => Dispatcher.UIThread.Post(a));
        panel.SetView(view);
        var control = new FilePanelView
        {
            Panel = panel,
            Commands = _commands,
            Format = new DisplayFormat(SizeUnits.Windows, PanelSettings.DateFormatIso, CultureInfo.GetCultureInfo("en-US")),
        };
        var window = new Window { Width = width, Height = height, Content = control };
        window.Closed += (_, _) => panel.Dispose();
        window.Show();
        Assert.True(await panel.NavigateAsync(_dir));
        control.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, control, panel);
    }

    private static void Press(Window w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task AltDigits_ChangeTheView_AndOnlyDetailedHasTheHeader()
    {
        var (w, view, panel) = await Open(PanelView.Detailed);
        Assert.Equal(1, view.GridShape().PerLine);
        foreach (var (key, expected) in new[] { (Key.D2, PanelView.Brief), (Key.D4, PanelView.Icons), (Key.D5, PanelView.Thumbnails), (Key.D6, PanelView.Tiles), (Key.D3, PanelView.Detailed) })
        {
            Press(w, key, RawInputModifiers.Alt);
            Assert.Equal(expected, panel.View);
            Assert.Equal(expected, view.ViewKind);
        }

        Assert.True(view.NameBounds(0)!.Value.Y >= 20);
        Press(w, Key.D4, RawInputModifiers.Alt);
        Assert.True(view.CellBounds(0)!.Value.Y < 10);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Icons_ArrowsMoveInTheGrid()
    {
        var (w, view, panel) = await Open(PanelView.Icons);
        var (perRow, _, down) = view.GridShape();
        Assert.False(down);
        Assert.True(perRow > 3, $"{perRow} icons in a row");
        Assert.Equal(0, panel.Focus);

        Press(w, Key.Right);
        Assert.Equal(1, panel.Focus);
        Press(w, Key.Down);
        Assert.Equal(1 + perRow, panel.Focus);
        Press(w, Key.Left);
        Assert.Equal(perRow, panel.Focus);
        Press(w, Key.Up);
        Assert.Equal(0, panel.Focus);
        Press(w, Key.Up);
        Assert.Equal(0, panel.Focus);

        Press(w, Key.Right);
        Press(w, Key.Down, RawInputModifiers.Shift);
        Assert.Equal(1 + perRow, panel.Focus);
        Assert.Equal(perRow, panel.GetSelectionSummary().Count);

        Press(w, Key.End);
        Assert.Equal(panel.Count - 1, panel.Focus);
        Press(w, Key.Down);
        Assert.Equal(panel.Count - 1, panel.Focus);
        Assert.NotNull(view.CellBounds(panel.Focus));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Icons_DownFromAShortLastRow_GoesToTheLastItem()
    {
        var (w, view, panel) = await Open(PanelView.Icons);
        var perRow = view.GridShape().PerLine;
        var lastRowStart = (panel.Count - 1) / perRow * perRow;
        Assert.True(lastRowStart > 0);
        var above = lastRowStart - 1;
        if (above + perRow <= panel.Count - 1)
        {
            return;
        }

        panel.SetFocus(above);
        Press(w, Key.Down);
        Assert.Equal(panel.Count - 1, panel.Focus);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Brief_GoesDownColumns_AndScrollsSideways()
    {
        var (w, view, panel) = await Open(PanelView.Brief, width: 520, height: 300);
        var (perColumn, columns, down) = view.GridShape();
        Assert.True(down);
        Assert.True(perColumn >= 5 && columns >= 1, $"{perColumn} names in a column, {columns} columns");

        Press(w, Key.Down);
        Assert.Equal(1, panel.Focus);
        var first = view.CellBounds(0)!.Value;
        var second = view.CellBounds(1)!.Value;
        Assert.Equal(first.X, second.X);
        Assert.True(second.Y > first.Y);

        Press(w, Key.Right);
        Assert.Equal(1 + perColumn, panel.Focus);
        Assert.True(view.CellBounds(panel.Focus)!.Value.X > first.X);

        Press(w, Key.End);
        Assert.Equal(panel.Count - 1, panel.Focus);
        Assert.True(view.Top > 0);
        Assert.Equal(0, view.Top % perColumn);
        Assert.NotNull(view.CellBounds(panel.Focus));

        Press(w, Key.Home);
        Assert.Equal(0, view.Top);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Brief_ColumnsAreAsWideAsTheLongestName()
    {
        File.WriteAllBytes(Path.Combine(_dir, "a rather long name for a holiday picture.txt"), new byte[10]);
        var (w, view, panel) = await Open(PanelView.Brief, width: 900);
        var wide = view.CellBounds(0)!.Value.Width;
        w.Close();

        File.Delete(Path.Combine(_dir, "a rather long name for a holiday picture.txt"));
        (w, view, panel) = await Open(PanelView.Brief, width: 900);
        Assert.True(view.CellBounds(0)!.Value.Width < wide);
        w.Close();
    }

    [AvaloniaFact]
    public async Task AClick_FocusesTheCellUnderIt()
    {
        foreach (var kind in new[] { PanelView.Brief, PanelView.Icons, PanelView.Thumbnails, PanelView.Tiles })
        {
            var (w, view, panel) = await Open(kind);
            var cell = view.CellBounds(3)!.Value;
            Assert.Equal(3, view.RowAt(cell.Center));
            w.MouseDown(cell.Center, MouseButton.Left);
            w.MouseUp(cell.Center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, panel.Focus);
            Assert.NotNull(view.NameBounds(3));
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task AltWheel_GoesThroughTheViews_CtrlWheel_AsksForOtherThumbnails()
    {
        var (w, view, panel) = await Open(PanelView.Detailed);
        var center = new Point(200, 150);
        w.MouseWheel(center, new Vector(0, -1), RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PanelView.Icons, panel.View);
        w.MouseWheel(center, new Vector(0, 1), RawInputModifiers.Alt);
        w.MouseWheel(center, new Vector(0, 1), RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PanelView.Brief, panel.View);
        w.MouseWheel(center, new Vector(0, 1), RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PanelView.Columns, panel.View);
        w.MouseWheel(center, new Vector(0, 1), RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PanelView.Types, panel.View);

        view.AltWheelViews = false;
        w.MouseWheel(center, new Vector(0, -1), RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PanelView.Types, panel.View);

        panel.SetView(PanelView.Thumbnails);
        var steps = new List<int>();
        view.ThumbnailSizeStep += (_, s) => steps.Add(s);
        w.MouseWheel(center, new Vector(0, 1), RawInputModifiers.Control);
        w.MouseWheel(center, new Vector(0, -1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([1, -1], steps);

        var before = view.GridShape().PerLine;
        view.ThumbnailSize = PanelSettings.MaxThumbnailSize;
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GridShape().PerLine < before);
        w.Close();
    }

    [AvaloniaFact]
    public async Task TheWheel_ScrollsALineOfCells()
    {
        var (w, view, _) = await Open(PanelView.Icons, height: 220);
        var perRow = view.GridShape().PerLine;
        w.MouseWheel(new Point(200, 100), new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(perRow, view.Top);
        w.Close();
    }

    [AvaloniaFact]
    public async Task TheViewGoesWithThePanel()
    {
        var (w, view, panel) = await Open(PanelView.Tiles);
        var other = new FilePanel(new LocalFileSystemProvider(), new EntrySorter(CultureInfo.GetCultureInfo("en-US"), true), a => Dispatcher.UIThread.Post(a));
        view.Panel = other;
        Assert.Equal(PanelView.Detailed, view.ViewKind);
        view.Panel = panel;
        Assert.Equal(PanelView.Tiles, view.ViewKind);
        other.Dispose();
        w.Close();
    }

    [AvaloniaFact]
    public async Task MainWindow_ViewMenu_ViewButton_AndThumbnailSize()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        for (var i = 0; i < 300 && (w.LeftPanel.Panel.Path.Length == 0 || w.RightPanel.Panel.Path.Length == 0); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(w.Run("View.Mode5", PanelTarget.Left));
        Assert.Equal(PanelView.Thumbnails, w.LeftPanel.Panel.View);
        Assert.Equal(PanelView.Detailed, w.RightPanel.Panel.View);
        Assert.Equal(PanelView.Thumbnails, w.LeftPanel.DirectoryLine.View);
        var items = w.MenuItems.Where(m => m.Node.CommandId == "View.Mode5").ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, m => Assert.Equal(MenuItemToggleType.Radio, m.Item.ToggleType));
        Assert.True(MainWindow.IsBuilt("View.Mode7"));

        Assert.False(w.RightPanel.DirectoryLine.ViewButtonBounds.Width == 0);
        var menu = w.ShowViewMenu(w.RightPanel);
        Dispatcher.UIThread.RunJobs();
        var tiles = menu.Items.OfType<MenuItem>().First(m => (m.Header as string)?.Replace("_", "") == "Tiles");
        tiles.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        menu.Close();
        Assert.Equal(PanelView.Tiles, w.RightPanel.Panel.View);

        w.SwapPanels();
        Assert.Equal(PanelView.Tiles, w.LeftPanel.Panel.View);
        Assert.Equal(PanelView.Thumbnails, w.RightPanel.Panel.View);
        w.SwapPanels();

        var size = w.Services.Settings.Current.Panels.ThumbnailSize;
        Assert.True(w.Run("View.ThumbnailsBigger", PanelTarget.Left));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(size + 16, w.Services.Settings.Current.Panels.ThumbnailSize);
        Assert.True(w.Run("View.ThumbnailsSmaller", PanelTarget.Left));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(size, w.Services.Settings.Current.Panels.ThumbnailSize);

        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        var over = w.LeftPanel.View.TranslatePoint(new Point(100, 100), w)!.Value;
        w.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        w.MouseWheel(over, new Vector(0, -1), RawInputModifiers.Alt);
        w.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PanelView.Tiles, w.LeftPanel.Panel.View);
        Assert.Same(w.LeftPanel.View, w.FocusManager?.GetFocusedElement());

        w.Services.Settings.Update(s => s with { Panels = s.Panels with { ViewButton = false } });
        await Task.Delay(20);
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.LeftPanel.DirectoryLine.ShowViewButton);
        w.Services.Settings.Update(s => s with { Panels = s.Panels with { ViewButton = true } });
        w.Close();
    }

    [AvaloniaFact]
    public async Task Capture_TheViews()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        (Application.Current as App)!.Thumbnails.Start();
        foreach (var (name, w0, h0, hue) in new[] { ("day01 lake.png", 640, 400, 0), ("day01 tall.png", 300, 600, 1), ("day02 sunset.png", 800, 450, 2) })
        {
            using var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(w0, h0), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
            using (var fb = bitmap.Lock())
            {
                var row = new byte[w0 * 4];
                for (var y = 0; y < h0; y++)
                {
                    for (var x = 0; x < w0; x++)
                    {
                        row[(x * 4) + hue] = (byte)(255 * x / w0);
                        row[(x * 4) + ((hue + 1) % 3)] = (byte)(255 * y / h0);
                        row[(x * 4) + ((hue + 2) % 3)] = 90;
                        row[(x * 4) + 3] = 255;
                    }

                    System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + (y * fb.RowBytes), row.Length);
                }
            }

            bitmap.Save(Path.Combine(_dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        foreach (var kind in new[] { PanelView.Brief, PanelView.Icons, PanelView.Thumbnails, PanelView.Tiles })
        {
            var (w, _, panel) = await Open(kind, width: 640, height: 420);
            panel.SetFocus(5);
            panel.Toggle(7);
            Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs();
            }

            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "view-" + kind.ToString().ToLowerInvariant() + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            w.Close();
        }
    }
}
