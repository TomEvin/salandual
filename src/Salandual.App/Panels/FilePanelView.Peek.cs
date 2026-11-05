// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/FilePanelView.Peek.cs

using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Vfs;

namespace Salandual.App.Panels;

public sealed partial class FilePanelView
{
    private const int PeekMax = 200;

    private PeekState? _peek;
    private DispatcherTimer? _peekTimer;
    private int _peekHoverRow = -1;

    public bool PeekOnHover { get; set; }

    private sealed class PeekState(string path, string name, int row, bool hover)
    {
        public string Folder { get; init; } = "";

        public string Path { get; } = path;

        public string Name { get; } = name;

        public int Row { get; } = row;

        public bool Hover { get; } = hover;

        public FileEntry[]? Items { get; set; }

        public bool IsFile { get; init; }

        public string[]? Lines { get; set; }

        public string? Error { get; set; }

        public int Focus { get; set; }

        public int Top { get; set; }

        public string Typed { get; set; } = "";

        public CancellationTokenSource Cts { get; } = new();

        public Rect Box { get; set; }

        public Rect List { get; set; }

        public int Rows { get; set; } = 1;
    }

    internal (string Path, IReadOnlyList<string>? Items, int Focus)? PeekShown
        => _peek is { } p ? (p.Path, p.Items?.Select(e => e.Name).ToList(), p.Focus) : null;

    internal Rect PeekBox => _peek?.Box ?? default;

    internal IReadOnlyList<string>? PeekLines => _peek?.Lines;

    internal void ElapsePeekHover()
    {
        if (_peekTimer is { IsEnabled: true } timer)
        {
            timer.Stop();
            OpenPeek(_peekHoverRow, hover: true);
        }
    }

    private void OpenPeek(int row, bool hover)
    {
        ClosePeek();
        if (_panel is not { } panel || row < 0 || row >= Count || panel.IsUp(row) || panel.Path.Length == 0)
        {
            return;
        }

        var entry = panel.Items[row];
        var path = panel.ChildPath(panel.Path, entry.Name);
        if (!entry.IsDirectory)
        {
            if (_packedSize is null && _place is null && TorrentFacts.MayRead(path, entry))
            {
                var file = _peek = new PeekState(path, Shown(entry), row, hover) { Folder = panel.Path, IsFile = true };
                void Show()
                {
                    if (!ReferenceEquals(_peek, file))
                    {
                        return;
                    }

                    if (TorrentFacts.Get(path, entry, Show) is { } summary)
                    {
                        file.Lines = TorrentFacts.Lines(summary, _format);
                    }
                    else if (TorrentFacts.IsRead(path, entry))
                    {
                        file.Error = AppStrings.Peek_NotTorrent;
                    }

                    InvalidateVisual();
                }

                Show();
                InvalidateVisual();
            }

            return;
        }

        var peek = _peek = new PeekState(path, Shown(entry), row, hover) { Folder = panel.Path };
        _ = Read();
        InvalidateVisual();

        async Task Read()
        {
            try
            {
                var items = await panel.ListColumnAsync(path, peek.Cts.Token);
                if (peek.Cts.IsCancellationRequested)
                {
                    return;
                }

                peek.Items = items;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                peek.Items = [];
                peek.Error = ex.Message;
            }

            if (ReferenceEquals(_peek, peek))
            {
                InvalidateVisual();
            }
        }
    }

    private void ClosePeek()
    {
        if (_peek is { } peek)
        {
            peek.Cts.Cancel();
            _peek = null;
            InvalidateVisual();
        }
    }

    private bool? PeekKey(KeyEventArgs e)
    {
        var peek = _peek!;
        var none = e.KeyModifiers == KeyModifiers.None;
        if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return null;
        }

        var count = Math.Min(PeekMax, peek.Items?.Length ?? 0);
        switch (e.Key)
        {
            case Key.Escape when none:
            case Key.Space when e.KeyModifiers == KeyModifiers.Shift:
                ClosePeek();
                return true;
            case Key.Up when none: MovePeek(peek.Focus - 1, count); return true;
            case Key.Down when none: MovePeek(peek.Focus + 1, count); return true;
            case Key.PageUp when none: MovePeek(peek.Focus - Math.Max(1, peek.Rows - 1), count); return true;
            case Key.PageDown when none: MovePeek(peek.Focus + Math.Max(1, peek.Rows - 1), count); return true;
            case Key.Home when none: MovePeek(0, count); return true;
            case Key.End when none: MovePeek(count - 1, count); return true;
            case Key.Enter when none:
                PeekGo();
                return true;
            case Key.Back when none && peek.Typed.Length > 0:
                peek.Typed = peek.Typed[..^1];
                return true;
        }

        if (IsTypingKey(e))
        {
            return null;
        }

