// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Imaging/MediaReport.cs

using System.Globalization;
using System.Runtime.InteropServices;
using Salandual.Core.Resources;

namespace Salandual.Imaging;

public enum MediaCardKind { Video, Audio, SubtitlesAndChapters, Tags }

public sealed record MediaCard(MediaCardKind Kind, string Title, IReadOnlyList<string> Tags, IReadOnlyList<InfoRow> Rows);

public sealed class MediaInfoUnavailableException(string message, Exception inner) : Exception(message, inner);

public sealed partial class MediaReport
{
    private MediaReport()
    {
    }

    public string? Title { get; private init; }

    public string Format { get; private init; } = "";

    public TimeSpan? Duration { get; private init; }

    public long? BitRate { get; private init; }

    public IReadOnlyList<MediaCard> Cards { get; private init; } = [];

    public IReadOnlyList<string> Codecs { get; private init; } = [];

    public bool HasVideo { get; private init; }

    public byte[]? Cover { get; private init; }

    public string Text { get; private init; } = "";

    public bool IsEmpty => Cards.Count == 0 && Duration is null && Format.Length == 0;

    public static MediaReport Read(string path)
    {
        nint handle;
        try
        {
            handle = Native.MediaInfo_New();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new MediaInfoUnavailableException(e.Message, e);
        }

        try
        {
            Native.Option(handle, "Internet", "No");
            Native.Option(handle, "Cover_Data", "base64");
            if (Native.MediaInfo_Open(handle, path) == 0)
            {
                return new MediaReport();
            }

            return From(new Reader(handle), Path.GetExtension(path));
        }
        finally
        {
            Native.MediaInfo_Close(handle);
            Native.MediaInfo_Delete(handle);
        }
    }

    internal interface IFields
    {
        int Count(StreamKind kind);

        string Get(StreamKind kind, int number, string parameter);

        string Report();
    }

    internal enum StreamKind { General, Video, Audio, Text, Other, Image, Menu }

