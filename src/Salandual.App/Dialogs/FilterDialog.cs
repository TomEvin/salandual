// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/FilterDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Find;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.UserMenu;

namespace Salandual.App.Dialogs;

public sealed record FilterChoice(PanelFilter? Filter, bool HideDotFiles, bool Subfolders = false);

public sealed class FilterDialog : Window
{
    private readonly RadioButton _off;
    private readonly RadioButton _on;
    private readonly ComboBox _mask;
    private readonly TextBlock _error;
    private readonly CheckBox _folders;
    private readonly CheckBox _dotFiles;
    private readonly CheckBox _subfolders;

    public FilterDialog(string side, PanelFilter? current, IReadOnlyList<string> history, bool hideDotFiles, bool flat = false)
    {
        Title = Loc.Format(AppStrings.Filter_Title, side);
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");
        Styles.Add(FindStyles.Create());

        _off = new RadioButton { Content = AppStrings.Filter_None, GroupName = "filter", IsChecked = current is null };
        _on = new RadioButton { Content = AppStrings.Filter_Show, GroupName = "filter", IsChecked = current is not null };
        _mask = new ComboBox
        {
            IsEditable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxDropDownHeight = 300,
            Margin = new Thickness(26, 0, 0, 0),
            Text = current?.Mask.Text ?? (history.Count > 0 ? history[0] : "*.*"),
        };
        _mask.Bind(ComboBox.FontFamilyProperty, _mask.GetResourceObservable("Font.Mono"));
        foreach (var item in history.Where(h => !h.StartsWith(FileMask.RegexPrefix, StringComparison.Ordinal)))
        {
            _mask.Items.Add(item);
        }

        Avalonia.Automation.AutomationProperties.SetName(_mask, AppStrings.Filter_Masks);
        _mask.SelectionChanged += (_, _) =>
        {
            if (_mask.SelectedItem is string picked)
            {
                Dispatcher.UIThread.Post(() => _mask.Text = picked);
            }
        };
        _mask.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.TextProperty && IsVisible && !string.IsNullOrEmpty(_mask.Text))
            {
                _on.IsChecked = true;
            }
        };

        _error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _error.IsVisible = false;
        _error.Margin = new Thickness(26, 0, 0, 0);
        var hint = OptionsUi.Secondary(AppStrings.Filter_Hint);
        hint.Margin = new Thickness(26, 0, 0, 0);

        var picks = new WrapPanel { Margin = new Thickness(26, 2, 0, 0) };
        foreach (var (label, mask) in QuickPicks())
        {
            var pick = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6) };
            pick.Classes.Add("chip");
            ToolTip.SetTip(pick, mask);
            pick.Click += (_, _) =>
            {
                _on.IsChecked = true;
                _mask.Text = mask;
                _error.IsVisible = false;
            };
            picks.Children.Add(pick);
        }

        _subfolders = new CheckBox { Content = AppStrings.Filter_Subfolders, IsChecked = flat, Margin = new Thickness(0, 4, 0, 0) };
        var subfoldersHint = Indented(OptionsUi.Secondary(AppStrings.Filter_Subfolders_Desc));
        _folders = new CheckBox { Content = AppStrings.Filter_Folders, IsChecked = current?.Folders == true };
        _dotFiles = new CheckBox { Content = AppStrings.Filter_DotFiles, IsChecked = hideDotFiles };
        AdvancedPanel = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(4, 2, 0, 0),
            IsVisible = current?.Folders == true,
            Children =
            {
                _folders,
                Indented(OptionsUi.Secondary(AppStrings.Filter_Folders_Desc)),
                _dotFiles,
                Indented(OptionsUi.Secondary(AppStrings.Filter_DotFiles_Desc)),
            },
        };
        AdvancedButton = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4, 3), Margin = new Thickness(-4, 4, 0, 0) };
        void ShowAdvanced() => AdvancedButton.Content = (AdvancedPanel.IsVisible ? "▾  " : "▸  ") + AppStrings.Filter_Advanced;
        AdvancedButton.Click += (_, _) =>
        {
            AdvancedPanel.IsVisible = !AdvancedPanel.IsVisible;
            ShowAdvanced();
        };
        ShowAdvanced();

        var ok = new Button { Content = AppStrings.Common_OK, IsDefault = true, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
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

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 18, 20, 16) };
        body.Children.Add(_off);
        body.Children.Add(_on);
        body.Children.Add(_mask);
        body.Children.Add(_error);
        body.Children.Add(hint);
        body.Children.Add(picks);
        body.Children.Add(_subfolders);
        body.Children.Add(subfoldersHint);
        body.Children.Add(AdvancedButton);
        body.Children.Add(AdvancedPanel);

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

    private static Control Indented(Control control)
    {
        control.Margin = new Thickness(28, -4, 0, 2);
        return control;
    }

    internal static IReadOnlyList<(string Label, string Mask)> QuickPicks() =>
    [
        (AppStrings.Filter_Pick_Pictures, UserMenuFit.MaskOf(UserMenuFileKind.Images)),
        (AppStrings.Filter_Pick_Videos, UserMenuFit.MaskOf(UserMenuFileKind.Video)),
        (AppStrings.Filter_Pick_Music, UserMenuFit.MaskOf(UserMenuFileKind.Audio)),
        (AppStrings.Filter_Pick_Documents, "*.pdf;*.doc;*.docx;*.odt;*.rtf;*.txt;*.md;*.xls;*.xlsx;*.ods;*.csv;*.ppt;*.pptx;*.odp;*.epub"),
        (AppStrings.Filter_Pick_Archives, UserMenuFit.MaskOf(UserMenuFileKind.Archives)),
        (AppStrings.Filter_Pick_Programs, UserMenuFit.MaskOf(UserMenuFileKind.Programs)),
    ];

    public FilterChoice? Result { get; private set; }

    internal ComboBox MaskBox => _mask;

    internal Button AdvancedButton { get; }

    internal StackPanel AdvancedPanel { get; }

    internal RadioButton NoFilter => _off;

    internal RadioButton UseFilter => _on;

    internal CheckBox FoldersBox => _folders;

    internal CheckBox DotFilesBox => _dotFiles;

    internal CheckBox SubfoldersBox => _subfolders;

    internal TextBlock ErrorText => _error;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _mask.IsDropDownOpen)
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
        PanelFilter? filter = null;
        if (_on.IsChecked == true)
        {
            if (FileMask.Parse(_mask.Text ?? _mask.SelectedItem as string) is not { } mask)
            {
                _error.Text = AppStrings.Mask_TypeMask;
                _error.IsVisible = true;
                return;
            }

            filter = new PanelFilter(mask, _folders.IsChecked == true);
        }

        Result = new FilterChoice(filter, _dotFiles.IsChecked == true, _subfolders.IsChecked == true);
        Close(Result);
    }
}
