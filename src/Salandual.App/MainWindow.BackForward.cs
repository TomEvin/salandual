// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.BackForward.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.App;

public partial class MainWindow
{
    internal const string NavBack = "back", NavForward = "forward", NavUp = "up";

    private BackForwardSettings BackForward => _services.Settings.Current.Layout.BackForward;

    internal ContextMenu? StepsMenu { get; private set; }

    private (PanelHost Host, bool Forward, Control Anchor)? _stepsFor;
    private long _stepsClosedAt;

    private void WireBackForward()
    {
        BackForwardBar.ShowAdd = false;
        BackForwardBar.ItemClicked += (_, item) => OnBackForwardItem(item);
        BackForwardBar.ItemHeld += (_, item) => OnBackForwardItem(item.Target is NavBack or NavForward ? item with { Kind = StripItemKind.NavList } : item);
        BackForwardBar.ItemMenuRequested += (_, _) => ShowBackForwardBarMenu();
        AddHandler(PointerPressedEvent, OnSideButton, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void WireBackForward(PanelHost host)
    {
        host.DirectoryLine.StepRequested += (_, forward) => Step(host, forward);
        host.DirectoryLine.StepsListRequested += (_, r) => ToggleStepsList(host, r.Forward, host.DirectoryLine, r.At);
        host.PathChanged += (_, _) => UpdateBackForward();
    }

    internal IReadOnlyList<StripItem> BackForwardItems()
    {
        var panel = ActivePanel.Panel;
        var withList = BackForward.ListOpens == StepsListOpens.Button;
        var items = new List<StripItem>();
        foreach (var forward in (ReadOnlySpan<bool>)[false, true])
        {
            var can = forward ? panel.CanGoForward : panel.CanGoBack;
            var target = forward ? NavForward : NavBack;
            items.Add(new StripItem(StripItemKind.Nav, "", target, Tip: NavTip(panel, forward), Glyph: forward ? "Icon.ChevronRight" : "Icon.ChevronLeft", Dim: !can));
            if (withList)
            {
                items.Add(new StripItem(StripItemKind.NavList, "", target, Tip: AppStrings.Nav_ListTip, Dim: !can));
            }
        }

        items[^1] = items[^1] with { SeparatorAfter = true };
        items.Add(new StripItem(StripItemKind.Nav, "", NavUp, Tip: AppStrings.Nav_UpTip, Glyph: "Icon.ArrowUp", Dim: panel.Path.Length == 0 || panel.Path == ShellPlace.ThisPc));
        return items;
    }

    private string NavTip(FilePanel panel, bool forward)
    {
        var steps = forward ? panel.ForwardHistory : panel.BackHistory;
        if (steps.Count == 0)
        {
            return forward ? AppStrings.Nav_ForwardNone : AppStrings.Nav_BackNone;
        }

        var tip = Loc.Format(forward ? AppStrings.Nav_ForwardTip : AppStrings.Nav_BackTip, ShellPlace.Display(steps[^1].Path));
        return BackForward.ListOpens == StepsListOpens.Hold ? tip + "\n" + AppStrings.Nav_HoldHint : tip;
    }

    private void UpdateBackForward()
    {
        if (_panels is null)
        {
            return;
        }

        var s = BackForward;
        var withList = s.ListOpens == StepsListOpens.Button;
        BackForwardBar.HoldForList = !withList;
        BackForwardBar.Items = BackForwardItems();
        foreach (var host in _panels)
        {
            var line = host.DirectoryLine;
            line.ShowBackForward = s.InDirectoryLines;
            line.StepsListButtons = withList;
            line.HoldForList = !withList;
            line.CanGoBack = host.Panel.CanGoBack;
            line.CanGoForward = host.Panel.CanGoForward;
            line.BackTip = NavTip(host.Panel, forward: false);
            line.ForwardTip = NavTip(host.Panel, forward: true);
        }
    }

    private void ApplyStepsKept(int steps)
    {
        foreach (var panel in _tabs.SelectMany(t => t).Select(t => t.Panel).Concat(_panels.Select(h => h.Panel)))
        {
            panel.HistoryLimit = steps;
        }
    }

    private void OnBackForwardItem(StripItem item)
    {
        if (item.Dim)
        {
            return;
        }

        if (item.Target == NavUp)
        {
            SetActive(_active, focus: true);
            ActivePanel.View.TryRunCommand("Navigation.Parent");
            return;
        }

        var forward = item.Target == NavForward;
        if (item.Kind == StripItemKind.NavList)
        {
            var nav = BackForwardBar.Items.FirstOrDefault(i => i.Kind == StripItemKind.Nav && i.Target == item.Target);
            var at = (nav is null ? null : BackForwardBar.ChipBounds(nav)) ?? BackForwardBar.ChipBounds(item) ?? new Rect(0, 0, 0, BackForwardBar.Bounds.Height);
            if (BackForwardBar.ChipBounds(item) is { } list)
            {
                at = at.Union(list);
            }

            ToggleStepsList(ActivePanel, forward, BackForwardBar, at);
        }
        else
        {
            Step(ActivePanel, forward);
        }
    }

    internal void Step(PanelHost host, bool forward)
    {
        SetActive(Array.IndexOf(_panels, host), focus: true);
        _ = forward ? host.Panel.GoForwardAsync() : host.Panel.GoBackAsync();
    }

    private void OnSideButton(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Properties;
        if (!(point.IsXButton1Pressed || point.IsXButton2Pressed) || !BackForward.MouseButtons
            || (e.Source as Visual)?.FindAncestorOfType<PanelHost>(includeSelf: true) is not { } host || Array.IndexOf(_panels, host) < 0)
        {
            return;
        }

        SideButton(host, forward: point.IsXButton2Pressed);
        e.Handled = true;
    }

    internal void SideButton(PanelHost host, bool forward)
    {
        if (BackForward.MouseButtons && (forward ? host.Panel.CanGoForward : host.Panel.CanGoBack))
        {
            Step(host, forward);
        }
    }

    internal void ToggleStepsList(PanelHost host, bool forward, Control anchor, Rect at)
    {
        if (_stepsFor == (host, forward, anchor) && (StepsMenu is { IsOpen: true } || Environment.TickCount64 - _stepsClosedAt < 500))
        {
            StepsMenu?.Close();
            _stepsClosedAt = 0;
            return;
        }

        ShowStepsList(host, forward, anchor, at);
    }

    internal ContextMenu ShowStepsList(PanelHost host, bool forward, Control anchor, Rect at)
    {
        StepsMenu?.Close();
        var both = BackForward.Listed == StepsListed.BothPanels;
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = at,
            PlacementAnchor = PopupAnchor.BottomLeft,
            PlacementGravity = PopupGravity.BottomRight,
        };

        var heading = both ? forward ? AppStrings.Nav_ListForwardBoth : AppStrings.Nav_ListBackBoth : forward ? AppStrings.Nav_ListForward : AppStrings.Nav_ListBack;
        menu.Items.Add(new MenuItem { Header = new TextBlock { Text = heading.ToUpper(CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeight.SemiBold }, IsEnabled = false });
        var side = Array.IndexOf(_panels, host);
        var tag = side == 0 ? AppStrings.Quick_Left : AppStrings.Quick_Right;

        var own = (forward ? host.Panel.ForwardHistory : host.Panel.BackHistory).Reverse().Select((s, i) => (Step: s, Own: true, Count: i + 1));
        var other = OtherPanel(host).Panel;
        var steps = both
            ? own.Concat((forward ? other.ForwardHistory : other.BackHistory).Select(s => (Step: s, Own: false, Count: 0))).OrderByDescending(s => s.Step.Order)
            : own;
        foreach (var (step, isOwn, count) in steps)
        {
            var path = ShellPlace.Display(step.Path);
            var item = new MenuItem
            {
                Header = Row(path, both ? isOwn ? tag : side == 0 ? AppStrings.Quick_Right : AppStrings.Quick_Left : "", digit: isOwn && count <= 9 ? count : null, dim: !isOwn),
            };
            if (isOwn)
            {
                item.Click += (_, _) => _ = forward ? host.Panel.GoForwardAsync(count) : host.Panel.GoBackAsync(count);
            }
            else
            {
                var target = step.Path;
                item.Click += (_, _) => _ = host.Panel.NavigateAsync(target);
            }

            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var clear = new MenuItem { Header = AppStrings.Nav_Clear };
        clear.Click += (_, _) =>
        {
            host.Panel.ClearHistory();
            if (both)
            {
                other.ClearHistory();
            }
        };
        menu.Items.Add(clear);

        StepsMenu = menu;
        _stepsFor = (host, forward, anchor);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(StepsMenu, menu))
            {
                StepsMenu = null;
                _stepsClosedAt = Environment.TickCount64;
            }

            host.View.Focus();
        };
        SetActive(side, focus: false);
        menu.Open(anchor);
        return menu;

        static Control Row(string path, string note, int? digit = null, bool dim = false)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*,Auto"), MinWidth = 300, MaxWidth = 560 };
            if (digit is { } d)
            {
                var key = new Avalonia.Controls.Primitives.AccessText { Text = "_" + d.ToString(CultureInfo.InvariantCulture), Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
                grid.Children.Add(key);
            }

            var text = new TextBlock
            {
                Text = path,
                Opacity = dim ? 0.6 : 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (note.Length > 0)
            {
                var right = new TextBlock { Text = note, FontSize = 11, Opacity = 0.6, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(right, 2);
                grid.Children.Add(right);
            }

            return grid;
        }
    }

    internal ContextMenu ShowBackForwardBarMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(WhereMenu(BarBlock.BackForward));
        menu.Items.Add(LockMenuItem());
        var hide = new MenuItem { Header = AppStrings.Nav_Hide };
        hide.Click += (_, _) => UpdateBackForwardSettings(b => b.WithBar(false));
        menu.Items.Add(hide);
        menu.Items.Add(new Separator());
        var options = new MenuItem { Header = AppStrings.Nav_Options };
        options.Click += (_, _) => _ = OpenOptionsAsync("layout");
        menu.Items.Add(options);
        menu.Closed += (_, _) => ActivePanel.View.Focus();
        menu.Open(BackForwardBar);
        return menu;
    }

    private void UpdateBackForwardSettings(Func<BackForwardSettings, BackForwardSettings> change)
        => _services.Settings.Update(s => s with { Layout = s.Layout with { BackForward = change(s.Layout.BackForward) } });
}
