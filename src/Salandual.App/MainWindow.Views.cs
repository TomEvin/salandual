// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Views.cs

using Avalonia.Controls;
using Avalonia.Input;
using Salandual.App.Input;
using Salandual.App.Panels;
using Salandual.Core.Commands;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace Salandual.App;

public partial class MainWindow
{
    internal ContextMenu? ViewMenu { get; private set; }

    private const int ThumbnailStep = 16;

    private PanelView StartView(int index)
        => (index == 0 ? _services.Session.LeftView : _services.Session.RightView) is { } kept && (!PanelViews.IsOwn(kept) || IsMadeView(kept))
            ? kept
            : _services.Settings.Current.Panels.NewPanelView;

    private static Core.Listing.EntrySorter Sorter(bool numericSort) => new(System.Globalization.CultureInfo.CurrentCulture, numericSort) { TypeName = e => TypeNames.Of(e) };

    private void OnAltWheel(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            return;
        }

        _altTap = false;
        RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.None, KeyModifiers = KeyModifiers.Alt, Source = this });
    }

    private void WireViews(PanelHost host)
    {
        host.View.ThumbnailSizeStep += (_, step) => StepThumbnailSize(step);
        host.View.ColumnCharsChanged += (_, chars) => _services.Settings.Update(s => s with { Panels = s.Panels with { ColumnsWidth = chars } });
        host.DirectoryLine.ViewMenuRequested += (_, _) => ShowViewMenu(host);
    }

    internal void StepThumbnailSize(int step)
    {
        var now = _services.Settings.Current.Panels.ThumbnailSize;
        var size = Math.Clamp((((now + (ThumbnailStep / 2)) / ThumbnailStep) + step) * ThumbnailStep, PanelSettings.MinThumbnailSize, PanelSettings.MaxThumbnailSize);
        if (size != now)
        {
            _services.Settings.Update(s => s with { Panels = s.Panels with { ThumbnailSize = size } });
        }
    }

    private bool IsViewChecked(string id, PanelTarget target)
        => PanelViews.FromCommand(id) is { } view && HostFor(target).Panel.View == view;

    private PanelHost HostFor(PanelTarget target) => target switch
    {
        PanelTarget.Left => _panels[0],
        PanelTarget.Right => _panels[1],
        _ => ActivePanel,
    };

    internal ContextMenu ShowViewMenu(PanelHost host)
    {
        var target = host == _panels[0] ? PanelTarget.Left : PanelTarget.Right;
        var menu = new ContextMenu { PlacementTarget = host.DirectoryLine, Placement = PlacementMode.AnchorAndGravity };
        var button = host.DirectoryLine.ViewButtonBounds;
        menu.PlacementRect = button;
        menu.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.BottomLeft;
        menu.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
        foreach (var node in ViewMenuNodes(target))
        {
            if (node.IsSeparator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            if (node.Dynamic == "View.Columns")
            {
                var columns = new MenuItem { Header = node.Header };
                foreach (var child in ColumnMenuItems(host))
                {
                    columns.Items.Add(child);
                }

                menu.Items.Add(columns);
                continue;
            }

            var id = node.CommandId!;
            var item = new MenuItem
            {
                Header = node.Header,
                InputGesture = _services.Commands.FirstChord(id) is { } chord ? KeyMapping.ToGesture(chord) : null,
                IsEnabled = CanExecute(id),
            };
            if (node.Radio)
            {
                item.ToggleType = MenuItemToggleType.Radio;
                item.IsChecked = IsViewChecked(id, target);
            }

            ShowOwnViewItem(item, id);
            if (id == "Panel.FlatView")
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = host.Panel.IsFlat;
            }

            if (id == "Panel.SmartNameWidth")
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsEnabled = PanelViews.HasColumns(host.Panel.View);
                item.IsChecked = item.IsEnabled && IsSmartName(host);
            }

            item.Click += (_, _) => Run(id, target);
            menu.Items.Add(item);
        }

        host.DirectoryLine.IsViewMenuOpen = true;
        menu.Closed += (_, _) =>
        {
            host.DirectoryLine.IsViewMenuOpen = false;
            ViewMenu = null;
            host.View.Focus();
        };
        ViewMenu = menu;
        menu.Open(host.DirectoryLine);
        return menu;
    }

    private static IReadOnlyList<MenuNode> ViewMenuNodes(PanelTarget target)
    {
        var side = MainMenu.Create().First(top => top.Children?.Any(c => c.CommandId is not null && c.Target == target) == true);
        return side.Children!.First(n => n.Children is { } c && c.Any(x => x.CommandId == "View.Mode2")).Children!;
    }
}
