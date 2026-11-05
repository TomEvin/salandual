// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/FilePanelView.Waiting.cs

using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Salandual.Core.Commands;
using Avalonia.Threading;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace Salandual.App.Panels;

public sealed partial class FilePanelView
{
    public const int WaitDelayMs = 300;

    private DispatcherTimer? _waitTimer;
    private long _waitSecond = -1;
    private bool _waitWasShown;
    private bool _wasLocked;

    public event EventHandler? WaitingChanged;

    public event EventHandler? Refused;

    public event EventHandler? LockChanged;

    public bool IsLocked
        => _panel?.LoadingSince is not null && (IsWaiting || Salandual.Remote.ServerAddress.IsServerPath(_panel.LoadingPath));

    public static bool AllowedWhileWaiting(string id)
        => id is "Panel.SwitchActive" or "Server.Log"
           || id.StartsWith("App.", StringComparison.Ordinal) || id.StartsWith("Help.", StringComparison.Ordinal) || id.StartsWith("Options.", StringComparison.Ordinal);

    private void LockedKey(KeyEventArgs e, KeyChord chord)
    {
        e.Handled = _suppressText = true;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            _panel!.CancelLoading();
            return;
        }

        if (Commands?.Resolve(chord) is { } id && AllowedWhileWaiting(id) && UnhandledCommand is { } handler)
        {
            var request = new CommandRequest(id) { Chord = chord };
            handler(this, request);
            e.Handled = _suppressText = request.Handled;
            return;
        }

        Refused?.Invoke(this, EventArgs.Empty);
    }

    public bool IsWaiting => WaitingFor is not null;

    public TimeSpan? WaitingFor
        => _panel?.LoadingSince is { } since && Environment.TickCount64 - since is var ms && ms >= WaitDelayMs ? TimeSpan.FromMilliseconds(ms) : null;

    private void WatchLoading()
    {
        if (_panel?.LoadingSince is null)
        {
            _waitTimer?.Stop();
            NoteWaiting();
            return;
        }

        if (_waitTimer is null)
        {
            _waitTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
            _waitTimer.Tick += (_, _) =>
            {
                if (_panel?.LoadingSince is null)
                {
                    _waitTimer.Stop();
                }

                NoteWaiting();
                if (IsWaiting)
                {
                    InvalidateVisual();
                }
            };
        }

        _waitTimer.Start();
        NoteWaiting();
    }

    private void NoteWaiting()
    {
        var locked = IsLocked;
        if (locked != _wasLocked)
        {
            _wasLocked = locked;
            LockChanged?.Invoke(this, EventArgs.Empty);
        }

        var shown = WaitingFor;
        var second = shown is { } s ? (long)s.TotalSeconds : -1;
        if (shown is not null != _waitWasShown || second != _waitSecond)
        {
            if (shown is not null != _waitWasShown)
            {
                InvalidateVisual();
            }

            _waitWasShown = shown is not null;
            _waitSecond = second;
            WaitingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DrawWaiting(DrawingContext context, Size size)
    {
        if (WaitingFor is not { } waited)
        {
            return;
        }

        var bg = _b.BackgroundRgb;
        var veil = new GeometryGroup { FillRule = FillRule.EvenOdd };
        veil.Children.Add(new RectangleGeometry(new Rect(size)));
        if (FocusedCellRect is { Width: > 0 } focused && focused.Intersects(new Rect(size)))
        {
            veil.Children.Add(new RectangleGeometry(focused));
        }

        context.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, bg.R, bg.G, bg.B)), null, veil);

        const double period = 1400;
        var t = waited.TotalMilliseconds % period / period;
        var width = size.Width / 3;
        var x = (-width) + (t * (size.Width + width));
        var bar = new Rect(Math.Max(0, x), 0, Math.Max(0, Math.Min(size.Width, x + width) - Math.Max(0, x)), 2);
        if (bar.Width > 0)
        {
            context.FillRectangle(_b.Accent, bar);
        }
    }
}
