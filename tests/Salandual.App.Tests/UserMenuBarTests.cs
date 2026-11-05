// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/UserMenuBarTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Core.Settings;
using Salandual.Core.UserMenu;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class UserMenuBarTests : IDisposable
{
    private const string Player = @"C:\Program Files\MPC-BE\mpc-be64.exe";
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-usermenubar-").FullName;
    private readonly string _left;

    public UserMenuBarTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Lake 2024")).FullName;
        File.WriteAllText(Path.Join(_left, "day2-swim.mp4"), "2");
        File.WriteAllText(Path.Join(_left, "cut-list.txt"), "c");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static List<UserMenuItem> Menu() =>
    [
        new() { Name = "&Play in MPC-BE", Program = "mpc-be64.exe", Arguments = "\"$(FullName)\"", InBar = true },
        new() { Name = "Not on the bar", Program = "mpc-be64.exe" },
        new() { Kind = UserMenuItemKind.Separator },
        new() { Name = "SHA-256", Kind = UserMenuItemKind.Script, Runs = UserMenuRuns.Once, Script = "Get-FileHash -LiteralPath $Files", InBar = true },
        new() { Name = "Video tools", Kind = UserMenuItemKind.Submenu, InBar = true, Items = [new() { Name = "Remux", Program = "mpc-be64.exe", Arguments = "\"$(FullName)\"" }] },
    ];

    private async Task<MainWindow> Open(List<UserMenuItem>? items = null, Func<LayoutSettings, LayoutSettings>? layout = null)
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Services.Settings.Update(s => s with
        {
            UserMenu = new UserMenuSettings { Items = items ?? Menu() },
            HotPaths = s.HotPaths.With(new HotPath(1, "Lake", _left)),
            Layout = (layout ?? (l => l))(s.Layout with { BackForward = s.Layout.BackForward with { Place = BackForwardPlace.Off } }),
        });
        var starter = (MemoryProgramStarter)w.Services.Starter;
        starter.Programs["mpc-be64.exe"] = Player;
        starter.Programs["pwsh.exe"] = @"C:\Program Files\PowerShell\7\pwsh.exe";
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_left);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Change(MainWindow w, Func<AppSettings, AppSettings> change)
    {
        w.Services.Settings.Update(change);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Blocks(MainWindow w, Func<BarBlockSettings, BarBlockSettings> change)
        => Change(w, s => s with { Layout = s.Layout with { Blocks = change(s.Layout.Blocks) } });

    private static List<string> Rows(MainWindow w)
        => [.. w.BarRowStrips.Select(r => string.Join("+", r.Select(s => s == w.UserMenuBar ? "user" : s == w.HotPathBar ? "hot" : "drives")))];

    private static StripItem Chip(MainWindow w, string label) => w.UserMenuBar.Items.First(i => i.Label == label || i.Tip?.StartsWith(label, StringComparison.Ordinal) == true);

    private static Point At(MainWindow w, DriveStrip strip, Point inStrip) => strip.TranslatePoint(inStrip, w)!.Value;

    private static Point ChipPoint(MainWindow w, StripItem chip)
    {
        w.CaptureRenderedFrame();
        return At(w, w.UserMenuBar, w.UserMenuBar.ChipBounds(chip)!.Value.Center);
    }

    private static string Text(object? header) => header is string s ? s.Replace("_", "", StringComparison.Ordinal) : "";

    private static MenuItem Item(ItemCollection items, string text) => items.OfType<MenuItem>().First(m => Text(m.Header) == text);

    private static void Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition, int tries = 500)
    {
        for (var i = 0; i < tries && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task TheBar_HasTheMarkedItems_InTheirOwnRow_AboveTheHotPaths()
    {
        var w = await Open();
        Assert.Equal(["user", "hot"], Rows(w));
        Assert.Equal(["Play in MPC-BE", "SHA-256", "Video tools"], w.UserMenuBar.Items.Select(i => i.Label));
        Assert.True(w.UserMenuBar.Items[0].SeparatorAfter);
        Assert.True(w.UserMenuBar.Grip && w.HotPathBar.Grip);

        Blocks(w, b => b with { IconsOnly = true });
        Assert.All(w.UserMenuBar.Items, i => Assert.Equal("", i.Label));
        Assert.StartsWith("Play in MPC-BE", w.UserMenuBar.Items[0].Tip);

        Change(w, s => s with { UserMenu = new UserMenuSettings { Items = [new() { Name = "x", Program = "mpc-be64.exe" }] } });
        Assert.Equal(["user", "hot"], Rows(w));
        var hint = Assert.Single(w.UserMenuBar.Items);
        Assert.Equal((AppStrings.UserMenuBar_Hint, true), (hint.Label, hint.Dim));
        w.UserMenuBar.Click(hint);
        await Until(() => w.OpenOptions?.UserMenuEditor is not null);
        w.OpenOptions!.Close();
        Dispatcher.UIThread.RunJobs();
        Change(w, s => s with { UserMenu = new UserMenuSettings { Items = Menu() } });
        Assert.True(w.Run("View.ToggleUserMenuBar", Core.Commands.PanelTarget.Active));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["hot"], Rows(w));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AButton_RunsItsItem_ASubmenuButtonOpensItsItems()
    {
        var w = await Open();
        w.LeftPanel.Panel.SetFocus(w.LeftPanel.Panel.Items.ToList().FindIndex(e => e.Name == "day2-swim.mp4"));
        w.UserMenuBar.Click(Chip(w, "Play in MPC-BE"));
        Dispatcher.UIThread.RunJobs();
        var started = Assert.Single(((MemoryProgramStarter)w.Services.Starter).Started);
        Assert.Equal(Player, started.Program);
        Assert.Equal([Path.Join(_left, "day2-swim.mp4")], started.Arguments);

        w.UserMenuBar.Click(Chip(w, "Video tools"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Remux"], w.UserMenuPopup!.Items.OfType<MenuItem>().Select(m => Text(m.Header)));
        w.UserMenuPopup.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ButtonsThatDontFit_GoIntoMore()
    {
        var many = Enumerable.Range(1, 30).Select(i => new UserMenuItem { Name = $"Program number {i}", Program = "mpc-be64.exe", InBar = true }).ToList();
        var w = await Open(many, l => l with { Blocks = l.Blocks.Move(BarBlock.UserMenu, new BlockMove(BlockPlace.WithOther, BarBlock.HotPaths, Before: true)) });
        Assert.Equal(["user+hot"], Rows(w));
        Assert.True(w.UserMenuBar.Bounds.Width <= (w.UserMenuBar.Parent as Control)!.Bounds.Width * BarRow.MaxShare + 1);
        w.CaptureRenderedFrame();
        Assert.NotEmpty(w.UserMenuBar.Overflowed);
        var more = w.UserMenuBar.Shown.Single(i => i.Kind == StripItemKind.More);
        Assert.Equal("»", more.Label);
        w.UserMenuBar.Click(more);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(w.UserMenuBar.Overflowed.Count, w.UserMenuPopup!.Items.OfType<MenuItem>().Count());
        w.UserMenuPopup.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task WhereTheBarIs_SharedRows_AndTheDrivesOnlyWhileShared()
    {
        var w = await Open();
        var menu = w.ShowStripMenu(w.LeftPanel, w.UserMenuBar, Chip(w, "SHA-256"));
        Assert.Equal(["Run", "Edit this item…", "Leave it off the bar", "Edit user menu…", "Where the bar is", "Lock the bars", "Hide the user menu bar"],
            menu.Items.OfType<MenuItem>().Select(m => Text(m.Header)));
        var where = Item(menu.Items, "Where the bar is");
        Assert.False(Item(where.Items, "With the drives").IsEnabled);
        Assert.Equal(["Move up", "Move down"], where.Items.OfType<MenuItem>().Skip(3).Select(m => Text(m.Header)));
        Assert.False(where.Items.OfType<MenuItem>().ElementAt(3).IsEnabled);
        Click(Item(where.Items, "With the hot paths"));
        menu.Close();
        Assert.Equal(["user+hot"], Rows(w));
        Assert.True(w.HotPathBar.DividerBefore && !w.UserMenuBar.DividerBefore);

        menu = w.ShowStripMenu(w.LeftPanel, w.HotPathBar, null);
        where = Item(menu.Items, "Where the bar is");
        Assert.Equal("At the row's start", Text(where.Items.OfType<MenuItem>().ElementAt(3).Header));
        Click(where.Items.OfType<MenuItem>().ElementAt(3));
        menu.Close();
        Assert.Equal(["hot+user"], Rows(w));

        Change(w, s => s with { Layout = s.Layout with { Strip = StripMode.Shared } });
        Blocks(w, b => b.Move(BarBlock.UserMenu, new BlockMove(BlockPlace.WithDrives, null, Before: false)));
        Assert.Equal(["hot", "drives+user"], Rows(w));
        Blocks(w, b => b.Move(BarBlock.HotPaths, new BlockMove(BlockPlace.WithOther, BarBlock.UserMenu, Before: true)));
        Assert.Equal(["drives+hot+user"], Rows(w));
        Change(w, s => s with { Layout = s.Layout with { Strip = StripMode.PerPanel } });
        Assert.Equal(["hot+user"], Rows(w));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheGrip_DragsABlock_IntoAnotherRow_UnlessLocked()
    {
        var w = await Open();
        var hot = w.HotPathBar;

        var into = At(w, hot, new Point(hot.Bounds.Width * 0.75, hot.Bounds.Height / 2));
        var drop = w.BlockDropAt(BarBlock.UserMenu, into);
        Assert.Equal(new BlockDrop(new BlockMove(BlockPlace.WithOther, BarBlock.HotPaths, Before: false), drop!.Mark), drop);
        w.DropBlock(BarBlock.UserMenu, into);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["hot+user"], Rows(w));

        var row = (Control)hot.Parent!;
        var top = row.TranslatePoint(new Point(row.Bounds.Width / 2, 1), w)!.Value;
        w.DropBlock(BarBlock.UserMenu, top);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["user", "hot"], Rows(w));

        Assert.Null(w.BlockDropAt(BarBlock.UserMenu, At(w, w.UserMenuBar, new Point(40, w.UserMenuBar.Bounds.Height / 2))));

        var menu = w.ShowStripMenu(w.LeftPanel, w.HotPathBar, null);
        Click(Item(menu.Items, "Lock the bars"));
        menu.Close();
        Assert.True(w.Services.Settings.Current.Layout.Blocks.Locked);
        Assert.False(w.UserMenuBar.Grip || w.HotPathBar.Grip);
        Assert.Equal(default, w.HotPathBar.GripBounds);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task FilesDroppedOnAButton_RunItWithThem_AProgramOnTheBar_BecomesAnItem()
    {
        var w = await Open();
        var swim = Path.Join(_left, "day2-swim.mp4");
        var at = ChipPoint(w, Chip(w, "Play in MPC-BE"));
        var feedback = w.DragEnterAt(new DropData([swim], null), DragKeys.LeftButton, at, DropEffects.Copy | DropEffects.Move | DropEffects.Link);
        Assert.Equal(new DropNote(DropImage.Link, AppStrings.Drop_RunWith, "Play in MPC-BE"), feedback.Note);
        Assert.Equal(DropEffects.Copy, w.DropAt(DragKeys.None, at, DropEffects.Copy | DropEffects.Move));
        Dispatcher.UIThread.RunJobs();
        var started = Assert.Single(((MemoryProgramStarter)w.Services.Starter).Started);
        Assert.Equal([swim], started.Arguments);
        Assert.True(File.Exists(swim));

        var program = Path.Join(_dir, "Code.exe");
        File.WriteAllText(program, "MZ");
        var empty = At(w, w.UserMenuBar, new Point(w.UserMenuBar.Bounds.Width - 20, w.UserMenuBar.Bounds.Height / 2));
        var note = w.DragEnterAt(new DropData([program], null), DragKeys.LeftButton, empty, DropEffects.Copy | DropEffects.Link);
        Assert.Equal(new DropNote(DropImage.Link, AppStrings.Drop_AddToBar, "Code"), note.Note);
        Assert.Equal(DropEffects.Link, w.DropAt(DragKeys.None, empty, DropEffects.Copy | DropEffects.Link));
        Dispatcher.UIThread.RunJobs();
        var added = w.Services.Settings.Current.UserMenu.Items[^1];
        Assert.Equal(("Code", program, "\"$(FullName)\"", true), (added.Name, added.Program, added.Arguments, added.InBar));
        Assert.Equal("Code", w.UserMenuBar.Items[^1].Label);

        Assert.Equal(DropEffects.None, w.DragEnterAt(new DropData([Path.Join(_left, "cut-list.txt")], null), DragKeys.LeftButton, empty, DropEffects.Copy).Effect);
        w.DragLeft();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task EditThisItem_OpensOptionsOnIt_WhereItsTickTakesItOffTheBar()
    {
        var w = await Open();
        var menu = w.ShowStripMenu(w.LeftPanel, w.UserMenuBar, Chip(w, "SHA-256"));
        Click(Item(menu.Items, "Edit this item…"));
        menu.Close();
        await Until(() => w.OpenOptions?.UserMenuEditor?.Fields.ContainsKey(AppStrings.Opt_UserMenu_Script) == true);
        var tick = Assert.IsType<CheckBox>(w.OpenOptions!.UserMenuEditor!.Fields[AppStrings.Opt_UserMenu_InBar]);
        Assert.True(tick.IsChecked);
        tick.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Play in MPC-BE", "Video tools"], w.UserMenuBar.Items.Select(i => i.Label));

        w.OpenOptions.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheEndLine_SetsABlocksLength_InPixels_TheNextBlockFillsTheRest()
    {
        var w = await Open(layout: l => l with { Blocks = l.Blocks.Move(BarBlock.UserMenu, new BlockMove(BlockPlace.WithOther, BarBlock.HotPaths, Before: false)) });
        Assert.Equal(["hot+user"], Rows(w));
        var hot = w.HotPathBar;
        var user = w.UserMenuBar;
        var row = (Control)hot.Parent!;

        Assert.True(hot.EndHandle);
        Assert.False(user.EndHandle);
        var end = hot.EndBounds;
        var from = At(w, hot, new Point(end.X + (end.Width / 2), end.Height / 2));

        w.MouseDown(from, Avalonia.Input.MouseButton.Left);
        w.MouseMove(from + new Point(300 - hot.Bounds.Width, 0));
        w.MouseUp(from + new Point(300 - hot.Bounds.Width, 0), Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(300, w.Services.Settings.Current.Layout.Blocks.HotPathsLength);
        Assert.Equal(300, hot.Bounds.Width, 1);
        Assert.Equal(row.Bounds.Width - 300, user.Bounds.Width, 1);

        Blocks(w, b => b.WithLength(BarBlock.UserMenu, 200));
        Assert.Equal(row.Bounds.Width, user.Bounds.Right, 1);

        Blocks(w, b => b.Move(BarBlock.UserMenu, new BlockMove(BlockPlace.WithOther, BarBlock.HotPaths, Before: true)));
        Assert.Equal(["user+hot"], Rows(w));
        Assert.Equal(200, user.Bounds.Width, 1);
        Assert.Equal(row.Bounds.Width, hot.Bounds.Right, 1);
        Blocks(w, b => b.WithLength(BarBlock.UserMenu, 4000));
        Assert.True(user.Bounds.Width < row.Bounds.Width);

        Blocks(w, b => b.WithLength(BarBlock.UserMenu, null));
        Assert.True(user.Bounds.Width < 400);

        Blocks(w, b => b.Move(BarBlock.UserMenu, new BlockMove(BlockPlace.OwnRow, BarBlock.HotPaths, Before: true)));
        Assert.Equal(["user", "hot"], Rows(w));
        Assert.False(user.EndHandle || hot.EndHandle);
        Assert.Equal(row.Bounds.Width, hot.Bounds.Width, 1);

        Blocks(w, b => b.Move(BarBlock.UserMenu, new BlockMove(BlockPlace.WithOther, BarBlock.HotPaths, Before: false)) with { Locked = true });
        Assert.False(user.EndHandle || hot.EndHandle);
        Assert.Equal(default, user.EndBounds);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task OptionsUserMenu_HasTheBarsSettings_AndLayoutNoLongerArrangesThem()
    {
        var w = await Open();
        _ = w.OpenOptionsAsync(MainWindow.UserMenuPage);
        await Until(() => w.OpenOptions?.UserMenuEditor is not null);
        var options = w.OpenOptions!;
        var mine = options.Items.Where(i => i.Category == MainWindow.UserMenuPage && i.Section == AppStrings.Bar_UserMenuBar).Select(i => i.Title);
        Assert.Equal([AppStrings.Opt_UserMenuBarShown, AppStrings.Opt_BarButtons, AppStrings.Opt_LockBars], mine);
        Assert.DoesNotContain(options.Items, i => i.Category == "layout" && (i.Title == AppStrings.Opt_LockBars || i.Title is null));

        Avalonia.Controls.Primitives.ToggleButton Choice(string label) => options.Page.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(b => b.Content as string == label);
        Choice(AppStrings.Opt_BarNames_IconsOnly).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.Services.Settings.Current.Layout.Blocks.IconsOnly);
        Assert.All(w.UserMenuBar.Items, i => Assert.Equal("", i.Label));

        var switches = options.Page.GetVisualDescendants().OfType<ToggleSwitch>().ToList();
        switches[^1].IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.Services.Settings.Current.Layout.Blocks.Locked);
        Assert.False(w.UserMenuBar.Grip);
        switches[^2].IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.UserMenuBar.IsVisible);
        options.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task LeaveItOffTheBar_WithUndo()
    {
        var w = await Open();
        var menu = w.ShowStripMenu(w.LeftPanel, w.UserMenuBar, Chip(w, "SHA-256"));
        Click(Item(menu.Items, "Leave it off the bar"));
        menu.Close();
        Assert.Equal(["Play in MPC-BE", "Video tools"], w.UserMenuBar.Items.Select(i => i.Label));
        Assert.False(w.Services.Settings.Current.UserMenu.Items[3].InBar);
        w.NoticeButtons.Single(b => b.Content as string == AppStrings.Common_Undo).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.Services.Settings.Current.UserMenu.Items[3].InBar);
        w.CloseNow();
    }
}
