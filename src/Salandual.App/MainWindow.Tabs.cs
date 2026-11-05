// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Tabs.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Input;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App;

public partial class MainWindow
{
    internal const int MaxTabs = 64;

    private const int MaxClosedTabs = 20;

    internal sealed class PanelTab(FilePanel panel)
    {
        public FilePanel Panel { get; } = panel;

        public bool Locked { get; set; }

        public NameColour Colour { get; set; }

        public string? Pending { get; set; }

        public string Path => Panel.Path.Length > 0 ? Panel.Path : Pending ?? "";
    }

    private readonly List<PanelTab>[] _tabs = [[], []];
    private readonly int[] _tabOn = [0, 0];
    private readonly List<(int Side, int Index, SavedTab Tab)> _closedTabs = [];

    internal IReadOnlyList<PanelTab> TabsOf(PanelHost host) => _tabs[Side(host)];

    internal int ShownTab(PanelHost host) => _tabOn[Side(host)];

    internal ContextMenu? TabMenu { get; private set; }

    private int Side(PanelHost host) => ReferenceEquals(host, _panels[1]) ? 1 : 0;

    private PanelHost? HostOf(FilePanel panel)
        => _panels is null ? null : ReferenceEquals(_panels[0].Panel, panel) ? _panels[0] : ReferenceEquals(_panels[1].Panel, panel) ? _panels[1] : null;

    private TabSettings TabOptions => _services.Settings.Current.Panels.Tabs;

    private void WireTabs(PanelHost host)
    {
        var strip = host.Tabs;
        strip.TabClicked += (_, i) =>
        {
            SetActive(Side(host), focus: false);
            ShowTab(Side(host), i);
            host.View.Focus();
        };
        strip.CloseRequested += (_, i) => CloseTab(Side(host), i);
        strip.NewRequested += (_, _) =>
        {
            SetActive(Side(host), focus: false);
            NewTab(host);
        };
        strip.MenuRequested += (_, r) => ShowTabMenu(host, r.Index, r.At);
        strip.ListRequested += (_, at) => ShowTabList(host, at);
        strip.Reordered += (_, r) => MoveTab(Side(host), r.From, r.To);
        strip.DraggedOut += (_, r) => DropTabAt(host, r.Index, r.Args.GetPosition(this));
    }

    internal void ShowTab(int side, int index)
    {
        var tabs = _tabs[side];
        if (index < 0 || index >= tabs.Count)
        {
            return;
        }

        var host = _panels[side];
        var tab = tabs[index];
        _tabOn[side] = index;
        if (!ReferenceEquals(host.Panel, tab.Panel))
        {
            HideComparison();
            host.Attach(tab.Panel);
            tab.Panel.SetShowHidden(_services.Settings.Current.Panels.ShowHiddenFiles);
            tab.Panel.SetSorter(_sorter);
            if (tab.Pending is { } pending && tab.Panel.Path.Length == 0)
            {
                tab.Pending = null;
                _ = NavigateOrParentAsync(tab.Panel, pending);
            }

            if (side == _active)
            {
                UpdateActiveInfo();
                ScheduleShellMenu();
            }
        }

        UpdateTabStrip(side);
    }

    private void UpdateTabStrip(int side)
    {
        var host = _panels[side];
        var tabs = _tabs[side];
        host.Tabs.Items = [.. tabs.Select(Look)];
        host.Tabs.Selected = _tabOn[side];
        host.Tabs.IsVisible = _services.Settings.Current.Layout.TabBar switch
        {
            TabBarMode.Always => true,
            TabBarMode.Never => false,
            _ => tabs.Count > 1,
        };
    }

