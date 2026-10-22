// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/SaveSearchDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

public sealed class SaveSearchDialog : Window
{
    private readonly TextBox _name;
    private readonly TextBlock _error;
    private readonly RadioButton? _folders;

    public SaveSearchDialog(string name, string lookIn, string keeps)
        : this(name, rename: false)
    {
        _folders = new RadioButton { GroupName = "where", IsChecked = lookIn.Length > 0, IsEnabled = lookIn.Length > 0 };
        _folders.Content = new StackPanel { Children = { new TextBlock { Text = AppStrings.Find_SaveLookIn }, OptionsUi.Text(lookIn, 12, brush: "Brush.TertiaryText") } };
        ActivePanel = new RadioButton { GroupName = "where", IsChecked = lookIn.Length == 0, Content = AppStrings.Find_SaveActive };
        var where = new StackPanel { Spacing = 4, Children = { OptionsUi.Text(AppStrings.Find_SaveWhere, 13, FontWeight.SemiBold), _folders, ActivePanel } };
        var keepsLine = OptionsUi.Text(Loc.Format(AppStrings.Find_SaveKeeps, keeps), 12, brush: "Brush.TertiaryText", wrap: true);
        Body.Children.Add(where);
        Body.Children.Add(keepsLine);
    }

    public SaveSearchDialog(string name)
        : this(name, rename: true)
    {
    }

    private SaveSearchDialog(string name, bool rename)
    {
        Title = rename ? AppStrings.Find_RenameTitle : AppStrings.Find_SaveTitle;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _name = new TextBox { Text = name, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(_name, AppStrings.Common_Name);
        _error = OptionsUi.Text(AppStrings.Find_SaveNameEmpty, 12, brush: "Brush.Danger", wrap: true);
        _error.IsVisible = false;
        _name.TextChanged += (_, _) => _error.IsVisible = false;

        var ok = new Button { Content = rename ? AppStrings.Find_SavedRename : AppStrings.Common_Save, IsDefault = true, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        Body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 16, 20, 16) };
        Body.Children.Add(new StackPanel { Spacing = 5, Children = { OptionsUi.Text(AppStrings.Common_Name, 13, FontWeight.SemiBold), _name, _error } });

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(Body);
        Content = Controls.TitleBarParts.ForDialog(this, root);
        Opened += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    private StackPanel Body { get; }

    internal TextBox NameBox => _name;

    internal RadioButton? FoldersChoice => _folders;

    internal RadioButton? ActivePanel { get; }

    public bool KeepsFolders => _folders?.IsChecked == true;

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
        if (name.Length == 0)
        {
            _error.IsVisible = true;
            return;
        }

        Close(name);
    }
}
