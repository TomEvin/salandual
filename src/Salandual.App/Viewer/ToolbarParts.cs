// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ToolbarParts.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using AppIcons = Salandual.App.Theme.AppIcons;

namespace Salandual.App.Viewer;

internal static class ToolbarParts
{
    internal static Control Glyph(string name) => AppIcons.Glyph(name, 16, 16, 1.3, "Brush.Text");

    internal static StackPanel Label(string? icon, string? text, string? key)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (icon is not null)
        {
            panel.Children.Add(Glyph(icon));
        }

        if (text is not null)
        {
            panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        }

        if (key is not null)
        {
            panel.Children.Add(new TextBlock { Text = key, FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
        }

        return panel;
    }

    internal static Button Tool(object content, string tip)
    {
        var b = new Button { Content = content, Focusable = false };
        b.Classes.Add("vtool");
        ToolTip.SetTip(b, tip);
        return b;
    }

    internal static ToggleButton Toggle(object content, string tip)
    {
        var b = new ToggleButton { Content = content, Focusable = false };
        b.Classes.Add("vtool");
        ToolTip.SetTip(b, tip);
        return b;
    }

    internal static ToggleButton Mode(string text, string? key)
    {
        var b = new ToggleButton { Content = Label(null, text, key), Focusable = false };
        b.Classes.Add("vmode");
        return b;
    }

    internal static ToggleButton Flag(string text, string tip)
    {
        var b = new ToggleButton { Content = text, Focusable = false, FontSize = 11 };
        b.Classes.Add("vflag");
        ToolTip.SetTip(b, tip);
        return b;
    }

    internal static Button Pieces(ToggleButton regex, Func<Control> box, bool replacement = false)
    {
        var b = new Button { Content = "▾", Focusable = false, IsVisible = regex.IsChecked == true };
        b.Classes.Add("vflag");
        ToolTip.SetTip(b, replacement ? AppStrings.Find_ReplacementPiecesTip : AppStrings.Find_PiecesTip);
        Avalonia.Automation.AutomationProperties.SetName(b, replacement ? AppStrings.Find_ReplacementPiecesTip : AppStrings.Find_PiecesTip);
        b.Flyout = replacement ? Salandual.App.Find.RegexMenu.CreateReplacement(box) : Salandual.App.Find.RegexMenu.Create(box);
        regex.IsCheckedChanged += (_, _) => b.IsVisible = regex.IsChecked == true;
        return b;
    }

    internal static Button Drop(string tip)
    {
        var b = new Button { Focusable = false };
        b.Classes.Add("vdrop");
        ToolTip.SetTip(b, tip);
        return b;
    }

    internal static void SetDropText(Button drop, string text)
        => drop.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }, AppIcons.Glyph("Icon.ChevronDown", 10, 10, 1.2) } };

    internal static Border Segment(params ToggleButton[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.AddRange(buttons);
        return new Border { Child = row, Padding = new Thickness(2), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center }
            .Res(Border.BorderBrushProperty, "Brush.Border");
    }

    internal static FitRow Row(params Control[] children)
    {
        var row = new FitRow { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        row.Children.AddRange(children);
        return row;
    }

    internal static FitRow Group(params Control[] children)
    {
        var row = new FitRow { Spacing = 4, VerticalAlignment = VerticalAlignment.Stretch, KeepTogether = true };
        row.Children.AddRange(children);
        return row;
    }

    internal static Border Separator() => new Border { Width = 1, Height = 20, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center }.Res(Border.BackgroundProperty, "Brush.Border");
}
