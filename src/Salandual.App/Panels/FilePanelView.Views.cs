// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/FilePanelView.Views.cs

using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.App.Panels;

public sealed partial class FilePanelView
{
    private const double GridPad = 4;

    private const double LargeIcon = 32;

    private readonly Dictionary<int, CellText> _cells = [];
    private ThumbnailCache? _thumbs;
    private double _briefFit = -1;
    private double _lineH = 17;
    private int _thumbnailSize = PanelSettings.DefaultThumbnailSize;
    private TileLines _tileLines = TileLines.SizeAndDate;

    public PanelView ViewKind => _panel is { View: PanelView.Columns, Results: not null } ? PanelView.Detailed : _panel?.View ?? PanelView.Detailed;

    private bool IsDetailed => PanelViews.HasColumns(ViewKind);

    public int ThumbnailSize
    {
        get => _thumbnailSize;
        set
        {
            value = Math.Clamp(value, PanelSettings.MinThumbnailSize, PanelSettings.MaxThumbnailSize);
            if (_thumbnailSize != value)
            {
                _thumbnailSize = value;
                Relayout();
            }
        }
    }

    public TileLines TileLines
    {
        get => _tileLines;
        set
        {
            if (_tileLines != value)
            {
                _tileLines = value;
                ClearTexts();
                InvalidateVisual();
            }
        }
    }

    public bool AltWheelViews { get; set; } = true;

    public event EventHandler<int>? ThumbnailSizeStep;

    internal (int PerLine, int Lines, bool Down) GridShape()
    {
        var l = Layout();
        return (l.PerLine, l.Lines, l.Down);
    }

    internal Rect? CellBounds(int index)
    {
        var l = Layout();
        return IsInView(index, l) ? CellRect(index, l) : null;
    }

    private readonly record struct ViewLayout(double CellW, double CellH, int PerLine, int Lines, bool Down, double X0, double Y0);

    private ViewLayout Layout()
    {
        var size = Bounds.Size;
        switch (ViewKind)
        {
            case var view when PanelViews.HasColumns(view):
            {
                var bar = ColumnsNow().ScrollMax > 0 ? ScrollBarWidth : 0;
                return new ViewLayout(size.Width - ScrollBarWidth, RowHeight, 1, Math.Max(1, (int)((size.Height - HeaderHeight - bar) / RowHeight)), false, 0, HeaderHeight);
            }
            case PanelView.Columns:
                EnsureTheme();
                return ColumnsLayout(size);
            case PanelView.Brief:
            {
                EnsureTheme();
                var perColumn = Math.Max(1, (int)((size.Height - ScrollBarWidth) / RowHeight));
                var width = BriefCellWidth(size.Width);
                return new ViewLayout(width, RowHeight, perColumn, Math.Max(1, (int)(size.Width / width)), true, 0, 0);
            }

            default:
            {
                EnsureTheme();
                var (baseWidth, height) = CellSize();
                var room = Math.Max(1, size.Width - ScrollBarWidth - GridPad);
                var perRow = Math.Max(1, (int)(room / baseWidth));
                return new ViewLayout(room / perRow, height, perRow, Math.Max(1, (int)((size.Height - GridPad) / height)), false, GridPad, GridPad / 2);
            }
        }
    }

    private double Scale => _fontSize / 13;

    private double TilePicture => Math.Ceiling((_lineH * 3) + 4);

    private (double Width, double Height) CellSize() => ViewKind switch
    {
        PanelView.Icons => (Math.Max(80, 88 * Scale), 6 + LargeIcon + 5 + _lineH + 7),
        PanelView.Thumbnails => (_thumbnailSize + 20, 6 + _thumbnailSize + 5 + _lineH + 7),
        _ => (TilePicture + 12 + Math.Max(160, 176 * Scale), TilePicture + 12),
    };

