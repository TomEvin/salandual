// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/DriveHealthTests.cs

using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Tests;

public sealed class DriveHealthTests
{
    [Fact]
    public void NvmeLog_IsParsed()
    {
        var l = new byte[512];
        l[0] = 0;
        BitConverter.TryWriteBytes(l.AsSpan(1), (ushort)(273 + 41));
        l[3] = 100;
        l[4] = 10;
        l[5] = 4;
        BitConverter.TryWriteBytes(l.AsSpan(32), 1000UL);
        BitConverter.TryWriteBytes(l.AsSpan(48), 2000UL);
        BitConverter.TryWriteBytes(l.AsSpan(112), 1042UL);
        BitConverter.TryWriteBytes(l.AsSpan(128), 2310UL);
        BitConverter.TryWriteBytes(l.AsSpan(144), 23UL);
        var n = DriveHealth.ParseNvme(l);
        Assert.Equal(new NvmeHealth(0, 41, 100, 10, 4, 512_000_000, 1_024_000_000, 1042, 2310, 23, 0, 0), n);
        Assert.Equal(HealthState.Good, Of(n).State);
        Assert.Equal(HealthState.Watch, Of(n with { Used = 92 }).State);
        Assert.Equal(HealthState.Watch, Of(n with { MediaErrors = 1 }).State);
        Assert.Equal(HealthState.Failing, Of(n with { CriticalWarning = 1 }).State);
        Assert.Equal(HealthState.Failing, Of(n with { Spare = 5 }).State);
    }

    [Fact]
    public void AtaAttributes_AreParsed_AndTheMovedSectorsAreWatched()
    {
        var s = new byte[512];
        void Put(int slot, int id, int value, int worst, long raw)
        {
            var at = 2 + slot * 12;
            s[at] = (byte)id;
            s[at + 3] = (byte)value;
            s[at + 4] = (byte)worst;
            for (var i = 0; i < 6; i++)
            {
                s[at + 5 + i] = (byte)(raw >> (8 * i));
            }
        }

        Put(0, 0x09, 58, 58, 31_400);
        Put(1, 0xC2, 112, 100, 0x0028_0012_0026);
        Put(2, 0x05, 198, 198, 8);
        var a = DriveHealth.ParseAttributes(s);
        Assert.Equal(3, a.Count);
        var h = new DriveHealth(null, null, null, a, false, false);
        Assert.Equal(31_400, h.Count(0x09));
        Assert.Equal(38, h.Count(0xC2));
        Assert.Null(h.Count(0xC5));
        Assert.Equal(HealthState.Watch, h.State);
        Assert.Equal(HealthState.Good, (h with { Attributes = [.. a.Where(x => x.Id != 0x05)] }).State);
        Assert.Equal(HealthState.Failing, (h with { PredictsFailure = true }).State);
    }

    [Theory]
    [InlineData((ushort)0b1110, (ushort)(3 << 1), 3, 3, false)]
    [InlineData((ushort)0b1110, (ushort)(2 << 1), 2, 3, true)]
    [InlineData((ushort)0b0110, (ushort)(2 << 1), 2, 2, false)]
    public void SataLink_FromIdentify(ushort w76, ushort w77, int now, int max, bool slower)
    {
        var link = DriveHealth.SataLink(w76, w77)!;
        Assert.Equal((LinkKind.Sata, now, max, slower), (link.Kind, link.Generation, link.MaxGeneration, link.Slower));
    }

    [Fact]
    public void PcieLink_SlowerWhenFewerLanesOrAnOlderGeneration()
    {
        Assert.False(new DriveLink(LinkKind.Pcie, 3, 4, 3, 4).Slower);
        Assert.True(new DriveLink(LinkKind.Pcie, 3, 4, 4, 4).Slower);
        Assert.True(new DriveLink(LinkKind.Pcie, 4, 2, 4, 4).Slower);
        Assert.False(new DriveLink(LinkKind.Usb).Slower);
    }

    [Fact]
    public void NoData_OrAdministratorsOnly()
    {
        Assert.Equal(HealthState.NoData, new DriveHealth(new DriveLink(LinkKind.Usb), null, null, [], null, false).State);
        Assert.Equal(HealthState.AdminOnly, new DriveHealth(null, null, null, [], null, true).State);
        Assert.Null(DriveHealth.Read(@"\\server\share\"));
    }

    [Fact]
    public void TheTempDrive_ReadsWithoutAdministratorRights()
    {
        var h = DriveHealth.Read(Path.GetPathRoot(Path.GetTempPath())!);
        Assert.NotNull(h);
        Assert.True(Enum.IsDefined(h.State));
    }

    private static DriveHealth Of(NvmeHealth n) => new(null, null, n, [], null, false);
}
