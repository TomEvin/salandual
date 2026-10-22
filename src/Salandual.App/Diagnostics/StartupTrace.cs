// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Diagnostics/StartupTrace.cs

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Salandual.App.Diagnostics;

internal static partial class StartupTrace
{
    private static readonly long _created = ProcessCreationTime();
    private static readonly List<(string Stage, double Ms)> _marks = [];
    private static readonly Lock _lock = new();

    public static bool IsEnabled { get; } = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SALANDUAL_STARTUP_LOG"));

    public static bool ExitAfterFirstFrame { get; } = Environment.GetEnvironmentVariable("SALANDUAL_STARTUP_EXIT") == "1";

    public static void Mark(string stage)
    {
        if (!IsEnabled)
        {
            return;
        }

        GetSystemTimePreciseAsFileTime(out var now);
        lock (_lock)
        {
            _marks.Add((stage, (now - _created) / 10_000.0));
        }
    }

    public static void Write()
    {
        if (!IsEnabled)
        {
            return;
        }

        var sb = new StringBuilder();
        lock (_lock)
        {
            foreach (var (stage, ms) in _marks)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{stage}={ms:F1};");
            }
        }

        File.AppendAllText(Environment.GetEnvironmentVariable("SALANDUAL_STARTUP_LOG")!, sb.Append('\n').ToString());
    }

    public static void DecodeSmokeImages()
    {
        if (!IsEnabled || Environment.GetEnvironmentVariable("SALANDUAL_SMOKE_IMAGES") is not { Length: > 0 } folder || !Directory.Exists(folder))
        {
            return;
        }

        var sb = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Path.GetExtension(file).ToLowerInvariant() is ".wav" or ".mp4" or ".mkv" or ".mp3" or ".flac" or ".ogg")
                {
                    var media = Salandual.Imaging.MediaReport.Read(file);
                    sb.Append(CultureInfo.InvariantCulture, $"media {Path.GetFileName(file)} {media.Format} cards={media.Cards.Count}{(media.IsEmpty ? " EMPTY" : "")}\n");
                    continue;
                }

                var image = Salandual.Imaging.ImageDecoder.Decode(file, 800, 600);
                using var bitmap = Viewer.ImageView.ToBitmap(image);
                var tags = Salandual.Imaging.PhotoInfo.Read(file).Groups.TryGetValue(Salandual.Imaging.InfoGroup.All, out var all) ? all.Count : 0;
                sb.Append(CultureInfo.InvariantCulture, $"decoded {Path.GetFileName(file)} {image.Width}x{image.Height} {image.Decoder} tags={tags}\n");
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                sb.Append(CultureInfo.InvariantCulture, $"FAILED {Path.GetFileName(file)} {e.GetType().Name}: {e.Message.Split('\n')[0]}\n");
            }
        }

        File.AppendAllText(Environment.GetEnvironmentVariable("SALANDUAL_STARTUP_LOG")!, sb.ToString());
    }

    public static void Append(string text)
    {
        if (IsEnabled)
        {
            File.AppendAllText(Environment.GetEnvironmentVariable("SALANDUAL_STARTUP_LOG")!, text);
        }
    }

    private static long ProcessCreationTime()
    {
        GetProcessTimes(GetCurrentProcess(), out var creation, out _, out _, out _);
        return creation;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll")]
    private static partial void GetSystemTimePreciseAsFileTime(out long fileTime);
}
