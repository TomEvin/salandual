// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/NetworkTests.cs

using Salandual.Core.Vfs;
using Salandual.Platform.Windows.Shell;

namespace Salandual.Platform.Windows.Tests;

public sealed class NetworkTests
{
    [Fact]
    public void AShare_IsReadFromItsRegistryValue()
    {
        Assert.Equal((@"D:\Projects", "Team files"), WindowsShellPlaces.ParseShare(["CATimeout=0", "Path=D:\\Projects", "Remark=Team files", "Type=0"]));
        Assert.Equal((@"C:\Scans", null), WindowsShellPlaces.ParseShare(["Path=C:\\Scans", "Remark=", "Type=0"]));
        Assert.Equal((null, null), WindowsShellPlaces.ParseShare(["Path=C:\\Spool", "Type=1"]));
        Assert.Equal((null, null), WindowsShellPlaces.ParseShare(["Path=relative", "Type=0"]));
    }

    [Theory]
    [InlineData("Media", true)]
    [InlineData("C$", true)]
    [InlineData("..", false)]
    [InlineData(@"a\b", false)]
    [InlineData("a/b", false)]
    [InlineData("", false)]
    public void AServersShareName_IsOnePartOfAPath(string name, bool ok) => Assert.Equal(ok, WindowsShellPlaces.IsShareName(name));

    [Fact]
    public async Task TheFreeLetters_YourNetworkDrives_AndThisPcsShares_AreOnlyRead()
    {
        var places = new WindowsShellPlaces();
        var free = places.FreeLetters();
        var used = DriveInfo.GetDrives().Select(d => d.Name[..2].ToUpperInvariant()).ToHashSet();
        Assert.All(free, l => Assert.DoesNotContain(l, used));
        Assert.Equal(free.OrderByDescending(l => l), free);

        var drives = await places.NetworkDrivesAsync();
        Assert.All(drives, d => Assert.StartsWith(@"\\", d.Remote));
        Assert.DoesNotContain(free, l => drives.Any(d => d.Letter == l));

        var shares = await places.ListAsync(ShellPlace.Shares, TestContext.Current.CancellationToken);
        Assert.All(shares, s => Assert.True(Path.IsPathFullyQualified(s.Target)));
    }
}
