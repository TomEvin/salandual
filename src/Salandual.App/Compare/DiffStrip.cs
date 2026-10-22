// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Compare/DiffStrip.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Salandual.Core.Compare;
using Salandual.Core.Settings;

namespace Salandual.App.Compare;

internal sealed class DiffStrip : Control
{
    private readonly DiffView _view;
    private bool _dragging;

    public DiffStrip(DiffView view)
    {
        _view = view;
        Width = 14;
        Cursor = new Cursor(StandardCursorType.Hand);
        view.ViewChanged += InvalidateVisual;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        ResourcesChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush("Brush.ColumnHeader"), bounds);
        context.DrawLine(new Pen(Brush("Brush.Border"), 1), new Point(0.5, 0), new Point(0.5, bounds.Height));
        var rows = _view.RowCount;
        if (rows == 0)
        {
            return;
        }

        var scale = bounds.Height / rows;
        var removed = Brush("Brush.Danger");
        var added = Brush("Brush.Success");
        var changed = Brush("Brush.Warning");
        var minor = Brush("Brush." + ColorTokens.CompareMinorMark);
        foreach (var block in _view.Blocks)
        {
            if (_view.Text is { } text)
            {
                for (var r = block.Row; r < block.Row + block.Count;)
                {
                    var kind = text.Rows[r].Change;
                    var start = r;
                    while (r < block.Row + block.Count && text.Rows[r].Change == kind)
                    {
                        r++;
                    }

                    Mark(start, r - start, kind switch { LineChange.Added => added, LineChange.Removed => removed, LineChange.SpacingOrCase => minor, _ => changed });
                }
            }
            else if (_view.Binary is { } binary)
            {
                var tail = (long)block.Row * BinaryDiff.RowBytes >= Math.Min(binary.LeftLength, binary.RightLength);
                Mark(block.Row, block.Count, tail ? added : changed);
            }
        }

        var first = _view.ItemAt(_view.Top).Row;
        var last = _view.ItemAt(Math.Min(_view.ItemCount - 1, _view.Top + _view.VisibleRows - 1)).End;
        var y = first * scale;
        var h = Math.Max(4, (last - first) * scale);
        context.DrawRectangle(null, new Pen(Brush("Brush.TertiaryText"), 1), new Rect(1.5, Math.Min(y, bounds.Height - h) + 0.5, bounds.Width - 3, h - 1), 2, 2);

        void Mark(long row, long count, IBrush brush)
            => context.FillRectangle(brush, new Rect(3, row * scale, bounds.Width - 5, Math.Max(2, count * scale)));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        e.Pointer.Capture(this);
        Go(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
        {
            Go(e.GetPosition(this).Y);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void Go(double y)
    {
        if (_view.RowCount > 0 && Bounds.Height > 0)
        {
            _view.ShowRow((long)(Math.Clamp(y / Bounds.Height, 0, 1) * (_view.RowCount - 1)));
        }
    }

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var b) && b is IBrush brush ? brush : Brushes.Gray;
}
