// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/DriveMediaTests.cs

using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Tests;

public sealed class DriveMediaTests
{
    [Theory]
    [InlineData(17, false, false, null, DriveMedium.Nvme)]
    [InlineData(11, false, false, null, DriveMedium.Ssd)]
    [InlineData(11, false, true, null, DriveMedium.HardDisk)]
    [InlineData(11, false, null, null, DriveMedium.Unknown)]
    [InlineData(7, true, null, "DataTraveler 3.0", DriveMedium.UsbStick)]
    [InlineData(7, false, true, "Expansion HDD", DriveMedium.External)]
    [InlineData(7, true, null, "USB3.0 Card Reader", DriveMedium.CardReader)]
    [InlineData(7, true, null, "SD/MMC", DriveMedium.CardReader)]
    [InlineData(12, true, null, null, DriveMedium.SdCard)]
    [InlineData(13, true, null, null, DriveMedium.SdCard)]
    public void Classify_ByBusRemovableAndHead(int bus, bool removable, bool? seekPenalty, string? product, DriveMedium expected)
        => Assert.Equal(expected, DriveMedia.Classify(bus, removable, seekPenalty, product));

    [Fact]
    public void NetworkPaths_AreNetwork_AndWaitedOn()
    {
        Assert.Equal(DriveMedium.Network, DriveMedia.Of(@"\\server\share\"));
        Assert.True(DriveMedia.WaitsOn(@"\\server"));
    }

    [Fact]
    public void TheTempDrive_ReadsWithoutAdministratorRights()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var medium = DriveMedia.Of(root);
        Assert.True(Enum.IsDefined(medium));
        Assert.Equal(DriveMedia.SeekPenalty(root) != false, DriveMedia.WaitsOn(root.TrimEnd('\\') + "\\"));
    }
}
