// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/TreeContentView.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;

namespace Salandual.App.Viewer;

internal sealed class TreeContentView : Control
{
    private const double Indent = 18;
    private const double LeftPad = 8;
    private const double TwistyWidth = 16;

    private readonly List<(DataNode Node, int Depth)> _rows = [];
    private readonly HashSet<DataNode> _open = [];
    private DataTree? _tree;
    private int _top;
    private int _current;
    private int _hover = -1;
    private bool _syncing;

    public TreeContentView()
    {
        Focusable = true;
        ClipToBounds = true;
        Scroll.ValueChanged += (_, e) =>
        {
            if (!_syncing)
            {
                _top = Math.Clamp((int)Math.Round(e.NewValue), 0, MaxTop);
                InvalidateVisual();
            }
        };
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        ResourcesChanged += (_, _) => InvalidateVisual();
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    public event Action? ViewChanged;

    public ScrollBar Scroll { get; } = new() { Orientation = Orientation.Vertical, Minimum = 0, AllowAutoHide = false, SmallChange = 1 };

    public HashSet<DataNode>? Matches { get; set; }

    internal IReadOnlyList<(DataNode Node, int Depth)> Rows => _rows;

    public DataTree? Tree => _tree;

    public DataNode? Selected => _current >= 0 && _current < _rows.Count ? _rows[_current].Node : null;

    internal int TopRow => _top;

    public double RowHeight => Math.Ceiling(MonoSize * 1.7);

    private int ViewportRows => Math.Max(1, (int)(Bounds.Height / RowHeight));

    private int MaxTop => Math.Max(0, _rows.Count - ViewportRows);

    public void Show(DataTree? tree, int level)
    {
        _tree = tree;
        _open.Clear();
        _top = 0;
        _current = 0;
        Matches = null;
        if (tree is not null)
        {
            _open.Add(tree.Root);
            OpenTo(tree.Root, 0, level);
        }

        Rebuild(null);
    }

    public bool IsOpen(DataNode node) => _open.Contains(node);

    public void ExpandTo(int level)
    {
        if (_tree is null)
        {
            return;
        }

        OpenTo(_tree.Root, 0, level);
        Rebuild(Selected);
    }

    public void CollapseAll()
    {
        if (_tree is null)
        {
            return;
        }

        var selected = Selected;
        _open.Clear();
        _open.Add(_tree.Root);
        while (selected?.Parent is { } parent && parent != _tree.Root)
        {
            selected = parent;
        }

        Rebuild(selected);
    }

    public void Toggle(DataNode node)
    {
        if (!node.CanOpen)
        {
            return;
        }

        if (!_open.Remove(node))
        {
            _open.Add(node);
        }

        Rebuild(Selected is { } s && IsInside(s, node) && !_open.Contains(node) ? node : Selected);
    }

    public void Unfold()
    {
        if (Selected is { CanOpen: true } node && _open.Add(node))
        {
            Rebuild(node);
        }
    }

    public void Fold()
    {
        if (Selected is { CanOpen: true } node && _open.Remove(node))
        {
            Rebuild(node);
        }
    }

    public void ExpandBranch()
    {
        if (Selected is { CanOpen: true } node)
        {
            OpenTo(node, 0, int.MaxValue);
            Rebuild(node);
        }
    }

    public void Right()
    {
        if (Selected is not { CanOpen: true } node)
        {
            return;
        }

        if (_open.Add(node))
        {
            Rebuild(node);
        }
        else
        {
            SelectRow(_current + 1);
        }
    }

    public void Left()
    {
        if (Selected is not { } node)
        {
            return;
        }

        if (node.CanOpen && _open.Remove(node))
        {
            Rebuild(node);
        }
        else if (node.Parent is { } parent)
        {
            SelectNode(parent);
        }
    }

    public void Reveal(DataNode node)
    {
        for (var at = node.Parent; at is not null; at = at.Parent)
        {
            _open.Add(at);
        }

        Rebuild(node, third: true);
    }

    public void SelectNode(DataNode node)
    {
        var row = IndexOf(node);
        if (row >= 0)
        {
            SelectRow(row);
        }
    }

    public void SelectRow(int row)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        _current = Math.Clamp(row, 0, _rows.Count - 1);
        BringIntoView(_current);
        Changed();
    }

