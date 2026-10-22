// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.HotPaths.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App;

public partial class MainWindow
{
    private IReadOnlyList<DriveChip> _drives = [];
    private IReadOnlyList<DriveDetails> _driveDetails = [];
    private MenuFlyout? _driveMenu;
    private PanelHost? _driveMenuHost;
    private Dialogs.HotPathListDialog? _hotPathList;

    private HotPathSettings HotPaths => _services.Settings.Current.HotPaths;

    private DriveMenuSettings DriveMenu => _services.Settings.Current.DriveMenu;

    internal MenuFlyout? DriveMenuFlyout => _driveMenu;

    internal Dialogs.HotPathListDialog? OpenHotPathList => _hotPathList;

    internal IReadOnlyList<StripItem> DriveStripItems()
    {
        var items = new List<StripItem>();
        var menu = DriveMenu;
        foreach (var d in _drives.Where(d => menu.IsDriveShown(d.Root[0])))
        {
            items.Add(new StripItem(StripItemKind.Drive, d.Root.TrimEnd('\\'), d.Root, IsNetwork: d.IsNetwork, IsRemovable: d.IsRemovable, SeparatorAfter: menu.HasSeparatorAfter(d.Root[0])));
        }

        foreach (var place in menu.Places.Where(p => p.InStrip))
        {
            if (Drives.KnownFolder(place.Id) is { } path)
            {
                items.Add(new StripItem(StripItemKind.Place, PlaceLabel(place.Id), path, Tip: path));
            }
        }

        return items;
    }

    internal IReadOnlyList<StripItem> HotPathBarItems()
    {
        var items = new List<StripItem>();
        foreach (var hp in HotPaths.Paths.Where(p => p.InStrip))
        {
            var number = (hp.Slot <= 10 ? hp.Slot % 10 : hp.Slot).ToString(CultureInfo.InvariantCulture);
            var keys = HotPathSettings.KeyOf(hp.Slot) is { } key
                ? Loc.Format(AppStrings.Hot_TipKeys, key, hp.Slot % 10)
                : Loc.Format(AppStrings.Hot_TipSlot, hp.Slot);
            items.Add(new StripItem(StripItemKind.HotPath, hp.Name, hp.Slot.ToString(CultureInfo.InvariantCulture), number, $"{hp.Path}\n{keys}"));
        }

        return items;
    }

    private void UpdateStrips()
    {
        var drives = DriveStripItems();
        foreach (var host in _panels)
        {
            host.Strip.Items = drives;
            host.Strip.ShowAdd = false;
        }

        SharedStrip.Items = drives;
        SharedStrip.ShowAdd = false;
        HotPathBar.Items = HotPathBarItems();
    }

    private void OnStripItem(PanelHost host, StripItem item, bool otherPanel = false)
    {
        if (otherPanel && item.Kind is StripItemKind.HotPath)
        {
            GoHotPath(OtherPanel(host), int.Parse(item.Target, CultureInfo.InvariantCulture));
            return;
        }

        SetActive(Array.IndexOf(_panels, host), focus: true);
        switch (item.Kind)
        {
            case StripItemKind.Drive:
                GoToDrive(host, item.Target);
                break;
            case StripItemKind.Place:
                _ = host.Panel.NavigateAsync(item.Target);
                break;
            case StripItemKind.HotPath:
                GoHotPath(host, int.Parse(item.Target, CultureInfo.InvariantCulture));
                break;
            case StripItemKind.More:
                _ = ShowHotPathListAsync();
                break;
            case StripItemKind.Add:
                AddHotPath(host);
                break;
        }
    }

