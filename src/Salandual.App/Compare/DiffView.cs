// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Compare/DiffView.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Salandual.App.Resources;
using Salandual.Core.Compare;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.App.Compare;

internal readonly record struct DiffItem(long Row, long Fold = 0)
{
    public bool IsFold => Fold > 0;

    public long End => Row + Math.Max(1, Fold);
}

internal sealed class DiffText
{
    private const int MaxColoredLines = 400_000;

    public DiffText(IReadOnlyList<string> lines, SyntaxLanguage language, int tabWidth)
    {
        Lines = [.. lines.Select(l => l.Contains('\t') ? Expand(l, tabWidth) : l)];
        Language = lines.Count > MaxColoredLines ? SyntaxLanguage.None : language;
        if (Language != SyntaxLanguage.None)
        {
            States = new int[Lines.Count];
            var spans = new List<SyntaxSpan>();
            var state = 0;
            for (var i = 0; i < Lines.Count; i++)
            {
                States[i] = state;
                spans.Clear();
                state = SyntaxRules.Tokenize(Language, Lines[i], state, spans);
            }
        }
    }

    public IReadOnlyList<string> Lines { get; }

    public SyntaxLanguage Language { get; }

    public int[]? States { get; }

    private static string Expand(string line, int tab)
    {
        var text = new System.Text.StringBuilder(line.Length + 8);
        foreach (var c in line)
        {
            if (c == '\t')
            {
                text.Append(' ', tab - (text.Length % tab));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }
}

internal sealed class DiffBytes : IDisposable
{
    private const int Page = 64 * 1024;
    private readonly FileStream _stream;
    private readonly Dictionary<long, byte[]> _pages = [];
    private readonly Queue<long> _order = new();

    public DiffBytes(string path)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess);
        Length = _stream.Length;
    }

    public long Length { get; }

    public int Read(long offset, Span<byte> into)
    {
        var n = (int)Math.Clamp(Length - offset, 0, into.Length);
        for (var i = 0; i < n;)
        {
            var at = offset + i;
            var page = PageAt(at / Page);
            var from = (int)(at % Page);
            var take = Math.Min(n - i, page.Length - from);
            if (take <= 0)
            {
                return i;
            }

            page.AsSpan(from, take).CopyTo(into[i..]);
            i += take;
        }

        return n;
    }

    private byte[] PageAt(long index)
    {
        if (_pages.TryGetValue(index, out var page))
        {
            return page;
        }

        _stream.Position = index * Page;
        page = new byte[(int)Math.Min(Page, Length - _stream.Position)];
        _stream.ReadAtLeast(page, page.Length, throwOnEndOfStream: false);
        _pages[index] = page;
        _order.Enqueue(index);
        if (_order.Count > 64)
        {
            _pages.Remove(_order.Dequeue());
        }

        return page;
    }

    public void Dispose() => _stream.Dispose();
}

internal sealed class DiffView : Control
{
    public double Gap => _text is not null ? 58 : 14;

    private const double ArrowWidth = 20;
    private const double ArrowHeight = 18;
    private const double ArrowSpace = 4;

    private readonly Dictionary<(int Side, int Line), TextLayout> _layouts = [];
    private readonly Dictionary<long, (List<InlinePiece> Left, List<InlinePiece> Right)> _inline = [];
    private readonly Dictionary<SyntaxKind, TextRunProperties> _runs = [];
    private readonly List<SyntaxSpan> _spans = [];
    private readonly HashSet<long> _unfolded = [];
    private DiffText? _leftText;
    private DiffText? _rightText;
    private TextDiff? _text;
    private BinaryDiff? _binary;
    private DiffBytes? _leftBytes;
    private DiffBytes? _rightBytes;
    private TextDiffOptions _options = new();
    private List<DiffItem>? _items;
    private bool _onlyDifferences;
    private int _context = 3;
    private long _top;
    private long _cursor;
    private double _horizontal;
    private double _widest;
    private double? _charWidth;
    private IReadOnlyList<bool>? _leftCopied;
    private IReadOnlyList<bool>? _rightCopied;
    private (int Block, long From, long To)? _selection;
    private long _anchor;
    private IReadOnlyList<DiffHit> _hits = [];
    private readonly Dictionary<(long Row, int Side), List<int>> _hitsAt = [];
    private int _currentHit = -1;

