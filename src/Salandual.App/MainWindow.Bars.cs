// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Bars.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.UserMenu;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App;

internal sealed record BlockDrop(BlockMove Move, Rect Mark);

public partial class MainWindow
{
    private IReadOnlyList<(UserMenuItem Item, bool SeparatorAfter)> _barButtons = [];
    private string _barRowsKey = "";
    private BarBlock? _grippedBlock;

    private BarBlockSettings Blocks => _services.Settings.Current.Layout.Blocks;

    internal IReadOnlyList<IReadOnlyList<DriveStrip>> BarRowStrips
        => [.. BarRows.Children.OfType<BarRow>().Where(r => r.IsVisible).Select(r => (IReadOnlyList<DriveStrip>)[.. r.Children.OfType<DriveStrip>()])];

    internal IReadOnlyList<StripItem> UserMenuBarItems()
    {
        _barButtons = UserMenuSettings.BarItems();
        if (_barButtons.Count == 0)
        {
            return [new StripItem(StripItemKind.UserMenu, AppStrings.UserMenuBar_Hint, HintTarget, Tip: AppStrings.UserMenuBar_HintTip, Glyph: "UserMenu.Open", Dim: true)];
        }

        var iconsOnly = Blocks.IconsOnly;
        return [.. _barButtons.Select((b, i) =>
        {
            var item = b.Item;
            var image = item.Kind == UserMenuItemKind.Submenu ? null : UserMenuIcon(item)?.Source;
            var glyph = item.Kind switch { UserMenuItemKind.Submenu => "Icon.Folder", UserMenuItemKind.Script => "Icon.Script", _ => "UserMenu.Open" };
            return new StripItem(StripItemKind.UserMenu, iconsOnly ? "" : item.PlainName, i.ToString(CultureInfo.InvariantCulture),
                Tip: BarButtonTip(item), SeparatorAfter: b.SeparatorAfter, Image: image, Glyph: glyph);
        })];
    }

    internal const string HintTarget = "hint";

    private static string BarButtonTip(UserMenuItem item)
    {
        var what = item.Kind switch
        {
            UserMenuItemKind.Submenu => Loc.Format(AppStrings.UserMenuBar_SubmenuTip, item.Items.Count(i => i.Kind != UserMenuItemKind.Separator)),
            UserMenuItemKind.Script => AppStrings.UserMenuBar_ScriptTip,
            _ => item.Program,
        };
        return item.Key is { } key ? $"{item.PlainName}  ({key})\n{what}" : $"{item.PlainName}\n{what}";
    }