    internal ContextMenu ShowStripMenu(PanelHost host, DriveStrip strip, StripItem? item)
    {
        SetActive(Array.IndexOf(_panels, host), focus: false);
        var menu = new ContextMenu();
        MenuItem Item(string header, Action run, string? gesture = null, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            if (gesture is not null)
            {
                mi.InputGesture = KeyGesture.Parse(gesture);
            }

            mi.Click += (_, _) => run();
            menu.Items.Add(mi);
            return mi;
        }

        if (item is { Kind: StripItemKind.HotPath } && HotPaths[int.Parse(item.Target, CultureInfo.InvariantCulture)] is { } hp)
        {
            var slot = hp.Slot;
            var digit = slot <= 10 ? (slot % 10).ToString(CultureInfo.InvariantCulture) : null;
            Item(AppStrings.Hot_Open, () => GoHotPath(host, slot), digit is null ? null : "Ctrl+" + digit);
            Item(AppStrings.Hot_OpenOther, () => GoHotPath(OtherPanel(host), slot), digit is null ? null : "Ctrl+Alt+" + digit);
            menu.Items.Add(new Separator());
            Item(AppStrings.Hot_Edit, () => _ = EditHotPathAsync(slot));
            Item(AppStrings.Hot_PutHere, () => AssignHotPath(host, slot), digit is null ? null : "Ctrl+Shift+" + digit);
            Item(AppStrings.Hot_HideFromBar, () => _services.Settings.Update(s => s with { HotPaths = s.HotPaths.With(hp with { InStrip = false }) }));
            Item(AppStrings.Hot_Clear, () =>
            {
                _services.Settings.Update(s => s with { HotPaths = s.HotPaths.Without(slot) });
                ShowNotice(Loc.Format(AppStrings.Hot_Cleared, slot, hp.Name),
                    (AppStrings.Common_Undo, () => _services.Settings.Update(s => s with { HotPaths = s.HotPaths.With(hp) })));
            });
            menu.Items.Add(new Separator());
            Item(AppStrings.Hot_ListMenu, () => _ = ShowHotPathListAsync(), "Shift+F9");
        }
        else
        {
            var free = HotPaths.NextFreeSlot();
            Item(free is { } n ? Loc.Format(AppStrings.Hot_AddAs, n) : AppStrings.Hot_AllTaken, () => AddHotPath(host), "Ctrl+Shift+OemPlus", enabled: free is not null);
            Item(AppStrings.Hot_ListMenu, () => _ = ShowHotPathListAsync(), "Shift+F9");
            menu.Items.Add(new Separator());
            var bars = _services.Settings.Current.Layout.Bars;
            var drives = Item(AppStrings.Hot_DrivesStripMenu, () => UpdateBars(b => b with { DriveStrip = !b.DriveStrip }));
            drives.ToggleType = MenuItemToggleType.CheckBox;
            drives.IsChecked = bars.DriveStrip;
            var hot = Item(AppStrings.Hot_HotPathsBarMenu, () => UpdateBars(b => b with { HotPathBar = !b.HotPathBar }));
            hot.ToggleType = MenuItemToggleType.CheckBox;
            hot.IsChecked = bars.HotPathBar;
        }

        Item(AppStrings.Hot_OptionsMenu, () => _ = OpenOptionsAsync("hotpaths"));
        menu.Closed += (_, _) => host.View.Focus();
        menu.Open(strip);
        return menu;
    }

    private static string PlaceLabel(string id) => AppStrings.ResourceManager.GetString("Place_" + id, AppStrings.Culture) ?? id;

    private bool RunHotPathCommand(string id, PanelHost host, KeyChord? chord)
    {
        static int? Digit(string id, string prefix)
            => id.StartsWith(prefix, StringComparison.Ordinal) && id.Length == prefix.Length + 1 && char.IsAsciiDigit(id[^1]) ? id[^1] - '0' : null;

        if (Digit(id, "HotPaths.Go") is { } go)
        {
            if (chord is { } c && c.Modifiers == Core.Commands.KeyModifiers.Shift && !HotPaths.ShiftDigits)
            {
                return false;
            }

            GoHotPath(host, HotPathSettings.SlotOfDigit(go));
            return true;
        }

        if (Digit(id, "HotPaths.GoOther") is { } other)
        {
            GoHotPath(OtherPanel(host), HotPathSettings.SlotOfDigit(other));
            return true;
        }

        if (Digit(id, "HotPaths.Define") is { } define)
        {
            AssignHotPath(host, HotPathSettings.SlotOfDigit(define));
            return true;
        }

        switch (id)
        {
            case "HotPaths.AddCurrent":
                AddHotPath(host);
                return true;
            case "HotPaths.Menu":
                _ = ShowHotPathListAsync();
                return true;
            case "Drive.ChangeLeft":
                ToggleDriveMenu(_panels[0]);
                return true;
            case "Drive.ChangeRight":
                ToggleDriveMenu(_panels[1]);
                return true;
            default:
                return false;
        }
    }

