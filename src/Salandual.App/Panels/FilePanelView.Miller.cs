// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/FilePanelView.Miller.cs

using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Vfs;

namespace Salandual.App.Panels;

public sealed partial class FilePanelView
{
    private const double ChevronRoom = 14;

    private static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(140);

    private readonly List<MillerColumn> _above = [];
    private MillerColumn? _next;
    private FilePreview? _preview;
    private string? _millerPath;
    private object? _millerKey;
    private double? _stripScroll;
    private bool _draggingStrip;
    private (double Left, int Chars)? _edgeDrag;
    private int _columnHit = -1;
    private (double From, long Start)? _slide;
    private DispatcherTimer? _columnsTimer;
    private int _columnChars = Core.Settings.PanelSettings.DefaultColumnsWidth;
    private double _charW = -1;
    private bool _columnsPreview = true;

    public int ColumnChars
    {
        get => _columnChars;
        set
        {
            value = Math.Clamp(value, Core.Settings.PanelSettings.MinColumnsWidth, Core.Settings.PanelSettings.MaxColumnsWidth);
            if (_columnChars != value && _edgeDrag is null)
            {
                _columnChars = value;
                Relayout();
            }
        }
    }

    public bool ColumnsPreview
    {
        get => _columnsPreview;
        set
        {
            if (_columnsPreview != value)
            {
                _columnsPreview = value;
                _preview = null;
                SyncColumns();
                InvalidateVisual();
            }
        }
    }

    public bool Animate { get; set; }

    public event EventHandler<int>? ColumnCharsChanged;

    private sealed class MillerColumn(string path)
    {
        public string Path { get; } = path;

        public FileEntry[]? Items { get; set; }

        public string? Error { get; set; }

        public int Top { get; set; } = -1;

        public bool Stale { get; set; }

        public IDisposable? Watch { get; set; }

        public CancellationTokenSource? Cts { get; set; }

        public Dictionary<int, FormattedText> Texts { get; } = [];
    }

    private sealed class FilePreview(string path, long ticks)
    {
        public string Path { get; } = path;

        public long Ticks { get; } = ticks;

        public string[]? Lines { get; set; }

        public List<FormattedText>? Texts { get; set; }
    }

    internal (IReadOnlyList<string> Above, string? Next, bool NextRead) ColumnsShown()
        => ([.. _above.Select(c => c.Path)], _next?.Path, _next?.Items is not null);