    private double BriefCellWidth(double panelWidth)
    {
        if (_briefFit < 0)
        {
            _briefFit = 0;
            if (_panel is not null)
            {
                foreach (var e in _panel.Items.OrderByDescending(e => e.Name.Length).Take(48))
                {
                    _briefFit = Math.Max(_briefFit, Math.Ceiling(Text(Shown(e), _face, _b.Text, _fontSize).WidthIncludingTrailingWhitespace));
                }
            }
        }

        var k = Scale;
        var want = BriefNameX + _briefFit + (14 * k);
        return Math.Clamp(want, 96 * k, Math.Max(96 * k, Math.Min(panelWidth - 2, 360 * k)));
    }

    private double BriefNameX => 28 + MarkShift;

    private string Shown(FileEntry e)
        => _panel is not { Results: not null } p || p.IsGitList || (p.IsFlat && (e.IsDirectory || p.FlatFolderShows == FlatFolderShows.InName)) ? e.Name : FilePanel.SplitResult(e.Name).Name;

    private int TotalLines(ViewLayout l) => (Count + l.PerLine - 1) / l.PerLine;

    private Rect CellRect(int index, ViewLayout l)
    {
        var line = (index / l.PerLine) - (_top / l.PerLine);
        var k = index % l.PerLine;
        return l.Down
            ? new Rect(l.X0 + (line * l.CellW), l.Y0 + (k * l.CellH), l.CellW, l.CellH)
            : new Rect(l.X0 + (k * l.CellW), l.Y0 + (line * l.CellH), l.CellW, l.CellH);
    }

    private bool IsInView(int index, ViewLayout l)
        => index >= _top && index < Count && (index / l.PerLine) - (_top / l.PerLine) < l.Lines;

    private int CellAt(Point p, ViewLayout l)
    {
        if (p.X < l.X0 || p.Y < l.Y0)
        {
            return -1;
        }

        var across = (int)Math.Floor((p.X - l.X0) / l.CellW);
        var down = (int)Math.Floor((p.Y - l.Y0) / l.CellH);
        var (line, k) = l.Down ? (across, down) : (down, across);
        if (k >= l.PerLine || line < 0)
        {
            return -1;
        }

        var index = ((_top / l.PerLine) + line) * l.PerLine + k;
        return index < Count ? index : -1;
    }

    private int? GridTarget(string id, out bool extend)
    {
        extend = id.StartsWith("Selection.Extend", StringComparison.Ordinal);
        if (IsDetailed || _panel is null || Count == 0)
        {
            return null;
        }

        var l = Layout();
        var f = _panel.Focus;
        var last = Count - 1;
        var page = l.PerLine * Math.Max(1, l.Lines);
        var move = extend ? id["Selection.Extend".Length..] : id.StartsWith("Focus.", StringComparison.Ordinal) ? id["Focus.".Length..] : null;
        if (l.Down)
        {
            return move switch
            {
                "Left" => Math.Max(0, f - l.PerLine),
                "Right" => Math.Min(last, f + l.PerLine),
                "PageUp" => Math.Max(0, f - page),
                "PageDown" => Math.Min(last, f + page),
                _ => null,
            };
        }

        return move switch
        {
            "Previous" or "Up" => f - l.PerLine >= 0 ? f - l.PerLine : -1,
            "Next" or "Down" => f + l.PerLine <= last ? f + l.PerLine : f / l.PerLine < last / l.PerLine ? last : -1,
            "Left" => Math.Max(0, f - 1),
            "Right" => Math.Min(last, f + 1),
            "PageUp" => Math.Max(0, f - page),
            "PageDown" => Math.Min(last, f + page),
            _ => null,
        };
    }

    private bool TryGridMove(string id)
    {
        if (GridTarget(id, out var extend) is not { } target)
        {
            return false;
        }

        if (target >= 0)
        {
            if (extend)
            {
                _panel!.ExtendTo(target, includeTarget: false);
            }
            else
            {
                _panel!.SetFocus(target);
            }
        }

        return true;
    }

    private void Relayout()
    {
        _briefFit = -1;
        ClearTexts();
        SetTop(_top);
        EnsureVisible();
        InvalidateVisual();
    }

    private void ClearTexts()
    {
        _cache.Clear();
        _cells.Clear();
        ClearColumnTexts();
    }

