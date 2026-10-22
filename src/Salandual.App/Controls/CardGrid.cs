// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/CardGrid.cs

using Avalonia;
using Avalonia.Controls;

namespace Salandual.App.Controls;

public sealed class CardGrid : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<CardGrid, double>(nameof(Spacing), 12);

    public static readonly StyledProperty<double> MinColumnWidthProperty = AvaloniaProperty.Register<CardGrid, double>(nameof(MinColumnWidth), 240);

    public static readonly StyledProperty<double> WideColumnWidthProperty = AvaloniaProperty.Register<CardGrid, double>(nameof(WideColumnWidth), 360);

    public static readonly AttachedProperty<int> LaneProperty = AvaloniaProperty.RegisterAttached<CardGrid, Control, int>("Lane");

    static CardGrid() => AffectsMeasure<CardGrid>(SpacingProperty, MinColumnWidthProperty, WideColumnWidthProperty);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double WideColumnWidth
    {
        get => GetValue(WideColumnWidthProperty);
        set => SetValue(WideColumnWidthProperty, value);
    }

    public static int GetLane(Control card) => card.GetValue(LaneProperty);

    public static void SetLane(Control card, int value) => card.SetValue(LaneProperty, Math.Clamp(value, 0, 2));

    internal int Columns { get; private set; } = 2;

    private bool Fits(double width, int columns, double each) => !double.IsFinite(width) || width >= columns * each + (columns - 1) * Spacing;

    private (int Columns, Func<Control, int>? ColumnOf) Plan(double width, List<Control> visible)
    {
        var lanes = visible.Select(GetLane).ToHashSet();
        if (lanes.Count == 3 && Fits(width, 3, WideColumnWidth))
        {
            return (3, GetLane);
        }

        if (!Fits(width, 2, MinColumnWidth))
        {
            return (1, null);
        }

        return lanes.Contains(1) && lanes.Count > 1 ? (2, c => GetLane(c) == 1 ? 1 : 0) : (2, null);
    }

    private double ColumnWidth(double width, int columns)
        => double.IsFinite(width) ? Math.Max(0, (width - (columns - 1) * Spacing) / columns) : MinColumnWidth;

    private List<(Control Card, int Column, double Top, double Height)> Place(double width, bool measure)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        var (columns, columnOf) = Plan(width, visible);
        Columns = columns;
        var column = ColumnWidth(width, columns);
        if (measure)
        {
            foreach (var card in visible)
            {
                card.Measure(new Size(column, double.PositiveInfinity));
            }
        }

        var places = new List<(Control, int, double, double)>();
        if (columnOf is not null)
        {
            var tops = new double[columns];
            foreach (var card in visible)
            {
                var c = columnOf(card);
                places.Add((card, c, tops[c], card.DesiredSize.Height));
                tops[c] += card.DesiredSize.Height + Spacing;
            }

            return places;
        }

        var y = 0.0;
        for (var i = 0; i < visible.Count; i += columns)
        {
            var row = visible.Skip(i).Take(columns).ToList();
            var height = row.Max(c => c.DesiredSize.Height);
            for (var j = 0; j < row.Count; j++)
            {
                places.Add((row[j], j, y, height));
            }

            y += height + Spacing;
        }

        return places;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var places = Place(availableSize.Width, measure: true);
        var height = places.Count == 0 ? 0 : places.Max(p => p.Top + p.Height);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : Columns * MinColumnWidth + (Columns - 1) * Spacing;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var places = Place(finalSize.Width, measure: false);
        var column = ColumnWidth(finalSize.Width, Columns);
        foreach (var (card, c, top, height) in places)
        {
            card.Arrange(new Rect(c * (column + Spacing), top, column, height));
        }

        return finalSize;
    }
}
