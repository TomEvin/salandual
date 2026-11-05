// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/TabStrip.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Salandual.App.Controls;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Settings;

namespace Salandual.App.Panels;

public sealed record TabLook(string Title, string Tip, bool Locked = false, NameColour Colour = NameColour.Normal);

public sealed class TabStrip : ThemedControl
{
    private const double MinTab = 72;
    private const double MaxTab = 180;
    private const double ButtonWidth = 26;
    private const double DragDistance = 5;

    private readonly List<Rect> _tabs = [];
    private IReadOnlyList<TabLook> _items = [];
    private int _selected;
    private int _hover = -1;
    private bool _hoverClose;
    private double _offset;
    private bool _scrolls;
    private Rect _plus, _left, _right, _list;
    private int _rows = 1;
    private int _dropIndex = -1;
    private TabColourLook _colours = TabColourLook.Bar;
    private bool _wrap;
    private int _pressed = -1;
    private bool _pressedClose;
    private Point _pressedAt;
    private bool _dragging;
    private int _dragTo = -1;
    private IBrush _bg = null!, _shown = null!, _text = null!, _dim = null!, _hoverBg = null!, _accent = null!, _dropBg = null!;
    private IPen _border = null!, _iconPen = null!, _dimPen = null!, _dropPen = null!, _markPen = null!;

    public event EventHandler<int>? TabClicked;

    public event EventHandler<int>? CloseRequested;

    public event EventHandler? NewRequested;

    public event EventHandler<(int Index, Point At)>? MenuRequested;

    public event EventHandler<Point>? ListRequested;

    public event EventHandler<(int From, int To)>? Reordered;

    public event EventHandler<(int Index, PointerReleasedEventArgs Args)>? DraggedOut;

    public IReadOnlyList<TabLook> Items
    {
        get => _items;
        set
        {
            _items = value;
            _hover = -1;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public int Selected
    {
        get => _selected;
        set
        {
            if (_selected != value)
            {
                _selected = value;
                _bringIntoView = true;
                InvalidateArrange();
                InvalidateVisual();
            }
        }
    }

    private bool _bringIntoView = true;

    public TabColourLook Colours
    {
        get => _colours;
        set
        {
            _colours = value;
            InvalidateVisual();
        }
    }

    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (_wrap != value)
            {
                _wrap = value;
                _offset = 0;
                InvalidateMeasure();
            }
        }
    }

    public int DropIndex
    {
        get => _dropIndex;
        set
        {
            if (_dropIndex != value)
            {
                _dropIndex = value;
                InvalidateVisual();
            }
        }
    }

    internal bool Scrolls => _scrolls;

    internal int Rows => _rows;

    internal IReadOnlyList<Rect> TabBounds => _tabs;

    internal Rect PlusBounds => _plus;

    internal Rect ListBounds => _list;

    public int IndexAt(Point p)
    {
        if (_tabs.Count != _items.Count)
        {
            Lay(Bounds.Width);
        }

        if (_scrolls && (_left.Contains(p) || _right.Contains(p) || _list.Contains(p)))
        {
            return -1;
        }

        return _tabs.FindIndex(r => Shifted(r).Contains(p) && Visible(Shifted(r)));
    }

    private double RowHeight => Math.Round(FontSize * 2.3);

    private Rect Shifted(Rect r) => _scrolls ? r.Translate(new Vector(-_offset, 0)) : r;

    private bool Visible(Rect r) => !_scrolls || (r.Right > _left.Right && r.X < _right.X);

    private Rect CloseBox(Rect tab) => new(tab.Right - 22, tab.Y + ((tab.Height - 16) / 2) + 1, 16, 16);

    protected override void LoadTheme()
    {
        _bg = Brush("ColumnHeader");
        _shown = Brush("PanelBackground");
        _text = Brush("Text");
        _dim = Brush("SecondaryText");
        _hoverBg = Brush("Hover");
        _accent = Brush("Accent");
        _dropBg = Brush("TextSelection");
        _border = new ImmutablePen(Brush("Border").ToImmutable(), 1);
        _iconPen = new ImmutablePen(Brush("Text").ToImmutable(), 1.3, lineCap: PenLineCap.Round);
        _dimPen = new ImmutablePen(Brush("SecondaryText").ToImmutable(), 1.2, lineCap: PenLineCap.Round);
        _dropPen = new ImmutablePen(Brush("Accent").ToImmutable(), 2);
        _markPen = new ImmutablePen(Brush("Accent").ToImmutable(), 2, lineCap: PenLineCap.Round);
    }

