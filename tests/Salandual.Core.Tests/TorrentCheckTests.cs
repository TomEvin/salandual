// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/TorrentCheckTests.cs

using System.Security.Cryptography;
using System.Text;
using Salandual.Core.Viewer;

namespace Salandual.Core.Tests;

public sealed class TorrentCheckTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-torrentcheck-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
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
        void Bytes(byte[] raw)
        {
            Ascii($"{raw.Length}:");
            s.Write(raw);
        }

        switch (value)
        {
            case long n: Ascii($"i{n}e"); break;
            case string t: Bytes(Encoding.UTF8.GetBytes(t)); break;
            case byte[] raw: Bytes(raw); break;
            case Dictionary<string, object> dict:
                Ascii("d");
                foreach (var (k, v) in dict.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    Bytes(Encoding.UTF8.GetBytes(k));
                    Write(s, v);
                }

                Ascii("e");
                break;
            case List<(byte[] Key, object Value)> raw:
                Ascii("d");
                foreach (var (k, v) in raw.OrderBy(p => Convert.ToHexString(p.Key), StringComparer.Ordinal))
                {
                    Bytes(k);
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

    private static byte[] Data(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private string Make(string relative, byte[] data)
    {
        var path = Path.Join(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static TorrentFileCheck Of(TorrentCheck check, string path) => check.Results.Single(r => r.File.Path == path);

    private static TorrentCheck Run(TorrentInfo info, string folder)
    {
        var check = new TorrentCheck(info, folder);
        check.Run(() => { }, TestContext.Current.CancellationToken);
        return check;
    }

    private const long PieceV1 = 16384;

    private static TorrentInfo V1(string name, params (string Path, byte[]? Data, int Padding)[] files)
    {
        var all = new MemoryStream();
        var list = new List<object>();
        foreach (var (path, data, padding) in files)
        {
            var bytes = data ?? new byte[padding];
            all.Write(bytes);
            var entry = D(("length", (long)bytes.Length), ("path", path.Split('\\').Cast<object>().ToList()));
            if (data is null)
            {
                entry["attr"] = "p";
            }

            list.Add(entry);
        }

        var joined = all.ToArray();
        var pieces = new MemoryStream();
        for (var at = 0; at < joined.Length; at += (int)PieceV1)
        {
            pieces.Write(SHA1.HashData(joined.AsSpan(at, (int)Math.Min(PieceV1, joined.Length - at))));
        }

        var info = D(("name", name), ("piece length", PieceV1), ("pieces", pieces.ToArray()), ("files", list));
        return TorrentInfo.Read(B(D(("info", info))), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AllTheFilesThere_AreComplete_ThePaddingNeverLookedFor_AndTheTorrentsFolderIsFoundInTheOneYouPicked()
    {
        var beds = Data(20000, 1);
        var walk = Data(40000, 2);
        var readme = Data(300, 3);
        Make(@"Garden\Spring\beds.jpg", beds);
        Make(@"Garden\Summer\walk.mp4", walk);
        Make(@"Garden\readme.txt", readme);
        var info = V1("Garden", (@"Spring\beds.jpg", beds, 0), (@".pad\12768", null, 12768), (@"Summer\walk.mp4", walk, 0), (@"readme.txt", readme, 0), (@"Empty\none.txt", [], 0));
        Make(@"Garden\Empty\none.txt", []);

        Assert.Equal(Path.Join(_dir, "Garden"), TorrentCheck.FolderFor(info, _dir));
        Assert.Equal(Path.Join(_dir, "Garden"), TorrentCheck.FolderFor(info, Path.Join(_dir, "Garden")));
        var check = Run(info, TorrentCheck.FolderFor(info, _dir));

        Assert.Equal(4, check.Results.Count);
        Assert.All(check.Results, r => Assert.Equal(TorrentFileState.Complete, r.State));
        Assert.Equal(check.PieceCount, check.PiecesDone);
        Assert.Equal(info.TotalSize, check.BytesDone);
        Assert.Equal((4, 0, 0, 0, 1.0), TorrentCheck.Count(check.Results));
    }

    [Fact]
    public void AChangedByte_MakesItsFilePartlyThere_AndAMissingFileIsMissing()
    {
        var a = Data(50000, 4);
        var b = Data(30000, 5);
        var c = Data(10000, 6);
        var info = V1("Set", ("a.bin", a, 0), ("b.bin", b, 0), ("c.bin", c, 0));
        var changed = (byte[])a.Clone();
        changed[100] ^= 0xFF;
        Make(@"Set\a.bin", changed);
        Make(@"Set\b.bin", b);

        var check = Run(info, Path.Join(_dir, "Set"));

        var first = Of(check, "a.bin");
        Assert.Equal(TorrentFileState.Partly, first.State);
        Assert.Equal(1, first.BadPieces);
        Assert.Equal(50000 - PieceV1, first.RightBytes);
        Assert.InRange(first.Share, 0.67, 0.68);
        Assert.Equal(TorrentFileState.Missing, Of(check, "c.bin").State);

        Assert.Equal(TorrentFileState.Partly, Of(check, "b.bin").State);
        var (complete, partly, missing, skipped, share) = TorrentCheck.Count(check.Results);
        Assert.Equal((0, 2, 1, 0), (complete, partly, missing, skipped));
        Assert.True(share is > 0.5 and < 0.9);
    }

    [Fact]
    public void AFileOfAnotherSize_IsPartlyThere_AndSaysItsSizeDiffers()
    {
        var a = Data(20000, 7);
        var info = V1("One", ("a.bin", a, 0));
        Make(@"One\a.bin", [.. a, 1, 2, 3]);

        var result = Of(Run(info, Path.Join(_dir, "One")), "a.bin");

        Assert.Equal(TorrentFileState.Partly, result.State);
        Assert.Equal(TorrentCheckNote.SizeDiffers, result.Note);
    }

    [Fact]
    public void AStrangeName_IsNeverOpened_EvenWhenAFileOfThatNameIsOutsideTheFolder()
    {
        var secret = Data(5000, 8);
        Make("outside.txt", secret);
        var info = V1("Box", (@"..\outside.txt", secret, 0), ("CON.txt", secret, 0), ("invoice‮fdp.exe", secret, 0));
        Directory.CreateDirectory(Path.Join(_dir, "Box"));

        var check = Run(info, Path.Join(_dir, "Box"));

        Assert.All(check.Results, r =>
        {
            Assert.Equal(TorrentFileState.Skipped, r.State);
            Assert.Equal(TorrentCheckNote.StrangeName, r.Note);
            Assert.Equal(0, r.RightBytes);
        });
    }

    [Fact]
    public void AFolderThatIsALink_IsNeverFollowed()
    {
        var a = Data(9000, 9);
        Make(@"Real\a.bin", a);
        Directory.CreateDirectory(Path.Join(_dir, "Linked"));
        try
        {
            Directory.CreateSymbolicLink(Path.Join(_dir, @"Linked\Sub"), Path.Join(_dir, "Real"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Windows doesn't let this account make a symbolic link (no developer mode).");
        }

        var info = V1("Linked", (@"Sub\a.bin", a, 0));
        var result = Of(Run(info, Path.Join(_dir, "Linked")), @"Sub\a.bin");

        Assert.Equal(TorrentFileState.Skipped, result.State);
        Assert.Equal(TorrentCheckNote.Link, result.Note);
    }

    [Fact]
    public void ASingleFileTorrent_IsLookedForInTheFolderYouPicked()
    {
        var a = Data(17000, 10);
        Make("notes.bin", a);
        var pieces = new MemoryStream();
        pieces.Write(SHA1.HashData(a.AsSpan(0, (int)PieceV1)));
        pieces.Write(SHA1.HashData(a.AsSpan((int)PieceV1)));
        var info = TorrentInfo.Read(B(D(("info", D(("name", "notes.bin"), ("piece length", PieceV1), ("pieces", pieces.ToArray()), ("length", (long)a.Length))))), TestContext.Current.CancellationToken);

        Assert.Equal(_dir, TorrentCheck.FolderFor(info, _dir));
        Assert.Equal(TorrentFileState.Complete, Run(info, _dir).Results.Single().State);
    }

    private const int PieceV2 = 32768;
    private const int BlockV2 = 16384;

    private static byte[] Pair(byte[] a, byte[] b) => SHA256.HashData([.. a, .. b]);

    private static byte[] Tree(List<byte[]> leaves, int width, byte[] pad)
    {
        var level = Enumerable.Range(0, width).Select(i => i < leaves.Count ? leaves[i] : pad).ToList();
        while (level.Count > 1)
        {
            level = [.. Enumerable.Range(0, level.Count / 2).Select(i => Pair(level[2 * i], level[(2 * i) + 1]))];
        }

        return level[0];
    }

    private static List<byte[]> Blocks(byte[] data, int from, int length)
        => [.. Enumerable.Range(0, (length + BlockV2 - 1) / BlockV2).Select(b => SHA256.HashData(data.AsSpan(from + (b * BlockV2), Math.Min(BlockV2, length - (b * BlockV2)))))];

    private static (byte[] Root, byte[]? Layer) Hashes(byte[] data)
    {
        var zero = new byte[32];
        if (data.Length <= PieceV2)
        {
            var leaves = Blocks(data, 0, data.Length);
            return (Tree(leaves, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)leaves.Count), zero), null);
        }

        var pieces = new List<byte[]>();
        for (var at = 0; at < data.Length; at += PieceV2)
        {
            pieces.Add(Tree(Blocks(data, at, Math.Min(PieceV2, data.Length - at)), PieceV2 / BlockV2, zero));
        }

        var pad = Pair(zero, zero);
        return (Tree(pieces, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)pieces.Count), pad), [.. pieces.SelectMany(p => p)]);
    }

    private static TorrentInfo V2(string name, bool spoilLayer, params (string Name, byte[] Data)[] files)
    {
        var tree = new Dictionary<string, object>();
        var layers = new List<(byte[] Key, object Value)>();
        foreach (var (file, data) in files)
        {
            var (root, layer) = Hashes(data);
            tree[file] = D(("", D(("length", (long)data.Length), ("pieces root", root))));
            if (layer is not null)
            {
                if (spoilLayer)
                {
                    layer[0] ^= 1;
                }

                layers.Add((root, layer));
            }
        }

        var info = D(("name", name), ("piece length", (long)PieceV2), ("meta version", 2L), ("file tree", tree));
        return TorrentInfo.Read(B(D(("info", info), ("piece layers", layers))), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AVersion2Torrent_IsCheckedWithItsSha256Trees_AFileOfOnePieceWithItsRoot()
    {
        var big = Data((PieceV2 * 2) + 5000, 11);
        var small = Data(20000, 12);
        Make(@"Two\big.bin", big);
        Make(@"Two\small.bin", small);
        var info = V2("Two", false, ("big.bin", big), ("small.bin", small));
        Assert.Equal(TorrentVersion.V2, info.Version);

        var check = Run(info, TorrentCheck.FolderFor(info, _dir));

        Assert.All(check.Results, r => Assert.Equal(TorrentFileState.Complete, r.State));
        Assert.Equal(4, check.PieceCount);

        var changed = (byte[])big.Clone();
        changed[PieceV2 + 10] ^= 0xFF;
        Make(@"Two\big.bin", changed);
        var again = Of(Run(info, Path.Join(_dir, "Two")), "big.bin");
        Assert.Equal(TorrentFileState.Partly, again.State);
        Assert.Equal(1, again.BadPieces);
        Assert.Equal(big.Length - PieceV2, again.RightBytes);
    }

    [Fact]
    public void AVersion2PieceLayerThatDoesntAddUp_IsNeverTrusted()
    {
        var big = Data(PieceV2 * 3, 13);
        Make(@"Bad\big.bin", big);
        var info = V2("Bad", true, ("big.bin", big));

        var result = Of(Run(info, Path.Join(_dir, "Bad")), "big.bin");

        Assert.Equal(TorrentFileState.Partly, result.State);
        Assert.Equal(TorrentCheckNote.NoHashes, result.Note);
        Assert.Equal(0, result.RightBytes);
    }

    [Fact]
    public void WhileAFileIsRead_ItIsCheckingWithHowFar_AMissingOneIsKnownAtOnce_AndTheNextWaits()
    {
        var a = Data((int)PieceV1 * 4, 15);
        var b = Data(1000, 16);
        var c = Data(1000, 17);
        Make(@"Far\a.bin", a);
        Make(@"Far\c.bin", c);
        var info = V1("Far", ("a.bin", a, 0), ("b.bin", b, 0), ("c.bin", c, 0));
        var check = new TorrentCheck(info, Path.Join(_dir, "Far"));
        IReadOnlyList<TorrentFileCheck>? seen = null;

        check.Run(() => seen ??= check.PiecesDone == 1 ? check.Results : null, TestContext.Current.CancellationToken);

        Assert.Equal(TorrentFileState.Checking, seen![0].State);
        Assert.Equal(PieceV1, seen[0].DoneBytes);
        Assert.Equal(0.25, seen[0].Done);
        Assert.Equal(TorrentFileState.Missing, seen[1].State);
        Assert.Equal(TorrentFileState.Waiting, seen[2].State);
        Assert.Equal(1.0, check.Results[0].Done);
    }

    [Fact]
    public void Cancelling_StopsTheCheck()
    {
        var a = Data(60000, 14);
        Make(@"Stop\a.bin", a);
        var info = V1("Stop", ("a.bin", a, 0));
        using var cancel = new CancellationTokenSource();
        var check = new TorrentCheck(info, Path.Join(_dir, "Stop"));

        Assert.Throws<OperationCanceledException>(() => check.Run(cancel.Cancel, cancel.Token));
        Assert.Equal(1, check.PiecesDone);
    }
}
