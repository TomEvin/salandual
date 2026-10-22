// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ContentView.cs

using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Salandual.Core.Viewer;

namespace Salandual.App.Viewer;

internal abstract class ContentView : Control
{
    private ViewerDocument? _document;

    protected ContentView()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        ActualThemeVariantChanged += (_, _) => OnLookChanged();
        ResourcesChanged += (_, _) => OnLookChanged();
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    public event Action? ViewChanged;

    public ViewerDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            Anchor = Caret = value?.Lines.Start ?? 0;
            OnDocumentChanged();
            Changed();
        }
    }

    public long Anchor { get; protected set; }

    public long Caret { get; protected set; }

    public long SelectionStart => Math.Min(Anchor, Caret);

    public long SelectionEnd => Math.Max(Anchor, Caret);

    public bool HasSelection => Anchor != Caret;

    public Regex? Highlight { get; set; }

    public byte[]? HighlightBytes { get; set; }

    public abstract double ScrollFraction { get; set; }

    public abstract double ViewportFraction { get; }

    public abstract long TopOffset { get; }

    public abstract void ScrollRows(int rows);

    public abstract void ScrollToTop();

    public abstract void ScrollToEnd();

    public abstract void Reveal(long offset);

    public abstract long? OffsetAtPoint(Point p);

    public void Select(long start, long end)
    {
        SelectionIsMatch = false;
        OnSelecting(end);
        Anchor = start;
        Caret = end;
        Changed();
    }

    public void SelectAll()
    {
        if (Document is { } d)
        {
            Select(d.Lines.Start, d.Length);
        }
    }

    public abstract void ShowAt(long offset);

    protected virtual void OnSelecting(long caret)
    {
    }

    protected virtual void OnDocumentChanged()
    {
    }

    protected virtual void OnLookChanged() => Changed();

    protected void Changed()
    {
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    protected Typeface MonoTypeface => new(this.TryFindResource("Font.Mono", ActualThemeVariant, out var f) && f is FontFamily family ? family : FontFamily.Default);

    protected double MonoSize => this.TryFindResource("Font.MonoSize", ActualThemeVariant, out var s) && s is double size ? size : 13;

    protected IBrush Brush(string key)
        => this.TryFindResource(key, ActualThemeVariant, out var b) && b is IBrush brush ? brush : Brushes.Gray;

    protected IBrush SelectionBrush => Brush("Brush.TextSelection");

    protected IBrush CaretBrush => new SolidColorBrush(Brush("Brush.ViewerCaret") is ISolidColorBrush caret ? caret.Color : Colors.DodgerBlue, IsFocused ? 1 : 0.45);

    protected IBrush MatchBrush => Brush("Brush.ViewerMatch");

    protected IBrush CurrentMatchBrush => Brush("Brush.ViewerCurrentMatch");

    public bool SelectionIsMatch { get; set; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        SelectionIsMatch = false;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ScrollRows((int)Math.Round(-e.Delta.Y * 3));
        e.Handled = true;
    }
}
