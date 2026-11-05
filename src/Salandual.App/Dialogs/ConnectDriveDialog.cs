// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ConnectDriveDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Vfs;

namespace Salandual.App.Dialogs;

internal sealed record ConnectRequest(string Letter, string Folder, bool Reconnect, bool OtherUser);

internal sealed class ConnectDriveDialog : Window
{
    private readonly Func<ConnectRequest, Task<string?>> _connect;
    private bool _busy;

    public ConnectDriveDialog(IReadOnlyList<string> letters, string folder, Func<string?, CancellationToken, Task<IEnumerable<object>>> suggest, Func<ConnectRequest, Task<string?>> connect)
    {
        _connect = connect;
        Title = AppStrings.Net_ConnectTitle;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        Letter = new ComboBox { ItemsSource = letters, SelectedIndex = letters.Count > 0 ? 0 : -1, MinWidth = 90, MinHeight = 30 };
        Avalonia.Automation.AutomationProperties.SetName(Letter, AppStrings.Net_Drive);
        Folder = new AutoCompleteBox
        {
            Text = folder,
            PlaceholderText = AppStrings.Net_FolderHint,
            MinimumPrefixLength = 2,
            MinimumPopulateDelay = TimeSpan.FromMilliseconds(150),
            FilterMode = AutoCompleteFilterMode.StartsWith,
            AsyncPopulator = suggest,
            MinHeight = 30,
        };
        Avalonia.Automation.AutomationProperties.SetName(Folder, AppStrings.Net_Folder);
        Reconnect = new CheckBox { Content = AppStrings.Net_Reconnect, IsChecked = true };
        OtherUser = new CheckBox { Content = AppStrings.Net_OtherUser };
        Error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        Error.IsVisible = false;

        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 10 };
        void Field(int row, string label, Control control)
        {
            var text = OptionsUi.Text(label, 13);
            text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(text, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            fields.Children.Add(text);
            fields.Children.Add(control);
        }

        Field(0, AppStrings.Net_Drive, new StackPanel { Orientation = Orientation.Horizontal, Children = { Letter } });
        Field(1, AppStrings.Net_Folder, Folder);

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 12, 20, 16) };
        body.Children.Add(fields);
        body.Children.Add(new StackPanel { Spacing = 4, Margin = new Thickness(90, 0, 0, 0), Children = { Reconnect, OtherUser } });
        body.Children.Add(OptionsUi.Text(AppStrings.Net_NoPassword, 12, brush: "Brush.TertiaryText", wrap: true));
        body.Children.Add(Error);

        Ok = new Button { Content = AppStrings.Net_ConnectButton, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        Ok.Classes.Add("accent");
        Ok.Click += (_, _) => _ = AcceptAsync();
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
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
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Folder.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } input)
            {
                input.Focus();
                input.SelectionStart = input.SelectionEnd = input.Text?.Length ?? 0;
            }
            else
            {
                Folder.Focus();
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    internal ComboBox Letter { get; }

    internal AutoCompleteBox Folder { get; }

    internal CheckBox Reconnect { get; }

    internal CheckBox OtherUser { get; }

    internal TextBlock Error { get; }

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
            _ = AcceptAsync();
            e.Handled = true;
        }
    }

    internal async Task AcceptAsync()
    {
        if (_busy)
        {
            return;
        }

        var folder = (Folder.Text ?? "").Trim().TrimEnd('\\');
        string? problem;
        if (Letter.SelectedItem is not string letter)
        {
            problem = AppStrings.Net_NoLetter;
        }
        else if (ShellPlace.ShareOf(folder) is null)
        {
            problem = AppStrings.Net_NotAFolder;
        }
        else
        {
            _busy = true;
            Ok.IsEnabled = false;
            try
            {
                problem = await _connect(new ConnectRequest(letter, folder, Reconnect.IsChecked == true, OtherUser.IsChecked == true));
            }
            finally
            {
                _busy = false;
                Ok.IsEnabled = true;
            }

            if (problem is null)
            {
                Close(letter);
                return;
            }
        }

        Error.Text = problem;
        Error.IsVisible = problem.Length > 0;
    }
}
