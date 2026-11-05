// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/FilePanelView.Columns.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.App.Panels;

public sealed record ColumnWidthChange(PanelColumn Column, int Width);

public sealed partial class FilePanelView
{
    private const double EdgeGrip = 4;

    private static readonly string _typeHead = AppStrings.Panel_Col_Type, _itemsHead = AppStrings.Panel_Col_Items, _modifiedHead = AppStrings.Panel_Col_Modified,
        _createdHead = AppStrings.Panel_Col_Created, _accessedHead = AppStrings.Panel_Col_Accessed, _dosHead = AppStrings.Panel_Col_DosName, _dirText = AppStrings.Panel_Dir,
        _gitHead = AppStrings.Panel_Col_Git;

    private IReadOnlyList<ColumnView> _viewColumns = [];
    private (PanelView View, IReadOnlyList<ColumnView> From, ColumnView Setup)? _setup;
    private double _hScroll;
    private bool _draggingHThumb;
    private Resize? _resize;
    private double _typeFit = -1;
    private FolderSizeText _folderSize = FolderSizeText.Folder;
    private ColumnAlignment _alignment = ColumnAlignment.NumbersRight;
    private LongNames _longNames = LongNames.CutEnd;
    private bool _rowStripes;
    private MarkLook _focusLook = MarkLook.WholeRow;
    private MarkLook _selectionLook = MarkLook.WholeRow;
    private bool _focusBackground = true;
    private MarkLook _focusBackgroundLook = MarkLook.WholeRow;
    private ColumnFacts? _facts;
    private string? _dateTip;

    private sealed record Resize(PanelColumn Column, double StartX, double StartWidth, bool GrowsLeft, double Width);

    private readonly record struct Placed(PanelColumn? Column, double X, double Width, bool DateOnly, bool Folder = false);

    private sealed record ColumnLayout(double NameRight, Placed[] Columns, double ScrollMax, bool Smart)
    {
        public bool Shows(PanelColumn column) => Columns.Any(c => c.Column == column);
    }

    public IReadOnlyList<ColumnView> ViewColumns
    {
        get => _viewColumns;
        set
        {
            if (!ReferenceEquals(_viewColumns, value))
            {
                _viewColumns = value;
                _setup = null;
                Relayout();
            }
        }
    }

    public event EventHandler<ColumnWidthChange>? ColumnWidthChanged;

    public event EventHandler<Point>? HeaderMenuRequested;

    public FolderSizeText FolderSize { get => _folderSize; set => Restyle(ref _folderSize, value); }

    public ColumnAlignment ColumnAlignment { get => _alignment; set => Restyle(ref _alignment, value); }

    public LongNames LongNames { get => _longNames; set => Restyle(ref _longNames, value); }

    public bool RowStripes { get => _rowStripes; set => Restyle(ref _rowStripes, value); }

    public Highlighter Highlighter { get => _highlighter; set => Restyle(ref _highlighter, value); }

    public FolderLooks? FolderLooks
    {
        get => _folderLooks;
        set
        {
            _folderLooks = value;
            InvalidateVisual();
        }
    }

    private Highlighter _highlighter = Highlighter.None;
    private FolderLooks? _folderLooks;

    public MarkLook FocusLook { get => _focusLook; set => Restyle(ref _focusLook, value); }

    public MarkLook SelectionLook { get => _selectionLook; set => Restyle(ref _selectionLook, value); }

    public bool FocusBackground { get => _focusBackground; set => Restyle(ref _focusBackground, value); }

    public MarkLook FocusBackgroundLook { get => _focusBackgroundLook; set => Restyle(ref _focusBackgroundLook, value); }

    internal Rect FocusMarkRect(bool background = false)
    {
        var look = background ? _focusBackgroundLook : _focusLook;
        var i = _panel?.Focus ?? -1;
        if (i < 0 || i >= Count)
        {
            return default;
        }

        if (ColumnsNow() is { } cols && IsDetailed)
        {
            var y = HeaderHeight + (RowHeight * (i - _top));
            var row = new Rect(4, y, Math.Max(0, Bounds.Width - ScrollBarWidth - 4), RowHeight);
            return look == MarkLook.WholeRow ? row : NameMark(i, y, cols, row.Right);
        }

        var l = Layout();
        var cell = CellRect(i, l);
        var box = ViewKind is PanelView.Brief or PanelView.Columns ? new Rect(cell.X + 3, cell.Y, Math.Max(0, cell.Width - 6), cell.Height) : cell.Deflate(new Thickness(2, 2));
        return look == MarkLook.WholeRow ? box : CellNameMark(GetCellText(i, cell.Width), cell, box);
    }

