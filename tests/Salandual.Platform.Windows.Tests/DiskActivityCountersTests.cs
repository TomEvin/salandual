// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/DiskActivityCountersTests.cs

using Salandual.Core;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Tests;

public sealed class DiskActivityCountersTests
{
    private static string SystemDrive => DiskActivity.DriveOf(Environment.SystemDirectory)!;

    [Fact]
    public void SystemDrive_HasAReadingFromTheSecondSample()
    {
        using var counters = new DiskActivityCounters();
        var first = counters.Sample([SystemDrive]);
        Assert.False(first.ContainsKey(SystemDrive));
        Thread.Sleep(1100);
        var second = counters.Sample([SystemDrive]);
        var sample = Assert.Contains(SystemDrive, second);
        Assert.InRange(sample.Busy, 0, 100);
        Assert.True(sample.ReadPerSecond >= 0 && sample.WritePerSecond >= 0);
    }

    [Fact]
    public void NoDriveLetter_NoReading()
    {
        using var counters = new DiskActivityCounters();
        Assert.Empty(counters.Sample([@"\\server\share", "", "C:\\"]));
        Thread.Sleep(200);
        Assert.Empty(counters.Sample([@"\\server\share"]));
    }

    [Fact]
    public void AfterDispose_NothingIsRead()
    {
        var counters = new DiskActivityCounters();
        counters.Sample([SystemDrive]);
        counters.Dispose();
        Assert.Empty(counters.Sample([SystemDrive]));
    }
}
