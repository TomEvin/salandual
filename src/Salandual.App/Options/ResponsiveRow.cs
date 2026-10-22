// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/ResponsiveRow.cs

using Avalonia;
using Avalonia.Controls;

namespace Salandual.App.Options;

internal sealed class ResponsiveRow : Panel
{
    private const double Gap = 12;

    public double MinTextWidth { get; init; } = 200;

    public bool Stacked { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children is not [var text, var control])
        {
            return default;
        }

        control.Measure(Size.Infinity);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : control.DesiredSize.Width + Gap + MinTextWidth;
        Stacked = width - control.DesiredSize.Width - Gap < MinTextWidth;
        if (Stacked)
        {
            text.Measure(new Size(width, double.PositiveInfinity));
            control.Measure(new Size(width, double.PositiveInfinity));
            return new Size(width, text.DesiredSize.Height + 6 + control.DesiredSize.Height);
        }

        text.Measure(new Size(width - control.DesiredSize.Width - Gap, double.PositiveInfinity));
        return new Size(width, Math.Max(text.DesiredSize.Height, control.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children is not [var text, var control])
        {
            return finalSize;
        }

        if (Stacked)
        {
            text.Arrange(new Rect(0, 0, finalSize.Width, text.DesiredSize.Height));
            control.Arrange(new Rect(0, text.DesiredSize.Height + 6, Math.Min(finalSize.Width, control.DesiredSize.Width), control.DesiredSize.Height));
            return finalSize;
        }

        var controlWidth = control.DesiredSize.Width;
        var textWidth = finalSize.Width - controlWidth - Gap;
        text.Arrange(new Rect(0, (finalSize.Height - text.DesiredSize.Height) / 2, textWidth, text.DesiredSize.Height));
        control.Arrange(new Rect(finalSize.Width - controlWidth, (finalSize.Height - control.DesiredSize.Height) / 2, controlWidth, control.DesiredSize.Height));
        return finalSize;
    }
}