    private static TabLook Look(PanelTab tab)
    {
        var path = tab.Path;
        var title = Core.Vfs.ShellPlace.IsPlace(path) ? Core.Vfs.ShellPlace.ShortName(path) : WindowsPath.GetName(path) is { Length: > 0 } name ? name : path.Length > 0 ? path.TrimEnd('\\') : "…";
        var shown = Core.Vfs.ShellPlace.Display(path);
        var tip = tab.Locked ? Loc.Format(AppStrings.Tabs_LockedTip, shown) : shown;
        return new TabLook(title, tip, tab.Locked, tab.Colour);
    }

    private void UpdateTabTitles(FilePanel panel)
    {
        for (var side = 0; side < 2; side++)
        {
            if (_tabs[side].Any(t => ReferenceEquals(t.Panel, panel)) && _panels is not null)
            {
                UpdateTabStrip(side);
            }
        }
    }

    private void ApplyTabSettings(AppSettings s)
    {
        foreach (var host in _panels)
        {
            host.Tabs.Colours = s.Panels.Tabs.Colours;
            host.Tabs.Wrap = s.Panels.Tabs.Many == ManyTabs.Rows;
        }

        foreach (var tab in _tabs.SelectMany(t => t).Where(t => HostOf(t.Panel) is null))
        {
            tab.Panel.SetSorter(_sorter);
        }

        UpdateTabStrip(0);
        UpdateTabStrip(1);
    }

    private FilePanel NewModel(PanelView view, SortSpec? sort = null, PanelFilter? filter = null)
    {
        var panel = new FilePanel(_services.PanelFiles, _sorter, a => Dispatcher.UIThread.Post(a));
        panel.SetShowHidden(_services.Settings.Current.Panels.ShowHiddenFiles);
        panel.HistoryLimit = _services.Settings.Current.Layout.BackForward.StepsKept;
        panel.SetView(view);
        if (sort is { } s)
        {
            panel.SetSort(s);
        }

        if (filter is not null)
        {
            panel.SetFilter(filter);
        }

        PanelHost.ApplyModel(panel, _services.Settings.Current);
        WireModel(panel);
        return panel;
    }

    internal PanelTab? NewTab(PanelHost host, string? path = null, string? focus = null)
    {
        var side = Side(host);
        if (_tabs[side].Count >= MaxTabs)
        {
            ShowNotice(Loc.Format(AppStrings.Tabs_TooMany, MaxTabs));
            return null;
        }

        var current = host.Panel;
        var same = path is not null || TabOptions.NewTab == NewTabOpens.SameFolder;
        var folder = path ?? (same ? TabFolder(_tabs[side][_tabOn[side]]) : NewTabPlace());
        var model = NewModel(same ? current.View : _services.Settings.Current.Panels.NewPanelView, same ? current.Sort : null);
        var tab = new PanelTab(model);
        var at = _tabOn[side] + 1;
        _tabs[side].Insert(at, tab);
        ShowTab(side, at);
        _ = NavigateOrParentAsync(model, folder, focus ?? (path is null && same && !current.IsUp(current.Focus) ? current.FocusedEntry?.Name : null));
        if (side == _active)
        {
            host.View.Focus();
        }

        return tab;
    }

    private string NewTabPlace()
        => TabOptions.NewTabPlace is { Length: > 0 } place ? place : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string TabFolder(PanelTab tab) => tab.Panel.Results is { } results ? results.ReturnPath : tab.Path;

    private static async Task NavigateOrParentAsync(FilePanel panel, string path, string? focus = null)
    {
        for (var p = path; p is { Length: > 0 }; p = WindowsPath.GetParent(p))
        {
            if (await panel.NavigateAsync(p, p == path ? focus : null))
            {
                return;
            }
        }
    }

