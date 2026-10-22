// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/HexContentView.cs

using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Salandual.App.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.App.Viewer;

internal sealed class HexContentView : ContentView
{
    private const double Pad = 12;

    private long _topRow;
    private int _bytesPerRow = 16;
    private int _group = 8;
    private bool _dragging;
    public const string Ascii = "ASCII";

    private Encoding? _characters;
    private double? _charWidth;
    private bool _decimal;
    private ByteColors _byteColors;

    public int BytesPerRowSetting
    {
        get => _bytesPerRow;
        set
        {
            var top = TopOffset;
            _bytesPerRow = value;
            ShowAt(top);
        }
    }

    public int Group
    {
        get => _group;
        set
        {
            _group = value;
            Changed();
        }
    }

    public TextEncoding Characters
    {
        set
        {
            _characters = value.SingleByte ? value.Encoding : null;
            CharactersName = value.SingleByte ? value.Name : Ascii;
            Changed();
        }
    }

    public string CharactersName { get; private set; } = Ascii;

    public int BytesPerRow => _bytesPerRow > 0 ? _bytesPerRow : Math.Max(8, FittingBytes() / 8 * 8);

    public double RowHeight => Math.Ceiling(MonoSize * 1.55);

    public override long TopOffset => _topRow * BytesPerRow;

    private long Rows => Document is { } d ? Math.Max(1, (d.Length + BytesPerRow - 1) / BytesPerRow) : 1;

    private int ViewportRows => Math.Max(1, (int)((Bounds.Height - RowHeight) / RowHeight));

    private double CharWidth => _charWidth ??= new FormattedText("0000000000", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoTypeface, MonoSize, Brushes.Black).Width / 10;

    private int OffsetDigits => Document is not { } d ? 8
        : DecimalOffsets ? Math.Max(8, d.Length.ToString(CultureInfo.InvariantCulture).Length)
        : d.Length > 0xFFFFFFFFL ? 12 : 8;

    public bool DecimalOffsets
    {
        get => _decimal;
        set
        {
            _decimal = value;
            Changed();
        }
    }

    public ByteColors ByteColors
    {
        get => _byteColors;
        set
        {
            _byteColors = value;
            InvalidateVisual();
        }
    }

    public event Action? OffsetHeaderClicked;

    private string OffsetText(long value) => value.ToString((DecimalOffsets ? "D" : "X") + OffsetDigits, CultureInfo.InvariantCulture);

    public override double ScrollFraction
    {
        get => Rows <= ViewportRows ? 0 : (double)_topRow / (Rows - ViewportRows);
        set
        {
            _topRow = (long)Math.Round(Math.Clamp(value, 0, 1) * Math.Max(0, Rows - ViewportRows));
            Changed();
        }
    }

    public override double ViewportFraction => Math.Clamp((double)ViewportRows / Rows, 0.002, 1);

    public override void ShowAt(long offset)
    {
        _topRow = Math.Clamp(offset / BytesPerRow, 0, Math.Max(0, Rows - ViewportRows));
        Changed();
    }

    public override void ScrollRows(int rows)
    {
        _topRow = Math.Clamp(_topRow + rows, 0, Math.Max(0, Rows - ViewportRows));
        Changed();
    }

    public override void ScrollToTop()
    {
        _topRow = 0;
        Changed();
    }

    public override void ScrollToEnd()
    {
        _topRow = Math.Max(0, Rows - ViewportRows);
        Changed();
    }

    public override void Reveal(long offset)
    {
        var row = offset / BytesPerRow;
        if (row < _topRow || row >= _topRow + ViewportRows)
        {
            _topRow = Math.Clamp(row - (ViewportRows / 3), 0, Math.Max(0, Rows - ViewportRows));
        }

        Changed();
    }

    public void MoveCaret(long delta, bool extend)
    {
        if (Document is not { } d)
        {
            return;
        }

        SelectionIsMatch = false;
        Caret = Math.Clamp(Caret + delta, 0, Math.Max(0, d.Length - 1));
        if (!extend)
        {
            Anchor = Caret;
        }

        Reveal(Caret);
    }

    protected override void OnLookChanged()
    {
        _charWidth = null;
        base.OnLookChanged();
    }

    protected override void OnDocumentChanged()
    {
        _topRow = 0;
        _charWidth = null;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush("Brush.PanelBackground"), bounds);
        if (Document is not { } d)
        {
            return;
        }

