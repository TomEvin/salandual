// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/ThemedControl.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Salandual.App.Controls;

public abstract class ThemedControl : Control
{
    private bool _themeValid;

    protected ThemedControl()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateTheme();
        BackgroundLayer.Follow(this);
    }

    protected void DrawBackdrop(DrawingContext context, Rect rect) => BackgroundLayer.DrawOnBar(this, context, rect);

    protected Typeface Face { get; private set; }

    protected Typeface Bold { get; private set; }

    protected double FontSize { get; private set; } = Salandual.Core.Settings.FontSetting.DefaultPanelSize;

    public void InvalidateTheme()
    {
        _themeValid = false;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Application.Current is App { Theme: { } theme })
        {
            theme.Applied += OnThemeApplied;
        }

        InvalidateTheme();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current is App { Theme: { } theme })
        {
            theme.Applied -= OnThemeApplied;
        }
    }

    private void OnThemeApplied(object? sender, EventArgs e) => InvalidateTheme();

    protected void EnsureTheme()
    {
        if (_themeValid)
        {
            return;
        }

        var family = Resource("Font.Panel") as FontFamily ?? new FontFamily("Segoe UI");
        FontSize = Resource("Font.PanelSize") is { } s ? Convert.ToDouble(s, CultureInfo.InvariantCulture) : Salandual.Core.Settings.FontSetting.DefaultPanelSize;

        Face = new Typeface(family);
        Bold = new Typeface(family, FontStyle.Normal, FontWeight.SemiBold);
        LoadTheme();
        _themeValid = true;
    }

    protected virtual void LoadTheme()
    {
    }

    protected object? Resource(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) ? v : null;

    protected IBrush Brush(string token) => Resource("Brush." + token) as IBrush ?? Brushes.Magenta;

    protected FormattedText Text(string s, IBrush brush, bool bold = false, double? size = null)
        => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? Bold : Face, size ?? FontSize, brush);
}