    private static bool IsHotPathOrDriveMenuCommand(string id)
        => id.StartsWith("HotPaths.", StringComparison.Ordinal) || id is "Drive.ChangeLeft" or "Drive.ChangeRight";

    internal void GoHotPath(PanelHost host, int slot)
    {
        if (HotPaths[slot] is not { } hp)
        {
            ShowNotice(slot <= 10
                ? Loc.Format(AppStrings.Hot_EmptyWithKey, slot, slot % 10)
                : Loc.Format(AppStrings.Hot_Empty, slot));
            return;
        }

        _ = host.Panel.NavigateAsync(HotPathSettings.Expand(hp.Path));
    }

    internal void AddHotPath(PanelHost host)
    {
        if (HotPaths.NextFreeSlot() is { } slot)
        {
            AssignHotPath(host, slot);
        }
        else
        {
            ShowNotice(AppStrings.Hot_AllTakenNotice);
        }
    }

    internal void AssignHotPath(PanelHost host, int slot)
    {
        var path = host.Panel.Path;
        if (path.Length == 0)
        {
            return;
        }

        var before = HotPaths[slot];
        var hp = new HotPath(slot, HotPathSettings.DefaultName(path), path, before?.InStrip ?? true, before?.InMenu ?? true);
        _services.Settings.Update(s => s with { HotPaths = s.HotPaths.With(hp) });
        if (HotPaths.AskForName)
        {
            _ = EditHotPathAsync(slot);
            return;
        }

        var key = HotPathSettings.KeyOf(slot) is { } k ? $" — {k}" : "";
        ShowNotice(Loc.Format(AppStrings.Hot_IsNow, slot, hp.Name, key),
            (AppStrings.Hot_Rename, () => _ = EditHotPathAsync(slot)),
            (AppStrings.Common_Undo, () => _services.Settings.Update(s => s with { HotPaths = before is null ? s.HotPaths.Without(slot) : s.HotPaths.With(before) })));
    }

    internal async Task EditHotPathAsync(int slot)
    {
        var current = HotPaths[slot] ?? new HotPath(slot, HotPathSettings.DefaultName(ActivePanel.Panel.Path), ActivePanel.Panel.Path);
        var dialog = new Dialogs.HotPathEditDialog(current);
        if (await dialog.ShowDialog<HotPath?>(this) is { } saved)
        {
            _services.Settings.Update(s =>
            {
                var hp = s.HotPaths;
                if (saved.Slot != slot)
                {
                    hp = hp.Swap(slot, saved.Slot);
                }

                return s with { HotPaths = hp.With(saved) };
            });
        }

        ActivePanel.View.Focus();
    }

    internal async Task ShowHotPathListAsync()
    {
        if (_hotPathList is not null)
        {
            _hotPathList.Activate();
            return;
        }

        _hotPathList = new Dialogs.HotPathListDialog(_services.Settings, removed => ShowNotice(
            Loc.Format(AppStrings.Hot_Cleared, removed.Slot, removed.Name),
            (AppStrings.Common_Undo, () => _services.Settings.Update(s => s with { HotPaths = s.HotPaths.With(removed) }))));
        Dialogs.HotPathListResult? result;
        try
        {
            result = await _hotPathList.ShowDialog<Dialogs.HotPathListResult?>(this);
        }
        finally
        {
            _hotPathList = null;
        }

        var host = ActivePanel;
        switch (result)
        {
            case { Action: Dialogs.HotPathListAction.Go } r:
                GoHotPath(host, r.Slot);
                break;
            case { Action: Dialogs.HotPathListAction.GoOther } r:
                GoHotPath(OtherPanel(host), r.Slot);
                break;
            case { Action: Dialogs.HotPathListAction.Edit } r:
                await EditHotPathAsync(r.Slot);
                return;
            case { Action: Dialogs.HotPathListAction.Options }:
                await OpenOptionsAsync("hotpaths");
                return;
        }

        host.View.Focus();
    }

