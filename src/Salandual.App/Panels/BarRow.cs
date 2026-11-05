// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/BarRow.cs

using Avalonia;
using Avalonia.Controls;

namespace Salandual.App.Panels;

internal sealed class BarRow : Panel
{
    public const double MaxShare = 0.5;

    private const double MinFill = 80;

    public Control? Fill { get; set; }

    private Control? Filler => Fill is DriveStrip { Length: not null } ? null : Fill;

    private static bool HasLength(Control c) => c is DriveStrip { Length: not null };

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        var fill = Filler;
        double used = 0, height = 0;
        foreach (var child in Children.Where(c => c != fill))
        {
            var room = Math.Max(0, width - used - (fill is null ? 0 : MinFill));
            var max = HasLength(child) || (fill is null && Children.Count == 1) ? room : Math.Min(room, width * MaxShare);
            child.Measure(new Size(double.IsFinite(availableSize.Width) ? max : availableSize.Width, availableSize.Height));
            used += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        if (fill is not null)
        {
            fill.Measure(new Size(Math.Max(0, width - used), availableSize.Height));
            height = Math.Max(height, fill.DesiredSize.Height);
        }

        return new Size(Math.Max(width, used), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var fill = Filler;
        var others = Children.Where(c => c != fill).Sum(c => c.DesiredSize.Width);
        var x = 0.0;
        foreach (var child in Children)
        {
            var w = child == fill ? Math.Max(0, finalSize.Width - others)
                : Children.Count == 1 && !HasLength(child) ? finalSize.Width
                : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w;
        }

        return finalSize;
    }
}
