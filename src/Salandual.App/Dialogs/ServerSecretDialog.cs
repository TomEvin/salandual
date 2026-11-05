// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ServerSecretDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Remote;

namespace Salandual.App.Dialogs;

internal sealed class ServerSecretDialog : Window
{
    public ServerSecretDialog(ServerAddress server, SecretKind kind, bool wrongBefore, bool keptBefore)
    {
        Title = kind == SecretKind.Passphrase ? AppStrings.Server_PassphraseTitle : AppStrings.Server_PasswordTitle;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        Secret = new TextBox { PasswordChar = '●', MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center, Classes = { "revealPasswordButton" } };
        Avalonia.Automation.AutomationProperties.SetName(Secret, Title);
        Remember = new CheckBox { Content = kind == SecretKind.Passphrase ? AppStrings.Server_RememberPassphrase : AppStrings.Server_RememberPassword, IsChecked = keptBefore };
        var wrong = OptionsUi.Text(keptBefore ? AppStrings.Server_KeptWrong : kind == SecretKind.Passphrase ? AppStrings.Server_PassphraseWrongAgain : AppStrings.Server_PasswordWrong, 12, brush: "Brush.Danger", wrap: true);
        wrong.IsVisible = wrongBefore;
        WrongText = wrong;

        var who = server.User.Length > 0 ? server.User + "@" + server.Host : server.Host;
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 8, 20, 16) };
        body.Children.Add(OptionsUi.Text(Loc.Format(kind == SecretKind.Passphrase ? AppStrings.Server_PassphraseHeading : AppStrings.Server_PasswordHeading, who), 15, FontWeight.SemiBold, wrap: true));
        body.Children.Add(new StackPanel { Spacing = 5, Margin = new Thickness(0, 4, 0, 0), Children = { Secret, wrong } });
        body.Children.Add(Remember);
        body.Children.Add(OptionsUi.Text(AppStrings.Server_RememberNote, 12, brush: "Brush.TertiaryText", wrap: true));

        Ok = new Button { Content = AppStrings.Server_Connect, Padding = new Thickness(14, 5) };
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
        Opened += (_, _) => Secret.Focus();
    }

    internal TextBox Secret { get; }

    internal CheckBox Remember { get; }

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

    internal void Accept() => Close(new SecretAnswer(Secret.Text ?? "", Remember.IsChecked == true));
}
