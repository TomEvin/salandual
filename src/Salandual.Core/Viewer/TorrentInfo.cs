// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/TorrentInfo.cs

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Salandual.Core.Resources;

namespace Salandual.Core.Viewer;

public enum TorrentVersion { V1, V2, Hybrid }

public enum TorrentNameIssue
{
    None,

    NotAllowed,

    Reserved,

    HiddenMark,
}

public sealed record TorrentFile(IReadOnlyList<string> Parts, long Length, bool IsPadding)
{
    public string Path => string.Join('\\', Parts);

    public string Name => Parts.Count > 0 ? Parts[^1] : "";

    public byte[]? PiecesRoot { get; init; }

    public TorrentNameIssue Issue
    {
        get
        {
            if (Parts.Count == 0)
            {
                return TorrentNameIssue.NotAllowed;
            }

            var worst = TorrentNameIssue.None;
            foreach (var part in Parts)
            {
                var issue = TorrentNames.Check(part);
                if (issue != TorrentNameIssue.None && (worst == TorrentNameIssue.None || issue < worst))
                {
                    worst = issue;
                }
            }

            return worst;
        }
    }
}

public sealed class TorrentInfo
{
    public const long MaxBytes = 64L * 1024 * 1024;

    private TorrentInfo(BencodeValue root, byte[] data)
    {
        Root = root;
        Data = data;
    }

    public BencodeValue Root { get; }

    internal byte[] Data { get; }

    public string Name { get; private set; } = "";

    public bool IsSingleFile { get; private set; }

    public IReadOnlyList<TorrentFile> Files { get; private set; } = [];

    public int FileCount { get; private set; }

    public int PaddingCount { get; private set; }

    public int FolderCount { get; private set; }

    public long TotalSize { get; private set; }

    public long PieceLength { get; private set; }

    public long PieceCount { get; private set; }

    public TorrentVersion Version { get; private set; }

    public byte[]? HashV1 { get; private set; }

    public byte[]? HashV2 { get; private set; }

    public bool IsPrivate { get; private set; }

    public string? CreatedBy { get; private set; }

    public DateTimeOffset? CreatedOn { get; private set; }

    public string? Comment { get; private set; }

    public string? Source { get; private set; }

    public IReadOnlyList<IReadOnlyList<string>> Trackers { get; private set; } = [];

    public IReadOnlyList<string> WebSeeds { get; private set; } = [];

    public ReadOnlyMemory<byte> PieceHashesV1 { get; private set; }

    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> PieceLayers { get; private set; } = new Dictionary<string, ReadOnlyMemory<byte>>();

    public int StrangeNames => Files.Count(f => !f.IsPadding && f.Issue != TorrentNameIssue.None) + (TorrentNames.Check(Name) != TorrentNameIssue.None ? 1 : 0);

    public static TorrentInfo ReadFile(string path, CancellationToken token = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
        if (stream.Length > MaxBytes)
        {
            throw new TorrentFileException(Loc.Format(Strings.Torrent_TooBig, MaxBytes / (1024 * 1024)), tooBig: true);
        }

        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return Read(data, token);
    }

    public static TorrentInfo Read(byte[] data, CancellationToken token = default)
    {
        if (data.LongLength > MaxBytes)
        {
            throw new TorrentFileException(Loc.Format(Strings.Torrent_TooBig, MaxBytes / (1024 * 1024)), tooBig: true);
        }

        BencodeValue root;
        try
        {
            root = Bencode.Read(data, token);
        }
        catch (BencodeException e)
        {
            throw new TorrentFileException(Loc.Format(Strings.Torrent_NotTorrentAt, e.Message, e.Offset.ToString("N0", CultureInfo.CurrentCulture)), tooBig: false);
        }

        if (root.Kind != BencodeKind.Dictionary || root["info"] is not { Kind: BencodeKind.Dictionary } info)
        {
            throw new TorrentFileException(Strings.Torrent_NoInfo, tooBig: false);
        }

        var torrent = new TorrentInfo(root, data);
        torrent.ReadInfo(info, token);
        torrent.ReadRest(root);
        return torrent;
    }

