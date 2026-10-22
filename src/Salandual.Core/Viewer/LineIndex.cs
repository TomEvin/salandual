// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/LineIndex.cs

namespace Salandual.Core.Viewer;

public sealed class LineIndex : IDisposable
{
    public const int Step = 1024;
    private const int Block = 4 * 1024 * 1024;

    private readonly TextLines _lines;
    private readonly List<long> _checkpoints;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cancel = new();
    private long _count;
    private long _scanned;

    public LineIndex(TextLines lines)
    {
        _lines = lines;
        _checkpoints = [lines.Start];
        _scanned = lines.Start;
    }

    public event Action? Progress;

    public long Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public long Scanned
    {
        get
        {
            lock (_lock)
            {
                return _scanned;
            }
        }
    }

    public bool IsComplete { get; private set; }

    public double Fraction => _lines.Length <= _lines.Start ? 1 : Math.Clamp((double)(Scanned - _lines.Start) / (_lines.Length - _lines.Start), 0, 1);

    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Start() => Completion = Task.Run(() => CountLines(_cancel.Token));

    public long? NumberOf(long lineStart)
    {
        long checkpoint;
        int k;
        lock (_lock)
        {
            if (lineStart > _scanned)
            {
                return null;
            }

            k = _checkpoints.BinarySearch(lineStart);
            k = k >= 0 ? k : ~k - 1;
            checkpoint = _checkpoints[k];
        }

        var number = (long)k * Step;
        for (var at = checkpoint; at < lineStart; number++)
        {
            var next = _lines.LineAt(at).Next;
            if (next == at)
            {
                break;
            }

            at = next;
        }

        return number;
    }

    public long? StartOf(long number)
    {
        long at;
        lock (_lock)
        {
            if (number > _count || (number == _count && !IsComplete))
            {
                return null;
            }

            at = _checkpoints[(int)(number / Step)];
        }

        for (var i = number / Step * Step; i < number; i++)
        {
            at = _lines.LineAt(at).Next;
        }

        return at;
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
    }

    private void CountLines(CancellationToken token)
    {
        var length = _lines.Length;
        var source = _lines.Bytes.Source;
        var buffer = new byte[Block + TextLines.MaxLineBytes + 8];
        var at = _lines.Start;
        long count = 0;
        var lastReport = Environment.TickCount64;
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
                    var (content, brk) = TextLines.Split(span[pos..], _lines.Encoding);
                    var next = pos + content + brk;
                    if (next == pos)
                    {
                        break;
                    }

                    count++;
                    pos = next;
                    if (count % Step == 0)
                    {
                        lock (_lock)
                        {
                            _checkpoints.Add(at + pos);
                        }
                    }
                }

                at += pos;
                lock (_lock)
                {
                    _count = count;
                    _scanned = at;
                }

                if (pos == 0)
                {
                    break;
                }

                if (Environment.TickCount64 - lastReport > 150)
                {
                    lastReport = Environment.TickCount64;
                    Progress?.Invoke();
                }
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        if (!token.IsCancellationRequested)
        {
            IsComplete = true;
            Progress?.Invoke();
        }
    }
}
