// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/MediaViewerTests.cs

using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Viewer;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class MediaViewerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-media-").FullName;
    private readonly TextClipboard _clip = new();

    public MediaViewerTests()
    {
        File.WriteAllBytes(Path.Join(_dir, "morning.wav"), Wave("Morning test"));
        File.WriteAllText(Path.Join(_dir, "words.mp4"), "not a video at all, just some words");
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

    private static byte[] Wave(string title)
    {
        static byte[] Chunk(string id, byte[] data)
            => [.. Encoding.ASCII.GetBytes(id), .. BitConverter.GetBytes(data.Length), .. data, .. data.Length % 2 == 1 ? new byte[1] : []];

        const int rate = 44100;
        byte[] format = [.. BitConverter.GetBytes((short)1), .. BitConverter.GetBytes((short)2), .. BitConverter.GetBytes(rate), .. BitConverter.GetBytes(rate * 4), .. BitConverter.GetBytes((short)4), .. BitConverter.GetBytes((short)16)];
        byte[] info = [.. Encoding.ASCII.GetBytes("INFO"), .. Chunk("INAM", [.. Encoding.ASCII.GetBytes(title), 0])];
        return Chunk("RIFF", [.. Encoding.ASCII.GetBytes("WAVE"), .. Chunk("fmt ", format), .. Chunk("LIST", info), .. Chunk("data", new byte[rate * 4])]);
    }

    private async Task<MainWindow> Open(string focus)
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.FileClipboardAccess = _clip;
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

    private static List<string> Texts(Control? root) => root is null ? [] : [.. root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

    private static List<string> Status(ViewerWindow v) => [.. v.StatusCells.Select(c => (c.Child as TextBlock)?.Text ?? "")];

    private static async Task<ViewerWindow> Viewer(MainWindow w)
    {
        Press(w, Key.F3);
        await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Media is not null);
        return w.Viewers[^1];
    }

    [AvaloniaFact]
    public async Task F3OnASoundFile_ShowsWhatItHolds_AndTheStatusLine()
    {
        var w = await Open("morning.wav");
        try
        {
            var v = await Viewer(w);
            Assert.True(v.IsMedia);
            await Until(() => Texts(v.MediaHost).Contains("Morning test"));
            var texts = Texts(v.MediaHost);
            Assert.Contains("Audio", texts);
            Assert.Contains(texts, t => t.StartsWith("Wave · 1 s · ", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.StartsWith("PCM · stereo · ", StringComparison.Ordinal));
            Assert.Contains("Open in the default app", texts);
            Assert.Contains("Copy as text", texts);
            var status = Status(v);
            Assert.Equal("Wave · PCM", status[0]);
            Assert.Equal("0:01", status[1]);
            Assert.Contains("Read by MediaInfo", status);
            Assert.Equal("1 / 2 media files", v.Counter.Text);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task HexAndInfo_SwitchWithoutReadingTheFileAgain()
    {
        var w = await Open("morning.wav");
        try
        {
            var v = await Viewer(w);
            var report = v.Media;
            Press(v, Key.F4);
            await Until(() => v.IsHex && v.Document is not null);
            Assert.False(v.IsMedia);
            Press(v, Key.F5);
            Assert.True(v.IsMedia);
            Assert.Same(report, v.Media);
            Assert.Contains("Read by MediaInfo", Status(v));
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Enter_OpensTheFileInItsProgram_UnlessTheSettingSaysNothing()
    {
        var w = await Open("morning.wav");
        try
        {
            var shell = Assert.IsType<NoShellAccess>(w.ShellAccess);
            var v = await Viewer(w);
            Press(v, Key.Enter);
            await Until(() => shell.Opened.Count == 1);
            Assert.Equal(Path.Join(_dir, "morning.wav"), shell.Opened[0]);

            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { MediaEnter = MediaEnter.Nothing } });
            Dispatcher.UIThread.RunJobs();
            Press(v, Key.Enter);
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(shell.Opened);
            Assert.Contains(v, w.Viewers);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task CtrlC_CopiesMediaInfosWholeReport()
    {
        var w = await Open("morning.wav");
        try
        {
            var v = await Viewer(w);
            Press(v, Key.C, RawInputModifiers.Control);
            Assert.NotNull(_clip.Text);
            Assert.StartsWith("General\r\n", _clip.Text, StringComparison.Ordinal);
            Assert.Contains("Morning test", _clip.Text, StringComparison.Ordinal);
            Assert.Contains("PCM", _clip.Text, StringComparison.Ordinal);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task AFileThatIsNotMedia_SaysSo_AndF4StillShowsItsBytes()
    {
        var w = await Open("words.mp4");
        try
        {
            Press(w, Key.F3);
            await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].IsVisible);
            var v = w.Viewers[^1];
            await Until(() => Texts(v).Any(t => t.Contains("can't be read as a media file", StringComparison.Ordinal)));
            Press(v, Key.F4);
            await Until(() => v.IsHex && v.Document is { Length: > 0 });
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task CtrlShiftF3_OffersMediaInformation()
    {
        var w = await Open("morning.wav");
        try
        {
            Press(w, Key.F3, RawInputModifiers.Control | RawInputModifiers.Shift);
            var items = w.ViewWithMenu!.Items.OfType<MenuItem>().ToList();
            var media = items.Single(i => (string?)i.Header == "Media information");
            Assert.Contains(items, i => (string?)i.Header == "Image viewer");
            w.ViewWithMenu.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(media);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task AVideoWithoutACover_ShowsThePictureWindowsHasForIt_OnlyWhenTurnedOn_AndAfterTheInformation()
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "_samples", "media", "Big_Buck_Bunny_360_10s_1MB.mp4");
        if (!File.Exists(sample))
        {
            Assert.Skip("The local _samples folder is not here.");
            return;
        }

        File.Copy(sample, Path.Join(_dir, "bunny.mp4"));
        var w = await Open("bunny.mp4");
        var asked = new List<(string Path, int Size)>();
        var slow = new TaskCompletionSource<IconPixels?>();
        w.VideoThumbnail = (path, size) =>
        {
            asked.Add((path, size));
            return slow.Task;
        };
        static List<Image> Pictures(ViewerWindow v) => [.. v.MediaHost.GetVisualDescendants().OfType<Image>()];
        try
        {
            var v = await Viewer(w);
            await Until(() => Texts(v.MediaHost).Contains("Big Buck Bunny, Sunflower version"));
            Assert.Empty(asked);
            Assert.Empty(Pictures(v));

            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { VideoFrame = true } });
            await Until(() => asked.Count == 1);
            Assert.EndsWith("bunny.mp4", asked[0].Path, StringComparison.Ordinal);
            Assert.Equal(640, asked[0].Size);
            Assert.Contains("Big Buck Bunny, Sunflower version", Texts(v.MediaHost));
            Assert.Empty(Pictures(v));
            Assert.Single(v.MediaHost.GetVisualDescendants().OfType<Salandual.App.Controls.BusyRing>());
            var bgra = new byte[32 * 18 * 4];
            Array.Fill(bgra, (byte)200);
            slow.SetResult(new IconPixels(32, 18, bgra));
            await Until(() => Pictures(v).Count == 1);
            Assert.Equal(Avalonia.Media.Stretch.UniformToFill, Pictures(v)[0].Stretch);
            Assert.Empty(v.MediaHost.GetVisualDescendants().OfType<Salandual.App.Controls.BusyRing>());

            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { VideoFrame = false } });
            await Until(() => Pictures(v).Count == 0);
            v.Close();
            await Until(() => !w.Viewers.Contains(v));

            w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { VideoFrame = true } });
            await w.LeftPanel.Panel.NavigateAsync(_dir, "morning.wav");
            var audio = await Viewer(w);
            Assert.EndsWith("morning.wav", audio.FilePath, StringComparison.Ordinal);
            Assert.Single(w.Viewers);
            await Until(() => Texts(audio.MediaHost).Contains("Morning test"));
            Assert.Single(asked);
            Assert.Empty(Pictures(audio));
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Capture_MediaInformation()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var sample = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "_samples", "media", "Big_Buck_Bunny_360_10s_1MB.mp4");
        if (File.Exists(sample))
        {
            File.Copy(sample, Path.Join(_dir, "bunny.mp4"));
        }

        var w = await Open(File.Exists(sample) ? "bunny.mp4" : "morning.wav");
        var waiting = new TaskCompletionSource<IconPixels?>();
        w.VideoThumbnail = async (path, size) =>
        {
            await waiting.Task;
            return await ShellIconLoader.LoadThumbnailAsync(path, size);
        };
        w.Services.Settings.Update(s => s with { Viewers = s.Viewers with { VideoFrame = true } });
        try
        {
            var v = await Viewer(w);
            v.Width = 1100;
            v.Height = 700;
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            v.CaptureRenderedFrame()!.Save(Path.Combine(dir, "media-loading.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            waiting.SetResult(null);
            await Until(() => v.MediaHost.GetVisualDescendants().OfType<Image>().Any());
            v.CaptureRenderedFrame()!.Save(Path.Combine(dir, "media-info.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            v.Width = v.MinWidth;
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            v.CaptureRenderedFrame()!.Save(Path.Combine(dir, "media-narrow.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            await w.LeftPanel.Panel.NavigateAsync(_dir, "morning.wav");
            v.Close();
            var audio = await Viewer(w);
            audio.Width = 1100;
            audio.Height = 700;
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            audio.CaptureRenderedFrame()!.Save(Path.Combine(dir, "media-audio.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally
        {
            w.CloseNow();
        }
    }

    private sealed class TextClipboard : IClipboardAccess
    {
        public string? Text { get; private set; }

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut) => true;

        public bool SetText(string text)
        {
            Text = text;
            SequenceNumber++;
            return true;
        }

        public bool SetImage(byte[] dib, byte[]? png) => true;

        public void Clear()
        {
        }

        public ClipboardContent? Read() => new();
    }
}