    internal static MediaReport From(IFields f, string extension)
    {
        string G(string p) => f.Get(StreamKind.General, 0, p);
        var cards = new List<MediaCard>();
        var codecs = new List<string>();

        var videos = f.Count(StreamKind.Video);
        for (var i = 0; i < videos; i++)
        {
            string V(string p) => f.Get(StreamKind.Video, i, p);
            var rows = new List<InfoRow>();
            var codec = VideoCodec(V("Format"));
            Add(rows, Strings.Media_Codec, Join(" ", codec, Profile(V("Format_Profile"))));
            if (Number(V("Width")) is { } w && Number(V("Height")) is { } h)
            {
                Add(rows, Strings.Media_Size, Join(" · ", Loc.Format(Strings.Media_Pixels, w, h), V("DisplayAspectRatio/String")));
            }

            if (Decimal(V("FrameRate")) is { } fps)
            {
                var rate = Loc.Format(Strings.Media_Fps, fps.ToString("0.###", CultureInfo.CurrentCulture));
                Add(rows, Strings.Media_FrameRate, V("FrameRate_Mode") == "VFR" ? Loc.Format(Strings.Media_Variable, rate) : rate);
            }

            Add(rows, Strings.Media_BitRate, Rate(V("BitRate")));
            if (Number(V("BitDepth")) is { } depth)
            {
                Add(rows, Strings.Media_BitDepth, Loc.Format(Strings.Media_Bits, depth));
            }

            Add(rows, Strings.Media_Hdr, V("HDR_Format/String"));
            Add(rows, Strings.Media_StreamTitle, V("Title"));
            var tags = new List<string>();
            AddTag(tags, Hdr(V("HDR_Format"), V("HDR_Format_Compatibility"), V("transfer_characteristics")));
            AddTag(tags, Language(V("Language"), V("Language/String")));
            cards.Add(new MediaCard(MediaCardKind.Video, videos == 1 ? Strings.Media_Video : Loc.Format(Strings.Media_VideoNumber, i + 1), tags, rows));
            if (i == 0)
            {
                codecs.Add(Short(codec));
            }
        }

        var audios = f.Count(StreamKind.Audio);
        for (var i = 0; i < audios; i++)
        {
            string A(string p) => f.Get(StreamKind.Audio, i, p);
            var rows = new List<InfoRow>();
            var codec = Join(" ", A("Format"), A("Format_AdditionalFeatures"));
            var hz = Number(A("SamplingRate")) is { } rate ? Loc.Format(Strings.Media_KHz, (rate / 1000.0).ToString("0.#", CultureInfo.CurrentCulture)) : "";
            Add(rows, Strings.Media_Codec, Join(" · ", codec, Channels(A("Channel(s)")), hz));
            Add(rows, Strings.Media_BitRate, Rate(A("BitRate")));
            if (Number(A("BitDepth")) is { } depth)
            {
                Add(rows, Strings.Media_BitDepth, Loc.Format(Strings.Media_Bits, depth));
            }

            Add(rows, Strings.Media_StreamTitle, A("Title"));
            var tags = new List<string>();
            AddTag(tags, Language(A("Language"), A("Language/String")));
            if (audios > 1 && A("Default") == "Yes")
            {
                tags.Add(Strings.Media_Default);
            }

            cards.Add(new MediaCard(MediaCardKind.Audio, audios == 1 ? Strings.Media_Audio : Loc.Format(Strings.Media_AudioNumber, i + 1), tags, rows));
            if (i == 0)
            {
                codecs.Add(A("Format"));
            }
        }

        var subtitles = Enumerable.Range(0, f.Count(StreamKind.Text))
            .Select(i => (Language: Language(f.Get(StreamKind.Text, i, "Language"), f.Get(StreamKind.Text, i, "Language/String")) ?? Strings.Media_NoLanguage, Format: SubtitleFormat(f.Get(StreamKind.Text, i, "Format"))))
            .GroupBy(s => s.Format)
            .Select(g => g.Key.Length == 0 ? string.Join(", ", g.Select(s => s.Language)) : Loc.Format(Strings.Media_SubtitleGroup, string.Join(", ", g.Select(s => s.Language)), g.Key))
            .ToList();
        var chapters = 0;
        for (var i = 0; i < f.Count(StreamKind.Menu); i++)
        {
            if (Number(f.Get(StreamKind.Menu, i, "Chapters_Pos_Begin")) is { } begin && Number(f.Get(StreamKind.Menu, i, "Chapters_Pos_End")) is { } end && end > begin)
            {
                chapters += (int)(end - begin);
            }
        }

        if (subtitles.Count > 0 || chapters > 0)
        {
            var rows = new List<InfoRow>();
            Add(rows, Strings.Media_Subtitles, string.Join("; ", subtitles));
            Add(rows, Strings.Media_Chapters, chapters > 0 ? chapters.ToString("N0", CultureInfo.CurrentCulture) : "");
            cards.Add(new MediaCard(MediaCardKind.SubtitlesAndChapters, subtitles.Count > 0 && chapters > 0 ? Strings.Media_SubtitlesAndChapters : subtitles.Count > 0 ? Strings.Media_Subtitles : Strings.Media_Chapters, [], rows));
        }

        var title = First(G("Title"), G("Movie"), G("Track"));
        var tagRows = new List<InfoRow>();
        Add(tagRows, Strings.Media_Title, title);
        Add(tagRows, Strings.Media_Artist, First(G("Performer"), G("Album_Performer")));
        Add(tagRows, Strings.Media_Album, G("Album"));
        Add(tagRows, Strings.Media_TrackNumber, Number(G("Track/Position")) is { } track ? (Number(G("Track/Position_Total")) is { } total ? Loc.Format(Strings.Media_OfTotal, track, total) : track.ToString(CultureInfo.CurrentCulture)) : "");
        Add(tagRows, Strings.Media_Genre, G("Genre"));
        Add(tagRows, Strings.Media_Recorded, Date(First(G("Recorded_Date"), G("Encoded_Date"), G("Tagged_Date"))));
        Add(tagRows, Strings.Media_Encoder, First(G("Encoded_Application/String"), G("Encoded_Application"), G("Encoded_Library/String")));
        Add(tagRows, Strings.Media_Comment, G("Comment"));
        Add(tagRows, Strings.Media_Copyright, G("Copyright"));
        if (tagRows.Count > 0)
        {
            cards.Add(new MediaCard(MediaCardKind.Tags, Strings.Media_Tags, [], tagRows));
        }

        byte[]? cover = null;
        if (G("Cover_Data") is { Length: > 0 } data)
        {
            try
            {
                cover = Convert.FromBase64String(data.Split(" / ")[0]);
            }
            catch (FormatException)
            {
            }
        }

        return new MediaReport
        {
            Title = title.Length > 0 ? title : null,
            Format = ContainerName(G("Format"), extension),
            Duration = Decimal(G("Duration")) is { } ms && ms > 0 ? TimeSpan.FromMilliseconds((double)ms) : null,
            BitRate = Number(G("OverallBitRate")) is { } bps && bps > 0 ? bps : null,
            Cards = cards,
            Codecs = [.. codecs.Where(c => c.Length > 0)],
            HasVideo = videos > 0,
            Cover = cover,
            Text = f.Report().Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).TrimEnd() + "\n",
        };
    }

    public static string LongDuration(TimeSpan t)
    {
        var s = (long)Math.Round(t.TotalSeconds);
        var (h, m, sec) = (s / 3600, s / 60 % 60, s % 60);
        return h > 0 ? Loc.Format(Strings.Media_Hms, h, m, sec) : m > 0 ? Loc.Format(Strings.Media_Ms, m, sec) : Loc.Format(Strings.Media_S, sec);
    }

    public static string ShortDuration(TimeSpan t)
    {
        var s = (long)Math.Round(t.TotalSeconds);
        return s >= 3600 ? string.Create(CultureInfo.InvariantCulture, $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}") : string.Create(CultureInfo.InvariantCulture, $"{s / 60}:{s % 60:00}");
    }

    public static string BitRateText(long bitsPerSecond) => Loc.Format(Strings.Media_Kbps, Math.Round(bitsPerSecond / 1000.0).ToString("N0", CultureInfo.CurrentCulture));

    private static string Rate(string text) => Number(text) is { } bps && bps > 0 ? BitRateText(bps) : "";

    internal static string ContainerName(string format, string extension)
    {
        var type = extension.TrimStart('.').ToUpperInvariant();
        return format switch
        {
            "MPEG-4" => type is "MP4" or "M4A" or "M4V" or "M4B" or "MOV" or "3GP" ? type : "MP4",
            "MPEG Audio" when type == "MP3" => type,
            _ => format,
        };
    }

    internal static string VideoCodec(string format) => format switch
    {
        "AVC" => "H.264 / AVC",
        "HEVC" => "H.265 / HEVC",
        "VVC" => "H.266 / VVC",
        _ => format,
    };

    private static string Short(string codec) => codec.Split(" / ")[0];

    private static string Profile(string profile) => profile.Split('@')[0];

    internal static string Channels(string text) => Number(text.Split(" / ")[0]) switch
    {
        null => "",
        1 => Strings.Media_Mono,
        2 => Strings.Media_Stereo,
        6 => "5.1",
        8 => "7.1",
        { } n => Loc.Format(Strings.Media_ChannelCount, n),
    };

    internal static string? Hdr(string format, string compatibility, string transfer)
    {
        var all = format + " " + compatibility;
        return all.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase) ? "Dolby Vision"
            : all.Contains("HDR10+", StringComparison.OrdinalIgnoreCase) ? "HDR10+"
            : all.Contains("HDR10", StringComparison.OrdinalIgnoreCase) || transfer == "PQ" ? "HDR10"
            : transfer == "HLG" ? "HLG"
            : null;
    }

    internal static string? Language(string code, string name)
    {
        if (code.Length == 0)
        {
            return name.Length > 0 ? name : null;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            if (culture.ThreeLetterISOLanguageName.Length > 0 && !culture.DisplayName.StartsWith("Unknown", StringComparison.Ordinal))
            {
                return culture.DisplayName;
            }
        }
        catch (CultureNotFoundException)
        {
        }

        return name.Length > 0 ? name : code;
    }

    private static string SubtitleFormat(string format) => format switch
    {
        "UTF-8" or "SubRip" => "SRT",
        "Timed Text" => "TTXT",
        _ => format,
    };

    internal static string Date(string text)
    {
        text = text.Split(" / ")[0].Replace("UTC ", "", StringComparison.Ordinal).Replace(" UTC", "", StringComparison.Ordinal).Trim();
        return DateTime.TryParseExact(text, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : text;
    }

    private static long? Number(string text)
        => long.TryParse(text.Split(" / ")[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n
        : Decimal(text) is { } d ? (long)Math.Round(d) : null;

    private static decimal? Decimal(string text)
        => decimal.TryParse(text.Split(" / ")[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string First(params string[] values) => values.FirstOrDefault(v => v.Length > 0) ?? "";

    private static string Join(string separator, params string[] parts) => string.Join(separator, parts.Where(p => p.Length > 0));

    private static void Add(List<InfoRow> rows, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(new InfoRow(name, value.Trim()));
        }
    }

    private static void AddTag(List<string> tags, string? tag)
    {
        if (!string.IsNullOrEmpty(tag))
        {
            tags.Add(tag);
        }
    }

    private sealed class Reader(nint handle) : IFields
    {
        public int Count(StreamKind kind) => (int)Native.MediaInfo_Count_Get(handle, kind, nuint.MaxValue);

        public string Get(StreamKind kind, int number, string parameter)
            => Marshal.PtrToStringUni(Native.MediaInfo_Get(handle, kind, (nuint)number, parameter, InfoKind.Text, InfoKind.Name)) ?? "";

        public string Report() => Marshal.PtrToStringUni(Native.MediaInfo_Inform(handle, 0)) ?? "";
    }

    private enum InfoKind { Name, Text }

    private static partial class Native
    {
        private const string Dll = "MediaInfo.dll";

        public static void Option(nint handle, string option, string value) => _ = MediaInfo_Option(handle, option, value);

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [LibraryImport(Dll)]
        public static partial nint MediaInfo_New();

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [LibraryImport(Dll)]
        public static partial void MediaInfo_Delete(nint handle);

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
        public static partial nuint MediaInfo_Open(nint handle, string file);

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [LibraryImport(Dll)]
        public static partial void MediaInfo_Close(nint handle);

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [LibraryImport(Dll)]
        public static partial nint MediaInfo_Inform(nint handle, nuint reserved);

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
        public static partial nint MediaInfo_Get(nint handle, StreamKind kind, nuint number, string parameter, InfoKind info, InfoKind search);

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
        public static partial nint MediaInfo_Option(nint handle, string option, string value);

        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [LibraryImport(Dll)]
        public static partial nuint MediaInfo_Count_Get(nint handle, StreamKind kind, nuint number);
    }
}
