// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ColumnsViewUiTests.cs

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.Core.Commands;
using Salandual.Core.Panels;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class ColumnsViewUiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-columns-").FullName;
    private readonly NoShellAccess _shell = new();

    public ColumnsViewUiTests()
    {
        foreach (var file in new[] { @"Work\todo.txt", @"Work\Projects\Clients\offer.pdf", @"Work\Projects\Website\index.html", @"Work\Projects\Website\docs\deploy.md", @"Work\Projects\Website\docs\faq.md" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Join(_dir, file))!);
            File.WriteAllText(Path.Join(_dir, file), "x");
        }

        Directory.CreateDirectory(Path.Join(_dir, @"Work\Projects\Website\assets"));
        File.WriteAllText(Path.Join(_dir, @"Work\Projects\Website\docs\notes.txt"), "Notes for the website\n=====================\n- move the hero picture\n");
    }

    private string Website => Path.Join(_dir, @"Work\Projects\Website");

    private string Docs => Path.Join(Website, "docs");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open(string path)
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.ShellAccess = _shell;
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(path);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
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

    private static void Focus(FilePanel panel, string name) => panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == name));

    private static string Focused(FilePanel panel) => panel.FocusedEntry?.Name ?? "";

    [AvaloniaFact]
    public async Task AltOne_TheFoldersAbove_ArrowsGoInAndBack_AClickGoesThere_AndTheOtherViewsAsBefore()
    {
        var w = await Open(Website);
        try
        {
            var host = w.ActivePanel;
            var panel = host.Panel;
            var view = host.View;
            Press(w, Key.D1, RawInputModifiers.Alt);
            Assert.Equal(PanelView.Columns, panel.View);
            Assert.Equal(PanelView.Columns, view.ViewKind);
            Assert.DoesNotContain(panel.Items, e => e.Name == "..");
            var shown = view.ColumnsShown();
            Assert.Equal(Path.Join(_dir, @"Work\Projects"), shown.Above[^1]);
            Assert.Contains(Path.Join(_dir, "Work"), shown.Above);

            Focus(panel, "docs");
            await Until(() => view.ColumnsShown() is { NextRead: true } s && s.Next == Docs);
            Assert.Equal(["deploy.md", "faq.md", "notes.txt"], view.ColumnItems(Docs));
            Capture(w, "columns-panel.png");

            Press(w, Key.Right);
            await Until(() => panel.Path == Docs);
            Assert.Equal("deploy.md", Focused(panel));
            Assert.Equal(Website, view.ColumnsShown().Above[^1]);
            var strip = view.StripShape();
            Assert.True(strip.Offset > 0 && strip.Max > 0);
            Assert.InRange(strip.Width, 150, 600);

            Focus(panel, "notes.txt");
            await Until(() => view.PreviewLines is { Count: > 0 });
            Assert.Equal("Notes for the website", view.PreviewLines![0]);
            Capture(w, "columns-file.png");

            view.TryRunCommand("Panel.ScrollLeft");
            Assert.True(view.StripShape().Offset < strip.Offset);
            Assert.Equal("notes.txt", Focused(panel));

            Press(w, Key.Left);
            await Until(() => panel.Path == Website && Focused(panel) == "docs");

            strip = view.StripShape();
            var column = view.ColumnsShown().Above.Count - 1;
            var at = view.TranslatePoint(new Point((column * strip.Width) - strip.Offset + 60, view.RowHeight / 2), w)!.Value;
            w.MouseDown(at, MouseButton.Left);
            w.MouseUp(at, MouseButton.Left);
            await Until(() => panel.Path == Path.Join(_dir, @"Work\Projects") && Focused(panel) == "Clients");

            Press(w, Key.D3, RawInputModifiers.Alt);
            Assert.Equal("..", panel.Items[0].Name);
            Assert.Equal("Clients", Focused(panel));

            Press(w, Key.D1, RawInputModifiers.Alt);
            Assert.True(w.Run("Panel.FlatView", PanelTarget.Active));
            await Until(() => panel.Results?.Flat is { Reading: false });
            Assert.Equal(PanelView.Detailed, view.ViewKind);
            Assert.Equal(PanelView.Columns, panel.View);
        }
        finally
        {
            w.Services.Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task ShiftSpace_PeeksIntoAFolder_TypingAndEnterGoIn_EscCloses_AndTheWidthSetting()
    {
        var w = await Open(Website);
        try
        {
            var host = w.ActivePanel;
            var panel = host.Panel;
            var view = host.View;
            Focus(panel, "docs");
            Press(w, Key.Space, RawInputModifiers.Shift);
            await Until(() => view.PeekShown is { Items: not null });
            Assert.Equal(Docs, view.PeekShown!.Value.Path);
            Assert.Equal(["deploy.md", "faq.md", "notes.txt"], view.PeekShown.Value.Items);
            Assert.Equal(Website, panel.Path);
            Capture(w, "columns-peek.png");

            Press(w, Key.Down);
            Assert.Equal(1, view.PeekShown!.Value.Focus);
            Press(w, Key.N);
            w.KeyTextInput("n");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, view.PeekShown!.Value.Focus);
            Press(w, Key.Enter);
            await Until(() => panel.Path == Docs && Focused(panel) == "notes.txt");
            Assert.Null(view.PeekShown);

            await panel.GoParentAsync();
            Dispatcher.UIThread.RunJobs();
            Focus(panel, "docs");
            Assert.True(w.Run("Panel.Peek", PanelTarget.Active));
            Assert.NotNull(view.PeekShown);
            Press(w, Key.Escape);
            Assert.Null(view.PeekShown);
            Focus(panel, "index.html");
            w.Run("Panel.Peek", PanelTarget.Active);
            Assert.Null(view.PeekShown);

            w.Services.Settings.Update(s => s with { Panels = s.Panels with { PeekOnHover = true } });
            Dispatcher.UIThread.RunJobs();
            var assets = panel.Items.ToList().FindIndex(e => e.Name == "assets");
            var r = view.NameBounds(assets)!.Value;
            w.MouseMove(view.TranslatePoint(new Point(r.X + 20, r.Y + (r.Height / 2)), w)!.Value);
            view.ElapsePeekHover();
            Assert.Equal(Path.Join(Website, "assets"), view.PeekShown?.Path);
            var index = view.NameBounds(panel.Items.ToList().FindIndex(e => e.Name == "index.html"))!.Value;
            w.MouseMove(view.TranslatePoint(new Point(index.X + 20, index.Y + (index.Height / 2)), w)!.Value);
            Assert.Null(view.PeekShown);

            Press(w, Key.D1, RawInputModifiers.Alt);
            var wide = view.StripShape().Width;
            w.Services.Settings.Update(s => s with { Panels = s.Panels with { ColumnsWidth = 16 } });
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.StripShape().Width < wide);
        }
        finally
        {
            w.Services.Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
            w.CloseNow();
        }
    }

    [Fact]
    public void TheLastColumnsText_OnlyForText()
    {
        Assert.Equal(["one", "two    tab"], FilePanelView.TextLines(Encoding.UTF8.GetBytes("one\r\ntwo\ttab"), 10)!);
        Assert.Equal(["Unicode"], FilePanelView.TextLines([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("Unicode")], 10)!);
        Assert.Null(FilePanelView.TextLines([0x4D, 0x5A, 0x90, 0x00, 0x03], 10));
        Assert.Null(FilePanelView.TextLines([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], 10));
        Assert.Equal(2, FilePanelView.TextLines(Encoding.UTF8.GetBytes("a\nb\nc"), 2)!.Length);
    }
}
