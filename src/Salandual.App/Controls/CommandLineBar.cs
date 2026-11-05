// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/CommandLineBar.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Salandual.App.Options;
using Salandual.App.Resources;

namespace Salandual.App.Controls;

public sealed class CommandLineBar : Panel
{
    private readonly TextBlock _prompt;
    private readonly TextBlock _warning;
    private readonly Popup _popup;
    private readonly StackPanel _rows = new();
    private int _selected = -1;

    public CommandLineBar()
    {
        Margin = new Thickness(8, 0, 8, 6);
        _prompt = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 2, 0), TextTrimming = TextTrimming.PrefixCharacterEllipsis, MaxWidth = 420 }
            .Res(TextBlock.ForegroundProperty, "Brush.SecondaryText").Res(TextBlock.FontFamilyProperty, "Font.Mono");
        _warning = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), IsVisible = false, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 }
            .Res(TextBlock.ForegroundProperty, "Brush.Danger");
        ShellButton = new Button
        {
            Padding = new Thickness(8, 0),
            Margin = new Thickness(4, 3, 4, 3),
            MinHeight = 22,
            FontSize = 11,
            Focusable = false,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        }.Res(Button.BorderBrushProperty, "Brush.BorderStrong").Res(Button.ForegroundProperty, "Brush.SecondaryText");
        ShellButton.Click += (_, _) => ShellClicked?.Invoke(this, EventArgs.Empty);

        Box = new TextBox
        {
            MinHeight = 30,
            Padding = new Thickness(6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn = false,
            InnerLeftContent = _prompt,
            InnerRightContent = new StackPanel { Orientation = Orientation.Horizontal, Children = { _warning, ShellButton } },
        };
        Box.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(Box, AppStrings.CommandLine_Name);

        foreach (var focus in new[] { false, true })
        {
            Box.Styles.Add(new Style(x => (focus ? x.OfType<TextBox>().Class("risky").Class(":focus") : x.OfType<TextBox>().Class("risky")).Template().OfType<Border>().Name("PART_BorderElement"))
            {
                Setters = { new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Brush.Danger")) },
            });
        }

        var head = new TextBlock { Text = AppStrings.CommandLine_HistoryHead, FontSize = 12, Margin = new Thickness(10, 4, 10, 6) }
            .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
        _popup = new Popup
        {
            PlacementTarget = Box,
            Placement = PlacementMode.TopEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                Padding = new Thickness(4),
                MinWidth = 520,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                BoxShadow = BoxShadows.Parse("0 8 24 0 #30000000"),
                Child = new DockPanel
                {
                    Children =
                    {
                        head.Also(h => DockPanel.SetDock(h, Dock.Top)),
                        new ScrollViewer { Content = _rows, MaxHeight = 360, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                    },
                },
            }.Res(Border.BackgroundProperty, "Brush.PanelBackground").Res(Border.BorderBrushProperty, "Brush.BorderStrong"),
        };
        _popup.Closed += (_, _) => _selected = -1;

        Children.Add(Box);
        Children.Add(_popup);
    }

    public TextBox Box { get; }

    public Button ShellButton { get; }

    public event EventHandler? ShellClicked;

    public event EventHandler<int>? HistoryChosen;

    public string Prompt
    {
        get => _prompt.Text ?? "";
        set => _prompt.Text = value;
    }

    public string? Warning
    {
        get => _warning.IsVisible ? _warning.Text : null;
        set
        {
            _warning.Text = value;
            _warning.IsVisible = value is not null;
            ToolTip.SetTip(_warning, value);
            Box.Classes.Set("risky", value is not null);
        }
    }

    public bool IsHistoryOpen => _popup.IsOpen;

    public int HistorySelected => _selected;

    internal int HistoryCount => _rows.Children.Count;

    public void ShowHistory(IReadOnlyList<(string Text, string Where)> rows, int selected)
    {
        _rows.Children.Clear();
        for (var i = 0; i < rows.Count; i++)
        {
            var index = i;
            var (text, where) = rows[i];
            var row = new Border
            {
                Padding = new Thickness(10, 4),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Child = new DockPanel
                {
                    Children =
                    {
                        new TextBlock { Text = where, FontSize = 12, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }
                            .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText").Also(w => DockPanel.SetDock(w, Dock.Right)),
                        new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }
                            .Res(TextBlock.FontFamilyProperty, "Font.Mono"),
                    },
                },
            };
            row.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                {
                    HistoryChosen?.Invoke(this, index);
                    e.Handled = true;
                }
            };
            _rows.Children.Add(row);
        }

        _popup.IsOpen = rows.Count > 0;
        Select(selected);
    }

    public void Select(int index)
    {
        _selected = _rows.Children.Count == 0 ? -1 : Math.Clamp(index, 0, _rows.Children.Count - 1);
        for (var i = 0; i < _rows.Children.Count; i++)
        {
            var row = (Border)_rows.Children[i];
            if (i == _selected)
            {
                row.Res(Border.BackgroundProperty, "Brush.SelectionBackground");
                row.BringIntoView();
            }
            else
            {
                row.Background = Brushes.Transparent;
            }
        }
    }

    public void HideHistory() => _popup.IsOpen = false;
}
