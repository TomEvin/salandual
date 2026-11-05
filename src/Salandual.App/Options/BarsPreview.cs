// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/BarsPreview.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

internal sealed class BarsPreview : Border
{
    private readonly SettingsService? _settings;
    private readonly Func<LayoutSettings> _layout;

    public BarsPreview(SettingsService settings)
        : this(() => settings.Current.Layout)
    {
        _settings = settings;
    }

    public BarsPreview(Func<LayoutSettings> layout)
    {
        _layout = layout;
        Width = 260;
        Padding = new Thickness(5);
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        this.Res(BackgroundProperty, "Brush.WindowBackground").Res(BorderBrushProperty, "Brush.BorderStrong");
        Avalonia.Automation.AutomationProperties.SetName(this, AppStrings.Bars_PreviewName);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_settings is not null)
        {
            _settings.Changed += OnSettingsChanged;
        }

        Build();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_settings is not null)
        {
            _settings.Changed -= OnSettingsChanged;
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Dispatcher.UIThread.Post(Build);

    public void Refresh() => Build();

    internal List<string> Shown { get; } = [];

    private void Build()
    {
        var layout = _layout();
        var bars = layout.Bars;
        Shown.Clear();
        var window = new DockPanel { LastChildFill = true };

        void Top(Control c, string name)
        {
            DockPanel.SetDock(c, Dock.Top);
            window.Children.Add(c);
            Shown.Add(name);
        }

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(3, 2), Height = 7 };
        title.Children.Add(Mark(6, accent: true, opacity: 1));
        if (!bars.MenuBar)
        {
            title.Children.Add(Mark(4, accent: true, opacity: 0.35));
        }
        else if (layout.MenuRow == MenuRow.InTitleBar)
        {
            for (var i = 0; i < 6; i++)
            {
                title.Children.Add(Mark(10, accent: true, opacity: 0.55));
            }
        }

        Top(new Border { Child = title, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 0, 3) }.Res(BackgroundProperty, "Brush.Pressed"), "title");
        if (bars.MenuBar && layout.MenuRow == MenuRow.OwnRow)
        {
            var menu = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(3, 0, 0, 3), Height = 6 };
            for (var i = 0; i < 6; i++)
            {
                menu.Children.Add(Mark(10, accent: true, opacity: 0.55));
            }

            Top(menu, "menu");
        }

        var drivesShared = bars.DriveStrip && layout.Strip == StripMode.Shared;
        var arrangement = layout.Blocks.Arrange(b => b switch { BarBlock.HotPaths => bars.HotPathBar, BarBlock.UserMenu => bars.UserMenuBar, _ => layout.BackForward.InBar }, drivesShared);
        static string Name(BarBlock b) => b switch { BarBlock.HotPaths => "hot paths bar", BarBlock.UserMenu => "user menu bar", _ => "back and forward bar" };
        static double Opacity(BarBlock b) => b switch { BarBlock.HotPaths => 0.4, BarBlock.UserMenu => 0.3, _ => 0.25 };
        Control Row(IEnumerable<(double Opacity, bool Fill)> parts)
        {
            var grid = new Grid { ColumnSpacing = 3, Margin = new Thickness(0, 0, 0, 3) };
            foreach (var (opacity, fill) in parts)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(fill ? 2 : 1, GridUnitType.Star));
                var part = Bar(6, opacity, default);
                Grid.SetColumn(part, grid.ColumnDefinitions.Count - 1);
                grid.Children.Add(part);
            }

            return grid;
        }

        foreach (var row in arrangement.Rows)
        {
            Top(Row(row.Select(b => (Opacity(b), b == BarBlock.HotPaths))), string.Join(" + ", row.Select(Name)));
        }

        if (drivesShared)
        {
            Top(Row([(0.55, true), .. arrangement.WithDrives.Select(b => (Opacity(b), false))]), string.Join(" + ", ["shared strip", .. arrangement.WithDrives.Select(Name)]));
        }

        if (bars.FunctionKeyBar)
        {
            var fkeys = Bar(7, 0.4, new Thickness(0, 3, 0, 0));
            DockPanel.SetDock(fkeys, Dock.Bottom);
            window.Children.Add(fkeys);
        }

        var panes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Height = 120 };
        if (bars.MiddleToolbar)
        {
            var middle = new StackPanel { Spacing = 3, Margin = new Thickness(1, 8, 1, 0), Width = 6 };
            for (var i = 0; i < 8; i++)
            {
                middle.Children.Add(new Border { Height = 5, CornerRadius = new CornerRadius(1), Opacity = 0.45 }.Res(BackgroundProperty, "Brush.Accent"));
            }

            Grid.SetColumn(middle, 1);
            panes.Children.Add(middle);
            Shown.Add("middle toolbar");
        }

        for (var p = 0; p < 2; p++)
        {
            var pane = new DockPanel { Margin = new Thickness(p == 0 ? 0 : 2, 0, p == 0 ? 2 : 0, 0) };
            var paneBorder = new Border
            {
                Child = pane,
                Padding = new Thickness(2),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = pane.Margin,
            }.Res(BackgroundProperty, "Brush.PanelBackground").Res(BorderBrushProperty, "Brush.Border");
            pane.Margin = default;
            Grid.SetColumn(paneBorder, p == 0 ? 0 : 2);

            void PaneBar(bool on, double height, double opacity, Dock dock)
            {
                if (on)
                {
                    var bar = Bar(height, opacity, dock == Dock.Top ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0));
                    DockPanel.SetDock(bar, dock);
                    pane.Children.Add(bar);
                }
            }

            PaneBar(bars.DriveStrip && layout.Strip == StripMode.PerPanel, 6, 0.55, Dock.Top);
            PaneBar(bars.DirectoryLine, 5, 0.35, Dock.Top);
            PaneBar(bars.ColumnHeader, 4, 0.2, Dock.Top);
            PaneBar(bars.InformationLine, 4, 0.2, Dock.Bottom);
            pane.Children.Add(new Border());
            panes.Children.Add(paneBorder);
        }

        if (bars.DriveStrip && layout.Strip == StripMode.PerPanel)
        {
            Shown.Add("panel strips");
        }

        if (bars.DirectoryLine)
        {
            Shown.Add("directory lines");
        }

        if (bars.ColumnHeader)
        {
            Shown.Add("column headers");
        }

        if (bars.InformationLine)
        {
            Shown.Add("information lines");
        }

        if (bars.FunctionKeyBar)
        {
            Shown.Add("function keys");
        }

        window.Children.Add(panes);
        Child = window;
    }

    private static Border Mark(double width, bool accent, double opacity)
        => new Border { Width = width, CornerRadius = new CornerRadius(1), Opacity = opacity }.Res(BackgroundProperty, accent ? "Brush.Accent" : "Brush.Pressed");

    private static Border Bar(double height, double opacity, Thickness margin)
        => new Border { Height = height, CornerRadius = new CornerRadius(2), Opacity = opacity, Margin = margin }.Res(BackgroundProperty, "Brush.Accent");
}
