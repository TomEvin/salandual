// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/PanelSplitter.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Salandual.App.Resources;

namespace Salandual.App.Controls;

public sealed class PanelSplitter : ThemedControl
{
    public const double PlainWidth = 8;
    public const double ToolbarWidth = 40;
    private const double ButtonWidth = 32;
    private const double ButtonHeight = 30;
    private const double Top = 30;
    private const double SeparatorHeight = 9;

    private bool _dragging;
    private bool _hover;
    private bool _showButtons;
    private bool _rightActive;
    private int _hot = -1;
    private int _pressed = -1;
    private int _visible;
    private IBrush _grip = null!, _gripActive = null!, _hoverBack = null!, _pressedBack = null!;
    private IPen _icon = null!, _iconHot = null!, _iconOff = null!, _iconDanger = null!;
    private IPen _iconDashed = null!, _iconHotDashed = null!, _iconOffDashed = null!, _iconDangerDashed = null!;
    private IBrush _separator = null!;
    private Func<string, bool> _canExecute = _ => false;
    private Func<string, string> _tip = id => id;

    public PanelSplitter()
    {
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    public event EventHandler<double>? Dragging;

    public event EventHandler? DragEnded;

    public event EventHandler? ResetRequested;

    public event EventHandler<string>? CommandInvoked;

    public bool IsDragging => _dragging;

    public bool ShowButtons
    {
        get => _showButtons;
        set
        {
            _showButtons = value;
            _hot = -1;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public bool RightActive
    {
        get => _rightActive;
        set
        {
            _rightActive = value;
            InvalidateVisual();
        }
    }

    public void Configure(Func<string, bool> canExecute, Func<string, string> tip)
    {
        _canExecute = canExecute;
        _tip = tip;
        InvalidateVisual();
    }

    internal static IReadOnlyList<ToolbarButton> Buttons => ToolbarButton.All;

    internal int VisibleButtons => _visible;

    protected override void LoadTheme()
    {
        _grip = Brush("BorderStrong");
        _gripActive = Brush("Accent");
        _hoverBack = Brush("Hover");
        _pressedBack = Brush("SelectionBackground");
        _separator = Brush("BorderStrong");
        IPen Pen(string token, bool dashed = false, double opacity = 1) => new ImmutablePen(new ImmutableSolidColorBrush(Brush(token) is ISolidColorBrush solid ? solid.Color : Colors.Gray, opacity), 1.25, dashed ? new ImmutableDashStyle([1.6, 1.3], 0) : null, PenLineCap.Round, PenLineJoin.Round);
        _icon = Pen("SecondaryText");
        _iconHot = Pen("Text");
        _iconOff = Pen("TertiaryText", opacity: 0.5);
        _iconDanger = Pen("Danger");
        _iconDashed = Pen("SecondaryText", true);
        _iconHotDashed = Pen("Text", true);
        _iconOffDashed = Pen("TertiaryText", true, 0.5);
        _iconDangerDashed = Pen("Danger", true);
    }

    protected override Size MeasureOverride(Size availableSize) => new(_showButtons ? ToolbarWidth : PlainWidth, 0);

    protected override Size ArrangeOverride(Size finalSize)
    {
        _visible = CountFitting(finalSize.Height);
        return base.ArrangeOverride(finalSize);
    }

    private IEnumerable<(int Index, Rect Rect)> Layout()
    {
        var x = (Bounds.Width - ButtonWidth) / 2;
        var y = Top;
        var all = ToolbarButton.All;
        for (var i = 0; i < _visible; i++)
        {
            if (all[i].SeparatorBefore && i > 0)
            {
                y += SeparatorHeight;
            }

            yield return (i, new Rect(x, y, ButtonWidth, ButtonHeight));
            y += ButtonHeight + 2;
        }

        if (_visible < all.Count)
        {
            yield return (all.Count, new Rect(x, y + 4, ButtonWidth, ButtonHeight));
        }
    }

    private static int CountFitting(double height)
    {
        var all = ToolbarButton.All;
        var y = Top;
        for (var i = 0; i < all.Count; i++)
        {
            var extra = all[i].SeparatorBefore && i > 0 ? SeparatorHeight : 0;
            var needsMore = i < all.Count - 1;
            if (y + extra + ButtonHeight + (needsMore ? ButtonHeight + 6 : 0) > height - 6)
            {
                return i;
            }

            y += extra + ButtonHeight + 2;
        }

        return all.Count;
    }

    private int ButtonAt(Point p)
    {
        if (!_showButtons)
        {
            return -1;
        }

        foreach (var (index, rect) in Layout())
        {
            if (rect.Contains(p))
            {
                return index;
            }
        }

        return -1;
    }

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var size = Bounds.Size;
        context.FillRectangle(Brushes.Transparent, new Rect(size));
        if (!_showButtons)
        {
            if (_hover || _dragging)
            {
                var h = Math.Min(48, size.Height / 3);
                context.FillRectangle(_dragging ? _gripActive : _grip, new Rect((size.Width / 2) - 1.5, (size.Height - h) / 2, 3, h), 1.5f);
            }

            return;
        }

        if (_dragging)
        {
            context.FillRectangle(_hoverBack, new Rect(size), 8);
        }

        var gx = (size.Width / 2) - 2;
        for (var gy = 10.0; gy < 26; gy += 4)
        {
            context.FillRectangle(_dragging ? _gripActive : _grip, new Rect(gx, gy, 4, 2), 1);
        }

        var all = ToolbarButton.All;
        foreach (var (index, rect) in Layout())
        {
            if (index < all.Count && all[index].SeparatorBefore && index > 0)
            {
                context.FillRectangle(_separator, new Rect((size.Width / 2) - 9, rect.Y - 5.5, 18, 1));
            }

            var enabled = index == all.Count || _canExecute(all[index].CommandId);
            if (index == _pressed && enabled)
            {
                context.FillRectangle(_pressedBack, rect, 4);
            }
            else if (index == _hot)
            {
                context.FillRectangle(_hoverBack, rect, 4);
            }

            if (index == all.Count)
            {
                DrawMore(context, rect);
                continue;
            }

            DrawIcon(context, all[index], rect, enabled, index == _hot);
        }
    }

    private void DrawIcon(DrawingContext context, ToolbarButton button, Rect rect, bool enabled, bool hot)
    {
        var scale = 18.0 / 16;
        var origin = new Point(rect.X + ((rect.Width - 18) / 2), rect.Y + ((rect.Height - 18) / 2));
        var matrix = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y);
        if (button.Directional && _rightActive)
        {
            matrix = Matrix.CreateTranslation(-8, 0) * Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(8, 0) * matrix;
        }

        using (context.PushTransform(matrix))
        {
            foreach (var (geometry, dashed, _) in button.Parts)
            {
                var pen = !enabled ? (dashed ? _iconOffDashed : _iconOff)
                    : hot && button.Danger ? (dashed ? _iconDangerDashed : _iconDanger)
                    : hot ? (dashed ? _iconHotDashed : _iconHot)
                    : dashed ? _iconDashed : _icon;
                context.DrawGeometry(null, pen, geometry);
            }
        }
    }

    private void DrawMore(DrawingContext context, Rect rect)
        => Salandual.App.Theme.AppIcons.Draw(context, "Icon.More", rect.Center - new Point(8, 8), _icon);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        SetHot(-1);
        InvalidateVisual();
    }

