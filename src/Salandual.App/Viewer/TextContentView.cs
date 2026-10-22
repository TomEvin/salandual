// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/TextContentView.cs

using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Utilities;
using Salandual.Core.Viewer;

namespace Salandual.App.Viewer;

internal sealed class TextContentView : ContentView
{
    private const double Pad = 8;
    private const int CacheLimit = 600;

    private readonly Dictionary<long, VisualLine> _cache = [];
    private readonly List<SyntaxSpan> _spans = [];
    private readonly Dictionary<SyntaxKind, TextRunProperties> _runs = [];
    private SyntaxStates? _syntax;
    private SyntaxLanguage _language;
    private long _exactColors = 200L * 1024 * 1024;
    private long _top;
    private int _topRow;
    private double _scrollX;
    private long _caretLine;
    private long _shownEnd;
    private double _widest;
    private int _gutterDigits = 4;
    private double _layoutWidth = -1;
    private bool _wrap = true;
    private bool _lineNumbers = true;
    private int _tabWidth = 8;
    private double? _charWidth;
    private bool _dragging;

    public bool Wrap
    {
        get => _wrap;
        set
        {
            _wrap = value;
            _scrollX = 0;
            Reset();
        }
    }

    public bool LineNumbers
    {
        get => _lineNumbers;
        set
        {
            _lineNumbers = value;
            Reset();
        }
    }

    public int TabWidth
    {
        get => _tabWidth;
        set
        {
            _tabWidth = value;
            Reset();
        }
    }

    public SyntaxLanguage Language => _language;

    public long ExactColorsLimit => _exactColors;

    public void SetColors(SyntaxLanguage language, long exactLimit, bool now = true)
    {
        if (_language == language && _exactColors == exactLimit)
        {
            return;
        }

        _language = language;
        _exactColors = exactLimit;
        if (now)
        {
            NewSyntax();
            Reset();
        }
    }

    public void StopColors()
    {
        _syntax?.Dispose();
        _syntax = null;
    }

    internal SyntaxStates? Syntax => _syntax;

    public double RowHeight => Math.Ceiling(MonoSize * 1.55);

    public double HorizontalExtent => _wrap ? 0 : Math.Max(0, _widest - TextWidth + (CharWidth * 4));

    public double HorizontalOffset
    {
        get => _scrollX;
        set
        {
            _scrollX = Math.Clamp(value, 0, HorizontalExtent);
            Changed();
        }
    }

    public double TextAreaWidth => TextWidth;

    public override long TopOffset => _top;

    public long CaretLineStart => _caretLine;

    public int CaretColumn => Document is null ? 1 : Line(_caretLine).Decoded.ColumnOf(Caret) + 1;

    public override double ScrollFraction
    {
        get => Document is { } d && d.Length > d.Lines.Start ? (double)(_top - d.Lines.Start) / (d.Length - d.Lines.Start) : 0;
        set
        {
            if (Document is not { } d)
            {
                return;
            }

            if (value >= 1)
            {
                ScrollToEnd();
                return;
            }

            _top = d.Lines.LineStartOf(d.Lines.Start + (long)(Math.Clamp(value, 0, 1) * (d.Length - d.Lines.Start)));
            _topRow = 0;
            Changed();
        }
    }

    public override double ViewportFraction => Document is { } d && d.Length > d.Lines.Start ? Math.Clamp((double)(_shownEnd - _top) / (d.Length - d.Lines.Start), 0.002, 1) : 1;

    private double TextLeft => (_lineNumbers ? (_gutterDigits * CharWidth) + 20 : 0) + Pad;

    private double TextWidth => Math.Max(40, Bounds.Width - TextLeft - Pad);

    private int ViewportRows => Math.Max(1, (int)(Bounds.Height / RowHeight));

    private double CharWidth => _charWidth ??= new FormattedText("0000000000", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoTypeface, MonoSize, Brushes.Black).Width / 10;

    public void Reset()
    {
        _cache.Clear();
        _charWidth = null;
        _widest = 0;
        if (Document is { } d)
        {
            _top = d.Lines.LineStartOf(_top);
            _topRow = 0;
            _caretLine = d.Lines.LineStartOf(Caret);
        }

        Changed();
    }

    public override void ShowAt(long offset)
    {
        if (Document is { } d)
        {
            _top = d.Lines.LineStartOf(offset);
            _topRow = 0;
            Changed();
        }
    }

    public override void ScrollToTop()
    {
        if (Document is { } d)
        {
            _top = d.Lines.Start;
            _topRow = 0;
            _scrollX = 0;
            Changed();
        }
    }

