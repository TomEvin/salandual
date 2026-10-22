// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/ZoomBox.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using AppIcons = Salandual.App.Theme.AppIcons;

namespace Salandual.App.Controls;

internal sealed class ZoomBox : Border
{
    public static readonly double[] Presets = [5, 10, 25, 50, 75, 100, 125, 150, 200, 400, 600, 800, 1600, 3200, 6400];

    private readonly TextBox _box = new()
    {
        Width = 64,
        MinHeight = 0,
        Padding = new Thickness(4, 0),
        BorderThickness = new Thickness(0),
        Background = null,
        VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        TextAlignment = Avalonia.Media.TextAlignment.Center,
        FontSize = 12,
    };

    private readonly Button _drop = new() { Focusable = false, Padding = new Thickness(4, 0), MinWidth = 0, VerticalAlignment = VerticalAlignment.Stretch };
    private double _percent = 100;

    public ZoomBox()
    {
        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(1);
        VerticalAlignment = VerticalAlignment.Stretch;
        this.Res(BorderBrushProperty, "Brush.Border").Res(BackgroundProperty, "Brush.PanelBackground");
        _drop.Classes.Add("vtool");
        _drop.Content = AppIcons.Glyph("Icon.ChevronDown", 10, 10, 1.2);
        ToolTip.SetTip(this, AppStrings.Viewer_ZoomBoxTip);
        Avalonia.Automation.AutomationProperties.SetName(_box, AppStrings.Viewer_ZoomBoxTip);
        Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _box, _drop } };
        _box.Tag = this;
        _drop.Click += (_, _) => Menu().ShowAt(this);
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Apply();
                e.Handled = true;
                Done?.Invoke();
            }
            else if (e.Key == Key.Escape)
            {
                Show();
                e.Handled = true;
                Done?.Invoke();
            }
        };
        _box.LostFocus += (_, _) => Apply();
        Show();
    }

    public event Action<double>? Chosen;

    public event Action? Done;

    public double Percent
    {
        get => _percent;
        set
        {
            _percent = value;
            if (!_box.IsFocused)
            {
                Show();
            }
        }
    }

    internal TextBox Box => _box;

    internal static double? Parse(string? text)
    {
        var t = (text ?? "").Replace("%", "", StringComparison.Ordinal).Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out var n) || double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out n))
        {
            return n > 0 && double.IsFinite(n) ? n : null;
        }

        return null;
    }

    internal static string Format(double percent)
        => Loc.Format(AppStrings.Viewer_ZoomPercent, percent >= 10 ? Math.Round(percent).ToString("N0", CultureInfo.CurrentCulture) : Math.Max(0.1, Math.Round(percent, 1)).ToString("0.#", CultureInfo.CurrentCulture));

    internal static IEnumerable<MenuItem> PresetItems(double current, Action<double> choose)
    {
        foreach (var preset in Presets)
        {
            var item = new MenuItem { Header = Format(preset), ToggleType = MenuItemToggleType.Radio, IsChecked = Math.Abs(preset - current) < 0.05 };
            item.Click += (_, _) => choose(preset);
            yield return item;
        }
    }

    private MenuFlyout Menu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var item in PresetItems(_percent, Choose))
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    private void Choose(double percent)
    {
        Chosen?.Invoke(percent);
        Show();
    }

    private void Apply()
    {
        if (Parse(_box.Text) is { } percent && Math.Abs(percent - _percent) > 0.001)
        {
            Chosen?.Invoke(percent);
        }

        Show();
    }

    private void Show() => _box.Text = Format(_percent);
}