        ClosePeek();
        return false;
    }

    private void MovePeek(int to, int count)
    {
        var peek = _peek!;
        if (count == 0)
        {
            return;
        }

        peek.Focus = Math.Clamp(to, 0, count - 1);
        peek.Typed = "";
        if (peek.Focus < peek.Top)
        {
            peek.Top = peek.Focus;
        }
        else if (peek.Focus >= peek.Top + peek.Rows)
        {
            peek.Top = peek.Focus - peek.Rows + 1;
        }

        InvalidateVisual();
    }

    private void PeekType(string text)
    {
        var peek = _peek!;
        if (peek.Items is not { } items)
        {
            return;
        }

        var typed = peek.Typed + text;
        var count = Math.Min(PeekMax, items.Length);
        for (var i = 0; i < count; i++)
        {
            if (items[i].Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            {
                MovePeek(i, count);
                peek.Typed = typed;
                return;
            }
        }
    }

    private void PeekGo()
    {
        var peek = _peek!;
        var panel = _panel!;
        ClosePeek();
        if (peek.IsFile)
        {
            return;
        }

        if (peek.Items is { Length: > 0 } items && peek.Focus < Math.Min(PeekMax, items.Length))
        {
            var e = items[peek.Focus];
            _ = e.IsDirectory ? panel.NavigateAsync(panel.ChildPath(peek.Path, e.Name)) : panel.NavigateAsync(peek.Path, e.Name);
        }
        else
        {
            _ = panel.NavigateAsync(peek.Path);
        }
    }

    private void PeekClick(Point p, int clicks)
    {
        var peek = _peek!;
        var count = Math.Min(PeekMax, peek.Items?.Length ?? 0);
        if (!peek.List.Contains(p) || count == 0)
        {
            return;
        }

        var i = peek.Top + (int)((p.Y - peek.List.Y) / RowHeight);
        if (i < count)
        {
            MovePeek(i, count);
            if (clicks == 2)
            {
                PeekGo();
            }
        }
    }

    private void PeekPointer(Point p, int row)
    {
        if (_peek is { Hover: true } peek && !peek.Box.Contains(p) && row != peek.Row)
        {
            ClosePeek();
        }

        if (!PeekOnHover || _peek is not null)
        {
            _peekTimer?.Stop();
            return;
        }

        var folder = row >= 0 && row < Count && !_panel!.IsUp(row) && _panel.Items[row].IsDirectory;
        if (!folder)
        {
            _peekTimer?.Stop();
            _peekHoverRow = -1;
            return;
        }

        if (row != _peekHoverRow || _peekTimer is not { IsEnabled: true })
        {
            _peekHoverRow = row;
            _peekTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _peekTimer.Tick -= OnPeekTimer;
            _peekTimer.Tick += OnPeekTimer;
            _peekTimer.Stop();
            _peekTimer.Start();
        }
    }

    private void OnPeekTimer(object? sender, EventArgs e) => ElapsePeekHover();

    private void PeekPointerLeft()
    {
        _peekTimer?.Stop();
        _peekHoverRow = -1;
        if (_peek is { Hover: true })
        {
            ClosePeek();
        }
    }

    private Rect PeekAnchor(int row)
    {
        if (IsDetailed)
        {
            var y = HeaderHeight + (RowHeight * (row - _top));
            var name = GetRowText(row, ColumnsNow()).Name;
            return new Rect(NameX, y, name.Width, RowHeight);
        }

        var l = Layout();
        if (!IsInView(row, l))
        {
            return new Rect(l.X0, l.Y0, 0, RowHeight);
        }

        var cell = CellRect(row, l);
        return ViewKind is PanelView.Brief or PanelView.Columns ? new Rect(cell.X + BriefNameX, cell.Y, GetCellText(row, cell.Width).Name.Width, cell.Height) : cell;
    }

    private void DrawPeek(DrawingContext context, Size size)
    {
        if (_peek is not { } peek || _panel is null)
        {
            return;
        }

        var small = Math.Max(9, _fontSize - 1);
        var title = Text(peek.Name, _bold, _b.Text, _fontSize);
        var items = peek.Items;
        var lines = peek.IsFile ? peek.Lines ?? [] : null;
        var shown = lines is not null ? Math.Max(1, lines.Length - 1) : Math.Min(PeekMax, items?.Length ?? 0);
        var summary = lines is not null ? (lines.Length > 0 ? lines[0] : peek.Error is null ? AppStrings.Torrent_Reading : "") : items is null ? "" : Summary(items);
        var sub = Text(summary, _face, _b.SecondaryText, small);
        var keys = Text(peek.IsFile ? AppStrings.Peek_KeysTorrent : AppStrings.Peek_Keys, _face, _b.TertiaryText, small);
        var more = items is { Length: > PeekMax } ? Text(Loc.Format(AppStrings.Peek_More, items.Length - PeekMax), _face, _b.TertiaryText, small) : null;

        var width = Math.Min(Math.Max(180, 280 * Scale), size.Width - 16);
        var head = title.Height + sub.Height + 14;
        var foot = keys.Height + 12 + (more?.Height ?? 0);
        var rows = Math.Clamp((int)((size.Height - 16 - head - foot) / RowHeight), 1, 16);
        var listRows = Math.Max(1, Math.Min(rows, shown));
        peek.Rows = listRows;
        var height = head + (listRows * RowHeight) + foot;

        var anchor = PeekAnchor(peek.Row);
        var x = anchor.Right + 16 + width <= size.Width - 8 ? anchor.Right + 16 : anchor.X + 24;
        x = Math.Clamp(x, 8, Math.Max(8, size.Width - width - 8));
        var y = anchor.Bottom + 2 + height <= size.Height - 4 ? anchor.Bottom + 2 : anchor.Y - height - 2;
        y = Math.Clamp(y, 4, Math.Max(4, size.Height - height - 4));
        var box = peek.Box = new Rect(x, y, width, height);

        context.FillRectangle(new ImmutableSolidColorBrush(Color.FromArgb(_b.Dark ? (byte)90 : (byte)36, 0, 0, 0)), box.Translate(new Vector(0, 3)).Inflate(2), 6);
        context.DrawRectangle(_b.Background, new ImmutablePen(_b.Border.ToImmutable(), 1), box, 4, 4);
        using var clip = context.PushClip(box);
        title.MaxTextWidth = sub.MaxTextWidth = Math.Max(10, width - 20);
        title.MaxLineCount = sub.MaxLineCount = 1;
        title.Trimming = sub.Trimming = TextTrimming.CharacterEllipsis;
        context.DrawText(title, new Point(x + 10, y + 6));
        context.DrawText(sub, new Point(x + 10, y + 6 + title.Height + 1));
        context.FillRectangle(_b.Border, new Rect(x + 1, y + head - 4, width - 2, 1));

        var list = peek.List = new Rect(x, y + head - 2, width, listRows * RowHeight);
        if (lines is not null)
        {
            if (peek.Error is { } why)
            {
                DrawColumnNote(context, why, list);
            }

            for (var i = 1; i < lines.Length && i - 1 < listRows; i++)
            {
                var line = Text(lines[i], _face, _b.Text, small);
                line.MaxTextWidth = Math.Max(10, width - 20);
                line.MaxLineCount = 1;
                line.Trimming = TextTrimming.CharacterEllipsis;
                context.DrawText(line, new Point(x + 10, list.Y + ((i - 1) * RowHeight) + ((RowHeight - line.Height) / 2)));
            }
        }
        else if (peek.Error is { } error)
        {
            DrawColumnNote(context, Loc.Format(AppStrings.Columns_CantRead, error), list);
        }
        else if (items is { Length: 0 })
        {
            DrawColumnNote(context, AppStrings.Peek_Empty, list);
        }
        else if (items is not null)
        {
            var scaling = Avalonia.Controls.TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            peek.Top = Math.Clamp(peek.Top, 0, Math.Max(0, shown - listRows));
            for (var i = peek.Top; i < Math.Min(shown, peek.Top + listRows); i++)
            {
                var e = items[i];
                var row = new Rect(list.X + 4, list.Y + ((i - peek.Top) * RowHeight), list.Width - 8, RowHeight);
                if (i == peek.Focus)
                {
                    context.FillRectangle(_b.FocusBackground, row, 4);
                    context.DrawRectangle(null, _b.FocusPen, row.Deflate(0.75), 4, 4);
                }

                DrawEntryIcon(context, peek.Path, e, new Rect(row.X + 4, row.Y + ((row.Height - IconSize) / 2), IconSize, IconSize), scaling);
                var (brush, face) = NameLook(e, false, false, false, git: false);
                var name = Text(e.Name, face, brush, _fontSize);
                name.MaxTextWidth = Math.Max(10, row.Width - 24 - ChevronRoom);
                name.MaxLineCount = 1;
                name.Trimming = TextTrimming.CharacterEllipsis;
                context.DrawText(name, new Point(row.X + 24, row.Y + ((row.Height - name.Height) / 2)));
                if (e.IsDirectory)
                {
                    DrawChevron(context, row);
                }
            }
        }

        var fy = list.Bottom + 4;
        if (more is not null)
        {
            context.DrawText(more, new Point(x + 10, fy));
            fy += more.Height;
        }

        context.DrawText(keys, new Point(x + 10, fy + 2));
    }

    private string Summary(FileEntry[] items)
    {
        var folders = items.Count(e => e.IsDirectory);
        var files = items.Length - folders;
        var bytes = items.Where(e => !e.IsDirectory).Sum(e => e.Size);
        var parts = new List<string>();
        if (folders > 0)
        {
            parts.Add(Loc.Plural(folders, AppStrings.Peek_Folders_One, AppStrings.Peek_Folders_Other));
        }

        if (files > 0 || folders == 0)
        {
            parts.Add(Loc.Plural(files, AppStrings.Peek_Files_One, AppStrings.Peek_Files_Other));
            parts.Add(_format.ShortSize(bytes));
        }

        return string.Join(", ", parts);
    }
}
