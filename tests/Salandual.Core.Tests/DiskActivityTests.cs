// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/DiskActivityTests.cs

namespace Salandual.Core.Tests;

public sealed class DiskActivityTests
{
    [Theory]
    [InlineData(@"C:\Users\user", "C:")]
    [InlineData(@"d:\", "D:")]
    [InlineData("e:", "E:")]
    [InlineData(@"\\?\C:\very\long", "C:")]
    [InlineData(@"\\server\share\folder", null)]
    [InlineData(@"\\?\UNC\server\share", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData(@"C:relative", null)]
    [InlineData(@"1:\", null)]
    public void DriveOf_OnlyDriveLetters(string? path, string? drive) => Assert.Equal(drive, DiskActivity.DriveOf(path));

    [Theory]
    [InlineData(100, 0)]
    [InlineData(38, 62)]
    [InlineData(0, 100)]
    [InlineData(104.5, 0)]
    [InlineData(-3, 100)]
    public void BusyFromIdle_ClampedToAPercent(double idle, double busy) => Assert.Equal(busy, DiskActivity.BusyFromIdle(idle));

    [Fact]
    public void Percent_IsRoundedAndClamped()
    {
        Assert.Equal(62, new DiskActivitySample(61.6, 0, 0).Percent);
        Assert.Equal(100, new DiskActivitySample(130, 0, 0).Percent);
        Assert.Equal(0, new DiskActivitySample(-1, 0, 0).Percent);
    }

    [Fact]
    public void MemorySource_LeavesOutDrivesWithoutAReading()
    {
        using var source = new MemoryDiskActivitySource();
        source.Values["C:"] = new DiskActivitySample(62, 140_000_000, 12_000_000);
        var sample = source.Sample(["C:", "S:"]);
        Assert.Equal(["C:"], sample.Keys);
        Assert.Equal(62, sample["c:"].Percent);
        Assert.Equal(["C:", "S:"], source.Asked.Single());
    }

    [Fact]
    public void Settings_LineOnAndBarOffAtFirst()
    {
        var menu = new Settings.DriveMenuSettings();
        Assert.True(menu.ActivityInLine);
        Assert.False(menu.ActivityInStrip);
        Assert.Equal(ActivityLook.Bar, menu.ActivityLook);
    }

    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0);

    [Fact]
    public void History_KeepsFifteenMinutes()
    {
        var history = new DiskActivityHistory();
        for (var s = 0; s <= 20 * 60; s += 60)
        {
            history.Add("C:", T0.AddSeconds(s), new DiskActivitySample(s / 60, 0, 0));
        }

        var kept = history.Since("c:", DateTime.MinValue);
        Assert.Equal(T0.AddMinutes(5), kept[0].Time);
        Assert.Equal(16, kept.Count);
        Assert.Empty(history.Since("D:", DateTime.MinValue));
    }

    [Fact]
    public void History_LastSeconds_OldestFirst_NullWithoutAReading()
    {
        var history = new DiskActivityHistory();
        history.Add("C:", T0.AddSeconds(-0.4), new DiskActivitySample(80, 0, 0));
        history.Add("C:", T0.AddSeconds(-2.2), new DiskActivitySample(40, 0, 0));
        history.Add("C:", T0.AddSeconds(-45), new DiskActivitySample(99, 0, 0));
        var last = history.LastSeconds("C:", T0, 30);
        Assert.Equal(30, last.Count);
        Assert.Equal(80, last[29]);
        Assert.Equal(40, last[27]);
        Assert.Null(last[28]);
        Assert.Equal(2, last.Count(v => v is not null));
    }
}
