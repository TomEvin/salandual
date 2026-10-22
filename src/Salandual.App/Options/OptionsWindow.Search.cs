// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Search.cs

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public partial class OptionsWindow
{
    private IEnumerable<SettingItem> FoundThemes(Func<string, bool> matches)
        => ThemeCatalog.All(Current.Appearance)
            .DistinctBy(t => t.Id)
            .Where(t => matches(t.Name + " " + Kind(t)))
            .Select(t => new SettingItem(ColorsPage, AppStrings.Themes_Tab_Library, t.Name, Kind(t), () => ThemeButtons(t)));

    private Control ThemeButtons(ColorTheme theme)
    {
        var (label, inUse) = UseState(Current.Appearance.Schedule, theme);
        var use = ActionButton(inUse ? "✓ " + label : label, () =>
        {
            UseNow(theme);
            RebuildLater();
        }, enabled: !inUse);
        var show = ActionButton(AppStrings.Common_Show, () => ShowTheme(theme.Id));
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { show, use } };
    }

    internal void ShowTheme(string id)
    {
        _themesTab = "library";
        _pickedTheme = id;
        _search.Text = "";
        ShowCategory(ColorsPage);
    }

    private IEnumerable<SettingItem> FoundHotPaths(Func<string, bool> matches)
        => Current.HotPaths.Paths
            .Where(hp => hp.Path.Length > 0 && matches(hp.Name + " " + hp.Path))
            .Select(hp => new SettingItem("hotpaths", AppStrings.Copy_Group_HotPaths, hp.Name.Length > 0 ? hp.Name : hp.Path,
                HotPathSettings.KeyOf(hp.Slot) is { } key ? Loc.Format(AppStrings.Opt_Found_HotPathKey, hp.Path, key) : Loc.Format(AppStrings.Opt_Found_HotPath, hp.Path, hp.Slot),
                () => ActionButton(AppStrings.Common_Show, () => ShowHotPath(hp.Slot))));

    internal void ShowHotPath(int slot)
    {
        _search.Text = "";
        ShowCategory("hotpaths");

        Dispatcher.UIThread.Post(() =>
        {
            if (_page.GetLogicalDescendants().OfType<HotPathTable>().FirstOrDefault() is { } table)
            {
                table.List.SelectedIndex = slot - 1;
                table.List.ScrollIntoView(slot - 1);
            }
        }, DispatcherPriority.Background);
    }
}
