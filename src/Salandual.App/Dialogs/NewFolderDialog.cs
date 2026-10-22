// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/NewFolderDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Dialogs;

public sealed record NewFolderRequest(NewFolderPlan Plan, bool MoveSelection);

public sealed class NewFolderDialog : Window
{
    private readonly IOperationFileSystem _fs;
    private readonly string _folder;
    private readonly TextBox _name;
    private readonly TextBlock _error;
    private readonly Button _create;
    private readonly bool _confirmCreate;
    private bool _busy;

    public NewFolderDialog(IOperationFileSystem fs, string folder, int selected, bool moveByDefault, bool confirmCreate = false)
    {
        _confirmCreate = confirmCreate;
        _fs = fs;
        _folder = folder;
        Title = AppStrings.NewFolder_Title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var heading = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        heading.Res(TextBlock.ForegroundProperty, "Brush.Text");
        Rich.Add(heading.Inlines!, AppStrings.NewFolder_Heading, text =>
            {
                var run = new Run(text);
                run.Bind(Run.ForegroundProperty, heading.GetResourceObservable("Brush.SecondaryText"));
                return run;
            },
            new Run(AppStrings.NewFolder_Title) { FontWeight = FontWeight.SemiBold },
            new Run(WindowsPath.GetName(folder).TrimEnd('\\') is { Length: > 0 } n ? n : folder) { FontWeight = FontWeight.SemiBold });

        _name = new TextBox { MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(_name, AppStrings.Common_Name);
        _name.TextChanged += (_, _) => Validate(showEmpty: false);
        _error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _error.IsVisible = false;
        var hint = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        hint.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        var sample = new Run(AppStrings.Sample_NewFolderPath);
        sample.Bind(Run.FontFamilyProperty, hint.GetResourceObservable("Font.Mono"));
        Rich.Add(hint.Inlines!, AppStrings.NewFolder_PathHint, null, sample);

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 6, 20, 16) };
        body.Children.Add(heading);
        body.Children.Add(new StackPanel { Spacing = 5, Margin = new Thickness(0, 4, 0, 0), Children = { OptionsUi.Text(AppStrings.Common_Name, 13, FontWeight.SemiBold), _name, _error, hint } });
        if (selected > 0)
        {
            MoveBox = new CheckBox
            {
                Content = Loc.Plural(selected, AppStrings.NewFolder_MoveSelected_One, AppStrings.NewFolder_MoveSelected_Other),
                IsChecked = moveByDefault,
                Margin = new Thickness(0, 4, 0, 0),
            };
            body.Children.Add(MoveBox);
        }

        _create = new Button { Content = AppStrings.NewFolder_Create, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
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

    internal CheckBox? MoveBox { get; }

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
        var parts = (_name.Text ?? "").Trim().Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        string? problem = null;
        if (parts.Length == 0)
        {
            problem = showEmpty ? Strings.Name_Empty : null;
        }
        else if (!Path.IsPathFullyQualified((_name.Text ?? "").Trim()))
        {
            problem = parts.Select(p => FileNames.Problem(p) is { } x ? (parts.Length == 1 ? x : Loc.Format(Strings.Name_PartProblem, p, x)) : null).FirstOrDefault(x => x is not null);
        }

        ShowError(problem);
        return problem is null && parts.Length > 0;
    }

    private void ShowError(string? problem)
    {
        _error.Text = problem ?? "";
        _error.IsVisible = problem is not null;
    }

    internal ConfirmDialog? CreateQuestion { get; private set; }

    internal async Task AcceptAsync()
    {
        if (_busy || !Validate(showEmpty: true))
        {
            return;
        }

        _busy = true;
        _create.IsEnabled = false;
        var typed = _name.Text ?? "";
        try
        {
            var (made, why) = await Task.Run(() => (NewFolderPlan.Make(_fs, _folder, typed, out var w), w));
            if (made is null)
            {
                ShowError(why);
                return;
            }

            if (_confirmCreate && made.Missing.Count > 1)
            {
                var ask = CreateQuestion = new ConfirmDialog(AppStrings.CreateFolder_Title,
                    Loc.Format(AppStrings.CreateFolder_Many, string.Join(", ", made.Missing.Select(WindowsPath.GetName))), AppStrings.CreateFolder_Create, AppStrings.Common_Cancel);
                bool create;
                try
                {
                    create = await ask.ShowDialog<bool>(this);
                }
                finally
                {
                    CreateQuestion = null;
                }

                if (!create)
                {
                    return;
                }
            }

            var problem = await Task.Run(() =>
            {
                try
                {
                    made.Create(_fs);
                    return null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Loc.Format(AppStrings.NewFolder_CantCreate, ex.Message);
                }
            });

            if (problem is not null)
            {
                ShowError(problem);
                return;
            }

            Close(new NewFolderRequest(made, MoveBox?.IsChecked == true));
        }
        finally
        {
            _busy = false;
            _create.IsEnabled = true;
        }
    }
}
