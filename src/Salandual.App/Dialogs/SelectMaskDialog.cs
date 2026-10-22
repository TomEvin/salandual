// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/SelectMaskDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Find;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

public sealed class SelectMaskDialog : Window
{
    private readonly ComboBox _mask;
    private readonly TextBlock _error;
    private readonly Button _ok;
    private readonly TextBlock _help;
    private readonly ToggleButton _maskKind;
    private readonly ToggleButton _regexKind;
    private readonly Button _pieces;

    public SelectMaskDialog(bool select, IReadOnlyList<string> history, bool includesFolders)
    {
        Title = select ? AppStrings.Mask_Select : AppStrings.Mask_Unselect;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");
        Styles.Add(FindStyles.Create());
        var newest = history.Count > 0 ? history[0] : "*.*";

        _mask = new ComboBox
        {
            IsEditable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxDropDownHeight = 300,
            Text = Shown(newest),
        };
        _mask.Bind(ComboBox.FontFamilyProperty, _mask.GetResourceObservable("Font.Mono"));
        foreach (var item in history)
        {
            _mask.Items.Add(item);
        }

        Avalonia.Automation.AutomationProperties.SetName(_mask, AppStrings.Mask_Mask);
        _error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _error.IsVisible = false;

        _maskKind = Kind(AppStrings.Mask_KindMask, regex: false);
        _regexKind = Kind(AppStrings.Mask_KindRegex, regex: true);
        var kinds = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _maskKind, _regexKind } },
        }.Res(Border.BorderBrushProperty, "Brush.Border");
        _pieces = new Button { Content = ".* ▾", FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), Padding = new Thickness(8, 4), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(_pieces, AppStrings.Find_PiecesTip);
        _pieces.Flyout = RegexMenu.Create(() => _mask);
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8, Children = { _mask, kinds, _pieces } };
        Grid.SetColumn(kinds, 1);
        Grid.SetColumn(_pieces, 2);

        _mask.SelectionChanged += (_, _) =>
        {
            if (_mask.SelectedItem is string picked)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SetRegex(picked.StartsWith(FileMask.RegexPrefix, StringComparison.Ordinal));
                    _mask.Text = Shown(picked);
                });
            }
        };

        var heading = OptionsUi.Text(select
            ? includesFolders ? AppStrings.Mask_SelectFilesFolders : AppStrings.Mask_SelectFiles
            : includesFolders ? AppStrings.Mask_UnselectFilesFolders : AppStrings.Mask_UnselectFiles, 14, FontWeight.SemiBold);
        _help = OptionsUi.Secondary(AppStrings.Mask_Help);

        _ok = new Button { Content = select ? AppStrings.Mask_Select : AppStrings.Mask_Unselect, IsDefault = true, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        _ok.Classes.Add("accent");
        _ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_ok);
        buttons.Children.Add(cancel);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = buttons,
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 18, 20, 16) };
        body.Children.Add(heading);
        body.Children.Add(line);
        body.Children.Add(_error);
        body.Children.Add(_help);
        SetRegex(newest.StartsWith(FileMask.RegexPrefix, StringComparison.Ordinal));

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = Controls.TitleBarParts.ForDialog(this, root);

        Opened += (_, _) =>
        {
            _mask.Focus();
            if (_mask.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } box)
            {
                box.SelectAll();
            }
        };
    }

    public FileMask? Result { get; private set; }

    internal ComboBox MaskBox => _mask;

    internal bool IsRegex => _regexKind.IsChecked == true;

    internal ToggleButton RegexKind => _regexKind;

    internal ToggleButton MaskKind => _maskKind;

    internal Button PiecesButton => _pieces;

    internal TextBlock HelpText => _help;

    private static string Shown(string entry)
        => entry.StartsWith(FileMask.RegexPrefix, StringComparison.Ordinal) ? entry[FileMask.RegexPrefix.Length..] : entry;

    private ToggleButton Kind(string text, bool regex)
    {
        var button = new ToggleButton { Content = text, Padding = new Thickness(10, 3) };
        button.Classes.Add("seg");
        button.Click += (_, _) => SetRegex(regex);
        return button;
    }

    internal void SetRegex(bool regex)
    {
        _maskKind.IsChecked = !regex;
        _regexKind.IsChecked = regex;
        _pieces.IsVisible = regex;
        _help.Text = regex ? AppStrings.Mask_RegexHelp : AppStrings.Mask_Help;
        _error.IsVisible = false;
    }

    internal TextBlock ErrorText => _error;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && !_mask.IsDropDownOpen)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !_mask.IsDropDownOpen)
        {
            Accept();
            e.Handled = true;
        }
    }

    internal void Accept()
    {
        var text = _mask.Text ?? (_mask.SelectedItem as string);
        if (IsRegex)
        {
            if (FileMask.ParseRegex(text, out var problem) is not { } regex)
            {
                _error.Text = problem is null ? AppStrings.Mask_TypeRegex : Loc.Format(AppStrings.Mask_BadRegex, problem);
                _error.IsVisible = true;
                return;
            }

            Result = regex;
            Close(regex);
            return;
        }

        if (FileMask.Parse(text) is not { } mask)
        {
            _error.Text = AppStrings.Mask_TypeMask;
            _error.IsVisible = true;
            return;
        }

        Result = mask;
        Close(mask);
    }
}
