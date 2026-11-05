// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/TorrentFacts.cs

using System.Globalization;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Viewer;
using Salandual.Core.Vfs;

namespace Salandual.App.Panels;

internal sealed record TorrentSummary(string Name, long TotalSize, int FileCount, TorrentVersion Version, long PieceCount, long PieceLength, IReadOnlyList<string> Hosts, DateTimeOffset? CreatedOn, bool IsPrivate)
{
    public static TorrentSummary From(TorrentInfo t)
        => new(t.Name, t.TotalSize, t.FileCount, t.Version, t.PieceCount, t.PieceLength, t.TrackerHosts(), t.CreatedOn, t.IsPrivate);
}

internal static class TorrentFacts
{
    private const int MaxKept = 64;

    private static readonly Dictionary<string, Entry> Kept = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> Order = new();
    private static readonly Lock Gate = new();

    public static bool MayRead(string path, in FileEntry entry)
        => !entry.IsDirectory && entry.Name.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && entry.Size > 0 && entry.Size <= TorrentInfo.MaxBytes
            && Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\?\", StringComparison.Ordinal) && !ThumbnailCache.IsOnlineOnly(entry) && (entry.Attributes & FileAttributes.Offline) == 0;

    public static TorrentSummary? Get(string path, in FileEntry entry, Action ready)
    {
        var size = entry.Size;
        var ticks = entry.LastWriteUtcTicks;
        Entry fresh;
        lock (Gate)
        {
            if (Kept.TryGetValue(path, out var kept) && kept.Size == size && kept.Ticks == ticks)
            {
                if (!kept.Done)
                {
                    kept.Waiting.Add(ready);
                }

                return kept.Summary;
            }

            fresh = Kept[path] = new Entry(size, ticks);
            fresh.Waiting.Add(ready);
            Order.Remove(path);
            Order.AddFirst(path);
            while (Order.Count > MaxKept)
            {
                Kept.Remove(Order.Last!.Value);
                Order.RemoveLast();
            }
        }

        _ = Task.Run(() =>
        {
            TorrentSummary? summary = null;
            try
            {
                summary = TorrentSummary.From(TorrentInfo.ReadFile(path));
            }
            catch (Exception e) when (e is TorrentFileException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
            }

            List<Action> waiting;
            lock (Gate)
            {
                fresh.Summary = summary;
                fresh.Done = true;
                waiting = [.. fresh.Waiting];
                fresh.Waiting.Clear();
            }

            Dispatcher.UIThread.Post(() => waiting.ForEach(w => w()));
        });
        return null;
    }

    public static string InfoText(TorrentSummary s, DisplayFormat format)
        => Loc.Format(AppStrings.Torrent_InfoLine, format.Size(s.TotalSize), Loc.Plural(s.FileCount, AppStrings.Torrent_Files_One, AppStrings.Torrent_Files_Other));

    public static string[] Lines(TorrentSummary s, DisplayFormat format)
    {
        var version = s.Version switch { TorrentVersion.V2 => AppStrings.Torrent_V2, TorrentVersion.Hybrid => AppStrings.Torrent_Hybrid, _ => AppStrings.Torrent_V1 };
        var lines = new List<string>
        {
            s.Name.Length > 0 ? TorrentNames.Shown(s.Name) : AppStrings.Torrent_NoName,
            Loc.Format(AppStrings.Torrent_Subtitle, format.Size(s.TotalSize), Loc.Plural(s.FileCount, AppStrings.Torrent_Files_One, AppStrings.Torrent_Files_Other), version),
        };
        if (s.PieceCount > 0)
        {
            lines.Add(AppStrings.Torrent_Pieces + ": " + Loc.Format(AppStrings.Torrent_PiecesValue, s.PieceCount.ToString("N0", CultureInfo.CurrentCulture), format.Size(s.PieceLength)));
        }

        lines.Add(Loc.Format(AppStrings.Torrent_TrackersLine, s.Hosts.Count == 0 ? AppStrings.Torrent_None : string.Join(", ", s.Hosts.Take(5)) + (s.Hosts.Count > 5 ? ", …" : "")));
        if (s.CreatedOn is { } on)
        {
            lines.Add(AppStrings.Torrent_MadeOn + ": " + format.Date(on.LocalDateTime));
        }

        lines.Add(AppStrings.Torrent_Private + ": " + (s.IsPrivate ? AppStrings.Torrent_Yes : AppStrings.Torrent_No));
        return [.. lines];
    }

    public static bool IsRead(string path, in FileEntry entry)
    {
        lock (Gate)
        {
            return Kept.TryGetValue(path, out var kept) && kept.Size == entry.Size && kept.Ticks == entry.LastWriteUtcTicks && kept.Done;
        }
    }

    private sealed class Entry(long size, long ticks)
    {
        public long Size { get; } = size;

        public long Ticks { get; } = ticks;

        public TorrentSummary? Summary { get; set; }

        public bool Done { get; set; }

        public List<Action> Waiting { get; } = [];
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            Kept.Clear();
            Order.Clear();
        }
    }
}
