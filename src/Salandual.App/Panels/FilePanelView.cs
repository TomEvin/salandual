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

internal interface ITypingTarget
{
    bool Wants { get; }

    bool HasText { get; }

    void Type(string text);

    void Key(Avalonia.Input.Key key);
}

public sealed partial class FilePanelView : Control
{
    private static readonly string _nameHead = AppStrings.Panel_Col_Name, _extHead = AppStrings.Panel_Col_Ext, _sizeHead = AppStrings.Panel_Col_Size,
        _timeHead = AppStrings.Panel_Col_Time, _attrHead = AppStrings.Panel_Col_Attr,
        _upText = AppStrings.Panel_Up, _folderText = Core.Resources.Strings.InfoLine_Folder, _folderHead = AppStrings.Panel_Col_Folder, _packedHead = AppStrings.Panel_Col_Packed;

    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<FilePanelView, double>(nameof(RowHeight), 24);

    public static readonly StyledProperty<bool> ShowHeaderProperty =
        AvaloniaProperty.Register<FilePanelView, bool>(nameof(ShowHeader), true);

    public static readonly StyledProperty<bool> IsActivePanelProperty =
        AvaloniaProperty.Register<FilePanelView, bool>(nameof(IsActivePanel), true);

    private const double ScrollBarWidth = 10;
    private const double MinName = 140;
    private static readonly Cursor _sizeCursor = new(StandardCursorType.SizeWestEast);
    private const double IconSize = 16;
    private const double BaseNameX = 34;

    private const double MarkWidth = 14;

    private readonly Dictionary<int, RowText> _cache = [];
    private FilePanel? _panel;
    private Brushes _b = null!;
    private Typeface _face;
    private Typeface _bold;
    private Typeface _italic;
    private Typeface _mono;
    private readonly Dictionary<HighlightRule, IBrush> _ruleBrushes = new(ReferenceEqualityComparer.Instance);
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

    private double HeaderHeight => ShowHeader && IsDetailed ? RowHeight : 0;

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

    internal Func<KeyChord, bool>? OwnKey { get; set; }

    internal ITypingTarget? TypingTarget { get; set; }

    public event EventHandler? RenameRequested;

    public Func<string, bool>? BeforeCommand { get; set; }

    public event EventHandler? Scrolled;

    public bool ClickToRename { get; set; } = true;

    public Func<FileEntry, long?>? PackedSize
    {
        get => _packedSize;
        set
        {
            _packedSize = value;
            ClearTexts();
            InvalidateVisual();
        }
    }

    private Func<FileEntry, long?>? _packedSize;

    public PlaceColumns? Place
    {
        get => _place;
        set
        {
            _place = value;
            ClearTexts();
            InvalidateVisual();
        }
    }

    private PlaceColumns? _place;

    private Avalonia.Media.Imaging.Bitmap? IconOf(in FileEntry entry, int px)
        => _place?.IconPath?.Invoke(entry) is { } path ? _icons?.GetAt(path, entry, px) : _icons?.Get(_panel!.Path, entry, px);

    private double _extFit = -1;

    internal (double NameRight, double ExtRight, double SizeLeft, double ExtWidth) RowLayout(int index)
    {
        var cols = ColumnsNow();
        var t = GetRowText(index, cols);
        var ext = Array.FindIndex(cols.Columns, c => c.Column == PanelColumn.Extension);
        var size = cols.Columns.First(c => c.Column == PanelColumn.Size);
        return (NameX + t.Name.Width, ext < 0 ? 0 : cols.Columns[ext].X + (t.Cells[ext]?.Width ?? 0), size.X, ext < 0 ? 0 : cols.Columns[ext].Width);
    }

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
        ClearTexts();
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

            ClosePeek();
            ClearColumns();
            _panel = value;
            if (_panel is not null)
            {
                _panel.Changed += OnPanelChanged;
            }

            WatchLoading();

            SyncColumns();

