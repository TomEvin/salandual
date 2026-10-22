// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/StartOptionsTests.cs

namespace Salandual.Core.Tests;

public sealed class StartOptionsTests
{
    private const string Cwd = @"D:\Work";

    private static (string Left, string Right) Resolve(params string[] args) => StartOptions.Parse(args, Cwd).Resolve(@"C:\", @"C:\Users\me");

    [Fact]
    public void Nothing_UsesTheDefaults() => Assert.Equal((@"C:\", @"C:\Users\me"), Resolve());

    [Fact]
    public void LeftAndRight_FullAndRelativePaths()
        => Assert.Equal((@"C:\Program Files", @"D:\Work\Fotók"), Resolve("-L", @"C:\Program Files", "-r", "Fotók"));

    [Fact]
    public void Active_GoesToTheLeftPanel_UnlessPanel2()
    {
        Assert.Equal((@"E:\", @"C:\Users\me"), Resolve("-A", @"E:\"));
        Assert.Equal((@"C:\", @"E:\"), Resolve("-A", @"E:\", "-P", "2"));
        Assert.Equal(1, StartOptions.Parse(["-P", "2"], Cwd).ActiveIndex);
        Assert.Equal(0, StartOptions.Parse(["-P", "1"], Cwd).ActiveIndex);
    }

    [Fact]
    public void Active_IsIgnoredWithLeftOrRight()
        => Assert.Equal((@"C:\", @"F:\"), Resolve("-A", @"E:\", "-R", @"F:\"));

    [Fact]
    public void AFullPathOnItsOwn_IsTheActiveFolder()
    {
        Assert.Equal((@"E:\Photos", @"C:\Users\me"), Resolve(@"E:\Photos"));
        Assert.Equal((@"C:\", @"E:\Photos"), Resolve(@"""E:\Photos""", "-P", "2"));
        Assert.Equal((@"F:\", @"C:\Users\me"), Resolve(@"E:\Photos", "-A", @"F:\"));
        Assert.Equal((@"C:\", @"C:\Users\me"), Resolve("Photos"));
    }

    [Fact]
    public void UnknownParameters_AndMissingValues_AreIgnored()
    {
        Assert.Equal((@"C:\x", @"C:\Users\me"), Resolve("-O", "-L", @"C:\x", "-T", "ADMIN", "-R"));
        Assert.Equal((@"C:\", @"C:\Users\me"), Resolve("-L", "  "));
    }
}
