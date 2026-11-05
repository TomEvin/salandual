// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/BarBlocksTests.cs

using Salandual.Core.Settings;
using Salandual.Core.UserMenu;

namespace Salandual.Core.Tests;

public sealed class BarBlocksTests
{
    private const BarBlock Hot = BarBlock.HotPaths;
    private const BarBlock User = BarBlock.UserMenu;
    private const BarBlock Nav = BarBlock.BackForward;

    private static bool All(BarBlock b) => true;

    private static Func<BarBlock, bool> Only(params BarBlock[] shown) => shown.Contains;

    private static string Rows(BarArrangement a)
        => string.Join(" / ", a.Rows.Select(r => string.Join("+", r))) + (a.WithDrives.Count > 0 ? " | drives+" + string.Join("+", a.WithDrives) : "");

    [Fact]
    public void At_first_each_bar_has_its_own_row_the_user_menu_bar_on_top_back_and_forward_above_the_hot_paths()
    {
        var blocks = new BarBlockSettings();
        Assert.Equal("UserMenu / BackForward / HotPaths", Rows(blocks.Arrange(All, drivesShared: false)));
        Assert.Equal("HotPaths", Rows(blocks.Arrange(Only(Hot), false)));
        Assert.Equal("", Rows(blocks.Arrange(Only(), true)));
        Assert.Equal(BlockPlace.OwnRow, blocks[Nav]);
    }

    [Fact]
    public void Blocks_share_a_row_in_any_order_and_a_hidden_one_leaves_it()
    {
        var shared = new BarBlockSettings().Move(User, new BlockMove(BlockPlace.WithOther, Hot, Before: false));
        Assert.Equal("BackForward / HotPaths+UserMenu", Rows(shared.Arrange(All, false)));
        Assert.Equal(BlockPlace.WithOther, shared[User]);
        Assert.Equal("BackForward / UserMenu+HotPaths", Rows(shared.Move(User, new BlockMove(BlockPlace.WithOther, Hot, Before: true)).Arrange(All, false)));

        var three = shared.Move(Nav, new BlockMove(BlockPlace.WithOther, Hot, Before: true));
        Assert.Equal("BackForward+HotPaths+UserMenu", Rows(three.Arrange(All, false)));
        Assert.Equal("BackForward+UserMenu", Rows(three.Arrange(Only(Nav, User), false)));
        Assert.Equal(BlockPlace.OwnRow, three.Effective(User, Only(User), false));
        Assert.Equal(BlockPlace.WithOther, three.Effective(User, All, false));
    }

    [Fact]
    public void With_the_drives_only_while_they_are_shared_and_then_at_the_rows_end()
    {
        var blocks = new BarBlockSettings().Move(User, new BlockMove(BlockPlace.WithDrives, null, Before: false));
        Assert.Equal("BackForward / HotPaths | drives+UserMenu", Rows(blocks.Arrange(All, drivesShared: true)));
        Assert.Equal("BackForward / HotPaths / UserMenu", Rows(blocks.Arrange(All, drivesShared: false)));
        Assert.Equal(BlockPlace.WithDrives, blocks[User]);

        var both = blocks.Move(Hot, new BlockMove(BlockPlace.WithOther, User, Before: true));
        Assert.Equal("BackForward | drives+HotPaths+UserMenu", Rows(both.Arrange(All, true)));
        Assert.Equal("BackForward / HotPaths+UserMenu", Rows(both.Arrange(All, false)));
    }

    [Fact]
    public void Moving_a_block_out_leaves_the_others_in_their_row_and_own_rows_go_above_or_below()
    {
        var shared = new BarBlockSettings().Move(Hot, new BlockMove(BlockPlace.WithOther, User, Before: false));
        Assert.Equal("UserMenu+HotPaths / BackForward", Rows(shared.Arrange(All, true)));
        var moved = shared.Move(User, new BlockMove(BlockPlace.WithDrives, null, Before: true));
        Assert.Equal(BlockPlace.OwnRow, moved[Hot]);
        Assert.Equal("HotPaths / BackForward | drives+UserMenu", Rows(moved.Arrange(All, true)));

        Assert.Equal("BackForward / UserMenu+HotPaths", Rows(shared.Move(Nav, new BlockMove(BlockPlace.OwnRow, User, Before: true)).Arrange(All, true)));
        Assert.Equal("UserMenu / HotPaths / BackForward", Rows(shared.Move(Hot, new BlockMove(BlockPlace.OwnRow, User, Before: false)).Arrange(All, true)));
        Assert.Equal("HotPaths / BackForward / UserMenu", Rows(moved.Move(User, new BlockMove(BlockPlace.OwnRow, null, Before: false)).Arrange(All, true)));
    }