        var per = BytesPerRow;
        var cw = CharWidth;
        var row = RowHeight;
        var kinds = ByteKinds.Tokens.Select(t => Brush("Brush." + t)).ToArray();
        var offsets = Brush("Brush.ViewerOffsets");
        var header = Brush("Brush.SecondaryText");
        var (bytesX, charsX) = Columns(per);

        context.FillRectangle(Brush("Brush.ColumnHeader"), new Rect(0, 0, bounds.Width, row));
        context.FillRectangle(Brush("Brush.Border"), new Rect(0, row - 1, bounds.Width, 1));
        Draw(context, AppStrings.Viewer_Offset, Pad, 0, header);
        for (var i = 0; i < per; i += _group == 1 ? 8 : _group)
        {
            Draw(context, i.ToString(DecimalOffsets ? "D2" : "X2", CultureInfo.InvariantCulture), ByteX(i, bytesX, cw), 0, offsets);
        }

        Draw(context, AppStrings.Viewer_Characters, charsX, 0, header);

        var buffer = new byte[per];
        var current = Caret / per;
        var hexParts = new StringBuilder[kinds.Length];
        var charParts = new StringBuilder[kinds.Length];
        for (var k = 0; k < kinds.Length; k++)
        {
            hexParts[k] = new StringBuilder();
            charParts[k] = new StringBuilder();
        }

