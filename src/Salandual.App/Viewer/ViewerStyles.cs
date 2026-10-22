// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerStyles.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Salandual.App.Viewer;

internal static class ViewerStyles
{
    public static Styles Create()
    {
        Style Own<T>(string cls, params (AvaloniaProperty Property, object Value)[] setters)
            where T : Control
        {
            var style = new Style(x => x.OfType<T>().Class(cls));
            foreach (var (p, v) in setters.GroupBy(s => s.Property).Select(g => g.Last()))
            {
                style.Setters.Add(new Setter(p, v));
            }

            return style;
        }

        Style Presenter<T>(Func<Selector?, Selector> state, params (AvaloniaProperty Property, string Resource)[] setters)
            where T : Control
        {
            var style = new Style(x => state(x.OfType<T>()).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            foreach (var (p, r) in setters)
            {
                style.Setters.Add(new Setter(p, new DynamicResourceExtension(r)));
            }

            return style;
        }

        var flat = new (AvaloniaProperty, object)[]
        {
            (TemplatedControl.BackgroundProperty, Brushes.Transparent),
            (TemplatedControl.BorderThicknessProperty, new Thickness(0)),
            (TemplatedControl.PaddingProperty, new Thickness(8, 5)),
            (TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
            (Layoutable.VerticalAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center),
            (TemplatedControl.FontSizeProperty, 13.0),
        };

        return
        [
            Own<Button>("vtool", flat),
            Own<ToggleButton>("vtool", flat),
            Own<ToggleButton>("vmode", [.. flat, (TemplatedControl.PaddingProperty, new Thickness(10, 3))]),
            Own<ToggleButton>("vflag", [.. flat, (TemplatedControl.PaddingProperty, new Thickness(5, 1)), (Layoutable.MinWidthProperty, 22.0)]),
            Own<Button>("vflag", [.. flat, (TemplatedControl.PaddingProperty, new Thickness(4, 1)), (Layoutable.MinWidthProperty, 18.0), (TemplatedControl.FontSizeProperty, 10.0)]),
            Presenter<Button>(s => s.Class("vflag"), (ContentPresenter.ForegroundProperty, "Brush.Accent")),
            Presenter<Button>(s => s.Class("vflag").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            Own<Button>("vdrop", [.. flat, (TemplatedControl.BorderThicknessProperty, new Thickness(1)), (TemplatedControl.PaddingProperty, new Thickness(9, 4))]),
            Presenter<Button>(s => s.Class("vdrop"), (ContentPresenter.BorderBrushProperty, "Brush.Border")),
            Presenter<Button>(s => s.Class("vtool").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            Presenter<Button>(s => s.Class("vdrop").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.BorderBrushProperty, "Brush.BorderStrong"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            Presenter<Button>(s => s.Class("vtool").Class(":pressed"), (ContentPresenter.BackgroundProperty, "Brush.Pressed")),
            Presenter<ToggleButton>(s => s.Class("vtool").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            Presenter<ToggleButton>(s => s.Class("vflag").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            Presenter<ToggleButton>(s => s.Class("vmode").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            Presenter<ToggleButton>(s => s.Class("vtool").Class(":checked"), (ContentPresenter.BackgroundProperty, "Brush.TextSelection"), (ContentPresenter.ForegroundProperty, "Brush.SelectedText")),
            Presenter<ToggleButton>(s => s.Class("vflag").Class(":checked"), (ContentPresenter.BackgroundProperty, "Brush.TextSelection"), (ContentPresenter.ForegroundProperty, "Brush.SelectedText")),
            Presenter<ToggleButton>(s => s.Class("vmode").Class(":checked"), (ContentPresenter.BackgroundProperty, "Brush.Accent"), (ContentPresenter.ForegroundProperty, "Brush.OnAccent")),

            Own<ToggleButton>("vpill", [.. flat, (TemplatedControl.BorderThicknessProperty, new Thickness(1)), (TemplatedControl.PaddingProperty, new Thickness(8, 1)), (TemplatedControl.CornerRadiusProperty, new CornerRadius(10)), (TemplatedControl.FontSizeProperty, 11.0)]),
            Presenter<ToggleButton>(s => s.Class("vpill"), (ContentPresenter.BorderBrushProperty, "Brush.Border"), (ContentPresenter.ForegroundProperty, "Brush.SecondaryText")),
            Presenter<ToggleButton>(s => s.Class("vpill").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            Presenter<ToggleButton>(s => s.Class("vpill").Class(":checked"), (ContentPresenter.BackgroundProperty, "Brush.TextSelection"), (ContentPresenter.ForegroundProperty, "Brush.SelectedText"), (ContentPresenter.BorderBrushProperty, "Brush.Accent")),
            Own<Button>("vlink", [.. flat, (TemplatedControl.PaddingProperty, new Thickness(0)), (TemplatedControl.FontSizeProperty, 12.0), (Layoutable.HorizontalAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Left)]),
            Presenter<Button>(s => s.Class("vlink"), (ContentPresenter.ForegroundProperty, "Brush.Accent")),
            new Style(x => x.OfType<Button>().Class("vlink").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) },
            },

            new Style(x => x.OfType<Button>().Class("vtool").Class(":disabled").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent), new Setter(Visual.OpacityProperty, 0.4) },
            },
        ];
    }
}