    private void ReadInfo(BencodeValue info, CancellationToken token)
    {
        Name = TextOf(info["name.utf-8"]) ?? TextOf(info["name"]) ?? "";
        PieceLength = info["piece length"] is { Kind: BencodeKind.Integer, Integer: > 0 } pl ? pl.Integer : 0;
        var v1Pieces = info["pieces"] is { Kind: BencodeKind.Bytes } p ? p : null;
        PieceHashesV1 = v1Pieces is not null ? v1Pieces.Bytes.ToArray() : ReadOnlyMemory<byte>.Empty;
        var v2 = info["meta version"] is { Kind: BencodeKind.Integer, Integer: 2 } && info["file tree"] is { Kind: BencodeKind.Dictionary };
        Version = v1Pieces is not null && v2 ? TorrentVersion.Hybrid : v2 ? TorrentVersion.V2 : TorrentVersion.V1;
        if (v1Pieces is not null || !v2)
        {
            HashV1 = SHA1.HashData(info.Raw);
        }

        if (v2)
        {
            HashV2 = SHA256.HashData(info.Raw);
        }

        IsPrivate = info["private"] is { Kind: BencodeKind.Integer, Integer: 1 };
        Source = TextOf(info["source"]);

        var files = new List<TorrentFile>();
        if (info["files"] is { Kind: BencodeKind.List } list)
        {
            foreach (var item in list.List)
            {
                if (item.Kind != BencodeKind.Dictionary)
                {
                    continue;
                }

                var path = item["path.utf-8"] is { Kind: BencodeKind.List } u ? u : item["path"];
                var parts = path is { Kind: BencodeKind.List } ? path.List.Select(x => TextOf(x) ?? "").ToList() : [];
                var padding = TextOf(item["attr"])?.Contains('p', StringComparison.Ordinal) == true;
                files.Add(new TorrentFile(parts, Math.Max(0, item["length"]?.Integer ?? 0), padding));
            }
        }
        else if (info["length"] is { Kind: BencodeKind.Integer } length)
        {
            IsSingleFile = true;
            files.Add(new TorrentFile([Name], Math.Max(0, length.Integer), false));
        }
        else if (v2)
        {
            WalkTree(info["file tree"]!, [], files, token);
            if (files.Count == 1 && files[0].Parts is [var only] && only == Name)
            {
                IsSingleFile = true;
            }
        }

        Files = files;
        FileCount = files.Count(f => !f.IsPadding);
        PaddingCount = files.Count - FileCount;
        TotalSize = files.Where(f => !f.IsPadding).Sum(f => f.Length);
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in files.Where(f => !f.IsPadding && !IsSingleFile))
        {
            for (var i = 1; i < f.Parts.Count; i++)
            {
                folders.Add(string.Join('\\', f.Parts.Take(i)));
            }
        }

