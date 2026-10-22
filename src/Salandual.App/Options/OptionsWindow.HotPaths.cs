// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.HotPaths.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    internal Func<string?>? PanelFolder { get; set; }

    private void AddHotPaths()
    {
        const string page = "hotpaths";
        Add(page, AppStrings.Copy_Group_HotPaths, null, null, () => new HotPathTable(Settings, PanelFolder), wide: true,
            keywords: AppStrings.Opt_HotPaths_Kw);

        var keys = AppStrings.OptSec_KeysDisplay;
        Add(page, keys, AppStrings.Opt_ShiftDigits, AppStrings.Opt_ShiftDigits_Desc,
            () => Toggle(Current.HotPaths.ShiftDigits, on => UpdateHotPaths(h => h with { ShiftDigits = on })), keywords: AppStrings.Opt_ShiftDigits_Kw);
        Add(page, keys, AppStrings.Bar_HotPathBar, AppStrings.Opt_HotBar_Desc,
            () => Toggle(Current.Layout.Bars.HotPathBar, on => UpdateBarsOnly(b => b with { HotPathBar = on })), keywords: AppStrings.Opt_HotBar_Kw);
        Add(page, keys, AppStrings.Opt_AskName, AppStrings.Opt_AskName_Desc,
            () => Toggle(Current.HotPaths.AskForName, on => UpdateHotPaths(h => h with { AskForName = on })), keywords: AppStrings.Opt_AskName_Kw);

        var drives = AppStrings.OptSec_DriveMenu;
        Add(page, drives, AppStrings.OptSec_Drives, AppStrings.Opt_DrivesShow_Desc,
            () => LetterPicker(letter => Current.DriveMenu.IsDriveShown(letter),
                (letter, on) => UpdateDriveMenu(d => d with { HiddenDrives = DriveMenuSettings.Toggle(d.HiddenDrives, letter, !on) }), AppStrings.Common_Show),
            wide: true, keywords: AppStrings.Opt_Drives_Kw);
        Add(page, drives, AppStrings.Opt_SeparatorAfter, AppStrings.Opt_SeparatorAfter_Desc,
            () => LetterPicker(letter => Current.DriveMenu.HasSeparatorAfter(letter),
                (letter, on) => UpdateDriveMenu(d => d with { SeparatorsAfter = DriveMenuSettings.Toggle(d.SeparatorsAfter, letter, on) }), AppStrings.Opt_SeparatorAfter),
            wide: true, keywords: AppStrings.Opt_SeparatorAfter_Kw);
        Add(page, drives, AppStrings.Opt_FreeSpaceMenu, AppStrings.Opt_FreeSpaceMenu_Desc,
            () => Toggle(Current.DriveMenu.FreeSpace, on => UpdateDriveMenu(d => d with { FreeSpace = on })), keywords: AppStrings.Opt_FreeSpaceMenu_Kw);
        Add(page, drives, AppStrings.Bar_DriveStrip, AppStrings.Opt_DriveStripHot_Desc,
            () => Toggle(Current.Layout.Bars.DriveStrip, on => UpdateBarsOnly(b => b with { DriveStrip = on })), keywords: AppStrings.Opt_DriveStripHot_Kw);
        Add(page, drives, AppStrings.Opt_Places, AppStrings.Opt_Places_Desc, PlacesTable,
            wide: true, keywords: AppStrings.Opt_Places_Kw);
    }

    private void UpdateHotPaths(Func<HotPathSettings, HotPathSettings> change)
        => Settings.Update(s => s with { HotPaths = change(s.HotPaths) });

    private void UpdateDriveMenu(Func<DriveMenuSettings, DriveMenuSettings> change)
        => Settings.Update(s => s with { DriveMenu = change(s.DriveMenu) });

    private void UpdateBarsOnly(Func<BarVisibility, BarVisibility> change)
        => Settings.Update(s => s with { Layout = s.Layout with { Bars = change(s.Layout.Bars) } });

    private static Control LetterPicker(Func<char, bool> isOn, Action<char, bool> changed, string what)
    {
        var present = Drives.List().Select(d => char.ToUpperInvariant(d.Root[0])).ToHashSet();
        var panel = new WrapPanel();
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            var l = letter;
            var exists = present.Contains(letter);
            var dot = new Ellipse { Width = 4, Height = 4, HorizontalAlignment = HorizontalAlignment.Center, Opacity = exists ? 1 : 0 };
            var label = new TextBlock
            {
                Text = letter.ToString(),
                FontSize = 13,
                FontWeight = exists ? FontWeight.SemiBold : FontWeight.Normal,
                Opacity = exists ? 1 : 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var content = new StackPanel { Spacing = 1, Children = { label, dot } };
            var button = new ToggleButton
            {
                Content = content,
                IsChecked = isOn(letter),
                Width = 30,
                Height = 34,
                Padding = new Thickness(0, 3, 0, 0),
                Margin = new Thickness(0, 0, 4, 4),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            dot.Bind(Shape.FillProperty, button.GetObservable(TemplatedControl.ForegroundProperty));
            ToolTip.SetTip(button, Loc.Format(exists ? AppStrings.Opt_DriveExists : AppStrings.Opt_DriveAbsent, what, letter));
            Avalonia.Automation.AutomationProperties.SetName(button, $"{what} {letter}:");
            button.IsCheckedChanged += (_, _) => changed(l, button.IsChecked == true);
            panel.Children.Add(button);
        }

        return panel;
    }

    private Control PlacesTable()
    {
        var table = new StackPanel { Spacing = 2 };
        var header = PlaceGrid();
        var title = Text(AppStrings.Opt_Places_Head, 12, FontWeight.SemiBold, "Brush.SecondaryText");
        var menuHead = Text(AppStrings.Opt_Places_Menu, 12, FontWeight.SemiBold, "Brush.SecondaryText");
        var stripHead = Text(AppStrings.Opt_Places_Strip, 12, FontWeight.SemiBold, "Brush.SecondaryText");
        menuHead.HorizontalAlignment = stripHead.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(menuHead, 1);
        Grid.SetColumn(stripHead, 2);
        header.Children.Add(title);
        header.Children.Add(menuHead);
        header.Children.Add(stripHead);
        table.Children.Add(header);

        foreach (var place in Current.DriveMenu.Places)
        {
            var id = place.Id;
            var (label, note, later, menuOnly) = id switch
            {
                DriveMenuSettings.Cloud => (AppStrings.Place_Cloud, AppStrings.Opt_Place_CloudNote, false, false),
                DriveMenuSettings.OtherPanel => (AppStrings.Place_OtherPanel, AppStrings.Opt_Place_OtherNote, false, true),
                DriveMenuSettings.Network => (AppStrings.Place_Network, AppStrings.Hot_NetworkLater, true, false),
                DriveMenuSettings.Registry => (AppStrings.Place_Registry, AppStrings.Hot_RegistryLater, true, false),
                _ => (AppStrings.ResourceManager.GetString("Place_" + id, AppStrings.Culture) ?? id, Drives.KnownFolder(id) ?? AppStrings.Opt_Place_NotFound, false, false),
            };

            var row = PlaceGrid();
            var text = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { Text(label, 13, brush: later ? "Brush.SecondaryText" : "Brush.Text"), Text(note, 12, brush: "Brush.TertiaryText") },
            };
            ((TextBlock)text.Children[1]).TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(text);
            var menu = PlaceBox(Loc.Format(AppStrings.Opt_PlaceInMenu, label), place.InMenu, !later, on => UpdatePlace(id, p => p with { InMenu = on }));
            Grid.SetColumn(menu, 1);
            row.Children.Add(menu);
            if (!menuOnly)
            {
                var strip = PlaceBox(Loc.Format(AppStrings.Opt_PlaceInStrip, label), place.InStrip, !later, on => UpdatePlace(id, p => p with { InStrip = on }));
                if (!later)
                {
                    EnabledWhile(strip, s => s.Layout.Bars.DriveStrip);
                }

                Grid.SetColumn(strip, 2);
                row.Children.Add(strip);
            }

            table.Children.Add(row);
        }

        return table;
    }

    private void UpdatePlace(string id, Func<PlaceSetting, PlaceSetting> change)
        => UpdateDriveMenu(d => d with { Places = [.. d.Places.Select(p => p.Id == id ? change(p) : p)] });

    private static Grid PlaceGrid() => new() { ColumnDefinitions = new ColumnDefinitions("*,64,64"), MinHeight = 30 };

    private static CheckBox PlaceBox(string name, bool value, bool enabled, Action<bool> changed)
    {
        var box = new CheckBox { IsChecked = value, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 0, Padding = default };
        Avalonia.Automation.AutomationProperties.SetName(box, name);
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }
}
