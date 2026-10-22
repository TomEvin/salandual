// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/TitleBarParts.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Salandual.App.Controls;

internal static class TitleBarParts
{
    public const double Height = 32;

    public const double CaptionButtonWidth = 47;

    public static void ExtendIntoTitleBar(Window window)
    {
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = Height;
    }

    public static TextBlock Title(Window window)
    {
        var title = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        title.Bind(TextBlock.TextProperty, window.GetObservable(Window.TitleProperty));
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("Brush.SecondaryText"));
        DimWhenInactive(window, title);
        return title;
    }

    public static void DimWhenInactive(Window window, Control control)
        => window.GetObservable(WindowBase.IsActiveProperty).Subscribe(new Observer<bool>(active => control.Opacity = active ? 1 : 0.55));

    public static Control ForDialog(Window window, Control content, params Control[] buttons)
    {
        ExtendIntoTitleBar(window);
        window.CanMinimize = false;
        window.CanMaximize = false;
        window.Classes.Add("dialog");
        DialogPlacement.KeepCentered(window);

        var bar = new Grid
        {
            Height = Height,
            ColumnDefinitions = new ColumnDefinitions($"*,{CaptionButtonWidth + 6}"),
            Background = Brushes.Transparent,
        };
        WindowDecorationProperties.SetElementRole(bar, WindowDecorationsElementRole.TitleBar);
        var title = Title(window);
        title.Margin = new Thickness(14, 0, 8, 0);
        bar.Children.Add(title);
        if (buttons.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            foreach (var button in buttons)
            {
                WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);
                row.Children.Add(button);
            }

            bar.Children.Add(row);
        }

        bar.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right)
            {
                var at = e.GetPosition(window);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SystemMenu.Show(window, at));
                e.Handled = true;
            }
        };
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Alt)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SystemMenu.Show(window, new Point(4, Height)));
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(content);
        return root;
    }

    public static Control ForWindow(Window window, Control title, Control content)
    {
        ExtendIntoTitleBar(window);
        var bar = new Grid
        {
            Height = Height,
            ColumnDefinitions = new ColumnDefinitions($"*,{3 * CaptionButtonWidth}"),
            Background = Brushes.Transparent,
        };
        WindowDecorationProperties.SetElementRole(bar, WindowDecorationsElementRole.TitleBar);
        title.Margin = new Thickness(12, 0, 8, 0);
        title.IsHitTestVisible = false;
        DimWhenInactive(window, title);
        bar.Children.Add(title);
        bar.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right)
            {
                var at = e.GetPosition(window);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SystemMenu.Show(window, at));
                e.Handled = true;
            }
        };
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Alt)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SystemMenu.Show(window, new Point(4, Height)));
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(content);
        return root;
    }

    public static Control Logo() => Salandual.App.Theme.AppIcons.Mark(16);

    public static Button Button(object content, string tip, string name)
    {
        var button = new Button
        {
            Content = content,
            Height = Height - 4,
            Padding = new Thickness(8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Focusable = false,
        };
        WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    private sealed class Observer<T>(Action<T> next) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => next(value);
    }
}
