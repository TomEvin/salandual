// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ConfirmDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;

namespace Salandual.App.Dialogs;

public sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string yes, string no)
    {
        Title = title;
        Message = message;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        Yes = new Button { Content = yes, Padding = new Thickness(14, 5) };
        Yes.Click += (_, _) => Close(true);
        No = new Button { Content = no, Padding = new Thickness(14, 5), IsDefault = true };
        No.Classes.Add("accent");
        No.Click += (_, _) => Close(false);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { Yes, No } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        var body = new StackPanel { Margin = new Thickness(20, 12, 20, 18), Children = { OptionsUi.Text(message, 13, wrap: true) } };
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) => No.Focus();
    }

    internal string Message { get; }

    internal Button Yes { get; }

    internal Button No { get; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }
}
