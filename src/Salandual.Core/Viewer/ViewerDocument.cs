// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/ViewerDocument.cs

namespace Salandual.Core.Viewer;

public sealed class ViewerDocument : IDisposable
{
    private readonly IByteSource _source;

    public ViewerDocument(string path, IByteSource source, string? encodingId = null)
    {
        Path = path;
        _source = source;
        Bytes = new CachedBytes(source);
        var start = new byte[(int)Math.Min(TextEncodings.SampleSize, source.Length)];
        start = start[..Bytes.Read(0, start)];
        Guess = TextEncodings.Detect(start);
        Sample = start;
        Lines = null!;
        Encoding = null!;
        Index = null!;
        SetEncoding(TextEncodings.Find(encodingId) ?? Guess.Encoding);
    }

    public string Path { get; }

    public CachedBytes Bytes { get; }

    public long Length => Bytes.Length;

    public EncodingGuess Guess { get; }

    public TextEncoding Encoding { get; private set; }

    public bool IsDetected => Encoding.Id == Guess.Encoding.Id;

    public long TextStart => IsDetected ? Guess.BomLength : 0;

    public TextLines Lines { get; private set; }

    public LineIndex Index { get; private set; }

    public string? LineEnding => TextEncodings.LineEnding(Sample, Encoding);

    private byte[] Sample { get; }

    public event Action? IndexProgress;

    public void SetEncoding(TextEncoding encoding)
    {
        Index?.Dispose();
        Encoding = encoding;
        Lines = new TextLines(Bytes, encoding, TextStart);
        Index = new LineIndex(Lines);
        Index.Progress += () => IndexProgress?.Invoke();
        Index.Start();
    }

    public byte[] Read(long start, long end, int limit = int.MaxValue)
    {
        var length = (int)Math.Clamp(end - start, 0, limit);
        var bytes = new byte[length];
        var n = Bytes.Read(start, bytes);
        return n == length ? bytes : bytes[..n];
    }

    public string Text(long start, long end, int limit = int.MaxValue)
        => Encoding.Encoding.GetString(Read(start, end, limit));

    public void Dispose()
    {
        Index?.Dispose();
        _source.Dispose();
    }
}
