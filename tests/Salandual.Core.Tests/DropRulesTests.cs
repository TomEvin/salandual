// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/DropRulesTests.cs

using Salandual.Core.Operations;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class DropRulesTests
{
    private const DropEffects All = DropEffects.Copy | DropEffects.Move | DropEffects.Link;

    [Theory]
    [InlineData(false, false, false, true, PlainDrag.AlwaysCopies, DropEffects.Copy)]
    [InlineData(false, false, false, true, PlainDrag.WindowsRule, DropEffects.Move)]
    [InlineData(false, false, false, false, PlainDrag.WindowsRule, DropEffects.Copy)]
    [InlineData(false, true, false, false, PlainDrag.AlwaysCopies, DropEffects.Move)]
    [InlineData(true, false, false, true, PlainDrag.WindowsRule, DropEffects.Copy)]
    [InlineData(false, false, true, true, PlainDrag.AlwaysCopies, DropEffects.Link)]
    [InlineData(true, true, false, true, PlainDrag.AlwaysCopies, DropEffects.Link)]
    public void Keys_choose_the_action(bool ctrl, bool shift, bool alt, bool sameDrive, PlainDrag rule, DropEffects expected)
        => Assert.Equal(expected, DropRules.Choose(ctrl, shift, alt, All, sameDrive, rule));

    [Fact]
    public void Only_what_the_source_allows()
    {
        Assert.Equal(DropEffects.None, DropRules.Choose(false, true, false, DropEffects.Copy, false, PlainDrag.AlwaysCopies));
        Assert.Equal(DropEffects.Move, DropRules.Choose(false, false, false, DropEffects.Move, false, PlainDrag.AlwaysCopies));
        Assert.Equal(DropEffects.None, DropRules.Choose(false, false, false, DropEffects.None, false, PlainDrag.AlwaysCopies));
    }

    [Fact]
    public void Same_drive()
    {
        Assert.True(DropRules.SameDrive(@"C:\a\b.txt", @"c:\x"));
        Assert.False(DropRules.SameDrive(@"C:\a", @"D:\a"));
        Assert.True(DropRules.SameDrive(@"\\server\share\a", @"\\SERVER\share\b"));
        Assert.False(DropRules.SameDrive(@"\\server\one\a", @"\\server\two\a"));
    }

    [Fact]
    public void Problems()
    {
        Assert.Equal(DropProblem.AlreadyThere, DropRules.Check([@"C:\Photos\a.jpg", @"C:\Photos\b.jpg"], @"C:\Photos"));
        Assert.Equal(DropProblem.AlreadyThere, DropRules.Check([@"C:\Photos\a.jpg"], @"c:\photos\"));
        Assert.Equal(DropProblem.None, DropRules.Check([@"C:\Photos\a.jpg", @"C:\Other\b.jpg"], @"C:\Photos"));
        Assert.Equal(DropProblem.IntoItself, DropRules.Check([@"C:\Photos\2025"], @"C:\Photos\2025"));
        Assert.Equal(DropProblem.IntoItself, DropRules.Check([@"C:\Photos\2025"], @"C:\Photos\2025\June"));
        Assert.Equal(DropProblem.None, DropRules.Check([@"C:\Photos\2025"], @"C:\Photos\2025-old"));
        Assert.Equal(DropProblem.None, DropRules.Check([], @"C:\Photos"));
    }

    [Fact]
    public void Settings_out_of_range_fall_back()
    {
        var s = new AppSettings { Mouse = new MouseSettings { PlainDrag = (PlainDrag)9, AfterDrop = (AfterDrop)9, DriveChipDrop = (DriveChipDrop)9 } }.Normalize();
        Assert.Equal(PlainDrag.AlwaysCopies, s.Mouse.PlainDrag);
        Assert.Equal(AfterDrop.StartAtOnce, s.Mouse.AfterDrop);
        Assert.Equal(DriveChipDrop.FolderItOpens, s.Mouse.DriveChipDrop);
    }
}