    private void RenderCells(DrawingContext context, Size size)
    {
        var l = Layout();
        var panel = _panel!;
        var scaling = Avalonia.Controls.TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var active = IsActivePanel;
        var last = Math.Min(Count, _top + (l.PerLine * (l.Lines + 1)));
        var strip = ViewKind == PanelView.Columns;
        if (strip)
        {
            DrawOtherColumns(context, size, scaling);
        }

        var area = strip ? new Rect(l.X0, 0, Math.Max(0, Strip().W - 1), Math.Max(0, size.Height - Strip().Bar)) : new Rect(size);
        using (context.PushClip(area))
        {
            for (var i = _top; i < last; i++)
            {
                var cell = CellRect(i, l);
                if (cell.X >= size.Width || cell.Y >= size.Height)
                {
                    continue;
                }

                DrawCell(context, i, cell, scaling, active);
            }

            if (Count == (panel.HasUpEntry ? 1 : 0) && panel.Path.Length > 0)
            {
                var empty = Text(AppStrings.Panel_Empty, _face, _b.TertiaryText, _fontSize);
                var middle = strip ? l.X0 + (l.CellW / 2) : size.Width / 2;
                context.DrawText(empty, new Point(middle - (empty.Width / 2), Math.Min(size.Height / 2, l.Y0 + (l.CellH * 1.5))));
            }
        }

        if (_dropRow == DropOnPanel)
        {
            context.DrawRectangle(null, _b.DropPen, new Rect(size).Deflate(1), 4, 4);
        }

        DrawScrollBar(context, size);
        if (strip)
        {
            DrawStripBar(context, size);
        }

        DrawPeek(context, size);
        DrawQuickSearch(context, size);
        EvictCache();
    }

    private void DrawCell(DrawingContext context, int i, Rect cell, double scaling, bool active)
    {
        var panel = _panel!;
        var entry = panel.Items[i];
        var up = panel.IsUp(i);
        var box = ViewKind is PanelView.Brief or PanelView.Columns ? new Rect(cell.X + 3, cell.Y, Math.Max(0, cell.Width - 6), cell.Height) : cell.Deflate(new Thickness(2, 2));
        var t = GetCellText(i, cell.Width);
        var selected = panel.IsSelected(i);
        var focused = i == panel.Focus;
        var nameRect = CellNameMark(t, cell, box);
        if (i == _dropRow)
        {
            context.FillRectangle(_b.SelectionBackground, box, 4);
            context.DrawRectangle(null, _b.DropPen, box.Deflate(1), 4, 4);
        }
        else
        {
            if (i == _hover && !(selected && _selectionLook == MarkLook.WholeRow))
            {
                context.FillRectangle(_b.Hover, box, 4);
            }

            FillMarks(context, selected, focused, active, box, nameRect);
        }

        using (IsCut is { } isCut && !up && isCut(entry.Name) ? context.PushOpacity(0.45) : default(DrawingContext.PushedState?))
        {
            switch (ViewKind)
            {
                case PanelView.Brief or PanelView.Columns:
                    DrawIconAt(context, entry, up, new Rect(box.X + 4 + MarkShift, box.Y + ((box.Height - IconSize) / 2), IconSize, IconSize), scaling);
                    if (panel.CompareMarks is { } marks && !up && marks.TryGetValue(entry.Name, out var mark))
                    {
                        var glyph = MarkText(mark);
                        context.DrawText(glyph, new Point(box.X + 2, box.Y + ((box.Height - glyph.Height) / 2)));
                    }

                    context.DrawText(t.Name, new Point(cell.X + BriefNameX, box.Y + ((box.Height - t.Name.Height) / 2)));
                    if (ViewKind == PanelView.Columns && entry.IsDirectory && !up)
                    {
                        DrawChevron(context, box);
                    }

                    break;
                case PanelView.Icons:
                    DrawIconAt(context, entry, up, new Rect(box.Center.X - (LargeIcon / 2), box.Y + 4, LargeIcon, LargeIcon), scaling);
                    context.DrawText(t.Name, new Point(cell.X + 4, box.Y + 4 + LargeIcon + 5));
                    break;
                case PanelView.Thumbnails:
                    var s = _thumbnailSize;
                    DrawPicture(context, entry, up, new Rect(box.Center.X - (s / 2.0), box.Y + 4, s, s), scaling, iconShare: 0.6);
                    context.DrawText(t.Name, new Point(cell.X + 4, box.Y + 4 + s + 5));
                    break;
                default:
                    var p = TilePicture;
                    DrawPicture(context, entry, up, new Rect(box.X + 4, box.Y + ((box.Height - p) / 2), p, p), scaling, iconShare: 0.9);
                    var x = box.X + 4 + p + 8;
                    var y = box.Y + ((box.Height - (_lineH * 3)) / 2);
                    context.DrawText(t.Name, new Point(x, y));
                    if (t.Line2 is { } line2)
                    {
                        context.DrawText(line2, new Point(x, y + _lineH));
                    }

                    if (t.Line3 is { } line3)
                    {
                        context.DrawText(line3, new Point(x, y + (_lineH * 2)));
                    }

                    break;
            }
        }

        if (focused)
        {
            DrawFocus(context, active, box, nameRect);
        }
    }