    internal UserMenuItem? BarButton(StripItem? chip)
        => chip is { Kind: StripItemKind.UserMenu } && int.TryParse(chip.Target, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < _barButtons.Count
            ? _barButtons[i].Item
            : null;

    private void OnUserMenuBarItem(PanelHost host, StripItem chip)
    {
        if (chip.Target == HintTarget)
        {
            _ = OpenOptionsAsync(UserMenuPage);
            return;
        }

        if (chip.Kind == StripItemKind.More)
        {
            ShowBarMenu(host, [.. UserMenuBar.Overflowed.Select(BarButton).OfType<UserMenuItem>()], chip);
            return;
        }

        switch (BarButton(chip))
        {
            case { Kind: UserMenuItemKind.Submenu } sub:
                ShowBarMenu(host, sub.Items, chip);
                break;
            case { IsRunnable: true } item:
                _ = RunUserMenuItemAsync(host, item);
                break;
        }
    }

    private void ShowBarMenu(PanelHost host, IReadOnlyList<UserMenuItem> items, StripItem chip)
    {
        if (UserMenuBlocked(host))
        {
            return;
        }

        UserMenuPopup?.Close();
        var menu = new ContextMenu { PlacementTarget = UserMenuBar, Placement = PlacementMode.Bottom };
        if (UserMenuBar.ChipBounds(chip) is { } at)
        {
            menu.Placement = PlacementMode.AnchorAndGravity;
            menu.PlacementRect = at;
            menu.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.BottomLeft;
            menu.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
        }

        AddUserMenuItems(menu.Items, items, host, UserMenuContextFor(host), UserMenuSettings.Unfit);
        UserMenuPopup = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(UserMenuPopup, menu))
            {
                UserMenuPopup = null;
            }

            host.View.Focus();
        };
        menu.Open(UserMenuBar);
    }

    private void ArrangeBars()
    {
        var layout = _services.Settings.Current.Layout;
        var bars = layout.Bars;
        var blocks = layout.Blocks;
        var drivesShared = bars.DriveStrip && layout.Strip == StripMode.Shared;
        var arrangement = blocks.Arrange(BlockShown, drivesShared);

        UserMenuBar.IsVisible = bars.UserMenuBar;
        HotPathBar.IsVisible = bars.HotPathBar;
        BackForwardBar.IsVisible = layout.BackForward.InBar;
        SharedStrip.IsVisible = drivesShared;
        UserMenuBar.Grip = HotPathBar.Grip = BackForwardBar.Grip = !blocks.Locked;

        var rows = arrangement.Rows.Select(r => (IReadOnlyList<BarBlock>)r).ToList();
        if (drivesShared && arrangement.WithDrives.Count > 0)
        {
            rows.Add(arrangement.WithDrives);
        }

        foreach (var block in BarBlockSettings.All)
        {
            var sized = rows.Any(r => r.Contains(block) && r[^1] != block);
            Strip(block).EndHandle = sized && !blocks.Locked;
            Strip(block).Length = sized ? blocks.Length(block) : null;
        }

        var key = string.Join("/", arrangement.Rows.Select(r => string.Join("+", r))) + "|" + drivesShared + "+" + string.Join("+", arrangement.WithDrives);
        if (key == _barRowsKey)
        {
            return;
        }

        _barRowsKey = key;
        foreach (var strip in BlockStrips.Append(SharedStrip))
        {
            (strip.Parent as Panel)?.Children.Remove(strip);
        }

        BarRows.Children.Clear();
        foreach (var row in arrangement.Rows)
        {
            BarRows.Children.Add(Row([.. row.Select(Strip)], row.Count > 1 ? Strip(row[^1]) : null));
        }

        if (drivesShared)
        {
            BarRows.Children.Add(Row([SharedStrip, .. arrangement.WithDrives.Select(Strip)], arrangement.WithDrives.Count > 0 ? Strip(arrangement.WithDrives[^1]) : null));
        }

        var hidden = new BarRow { IsVisible = false };
        foreach (var strip in BlockStrips.Append(SharedStrip).Where(s => s.Parent is null))
        {
            hidden.Children.Add(strip);
        }

        BarRows.Children.Add(hidden);

        BarRow Row(IReadOnlyList<DriveStrip> strips, DriveStrip? fill)
        {
            var row = new BarRow { Fill = fill, Margin = new Thickness(8, 4, 8, 0) };
            for (var i = 0; i < strips.Count; i++)
            {
                strips[i].DividerBefore = i > 0;
                strips[i].SizeToItems = strips.Count > 1 && strips[i] != fill;
                row.Children.Add(strips[i]);
            }

            return row;
        }
    }

    private bool BlockShown(BarBlock block)
    {
        var layout = _services.Settings.Current.Layout;
        return block switch
        {
            BarBlock.HotPaths => layout.Bars.HotPathBar,
            BarBlock.UserMenu => layout.Bars.UserMenuBar,
            _ => layout.BackForward.InBar,
        };
    }

    private DriveStrip Strip(BarBlock block) => block switch
    {
        BarBlock.HotPaths => HotPathBar,
        BarBlock.UserMenu => UserMenuBar,
        _ => BackForwardBar,
    };

    private IEnumerable<DriveStrip> BlockStrips => BarBlockSettings.All.Select(Strip);

    private BarBlock? BlockOf(DriveStrip strip) => BarBlockSettings.All.Where(b => Strip(b) == strip).Select(b => (BarBlock?)b).FirstOrDefault();

    private static string WithBlock(BarBlock block) => block switch
    {
        BarBlock.HotPaths => AppStrings.Bars_WithHotPaths,
        BarBlock.UserMenu => AppStrings.Bars_WithUserMenu,
        _ => AppStrings.Bars_WithBackForward,
    };

    private void UpdateBlocks(Func<BarBlockSettings, BarBlockSettings> change)
        => _services.Settings.Update(s => s with { Layout = s.Layout with { Blocks = change(s.Layout.Blocks) } });

    private MenuItem WhereMenu(BarBlock block)
    {
        var layout = _services.Settings.Current.Layout;
        var blocks = layout.Blocks;
        var drivesShared = layout.Bars.DriveStrip && layout.Strip == StripMode.Shared;
        var arrangement = blocks.Arrange(BlockShown, drivesShared);
        var place = blocks.Effective(block, BlockShown, drivesShared);
        var row = arrangement.WithDrives.Contains(block) ? arrangement.WithDrives : arrangement.Rows.FirstOrDefault(r => r.Contains(block)) ?? [block];
        var sub = new MenuItem { Header = AppStrings.Bars_Where };

        MenuItem Radio(string header, bool on, Action pick, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = on, IsEnabled = enabled };
            mi.Click += (_, _) => pick();
            sub.Items.Add(mi);
            return mi;
        }

        Radio(AppStrings.Bars_OwnRow, place == BlockPlace.OwnRow, () =>
        {
            if (place != BlockPlace.OwnRow)
            {
                var beside = place == BlockPlace.WithOther ? row.FirstOrDefault(b => b != block) : (BarBlock?)null;
                UpdateBlocks(b => b.Move(block, new BlockMove(BlockPlace.OwnRow, beside, Before: beside is not null)));
            }
        });
        var rowOf = (BarBlock b) => arrangement.Rows.ToList().FindIndex(r => r.Contains(b));
        foreach (var other in BarBlockSettings.All.Where(b => b != block && BlockShown(b)))
        {
            var mine = row.Contains(other) && place != BlockPlace.WithDrives;
            var before = rowOf(block) is var at and >= 0 && at < rowOf(other);
            Radio(WithBlock(other), mine, () => UpdateBlocks(b => b.Move(block, new BlockMove(BlockPlace.WithOther, other, before))));
        }

        var drives = Radio(AppStrings.Bars_WithDrives, place == BlockPlace.WithDrives, () => UpdateBlocks(b => b.Move(block, new BlockMove(BlockPlace.WithDrives, null, Before: false))), enabled: drivesShared);
        if (!drivesShared)
        {
            ToolTip.SetTip(drives, AppStrings.Bars_WithDrivesNeedsShared);
            ToolTip.SetShowOnDisabled(drives, true);
        }

        var others = row.Where(b => b != block).ToList();
        if (others.Count > 0)
        {
            sub.Items.Add(new Separator());
            Radio(AppStrings.Bars_AtStart, row[0] == block, () => UpdateBlocks(b => b.Move(block, new BlockMove(place, others[0], Before: true))));
            Radio(AppStrings.Bars_AtEnd, row[^1] == block, () => UpdateBlocks(b => b.Move(block, new BlockMove(place, others[^1], Before: false))));
        }
        else if (place == BlockPlace.OwnRow && arrangement.Rows.Count > 1)
        {
            var at = arrangement.Rows.ToList().FindIndex(r => r.Contains(block));
            sub.Items.Add(new Separator());
            var up = new MenuItem { Header = AppStrings.Bars_MoveUp, IsEnabled = at > 0 };
            up.Click += (_, _) => UpdateBlocks(b => b.Move(block, new BlockMove(BlockPlace.OwnRow, arrangement.Rows[at - 1][0], Before: true)));
            var down = new MenuItem { Header = AppStrings.Bars_MoveDown, IsEnabled = at >= 0 && at < arrangement.Rows.Count - 1 };
            down.Click += (_, _) => UpdateBlocks(b => b.Move(block, new BlockMove(BlockPlace.OwnRow, arrangement.Rows[at + 1][0], Before: false)));
            sub.Items.Add(up);
            sub.Items.Add(down);
        }

        return sub;
    }

    private MenuItem LockMenuItem()
    {
        var mi = new MenuItem { Header = AppStrings.Bars_Lock, ToggleType = MenuItemToggleType.CheckBox, IsChecked = Blocks.Locked };
        mi.Click += (_, _) => UpdateBlocks(b => b with { Locked = !b.Locked });
        return mi;
    }

    private ContextMenu ShowUserMenuBarMenu(PanelHost host, StripItem? chip)
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action run)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => run();
            menu.Items.Add(mi);
            return mi;
        }

        if (BarButton(chip) is { } item)
        {
            if (item.IsRunnable)
            {
                Item(AppStrings.UserMenuBar_Run, () => _ = RunUserMenuItemAsync(host, item));
            }

            Item(AppStrings.UserMenuBar_EditItem, () => EditUserMenuItem(item));
            Item(AppStrings.UserMenuBar_LeaveOff, () =>
            {
                var path = UserMenuPath(UserMenuSettings.Items, item) ?? [];
                ChangeUserMenuItem(item, i => i with { InBar = false });
                ShowNotice(Loc.Format(AppStrings.UserMenuBar_LeftOff, item.PlainName), (AppStrings.Common_Undo, () =>
                {
                    if (UserMenuAt(UserMenuSettings.Items, path) is { } now)
                    {
                        ChangeUserMenuItem(now, i => i with { InBar = true });
                    }
                }));
            });
            menu.Items.Add(new Separator());
        }

        Item(AppStrings.UserMenu_Edit, () => _ = OpenOptionsAsync(UserMenuPage));
        menu.Items.Add(WhereMenu(BarBlock.UserMenu));
        menu.Items.Add(LockMenuItem());
        Item(AppStrings.UserMenuBar_Hide, () => UpdateBars(b => b with { UserMenuBar = false }));
        menu.Closed += (_, _) => host.View.Focus();
        menu.Open(UserMenuBar);
        return menu;
    }

    private void EditUserMenuItem(UserMenuItem item)
    {
        var path = UserMenuPath(UserMenuSettings.Items, item) ?? [];
        _ = OpenOptionsAsync(UserMenuPage);
        Dispatcher.UIThread.Post(() => _options?.UserMenuEditor?.Select(path), DispatcherPriority.Loaded);
    }

    private static List<int>? UserMenuPath(List<UserMenuItem> items, UserMenuItem item)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
            {
                return [i];
            }

            if (UserMenuPath(items[i].Items, item) is { } inner)
            {
                return [i, .. inner];
            }
        }

        return null;
    }

    private static UserMenuItem? UserMenuAt(List<UserMenuItem> items, IReadOnlyList<int> path)
    {
        UserMenuItem? item = null;
        foreach (var i in path)
        {
            if (i < 0 || i >= items.Count)
            {
                return null;
            }

            item = items[i];
            items = item.Items;
        }

        return item;
    }

    private void WireBarGrips()
    {
        foreach (var strip in BlockStrips)
        {
            strip.LengthSet += (_, px) =>
            {
                if (BlockOf(strip) is { } block)
                {
                    UpdateBlocks(b => b.WithLength(block, px is { } p ? (int)Math.Round(p) : null));
                    strip.Length = Blocks.Length(block);
                }
            };

            strip.GripPressed += (_, _) => _grippedBlock = BlockOf(strip);
            strip.GripMoved += (_, e) => ShowBlockDrop(_grippedBlock is { } b ? BlockDropAt(b, e.GetPosition(this)) : null);
            strip.GripReleased += (_, e) =>
            {
                if (_grippedBlock is { } block && e is not null)
                {
                    DropBlock(block, e.GetPosition(this));
                }

                _grippedBlock = null;
                ShowBlockDrop(null);
            };
        }
    }

    internal BlockDrop? BlockDropAt(BarBlock block, Point at)
    {
        var mine = Strip(block);
        foreach (var row in BarRows.Children.OfType<BarRow>().Where(r => r.IsVisible))
        {
            if (row.TranslatePoint(default, this) is not { } origin)
            {
                continue;
            }

            var bounds = new Rect(origin, row.Bounds.Size);
            if (at.Y < bounds.Y - 4 || at.Y > bounds.Bottom + 4)
            {
                continue;
            }

            var others = row.Children.OfType<DriveStrip>().Where(s => s != mine && s != SharedStrip && BlockOf(s) is not null).ToList();
            var drives = row.Children.Contains(SharedStrip);
            var edge = Math.Max(4, bounds.Height / 4);
            if (others.Count == 0 && !drives)
            {
                return null;
            }

            if (at.Y < bounds.Y + edge || at.Y > bounds.Bottom - edge)
            {
                var above = at.Y < bounds.Y + edge;
                var beside = drives ? null : BlockOf(others[0]);
                return new BlockDrop(new BlockMove(BlockPlace.OwnRow, beside, Before: above && beside is not null), new Rect(bounds.X, above ? bounds.Y - 2 : bounds.Bottom, bounds.Width, 3));
            }

            var place = drives ? BlockPlace.WithDrives : BlockPlace.WithOther;
            DriveStrip? near = null;
            var nearX = 0.0;
            foreach (var strip in others)
            {
                if (strip.TranslatePoint(default, this) is { } o && (near is null || Math.Abs(at.X - (o.X + (strip.Bounds.Width / 2))) < Math.Abs(at.X - (nearX + (near.Bounds.Width / 2)))))
                {
                    near = strip;
                    nearX = o.X;
                }
            }

            if (near is not null)
            {
                var before = at.X < nearX + (near.Bounds.Width / 2);
                return new BlockDrop(new BlockMove(place, BlockOf(near), before), new Rect((before ? nearX : nearX + near.Bounds.Width) - 1, bounds.Y + 3, 3, bounds.Height - 6));
            }

            var markX = SharedStrip.TranslatePoint(default, this) is { } d ? d.X + SharedStrip.Bounds.Width : bounds.Right;
            return new BlockDrop(new BlockMove(BlockPlace.WithDrives, null, Before: false), new Rect(markX - 1, bounds.Y + 3, 3, bounds.Height - 6));
        }

        return null;
    }

    internal void DropBlock(BarBlock block, Point at)
    {
        if (BlockDropAt(block, at) is { } drop)
        {
            UpdateBlocks(b => b.Move(block, drop.Move));
        }
    }

    private void ShowBlockDrop(BlockDrop? drop)
    {
        BarDropMark.IsVisible = drop is not null;
        if (drop is not null)
        {
            Canvas.SetLeft(BarDropMark, drop.Mark.X);
            Canvas.SetTop(BarDropMark, drop.Mark.Y);
            BarDropMark.Width = drop.Mark.Width;
            BarDropMark.Height = drop.Mark.Height;
        }
    }

    private DropSpot UserMenuBarSpot(StripItem? chip)
    {
        if (BarButton(chip) is { IsRunnable: true } item)
        {
            return new DropSpot(DropSpotKind.RunItem, ActivePanel, Name: item.PlainName, Strip: UserMenuBar, Item: chip);
        }

        return chip is null || chip.Kind == StripItemKind.More || chip.Target == HintTarget ? new DropSpot(DropSpotKind.NewItem, ActivePanel, Strip: UserMenuBar) : DropSpot.Nowhere;
    }

    private static string? DroppedProgram(IReadOnlyList<string> files)
        => files.Count == 1 && Path.GetExtension(files[0]).ToUpperInvariant() is ".EXE" or ".COM" && File.Exists(files[0]) ? files[0] : null;

    private DropFeedback UserMenuBarFeedback(DropSpot spot, IReadOnlyList<string> files)
    {
        if (spot.Kind == DropSpotKind.RunItem && files.Count > 0)
        {
            return new DropFeedback(DropEffects.Copy, new DropNote(DropImage.Link, AppStrings.Drop_RunWith, spot.Name));
        }

        return spot.Kind == DropSpotKind.NewItem && DroppedProgram(files) is { } program
            ? new DropFeedback(DropEffects.Link, new DropNote(DropImage.Link, AppStrings.Drop_AddToBar, Path.GetFileNameWithoutExtension(program)))
            : DropFeedback.Nothing;
    }

    private void DropOnUserMenuBar(DropSpot spot, IReadOnlyList<string> files)
    {
        if (spot.Kind == DropSpotKind.RunItem && BarButton(spot.Item) is { } item)
        {
            _ = RunUserMenuItemAsync(spot.Host ?? ActivePanel, item, files);
        }
        else if (spot.Kind == DropSpotKind.NewItem && DroppedProgram(files) is { } program)
        {
            AddBarProgram(program);
        }
    }

    internal void AddBarProgram(string program)
    {
        var item = new UserMenuItem { Name = Path.GetFileNameWithoutExtension(program), Program = program, Arguments = "\"$(FullName)\"", InBar = true };
        _services.Settings.Update(s => s with { UserMenu = s.UserMenu with { Items = [.. s.UserMenu.Items, item] } });
        var index = UserMenuSettings.Items.Count - 1;
        bool Still(UserMenuItem i) => i.Kind == UserMenuItemKind.Program && i.Program == program;
        ShowNotice(Loc.Format(AppStrings.UserMenuBar_Added, item.PlainName),
            (AppStrings.UserMenuBar_EditShort, () =>
            {
                if (UserMenuAt(UserMenuSettings.Items, [index]) is { } now && Still(now))
                {
                    EditUserMenuItem(now);
                }
            }),
            (AppStrings.Common_Undo, () => _services.Settings.Update(s => s with
            {
                UserMenu = s.UserMenu with { Items = [.. s.UserMenu.Items.Where((i, n) => n != index || !Still(i))] },
            })));
    }
}
