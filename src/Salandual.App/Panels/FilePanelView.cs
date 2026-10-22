// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/FilePanelView.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Salandual.App.Input;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Commands;
using Salandual.Core.Compare;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace Salandual.App.Panels;

public sealed class FilePanelView : Control
{
    private static readonly string _nameHead = AppStrings.Panel_Col_Name, _extHead = AppStrings.Panel_Col_Ext, _sizeHead = AppStrings.Panel_Col_Size,
        _dateHead = AppStrings.Panel_Col_Date, _timeHead = AppStrings.Panel_Col_Time, _attrHead = AppStrings.Panel_Col_Attr,
        _upText = AppStrings.Panel_Up, _folderText = AppStrings.InfoLine_Folder, _folderHead = AppStrings.Panel_Col_Folder;

    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<FilePanelView, double>(nameof(RowHeight), 24);

    public static readonly StyledProperty<bool> ShowHeaderProperty =
        AvaloniaProperty.Register<FilePanelView, bool>(nameof(ShowHeader), true);

    public static readonly StyledProperty<bool> IsActivePanelProperty =
        AvaloniaProperty.Register<FilePanelView, bool>(nameof(IsActivePanel), true);

    private const double ScrollBarWidth = 10;
    private const double IconSize = 16;
    private const double BaseNameX = 34;

    private const double MarkWidth = 14;

    private readonly Dictionary<int, RowText> _cache = [];
    private FilePanel? _panel;
    private Brushes _b = null!;
    private Typeface _face;
    private Typeface _bold;
    private double _fontSize = 13;
    private DisplayFormat _format = DisplayFormat.Default;
    private ColumnWidths _widths;
    private bool _themeValid;
    private double _cachedWidth = -1;
    private double _cachedShift;
    private readonly Dictionary<DifferenceKind, FormattedText> _markTexts = [];
    private int _top;
    private int _hover = -1;
    private bool _draggingThumb;
    private double _dragOffset;
    private bool _suppressText;
    private Theme.BackgroundImageService? _image;
    private IconCache? _icons;
    private Avalonia.Threading.DispatcherTimer? _slowClick;
    private Point _slowClickAt;
    private (int Row, Point At)? _rightPress;
    private (bool Select, int LastRow)? _rightSelect;
    private Avalonia.Threading.DispatcherTimer? _rightHold;
    private (int Row, Point At)? _leftPress;
    private int _dropRow = NoDrop;

    static FilePanelView()
    {
        FocusableProperty.OverrideDefaultValue<FilePanelView>(true);
        AffectsRender<FilePanelView>(RowHeightProperty, ShowHeaderProperty, IsActivePanelProperty);
    }