    private Rect CellNameMark(CellText t, Rect cell, Rect box)
    {
        var w = t.Name.Width;
        var h = t.Name.Height;
        var rect = ViewKind switch
        {
            PanelView.Brief or PanelView.Columns => new Rect(box.X, box.Y, cell.X + BriefNameX + w + 5 - box.X, box.Height),
            PanelView.Icons => new Rect(cell.X + 4 + ((t.Name.MaxTextWidth - w) / 2) - 4, box.Y + 4 + LargeIcon + 5 - 2, w + 8, h + 4),
            PanelView.Thumbnails => new Rect(cell.X + 4 + ((t.Name.MaxTextWidth - w) / 2) - 4, box.Y + 4 + _thumbnailSize + 5 - 2, w + 8, h + 4),
            _ => new Rect(box.X + 4 + TilePicture + 8 - 4, box.Y + ((box.Height - (_lineH * 3)) / 2) - 2, w + 8, h + 4),
        };
        return rect.Intersect(box);
    }

    private void DrawIconAt(DrawingContext context, in FileEntry entry, bool up, Rect at, double scaling)
    {
        var px = (int)Math.Round(at.Width * scaling);
        if (HasFolderColour(entry, up))
        {
            DrawFolderLook(context, entry, up, at, badgeOnly: false);
            return;
        }

        if (!up && IconOf(entry, px) is { } icon)
        {
            context.DrawImage(icon, at);
            DrawFolderLook(context, entry, up, at, badgeOnly: true);
            return;
        }

        var scale = at.Width / 16;
        var origin = at.TopLeft;
        if (up)
        {
            AppIcons.Draw(context, "Icon.ParentFolder", origin, scale == 1 ? _b.IconPen : new ImmutablePen(_b.SecondaryText.ToImmutable(), 1.5 / Math.Sqrt(scale), lineCap: PenLineCap.Round), scale: scale);
        }
        else
        {
            AppIcons.Draw(context, entry.IsDirectory ? "Icon.FolderLoading" : "Icon.FileLoading", origin, _b.IconPen, entry.IsDirectory ? _b.Folder : _b.FileIcon, scale);
        }
    }

