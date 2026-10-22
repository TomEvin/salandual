// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsUi.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Controls.Presenters;
using Avalonia.Styling;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Settings;

namespace Salandual.App.Options;

internal static class OptionsUi
{
    public static T Res<T>(this T control, AvaloniaProperty property, string key)
        where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }

    public static TextBlock Text(string text, double size = 14, FontWeight weight = FontWeight.Normal, string brush = "Brush.Text", bool wrap = false)
        => new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        }.Res(TextBlock.ForegroundProperty, brush);

    public static TextBlock Secondary(string text, double size = 12) => Text(text, size, brush: "Brush.SecondaryText", wrap: true);

    public static Border Badge(string text) => new Border
    {
        CornerRadius = Corners.Chip,
        Padding = new Thickness(7, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = Text(text, 11, FontWeight.SemiBold, "Brush.SecondaryText"),
    }.Res(Border.BackgroundProperty, "Brush.Hover");

    public static Control Card(string? title, IEnumerable<Control> rows, Control? aside = null)
    {
        if (aside is not null)
        {
            var rowsCard = (StackPanel)Card(null, rows);
            var inner = (Border)rowsCard.Children[^1];
            var withAside = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var list = inner.Child!;
            inner.Child = null;
            withAside.Children.Add(list);
            var sticky = new StickyPanel { Margin = new Thickness(8, 12, 16, 12), Children = { aside } };
            Grid.SetColumn(sticky, 1);
            withAside.Children.Add(sticky);
            inner.Child = withAside;
            if (title is not null)
            {
                rowsCard.Children.Insert(0, Text(title, 14, FontWeight.SemiBold));
            }

            return rowsCard;
        }

        return CardCore(title, rows);
    }

    private static Control CardCore(string? title, IEnumerable<Control> rows)
    {
        var list = new StackPanel();
        var first = true;
        foreach (var row in rows)
        {
            if (!first)
            {
                list.Children.Add(new Border { Height = 1 }.Res(Border.BackgroundProperty, "Brush.Border"));
            }

            list.Children.Add(row);
            first = false;
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = list,
        }.Res(Border.BackgroundProperty, "Brush.PanelBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 20) };
        if (title is not null)
        {
            panel.Children.Add(Text(title, 14, FontWeight.SemiBold));
        }

        panel.Children.Add(card);
        return panel;
    }

    public static Control Row(string? title, string? description, Control? control, bool wide = false, string? extra = null, string? badge = null, Control? below = null)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (title is not null && badge is not null)
        {
            text.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Text(title, 14), Badge(badge) } });
        }
        else if (title is not null)
        {
            text.Children.Add(Text(title, 14, wrap: true));
        }

        if (extra is not null)
        {
            text.Children.Add(Text(extra, 12, brush: "Brush.Accent"));
        }

        if (description is not null)
        {
            text.Children.Add(Secondary(description));
        }

        if (wide)
        {
            var stack = new StackPanel { Spacing = 10, Margin = new Thickness(16, 12) };
            if (text.Children.Count > 0)
            {
                stack.Children.Add(text);
            }

            if (control is not null)
            {
                stack.Children.Add(control);
            }

            if (below is not null)
            {
                stack.Children.Add(below);
            }

            return stack;
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(16, 10), MinHeight = 40 };
        grid.Children.Add(text);
        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        if (below is not null)
        {
            below.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(below, 1);
            Grid.SetColumnSpan(below, 2);
            grid.Children.Add(below);
        }

        if (badge is not null)
        {
            grid.Opacity = 0.55;
        }

        return grid;
    }

    public static ToggleSwitch Toggle(bool value, Action<bool> changed, bool enabled = true)
    {
        var toggle = new ToggleSwitch { IsChecked = value, IsEnabled = enabled, OnContent = AppStrings.Common_On, OffContent = AppStrings.Common_Off, MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        return toggle;
    }

    public static Control Segmented<T>(IReadOnlyList<(T Value, string Label)> options, T current, Action<T> changed, bool enabled = true)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, IsEnabled = enabled };
        var buttons = new List<ToggleButton>();
        for (var i = 0; i < options.Count; i++)
        {
            var (value, label) = options[i];
            var radius = options.Count == 1 ? new CornerRadius(4)
                : i == 0 ? new CornerRadius(4, 0, 0, 4)
                : i == options.Count - 1 ? new CornerRadius(0, 4, 4, 0)
                : new CornerRadius(0);
            var button = new ToggleButton
            {
                Content = label,
                IsChecked = EqualityComparer<T>.Default.Equals(value, current),
                CornerRadius = radius,
                Padding = new Thickness(12, 5),
                Margin = new Thickness(i == 0 ? 0 : -1, 0, 0, 0),
                MinWidth = 56,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            button.Click += (_, _) =>
            {
                foreach (var b in buttons)
                {
                    b.IsChecked = ReferenceEquals(b, button);
                }

                changed(value);
            };
            buttons.Add(button);
            panel.Children.Add(button);
        }

        return panel;
    }

    public static Control ChoiceCards<T>(IReadOnlyList<(T Value, string Title, string Description, Func<Control> Picture)> options, T current, Action<T> changed)
    {
        var grid = new UniformGrid { Columns = options.Count, Rows = 1 };
        grid.Styles.AddRange(ChoiceCardStyles());
        var cards = new List<(Button Card, T Value)>();
        void Mark(T selected)
        {
            foreach (var (card, value) in cards)
            {
                var on = EqualityComparer<T>.Default.Equals(value, selected);
                card.Classes.Set("selected", on);
                card.BorderThickness = new Thickness(on ? 2 : 1);
                card.Padding = new Thickness(on ? 11 : 12);
                card.Res(Button.BorderBrushProperty, on ? "Brush.Accent" : "Brush.BorderStrong");
            }
        }

        for (var i = 0; i < options.Count; i++)
        {
            var (value, title, description, picture) = options[i];
            var pic = picture();
            pic.Margin = new Thickness(0, 0, 0, 10);
            var content = new StackPanel { Spacing = 2, Children = { pic, Text(title, 14, FontWeight.SemiBold), Secondary(description) } };
            var card = new Button
            {
                Content = content,
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                Margin = new Thickness(i == 0 ? 0 : 6, 0, i == options.Count - 1 ? 0 : 6, 0),
            }.Res(Button.BackgroundProperty, "Brush.PanelBackground");
            card.Classes.Add("choice-card");
            Avalonia.Automation.AutomationProperties.SetName(card, title);
            card.Click += (_, _) =>
            {
                Mark(value);
                changed(value);
            };
            cards.Add((card, value));
            grid.Children.Add(card);
        }

        Mark(current);
        return grid;
    }

    private static Styles ChoiceCardStyles()
    {
        static Style Presenter(Func<Selector?, Selector> state, string background, string border) => new(x => state(x.OfType<Button>().Class("choice-card")).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, new DynamicResourceExtension(background)),
                new Setter(ContentPresenter.BorderBrushProperty, new DynamicResourceExtension(border)),
            },
        };

        return
        [
            Presenter(s => s.Class(":pointerover"), "Brush.PanelBackground", "Brush.SecondaryText"),
            Presenter(s => s.Class("selected").Class(":pointerover"), "Brush.PanelBackground", "Brush.Accent"),
            Presenter(s => s.Class(":pressed"), "Brush.PanelBackground", "Brush.Accent"),
        ];
    }

    public static Control MiniWindow(string[] rows, int titleMarks = 0, bool paneStrips = false, bool logoChevron = false)
    {
        var stack = new StackPanel { Spacing = 3 };
        const double total = 56;
        var used = rows.Sum(r => r switch { "title" or "menu" => 10.0, "strip" => 7.0, _ => 0.0 }) + (3 * (rows.Length - 1));
        var panesHeight = total - used;
        Border Bar(double height, string brush, double opacity = 1) => new Border { Height = height, CornerRadius = new CornerRadius(2), Opacity = opacity }.Res(Border.BackgroundProperty, brush);
        StackPanel Marks(int count, bool logo)
        {
            var marks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(3, 2) };
            if (logo)
            {
                marks.Children.Add(new Border { Width = 5, CornerRadius = new CornerRadius(1) }.Res(Border.BackgroundProperty, "Brush.Accent"));
                if (logoChevron)
                {
                    marks.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(1), Opacity = 0.35 }.Res(Border.BackgroundProperty, "Brush.Accent"));
                }
            }

            for (var i = 0; i < count; i++)
            {
                marks.Children.Add(new Border { Width = 9, CornerRadius = new CornerRadius(1), Opacity = 0.55 }.Res(Border.BackgroundProperty, "Brush.Accent"));
            }

            return marks;
        }

        foreach (var row in rows)
        {
            switch (row)
            {
                case "title":
                    stack.Children.Add(new Border { Height = 10, CornerRadius = new CornerRadius(2), Child = Marks(titleMarks, logo: true) }.Res(Border.BackgroundProperty, "Brush.Pressed"));
                    break;
                case "menu":
                    stack.Children.Add(new Border { Height = 10, Child = Marks(5, logo: false) });
                    break;
                case "strip":
                    stack.Children.Add(Bar(7, "Brush.Accent", 0.55));
                    break;
                case "panes":
                    var panes = new UniformGrid { Columns = 2, Rows = 1 };
                    for (var p = 0; p < 2; p++)
                    {
                        var pane = new StackPanel { Spacing = 3, Margin = new Thickness(p == 0 ? 0 : 2, 0, p == 0 ? 2 : 0, 0) };
                        if (paneStrips)
                        {
                            pane.Children.Add(Bar(7, "Brush.Accent", 0.55));
                        }

                        pane.Children.Add(Bar(paneStrips ? panesHeight - 10 : panesHeight, "Brush.Pressed"));
                        panes.Children.Add(pane);
                    }

                    stack.Children.Add(panes);
                    break;
            }
        }

        return stack;
    }

    public static ComboBox Combo<T>(IReadOnlyList<(T Value, string Label)> options, T current, Action<T> changed, double minWidth = 180)
    {
        var combo = new ComboBox { MinWidth = minWidth, MaxDropDownHeight = 360 };
        foreach (var (_, label) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = label });
        }

        var index = -1;
        for (var i = 0; i < options.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(options[i].Value, current))
            {
                index = i;
            }
        }

        combo.SelectedIndex = index;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                changed(options[combo.SelectedIndex].Value);
            }
        };
        return combo;
    }

    public static Button ActionButton(string text, Action click, bool enabled = true)
    {
        var button = new Button { Content = text, IsEnabled = enabled, Padding = new Thickness(12, 5) };
        button.Click += (_, _) => click();
        return button;
    }

    public static Border Swatch(Rgb color, double size = 18) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(4),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
        Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static readonly string[] Palette =
    [
        "#FFFFFF", "#F9F9F9", "#F3F3F3", "#E1E1E1", "#C8C8C8", "#8A8A8A", "#5C5C5C", "#3A3A3A", "#272727", "#1F1F1F", "#000000",
        "#C42B1C", "#CA5010", "#9D5D00", "#498205", "#107C10", "#107C41", "#038387", "#0063B1", "#0F6CBD", "#5C2E91", "#881798",
        "#FF99A4", "#F7A35C", "#F7C04A", "#A4D96C", "#6CCB5F", "#9FD89F", "#58D3DB", "#60CDFF", "#479EF5", "#B696E4", "#E8A0E8",
    ];

    public static Button ColorButton(Rgb current, Action<Rgb> changed, string? automationName = null)
    {
        var button = ColorButton(() => current, color =>
        {
            current = color;
            changed(color);
        }, automationName, 20);
        return button;
    }

    public static Button ColorButton(Func<Rgb> current, Action<Rgb> changed, string? automationName, double size)
    {
        var button = new Button
        {
            Padding = new Thickness(4),
            MinWidth = 0,
            Content = Swatch(current(), size),
        };
        if (automationName is not null)
        {
            Avalonia.Automation.AutomationProperties.SetName(button, automationName);
        }

        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        button.Flyout = flyout;
        button.Click += (_, _) => flyout.Content = PalettePanel(current(), color =>
        {
            button.Content = Swatch(color, size);
            flyout.Hide();
            changed(color);
        });
        return button;
    }

    private static Control PalettePanel(Rgb current, Action<Rgb> pick)
    {
        var grid = new UniformGrid { Columns = 11 };
        foreach (var hex in Palette)
        {
            var color = ColorMath.ParseHex(hex);
            var swatch = new Button
            {
                Padding = new Thickness(0),
                Margin = new Thickness(2),
                MinWidth = 0,
                Width = 26,
                Height = 26,
                Content = Swatch(color, 22),
            };
            ToolTip.SetTip(swatch, hex);
            swatch.Click += (_, _) => pick(color);
            grid.Children.Add(swatch);
        }

        var hexBox = new TextBox { Text = ColorMath.ToHex(current), Width = 110, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        var apply = new Button { Content = AppStrings.Common_Apply, Padding = new Thickness(12, 5) };
        void Apply()
        {
            if (ColorMath.TryParseHex(hexBox.Text, out var c))
            {
                pick(c);
            }
        }

        apply.Click += (_, _) => Apply();
        hexBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Apply();
                e.Handled = true;
            }
        };

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(2, 8, 0, 0) };
        bottom.Children.Add(Swatch(current, 26));
        bottom.Children.Add(hexBox);
        bottom.Children.Add(apply);
        return new StackPanel { Children = { grid, bottom } };
    }
}
