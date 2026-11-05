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
using Salandual.Core;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Panels;

public sealed record ArchiveFacts(string ArchivePath, string Format, string Contents);

public sealed record ServerFacts(string Security, bool Encrypted);

public sealed partial class DirectoryLine : ThemedControl
{
    private const double MarkerWidth = 3;
    private const double MeterWidth = 60;
    private const double ActivityWidth = 36;
    private const double ActivityGraphWidth = 60;
    private const double DriveButtonWidth = 34;
    private const double ButtonSize = 24;

    private enum Zone { None, DriveButton, Crumb, Edit, History, FreeSpace, Activity, Maximize, ViewButton, FilterChip, FilterOff, NamesChip, NamesOff, Back, BackList, Forward, ForwardList, Branch }

    private readonly List<(Rect Bounds, string Path)> _hits = [];
    private string _path = "";
    private bool _isActive;
    private (long Free, long Total)? _space;

    private static readonly Dictionary<string, (long Free, long Total)> _spaceByRoot = new(StringComparer.OrdinalIgnoreCase);
    private DisplayFormat _format = DisplayFormat.Default;
    private int _hover = -1;
    private Zone _hoverZone;
    private IBrush _bg = null!, _text = null!, _dim = null!, _accent = null!, _track = null!, _hoverBg = null!, _border = null!, _dropBg = null!, _warning = null!;
    private Rect _driveButton, _history, _freeSpace, _activityBox, _maximize, _editZone, _viewButton;
    private bool _showViewButton = true, _viewMenuOpen;
    private string? _filterChip, _namesChip;
    private Rect _filterChipBox, _filterOff, _namesChipBox, _namesOff;
    private Core.Panels.PanelView _view = Core.Panels.PanelView.Detailed;
    private bool _driveMenuOpen, _historyOpen, _isMaximized, _showMaximize = true, _showFreeSpace = true, _dropping;

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

    public event EventHandler<Point>? SpaceMenuRequested;

    public event EventHandler? HistoryRequested;

    public event EventHandler? DriveInfoRequested;

    public event EventHandler? MaximizeRequested;

    public event EventHandler? ViewMenuRequested;

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

    public Core.Panels.PanelView View
    {
        get => _view;
        set
        {
            if (_view != value)
            {
                _view = value;
                InvalidateVisual();
            }
        }
    }

    public bool ShowViewButton
    {
        get => _showViewButton;
        set
        {
            if (_showViewButton != value)
            {
                _showViewButton = value;
                InvalidateVisual();
            }
        }
    }

    public bool IsViewMenuOpen
    {
        get => _viewMenuOpen;
        set
        {
            _viewMenuOpen = value;
            InvalidateVisual();
        }
    }

    public string? FilterChip
    {
        get => _filterChip;
        set
        {
            if (_filterChip != value)
            {
                _filterChip = value;
                InvalidateVisual();
            }
        }
    }

    public string? NamesChip
    {
        get => _namesChip;
        set
        {
            if (_namesChip != value)
            {
                _namesChip = value;
                InvalidateVisual();
            }
        }
    }

    public event EventHandler<Point>? FilterMenuRequested;

    public event EventHandler? FilterOffRequested;

    public event EventHandler? ShowNamesRequested;

    internal Rect FilterChipBounds => _filterChipBox;

    internal Rect FilterOffBounds => _filterOff;

    internal Rect NamesChipBounds => _namesChipBox;

    internal Rect ViewButtonBounds => _viewButton;

    public bool ShowMaximizeButton
    {
        get => _showMaximize;
        set
        {
            _showMaximize = value;
            InvalidateVisual();
        }
    }

