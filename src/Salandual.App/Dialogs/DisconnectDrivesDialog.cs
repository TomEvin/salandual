// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/DisconnectDrivesDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Vfs;

namespace Salandual.App.Dialogs;

internal sealed class DisconnectDrivesDialog : Window
{
    private readonly List<(CheckBox Box, string Letter)> _boxes = [];

    public DisconnectDrivesDialog(IReadOnlyList<NetworkDrive> drives, string? ticked, Func<NetworkDrive, int?> openFiles)
    {
        Title = AppStrings.Net_DisconnectTitle;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var list = new StackPanel { Spacing = 6 };
        foreach (var drive in drives)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            var remote = OptionsUi.Text(drive.Remote, 12, brush: "Brush.SecondaryText");
            remote.TextTrimming = TextTrimming.CharacterEllipsis;
            remote.VerticalAlignment = VerticalAlignment.Center;
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { remote } };
            if (openFiles(drive) is int open && open > 0)
            {
                line.Children.Add(OptionsUi.Text(Loc.Plural(open, AppStrings.Net_FilesOpen_One, AppStrings.Net_FilesOpen_Other), 12, brush: "Brush.Danger"));
            }

            var box = new CheckBox { Content = drive.Letter, IsChecked = string.Equals(drive.Letter, ticked, StringComparison.OrdinalIgnoreCase), MinWidth = 64 };
            Avalonia.Automation.AutomationProperties.SetName(box, drive.Letter + " " + drive.Remote);
            Grid.SetColumn(line, 1);
            row.Children.Add(box);
            row.Children.Add(line);
            list.Children.Add(row);
            _boxes.Add((box, drive.Letter));
        }

        var body = new StackPanel { Spacing = 12, Margin = new Thickness(20, 12, 20, 16) };
        body.Children.Add(list);
        body.Children.Add(OptionsUi.Text(AppStrings.Net_OpenFilesNote, 12, brush: "Brush.TertiaryText", wrap: true));

        Ok = new Button { Content = AppStrings.Net_DisconnectButton, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        Ok.Classes.Add("accent");
        Ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { Ok, cancel } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) => (_boxes.FirstOrDefault(b => b.Box.IsChecked == true).Box ?? _boxes.FirstOrDefault().Box)?.Focus();
    }

    internal IReadOnlyList<CheckBox> Boxes => [.. _boxes.Select(b => b.Box)];

    internal Button Ok { get; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Accept();
            e.Handled = true;
        }
    }

    internal void Accept() => Close(_boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Letter).ToList());
}
