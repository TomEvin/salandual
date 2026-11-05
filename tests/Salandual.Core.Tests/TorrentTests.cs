// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/TorrentTests.cs

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.Core.Tests;

public sealed class TorrentTests
{

    private static byte[] B(object value)
    {
        var out_ = new MemoryStream();
        Write(out_, value);
        return out_.ToArray();
    }

    private static void Write(MemoryStream s, object value)
    {
        void Ascii(string t) => s.Write(Encoding.ASCII.GetBytes(t));
        switch (value)
        {
            case long n: Ascii($"i{n}e"); break;
            case int n: Ascii($"i{n}e"); break;
            case string t:
                var bytes = Encoding.UTF8.GetBytes(t);
                Ascii($"{bytes.Length}:");
                s.Write(bytes);
                break;
            case byte[] raw:
                Ascii($"{raw.Length}:");
                s.Write(raw);
                break;
            case IEnumerable<KeyValuePair<string, object>> dict:
                Ascii("d");
                foreach (var (k, v) in dict.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    Write(s, k);
                    Write(s, v);
                }

                Ascii("e");
                break;
            case System.Collections.IEnumerable list:
                Ascii("l");
                foreach (var item in list)
                {
                    Write(s, item!);
                }

                Ascii("e");
                break;
        }
    }

    private static Dictionary<string, object> D(params (string Key, object Value)[] items) => items.ToDictionary(i => i.Key, i => i.Value);

    private static Dictionary<string, object> Info() => D(
        ("name", "Garden photos 2026"),
        ("piece length", 2097152L),
        ("pieces", new byte[20 * 3]),
        ("files", new object[]
        {
            D(("length", 1000L), ("path", new object[] { "Spring", "beds.jpg" })),
            D(("length", 48L), ("path", new object[] { ".pad", "48" }), ("attr", "p")),
            D(("length", 3000L), ("path", new object[] { "Summer", "walk.mp4" })),
            D(("length", 20L), ("path", new object[] { "readme.txt" })),
        }));

    private static byte[] Torrent(Dictionary<string, object>? info = null, params (string Key, object Value)[] more)
    {
        var root = D(("info", info ?? Info()));
        foreach (var (k, v) in more)
        {
            root[k] = v;
        }

        return B(root);
    }

    private static TorrentInfo Read(byte[] data) => TorrentInfo.Read(data, TestContext.Current.CancellationToken);

