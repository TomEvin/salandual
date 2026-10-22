// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ChangeDirectoryDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Options;
using Salandual.App.Panels;
using Salandual.App.Resources;

namespace Salandual.App.Dialogs;

public sealed class ChangeDirectoryDialog : Window
{
    private readonly ChangeDirectoryBox _box = new();

    public ChangeDirectoryDialog(ChangeDirectoryContext context)
    {
        Title = AppStrings.Opt_ChangeDirectory;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var recent = new Button { Content = "▼", FontSize = 9, Padding = new Thickness(8, 0), Margin = new Thickness(0, 2, 2, 2), VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent, BorderThickness = default, Focusable = false };
        ToolTip.SetTip(recent, AppStrings.Cd_RecentFolders);
        recent.Click += (_, _) => _box.ShowRecent();
        _box.Input.InnerRightContent = recent;
        _box.Input.Height = 32;
        _box.BrowseButton.MinWidth = 96;
        _box.BrowseButton.HorizontalContentAlignment = HorizontalAlignment.Center;

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        row.Children.Add(_box.Input);
        Grid.SetColumn(_box.BrowseButton, 1);
        row.Children.Add(_box.BrowseButton);

        _box.List.BoxShadow = default;
        _box.List.CornerRadius = new CornerRadius(8);
        _box.List.Height = 268;
        _box.List.Margin = new Thickness(0, 6, 0, 0);

        var label = OptionsUi.Text(AppStrings.Cd_InputName, 13, FontWeight.SemiBold);
        var body = new StackPanel { Spacing = 6, Margin = new Thickness(20, 12, 20, 16) };
        body.Children.Add(label);
        body.Children.Add(row);
        body.Children.Add(_box.List);
        body.Children.Add(OptionsUi.Secondary(AppStrings.Cd_DialogHelp));

        var ok = new Button { Content = AppStrings.Common_OK, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => _box.Go(_box.Input.Text ?? "");
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = Controls.TitleBarParts.ForDialog(this, root);

        _box.Accepted += (_, target) => Close(target);
        _box.Cancelled += (_, _) => Close(null);
        _box.Open(context);
        Opened += (_, _) =>
        {
            _box.Input.Focus();
            _box.Input.SelectAll();
        };
    }

    internal ChangeDirectoryBox Box => _box;
}
