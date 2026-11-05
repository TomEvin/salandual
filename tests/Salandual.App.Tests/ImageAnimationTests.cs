// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ImageAnimationTests.cs

using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using ImageMagick;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Viewer;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class ImageAnimationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-animation-").FullName;

    public ImageAnimationTests()
    {
        Animated("a-moving.gif", delay: 20, loops: 0);
        Animated("b-once.gif", delay: 2, loops: 1);
        using var half = new MagickImage(MagickColors.Transparent, 40, 20);
        using (var red = new MagickImage(MagickColors.Red, 20, 20))
        {
            half.Composite(red, 0, 0, CompositeOperator.Over);
        }

        half.Write(Path.Join(_dir, "c-half.png"), MagickFormat.Png);
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

    private void Animated(string name, uint delay, uint loops)
    {
        using var collection = new MagickImageCollection();
        for (var i = 0; i < 3; i++)
        {
            var frame = new MagickImage(MagickColors.Transparent, 30, 10);
            using (var square = new MagickImage(MagickColors.Red, 10, 10))
            {
                frame.Composite(square, i * 10, 0, CompositeOperator.Over);
            }

            frame.AnimationDelay = delay;
            frame.AnimationIterations = loops;
            frame.GifDisposeMethod = GifDisposeMethod.Background;
            collection.Add(frame);
        }

        collection.Write(Path.Join(_dir, name), MagickFormat.Gif);
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

    private static void Save(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task AnAnimatedGif_Plays_PPauses_AndTheCommaAndFullStopStepAFrame()
    {
        var w = await Open("a-moving.gif");
        var clipboard = new FrameClipboard();
        w.FileClipboardAccess = clipboard;
        var v = await Viewer(w);
        await v.AnimationLoading;
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(v.Animated);
        Assert.True(v.IsPlaying);
        Assert.True(v.ImageView.IsAnimated);
        Assert.True(v.PlayButton.IsVisible);
        v.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        Assert.False(v.MoreHoldsTools);
        Assert.Contains("animated", StatusText(v), StringComparison.Ordinal);
        await Until(() => v.FrameIndex != 0);
        Save(v, "image-animation-playing.png");

        Press(v, Key.P);
        Assert.False(v.IsPlaying);
        var at = v.FrameIndex;
        Press(v, Key.OemPeriod);
        Assert.Equal((at + 1) % 3, v.FrameIndex);
        Assert.Contains($"Frame {v.FrameIndex + 1} / 3 · 200 ms", StatusText(v), StringComparison.Ordinal);
        Press(v, Key.OemComma);
        Press(v, Key.OemComma);
        Assert.Equal((at + 2) % 3, v.FrameIndex);
        await Task.Delay(450);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((at + 2) % 3, v.FrameIndex);
        Save(v, "image-animation-paused.png");

        Press(v, Key.C, RawInputModifiers.Control);
        await Until(() => clipboard.Dib is not null);
        Assert.Equal(30, BinaryPrimitives.ReadInt32LittleEndian(clipboard.Dib.AsSpan(4)));
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(clipboard.Dib.AsSpan(8)));

        v.PlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(v.IsPlaying);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AnAnimation_PlaysAsOftenAsTheFileSays_ThenPlayStartsItAgain()
    {
        var w = await Open("b-once.gif");
        var v = await Viewer(w);
        await v.AnimationLoading;

        await Until(() => !v.IsPlaying);
        Assert.Equal(2, v.FrameIndex);
        Press(v, Key.P);
        Assert.True(v.IsPlaying);
        Assert.Equal(0, v.FrameIndex);
        v.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TransparentParts_ShowACheckerboard_OrWhatYouChoose_InTheMoreMenuToo()
    {
        var w = await Open("c-half.png");
        var settings = w.Services.Settings;
        var before = settings.Current.Viewers;
        try
        {
            var v = await Viewer(w);
            Assert.Null(v.Animated);
            Assert.True(v.ImageView.Image!.HasTransparency);
            Assert.Same(ViewerWindow.CheckerLight, v.ImageView.Checker);
            Assert.Null(v.ImageView.Backdrop);
            v.ImageView.ZoomTo(8);
            Dispatcher.UIThread.RunJobs();
            AssertOnlyCheckerUnderTheTransparentHalf(v);
            Save(v, "image-transparent-checker.png");

            var menu = v.BuildMoreMenu();
            var parts = menu.Items.OfType<MenuItem>().First(m => (string?)m.Header == AppStrings.Viewer_TransparentParts);
            Assert.Equal(
                [AppStrings.Viewer_Transparent_CheckerLight, AppStrings.Viewer_Transparent_CheckerDark, AppStrings.Viewer_Transparent_Canvas, AppStrings.Viewer_Transparent_White, AppStrings.Viewer_Transparent_Black, AppStrings.Viewer_Canvas_Custom],
                parts.Items.OfType<MenuItem>().Select(m => (string?)m.Header));
            parts.Items.OfType<MenuItem>().First(m => (string?)m.Header == AppStrings.Viewer_Transparent_CheckerDark).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => ReferenceEquals(v.ImageView.Checker, ViewerWindow.CheckerDark));
            Save(v, "image-transparent-checker-dark.png");

            settings.Update(s => s with { Viewers = s.Viewers with { ImageTransparent = TransparentParts.Canvas } });
            await Until(() => v.ImageView.Backdrop is null && v.ImageView.Checker is null);
            settings.Update(s => s with { Viewers = s.Viewers with { ImageTransparent = TransparentParts.Custom, TransparentCustom = "#CDE6F7" } });
            await Until(() => v.ImageView.Checker is null && v.ImageView.Backdrop is ISolidColorBrush { Color: var c } && c == Color.FromRgb(0xCD, 0xE6, 0xF7));
            v.Close();
        }
        finally
        {
            settings.Update(s => s with { Viewers = before });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public void Options_Images_HasTransparentPartsBesideTheCanvasColour()
    {
        var settings = ((App)Application.Current!).Services.Settings;
        var before = settings.Current.Viewers;
        try
        {
            var w = new OptionsWindow();
            w.Show();
            w.ShowCategory("viewers");
            Dispatcher.UIThread.RunJobs();
            Button Choice(string label) => w.Page.GetLogicalDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == label);

            Choice(AppStrings.Viewer_Transparent_CheckerDark).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TransparentParts.CheckerDark, settings.Current.Viewers.ImageTransparent);
            Choice(AppStrings.Viewer_Transparent_White).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TransparentParts.White, settings.Current.Viewers.ImageTransparent);
            Assert.Equal(CanvasColor.Dark, settings.Current.Viewers.ImageCanvas);
            Choice(AppStrings.Viewer_Transparent_White).BringIntoView();
            w.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
            Save(w, "options-transparent-parts.png");
            w.Close();
        }
        finally
        {
            settings.Update(s => s with { Viewers = before });
        }
    }

    private static void AssertOnlyCheckerUnderTheTransparentHalf(Window v)
    {
        var frame = v.CaptureRenderedFrame()!;
        var (width, height) = (frame.PixelSize.Width, frame.PixelSize.Height);
        var pixels = new int[width * height];
        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(0, 0, width, height), pinned.AddrOfPinnedObject(), pixels.Length * 4, width * 4);
        }
        finally
        {
            pinned.Free();
        }

        int left = width, top = height, right = -1, bottom = -1;
        for (var i = 0; i < pixels.Length; i++)
        {
            if ((uint)pixels[i] is 0xFFFF0000u or 0xFF0000FFu)
            {
                (left, top, right, bottom) = (Math.Min(left, i % width), Math.Min(top, i / width), Math.Max(right, i % width), Math.Max(bottom, i / width));
            }
        }

        Assert.True(right > left);
        var seen = new HashSet<uint>();
        for (var y = top; y <= bottom; y++)
        {
            for (var x = right + 1; x <= right + 1 + (right - left); x++)
            {
                seen.Add((uint)pixels[(y * width) + x]);
            }
        }

        Assert.Equal([0xFFCCCCCCu, 0xFFFFFFFFu], seen.Order());
    }

    private sealed class FrameClipboard : IClipboardAccess
    {
        public byte[]? Dib { get; private set; }

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut) => true;

        public bool SetText(string text) => true;

        public bool SetImage(byte[] dib, byte[]? png)
        {
            Dib = dib;
            SequenceNumber++;
            return true;
        }

        public void Clear()
        {
        }

        public ClipboardContent? Read() => new();
    }
}
