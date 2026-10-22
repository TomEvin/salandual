// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/DirectoryLineTests.cs

using Avalonia.Headless.XUnit;
using Salandual.App.Panels;

namespace Salandual.App.Tests;

public sealed class DirectoryLineTests
{
    [AvaloniaFact]
    public void ChangingFolder_KeepsTheDrivesFreeSpace_UntilTheFreshValueArrives()
    {
        var line = new DirectoryLine { Path = @"Q:\Photos" };
        Assert.Null(line.Space);
        line.SetSpace(@"Q:\Photos", 100, 400);
        Assert.Equal((100L, 400L), line.Space);

        line.Path = @"Q:\Photos\2026";
        Assert.Equal((100L, 400L), line.Space);
        line.SetSpace(@"Q:\Photos\2026", 90, 400);
        Assert.Equal((90L, 400L), line.Space);

        line.Path = @"R:\";
        line.SetSpace(@"Q:\Photos", 80, 400);
        Assert.Null(line.Space);
        line.Path = @"Q:\";
        Assert.Equal((80L, 400L), line.Space);
    }

    [Fact]
    public void Split_RootAndParts()
        => Assert.Equal(
            [("C:", @"C:\"), ("Users", @"C:\Users"), ("Nyaralás 2026", @"C:\Users\Nyaralás 2026")],
            DirectoryLine.Split(@"C:\Users\Nyaralás 2026"));
}