        var used = new bool[kinds.Length];
        using (context.PushClip(new Rect(0, row, bounds.Width, Math.Max(0, bounds.Height - row))))
        {
            for (var r = 0; r <= ViewportRows; r++)
            {
                var index = _topRow + r;
                var start = index * per;
                if (start >= d.Length && !(start == 0 && d.Length == 0))
                {
                    break;
                }

                var y = row + (r * row);
                var n = d.Bytes.Read(start, buffer);
                if (index == current)
                {
                    context.FillRectangle(Brush("Brush.Hover"), new Rect(0, y, bounds.Width, row));
                }

                Draw(context, OffsetText(start), Pad, y, offsets);
                var chars = _characters?.GetChars(buffer, 0, n) ?? [.. buffer.AsSpan(0, n).ToArray().Select(b => b < 0x80 ? (char)b : '\u0000')];

                var every = _group == 1 ? 8 : _group;
                var matches = MatchMask(d, start, n);
                for (var k = 0; k < kinds.Length; k++)
                {
                    hexParts[k].Clear();
                    charParts[k].Clear();
                    used[k] = false;
                }

                for (var i = 0; i < n; i++)
                {
                    var at = start + i;
                    var b = buffer[i];
                    var selected = HasSelection && at >= SelectionStart && at < SelectionEnd;
                    if (selected || matches[i])
                    {
                        var fill = selected ? (SelectionIsMatch ? CurrentMatchBrush : SelectionBrush) : MatchBrush;
                        var joined = i + 1 < n && (i + 1) % every != 0 && (selected ? at + 1 < SelectionEnd : matches[i + 1]);
                        context.FillRectangle(fill, new Rect(ByteX(i, bytesX, cw) - 1, y, (cw * 2) + (joined ? cw : 2), row));
                        context.FillRectangle(fill, new Rect(charsX + (i * cw), y, cw, row));
                    }

                    var kind = (int)ByteKinds.Of(b, _byteColors);
                    var digits = b.ToString("X2", CultureInfo.InvariantCulture);
                    var gap = i + 1 < n ? (i + 1) % every == 0 ? "  " : " " : "";
                    var c = chars.Length == n ? chars[i] : '.';
                    var dot = c < 0x20 || c == 0x7F || (c >= 0x80 && c < 0xA0) || char.IsControl(c);

                    var charKind = _byteColors == ByteColors.Two ? (int)(dot ? ByteKind.Zero : ByteKind.Text) : kind;
                    for (var k = 0; k < kinds.Length; k++)
                    {
                        hexParts[k].Append(k == kind ? digits : "  ").Append(gap);
                        charParts[k].Append(k == charKind ? (dot ? '.' : c) : ' ');
                    }

                    used[kind] = true;
                    used[charKind] = true;
                }

                for (var k = 0; k < kinds.Length; k++)
                {
                    if (used[k])
                    {
                        Draw(context, hexParts[k].ToString(), bytesX, y, kinds[k]);
                        Draw(context, charParts[k].ToString(), charsX, y, kinds[k]);
                    }
                }

                if (index == current && Caret < d.Length)
                {
                    var c = (int)(Caret - start);
                    var pen = new Pen(CaretBrush, 1.5);
                    context.DrawRectangle(null, pen, new Rect(ByteX(c, bytesX, cw) - 2.5, y + 1.5, (cw * 2) + 4, row - 3), 2, 2);
                    context.DrawRectangle(null, pen, new Rect(charsX + (c * cw) - 1.5, y + 1.5, cw + 3, row - 3), 2, 2);
                }
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Document is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (p.Y < RowHeight && p.X < Columns(BytesPerRow).Bytes)
        {
            OffsetHeaderClicked?.Invoke();
            e.Handled = true;
            return;
        }

        Focus();
        if (OffsetAt(e.GetPosition(this)) is not { } offset)
        {
            return;
        }

        Caret = offset;
        if ((e.KeyModifiers & KeyModifiers.Shift) == 0)
        {
            Anchor = offset;
        }

        _dragging = true;
        e.Pointer.Capture(this);
        Changed();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (p.Y < RowHeight)
        {
            ScrollRows(-1);
        }
        else if (p.Y > Bounds.Height)
        {
            ScrollRows(1);
        }

        if (OffsetAt(new Point(p.X, Math.Clamp(p.Y, RowHeight, Bounds.Height - 1))) is { } offset)
        {
            Caret = offset + (offset >= Anchor ? 1 : 0);
            Changed();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    public override long? OffsetAtPoint(Point p) => OffsetAt(p);

    internal long? OffsetAt(Point p)
    {
        if (Document is not { } d || p.Y < RowHeight)
        {
            return null;
        }

        var per = BytesPerRow;
        var cw = CharWidth;
        var (bytesX, charsX) = Columns(per);
        var r = _topRow + (long)((p.Y - RowHeight) / RowHeight);
        int column;
        if (p.X >= charsX - (cw / 2))
        {
            column = (int)((p.X - charsX) / cw);
        }
        else
        {
            column = 0;
            for (var i = per - 1; i >= 0; i--)
            {
                if (p.X >= ByteX(i, bytesX, cw) - (cw / 2))
                {
                    column = i;
                    break;
                }
            }
        }

        return Math.Clamp((r * per) + Math.Clamp(column, 0, per - 1), 0, Math.Max(0, d.Length - 1));
    }

    private bool[] MatchMask(ViewerDocument d, long start, int n)
    {
        var mask = new bool[n + 1];
        if (HighlightBytes is not { Length: > 0 } pattern)
        {
            return mask;
        }

        var from = Math.Max(0, start - pattern.Length + 1);
        var window = new byte[(int)(start - from) + n + pattern.Length];
        var span = window.AsSpan(0, d.Bytes.Read(from, window));
        for (var s = 0; s + pattern.Length <= span.Length; s++)
        {
            if (span.Slice(s, pattern.Length).SequenceEqual(pattern))
            {
                for (var k = 0; k < pattern.Length; k++)
                {
                    var i = from + s + k - start;
                    if (i >= 0 && i <= n)
                    {
                        mask[i] = true;
                    }
                }
            }
        }

        return mask;
    }

    private (double Bytes, double Chars) Columns(int per)
    {
        var cw = CharWidth;
        var bytesX = Pad + ((OffsetDigits + 3) * cw);
        var charsX = ByteX(per - 1, bytesX, cw) + (cw * 4);
        return (bytesX, charsX);
    }

    private double ByteX(int i, double bytesX, double cw)
    {
        var every = _group == 1 ? 8 : _group;
        return bytesX + (i * 3 * cw) + (i / every * cw);
    }

    private int FittingBytes()
    {
        var cw = CharWidth;
        var available = Bounds.Width - Pad - ((OffsetDigits + 3) * cw) - (cw * 4) - Pad;
        return Math.Max(8, (int)(available / (cw * 4.2)));
    }

    private void Draw(DrawingContext context, string s, double x, double y, IBrush brush)
    {
        var text = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, MonoTypeface, MonoSize, brush);
        context.DrawText(text, new Point(x, y + ((RowHeight - text.Height) / 2)));
    }
}