    private double NaturalWidth(TabLook tab)
        => Math.Clamp(10 + (tab.Locked ? 16 : 0) + Text(tab.Title, _text, size: Small).Width + 6 + (tab.Locked ? 4 : 22), MinTab, MaxTab);

    private double Small => Math.Max(9, FontSize - 0.5);

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureTheme();
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 800;
        Lay(width);
        return new Size(0, (RowHeight * _rows) + 4);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Lay(finalSize.Width);
        return finalSize;
    }

    private void Lay(double width)
    {
        _tabs.Clear();
        _rows = 1;
        _scrolls = false;
        var top = 4.0;
        var h = RowHeight;
        var start = 6.0;
        var natural = _items.Select(NaturalWidth).ToList();
        var room = width - start - ButtonWidth - 6;
        if (_wrap)
        {
            double x = start, y = top;
            for (var i = 0; i < natural.Count; i++)
            {
                if (x + natural[i] > width - 6 && x > start)
                {
                    x = start;
                    y += h;
                    _rows++;
                }

                _tabs.Add(new Rect(x, y, natural[i], h));
                x += natural[i] + 2;
            }

            if (x + ButtonWidth > width - 2 && x > start)
            {
                x = start;
                y += h;
                _rows++;
            }

            _plus = new Rect(x, y + 2, ButtonWidth, h - 4);
            return;
        }

        var total = natural.Sum() + (2 * Math.Max(0, natural.Count - 1));
        if (total > room && natural.Count > 0)
        {
            var each = Math.Max(MinTab, ((room + 2) / natural.Count) - 2);
            natural = [.. natural.Select(n => Math.Min(n, each))];
            total = natural.Sum() + (2 * (natural.Count - 1));
        }

        if (total > room && natural.Count > 0)
        {
            _scrolls = true;
            _left = new Rect(2, top + 2, 20, h - 4);
            _list = new Rect(width - ButtonWidth - 2 - 22, top + 2, 22, h - 4);
            _plus = new Rect(width - ButtonWidth - 2, top + 2, ButtonWidth, h - 4);
            _right = new Rect(_list.X - 22, top + 2, 20, h - 4);
            start = _left.Right + 2;
        }

        var at = start;
        foreach (var n in natural)
        {
            _tabs.Add(new Rect(at, top, n, h));
            at += n + 2;
        }

        if (!_scrolls)
        {
            _offset = 0;
            _plus = new Rect(at + 2, top + 2, ButtonWidth, h - 4);
            return;
        }

        var view = _right.X - 2 - start;
        var max = Math.Max(0, at - 2 - start - view);
        if (_bringIntoView && _selected >= 0 && _selected < _tabs.Count)
        {
            var tab = _tabs[_selected];
            if (tab.X - _offset < start)
            {
                _offset = tab.X - start;
            }
            else if (tab.Right - _offset > start + view)
            {
                _offset = tab.Right - start - view;
            }
        }

        _bringIntoView = false;
        _offset = Math.Clamp(_offset, 0, max);
    }

    internal void ScrollBy(double delta)
    {
        if (!_scrolls)
        {
            return;
        }

        _offset += delta;
        _bringIntoView = false;
        Lay(Bounds.Width);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var size = Bounds.Size;
        if (_tabs.Count != _items.Count)
        {
            Lay(size.Width);
        }

        context.FillRectangle(_bg, new Rect(size));
        var bottom = size.Height - 0.5;
        var dark = FolderLooks.IsDarkTheme;
        var selected = _selected >= 0 && _selected < _tabs.Count ? Shifted(_tabs[_selected]) : default;

        if (selected.Width > 0 && Visible(selected) && selected.Bottom >= size.Height - 4)
        {
            context.DrawLine(_border, new Point(0, bottom), new Point(selected.X, bottom));
            context.DrawLine(_border, new Point(selected.Right, bottom), new Point(size.Width, bottom));
        }
        else
        {
            context.DrawLine(_border, new Point(0, bottom), new Point(size.Width, bottom));
        }

        var clip = _scrolls ? new Rect(_left.Right + 1, 0, Math.Max(0, _right.X - _left.Right - 2), size.Height) : new Rect(size);
        using (context.PushClip(clip))
        {
            for (var i = 0; i < _tabs.Count; i++)
            {
                DrawTab(context, i, Shifted(_tabs[i]), dark, bottom);
            }

            if (_dragging && _dragTo >= 0)
            {
                var x = _dragTo < _tabs.Count ? Shifted(_tabs[_dragTo]).X - 1 : Shifted(_tabs[^1]).Right + 1;
                var row = _tabs[Math.Min(_dragTo, _tabs.Count - 1)];
                context.DrawLine(_markPen, new Point(x, row.Y + 3), new Point(x, row.Bottom - 3));
            }
        }

        DrawButton(context, _plus, "Icon.Add", _hover == -10);
        if (_scrolls)
        {
            DrawButton(context, _left, "Icon.ChevronLeft", _hover == -11);
            DrawButton(context, _right, "Icon.ChevronRight", _hover == -12);
            DrawButton(context, _list, "Icon.ChevronDown", _hover == -13);
        }
    }

    private void DrawTab(DrawingContext context, int i, Rect r, bool dark, double bottom)
    {
        var tab = _items[i];
        var on = i == _selected;
        var shape = new Rect(r.X, r.Y, r.Width, (on ? bottom + 0.5 : r.Bottom - 1) - r.Y);
        var colour = tab.Colour != NameColour.Normal && _colours != TabColourLook.None ? FolderLooks.FolderBrush(tab.Colour, dark) as ISolidColorBrush : null;
        var geometry = TopRounded(shape, Corners.ChipRadius);
        if (on)
        {
            context.DrawGeometry(_shown, null, geometry);
        }
        else if (i == _hover)
        {
            context.DrawGeometry(_hoverBg, null, geometry);
        }

        if (colour is not null && _colours == TabColourLook.Tint)
        {
            context.DrawGeometry(new ImmutableSolidColorBrush(colour.Color, on ? 0.22 : 0.16), null, geometry);
        }

        if (i == _dropIndex)
        {
            context.DrawGeometry(_dropBg, _dropPen, geometry);
        }
        else if (on)
        {
            context.DrawGeometry(null, _border, TopRounded(shape.Deflate(0.5), Corners.ChipRadius, open: true));
        }

        if (colour is not null && _colours == TabColourLook.Bar)
        {
            context.DrawRectangle(colour, null, new Rect(r.X + 4, r.Y, r.Width - 8, 3), 0, 0);
        }

        var x = r.X + 10;
        var mid = r.Y + (r.Height / 2) + 1;
        if (tab.Locked)
        {
            AppIcons.Draw(context, "Icon.Lock", new Point(x - 1, mid - 6.5), _dimPen, scale: 0.8);
            x += 16;
        }

        var showClose = !tab.Locked && (on || i == _hover);
        var right = r.Right - (tab.Locked ? 6 : 24);
        var text = Text(tab.Title, on ? _text : _dim, bold: on, size: Small);
        text.MaxTextWidth = Math.Max(1, right - x);
        text.MaxLineCount = 1;
        text.Trimming = TextTrimming.CharacterEllipsis;
        context.DrawText(text, new Point(x, mid - (text.Height / 2)));
        if (showClose)
        {
            var box = CloseBox(r);
            if (i == _hover && _hoverClose)
            {
                context.DrawRectangle(_hoverBg, null, box.Inflate(1), 3, 3);
            }

            AppIcons.Draw(context, "Icon.Cross", box.TopLeft, i == _hover && _hoverClose ? _iconPen : _dimPen);
        }
    }

    private void DrawButton(DrawingContext context, Rect r, string icon, bool hover)
    {
        if (r.Width <= 0)
        {
            return;
        }

        if (hover)
        {
            context.DrawRectangle(_hoverBg, null, r, Corners.ChipRadius, Corners.ChipRadius);
        }

        AppIcons.Draw(context, icon, r.Center - new Point(8, 8), _dimPen);
    }

    private static Geometry TopRounded(Rect r, double radius, bool open = false)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.X, r.Bottom), isFilled: !open);
            c.LineTo(new Point(r.X, r.Y + radius));
            c.ArcTo(new Point(r.X + radius, r.Y), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(r.Right - radius, r.Y));
            c.ArcTo(new Point(r.Right, r.Y + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(r.Right, r.Bottom));
            c.EndFigure(!open);
        }

        return g;
    }

    private (int Hit, bool Close) HitAt(Point p)
    {
        if (_plus.Contains(p))
        {
            return (-10, false);
        }

        if (_scrolls)
        {
            if (_left.Contains(p))
            {
                return (-11, false);
            }

            if (_right.Contains(p))
            {
                return (-12, false);
            }

            if (_list.Contains(p))
            {
                return (-13, false);
            }
        }

        var i = IndexAt(p);
        return i < 0 ? (-1, false) : (i, !_items[i].Locked && CloseBox(Shifted(_tabs[i])).Inflate(2).Contains(p));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_pressed >= 0 && !_pressedClose)
        {
            if (!_dragging && Math.Abs(p.X - _pressedAt.X) + Math.Abs(p.Y - _pressedAt.Y) > DragDistance)
            {
                _dragging = true;
            }

            if (_dragging)
            {
                _dragTo = new Rect(Bounds.Size).Contains(p) ? InsertAt(p) : -1;
                Cursor = new Cursor(_dragTo >= 0 ? StandardCursorType.SizeWestEast : StandardCursorType.DragMove);
                InvalidateVisual();
                return;
            }
        }

        var (hit, close) = HitAt(p);
        if (hit != _hover || close != _hoverClose)
        {
            _hover = hit;
            _hoverClose = close;
            ToolTip.SetTip(this, hit >= 0 ? (close ? AppStrings.Tabs_CloseTip : _items[hit].Tip) : hit switch
            {
                -10 => AppStrings.Tabs_NewTip,
                -13 => AppStrings.Tabs_ListTip,
                _ => null,
            });
            InvalidateVisual();
        }
    }

    private int InsertAt(Point p)
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            var r = Shifted(_tabs[i]);
            if (p.Y >= r.Y && p.Y < r.Bottom && p.X < r.Center.X)
            {
                return i;
            }
        }

        return _tabs.Count;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        _hoverClose = false;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_scrolls)
        {
            ScrollBy(-(e.Delta.Y + e.Delta.X) * 60);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        var (hit, close) = HitAt(p);
        var properties = e.GetCurrentPoint(this).Properties;
        e.Handled = true;
        if (properties.IsMiddleButtonPressed)
        {
            if (hit >= 0)
            {
                CloseRequested?.Invoke(this, hit);
            }

            return;
        }

        if (properties.IsRightButtonPressed)
        {
            MenuRequested?.Invoke(this, (hit >= 0 ? hit : -1, p));
            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        switch (hit)
        {
            case -10:
                NewRequested?.Invoke(this, EventArgs.Empty);
                return;
            case -11:
                ScrollBy(-MinTab);
                return;
            case -12:
                ScrollBy(MinTab);
                return;
            case -13:
                ListRequested?.Invoke(this, _list.BottomLeft);
                return;
            case < 0:
                if (e.ClickCount == 2)
                {
                    NewRequested?.Invoke(this, EventArgs.Empty);
                }

                return;
        }

        _pressed = hit;
        _pressedClose = close;
        _pressedAt = p;
        _dragging = false;
        e.Pointer.Capture(this);
        if (!close)
        {
            TabClicked?.Invoke(this, hit);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pressed = _pressed;
        var wasClose = _pressedClose;
        var dragging = _dragging;
        var to = _dragTo;
        EndPress();
        e.Pointer.Capture(null);
        if (pressed < 0)
        {
            return;
        }

        e.Handled = true;
        var p = e.GetPosition(this);
        if (wasClose)
        {
            if (HitAt(p) is var (hit, close) && hit == pressed && close)
            {
                CloseRequested?.Invoke(this, pressed);
            }
        }
        else if (dragging && to >= 0)
        {
            var target = to > pressed ? to - 1 : to;
            if (target != pressed)
            {
                Reordered?.Invoke(this, (pressed, target));
            }
        }
        else if (dragging)
        {
            DraggedOut?.Invoke(this, (pressed, e));
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndPress();
    }

    private void EndPress()
    {
        _pressed = -1;
        _pressedClose = false;
        _dragTo = -1;
        if (_dragging)
        {
            _dragging = false;
            Cursor = null;
            InvalidateVisual();
        }
    }

    internal void Press(int index) => TabClicked?.Invoke(this, index);

    internal void PressClose(int index) => CloseRequested?.Invoke(this, index);

    internal void PressNew() => NewRequested?.Invoke(this, EventArgs.Empty);

    internal void Move(int from, int to) => Reordered?.Invoke(this, (from, to));
}
