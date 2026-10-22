// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/DirectoryLine.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Salandual.App.Controls;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Panels;

public sealed class DirectoryLine : ThemedControl
{
    private const double MarkerWidth = 3;
    private const double MeterWidth = 60;
    private const double DriveButtonWidth = 34;
    private const double ButtonSize = 24;

    private enum Zone { None, DriveButton, Crumb, Edit, History, FreeSpace, Maximize }

    private readonly List<(Rect Bounds, string Path)> _hits = [];
    private string _path = "";
    private bool _isActive;
    private (long Free, long Total)? _space;

    private static readonly Dictionary<string, (long Free, long Total)> _spaceByRoot = new(StringComparer.OrdinalIgnoreCase);
    private DisplayFormat _format = DisplayFormat.Default;
    private int _hover = -1;
    private Zone _hoverZone;
    private IBrush _bg = null!, _text = null!, _dim = null!, _accent = null!, _track = null!, _hoverBg = null!, _border = null!, _dropBg = null!;
    private Rect _driveButton, _history, _freeSpace, _maximize, _editZone;
    private bool _driveMenuOpen, _historyOpen, _isMaximized, _showMaximize = true, _dropping;

    public DirectoryLine()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => Dropping = false);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public event EventHandler<string>? NavigateRequested;

    public event EventHandler? EditRequested;

    public event EventHandler? DriveButtonClicked;

    public event EventHandler<Point>? PathMenuRequested;

    public event EventHandler? HistoryRequested;

    public event EventHandler? DriveInfoRequested;

    public event EventHandler? MaximizeRequested;

    public event EventHandler<string>? PathDropped;

    public bool IsDriveMenuOpen
    {
        get => _driveMenuOpen;
        set
        {
            _driveMenuOpen = value;
            InvalidateVisual();
        }
    }

    public bool IsHistoryOpen
    {
        get => _historyOpen;
        set
        {
            _historyOpen = value;
            InvalidateVisual();
        }
    }

    public bool IsMaximized
    {
        get => _isMaximized;
        set
        {
            _isMaximized = value;
            InvalidateVisual();
        }
    }

    public bool ShowMaximizeButton
    {
        get => _showMaximize;
        set
        {
            _showMaximize = value;
            InvalidateVisual();
        }
    }

    internal bool Dropping
    {
        get => _dropping;
        set
        {
            if (_dropping != value)
            {
                _dropping = value;
                InvalidateVisual();
            }
        }
    }

    internal Rect DriveButtonBounds => _driveButton;

    internal Rect HistoryButtonBounds => _history;

    internal Rect FreeSpaceBounds => _freeSpace;

    internal Rect MaximizeButtonBounds => _maximize;

    internal Rect EditZoneBounds => _editZone;

    internal IReadOnlyList<(Rect Bounds, string Path)> Crumbs => _hits;

    public string Path
    {
        get => _path;
        set
        {
            _path = value;
            _space = WindowsPath.GetRoot(value) is { } root && _spaceByRoot.TryGetValue(root, out var known) ? known : null;
            InvalidateVisual();
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            InvalidateVisual();
        }
    }

    public DisplayFormat Format
    {
        get => _format;
        set
        {
            _format = value;
            InvalidateVisual();
        }
    }

    internal (long Free, long Total)? Space => _space;

    public void SetSpace(string path, long free, long total)
    {
        if (WindowsPath.GetRoot(path) is { } root)
        {
            _spaceByRoot[root] = (free, total);
        }

        if (path == _path && _space != (free, total))
        {
            _space = (free, total);
            InvalidateVisual();
        }
    }

    protected override void LoadTheme()
    {
        _bg = Brush("ColumnHeader");
        _text = Brush("Text");
        _dim = Brush("SecondaryText");
        _accent = Brush("Accent");
        _track = Brush("Border");
        _hoverBg = Brush("Hover");
        _border = Brush("Border");
        _dropBg = Brush("TextSelection");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureTheme();
        return new Size(0, Math.Round(FontSize * 2.4));
    }

    public override void Render(DrawingContext context)
    {
        EnsureTheme();
        var size = Bounds.Size;
        context.FillRectangle(_dropping ? _dropBg : _bg, new Rect(size));
        DrawBackdrop(context, new Rect(size));
        context.FillRectangle(_border, new Rect(0, size.Height - 1, size.Width, 1));
        if (_dropping)
        {
            context.DrawRectangle(null, new ImmutablePen(_accent.ToImmutable(), 2), new Rect(size).Deflate(1), 4, 4);
        }

        if (_isActive)
        {
            context.FillRectangle(_accent, new Rect(0, 4, MarkerWidth, size.Height - 8), 1.5f);
        }

        DrawDriveButton(context, size);

        var buttonTop = (size.Height - ButtonSize) / 2;
        var right = size.Width - 8;
        _maximize = default;
        if (_showMaximize)
        {
            _maximize = new Rect(right - ButtonSize, buttonTop, ButtonSize, ButtonSize);
            DrawMaximize(context);
            right = _maximize.X - 4;
        }

        _freeSpace = default;
        if (_space is { } cloud && DiskSpace.IsMadeUp(cloud.Total))
        {
            var label = Text(AppStrings.Drive_CloudDrive, _hoverZone == Zone.FreeSpace ? _text : _dim, size: Math.Max(9, FontSize - 1));
            var width = 6 + label.Width + 6;
            _freeSpace = new Rect(right - width, buttonTop, width, ButtonSize);
            if (_hoverZone == Zone.FreeSpace)
            {
                context.FillRectangle(_hoverBg, _freeSpace, 4);
            }

            context.DrawText(label, new Point(_freeSpace.X + 6, (size.Height - label.Height) / 2));
            right = _freeSpace.X - 4;
        }
        else if (_space is { } space && space.Total > 0)
        {
            var label = Text(Loc.Format(AppStrings.Dir_Free, _format.Size(space.Free)), _hoverZone == Zone.FreeSpace ? _text : _dim, size: Math.Max(9, FontSize - 1));
            var width = 6 + MeterWidth + 8 + label.Width + 6;
            _freeSpace = new Rect(right - width, buttonTop, width, ButtonSize);
            if (_hoverZone == Zone.FreeSpace)
            {
                context.FillRectangle(_hoverBg, _freeSpace, 4);
            }

            var meter = new Rect(_freeSpace.X + 6, (size.Height / 2) - 2, MeterWidth, 4);
            context.FillRectangle(_track, meter, 2);
            var used = 1 - ((double)space.Free / space.Total);
            context.FillRectangle(_accent, meter.WithWidth(MeterWidth * Math.Clamp(used, 0, 1)), 2);
            context.DrawText(label, new Point(meter.Right + 8, (size.Height - label.Height) / 2));
            right = _freeSpace.X - 4;
        }

        var crumbsEnd = DrawCrumbs(context, size, right - ButtonSize - 4);

        var x = Math.Clamp((size.Width / 2) - (ButtonSize / 2), crumbsEnd + 4, Math.Max(crumbsEnd + 4, right - ButtonSize));
        _history = new Rect(x, buttonTop, ButtonSize, ButtonSize);
        _editZone = new Rect(crumbsEnd, 0, Math.Max(0, right - crumbsEnd), size.Height);
        DrawHistory(context);
    }

    private double DrawCrumbs(DrawingContext context, Size size, double right)
    {
        _hits.Clear();
        var parts = Split(_path);
        var x = _driveButton.Right + 8;
        if (parts.Count == 0)
        {
            return x;
        }

        var separator = Text("›", _dim);
        var texts = parts.Select((p, i) => Text(p.Label, _text, bold: i == parts.Count - 1)).ToList();
        var available = right - x;

        var first = 1;
        double Width(int from) => texts[0].Width + parts.Skip(from).Select((_, k) => texts[from + k].Width + separator.Width + 16).Sum() + (from > 1 ? Text("…", _dim).Width + separator.Width + 16 : 0);
        while (first < parts.Count - 1 && Width(first) > available)
        {
            first++;
        }

        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0 && i < first)
            {
                if (i == 1)
                {
                    x = DrawSeparator(context, separator, x, size);
                    var dots = Text("…", _dim);
                    context.DrawText(dots, new Point(x, (size.Height - dots.Height) / 2));
                    x += dots.Width + 8;
                }

                continue;
            }

            if (i > 0)
            {
                x = DrawSeparator(context, separator, x, size);
            }

            var t = texts[i];
            var bounds = new Rect(x - 4, 4, t.Width + 8, size.Height - 8);
            if (_hits.Count == _hover)
            {
                if (i == parts.Count - 1)
                {
                    context.DrawRectangle(null, new ImmutablePen(_track.ToImmutable(), 1), bounds.Deflate(0.5), 4, 4);
                }
                else
                {
                    context.FillRectangle(_hoverBg, bounds, 4);
                }
            }

            context.DrawText(t, new Point(x, (size.Height - t.Height) / 2));
            _hits.Add((bounds, parts[i].Path));
            x += t.Width + 4;
        }

        return x + 4;
    }

    private static double DrawSeparator(DrawingContext context, FormattedText separator, double x, Size size)
    {
        x += 4;
        context.DrawText(separator, new Point(x, (size.Height - separator.Height) / 2));
        return x + separator.Width + 8;
    }

    private void DrawDriveButton(DrawingContext context, Size size)
    {
        var h = Math.Min(size.Height - 8, 26);
        _driveButton = new Rect(MarkerWidth + 6, (size.Height - h) / 2, DriveButtonWidth, h);
        if (_hoverZone == Zone.DriveButton || _driveMenuOpen)
        {
            context.FillRectangle(_hoverBg, _driveButton, 4);
        }

        context.DrawRectangle(null, new ImmutablePen(_track.ToImmutable(), 1), _driveButton.Deflate(0.5), 4, 4);
        var pen = new ImmutablePen(_dim.ToImmutable(), 1.2);
        var c = new Point(_driveButton.X + 12, _driveButton.Center.Y);
        context.DrawRectangle(null, pen, new Rect(c.X - 5.5, c.Y - 3, 11, 6), 2, 2);
        context.FillRectangle(_text, new Rect(c.X + 2, c.Y - 0.75, 1.5, 1.5));
        DrawChevron(context, pen, new Point(_driveButton.Right - 10, c.Y), 3);
    }

    private void DrawHistory(DrawingContext context)
    {
        var pressed = _hoverZone == Zone.History || _historyOpen;
        if (pressed)
        {
            context.FillRectangle(_hoverBg, _history, 4);
            context.DrawRectangle(null, new ImmutablePen(_track.ToImmutable(), 1), _history.Deflate(0.5), 4, 4);
        }

        DrawChevron(context, new ImmutablePen((pressed ? _text : _dim).ToImmutable(), 1.5), _history.Center, 4);
    }

    private void DrawMaximize(DrawingContext context)
    {
        var hover = _hoverZone == Zone.Maximize;
        if (hover)
        {
            context.FillRectangle(_hoverBg, _maximize, 4);
        }

        var pen = new ImmutablePen((hover ? _text : _dim).ToImmutable(), 1.2);
        AppIcons.Draw(context, _isMaximized ? "Icon.PanelRestore" : "Icon.PanelMaximize", _maximize.Center - new Point(8, 8), pen);
    }

    private static void DrawChevron(DrawingContext context, IPen pen, Point center, double half)
        => AppIcons.Draw(context, "Icon.ChevronDown", center - new Point(2 * half, 2 * half), pen, scale: half / 4);

    internal static List<(string Label, string Path)> Split(string path)
    {
        var result = new List<(string, string)>();
        if (WindowsPath.GetRoot(path) is not { } root)
        {
            if (path.Length > 0)
            {
                result.Add((path, path));
            }

            return result;
        }

        result.Add((root.TrimEnd('\\'), root));
        var current = root;
        foreach (var part in path[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = WindowsPath.Join(current, part);
            result.Add((part, current));
        }

        return result;
    }

    private Zone ZoneAt(Point p, out int crumb)
    {
        crumb = _hits.FindIndex(h => h.Bounds.Contains(p));
        if (_driveButton.Contains(p))
        {
            return Zone.DriveButton;
        }

        if (_history.Contains(p))
        {
            return Zone.History;
        }

        if (_freeSpace.Contains(p))
        {
            return Zone.FreeSpace;
        }

        if (_showMaximize && _maximize.Contains(p))
        {
            return Zone.Maximize;
        }

        if (crumb >= 0)
        {
            return crumb == _hits.Count - 1 ? Zone.Edit : Zone.Crumb;
        }

        return _editZone.Contains(p) ? Zone.Edit : Zone.None;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var zone = ZoneAt(e.GetPosition(this), out var crumb);
        if (zone == _hoverZone && crumb == _hover)
        {
            return;
        }

        _hoverZone = zone;
        _hover = crumb;
        ToolTip.SetTip(this, zone switch
        {
            Zone.DriveButton => AppStrings.Dir_Tip_Drive,
            Zone.History => AppStrings.Dir_Tip_History,
            Zone.FreeSpace => AppStrings.Dir_Tip_FreeSpace,
            Zone.Maximize => _isMaximized ? AppStrings.Dir_Tip_Restore : AppStrings.Dir_Tip_Maximize,
            _ => null,
        });
        Cursor = zone switch
        {
            Zone.Edit => new Cursor(StandardCursorType.Ibeam),
            Zone.None => Cursor.Default,
            _ => new Cursor(StandardCursorType.Hand),
        };
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverZone = Zone.None;
        ToolTip.SetTip(this, null);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        var point = e.GetCurrentPoint(this).Properties;
        if (point.IsRightButtonPressed)
        {
            PathMenuRequested?.Invoke(this, p);
            e.Handled = true;
            return;
        }

        if (!point.IsLeftButtonPressed)
        {
            return;
        }

        Click(p);
        e.Handled = true;
    }

    internal void Click(Point p)
    {
        switch (ZoneAt(p, out var crumb))
        {
            case Zone.DriveButton:
                DriveButtonClicked?.Invoke(this, EventArgs.Empty);
                break;
            case Zone.History:
                HistoryRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Zone.FreeSpace:
                DriveInfoRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Zone.Maximize:
                MaximizeRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Zone.Crumb:
                NavigateRequested?.Invoke(this, _hits[crumb].Path);
                break;
            default:
                EditRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var data = e.DataTransfer;
        var accepts = data.Contains(DataFormat.File) || data.Contains(DataFormat.Text);
        e.DragEffects = !accepts ? DragDropEffects.None
            : (e.DragEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link
            : e.DragEffects & DragDropEffects.Copy;
        Dropping = accepts;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        Dropping = false;
        var data = e.DataTransfer;
        var path = data.TryGetFiles() is { Length: > 0 } files ? files[0].TryGetLocalPath() : data.TryGetText()?.Trim();
        if (!string.IsNullOrEmpty(path))
        {
            e.DragEffects = DragDropEffects.Link;
            Drop(path);
        }

        e.Handled = true;
    }

    internal void Drop(string path)
    {
        var first = path.Split('\n', 2)[0].Trim().Trim('"');
        if (first.Length > 0)
        {
            PathDropped?.Invoke(this, first);
        }
    }
}
