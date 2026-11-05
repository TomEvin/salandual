// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Filter.cs

using Avalonia;
using Avalonia.Controls;
using Salandual.App.Dialogs;
using Salandual.App.Input;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.Settings;

namespace Salandual.App;

public partial class MainWindow
{
    private FilterDialog? _filterDialog;

    internal FilterDialog? OpenFilterDialog => _filterDialog;

    internal ContextMenu? FilterMenu { get; private set; }

    private void WireFilter(PanelHost host)
        => host.DirectoryLine.FilterMenuRequested += (_, _) => ShowFilterMenu(host);

    internal async Task FilterAsync(PanelHost host)
    {
        if (_filterDialog is not null)
        {
            _filterDialog.Activate();
            return;
        }

        var s = _services.Settings.Current;
        var side = host == _panels[0] ? AppStrings.Filter_Left : AppStrings.Filter_Right;
        _filterDialog = new FilterDialog(side, host.Panel.Filter, s.History.FilterMasks, s.Panels.HideDotFiles, host.Panel.IsFlat);
        try
        {
            if (await _filterDialog.ShowDialog<FilterChoice?>(this) is { } choice)
            {
                host.Panel.SetFilter(choice.Filter);
                if (choice.Subfolders != host.Panel.IsFlat)
                {
                    ToggleFlat(host, []);
                }

                _services.Settings.Update(x => x with
                {
                    Panels = x.Panels with { HideDotFiles = choice.HideDotFiles },
                    History = choice.Filter is { } f ? x.History with { FilterMasks = HistorySettings.Push(x.History.FilterMasks, f.Mask.Text) } : x.History,
                });
            }
        }
        finally
        {
            _filterDialog = null;
            host.View.Focus();
        }
    }

    private void ToggleDotFiles()
        => _services.Settings.Update(s => s with { Panels = s.Panels with { HideDotFiles = !s.Panels.HideDotFiles } });

    internal ContextMenu ShowFilterMenu(PanelHost host)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = host.DirectoryLine,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = host.DirectoryLine.FilterChipBounds,
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.BottomLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
        };
        MenuItem Item(string header, string? gestureOf, Action run, bool enabled = true, bool? check = null)
        {
            var item = new MenuItem
            {
                Header = header,
                IsEnabled = enabled,
                InputGesture = gestureOf is not null && _services.Commands.FirstChord(gestureOf) is { } chord ? KeyMapping.ToGesture(chord) : null,
            };
            if (check is { } on)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = on;
            }

            item.Click += (_, _) => run();
            menu.Items.Add(item);
            return item;
        }

        var panels = _services.Settings.Current.Panels;
        Item(AppStrings.Filter_Menu_Change, "Panel.Filter", () => _ = FilterAsync(host));
        Item(AppStrings.Filter_Menu_Off, "Panel.FilterOff", () => host.Panel.ClearFilter());
        menu.Items.Add(new Separator());
        Item(AppStrings.Filter_Menu_ShowNames, "Panel.ShowHiddenNames", () => host.Panel.ShowHiddenNames(), enabled: host.Panel.HiddenNameCount > 0);
        Item(AppStrings.Filter_Menu_Hidden, "Panel.ToggleHidden", () => Run("Panel.ToggleHidden", host == _panels[0] ? PanelTarget.Left : PanelTarget.Right), check: panels.ShowHiddenFiles);
        Item(AppStrings.Filter_Menu_DotFiles, "Panel.ToggleDotFiles", ToggleDotFiles, check: panels.HideDotFiles);
        menu.Closed += (_, _) =>
        {
            FilterMenu = null;
            host.View.Focus();
        };
        FilterMenu = menu;
        menu.Open(host.DirectoryLine);
        return menu;
    }
}
