// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/DriveStrip.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Panels;

public readonly record struct DriveChip(string Root, bool IsNetwork, bool IsRemovable);

public enum StripItemKind { Drive, Place, HotPath, More, Add, UserMenu, Nav, NavList }

public sealed record StripItem(StripItemKind Kind, string Label, string Target, string? Number = null, string? Tip = null,
    bool IsNetwork = false, bool IsRemovable = false, bool SeparatorAfter = false, IImage? Image = null, string? Glyph = null, bool Dim = false, string? Setting = null);

public sealed class DriveStrip : ThemedControl
{
    private const double ChipRadius = Corners.ChipRadius;
    private const double IconInset = 9;
    private const double Gap = 6;
    private const double SeparatorWidth = 9;

    static DriveStrip() => AffectsMeasure<DriveStrip>(BarSizing.BarHeightProperty);

    private readonly List<(Rect Bounds, StripItem Item)> _hits = [];
    private IReadOnlyList<StripItem> _items = [];
    private string? _current;
    private int _hover = -1;
    private IBrush _bg = null!, _text = null!, _dim = null!, _hoverBg = null!, _border = null!, _strongBorder = null!;
    private IPen _chipPen = null!, _onPen = null!, _iconPen = null!, _dropPen = null!;
    private IBrush _dropBg = null!;
    private StripItem? _dropItem;

    public event EventHandler<StripItem>? ItemClicked;

    public bool ClickedWithCtrl { get; private set; }

    public event EventHandler<(StripItem? Item, Point At)>? ItemMenuRequested;

    public event EventHandler<StripItem>? ItemHeld;

    public bool HoldForList { get; set; }

    private DispatcherTimer? _holdTimer;
    private StripItem? _holdItem;

    public IReadOnlyList<StripItem> Items
    {
        get => _items;
        set
        {
            _items = value;
            _hover = -1;
            InvalidateVisual();
        }
    }

    public bool ShowAdd
    {
        get => _showAdd;
        set
        {
            if (_showAdd != value)
            {
                _showAdd = value;
                InvalidateVisual();
            }
        }
    }

    private bool _showAdd = true;

    public string? Current
    {
        get => _current;
        set
        {
            if (!string.Equals(_current, value, StringComparison.OrdinalIgnoreCase))
            {
                _current = value;
                InvalidateVisual();
            }
        }
    }

    public StripItem? DropItem
    {
        get => _dropItem;
        set
        {
            if (_dropItem != value)
            {
                _dropItem = value;
                InvalidateVisual();
            }
        }
    }

