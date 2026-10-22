// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/SyntaxStates.cs

namespace Salandual.Core.Viewer;

public sealed class SyntaxStates : IDisposable
{
    public const int Step = 256;
    private const int GuessLines = 64;
    private const int CacheLimit = 20_000;
    private const int Block = 4 * 1024 * 1024;

    private readonly TextLines _lines;
    private readonly List<(long Offset, int State)> _kept;
    private readonly Dictionary<long, int> _cache = [];
    private readonly List<SyntaxSpan> _scratch = [];
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cancel = new();
    private long _scanned;
    private bool _guessed;

    public SyntaxStates(TextLines lines, SyntaxLanguage language, long exactLimit)
    {
        _lines = lines;
        Language = language;
        _kept = [(lines.Start, 0)];
        _scanned = lines.Start;
        if (language != SyntaxLanguage.None && lines.Length <= exactLimit)
        {
            Completion = Task.Run(() => Scan(_cancel.Token));
        }
    }

    public SyntaxLanguage Language { get; }

    public TextLines Lines => _lines;

    public bool IsExact { get; private set; }

    public Task Completion { get; } = Task.CompletedTask;

    public event Action? Exact;

    public int StateAt(long lineStart)
    {
        if (Language == SyntaxLanguage.None || lineStart <= _lines.Start)
        {
            return 0;
        }

        if (_cache.TryGetValue(lineStart, out var known))
        {
            return known;
        }

        long from;
        int state;
        lock (_lock)
        {
            if (lineStart <= _scanned)
            {
                var k = _kept.BinarySearch((lineStart, int.MinValue), Comparer<(long Offset, int State)>.Create((a, b) => a.Offset.CompareTo(b.Offset)));
                k = k >= 0 ? k : ~k - 1;
                (from, state) = _kept[Math.Max(0, k)];
            }
            else
            {
                from = -1;
                state = 0;
            }
        }

        var guess = from < 0;
        if (guess)
        {
            from = lineStart;
            for (var i = 0; i < GuessLines && _lines.Previous(from) is { } previous; i++)
            {
                from = previous;
            }

            _guessed = true;
        }

        if (_cache.Count > CacheLimit)
        {
            _cache.Clear();
        }

        var buffer = new byte[TextLines.MaxLineBytes + 8];
        for (var at = from; at < lineStart;)
        {
            if (guess && _cache.TryGetValue(at, out var cached))
            {
                state = cached;
            }

            _cache[at] = state;
            var (end, next) = _lines.LineAt(at);
            var n = _lines.Bytes.Read(at, buffer.AsSpan(0, (int)Math.Min(buffer.Length, end - at)));
            state = Advance(buffer.AsSpan(0, n), state);
            if (next == at)
            {
                break;
            }

            at = next;
        }

        _cache[lineStart] = state;
        return state;
    }

    public void ForgetGuesses()
    {
        _cache.Clear();
        _guessed = false;
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
    }

    private int Advance(ReadOnlySpan<byte> bytes, int state)
    {
        _scratch.Clear();
        return SyntaxRules.Tokenize(Language, _lines.Encoding.Encoding.GetString(bytes), state, _scratch);
    }

    private void Scan(CancellationToken token)
    {
        var length = _lines.Length;
        var source = _lines.Bytes.Source;
        var buffer = new byte[Block + TextLines.MaxLineBytes + 8];
        var spans = new List<SyntaxSpan>();
        var encoding = _lines.Encoding;
        var at = _lines.Start;
        var state = 0;
        long count = 0;
        try
        {
            while (at < length && !token.IsCancellationRequested)
            {
                var n = source.Read(at, buffer);
                if (n == 0)
                {
                    break;
                }

                var span = buffer.AsSpan(0, n);
                var pos = 0;
                while (pos < n && (n - pos >= TextLines.MaxLineBytes + 4 || at + n >= length))
                {
                    var (content, brk) = TextLines.Split(span[pos..], encoding);
                    if (content + brk == 0)
                    {
                        break;
                    }

                    spans.Clear();
                    state = SyntaxRules.Tokenize(Language, encoding.Encoding.GetString(span.Slice(pos, content)), state, spans);
                    pos += content + brk;
                    if (++count % Step == 0)
                    {
                        lock (_lock)
                        {
                            _kept.Add((at + pos, state));
                            _scanned = at + pos;
                        }
                    }
                }

                at += pos;
                if (pos == 0)
                {
                    break;
                }
            }
        }
        catch (IOException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        lock (_lock)
        {
            _scanned = length;
        }

        IsExact = true;
        if (_guessed)
        {
            Exact?.Invoke();
        }
    }
}
