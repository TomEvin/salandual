// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/OccupiedSpaceTests.cs

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.Core.Listing;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class OccupiedSpaceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-space-").FullName;

    public OccupiedSpaceTests()
    {
        Write("README.md", 7);
        Write("notes.txt", 5000);
        Write(Path.Join("assets", "logo.svg"), 9000);
        Write(Path.Join("assets", "img", "photo.jpg"), 20000);
        Write(Path.Join("docs", "guide.md"), 3000);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string name, int bytes)
    {
        var path = Path.Join(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private async Task<MainWindow> Open(params string[] select)
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        w.FileClipboardAccess = new TextClipboard();
        await w.LeftPanel.Panel.NavigateAsync(_dir);
        w.LeftPanel.Panel.SelectWhere(e => select.Contains(e.Name), select: true);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
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

    private static async Task<OccupiedSpaceDialog> Dialog(MainWindow w, Key key = Key.F10, RawInputModifiers modifiers = RawInputModifiers.Alt)
    {
        w.KeyPress(key, modifiers, PhysicalKey.None, null);
        await Until(() => w.SpaceDialog is not null);
        var d = w.SpaceDialog!;
        await Until(() => !d.IsCounting);
        return d;
    }

    private static string Value(OccupiedSpaceDialog d, string label) => d.Numbers.First(n => n.Label == label).Value;

    [AvaloniaFact]
    public async Task AltF10_CountsTheSelection_WithWhatIsInsideTheFolders()
    {
        var w = await Open("README.md", "notes.txt", "assets", "docs");
        var d = await Dialog(w);
        Assert.Equal("Occupied space", d.Title);
        Assert.Equal("5", Value(d, "Files"));
        Assert.Equal("3", Value(d, "Folders"));
        Assert.DoesNotContain(d.Numbers, n => n.Label is "NTFS compression" or "Online only (cloud)");

        Assert.Equal(["assets", "notes.txt", "docs", "README.md"], d.InsideRows.Select(r => r.Name));
        Assert.Equal("78 %", d.InsideRows[0].Share);
        Assert.Equal(37007, d.Result!.Totals.Bytes);

        if (d.Volume is { } v)
        {
            Assert.Equal(["This selection", "Other files on " + v.Name, "Free"], d.OutsideRows.Select(r => r.Name));
        }

        d.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        await Until(() => w.SpaceDialog is null);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlQ_IsTheSameCommand_OneFolderShowsWhatIsInIt()
    {
        var w = await Open();
        w.LeftPanel.Panel.SetFocus(w.LeftPanel.Panel.Items.ToList().FindIndex(e => e.Name == "assets"));
        var d = await Dialog(w, Key.Q, RawInputModifiers.Control);
        Assert.Equal(["img", "logo.svg"], d.InsideRows.Select(r => r.Name));
        Assert.Equal("2", Value(d, "Folders"));
        d.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AnotherClusterSize_SaysWhatItWouldTake()
    {
        var w = await Open("README.md", "notes.txt");
        var d = await Dialog(w);
        Assert.True(d.ClusterBox.SelectedIndex >= 0);
        d.ClusterBox.SelectedIndex = OccupiedSpace.IndexOf(131072);
        Assert.Equal("it would take 256 KB", d.Estimate);
        d.ClusterBox.SelectedIndex = OccupiedSpace.IndexOf(512);
        Assert.Equal("it would take 6 KB", d.Estimate);
        d.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Copy_TheNumbersAndTheListsAsText()
    {
        var w = await Open("README.md", "assets");
        var d = await Dialog(w);
        Assert.True(d.Copy());
        var text = ((TextClipboard)w.FileClipboardAccess).Text!;
        Assert.Contains("Files: 3", text, StringComparison.Ordinal);
        Assert.Contains("Inside: the selection", text, StringComparison.Ordinal);
        Assert.Contains("assets: 29 KB, 100 %", text, StringComparison.Ordinal);
        Assert.Equal("Copied", d.CopyButton.Content);
        d.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheChart_ASliceSaysItsNumbersUnderTheMouse()
    {
        var w = await Open("README.md", "notes.txt", "assets", "docs");
        var d = await Dialog(w);
        Assert.True(d.Chart.IsVisible);
        d.Chart.HoverAt(new Point(115 + 10, 112 - 60));
        Assert.StartsWith("assets: 29 KB, 78 % of the selection", d.Chart.HoverTip, StringComparison.Ordinal);
        d.Chart.HoverAt(new Point(115, 112));
        Assert.Null(d.Chart.HoverTip);
        d.Close();
        w.CloseNow();
    }

    [Fact]
    public void TheDisksRing_ASmallSelectionStillShows()
    {
        var angles = SpaceChart.Angles([new(1, default, ""), new(500, default, ""), new(499, default, "")], SpaceChart.OuterMinDegrees);
        Assert.Equal(4, (angles[0].To - angles[0].From) * 180 / Math.PI, 6);
        Assert.Equal(2 * Math.PI, angles[^1].To, 6);
    }

    [AvaloniaFact]
    public async Task Capture_TheDialog()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            Assert.Skip("Only with SALANDUAL_CAPTURE_DIR.");
            return;
        }

        var w = await Open("README.md", "notes.txt", "assets", "docs");
        var d = await Dialog(w);
        d.Chart.HoverAt(new Point(115 + 10, 112 - 60));
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        d.CaptureRenderedFrame()?.Save(Path.Join(dir, "space-1.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        d.Close();
        w.CloseNow();
    }

    private sealed class TextClipboard : IClipboardAccess
    {
        public string? Text { get; private set; }

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut) => false;

        public bool SetText(string text)
        {
            Text = text;
            SequenceNumber++;
            return true;
        }

        public void Clear() => Text = null;

        public ClipboardContent? Read() => new() { Text = Text };
    }
}