    public bool Grip
    {
        get => _grip;
        set
        {
            if (_grip != value)
            {
                _grip = value;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    private bool _grip;

    public bool DividerBefore
    {
        get => _dividerBefore;
        set
        {
            if (_dividerBefore != value)
            {
                _dividerBefore = value;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    private bool _dividerBefore;

    public bool SizeToItems
    {
        get => _sizeToItems;
        set
        {
            if (_sizeToItems != value)
            {
                _sizeToItems = value;
                InvalidateMeasure();
            }
        }
    }

    private bool _sizeToItems;

    public double? Length
    {
        get => _length;
        set
        {
            if (_length != value)
            {
                _length = value;
                InvalidateMeasure();
            }
        }
    }

    private double? _length;

    public bool EndHandle
    {
        get => _endHandle;
        set
        {
            if (_endHandle != value)
            {
                _endHandle = value;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    private bool _endHandle;

    public event EventHandler<double?>? LengthSet;

    private bool _sizing;
    private double _sizingFrom, _sizingStart;

    private const double EndWidth = 7;

    internal Rect EndBounds => _endHandle ? new Rect(Bounds.Width - EndWidth, 0, EndWidth, Bounds.Height) : default;

    public event EventHandler? GripPressed;

    public event EventHandler<PointerEventArgs>? GripMoved;

    public event EventHandler<PointerEventArgs?>? GripReleased;

    private bool _gripping;

    private const double GripWidth = 14;

    internal Rect GripBounds => _grip ? new Rect(DividerWidth, 0, GripWidth, Bounds.Height) : default;

    private double DividerWidth => _dividerBefore ? 9 : 0;

    private double Start => DividerWidth + (_grip ? GripWidth : 0) + 8;

    internal IReadOnlyList<StripItem> Overflowed { get; private set; } = [];

    internal Rect? ChipBounds(StripItem item) => _hits.Where(h => h.Item == item).Select(h => (Rect?)h.Bounds).FirstOrDefault();

    public StripItem? ItemAt(Point p) => _hits.FirstOrDefault(h => h.Bounds.Contains(p)).Item;

    internal int OverflowCount { get; private set; }

    internal IReadOnlyList<StripItem> Shown => [.. _hits.Select(h => h.Item)];

    internal void Click(StripItem item, bool ctrl = false)
    {
        ClickedWithCtrl = ctrl;
        ItemClicked?.Invoke(this, item);
    }

    internal void Hold(StripItem item) => ItemHeld?.Invoke(this, item);

    protected override void LoadTheme()
    {
        _bg = Brush("PanelBackground");
        _text = Brush("Text");
        _dim = Brush("SecondaryText");
        _hoverBg = Brush("Hover");
        _border = Brush("Border");
        _strongBorder = Brush("BorderStrong");
        _chipPen = new ImmutablePen(Brush("Border").ToImmutable(), 1);
        _onPen = new ImmutablePen(Brush("Accent").ToImmutable(), 1.5);
        _dropPen = new ImmutablePen(Brush("Accent").ToImmutable(), 2);
        _dropBg = Brush("TextSelection");
        _iconPen = new ImmutablePen(Brush("SecondaryText").ToImmutable(), 1.2);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureTheme();
        var height = BarSizing.Strip(BarSizing.GetBarHeight(this), FontSize, Small).Strip;
        if (_length is { } length)
        {
            return new Size(Math.Min(length, availableSize.Width), height);
        }

        if (!_sizeToItems)
        {
            return new Size(0, height);
        }

        Layout(double.PositiveInfinity, 0, out _, out var end);
        var width = end + 4 + (_showAdd ? ChipWidth(new StripItem(StripItemKind.Add, "", "")) + Gap : 0) + (_endHandle ? EndWidth : 0);
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    public IReadOnlyDictionary<string, DiskActivitySample>? Activity
    {
        get => _activity;
        set
        {
            if (ReferenceEquals(_activity, value))
            {
                return;
            }

            _activity = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyDictionary<string, DiskActivitySample>? _activity;

    internal Rect? ActivityBar(StripItem item)
        => _activity is not null && item.Kind == StripItemKind.Drive && Core.DiskActivity.DriveOf(item.Target) is { } d && _activity.ContainsKey(d) && ChipBounds(item) is { } chip
            ? ActivityRect(chip)
            : null;

    private static Rect ActivityRect(Rect chip) => new(chip.X + 6, chip.Bottom - 4, chip.Width - 12, 2);

    private double Small => Math.Max(9, FontSize - 1);

    private double ChipWidth(StripItem item) => item.Kind switch
    {
        StripItemKind.Place when item.Label.Length == 0 => IconInset + 11 + IconInset,
        StripItemKind.Drive or StripItemKind.Place => Text(item.Label, _text, size: Small).Width + IconInset + 11 + 6 + 10,
        StripItemKind.HotPath => (item.Number is { } n ? Text(n, _dim, size: Small - 1).Width + 6 : 0) + Text(item.Label, _text, size: Small).Width + 20,
        StripItemKind.Add => 28,
        StripItemKind.UserMenu => 10 + IconSize + (item.Label.Length > 0 ? 6 + Text(item.Label, _text, size: Small).Width : 0) + 10,
        StripItemKind.Nav => 8 + IconSize + 8,
        StripItemKind.NavList => 16,
        _ => Text(item.Label, _text, size: Small).Width + 20,
    };

    private const double IconSize = 16;

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var size = Bounds.Size;
        context.FillRectangle(_bg, new Rect(size));
        DrawBackdrop(context, new Rect(size));
        context.FillRectangle(_border, new Rect(0, size.Height - 1, size.Width, 1));
        _hits.Clear();
        if (_dividerBefore)
        {
            context.FillRectangle(_strongBorder, new Rect(0, (size.Height / 2) - 9, 1, 18));
        }

        if (_grip)
        {
            var dots = GripBounds;
            for (var row = 0; row < 4; row++)
            {
                for (var col = 0; col < 2; col++)
                {
                    context.FillRectangle(_dim, new Rect(dots.X + 4 + (col * 4), (size.Height / 2) - 7 + (row * 4), 2, 2), 1);
                }
            }
        }

        var chipH = BarSizing.Strip(BarSizing.GetBarHeight(this), FontSize, Small).Chip;
        var y = (size.Height - chipH) / 2;
        var add = new StripItem(StripItemKind.Add, "", "", Tip: AppStrings.Strip_AddTip);
        var right = size.Width - 4 - (_showAdd ? ChipWidth(add) + Gap : 0) - (_endHandle ? EndWidth : 0);
        if (_endHandle)
        {
            var end = EndBounds;
            context.FillRectangle(_sizing ? _onPen.Brush! : _strongBorder, new Rect(end.Right - 2, 4, 1, size.Height - 8));
            context.FillRectangle(_sizing ? _onPen.Brush! : _dim, new Rect(end.Right - 3, (size.Height / 2) - 5, 3, 10), 1);
        }

        var placed = Layout(right, moreWidth: 0, out var overflow, out _);
        if (overflow > 0)
        {
            placed = Layout(right - ChipWidth(MoreChip(99)) - Gap, 0, out overflow, out _);
        }

        OverflowCount = overflow;
        Overflowed = overflow > 0 ? [.. _items.Where(i => i.Kind == StripItemKind.UserMenu && !placed.Contains(i))] : [];
        StripItem? previous = null;
        var x = Start;
        foreach (var item in placed)
        {
            if (previous is not null && (previous.SeparatorAfter || Group(previous.Kind) != Group(item.Kind)))
            {
                x = Separator(context, x, size);
            }

            x = Chip(context, item, x - Pull(item), y, chipH);
            previous = item;
        }

        if (overflow > 0)
        {
            x = Chip(context, MoreChip(overflow), x, y, chipH);
        }

        if (_showAdd)
        {
            Chip(context, add, x, y, chipH);
        }
    }

    private List<StripItem> Layout(double right, double moreWidth, out int overflow, out double end)
    {
        var placed = new List<StripItem>();
        overflow = 0;
        var x = Start;
        end = x;
        StripItem? previous = null;
        foreach (var item in _items)
        {
            var sep = previous is not null && (previous.SeparatorAfter || Group(previous.Kind) != Group(item.Kind)) ? SeparatorWidth : 0;
            var w = ChipWidth(item) - Pull(item);
            if (overflow > 0 || x + sep + w > right - moreWidth)
            {
                if (item.Kind is StripItemKind.HotPath or StripItemKind.UserMenu)
                {
                    overflow++;
                }

                continue;
            }

            placed.Add(item);
            end = x + sep + w;
            x = end + Gap;
            previous = item;
        }

        return placed;
    }

    private static double Pull(StripItem item) => item.Kind == StripItemKind.NavList ? Gap - 1 : 0;

    private static StripItemKind Group(StripItemKind kind) => kind == StripItemKind.NavList ? StripItemKind.Nav : kind;

    private StripItem MoreChip(int overflow)
        => _items.Any(i => i.Kind == StripItemKind.UserMenu)
            ? new StripItem(StripItemKind.More, "»", nameof(StripItemKind.UserMenu), Tip: AppStrings.Strip_UserMenuMoreTip)
            : new StripItem(StripItemKind.More, "+" + overflow.ToString(System.Globalization.CultureInfo.InvariantCulture), "", Tip: AppStrings.Strip_MoreTip);

    private double Separator(DrawingContext context, double x, Size size)
    {
        context.FillRectangle(_strongBorder, new Rect(x - 1, (size.Height / 2) - 9, 1, 18));
        return x + SeparatorWidth - Gap + 2;
    }

    private double Chip(DrawingContext context, StripItem item, double x, double y, double chipH)
    {
        var w = ChipWidth(item);
        var chip = new Rect(x, y, w, chipH);
        var on = item.Kind == StripItemKind.Drive && string.Equals(item.Target, _current, StringComparison.OrdinalIgnoreCase);
        var dropping = _dropItem is not null && _dropItem == item;
        if (dropping)
        {
            context.FillRectangle(_dropBg, chip, (float)ChipRadius);
        }
        else if (_hits.Count == _hover && !(item.Dim && item.Kind is StripItemKind.Nav or StripItemKind.NavList))
        {
            context.FillRectangle(_hoverBg, chip, (float)ChipRadius);
        }

        context.DrawRectangle(null, dropping ? _dropPen : on ? _onPen : _chipPen, chip.Deflate(dropping ? 1 : 0.5), ChipRadius, ChipRadius);
        var mid = y + (chipH / 2);
        switch (item.Kind)
        {
            case StripItemKind.Drive:
                DrawDriveIcon(context, item, new Point(x + IconInset + 5.5, mid));
                DrawLabel(context, item.Label, x + IconInset + 17, y, chipH, _text);
                if (_activity is not null && Core.DiskActivity.DriveOf(item.Target) is { } drive && _activity.TryGetValue(drive, out var busy))
                {
                    var bar = ActivityRect(chip);
                    context.FillRectangle(_strongBorder, bar, 1);
                    context.FillRectangle(_onPen.Brush!, bar.WithWidth(bar.Width * busy.Percent / 100.0), 1);
                }

                break;
            case StripItemKind.Place:
                AppIcons.Draw(context, item.Glyph ?? "Icon.StripFolder", new Point(x + IconInset + 5.5, mid) - new Point(8, 8), _iconPen);
                DrawLabel(context, item.Label, x + IconInset + 17, y, chipH, _text);
                break;
            case StripItemKind.HotPath:
                var tx = x + 10;
                if (item.Number is { } n)
                {
                    var number = Text(n, _dim, size: Small - 1);
                    context.DrawText(number, new Point(tx, y + ((chipH - number.Height) / 2)));
                    tx += number.Width + 6;
                }

                DrawLabel(context, item.Label, tx, y, chipH, _text);
                break;
            case StripItemKind.Add:
                DrawStar(context, new Point(x + (w / 2), mid));
                break;
            case StripItemKind.Nav:
                AppIcons.Draw(context, item.Glyph ?? "Icon.ChevronLeft", new Point(x + (w / 2), mid) - new Point(8, 8),
                    new ImmutablePen((item.Dim ? _border : _text).ToImmutable(), 1.5));
                break;
            case StripItemKind.NavList:
                AppIcons.Draw(context, "Icon.ChevronDown", new Point(x + (w / 2), mid) - new Point(5, 5),
                    new ImmutablePen((item.Dim ? _border : _dim).ToImmutable(), 1.2), scale: 0.625);
                break;
            case StripItemKind.UserMenu:
                var icon = new Rect(x + 10, mid - (IconSize / 2), IconSize, IconSize);
                if (item.Image is { } image)
                {
                    context.DrawImage(image, icon);
                }
                else
                {
                    AppIcons.Draw(context, item.Glyph ?? "UserMenu.Open", icon.TopLeft, _iconPen);
                }

                if (item.Label.Length > 0)
                {
                    DrawLabel(context, item.Label, icon.Right + 6, y, chipH, item.Dim ? _dim : _text);
                }

                break;
            default:
                DrawLabel(context, item.Label, x + 10, y, chipH, _dim);
                break;
        }

        _hits.Add((chip, item));
        return x + w + Gap;
    }

    private void DrawLabel(DrawingContext context, string text, double x, double y, double chipH, IBrush brush)
    {
        var label = Text(text, brush, size: Small);
        context.DrawText(label, new Point(x, y + ((chipH - label.Height) / 2)));
    }

    private void DrawDriveIcon(DrawingContext context, StripItem drive, Point center)
        => AppIcons.Draw(context, drive.IsNetwork ? "Icon.StripNetwork" : "Icon.StripDrive", center - new Point(8, 8), _iconPen, drive.IsRemovable ? _dim : _text);

    private void DrawStar(DrawingContext context, Point center)
        => AppIcons.Draw(context, "Icon.StripAdd", center - new Point(8, 8), _iconPen);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_gripping)
        {
            GripMoved?.Invoke(this, e);
            return;
        }

        var p = e.GetPosition(this);
        if (_sizing)
        {
            Length = Math.Clamp(_sizingStart + p.X - _sizingFrom, Core.Settings.BarBlockSettings.MinLength, Core.Settings.BarBlockSettings.MaxLength);
            return;
        }

        var onEnd = _endHandle && EndBounds.Contains(p);
        Cursor = _grip && GripBounds.Contains(p) ? new Cursor(StandardCursorType.SizeAll) : onEnd ? new Cursor(StandardCursorType.SizeWestEast) : null;
        var hover = onEnd ? -2 : _hits.FindIndex(h => h.Bounds.Contains(p));
        if (hover != _hover)
        {
            _hover = hover;
            ToolTip.SetTip(this, hover >= 0 ? _hits[hover].Item.Tip : onEnd ? AppStrings.Strip_EndTip : null);
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        var hit = _hits.FindIndex(h => h.Bounds.Contains(p));
        var properties = e.GetCurrentPoint(this).Properties;
        if (_endHandle && properties.IsLeftButtonPressed && EndBounds.Contains(p))
        {
            if (e.ClickCount == 2)
            {
                LengthSet?.Invoke(this, null);
            }
            else
            {
                _sizing = true;
                _sizingFrom = p.X;
                _sizingStart = Bounds.Width;
                e.Pointer.Capture(this);
                InvalidateVisual();
            }

            e.Handled = true;
        }
        else if (_grip && properties.IsLeftButtonPressed && GripBounds.Contains(p))
        {
            _gripping = true;
            e.Pointer.Capture(this);
            GripPressed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (hit >= 0 && properties.IsLeftButtonPressed && HoldForList && _hits[hit].Item is { Kind: StripItemKind.Nav, Dim: false } held)
        {
            _holdItem = held;
            _holdTimer?.Stop();
            _holdTimer = new DispatcherTimer { Interval = DirectoryLine.HoldTime };
            _holdTimer.Tick += (_, _) =>
            {
                _holdTimer?.Stop();
                _holdTimer = null;
                if (_holdItem is { } item)
                {
                    _holdItem = null;
                    ItemHeld?.Invoke(this, item);
                }
            };
            _holdTimer.Start();
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else if (hit >= 0 && properties.IsLeftButtonPressed)
        {
            ClickedWithCtrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            ItemClicked?.Invoke(this, _hits[hit].Item);
            e.Handled = true;
        }
        else if (properties.IsRightButtonPressed)
        {
            ItemMenuRequested?.Invoke(this, (hit >= 0 ? _hits[hit].Item : null, p));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_holdTimer is not null || _holdItem is not null)
        {
            _holdTimer?.Stop();
            _holdTimer = null;
            e.Pointer.Capture(null);
            if (_holdItem is { } item)
            {
                _holdItem = null;
                ClickedWithCtrl = false;
                ItemClicked?.Invoke(this, item);
            }

            e.Handled = true;
        }
        else if (_sizing)
        {
            _sizing = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            LengthSet?.Invoke(this, Length);
            e.Handled = true;
        }
        else if (_gripping)
        {
            _gripping = false;
            e.Pointer.Capture(null);
            GripReleased?.Invoke(this, e);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_sizing)
        {
            _sizing = false;
            InvalidateVisual();
            LengthSet?.Invoke(this, Length);
        }
        else if (_gripping)
        {
            _gripping = false;
            GripReleased?.Invoke(this, null);
        }
    }

    public static Task<IReadOnlyList<DriveChip>> LoadAsync() => Task.Run<IReadOnlyList<DriveChip>>(() =>
        [.. Drives.List().Select(d => new DriveChip(d.Root, d.Kind == DriveKind.Network, d.Kind is DriveKind.Removable or DriveKind.Optical))]);
}