    internal bool CloseTab(int side, int index)
    {
        var tabs = _tabs[side];
        if (index < 0 || index >= tabs.Count)
        {
            return false;
        }

        if (tabs[index].Locked)
        {
            ShowNotice(AppStrings.Tabs_LockedNoClose);
            return false;
        }

        if (tabs.Count == 1)
        {
            return false;
        }

        var tab = tabs[index];
        if (index == _tabOn[side])
        {
            ShowTab(side, index + 1 < tabs.Count ? index + 1 : index - 1);
        }

        tabs.RemoveAt(index);
        if (_tabOn[side] > index)
        {
            _tabOn[side]--;
        }

        _closedTabs.Add((side, index, Saved(side, tab)));
        if (_closedTabs.Count > MaxClosedTabs)
        {
            _closedTabs.RemoveAt(0);
        }

        tab.Panel.Dispose();
        UpdateTabStrip(side);
        return true;
    }

    private void CloseOtherTabs(int side, int keep)
    {
        var kept = _tabs[side][keep];
        foreach (var tab in _tabs[side].Where(t => t != kept && !t.Locked).ToList())
        {
            CloseTab(side, _tabs[side].IndexOf(tab));
        }
    }

    internal bool ReopenTab()
    {
        if (_closedTabs.Count == 0)
        {
            ShowNotice(AppStrings.Tabs_NoneClosed);
            return false;
        }

        var (side, index, saved) = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);
        if (_tabs[side].Count >= MaxTabs)
        {
            return false;
        }

        var tab = FromSaved(saved);
        var at = Math.Clamp(index, 0, _tabs[side].Count);
        _tabs[side].Insert(at, tab);
        if (_tabOn[side] >= at)
        {
            _tabOn[side]++;
        }