    private void FillMarks(DrawingContext context, bool selected, bool focused, bool active, Rect whole, Rect name)
    {
        if (focused && _focusBackground)
        {
            context.FillRectangle(active ? _b.FocusBackground : _b.FocusBackgroundInactive, _focusBackgroundLook == MarkLook.WholeRow ? whole : name, 4);
        }

        if (selected)
        {
            context.FillRectangle(_b.SelectionBackground, _selectionLook == MarkLook.WholeRow ? whole : name, 4);
        }
    }

    private void DrawFocus(DrawingContext context, bool active, Rect whole, Rect name)
        => context.DrawRectangle(null, active ? _b.FocusPen : _b.FocusPenInactive, (_focusLook == MarkLook.WholeRow ? whole : name).Deflate(0.75), 4, 4);

    private Rect NameMark(int i, double y, ColumnLayout cols, double rowRight)
    {
        var t = GetRowText(i, cols);
        var left = 6 + MarkShift;
        var right = NameX + t.Name.Width + 5;
        if (cols.Columns.Length > 0 && cols.Columns[0].Column == PanelColumn.Extension && t.Cells[0] is { } ext)
        {
            var x = ScreenX(cols.Columns[0], cols);
            if (x >= cols.NameRight)
            {
                right = x + ext.Width + 5;
            }
        }

        return new Rect(left, y, Math.Max(0, Math.Min(right, rowRight) - left), RowHeight);
    }

    internal IReadOnlyList<(PanelColumn Column, double X, double Width)> ShownColumns()
    {
        var cols = ColumnsNow();
        return [.. cols.Columns.Where(c => c.Column is not null).Select(c => (c.Column!.Value, ScreenX(c, cols), c.Width))];
    }

    internal (double X, double Width)? FolderShown()
    {
        var cols = ColumnsNow();
        return cols.Columns.FirstOrDefault(c => c.Folder) is { Folder: true } c ? (ScreenX(c, cols), c.Width) : null;
    }

    internal int NameWidthNow => (int)Math.Round(ColumnsNow().NameRight - NameX);

    internal (double NameRight, double ScrollMax) NameAndScroll()
    {
        var cols = ColumnsNow();
        return (cols.NameRight, cols.ScrollMax);
    }

    internal string? ColumnText(int index, PanelColumn column)
    {
        var cols = ColumnsNow();
        var i = Array.FindIndex(cols.Columns, c => c.Column == column);
        if (i < 0)
        {
            return null;
        }

        return GetRowText(index, cols).Texts[i];
    }

    internal string? FolderColumnText(int index)
    {
        var cols = ColumnsNow();
        var i = Array.FindIndex(cols.Columns, c => c.Folder);
        return i < 0 ? null : GetRowText(index, cols).Texts[i];
    }

    internal string RowName(int index) => GetRowText(index, ColumnsNow()).NameShown;

