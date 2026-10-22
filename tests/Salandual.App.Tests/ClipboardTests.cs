// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ClipboardTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.Core.Commands;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class ClipboardTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-clip-").FullName;
    private readonly string _left;
    private readonly string _right;
    private readonly FakeClipboard _clip = new();

    public ClipboardTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Left")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Right")).FullName;
        Directory.CreateDirectory(Path.Join(_right, "Inbox"));
        File.WriteAllText(Path.Join(_left, "a.txt"), "a");
        File.WriteAllText(Path.Join(_left, "b.txt"), "b");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.FileClipboardAccess = _clip;
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_left);
        await w.RightPanel.Panel.NavigateAsync(_right);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(TopLevel w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition, int tries = 500)
    {
        for (var i = 0; i < tries && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static void Focus(Panels.PanelHost host, string name)
    {
        host.Panel.SetFocus(host.Panel.Items.ToList().FindIndex(e => e.Name == name));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task CtrlC_PutsTheSelectionOnTheClipboard_CtrlVInTheOtherPanelCopies()
    {
        var w = await Open();
        w.LeftPanel.Panel.Toggle(w.LeftPanel.Panel.Items.ToList().FindIndex(e => e.Name == "a.txt"));
        w.LeftPanel.Panel.Toggle(w.LeftPanel.Panel.Items.ToList().FindIndex(e => e.Name == "b.txt"));
        Press(w, Key.C, RawInputModifiers.Control);
        Assert.Equal([Path.Join(_left, "a.txt"), Path.Join(_left, "b.txt")], _clip.Files);
        Assert.False(_clip.Cut);
        Assert.Empty(w.CutPaths);

        Press(w, Key.Tab);
        Press(w, Key.V, RawInputModifiers.Control);
        await Until(() => File.Exists(Path.Join(_right, "a.txt")) && File.Exists(Path.Join(_right, "b.txt")));
        Assert.True(File.Exists(Path.Join(_left, "a.txt")));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlX_DimsTheItems_PasteMovesThem_AndEmptiesTheClipboard()
    {
        var w = await Open();
        Focus(w.LeftPanel, "a.txt");
        Press(w, Key.X, RawInputModifiers.Control);
        Assert.True(_clip.Cut);
        Assert.Equal([Path.Join(_left, "a.txt")], w.CutPaths);

        w.RightPanel.View.Focus();
        Press(w, Key.Insert, RawInputModifiers.Shift);
        await Until(() => File.Exists(Path.Join(_right, "a.txt")) && !File.Exists(Path.Join(_left, "a.txt")));
        await Until(() => w.CutPaths.Count == 0);
        Assert.Empty(_clip.Files);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task SomethingElseOnTheClipboard_EndsTheCut()
    {
        var w = await Open();
        Focus(w.LeftPanel, "a.txt");
        Press(w, Key.X, RawInputModifiers.Control);
        Assert.Single(w.CutPaths);
        _clip.SetText("copied in another program");
        w.CheckCutStillOnClipboard();
        Assert.Empty(w.CutPaths);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task PasteIntoTheSameFolder_MakesName2()
    {
        var w = await Open();
        Focus(w.LeftPanel, "a.txt");
        Press(w, Key.C, RawInputModifiers.Control);
        Press(w, Key.V, RawInputModifiers.Control);
        await Until(() => File.Exists(Path.Join(_left, "a (2).txt")));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task PasteIntoTheFocusedFolder()
    {
        var w = await Open();
        _clip.SetFiles([Path.Join(_left, "b.txt")], cut: false);
        Focus(w.RightPanel, "Inbox");
        await w.PasteAsync(w.RightPanel, intoFocused: true);
        await Until(() => File.Exists(Path.Join(_right, @"Inbox\b.txt")) && w.Operations.Jobs.All(j => j.IsFinished));

        for (var i = 0; i < 50 && w.NoticeMessage != "Focus a folder to paste into it."; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Focus(w.RightPanel, "..");
            await w.PasteAsync(w.RightPanel, intoFocused: true);
        }

        Assert.Equal("Focus a folder to paste into it.", w.NoticeMessage);
        Assert.False(File.Exists(Path.Join(_right, "b.txt")));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task PastingAPath_GoesThere_AFilePathFocusesTheFile()
    {
        var w = await Open();
        _clip.SetText($"\"{Path.Join(_right, "Inbox")}\"");
        Press(w, Key.V, RawInputModifiers.Control);
        await Until(() => w.LeftPanel.Panel.Path == Path.Join(_right, "Inbox"));

        _clip.SetText(Path.Join(_left, "b.txt"));
        Press(w, Key.V, RawInputModifiers.Control);
        await Until(() => w.LeftPanel.Panel.Path == _left && w.LeftPanel.Panel.FocusedEntry?.Name == "b.txt");

        _clip.SetText(Path.Join(_left, "gone"));
        await w.PasteAsync(w.LeftPanel, intoFocused: false);
        Assert.Equal($"{Path.Join(_left, "gone")} isn't there.", w.NoticeMessage);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task PastingText_SavesPastedTextTxt_WithTheNameReadyToEdit_UndoRemovesIt()
    {
        var w = await Open();
        _clip.SetText("Hello\nworld");
        await w.PasteAsync(w.LeftPanel, intoFocused: false);
        var file = Path.Join(_left, "Pasted text.txt");
        Assert.Equal("Hello\nworld", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        Assert.Equal("Pasted text.txt", w.LeftPanel.Panel.FocusedEntry?.Name);
        Assert.True(w.LeftPanel.IsRenaming);
        Assert.Equal((0, 11), (w.LeftPanel.Rename!.Box.SelectionStart, w.LeftPanel.Rename.Box.SelectionEnd));
        Assert.Equal("Saved the clipboard as Pasted text.txt.", w.NoticeMessage);

        Press(w, Key.Escape);
        await w.PasteAsync(w.LeftPanel, intoFocused: false);
        Assert.True(File.Exists(Path.Join(_left, "Pasted text (2).txt")));

        w.NoticeButtons.Single(b => (string?)b.Content == "Undo").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Until(() => !File.Exists(Path.Join(_left, "Pasted text (2).txt")));
        Assert.True(File.Exists(file));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task PastingAnImage_SavesPastedImagePng()
    {
        var w = await Open();
        var bitmap = new WriteableBitmap(new Avalonia.PixelSize(4, 3), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using var png = new MemoryStream();
        bitmap.Save(png, PngBitmapEncoderOptions.Default);
        _clip.Content = new ClipboardContent { Image = png.ToArray(), ImageIsPng = true };
        await w.PasteAsync(w.LeftPanel, intoFocused: false);
        var saved = Path.Join(_left, "Pasted image.png");
        Assert.True(File.Exists(saved));
        using var decoded = new Bitmap(saved);
        Assert.Equal(4, decoded.PixelSize.Width);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AltInsFamily_CopiesNamesAndPaths()
    {
        var w = await Open();
        Focus(w.LeftPanel, "a.txt");
        Press(w, Key.Insert, RawInputModifiers.Alt);
        await Until(() => _clip.Text == Path.Join(_left, "a.txt"));
        Press(w, Key.Insert, RawInputModifiers.Alt | RawInputModifiers.Shift);
        await Until(() => _clip.Text == "a.txt");
        Press(w, Key.Insert, RawInputModifiers.Control | RawInputModifiers.Alt);
        await Until(() => _clip.Text == _left);
        Press(w, Key.Insert, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Until(() => _clip.Text == Path.Join(_left, "a.txt"));

        w.LeftPanel.Panel.Toggle(w.LeftPanel.Panel.Items.ToList().FindIndex(e => e.Name == "a.txt"));
        w.LeftPanel.Panel.Toggle(w.LeftPanel.Panel.Items.ToList().FindIndex(e => e.Name == "b.txt"));
        w.Run("Clipboard.CopyName", PanelTarget.Active);
        await Until(() => _clip.Text == "a.txt" + Environment.NewLine + "b.txt");
        Assert.Equal("Copied 2 names.", w.NoticeMessage);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task EmptyClipboard_SaysSo_AndTheToolbarButtonsWork()
    {
        var w = await Open();
        _clip.Content = new ClipboardContent();
        await w.PasteAsync(w.LeftPanel, intoFocused: false);
        Assert.Equal("Nothing to paste: the clipboard has no files, text or image.", w.NoticeMessage);

        Focus(w.LeftPanel, "b.txt");
        var copy = Controls.PanelSplitter.Buttons.ToList().FindIndex(b => b.CommandId == "Clipboard.Copy");
        w.Splitter.Click(copy);
        Assert.Equal([Path.Join(_left, "b.txt")], _clip.Files);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Capture_CutAndPastedText()
    {
        var w = await Open();
        var app = (App)Avalonia.Application.Current!;
        foreach (var dark in new[] { false, true })
        {
            app.Theme.Apply(Core.Settings.AppearanceSettings.Fixed(dark ? Core.Settings.ThemeCatalog.SalandualDarkId : Core.Settings.ThemeCatalog.SalandualLightId));
            Focus(w.LeftPanel, "a.txt");
            Press(w, Key.X, RawInputModifiers.Control);
            _clip.SetText("Bevásárlólista:\ntej, kenyér");
            await w.PasteAsync(w.RightPanel, intoFocused: false);
            Save(w, $"m34-cut-pasted-{(dark ? "dark" : "light")}.png");
            Press(w, Key.Escape);
            File.Delete(Path.Join(_right, "Pasted text.txt"));
            await w.RightPanel.Panel.RefreshAsync();
        }

        app.Theme.Apply(new Core.Settings.AppearanceSettings());
        w.CloseNow();
    }

    private static void Save(TopLevel w, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    [Fact]
    public void ClipboardBitmap_BecomesABmpFile()
    {
        var dib = new byte[44];
        BitConverter.TryWriteBytes(dib.AsSpan(0), 40);
        BitConverter.TryWriteBytes(dib.AsSpan(4), 1);
        BitConverter.TryWriteBytes(dib.AsSpan(8), 1);
        BitConverter.TryWriteBytes(dib.AsSpan(12), (short)1);
        BitConverter.TryWriteBytes(dib.AsSpan(14), (short)32);
        var bmp = FileClipboard.BmpFromDib(dib)!;
        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal(58, BitConverter.ToInt32(bmp, 2));
        Assert.Equal(54, BitConverter.ToInt32(bmp, 10));
    }

    private sealed class FakeClipboard : IClipboardAccess
    {
        public ClipboardContent Content { get; set; } = new();

        public IReadOnlyList<string> Files => Content.Files;

        public bool Cut => Content.Cut;

        public string? Text => Content.Text;

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut)
        {
            Content = new ClipboardContent { Files = [.. paths], Cut = cut };
            SequenceNumber++;
            return true;
        }

        public bool SetText(string text)
        {
            Content = new ClipboardContent { Text = text };
            SequenceNumber++;
            return true;
        }

        public void Clear()
        {
            Content = new ClipboardContent();
            SequenceNumber++;
        }

        public ClipboardContent? Read() => Content;
    }
}
