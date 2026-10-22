// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/CompareDirectoriesDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Dialogs;

public sealed record CompareDirectoriesChoice(string Left, string Right, CompareDirectoriesOptions Options, bool SelectInPanels, bool ShowList, bool SaveDefaults);

public sealed class CompareDirectoriesDialog : Window
{
    private readonly string _panelLeft;
    private readonly string _panelRight;
    private readonly TextBox _leftBox;
    private readonly TextBox _rightBox;
    private readonly TextBlock _problem;
    private readonly TextBlock _showHelp;
    private readonly CheckBox _select;
    private readonly CheckBox _list;
    private readonly CheckBox _newer;
    private readonly CheckBox _bigger;
    private readonly CheckBox _attributes;
    private readonly CheckBox _content;
    private readonly CheckBox _twoSeconds;
    private readonly CheckBox _oneHour;
    private readonly CheckBox _foldersOneSide;
    private readonly CheckBox _subfolders;
    private readonly CheckBox _subfolderAttributes;
    private readonly CheckBox _leaveOutFiles;
    private readonly CheckBox _leaveOutFolders;
    private readonly TextBox _filesMask;
    private readonly TextBox _foldersMask;
    private readonly CheckBox _saveDefaults;
    private bool _selectWanted;
    private bool _listWanted;

