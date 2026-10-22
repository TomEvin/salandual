// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/TextEncodings.cs

using System.Globalization;
using System.Text;

namespace Salandual.Core.Viewer;

public sealed record TextEncoding(string Id, string Name, Encoding Encoding)
{
    public int Unit => Id is TextEncodings.Utf16LE or TextEncodings.Utf16BE ? 2 : 1;

    public bool IsUtf8 => Id == TextEncodings.Utf8;

    public bool BigEndian => Id == TextEncodings.Utf16BE;

    public bool SingleByte => !IsUtf8 && Unit == 1;
}

public sealed record EncodingGuess(TextEncoding Encoding, int BomLength, bool IsBinary);

public static class TextEncodings
{
    public const string Utf8 = "utf-8";
    public const string Utf16LE = "utf-16le";
    public const string Utf16BE = "utf-16be";

    public const int SampleSize = 64 * 1024;

    private static readonly Lazy<IReadOnlyList<TextEncoding>> _all = new(Build);

    public static IReadOnlyList<TextEncoding> All => _all.Value;

    public static TextEncoding Default => All[0];

    public static TextEncoding Ansi => Find("cp" + CultureInfo.CurrentCulture.TextInfo.ANSICodePage) ?? Find("cp1252")!;

    public static TextEncoding? Find(string? id) => id is null ? null : All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    public static TextEncoding Step(TextEncoding current, int direction)
    {
        var i = All.ToList().FindIndex(e => e.Id == current.Id);
        return All[(((i < 0 ? 0 : i) + direction) % All.Count + All.Count) % All.Count];
    }

    public static EncodingGuess Detect(ReadOnlySpan<byte> start)
    {
        if (start.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return new(Find(Utf8)!, 3, false);
        }

        if (start.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return new(Find(Utf16LE)!, 2, false);
        }

        if (start.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return new(Find(Utf16BE)!, 2, false);
        }

        if (start.Length >= 4 && LooksLikeUtf16(start, out var bigEndian))
        {
            return new(Find(bigEndian ? Utf16BE : Utf16LE)!, 0, false);
        }

        var binary = LooksBinary(start);
        if (!binary && IsUtf8WithNonAscii(start))
        {
            return new(Find(Utf8)!, 0, false);
        }

        return new(binary ? Ansi : (Ascii(start) ? Find(Utf8)! : Ansi), 0, binary);
    }

    public static string? LineEnding(ReadOnlySpan<byte> start, TextEncoding encoding)
    {
        var step = encoding.Unit;
        var low = encoding.BigEndian ? 1 : 0;
        for (var i = low; i + step - 1 < start.Length; i += step)
        {
            if (encoding.Unit == 2 && start[i + (encoding.BigEndian ? -1 : 1)] != 0)
            {
                continue;
            }

            if (start[i] == '\n')
            {
                return "LF";
            }

            if (start[i] == '\r')
            {
                return i + step < start.Length && start[i + step] == '\n' ? "CRLF" : "CR";
            }
        }

        return null;
    }

    private static bool LooksLikeUtf16(ReadOnlySpan<byte> s, out bool bigEndian)
    {
        int evenZeros = 0, oddZeros = 0;
        var n = s.Length & ~1;
        for (var i = 0; i < n; i += 2)
        {
            evenZeros += s[i] == 0 ? 1 : 0;
            oddZeros += s[i + 1] == 0 ? 1 : 0;
        }

        var pairs = n / 2;
        bigEndian = evenZeros > oddZeros;
        var (zeros, other) = bigEndian ? (evenZeros, oddZeros) : (oddZeros, evenZeros);
        return zeros > pairs * 0.4 && other < pairs * 0.05;
    }

    private static bool LooksBinary(ReadOnlySpan<byte> s)
    {
        var control = 0;
        foreach (var b in s)
        {
            if (b == 0)
            {
                return true;
            }

            if (b < 0x20 && b is not ((byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0C or 0x1B or 0x08))
            {
                control++;
            }
        }

        return control > s.Length / 20 + 1;
    }

    private static bool Ascii(ReadOnlySpan<byte> s) => !s.ContainsAnyExceptInRange((byte)0, (byte)0x7F);

    private static bool IsUtf8WithNonAscii(ReadOnlySpan<byte> s)
    {
        var nonAscii = false;
        var i = 0;
        while (i < s.Length)
        {
            var b = s[i];
            if (b < 0x80)
            {
                i++;
                continue;
            }

            var length = b switch
            {
                >= 0xC2 and <= 0xDF => 2,
                >= 0xE0 and <= 0xEF => 3,
                >= 0xF0 and <= 0xF4 => 4,
                _ => 0,
            };
            if (length == 0)
            {
                return false;
            }

            for (var k = 1; k < length; k++)
            {
                if (i + k >= s.Length)
                {
                    return nonAscii || k > 1;
                }

                if ((s[i + k] & 0xC0) != 0x80)
                {
                    return false;
                }
            }

            nonAscii = true;
            i += length;
        }

        return nonAscii;
    }

    private static List<TextEncoding> Build()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var list = new List<TextEncoding>
        {
            new(Utf8, "UTF-8", new UTF8Encoding(false)),
            new(Utf16LE, "UTF-16 LE", new UnicodeEncoding(false, false)),
            new(Utf16BE, "UTF-16 BE", new UnicodeEncoding(true, false)),
        };

        var info = CultureInfo.CurrentCulture.TextInfo;
        var pages = new List<int> { info.ANSICodePage, info.OEMCodePage, 1250, 1252, 1251, 1253, 1254, 1257, 28592, 28591, 28605, 852, 437, 850, 866, 20866 };
        foreach (var page in pages.Distinct())
        {
            try
            {
                var e = Encoding.GetEncoding(page, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
                if (e.IsSingleByte)
                {
                    list.Add(new("cp" + page, Name(page, e), e));
                }
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return list;
    }

    private static string Name(int page, Encoding e) => page switch
    {
        >= 1250 and <= 1258 => "Windows-" + page,
        28591 => "ISO-8859-1",
        28592 => "ISO-8859-2",
        28605 => "ISO-8859-15",
        20866 => "KOI8-R",
        437 or 850 or 852 or 866 => $"CP{page} (DOS)",
        _ => e.WebName.ToUpperInvariant(),
    };
}
