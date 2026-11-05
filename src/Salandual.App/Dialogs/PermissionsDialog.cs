// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/PermissionsDialog.cs

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

internal sealed record PermissionsChoice(int Mode, PermissionsScope Scope);

internal sealed class PermissionsDialog : Window
{
    private readonly CheckBox[] _boxes = new CheckBox[9];
    private readonly ComboBox? _scope;
    private bool _syncing;

    public PermissionsDialog(IReadOnlyList<string> names, IReadOnlyList<string> permissions, bool anyFolder, string owner)
    {
        Title = names.Count == 1 ? Loc.Format(AppStrings.Perm_Title, names[0]) : Loc.Format(AppStrings.Perm_Title, Loc.Plural(names.Count, Strings.Pack_Items_One, Strings.Pack_Items_Other));
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var modes = permissions.Select(RemoteFileSystem.ModeOf).ToList();
        var start = modes.FirstOrDefault(m => m is not null) ?? 0b110_100_100;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70,70,70"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), RowSpacing = 4 };
        void Cell(Control c, int row, int column)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, column);
            grid.Children.Add(c);
        }

        string[] heads = [AppStrings.Perm_Read, AppStrings.Perm_Write, AppStrings.Perm_Run];
        for (var c = 0; c < 3; c++)
        {
            Cell(OptionsUi.Text(heads[c], 12, brush: "Brush.SecondaryText").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center), 0, c + 1);
        }

        string[] who = [AppStrings.Perm_Owner, AppStrings.Perm_Group, AppStrings.Perm_Others];
        for (var r = 0; r < 3; r++)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { OptionsUi.Text(who[r], 13) } };
            if (r == 0 && owner.Length > 0)
            {
                label.Children.Add(OptionsUi.Text(owner, 13, brush: "Brush.TertiaryText"));
            }

            Cell(label, r + 1, 0);
            for (var c = 0; c < 3; c++)
            {
                var bit = 8 - (r * 3 + c);
                var box = _boxes[r * 3 + c] = new CheckBox { IsChecked = (start & (1 << bit)) != 0, HorizontalAlignment = HorizontalAlignment.Center, MinHeight = 26 };
                Avalonia.Automation.AutomationProperties.SetName(box, who[r] + " " + heads[c]);
                box.IsCheckedChanged += (_, _) => FromBoxes();
                Cell(box, r + 1, c + 1);
            }
        }

        Number = new TextBox { Width = 70, MinHeight = 28, MaxLength = 3, VerticalContentAlignment = VerticalAlignment.Center, Text = Octal(start) };
        Number.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(Number, AppStrings.Perm_Number);
        Number.TextChanged += (_, _) => FromNumber();

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 12, 20, 16) };
        if (names.Count > 1)
        {
            body.Children.Add(OptionsUi.Text(string.Join(", ", names.Take(5)) + (names.Count > 5 ? " …" : ""), 12, brush: "Brush.SecondaryText", wrap: true));
        }

        body.Children.Add(grid);
        body.Children.Add(Row(AppStrings.Perm_Number, Number));
        if (anyFolder)
        {
            _scope = new ComboBox
            {
                ItemsSource = new[] { AppStrings.Perm_ScopeItself, AppStrings.Perm_ScopeFiles, AppStrings.Perm_ScopeFolders, AppStrings.Perm_ScopeAll },
                SelectedIndex = 0,
                MinWidth = 260,
            };
            body.Children.Add(Row(AppStrings.Perm_Folders, _scope));
        }

        if (modes.Distinct().Count() > 1)
        {
            body.Children.Add(OptionsUi.Text(AppStrings.Perm_Different, 12, brush: "Brush.TertiaryText", wrap: true));
        }

        Error = OptionsUi.Text(AppStrings.Perm_BadNumber, 12, brush: "Brush.Danger", wrap: true);
        Error.IsVisible = false;
        body.Children.Add(Error);

        Ok = new Button { Content = AppStrings.Common_OK, Padding = new Thickness(14, 5), MinWidth = 84, IsDefault = true };
        Ok.Classes.Add("accent");
        Ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = AppStrings.Common_Cancel, Padding = new Thickness(14, 5), MinWidth = 84 };
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
        Opened += (_, _) => _boxes[0].Focus();
    }

    internal TextBox Number { get; }

    internal TextBlock Error { get; }

    internal Button Ok { get; }

    internal ComboBox? Scope => _scope;

    internal IReadOnlyList<CheckBox> Boxes => _boxes;

    private static Control Row(string label, Control control)
    {
        var text = OptionsUi.Text(label, 13);
        text.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*") };
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        row.Children.Add(text);
        row.Children.Add(control);
        return row;
    }

    private static string Octal(int mode) => Convert.ToString(mode & 0x1FF, 8).PadLeft(3, '0');

    private int ModeOfBoxes()
    {
        var mode = 0;
        for (var i = 0; i < 9; i++)
        {
            mode |= _boxes[i].IsChecked == true ? 1 << (8 - i) : 0;
        }

        return mode;
    }

    private void FromBoxes()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        Number.Text = Octal(ModeOfBoxes());
        Error.IsVisible = false;
        _syncing = false;
    }

    private void FromNumber()
    {
        if (_syncing || ParseNumber(Number.Text) is not { } mode)
        {
            return;
        }

        _syncing = true;
        for (var i = 0; i < 9; i++)
        {
            _boxes[i].IsChecked = (mode & (1 << (8 - i))) != 0;
        }

        Error.IsVisible = false;
        _syncing = false;
    }

    internal static int? ParseNumber(string? text)
    {
        text = (text ?? "").Trim();
        return text.Length == 3 && text.All(c => c is >= '0' and <= '7') ? Convert.ToInt32(text, 8) : null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }

    internal void Accept()
    {
        if (ParseNumber(Number.Text) is not { } mode)
        {
            Error.IsVisible = true;
            return;
        }

        Close(new PermissionsChoice(mode, (PermissionsScope)Math.Max(0, _scope?.SelectedIndex ?? 0)));
    }
}
