// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/TorrentViewerTests.cs

using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.App.Viewer;
using Salandual.Core.Panels;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class TorrentViewerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-torrent-").FullName;
    private readonly TextClipboard _clip = new();

    public TorrentViewerTests()
    {
        TorrentFacts.Clear();
        File.WriteAllBytes(Path.Join(_dir, "a-garden.torrent"), Garden());
        File.WriteAllText(Path.Join(_dir, "b-broken.torrent"), "hello, not bencode");
        File.WriteAllBytes(Path.Join(_dir, "c-strange.torrent"), Strange());
        File.WriteAllText(Path.Join(_dir, "d-notes.txt"), "notes");
    }

    public void Dispose()
    {
        TorrentFacts.Clear();
        Directory.Delete(_dir, recursive: true);
    }

    private static byte[] B(object value)
    {
        var s = new MemoryStream();
        Write(s, value);
        return s.ToArray();
    }

    private static void Write(MemoryStream s, object value)
    {
        void Ascii(string t) => s.Write(Encoding.ASCII.GetBytes(t));
        switch (value)
        {
            case long n: Ascii($"i{n}e"); break;
            case string t:
                var bytes = Encoding.UTF8.GetBytes(t);
                Ascii($"{bytes.Length}:");
                s.Write(bytes);
                break;
            case byte[] raw:
                Ascii($"{raw.Length}:");
                s.Write(raw);
                break;
            case Dictionary<string, object> dict:
                Ascii("d");
                foreach (var (k, v) in dict.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    Write(s, k);
                    Write(s, v);
                }

                Ascii("e");
                break;
            case object[] list:
                Ascii("l");
                foreach (var item in list)
                {
                    Write(s, item);
                }

                Ascii("e");
                break;
        }
    }

    private static Dictionary<string, object> D(params (string Key, object Value)[] items) => items.ToDictionary(i => i.Key, i => i.Value);

    private static Dictionary<string, object> File_(long length, params string[] path) => D(("length", length), ("path", path.Cast<object>().ToArray()));

    private static byte[] Garden() => B(D(
        ("announce", "udp://tracker.example.org:6969/announce"),
        ("announce-list", new object[] { new object[] { "udp://tracker.example.org:6969/announce" }, new object[] { "http://backup.example.com/announce" } }),
        ("comment", "The club's photos of the year. Details: https://club.example.org/photos"),
        ("created by", "ExampleTorrent 4.2"),
        ("creation date", 1793644800L),
        ("url-list", "https://seed.example.org/files/"),
        ("info", D(
            ("name", "Garden photos 2026"),
            ("piece length", 2097152L),
            ("pieces", new byte[20 * 4]),
            ("files", new object[]
            {
                File_(1_100_000, "Spring", "beds-in-june.jpg"),
                D(("length", 48L), ("path", new object[] { ".pad", "48" }), ("attr", "p")),
                File_(1_600_000, "Summer", "walk-through.mp4"),
                File_(790_000, "Summer", "tomatoes-row-3.jpg"),
                File_(2048, "readme.txt"),
            })))));

    private static byte[] Strange() => B(D(("info", D(
        ("name", "Invoices"),
        ("piece length", 16384L),
        ("pieces", new byte[20]),
        ("files", new object[] { File_(1024, "..", "..", "Windows", "win.ini"), File_(320, "invoice‮fdp.exe"), File_(4096, "CON.txt"), File_(10, "fine.txt") })))));

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
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static async Task<ViewerWindow> Viewer(MainWindow w)
    {
        Press(w, Key.F3);
        await Until(() => w.Viewers.Count > 0 && w.Viewers[^1].Document is not null);
        var v = w.Viewers[^1];
        v.Width = 1100;
        v.Height = 700;
        await v.Loading;
        Dispatcher.UIThread.RunJobs();
        v.CaptureRenderedFrame();
        return v;
    }

    private static List<string> Status(ViewerWindow v) => [.. v.StatusCells.Select(c => (c.Child as TextBlock)?.Text ?? "")];

    private static string SummaryText(ViewerWindow v)
    {
        var text = new StringBuilder();
        void Walk(object? o)
        {
            switch (o)
            {
                case TextBlock t:
                    text.Append(t.Text).Append('\n');
                    foreach (var inline in t.Inlines ?? [])
                    {
                        if (inline is Avalonia.Controls.Documents.Run run)
                        {
                            text.Append(run.Text);
                        }
                        else if (inline is Avalonia.Controls.Documents.InlineUIContainer { Child: TextBlock link })
                        {
                            text.Append(link.Text).Append('\n');
                        }
                    }

                    break;
                case Panel p:
                    foreach (var c in p.Children)
                    {
                        Walk(c);
                    }

                    break;
                case Decorator d:
                    Walk(d.Child);
                    break;
                case ContentControl cc:
                    Walk(cc.Content);
                    break;
            }
        }

        Walk(v.TorrentSummaryHost.Content);
        return text.ToString();
    }

    private static void Focus(FilePanel panel, string name) => panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == name));

    [AvaloniaFact]
    public async Task F3_ShowsTheSummary_WithFilesStructureTextAndHexBeside_AndCopiesOnlyToTheClipboard()
    {
        var w = await Open("a-garden.torrent");
        try
        {
            var v = await Viewer(w);
            Assert.Equal(TorrentPage.Summary, v.TorrentPageShown);
            var t = v.Torrent!;
            Assert.Equal("Garden photos 2026", t.Name);
            Assert.Equal(4, t.FileCount);
            Assert.Equal(["Torrent", "Files", "Structure", "Text", "Hex"], v.ModeOrder);

            var page = SummaryText(v);
            Assert.Contains("Garden photos 2026", page, StringComparison.Ordinal);
            Assert.Contains("in 4 files · version 1", page, StringComparison.Ordinal);
            Assert.Contains(Salandual.App.Viewer.ViewerWindow.Grouped(t.InfoHash), page, StringComparison.Ordinal);
            Assert.Contains("ExampleTorrent 4.2", page, StringComparison.Ordinal);
            var format = Salandual.Core.Listing.DisplayFormat.From(new Salandual.Core.Settings.PanelSettings(), System.Globalization.CultureInfo.CurrentCulture);
            var madeOn = DateTimeOffset.FromUnixTimeSeconds(1793644800L).LocalDateTime;
            Assert.Contains(format.Date(madeOn) + " " + format.Time(madeOn), page, StringComparison.Ordinal);
            Assert.Contains("udp://tracker.example.org:6969/announce", page, StringComparison.Ordinal);
            Assert.Contains("HTTP, not encrypted", page, StringComparison.Ordinal);
            Assert.Contains("https://club.example.org/photos", page, StringComparison.Ordinal);
            Assert.Contains("Nothing here was asked online", page, StringComparison.Ordinal);
            Assert.Contains(Status(v), s => s.Contains("Garden photos 2026", StringComparison.Ordinal));

            v.CopyMagnetLink();
            Assert.StartsWith("magnet:?xt=urn:btih:" + t.InfoHash + "&dn=Garden%20photos%202026&tr=", _clip.Text, StringComparison.Ordinal);
            v.CopyInfoHash();
            Assert.Equal(t.InfoHash, _clip.Text);

            Press(v, Key.F6);
            Assert.True(v.IsTree);
            Assert.Equal(Salandual.Core.Viewer.DataFormat.Bencode, v.Tree!.Format);
            Assert.Contains(Status(v), s => s.Contains("Bencode", StringComparison.Ordinal));
            Press(v, Key.F4);
            Assert.True(v.IsHex);
            Assert.Equal(TorrentPage.None, v.TorrentPageShown);
            v.ShowTorrentPage(TorrentPage.Summary);
            Assert.Equal(TorrentPage.Summary, v.TorrentPageShown);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Files_IsATreeWithSizesAndShare_FoldersOpenWithRight_TheFilterAndCopyTheList()
    {
        var w = await Open("a-garden.torrent");
        try
        {
            var v = await Viewer(w);
            v.ShowTorrentPage(TorrentPage.Files);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TorrentPage.Files, v.TorrentPageShown);

            Assert.Equal(["Garden photos 2026", "Spring", "Summer", "readme.txt"], v.TorrentRows.Select(r => r.Shown));
            Assert.Equal(3_492_048, v.TorrentRows[0].Size);
            Assert.StartsWith("46", Salandual.App.Viewer.ViewerWindow.ShareText(1_600_000, 3_492_048), StringComparison.Ordinal);
            Assert.StartsWith("0", Salandual.App.Viewer.ViewerWindow.ShareText(2048, 3_492_048), StringComparison.Ordinal);

            Press(v, Key.Down);
            Press(v, Key.Down);
            Press(v, Key.Right);
            Assert.Equal(["Garden photos 2026", "Spring", "Summer", "tomatoes-row-3.jpg", "walk-through.mp4", "readme.txt"], v.TorrentRows.Select(r => r.Shown));
            Press(v, Key.Left);
            Assert.Equal(4, v.TorrentRows.Count);

            v.TorrentFilter.Text = "walk";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([@"Summer\walk-through.mp4"], v.TorrentRows.Select(r => r.Shown));
            Press(v, Key.C, RawInputModifiers.Control);
            Assert.Equal("Summer\\walk-through.mp4\t1600000\r\n", _clip.Text);
            v.TorrentFilter.Text = "";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, v.TorrentRows.Count);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task AFileThatIsNotATorrent_OpensInHex_WithTheBarSayingWhy()
    {
        var w = await Open("b-broken.torrent");
        try
        {
            var v = await Viewer(w);
            await Until(() => v.IsHex);
            Assert.Null(v.Torrent);
            Assert.Contains("This isn't a torrent file", v.LimitText, StringComparison.Ordinal);
            Assert.DoesNotContain("Torrent", v.ModeOrder);
            Assert.Contains("Text", v.ModeOrder);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task StrangeNames_AreMarked_AndHiddenCharactersWrittenOut()
    {
        var w = await Open("c-strange.torrent");
        try
        {
            var v = await Viewer(w);
            Assert.Contains("3 names inside", SummaryText(v), StringComparison.Ordinal);
            v.ShowTorrentPage(TorrentPage.Files);
            v.TorrentFilter.Text = ".";
            Dispatcher.UIThread.RunJobs();
            var rows = v.TorrentRows.ToDictionary(r => r.Shown, r => r.Issue);
            Assert.Equal(TorrentNameIssue.NotAllowed, rows[@"..\..\Windows\win.ini"]);
            Assert.Equal(TorrentNameIssue.HiddenMark, rows["invoice⟨RLO⟩fdp.exe"]);
            Assert.Equal(TorrentNameIssue.Reserved, rows["CON.txt"]);
            Assert.Equal(TorrentNameIssue.None, rows["fine.txt"]);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task ThePanel_ShowsTheSummary_OnTheInformationLine_InPeek_AndInTheColumnsView()
    {
        var w = await Open("a-garden.torrent");
        try
        {
            var host = w.ActivePanel;
            await Until(() => host.Info.LeftText.Contains("Torrent · ", StringComparison.Ordinal));
            Assert.Contains("in 4 files", host.Info.LeftText, StringComparison.Ordinal);

            Focus(host.Panel, "b-broken.torrent");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("Torrent · ", host.Info.LeftText, StringComparison.Ordinal);

            Focus(host.Panel, "a-garden.torrent");
            Press(w, Key.Space, RawInputModifiers.Shift);
            await Until(() => host.View.PeekLines is { Count: > 2 });
            Assert.Equal("Garden photos 2026", host.View.PeekLines![0]);
            var dates = Salandual.Core.Listing.DisplayFormat.From(new Salandual.Core.Settings.PanelSettings(), System.Globalization.CultureInfo.CurrentCulture);
            Assert.Contains(host.View.PeekLines, l => l.EndsWith(": " + dates.Date(DateTimeOffset.FromUnixTimeSeconds(1793644800L).LocalDateTime), StringComparison.Ordinal));
            Assert.Contains(host.View.PeekLines, l => l.Contains("tracker.example.org, backup.example.com", StringComparison.Ordinal));
            Press(w, Key.Escape);
            Assert.Null(host.View.PeekLines);

            Focus(host.Panel, "d-notes.txt");
            Press(w, Key.Space, RawInputModifiers.Shift);
            Assert.Null(host.View.PeekShown);

            Press(w, Key.D1, RawInputModifiers.Alt);
            Assert.Equal(PanelView.Columns, host.Panel.View);
            Focus(host.Panel, "a-garden.torrent");
            await Until(() => host.View.PreviewLines is { Count: > 2 });
            Assert.Equal("Garden photos 2026", host.View.PreviewLines![0]);
            host.Panel.SetView(PanelView.Detailed);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Captures_ForYourCheck()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var w = await Open("a-garden.torrent");
        try
        {
            async Task Save(ViewerWindow v, string name)
            {
                await Task.Delay(50);
                Dispatcher.UIThread.RunJobs();
                v.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }

            var v = await Viewer(w);
            await Save(v, "torrent-summary.png");
            v.ShowTorrentPage(TorrentPage.Files);
            Press(v, Key.Down);
            Press(v, Key.Down);
            Press(v, Key.Right);
            await Save(v, "torrent-files.png");
            Press(v, Key.F6);
            v.TreeView.ExpandTo(2);
            await Save(v, "torrent-structure.png");
            v.Close();

            await w.LeftPanel.Panel.NavigateAsync(_dir, "c-strange.torrent");
            w.LeftPanel.View.Focus();
            var s = await Viewer(w);
            s.ShowTorrentPage(TorrentPage.Files);
            await Save(s, "torrent-strange.png");
            s.Close();

            await w.LeftPanel.Panel.NavigateAsync(_dir, "a-garden.torrent");
            w.LeftPanel.View.Focus();
            Press(w, Key.Space, RawInputModifiers.Shift);
            await Until(() => w.ActivePanel.View.PeekLines is { Count: > 2 });
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, "torrent-peek.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
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
