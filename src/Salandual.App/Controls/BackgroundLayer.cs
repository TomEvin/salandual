// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/BackgroundLayer.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Salandual.App.Theme;
using Salandual.Core.Settings;

namespace Salandual.App.Controls;

public sealed class BackgroundLayer : Control
{
    private BackgroundImageService? _service;

    public BackgroundLayer()
    {
        IsHitTestVisible = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _service = (Application.Current as App)?.BackgroundImage;
        if (_service is not null)
        {
            _service.Changed += OnChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_service is not null)
        {
            _service.Changed -= OnChanged;
        }
    }

    private void OnChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        if (_service is { Settings.Placement: BackgroundPlacement.Window } service)
        {
            service.Draw(context, WindowArea(this), new Rect(Bounds.Size));
        }
    }

    internal static void DrawOnBar(Visual bar, DrawingContext context, Rect rect)
    {
        if (Service is { Image: not null, Settings.Placement: BackgroundPlacement.Window } service)
        {
            service.Draw(context, WindowArea(bar), rect);
        }
    }

    internal static void Follow(Visual bar)
    {
        void Invalidate(object? sender, EventArgs e) => bar.InvalidateVisual();
        bar.AttachedToVisualTree += (_, _) =>
        {
            if (Service is { } service)
            {
                service.Changed += Invalidate;
            }
        };
        bar.DetachedFromVisualTree += (_, _) =>
        {
            if (Service is { } service)
            {
                service.Changed -= Invalidate;
            }
        };
    }

    private static BackgroundImageService? Service => (Application.Current as App)?.BackgroundImage;

    internal static Rect WindowArea(Visual control)
    {
        var root = TopLevel.GetTopLevel(control);
        if (root is null)
        {
            return new Rect(control.Bounds.Size);
        }

        var origin = root.TranslatePoint(default, control) ?? default;
        return new Rect(origin, root.Bounds.Size);
    }
}
