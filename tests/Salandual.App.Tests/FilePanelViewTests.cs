// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/FilePanelViewTests.cs

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

public sealed class FilePanelViewTests : IDisposable
{
    private static readonly CommandRegistry _commands = CommandRegistry.CreateDefault();
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-view-").FullName;

    public FilePanelViewTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Balaton"));
        Directory.CreateDirectory(Path.Combine(_dir, "Tihany – drón"));
        foreach (var (name, size) in new[] { ("IMG_0412.HEIC", 3_400_000), ("IMG_0413.HEIC", 3_100_000), ("IMG_0412.JPG", 900_000), ("árak.json", 2_000), ("jegyzetek.md", 3_000), ("日本語メモ.txt", 1_000), ("emoji 😀.md", 500) })
        {
            File.WriteAllBytes(Path.Combine(_dir, name), new byte[size]);
        }

        File.SetLastWriteTime(Path.Combine(_dir, "IMG_0412.HEIC"), new DateTime(2026, 8, 14, 18, 22, 0));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<(Window Window, FilePanelView View, FilePanel Panel)> Open(double width = 720, string dateFormat = PanelSettings.DateFormatIso, SizeUnits units = SizeUnits.Windows)
    {
        var panel = new FilePanel(new LocalFileSystemProvider(), new EntrySorter(CultureInfo.GetCultureInfo("en-US"), true), a => Dispatcher.UIThread.Post(a));
        var view = new FilePanelView
        {
            Panel = panel,
            Commands = _commands,
            Format = new DisplayFormat(units, dateFormat, CultureInfo.GetCultureInfo("en-US")),
        };
        var window = new Window { Width = width, Height = 360, Content = view };
        window.Closed += (_, _) => panel.Dispose();
        window.Show();
        Assert.True(await panel.NavigateAsync(_dir));
        view.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, view, panel);
    }

    private static void Press(Window w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Arrows_Insert_ShiftArrows_UseSalamanderKeys()
    {
        var (w, _, panel) = await Open();
        Assert.Equal("..", panel.FocusedEntry?.Name);

        Press(w, Key.Down);
        Assert.Equal("Balaton", panel.FocusedEntry?.Name);

        Press(w, Key.Insert);
        Assert.True(panel.IsSelected(1));
        Assert.Equal(2, panel.Focus);

        Press(w, Key.Down, RawInputModifiers.Shift);
        Press(w, Key.Down, RawInputModifiers.Shift);
        Assert.Equal(3, panel.GetSelectionSummary().Count);

        Press(w, Key.D, RawInputModifiers.Control);
        Assert.Equal(0, panel.GetSelectionSummary().Count);

        Press(w, Key.End);
        Assert.Equal(panel.Count - 1, panel.Focus);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Typing_StartsQuickSearch_EscapeEnds()
    {
        var (w, _, panel) = await Open();
        w.KeyTextInput("jeg");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("jeg", panel.QuickSearchText);
        Assert.Equal("jegyzetek.md", panel.FocusedEntry?.Name);

        Press(w, Key.Escape);
        Assert.Null(panel.QuickSearchText);
        w.Close();
    }

    [AvaloniaFact]
    public async Task CtrlF6_SortsBySize_EnterOpensFolder_BackspaceReturns()
    {
        var (w, _, panel) = await Open();
        Press(w, Key.F6, RawInputModifiers.Control);
        Assert.Equal(SortColumn.Size, panel.Sort.Column);
        Assert.Equal("emoji 😀.md", panel.Items[3].Name);

        panel.SetFocus(1);
        Press(w, Key.Enter);
        await WaitFor(() => panel.Path.EndsWith("Balaton", StringComparison.Ordinal));

        Press(w, Key.Back);
        await WaitFor(() => panel.Path == _dir);
        Assert.Equal("Balaton", panel.FocusedEntry?.Name);
        w.Close();
    }

    [AvaloniaFact]
    public async Task UnhandledCommand_GoesToTheWindow()
    {
        var (w, view, _) = await Open();
        string? id = null;
        view.UnhandledCommand += (_, c) => id = c.Id;
        Press(w, Key.F5);
        Assert.Equal("File.Copy", id);
        w.Close();
    }

    [AvaloniaFact]
    public async Task ALongExtension_GetsItsRoom_AndNeverRunsIntoSize()
    {
        File.WriteAllBytes(Path.Combine(_dir, "settings.json.broken-20261019-180304"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_dir, "notes.txt.broken-20261019-180304-and-much-longer-still-than-that"), new byte[100]);
        foreach (var width in new[] { 900.0, 560, 420 })
        {
            var (w, view, panel) = await Open(width);
            foreach (var name in new[] { "settings.json.broken-20261019-180304", "notes.txt.broken-20261019-180304-and-much-longer-still-than-that" })
            {
                var row = view.RowLayout(panel.Items.ToList().FindIndex(e => e.Name == name));
                Assert.True(row.ExtRight <= row.SizeLeft - 4, $"{width}: {name}'s extension ends at {row.ExtRight}, Size starts at {row.SizeLeft}");
                Assert.True(row.NameRight <= row.SizeLeft - row.ExtWidth, $"{width}: {name}");
            }

            if (width == 900)
            {
                Assert.True(view.RowLayout(0).ExtWidth > 120);
            }

            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task ExtensionColumnOff_TheWholeNameInOneColumn()
    {
        var (w, view, panel) = await Open(900);
        var row = panel.Items.ToList().FindIndex(e => e.Name == "jegyzetek.md");
        var withExt = view.RowLayout(row);
        Assert.InRange(withExt.ExtWidth, 30, 60);


        view.ExtensionColumn = false;
        var together = view.RowLayout(row);
        Assert.Equal(0, together.ExtWidth);
        Assert.True(together.NameRight > withExt.NameRight);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Renders_InEveryWidth_AndFormat()

    {
        foreach (var (width, format, units) in new[] { (720.0, "iso", SizeUnits.Windows), (420.0, "en-US", SizeUnits.Binary), (260.0, "hu-HU", SizeUnits.Bytes) })
        {
            var (w, _, panel) = await Open(width, format, units);
            panel.SetFocus(4);
            panel.Toggle(4);
            var frame = w.CaptureRenderedFrame();
            Assert.NotNull(frame);
            SaveForReview(frame!, $"panel-{width}-{format}-{units}.png");
            w.Close();
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static void SaveForReview(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
}
