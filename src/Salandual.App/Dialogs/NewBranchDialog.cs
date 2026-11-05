// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/NewBranchDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Options;
using Salandual.App.Controls;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Git;

namespace Salandual.App.Dialogs;

public sealed class NewBranchDialog : Window
{
    private readonly HashSet<string> _taken;
    private readonly Func<string, bool, Task<GitRun>> _make;
    private readonly TextBox _name;
    private readonly TextBlock _error;
    private readonly Button _create;
    private bool _busy;

    public NewBranchDialog(string repository, string from, IEnumerable<string> taken, Func<string, bool, Task<GitRun>> make)
    {
        _taken = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        _make = make;
        Title = Loc.Format(AppStrings.Git_NewBranch_Title, repository);
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _name = new TextBox { MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(_name, AppStrings.Common_Name);
        _name.TextChanged += (_, _) => Validate(showEmpty: false);
        _error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _error.IsVisible = false;
        var hint = OptionsUi.Text(Loc.Format(AppStrings.Git_NewBranch_From, from), 12, brush: "Brush.SecondaryText", wrap: true);
        SwitchBox = new CheckBox { Content = AppStrings.Git_NewBranch_Switch, IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 6, 20, 16) };
        body.Children.Add(new StackPanel { Spacing = 5, Children = { OptionsUi.Text(AppStrings.Common_Name, 13, FontWeight.SemiBold), _name, _error, hint } });
        body.Children.Add(SwitchBox);

        _create = new Button { Content = AppStrings.Git_NewBranch_Create, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        _create.Classes.Add("accent");
        _create.Click += async (_, _) => await AcceptAsync();
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 84, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, _create } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) => _name.Focus();
    }

    internal TextBox NameBox => _name;

    internal CheckBox SwitchBox { get; }

    internal TextBlock ErrorText => _error;

    internal Button CreateButton => _create;

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
            _ = AcceptAsync();
            e.Handled = true;
        }
    }

    private bool Validate(bool showEmpty)
    {
        var name = (_name.Text ?? "").Trim();
        var problem = name.Length == 0 ? (showEmpty ? AppStrings.Git_NewBranch_Empty : null)
            : !GitNames.IsValidBranch(name) ? AppStrings.Git_NewBranch_NotAllowed
            : _taken.Contains(name) ? Loc.Format(AppStrings.Git_NewBranch_Taken, name)
            : null;
        ShowError(problem);
        return problem is null && name.Length > 0;
    }

    private void ShowError(string? problem)
    {
        _error.Text = problem ?? "";
        _error.IsVisible = problem is not null;
    }

    internal async Task AcceptAsync()
    {
        if (_busy || !Validate(showEmpty: true))
        {
            return;
        }

        _busy = true;
        _create.IsEnabled = false;
        var name = (_name.Text ?? "").Trim();
        try
        {
            var run = await _make(name, SwitchBox.IsChecked == true);
            if (!run.Succeeded)
            {
                ShowError(Loc.Format(AppStrings.Git_Refused, run.Message));
                return;
            }

            Close(name);
        }
        finally
        {
            _busy = false;
            _create.IsEnabled = true;
        }
    }
}