    public DiffView()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        ActualThemeVariantChanged += (_, _) => Forget();
        ResourcesChanged += (_, _) => Forget();
    }

    public event Action? ViewChanged;

    public event Action<int, bool>? CopyRequested;

    public (int Block, long From, long To)? Selection => _selection;

    public bool IsBinary => _binary is not null;

    public TextDiff? Text => _text;

    public IReadOnlyList<string>? LeftLines => _leftText?.Lines;

    public IReadOnlyList<string>? RightLines => _rightText?.Lines;

    public BinaryDiff? Binary => _binary;

    public IReadOnlyList<DiffBlock> Blocks => _text?.Blocks ?? _binary?.Blocks ?? [];

    public long RowCount => _text?.Rows.Count ?? _binary?.RowCount ?? 0;

    public long ItemCount => _items?.Count ?? RowCount;

    public long Top => _top;

    public long CursorItem => _cursor;

    public int Side { get; set; }

    public double RowHeight => Math.Ceiling(MonoSize * 1.55);

    public int VisibleRows => Math.Max(1, (int)(Bounds.Height / RowHeight));

    public double HorizontalOffset
    {
        get => _horizontal;
        set
        {
            var clamped = Math.Clamp(value, 0, HorizontalExtent);
            if (clamped != _horizontal)
            {
                _horizontal = clamped;
                Changed();
            }
        }
    }

    public double HorizontalExtent => Math.Max(0, _widest + (CharWidth * (_binary is null ? 4 : 1)) - TextWidth);

    public double TextWidth => Math.Max(0, SideWidth - NumberWidth - 6);

    private double SideWidth => Math.Max(0, (Bounds.Width - Gap) / 2);

    public bool OnlyDifferences
    {
        get => _onlyDifferences;
        set
        {
            if (value != _onlyDifferences)
            {
                var row = CursorRow;
                _onlyDifferences = value;
                _unfolded.Clear();
                Rebuild(row);
            }
        }
    }

    public (int Row, int Count) TargetRows(int block)
    {
        var b = Blocks[block];
        return _selection is { } s && s.Block == block ? ((int)s.From, (int)(s.To - s.From + 1)) : (b.Row, b.Count);
    }

    public bool ClearSelection()
    {
        if (_selection is null)
        {
            return false;
        }

        _selection = null;
        Changed();
        return true;
    }

    public void SelectLine(long row, bool extend)
    {
        var block = _text?.BlockOf((int)row) ?? -1;
        if (block < 0)
        {
            _selection = null;
        }
        else if (extend && _selection is { } s && s.Block == block)
        {
            _selection = (block, Math.Min(_anchor, row), Math.Max(_anchor, row));
        }
        else if (_selection is { } t && t.Block == block && t.From == row && t.To == row)
        {
            _selection = null;
        }
        else
        {
            _selection = (block, row, row);
            _anchor = row;
        }

        Changed();
    }

    public void SetHits(IReadOnlyList<DiffHit> hits, int current)
    {
        _hits = hits;
        _currentHit = current;
        _hitsAt.Clear();
        for (var i = 0; i < hits.Count; i++)
        {
            var h = hits[i];
            var last = _binary is not null ? (h.Start + h.Length - 1) / BinaryDiff.RowBytes : h.Row;
            for (var row = h.Row; row <= last; row++)
            {
                if (!_hitsAt.TryGetValue((row, h.Side), out var list))
                {
                    _hitsAt[(row, h.Side)] = list = [];
                }

                list.Add(i);
            }
        }

        InvalidateVisual();
    }

    public IReadOnlyList<DiffHit> Hits => _hits;

    public int CurrentHit => _currentHit;

    public void ShowHit(int index)
    {
        if (index < 0 || index >= _hits.Count)
        {
            return;
        }

        _currentHit = index;
        var hit = _hits[index];
        if (ItemAt(ItemOfRow(hit.Row)) is { IsFold: true } fold)
        {
            _unfolded.Add(fold.Row);
            _items = Fold();
        }

        _cursor = ItemOfRow(hit.Row);
        Side = hit.Side;
        Reveal(_cursor, third: _cursor < _top || _cursor >= _top + VisibleRows);
        var cw = CharWidth;
        double x, w;
        if (_binary is null)
        {
            (x, w) = (hit.Start * cw, hit.Length * cw);
        }
        else
        {
            var i = (int)(hit.Start % BinaryDiff.RowBytes);
            (x, w) = ((i * 3) + (i >= 8 ? 1 : 0)) * cw is var hx ? (hx, 2 * cw) : default;
        }

        if (x < _horizontal || x + w > _horizontal + TextWidth - (2 * cw))
        {
            _horizontal = Math.Clamp(x - (TextWidth / 3), 0, HorizontalExtent);
        }

        Changed();
    }

    public long CursorRow => ItemCount == 0 ? 0 : ItemAt(_cursor).Row;

    public int CursorBlock => _text?.BlockOf((int)Math.Min(int.MaxValue, CursorRow)) ?? _binary?.BlockOf(CursorRow) ?? -1;

    public void ShowText(DiffText left, DiffText right, TextDiff diff, TextDiffOptions options, int context, IReadOnlyList<bool>? leftCopied = null, IReadOnlyList<bool>? rightCopied = null)
    {
        var row = ItemCount == 0 ? -1 : CursorRow;
        Clear();
        (_leftText, _rightText, _text, _options, _context) = (left, right, diff, options, context);
        (_leftCopied, _rightCopied) = (leftCopied, rightCopied);
        _widest = Math.Max(Widest(left), Widest(right));
        Rebuild(row);
    }

    public void ShowBinary(DiffBytes left, DiffBytes right, BinaryDiff diff)
    {
        var row = ItemCount == 0 ? -1 : CursorRow;
        Clear();
        (_leftBytes, _rightBytes, _binary, _context) = (left, right, diff, 1);
        _widest = CharWidth * HexColumns;
        Rebuild(row);
    }

    private void Clear()
    {
        _leftBytes?.Dispose();
        _rightBytes?.Dispose();
        (_leftText, _rightText, _text, _binary, _leftBytes, _rightBytes) = (null, null, null, null, null, null);
        (_leftCopied, _rightCopied, _selection) = (null, null, null);
        _hits = [];
        _hitsAt.Clear();
        _currentHit = -1;
        _layouts.Clear();
        _inline.Clear();
        _unfolded.Clear();
    }

    public void Release() => Clear();

    private double Widest(DiffText text)
    {
        var longest = text.Lines.Count == 0 ? 0 : text.Lines.Max(l => l.Length);
        return longest * CharWidth;
    }

    private void Rebuild(long row)
    {
        _items = _onlyDifferences ? Fold() : null;
        if (row < 0)
        {
            _top = 0;
            _cursor = Blocks.Count > 0 ? ItemOfRow(Blocks[0].Row) : 0;
            Reveal(_cursor, third: true);
        }
        else
        {
            _cursor = ItemOfRow(row);
            Reveal(_cursor, third: false);
        }

        Changed();
    }

    private List<DiffItem> Fold()
    {
        var items = new List<DiffItem>();
        var count = RowCount;
        long next = 0;
        foreach (var block in Blocks)
        {
            var start = Math.Max(next, block.Row - _context);
            AddSame(next, start);
            var end = Math.Min(count, (long)block.Row + block.Count + _context);
            for (var r = start; r < end; r++)
            {
                items.Add(new DiffItem(r));
            }

            next = Math.Max(next, end);
        }

        AddSame(next, count);
        return items;

        void AddSame(long from, long to)
        {
            if (to - from <= 1 || _unfolded.Contains(from))
            {
                for (var r = from; r < to; r++)
                {
                    items.Add(new DiffItem(r));
                }
            }
            else if (to > from)
            {
                items.Add(new DiffItem(from, to - from));
            }
        }
    }

    public DiffItem ItemAt(long index)
    {
        if (_items is null)
        {
            return new DiffItem(index);
        }

        return _items[(int)Math.Clamp(index, 0, _items.Count - 1)];
    }

    public long ItemOfRow(long row)
    {
        if (_items is null)
        {
            return Math.Clamp(row, 0, Math.Max(0, RowCount - 1));
        }

        int lo = 0, hi = _items.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_items[mid].Row <= row)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    public void Unfold(long item)
    {
        if (ItemAt(item) is { IsFold: true } fold)
        {
            _unfolded.Add(fold.Row);
            var top = _top;
            _items = Fold();
            _cursor = ItemOfRow(fold.Row);
            _top = Math.Min(top, Math.Max(0, ItemCount - 1));
            Changed();
        }
    }

    public void MoveCursor(long item)
    {
        _cursor = Math.Clamp(item, 0, Math.Max(0, ItemCount - 1));
        Reveal(_cursor, third: false);
        Changed();
    }

    public bool GoToDifference(int direction)
    {
        var blocks = Blocks;
        if (blocks.Count == 0)
        {
            return false;
        }

        var row = CursorRow;
        var current = CursorBlock;
        int target;
        if (direction > 0)
        {
            target = current >= 0 ? current + 1 : FirstAfter(row);
        }
        else
        {
            target = current >= 0 ? current - 1 : FirstAfter(row) - 1;
        }

        if (target < 0 || target >= blocks.Count)
        {
            return false;
        }

        GoToBlock(target);
        return true;

        int FirstAfter(long r)
        {
            var i = 0;
            while (i < blocks.Count && blocks[i].Row <= r)
            {
                i++;
            }

            return i;
        }
    }

    public void GoToBlock(int index)
    {
        _cursor = ItemOfRow(Blocks[index].Row);
        Reveal(_cursor, third: true);
        Changed();
    }

    private void Reveal(long item, bool third)
    {
        var visible = VisibleRows;
        if (third)
        {
            _top = item - (visible / 3);
        }
        else if (item < _top)
        {
            _top = item;
        }
        else if (item >= _top + visible)
        {
            _top = item - visible + 1;
        }

        _top = Math.Clamp(_top, 0, Math.Max(0, ItemCount - visible));
    }

    public void ScrollRows(long rows)
    {
        var top = Math.Clamp(_top + rows, 0, Math.Max(0, ItemCount - VisibleRows));
        if (top != _top)
        {
            _top = top;
            Changed();
        }
    }

    public void ShowRow(long row)
    {
        var item = ItemOfRow(row);
        _top = Math.Clamp(item - (VisibleRows / 3), 0, Math.Max(0, ItemCount - VisibleRows));
        Changed();
    }

    public string? CursorLineText()
    {
        if (_text is null || ItemAt(_cursor) is { IsFold: true })
        {
            return null;
        }

        var r = _text.Rows[(int)CursorRow];
        var (text, line) = Side == 0 ? (_leftText, r.Left) : (_rightText, r.Right);
        return line >= 0 ? text!.Lines[line] : null;
    }

    public long? CursorLine()
    {
        if (_binary is not null)
        {
            return CursorRow * BinaryDiff.RowBytes;
        }

        if (_text is null || ItemCount == 0 || ItemAt(_cursor).IsFold)
        {
            return null;
        }

        var r = _text.Rows[(int)CursorRow];
        var line = Side == 0 ? r.Left : r.Right;
        return line >= 0 ? line + 1 : null;
    }

    private void Changed()
    {
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _top = Math.Clamp(_top, 0, Math.Max(0, ItemCount - VisibleRows));
        ViewChanged?.Invoke();
    }

    public void Forget()
    {
        _layouts.Clear();
        _runs.Clear();
        _charWidth = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        var item = _top + (long)(p.Y / RowHeight);
        if (item >= ItemCount)
        {
            return;
        }

        Focus();
        if (ArrowAt(p) is { } arrow)
        {
            CopyRequested?.Invoke(arrow.Block, arrow.ToRight);
        }
        else if (ItemAt(item).IsFold)
        {
            Unfold(item);
        }
        else if (p.X < SideWidth || p.X >= SideWidth + Gap)
        {
            Side = p.X < SideWidth + (Gap / 2) ? 0 : 1;
            MoveCursor(item);
            if (_text is not null)
            {
                SelectLine(ItemAt(item).Row, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            }
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var arrow = ArrowAt(e.GetPosition(this));
        Cursor = new Cursor(arrow is not null ? StandardCursorType.Hand : StandardCursorType.Ibeam);
        string? tip = null;
        if (arrow is { } a)
        {
            var n = TargetRows(a.Block).Count;
            tip = a.ToRight ? Loc.Plural(n, AppStrings.FileCompare_CopyRight_One, AppStrings.FileCompare_CopyRight_Other) : Loc.Plural(n, AppStrings.FileCompare_CopyLeft_One, AppStrings.FileCompare_CopyLeft_Other);
        }

        if (!Equals(ToolTip.GetTip(this), tip))
        {
            ToolTip.SetTip(this, tip);
            ToolTip.SetIsOpen(this, tip is not null);
        }
    }

    public (int Block, bool ToRight)? ArrowAt(Point p)
    {
        if (_text is null || ItemCount == 0)
        {
            return null;
        }

        var item = _top + (long)(p.Y / RowHeight);
        if (item >= ItemCount || ItemAt(item) is { IsFold: true } || ArrowsOf(ItemAt(item).Row) is not { } block)
        {
            return null;
        }

        var x = SideWidth + ((Gap - (2 * ArrowWidth) - ArrowSpace) / 2);
        return p.X >= x && p.X < x + ArrowWidth ? (block, true)
            : p.X >= x + ArrowWidth + ArrowSpace && p.X < x + (2 * ArrowWidth) + ArrowSpace ? (block, false)
            : null;
    }

    private int? ArrowsOf(long row)
    {
        var block = _text?.BlockOf((int)row) ?? -1;
        return block >= 0 && TargetRows(block).Row == row ? block : null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.X != 0)
        {
            HorizontalOffset -= (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * CharWidth * 6;
        }
        else
        {
            ScrollRows((long)Math.Round(-e.Delta.Y * 3));
        }

        e.Handled = true;
    }

    private Typeface MonoTypeface => new(this.TryFindResource("Font.Mono", ActualThemeVariant, out var f) && f is FontFamily family ? family : FontFamily.Default);

    private double MonoSize => this.TryFindResource("Font.MonoSize", ActualThemeVariant, out var s) && s is double size ? size : 13;

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var b) && b is IBrush brush ? brush : Brushes.Gray;

    private double CharWidth => _charWidth ??= new FormattedText("0000000000", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoTypeface, MonoSize, Brushes.Black).Width / 10;

    private double NumberWidth
    {
        get
        {
            var digits = _binary is not null ? OffsetDigits : Math.Max(3, (int)Math.Log10(Math.Max(1, Math.Max(_leftText?.Lines.Count ?? 0, _rightText?.Lines.Count ?? 0))) + 1);
            return (digits * CharWidth) + 18;
        }
    }

    private int OffsetDigits => _binary is { } b && Math.Max(b.LeftLength, b.RightLength) > 0xFFFFFFFFL ? 12 : 8;

    private const int AsciiColumn = (16 * 3) + 2;

    private const int HexColumns = AsciiColumn + 16;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush("Brush.CodeSurface"), bounds);
        var side = SideWidth;
        var row = RowHeight;
        var gapX = side;
        context.FillRectangle(Brush("Brush.WindowBackground"), new Rect(gapX, 0, Gap, bounds.Height));
        var edge = new Pen(Brush("Brush.Border"), 1);
        context.DrawLine(edge, new Point(gapX + 0.5, 0), new Point(gapX + 0.5, bounds.Height));
        context.DrawLine(edge, new Point(gapX + Gap - 0.5, 0), new Point(gapX + Gap - 0.5, bounds.Height));
        if (ItemCount == 0)
        {
            return;
        }

        var count = Math.Min(ItemCount - _top, (long)Math.Ceiling(bounds.Height / row) + 1);
        for (var i = 0; i < count; i++)
        {
            var index = _top + i;
            var item = ItemAt(index);
            var y = i * row;
            if (item.IsFold)
            {
                DrawFold(context, item, new Rect(0, y, bounds.Width, row));
                continue;
            }

            DrawSide(context, 0, item.Row, new Rect(0, y, side, row));
            DrawSide(context, 1, item.Row, new Rect(side + Gap, y, side, row));
            if (_text is not null)
            {
                DrawCopyFrame(context, item.Row, y, bounds.Width);
            }

            if (index == _cursor)
            {
                var accent = Brush("Brush.Accent");
                context.FillRectangle(accent, new Rect(0, y, 3, row));
                context.FillRectangle(accent, new Rect(side + Gap, y, 3, row));
            }
        }
    }

    private void DrawCopyFrame(DrawingContext context, long row, double y, double width)
    {
        var block = _text!.BlockOf((int)row);
        if (block < 0)
        {
            return;
        }

        var (first, count) = TargetRows(block);
        if (row < first || row >= first + count)
        {
            return;
        }

        var current = block == CursorBlock;
        var brush = Brush(current ? "Brush.Accent" : "Brush.BorderStrong");
        var line = new Pen(brush, 1);
        var frame = new Pen(brush, 2);
        var h = RowHeight;
        var side = SideWidth;
        var (left, right) = (side + 5, side + Gap - 5);
        var top = row == first;
        var bottom = row == first + count - 1;
        if (top)
        {
            context.DrawLine(line, new Point(0, y + 0.5), new Point(width, y + 0.5));
            context.DrawLine(frame, new Point(left, y + 1), new Point(right, y + 1));
        }

        if (bottom)
        {
            context.DrawLine(line, new Point(0, y + h - 0.5), new Point(width, y + h - 0.5));
            context.DrawLine(frame, new Point(left, y + h - 1), new Point(right, y + h - 1));
        }

        context.DrawLine(frame, new Point(left + 1, y), new Point(left + 1, y + h));
        context.DrawLine(frame, new Point(right - 1, y), new Point(right - 1, y + h));
        if (_selection is { } s && s.Block == block)
        {
            var mark = new Pen(Brush("Brush.Accent"), 1);
            context.DrawRectangle(null, mark, new Rect(0.5, y + 0.5, side - 1, h - 1));
            context.DrawRectangle(null, mark, new Rect(side + Gap + 0.5, y + 0.5, side - 1, h - 1));
        }

        if (!top)
        {
            return;
        }

        var x = side + ((Gap - (2 * ArrowWidth) - ArrowSpace) / 2);
        var ay = y + Math.Max(2, (h - ArrowHeight) / 2);
        Arrow(new Rect(x, ay, ArrowWidth, ArrowHeight), toRight: true);
        Arrow(new Rect(x + ArrowWidth + ArrowSpace, ay, ArrowWidth, ArrowHeight), toRight: false);

        void Arrow(Rect r, bool toRight)
        {
            if (current)
            {
                context.FillRectangle(Brush("Brush.Accent"), r, 3);
            }
            else
            {
                context.DrawRectangle(Brush("Brush.CodeSurface"), new Pen(Brush("Brush.BorderStrong"), 1), r.Deflate(0.5), 3, 3);
            }

            var pen = new Pen(Brush(current ? "Brush.OnAccent" : "Brush.SecondaryText"), 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var (cx, cy) = (r.Center.X, r.Center.Y);
            var d = toRight ? 1 : -1;
            context.DrawLine(pen, new Point(cx - (5 * d), cy), new Point(cx + (5 * d), cy));
            context.DrawLine(pen, new Point(cx + (1 * d), cy - 4), new Point(cx + (5 * d), cy));
            context.DrawLine(pen, new Point(cx + (1 * d), cy + 4), new Point(cx + (5 * d), cy));
        }
    }

    private void DrawFold(DrawingContext context, DiffItem item, Rect area)
    {
        context.FillRectangle(Brush("Brush.ColumnHeader"), area);
        var dash = new Pen(Brush("Brush.Border"), 1, new DashStyle([3, 3], 0));
        context.DrawLine(dash, new Point(0, area.Top + 0.5), new Point(area.Right, area.Top + 0.5));
        context.DrawLine(dash, new Point(0, area.Bottom - 0.5), new Point(area.Right, area.Bottom - 0.5));
        var label = Loc.Plural(item.Fold, AppStrings.FileCompare_Fold_One, AppStrings.FileCompare_Fold_Other);
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11.5, Brush("Brush.TertiaryText"));
        context.DrawText(text, new Point(area.Center.X - (text.Width / 2), area.Top + ((area.Height - text.Height) / 2)));
    }

    private void DrawSide(DrawingContext context, int side, long row, Rect area)
    {
        if (_text is not null)
        {
            DrawTextSide(context, side, (int)row, area);
        }
        else if (_binary is not null)
        {
            DrawBytesSide(context, side, row, area);
        }
    }

    private void DrawTextSide(DrawingContext context, int side, int row, Rect area)
    {
        var r = _text!.Rows[row];
        var line = side == 0 ? r.Left : r.Right;
        if (line < 0)
        {
            context.FillRectangle(Brush("Brush.WindowBackground"), area);
            return;
        }

        var background = r.Change switch
        {
            LineChange.Added => "Brush." + ColorTokens.CompareAdded,
            LineChange.Removed => "Brush." + ColorTokens.CompareRemoved,
            _ => null,
        };
        var copied = r.Change == LineChange.Same && ((r.Left >= 0 && _leftCopied is { } lc && r.Left < lc.Count && lc[r.Left]) || (r.Right >= 0 && _rightCopied is { } rc && r.Right < rc.Count && rc[r.Right]));
        if (copied)
        {
            background = "Brush." + ColorTokens.CompareCopied;
        }

        if (background is not null)
        {
            context.FillRectangle(Brush(background), area);
        }

        var numbers = NumberWidth;
        var numberLit = r.Change switch
        {
            LineChange.Changed => "Brush." + ColorTokens.CompareChangedText,
            LineChange.SpacingOrCase => "Brush." + ColorTokens.CompareMinorText,
            _ => null,
        };
        if (numberLit is not null)
        {
            context.FillRectangle(Brush(numberLit), new Rect(area.Left, area.Top, numbers, area.Height));
        }

        var number = new FormattedText((line + 1).ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, MonoTypeface, MonoSize, Brush(numberLit is null ? "Brush.CodeLineNumbers" : "Brush.CodeInk"));
        if (!copied)
        {
            context.DrawText(number, new Point(area.Left + numbers - 8 - number.Width, area.Top + ((area.Height - number.Height) / 2)));
        }

        var textArea = new Rect(area.Left + numbers, area.Top, Math.Max(0, area.Width - numbers), area.Height);
        var layout = Layout(side, line);
        var origin = new Point(textArea.Left + 6 - _horizontal, area.Top + ((area.Height - layout.Height) / 2));
        using (context.PushClip(textArea))
        {
            if (r.Change is LineChange.Changed or LineChange.SpacingOrCase)
            {
                var pieces = Inline(row);
                var lit = Brush("Brush." + ColorTokens.CompareChangedText);
                var minor = Brush("Brush." + ColorTokens.CompareMinorText);
                foreach (var piece in side == 0 ? pieces.Left : pieces.Right)
                {
                    foreach (var rect in layout.HitTestTextRange(piece.Start, piece.Length))
                    {
                        context.FillRectangle(piece.SpacingOrCase ? minor : lit, new Rect(origin.X + rect.X, area.Top + 1, rect.Width, area.Height - 2), 2);
                    }
                }
            }

            if (_hitsAt.TryGetValue((row, side), out var found))
            {
                foreach (var i in found)
                {
                    var hit = _hits[i];
                    var brush = Brush(i == _currentHit ? "Brush.ViewerCurrentMatch" : "Brush.ViewerMatch");
                    foreach (var rect in layout.HitTestTextRange((int)hit.Start, hit.Length))
                    {
                        context.FillRectangle(brush, new Rect(origin.X + rect.X, area.Top + 1, rect.Width, area.Height - 2), 2);
                    }
                }
            }

            layout.Draw(context, origin);
        }
    }

    private (List<InlinePiece> Left, List<InlinePiece> Right) Inline(int row)
    {
        if (!_inline.TryGetValue(row, out var pieces))
        {
            var r = _text!.Rows[row];
            pieces = TextDiff.Inline(_leftText!.Lines[r.Left], _rightText!.Lines[r.Right], _options);
            _inline[row] = pieces;
        }

        return pieces;
    }

    private TextLayout Layout(int side, int line)
    {
        if (_layouts.TryGetValue((side, line), out var layout))
        {
            return layout;
        }

        if (_layouts.Count > 4000)
        {
            _layouts.Clear();
        }

        var text = side == 0 ? _leftText! : _rightText!;
        var s = text.Lines[line];
        layout = new TextLayout(s, MonoTypeface, MonoSize, Brush("Brush.CodeInk"), TextAlignment.Left, TextWrapping.NoWrap, TextTrimming.None, null,
            FlowDirection.LeftToRight, double.PositiveInfinity, double.PositiveInfinity, RowHeight, 0, 0, null, Colors(text, line, s));
        _layouts[(side, line)] = layout;
        return layout;
    }

    private List<ValueSpan<TextRunProperties>>? Colors(DiffText text, int line, string s)
    {
        if (text.States is not { } states || s.Length == 0)
        {
            return null;
        }

        _spans.Clear();
        SyntaxRules.Tokenize(text.Language, s, states[line], _spans);
        if (_spans.Count == 0)
        {
            return null;
        }

        _spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var runs = new List<ValueSpan<TextRunProperties>>(_spans.Count);
        var reached = 0;
        foreach (var span in _spans)
        {
            var from = Math.Max(span.Start, reached);
            var to = Math.Min(span.Start + span.Length, s.Length);
            if (to > from)
            {
                runs.Add(new ValueSpan<TextRunProperties>(from, to - from, Run(span.Kind)));
                reached = to;
            }
        }

        return runs;
    }

    private TextRunProperties Run(SyntaxKind kind)
    {
        if (_runs.TryGetValue(kind, out var run))
        {
            return run;
        }

        var token = ColorTokens.TokenFor(kind);
        var weight = kind switch { SyntaxKind.Keyword => FontWeight.SemiBold, SyntaxKind.Heading or SyntaxKind.Strong => FontWeight.Bold, _ => FontWeight.Normal };
        var style = kind is SyntaxKind.Comment or SyntaxKind.Emphasis ? FontStyle.Italic : FontStyle.Normal;
        run = new GenericTextRunProperties(new Typeface(MonoTypeface.FontFamily, style, weight), MonoSize, foregroundBrush: Brush(token == ColorTokens.SyntaxText ? "Brush.CodeInk" : "Brush." + token));
        _runs[kind] = run;
        return run;
    }

    private void DrawBytesSide(DrawingContext context, int side, long row, Rect area)
    {
        var (mine, other) = side == 0 ? (_leftBytes!, _rightBytes!) : (_rightBytes!, _leftBytes!);
        var offset = row * BinaryDiff.RowBytes;
        Span<byte> a = stackalloc byte[BinaryDiff.RowBytes];
        Span<byte> b = stackalloc byte[BinaryDiff.RowBytes];
        var n = mine.Read(offset, a);
        if (n == 0)
        {
            context.FillRectangle(Brush("Brush.WindowBackground"), area);
            return;
        }

        var m = other.Read(offset, b);
        var differs = n != m || !a[..n].SequenceEqual(b[..m]);
        var cw = CharWidth;
        var numbers = NumberWidth;
        if (differs && m == 0)
        {
            context.FillRectangle(Brush("Brush." + (side == 0 ? ColorTokens.CompareRemoved : ColorTokens.CompareAdded)), area);
        }
        else if (differs)
        {
            context.FillRectangle(Brush("Brush." + ColorTokens.CompareChangedText), new Rect(area.Left, area.Top, numbers, area.Height));
        }

        var top = area.Top + ((area.Height - (MonoSize * 1.3)) / 2);
        var offsetText = new FormattedText(offset.ToString(OffsetDigits == 8 ? "X8" : "X12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoTypeface, MonoSize, Brush(differs && m > 0 ? "Brush.CodeInk" : "Brush.CodeLineNumbers"));
        context.DrawText(offsetText, new Point(area.Left + numbers - 8 - offsetText.Width, top));
        var textArea = new Rect(area.Left + numbers, area.Top, Math.Max(0, area.Width - numbers), area.Height);
        var x0 = textArea.Left + 6 - _horizontal;
        var chars = new char[HexColumns];
        Array.Fill(chars, ' ');
        for (var i = 0; i < n; i++)
        {
            var hex = (i * 3) + (i >= 8 ? 1 : 0);
            chars[hex] = "0123456789ABCDEF"[a[i] >> 4];
            chars[hex + 1] = "0123456789ABCDEF"[a[i] & 15];
            chars[AsciiColumn + i] = a[i] is >= 32 and < 127 ? (char)a[i] : '·';
        }

        using (context.PushClip(textArea))
        {
            var lit = Brush("Brush." + ColorTokens.CompareChangedText);
            for (var i = 0; i < n; i++)
            {
                if (i >= m || a[i] != b[i])
                {
                    var hex = (i * 3) + (i >= 8 ? 1 : 0);
                    context.FillRectangle(lit, new Rect(x0 + (hex * cw) - 1, area.Top + 1, (2 * cw) + 2, area.Height - 2), 2);
                    context.FillRectangle(lit, new Rect(x0 + ((AsciiColumn + i) * cw), area.Top + 1, cw, area.Height - 2), 2);
                }
            }

            if (_hitsAt.TryGetValue((row, side), out var found))
            {
                foreach (var h in found)
                {
                    var hit = _hits[h];
                    var brush = Brush(h == _currentHit ? "Brush.ViewerCurrentMatch" : "Brush.ViewerMatch");
                    for (var at = Math.Max(hit.Start, offset); at < Math.Min(hit.Start + hit.Length, offset + n); at++)
                    {
                        var i = (int)(at - offset);
                        var hex = (i * 3) + (i >= 8 ? 1 : 0);
                        context.FillRectangle(brush, new Rect(x0 + (hex * cw) - 1, area.Top + 1, (2 * cw) + 2, area.Height - 2), 2);
                        context.FillRectangle(brush, new Rect(x0 + ((AsciiColumn + i) * cw), area.Top + 1, cw, area.Height - 2), 2);
                    }
                }
            }

            var text = new FormattedText(new string(chars), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoTypeface, MonoSize, Brush("Brush.CodeInk"));
            context.DrawText(text, new Point(x0, top));
        }
    }
}
