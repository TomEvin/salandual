// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/PutBackDialog.cs

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

internal sealed record PutBackAnswer(bool PutBack, bool Always);

internal sealed class PutBackDialog : Window
{
    private readonly CheckBox _always;
    private readonly bool _enterPutsBack;

    public PutBackDialog(string fileName, string archiveName)
    {
        Title = AppStrings.PutBack_Title;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _always = new CheckBox
        {
            FontSize = 13,
            Content = new StackPanel { Spacing = 1, Children = { OptionsUi.Text(AppStrings.PutBack_Always, 13), OptionsUi.Text(AppStrings.PutBack_AlwaysNote, 12, brush: "Brush.TertiaryText", wrap: true).Also(t => t.MaxWidth = 420) } },
        };
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 8, 20, 16) };
        body.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.PutBack_Heading, fileName, archiveName), 15, FontWeight.SemiBold, wrap: true));
        body.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.PutBack_Text, archiveName), 13, brush: "Brush.SecondaryText", wrap: true));
        body.Children.Add(_always.Also(c => c.Margin = new Thickness(0, 4, 0, 0)));

        _enterPutsBack = App.EnterActs(Core.Settings.ConfirmGroup.Create);
        PutBack = new Button { Content = _enterPutsBack ? AppStrings.PutBack_PutBackEnter : AppStrings.PutBack_PutBack, Padding = new Thickness(14, 5) };
        PutBack.Click += (_, _) => Answer(true);
        Keep = new Button { Content = _enterPutsBack ? AppStrings.PutBack_Keep : AppStrings.PutBack_KeepEnter, Padding = new Thickness(14, 5) };
        Keep.Click += (_, _) => Answer(false);
        (_enterPutsBack ? PutBack : Keep).Classes.Add("accent");
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { Keep, PutBack } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        DialogButtons.ArrowsAcross(this, () => [Keep, PutBack]);
        Opened += (_, _) => DialogButtons.FocusShown(_enterPutsBack ? PutBack : Keep);
    }

    internal Button PutBack { get; }

    internal Button Keep { get; }

    internal CheckBox AlwaysBox => _always;

    internal void Answer(bool putBack) => Close(new PutBackAnswer(putBack, putBack && _always.IsChecked == true));

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
        else if (e.Key == Key.Enter && e.Source is not Button)
        {
            Answer(_enterPutsBack);
            e.Handled = true;
        }
    }
}