    private void Restyle<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            ClearTexts();
            InvalidateVisual();
        }
    }

    private ColumnFacts? Facts => _facts;

    private void OnFactsChanged(object? sender, EventArgs e)
    {
        if (IsDetailed && (Setup.Columns.Any(c => c.Shown && c.Column is PanelColumn.Items or PanelColumn.DosName)))
        {
            ClearTexts();
            InvalidateVisual();
        }
    }

    private ColumnView Setup
    {
        get
        {
            var view = ViewKind;
            if (_setup is { } s && s.View == view && ReferenceEquals(s.From, _viewColumns))
            {
                return s.Setup;
            }

            var setup = Core.Panels.ColumnViews.For(_viewColumns, view);
            _setup = (view, _viewColumns, setup);
            return setup;
        }
    }

    private ColumnLayout ColumnsNow()
    {
        EnsureTheme();
        return LayOut(Bounds.Width - ScrollBarWidth);
    }

    private ColumnLayout LayOut(double width)
    {
        var setup = Setup;
        var smart = setup.SmartName && _resize?.Column != PanelColumn.Name;
        var gap = _widths.Gap;
        var list = new List<(PanelColumn? Column, double Width, bool Auto, bool DateOnly)>();
        var gitPlaced = false;
        foreach (var c in setup.Columns)
        {
            if (c.Column == PanelColumn.Git)
            {
                gitPlaced = true;
                if (!GitColumnShown(c.Shown) || _place?.Hide.Contains(PanelColumn.Git) == true)
                {
                    continue;
                }
            }
            else if (!c.Shown || c.Column == PanelColumn.Name || (_place is { } hiding && hiding.Hide.Contains(c.Column)))
            {
                continue;
            }

            var w = _resize is { } r && r.Column == c.Column ? r.Width : c.Width > 0 ? c.Width : AutoWidth(c.Column, dateOnly: false);
            list.Add((c.Column, w, c.Width <= 0 && _resize?.Column != c.Column, false));
            if (c.Column == PanelColumn.Size && _packedSize is not null)
            {
                list.Add((null, _widths.Size, true, false));
            }
        }

        if (!gitPlaced && GitColumnShown(false) && _place?.Hide.Contains(PanelColumn.Git) != true)
        {
            list.Add((PanelColumn.Git, AutoWidth(PanelColumn.Git, dateOnly: false), true, false));
        }

        if (!smart)
        {
            var nameWidth = _resize is { Column: PanelColumn.Name } nr ? nr.Width : setup.NameWidth;
            var nameRight = NameX + nameWidth;
            var at = FolderAt(list, Math.Round(220 * Scale));
            var x = nameRight + 8;
            var placed = new Placed[list.Count];
            for (var i = 0; i < list.Count; i++)
            {
                placed[i] = new Placed(list[i].Column, x, list[i].Width, false, i == at);
                x += list[i].Width + gap;
            }

            var total = (list.Count > 0 ? x - gap : nameRight) + 6;
            var max = Math.Max(0, total - width);
            _hScroll = Math.Clamp(_hScroll, 0, max);
            return new ColumnLayout(nameRight, placed, max, false);
        }

        _hScroll = 0;
        double Needed() => BaseNameX + MarkWidth + MinName + 6 + list.Sum(l => l.Width + gap);
        while (Needed() > width)
        {
            var i = list.FindLastIndex(l => l.Column is not (PanelColumn.Size or PanelColumn.Extension or null));
            if (i >= 0)
            {
                var l = list[i];
                if (l.Column is PanelColumn.Modified or PanelColumn.Created or PanelColumn.Accessed && l.Auto && !l.DateOnly && !_format.IsRelative)
                {
                    list[i] = l with { Width = _widths.Date, DateOnly = true };
                }
                else
                {
                    list.RemoveAt(i);
                }

                continue;
            }

            i = list.FindIndex(l => l.Column is null);
            i = i >= 0 ? i : list.FindIndex(l => l.Column == PanelColumn.Extension);
            if (i < 0)
            {
                break;
            }

            list.RemoveAt(i);
        }

        var ext = list.FindIndex(l => l.Column == PanelColumn.Extension && l.Auto);
        if (ext >= 0)
        {
            var room = width - 6 - NameX - list.Where(l => l.Column != PanelColumn.Extension).Sum(l => l.Width + gap) - gap;
            list[ext] = list[ext] with { Width = Math.Min(list[ext].Width, Math.Max(_widths.Ext, room / 3)) };
        }

        var folderRoom = Math.Max(0, width - 6 - NameX - list.Sum(l => l.Width + gap));
        var folderAt = FolderAt(list, Math.Floor(_place is null ? folderRoom * 0.4 : _place.Share is { } share ? folderRoom * share : _place.Fill is null ? folderRoom * 0.5 : Math.Min(folderRoom * 0.6, Math.Max(folderRoom * 0.4, 230 * Scale))));

        var cols = new Placed[list.Count];
        var right = width - 6;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            cols[i] = new Placed(list[i].Column, right - list[i].Width, list[i].Width, list[i].DateOnly, i == folderAt);
            right = cols[i].X - gap;
        }

        return new ColumnLayout((list.Count > 0 ? cols[0].X : width - 6) - 8, cols, 0, true);
    }

    private int FolderAt(List<(PanelColumn? Column, double Width, bool Auto, bool DateOnly)> list, double width)
    {
        if (!FolderColumn || width < 40)
        {
            return -1;
        }

        var at = list.FindIndex(l => l.Column == PanelColumn.Extension) + 1;
        list.Insert(at, (null, width, false, false));
        return at;
    }

    private double AutoWidth(PanelColumn column, bool dateOnly) => column switch
    {
        PanelColumn.Extension => Math.Max(_widths.Ext, ExtFit()),
        PanelColumn.Type => TypeFit(),
        PanelColumn.Size => _widths.Size,
        PanelColumn.Items => _widths.Items,
        PanelColumn.Modified or PanelColumn.Created or PanelColumn.Accessed => _format.IsRelative ? _widths.Relative : dateOnly ? _widths.Date : _widths.Date + 8 + _widths.Time,
        PanelColumn.Attributes => _widths.Attr,
        PanelColumn.DosName => _widths.Dos,
        PanelColumn.Git => _widths.Git,
        _ => 0,
    };

    private double TypeFit()
    {
        if (_typeFit >= 0)
        {
            return _typeFit;
        }

        _typeFit = Math.Ceiling(Text(_typeHead, _face, _b.Text, _fontSize).WidthIncludingTrailingWhitespace);
        if (_panel is null)
        {
            return _typeFit;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _panel.Items)
        {
            if (seen.Count >= 64)
            {
                break;
            }

            if (seen.Add(e.IsDirectory ? "/" : e.Extension.ToString()))
            {
                _typeFit = Math.Max(_typeFit, Math.Ceiling(Text(TypeName(e), _face, _b.Text, _fontSize).WidthIncludingTrailingWhitespace));
            }
        }

        _typeFit = Math.Min(_typeFit, 260 * Scale);
        return _typeFit;
    }

    private double ScreenX(Placed c, ColumnLayout cols) => c.X - (cols.Smart ? 0 : _hScroll);

    private bool RightAligned(Placed c) => !c.Folder && RightAligned(c.Column);

    private bool RightAligned(PanelColumn? column)
        => _alignment == ColumnAlignment.NumbersRight && column is null or PanelColumn.Size or PanelColumn.Items or PanelColumn.Modified or PanelColumn.Created or PanelColumn.Accessed
            && !(column == PanelColumn.Size && _place?.SizeHead is not null);

    private static SortColumn? SortOf(PanelColumn? column) => column switch
    {
        PanelColumn.Name => SortColumn.Name,
        PanelColumn.Extension => SortColumn.Extension,
        PanelColumn.Type => SortColumn.Type,
        PanelColumn.Size => SortColumn.Size,
        PanelColumn.Modified => SortColumn.Time,
        PanelColumn.Created => SortColumn.Created,
        PanelColumn.Accessed => SortColumn.Accessed,
        _ => null,
    };

    private static string Caption(PanelColumn? column) => column switch
    {
        null => _packedHead,
        PanelColumn.Extension => _extHead,
        PanelColumn.Type => _typeHead,
        PanelColumn.Size => _sizeHead,
        PanelColumn.Items => _itemsHead,
        PanelColumn.Modified => _modifiedHead,
        PanelColumn.Created => _createdHead,
        PanelColumn.Accessed => _accessedHead,
        PanelColumn.Attributes => _attrHead,
        PanelColumn.DosName => _dosHead,
        PanelColumn.Git => _gitHead,
        _ => _nameHead,
    };

    private void DrawHeader(DrawingContext context, Size size, ColumnLayout cols)
    {
        var rowH = RowHeight;
        context.FillRectangle(_b.Header, new Rect(0, 0, size.Width, rowH));
        Controls.BackgroundLayer.DrawOnBar(this, context, new Rect(0, 0, size.Width, rowH));
        context.FillRectangle(_b.Border, new Rect(0, rowH - 1, size.Width, 1));
        var sort = _panel?.Sort ?? SortSpec.Default;
        var arrow = sort.Descending ? " ↓" : " ↑";
        var captionSize = Math.Max(9, _fontSize - 1);

        FormattedText Head(string label, SortColumn? column)
        {
            var sorted = column == sort.Column;
            return Text(sorted ? label + arrow : label, sorted ? _bold : _face, sorted ? _b.Text : _b.SecondaryText, captionSize);
        }

        var name = Head(_nameHead, SortColumn.Name);
        var y = (rowH - name.Height) / 2;
        context.DrawText(name, new Point(NameX, y));
        using (cols.Smart ? default(DrawingContext.PushedState?) : context.PushClip(new Rect(cols.NameRight + 4, 0, Math.Max(0, size.Width - cols.NameRight - 4), rowH)))
        {
            foreach (var c in cols.Columns)
            {
                var head = c.Folder ? Head(_place?.Head ?? _folderHead, null)
                    : c.Column == PanelColumn.Modified && _place?.DateHead is { } dateHead ? Head(dateHead, SortOf(c.Column))
                    : c.Column == PanelColumn.Size && _place?.SizeHead is { } sizeHead ? Head(sizeHead, null)
                    : Head(Caption(c.Column), SortOf(c.Column));
                head.MaxTextWidth = Math.Max(10, c.Width);
                head.MaxLineCount = 1;
                head.Trimming = TextTrimming.CharacterEllipsis;
                var x = ScreenX(c, cols);
                context.DrawText(head, new Point(RightAligned(c) ? x + c.Width - head.Width : x, y));
            }
        }

        if (_resize is { } r)
        {
            var tip = Text(Loc.Format(AppStrings.Panel_ColumnWidthTip, r.Column == PanelColumn.Name ? _nameHead : Caption(r.Column), (int)Math.Round(r.Width)), _face, _b.Text, captionSize);
            var box = new Rect(Math.Clamp(r.StartX, 4, Math.Max(4, size.Width - tip.Width - 20)), rowH + 4, tip.Width + 16, tip.Height + 8);
            context.DrawRectangle(_b.Background, new Avalonia.Media.Immutable.ImmutablePen(_b.Thumb.ToImmutable(), 1), box, 4, 4);
            context.DrawText(tip, new Point(box.X + 8, box.Y + 4));
        }
    }

    private void DrawHScrollBar(DrawingContext context, Size size, ColumnLayout cols)
    {
        if (cols.ScrollMax <= 0)
        {
            return;
        }

        var (x, w) = HThumb(cols);
        context.FillRectangle(_draggingHThumb ? _b.TertiaryText : _b.Thumb, new Rect(x + 2, size.Height - ScrollBarWidth + 3, w - 4, ScrollBarWidth - 6), 2);
    }

    private (double X, double Width) HThumb(ColumnLayout cols)
    {
        var track = Bounds.Width - ScrollBarWidth;
        var w = Math.Max(24, track * track / (track + cols.ScrollMax));
        return ((track - w) * _hScroll / cols.ScrollMax, w);
    }

    private void DragHThumb(double x)
    {
        var cols = ColumnsNow();
        var (_, w) = HThumb(cols);
        var track = Bounds.Width - ScrollBarWidth;
        ScrollSideways(((x - _dragOffset) / Math.Max(1, track - w) * cols.ScrollMax) - _hScroll);
    }

    private void ScrollSideways(double by)
    {
        var max = ColumnsNow().ScrollMax;
        var to = Math.Clamp(_hScroll + by, 0, max);
        if (to != _hScroll)
        {
            _hScroll = to;
            Scrolled?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    private Resize? EdgeAt(double x)
    {
        var cols = ColumnsNow();
        if (cols.Smart)
        {
            for (var i = 0; i < cols.Columns.Length; i++)
            {
                var c = cols.Columns[i];
                var edge = i == 0 ? (cols.NameRight + c.X) / 2 : c.X - (_widths.Gap / 2);
                if (c.Column is { } column && Math.Abs(x - edge) <= EdgeGrip)
                {
                    return new Resize(column, x, c.Width, GrowsLeft: true, c.Width);
                }
            }

            return null;
        }

        if (Math.Abs(x - (cols.NameRight + 4)) <= EdgeGrip)
        {
            return new Resize(PanelColumn.Name, x, cols.NameRight - NameX, GrowsLeft: false, cols.NameRight - NameX);
        }

        foreach (var c in cols.Columns)
        {
            var edge = ScreenX(c, cols) + c.Width + (_widths.Gap / 2);
            if (c.Column is { } column && edge > cols.NameRight + 8 && Math.Abs(x - edge) <= EdgeGrip)
            {
                return new Resize(column, x, c.Width, GrowsLeft: false, c.Width);
            }
        }

        return null;
    }

    private void HeaderPressed(PointerPressedEventArgs e, Point at)
    {
        if (EdgeAt(at.X) is { } edge)
        {
            if (e.ClickCount >= 2)
            {
                _resize = null;
                ColumnWidthChanged?.Invoke(this, new ColumnWidthChange(edge.Column, edge.Column == PanelColumn.Name ? (int)Math.Ceiling(LongestName()) + 8 : 0));
                return;
            }

            _resize = edge;
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        if (ColumnAt(at.X) is { } column)
        {
            _panel?.ToggleSort(column);
        }
    }

    private void DragEdge(double x)
    {
        if (_resize is not { } r)
        {
            return;
        }

        var min = r.Column == PanelColumn.Name ? Core.Panels.ColumnViews.MinNameWidth : Core.Panels.ColumnViews.MinWidth;
        var width = Math.Clamp(r.StartWidth + ((x - r.StartX) * (r.GrowsLeft ? -1 : 1)), min, Core.Panels.ColumnViews.MaxWidth);
        if (width != r.Width)
        {
            _resize = r with { Width = width };
            ClearTexts();
            InvalidateVisual();
        }
    }

    private void EndEdgeDrag()
    {
        if (_resize is { } r)
        {
            _resize = null;
            ColumnWidthChanged?.Invoke(this, new ColumnWidthChange(r.Column, (int)Math.Round(r.Width)));
            ClearTexts();
            InvalidateVisual();
        }
    }

    private double LongestName()
    {
        if (_panel is null)
        {
            return 200;
        }

        var longest = _panel.Items.Take(5000).Select(e => e.IsDirectory || !ColumnsNow().Shows(PanelColumn.Extension) ? e.Name : e.BaseName.ToString())
            .OrderByDescending(n => n.Length).Take(32);
        return longest.Select(n => Text(n, _face, _b.Text, _fontSize).WidthIncludingTrailingWhitespace).DefaultIfEmpty(120).Max();
    }

    private SortColumn? ColumnAt(double x)
    {
        var cols = ColumnsNow();
        if (x < cols.NameRight + 4)
        {
            return SortColumn.Name;
        }

        foreach (var c in cols.Columns)
        {
            var left = ScreenX(c, cols);
            if (x >= left - (_widths.Gap / 2) && x <= left + c.Width + (_widths.Gap / 2))
            {
                return c.Folder ? null : SortOf(c.Column);
            }
        }

        return null;
    }

    private void UpdateDateTip(Point p, int row)
    {
        string? tip = null;
        if (_format.IsRelative && IsDetailed && row >= 0 && _panel is { } panel && !panel.IsUp(row))
        {
            var cols = ColumnsNow();
            foreach (var c in cols.Columns)
            {
                var x = ScreenX(c, cols);
                if (p.X >= x && p.X <= x + c.Width && Ticks(panel.Items[row], c.Column) is > 0 and var ticks)
                {
                    tip = _format.DateAndTime(new DateTime(ticks, DateTimeKind.Utc).ToLocalTime());
                }
            }
        }

        if (tip != _dateTip)
        {
            _dateTip = tip;
            ToolTip.SetTip(this, tip);
        }
    }

    private static long Ticks(in FileEntry e, PanelColumn? column) => column switch
    {
        PanelColumn.Modified => e.LastWriteUtcTicks,
        PanelColumn.Created => e.CreationUtcTicks,
        PanelColumn.Accessed => e.LastAccessUtcTicks,
        _ => 0,
    };

    private sealed record RowText(FormattedText Name, string NameShown, FormattedText?[] Cells, FormattedText?[] Times, string?[] Texts);

    private void DrawRowCells(DrawingContext context, int i, double y, ColumnLayout cols, Size size)
    {
        var t = GetRowText(i, cols);
        var rowH = RowHeight;
        var ty = y + ((rowH - t.Name.Height) / 2);
        context.DrawText(t.Name, new Point(NameX, ty));
        if ((_panel!.Items[i].Attributes & FileAttributes.Encrypted) != 0 && !_panel.IsUp(i))
        {
            AppIcons.Draw(context, "Icon.Lock", new Point(NameX + t.Name.Width + 3, y + ((rowH - 12) / 2)), _b.LockPen, scale: 0.75);
        }

        using (cols.Smart ? default(DrawingContext.PushedState?) : context.PushClip(new Rect(cols.NameRight + 4, y, Math.Max(0, size.Width - cols.NameRight - 4), rowH)))
        {
            for (var c = 0; c < cols.Columns.Length; c++)
            {
                var col = cols.Columns[c];
                var x = ScreenX(col, cols);
                if (col.Folder && _place?.Fill?.Invoke(_panel!.Items[i]) is { } fill)
                {
                    var bar = new Rect(x, y + ((rowH - 6) / 2), Math.Min(PlaceBarWidth, col.Width), 6);
                    context.DrawRectangle(_b.Border, null, bar, 3, 3);
                    context.DrawRectangle(fill >= 0.9 ? _b.Folder : _b.Accent, null, bar.WithWidth(bar.Width * Math.Clamp(fill, 0, 1)), 3, 3);
                    x += PlaceBarWidth + 8;
                }

                if (t.Cells[c] is not { } cell)
                {
                    continue;
                }

                if (t.Times[c] is { } time)
                {
                    if (RightAligned(col))
                    {
                        context.DrawText(cell, new Point(x + col.Width - _widths.Time - 8 - cell.Width, ty));
                        context.DrawText(time, new Point(x + col.Width - time.Width, ty));
                    }
                    else
                    {
                        context.DrawText(cell, new Point(x, ty));
                        context.DrawText(time, new Point(x + _widths.Date + 8, ty));
                    }

                    continue;
                }

                if (col.Column == PanelColumn.Git && GitTag(i) is { } tag)
                {
                    var w = Math.Min(col.Width, Math.Max(cell.Width + GitTagPadding, rowH - 6));
                    var tx = RightAligned(col) ? x + col.Width - w : x;
                    var r = new Rect(tx, y + 3, w, Math.Max(cell.Height, rowH - 6));
                    context.DrawRectangle(tag, null, r, Salandual.App.Theme.Corners.ChipRadius, Salandual.App.Theme.Corners.ChipRadius);
                    context.DrawText(cell, new Point(tx + ((w - cell.Width) / 2), ty));
                    continue;
                }

                context.DrawText(cell, new Point(RightAligned(col) ? x + col.Width - cell.Width : x, ty));
            }
        }
    }

    private RowText GetRowText(int index, ColumnLayout cols)
    {
        if (_cache.TryGetValue(index, out var t))
        {
            return t;
        }

        var panel = _panel!;
        var e = panel.Items[index];
        var up = panel.IsUp(index);
        var selected = panel.IsSelected(index);
        var cut = !up && IsCut?.Invoke(e.Name) == true;
        var (nameBrush, nameFace) = NameLook(e, up, selected, cut);
        var dim = RowInk(e, up, selected, cut);

        var showExt = cols.Shows(PanelColumn.Extension);
        var nameText = up || e.IsDirectory || !showExt || panel.IsHistory || panel.IsTags ? e.Name : e.BaseName.ToString();
        var heading = panel.IsHeading(index);
        var folderPart = 0;
        if (heading)
        {
            nameText = e.Name;
            nameFace = _bold;
            nameBrush = _b.SecondaryText;
        }
        else if (panel.IsFlat && panel.FlatFolderShows != FlatFolderShows.Column)
        {
            folderPart = panel.FlatFolderShows == FlatFolderShows.InName && FilePanel.SplitResult(e.Name).Folder is { Length: > 0 } f ? f.Length + 1 : 0;
            nameText = folderPart > 0 ? nameText : FilePanel.SplitResult(nameText).Name;
        }
        else if (panel.Results is not null && !panel.IsGitList)
        {
            nameText = FilePanel.SplitResult(nameText).Name;
        }

        var nameWidth = Math.Max(10, cols.NameRight - NameX);
        var shown = _longNames == LongNames.CutMiddle ? CutInTheMiddle(nameText, nameBrush, nameWidth) : nameText;
        var name = Text(shown, nameFace, nameBrush, _fontSize);
        name.MaxTextWidth = nameWidth;
        name.Trimming = TextTrimming.CharacterEllipsis;
        name.MaxLineCount = 1;
        if (folderPart > 0 && shown == nameText)
        {
            name.SetForegroundBrush(dim, 0, folderPart);
        }

        var cells = new FormattedText?[cols.Columns.Length];
        var times = new FormattedText?[cols.Columns.Length];
        var texts = new string?[cols.Columns.Length];
        for (var c = 0; c < cols.Columns.Length; c++)
        {
            var col = cols.Columns[c];
            if (col.Folder)
            {
                texts[c] = up || heading ? "" : _place is { } place ? place.Text(e) ?? "" : FilePanel.SplitResult(e.Name).Folder;
                if (texts[c]!.Length > 0)
                {
                    cells[c] = Cell(texts[c]!, dim, _place?.Fill?.Invoke(e) is not null ? col.Width - PlaceBarWidth - 8 : col.Width);
                    cells[c]!.Trimming = TextTrimming.PrefixCharacterEllipsis;
                }

                continue;
            }

            string? second = null;
            var text = up ? (col.Column == PanelColumn.Size ? _upText : "") : heading ? "" : col.Column switch
            {
                null => !e.IsDirectory && _packedSize?.Invoke(e) is { } packed ? _format.ColumnSize(packed) : "",
                PanelColumn.Extension => e.IsDirectory || panel.IsHistory || panel.IsTags ? "" : e.Extension.ToString(),
                PanelColumn.Type => TypeName(e),
                PanelColumn.Size => _place?.Size?.Invoke(e) ?? SizeText(panel, e, up),
                PanelColumn.Items => ItemsText(panel, e),
                PanelColumn.Attributes => DisplayFormat.Attributes(e.Attributes),
                PanelColumn.DosName => _packedSize is not null ? "" : _facts?.DosName(panel.Path, e) ?? "…",
                PanelColumn.Git => GitLetterOf(e, up),
                _ => DateText(Ticks(e, col.Column), col.DateOnly || col.Width < _widths.Date + 8 + _widths.Time, out second),
            };
            if (text.Length > 0)
            {
                cells[c] = Cell(text, dim, second is null ? col.Width : Math.Min(col.Width, _widths.Date));
            }

            if (second is not null)
            {
                times[c] = Cell(second, dim, _widths.Time + 4);
            }

            texts[c] = second is null ? text : text + " " + second;
        }

        t = new RowText(name, shown, cells, times, texts);
        _cache[index] = t;
        return t;
    }

    private FormattedText Cell(string text, IBrush brush, double width)
    {
        var cell = Text(text, _face, brush, _fontSize);
        cell.MaxTextWidth = Math.Max(10, width);
        cell.Trimming = TextTrimming.CharacterEllipsis;
        cell.MaxLineCount = 1;
        return cell;
    }

    private string DateText(long ticks, bool dateOnly, out string? time)
    {
        time = null;
        if (ticks <= 0)
        {
            return "";
        }

        var local = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
        if (_format.IsRelative)
        {
            return _format.ColumnDate(local, DateTime.Now);
        }

        if (!dateOnly)
        {
            time = _format.Time(local);
        }

        return _format.Date(local);
    }

    private string ItemsText(FilePanel panel, in FileEntry e)
    {
        if (!e.IsDirectory || _packedSize is not null || _facts is not { } facts)
        {
            return "";
        }

        return facts.Items(panel.Path, e, panel.ShowHidden, out var counting) is { } n ? n >= 0 ? _format.Number(n) : "" : counting ? "…" : "";
    }

    private string SizeText(FilePanel panel, in FileEntry e, bool up)
        => up ? _upText
            : !e.IsDirectory ? _format.ColumnSize(e.Size)
            : panel.TryGetDirectorySize(e, out var size) ? _format.ColumnSize(size)
            : panel.IsSizing(e) ? "…"
            : _folderSize switch { FolderSizeText.Dir => _dirText, FolderSizeText.Blank => "", _ => _folderText };

    private string CutInTheMiddle(string name, IBrush brush, double width)
    {
        if (name.Length < 8 || Text(name, _face, brush, _fontSize).WidthIncludingTrailingWhitespace <= width)
        {
            return name;
        }

        var dot = name.LastIndexOf('.');
        var tail = Math.Min(name.Length / 2, dot > 0 && name.Length - dot <= 8 ? name.Length - dot + 3 : 6);
        int lo = 1, hi = name.Length - tail - 1, best = 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (Text(name[..mid] + "…" + name[^tail..], _face, brush, _fontSize).WidthIncludingTrailingWhitespace <= width)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return name[..best] + "…" + name[^tail..];
    }

    private bool FolderColumn => _place is not null || _panel is { Results: not null } p && (!p.IsFlat || p.FlatFolderShows == FlatFolderShows.Column);

    private const double PlaceBarWidth = 60;
}