    internal IReadOnlyList<string>? ColumnItems(string path)
        => (_above.Concat(_next is null ? [] : [_next]).FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))?.Items?.Select(e => e.Name).ToList();

    internal (double Width, double Offset, double Max) StripShape()
    {
        var s = Strip();
        return (s.W, s.Offset, Math.Max(0, s.Total - Bounds.Width));
    }

    internal IReadOnlyList<string>? PreviewLines => _preview?.Lines;

    private readonly record struct StripLayout(double W, double Offset, double Total, double Bar);

    private double CharW
    {
        get
        {
            if (_charW <= 0)
            {
                EnsureTheme();
                _charW = Text(new string([.. Enumerable.Range('a', 26).Select(c => (char)c)]), _face, _b.Text, _fontSize).Width / 26;
            }

            return _charW;
        }
    }

    private StripLayout Strip()
    {
        var width = Math.Max(1, Bounds.Width);
        var w = Math.Round((_columnChars * CharW) + BriefNameX + ChevronRoom + 12);
        var fit = _above.Count > 0 ? 3 : 2;
        if (width < fit * w)
        {
            w = Math.Max(Math.Floor(width / fit), Math.Round(w * 0.6));
        }

        w = Math.Max(60, Math.Min(w, _above.Count > 0 ? Math.Floor(width / 2) : width));
        var total = (_above.Count + 2) * w;
        var max = Math.Max(0, total - width);
        var parent = Math.Max(0, (_above.Count - 1) * w);
        var auto = Math.Min(max, Math.Min(total - width, parent));
        var offset = Math.Clamp(_stripScroll ?? Math.Max(0, auto), 0, max);
        if (_slide is { } slide && _stripScroll is null)
        {
            var t = Stopwatch.GetElapsedTime(slide.Start) / SlideTime;
            if (t >= 1)
            {
                _slide = null;
            }
            else
            {
                var ease = 1 - Math.Pow(1 - t, 3);
                offset = offset + ((slide.From - offset) * (1 - ease));
            }
        }

        return new StripLayout(w, offset, total, max > 0 ? ScrollBarWidth : 0);
    }

    private ViewLayout ColumnsLayout(Size size)
    {
        var s = Strip();
        var rows = Math.Max(1, (int)((size.Height - s.Bar) / RowHeight));
        var cellW = s.W - (Count > rows ? ScrollBarWidth - 4 : 0);
        return new ViewLayout(cellW, RowHeight, 1, rows, false, (_above.Count * s.W) - s.Offset, 0);
    }

    private (double X, double Bottom) VBar()
    {
        if (ViewKind != PanelView.Columns)
        {
            return (Bounds.Width - ScrollBarWidth, Bounds.Height);
        }

        var s = Strip();
        return ((_above.Count * s.W) - s.Offset + s.W - ScrollBarWidth, Bounds.Height - s.Bar);
    }

    private void SyncColumns()
    {
        if (_panel is null || ViewKind != PanelView.Columns)
        {
            ClearColumns();
            return;
        }

        var panel = _panel;
        var key = panel.ColumnKey;
        if (!Equals(key, _millerKey))
        {
            ClearColumns();
            _millerKey = key;
        }

        if (!string.Equals(panel.Path, _millerPath, StringComparison.OrdinalIgnoreCase))
        {
            var before = _millerPath is null ? (double?)null : Strip().Offset;
            var oldPath = _millerPath;
            var kept = _above.ToDictionary(c => c.Path, StringComparer.OrdinalIgnoreCase);
            if (_next is not null)
            {
                kept.TryAdd(_next.Path, _next);
                _next = null;
            }

            _above.Clear();
            foreach (var path in panel.Ancestors())
            {
                if (kept.Remove(path, out var column))
                {
                    column.Top = -1;
                    _above.Add(column);
                }
                else
                {
                    _above.Add(Load(new MillerColumn(path)));
                }
            }

            foreach (var gone in kept.Values)
            {
                Drop(gone);
            }

            _millerPath = panel.Path;
            _stripScroll = null;
            _slide = null;
            if (Animate && before is { } from && oldPath is not null && Related(oldPath, panel.Path) && Math.Abs(from - Strip().Offset) > 0.5)
            {
                _slide = (from, Stopwatch.GetTimestamp());
            }
        }

        var focused = panel.FocusedEntry;
        string? nextPath = focused is { IsDirectory: true } f && !panel.IsUp(panel.Focus) && panel.Path.Length > 0 ? panel.ChildPath(panel.Path, f.Name) : null;
        if (!string.Equals(_next?.Path, nextPath, StringComparison.OrdinalIgnoreCase))
        {
            if (_next is not null)
            {
                Drop(_next);
            }

            _next = nextPath is null ? null : Load(new MillerColumn(nextPath));
        }

        if (focused is { IsDirectory: false } file && _columnsPreview)
        {
            ReadPreview(file);
        }
        else
        {
            _preview = null;
        }
    }

    private static bool Related(string a, string b)
        => a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);

    private MillerColumn Load(MillerColumn column)
    {
        column.Cts?.Cancel();
        var cts = column.Cts = new CancellationTokenSource();
        var panel = _panel!;
        _ = Read();
        column.Watch ??= panel.WatchColumn(column.Path, () =>
        {
            column.Stale = true;
            ScheduleColumns();
        });
        return column;

        async Task Read()
        {
            try
            {
                var items = await panel.ListColumnAsync(column.Path, cts.Token);
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                column.Items = items;
                column.Error = null;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                column.Items = [];
                column.Error = ex.Message;
            }

            column.Texts.Clear();
            InvalidateVisual();
        }
    }

    private void ScheduleColumns()
    {
        if (_columnsTimer is null)
        {
            _columnsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _columnsTimer.Tick += (_, _) =>
            {
                _columnsTimer?.Stop();
                foreach (var c in _above.Concat(_next is null ? [] : [_next]).Where(c => c.Stale))
                {
                    c.Stale = false;
                    Load(c);
                }
            };
        }

        _columnsTimer.Stop();
        _columnsTimer.Start();
    }

    private static void Drop(MillerColumn column)
    {
        column.Cts?.Cancel();
        column.Watch?.Dispose();
        column.Watch = null;
    }

    private void ClearColumns()
    {
        foreach (var c in _above)
        {
            Drop(c);
        }

        if (_next is not null)
        {
            Drop(_next);
        }

        _above.Clear();
        _next = null;
        _preview = null;
        _millerPath = null;
        _millerKey = null;
        _stripScroll = null;
        _slide = null;
    }

    private void ClearColumnTexts()
    {
        _charW = -1;
        foreach (var c in _above)
        {
            c.Texts.Clear();
        }

        _next?.Texts.Clear();
        if (_preview is not null)
        {
            _preview.Texts = null;
        }
    }

    private void ReadPreview(FileEntry file)
    {
        var panel = _panel!;
        var path = panel.ChildPath(panel.Path, file.Name);
        if (_preview is { } p && p.Path == path && p.Ticks == file.LastWriteUtcTicks)
        {
            return;
        }

        var preview = _preview = new FilePreview(path, file.LastWriteUtcTicks);
        if (_packedSize is not null || _place is not null || file.Size == 0 || ThumbnailCache.IsOnlineOnly(file) || (file.Attributes & FileAttributes.Offline) != 0
            || !System.IO.Path.IsPathFullyQualified(path))
        {
            return;
        }

        if (TorrentFacts.MayRead(path, file))
        {
            var entry = file;
            void Show()
            {
                if (ReferenceEquals(_preview, preview) && TorrentFacts.Get(path, entry, Show) is { } summary)
                {
                    preview.Lines = TorrentFacts.Lines(summary, _format);
                    preview.Texts = null;
                    InvalidateVisual();
                }
            }

            Show();
            return;
        }

        _ = Task.Run(() => TextStart(path)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            preview.Lines = t.IsCompletedSuccessfully ? t.Result : null;
            if (ReferenceEquals(_preview, preview))
            {
                InvalidateVisual();
            }
        }), TaskScheduler.Default);
    }

    internal static string[]? TextStart(string path, int maxLines = 60)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var buffer = new byte[4096];
            int n;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1))
            {
                n = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            }

            return TextLines(buffer.AsSpan(0, n), maxLines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    internal static string[]? TextLines(ReadOnlySpan<byte> bytes, int maxLines)
    {
        string text;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(bytes[2..]);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }
        else
        {
            if (bytes.IndexOf((byte)0) >= 0)
            {
                return null;
            }

            text = Encoding.UTF8.GetString(bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? bytes[3..] : bytes);
        }

        var odd = 0;
        foreach (var c in text)
        {
            if (c == '�' || (c < ' ' && c is not ('\t' or '\r' or '\n' or '\f')))
            {
                odd++;
            }
        }

        if (text.Length == 0 || odd > 2 + (text.Length / 50))
        {
            return null;
        }

        return [.. text.Split('\n').Take(maxLines).Select(l => l.TrimEnd('\r').Replace("\t", "    ", StringComparison.Ordinal)).Select(l => l.Length > 300 ? l[..300] : l)];
    }

    private void DrawOtherColumns(DrawingContext context, Size size, double scaling)
    {
        var s = Strip();
        var height = Math.Max(0, size.Height - s.Bar);
        var count = _above.Count + 2;
        for (var i = 0; i < count; i++)
        {
            var x = (i * s.W) - s.Offset;
            if (x + s.W <= 0 || x >= size.Width)
            {
                continue;
            }

            var rect = new Rect(x, 0, i == count - 1 ? Math.Max(s.W, size.Width - x) : s.W, height);
            if (i < _above.Count)
            {
                var open = i + 1 < _above.Count ? _above[i + 1].Path : _panel!.Path;
                DrawColumn(context, _above[i], rect, _panel!.NameOf(open), scaling);
            }
            else if (i == count - 1)
            {
                if (_next is not null)
                {
                    DrawColumn(context, _next, rect, null, scaling);
                }
                else if (_panel!.FocusedEntry is { IsDirectory: false } file)
                {
                    DrawFileColumn(context, file, rect, scaling);
                }
            }

            if (i < count - 1)
            {
                context.FillRectangle(_b.Border, new Rect(Math.Round(x + s.W) - 1, 0, 1, height));
            }
        }

        if (s.Offset > 0.5)
        {
            var shade = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(_b.Dark ? (byte)110 : (byte)46, 0, 0, 0), 0), new GradientStop(Colors.Transparent, 1) },
            };
            context.FillRectangle(shade, new Rect(0, 0, 10, height));
        }
    }

    private void DrawColumn(DrawingContext context, MillerColumn column, Rect rect, string? open, double scaling)
    {
        using var clip = context.PushClip(rect);
        var y0 = rect.Y;
        if (column.Error is { } error)
        {
            DrawColumnNote(context, Loc.Format(AppStrings.Columns_CantRead, error), rect);
            return;
        }

        if (column.Items is not { } items)
        {
            return;
        }

        if (items.Length == 0)
        {
            DrawColumnNote(context, AppStrings.Panel_Empty, rect);
            return;
        }

        var rows = Math.Max(1, (int)(rect.Height / RowHeight));
        var openIndex = open is null ? -1 : Array.FindIndex(items, e => string.Equals(e.Name, open, StringComparison.OrdinalIgnoreCase));
        if (column.Top < 0)
        {
            column.Top = openIndex < rows ? 0 : openIndex - (rows / 2);
        }

        column.Top = Math.Clamp(column.Top, 0, Math.Max(0, items.Length - rows));
        var nameWidth = Math.Max(10, rect.Width - BriefNameX - 8 - ChevronRoom);
        for (var i = column.Top; i < Math.Min(items.Length, column.Top + rows + 1); i++)
        {
            var e = items[i];
            var box = new Rect(rect.X + 3, y0 + ((i - column.Top) * RowHeight), Math.Max(0, rect.Width - 6), RowHeight);
            if (i == openIndex)
            {
                context.FillRectangle(_b.FocusBackground, box, 4);
            }

            DrawEntryIcon(context, column.Path, e, new Rect(box.X + 4 + MarkShift, box.Y + ((box.Height - IconSize) / 2), IconSize, IconSize), scaling);
            if (!column.Texts.TryGetValue(i, out var name))
            {
                var (brush, face) = NameLook(e, false, false, false, git: false);
                name = Text(e.Name, face, brush, _fontSize);
                name.MaxTextWidth = nameWidth;
                name.MaxLineCount = 1;
                name.Trimming = TextTrimming.CharacterEllipsis;
                column.Texts[i] = name;
            }

            context.DrawText(name, new Point(rect.X + BriefNameX, box.Y + ((box.Height - name.Height) / 2)));
            if (e.IsDirectory)
            {
                DrawChevron(context, box);
            }
        }

        if (items.Length > rows)
        {
            var h = Math.Max(24, rect.Height * rows / items.Length);
            var y = rect.Y + ((rect.Height - h) * column.Top / Math.Max(1, items.Length - rows));
            context.FillRectangle(_b.Thumb, new Rect(rect.Right - 5, y + 2, 3, h - 4), 1.5f);
        }
    }

    private void DrawColumnNote(DrawingContext context, string text, Rect rect)
    {
        var note = Text(text, _face, _b.TertiaryText, _fontSize);
        note.MaxTextWidth = Math.Max(10, rect.Width - 20);
        note.MaxLineCount = 4;
        note.Trimming = TextTrimming.CharacterEllipsis;
        context.DrawText(note, new Point(rect.X + 10, rect.Y + 8));
    }

    private void DrawChevron(DrawingContext context, Rect box)
    {
        AppIcons.Draw(context, "Icon.ChevronRight", new Point(box.Right - 14, box.Center.Y - 6), _b.IconPen, scale: 0.75);
    }

    private void DrawEntryIcon(DrawingContext context, string folder, in FileEntry entry, Rect at, double scaling)
    {
        var look = entry.IsDirectory && _folderLooks is { IsEmpty: false } looks
            ? looks.Of(Salandual.Platform.Windows.FileSystem.WindowsPath.Join(folder, entry.Name))
            : null;
        if (look is not null && FolderLooks.DrawFolder(context, look, at, _b.Dark, _b.IconPen))
        {
            _folderLooks!.DrawBadge(context, look, at, _bold, _b.Background, _b.BadgePen, _b.Text);
            return;
        }

        if (_icons?.Get(folder, entry, (int)Math.Round(at.Width * scaling)) is { } icon)
        {
            context.DrawImage(icon, at);
        }
        else
        {
            AppIcons.Draw(context, entry.IsDirectory ? "Icon.FolderLoading" : "Icon.FileLoading", at.TopLeft, _b.IconPen, entry.IsDirectory ? _b.Folder : _b.FileIcon);
        }

        if (look is not null)
        {
            _folderLooks!.DrawBadge(context, look, at, _bold, _b.Background, _b.BadgePen, _b.Text);
        }
    }

    private void DrawFileColumn(DrawingContext context, in FileEntry file, Rect rect, double scaling)
    {
        using var clip = context.PushClip(rect);
        var area = rect.Deflate(new Thickness(10, 10, 12, 10));
        var y = area.Y;
        if (_columnsPreview)
        {
            var side = Math.Round(Math.Min(area.Width, Math.Max(64, area.Height * 0.45)));
            var box = new Rect(area.X, y, area.Width, side);
            if (_preview?.Lines is { } lines && !HasThumbnail(file, box, scaling))
            {
                DrawTextPreview(context, lines, box);
            }
            else
            {
                DrawPicture(context, file, false, box, scaling, iconShare: Math.Min(0.6, 64 / Math.Max(64, box.Width)));
            }

            y += side + 12;
        }

        var name = Text(file.Name, _bold, _b.Text, _fontSize);
        name.MaxTextWidth = Math.Max(10, area.Width);
        name.MaxLineCount = 3;
        name.Trimming = TextTrimming.CharacterEllipsis;
        context.DrawText(name, new Point(area.X, y));
        y += name.Height + 8;
        var local = file.LastWriteUtc.ToLocalTime();
        var facts = new List<(string Label, string Value)>
        {
            (ColumnNames.Of(PanelColumn.Size), SizeText(_panel!, file, false)),
            (ColumnNames.Of(PanelColumn.Modified), _format.Date(local) + " " + _format.Time(local)),
        };
        if (file.CreationUtcTicks > 0)
        {
            var created = new DateTime(file.CreationUtcTicks, DateTimeKind.Utc).ToLocalTime();
            facts.Add((ColumnNames.Of(PanelColumn.Created), _format.Date(created) + " " + _format.Time(created)));
        }

        facts.Add((ColumnNames.Of(PanelColumn.Type), TypeName(file)));
        var labels = facts.Select(f => Text(f.Label, _face, _b.SecondaryText, Math.Max(9, _fontSize - 1))).ToList();
        var labelW = Math.Min(area.Width * 0.4, labels.Max(l => l.Width) + 10);
        for (var i = 0; i < facts.Count; i++)
        {
            var value = Line(facts[i].Value, _b.Text, Math.Max(10, area.Width - labelW));
            context.DrawText(labels[i], new Point(area.X, y));
            context.DrawText(value, new Point(area.X + labelW, y));
            y += Math.Max(labels[i].Height, value.Height) + 3;
        }
    }

    private bool HasThumbnail(in FileEntry file, Rect box, double scaling)
    {
        if (_thumbs is not { } thumbs || _packedSize is not null || _place is not null)
        {
            return false;
        }

        return thumbs.Get(_panel!.Path, file, (int)Math.Round(box.Width * scaling), out _) is not null;
    }

    private void DrawTextPreview(DrawingContext context, string[] lines, Rect box)
    {
        context.DrawRectangle(_b.Background, new ImmutablePen(_b.Border.ToImmutable(), 1), box.Deflate(0.5), 4, 4);
        var preview = _preview!;
        var size = Math.Max(9, _fontSize - 2);
        preview.Texts ??= [.. lines.Select(l =>
        {
            var t = Text(l.Length == 0 ? " " : l, _mono, _b.SecondaryText, size);
            t.MaxLineCount = 1;
            t.MaxTextWidth = Math.Max(10, box.Width - 14);
            t.Trimming = TextTrimming.CharacterEllipsis;
            return t;
        })];
        using var clip = context.PushClip(box.Deflate(1));
        var y = box.Y + 6;
        foreach (var t in preview.Texts)
        {
            if (y > box.Bottom - 4)
            {
                break;
            }

            context.DrawText(t, new Point(box.X + 7, y));
            y += t.Height;
        }
    }

    private void DrawStripBar(DrawingContext context, Size size)
    {
        var s = Strip();
        if (s.Bar <= 0)
        {
            return;
        }

        var (x, w) = StripThumb(s);
        context.FillRectangle(_draggingStrip ? _b.TertiaryText : _b.Thumb, new Rect(x + 2, size.Height - ScrollBarWidth + 3, Math.Max(0, w - 4), ScrollBarWidth - 6), 2);
        if (_slide is not null)
        {
            Avalonia.Controls.TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => InvalidateVisual());
        }
    }

    private (double X, double Width) StripThumb(StripLayout s)
    {
        var width = Bounds.Width;
        var w = Math.Max(24, width * width / Math.Max(1, s.Total));
        var max = Math.Max(1, s.Total - width);
        return ((width - w) * s.Offset / max, w);
    }

    private (Zone Zone, int Row) ColumnsHit(Point p)
    {
        var s = Strip();
        if (s.Bar > 0 && p.Y >= Bounds.Height - s.Bar)
        {
            return (Zone.Strip, -1);
        }

        var count = _above.Count + 2;
        for (var i = 0; i < count - 1; i++)
        {
            if (Math.Abs(p.X - (((i + 1) * s.W) - s.Offset)) <= 3)
            {
                return (Zone.Edge, i);
            }
        }

        var col = Math.Min(count - 1, (int)Math.Floor((p.X + s.Offset) / s.W));
        if (col == _above.Count)
        {
            var l = ColumnsLayout(Bounds.Size);
            if (Count > l.Lines && p.X >= l.X0 + s.W - ScrollBarWidth)
            {
                return (Zone.ScrollBar, -1);
            }

            return CellAt(p, l) is var cell and >= 0 ? (Zone.Row, cell) : (Zone.None, -1);
        }

        _columnHit = col;
        var column = col < _above.Count && col >= 0 ? _above[col] : col == count - 1 ? _next : null;
        if (column?.Items is not { } items)
        {
            return (Zone.Column, -1);
        }

        var item = Math.Max(0, column.Top) + (int)(p.Y / RowHeight);
        return (Zone.Column, item < items.Length ? item : -1);
    }

    private void ColumnClicked(int item)
    {
        var col = _columnHit;
        var panel = _panel!;
        if (col >= 0 && col < _above.Count)
        {
            var column = _above[col];
            var name = column.Items is { } items && item >= 0 && item < items.Length ? items[item].Name
                : panel.NameOf(col + 1 < _above.Count ? _above[col + 1].Path : panel.Path);
            _ = panel.NavigateAsync(column.Path, name);
        }
        else if (col == _above.Count + 1 && _next is { Items: { } next } && item >= 0 && item < next.Length)
        {
            _ = panel.NavigateAsync(_next.Path, next[item].Name);
        }
    }

    private void StartStripDrag(double x)
    {
        var (tx, tw) = StripThumb(Strip());
        _dragOffset = x >= tx && x <= tx + tw ? x - tx : tw / 2;
        _draggingStrip = true;
        DragStrip(x);
    }

    private void DragStrip(double x)
    {
        var s = Strip();
        var (_, w) = StripThumb(s);
        var fraction = (x - _dragOffset) / Math.Max(1, Bounds.Width - w);
        _stripScroll = Math.Clamp(fraction, 0, 1) * Math.Max(0, s.Total - Bounds.Width);
        _slide = null;
        InvalidateVisual();
    }

    private void ScrollStrip(double by)
    {
        var s = Strip();
        _stripScroll = Math.Clamp(s.Offset + by, 0, Math.Max(0, s.Total - Bounds.Width));
        _slide = null;
        InvalidateVisual();
    }

    private void StartEdgeDrag(int column)
    {
        var s = Strip();
        _edgeDrag = ((column * s.W) - s.Offset, _columnChars);
    }

    private void DragColumnEdge(double x)
    {
        var left = _edgeDrag!.Value.Left;
        var chars = (int)Math.Round((x - left - BriefNameX - ChevronRoom - 12) / CharW);
        chars = Math.Clamp(chars, Core.Settings.PanelSettings.MinColumnsWidth, Core.Settings.PanelSettings.MaxColumnsWidth);
        if (chars != _columnChars)
        {
            _columnChars = chars;
            Relayout();
        }
    }

    private void EndEdgeDrag(bool changed)
    {
        var start = _edgeDrag?.Chars;
        _edgeDrag = null;
        if (changed && start != _columnChars)
        {
            ColumnCharsChanged?.Invoke(this, _columnChars);
        }
    }

    private bool ColumnsWheel(Point p, double notches, bool sideways)
    {
        if (sideways)
        {
            ScrollStrip(-notches * 48);
            return true;
        }

        if (ColumnsHit(p).Zone != Zone.Column)
        {
            return false;
        }

        var column = _columnHit < _above.Count && _columnHit >= 0 ? _above[_columnHit] : _columnHit == _above.Count + 1 ? _next : null;
        if (column is not null)
        {
            column.Top = Math.Max(0, column.Top - (int)Math.Round(notches * 3, MidpointRounding.AwayFromZero));
            InvalidateVisual();
        }

        return true;
    }

    private bool ColumnsKey(string id)
    {
        var panel = _panel!;
        switch (id)
        {
            case "Focus.Left":
                if (panel.Ancestors().Count > 0)
                {
                    _ = panel.GoParentAsync();
                }

                return true;
            case "Focus.Right":
                if (panel.FocusedEntry is { IsDirectory: true } && !panel.IsUp(panel.Focus))
                {
                    _ = panel.OpenFocusedAsync();
                }

                return true;
            case "Selection.ExtendLeft" or "Selection.ExtendRight":
                return true;
            case "Panel.ScrollLeft":
                ScrollStrip(-Strip().W);
                return true;
            case "Panel.ScrollRight":
                ScrollStrip(Strip().W);
                return true;
            default:
                return false;
        }
    }
}
