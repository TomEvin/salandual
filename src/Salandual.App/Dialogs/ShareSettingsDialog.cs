// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ShareSettingsDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Settings;

namespace Salandual.App.Dialogs;

public sealed class ShareSettingsDialog : Window
{
    internal static readonly (string Label, string? Hint, SettingsPart[] Parts)[] Groups =
    [
        (AppStrings.Share_G_Appearance, null, [SettingsPart.Appearance, SettingsPart.Themes]),
        (AppStrings.OptCat_Layout, null, [SettingsPart.Layout]),
        (AppStrings.OptCat_Panels, null, [SettingsPart.Panels]),
        (AppStrings.OptCat_Keyboard, null, [SettingsPart.Keyboard]),
        (AppStrings.Share_G_HotPaths, AppStrings.Share_G_HotPaths_Hint, [SettingsPart.HotPaths]),
        (AppStrings.Share_G_Operations, null, [SettingsPart.Operations, SettingsPart.Confirmations]),
        (AppStrings.Share_G_Viewers, null, [SettingsPart.Viewers, SettingsPart.Editors]),
        (AppStrings.Share_G_Compare, null, [SettingsPart.Compare]),
        (AppStrings.Share_G_General, null, [SettingsPart.General]),
        (AppStrings.Share_G_History, AppStrings.Share_G_History_Hint, [SettingsPart.History]),
        (AppStrings.Share_G_Programs, AppStrings.Share_G_Programs_Hint, [SettingsPart.Programs]),
    ];

    private readonly List<(CheckBox Box, SettingsPart[] Parts)> _ticks = [];
    private readonly List<(Button Chip, string Folder)> _chips = [];

    public ShareSettingsDialog(SharingSettings current, IReadOnlyList<CloudFolder> clouds)
    {
        Title = AppStrings.Share_Title;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.WindowBackground");

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 6, 20, 16) };
        body.Children.Add(OptionsUi.Secondary(AppStrings.Share_Lead, 13));

        Folder = new TextBox { Text = current.IsOn ? current.Folder : clouds.Count > 0 ? CloudFolders.SuggestedFolder(clouds[0]) : "", FontFamily = Salandual.App.Options.OptionsUi.MonoFamily, FontSize = 12 };
        var browse = new Button { Content = AppStrings.Common_Browse, Padding = new Thickness(14, 5) };
        browse.Click += async (_, _) =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = AppStrings.Share_PickFolder });
            if (picked is [var folder] && folder.TryGetLocalPath() is { } path)
            {
                Folder.Text = path;
            }
        };
        var folderRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Margin = new Thickness(8, 0, 0, 0);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(Folder);
        body.Children.Add(folderRow);

        var found = new WrapPanel { Orientation = Orientation.Horizontal };
        if (clouds.Count > 0)
        {
            found.Children.Add(OptionsUi.Secondary(AppStrings.Share_Found).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 6, 0); }));
            foreach (var cloud in clouds)
            {
                var folder = CloudFolders.SuggestedFolder(cloud);
                var chip = new Button { Content = cloud.Name, FontSize = 11.5, Padding = new Thickness(7, 1), Margin = new Thickness(0, 0, 4, 2), CornerRadius = Corners.Inner };
                ToolTip.SetTip(chip, folder);
                chip.Click += (_, _) => Folder.Text = folder;
                _chips.Add((chip, folder));
                found.Children.Add(chip);
            }
        }

        body.Children.Add(found);
        body.Children.Add(OptionsUi.Secondary(clouds.Count > 0 ? AppStrings.Share_Found_Note : AppStrings.Share_NoneFound_Note));

        body.Children.Add(OptionsUi.Text(AppStrings.Share_WhatGoes, 13, FontWeight.SemiBold).Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 18 };
        for (var i = 0; i < Groups.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var (label, hint, parts) = Groups[i];
            var text = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } } };
            if (hint is not null)
            {
                text.Children.Add(new TextBlock { Text = hint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText"));
            }

            var box = new CheckBox { Content = text, IsChecked = parts.All(current.Parts.Contains), MinHeight = 26 };
            Grid.SetRow(box, i / 2);
            Grid.SetColumn(box, i % 2);
            grid.Children.Add(box);
            _ticks.Add((box, parts));
        }

        body.Children.Add(grid);

        body.Children.Add(OptionsUi.Text(AppStrings.Share_WhenChanged, 13, FontWeight.SemiBold).Also(t => t.Margin = new Thickness(0, 6, 0, 0)));
        AtOnce = new RadioButton { Content = AppStrings.Share_AtOnce, GroupName = "share-when", IsChecked = !current.AskFirst };
        AskFirst = new RadioButton { Content = AppStrings.Share_AskFirst, GroupName = "share-when", IsChecked = current.AskFirst };
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { AtOnce, AskFirst } });

        body.Children.Add(new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = Corners.Chip,
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 4, 0, 0),
            Child = OptionsUi.Secondary(AppStrings.Share_Never),
        }.Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.PanelBackground"));

        Primary = new Button { Content = AppStrings.Share_Primary, Padding = new Thickness(16, 5) };
        Primary.Classes.Add("accent");
        Primary.Click += (_, _) => Close(Result);
        var cancel = new Button { Content = AppStrings.Common_Cancel, Padding = new Thickness(16, 5) };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, Primary } },
        }.Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);

        Folder.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Refresh();
            }
        };
        foreach (var (box, _) in _ticks)
        {
            box.IsCheckedChanged += (_, _) => Refresh();
        }

        Refresh();
        Opened += (_, _) => Primary.Focus();
    }

    internal TextBox Folder { get; }

    internal RadioButton AtOnce { get; }

    internal RadioButton AskFirst { get; }

    internal Button Primary { get; }

    internal IReadOnlyList<(CheckBox Box, SettingsPart[] Parts)> Ticks => _ticks;

    internal IReadOnlyList<(Button Chip, string Folder)> Chips => _chips;

    internal SharingSettings? Result
    {
        get
        {
            var folder = (Folder.Text ?? "").Trim();
            var parts = _ticks.Where(t => t.Box.IsChecked == true).SelectMany(t => t.Parts).ToList();
            return Path.IsPathFullyQualified(folder) && parts.Count > 0
                ? new SharingSettings { Folder = folder, Parts = parts, AskFirst = AskFirst.IsChecked == true }
                : null;
        }
    }

    private void Refresh()
    {
        Primary.IsEnabled = Result is not null;
        foreach (var (chip, folder) in _chips)
        {
            var picked = string.Equals((Folder.Text ?? "").Trim(), folder, StringComparison.OrdinalIgnoreCase);
            chip.Classes.Set("accent", picked);
        }
    }
}
