// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/HotPathEditDialog.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Dialogs;

public sealed class HotPathEditDialog : Window
{
    private readonly TextBox _name;
    private readonly TextBox _path;
    private readonly ComboBox _slot;
    private readonly CheckBox _strip;
    private readonly CheckBox _menu;
    private readonly TextBlock _error;

    public HotPathEditDialog(HotPath hotPath)
    {
        Title = Loc.Format(AppStrings.HotEdit_Title, hotPath.Slot);
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _name = new TextBox { Text = hotPath.Name };
        _path = new TextBox { Text = hotPath.Path };
        _path.Bind(TextBox.FontFamilyProperty, _path.GetResourceObservable("Font.Mono"));
        var browse = new Button { Content = AppStrings.Common_Browse, Padding = new Thickness(12, 4), VerticalAlignment = VerticalAlignment.Stretch };
        browse.Click += async (_, _) => await BrowseAsync();
        var pathRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        browse.Margin = new Thickness(8, 0, 0, 0);
        pathRow.Children.Add(browse);
        pathRow.Children.Add(_path);

        _slot = new ComboBox { MinWidth = 160 };
        for (var slot = 1; slot <= HotPathSettings.SlotCount; slot++)
        {
            var key = HotPathSettings.KeyOf(slot) is { } k ? $" · {k}" : "";
            _slot.Items.Add(new ComboBoxItem { Content = slot.ToString(CultureInfo.InvariantCulture) + key, Tag = slot });
        }

        _slot.SelectedIndex = hotPath.Slot - 1;
        _strip = new CheckBox { Content = AppStrings.HotEdit_InBar, IsChecked = hotPath.InStrip };
        _menu = new CheckBox { Content = AppStrings.HotEdit_InMenu, IsChecked = hotPath.InMenu };
        _error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _error.IsVisible = false;

        Avalonia.Automation.AutomationProperties.SetName(_name, AppStrings.Common_Name);
        Avalonia.Automation.AutomationProperties.SetName(_path, AppStrings.HotTable_Folder);
        Avalonia.Automation.AutomationProperties.SetName(_slot, AppStrings.HotEdit_Slot);

        var body = new StackPanel { Spacing = 6, Margin = new Thickness(20, 16, 20, 16) };
        body.Children.Add(Label(AppStrings.Common_Name));
        body.Children.Add(_name);
        body.Children.Add(Label(AppStrings.HotTable_Folder, top: 8));
        body.Children.Add(pathRow);
        body.Children.Add(OptionsUi.Secondary(AppStrings.HotEdit_EnvHint));
        body.Children.Add(_error);
        body.Children.Add(Label(AppStrings.HotEdit_Slot, top: 8));
        body.Children.Add(_slot);
        _strip.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(_strip);
        body.Children.Add(_menu);

        var save = new Button { Content = AppStrings.Common_Save, IsDefault = true, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        save.Classes.Add("accent");
        save.Click += (_, _) => Accept();
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { save, cancel } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = Controls.TitleBarParts.ForDialog(this, root);

        Opened += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    internal TextBox NameBox => _name;

    internal TextBox PathBox => _path;

    internal ComboBox SlotBox => _slot;

    internal CheckBox StripBox => _strip;

    internal CheckBox MenuBox => _menu;

    private static TextBlock Label(string text, double top = 0)
    {
        var label = OptionsUi.Text(text, 12, FontWeight.SemiBold, "Brush.SecondaryText");
        label.Margin = new Thickness(0, top, 0, 0);
        return label;
    }

    private async Task BrowseAsync()
    {
        var start = await StorageProvider.TryGetFolderFromPathAsync(HotPathSettings.Expand(_path.Text ?? ""));
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = AppStrings.HotEdit_PickerTitle, SuggestedStartLocation = start });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            _path.Text = path;
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                _name.Text = HotPathSettings.DefaultName(path);
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _slot.IsDropDownOpen)
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
        var path = (_path.Text ?? "").Trim();
        if (path.Length == 0)
        {
            _error.Text = AppStrings.HotEdit_TypeFolder;
            _error.IsVisible = true;
            _path.Focus();
            return;
        }

        var name = string.IsNullOrWhiteSpace(_name.Text) ? HotPathSettings.DefaultName(HotPathSettings.Expand(path)) : _name.Text.Trim();
        var slot = _slot.SelectedItem is ComboBoxItem { Tag: int s } ? s : _slot.SelectedIndex + 1;
        Result = new HotPath(slot, name, path, _strip.IsChecked == true, _menu.IsChecked == true);
        Close(Result);
    }

    public HotPath? Result { get; private set; }
}
