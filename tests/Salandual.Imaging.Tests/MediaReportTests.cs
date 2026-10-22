// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Imaging.Tests/MediaReportTests.cs

using System.Globalization;
using System.Text;

namespace Salandual.Imaging.Tests;

public sealed class MediaReportTests
{
    private static string? Samples
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Salandual.slnx")))
                {
                    var samples = Path.Combine(dir.FullName, "_samples", "media");
                    return Directory.Exists(samples) ? samples : null;
                }
            }

            return null;
        }
    }

    internal static byte[] Wave(string title, string comment)
    {
        static byte[] Chunk(string id, byte[] data)
        {
            var padded = data.Length % 2 == 1 ? [.. data, 0] : data;
            return [.. Encoding.ASCII.GetBytes(id), .. BitConverter.GetBytes(data.Length), .. padded];
        }

        static byte[] Text(string s) => [.. Encoding.ASCII.GetBytes(s), 0];
        const int rate = 44100;
        byte[] format = [.. BitConverter.GetBytes((short)1), .. BitConverter.GetBytes((short)2), .. BitConverter.GetBytes(rate), .. BitConverter.GetBytes(rate * 4), .. BitConverter.GetBytes((short)4), .. BitConverter.GetBytes((short)16)];
        byte[] info = [.. Encoding.ASCII.GetBytes("INFO"), .. Chunk("INAM", Text(title)), .. Chunk("ICMT", Text(comment))];
        byte[] body = [.. Encoding.ASCII.GetBytes("WAVE"), .. Chunk("fmt ", format), .. Chunk("LIST", info), .. Chunk("data", new byte[rate * 4])];
        return Chunk("RIFF", body);
    }

    [Fact]
    public void AWaveFile_GivesItsAudio_LengthAndTags()
    {
        var path = Path.Combine(Path.GetTempPath(), "salandual-media-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(path, Wave("Morning test", "Recorded for a test"));
        try
        {
            var report = MediaReport.Read(path);
            Assert.False(report.IsEmpty);
            Assert.Equal("Wave", report.Format);
            Assert.False(report.HasVideo);
            Assert.Equal(1, Math.Round(report.Duration!.Value.TotalSeconds));
            Assert.Equal("Morning test", report.Title);
            var audio = Assert.Single(report.Cards, c => c.Title == "Audio");
            var codec = audio.Rows.Single(r => r.Name == "Codec").Value;
            Assert.Contains("PCM", codec, StringComparison.Ordinal);
            Assert.Contains("stereo", codec, StringComparison.Ordinal);
            Assert.Contains(44.1.ToString("0.#", CultureInfo.CurrentCulture) + " kHz", codec, StringComparison.Ordinal);
            var tags = report.Cards.Single(c => c.Title == "Tags").Rows.ToDictionary(r => r.Name, r => r.Value);
            Assert.Equal("Recorded for a test", tags["Comment"]);
            Assert.Contains("Wave", report.Text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFileMediaInfoDoesNotKnow_GivesAnEmptyReport()
    {
        var path = Path.Combine(Path.GetTempPath(), "salandual-media-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllText(path, "not a video at all, just some words");
        try
        {
            Assert.True(MediaReport.Read(path).IsEmpty);
            Assert.True(MediaReport.Read(path + ".missing").IsEmpty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SampleVideos_GiveFormatCodecAndLength()
    {
        if (Samples is not { } samples)
        {
            Assert.Skip("The local _samples folder is not here.");
            return;
        }

        var mp4 = MediaReport.Read(Path.Combine(samples, "Big_Buck_Bunny_360_10s_1MB.mp4"));
        Assert.Equal("MP4", mp4.Format);
        Assert.True(mp4.HasVideo);
        Assert.Equal("H.265", mp4.Codecs[0]);
        Assert.Equal(10, Math.Round(mp4.Duration!.Value.TotalSeconds));
        var video = mp4.Cards.Single(c => c.Title == "Video").Rows.ToDictionary(r => r.Name, r => r.Value);
        Assert.StartsWith("H.265 / HEVC", video["Codec"], StringComparison.Ordinal);
        Assert.StartsWith("640 × 360", video["Size"], StringComparison.Ordinal);

        var mkv = MediaReport.Read(Path.Combine(samples, "Big_Buck_Bunny_360_10s_1MB.mkv"));
        Assert.Equal("MP4", mkv.Format);
        var webm = MediaReport.Read(Path.Combine(samples, "Big_Buck_Bunny_360_10s_1MB.webm"));
        Assert.Equal("VP9", webm.Codecs[0]);
    }

    [Fact]
    public void AnHdrFilm_WithTwoAudioTracksSubtitlesAndChapters_ReadsLikeTheMockup()
    {
        var f = new Fields
        {
            [(MediaReport.StreamKind.General, 0)] = new() { ["Format"] = "MPEG-4", ["Duration"] = "5530000.000", ["OverallBitRate"] = "6210000", ["Title"] = "Summer 2026", ["Recorded_Date"] = "2026-08-14 18:25:31 UTC", ["Encoded_Application"] = "HandBrake 1.9.0" },
            [(MediaReport.StreamKind.Video, 0)] = new() { ["Format"] = "HEVC", ["Format_Profile"] = "Main 10@L5.1@High", ["Width"] = "3840", ["Height"] = "2160", ["DisplayAspectRatio/String"] = "16:9", ["FrameRate"] = "23.976", ["BitRate"] = "5800000", ["HDR_Format"] = "SMPTE ST 2086", ["HDR_Format_Compatibility"] = "HDR10" },
            [(MediaReport.StreamKind.Audio, 0)] = new() { ["Format"] = "E-AC-3", ["Channel(s)"] = "6", ["SamplingRate"] = "48000", ["BitRate"] = "640000", ["Language"] = "hu", ["Default"] = "Yes" },
            [(MediaReport.StreamKind.Audio, 1)] = new() { ["Format"] = "AAC", ["Format_AdditionalFeatures"] = "LC", ["Channel(s)"] = "2", ["SamplingRate"] = "48000", ["BitRate"] = "192000", ["Language"] = "en", ["Default"] = "No" },
            [(MediaReport.StreamKind.Text, 0)] = new() { ["Format"] = "UTF-8", ["Language"] = "hu" },
            [(MediaReport.StreamKind.Text, 1)] = new() { ["Format"] = "UTF-8", ["Language"] = "en" },
            [(MediaReport.StreamKind.Menu, 0)] = new() { ["Chapters_Pos_Begin"] = "100", ["Chapters_Pos_End"] = "124" },
        };
        var report = MediaReport.From(f, ".mp4");
        var hu = CultureInfo.GetCultureInfo("hu").DisplayName;
        var en = CultureInfo.GetCultureInfo("en").DisplayName;

        Assert.Equal("MP4", report.Format);
        Assert.Equal("Summer 2026", report.Title);
        Assert.Equal("1 h 32 min 10 s", MediaReport.LongDuration(report.Duration!.Value));
        Assert.Equal("1:32:10", MediaReport.ShortDuration(report.Duration.Value));
        Assert.Equal(["H.265", "E-AC-3"], report.Codecs);
        Assert.Equal(["Video", "Audio 1", "Audio 2", "Subtitles and chapters", "Tags"], report.Cards.Select(c => c.Title));
        Assert.Equal([MediaCardKind.Video, MediaCardKind.Audio, MediaCardKind.Audio, MediaCardKind.SubtitlesAndChapters, MediaCardKind.Tags], report.Cards.Select(c => c.Kind));

        var video = report.Cards[0];
        Assert.Equal(["HDR10"], video.Tags);
        var v = video.Rows.ToDictionary(r => r.Name, r => r.Value);
        Assert.Equal("H.265 / HEVC Main 10", v["Codec"]);
        Assert.Equal("3840 × 2160 · 16:9", v["Size"]);
        Assert.Equal(Fps(23.976m), v["Frame rate"]);
        Assert.Equal(MediaReport.BitRateText(5_800_000), v["Bit rate"]);

        Assert.Equal([hu, "default"], report.Cards[1].Tags);
        Assert.Equal($"E-AC-3 · 5.1 · 48 kHz", report.Cards[1].Rows[0].Value);
        Assert.Equal([en], report.Cards[2].Tags);
        Assert.Equal("AAC LC · stereo · 48 kHz", report.Cards[2].Rows[0].Value);

        var extra = report.Cards[3].Rows.ToDictionary(r => r.Name, r => r.Value);
        Assert.Equal($"{hu}, {en} (SRT)", extra["Subtitles"]);
        Assert.Equal("24", extra["Chapters"]);
        var tags = report.Cards[4].Rows.ToDictionary(r => r.Name, r => r.Value);
        Assert.Equal("2026-08-14 18:25", tags["Recorded"]);
        Assert.Equal("HandBrake 1.9.0", tags["Encoder"]);

        static string Fps(decimal n) => n.ToString("0.###", CultureInfo.CurrentCulture) + " fps";
    }

    [Theory]
    [InlineData("SMPTE ST 2094 App 4", "HDR10+ Profile A", "PQ", "HDR10+")]
    [InlineData("Dolby Vision / SMPTE ST 2086", "dvhe.08 / HDR10", "PQ", "Dolby Vision")]
    [InlineData("", "", "PQ", "HDR10")]
    [InlineData("", "", "HLG", "HLG")]
    [InlineData("", "", "BT.709", null)]
    public void Hdr_IsNamedByItsBestKnownName(string format, string compatibility, string transfer, string? expected)
        => Assert.Equal(expected, MediaReport.Hdr(format, compatibility, transfer));

    [Fact]
    public void Wording_ForLengthsChannelsAndContainers()
    {
        Assert.Equal("3 min 5 s", MediaReport.LongDuration(TimeSpan.FromSeconds(185)));
        Assert.Equal("42 s", MediaReport.LongDuration(TimeSpan.FromSeconds(42.2)));
        Assert.Equal("3:05", MediaReport.ShortDuration(TimeSpan.FromSeconds(185)));
        Assert.Equal("mono", MediaReport.Channels("1"));
        Assert.Equal("7.1", MediaReport.Channels("8"));
        Assert.Equal("3 channels", MediaReport.Channels("3"));
        Assert.Equal("MP3", MediaReport.ContainerName("MPEG Audio", ".mp3"));
        Assert.Equal("MOV", MediaReport.ContainerName("MPEG-4", ".mov"));
        Assert.Equal("Matroska", MediaReport.ContainerName("Matroska", ".mkv"));
        Assert.Equal("2026-08-14 18:25", MediaReport.Date("UTC 2026-08-14 18:25:31"));
    }

    private sealed class Fields : MediaReport.IFields
    {
        private readonly Dictionary<(MediaReport.StreamKind, int), Dictionary<string, string>> _streams = [];

        public Dictionary<string, string> this[(MediaReport.StreamKind Kind, int Number) stream]
        {
            set => _streams[stream] = value;
        }

        public int Count(MediaReport.StreamKind kind) => _streams.Keys.Count(k => k.Item1 == kind);

        public string Get(MediaReport.StreamKind kind, int number, string parameter)
            => _streams.TryGetValue((kind, number), out var values) && values.TryGetValue(parameter, out var v) ? v : "";

        public string Report() => "General\nFormat : MPEG-4\n";
    }
}