    private void ShowPathMenu(PanelHost host, Point point)
    {
        SetActive(Array.IndexOf(_panels, host), focus: false);
        var path = host.Panel.Path;
        var add = new MenuItem { Header = AppStrings.Hot_AddAsMenu };
        for (var slot = 1; slot <= 10; slot++)
        {
            var s = slot;
            var name = HotPaths[s]?.Name;
            var item = new MenuItem
            {
                Header = SlotRow(s.ToString(CultureInfo.InvariantCulture), name ?? AppStrings.Hot_Empty_Slot, name is null),
                InputGesture = KeyGesture.Parse(HotPathSettings.KeyOf(s)!),
            };
            item.Click += (_, _) => AssignHotPath(host, s);
            add.Items.Add(item);
        }

        add.Items.Add(new Separator());
        var next = HotPaths.NextFreeSlot();
        var nextItem = new MenuItem
        {
            Header = next is { } n ? Loc.Format(AppStrings.Hot_FirstFree, n) : AppStrings.Hot_AllSlotsTaken,
            IsEnabled = next is not null,
            InputGesture = new KeyGesture(Key.OemPlus, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift),
        };
        nextItem.Click += (_, _) => AddHotPath(host);
        add.Items.Add(nextItem);

        var copy = new MenuItem { Header = AppStrings.Hot_CopyPath };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(path);
            }
        };
        var menu = new ContextMenu { Items = { add, copy } };
        menu.Closed += (_, _) => host.View.Focus();
        menu.Open(host.DirectoryLine);
    }

    private static Control SlotRow(string number, string text, bool dim)
    {
        var slot = Text(number, 13, brush: "Brush.TertiaryText");
        slot.MinWidth = 18;
        slot.TextAlignment = TextAlignment.Right;
        var label = Text(text, 13, brush: dim ? "Brush.TertiaryText" : "Brush.Text");
        if (dim)
        {
            label.FontStyle = FontStyle.Italic;
        }

        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { slot, label } };
    }

    internal void ToggleDriveMenu(PanelHost host)
    {
        if (_driveMenu is { IsOpen: true })
        {
            var same = ReferenceEquals(_driveMenuHost, host);
            _driveMenu.Hide();
            if (same)
            {
                return;
            }
        }

        _workMenu?.Hide();
        SetActive(Array.IndexOf(_panels, host), focus: false);
        _driveMenuHost = host;
        var current = WindowsPath.GetRoot(host.Panel.Path);
        var flyout = _driveMenu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft, HorizontalOffset = host.DirectoryLine.DriveButtonBounds.X };
        FillDriveMenu(flyout, host);
        flyout.Closed += (_, _) =>
        {
            host.DirectoryLine.IsDriveMenuOpen = false;
            if (ReferenceEquals(_driveMenu, flyout))
            {
                _driveMenu = null;
            }

            if (!IsActive || FocusManager?.GetFocusedElement() is null or MenuItem)
            {
                host.View.Focus();
            }
        };
        host.DirectoryLine.IsDriveMenuOpen = true;
        HookDriveMenuKeys();
        flyout.ShowAt(host.DirectoryLine);

        Dispatcher.UIThread.Post(() =>
        {
            var items = flyout.Items.OfType<MenuItem>().Where(i => i.IsEnabled && i.Focusable).ToList();
            var start = items.FirstOrDefault(i => i.Tag is char c && current is { } root && char.ToUpperInvariant(root[0]) == c) ?? items.FirstOrDefault();
            start?.Focus(NavigationMethod.Directional);
        }, DispatcherPriority.Loaded);

        _ = RefreshDriveDetailsAsync(flyout, host);
    }

    private async Task RefreshDriveDetailsAsync(MenuFlyout flyout, PanelHost host)
    {
        var details = await Drives.ReadAsync(TimeSpan.FromSeconds(1.5)).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() =>
        {
            _driveDetails = details;
            if (flyout.IsOpen)
            {
                FillDriveMenu(flyout, host);
            }
        });
    }

    private void FillDriveMenu(MenuFlyout flyout, PanelHost host)
    {
        flyout.Items.Clear();
        var menu = DriveMenu;
        var current = WindowsPath.GetRoot(host.Panel.Path);

        void Header(string text)
        {
            var header = Text(text.ToUpperInvariant(), 11, FontWeight.SemiBold, "Brush.TertiaryText");
            flyout.Items.Add(new MenuItem { Header = header, IsEnabled = false, Focusable = false, Padding = new Thickness(10, 6, 10, 0), MinHeight = 0 });
        }

        void Add(MenuItem item, Action run, char? accessKey)
        {
            item.Click += (_, _) => run();
            if (accessKey is { } key)
            {
                item.Tag = char.ToUpperInvariant(key);
            }

            item.AddHandler(KeyDownEvent, OnDriveMenuKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            flyout.Items.Add(item);
        }

        var drives = _drives.Where(d => menu.IsDriveShown(d.Root[0])).ToList();
        if (drives.Count > 0)
        {
            Header(AppStrings.OptSec_Drives);
        }

        foreach (var drive in drives)
        {
            var details = _driveDetails.FirstOrDefault(d => string.Equals(d.Root, drive.Root, StringComparison.OrdinalIgnoreCase));
            var root = drive.Root;
            var isCurrent = string.Equals(root, current, StringComparison.OrdinalIgnoreCase);
            Add(new MenuItem { Header = DriveRow(drive, details, menu.FreeSpace, isCurrent), Icon = MenuIcon(drive.IsNetwork ? NetworkGlyph : DriveGlyph) }, () =>
            {
                if (details is { Ready: false } && drive.IsNetwork)
                {
                    ShowNotice(Loc.Format(AppStrings.Hot_NotConnected, details.Share ?? root, root.TrimEnd('\\')));
                    return;
                }

                GoToDrive(host, root);
            }, root[0]);
            if (menu.HasSeparatorAfter(root[0]))
            {
                flyout.Items.Add(new Separator());
            }
        }

        var places = menu.Places.Where(p => p.InMenu).ToList();
        if (places.Count > 0)
        {
            flyout.Items.Add(new Separator());
            Header(AppStrings.Copy_Group_Folders);
        }

        foreach (var place in places)
        {
            switch (place.Id)
            {
                case DriveMenuSettings.OtherPanel:
                    var other = OtherPanel(host).Panel.Path;
                    Add(new MenuItem { Header = Row(AppStrings.Place_OtherPanel, other), Icon = MenuIcon(SwapGlyph) }, () => _ = host.Panel.NavigateAsync(other), null);
                    break;
                case DriveMenuSettings.Network:
                    flyout.Items.Add(new MenuItem { Header = Row(AppStrings.Place_Network, AppStrings.Hot_NetworkLater), Icon = MenuIcon(NetworkGlyph), IsEnabled = false });
                    break;
                case DriveMenuSettings.Registry:
                    flyout.Items.Add(new MenuItem { Header = Row(AppStrings.Place_Registry, AppStrings.Hot_RegistryLater), Icon = MenuIcon(RegistryGlyph), IsEnabled = false });
                    break;
                default:
                    if (Drives.KnownFolder(place.Id) is { } path)
                    {
                        Add(new MenuItem { Header = Row(PlaceLabel(place.Id), place.Id == DriveMenuSettings.Cloud ? AppStrings.Hot_CloudHint : null), Icon = MenuIcon(FolderGlyph) }, () => _ = host.Panel.NavigateAsync(path), null);
                    }

                    break;
            }
        }

        var hot = HotPaths.Paths.Where(p => p.InMenu).ToList();
        if (hot.Count > 0)
        {
            flyout.Items.Add(new Separator());
            Header(AppStrings.Copy_Group_HotPaths);
        }

        foreach (var hp in hot)
        {
            var slot = hp.Slot;
            var row = Row(hp.Name, hp.Path);
            ((StackPanel)((Grid)row).Children[0]).Children.Insert(0, new TextBlock { Text = (slot <= 10 ? slot % 10 : slot).ToString(CultureInfo.InvariantCulture), MinWidth = 18, TextAlignment = TextAlignment.Right }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText"));
            Add(new MenuItem { Header = row }, () => GoHotPath(host, slot), slot <= 10 ? (char)('0' + (slot % 10)) : null);
        }
    }

    private void OnDriveMenuKey(object? sender, KeyEventArgs e) => PickFromDriveMenu(e);

    private void HookDriveMenuKeys()
    {
        if (_driveMenuKeysHooked)
        {
            return;
        }

        _driveMenuKeysHooked = true;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (PickMenu is not { } menu || (e.KeyModifiers & (Avalonia.Input.KeyModifiers.Alt | Avalonia.Input.KeyModifiers.Control)) != 0)
            {
                return;
            }

            if (e.Key == Key.Escape)
            {
                menu.Hide();
                e.Handled = true;
            }
            else if (e.Key is Key.Up or Key.Down && menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsEnabled && i.Focusable) is { } first)
            {
                first.Focus(NavigationMethod.Directional);
                e.Handled = true;
            }
            else if (!PickFromDriveMenu(e))
            {
                e.Handled = e.Key is not (Key.LeftShift or Key.RightShift or Key.Tab);
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, (_, e) =>
        {
            if (PickMenu is not null)
            {
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private bool _driveMenuKeysHooked;

    private MenuFlyout? PickMenu => _driveMenu is { IsOpen: true } drive ? drive : _workMenu is { IsOpen: true } work ? work : null;

    private bool PickFromDriveMenu(KeyEventArgs e)
    {
        if (PickMenu is not { } menu || (e.KeyModifiers & ~Avalonia.Input.KeyModifiers.Shift) != 0)
        {
            return false;
        }

        char? key = e.Key is >= Key.A and <= Key.Z ? (char)('A' + (e.Key - Key.A))
            : e.Key is >= Key.D0 and <= Key.D9 ? (char)('0' + (e.Key - Key.D0))
            : e.Key is >= Key.NumPad0 and <= Key.NumPad9 ? (char)('0' + (e.Key - Key.NumPad0))
            : null;
        if (key is null || menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Tag is char c && c == key) is not { } item)
        {
            return false;
        }

        e.Handled = true;
        menu.Hide();
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        return true;
    }

    private Control DriveRow(DriveChip drive, DriveDetails? details, bool freeSpace, bool isCurrent)
    {
        var letter = Text(drive.Root.TrimEnd('\\'), 13, isCurrent ? FontWeight.SemiBold : FontWeight.Normal);
        var label = Text(details?.Share ?? details?.Label ?? "", 13, brush: "Brush.SecondaryText");
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { letter, label } };
        Control? right = null;
        if (details is { Ready: false })
        {
            right = Text(drive.IsNetwork ? AppStrings.Drive_NotConnected : AppStrings.Drive_NotReady, 12, brush: "Brush.TertiaryText");
        }
        else if (freeSpace && details is { Total: > 0 } cloud && DiskSpace.IsMadeUp(cloud.Total))
        {
            right = Text(AppStrings.Drive_CloudDrive, 12, brush: "Brush.SecondaryText");
        }
        else if (freeSpace && details is { Total: > 0 } d)
        {
            var format = DisplayFormat.From(_services.Settings.Current.Panels, CultureInfo.CurrentCulture);
            var used = 1 - ((double)d.Free / d.Total);
            var meter = new Border { Width = 56, Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true }.Res(Border.BackgroundProperty, "Brush.Pressed");
            meter.Child = new Border { Width = 56 * Math.Clamp(used, 0, 1), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) }.Res(Border.BackgroundProperty, "Brush.Accent");
            var amount = Text(Loc.Format(AppStrings.Drive_FreeOf, format.Size(d.Free), format.Size(d.Total)), 12, brush: "Brush.SecondaryText");
            amount.MinWidth = 140;
            amount.TextAlignment = TextAlignment.Right;
            right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { meter, amount } };
        }

        return RowWith(left, right);
    }

    private static Control Row(string text, string? note)
        => RowWith(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Text(text, 13) } },
            note is null ? null : new TextBlock { Text = note, FontSize = 12, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText"));

    private static Grid RowWith(Control left, Control? right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinWidth = 300 };
        grid.Children.Add(left);
        if (right is not null)
        {
            right.Margin = new Thickness(24, 0, 0, 0);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }

        return grid;
    }

    private const string DriveGlyph = "Icon.MenuDrive";
    private const string NetworkGlyph = "Icon.MenuNetwork";
    private const string FolderGlyph = "Icon.Folder";
    private const string SwapGlyph = "Panel.Swap";
    private const string RegistryGlyph = "Icon.Registry";

    private static Control MenuIcon(string name) => AppIcons.Glyph(name, 16, 16, 1.2);
}