    private void DrawPicture(DrawingContext context, in FileEntry entry, bool up, Rect box, double scaling, double iconShare)
    {
        var none = true;
        Thumbnail? thumb = null;
        if (!up && _thumbs is { } thumbs && _packedSize is null && _place is null && !(entry.IsDirectory && HasFolderLook(entry)))
        {
            thumb = thumbs.Get(_panel!.Path, entry, (int)Math.Round(box.Width * scaling), out none);
        }

        if (thumb is { } t)
        {
            var w = Math.Max(1, t.Image.PixelSize.Width);
            var h = Math.Max(1, t.Image.PixelSize.Height);
            var fit = Math.Min(Math.Min(box.Width / w, box.Height / h), 1 / scaling);
            var rect = new Rect(box.Center.X - (w * fit / 2), box.Center.Y - (h * fit / 2), w * fit, h * fit);
            context.DrawImage(t.Image, rect);
            context.DrawRectangle(null, _b.PicturePen, rect.Deflate(0.5));
            if (t.Length is { } length && box.Width >= 64)
            {
                DrawBadge(context, Duration(length), new Point(rect.Right - 3, rect.Bottom - 3));
            }
        }
        else if (!none)
        {
            using (context.PushClip(new RoundedRect(box, 4)))
            {
                context.FillRectangle(_b.Hover, box);
                for (var d = -box.Height; d < box.Width; d += 10)
                {
                    context.DrawLine(_b.StripePen, new Point(box.X + d, box.Bottom), new Point(box.X + d + box.Height, box.Y));
                }
            }
        }
        else
        {
            var side = Math.Round(Math.Max(IconSize, box.Width * iconShare));
            DrawIconAt(context, entry, up, new Rect(box.Center.X - (side / 2), box.Center.Y - (side / 2), side, side), scaling);
        }

        if (!up && ThumbnailCache.IsOnlineOnly(entry))
        {
            var cloud = Text("☁", _face, _b.SecondaryText, Math.Max(10, _fontSize));
            var at = new Point(box.X + 2, box.Bottom - cloud.Height - 1);
            context.FillRectangle(_b.Background, new Rect(at, new Size(cloud.Width + 2, cloud.Height)).Inflate(1), 3);
            context.DrawText(cloud, at + new Point(1, 0));
        }
    }

    private void DrawBadge(DrawingContext context, string text, Point corner)
    {
        var label = Text(text, _face, Avalonia.Media.Brushes.White, Math.Max(9, _fontSize - 2));
        var rect = new Rect(corner.X - label.Width - 8, corner.Y - label.Height - 2, label.Width + 8, label.Height + 2);
        context.FillRectangle(_b.Badge, rect, 3);
        context.DrawText(label, new Point(rect.X + 4, rect.Y + 1));
    }

    private static string Duration(TimeSpan t)
        => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private sealed record CellText(FormattedText Name, FormattedText? Line2, FormattedText? Line3);

    private CellText GetCellText(int index, double cellWidth)
    {
        if (_cells.TryGetValue(index, out var t))
        {
            return t;
        }

        var panel = _panel!;
        var e = panel.Items[index];
        var up = panel.IsUp(index);
        var selected = panel.IsSelected(index);
        var (nameBrush, nameFace) = NameLook(e, up, selected, !up && IsCut?.Invoke(e.Name) == true);
        var dim = RowInk(e, up, selected, !up && IsCut?.Invoke(e.Name) == true);
        var name = Text(Shown(e), nameFace, nameBrush, _fontSize);
        name.MaxLineCount = 1;
        name.Trimming = TextTrimming.CharacterEllipsis;
        FormattedText? line2 = null, line3 = null;
        switch (ViewKind)
        {
            case PanelView.Brief or PanelView.Columns:
                name.MaxTextWidth = Math.Max(10, cellWidth - BriefNameX - 8 - (ViewKind == PanelView.Columns ? ChevronRoom : 0));
                break;
            case PanelView.Icons or PanelView.Thumbnails:
                name.MaxTextWidth = Math.Max(10, cellWidth - 8);
                name.TextAlignment = TextAlignment.Center;
                break;
            default:
                var room = Math.Max(10, cellWidth - TilePicture - 24);
                name.MaxTextWidth = room;
                var local = up ? default : e.LastWriteUtc.ToLocalTime();
                var (second, third) = up ? (AppStrings.Panel_UpALevel, "")
                    : _tileLines == TileLines.TypeAndSize ? (TypeName(e), e.IsDirectory ? _format.Date(local) + " " + _format.Time(local) : SizeText(panel, e, up))
                    : (SizeText(panel, e, up), _format.Date(local) + " " + _format.Time(local));
                line2 = Line(second, dim, room);
                line3 = third.Length > 0 ? Line(third, dim, room) : null;
                break;
        }

        t = new CellText(name, line2, line3);
        _cells[index] = t;
        return t;
    }

