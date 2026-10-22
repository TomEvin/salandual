// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/EditorFile.cs

using System.Text;
using Salandual.Core.Settings;

namespace Salandual.Core.Viewer;

public sealed record EditorFile(string Text, TextEncoding Encoding, bool Bom, LineEnding? LineEnding, IndentStyle? Indent, long Size, DateTime WriteTimeUtc)
{
    public string EncodingName => Bom && Encoding.IsUtf8 ? Resources.Strings.Editor_Utf8Bom : Encoding.Name;
}

public enum DiskState { Same, Changed, Gone }

public static class EditorFiles
{
    public static EditorFile Load(string path, string? encodingId = null)
    {
        var info = new FileInfo(path);
        var bytes = File.ReadAllBytes(path);
        var guess = TextEncodings.Detect(bytes.AsSpan(0, Math.Min(bytes.Length, TextEncodings.SampleSize)));
        var encoding = TextEncodings.Find(encodingId) ?? guess.Encoding;
        var bomLength = encodingId is null || encoding.Id == guess.Encoding.Id ? guess.BomLength : 0;
        string text;
        if (encoding.IsUtf8 && encodingId is null)
        {
            try
            {
                text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, bomLength, bytes.Length - bomLength);
            }
            catch (DecoderFallbackException)
            {
                encoding = TextEncodings.Ansi;
                text = encoding.Encoding.GetString(bytes);
                bomLength = 0;
            }
        }
        else
        {
            text = encoding.Encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        }

        return new EditorFile(text, encoding, bomLength > 0, DetectLineEnding(text), DetectIndent(text), info.Length, info.LastWriteTimeUtc);
    }

    public static EditorFile New(EditorSettings settings)
    {
        var encoding = TextEncodings.Find(settings.NewEncoding) ?? TextEncodings.Default;
        return new("", encoding, encoding.Unit == 2, settings.NewLineEnding, null, 0, default);
    }

    public static LineEnding? DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0;
        for (var i = text.IndexOf('\n'); i >= 0; i = text.IndexOf('\n', i + 1))
        {
            if (i > 0 && text[i - 1] == '\r')
            {
                crlf++;
            }
            else
            {
                lf++;
            }
        }

        return (crlf, lf) switch
        {
            (_, 0) => LineEnding.CrLf,
            (0, _) => LineEnding.Lf,
            _ => null,
        };
    }

    public static IndentStyle? DetectIndent(string text)
    {
        int tabs = 0, two = 0, four = 0, lines = 0;
        var previous = 0;
        var start = 0;
        while (start < text.Length && lines < 2000)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
            {
                end = text.Length;
            }

            var spaces = 0;
            var i = start;
            while (i < end && text[i] == ' ')
            {
                spaces++;
                i++;
            }

            if (i < end && text[i] == '\t' && spaces == 0)
            {
                tabs++;
            }
            else if (i < end && text[i] is not ('\r' or '\n'))
            {
                var step = Math.Abs(spaces - previous);
                if (step == 2)
                {
                    two++;
                }
                else if (step == 4)
                {
                    four++;
                }

                previous = spaces;
            }

            lines++;
            start = end + 1;
        }

        if (tabs == 0 && two == 0 && four == 0)
        {
            return null;
        }

        return tabs > two + four ? IndentStyle.Tabs : two >= four ? IndentStyle.Spaces2 : IndentStyle.Spaces4;
    }

    public static string Prepare(string text, LineEnding? lineEnding, bool trim, FinalNewline final)
    {
        if (trim)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var cr = lines[i].EndsWith('\r');
                var body = (cr ? lines[i][..^1] : lines[i]).TrimEnd(' ', '\t');
                lines[i] = cr ? body + "\r" : body;
            }

            text = string.Join('\n', lines);
        }

        if (lineEnding is { } eol)
        {
            text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            if (eol == LineEnding.CrLf)
            {
                text = text.Replace("\n", "\r\n", StringComparison.Ordinal);
            }
        }

        if (final == FinalNewline.Always && text.Length > 0 && !text.EndsWith('\n'))
        {
            text += lineEnding == LineEnding.Lf || (lineEnding is null && !text.Contains("\r\n", StringComparison.Ordinal) && text.Contains('\n')) ? "\n" : "\r\n";
        }

        return text;
    }

    public static byte[] Encode(string text, TextEncoding encoding, bool bom)
    {
        var body = encoding.Encoding.GetBytes(text);
        if (!bom)
        {
            return body;
        }

        byte[] preamble = encoding.Id switch
        {
            TextEncodings.Utf8 => [0xEF, 0xBB, 0xBF],
            TextEncodings.Utf16LE => [0xFF, 0xFE],
            TextEncodings.Utf16BE => [0xFE, 0xFF],
            _ => [],
        };
        return [.. preamble, .. body];
    }

    public static int Unrepresentable(string text, TextEncoding encoding)
    {
        if (encoding.IsUtf8 || encoding.Unit == 2)
        {
            return 0;
        }

        var back = encoding.Encoding.GetString(encoding.Encoding.GetBytes(text));
        var count = 0;
        for (var i = 0; i < text.Length && i < back.Length; i++)
        {
            count += back[i] != text[i] ? 1 : 0;
        }

        return count;
    }

    public static (long Size, DateTime WriteTimeUtc) Save(string path, byte[] bytes, bool backup)
    {
        var folder = Path.GetDirectoryName(path) ?? ".";
        var temporary = Path.Combine(folder, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".saving");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
            }

            if (File.Exists(path))
            {
                File.Replace(temporary, path, backup ? path + ".bak" : null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        var info = new FileInfo(path);
        return (info.Length, info.LastWriteTimeUtc);
    }

    public static DiskState Check(string path, long size, DateTime writeTimeUtc)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return DiskState.Gone;
            }

            return info.Length != size || info.LastWriteTimeUtc != writeTimeUtc ? DiskState.Changed : DiskState.Same;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DiskState.Same;
        }
    }
}
