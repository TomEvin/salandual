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
using Salandual.App.Controls;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Panels;

public readonly record struct DriveChip(string Root, bool IsNetwork, bool IsRemovable);

public enum StripItemKind { Drive, Place, HotPath, More, Add }

public sealed record StripItem(StripItemKind Kind, string Label, string Target, string? Number = null, string? Tip = null,
    bool IsNetwork = false, bool IsRemovable = false, bool SeparatorAfter = false);

public sealed class DriveStrip : ThemedControl
{
    private const double ChipRadius = Corners.ChipRadius;
    private const double IconInset = 9;
    private const double Gap = 6;
    private const double SeparatorWidth = 9;

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

    internal Rect? ChipBounds(StripItem item) => _hits.Where(h => h.Item == item).Select(h => (Rect?)h.Bounds).FirstOrDefault();

    public StripItem? ItemAt(Point p) => _hits.FirstOrDefault(h => h.Bounds.Contains(p)).Item;

    internal int OverflowCount { get; private set; }

    internal IReadOnlyList<StripItem> Shown => [.. _hits.Select(h => h.Item)];

    internal void Click(StripItem item, bool ctrl = false)
    {
        ClickedWithCtrl = ctrl;
        ItemClicked?.Invoke(this, item);
    }

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
        return new Size(0, Math.Round(FontSize * 2.6));
    }

    private double Small => Math.Max(9, FontSize - 1);

    private double ChipWidth(StripItem item) => item.Kind switch
    {
        StripItemKind.Drive or StripItemKind.Place => Text(item.Label, _text, size: Small).Width + IconInset + 11 + 6 + 10,
        StripItemKind.HotPath => (item.Number is { } n ? Text(n, _dim, size: Small - 1).Width + 6 : 0) + Text(item.Label, _text, size: Small).Width + 20,
        StripItemKind.Add => 28,
        _ => Text(item.Label, _text, size: Small).Width + 20,
    };

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var size = Bounds.Size;
        context.FillRectangle(_bg, new Rect(size));
        DrawBackdrop(context, new Rect(size));
        context.FillRectangle(_border, new Rect(0, size.Height - 1, size.Width, 1));
        _hits.Clear();

        var chipH = Math.Round(Small * 1.9);
        var y = (size.Height - chipH) / 2;
        var add = new StripItem(StripItemKind.Add, "", "", Tip: AppStrings.Strip_AddTip);
        var right = size.Width - 4 - (_showAdd ? ChipWidth(add) + Gap : 0);

        var placed = Layout(right, moreWidth: 0, out var overflow);
        if (overflow > 0)
        {
            var more = new StripItem(StripItemKind.More, "+" + overflow.ToString(System.Globalization.CultureInfo.InvariantCulture), "");
            placed = Layout(right - ChipWidth(more with { Label = "+99" }) - Gap, 0, out overflow);
        }

        OverflowCount = overflow;
        StripItem? previous = null;
        var x = 8.0;
        foreach (var item in placed)
        {
            if (previous is not null && (previous.SeparatorAfter || previous.Kind != item.Kind))
            {
                x = Separator(context, x, size);
            }

            x = Chip(context, item, x, y, chipH);
            previous = item;
        }

        if (overflow > 0)
        {
            x = Chip(context, new StripItem(StripItemKind.More, "+" + overflow.ToString(System.Globalization.CultureInfo.InvariantCulture), "", Tip: AppStrings.Strip_MoreTip), x, y, chipH);
        }

        if (_showAdd)
        {
            Chip(context, add, x, y, chipH);
        }
    }

    private List<StripItem> Layout(double right, double moreWidth, out int overflow)
    {
        var placed = new List<StripItem>();
        overflow = 0;
        var x = 8.0;
        StripItem? previous = null;
        foreach (var item in _items)
        {
            var sep = previous is not null && (previous.SeparatorAfter || previous.Kind != item.Kind) ? SeparatorWidth : 0;
            var w = ChipWidth(item);
            if (overflow > 0 || x + sep + w > right - moreWidth)
            {
                if (item.Kind == StripItemKind.HotPath)
                {
                    overflow++;
                }

                continue;
            }

            placed.Add(item);
            x += sep + w + Gap;
            previous = item;
        }

        return placed;
    }

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
        else if (_hits.Count == _hover)
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
                break;
            case StripItemKind.Place:
                DrawFolderIcon(context, new Point(x + IconInset + 5.5, mid));
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

    private void DrawFolderIcon(DrawingContext context, Point center)
        => AppIcons.Draw(context, "Icon.StripFolder", center - new Point(8, 8), _iconPen);

    private void DrawStar(DrawingContext context, Point center)
        => AppIcons.Draw(context, "Icon.StripAdd", center - new Point(8, 8), _iconPen);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        var hover = _hits.FindIndex(h => h.Bounds.Contains(p));
        if (hover != _hover)
        {
            _hover = hover;
            ToolTip.SetTip(this, hover >= 0 ? _hits[hover].Item.Tip : null);
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
        if (hit >= 0 && properties.IsLeftButtonPressed)
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

    public static Task<IReadOnlyList<DriveChip>> LoadAsync() => Task.Run<IReadOnlyList<DriveChip>>(() =>
        [.. Drives.List().Select(d => new DriveChip(d.Root, d.Kind == DriveKind.Network, d.Kind is DriveKind.Removable or DriveKind.Optical))]);
}
