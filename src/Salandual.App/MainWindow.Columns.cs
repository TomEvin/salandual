// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Columns.cs

using Avalonia.Controls;
using Salandual.App.Input;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.Panels;

namespace Salandual.App;

public partial class MainWindow
{
    internal const string ViewsPage = "views";

    internal ContextMenu? ColumnMenu { get; private set; }

    private void WireColumns(PanelHost host)
    {
        host.View.ColumnWidthChanged += (_, c) => UpdateColumns(host, v => ColumnViews.SetWidth(v, c.Column, c.Width));
        host.View.HeaderMenuRequested += (_, at) => ShowColumnMenu(host, at);
        host.DirectoryLine.OwnViewNames = OwnViewName;
    }

    private bool IsMadeView(PanelView view) => ColumnViews.IsMade(_services.Settings.Current.Panels.ColumnViews, view);

    private string? OwnViewName(PanelView view)
        => _services.Settings.Current.Panels.ColumnViews.FirstOrDefault(v => v.View == view)?.Name is { Length: > 0 } name ? name : null;

    private void RunOwnView(string id, PanelHost host)
    {
        if (PanelViews.FromCommand(id) is { } view && IsMadeView(view))
        {
            host.Panel.SetView(view);
        }
    }

    private void KeepOwnViewsMade()
    {
        foreach (var host in _panels)
        {
            if (PanelViews.IsOwn(host.Panel.View) && !IsMadeView(host.Panel.View))
            {
                host.Panel.SetView(PanelView.Detailed);
            }
        }
    }

    internal void UpdateColumns(PanelHost host, Func<ColumnView, ColumnView> change)
    {
        var view = host.Panel.View;
        if (!PanelViews.HasColumns(view))
        {
            return;
        }

        _services.Settings.Update(s =>
        {
            var kept = s.Panels.ColumnViews;
            return s with { Panels = s.Panels with { ColumnViews = ColumnViews.With(kept, change(ColumnViews.For(kept, view))) } };
        });
    }

    private void ToggleSmartName(PanelHost host)
        => UpdateColumns(host, v => v.SmartName ? v with { SmartName = false, NameWidth = host.View.NameWidthNow } : v with { SmartName = true });

    private bool IsSmartName(PanelHost host)
        => ColumnViews.For(_services.Settings.Current.Panels.ColumnViews, host.Panel.View).SmartName;

    internal ContextMenu ShowColumnMenu(PanelHost host, Avalonia.Point at)
    {
        var menu = new ContextMenu { PlacementTarget = host.View, Placement = PlacementMode.AnchorAndGravity, PlacementRect = new Avalonia.Rect(at, new Avalonia.Size(1, 1)) };
        foreach (var item in ColumnMenuItems(host))
        {
            menu.Items.Add(item);
        }

        menu.Closed += (_, _) =>
        {
            ColumnMenu = null;
            host.View.Focus();
        };
        ColumnMenu = menu;
        menu.Open(host.View);
        return menu;
    }

    private List<Control> ColumnMenuItems(PanelHost host)
    {
        var view = host.Panel.View;
        var hasColumns = PanelViews.HasColumns(view);
        var setup = ColumnViews.For(_services.Settings.Current.Panels.ColumnViews, view);
        var items = new List<Control>();
        foreach (var column in ColumnViews.AllColumns)
        {
            var shown = setup.Columns.FirstOrDefault(c => c.Column == column)?.Shown == true;
            var item = new MenuItem
            {
                Header = ColumnNames.Of(column),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = shown,
                IsEnabled = hasColumns && column != PanelColumn.Name,
                StaysOpenOnClick = true,
            };
            item.Click += (_, _) =>
            {
                UpdateColumns(host, v => ColumnViews.Toggle(v, column));
                item.IsChecked = ColumnViews.For(_services.Settings.Current.Panels.ColumnViews, view).Columns.FirstOrDefault(c => c.Column == column)?.Shown == true;
            };
            items.Add(item);
        }

        items.Add(new Separator());
        var smart = new MenuItem
        {
            Header = AppStrings.Columns_SmartName,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = hasColumns && setup.SmartName,
            IsEnabled = hasColumns,
            InputGesture = _services.Commands.FirstChord("Panel.SmartNameWidth") is { } chord ? KeyMapping.ToGesture(chord) : null,
        };
        smart.Click += (_, _) => ToggleSmartName(host);
        items.Add(smart);
        var more = new MenuItem { Header = AppStrings.Columns_More };
        more.Click += (_, _) => _ = OpenOptionsAsync(ViewsPage);
        items.Add(more);
        return items;
    }

    private MenuItem ColumnsSubmenu(MenuNode node)
    {
        var item = new MenuItem { Header = node.Header };
        item.Items.Add(new MenuItem { Header = AppStrings.Columns_Name, IsEnabled = false });
        item.SubmenuOpened += (_, e) =>
        {
            if (ReferenceEquals(e.Source, item))
            {
                item.Items.Clear();
                foreach (var child in ColumnMenuItems(HostFor(node.Target)))
                {
                    item.Items.Add(child);
                }
            }
        };
        return item;
    }

    private void ShowOwnViewItem(MenuItem item, string id)
    {
        if (PanelViews.FromCommand(id) is { } view && PanelViews.IsOwn(view))
        {
            item.IsVisible = IsMadeView(view);
            if (OwnViewName(view) is { } name)
            {
                item.Header = name.Replace("_", "__", StringComparison.Ordinal);
            }
        }
    }
}
