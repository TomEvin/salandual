// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/StickyPanel.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Salandual.App.Options;

internal sealed class StickyPanel : Panel
{
    private const double Gap = 12;
    private ScrollViewer? _scroll;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = this.FindAncestorOfType<ScrollViewer>();
        if (_scroll is not null)
        {
            _scroll.ScrollChanged += OnScrollChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_scroll is not null)
        {
            _scroll.ScrollChanged -= OnScrollChanged;
            _scroll = null;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => InvalidateArrange();

    internal double ChildOffset { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = default(Size);
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            size = new Size(Math.Max(size.Width, child.DesiredSize.Width), Math.Max(size.Height, child.DesiredSize.Height));
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var offset = 0.0;
        if (_scroll is not null && this.TranslatePoint(default, _scroll) is { } top && top.Y < Gap)
        {
            offset = Gap - top.Y;
        }

        foreach (var child in Children)
        {
            var height = child.DesiredSize.Height;
            var y = Math.Clamp(offset, 0, Math.Max(0, finalSize.Height - height));
            ChildOffset = y;
            child.Arrange(new Rect(0, y, finalSize.Width, height));
        }

        return finalSize;
    }
}
