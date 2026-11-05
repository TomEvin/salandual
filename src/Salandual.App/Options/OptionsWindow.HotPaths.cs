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
        Add(page, drives, AppStrings.Opt_RowSpacing, AppStrings.Opt_RowSpacing_Desc,
            () => Segmented([(MenuRowSpacing.Closer, AppStrings.Opt_RowSpacing_Closer), (MenuRowSpacing.Normal, AppStrings.Opt_RowSpacing_Normal)],
                Current.DriveMenu.RowSpacing, v => UpdateDriveMenu(d => d with { RowSpacing = v })), keywords: AppStrings.Opt_RowSpacing_Kw);
        Add(page, drives, AppStrings.Opt_SpaceShown, AppStrings.Opt_SpaceShown_Desc,
            () => Segmented([(DriveSpaceShown.Free, AppStrings.Opt_SpaceShown_Free), (DriveSpaceShown.FreeOfTotal, AppStrings.Opt_SpaceShown_FreeOfTotal), (DriveSpaceShown.Total, AppStrings.Opt_SpaceShown_Total), (DriveSpaceShown.Nothing, AppStrings.Opt_SpaceShown_Nothing)],
                Current.DriveMenu.SpaceShown, v => UpdateDriveMenu(d => d with { SpaceShown = v })), keywords: AppStrings.Opt_SpaceShown_Kw);
        Add(page, drives, AppStrings.Opt_DriveListIcon, AppStrings.Opt_DriveListIcon_Desc,
            () => Segmented([(DriveIconLook.Plain, AppStrings.Opt_DriveListIcon_Plain), (DriveIconLook.Picture, AppStrings.Opt_DriveInfoLook_Picture), (DriveIconLook.Picture3D, AppStrings.Opt_DriveInfoLook_Picture3D)],
                Current.DriveMenu.DriveListIcon, icon => UpdateDriveMenu(d => d with { DriveListIcon = icon })), keywords: AppStrings.Opt_DriveListIcon_Kw);
        Add(page, drives, AppStrings.Opt_ActivityInStrip, AppStrings.Opt_ActivityInStrip_Desc,
            () => Toggle(Current.DriveMenu.ActivityInStrip, on => UpdateDriveMenu(d => d with { ActivityInStrip = on })), keywords: AppStrings.Opt_ActivityInStrip_Kw);
        Add(page, drives, AppStrings.Bar_DriveStrip, AppStrings.Opt_DriveStripHot_Desc,
            () => Toggle(Current.Layout.Bars.DriveStrip, on => UpdateBarsOnly(b => b with { DriveStrip = on })), keywords: AppStrings.Opt_DriveStripHot_Kw);
        Add(page, drives, AppStrings.Opt_FoldersGroup, AppStrings.Opt_FoldersGroup_Desc,
            () => Toggle(Current.DriveMenu.FoldersInMenu, on => UpdateDriveMenu(d => d with { FoldersInMenu = on })), keywords: AppStrings.Opt_FoldersGroup_Kw);
        Add(page, drives, AppStrings.Opt_Places, AppStrings.Opt_Places_Desc, PlacesTable,
            wide: true, keywords: AppStrings.Opt_Places_Kw);
        Add(page, drives, AppStrings.Opt_Clouds, AppStrings.Opt_Clouds_Desc, CloudsTable,
            wide: true, keywords: AppStrings.Opt_Clouds_Kw);
        Add(page, drives, AppStrings.Opt_ServersInMenu, AppStrings.Opt_ServersInMenu_Desc, ServersInMenuPicker, keywords: AppStrings.Opt_ServersInMenu_Kw);
        Add(page, drives, AppStrings.Opt_HotPathsGroup, AppStrings.Opt_HotPathsGroup_Desc,
            () => Toggle(Current.DriveMenu.HotPathsInMenu, on => UpdateDriveMenu(d => d with { HotPathsInMenu = on })), keywords: AppStrings.Opt_HotPathsGroup_Kw);
        Add(page, drives, AppStrings.Opt_PlaceChips, AppStrings.Opt_PlaceChips_Desc,
            () => Segmented([(false, AppStrings.Opt_BarNames_Names), (true, AppStrings.Opt_BarNames_IconsOnly)],
                Current.DriveMenu.PlaceIconsOnly, iconsOnly => UpdateDriveMenu(d => d with { PlaceIconsOnly = iconsOnly })), keywords: AppStrings.Opt_PlaceChips_Kw);

        var line = AppStrings.OptSec_DirLineDrive;
        Add(page, line, AppStrings.Opt_FreeSpaceInLine, AppStrings.Opt_FreeSpaceInLine_Desc,
            () => Toggle(Current.DriveMenu.FreeSpaceInLine, on => UpdateDriveMenu(d => d with { FreeSpaceInLine = on })), keywords: AppStrings.Opt_FreeSpaceInLine_Kw);
        Add(page, line, AppStrings.Opt_ActivityInLine, AppStrings.Opt_ActivityInLine_Desc,
            () => Toggle(Current.DriveMenu.ActivityInLine, on => UpdateDriveMenu(d => d with { ActivityInLine = on })), keywords: AppStrings.Opt_ActivityInLine_Kw);
        Add(page, line, AppStrings.Opt_ActivityLook, AppStrings.Opt_ActivityLook_Desc,
            () => Segmented([(Core.ActivityLook.Bar, AppStrings.Opt_ActivityLook_Bar), (Core.ActivityLook.Columns, AppStrings.Opt_ActivityLook_Columns), (Core.ActivityLook.Line, AppStrings.Opt_ActivityLook_Line)],
                Current.DriveMenu.ActivityLook, look => UpdateDriveMenu(d => d with { ActivityLook = look })), keywords: AppStrings.Opt_ActivityLook_Kw);

        Add(page, AppStrings.OptSec_DriveInfo, AppStrings.Opt_DriveInfoLook, AppStrings.Opt_DriveInfoLook_Desc,
            () => Segmented([(DriveSpaceLook.Ring, AppStrings.Opt_DriveInfoLook_Ring), (DriveSpaceLook.Picture, AppStrings.Opt_DriveInfoLook_Picture), (DriveSpaceLook.Picture3D, AppStrings.Opt_DriveInfoLook_Picture3D)],
                Current.DriveMenu.DriveInfoLook, look => UpdateDriveMenu(d => d with { DriveInfoLook = look })), keywords: AppStrings.Opt_DriveInfoLook_Kw);
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
                DriveMenuSettings.Cloud => (AppStrings.DriveMenu_Cloud, AppStrings.Opt_Place_CloudNote, false, false),
                DriveMenuSettings.OtherPanel => (AppStrings.Place_OtherPanel, AppStrings.Opt_Place_OtherNote, false, true),
                DriveMenuSettings.Registry => (AppStrings.Place_Registry, AppStrings.Hot_RegistryLater, true, false),
                DriveMenuSettings.Ftp => (AppStrings.Place_Ftp, AppStrings.Opt_Place_FtpNote, false, false),
                _ when DriveMenuSettings.IsShellPlace(id) => (MainWindow.PlaceLabel(id), AppStrings.Opt_Place_ShellNote, false, false),
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

    internal Func<Task<IReadOnlyList<CloudFolder>>>? FindClouds { get; set; }

    private Control CloudsTable()
    {
        var table = new StackPanel { Spacing = 2 };
        IReadOnlyList<CloudFolder>? found = null;

        void Fill()
        {
            table.Children.Clear();
            var menu = Current.DriveMenu;
            if (found is null)
            {
                table.Children.Add(Text(AppStrings.Opt_Clouds_Searching, 12, brush: "Brush.TertiaryText"));
            }
            else if (found.Count == 0 && menu.AddedClouds.Count == 0)
            {
                table.Children.Add(Text(AppStrings.Opt_Clouds_None, 12, brush: "Brush.TertiaryText"));
            }

            var rows = (found ?? []).Where(c => !c.IsDrive).Select(c => (c.Name, c.Path, Added: false))
                .Concat(menu.AddedClouds.Where(a => !(found ?? []).Any(c => string.Equals(c.Path, a.Path, StringComparison.OrdinalIgnoreCase))).Select(a => (a.Name, a.Path, Added: true)));
            foreach (var (name, path, added) in rows)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), MinHeight = 30 };
                var cloudPath = path;
                var box = PlaceBox(Loc.Format(AppStrings.Opt_PlaceInMenu, name), menu.IsCloudShown(path), true,
                    on => UpdateDriveMenu(d => d with { HiddenClouds = on ? [.. d.HiddenClouds.Where(p => !string.Equals(p, cloudPath, StringComparison.OrdinalIgnoreCase))] : [.. d.HiddenClouds, cloudPath] }));
                box.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(box);
                var label = Text(name, 13);
                label.VerticalAlignment = VerticalAlignment.Center;
                label.Margin = new Thickness(0, 0, 12, 0);
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var where = Text(path, 12, brush: "Brush.TertiaryText");
                where.VerticalAlignment = VerticalAlignment.Center;
                where.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(where, 2);
                row.Children.Add(where);
                if (added)
                {
                    var remove = ActionButton(AppStrings.Opt_Clouds_Remove, () =>
                    {
                        UpdateDriveMenu(d => d with { AddedClouds = [.. d.AddedClouds.Where(a => !string.Equals(a.Path, cloudPath, StringComparison.OrdinalIgnoreCase))] });
                        Fill();
                    });
                    Avalonia.Automation.AutomationProperties.SetName(remove, AppStrings.Opt_Clouds_Remove + " " + name);
                    Grid.SetColumn(remove, 3);
                    row.Children.Add(remove);
                }

                table.Children.Add(row);
            }

            var add = ActionButton(AppStrings.Opt_Clouds_Add, () => _ = AddCloudAsync(Fill));
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 6, 0, 0);
            table.Children.Add(add);
        }

        Fill();
        var find = FindClouds ?? (_services.IsIsolated
            ? () => Task.FromResult<IReadOnlyList<CloudFolder>>([])
            : () => Task.Run(async () => Clouds.OnThisPc(await Drives.ReadAsync(TimeSpan.FromSeconds(1.5)).ConfigureAwait(false))));
        _ = find().ContinueWith(t => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            found = t.IsCompletedSuccessfully ? t.Result : [];
            Fill();
        }), TaskScheduler.Default);
        return table;
    }

    internal Func<Task<string?>>? PickCloudFolder { get; set; }

    private async Task AddCloudAsync(Action refill)
    {
        string? path;
        if (PickCloudFolder is { } pick)
        {
            path = await pick();
        }
        else
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = AppStrings.Opt_Clouds_Add.TrimEnd('…'), AllowMultiple = false });
            path = folders.Count > 0 ? Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(folders[0]) : null;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        UpdateDriveMenu(d => d with { AddedClouds = [.. d.AddedClouds, new AddedCloud(string.IsNullOrEmpty(name) ? path : name, path)], HiddenClouds = [.. d.HiddenClouds.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))] });
        refill();
    }

    private Control ServersInMenuPicker()
    {
        var number = new NumericUpDown
        {
            Minimum = 0,
            Maximum = ServerSettings.MaxInDriveMenu,
            Increment = 1,
            FormatString = "0",
            Value = Current.Servers.InDriveMenu,
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetName(number, AppStrings.Opt_ServersInMenu);
        number.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } v)
            {
                var n = (int)Math.Clamp(Math.Round(v), 0, ServerSettings.MaxInDriveMenu);
                Settings.Update(s => s with { Servers = s.Servers with { InDriveMenu = n } });
            }
        };
        var unit = Text(AppStrings.Opt_ServersInMenu_Unit, 13, brush: "Brush.SecondaryText");
        unit.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { number, unit } };
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