    public override void ScrollToEnd()
    {
        if (Document is not { } d)
        {
            return;
        }

        var last = d.Lines.LineStartOf(d.Length);
        if (last >= d.Length && d.Lines.Previous(d.Length) is { } previous)
        {
            last = previous;
        }

        _top = last;
        _topRow = Line(last).Rows - 1;
        ScrollRows(-(ViewportRows - 1));
        Changed();
    }

    public override void ScrollRows(int rows)
    {
        if (Document is not { } d)
        {
            return;
        }

        for (var i = 0; i < rows && CanScrollDown(); i++)
        {
            var line = Line(_top);
            if (_topRow + 1 < line.Rows)
            {
                _topRow++;
            }
            else
            {
                _top = line.Next;
                _topRow = 0;
            }
        }

        for (var i = 0; i > rows; i--)
        {
            if (_topRow > 0)
            {
                _topRow--;
            }
            else if (d.Lines.Previous(_top) is { } previous)
            {
                _top = previous;
                _topRow = Line(previous).Rows - 1;
            }
            else
            {
                break;
            }
        }

        Changed();
    }

    public void ScrollColumns(int columns)
    {
        if (_wrap)
        {
            return;
        }

        _scrollX = Math.Clamp(_scrollX + (columns * CharWidth), 0, Math.Max(0, _widest - TextWidth + (CharWidth * 4)));
        Changed();
    }

    public override void Reveal(long offset)
    {
        if (Document is not { } d)
        {
            return;
        }

        var start = d.Lines.LineStartOf(offset);
        var line = Line(start);
        var row = RowOf(line, offset);
        var (visible, rowY) = RowY(start, row);
        if (visible && rowY >= 0 && rowY + RowHeight <= Bounds.Height)
        {
            RevealColumn(line, offset);
            Changed();
            return;
        }

        _top = start;
        _topRow = row;
        ScrollRows(-(ViewportRows / 3));
        RevealColumn(line, offset);
        Changed();
    }

    public override long? OffsetAtPoint(Point p) => Document is null ? null : HitTest(p).Offset;

    protected override void OnSelecting(long caret)
    {
        if (Document is { } d)
        {
            _caretLine = d.Lines.LineStartOf(caret);
        }
    }

    protected override void OnLookChanged()
    {
        _cache.Clear();
        _runs.Clear();
        _charWidth = null;
        base.OnLookChanged();
    }

    protected override void OnDocumentChanged()
    {
        _cache.Clear();
        NewSyntax();
        _top = Document?.Lines.Start ?? 0;
        _topRow = 0;
        _scrollX = 0;
        _caretLine = _top;
        _widest = 0;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && _wrap && Math.Abs(TextWidth - _layoutWidth) > 0.5)
        {
            _cache.Clear();
            _topRow = 0;
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush("Brush.CodeSurface"), bounds);
        if (Document is not { } d)
        {
            return;
        }

        UpdateGutter(d);
        if (_wrap && Math.Abs(TextWidth - _layoutWidth) > 0.5)
        {
            _cache.Clear();
        }

        var row = RowHeight;
        var gutterBrush = Brush("Brush.CodeLineNumbers");
        var currentBrush = Brush("Brush.CodeCurrentLine");
        var number = d.Index.NumberOf(_top);
        var y = -_topRow * row;
        var at = _top;
        var textLeft = TextLeft;
        while (y < bounds.Height && (at < d.Length || (at == d.Lines.Start && d.Length == at)))
        {
            var line = Line(at);
            var height = line.Rows * row;
            if (at == _caretLine)
            {
                context.FillRectangle(currentBrush, new Rect(0, y, bounds.Width, height));
            }

            if (_lineNumbers && number is { } n)
            {
                var text = new FormattedText((n + 1).ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, MonoTypeface, MonoSize, gutterBrush);
                context.DrawText(text, new Point(textLeft - 12 - text.Width, y + ((row - text.Height) / 2)));
            }

            using (context.PushClip(new Rect(textLeft - 2, 0, Math.Max(0, bounds.Width - textLeft + 2), bounds.Height)))
            {
                var origin = new Point(textLeft - _scrollX, y);
                if (Highlight is { } regex)
                {
                    foreach (var hit in ViewerFind.InLine(line.Decoded, regex))
                    {
                        FillRange(context, line, origin, hit.Start, hit.End, SelectionIsMatch && hit.Start == SelectionStart ? CurrentMatchBrush : MatchBrush);
                    }
                }

                if (HasSelection && !(SelectionIsMatch && Highlight is not null) && SelectionStart < line.Next && SelectionEnd > at)
                {
                    FillRange(context, line, origin, Math.Max(SelectionStart, at), Math.Min(SelectionEnd, line.End), Brush("Brush.CodeSelection"));
                }

                line.Layout.Draw(context, origin);
                if (Caret >= at && (Caret < line.Next || (Caret == line.Next && line.Next >= d.Length)))
                {
                    var c = line.Layout.HitTestTextPosition(line.Decoded.ColumnOf(Caret));
                    context.FillRectangle(CaretBrush, new Rect(origin.X + c.X - 1, origin.Y + c.Y, 2, row));
                }
            }

            _widest = Math.Max(_widest, line.Layout.WidthIncludingTrailingWhitespace);
            y += height;
            number = number + 1;
            if (line.Next == at)
            {
                break;
            }

            at = line.Next;
        }

