// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Diagnostics/StartupWarmup.cs

using System.Runtime.InteropServices;
using Salandual.Core.Resources;

namespace Salandual.App.Diagnostics;

internal static class StartupWarmup
{
    public static void Start()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_WARMUP") == "none")
        {
            return;
        }

        new Thread(Run) { IsBackground = true, Name = "Startup warm-up" }.Start();
    }

    private static void Run()
    {
        foreach (var name in new[] { "libSkiaSharp.dll", "av_libglesv2.dll", "libHarfBuzzSharp.dll", "dcomp.dll" })
        {
            var local = Path.Combine(AppContext.BaseDirectory, name);
            NativeLibrary.TryLoad(File.Exists(local) ? local : name, out _);
        }

        WarmText();
        StartupTrace.Mark("warmup-done");
    }

    private static void WarmText()
    {
        try
        {
            var manager = SkiaSharp.SKFontManager.Default;
            using var typeface = manager.MatchFamily("Segoe UI Variable Text") ?? manager.MatchFamily("Segoe UI") ?? SkiaSharp.SKTypeface.Default;
            using var font = new SkiaSharp.SKFont(typeface, 13);
            _ = font.MeasureText(string.Join(" ", Strings.MenuSub_Left, Strings.MenuSub_Files, Strings.MenuSub_Edit, Strings.MenuSub_Commands, Strings.MenuSub_Tools, Strings.MenuSub_Options, Strings.MenuSub_Right, Strings.MenuSub_Help).Replace("_", "", StringComparison.Ordinal));

            using var stream = typeface.OpenStream(out var index);
            if (stream is null || stream.Length <= 0)
            {
                return;
            }

            var data = new byte[stream.Length];
            stream.Read(data, data.Length);
            using var blob = HarfBuzzSharp.Blob.FromStream(new MemoryStream(data, writable: false));
            using var face = new HarfBuzzSharp.Face(blob, index);
            using var hbFont = new HarfBuzzSharp.Font(face);
            using var buffer = new HarfBuzzSharp.Buffer();
            buffer.AddUtf16("Nyaralás 2026 · 3 KB");
            buffer.GuessSegmentProperties();
            hbFont.Shape(buffer);
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException or IOException)
        {
        }
    }
}
