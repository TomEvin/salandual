// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ImageViewerTests.cs

using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Resources;
using Salandual.App.Viewer;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class ImageViewerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-image-").FullName;

    public ImageViewerTests()
    {
        Bmp("a-big.bmp", 2400, 1600, (x, y) => x < 200 && y < 200 ? ((byte)255, (byte)0, (byte)0) : ((byte)(x * 255 / 2400), (byte)(y * 255 / 1600), (byte)128));
        File.WriteAllText(Path.Join(_dir, "b-notes.txt"), "not a picture\r\n");
        Bmp("c-small.bmp", 300, 200, (_, _) => ((byte)0, (byte)160, (byte)0));
        File.WriteAllText(Path.Join(_dir, "d-broken.png"), "this is no PNG");
    }

    public void Dispose()
    {
        for (var i = 0; ; i++)
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
                return;
            }
            catch (IOException) when (i < 50)
            {
                Thread.Sleep(20);
            }
        }
    }

    private void Bmp(string name, int width, int height, Func<int, int, (byte R, byte G, byte B)> color)
    {
        var stride = ((width * 3) + 3) & ~3;
        var data = new byte[54 + (stride * height)];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), 24);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = color(x, y);
                var at = 54 + ((height - 1 - y) * stride) + (x * 3);
                (data[at], data[at + 1], data[at + 2]) = (b, g, r);
            }
        }

        File.WriteAllBytes(Path.Join(_dir, name), data);
    }

    private async Task<MainWindow> Open(string focus)
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir, focus);
        await w.RightPanel.Panel.NavigateAsync(_dir);
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

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 1000 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static async Task<ViewerWindow> Viewer(MainWindow w)
    {
        Press(w, Key.F3);
        await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].ImageView.Image is not null);
        var v = w.Viewers[^1];
        v.Width = 1100;
        v.Height = 700;
        v.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return v;
    }

    private static string StatusText(ViewerWindow v) => string.Join(" | ", v.StatusCells.Select(c => (c.Child as TextBlock)?.Text));

    [AvaloniaFact]
    public async Task F3_OnAnImage_ShowsItFitted_WithItsSizeFormatAndKeys()
    {
        var w = await Open("a-big.bmp");
        var v = await Viewer(w);

        Assert.True(v.IsImage);
        Assert.Null(v.Document);
        Assert.Equal(ZoomMode.Fit, v.ImageView.Mode);
        Assert.True(v.ImageView.Zoom < 1);
        Assert.Equal(new PixelSize(2400, 1600), v.ImageView.Size);
        Assert.True(v.ImageView.Image!.IsReduced);
        Assert.Contains("2400 × 1600", StatusText(v), StringComparison.Ordinal);
        Assert.Contains("BMP", StatusText(v), StringComparison.Ordinal);
        Assert.StartsWith(AppStrings.Viewer_Fit, v.ZoomBadgeText, StringComparison.Ordinal);
        Assert.Equal("1 / 3 " + AppStrings.Viewer_Kind_Images, v.Counter.Text);

        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            v.CaptureRenderedFrame()!.Save(Path.Combine(dir, "viewer-image.png"), PngBitmapEncoderOptions.Default);
        }

        Press(v, Key.F7);
        Assert.Equal(ZoomMode.Actual, v.ImageView.Mode);
        Assert.Equal(1, v.ImageView.Zoom);
        await Until(() => v.ImageView.Detail is not null);
        Assert.Equal(2400, v.ImageView.Detail!.Width);
        Assert.Equal("100 %", v.ZoomBadgeText);

        Press(v, Key.F6);
        Assert.Equal(ZoomMode.Width, v.ImageView.Mode);
        Press(v, Key.F5);
        Assert.Equal(ZoomMode.Fit, v.ImageView.Mode);
        var fit = v.ImageView.Zoom;
        Press(v, Key.Add);
        Assert.Equal(ZoomMode.Free, v.ImageView.Mode);
        Assert.True(v.ImageView.Zoom > fit);

        Press(v, Key.R);
        Assert.Equal(1, v.ImageView.Turns);
        Assert.Equal(new PixelSize(1600, 2400), v.ImageView.Size);
        Press(v, Key.L);
        Press(v, Key.L);
        Assert.Equal(3, v.ImageView.Turns);

        Press(v, Key.H);
        Assert.True(v.ImageView.FlippedHorizontally);
        Press(v, Key.R);
        Assert.Equal(2, v.ImageView.Turns);
        Press(v, Key.V);
        Assert.True(v.ImageView.FlippedVertically);
        Press(v, Key.H);
        Press(v, Key.V);
        Assert.False(v.ImageView.FlippedHorizontally || v.ImageView.FlippedVertically);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheZoomBox_TakesAValueFromItsList_OrOneYouType()
    {
        var w = await Open("a-big.bmp");
        var v = await Viewer(w);
        var box = v.ImageZoomBox;
        Assert.Equal(Salandual.App.Viewer.ViewerWindow.Percent(v.ImageView.Zoom), box.Box.Text);
        Assert.Equal(v.ActualButton.Bounds.Height, box.Bounds.Height, 1);
        Assert.Equal(Avalonia.VisualExtensions.TranslatePoint(v.ActualButton, new Avalonia.Point(0, v.ActualButton.Bounds.Height / 2), v.ToolbarLeft)!.Value.Y, Avalonia.VisualExtensions.TranslatePoint(box, new Avalonia.Point(0, box.Bounds.Height / 2), v.ToolbarLeft)!.Value.Y, 1);
        Assert.Equal(Avalonia.Media.TextAlignment.Center, box.Box.TextAlignment);
        Assert.Equal("1:1", ((StackPanel)v.ActualButton.Content!).Children.OfType<TextBlock>().First().Text);

        box.Box.Focus();
        box.Box.Text = "400";
        Press(v, Key.Enter);
        Assert.Equal(4, v.ImageView.Zoom, 3);
        Assert.Equal(ZoomMode.Free, v.ImageView.Mode);
        Assert.Equal("400 %", box.Box.Text);
        Assert.Contains(v, w.Viewers);

        box.Box.Focus();
        box.Box.Text = "nonsense";
        Press(v, Key.Escape);
        Assert.Equal("400 %", box.Box.Text);
        Assert.Contains(v, w.Viewers);

        Assert.Equal([5, 10, 25, 50, 75, 100, 125, 150, 200, 400, 600, 800, 1600, 3200, 6400], Salandual.App.Controls.ZoomBox.Presets);
        Assert.Equal(
            Salandual.App.Controls.ZoomBox.Presets.Select(Salandual.App.Controls.ZoomBox.Format),
            Salandual.App.Controls.ZoomBox.PresetItems(400, _ => { }).Select(i => (string)i.Header!));
        var pick = Salandual.App.Controls.ZoomBox.PresetItems(400, _ => { }).ToList();
        Assert.True(pick[9].IsChecked);
        v.Close();
        w.CloseNow();
    }

    [Fact]
    public void TheZoomBox_ReadsWhatYouType()
    {
        Assert.Equal(150, Salandual.App.Controls.ZoomBox.Parse("150"));
        Assert.Equal(150, Salandual.App.Controls.ZoomBox.Parse(" 150 % "));
        Assert.Equal(12.5, Salandual.App.Controls.ZoomBox.Parse("12.5%"));
        Assert.Null(Salandual.App.Controls.ZoomBox.Parse("0"));
        Assert.Null(Salandual.App.Controls.ZoomBox.Parse("abc"));
    }

    [AvaloniaFact]
    public async Task Space_StepsThroughTheImagesOnly_AndHexShowsTheBytes()
    {
        var w = await Open("a-big.bmp");
        var v = await Viewer(w);

        Press(v, Key.Space);
        await Until(() => v.FilePath.EndsWith("c-small.bmp", StringComparison.Ordinal) && v.ImageView.Image is { Width: 300 });
        Assert.Equal("2 / 3 " + AppStrings.Viewer_Kind_Images, v.Counter.Text);
        Assert.Equal(1, v.ImageView.Zoom);
        Assert.False(v.ImageView.Image!.IsReduced);

        Press(v, Key.F4);
        await Until(() => v.Document is not null);
        Assert.False(v.IsImage);
        Assert.True(v.IsHex);
        Press(v, Key.F5);
        Assert.True(v.IsImage);
        Assert.NotNull(v.ImageView.Image);

        Press(v, Key.Back);
        await Until(() => v.FilePath.EndsWith("a-big.bmp", StringComparison.Ordinal) && v.ImageView.Image is not null);
        Assert.True(v.IsImage);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AFileThatIsNoImage_SaysSo_AndF4StillShowsItsBytes()
    {
        var w = await Open("d-broken.png");
        Press(w, Key.F3);
        await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].IsImage && w.Viewers[^1].Loading.IsCompleted);
        var v = w.Viewers[^1];
        await v.Loading;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(v.ImageView.Image);

        Press(v, Key.F4);
        await Until(() => v.Document is not null && v.IsHex);
        Assert.Equal(14, v.Document!.Length);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlC_CopiesTheWholeImageAsTurned_AsABitmapAndPng()
    {
        var w = await Open("c-small.bmp");
        var clipboard = new ImageClipboard();
        w.FileClipboardAccess = clipboard;
        var v = await Viewer(w);

        Press(v, Key.R);
        Press(v, Key.C, RawInputModifiers.Control);
        await Until(() => clipboard.Dib is not null);

        Assert.Equal(200, BinaryPrimitives.ReadInt32LittleEndian(clipboard.Dib.AsSpan(4)));
        Assert.Equal(300, BinaryPrimitives.ReadInt32LittleEndian(clipboard.Dib.AsSpan(8)));
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], clipboard.Png![..4]);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheCanvasColourAndZoomWhenOpening_AreSettings_InTheMoreMenuToo()
    {
        var w = await Open("a-big.bmp");
        var settings = w.Services.Settings;
        var before = settings.Current.Viewers;
        try
        {
            var v = await Viewer(w);
            Assert.Equal(Avalonia.Media.Color.FromRgb(0x1C, 0x1C, 0x1C), ((Avalonia.Media.ISolidColorBrush)v.ImageView.Background!).Color);

            var menu = v.BuildMoreMenu();
            Assert.True(v.OnToolbar(v.CopyImageButton));
            Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), m => (string?)m.Header == AppStrings.Viewer_CopyImage);
            Assert.Contains(menu.Items.OfType<MenuItem>(), m => (string?)m.Header == AppStrings.Viewer_FlipHorizontally);
            var canvas = menu.Items.OfType<MenuItem>().First(m => (string?)m.Header == AppStrings.Viewer_CanvasColor);
            canvas.Items.OfType<MenuItem>().First(m => (string?)m.Header == AppStrings.Viewer_Canvas_Black).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => settings.Current.Viewers.ImageCanvas == CanvasColor.Black);
            await Until(() => ((Avalonia.Media.ISolidColorBrush)v.ImageView.Background!).Color == Avalonia.Media.Colors.Black);

            settings.Update(s => s with { Viewers = s.Viewers with { ImageCanvas = CanvasColor.Magenta } });
            await Until(() => ((Avalonia.Media.ISolidColorBrush)v.ImageView.Background!).Color == Avalonia.Media.Color.FromRgb(0xFF, 0x00, 0xFF));

            settings.Update(s => s with { Viewers = s.Viewers with { ImageZoom = ImageZoom.Actual } });
            Press(v, Key.R, RawInputModifiers.Control);
            await Until(() => v.ImageView.Mode == ZoomMode.Actual && v.ImageView.Image is { IsReduced: false });
            v.Close();
        }
        finally
        {
            settings.Update(s => s with { Viewers = before });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public void Capture_ContactSheetOfTheSamples()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Join(root, "Salandual.slnx")))
        {
            root = Path.GetDirectoryName(root.TrimEnd('\\'));
        }

        var samples = root is null ? null : Path.Join(root, "_samples", "images");
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir || samples is null || !Directory.Exists(samples))
        {
            Assert.Skip("Only with SALANDUAL_CAPTURE_DIR and the local _samples folder.");
            return;
        }

        var files = new[] { "avif", "heic", "jxl", "webp", "raw", "psd", "svg", "tga", "dds-variants", "jpeg-orientation", "tiff", "gif", "ico" }
            .SelectMany(f => Directory.EnumerateFiles(Path.Join(samples, f)).Order(StringComparer.OrdinalIgnoreCase)).ToList();
        const int cell = 160, columns = 10;
        var rows = (files.Count + columns - 1) / columns;
        using var sheet = new RenderTargetBitmap(new PixelSize(columns * cell, rows * (cell + 16)));
        using (var context = sheet.CreateDrawingContext())
        {
            for (var y = 0; y < sheet.PixelSize.Height; y += 8)
            {
                for (var x = 0; x < sheet.PixelSize.Width; x += 8)
                {
                    context.FillRectangle((x / 8 + y / 8) % 2 == 0 ? Avalonia.Media.Brushes.LightGray : Avalonia.Media.Brushes.White, new Rect(x, y, 8, 8));
                }
            }

            for (var i = 0; i < files.Count; i++)
            {
                var at = new Point(i % columns * cell, i / columns * (cell + 16));
                try
                {
                    var image = Salandual.Imaging.ImageDecoder.Decode(files[i], cell - 8, cell - 8, TestContext.Current.CancellationToken);
                    using var bitmap = ImageView.ToBitmap(image);
                    context.DrawImage(bitmap, new Rect(at.X + 4, at.Y + 4, image.Width, image.Height));
                }
                catch (Salandual.Imaging.ImageDecodeException)
                {
                }

                var name = new Avalonia.Media.FormattedText(Path.GetFileName(files[i]), System.Globalization.CultureInfo.InvariantCulture, Avalonia.Media.FlowDirection.LeftToRight, Avalonia.Media.Typeface.Default, 10, Avalonia.Media.Brushes.Black)
                {
                    MaxTextWidth = cell - 4,
                    MaxLineCount = 1,
                    Trimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                };
                context.DrawText(name, new Point(at.X + 2, at.Y + cell));
            }
        }

        Directory.CreateDirectory(dir);
        sheet.Save(Path.Combine(dir, "image-samples.png"), PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public async Task AViewerLastClosedMaximized_OpensMaximized()
    {
        var w = await Open("c-small.bmp");
        var settings = w.Services.Settings;
        var before = settings.Current.Viewers;
        try
        {
            settings.Update(s => s with { Viewers = s.Viewers with { WindowMaximized = true } });
            Press(w, Key.F3);
            await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].ImageView.Image is not null);
            Assert.Equal(WindowState.Maximized, w.Viewers[^1].WindowState);
            w.Viewers[^1].Close();
        }
        finally
        {
            settings.Update(s => s with { Viewers = before });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task ClosingSalandual_ClosesItsViewers()
    {
        var w = await Open("c-small.bmp");
        w.OpenViewer(w.ActivePanel, alternate: false);
        w.OpenViewer(w.ActivePanel, alternate: false);
        await Until(() => w.Viewers.Count == 2);
        var viewers = w.Viewers.ToList();
        w.Close();
        await Until(() => w.OpenCloseQuestion is not null);
        Assert.True(viewers.All(v => v.IsVisible));
        w.OpenCloseQuestion!.Yes.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Until(() => viewers.All(v => !v.IsVisible));
        Assert.Empty(w.Viewers);
    }

    [AvaloniaFact]
    public async Task ANarrowViewer_LeavesOutTools_InsteadOfOverlappingThem()
    {
        var w = await Open("c-small.bmp");
        try
        {
            Press(w, Key.F3);
            await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].ImageView.Image is not null);
            var v = w.Viewers[^1];
            foreach (var image in new[] { true, false })
            {
                if (!image)
                {
                    Press(v, Key.F4);
                    await Until(() => v.Document is not null && v.IsHex);
                }

                v.Width = v.MinWidth;
                v.UpdateLayout();
                var left = v.ToolbarLeft.Bounds;
                var right = v.ToolbarRight.TranslatePoint(default, (Visual)v.ToolbarLeft.Parent!)!.Value.X;
                Assert.True(left.Right <= right + 0.5, $"left ends at {left.Right}, right starts at {right}");
                foreach (var tool in v.ToolbarLeft.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TemplatedControl>().Where(c => c.IsEffectivelyVisible))
                {
                    var x = tool.TranslatePoint(default, v.ToolbarLeft)!.Value.X;
                    Assert.True(x >= left.Width || x + tool.Bounds.Width <= left.Width + 0.5, $"a tool at {x}..{x + tool.Bounds.Width} runs past {left.Width}");
                }
            }

            v.Close();
        }
        finally
        {
            w.CloseNow();
        }
    }

    private sealed class ImageClipboard : IClipboardAccess
    {
        public byte[]? Dib { get; private set; }

        public byte[]? Png { get; private set; }

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut) => true;

        public bool SetText(string text) => true;

        public bool SetImage(byte[] dib, byte[]? png)
        {
            (Dib, Png) = (dib, png);
            SequenceNumber++;
            return true;
        }

        public void Clear()
        {
        }

        public ClipboardContent? Read() => new();
    }
}
