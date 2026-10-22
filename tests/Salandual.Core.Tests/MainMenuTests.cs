// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/MainMenuTests.cs

using System.Globalization;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Core.Tests;

public sealed class MainMenuTests
{
    [Fact]
    public void EveryMenuCommand_IsRegistered()
    {
        var registry = CommandRegistry.CreateDefault();
        foreach (var node in MainMenu.Flatten(MainMenu.Create()))
        {
            Assert.True(registry.TryGet(node.CommandId!, out _), node.CommandId);
        }
    }

    [Fact]
    public void Menus_InSalamanderOrder_WithToolsForPlugins()
        => Assert.Equal(["_Left", "_Files", "_Edit", "_Commands", "_Tools", "_Options", "_Right", "_Help"], MainMenu.Create().Select(m => m.Header));

    [Fact]
    public void LeftAndRightMenus_TargetTheirPanel()
    {
        var menus = MainMenu.Create();
        Assert.All(MainMenu.Flatten([menus[0]]).Where(n => n.CommandId != "Panel.Swap"), n => Assert.Equal(PanelTarget.Left, n.Target));
        Assert.All(MainMenu.Flatten([menus[6]]).Where(n => n.CommandId != "Panel.Swap"), n => Assert.Equal(PanelTarget.Right, n.Target));
    }

    [Fact]
    public void AccessKeys_AreUniqueWithinEachMenu()
    {
        static void Check(IReadOnlyList<MenuNode> nodes)
        {
            var keys = nodes.Where(n => !n.IsSeparator && n.Header?.Contains('_') == true)
                .Select(n => char.ToUpperInvariant(n.Header![n.Header.IndexOf('_') + 1])).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
            foreach (var n in nodes.Where(n => n.Children is not null))
            {
                Check(n.Children!);
            }
        }

        Check(MainMenu.Create());
    }

    [Fact]
    public void InformationLine_ExpandsTemplate()
    {
        var format = new DisplayFormat(SizeUnits.Windows, PanelSettings.DateFormatIso, CultureInfo.GetCultureInfo("en-US"), TimeFormat.Hours24);
        var when = new DateTime(2026, 8, 14, 18, 25, 0, DateTimeKind.Local).ToUniversalTime();
        var entry = new FileEntry("IMG_0414.HEIC", 3_879_731, when.Ticks, FileAttributes.Archive);

        Assert.Equal("IMG_0414.HEIC: 3.7 MB, 2026-08-14, 18:25, A", InformationLine.Expand(PanelSettings.DefaultInformationLine, entry, false, format));
        Assert.Equal("IMG_0414 / HEIC $(Nope)", InformationLine.Expand("$(FileNameOnly) / $(FileExt) $(Nope)", entry, false, format));
        Assert.Equal("..: parent folder", InformationLine.Expand(PanelSettings.DefaultInformationLine, entry, true, format));
    }
}