            _briefFit = -1;
            SetTop(value?.TopIndex ?? 0);
            ClearTexts();
            InvalidateVisual();
        }
    }

    public int PageRows
    {
        get
        {
            var l = Layout();
            return l.PerLine * l.Lines;
        }
    }

    public int Top => _top;

    public void InvalidateTheme()
    {
        _themeValid = false;
        _extFit = _briefFit = _typeFit = -1;
        ClearTexts();
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
        if ((change & PanelChange.View) != 0 || ((change & PanelChange.Items) != 0 && _peek is { } peek && peek.Folder != _panel!.Path))
        {
            ClosePeek();
        }

        if ((change & (PanelChange.Items | PanelChange.Focus | PanelChange.View)) != 0)
        {
            SyncColumns();
        }

        if ((change & (PanelChange.Items | PanelChange.Selection | PanelChange.Sizes)) != 0)
        {
            ClearTexts();
        }

        if ((change & PanelChange.Items) != 0)
        {
            _extFit = _briefFit = _typeFit = -1;
            if (_panel!.Path.Length > 0)
            {
                _facts?.Forget(_panel.Path);
                GitRelisted();
            }

            _hover = -1;
            CancelSlowClick();
            SetTop(_panel!.TakeFocusScreenRow() is { } row ? _panel.Focus - Math.Clamp(row, 0, PageRows - 1) : _top);
        }

        if ((change & PanelChange.View) != 0)
        {
            _hover = -1;
            CancelSlowClick();
            Relayout();
        }

        if ((change & (PanelChange.Focus | PanelChange.QuickSearch)) != 0)
        {
            EnsureVisible();
        }

        if ((change & PanelChange.Loading) != 0)
        {
            WatchLoading();
        }

        InvalidateVisual();
    }

    private int Count => _panel?.Count ?? 0;

    private int MaxTop
    {
        get
        {
            var l = Layout();
            return Math.Max(0, TotalLines(l) - l.Lines) * l.PerLine;
        }
    }

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
        var per = Layout().PerLine;
        top = Math.Clamp(top, 0, MaxTop);
        _top = top - (top % per);
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

        var l = Layout();
        var lines = Math.Max(1, l.Lines - ChipLines(l));
        var line = _panel.Focus / l.PerLine;
        var topLine = _top / l.PerLine;
        if (line < topLine)
        {
            ScrollTo(line * l.PerLine);
        }
        else if (line >= topLine + lines)
        {
            ScrollTo((line - lines + 1) * l.PerLine);
        }
    }

    private double ChipBand => _lineH + 10 + 8;

    private int ChipLines(ViewLayout l)
    {
        if (_panel?.QuickSearchText is null || l.Down || l.Lines < 3)
        {
            return 0;
        }

        var covered = (l.Y0 + (l.Lines * l.CellH)) - (Bounds.Height - ChipBand);
        return covered <= 0 ? 0 : (int)Math.Ceiling(covered / l.CellH);
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

        if (Application.Current is App { Thumbnails: { } thumbs })
        {
            _thumbs = thumbs;
            thumbs.Changed += OnImageChanged;
        }

        if (Application.Current is App { Git: { } git })
        {
            _git = git;
            git.Changed += OnGitChanged;
        }

        if (Application.Current is App { ColumnFacts: { } facts })
        {
            _facts = facts;
            facts.Changed += OnFactsChanged;
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

        if (_thumbs is not null)
        {
            _thumbs.Changed -= OnImageChanged;
            _thumbs = null;
        }

        if (_git is not null)
        {
            _git.Changed -= OnGitChanged;
            _git = null;
        }

        if (_facts is not null)
        {
            _facts.Changed -= OnFactsChanged;
            _facts = null;
        }
    }

    private void OnThemeApplied(object? sender, EventArgs e) => InvalidateTheme();

    private void OnImageChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        CancelSlowClick();
        _suppressText = false;
        if (_peek is not null && _panel is not null && !e.Handled)
        {
            switch (PeekKey(e))
            {
                case null:
                    return;
                case true:
                    e.Handled = _suppressText = true;
                    return;
            }
        }

        if (_panel is null || e.Handled || !KeyMapping.TryGetChord(e.Key, e.KeyModifiers, out var chord))
        {
            return;
        }

        if (IsLocked)
        {
            LockedKey(e, chord);
            return;
        }

        var typingElsewhere = _panel.QuickSearchText is null && TypingTarget is { Wants: true };
        if (typingElsewhere)
        {
            if (IsTypingKey(e))
            {
                return;
            }

            if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Back or Key.Enter or Key.Escape or Key.Space && TypingTarget!.HasText)
            {
                TypingTarget.Key(e.Key);
                e.Handled = _suppressText = true;
                return;
            }
        }

        if ((QuickSearchWithCtrlAlt || typingElsewhere) && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt) && e.Key is >= Key.A and <= Key.Z)
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

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _panel.LoadingSince is not null)
        {
            _panel.CancelLoading();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _panel.CancelSizing())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _panel.Results?.Flat is { Reading: true })
        {
            _panel.StopFlat();
            e.Handled = true;
            return;
        }

        var id = Commands?.Resolve(chord);
        if (id is null)
        {
            if (OwnKey?.Invoke(chord) == true)
            {
                e.Handled = _suppressText = true;
            }

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

        if (BeforeCommand?.Invoke(id) == true)
        {
            return true;
        }

        if (ViewKind == PanelView.Columns && ColumnsKey(id))
        {
            return true;
        }

        if (TryGridMove(id))
        {
            return true;
        }

        var line = Layout().PerLine;
        switch (id)
        {
            case "Panel.ScrollUp" when ViewKind != PanelView.Brief:
                ScrollTo(_top - line);
                return true;
            case "Panel.ScrollDown" when ViewKind != PanelView.Brief:
                ScrollTo(_top + line);
                return true;
            case "Panel.ScrollLeft" when ViewKind == PanelView.Brief:
                ScrollTo(_top - line);
                return true;
            case "Panel.ScrollRight" when ViewKind == PanelView.Brief:
                ScrollTo(_top + line);
                return true;
            case "Panel.ScrollUp" or "Panel.ScrollDown" or "Panel.ScrollLeft" or "Panel.ScrollRight":
                return true;
            case "Panel.Peek":
                if (_peek is null)
                {
                    OpenPeek(_panel.Focus, hover: false);
                }
                else
                {
                    ClosePeek();
                }

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
            if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
            {
                panel.ToggleQuickFilter();
                return true;
            }

            panel.EndQuickSearch(keepFilter: true);
            return false;
        }

        var page = Math.Max(1, PageRows - 1);
        switch (e.Key)
        {
            case Key.Escape: panel.EndQuickSearch(); return true;
            case Key.Enter when panel.IsQuickFiltering: panel.EndQuickSearch(keepFilter: true); return true;
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

                panel.EndQuickSearch(keepFilter: true);
                return false;
        }
    }

    private static bool IsTypingKey(KeyEventArgs e)
        => (e.KeyModifiers & ~KeyModifiers.Shift) == 0
           && e.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9 or >= Key.Oem1 and <= Key.Oem102;

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (IsLocked)
        {
            e.Handled = true;
            return;
        }

        if (_peek is not null && !_suppressText && !string.IsNullOrEmpty(e.Text))
        {
            PeekType(new string([.. e.Text.Where(c => !char.IsControl(c))]));
            e.Handled = true;
            return;
        }

        if (_panel is not null && !_suppressText && !string.IsNullOrEmpty(e.Text) && _panel.QuickSearchText is null && TypingTarget is { Wants: true } target)
        {
            target.Type(new string([.. e.Text.Where(c => !char.IsControl(c))]));
            e.Handled = true;
            return;
        }

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

    private enum Zone { None, Header, Row, ScrollBar, SideBar, Strip, Edge, Column }

    private (Zone Zone, int Row) HitTest(Point p)
    {
        if (p.Y < HeaderHeight)
        {
            return (Zone.Header, -1);
        }

        if (ViewKind == PanelView.Columns)
        {
            return ColumnsHit(p);
        }

        var l = Layout();
        if (TotalLines(l) > l.Lines && (l.Down ? p.Y >= Bounds.Height - ScrollBarWidth : p.X >= Bounds.Width - ScrollBarWidth))
        {
            return (Zone.ScrollBar, -1);
        }

        if (IsDetailed && p.Y >= Bounds.Height - ScrollBarWidth && ColumnsNow().ScrollMax > 0)
        {
            return (Zone.SideBar, -1);
        }

        if (!IsDetailed)
        {
            return CellAt(p, l) is var cell and >= 0 ? (Zone.Row, cell) : (Zone.None, -1);
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

        if (IsLocked)
        {
            e.Handled = true;
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (_peek is { } peek)
        {
            if (peek.Box.Contains(point.Position))
            {
                PeekClick(point.Position, e.ClickCount);
                e.Handled = true;
                return;
            }

            ClosePeek();
        }

        if (_panel.QuickSearchText is not null && _quickBox.Contains(point.Position) && point.Properties.IsLeftButtonPressed)
        {
            _panel.ToggleQuickFilter();
            e.Handled = true;
            return;
        }

        var (zone, row) = HitTest(point.Position);
        _panel.EndQuickSearch(keepFilter: true);
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
                HeaderPressed(e, point.Position);
                break;
            case Zone.Header when point.Properties.IsRightButtonPressed:
                HeaderMenuRequested?.Invoke(this, point.Position);
                break;
            case Zone.SideBar when point.Properties.IsLeftButtonPressed:
                var (barX, barW) = HThumb(ColumnsNow());
                _dragOffset = point.Position.X >= barX && point.Position.X <= barX + barW ? point.Position.X - barX : barW / 2;
                _draggingHThumb = true;
                e.Pointer.Capture(this);
                DragHThumb(point.Position.X);
                break;
            case Zone.Strip when point.Properties.IsLeftButtonPressed:
                e.Pointer.Capture(this);
                StartStripDrag(point.Position.X);
                break;
            case Zone.Edge when point.Properties.IsLeftButtonPressed:
                e.Pointer.Capture(this);
                StartEdgeDrag(row);
                break;
            case Zone.Column when point.Properties.IsLeftButtonPressed:
                ColumnClicked(row);
                break;
            case Zone.ScrollBar when point.Properties.IsLeftButtonPressed:
                var (thumbY, thumbH) = Thumb();
                var y = Layout().Down ? point.Position.X : point.Position.Y;
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
        if (IsLocked)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (_draggingThumb)
        {
            DragThumb(Layout().Down ? p.X : p.Y);
            return;
        }

        if (_draggingHThumb)
        {
            DragHThumb(p.X);
            return;
        }

        if (_draggingStrip)
        {
            DragStrip(p.X);
            return;
        }

        if (_edgeDrag is not null)
        {
            DragColumnEdge(p.X);
            return;
        }

        if (_resize is not null)
        {
            DragEdge(p.X);
            return;
        }

        Cursor = (p.Y < HeaderHeight && IsDetailed && EdgeAt(p.X) is not null) || (ViewKind == PanelView.Columns && HitTest(p).Zone == Zone.Edge) ? _sizeCursor : null;

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
        PeekPointer(p, hover);
        UpdateDateTip(p, hover);
        if (hover != _hover)
        {
            _hover = hover;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (IsLocked)
        {
            _leftPress = null;
            e.Handled = true;
            return;
        }

        _leftPress = null;
        if (_edgeDrag is not null)
        {
            e.Pointer.Capture(null);
            EndEdgeDrag(changed: true);
        }

        if (_draggingThumb || _draggingHThumb || _draggingStrip)
        {
            _draggingThumb = _draggingHThumb = _draggingStrip = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
        }

        if (_resize is not null)
        {
            e.Pointer.Capture(null);
            EndEdgeDrag();
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
        PeekPointerLeft();
        if (_hover >= 0)
        {
            _hover = -1;
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
        if (IsLocked)
        {
            return;
        }

        var notches = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : -e.Delta.X;
        var at = e.GetPosition(this);
        if (_peek is { } peek)
        {
            if (peek.Box.Contains(at))
            {
                peek.Top = Math.Max(0, peek.Top - (int)Math.Round(notches * 3, MidpointRounding.AwayFromZero));
                InvalidateVisual();
                return;
            }

            ClosePeek();
        }
        if (e.KeyModifiers == KeyModifiers.Alt && AltWheelViews && _panel is not null)
        {
            if (notches != 0)
            {
                _panel.SetView(PanelViews.Next(_panel.View, notches < 0 ? 1 : -1));
            }

            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && ViewKind == PanelView.Thumbnails)
        {
            if (notches != 0)
            {
                ThumbnailSizeStep?.Invoke(this, notches > 0 ? 1 : -1);
            }

            return;
        }

        if (IsDetailed && (e.KeyModifiers == KeyModifiers.Shift || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)) && ColumnsNow().ScrollMax > 0)
        {
            ScrollSideways(-(Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? -e.Delta.X : e.Delta.Y) * 48);
            return;
        }

        if (ViewKind == PanelView.Columns && ColumnsWheel(at, notches, e.KeyModifiers == KeyModifiers.Shift || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)))
        {
            return;
        }

        var l = Layout();
        var lines = IsDetailed || ViewKind == PanelView.Columns ? notches * 3 : notches;
        ScrollTo(_top - ((int)Math.Round(lines, MidpointRounding.AwayFromZero) * l.PerLine));
    }

    public Rect? NameBounds(int index)
    {
        if (!IsDetailed)
        {
            var l = Layout();
            return IsInView(index, l) ? CellNameBounds(index, l) : null;
        }

        if (index < _top || index >= _top + PageRows || index >= Count)
        {
            return null;
        }

        var cols = ColumnsNow();
        var right = cols.NameRight + 2;
        return new Rect(NameX - 5, HeaderHeight + (RowHeight * (index - _top)), Math.Max(120, right - NameX + 5), RowHeight);
    }

    private bool IsOnName(int row, Point p)
    {
        if (!IsDetailed)
        {
            return IsOnCellName(row, p, Layout());
        }

        var text = GetRowText(row, ColumnsNow()).Name;
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
        var l = Layout();
        var track = l.Down ? Bounds.Width : VBar().Bottom - HeaderHeight;
        var total = TotalLines(l);
        var height = Math.Max(24, track * l.Lines / Math.Max(1, total));
        var y = (l.Down ? 0 : HeaderHeight) + ((track - height) * (_top / l.PerLine) / Math.Max(1, total - l.Lines));
        return (y, height);
    }

    private void DragThumb(double y)
    {
        var l = Layout();
        var start = l.Down ? 0 : HeaderHeight;
        var track = l.Down ? Bounds.Width : VBar().Bottom - HeaderHeight;
        var (_, height) = Thumb();
        var fraction = (y - _dragOffset - start) / Math.Max(1, track - height);
        ScrollTo((int)Math.Round(fraction * Math.Max(0, TotalLines(l) - l.Lines)) * l.PerLine);
    }

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var size = Bounds.Size;
        if (size.Width != _cachedWidth || MarkShift != _cachedShift)
        {
            ClearTexts();
            _cachedWidth = size.Width;
            _cachedShift = MarkShift;
        }

        var cols = IsDetailed ? ColumnsNow() : null;
        var rowH = RowHeight;
        context.FillRectangle(_b.Background, new Rect(size));
        if (_image?.Image is not null)
        {
            var rows = new Rect(0, HeaderHeight, size.Width, Math.Max(0, size.Height - HeaderHeight));
            var area = _image.Settings.Placement == BackgroundPlacement.Window ? Controls.BackgroundLayer.WindowArea(this) : new Rect(size);
            _image.Draw(context, area, rows);
        }

        if (HeaderHeight > 0 && cols is not null)
        {
            DrawHeader(context, size, cols);
        }

        if (_panel is null)
        {
            return;
        }

        if (cols is null)
        {
            RenderCells(context, size);
            DrawWaiting(context, size);
            return;
        }

        var last = Math.Min(Count, _top + PageRows + 1);
        var iconPx = (int)Math.Round(IconSize * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
        var active = IsActivePanel;
        var bar = cols.ScrollMax > 0 ? ScrollBarWidth : 0;
        using (context.PushClip(new Rect(0, HeaderHeight, size.Width, Math.Max(0, size.Height - HeaderHeight - bar))))
        {
            for (var i = _top; i < last; i++)
            {
                var y = HeaderHeight + (rowH * (i - _top));
                var rowRect = new Rect(4, y, Math.Max(0, size.Width - ScrollBarWidth - 4), rowH);
                var selected = _panel.IsSelected(i);
                var focused = i == _panel.Focus;
                var nameRect = (selected && _selectionLook == MarkLook.NameOnly) || (focused && _focusLook == MarkLook.NameOnly) ? NameMark(i, y, cols, rowRect.Right) : rowRect;
                if (i == _dropRow)
                {
                    context.FillRectangle(_b.SelectionBackground, rowRect, 4);
                    context.DrawRectangle(null, _b.DropPen, rowRect.Deflate(1), 4, 4);
                }
                else
                {
                    var wholeSelected = selected && _selectionLook == MarkLook.WholeRow;
                    if (!wholeSelected && i == _hover)
                    {
                        context.FillRectangle(_b.Hover, rowRect, 4);
                    }
                    else if (!wholeSelected && _rowStripes && (i % 2) == 1)
                    {
                        context.FillRectangle(_b.Stripe, rowRect, 4);
                    }

                    FillMarks(context, selected, focused, active, rowRect, nameRect);
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

                DrawRowCells(context, i, y, cols, size);
                if (focused)
                {
                    DrawFocus(context, active, rowRect, nameRect);
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
        DrawHScrollBar(context, size, cols);
        DrawPeek(context, size);
        DrawWaiting(context, size);
        DrawQuickSearch(context, size);
        EvictCache();
    }

    private void DrawIcon(DrawingContext context, in FileEntry entry, bool up, double y, int iconPx)
    {
        if (!up && entry.IsDirectory && _panel is { IsGitList: true, IsTree: false })
        {
            return;
        }

        var x = 10.0 + MarkShift;
        var at = new Rect(x, y, IconSize, IconSize);
        if (HasFolderColour(entry, up))
        {
            DrawFolderLook(context, entry, up, at, badgeOnly: false);
        }
        else if (!up && _place?.Glyph?.Invoke(entry) is { } glyph)
        {
            AppIcons.Draw(context, glyph, new Point(x, y), _b.IconPen);
        }
        else if (!up && IconOf(entry, iconPx) is { } icon)
        {
            context.DrawImage(icon, at);
            DrawFolderLook(context, entry, up, at, badgeOnly: true);
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
        var l = Layout();
        if (TotalLines(l) <= l.Lines)
        {
            return;
        }

        var (y, height) = Thumb();
        if (l.Down)
        {
            context.FillRectangle(_draggingThumb ? _b.TertiaryText : _b.Thumb, new Rect(y + 2, size.Height - ScrollBarWidth + 3, height - 4, ScrollBarWidth - 6), 2);
            return;
        }

        context.FillRectangle(_draggingThumb ? _b.TertiaryText : _b.Thumb, new Rect(VBar().X + 3, y + 2, ScrollBarWidth - 6, height - 4), 2);
    }

    private Rect _quickBox;

    internal Rect QuickSearchBox => _quickBox;

    internal Rect FocusedCellRect => _panel is null ? default : CellRect(_panel.Focus, Layout());

    private void DrawQuickSearch(DrawingContext context, Size size)
    {
        _quickBox = default;
        if (_panel?.QuickSearchText is not { } text)
        {
            return;
        }

        var filter = _panel.QuickFilterMode;
        var small = Math.Max(9, _fontSize - 1);
        var label = Text((filter ? AppStrings.Panel_QuickFilter : AppStrings.Panel_QuickSearch) + " ", _face, _b.SecondaryText, _fontSize);
        var value = Text(text, _bold, _b.Text, _fontSize);
        var search = Text(AppStrings.Panel_QuickMode_Search, _face, filter ? _b.SecondaryText : _b.SelectedText, small);
        var filtering = Text(AppStrings.Panel_QuickMode_Filter, _face, filter ? _b.SelectedText : _b.SecondaryText, small);
        var key = Text(AppStrings.Panel_QuickMode_Key, _face, _b.TertiaryText, small);
        var segWidth = search.Width + filtering.Width + 24;
        var h = Math.Max(label.Height, value.Height) + 10;
        var labelWidth = label.WidthIncludingTrailingWhitespace;
        var w = Math.Min(size.Width - 16, labelWidth + value.Width + 14 + segWidth + 8 + key.Width + 20);
        var box = new Rect(8, size.Height - h - 8, w, h);
        var l = Layout();
        if (_panel.Focus >= _top && CellRect(_panel.Focus, l).Intersects(box))
        {
            box = box.WithY(l.Y0 + 8);
        }

        _quickBox = box;
        context.DrawRectangle(_b.Background, _b.AccentPen, box, 4, 4);
        using (context.PushClip(box))
        {
            context.DrawText(label, new Point(box.X + 10, box.Y + 5));
            context.DrawText(value, new Point(box.X + 10 + labelWidth, box.Y + 5));
            var seg = new Rect(box.X + 10 + labelWidth + value.Width + 14, box.Y + 4, segWidth, h - 8);
            context.DrawRectangle(null, new ImmutablePen(_b.Border.ToImmutable(), 1), seg.Deflate(0.5), 4, 4);
            var on = filter ? new Rect(seg.X + search.Width + 12, seg.Y, filtering.Width + 12, seg.Height) : new Rect(seg.X, seg.Y, search.Width + 12, seg.Height);
            context.FillRectangle(_b.SelectionBackground, on.Deflate(1), 3);
            context.DrawText(search, new Point(seg.X + 6, seg.Center.Y - (search.Height / 2)));
            context.DrawText(filtering, new Point(seg.X + search.Width + 18, seg.Center.Y - (filtering.Height / 2)));
            context.DrawText(key, new Point(seg.Right + 8, seg.Center.Y - (key.Height / 2)));
        }
    }

    private void EvictCache()
    {
        var page = PageRows;
        var lo = _top - page;
        var hi = _top + (page * 2);
        if (_cache.Count > page * 4)
        {
            foreach (var key in _cache.Keys.Where(k => k < lo || k > hi).ToList())
            {
                _cache.Remove(key);
            }
        }

        if (_cells.Count > page * 4)
        {
            foreach (var key in _cells.Keys.Where(k => k < lo || k > hi).ToList())
            {
                _cells.Remove(key);
            }
        }
    }

    private void EnsureTheme()
    {
        if (_themeValid)
        {
            return;
        }

        var family = Resource("Font.Panel") as FontFamily ?? new FontFamily("Segoe UI");
        _fontSize = Resource("Font.PanelSizeShown") is { } s ? Convert.ToDouble(s, CultureInfo.InvariantCulture) : FontSetting.DefaultPanelSize;
        _face = new Typeface(family);
        _bold = new Typeface(family, FontStyle.Normal, FontWeight.SemiBold);
        _italic = new Typeface(family, FontStyle.Italic);
        _mono = new Typeface(Resource("Font.Mono") as FontFamily ?? FontFamily.Default);
        _b = new Brushes(Brush);
        _ruleBrushes.Clear();
        _markTexts.Clear();
        _lineH = Math.Ceiling(Text(" ", _face, _b.Text, _fontSize).Height);
        _briefFit = -1;

        double Measure(string s) => Math.Ceiling(Text(s, _face, _b.Text, _fontSize).WidthIncludingTrailingWhitespace);
        var (size, date, time, relative) = _format.Samples;
        var k = _fontSize / 13;
        _widths = new ColumnWidths(
            Ext: Math.Max(Measure("tar.gz"), 44 * k),
            Size: Math.Max(Measure(size), Math.Max(Measure(_folderText), Measure(_dirText))),
            Date: Math.Max(Measure(date), Measure(_modifiedHead) - Measure(time) - 8),
            Time: Measure(time),
            Attr: Math.Max(Measure("RHSA"), Measure(_attrHead)),
            Items: Math.Max(Measure(_format.Number(99999)), Measure(_itemsHead)),
            Dos: Math.Max(Measure("WWWWWW~1.WWW"), Measure(_dosHead)),
            Git: Math.Max(Measure("M") + GitTagPadding, Measure(_gitHead)),
            Relative: Math.Max(Measure(relative), Measure(_modifiedHead)),
            Gap: 14 * k);
        _themeValid = true;
    }

    private object? Resource(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) ? v : null;

    private IBrush Brush(string token) => Resource("Brush." + token) as IBrush ?? Avalonia.Media.Brushes.Magenta;

    private static FormattedText Text(string s, Typeface face, IBrush brush, double size)
        => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush);

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

    private bool HasFolderLook(in FileEntry entry)
        => _folderLooks is { IsEmpty: false } looks && _panel is not null && looks.Of(Salandual.Platform.Windows.FileSystem.WindowsPath.Join(_panel.Path, entry.Name)) is not null;

    private bool HasFolderColour(in FileEntry entry, bool up)
        => !up && entry.IsDirectory && _folderLooks is { IsEmpty: false } looks && _panel is not null
            && looks.Of(Salandual.Platform.Windows.FileSystem.WindowsPath.Join(_panel.Path, entry.Name)) is { Colour: not NameColour.Normal };

    private sealed class Brushes(Func<string, IBrush> get)
    {
        public IBrush Background { get; } = get("PanelBackground");
        public Rgb BackgroundRgb { get; } = get("PanelBackground") is ISolidColorBrush { Color: var c } ? new Rgb(c.R, c.G, c.B) : new Rgb(255, 255, 255);
        public bool Dark => ColorMath.Luminance(BackgroundRgb) < 0.2;
        public IPen BadgePen { get; } = new ImmutablePen(get("BorderStrong").ToImmutable(), 1);
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
        public IBrush Warning { get; } = get("Warning");
        public IBrush Danger { get; } = get("Danger");
        public IBrush Success { get; } = get("Success");
        public IBrush Accent { get; } = get("Accent");
        public IBrush GitChanged { get; } = get("GitChanged");
        public IBrush GitStaged { get; } = get("GitStaged");
        public IBrush GitNew { get; } = get("GitNew");
        public IBrush GitDeleted { get; } = get("GitDeleted");
        public IBrush GitConflicted { get; } = get("GitConflicted");
        public IBrush GitIgnored { get; } = get("GitIgnored");
        public IBrush FileIcon { get; } = get("BorderStrong");
        public IPen FocusPen { get; } = new ImmutablePen(get("FocusOutline").ToImmutable(), 1.5);
        public IPen FocusPenInactive { get; } = new ImmutablePen(get("FocusOutlineInactive").ToImmutable(), 1);
        public IBrush FocusBackground { get; } = get("FocusBackground");
        public IBrush FocusBackgroundInactive { get; } = get("FocusBackgroundInactive");
        public IPen AccentPen { get; } = new ImmutablePen(get("Accent").ToImmutable(), 1.5);
        public IPen DropPen { get; } = new ImmutablePen(get("Accent").ToImmutable(), 2);
        public IPen LockPen { get; } = new ImmutablePen(get("EncryptedFiles").ToImmutable(), 1.3, lineCap: PenLineCap.Round);
        public IPen IconPen { get; } = new ImmutablePen(get("SecondaryText").ToImmutable(), 1.5, lineCap: PenLineCap.Round);
        public IPen PicturePen { get; } = new ImmutablePen(get("Border").ToImmutable(), 1);
        public IPen StripePen { get; } = new ImmutablePen(get("Border").ToImmutable(), 3);
        public IBrush Badge { get; } = new ImmutableSolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x10));
        public IBrush Stripe { get; } = get("RowStripe");
    }

    private readonly record struct ColumnWidths(double Ext, double Size, double Date, double Time, double Attr, double Items, double Dos, double Git, double Relative, double Gap);
}


public sealed record PlaceColumns(string Head, Func<FileEntry, string?> Text)
{
    public Func<FileEntry, double?>? Fill { get; init; }

    public Func<FileEntry, string?>? Size { get; init; }

    public string? DateHead { get; init; }

    public string? SizeHead { get; init; }

    public Func<FileEntry, string?>? IconPath { get; init; }

    public double? Share { get; init; }

    public IReadOnlyCollection<PanelColumn> Hide { get; init; } = [];

    public Func<FileEntry, string?>? Glyph { get; init; }
}

public sealed class CommandRequest(string id)
{
    public string Id { get; } = id;

    public KeyChord? Chord { get; init; }

    public bool Handled { get; set; }
}

public sealed record PanelMenuRequest(int Row, Point Position, bool Shift, bool ForSelection = false);

public sealed record PanelDragRequest(int Row, bool RightButton);