    public bool ShowFreeSpace
    {
        get => _showFreeSpace;
        set
        {
            if (_showFreeSpace != value)
            {
                _showFreeSpace = value;
                InvalidateVisual();
            }
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

    internal Rect ActivityBounds => _activityBox;

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

    public DiskActivitySample? Activity
    {
        get => _activity;
        set
        {
            if (_activity == value)
            {
                return;
            }

            _activity = value;
            if (_hoverZone == Zone.Activity)
            {
                ToolTip.SetTip(this, ActivityTip());
            }

            InvalidateVisual();
        }
    }

    private DiskActivitySample? _activity;

    public ActivityLook ActivityLook
    {
        get => _activityLook;
        set
        {
            if (_activityLook != value)
            {
                _activityLook = value;
                InvalidateVisual();
            }
        }
    }

    private ActivityLook _activityLook;

    public IReadOnlyList<double?>? ActivityRecent
    {
        get => _activityRecent;
        set
        {
            if (!ReferenceEquals(_activityRecent, value))
            {
                _activityRecent = value;
                InvalidateVisual();
            }
        }
    }

    private IReadOnlyList<double?>? _activityRecent;

    internal string? ActivityTip()
        => _activity is { } a && Core.DiskActivity.DriveOf(_path) is { } drive
            ? Loc.Format(AppStrings.Dir_Tip_Activity, drive, a.Percent, _format.Size((long)a.ReadPerSecond), _format.Size((long)a.WritePerSecond)) + Environment.NewLine
                + (RecentReadings() is { Count: > 0 } recent ? Loc.Format(AppStrings.Dir_Tip_ActivityRecent, (int)Math.Round(recent.Max()), (int)Math.Round(recent.Average())) + Environment.NewLine : "")
                + AppStrings.Dir_Tip_ActivityClick
            : null;

    private List<double>? RecentReadings()
        => _activityLook != ActivityLook.Bar && _activityRecent is { } recent ? [.. recent.OfType<double>()] : null;

    public ArchiveFacts? Archive
    {
        get => _archive;
        set
        {
            _archive = value;
            InvalidateVisual();
        }
    }

    private ArchiveFacts? _archive;

    public ServerFacts? Server
    {
        get => _server;
        set
        {
            _server = value;
            InvalidateVisual();
        }
    }

    private ServerFacts? _server;

    internal Rect ServerChipBounds { get; private set; }

    internal Rect ArchiveFactsBounds { get; private set; }

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

    public FolderLooks? FolderLooks
    {
        get => _folderLooks;
        set
        {
            _folderLooks = value;
            InvalidateVisual();
        }
    }

    private FolderLooks? _folderLooks;

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
        _warning = Brush("Warning");
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

        DrawDriveButton(context, size, DrawBackForward(context, size, MarkerWidth + 6));

        var buttonTop = (size.Height - ButtonSize) / 2;
        var right = size.Width - 8;
        _maximize = default;
        if (_showMaximize)
        {
            _maximize = new Rect(right - ButtonSize, buttonTop, ButtonSize, ButtonSize);
            DrawMaximize(context);
            right = _maximize.X - 4;
        }

        _viewButton = default;
        if (_showViewButton)
        {
            right = DrawViewButton(context, size, right, buttonTop);
        }

        (_namesChipBox, _namesOff) = _namesChip is { } names ? DrawChip(context, size, ref right, buttonTop, names, Zone.NamesChip, Zone.NamesOff, icon: false) : default;
        (_filterChipBox, _filterOff) = _filterChip is { } masks ? DrawChip(context, size, ref right, buttonTop, masks, Zone.FilterChip, Zone.FilterOff, icon: true) : default;

        _freeSpace = default;
        ArchiveFactsBounds = default;
        ServerChipBounds = default;
        BranchBounds = default;
        if (_server is { } server)
        {
            var brush = server.Encrypted ? _accent : _warning;
            var label = Text(server.Encrypted ? server.Security : AppStrings.Server_NotEncrypted, brush, size: Math.Max(8, FontSize - 2));
            var lockWidth = server.Encrypted ? 12 : 0;
            var chipBox = new Rect(right - label.Width - 12 - lockWidth - 4, (size.Height - label.Height - 2) / 2, label.Width + 12 + lockWidth, label.Height + 2);
            context.DrawRectangle(null, new ImmutablePen(brush.ToImmutable(), 1), chipBox.Deflate(0.5), 2, 2);
            if (server.Encrypted)
            {
                DrawLock(context, new Point(chipBox.X + 6, chipBox.Center.Y), new ImmutablePen(brush.ToImmutable(), 1.1));
            }

            context.DrawText(label, new Point(chipBox.X + 6 + lockWidth, chipBox.Y + 1));
            ServerChipBounds = chipBox;
            right = chipBox.X - 6;
        }
        else if (_archive is { } archive)
        {
            var small = Math.Max(9, FontSize - 1);
            var holds = Text(archive.Contents, _dim, size: small);
            var chip = Text(archive.Format, _dim, size: Math.Max(8, FontSize - 2));
            var chipBox = new Rect(right - holds.Width - 10 - chip.Width - 12, (size.Height - chip.Height - 2) / 2, chip.Width + 12, chip.Height + 2);
            context.DrawRectangle(null, new ImmutablePen(_track.ToImmutable(), 1), chipBox.Deflate(0.5), 2, 2);
            context.DrawText(chip, new Point(chipBox.X + 6, chipBox.Y + 1));
            context.DrawText(holds, new Point(right - holds.Width - 4, (size.Height - holds.Height) / 2));
            ArchiveFactsBounds = new Rect(chipBox.X, 0, right - chipBox.X, size.Height);
            right = chipBox.X - 6;
        }
        else if (_git is not null)
        {
            right = DrawGit(context, size, right, buttonTop);
        }
        else if (!_showFreeSpace)
        {
        }
        else if (_space is { } cloud && DiskSpace.IsMadeUp(cloud.Total))
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
        else if (_space is { } space && space.Total > 0 && right - (6 + MeterWidth + 8 + 80 + 6) >= _driveButton.Right + 8 + PathRoom())
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

        _activityBox = default;
        if (_activity is { } activity && _archive is null && !(_space is { } made && DiskSpace.IsMadeUp(made.Total)))
        {
            var label = Text(Loc.Format(AppStrings.Dir_Activity, activity.Percent), _hoverZone == Zone.Activity ? _text : _dim, size: Math.Max(9, FontSize - 1));
            var graph = _activityLook != ActivityLook.Bar && _activityRecent is not null;
            var graphWidth = graph ? ActivityGraphWidth : ActivityWidth;
            var width = 6 + graphWidth + 5 + Math.Max(label.Width, Text(Loc.Format(AppStrings.Dir_Activity, 100), _dim, size: Math.Max(9, FontSize - 1)).Width) + 6;
            if (right - width >= _driveButton.Right + 8 + PathRoom())
            {
                _activityBox = new Rect(right - width, buttonTop, width, ButtonSize);
                if (_hoverZone == Zone.Activity)
                {
                    context.FillRectangle(_hoverBg, _activityBox, 4);
                }

                Rect bar;
                if (graph)
                {
                    bar = new Rect(_activityBox.X + 6, (size.Height / 2) - 8, graphWidth, 16);
                    DrawActivityGraph(context, bar, _activityRecent!);
                }
                else
                {
                    bar = new Rect(_activityBox.X + 6, (size.Height / 2) - 3, ActivityWidth, 6);
                    context.FillRectangle(_track, bar, 2);
                    context.FillRectangle(_accent, bar.WithWidth(ActivityWidth * activity.Percent / 100.0), 2);
                }

                context.DrawText(label, new Point(bar.Right + 5, (size.Height - label.Height) / 2));
                right = _activityBox.X - 2;
            }
        }

        var crumbsEnd = DrawCrumbs(context, size, right - ButtonSize - 4);

        var x = Math.Clamp((size.Width / 2) - (ButtonSize / 2), crumbsEnd + 4, Math.Max(crumbsEnd + 4, right - ButtonSize));
        _history = new Rect(x, buttonTop, ButtonSize, ButtonSize);
        _editZone = new Rect(crumbsEnd, 0, Math.Max(0, right - crumbsEnd), size.Height);
        DrawHistory(context);
    }

    private double PathRoom()
    {
        var parts = Split(_path);
        return parts.Count == 0 ? 0 : Text(parts[0].Label, _text).Width + (parts.Count > 1 ? Text(parts[^1].Label, _text, bold: true).Width + 60 : 0);
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

        if (Width(first) - available is var over and > 0 && parts.Count > 1)
        {
            var last = texts[^1];
            last.MaxTextWidth = Math.Max(24, last.Width - over);
            last.MaxLineCount = 1;
            last.Trimming = TextTrimming.CharacterEllipsis;
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

            if (_archive is { } a && string.Equals(parts[i].Path, a.ArchivePath, StringComparison.OrdinalIgnoreCase))
            {
                AppIcons.Draw(context, "Icon.Archive", new Point(x - 1, (size.Height - 14) / 2), new ImmutablePen(_accent.ToImmutable(), 1.2), scale: 0.875);
                x += 16;
                bounds = bounds.WithWidth(bounds.Width + 16);
            }

            if (i == parts.Count - 1 && _folderLooks is { IsEmpty: false } looks && looks.Of(parts[i].Path) is { } look)
            {
                var at = new Rect(x - 1, Math.Round((size.Height - 14) / 2), 14, 14);
                var dark = _bg is ISolidColorBrush { Color: var c } && Salandual.Core.Settings.ColorMath.Luminance(new(c.R, c.G, c.B)) < 0.2;
                var pen = new ImmutablePen(_dim.ToImmutable(), 1.2);
                if (!FolderLooks.DrawFolder(context, look, at, dark, pen))
                {
                    AppIcons.Draw(context, "Icon.FolderLoading", at.TopLeft, pen, Brush("Warning"), 14.0 / 16);
                }

                looks.DrawBadge(context, look, at, Bold, _bg, new ImmutablePen(Brush("BorderStrong").ToImmutable(), 1), _text);
                x += 20;
                bounds = bounds.WithWidth(bounds.Width + 20);
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

    private void DrawDriveButton(DrawingContext context, Size size, double x)
    {
        var h = Math.Min(size.Height - 8, 26);
        _driveButton = new Rect(x, (size.Height - h) / 2, DriveButtonWidth, h);
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

    private void DrawActivityGraph(DrawingContext context, Rect box, IReadOnlyList<double?> recent)
    {
        context.FillRectangle(_track, new Rect(box.X, box.Bottom - 1, box.Width, 1));
        if (recent.Count == 0)
        {
            return;
        }

        var step = box.Width / recent.Count;
        double Y(double busy) => box.Bottom - 1 - ((box.Height - 1) * Math.Clamp(busy, 0, 100) / 100);
        if (_activityLook == ActivityLook.Columns)
        {
            for (var i = 0; i < recent.Count; i++)
            {
                if (recent[i] is { } busy)
                {
                    var top = Math.Min(Y(busy), box.Bottom - 2);
                    context.FillRectangle(_accent, new Rect(box.X + (i * step), top, Math.Max(1, step - 0.5), box.Bottom - 1 - top));
                }
            }

            return;
        }

        var accent = (_accent as ISolidColorBrush)?.Color ?? Colors.SteelBlue;
        var area = new Color(56, accent.R, accent.G, accent.B);
        var pen = new ImmutablePen(_accent.ToImmutable(), 1.2);
        var start = 0;
        while (start < recent.Count)
        {
            while (start < recent.Count && recent[start] is null)
            {
                start++;
            }

            var end = start;
            while (end < recent.Count && recent[end] is not null)
            {
                end++;
            }

            if (end > start)
            {
                var line = new StreamGeometry();
                var fill = new StreamGeometry();
                using (var l = line.Open())
                using (var f = fill.Open())
                {
                    var x0 = box.X + ((start + 0.5) * step);
                    l.BeginFigure(new Point(x0, Y(recent[start]!.Value)), false);
                    f.BeginFigure(new Point(x0, box.Bottom - 1), true);
                    for (var i = start; i < end; i++)
                    {
                        var p = new Point(box.X + ((i + 0.5) * step), Y(recent[i]!.Value));
                        l.LineTo(p);
                        f.LineTo(p);
                    }

                    f.LineTo(new Point(box.X + ((end - 0.5) * step), box.Bottom - 1));
                    l.EndFigure(false);
                    f.EndFigure(true);
                }

                context.DrawGeometry(new ImmutableSolidColorBrush(area), null, fill);
                context.DrawGeometry(null, pen, line);
            }

            start = end;
        }
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

    private double DrawViewButton(DrawingContext context, Size size, double right, double top)
    {
        var hover = _hoverZone == Zone.ViewButton;
        var label = size.Width >= 460 ? Text(OwnViewNames?.Invoke(_view) ?? ViewName(_view), hover || _viewMenuOpen ? _text : _dim, size: Math.Max(9, FontSize - 1)) : null;
        var width = 6 + 16 + (label is null ? 0 : 4 + label.Width) + 6;
        _viewButton = new Rect(right - width, top, width, ButtonSize);
        if (hover || _viewMenuOpen)
        {
            context.FillRectangle(_hoverBg, _viewButton, 4);
        }

        if (_viewMenuOpen)
        {
            context.DrawRectangle(null, new ImmutablePen(_track.ToImmutable(), 1), _viewButton.Deflate(0.5), 4, 4);
        }

        var pen = new ImmutablePen((hover || _viewMenuOpen ? _text : _dim).ToImmutable(), 1.3, lineCap: PenLineCap.Round);
        AppIcons.Draw(context, ViewIcon(_view), new Point(_viewButton.X + 6, _viewButton.Center.Y - 8), pen);
        if (label is not null)
        {
            context.DrawText(label, new Point(_viewButton.X + 6 + 16 + 4, (size.Height - label.Height) / 2));
        }

        return _viewButton.X - 4;
    }

    private (Rect Chip, Rect Off) DrawChip(DrawingContext context, Size size, ref double right, double top, string text, Zone zone, Zone offZone, bool icon)
    {
        var hover = _hoverZone == zone || _hoverZone == offZone;
        var small = Math.Max(9, FontSize - 1);
        var label = Text(text, icon ? _accent : _dim, size: small);
        label.MaxTextWidth = Math.Max(40, Math.Min(220, size.Width / 4));
        label.MaxLineCount = 1;
        label.Trimming = TextTrimming.CharacterEllipsis;
        var x = Text("✕", _hoverZone == offZone ? _text : _dim, size: small);
        var width = 6 + (icon ? 16 + 4 : 0) + Math.Min(label.Width, label.MaxTextWidth) + 6 + x.Width + 6;
        var chip = new Rect(right - width, top, width, ButtonSize);
        if (hover)
        {
            context.FillRectangle(_hoverBg, chip, 4);
        }

        context.DrawRectangle(null, new ImmutablePen((icon ? _accent : _track).ToImmutable(), 1), chip.Deflate(0.5), 4, 4);
        var textX = chip.X + 6;
        if (icon)
        {
            AppIcons.Draw(context, "Panel.Filter", new Point(textX, chip.Center.Y - 8), new ImmutablePen(_accent.ToImmutable(), 1.3, lineCap: PenLineCap.Round));
            textX += 16 + 4;
        }

        context.DrawText(label, new Point(textX, (size.Height - label.Height) / 2));
        var off = new Rect(chip.Right - 6 - x.Width - 4, top, x.Width + 8, ButtonSize);
        context.DrawText(x, new Point(off.X + 4, (size.Height - x.Height) / 2));
        right = chip.X - 4;
        return (chip, off);
    }

    internal static string ViewIcon(Core.Panels.PanelView view) => view switch
    {
        Core.Panels.PanelView.Columns => "Icon.ViewColumns",
        Core.Panels.PanelView.Brief => "Icon.ViewBrief",
        Core.Panels.PanelView.Icons => "Icon.ViewIcons",
        Core.Panels.PanelView.Thumbnails => "Icon.ViewThumbnails",
        Core.Panels.PanelView.Tiles => "Icon.ViewTiles",
        _ => "Icon.ViewDetailed",
    };

    internal static string ViewName(Core.Panels.PanelView view) => view switch
    {
        Core.Panels.PanelView.Columns => AppStrings.View_Columns,
        Core.Panels.PanelView.Brief => AppStrings.View_Brief,
        Core.Panels.PanelView.Icons => AppStrings.View_Icons,
        Core.Panels.PanelView.Thumbnails => AppStrings.View_Thumbnails,
        Core.Panels.PanelView.Tiles => AppStrings.View_Tiles,
        Core.Panels.PanelView.Types => AppStrings.View_Types,
        _ => AppStrings.View_Detailed,
    };

    internal Func<Core.Panels.PanelView, string?>? OwnViewNames { get; set; }

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

    private static void DrawLock(DrawingContext context, Point left, IPen pen)
    {
        context.DrawRectangle(null, pen, new Rect(left.X, left.Y - 1.5, 8, 6), 1, 1);
        var shackle = new StreamGeometry();
        using (var g = shackle.Open())
        {
            g.BeginFigure(new Point(left.X + 1.8, left.Y - 1.5), false);
            g.LineTo(new Point(left.X + 1.8, left.Y - 3.5));
            g.ArcTo(new Point(left.X + 6.2, left.Y - 3.5), new Size(2.2, 2.2), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(left.X + 6.2, left.Y - 1.5));
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, shackle);
    }

    private static void DrawChevron(DrawingContext context, IPen pen, Point center, double half)
        => AppIcons.Draw(context, "Icon.ChevronDown", center - new Point(2 * half, 2 * half), pen, scale: half / 4);

    internal static List<(string Label, string Path)> Split(string path)
    {
        var result = new List<(string, string)>();
        if (Remote.ServerAddress.Parse(path) is { } address)
        {
            var at = address.WithPath("/");
            result.Add((at.Root[at.Scheme.Length..], at.ToString()));
            foreach (var part in address.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                at = at.Child(part);
                result.Add((part, at.ToString()));
            }

            return result;
        }

        if (Core.Vfs.ShellPlace.RootOf(path) is { } place)
        {
            result.Add((Core.Vfs.ShellPlace.Label(place), place));
            if (Core.Vfs.ShellPlace.Inner(path) is { Length: > 0 } inner)
            {
                result.Add((inner, path));
            }

            return result;
        }

        if (Core.Vfs.ShellPlace.ServerOf(path) is { } server)
        {
            result.Add((Core.Vfs.ShellPlace.Label(Core.Vfs.ShellPlace.Network), Core.Vfs.ShellPlace.Network));
            result.Add((server[2..], server));
            var at = server;
            foreach (var part in path[server.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                at += "\\" + part;
                result.Add((part, at));
            }

            return result;
        }

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
        if (NavZoneAt(p) is var nav and not Zone.None)
        {
            return nav;
        }

        if (_driveButton.Contains(p))
        {
            return Zone.DriveButton;
        }

        if (_history.Contains(p))
        {
            return Zone.History;
        }

        if (_git is not null && BranchBounds.Contains(p))
        {
            return Zone.Branch;
        }

        if (_freeSpace.Contains(p))
        {
            return Zone.FreeSpace;
        }

        if (_activityBox.Contains(p))
        {
            return Zone.Activity;
        }

        if (_showMaximize && _maximize.Contains(p))
        {
            return Zone.Maximize;
        }

        if (_showViewButton && _viewButton.Contains(p))
        {
            return Zone.ViewButton;
        }

        if (_filterChip is not null && _filterChipBox.Contains(p))
        {
            return _filterOff.Contains(p) ? Zone.FilterOff : Zone.FilterChip;
        }

        if (_namesChip is not null && _namesChipBox.Contains(p))
        {
            return _namesOff.Contains(p) ? Zone.NamesOff : Zone.NamesChip;
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
            Zone.Branch => GitTip(),
            Zone.Activity => ActivityTip(),
            Zone.Maximize => _isMaximized ? AppStrings.Dir_Tip_Restore : AppStrings.Dir_Tip_Maximize,
            Zone.ViewButton => AppStrings.Dir_Tip_View,
            Zone.FilterChip => Loc.Format(AppStrings.Dir_Tip_Filter, _filterChip),
            Zone.FilterOff => AppStrings.Dir_Tip_FilterOff,
            Zone.NamesChip or Zone.NamesOff => AppStrings.Dir_Tip_ShowNames,
            _ => NavTip(zone),
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
            if (ZoneAt(p, out _) is Zone.FreeSpace or Zone.Activity)
            {
                SpaceMenuRequested?.Invoke(this, p);
            }
            else
            {
                PathMenuRequested?.Invoke(this, p);
            }

            e.Handled = true;
            return;
        }

        if (!point.IsLeftButtonPressed)
        {
            return;
        }

        if (!StartHold(ZoneAt(p, out _), e.Pointer))
        {
            Click(p);
        }

        e.Handled = true;
    }

    internal void Click(Point p)
    {
        switch (ZoneAt(p, out var crumb))
        {
            case Zone.Back or Zone.BackList or Zone.Forward or Zone.ForwardList:
                NavClick(ZoneAt(p, out _));
                break;
            case Zone.DriveButton:
                DriveButtonClicked?.Invoke(this, EventArgs.Empty);
                break;
            case Zone.History:
                HistoryRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Zone.Branch:
                ClickBranch();
                break;
            case Zone.FreeSpace or Zone.Activity:
                DriveInfoRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Zone.Maximize:
                MaximizeRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Zone.ViewButton:
                ViewMenuRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Zone.FilterChip:
                FilterMenuRequested?.Invoke(this, _filterChipBox.BottomLeft);
                break;
            case Zone.FilterOff:
                FilterOffRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Zone.NamesChip or Zone.NamesOff:
                ShowNamesRequested?.Invoke(this, EventArgs.Empty);
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
