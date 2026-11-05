// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/InstallerTests.cs

using Salandual.Core;

namespace Salandual.App.Tests;

public sealed class InstallerTests
{
    private const string Installed = @"C:\Users\you\AppData\Local\Salandual\current\salandual.exe";

    [Fact]
    public void Uninstalling_SwitchesOffStartWithWindows_WhenItStartsThisCopy()
    {
        var start = new MemoryStartWithWindows { Registered = Installed };
        Installer.Uninstalling(start, Installed.ToUpperInvariant());
        Assert.Null(start.Registered);
    }

    [Fact]
    public void Uninstalling_LeavesAnotherCopysStartWithWindows()
    {
        var start = new MemoryStartWithWindows { Registered = @"D:\Tools\Salandual\salandual.exe" };
        Installer.Uninstalling(start, Installed);
        Assert.Equal(@"D:\Tools\Salandual\salandual.exe", start.Registered);

        Installer.Uninstalling(start, null);
        Assert.NotNull(start.Registered);
    }
}