        SetActive(side, focus: true);
        ShowTab(side, at);
        return true;
    }

    private void CopyTab(int fromSide, int index, int toSide, int at)
    {
        if (_tabs[toSide].Count >= MaxTabs)
        {
            ShowNotice(Loc.Format(AppStrings.Tabs_TooMany, MaxTabs));
            return;
        }

        var source = _tabs[fromSide][index];
        var tab = FromSaved(Saved(toSide, source) with { Locked = false, Path = TabFolder(source) });
        var focus = source.Panel.FocusedEntry is { } f && !source.Panel.IsUp(source.Panel.Focus) ? f.Name : null;
        tab.Pending = null;
        _tabs[toSide].Insert(at, tab);
        if (_tabOn[toSide] >= at && toSide == fromSide)
        {
            _tabOn[toSide] = Math.Min(_tabOn[toSide] + 1, _tabs[toSide].Count - 1);
        }

        SetActive(toSide, focus: true);
        ShowTab(toSide, at);
        _ = NavigateOrParentAsync(tab.Panel, TabFolder(source), focus);
    }

    internal void MoveTabToOther(int side, int index, int? at = null)
    {
        var other = 1 - side;
        var tabs = _tabs[side];
        if (index < 0 || index >= tabs.Count)
        {
            return;
        }

        if (tabs.Count == 1)
        {
            ShowNotice(AppStrings.Tabs_LastStays);
            return;
        }

        if (_tabs[other].Count >= MaxTabs)
        {
            ShowNotice(Loc.Format(AppStrings.Tabs_TooMany, MaxTabs));
            return;
        }

        var tab = tabs[index];
        if (index == _tabOn[side])
        {
            ShowTab(side, index + 1 < tabs.Count ? index + 1 : index - 1);
        }

        tabs.RemoveAt(index);
        if (_tabOn[side] > index)
        {
            _tabOn[side]--;
        }

        UpdateTabStrip(side);
        var to = Math.Clamp(at ?? _tabs[other].Count, 0, _tabs[other].Count);
        _tabs[other].Insert(to, tab);
        if (_tabOn[other] >= to)
        {
            _tabOn[other]++;
        }

        SetActive(other, focus: true);
        ShowTab(other, to);
    }

    private void MoveTab(int side, int from, int to)
    {
        var tabs = _tabs[side];
        if (from < 0 || from >= tabs.Count || to < 0 || to >= tabs.Count || from == to)
        {
            return;
        }

        var shown = tabs[_tabOn[side]];
        var tab = tabs[from];
        tabs.RemoveAt(from);
        tabs.Insert(to, tab);
        _tabOn[side] = tabs.IndexOf(shown);
        UpdateTabStrip(side);
    }

    private void DropTabAt(PanelHost from, int index, Point at)
    {
        var other = OtherPanel(from);
        if (!other.IsVisible || this.InputHitTest(at) is not Visual v || !(ReferenceEquals(v, other) || other.IsVisualAncestorOf(v)))
        {
            return;
        }

        var place = other.Tabs.IsVisible && this.TranslatePoint(at, other.Tabs) is { } p && new Rect(other.Tabs.Bounds.Size).Contains(p) ? other.Tabs.IndexAt(p) : -1;
        MoveTabToOther(Side(from), index, place >= 0 ? place : null);
    }

    private void NextTab(PanelHost host, int step)
    {
        var side = Side(host);
        var count = _tabs[side].Count;
        if (count > 1)
        {
            ShowTab(side, (((_tabOn[side] + step) % count) + count) % count);
            host.View.Focus();
        }
    }

    private void ToggleTabLock(int side, int index)
    {
        var tab = _tabs[side][index];
        tab.Locked = !tab.Locked;
        UpdateTabStrip(side);
    }

    private void SetTabColour(int side, int index, NameColour colour)
    {
        _tabs[side][index].Colour = colour;
        UpdateTabStrip(side);
    }

    private bool DivertFromLockedTab(FilePanel panel, string path, string? focus)
    {
        if (TabOptions.Locked != LockedTab.StaysInFolder || HostOf(panel) is not { } host || !_tabs[Side(host)].Any(t => ReferenceEquals(t.Panel, panel) && t.Locked))
        {
            return false;
        }

        Dispatcher.UIThread.Post(() => NewTab(host, path, focus));
        return true;
    }

    private bool RunTabCommand(string id, PanelHost host)
    {
        var side = Side(host);
        var on = _tabOn[side];
        switch (id)
        {
            case "Tab.New": NewTab(host); return true;
            case "Tab.Close": CloseTab(side, on); return true;
            case "Tab.Next": NextTab(host, 1); return true;
            case "Tab.Previous": NextTab(host, -1); return true;
            case "Tab.Reopen": ReopenTab(); return true;
            case "Tab.Duplicate": CopyTab(side, on, side, on + 1); return true;
            case "Tab.CloseOthers": CloseOtherTabs(side, on); return true;
            case "Tab.Lock": ToggleTabLock(side, on); return true;
            case "Tab.MoveToOther": MoveTabToOther(side, on); return true;
            case "Tab.CopyToOther": CopyTab(side, on, 1 - side, _tabs[1 - side].Count); return true;
            default: return false;
        }
    }

    private static readonly HashSet<string> _tabCommands =
        ["Tab.New", "Tab.Close", "Tab.Next", "Tab.Previous", "Tab.Reopen", "Tab.Duplicate", "Tab.CloseOthers", "Tab.Lock", "Tab.MoveToOther", "Tab.CopyToOther"];

    internal static bool IsTabCommand(string id) => _tabCommands.Contains(id);

    private bool TabCommandEnabled(string id, PanelHost host)
    {
        var side = Side(host);
        var tab = _tabs[side][_tabOn[side]];
        return id switch
        {
            "Tab.Close" => _tabs[side].Count > 1 && !tab.Locked,
            "Tab.Next" or "Tab.Previous" or "Tab.MoveToOther" => _tabs[side].Count > 1,
            "Tab.CloseOthers" => _tabs[side].Any(t => t != tab && !t.Locked),
            "Tab.Reopen" => _closedTabs.Count > 0,
            _ => true,
        };
    }

    private bool TabChecked(string id, PanelHost host) => id == "Tab.Lock" && _tabs[Side(host)][_tabOn[Side(host)]].Locked;

    internal ContextMenu ShowTabMenu(PanelHost host, int index, Point at)
    {
        var side = Side(host);
        SetActive(side, focus: false);
        if (index < 0 || index >= _tabs[side].Count)
        {
            index = _tabOn[side];
        }

        var tab = _tabs[side][index];
        var menu = new ContextMenu
        {
            PlacementTarget = host.Tabs,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(at, new Size(1, 1)),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
        };
        MenuItem Item(string header, string? commandId, Action run, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            if (commandId is not null && _services.Commands.FirstChord(commandId) is { } chord && KeyMapping.ToGesture(chord) is { } gesture)
            {
                mi.InputGesture = gesture;
            }

            mi.Click += (_, _) => run();
            menu.Items.Add(mi);
            return mi;
        }

        var count = _tabs[side].Count;
        Item(AppStrings.Tabs_New, "Tab.New", () => NewTab(host));
        Item(AppStrings.Tabs_Duplicate, "Tab.Duplicate", () => CopyTab(side, _tabs[side].IndexOf(tab), side, _tabs[side].IndexOf(tab) + 1));
        Item(tab.Locked ? AppStrings.Tabs_Unlock : AppStrings.Tabs_Lock, "Tab.Lock", () => ToggleTabLock(side, _tabs[side].IndexOf(tab)));
        var colours = new MenuItem { Header = AppStrings.Tabs_Colour };
        foreach (var colour in (NameColour[])[NameColour.Normal, .. NamedColours])
        {
            var c = colour;
            var mi = new MenuItem
            {
                Header = c == NameColour.Normal ? AppStrings.Tabs_NoColour : ColourName(c),
                Icon = c == NameColour.Normal ? null : Swatch(c),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = tab.Colour == c,
            };
            mi.Click += (_, _) => SetTabColour(side, _tabs[side].IndexOf(tab), c);
            colours.Items.Add(mi);
        }

        menu.Items.Add(colours);
        menu.Items.Add(new Separator());
        Item(side == 0 ? AppStrings.Tabs_MoveRight : AppStrings.Tabs_MoveLeft, "Tab.MoveToOther", () => MoveTabToOther(side, _tabs[side].IndexOf(tab)), enabled: count > 1);
        Item(side == 0 ? AppStrings.Tabs_CopyRight : AppStrings.Tabs_CopyLeft, "Tab.CopyToOther", () => CopyTab(side, _tabs[side].IndexOf(tab), 1 - side, _tabs[1 - side].Count));
        menu.Items.Add(new Separator());
        Item(AppStrings.Tabs_Close, "Tab.Close", () => CloseTab(side, _tabs[side].IndexOf(tab)), enabled: count > 1 && !tab.Locked);
        Item(AppStrings.Tabs_CloseOthers, "Tab.CloseOthers", () => CloseOtherTabs(side, _tabs[side].IndexOf(tab)), enabled: _tabs[side].Any(t => t != tab && !t.Locked));
        Item(AppStrings.Tabs_Reopen, "Tab.Reopen", () => ReopenTab(), enabled: _closedTabs.Count > 0);
        menu.Items.Add(new Separator());
        Item(AppStrings.Tabs_Options, null, () => _ = OpenOptionsAsync("panels"));
        TabMenu = menu;
        menu.Closed += (_, _) =>
        {
            TabMenu = null;
            host.View.Focus();
        };
        menu.Open(host.Tabs);
        return menu;
    }

    internal ContextMenu ShowTabList(PanelHost host, Point at)
    {
        var side = Side(host);
        var menu = new ContextMenu
        {
            PlacementTarget = host.Tabs,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(at, new Size(1, 1)),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomLeft,
        };
        for (var i = 0; i < _tabs[side].Count; i++)
        {
            var index = i;
            var look = Look(_tabs[side][i]);
            var mi = new MenuItem { Header = look.Title, ToggleType = MenuItemToggleType.Radio, IsChecked = i == _tabOn[side] };
            ToolTip.SetTip(mi, look.Tip);
            mi.Click += (_, _) =>
            {
                SetActive(side, focus: false);
                ShowTab(side, index);
            };
            menu.Items.Add(mi);
        }

        TabMenu = menu;
        menu.Closed += (_, _) =>
        {
            TabMenu = null;
            host.View.Focus();
        };
        menu.Open(host.Tabs);
        return menu;
    }

    private SavedTab Saved(int side, PanelTab tab)
    {
        var panel = tab.Panel;
        var folder = TabFolder(tab);
        if (_services.Archives.LocateCached(folder) is { } archive && System.IO.Path.GetDirectoryName(archive.FileOnDisk) is { Length: > 0 } outside)
        {
            folder = outside;
        }

        return new SavedTab(side, folder, panel.Path.Length > 0 ? panel.View : null, panel.Path.Length > 0 ? panel.Sort : null, tab.Locked, tab.Colour,
            panel.Filter?.Mask.Text, panel.Filter?.Folders ?? false);
    }

    private PanelTab FromSaved(SavedTab saved)
    {
        var filter = saved.Filter is { } text && FileMask.FromHistory(text) is { } mask ? new PanelFilter(mask, saved.FilterFolders) : null;
        var view = saved.View is { } v && (!PanelViews.IsOwn(v) || IsMadeView(v)) ? v : _services.Settings.Current.Panels.NewPanelView;
        return new PanelTab(NewModel(view, saved.Sort, filter)) { Locked = saved.Locked, Colour = saved.Colour, Pending = saved.Path };
    }

    private List<SavedTab> SavedTabs()
        => [.. Enumerable.Range(0, 2).SelectMany(side => _tabs[side].Select(t => Saved(side, t))).Where(t => t.Path.Length > 0)];

    private void RestoreTabs()
    {
        var session = _services.Session;
        var s = _services.Settings.Current;
        if (session.Tabs.Count == 0 || s.General.StartFolders != StartFolders.WhereYouLeft)
        {
            UpdateTabStrip(0);
            UpdateTabStrip(1);
            return;
        }

        for (var side = 0; side < 2; side++)
        {
            var saved = session.Tabs.Where(t => t.Side == side).ToList();
            var on = side == 0 ? session.LeftTab : session.RightTab;
            var start = side == 0 ? _services.InitialPaths.Left : _services.InitialPaths.Right;
            if (on >= 0 && on < saved.Count && Remote.ServerAddress.IsServerPath(saved[on].Path))
            {
                var local = saved.FindIndex(t => string.Equals(t.Path.TrimEnd('\\'), start.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                if (local < 0)
                {
                    if (s.Panels.Tabs.AtStart == TabsAtStart.All)
                    {
                        foreach (var t in saved.Where(_ => _tabs[side].Count < MaxTabs))
                        {
                            _tabs[side].Add(FromSaved(t));
                        }

                        _tabOn[side] = 0;
                    }

                    continue;
                }

                on = local;
            }

            if (on < 0 || on >= saved.Count || !string.Equals(saved[on].Path.TrimEnd('\\'), start.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var shown = _tabs[side][0];
            shown.Locked = saved[on].Locked;
            shown.Colour = saved[on].Colour;
            if (saved[on].Sort is { } sort)
            {
                shown.Panel.SetSort(sort);
            }

            if (saved[on].Filter is { } text && FileMask.FromHistory(text) is { } mask)
            {
                shown.Panel.SetFilter(new PanelFilter(mask, saved[on].FilterFolders));
            }

            if (s.Panels.Tabs.AtStart == TabsAtStart.All)
            {
                for (var i = 0; i < saved.Count && _tabs[side].Count < MaxTabs; i++)
                {
                    if (i != on)
                    {
                        _tabs[side].Insert(i < on ? _tabs[side].IndexOf(shown) : _tabs[side].Count, FromSaved(saved[i]));
                    }
                }

                _tabOn[side] = _tabs[side].IndexOf(shown);
            }
        }

        UpdateTabStrip(0);
        UpdateTabStrip(1);
    }
}
