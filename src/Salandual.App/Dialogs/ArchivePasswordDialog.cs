// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ArchivePasswordDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

internal sealed class ArchivePasswordDialog : Window
{
    private readonly TextBox _password;

    public ArchivePasswordDialog(string archiveName, bool wrongBefore)
    {
        Title = AppStrings.ArchivePassword_Title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _password = new TextBox { PasswordChar = '●', MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center, Classes = { "revealPasswordButton" } };
        Avalonia.Automation.AutomationProperties.SetName(_password, AppStrings.ArchivePassword_Title);
        var error = OptionsUi.Text(AppStrings.ArchivePassword_Wrong, 12, brush: "Brush.Danger", wrap: true);
        error.IsVisible = wrongBefore;
        WrongText = error;

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 8, 20, 16) };
        body.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.ArchivePassword_Heading, archiveName), 15, FontWeight.SemiBold, wrap: true));
        body.Children.Add(OptionsUi.Text(AppStrings.ArchivePassword_Kept, 13, brush: "Brush.SecondaryText", wrap: true));
        body.Children.Add(new StackPanel { Spacing = 5, Margin = new Thickness(0, 4, 0, 0), Children = { _password, error } });

        Ok = new Button { Content = AppStrings.Common_OK, Padding = new Thickness(14, 5) };
        Ok.Classes.Add("accent");
        Ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = AppStrings.Common_Cancel, Padding = new Thickness(14, 5) };
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
        Opened += (_, _) => _password.Focus();
    }

    internal TextBox PasswordBox => _password;

    internal TextBlock WrongText { get; }

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

    internal void Accept() => Close(_password.Text ?? "");
}
