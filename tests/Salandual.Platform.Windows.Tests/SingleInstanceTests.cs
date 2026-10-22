// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/SingleInstanceTests.cs

using Salandual.Platform.Windows.Shell;

namespace Salandual.Platform.Windows.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void Names_FollowTheSettingsFolder()
    {
        Assert.Equal(SingleInstance.NameFor(@"C:\Users\me\AppData\Roaming\Salandual"), SingleInstance.NameFor(@"c:\users\ME\AppData\Roaming\Salandual\"));
        Assert.NotEqual(SingleInstance.NameFor(@"C:\Users\me\AppData\Roaming\Salandual"), SingleInstance.NameFor(@"D:\Tools\Salandual\Data"));
    }

    [Fact]
    public async Task ALaterStart_HandsItsFoldersToTheRunningOne()
    {
        var name = "SalandualTest-" + Guid.NewGuid().ToString("N");
        Assert.False(SingleInstance.TryHandOff(name, @"C:\", ["-L", @"D:\"]));

        var got = new TaskCompletionSource<(string, IReadOnlyList<string>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (SingleInstance.Listen(name, (folder, args) => got.TrySetResult((folder, args))))
        {
            Assert.True(await Task.Run(() => SingleInstance.TryHandOff(name, @"E:\Work", ["-L", @"D:\Photos", @"F:\Dropped folder"]), TestContext.Current.CancellationToken));
            var (folder, args) = await got.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(@"E:\Work", folder);
            Assert.Equal(["-L", @"D:\Photos", @"F:\Dropped folder"], args);
        }

        Assert.False(SingleInstance.TryHandOff(name, @"C:\", []));
    }
}