        _shownEnd = at;
        if (_cache.Count > CacheLimit)
        {
            _cache.Clear();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Document is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        var (lineStart, offset) = HitTest(e.GetPosition(this));
        if (e.ClickCount == 2)
        {
            SelectWord(lineStart, offset);
            return;
        }

        Caret = offset;
        if ((e.KeyModifiers & KeyModifiers.Shift) == 0)
        {
            Anchor = offset;
        }

        _caretLine = lineStart;
        _dragging = true;
        e.Pointer.Capture(this);
        Changed();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || Document is null)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (p.Y < 0)
        {
            ScrollRows(-1);
        }
        else if (p.Y > Bounds.Height)
        {
            ScrollRows(1);
        }

        var (lineStart, offset) = HitTest(new Point(p.X, Math.Clamp(p.Y, 0, Bounds.Height - 1)));
        Caret = offset;
        _caretLine = lineStart;
        Changed();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) || (e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            ScrollColumns((int)Math.Round(-(e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 8));
            e.Handled = true;
            return;
        }

        base.OnPointerWheelChanged(e);
    }

    private void SelectWord(long lineStart, long offset)
    {
        var line = Line(lineStart);
        var text = line.Decoded.Text;
        var c = Math.Min(line.Decoded.ColumnOf(offset), Math.Max(0, text.Length - 1));
        if (text.Length == 0)
        {
            return;
        }

        static bool Word(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
        int a = c, b = c;
        if (Word(text[c]))
        {
            while (a > 0 && Word(text[a - 1]))
            {
                a--;
            }

            while (b < text.Length && Word(text[b]))
            {
                b++;
            }
        }
        else
        {
            b = c + 1;
        }

        Anchor = line.Decoded.ByteOf[a];
        Caret = line.Decoded.ByteOf[b];
        _caretLine = lineStart;
        Changed();
    }

    private (long LineStart, long Offset) HitTest(Point p)
    {
        var d = Document!;
        var y = -_topRow * RowHeight;
        var at = _top;
        while (true)
        {
            var line = Line(at);
            var height = line.Rows * RowHeight;
            if (p.Y < y + height || line.Next >= d.Length || line.Next == at)
            {
                var hit = line.Layout.HitTestPoint(new Point(p.X - TextLeft + _scrollX, Math.Max(0, p.Y - y)));
                var index = Math.Clamp(hit.TextPosition, 0, line.Decoded.ByteOf.Length - 1);
                return (at, line.Decoded.ByteOf[index]);
            }

            y += height;
            at = line.Next;
        }
    }

    private void FillRange(DrawingContext context, VisualLine line, Point origin, long from, long to, IBrush brush)
    {
        var a = line.Decoded.ColumnOf(from);
        var b = to >= line.End ? line.Decoded.Text.Length : line.Decoded.ColumnOf(to);
        if (b <= a)
        {
            if (to > line.End && from <= line.End)
            {
                var end = line.Layout.HitTestTextPosition(line.Decoded.Text.Length);
                context.FillRectangle(brush, new Rect(origin.X + end.X, origin.Y + end.Y, CharWidth / 2, RowHeight));
            }

            return;
        }

        foreach (var rect in line.Layout.HitTestTextRange(a, b - a))
        {
            context.FillRectangle(brush, new Rect(origin.X + rect.X, origin.Y + rect.Y, rect.Width, RowHeight));
        }
    }

    private int RowOf(VisualLine line, long offset)
    {
        var rect = line.Layout.HitTestTextPosition(line.Decoded.ColumnOf(offset));
        return Math.Clamp((int)(rect.Y / RowHeight), 0, line.Rows - 1);
    }

    private (bool Visible, double Y) RowY(long lineStart, int row)
    {
        if (lineStart < _top)
        {
            return (false, 0);
        }

        var y = -_topRow * RowHeight;
        for (var at = _top; y < Bounds.Height + RowHeight;)
        {
            var line = Line(at);
            if (at == lineStart)
            {
                return (true, y + (row * RowHeight));
            }

            if (line.Next == at)
            {
                break;
            }

            y += line.Rows * RowHeight;
            at = line.Next;
        }

        return (false, 0);
    }

    private void RevealColumn(VisualLine line, long offset)
    {
        _caretLine = line.Start;
        if (_wrap)
        {
            return;
        }

        var x = line.Layout.HitTestTextPosition(line.Decoded.ColumnOf(offset)).X;
        if (x < _scrollX || x > _scrollX + TextWidth - (CharWidth * 2))
        {
            _scrollX = Math.Max(0, x - (TextWidth / 3));
        }
    }

    private bool CanScrollDown()
    {
        var d = Document!;
        var rows = -_topRow;
        for (var at = _top; rows <= ViewportRows;)
        {
            var line = Line(at);
            rows += line.Rows;
            if (line.Next >= d.Length || line.Next == at)
            {
                return rows > ViewportRows;
            }

            at = line.Next;
        }

        return true;
    }

    private void UpdateGutter(ViewerDocument d)
    {
        var estimate = d.Index.IsComplete ? d.Index.Count : (long)(d.Index.Count / Math.Max(d.Index.Fraction, 1e-6));
        var digits = Math.Max(4, Math.Max(estimate, 1).ToString(CultureInfo.InvariantCulture).Length);
        if (digits > _gutterDigits || d.Index.IsComplete)
        {
            if (digits != _gutterDigits && _wrap)
            {
                _cache.Clear();
            }

            _gutterDigits = digits;
        }
    }

    private void NewSyntax()
    {
        _syntax?.Dispose();
        _syntax = null;
        if (Document is not { } d || _language == SyntaxLanguage.None)
        {
            return;
        }

        var syntax = new SyntaxStates(d.Lines, _language, _exactColors);
        syntax.Exact += () => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(syntax, _syntax))
            {
                syntax.ForgetGuesses();
                _cache.Clear();
                Changed();
            }
        });
        _syntax = syntax;
    }

    private TextRunProperties Run(SyntaxKind kind)
    {
        if (_runs.TryGetValue(kind, out var run))
        {
            return run;
        }

        var token = Salandual.Core.Settings.ColorTokens.TokenFor(kind);
        var weight = kind switch { SyntaxKind.Keyword => FontWeight.SemiBold, SyntaxKind.Heading or SyntaxKind.Strong => FontWeight.Bold, _ => FontWeight.Normal };
        var style = kind is SyntaxKind.Comment or SyntaxKind.Emphasis ? FontStyle.Italic : FontStyle.Normal;
        var typeface = new Typeface(MonoTypeface.FontFamily, style, weight);
        run = new GenericTextRunProperties(typeface, MonoSize, foregroundBrush: Brush(token == Salandual.Core.Settings.ColorTokens.SyntaxText ? "Brush.CodeInk" : "Brush." + token));
        _runs[kind] = run;
        return run;
    }

    private List<ValueSpan<TextRunProperties>>? Colors(long start, string text)
    {
        if (_syntax is not { } syntax || text.Length == 0)
        {
            return null;
        }

        _spans.Clear();
        SyntaxRules.Tokenize(syntax.Language, text, syntax.StateAt(start), _spans);
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
            var to = Math.Min(span.Start + span.Length, text.Length);
            if (to > from)
            {
                runs.Add(new ValueSpan<TextRunProperties>(from, to - from, Run(span.Kind)));
                reached = to;
            }
        }

        return runs;
    }

    private VisualLine Line(long start)
    {
        if (_cache.TryGetValue(start, out var line))
        {
            return line;
        }

        var d = Document!;
        if (_syntax is { } kept && !ReferenceEquals(kept.Lines, d.Lines))
        {
            NewSyntax();
        }

        var (end, next) = d.Lines.LineAt(start);
        var decoded = d.Lines.Decode(start, end, _tabWidth);
        var colors = Colors(start, decoded.Text);
        _layoutWidth = TextWidth;
        var layout = new TextLayout(
            decoded.Text,
            MonoTypeface,
            MonoSize,
            Brush("Brush.CodeInk"),
            TextAlignment.Left,
            _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming.None,
            null,
            FlowDirection.LeftToRight,
            _wrap ? _layoutWidth : double.PositiveInfinity,
            double.PositiveInfinity,
            RowHeight,
            0,
            0,
            null,
            colors);
        line = new VisualLine(start, end, next, decoded, layout, Math.Max(1, layout.TextLines.Count));
        _cache[start] = line;
        return line;
    }

    private sealed record VisualLine(long Start, long End, long Next, DecodedLine Decoded, TextLayout Layout, int Rows);
}
