// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/FitRow.cs

using Avalonia;
using Avalonia.Controls;

namespace Salandual.App.Controls;

public sealed class FitRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<FitRow, double>(nameof(Spacing));

    static FitRow()
    {
        AffectsMeasure<FitRow>(SpacingProperty);
        ClipToBoundsProperty.OverrideDefaultValue<FitRow>(true);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public bool KeepTogether { get; init; }

    internal int ShownCount { get; private set; }

    private List<Control> _hidden = [];

    public IReadOnlyList<Control> Hidden => _hidden;

    public event EventHandler? HiddenChanged;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        var any = false;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (!child.IsVisible)
            {
                continue;
            }

            width += (any ? Spacing : 0) + child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
            any = true;
        }

        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var full = false;
        var shown = new List<(Control Child, Rect Rect)>();
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            var start = shown.Count > 0 ? x + Spacing : x;
            var room = finalSize.Width - start;
            var w = child.DesiredSize.Width;
            if (!full && w <= room + 0.5)
            {
                shown.Add((child, new Rect(start, 0, w, finalSize.Height)));
                x = start + w;
            }
            else if (!full && child is FitRow { KeepTogether: false } nested && nested.FirstWidth is > 0 and var first && first <= room + 0.5)
            {
                shown.Add((child, new Rect(start, 0, room, finalSize.Height)));
                x = finalSize.Width;
                full = true;
            }
            else
            {
                full = true;
                child.Arrange(new Rect(finalSize.Width + 10000, 0, w, finalSize.Height));
            }
        }

        while (full && shown.Count > 0 && IsSeparator(shown[^1].Child))
        {
            var last = shown[^1].Child;
            last.Arrange(new Rect(finalSize.Width + 10000, 0, last.DesiredSize.Width, finalSize.Height));
            shown.RemoveAt(shown.Count - 1);
        }

        foreach (var (child, rect) in shown)
        {
            child.Arrange(rect);
        }

        ShownCount = shown.Count;
        var hidden = new List<Control>();
        foreach (var child in Children.Where(c => c.IsVisible))
        {
            if (shown.Any(s => ReferenceEquals(s.Child, child)))
            {
                hidden.AddRange(child is FitRow row ? row.Hidden : []);
            }
            else
            {
                hidden.AddRange(Leaves(child));
            }
        }

        if (!hidden.SequenceEqual(_hidden))
        {
            _hidden = hidden;
            HiddenChanged?.Invoke(this, EventArgs.Empty);
        }

        return finalSize;
    }

    private double FirstWidth => Children.FirstOrDefault(c => c.IsVisible && !IsSeparator(c)) is { } c ? (c is FitRow row ? row.FirstWidth : c.DesiredSize.Width) : 0;

    private static IEnumerable<Control> Leaves(Control c)
        => c is FitRow row ? row.Children.Where(x => x.IsVisible).SelectMany(Leaves) : IsSeparator(c) ? [] : [c];

    private static bool IsSeparator(Control c) => c is Border { Child: null, Width: <= 2 };
}
