// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Find/FindStyles.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace Salandual.App.Find;

internal static class FindStyles
{
    public static Styles Create()
    {
        static Style Own<T>(string cls, params (AvaloniaProperty Property, object Value)[] setters)
            where T : Control
        {
            var style = new Style(x => x.OfType<T>().Class(cls));
            foreach (var (p, v) in setters.GroupBy(s => s.Property).Select(g => g.Last()))
            {
                style.Setters.Add(new Setter(p, v));
            }

            return style;
        }

        static Style Presenter<T>(Func<Selector?, Selector> state, params (AvaloniaProperty Property, string Resource)[] setters)
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
            (Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
            (TemplatedControl.FontSizeProperty, 13.0),
        };

        return
        [
            Own<Button>("chip", [.. flat, (TemplatedControl.BorderThicknessProperty, new Thickness(1)), (TemplatedControl.PaddingProperty, new Thickness(10, 2)), (TemplatedControl.CornerRadiusProperty, new CornerRadius(4)), (TemplatedControl.FontSizeProperty, 12.0), (Layoutable.MinHeightProperty, 24.0)]),
            Presenter<Button>(s => s.Class("chip"), (ContentPresenter.BorderBrushProperty, "Brush.BorderStrong"), (ContentPresenter.ForegroundProperty, "Brush.SecondaryText"), (ContentPresenter.BackgroundProperty, "Brush.PanelBackground")),
            Presenter<Button>(s => s.Class("chip").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            Presenter<Button>(s => s.Class("chip").Class("on"), (ContentPresenter.BorderBrushProperty, "Brush.Accent"), (ContentPresenter.ForegroundProperty, "Brush.Accent"), (ContentPresenter.BackgroundProperty, "Brush.InfoBackground")),
            Presenter<Button>(s => s.Class("chip").Class("no"), (ContentPresenter.BorderBrushProperty, "Brush.Danger"), (ContentPresenter.ForegroundProperty, "Brush.Danger"), (ContentPresenter.BackgroundProperty, "Brush.DangerBackground")),

            Own<Button>("link", [.. flat, (TemplatedControl.PaddingProperty, new Thickness(0)), (TemplatedControl.FontSizeProperty, 12.5)]),
            Presenter<Button>(s => s.Class("link"), (ContentPresenter.ForegroundProperty, "Brush.Accent")),
            new Style(x => x.OfType<Button>().Class("link").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) },
            },

            Own<ToggleButton>("seg", [.. flat, (TemplatedControl.PaddingProperty, new Thickness(10, 3))]),

            Own<ToggleButton>("opt", [.. flat, (TemplatedControl.BorderThicknessProperty, new Thickness(1)), (TemplatedControl.PaddingProperty, new Thickness(10, 3)), (Layoutable.MinHeightProperty, 28.0)]),
            Presenter<ToggleButton>(s => s.Class("opt"), (ContentPresenter.BorderBrushProperty, "Brush.BorderStrong"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            Presenter<ToggleButton>(s => s.Class("opt").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            Presenter<ToggleButton>(s => s.Class("opt").Class(":checked"), (ContentPresenter.BackgroundProperty, "Brush.Accent"), (ContentPresenter.ForegroundProperty, "Brush.OnAccent"), (ContentPresenter.BorderBrushProperty, "Brush.Accent")),
            Presenter<ToggleButton>(s => s.Class("opt").Class(":checked").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Accent"), (ContentPresenter.ForegroundProperty, "Brush.OnAccent"), (ContentPresenter.BorderBrushProperty, "Brush.Accent")),
            new Style(x => x.OfType<FlyoutPresenter>().Class("compact"))
            {
                Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(6)), new Setter(TemplatedControl.FontSizeProperty, 13.0) },
            },

            new Style(x => x.OfType<CalendarDayButton>().Class(":today").Template().OfType<Border>().Name("Root"))
            {
                Setters = { new Setter(Border.BackgroundProperty, Brushes.Transparent) },
            },
            new Style(x => x.OfType<CalendarDayButton>().Class(":today").Template().OfType<Border>().Name("Border"))
            {
                Setters = { new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Brush.Accent")) },
            },
            new Style(x => x.OfType<CalendarDayButton>().Class(":today").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.ForegroundProperty, new DynamicResourceExtension("Brush.Accent")) },
            },
            new Style(x => x.OfType<CalendarDayButton>().Class("inrange").Template().OfType<Border>().Name("Root"))
            {
                Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Brush.FindRange")) },
            },
            new Style(x => x.OfType<CalendarDayButton>().Class("rangeend").Template().OfType<Border>().Name("Border"))
            {
                Setters = { new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Brush.Accent")) },
            },
            new Style(x => x.OfType<CalendarDatePicker>().Class("error"))
            {
                Setters = { new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension("Brush.Danger")) },
            },
            new Style(x => x.OfType<CalendarDayButton>().Class(":selected").Template().OfType<Border>().Name("Root"))
            {
                Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Brush.Accent")) },
            },
            new Style(x => x.OfType<CalendarDayButton>().Class(":selected").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.ForegroundProperty, new DynamicResourceExtension("Brush.OnAccent")) },
            },
            new Style(x => x.OfType<CalendarDayButton>().Class(":selected").Template().OfType<Border>().Name("Border"))
            {
                Setters = { new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Brush.Accent")) },
            },
            new Style(x => x.OfType<CalendarDayButton>().Class(":selected").Class(":today").Template().OfType<Border>().Name("Border"))
            {
                Setters = { new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Brush.Text")) },
            },
            Presenter<ToggleButton>(s => s.Class("seg").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            Presenter<ToggleButton>(s => s.Class("seg").Class(":checked"), (ContentPresenter.BackgroundProperty, "Brush.Accent"), (ContentPresenter.ForegroundProperty, "Brush.OnAccent")),

            Own<Button>("ftool", flat),

            Own<Button>("clearadv", (TemplatedControl.BorderThicknessProperty, new Thickness(1))),
            Presenter<Button>(s => s.Class("clearadv"), (ContentPresenter.BackgroundProperty, "Brush.DangerBackground"), (ContentPresenter.BorderBrushProperty, "Brush.Danger")),
            Presenter<Button>(s => s.Class("clearadv").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Danger"), (ContentPresenter.BorderBrushProperty, "Brush.Danger")),
            Presenter<Button>(s => s.Class("clearadv").Class(":pressed"), (ContentPresenter.BackgroundProperty, "Brush.Danger"), (ContentPresenter.BorderBrushProperty, "Brush.Danger")),
            new Style(x => x.OfType<Button>().Class("clearadv").Descendant().OfType<Avalonia.Controls.Shapes.Path>())
            {
                Setters = { new Setter(Avalonia.Controls.Shapes.Shape.StrokeProperty, new DynamicResourceExtension("Brush.Danger")) },
            },
            new Style(x => x.OfType<Button>().Class("clearadv").Class(":pointerover").Descendant().OfType<Avalonia.Controls.Shapes.Path>())
            {
                Setters = { new Setter(Avalonia.Controls.Shapes.Shape.StrokeProperty, new DynamicResourceExtension("Brush.PanelBackground")) },
            },

            Presenter<Button>(s => s.Class("fstop"), (ContentPresenter.BackgroundProperty, "Brush.Danger"), (ContentPresenter.ForegroundProperty, "Brush.PanelBackground"), (ContentPresenter.BorderBrushProperty, "Brush.Danger")),
            Presenter<Button>(s => s.Class("fstop").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Danger"), (ContentPresenter.ForegroundProperty, "Brush.PanelBackground"), (ContentPresenter.BorderBrushProperty, "Brush.Text")),
            Presenter<Button>(s => s.Class("fstop").Class(":pressed"), (ContentPresenter.BackgroundProperty, "Brush.Danger"), (ContentPresenter.ForegroundProperty, "Brush.PanelBackground"), (ContentPresenter.BorderBrushProperty, "Brush.Text")),

            new Style(x => x.OfType<Border>().Class("fcriteria").Descendant().OfType<Button>().Class(":disabled")) { Setters = { new Setter(Visual.OpacityProperty, 0.45) } },
            new Style(x => x.OfType<Border>().Class("fcriteria").Descendant().OfType<ToggleButton>().Class(":disabled")) { Setters = { new Setter(Visual.OpacityProperty, 0.45) } },

            new Style(x => x.OfType<MenuFlyoutPresenter>().Class("wide")) { Setters = { new Setter(Layoutable.MaxWidthProperty, 640.0) } },

            new Style(x => x.OfType<Button>().Class("saved-ed")) { Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
            new Style(x => x.OfType<MenuItem>().Class(":pointerover").Descendant().OfType<Button>().Class("saved-ed")) { Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
            new Style(x => x.OfType<MenuItem>().Class(":selected").Descendant().OfType<Button>().Class("saved-ed")) { Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
            Presenter<Button>(s => s.Class("ftool").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.ForegroundProperty, "Brush.Text")),

            Own<Button>("fcol", [.. flat, (TemplatedControl.PaddingProperty, new Thickness(8, 4)), (TemplatedControl.CornerRadiusProperty, new CornerRadius(0)), (TemplatedControl.FontSizeProperty, 12.0)]),
            Presenter<Button>(s => s.Class("fcol"), (ContentPresenter.ForegroundProperty, "Brush.SecondaryText")),
            Presenter<Button>(s => s.Class("fcol").Class(":pointerover"), (ContentPresenter.BackgroundProperty, "Brush.Hover"), (ContentPresenter.ForegroundProperty, "Brush.Text")),
            new Style(x => x.OfType<Button>().Class("fcol").Class(":disabled").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) },
            },

            new Style(x => x.OfType<ListBoxItem>())
            {
                Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(0)), new Setter(Layoutable.MinHeightProperty, 0.0) },
            },
        ];
    }
}
