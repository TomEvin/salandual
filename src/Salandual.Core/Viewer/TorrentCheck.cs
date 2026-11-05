// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/TorrentCheck.cs

using System.Numerics;
using System.Security.Cryptography;
using Salandual.Core.Resources;

namespace Salandual.Core.Viewer;

public enum TorrentFileState
{
    Waiting,

    Checking,

    Complete,

    Partly,

    Missing,

    Skipped,
}

public enum TorrentCheckNote
{
    None,

    StrangeName,

    Link,

    OnlineOnly,

    SizeDiffers,

    Unreadable,

    NoHashes,
}

public sealed record TorrentFileCheck(TorrentFile File, TorrentFileState State, long RightBytes, int BadPieces, TorrentCheckNote Note)
{
    public long DoneBytes { get; init; }

    public double Done => File.Length == 0 ? 1 : Math.Clamp(DoneBytes / (double)File.Length, 0, 1);

    public double Share => File.Length == 0 ? (State == TorrentFileState.Complete ? 1 : 0) : Math.Clamp(RightBytes / (double)File.Length, 0, 1);
}

public sealed class TorrentCheckException(string message) : Exception(message);

public sealed class TorrentCheck
{
    public const long MaxPieceLength = 256L * 1024 * 1024;

    private const int Block = 16 * 1024;
    private const int OfflineAttribute = 0x1000, RecallOnOpenAttribute = 0x40000, RecallOnDataAccessAttribute = 0x400000;

    private readonly TorrentInfo _info;
    private readonly TorrentFileCheck[] _results;
    private readonly Dictionary<int, int> _resultOf = [];
    private readonly long[] _done;
    private readonly Dictionary<string, bool> _linkFolders = new(StringComparer.OrdinalIgnoreCase);
    private long _piecesDone, _bytesDone;

    public TorrentCheck(TorrentInfo info, string folder)
    {
        _info = info;
        Folder = folder;
        var results = new List<TorrentFileCheck>();
        for (var i = 0; i < info.Files.Count; i++)
        {
            if (!info.Files[i].IsPadding)
            {
                _resultOf[i] = results.Count;
                results.Add(new TorrentFileCheck(info.Files[i], TorrentFileState.Waiting, 0, 0, TorrentCheckNote.None));
            }
        }

        _results = [.. results];
        _done = new long[_results.Length];
        PieceCount = UsesV1 ? info.PieceHashesV1.Length / 20 : info.Files.Where(f => !f.IsPadding).Sum(f => PiecesOf(f.Length));
    }

    public string Folder { get; }

    public long PieceCount { get; }

    public long PiecesDone => Interlocked.Read(ref _piecesDone);

    public long BytesDone => Interlocked.Read(ref _bytesDone);

    public IReadOnlyList<TorrentFileCheck> Results => [.. _results.Select((r, i) => r.State == TorrentFileState.Waiting && Interlocked.Read(ref _done[i]) is > 0 and var done ? r with { State = TorrentFileState.Checking, DoneBytes = done } : r)];

    private bool UsesV1 => _info.Version != TorrentVersion.V2;

