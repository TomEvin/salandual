// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Compare/CompareFile.cs

using Salandual.Core.Viewer;

namespace Salandual.Core.Compare;

public sealed class CompareFile
{
    public const long MaxTextBytes = 32L * 1024 * 1024;

    private CompareFile(string path, long length, DateTime lastWriteUtc, EncodingGuess guess, string? lineEnding, IReadOnlyList<string> lines)
    {
        Path = path;
        Length = length;
        LastWriteUtc = lastWriteUtc;
        Encoding = guess.Encoding;
        BomLength = guess.BomLength;
        LooksBinary = guess.IsBinary;
        LineEnding = lineEnding;
        Lines = lines;
        Language = SyntaxRules.LanguageFor(System.IO.Path.GetFileName(path));
    }

    public string Path { get; }

    public long Length { get; }

    public DateTime LastWriteUtc { get; }

    public TextEncoding Encoding { get; }

    public int BomLength { get; }

    public bool LooksBinary { get; }

    public bool TooBig => Length > MaxTextBytes;

    public string? LineEnding { get; }

    public IReadOnlyList<string> Lines { get; }

    public bool EndsWithLineBreak { get; private init; }

    public SyntaxLanguage Language { get; }

    public static CompareFile Read(string path, CancellationToken cancel = default)
    {
        var info = new FileInfo(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        var length = stream.Length;
        var sample = new byte[(int)Math.Min(length, TextEncodings.SampleSize)];
        stream.ReadExactly(sample);
        var guess = TextEncodings.Detect(sample);
        var ending = TextEncodings.LineEnding(sample, guess.Encoding);
        if (length > MaxTextBytes)
        {
            return new CompareFile(path, length, info.LastWriteTimeUtc, guess, ending, []);
        }

        cancel.ThrowIfCancellationRequested();
        var bytes = new byte[length];
        sample.CopyTo(bytes, 0);
        stream.ReadExactly(bytes, sample.Length, (int)(length - sample.Length));
        cancel.ThrowIfCancellationRequested();
        var text = guess.Encoding.Encoding.GetString(bytes, guess.BomLength, bytes.Length - guess.BomLength);
        return new CompareFile(path, length, info.LastWriteTimeUtc, guess, ending, SplitLines(text)) { EndsWithLineBreak = text.EndsWith('\n') || text.EndsWith('\r') };
    }

    public static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\n' or '\r')
            {
                lines.Add(text[start..i]);
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }
}
