// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Tabs.cs

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Salandual.App.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    private void UpdateTabs(Func<TabSettings, TabSettings> change) => UpdatePanels(p => p with { Tabs = change(p.Tabs) });

    private void AddTabs(string page)
    {
        var tabs = AppStrings.OptSec_Tabs;
        var kw = AppStrings.Opt_Tabs_Kw;
        Add(page, tabs, AppStrings.Opt_TabStrip, AppStrings.Opt_TabStrip_Desc, () =>
            Segmented([(TabBarMode.WhenNeeded, AppStrings.Opt_TabStrip_FromTwo), (TabBarMode.Always, AppStrings.Opt_TabStrip_Always)],
                Current.Layout.TabBar == TabBarMode.Always ? TabBarMode.Always : TabBarMode.WhenNeeded,
                v => Settings.Update(s => s with { Layout = s.Layout with { TabBar = v } })), keywords: kw);
        Add(page, tabs, AppStrings.Opt_NewTab, AppStrings.Opt_NewTab_Desc, () =>
            Segmented([(NewTabOpens.SameFolder, AppStrings.Opt_NewTab_Same), (NewTabOpens.Place, AppStrings.Opt_NewTab_Place)],
                Current.Panels.Tabs.NewTab, v =>
                {
                    UpdateTabs(t => t with { NewTab = v });
                    RebuildLater();
                }), keywords: kw, below: NewTabPlaceField);
        Add(page, tabs, AppStrings.Opt_LockedTab, AppStrings.Opt_LockedTab_Desc, () =>
            Segmented([(LockedTab.CantClose, AppStrings.Opt_LockedTab_CantClose), (LockedTab.StaysInFolder, AppStrings.Opt_LockedTab_Stays)],
                Current.Panels.Tabs.Locked, v => UpdateTabs(t => t with { Locked = v })), keywords: kw);
        Add(page, tabs, AppStrings.Opt_TabColours, AppStrings.Opt_TabColours_Desc, () =>
            Segmented([(TabColourLook.Bar, AppStrings.Opt_TabColours_Bar), (TabColourLook.Tint, AppStrings.Opt_TabColours_Tint), (TabColourLook.None, AppStrings.Opt_TabColours_None)],
                Current.Panels.Tabs.Colours, v => UpdateTabs(t => t with { Colours = v })), keywords: kw);
        Add(page, tabs, AppStrings.Opt_TabsAtStart, AppStrings.Opt_TabsAtStart_Desc, () =>
            Segmented([(TabsAtStart.All, AppStrings.Opt_TabsAtStart_All), (TabsAtStart.ActiveOnly, AppStrings.Opt_TabsAtStart_Shown)],
                Current.Panels.Tabs.AtStart, v => UpdateTabs(t => t with { AtStart = v })), keywords: kw);
        Add(page, tabs, AppStrings.Opt_ManyTabs, AppStrings.Opt_ManyTabs_Desc, () =>
            Segmented([(ManyTabs.Scroll, AppStrings.Opt_ManyTabs_Scroll), (ManyTabs.Rows, AppStrings.Opt_ManyTabs_Rows)],
                Current.Panels.Tabs.Many, v => UpdateTabs(t => t with { Many = v })), keywords: kw);
    }

    private Control NewTabPlaceField()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        grid.IsVisible = Current.Panels.Tabs.NewTab == NewTabOpens.Place;
        var box = new TextBox { Text = Current.Panels.Tabs.NewTabPlace, PlaceholderText = AppStrings.Opt_NewTab_Home, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Opt_NewTab_Place);
        box.TextChanged += (_, _) => UpdateTabs(t => t with { NewTabPlace = (box.Text ?? "").Trim() });
        var browse = ActionButton(AppStrings.Opt_StartFolders_Choose, () => _ = ChooseAsync());
        Grid.SetColumn(browse, 1);
        grid.Children.Add(box);
        grid.Children.Add(browse);
        return grid;

        async Task ChooseAsync()
        {
            var text = (box.Text ?? "").Trim();
            var start = Directory.Exists(text) ? await StorageProvider.TryGetFolderFromPathAsync(text) : null;
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = AppStrings.Opt_NewTab_Place, SuggestedStartLocation = start });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            {
                box.Text = path;
            }
        }
    }
}