    private void SetHot(int index)
    {
        if (index == _hot)
        {
            return;
        }

        _hot = index;
        Cursor = new Cursor(index >= 0 ? StandardCursorType.Arrow : StandardCursorType.SizeWestEast);
        var all = ToolbarButton.All;
        ToolTip.SetTip(this, index < 0 ? null : index == all.Count ? AppStrings.Splitter_MoreButtons : _tip(all[index].CommandId));
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var button = ButtonAt(point.Position);
        if (button >= 0)
        {
            _pressed = button;
            e.Pointer.Capture(this);
            InvalidateVisual();
        }
        else if (e.ClickCount == 2)
        {
            ResetRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _dragging = true;
            e.Pointer.Capture(this);
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging && Parent is Visual parent)
        {
            Dragging?.Invoke(this, e.GetPosition(parent).X);
            return;
        }

        if (_pressed < 0)
        {
            SetHot(ButtonAt(e.GetPosition(this)));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressed >= 0)
        {
            var pressed = _pressed;
            _pressed = -1;
            e.Pointer.Capture(null);
            InvalidateVisual();
            if (ButtonAt(e.GetPosition(this)) == pressed)
            {
                Click(pressed);
            }

            return;
        }

        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            DragEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pressed = -1;
        if (_dragging)
        {
            _dragging = false;
            InvalidateVisual();
            DragEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void Click(int index)
    {
        var all = ToolbarButton.All;
        if (index == all.Count)
        {
            ShowMore();
            return;
        }

        if (_canExecute(all[index].CommandId))
        {
            CommandInvoked?.Invoke(this, all[index].CommandId);
        }
    }

    private void ShowMore()
    {
        var menu = new ContextMenu();
        var all = ToolbarButton.All;
        for (var i = _visible; i < all.Count; i++)
        {
            var id = all[i].CommandId;
            if (all[i].SeparatorBefore && i > _visible)
            {
                menu.Items.Add(new Separator());
            }

            var item = new MenuItem { Header = _tip(id).Split('\n')[0], IsEnabled = _canExecute(id) };
            item.Click += (_, _) => CommandInvoked?.Invoke(this, id);
            menu.Items.Add(item);
        }

        menu.Open(this);
    }
}

internal sealed record ToolbarButton(string CommandId, IReadOnlyList<Theme.AppIcons.Part> Parts, bool SeparatorBefore = false, bool Directional = false, bool Danger = false)
{
    public static IReadOnlyList<ToolbarButton> All { get; } =
    [
        B("File.CreateDirectory"),
        B("Find.Files"),
        B("Panel.Swap"),
        B("File.Copy") with { SeparatorBefore = true, Directional = true },
        B("File.Move") with { Directional = true },
        B("File.Delete") with { Danger = true },
        B("File.QuickRename") with { SeparatorBefore = true },
        B("File.ChangeAttributes"),
        B("File.View") with { SeparatorBefore = true },
        B("File.Edit"),
        B("Clipboard.Cut") with { SeparatorBefore = true },
        B("Clipboard.Copy"),
        B("Clipboard.Paste"),
        B("Archive.Pack") with { SeparatorBefore = true },
        B("Archive.Unpack"),
        B("File.Email") with { SeparatorBefore = true },
    ];

    private static ToolbarButton B(string id) => new(id, Theme.AppIcons.Get(id).Parts);
}