    [Fact]
    public void AVersion1Torrent_GivesItsNameFilesPiecesAndHash_ThePaddingHidden()
    {
        var info = Info();
        var t = Read(Torrent(info,
            ("announce", "udp://tracker.example.org:6969/announce"),
            ("announce-list", new object[] { new object[] { "udp://tracker.example.org:6969/announce", "udp://tracker.example.net:1337" }, new object[] { "http://backup.example.com/announce" } }),
            ("url-list", "https://seed.example.org/files/"),
            ("comment", "The club's photos. Details: https://club.example.org/photos"),
            ("created by", "ExampleTorrent 4.2"),
            ("creation date", 1793644800L)));

        Assert.Equal("Garden photos 2026", t.Name);
        Assert.False(t.IsSingleFile);
        Assert.Equal(TorrentVersion.V1, t.Version);
        Assert.Equal(3, t.FileCount);
        Assert.Equal(1, t.PaddingCount);
        Assert.Equal(4020, t.TotalSize);
        Assert.Equal(2, t.FolderCount);
        Assert.Equal(3, t.PieceCount);
        Assert.Equal(2097152, t.PieceLength);
        Assert.Equal(@"Spring\beds.jpg", t.Files[0].Path);
        Assert.True(t.Files[1].IsPadding);
        Assert.Equal(SHA1.HashData(B(info)), t.HashV1);
        Assert.Null(t.HashV2);
        Assert.False(t.IsPrivate);
        Assert.Equal("ExampleTorrent 4.2", t.CreatedBy);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793644800), t.CreatedOn);
        Assert.Equal(2, t.Trackers.Count);
        Assert.Equal(["udp://tracker.example.org:6969/announce", "udp://tracker.example.net:1337"], t.Trackers[0]);
        Assert.Equal(["https://seed.example.org/files/"], t.WebSeeds);
        Assert.Equal(["tracker.example.org", "tracker.example.net", "backup.example.com"], t.TrackerHosts());
    }

    [Fact]
    public void ASingleFileTorrent_IsOneFile_AndAnnounceAloneIsTheOnlyTier()
    {
        var info = D(("name", "notes.txt"), ("piece length", 16384L), ("pieces", new byte[20]), ("length", 1234L), ("private", 1L));
        var t = Read(Torrent(info, ("announce", "https://tracker.example.org/announce")));
        Assert.True(t.IsSingleFile);
        Assert.Equal("notes.txt", Assert.Single(t.Files).Path);
        Assert.Equal(1234, t.TotalSize);
        Assert.True(t.IsPrivate);
        Assert.Equal(["https://tracker.example.org/announce"], Assert.Single(t.Trackers));
    }

    [Fact]
    public void AVersion2AndAHybridTorrent_HaveTheirSha256Hash_AndTheFileTreeIsRead()
    {
        var a = D(("", D(("length", 5000L), ("pieces root", new byte[32]))));
        var b = D(("", D(("length", 10L))));
        var tree = D(("Garden", D(("a.jpg", a), ("b.jpg", b))));
        var v2 = D(("name", "Photos"), ("piece length", 4096L), ("meta version", 2L), ("file tree", tree));
        var t = Read(Torrent(v2));
        Assert.Equal(TorrentVersion.V2, t.Version);
        Assert.Null(t.HashV1);
        Assert.Equal(SHA256.HashData(B(v2)), t.HashV2);
        Assert.Equal([@"Garden\a.jpg", @"Garden\b.jpg"], t.Files.Select(f => f.Path));
        Assert.Equal(2 + 1, t.PieceCount);

        var hybrid = Info();
        hybrid["meta version"] = 2L;
        hybrid["file tree"] = tree;
        var h = Read(Torrent(hybrid));
        Assert.Equal(TorrentVersion.Hybrid, h.Version);
        Assert.NotNull(h.HashV1);
        Assert.NotNull(h.HashV2);
        Assert.Contains("urn:btih:", h.MagnetLink(), StringComparison.Ordinal);
        Assert.Contains("urn:btmh:1220", h.MagnetLink(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMagnetLink_HasTheHashTheNameAndEachTracker_Escaped()
    {
        var t = Read(Torrent(null, ("announce-list", new object[] { new object[] { "udp://tracker.example.org:6969/announce" } })));
        var link = t.MagnetLink();
        Assert.StartsWith("magnet:?xt=urn:btih:" + t.InfoHash, link, StringComparison.Ordinal);
        Assert.Contains("&dn=Garden%20photos%202026", link, StringComparison.Ordinal);
        Assert.Contains("&tr=udp%3A%2F%2Ftracker.example.org%3A6969%2Fannounce", link, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{40}$", t.InfoHash);
    }

    [Theory]
    [InlineData("d4:infod4:name1:a", "cut short")]
    [InlineData("d4:infod4:name99:ae", "longer than the file")]
    [InlineData("i03e", "written wrongly")]
    [InlineData("i-0e", "written wrongly")]
    [InlineData("i99999999999999999999e", "written wrongly")]
    [InlineData("dei1e", "more after")]
    [InlineData("di1ei2ee", "key isn't text")]
    [InlineData("x", "unknown")]
    public void ABrokenFile_IsNotATorrent_AndSaysWhy(string text, string why)
    {
        var e = Assert.Throws<TorrentFileException>(() => Read(Encoding.ASCII.GetBytes(text)));
        Assert.Contains(why, e.Message, StringComparison.Ordinal);
        Assert.False(e.TooBig);
    }

    [Fact]
    public void NestingTooDeep_IsRefused_NotAStackOverflow()
    {
        var deep = new string('l', 100_000) + new string('e', 100_000);
        var e = Assert.Throws<TorrentFileException>(() => Read(Encoding.ASCII.GetBytes(deep)));
        Assert.Contains("nested too deeply", e.Message, StringComparison.Ordinal);
        Assert.Equal(64, Bencode.MaxDepth);
    }

    [Fact]
    public void AFileWithoutAnInfoDictionary_OrTooBig_IsNotATorrent()
    {
        Assert.Contains("no info dictionary", Assert.Throws<TorrentFileException>(() => Read(B(D(("announce", "x"))))).Message, StringComparison.Ordinal);
        Assert.True(Assert.Throws<TorrentFileException>(() => Read(new byte[TorrentInfo.MaxBytes + 1])).TooBig);
    }

    [Theory]
    [InlineData("..", TorrentNameIssue.NotAllowed)]
    [InlineData(".", TorrentNameIssue.NotAllowed)]
    [InlineData("", TorrentNameIssue.NotAllowed)]
    [InlineData(@"..\..\Windows", TorrentNameIssue.NotAllowed)]
    [InlineData("a/b", TorrentNameIssue.NotAllowed)]
    [InlineData("C:", TorrentNameIssue.NotAllowed)]
    [InlineData("what?", TorrentNameIssue.NotAllowed)]
    [InlineData("dot.", TorrentNameIssue.NotAllowed)]
    [InlineData("tab\there", TorrentNameIssue.NotAllowed)]
    [InlineData("CON.txt", TorrentNameIssue.Reserved)]
    [InlineData("com1", TorrentNameIssue.Reserved)]
    [InlineData("LPT9.log", TorrentNameIssue.Reserved)]
    [InlineData("nul .txt", TorrentNameIssue.Reserved)]
    [InlineData("invoice‮fdp.exe", TorrentNameIssue.HiddenMark)]
    [InlineData("zero​width", TorrentNameIssue.HiddenMark)]
    [InlineData("console.txt", TorrentNameIssue.None)]
    [InlineData("beds-in-june.jpg", TorrentNameIssue.None)]
    [InlineData("Fényképek 2026", TorrentNameIssue.None)]
    public void TheNamesInside_AreChecked(string name, TorrentNameIssue issue) => Assert.Equal(issue, TorrentNames.Check(name));

    [Fact]
    public void HiddenCharacters_AreWrittenOut_AndAFileGetsItsWorstIssue()
    {
        Assert.Equal("invoice⟨RLO⟩fdp.exe", TorrentNames.Shown("invoice‮fdp.exe"));
        Assert.Equal("a⟨0x01⟩b", TorrentNames.Shown("a\u0001b"));
        Assert.Equal("plain", TorrentNames.Shown("plain"));
        Assert.Equal(TorrentNameIssue.NotAllowed, new TorrentFile(["CON", "..", "x‮"], 1, false).Issue);
        Assert.Equal(TorrentNameIssue.Reserved, new TorrentFile(["CON", "x‮"], 1, false).Issue);

        var info = Info();
        info["files"] = new object[] { D(("length", 1L), ("path", new object[] { "..", "..", "win.ini" })), D(("length", 1L), ("path", new object[] { "ok.txt" })) };
        Assert.Equal(1, Read(Torrent(info)).StrangeNames);
    }

    [Fact]
    public void TheStructure_ShowsTextsInQuotes_ThePiecesByCount_AndTheDateBesideItsNumber()
    {
        var t = Read(Torrent(null, ("creation date", 1793644800L), ("comment", "hi‮")));
        var tree = DataTree.FromBencode(t.Root, d => "the date", TestContext.Current.CancellationToken);
        Assert.Equal(DataFormat.Bencode, tree.Format);
        var nodes = tree.Walk().ToList();
        var pieces = nodes.Single(n => n.Name == "pieces");
        Assert.Equal(DataKind.Bytes, pieces.Kind);
        Assert.Matches(new Regex("^60 bytes · 3 SHA-1 hashes$"), tree.Shown(pieces));
        Assert.Equal("1793644800 · the date", tree.Shown(nodes.Single(n => n.Name == "creation date")));
        Assert.Equal("1793644800", tree.Value(nodes.Single(n => n.Name == "creation date")));
        Assert.Equal("\"hi⟨RLO⟩\"", tree.Shown(nodes.Single(n => n.Name == "comment")));
        Assert.Equal("$.info['piece length']", tree.PathOf(nodes.Single(n => n.Name == "piece length"), DataPathStyle.Dollar));
        Assert.Single(tree.Find(new Regex("readme"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TorrentFiles_OpenInTheTorrentViewer_HexWithAltF3_UpTo64MB()
    {
        var v = new ViewerSettings();
        Assert.Equal(ViewerKind.Torrent, v.KindFor("Garden.TORRENT", alternate: false));
        Assert.Equal(ViewerKind.Hex, v.KindFor("Garden.torrent", alternate: true));
        Assert.Equal(64L * 1024 * 1024, v.LargestBytes("a.torrent", ViewerKind.Torrent));
    }
}