        FolderCount = folders.Count;
        PieceCount = v1Pieces is not null
            ? v1Pieces.Bytes.Length / 20
            : PieceLength > 0 ? files.Sum(f => (f.Length + PieceLength - 1) / PieceLength) : 0;
    }

    private static void WalkTree(BencodeValue folder, List<string> above, List<TorrentFile> files, CancellationToken token)
    {
        foreach (var (key, value) in folder.Dictionary)
        {
            if (value.Kind != BencodeKind.Dictionary)
            {
                continue;
            }

            if ((files.Count & 0x3FF) == 0x3FF)
            {
                token.ThrowIfCancellationRequested();
            }

            if (value[""] is { Kind: BencodeKind.Dictionary } file)
            {
                var root = file["pieces root"] is { Kind: BencodeKind.Bytes } r && r.Bytes.Length == 32 ? r.Bytes.ToArray() : null;
                files.Add(new TorrentFile([.. above, key], Math.Max(0, file["length"]?.Integer ?? 0), false) { PiecesRoot = root });
            }
            else
            {
                above.Add(key);
                WalkTree(value, above, files, token);
                above.RemoveAt(above.Count - 1);
            }
        }
    }

    private void ReadRest(BencodeValue root)
    {
        CreatedBy = TextOf(root["created by"]);
        Comment = TextOf(root["comment.utf-8"]) ?? TextOf(root["comment"]);
        if (root["creation date"] is { Kind: BencodeKind.Integer, Integer: > 0 and < 253402300800 } date)
        {
            CreatedOn = DateTimeOffset.FromUnixTimeSeconds(date.Integer);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tiers = new List<IReadOnlyList<string>>();
        if (root["announce-list"] is { Kind: BencodeKind.List } announceList)
        {
            foreach (var tier in announceList.List)
            {
                var urls = (tier.Kind == BencodeKind.List ? tier.List : [tier]).Select(TextOf).OfType<string>().Where(u => u.Length > 0 && seen.Add(u)).ToList();
                if (urls.Count > 0)
                {
                    tiers.Add(urls);
                }
            }
        }

        if (tiers.Count == 0 && TextOf(root["announce"]) is { Length: > 0 } announce)
        {
            tiers.Add([announce]);
        }

        Trackers = tiers;
        var layers = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        if (root["piece layers"] is { Kind: BencodeKind.Dictionary } pieceLayers)
        {
            for (var i = 0; i < pieceLayers.Dictionary.Count; i++)
            {
                if (pieceLayers.KeyBytes(i) is { Length: 32 } key && pieceLayers.Dictionary[i].Value is { Kind: BencodeKind.Bytes } layer)
                {
                    layers.TryAdd(Hex(key), layer.Bytes.ToArray());
                }
            }
        }

        PieceLayers = layers;
        var seeds = root["url-list"] switch
        {
            { Kind: BencodeKind.Bytes } one => [TextOf(one)],
            { Kind: BencodeKind.List } many => many.List.Select(TextOf),
            _ => [],
        };
        WebSeeds = [.. seeds.OfType<string>().Where(s => s.Length > 0).Distinct(StringComparer.Ordinal)];
    }

    private static string? TextOf(BencodeValue? value) => value is { Kind: BencodeKind.Bytes } ? value.Text : null;

    public string InfoHash => Hex(HashV1 ?? HashV2 ?? []);

    public static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(bytes);

    public string MagnetLink()
    {
        var link = new StringBuilder("magnet:?");
        var first = true;
        void Add(string key, string value)
        {
            link.Append(first ? "" : "&").Append(key).Append('=').Append(value);
            first = false;
        }

        if (HashV1 is { } v1)
        {
            Add("xt", "urn:btih:" + Hex(v1));
        }

        if (HashV2 is { } v2)
        {
            Add("xt", "urn:btmh:1220" + Hex(v2));
        }

        if (Name.Length > 0)
        {
            Add("dn", Uri.EscapeDataString(Name));
        }

        foreach (var tracker in Trackers.SelectMany(t => t))
        {
            Add("tr", Uri.EscapeDataString(tracker));
        }

        return link.ToString();
    }

    public IReadOnlyList<string> TrackerHosts()
        => [.. Trackers.SelectMany(t => t).Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : u).Distinct(StringComparer.OrdinalIgnoreCase)];
}

public sealed class TorrentFileException(string message, bool tooBig) : Exception(message)
{
    public bool TooBig { get; } = tooBig;
}

public static class TorrentNames
{
    private static readonly string[] Devices = ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$"];

    public static string? MarkName(char c) => c switch
    {
        '­' => "SHY",
        '؜' => "ALM",
        '​' => "ZWSP",
        '‌' => "ZWNJ",
        '‍' => "ZWJ",
        '‎' => "LRM",
        '‏' => "RLM",
        '‪' => "LRE",
        '‫' => "RLE",
        '‬' => "PDF",
        '‭' => "LRO",
        '‮' => "RLO",
        '⁠' => "WJ",
        '⁦' => "LRI",
        '⁧' => "RLI",
        '⁨' => "FSI",
        '⁩' => "PDI",
        '﻿' => "BOM",
        _ => null,
    };

    public static TorrentNameIssue Check(string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' '))
        {
            return TorrentNameIssue.NotAllowed;
        }

        foreach (var c in name)
        {
            if (c < ' ' || c is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*' || c == '�')
            {
                return TorrentNameIssue.NotAllowed;
            }
        }

        var stem = name.Split('.')[0].TrimEnd(' ');
        if (Devices.Any(d => string.Equals(stem, d, StringComparison.OrdinalIgnoreCase))
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && (char.IsAsciiDigit(stem[3]) || stem[3] is '¹' or '²' or '³')))
        {
            return TorrentNameIssue.Reserved;
        }

        return name.Any(c => MarkName(c) is not null) ? TorrentNameIssue.HiddenMark : TorrentNameIssue.None;
    }

    public static string Shown(string name)
    {
        if (!name.Any(c => c < ' ' || MarkName(c) is not null))
        {
            return name;
        }

        var shown = new StringBuilder(name.Length + 8);
        foreach (var c in name)
        {
            if (MarkName(c) is { } mark)
            {
                shown.Append('⟨').Append(mark).Append('⟩');
            }
            else if (c < ' ')
            {
                shown.Append("⟨0x").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture)).Append('⟩');
            }
            else
            {
                shown.Append(c);
            }
        }

        return shown.ToString();
    }
}
