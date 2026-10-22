// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/EditNewFileDialog.cs

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

internal sealed class EditNewFileDialog : Window
{
    private readonly TextBox _name;
    private readonly TextBlock _error;

    public EditNewFileDialog(string folder, string suggested)
    {
        Title = AppStrings.EditNew_Title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _name = new TextBox { Text = suggested, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(_name, AppStrings.Common_Name);
        _name.TextChanged += (_, _) => ShowError(null);
        _error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _error.IsVisible = false;
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 8, 20, 16) };
        body.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.EditNew_In, folder), 13, brush: "Brush.SecondaryText", wrap: true));
        body.Children.Add(new StackPanel { Spacing = 5, Children = { OptionsUi.Text(AppStrings.Common_Name, 13, FontWeight.SemiBold), _name, _error } });
        body.Children.Add(OptionsUi.Text(AppStrings.EditNew_Hint, 12, brush: "Brush.SecondaryText", wrap: true));

        Create = new Button { Content = WithKey(AppStrings.EditNew_Create, "Enter"), Padding = new Thickness(14, 5) };
        Create.Classes.Add("accent");
        Create.Click += (_, _) => Accept();
        var cancel = new Button { Content = WithKey(AppStrings.Common_Cancel, "Esc"), Padding = new Thickness(14, 5) };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { Create, cancel } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) =>
        {
            _name.Focus();
            var dot = suggested.LastIndexOf('.');
            _name.SelectionStart = 0;
            _name.SelectionEnd = dot > 0 ? dot : suggested.Length;
        };
    }

    internal TextBox NameBox => _name;

    internal TextBlock ErrorText => _error;

    internal Button Create { get; }

    private static StackPanel WithKey(string label, string key) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children = { new TextBlock { Text = label }, new TextBlock { Text = key, FontSize = 11, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center } },
    };

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

    internal void Accept()
    {
        var name = (_name.Text ?? "").Trim();
        var problem = name.Length == 0 ? Strings.Name_Empty : FileNames.Problem(name);
        if (problem is not null)
        {
            ShowError(problem);
            return;
        }

        Close(name);
    }

    private void ShowError(string? problem)
    {
        _error.Text = problem ?? "";
        _error.IsVisible = problem is not null;
    }
}