    public void MoveBy(int rows) => SelectRow(_current + rows);

    public int PageRows => Math.Max(1, ViewportRows - 1);

    private void Rebuild(DataNode? keep, bool third = false)
    {
        _rows.Clear();
        if (_tree is not null)
        {
            var stack = new Stack<(DataNode Node, int Depth)>();
            stack.Push((_tree.Root, 0));
            while (stack.Count > 0)
            {
                var (node, depth) = stack.Pop();
                _rows.Add((node, depth));
                if (node.CanOpen && _open.Contains(node))
                {
                    var list = node.Children;
                    for (var i = list.Count - 1; i >= 0; i--)
                    {
                        stack.Push((list[i], depth + 1));
                    }
                }
            }
        }

        var row = keep is null ? -1 : IndexOf(keep);
        _current = row >= 0 ? row : Math.Clamp(_current, 0, Math.Max(0, _rows.Count - 1));
        _top = Math.Clamp(_top, 0, MaxTop);
        if (keep is not null)
        {
            BringIntoView(_current, third);
        }

        Changed();
    }

    private int IndexOf(DataNode node)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Node == node)
            {
                return i;
            }
        }

        return -1;
    }

    private void OpenTo(DataNode node, int depth, int level)
    {
        var stack = new Stack<(DataNode Node, int Depth)>();
        stack.Push((node, depth));
        while (stack.Count > 0)
        {
            var (at, d) = stack.Pop();
            if (d >= level || !at.CanOpen)
            {
                continue;
            }

            _open.Add(at);
            foreach (var child in at.Children)
            {
                if (child.CanOpen)
                {
                    stack.Push((child, d + 1));
                }
            }
        }
    }

    private static bool IsInside(DataNode node, DataNode branch)
    {
        for (var at = node.Parent; at is not null; at = at.Parent)
        {
            if (at == branch)
            {
                return true;
            }
        }

        return false;
    }

    private void BringIntoView(int row, bool third = false)
    {
        var rows = ViewportRows;
        if (row >= _top && row < _top + rows)
        {
            return;
        }

        _top = third ? row - (rows / 3) : row < _top ? row : row - rows + 1;
        _top = Math.Clamp(_top, 0, MaxTop);
    }

    public void ScrollRows(int rows)
    {
        _top = Math.Clamp(_top + rows, 0, MaxTop);
        Changed();
    }

    private void Changed()
    {
        _syncing = true;
        try
        {
            Scroll.Maximum = MaxTop;
            Scroll.ViewportSize = ViewportRows;
            Scroll.LargeChange = Math.Max(1, ViewportRows - 1);
            Scroll.Value = _top;
        }
        finally
        {
            _syncing = false;
        }

        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _top = Math.Clamp(_top, 0, MaxTop);
        Changed();
    }

    private int RowAt(Point p)
    {
        var row = _top + (int)(p.Y / RowHeight);
        return p.Y >= 0 && row < _rows.Count ? row : -1;
    }

    private bool OnTwisty(int row, Point p)
    {
        var x = LeftPad + (_rows[row].Depth * Indent);
        return p.X >= x - 2 && p.X < x + TwistyWidth + 2;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetPosition(this);
        var row = RowAt(p);
        if (row < 0)
        {
            return;
        }

        var node = _rows[row].Node;
        var left = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        if (left && (OnTwisty(row, p) || e.ClickCount == 2) && node.CanOpen)
        {
            _current = row;
            Toggle(node);
        }
        else
        {
            SelectRow(row);
        }

        e.Handled = left;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var row = RowAt(e.GetPosition(this));
        if (row != _hover)
        {
            _hover = row;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ScrollRows((int)Math.Round(-e.Delta.Y * 3));
        e.Handled = true;
    }

    private Typeface MonoTypeface => new(this.TryFindResource("Font.Mono", ActualThemeVariant, out var f) && f is FontFamily family ? family : FontFamily.Default);

    private double MonoSize => this.TryFindResource("Font.MonoSize", ActualThemeVariant, out var s) && s is double size ? size : 13;

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var b) && b is IBrush brush ? brush : Brushes.Gray;

    public bool Colors { get; set; } = true;

    private IBrush Token(string token) => Brush(Colors ? "Brush." + token : "Brush.CodeInk");

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush("Brush.CodeSurface"), bounds);
        if (_tree is null)
        {
            return;
        }

        var row = RowHeight;
        var mono = MonoTypeface;
        var size = MonoSize;
        var ui = new Typeface(FontFamily.Default);
        var punctuation = Token(ColorTokens.SyntaxPunctuation);
        var meta = Brush("Brush.TertiaryText");
        var match = Brush("Brush.ViewerMatch");
        for (var i = _top; i < _rows.Count && (i - _top) * row < bounds.Height; i++)
        {
            var (node, depth) = _rows[i];
            var y = (i - _top) * row;
            var line = new Rect(0, y, bounds.Width, row);
            if (i == _current)
            {
                context.FillRectangle(Brush(IsFocused ? "Brush.CodeSelection" : "Brush.CodeCurrentLine"), line);
            }
            else if (i == _hover)
            {
                context.FillRectangle(Brush("Brush.Hover"), line);
            }

            var x = LeftPad + (depth * Indent);
            if (node.CanOpen)
            {
                var twisty = Text(_open.Contains(node) ? "▾" : "▸", ui, 10, meta);
                context.DrawText(twisty, new Point(x + ((TwistyWidth - twisty.Width) / 2), y + ((row - twisty.Height) / 2)));
            }

            x += TwistyWidth;
            var (key, keyBrush) = KeyOf(node);
            var keyText = Text(key, mono, size, keyBrush);
            if (Matches?.Contains(node) == true)
            {
                context.FillRectangle(match, new Rect(x - 2, y + 2, keyText.Width + 4, row - 4));
            }

            context.DrawText(keyText, new Point(x, y + ((row - keyText.Height) / 2)));
            x += keyText.Width;

            var (value, valueBrush) = ValueOf(node);
            if (value.Length > 0)
            {
                var colon = Text(": ", mono, size, punctuation);
                context.DrawText(colon, new Point(x, y + ((row - colon.Height) / 2)));
                x += colon.WidthIncludingTrailingWhitespace;
                var valueText = Text(value, mono, size, valueBrush ?? punctuation);
                context.DrawText(valueText, new Point(x, y + ((row - valueText.Height) / 2)));
                x += valueText.Width;
            }

            if (node.IsBranch)
            {
                var count = node.Count;
                var label = node.Kind == DataKind.Object
                    ? Loc.Plural(count, AppStrings.Viewer_Keys_One, AppStrings.Viewer_Keys_Other)
                    : Loc.Plural(count, AppStrings.Viewer_Items_One, AppStrings.Viewer_Items_Other);
                var metaText = Text(label, ui, 12, meta);
                context.DrawText(metaText, new Point(x + 10, y + ((row - metaText.Height) / 2)));
            }
        }
    }

    private (string Text, IBrush Brush) KeyOf(DataNode node)
    {
        if (node.Parent is null && node.Kind != DataKind.Element)
        {
            return ("$", Token(ColorTokens.SyntaxPunctuation));
        }

        return node.Kind switch
        {
            DataKind.Element => (node.Name ?? "", Token(ColorTokens.SyntaxTag)),
            DataKind.Attribute => ("@" + node.Name, Token(ColorTokens.SyntaxAttribute)),
            DataKind.Text => ("#text", Token(ColorTokens.SyntaxPunctuation)),
            _ when node.Name is null => ("[" + node.Index.ToString(CultureInfo.InvariantCulture) + "]", Token(ColorTokens.SyntaxPunctuation)),
            _ => (node.Name, Token(ColorTokens.SyntaxKey)),
        };
    }

    private (string Text, IBrush? Brush) ValueOf(DataNode node)
    {
        if (node.IsBranch)
        {
            return node.Kind == DataKind.Element || _open.Contains(node) && node.CanOpen ? ("", null)
                : node.Kind == DataKind.Object ? ("{ … }", null) : ("[ … ]", null);
        }

        var shown = _tree!.Shown(node);
        return node.Kind switch
        {
            DataKind.Number => (shown, Token(ColorTokens.SyntaxNumber)),
            DataKind.Boolean or DataKind.Null or DataKind.Alias => (shown.Length == 0 ? "null" : shown, Token(ColorTokens.SyntaxKeyword)),
            _ => (shown, Token(ColorTokens.SyntaxString)),
        };
    }

    private static FormattedText Text(string text, Typeface typeface, double size, IBrush brush)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);
}
