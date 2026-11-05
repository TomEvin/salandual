// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/TorrentCheckUiTests.cs

using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Viewer;
using Salandual.Core.Viewer;

namespace Salandual.App.Tests;

public sealed class TorrentCheckUiTests : IDisposable
{
    private const int Piece = 16384;
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-torrentcheck-ui-").FullName;
    private readonly string _downloads;

    public TorrentCheckUiTests()
    {
        _downloads = Path.Join(_dir, "Downloads");
        var beds = Data(40000, 1);
        var walk = Data(70000, 2);
        var readme = Data(500, 3);
        Make(@"Garden\Spring\beds.jpg", beds);
        Make(@"Garden\Summer\walk.mp4", [.. walk.Take(30000), .. new byte[40000]]);
        File.WriteAllBytes(Path.Join(_dir, "garden.torrent"), Torrent("Garden", (@"Spring\beds.jpg", beds), (@"Summer\walk.mp4", walk), ("readme.txt", readme)));
        var big = Data(Piece * 40, 4);
        File.WriteAllBytes(Path.Join(_dir, "hold.torrent"), Torrent("Hold", ("a.bin", big), ("b.bin", big)));
        Make(@"Hold\a.bin", big);
        Make(@"Hold\b.bin", big);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] Data(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private void Make(string relative, byte[] data)
    {
        var path = Path.Join(_downloads, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    private static byte[] Torrent(string name, params (string Path, byte[] Data)[] files)
    {
        var all = files.SelectMany(f => f.Data).ToArray();
        var pieces = new MemoryStream();
        for (var at = 0; at < all.Length; at += Piece)
        {
            pieces.Write(SHA1.HashData(all.AsSpan(at, Math.Min(Piece, all.Length - at))));
        }

        var s = new MemoryStream();
        void A(string t) => s.Write(Encoding.ASCII.GetBytes(t));
        void Str(byte[] b)
        {
            A($"{b.Length}:");
            s.Write(b);
        }

        void Text(string t) => Str(Encoding.UTF8.GetBytes(t));
        A("d");
        Text("info");
        A("d");
        Text("files");
        A("l");
        foreach (var (path, data) in files)
        {
            A("d");
            Text("length");
            A($"i{data.Length}e");
            Text("path");
            A("l");
            foreach (var part in path.Split('\\'))
            {
                Text(part);
            }

            A("ee");
        }

        A("e");
        Text("name");
        Text(name);
        Text("piece length");
        A($"i{Piece}e");
        Text("pieces");
        Str(pieces.ToArray());
        A("ee");
        return s.ToArray();
    }

    private async Task<MainWindow> Open(string focus)
    {
        var w = new MainWindow { Width = 1300, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir, focus);
        await w.RightPanel.Panel.NavigateAsync(_downloads);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(TopLevel w, Key key)
    {
        w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
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
        await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Torrent is not null);
        var v = w.Viewers[^1];
        v.Width = 1100;
        v.Height = 700;
        Dispatcher.UIThread.RunJobs();
        return v;
    }

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            for (var k = 0; k < 10; k++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    private static async Task CloseAll(MainWindow w)
    {
        foreach (var viewer in w.Viewers.ToList())
        {
            viewer.CheckReport = null;
            viewer.StopCheck();
            await Until(() => !viewer.TorrentCheckRunning);
            viewer.Close();
        }

        Dispatcher.UIThread.RunJobs();
        w.Close();
    }

    [AvaloniaFact]
    public async Task CheckTheFiles_HappensInTheFilesPage_TheOtherPanelsFolderOffered_AndShowsEachFilesResult()
    {
        var w = await Open("garden.torrent");
        try
        {
            var v = await Viewer(w);
            Assert.True(v.TorrentCheckButton.IsVisible);
            v.ShowCheckFolder();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TorrentPage.Files, v.TorrentPageShown);
            Assert.Equal(_downloads, v.TorrentCheckFolder.Text);
            Assert.True(v.TorrentCheckStart.IsVisible);
            Assert.False(v.TorrentCheckStop.IsVisible);

            v.TorrentCheckFolder.Text = Path.Join(_dir, "nowhere");
            v.StartCheck();
            Assert.True(v.TorrentCheckError.IsVisible);
            Assert.Equal("There's no such folder.", v.TorrentCheckError.Text);
            Assert.Null(v.TorrentCheckShown);

            v.TorrentCheckFolder.Text = _downloads;
            v.StartCheck();
            var run = v.TorrentCheckShown!;
            Assert.False(v.TorrentCheckError.IsVisible);
            Assert.Equal(Path.Join(_downloads, "Garden"), run.Check.Folder);
            await Until(() => !v.TorrentCheckRunning);

            Assert.Equal(TorrentCheckState.Done, run.State);
            Assert.Equal("Garden: 1 file complete, 1 partly there, 1 missing (" + 59.3.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " %).", v.TorrentCheckTitle.Text);
            Assert.Equal("Checked in " + Path.Join(_downloads, "Garden"), v.TorrentCheckLine.Text);
            Assert.True(v.TorrentCheckStart.IsVisible);
            Capture(v, "torrent-check-done.png");

            Assert.Equal("2 not complete", v.TorrentCheckCells[""].Text);
            Assert.Equal("complete", v.TorrentCheckCells["Spring"].Text);
            Assert.Equal("missing", v.TorrentCheckCells["readme.txt"].Text);
            Assert.Null(v.TorrentCheckCells["readme.txt"].Bar);

            v.TorrentOnlyIncomplete.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([@"Summer\walk.mp4", "readme.txt"], v.TorrentRows.Select(r => r.Key));
            Assert.Equal("36 % right", v.TorrentCheckCells[@"Summer\walk.mp4"].Text);
            Capture(v, "torrent-check-incomplete.png");
        }
        finally
        {
            await CloseAll(w);
        }
    }

    [AvaloniaFact]
    public async Task WhileItRuns_EachFileHasItsBarAndPercent_TheViewerDoesntCloseWithoutStopping_AndStopEndsIt()
    {
        var w = await Open("hold.torrent");
        using var gate = new ManualResetEventSlim(false);
        var pieces = 0;
        try
        {
            var v = await Viewer(w);
            v.CheckReport = () =>
            {
                if (Interlocked.Increment(ref pieces) == 10)
                {
                    gate.Wait(TimeSpan.FromSeconds(30));
                }
            };
            v.ShowCheckFolder();
            v.StartCheck();
            await Until(() => Volatile.Read(ref pieces) >= 10);
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();

            Assert.True(v.TorrentCheckRunning);
            Assert.True(v.TorrentCheckStop.IsVisible);
            Assert.False(v.TorrentCheckStart.IsVisible);
            var a = v.TorrentCheckCells["a.bin"];
            Assert.Equal(0.25, a.Bar!.Value, 2);
            Assert.Equal("25 %", a.Text);
            Assert.Equal("waiting", v.TorrentCheckCells["b.bin"].Text);
            Assert.StartsWith("10 of 80 pieces", v.TorrentCheckLine.Text, StringComparison.Ordinal);
            Capture(v, "torrent-check-running.png");

            v.Close();
            await Until(() => v.TorrentCheckStopQuestion is not null);
            Capture(v.TorrentCheckStopQuestion!, "torrent-check-stop-question.png");
            v.TorrentCheckStopQuestion!.Close(false);
            await Until(() => v.TorrentCheckStopQuestion is null);
            Assert.True(v.IsVisible);
            Assert.True(v.TorrentCheckRunning);

            v.Close();
            await Until(() => v.TorrentCheckStopQuestion is not null);
            var held = v.TorrentCheckShown!;
            v.TorrentCheckStopQuestion!.Close(true);
            await Until(() => held.Cancel.IsCancellationRequested);
            gate.Set();
            await Until(() => !w.Viewers.Contains(v));
            await Until(() => v.TorrentCheckShown!.State == TorrentCheckState.Stopped);
        }
        finally
        {
            gate.Set();
            await CloseAll(w);
        }
    }

    [AvaloniaFact]
    public async Task Stop_EndsTheCheck_AndSaysHowFarItGot()
    {
        var w = await Open("hold.torrent");
        using var gate = new ManualResetEventSlim(false);
        var pieces = 0;
        try
        {
            var v = await Viewer(w);
            v.CheckReport = () =>
            {
                if (Interlocked.Increment(ref pieces) == 5)
                {
                    gate.Wait(TimeSpan.FromSeconds(30));
                }
            };
            v.ShowCheckFolder();
            v.StartCheck();
            await Until(() => Volatile.Read(ref pieces) >= 5);
            v.StopCheck();
            gate.Set();
            await Until(() => !v.TorrentCheckRunning);

            await Until(() => v.TorrentCheckShown!.State == TorrentCheckState.Stopped);
            Assert.Equal("The check was stopped: 5 of 80 pieces checked.", v.TorrentCheckTitle.Text);
            Assert.Equal("not checked", v.TorrentCheckCells["b.bin"].Text);
            Assert.False(v.TorrentOnlyIncomplete.IsVisible);
            Assert.True(v.TorrentCheckStart.IsVisible);
        }
        finally
        {
            gate.Set();
            await CloseAll(w);
        }
    }
}
