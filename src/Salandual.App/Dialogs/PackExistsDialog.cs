// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/PackExistsDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

internal sealed class PackExistsDialog : Window
{
    private readonly RadioButton _add, _replace, _newName;
    private readonly bool _enterPacks;

    public PackExistsDialog(string archiveName, string facts, string newName, bool canAdd)
    {
        Title = AppStrings.Pack_Title;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var group = "PackExists" + GetHashCode();
        _add = Radio(group, AppStrings.PackExists_Add, canAdd ? AppStrings.PackExists_AddNote : AppStrings.PackExists_CantAdd);
        _add.IsEnabled = canAdd;
        _replace = Radio(group, AppStrings.PackExists_Replace, AppStrings.PackExists_ReplaceNote);
        _newName = Radio(group, Loc.Format(AppStrings.PackExists_NewName, newName), null);
        (canAdd ? _add : _replace).IsChecked = true;

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 8, 20, 16) };
        body.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.PackExists_Heading, archiveName), 15, FontWeight.SemiBold, wrap: true));
        body.Children.Add(OptionsUi.Text(facts, 13, brush: "Brush.SecondaryText", wrap: true));
        body.Children.Add(new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 0), Children = { _add, _replace, _newName } });
        body.Children.Add(OptionsUi.Text(AppStrings.PackExists_Setting, 12, brush: "Brush.TertiaryText", wrap: true).Also(t => t.Margin = new Thickness(0, 6, 0, 0)));

        Ok = new Button { Content = AppStrings.Pack_Title, Padding = new Thickness(14, 5), MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        Ok.Click += (_, _) => Accept();
        _enterPacks = App.EnterActs(Core.Settings.ConfirmGroup.Create);
        var cancel = CancelButton = new Button { Content = AppStrings.Common_Cancel, Padding = new Thickness(14, 5), MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        (_enterPacks ? Ok : cancel).Classes.Add("accent");
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, Ok } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) => ((canAdd ? _add : _replace) as Control).Focus();
    }

    internal Button Ok { get; }

    internal Button CancelButton { get; }

    internal RadioButton AddOption => _add;

    internal RadioButton ReplaceOption => _replace;

    internal RadioButton NewNameOption => _newName;

    internal void Accept() => Close(_add.IsChecked == true ? PackExisting.Add : _replace.IsChecked == true ? PackExisting.Replace : PackExisting.NewName);

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
            if (_enterPacks)
            {
                Accept();
            }
            else
            {
                Close(null);
            }

            e.Handled = true;
        }
    }

    private static RadioButton Radio(string group, string text, string? note)
        => new()
        {
            GroupName = group,
            FontSize = 13,
            MinHeight = 26,
            Content = note is null ? text : new StackPanel { Spacing = 1, Children = { OptionsUi.Text(text, 13), OptionsUi.Text(note, 12, brush: "Brush.TertiaryText", wrap: true).Also(t => t.MaxWidth = 420) } },
        };
}