    [Fact]
    public void Settings_with_unknown_or_missing_blocks_are_put_right()
    {
        var settings = new AppSettings { Layout = new LayoutSettings { Blocks = new BarBlockSettings { Rows = [[(BarBlock)9, Hot], [Hot, User]], WithDrives = [User], Locked = true } } }.Normalize();
        var blocks = settings.Layout.Blocks;
        Assert.Equal("BackForward / HotPaths / UserMenu", Rows(blocks.Arrange(All, false)));
        Assert.True(blocks.Locked);
        Assert.NotNull(new AppSettings { Layout = new LayoutSettings { Blocks = null! } }.Normalize().Layout.Blocks);
        Assert.Equal("UserMenu / BackForward / HotPaths", Rows(new BarBlockSettings { Rows = null!, WithDrives = null! }.Normalize().Arrange(All, false)));
    }

    [Fact]
    public void A_length_is_kept_in_pixels_between_the_limits_and_null_goes_back_to_the_buttons()
    {
        var blocks = new BarBlockSettings().WithLength(User, 240).WithLength(Hot, 5).WithLength(Nav, 120);
        Assert.Equal(240, blocks.Length(User));
        Assert.Equal(BarBlockSettings.MinLength, blocks.Length(Hot));
        Assert.Equal(120, blocks.Length(Nav));
        Assert.Null(blocks.WithLength(User, null).Length(User));
        Assert.Equal(BarBlockSettings.MaxLength, new BarBlockSettings().WithLength(Hot, 99999).HotPathsLength);

        var read = new AppSettings { Layout = new LayoutSettings { Blocks = new BarBlockSettings { UserMenuLength = -3, HotPathsLength = 300 } } }.Normalize().Layout.Blocks;
        Assert.Equal(BarBlockSettings.MinLength, read.UserMenuLength);
        Assert.Equal(300, read.Move(Hot, new BlockMove(BlockPlace.WithOther, User, Before: true)).HotPathsLength);
    }

    [Theory]
    [InlineData("OwnRow", "OwnRow", "UserMenu", false, "UserMenu / BackForward / HotPaths")]
    [InlineData("OwnRow", "OwnRow", "HotPaths", false, "BackForward / HotPaths / UserMenu")]
    [InlineData("WithOther", "OwnRow", "UserMenu", false, "BackForward / UserMenu+HotPaths")]
    [InlineData("OwnRow", "WithOther", "HotPaths", false, "BackForward / HotPaths+UserMenu")]
    [InlineData("OwnRow", "WithDrives", "UserMenu", true, "BackForward / HotPaths | drives+UserMenu")]
    [InlineData("WithOther", "WithDrives", "HotPaths", true, "BackForward | drives+HotPaths+UserMenu")]
    public void Schema9_KeepsWhereTheTwoBarsWere_AndPutsBackAndForwardAboveTheHotPaths(string hot, string user, string first, bool drivesShared, string rows)
    {
        var dir = Directory.CreateTempSubdirectory("salandual-settings-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, SettingsStore.FileName),
                $$"""{ "schemaVersion": 8, "layout": { "blocks": { "hotPaths": "{{hot}}", "userMenu": "{{user}}", "first": "{{first}}", "locked": true, "hotPathsLength": 300 } } }""");
            var blocks = SettingsStore.InDirectory(dir).Load().Settings.Layout.Blocks;
            Assert.Equal(rows, Rows(blocks.Arrange(All, drivesShared)));
            Assert.True(blocks.Locked);
            Assert.Equal(300, blocks.HotPathsLength);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_bar_has_the_marked_items_with_the_menus_separators_between_them()
    {
        static UserMenuItem P(string name, bool bar) => new() { Name = name, Program = "x.exe", InBar = bar };
        var menu = new UserMenuSettings
        {
            Items =
            [
                new UserMenuItem { Kind = UserMenuItemKind.Separator },
                P("MPC-BE", true),
                P("Not on the bar", false),
                new UserMenuItem { Kind = UserMenuItemKind.Separator },
                P("WinMerge", true),
                new UserMenuItem { Kind = UserMenuItemKind.Submenu, Name = "Tools", Items = [P("SHA-256", true), P("Hidden", false)] },
                new UserMenuItem { Kind = UserMenuItemKind.Separator },
            ],
        };
        var bar = menu.BarItems();
        Assert.Equal(["MPC-BE", "WinMerge", "SHA-256"], bar.Select(b => b.Item.Name));
        Assert.Equal([true, false, false], bar.Select(b => b.SeparatorAfter));
    }
}
