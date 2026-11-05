// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/CloneDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Git;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

public sealed record CloneRequest(string Address, string Folder, bool OpenWhenDone);

public sealed class CloneDialog : Window
{
    private readonly string _offered;
    private readonly Func<string, string?> _folderProblem;
    private readonly TextBox _address;
    private readonly TextBox _folder;
    private readonly TextBlock _error;
    private string? _named;

    public CloneDialog(string offered, Func<string, string?> folderProblem)
    {
        _offered = offered;
        _folderProblem = folderProblem;
        Title = AppStrings.Git_Clone_Title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _address = new TextBox { MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center, PlaceholderText = AppStrings.Git_Clone_Example }.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(_address, AppStrings.Git_Clone_Address);
        _address.TextChanged += (_, _) => OnAddress();
        _folder = new TextBox { MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center, Text = offered }.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(_folder, AppStrings.Git_Clone_Into);
        _folder.TextChanged += (_, _) => ShowError(null);
        _error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _error.IsVisible = false;
        var note = OptionsUi.Text(AppStrings.Git_Clone_Note, 12, brush: "Brush.SecondaryText", wrap: true);
        OpenBox = new CheckBox { Content = AppStrings.Git_Clone_Open, IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 6, 20, 16) };
        body.Children.Add(new StackPanel { Spacing = 5, Children = { OptionsUi.Text(AppStrings.Git_Clone_Address, 13, FontWeight.SemiBold), _address } });
        body.Children.Add(new StackPanel { Spacing = 5, Children = { OptionsUi.Text(AppStrings.Git_Clone_Into, 13, FontWeight.SemiBold), _folder, _error } });
        body.Children.Add(note);
        body.Children.Add(OpenBox);

        CloneButton = new Button { Content = AppStrings.Git_Clone_Clone, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        CloneButton.Classes.Add("accent");
        CloneButton.Click += (_, _) => Accept();
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, CloneButton } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) => _address.Focus();
    }

    internal TextBox AddressBox => _address;

    internal TextBox FolderBox => _folder;

    internal CheckBox OpenBox { get; }

    internal TextBlock ErrorText => _error;

    internal Button CloneButton { get; }

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

    private void OnAddress()
    {
        ShowError(null);
        var folder = _folder.Text ?? "";
        var following = folder == _offered || (_named is not null && folder == _named);
        if (following && GitSync.FolderNameOf(_address.Text) is { } name)
        {
            _named = Path.Join(_offered, name);
            _folder.Text = _named;
        }
    }

    private void ShowError(string? problem)
    {
        _error.Text = problem ?? "";
        _error.IsVisible = problem is not null;
    }

    internal void Accept()
    {
        var address = (_address.Text ?? "").Trim();
        var folder = (_folder.Text ?? "").Trim();
        var problem = GitSync.CheckAddress(address) switch
        {
            GitAddressProblem.Empty => AppStrings.Git_Clone_NoAddress,
            GitAddressProblem.Password => AppStrings.Git_Clone_Password,
            GitAddressProblem.NotSupported => AppStrings.Git_Clone_NotSupported,
            _ => _folderProblem(folder),
        };
        if (problem is not null)
        {
            ShowError(problem);
            return;
        }

        Close(new CloneRequest(address, Path.TrimEndingDirectorySeparator(folder), OpenBox.IsChecked == true));
    }
}