    public FilePanelView()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateTheme();
        ClipToBounds = true;
    }

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public bool ShowHeader
    {
        get => GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }

    public bool IsActivePanel
    {
        get => GetValue(IsActivePanelProperty);
        set => SetValue(IsActivePanelProperty, value);
    }

    private double HeaderHeight => ShowHeader ? RowHeight : 0;

    public event EventHandler? Activated;

    public CommandRegistry? Commands { get; set; }

    public DisplayFormat Format
    {
        get => _format;
        set
        {
            _format = value;
            InvalidateTheme();
        }
    }

    public event EventHandler<CommandRequest>? UnhandledCommand;

    public event EventHandler? RenameRequested;

    public event EventHandler? Scrolled;

    public bool ClickToRename { get; set; } = true;

    public bool ExtensionColumn
    {
        get => _extensionColumn;
        set
        {
            if (_extensionColumn != value)
            {
                _extensionColumn = value;
                _cache.Clear();
                InvalidateVisual();
            }
        }
    }

    private bool _extensionColumn = true;

    private double _extFit = -1;

    internal (double NameRight, double ExtRight, double SizeLeft, double ExtWidth) RowLayout(int index)
    {
        EnsureTheme();
        var cols = ColumnsFor(Bounds.Width - ScrollBarWidth);
        var t = GetRowText(index, cols);
        return (NameX + t.Name.Width, cols.ExtX + t.Ext.Width, cols.SizeRight - cols.SizeWidth, cols.ShowExt ? cols.ExtWidth : 0);
    }

    private Columns ColumnsFor(double width) => Columns.For(width, _widths with { ExtFit = ExtFit() }, _extensionColumn);

    private double ExtFit()
    {
        if (_extFit >= 0)
        {
            return _extFit;
        }

        _extFit = _widths.Ext;
        if (_panel is null)
        {
            return _extFit;
        }

        var longest = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in _panel.Items)
        {
            if (!e.IsDirectory && e.Extension.Length > 6)
            {
                longest.Add(e.Extension.ToString());
            }
        }

        foreach (var ext in longest.OrderByDescending(x => x.Length).Take(32))
        {
            _extFit = Math.Max(_extFit, Math.Ceiling(Text(ext, _face, _b.Text, _fontSize).WidthIncludingTrailingWhitespace));
        }

        return _extFit;
    }

    public event EventHandler<PanelMenuRequest>? MenuRequested;

    public bool RightButtonSelects { get; set; }

    public bool QuickSearchWithCtrlAlt { get; set; }

    public event EventHandler<PanelDragRequest>? DragRequested;

    public const int NoDrop = -1;

    public const int DropOnPanel = -2;

    public int DropRow
    {
        get => _dropRow;
        set
        {
            if (_dropRow != value)
            {
                _dropRow = value;
                InvalidateVisual();
            }
        }
    }

    public int RowAt(Point p) => HitTest(p) is (Zone.Row, var row) ? row : -1;

    public Func<string, bool>? IsCut { get; set; }

    public void InvalidateRows()
    {
        _cache.Clear();
        InvalidateVisual();
    }

    public FilePanel? Panel
    {
        get => _panel;
        set
        {
            if (_panel is not null)
            {
                _panel.Changed -= OnPanelChanged;
            }

            _panel = value;
            if (_panel is not null)
            {
                _panel.Changed += OnPanelChanged;
            }

            SetTop(value?.TopIndex ?? 0);
            _cache.Clear();
            InvalidateVisual();
        }
    }

    public int PageRows => Math.Max(1, (int)((Bounds.Height - HeaderHeight) / RowHeight));

    public int Top => _top;

    public void InvalidateTheme()
    {
        _themeValid = false;
        _extFit = -1;
        _cache.Clear();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowHeightProperty)
        {
            EnsureVisible();
        }
    }

    private void OnPanelChanged(object? sender, PanelChange change)
    {
        if ((change & (PanelChange.Items | PanelChange.Selection | PanelChange.Sizes)) != 0)
        {
            _cache.Clear();
        }

        if ((change & PanelChange.Items) != 0)
        {
            _extFit = -1;
            _hover = -1;
            CancelSlowClick();
            SetTop(_panel!.TakeFocusScreenRow() is { } row ? _panel.Focus - Math.Clamp(row, 0, PageRows - 1) : _top);
        }

        if ((change & PanelChange.Focus) != 0)
        {
            EnsureVisible();
        }

        InvalidateVisual();
    }

    private int Count => _panel?.Count ?? 0;

    private int MaxTop => Math.Max(0, Count - PageRows);

    public void ScrollTo(int top)
    {
        top = Math.Clamp(top, 0, MaxTop);
        if (top != _top)
        {
            SetTop(top);
            InvalidateVisual();
        }
    }

    private void SetTop(int top)
    {
        var before = _top;
        _top = Math.Clamp(top, 0, MaxTop);
        if (_top != before)
        {
            Scrolled?.Invoke(this, EventArgs.Empty);
        }

        if (_panel is not null)
        {
            _panel.TopIndex = _top;
        }
    }

    public void EnsureFocusVisible() => EnsureVisible();

    private void EnsureVisible()
    {
        if (_panel is null)
        {
            return;
        }

        var focus = _panel.Focus;
        if (focus < _top)
        {
            ScrollTo(focus);
        }
        else if (focus >= _top + PageRows)
        {
            ScrollTo(focus - PageRows + 1);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        SetTop(_top);
        EnsureVisible();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        Activated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Application.Current is App { Theme: { } theme })
        {
            theme.Applied += OnThemeApplied;
        }

        if (Application.Current is App { BackgroundImage: { } image })
        {
            _image = image;
            image.Changed += OnImageChanged;
        }

        if (Application.Current is App { Icons: { } icons })
        {
            _icons = icons;
            icons.Changed += OnImageChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current is App { Theme: { } theme })
        {
            theme.Applied -= OnThemeApplied;
        }

        if (_image is not null)
        {
            _image.Changed -= OnImageChanged;
            _image = null;
        }

        if (_icons is not null)
        {
            _icons.Changed -= OnImageChanged;
            _icons = null;
        }
    }

    private void OnThemeApplied(object? sender, EventArgs e) => InvalidateTheme();

    private void OnImageChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        CancelSlowClick();
        _suppressText = false;
        if (_panel is null || e.Handled || !KeyMapping.TryGetChord(e.Key, e.KeyModifiers, out var chord))
        {
            return;
        }

        if (QuickSearchWithCtrlAlt && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt) && e.Key is >= Key.A and <= Key.Z)
        {
            _panel.QuickSearchType((char)('a' + (e.Key - Key.A)));
            e.Handled = _suppressText = true;
            return;
        }

        if (_panel.QuickSearchText is not null)
        {
            if (HandleQuickSearchKey(e))
            {
                e.Handled = _suppressText = true;
                return;
            }

            if (_panel.QuickSearchText is not null && IsTypingKey(e))
            {
                return;
            }
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _panel.CancelSizing())
        {
            e.Handled = true;
            return;
        }

        var id = Commands?.Resolve(chord);
        if (id is null)
        {
            return;
        }

        if (TryRunCommand(id))
        {
            e.Handled = _suppressText = true;
        }
        else if (UnhandledCommand is { } handler)
        {
            var request = new CommandRequest(id) { Chord = chord };
            handler(this, request);
            e.Handled = _suppressText = request.Handled;
        }
    }

    public bool TryRunCommand(string id)
    {
        if (_panel is null)
        {
            return false;
        }

        switch (id)
        {
            case "Panel.ScrollUp":
                ScrollTo(_top - 1);
                return true;
            case "Panel.ScrollDown":
                ScrollTo(_top + 1);
                return true;
            case "Panel.ScrollLeft" or "Panel.ScrollRight":
                return true;
            default:
                return _panel!.Execute(id, PageRows);
        }
    }

    private bool HandleQuickSearchKey(KeyEventArgs e)
    {
        var panel = _panel!;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (ctrl)
        {
            panel.EndQuickSearch();
            return false;
        }

        var page = Math.Max(1, PageRows - 1);
        switch (e.Key)
        {
            case Key.Escape: panel.EndQuickSearch(); return true;
            case Key.Up: panel.QuickSearchMove(-1, shift, alt); return true;
            case Key.Down: panel.QuickSearchMove(1, shift, alt); return true;
            case Key.PageUp: panel.QuickSearchMove(-page, shift, alt); return true;
            case Key.PageDown: panel.QuickSearchMove(page, shift, alt); return true;
            case Key.Home: panel.QuickSearchMove(int.MinValue, shift, alt); return true;
            case Key.End: panel.QuickSearchMove(int.MaxValue, shift, alt); return true;
            case Key.Left or Key.Back when !shift && !alt: panel.QuickSearchBack(); return true;
            case Key.Right when !shift && !alt: panel.QuickSearchExtend(); return true;
            default:
                if (IsTypingKey(e))
                {
                    return false;
                }

                panel.EndQuickSearch();
                return false;
        }
    }

    private static bool IsTypingKey(KeyEventArgs e)
        => (e.KeyModifiers & ~KeyModifiers.Shift) == 0
           && e.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9 or >= Key.Oem1 and <= Key.Oem102;

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_panel is null || _suppressText || string.IsNullOrEmpty(e.Text) || QuickSearchWithCtrlAlt && _panel.QuickSearchText is null)
        {
            _suppressText = false;
            return;
        }

        foreach (var c in e.Text)
        {
            if (!char.IsControl(c) && c != ' ')
            {
                _panel.QuickSearchType(c);
            }
        }

        e.Handled = true;
    }

    private enum Zone { None, Header, Row, ScrollBar }

    private (Zone Zone, int Row) HitTest(Point p)
    {
        if (p.Y < HeaderHeight)
        {
            return (Zone.Header, -1);
        }

        if (p.X >= Bounds.Width - ScrollBarWidth && Count > PageRows)
        {
            return (Zone.ScrollBar, -1);
        }

        var row = _top + (int)((p.Y - HeaderHeight) / RowHeight);
        return row < Count ? (Zone.Row, row) : (Zone.None, -1);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var hadFocus = IsFocused;
        CancelSlowClick();
        Focus();
        if (_panel is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var (zone, row) = HitTest(point.Position);
        _panel.EndQuickSearch();
        _leftPress = null;
        if (point.Properties.IsRightButtonPressed && zone is Zone.Row or Zone.None)
        {
            RightPressed(zone == Zone.Row ? row : -1, point.Position);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        switch (zone)
        {
            case Zone.Header when point.Properties.IsLeftButtonPressed:
                if (ColumnAt(point.Position.X) is { } column)
                {
                    _panel.ToggleSort(column);
                }

                break;
            case Zone.ScrollBar when point.Properties.IsLeftButtonPressed:
                var (thumbY, thumbH) = Thumb();
                var y = point.Position.Y;
                _dragOffset = y >= thumbY && y <= thumbY + thumbH ? y - thumbY : thumbH / 2;
                _draggingThumb = true;
                e.Pointer.Capture(this);
                DragThumb(y);
                break;
            case Zone.Row:
                var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
                var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
                if (point.Properties.IsLeftButtonPressed && e.ClickCount == 2)
                {
                    _panel.SetFocus(row);
                    _ = _panel.OpenFocusedAsync();
                }
                else if (point.Properties.IsLeftButtonPressed && ctrl)
                {
                    _panel.Toggle(row);
                    _panel.SetFocus(row);
                }
                else if (point.Properties.IsLeftButtonPressed && shift)
                {
                    _panel.ExtendTo(row, includeTarget: true);
                }
                else
                {
                    var wasFocused = row == _panel.Focus;
                    _panel.SetFocus(row);
                    if (point.Properties.IsLeftButtonPressed && e.ClickCount == 1 && wasFocused && hadFocus && ClickToRename && !_panel.IsUp(row) && IsOnName(row, point.Position))
                    {
                        StartSlowClick(point.Position);
                    }
                }

                if (point.Properties.IsLeftButtonPressed && e.ClickCount == 1 && !_panel.IsUp(row))
                {
                    _leftPress = (row, point.Position);
                }

                break;
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_draggingThumb)
        {
            DragThumb(p.Y);
            return;
        }

        if (_slowClick is not null && (Math.Abs(p.X - _slowClickAt.X) > 4 || Math.Abs(p.Y - _slowClickAt.Y) > 4))
        {
            CancelSlowClick();
        }

        if (_leftPress is { } left)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _leftPress = null;
            }
            else if (Moved(left.At, p))
            {
                _leftPress = null;
                CancelSlowClick();
                e.Pointer.Capture(null);
                DragRequested?.Invoke(this, new PanelDragRequest(left.Row, RightButton: false));
                return;
            }
        }

        if (_rightPress is { Row: >= 0 } right && !RightButtonSelects && Moved(right.At, p) && _panel is not null && !_panel.IsUp(right.Row))
        {
            _rightPress = null;
            e.Pointer.Capture(null);
            DragRequested?.Invoke(this, new PanelDragRequest(right.Row, RightButton: true));
            return;
        }

        if (_rightPress is { } press && (Math.Abs(p.X - press.At.X) > 4 || Math.Abs(p.Y - press.At.Y) > 4))
        {
            _rightHold?.Stop();
            _rightHold = null;
            if (_rightSelect is { } sel && _panel is not null && HitTest(p) is (Zone.Row, var over) && over != sel.LastRow)
            {
                if (_panel.IsSelected(over) != sel.Select && !_panel.IsUp(over))
                {
                    _panel.Toggle(over);
                }

                _panel.SetFocus(over);
                _rightSelect = sel with { LastRow = over };
            }
        }

        var (zone, row) = HitTest(p);
        var hover = zone == Zone.Row ? row : -1;
        if (hover != _hover)
        {
            _hover = hover;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _leftPress = null;
        if (_draggingThumb)
        {
            _draggingThumb = false;
            e.Pointer.Capture(null);
        }

        if (e.InitialPressMouseButton == MouseButton.Right && _rightPress is { } press)
        {
            _rightHold?.Stop();
            _rightHold = null;
            _rightPress = null;
            var selecting = _rightSelect is not null;
            _rightSelect = null;
            e.Pointer.Capture(null);
            if (!selecting)
            {
                MenuRequested?.Invoke(this, new PanelMenuRequest(press.Row, press.At, (e.KeyModifiers & KeyModifiers.Shift) != 0));
            }

            e.Handled = true;
        }
    }

    private void RightPressed(int row, Point at)
    {
        _rightPress = (row, at);
        if (!RightButtonSelects || _panel is null || row < 0)
        {
            return;
        }

        if (!_panel.IsUp(row))
        {
            _panel.Toggle(row);
        }

        _panel.SetFocus(row);
        _rightSelect = (_panel.IsSelected(row), row);
        _rightHold = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _rightHold.Tick += (_, _) =>
        {
            _rightHold?.Stop();
            _rightHold = null;
            _rightSelect = null;
            _rightPress = null;
            MenuRequested?.Invoke(this, new PanelMenuRequest(row, at, Shift: false, ForSelection: true));
        };
        _rightHold.Start();
    }

    private static bool Moved(Point from, Point to) => Math.Abs(to.X - from.X) > 4 || Math.Abs(to.Y - from.Y) > 4;

    internal void ElapseRightHold()
    {
        if (_rightHold is { } timer)
        {
            timer.Stop();
            _rightHold = null;
            _rightSelect = null;
            var press = _rightPress;
            _rightPress = null;
            if (press is { } p)
            {
                MenuRequested?.Invoke(this, new PanelMenuRequest(p.Row, p.At, Shift: false, ForSelection: true));
            }
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover >= 0)
        {
            _hover = -1;
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ScrollTo(_top - (int)Math.Round(e.Delta.Y * 3));
        e.Handled = true;
    }

    public Rect? NameBounds(int index)
    {
        if (index < _top || index >= _top + PageRows || index >= Count)
        {
            return null;
        }

        EnsureTheme();
        var cols = ColumnsFor(Bounds.Width - ScrollBarWidth);
        var right = _panel?.Results is not null ? FolderX(cols) - 8 : cols.SizeRight - cols.SizeWidth - 6;
        return new Rect(NameX - 5, HeaderHeight + (RowHeight * (index - _top)), Math.Max(120, right - NameX + 5), RowHeight);
    }

    private bool IsOnName(int row, Point p)
    {
        var cols = ColumnsFor(Bounds.Width - ScrollBarWidth);
        var text = GetRowText(row, cols).Name;
        return p.X >= NameX - 2 && p.X <= NameX + text.Width + 4;
    }

    private void StartSlowClick(Point at)
    {
        _slowClickAt = at;
        _slowClick = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        _slowClick.Tick += (_, _) =>
        {
            CancelSlowClick();
            if (IsFocused)
            {
                RenameRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        _slowClick.Start();
    }

    private void CancelSlowClick()
    {
        _slowClick?.Stop();
        _slowClick = null;
    }

    private (double Y, double Height) Thumb()
    {
        var track = Bounds.Height - HeaderHeight;
        var height = Math.Max(24, track * PageRows / Math.Max(1, Count));
        var y = HeaderHeight + ((track - height) * _top / Math.Max(1, MaxTop));
        return (y, height);
    }

    private void DragThumb(double y)
    {
        var track = Bounds.Height - HeaderHeight;
        var (_, height) = Thumb();
        var fraction = (y - _dragOffset - HeaderHeight) / Math.Max(1, track - height);
        ScrollTo((int)Math.Round(fraction * MaxTop));
    }

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var size = Bounds.Size;
        if (size.Width != _cachedWidth || MarkShift != _cachedShift)
        {
            _cache.Clear();
            _cachedWidth = size.Width;
            _cachedShift = MarkShift;
        }

        var cols = ColumnsFor(size.Width - ScrollBarWidth);
        var rowH = RowHeight;
        context.FillRectangle(_b.Background, new Rect(size));
        if (_image?.Image is not null)
        {
            var rows = new Rect(0, HeaderHeight, size.Width, Math.Max(0, size.Height - HeaderHeight));
            var area = _image.Settings.Placement == BackgroundPlacement.Window ? Controls.BackgroundLayer.WindowArea(this) : new Rect(size);
            _image.Draw(context, area, rows);
        }

        if (ShowHeader)
        {
            DrawHeader(context, size, cols);
        }

        if (_panel is null)
        {
            return;
        }

        var last = Math.Min(Count, _top + PageRows + 1);
        var iconPx = (int)Math.Round(IconSize * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
        var active = IsActivePanel;
        using (context.PushClip(new Rect(0, HeaderHeight, size.Width, Math.Max(0, size.Height - HeaderHeight))))
        {
            for (var i = _top; i < last; i++)
            {
                var y = HeaderHeight + (rowH * (i - _top));
                var rowRect = new Rect(4, y, Math.Max(0, cols.Right - 4), rowH);
                var selected = _panel.IsSelected(i);
                if (i == _dropRow)
                {
                    context.FillRectangle(_b.SelectionBackground, rowRect, 4);
                    context.DrawRectangle(null, _b.DropPen, rowRect.Deflate(1), 4, 4);
                }
                else if (selected)
                {
                    context.FillRectangle(_b.SelectionBackground, rowRect, 4);
                }
                else if (i == _hover)
                {
                    context.FillRectangle(_b.Hover, rowRect, 4);
                }

                using (IsCut is { } isCut && !_panel.IsUp(i) && isCut(_panel.Items[i].Name) ? context.PushOpacity(0.45) : default(DrawingContext.PushedState?))
                {
                    DrawIcon(context, _panel.Items[i], _panel.IsUp(i), y + ((rowH - IconSize) / 2), iconPx);
                }

                if (_panel.CompareMarks is { } marks && !_panel.IsUp(i) && marks.TryGetValue(_panel.Items[i].Name, out var mark))
                {
                    var glyph = MarkText(mark);
                    context.DrawText(glyph, new Point(8 + ((MarkWidth - glyph.Width) / 2), y + ((rowH - glyph.Height) / 2)));
                }

                var t = GetRowText(i, cols);
                var ty = y + ((rowH - t.Name.Height) / 2);
                context.DrawText(t.Name, new Point(NameX, ty));
                if (t.Folder is { } folder)
                {
                    context.DrawText(folder, new Point(FolderX(cols), ty));
                }

                if (cols.ShowExt)
                {
                    context.DrawText(t.Ext, new Point(cols.ExtX, ty));
                }

                context.DrawText(t.Size, new Point(cols.SizeRight - t.Size.Width, ty));
                if (cols.ShowDate)
                {
                    context.DrawText(t.Date, new Point(cols.DateRight - t.Date.Width, ty));
                }

                if (cols.ShowTime)
                {
                    context.DrawText(t.Time, new Point(cols.TimeRight - t.Time.Width, ty));
                }

                if (cols.ShowAttr)
                {
                    context.DrawText(t.Attr, new Point(cols.AttrX, ty));
                }

                if (i == _panel.Focus)
                {
                    context.DrawRectangle(null, active ? _b.FocusPen : _b.FocusPenInactive, rowRect.Deflate(0.75), 4, 4);
                }
            }

            if (Count == (_panel.HasUpEntry ? 1 : 0) && _panel.Path.Length > 0)
            {
                var empty = Text(AppStrings.Panel_Empty, _face, _b.TertiaryText, _fontSize);
                context.DrawText(empty, new Point((size.Width - empty.Width) / 2, HeaderHeight + (rowH * (Count + 0.5))));
            }
        }

        if (_dropRow == DropOnPanel)
        {
            context.DrawRectangle(null, _b.DropPen, new Rect(size).Deflate(1), 4, 4);
        }

        DrawScrollBar(context, size);
        DrawQuickSearch(context, size);
        EvictCache();
    }

    private void DrawHeader(DrawingContext context, Size size, Columns cols)
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
        if (_panel?.Results is not null)
        {
            context.DrawText(Head(_folderHead, null), new Point(FolderX(cols), y));
        }

        if (cols.ShowExt)
        {
            context.DrawText(Head(_extHead, SortColumn.Extension), new Point(cols.ExtX, y));
        }

        var s = Head(_sizeHead, SortColumn.Size);
        context.DrawText(s, new Point(cols.SizeRight - s.Width, y));
        if (cols.ShowDate)
        {
            var d = Head(_dateHead, SortColumn.Time);
            context.DrawText(d, new Point(cols.DateRight - d.Width, y));
        }

        if (cols.ShowTime)
        {
            var t = Head(_timeHead, null);
            context.DrawText(t, new Point(cols.TimeRight - t.Width, y));
        }

        if (cols.ShowAttr)
        {
            context.DrawText(Head(_attrHead, null), new Point(cols.AttrX, y));
        }
    }

    private void DrawIcon(DrawingContext context, in FileEntry entry, bool up, double y, int iconPx)
    {
        var x = 10.0 + MarkShift;
        if (!up && _icons?.Get(_panel!.Path, entry, iconPx) is { } icon)
        {
            context.DrawImage(icon, new Rect(x, y, IconSize, IconSize));
        }
        else if (up)
        {
            AppIcons.Draw(context, "Icon.ParentFolder", new Point(x, y), _b.IconPen);
        }
        else
        {
            AppIcons.Draw(context, entry.IsDirectory ? "Icon.FolderLoading" : "Icon.FileLoading", new Point(x, y), _b.IconPen, entry.IsDirectory ? _b.Folder : _b.FileIcon);
        }
    }

    private void DrawScrollBar(DrawingContext context, Size size)
    {
        if (Count <= PageRows)
        {
            return;
        }

        var (y, height) = Thumb();
        context.FillRectangle(_draggingThumb ? _b.TertiaryText : _b.Thumb, new Rect(size.Width - ScrollBarWidth + 3, y + 2, ScrollBarWidth - 6, height - 4), 2);
    }

    private void DrawQuickSearch(DrawingContext context, Size size)
    {
        if (_panel?.QuickSearchText is not { } text)
        {
            return;
        }

        var label = Text(AppStrings.Panel_QuickSearch + " ", _face, _b.SecondaryText, _fontSize);
        var value = Text(text, _bold, _b.Text, _fontSize);
        var h = Math.Max(label.Height, value.Height) + 10;
        var w = Math.Min(size.Width - 16, label.Width + value.Width + 20);
        var box = new Rect(8, size.Height - h - 8, w, h);
        context.DrawRectangle(_b.Background, _b.AccentPen, box, 6, 6);
        context.DrawText(label, new Point(box.X + 10, box.Y + 5));
        context.DrawText(value, new Point(box.X + 10 + label.Width, box.Y + 5));
    }

    private RowText GetRowText(int index, Columns cols)
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
        var nameBrush = selected ? _b.SelectedText
            : cut ? _b.TertiaryText
            : (e.Attributes & FileAttributes.Encrypted) != 0 ? _b.Encrypted
            : (e.Attributes & FileAttributes.Compressed) != 0 ? _b.Compressed
            : e.IsHidden && !up ? _b.SecondaryText
            : _b.Text;
        var dim = selected ? _b.SelectedText : _b.SecondaryText;

        var nameText = up || e.IsDirectory || !cols.ShowExt ? e.Name : e.BaseName.ToString();
        var nameRight = (cols.ShowExt ? cols.ExtX : cols.SizeRight - cols.SizeWidth) - 8;
        var ext = Text(up || e.IsDirectory ? "" : e.Extension.ToString(), _face, dim, _fontSize);
        ext.MaxTextWidth = Math.Max(10, cols.ExtWidth);
        ext.Trimming = TextTrimming.CharacterEllipsis;
        ext.MaxLineCount = 1;
        FormattedText? folder = null;
        if (panel.Results is not null)
        {
            var (inFolder, _) = FilePanel.SplitResult(e.Name);
            nameText = FilePanel.SplitResult(nameText).Name;
            folder = Text(inFolder, _face, dim, _fontSize);
            folder.MaxTextWidth = Math.Max(10, nameRight - FolderX(cols));
            folder.Trimming = TextTrimming.PrefixCharacterEllipsis;
            folder.MaxLineCount = 1;
            nameRight = FolderX(cols) - 8;
        }

        var name = Text(nameText, _face, nameBrush, _fontSize);
        name.MaxTextWidth = Math.Max(10, nameRight - NameX);
        name.Trimming = TextTrimming.CharacterEllipsis;
        name.MaxLineCount = 1;

        var local = up ? default : e.LastWriteUtc.ToLocalTime();
        t = new RowText(
            name,
            ext,
            Text(SizeText(panel, e, up), _face, dim, _fontSize),
            Text(up ? "" : _format.Date(local), _face, dim, _fontSize),
            Text(up ? "" : _format.Time(local), _face, dim, _fontSize),
            Text(up ? "" : DisplayFormat.Attributes(e.Attributes), _face, dim, _fontSize),
            folder);
        _cache[index] = t;
        return t;
    }

    private string SizeText(FilePanel panel, in FileEntry e, bool up)
        => up ? _upText
            : !e.IsDirectory ? _format.Size(e.Size)
            : panel.TryGetDirectorySize(e, out var size) ? _format.Size(size)
            : panel.IsSizing(e) ? "…"
            : _folderText;

    private void EvictCache()
    {
        var page = PageRows;
        if (_cache.Count <= page * 4)
        {
            return;
        }

        var lo = _top - page;
        var hi = _top + (page * 2);
        foreach (var key in _cache.Keys.Where(k => k < lo || k > hi).ToList())
        {
            _cache.Remove(key);
        }
    }

    private void EnsureTheme()
    {
        if (_themeValid)
        {
            return;
        }

        var family = Resource("Font.Panel") as FontFamily ?? new FontFamily("Segoe UI");
        _fontSize = Resource("Font.PanelSize") is { } s ? Convert.ToDouble(s, CultureInfo.InvariantCulture) : FontSetting.DefaultPanelSize;
        _face = new Typeface(family);
        _bold = new Typeface(family, FontStyle.Normal, FontWeight.SemiBold);
        _b = new Brushes(Brush);
        _markTexts.Clear();

        double Measure(string s) => Math.Ceiling(Text(s, _face, _b.Text, _fontSize).WidthIncludingTrailingWhitespace);
        var (size, date, time) = _format.Samples;
        var k = _fontSize / 13;
        _widths = new ColumnWidths(
            Ext: Math.Max(Measure("tar.gz"), 44 * k),
            ExtFit: 0,
            Size: Math.Max(Measure(size), Measure(_folderText)),
            Date: Measure(date),
            Time: Math.Max(Measure(time), Measure(_timeHead)),
            Attr: Math.Max(Measure("RHSA"), Measure(_attrHead)),
            Gap: 14 * k);
        _themeValid = true;
    }

    private object? Resource(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) ? v : null;

    private IBrush Brush(string token) => Resource("Brush." + token) as IBrush ?? Avalonia.Media.Brushes.Magenta;

    private static FormattedText Text(string s, Typeface face, IBrush brush, double size)
        => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush);

    private SortColumn? ColumnAt(double x)
    {
        var cols = ColumnsFor(Bounds.Width - ScrollBarWidth);
        if (x < (cols.ShowExt ? cols.ExtX : cols.SizeRight - cols.SizeWidth) - 4)
        {
            return SortColumn.Name;
        }

        if (cols.ShowExt && x < cols.SizeRight - cols.SizeWidth)
        {
            return SortColumn.Extension;
        }

        return x <= cols.SizeRight + 4 ? SortColumn.Size : x <= cols.TimeRight + 4 ? SortColumn.Time : null;
    }

    private sealed record RowText(FormattedText Name, FormattedText Ext, FormattedText Size, FormattedText Date, FormattedText Time, FormattedText Attr, FormattedText? Folder);

    private double MarkShift => _panel?.CompareMarks is not null ? MarkWidth : 0;

    private double NameX => BaseNameX + MarkShift;

    private FormattedText MarkText(DifferenceKind kind)
    {
        if (!_markTexts.TryGetValue(kind, out var text))
        {
            var (glyph, size) = kind switch
            {
                DifferenceKind.OnlyLeft => ("←", _fontSize + 3),
                DifferenceKind.OnlyRight => ("→", _fontSize + 3),
                DifferenceKind.NewerOrBigger => ("▲", _fontSize - 3),
                _ => ("≠", _fontSize),
            };
            _markTexts[kind] = text = Text(glyph, _bold, _b.Accent, size);
        }

        return text;
    }

    private double FolderX(Columns cols) => NameX + (((cols.ShowExt ? cols.ExtX : cols.SizeRight - cols.SizeWidth) - 8 - NameX) * 0.45);

    private sealed class Brushes(Func<string, IBrush> get)
    {
        public IBrush Background { get; } = get("PanelBackground");
        public IBrush Header { get; } = get("ColumnHeader");
        public IBrush Border { get; } = get("Border");
        public IBrush Text { get; } = get("Text");
        public IBrush SecondaryText { get; } = get("SecondaryText");
        public IBrush TertiaryText { get; } = get("TertiaryText");
        public IBrush SelectedText { get; } = get("SelectedText");
        public IBrush SelectionBackground { get; } = get("SelectionBackground");
        public IBrush Hover { get; } = get("Hover");
        public IBrush Compressed { get; } = get("CompressedFiles");
        public IBrush Encrypted { get; } = get("EncryptedFiles");
        public IBrush Thumb { get; } = get("BorderStrong");
        public IBrush Folder { get; } = get("Warning");
        public IBrush Accent { get; } = get("Accent");
        public IBrush FileIcon { get; } = get("BorderStrong");
        public IPen FocusPen { get; } = new ImmutablePen(get("FocusOutline").ToImmutable(), 1.5);
        public IPen FocusPenInactive { get; } = new ImmutablePen(get("FocusOutlineInactive").ToImmutable(), 1);
        public IPen AccentPen { get; } = new ImmutablePen(get("Accent").ToImmutable(), 1.5);
        public IPen DropPen { get; } = new ImmutablePen(get("Accent").ToImmutable(), 2);
        public IPen IconPen { get; } = new ImmutablePen(get("SecondaryText").ToImmutable(), 1.5, lineCap: PenLineCap.Round);
    }

    private readonly record struct ColumnWidths(double Ext, double ExtFit, double Size, double Date, double Time, double Attr, double Gap);

    private readonly record struct Columns(double ExtX, double ExtWidth, double SizeRight, double SizeWidth, double DateRight, double TimeRight, double AttrX, double Right,
        bool ShowExt, bool ShowDate, bool ShowTime, bool ShowAttr)
    {
        private const double MinName = 140;

        public static Columns For(double width, ColumnWidths w, bool extColumn = true)
        {
            bool attr = true, time = true, date = true, ext = extColumn;
            double Needed() => BaseNameX + MarkWidth + MinName + w.Size + w.Gap + (ext ? w.Ext + w.Gap : 0) + (date ? w.Date + w.Gap : 0) + (time ? w.Time + w.Gap : 0) + (attr ? w.Attr + w.Gap : 0);
            attr = Needed() <= width;
            time = Needed() <= width;
            date = Needed() <= width;
            ext = ext && Needed() <= width;

            var x = width - 6;
            var attrX = x;
            if (attr)
            {
                attrX = x - w.Attr;
                x = attrX - w.Gap;
            }

            var timeRight = x;
            if (time)
            {
                x -= w.Time + w.Gap;
            }

            var dateRight = x;
            if (date)
            {
                x -= w.Date + w.Gap;
            }

            var sizeRight = x;
            x -= w.Size + w.Gap;
            var extWidth = ext ? Math.Min(Math.Max(w.Ext, w.ExtFit), Math.Max(w.Ext, (x - BaseNameX - MarkWidth) / 3)) : 0;
            var extX = x - extWidth;
            return new Columns(extX, extWidth, sizeRight, w.Size, dateRight, timeRight, attrX, width, ext, date, time, attr);
        }
    }
}

public sealed class CommandRequest(string id)
{
    public string Id { get; } = id;

    public KeyChord? Chord { get; init; }

    public bool Handled { get; set; }
}

public sealed record PanelMenuRequest(int Row, Point Position, bool Shift, bool ForSelection = false);

public sealed record PanelDragRequest(int Row, bool RightButton);