    public CompareDirectoriesDialog(CompareSettings settings, string left, string right, string from, string panelLeft, string panelRight)
    {
        var o = settings.Directories;
        _panelLeft = panelLeft;
        _panelRight = panelRight;
        _selectWanted = settings.SelectInPanels;
        _listWanted = settings.ShowList;
        Title = AppStrings.Compare_Title;
        Width = 580;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _leftBox = PathBox(left, AppStrings.Compare_Left);
        _rightBox = PathBox(right, AppStrings.Compare_Right);
        var swap = new Button { Content = AppIcons.Glyph("Icon.SwapUpDown", 16, 16, 1.3), Padding = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(swap, AppStrings.Compare_SwapTip);
        Avalonia.Automation.AutomationProperties.SetName(swap, AppStrings.Compare_SwapTip);
        swap.Click += (_, _) => Swap();
        SwapButton = swap;
        var paths = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 6, RowSpacing = 6 };
        AddPathRow(paths, 0, AppStrings.Compare_Left, _leftBox);
        AddPathRow(paths, 1, AppStrings.Compare_Right, _rightBox);
        Grid.SetColumn(swap, 3);
        Grid.SetRowSpan(swap, 2);
        paths.Children.Add(swap);
        From = OptionsUi.Text(from, 12, brush: "Brush.SecondaryText", wrap: true);
        _problem = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _problem.IsVisible = false;

        _select = Check(AppStrings.Compare_SelectInPanels, settings.SelectInPanels);
        _list = Check(AppStrings.Compare_InList, settings.ShowList);
        _select.IsCheckedChanged += (_, _) => _selectWanted = _select.IsEnabled ? _select.IsChecked == true : _selectWanted;
        _list.IsCheckedChanged += (_, _) => _listWanted = _list.IsEnabled ? _list.IsChecked == true : _listWanted;
        _showHelp = OptionsUi.Text("", 12, brush: "Brush.TertiaryText", wrap: true);
        _newer = Check(AppStrings.Compare_Newer, o.Newer);
        _bigger = Check(AppStrings.Compare_Bigger, o.Bigger);
        _attributes = Check(AppStrings.Compare_Attributes, o.Attributes);
        _content = Check(AppStrings.Compare_Content, o.Content);
        _twoSeconds = Check(AppStrings.Compare_TwoSeconds, o.IgnoreTwoSeconds);
        _oneHour = Check(AppStrings.Compare_OneHour, o.IgnoreOneHour);
        _foldersOneSide = Check(AppStrings.Compare_FoldersOneSide, o.FoldersOnOneSide);
        _subfolders = Check(AppStrings.Compare_Subfolders, o.Subfolders);
        _subfolderAttributes = Check(AppStrings.Compare_SubfolderAttributes, o.SubfolderAttributes);
        _leaveOutFiles = Check(AppStrings.Compare_Files, o.LeaveOutFiles);
        _leaveOutFolders = Check(AppStrings.Compare_FoldersHeading, o.LeaveOutFolders);
        _leaveOutFiles.Width = _leaveOutFolders.Width = 90;
        _filesMask = Mask(o.LeaveOutFilesMask, AppStrings.Compare_FilesHint, _leaveOutFiles);
        _foldersMask = Mask(o.LeaveOutFoldersMask, AppStrings.Compare_FoldersHint, _leaveOutFolders);

        var body = new StackPanel { Spacing = 14, Margin = new Thickness(20, 16, 20, 18) };
        body.Children.Add(Section(AppStrings.Compare_PathsHeading, null, paths, From, _problem));
        body.Children.Add(Section(AppStrings.Compare_ShowHeading, null, Row(_select, _list), _showHelp));
        body.Children.Add(Section(AppStrings.Compare_SameNameHeading, AppStrings.Compare_SameNameHelp, Row(_newer, _bigger), Row(_attributes, _content)));
        body.Children.Add(Section(AppStrings.Compare_TimesHeading, AppStrings.Compare_TimesHelp, Row(_twoSeconds, _oneHour)));
        body.Children.Add(Section(AppStrings.Compare_FoldersHeading, null, Row(_foldersOneSide), Row(_subfolders, _subfolderAttributes)));
        body.Children.Add(Section(AppStrings.Compare_LeaveOut, null, MaskRow(_leaveOutFiles, _filesMask), MaskRow(_leaveOutFolders, _foldersMask)));

        _saveDefaults = new CheckBox { Content = AppStrings.Compare_SaveDefaults, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        Go = new Button { Content = WithKey(AppStrings.Compare_Go, "Enter"), MinWidth = 96, Padding = new Thickness(14, 5) };
        Go.Classes.Add("accent");
        Go.Click += (_, _) => Accept();
        var cancel = new Button { Content = WithKey(AppStrings.Common_Cancel, "Esc"), MinWidth = 96, Padding = new Thickness(14, 5) };
        cancel.Click += (_, _) => Close(null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, Go } };
        var bar = new DockPanel { Children = { buttons, _saveDefaults } };
        DockPanel.SetDock(buttons, Dock.Right);
        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = bar }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = Controls.TitleBarParts.ForDialog(this, root);
        _leftBox.TextChanged += (_, _) => UpdateShow();
        _rightBox.TextChanged += (_, _) => UpdateShow();
        UpdateShow();
        Opened += (_, _) => Go.Focus();
    }

    internal Button Go { get; }

    internal Button SwapButton { get; }

    internal TextBlock From { get; }

    internal TextBox LeftBox => _leftBox;

    internal TextBox RightBox => _rightBox;

    internal string Problem => _problem.IsVisible ? _problem.Text ?? "" : "";

    internal CheckBox SelectBox => _select;

    internal CheckBox ListBox => _list;

    internal CheckBox NewerBox => _newer;

    internal CheckBox BiggerBox => _bigger;

    internal CheckBox ContentBox => _content;

    internal CheckBox SubfoldersBox => _subfolders;

    internal CheckBox SubfolderAttributesBox => _subfolderAttributes;

    internal CheckBox LeaveOutFilesBox => _leaveOutFiles;

    internal TextBox FilesMaskBox => _filesMask;

    internal CheckBox SaveDefaultsBox => _saveDefaults;

    internal bool PanelsShowThem => SamePath(Clean(_leftBox.Text), _panelLeft) && SamePath(Clean(_rightBox.Text), _panelRight);

    private static string Clean(string? path) => (path ?? "").Trim().Trim('"');

    private static bool SamePath(string a, string b)
        => a.Length > 0 && b.Length > 0 && string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private TextBox PathBox(string text, string name)
    {
        var box = new TextBox { Text = text, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(box, name);
        return box;
    }

    private void AddPathRow(Grid grid, int row, string label, TextBox box)
    {
        var text = OptionsUi.Text(label, 12.5, brush: "Brush.SecondaryText");
        text.VerticalAlignment = VerticalAlignment.Center;
        var browse = new Button { Content = AppIcons.Glyph("Icon.Folder", 16, 16, 1.3), Padding = new Thickness(7, 0), VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(browse, AppStrings.Compare_ChooseFolder);
        Avalonia.Automation.AutomationProperties.SetName(browse, AppStrings.Compare_ChooseFolder);
        browse.Click += async (_, _) =>
        {
            var start = Directory.Exists(Clean(box.Text)) ? await StorageProvider.TryGetFolderFromPathAsync(Clean(box.Text)) : null;
            var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = AppStrings.Compare_ChooseFolder, SuggestedStartLocation = start });
            if (folders.Count > 0 && Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(folders[0]) is { } path)
            {
                box.Text = path;
            }
        };
        foreach (var (control, column) in new (Control, int)[] { (text, 0), (box, 1), (browse, 2) })
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }
    }

    internal void Swap() => (_leftBox.Text, _rightBox.Text) = (_rightBox.Text, _leftBox.Text);

    private void UpdateShow()
    {
        var panels = PanelsShowThem;
        _select.IsEnabled = _list.IsEnabled = panels;
        _select.IsChecked = panels && _selectWanted;
        _list.IsChecked = !panels || _listWanted;
        _showHelp.Text = panels ? AppStrings.Compare_ShowHelpPanels : AppStrings.Compare_ShowHelpList;
        _problem.IsVisible = false;
    }

    private static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value, MinWidth = 210 };

    private static StackPanel Row(params Control[] boxes)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.AddRange(boxes);
        return row;
    }

    private static TextBox Mask(string text, string hint, CheckBox box)
    {
        var mask = new TextBox { Text = text, PlaceholderText = hint, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = box.IsChecked == true };
        Avalonia.Automation.AutomationProperties.SetName(mask, hint);
        box.IsCheckedChanged += (_, _) => mask.IsEnabled = box.IsChecked == true;
        return mask;
    }

    private static DockPanel MaskRow(CheckBox box, TextBox mask)
    {
        var row = new DockPanel { Children = { box, mask } };
        DockPanel.SetDock(box, Dock.Left);
        return row;
    }

    private static StackPanel Section(string heading, string? help, params Control[] rows)
    {
        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(OptionsUi.Text(heading, 13, FontWeight.SemiBold));
        section.Children.AddRange(rows);
        if (help is not null)
        {
            section.Children.Add(OptionsUi.Text(help, 12, brush: "Brush.TertiaryText", wrap: true));
        }

        return section;
    }

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
        var left = Clean(_leftBox.Text);
        var right = Clean(_rightBox.Text);
        var missing = !Directory.Exists(left) ? left : !Directory.Exists(right) ? right : null;
        if (missing is not null || SamePath(left, right))
        {
            _problem.Text = missing is not null ? Loc.Format(AppStrings.Compare_NoFolder, missing) : AppStrings.Compare_SamePaths;
            _problem.IsVisible = true;
            return;
        }

        var options = new CompareDirectoriesOptions
        {
            Newer = _newer.IsChecked == true,
            Bigger = _bigger.IsChecked == true,
            Attributes = _attributes.IsChecked == true,
            Content = _content.IsChecked == true,
            IgnoreTwoSeconds = _twoSeconds.IsChecked == true,
            IgnoreOneHour = _oneHour.IsChecked == true,
            FoldersOnOneSide = _foldersOneSide.IsChecked == true,
            Subfolders = _subfolders.IsChecked == true,
            SubfolderAttributes = _subfolderAttributes.IsChecked == true,
            LeaveOutFiles = _leaveOutFiles.IsChecked == true,
            LeaveOutFilesMask = (_filesMask.Text ?? "").Trim(),
            LeaveOutFolders = _leaveOutFolders.IsChecked == true,
            LeaveOutFoldersMask = (_foldersMask.Text ?? "").Trim(),
        };
        var panels = PanelsShowThem;
        Close(new CompareDirectoriesChoice(left, right, options, panels ? _select.IsChecked == true : _selectWanted, panels ? _list.IsChecked == true : _listWanted, _saveDefaults.IsChecked == true));
    }
}