    public static string FolderFor(TorrentInfo info, string picked)
    {
        if (info.IsSingleFile || TorrentNames.Check(info.Name) != TorrentNameIssue.None)
        {
            return picked;
        }

        var named = Path.Join(picked, info.Name);
        return string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(picked)), info.Name, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(named)
            ? picked
            : Directory.Exists(named) && !IsLink(named) ? named : picked;
    }

    public void Run(Action report, CancellationToken token)
    {
        if (_info.PieceLength is <= 0 or > MaxPieceLength || (!UsesV1 && (_info.PieceLength < Block || !BitOperations.IsPow2(_info.PieceLength))))
        {
            throw new TorrentCheckException(Strings.TorrentCheck_BadPieceSize);
        }

        if (UsesV1)
        {
            RunV1(report, token);
        }
        else
        {
            RunV2(report, token);
        }
    }

    private void RunV1(Action report, CancellationToken token)
    {
        var files = _info.Files;
        var openers = new FileReader?[files.Count];
        var notes = new TorrentCheckNote[files.Count];
        var exists = new bool[files.Count];
        var right = new long[files.Count];
        var bad = new int[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            if (!files[i].IsPadding)
            {
                (openers[i], notes[i], exists[i]) = Open(files[i]);
                if (!exists[i] || notes[i] is TorrentCheckNote.StrangeName or TorrentCheckNote.Link or TorrentCheckNote.OnlineOnly)
                {
                    Finish(i, exists[i], 0, 0, notes[i]);
                }
            }
        }

        var hashes = _info.PieceHashesV1.Span;
        var pieceLength = (int)_info.PieceLength;
        var buffer = new byte[pieceLength];
        var file = 0;
        long inFile = 0;
        try
        {
            for (long piece = 0; piece < PieceCount; piece++)
            {
                token.ThrowIfCancellationRequested();

                var filled = 0;
                var readable = true;
                var touched = new List<(int File, long Bytes)>();
                while (filled < pieceLength && file < files.Count)
                {
                    var f = files[file];
                    var take = (int)Math.Min(pieceLength - filled, f.Length - inFile);
                    if (take > 0)
                    {
                        if (f.IsPadding)
                        {
                            Array.Clear(buffer, filled, take);
                        }
                        else
                        {
                            touched.Add((file, take));
                            if (readable && !(openers[file]?.Read(inFile, buffer.AsSpan(filled, take)) ?? false))
                            {
                                readable = false;
                            }
                        }

                        filled += take;
                        inFile += take;
                    }

                    if (inFile >= f.Length)
                    {
                        openers[file]?.Dispose();
                        openers[file] = null;
                        file++;
                        inFile = 0;
                    }
                }

                var ok = readable && filled > 0 && SHA1.HashData(buffer.AsSpan(0, filled)).AsSpan().SequenceEqual(hashes.Slice((int)(piece * 20), 20));
                foreach (var (index, bytes) in touched)
                {
                    if (ok)
                    {
                        right[index] += bytes;
                    }
                    else
                    {
                        bad[index]++;
                    }

                    Interlocked.Add(ref _bytesDone, bytes);
                    Interlocked.Add(ref _done[_resultOf[index]], bytes);
                }

                Interlocked.Increment(ref _piecesDone);

                foreach (var (index, _) in touched)
                {
                    if (index < file)
                    {
                        Finish(index, exists[index], right[index], bad[index], notes[index]);
                    }
                }

                report();
            }
        }
        finally
        {
            foreach (var opener in openers)
            {
                opener?.Dispose();
            }
        }

        for (var i = 0; i < files.Count; i++)
        {
            if (!files[i].IsPadding && _results[_resultOf[i]].State == TorrentFileState.Waiting)
            {
                Finish(i, exists[i], right[i], bad[i] + (files[i].Length > right[i] && bad[i] == 0 ? 1 : 0), notes[i]);
            }
        }
    }

    private void RunV2(Action report, CancellationToken token)
    {
        var pieceLength = (int)_info.PieceLength;
        var buffer = new byte[pieceLength];
        var leaves = new byte[pieceLength / Block][];
        for (var i = 0; i < _info.Files.Count; i++)
        {
            var f = _info.Files[i];
            if (f.IsPadding)
            {
                continue;
            }

            var (reader, note, exists) = Open(f);
            try
            {
                var pieces = PiecesOf(f.Length);
                long right = 0;
                var bad = 0;
                var layer = Layer(f, pieces);
                if (f.Length > 0 && layer is null)
                {
                    note = note == TorrentCheckNote.None ? TorrentCheckNote.NoHashes : note;
                    reader?.Dispose();
                    reader = null;
                }

                for (long piece = 0; piece < pieces; piece++)
                {
                    token.ThrowIfCancellationRequested();
                    var at = piece * pieceLength;
                    var length = (int)Math.Min(pieceLength, f.Length - at);
                    var ok = false;
                    if (reader is not null && reader.Read(at, buffer.AsSpan(0, length)))
                    {
                        var blocks = (length + Block - 1) / Block;
                        for (var b = 0; b < blocks; b++)
                        {
                            leaves[b] = SHA256.HashData(buffer.AsSpan(b * Block, Math.Min(Block, length - (b * Block))));
                        }

                        var width = pieces == 1 ? (int)BitOperations.RoundUpToPowerOf2((uint)blocks) : leaves.Length;
                        var hash = Root(leaves.AsSpan(0, blocks), width, new byte[32]);
                        ok = hash.AsSpan().SequenceEqual(layer!.Value.Span.Slice((int)(piece * 32), 32));
                    }

                    if (ok)
                    {
                        right += length;
                    }
                    else
                    {
                        bad++;
                    }

                    Interlocked.Add(ref _bytesDone, length);
                    Interlocked.Add(ref _done[_resultOf[i]], length);
                    Interlocked.Increment(ref _piecesDone);
                    report();
                }

                Finish(i, exists, right, bad, note);
            }
            finally
            {
                reader?.Dispose();
            }
        }
    }

    private ReadOnlyMemory<byte>? Layer(TorrentFile file, long pieces)
    {
        if (file.PiecesRoot is not { } root)
        {
            return null;
        }

        if (pieces <= 1)
        {
            return root;
        }

        if (!_info.PieceLayers.TryGetValue(TorrentInfo.Hex(root), out var layer) || layer.Length != pieces * 32)
        {
            return null;
        }

        var hashes = new byte[pieces][];
        for (var p = 0; p < pieces; p++)
        {
            hashes[p] = layer.Span.Slice(p * 32, 32).ToArray();
        }

        var zeros = new byte[32];
        for (var width = Block; width < _info.PieceLength; width *= 2)
        {
            zeros = SHA256.HashData([.. zeros, .. zeros]);
        }

        var width2 = (int)BitOperations.RoundUpToPowerOf2((ulong)pieces);
        return Root(hashes, width2, zeros).AsSpan().SequenceEqual(root) ? layer : (ReadOnlyMemory<byte>?)null;
    }

    private static byte[] Root(ReadOnlySpan<byte[]> hashes, int width, byte[] pad)
    {
        var level = new byte[width][];
        for (var i = 0; i < width; i++)
        {
            level[i] = i < hashes.Length ? hashes[i] : pad;
        }

        var pair = new byte[64];
        while (width > 1)
        {
            width /= 2;
            for (var i = 0; i < width; i++)
            {
                level[2 * i].CopyTo(pair, 0);
                level[(2 * i) + 1].CopyTo(pair, 32);
                level[i] = SHA256.HashData(pair);
            }
        }

        return level[0];
    }

    private long PiecesOf(long length) => _info.PieceLength > 0 ? (length + _info.PieceLength - 1) / _info.PieceLength : 0;

    private (FileReader? Reader, TorrentCheckNote Note, bool Exists) Open(TorrentFile file)
    {
        if (file.Issue != TorrentNameIssue.None)
        {
            return (null, TorrentCheckNote.StrangeName, false);
        }

        var path = Folder;
        for (var i = 0; i < file.Parts.Count - 1; i++)
        {
            path = Path.Join(path, file.Parts[i]);
            if (!_linkFolders.TryGetValue(path, out var link))
            {
                link = _linkFolders[path] = IsLink(path);
            }

            if (link)
            {
                return (null, TorrentCheckNote.Link, true);
            }
        }

        path = Path.Join(path, file.Parts[^1]);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return (null, TorrentCheckNote.None, false);
            }

            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget is not null)
            {
                return (null, TorrentCheckNote.Link, true);
            }

            if (((int)info.Attributes & (OfflineAttribute | RecallOnOpenAttribute | RecallOnDataAccessAttribute)) != 0)
            {
                return (null, TorrentCheckNote.OnlineOnly, true);
            }

            var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.SequentialScan,
                BufferSize = 0,
            });
            return (new FileReader(stream), stream.Length != file.Length ? TorrentCheckNote.SizeDiffers : TorrentCheckNote.None, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, TorrentCheckNote.Unreadable, true);
        }
    }

    private static bool IsLink(string folder)
    {
        try
        {
            var info = new DirectoryInfo(folder);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private void Finish(int index, bool exists, long right, int bad, TorrentCheckNote note)
    {
        var file = _info.Files[index];
        var state = note is TorrentCheckNote.StrangeName or TorrentCheckNote.Link or TorrentCheckNote.OnlineOnly ? TorrentFileState.Skipped
            : !exists ? TorrentFileState.Missing
            : bad == 0 && right >= file.Length && note == TorrentCheckNote.None ? TorrentFileState.Complete
            : TorrentFileState.Partly;
        Volatile.Write(ref _results[_resultOf[index]], new TorrentFileCheck(file, state, state is TorrentFileState.Skipped or TorrentFileState.Missing ? 0 : right, bad, note) { DoneBytes = file.Length });
    }

    private sealed class FileReader(FileStream stream) : IDisposable
    {
        public bool Read(long at, Span<byte> into)
        {
            try
            {
                if (stream.Position != at)
                {
                    stream.Position = at;
                }

                var done = 0;
                while (done < into.Length)
                {
                    var n = stream.Read(into[done..]);
                    if (n == 0)
                    {
                        return false;
                    }

                    done += n;
                }

                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Dispose() => stream.Dispose();
    }

    public static (int Complete, int Partly, int Missing, int Skipped, double Share) Count(IReadOnlyList<TorrentFileCheck> results)
    {
        var total = results.Sum(r => r.File.Length);
        var right = results.Sum(r => r.RightBytes);
        return (results.Count(r => r.State == TorrentFileState.Complete), results.Count(r => r.State == TorrentFileState.Partly),
            results.Count(r => r.State == TorrentFileState.Missing), results.Count(r => r.State == TorrentFileState.Skipped),
            total == 0 ? 1 : right / (double)total);
    }
}
