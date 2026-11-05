// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/ConnectionLog.cs

namespace Salandual.Remote;

public readonly record struct LogLine(DateTime Time, string Text);

public sealed class ConnectionLog
{
    public const int MaxLines = 2000;

    private readonly Queue<LogLine> _lines = new();

    public event Action? Changed;

    public void Add(string text)
    {
        lock (_lines)
        {
            _lines.Enqueue(new LogLine(DateTime.Now, text));
            while (_lines.Count > MaxLines)
            {
                _lines.Dequeue();
            }
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<LogLine> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    public void Clear()
    {
        lock (_lines)
        {
            _lines.Clear();
        }

        Changed?.Invoke();
    }
}