    private FormattedText Line(string text, IBrush brush, double width)
    {
        var line = Text(text, _face, brush, Math.Max(9, _fontSize - 1));
        line.MaxTextWidth = width;
        line.MaxLineCount = 1;
        line.Trimming = TextTrimming.CharacterEllipsis;
        return line;
    }

    private static string TypeName(in FileEntry e) => TypeNames.Of(e);

    private (IBrush Brush, Typeface Face) NameLook(in FileEntry e, bool up, bool selected, bool cut, bool git = true)
    {
        var rule = up ? null : _highlighter.Match(e);
        var face = rule?.Style switch { NameStyle.Bold => _bold, NameStyle.Italic => _italic, _ => _face };
        if (git && GitNameLook(e, up, face) is { } look)
        {
            return (selected ? _b.SelectedText : cut ? _b.TertiaryText : look.Brush, look.Face);
        }

        var brush = selected ? _b.SelectedText : cut ? _b.TertiaryText : rule is null ? _b.Text : RuleBrush(rule);
        return (brush, face);
    }

    internal (IBrush Brush, Typeface Face) NameLookOf(int index)
    {
        EnsureTheme();
        var e = _panel!.Items[index];
        return NameLook(e, _panel.IsUp(index), _panel.IsSelected(index), false);
    }

    private IBrush RuleBrush(HighlightRule rule)
    {
        if (!_ruleBrushes.TryGetValue(rule, out var brush))
        {
            brush = Highlighter.Colour(rule.Colour, _b.BackgroundRgb, t => ColorOf(Brush(t)), rule.OwnLight, rule.OwnDark) is { } c
                ? new ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B))
                : _b.Text;
            _ruleBrushes[rule] = brush;
        }

        return brush;
    }

    private static Rgb ColorOf(IBrush brush) => brush is ISolidColorBrush { Color: var c } ? new Rgb(c.R, c.G, c.B) : new Rgb(0x80, 0x80, 0x80);

    private bool DrawFolderLook(DrawingContext context, in FileEntry entry, bool up, Rect at, bool badgeOnly)
    {
        if (up || !entry.IsDirectory || _folderLooks is not { IsEmpty: false } looks || _panel is null
            || looks.Of(Salandual.Platform.Windows.FileSystem.WindowsPath.Join(_panel.Path, entry.Name)) is not { } look)
        {
            return false;
        }

        var drawn = !badgeOnly && FolderLooks.DrawFolder(context, look, at, _b.Dark, _b.IconPen);
        looks.DrawBadge(context, look, at, _bold, _b.Background, _b.BadgePen, _b.Text);
        return drawn;
    }

    private Rect CellNameBounds(int index, ViewLayout l)
    {
        var cell = CellRect(index, l);
        return ViewKind switch
        {
            PanelView.Brief or PanelView.Columns => new Rect(cell.X + BriefNameX - 5, cell.Y, Math.Max(120, cell.Width - BriefNameX + 5), cell.Height),
            PanelView.Icons => new Rect(cell.X, cell.Y + 6 + LargeIcon + 3, Math.Max(120, cell.Width), _lineH + 6),
            PanelView.Thumbnails => new Rect(cell.X, cell.Y + 6 + _thumbnailSize + 3, Math.Max(120, cell.Width), _lineH + 6),
            _ => new Rect(cell.X + 6 + TilePicture + 3, cell.Y + ((cell.Height - (_lineH * 3)) / 2) - 2, Math.Max(120, cell.Width - TilePicture - 12), _lineH + 4),
        };
    }

    private bool IsOnCellName(int index, Point p, ViewLayout l)
    {
        var bounds = CellNameBounds(index, l);
        var text = GetCellText(index, l.CellW).Name;
        var left = ViewKind is PanelView.Icons or PanelView.Thumbnails ? bounds.Center.X - (text.Width / 2) : bounds.X + 5;
        return p.Y >= bounds.Y && p.Y <= bounds.Bottom && p.X >= left - 2 && p.X <= left + text.Width + 4;
    }
}
