// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/ProductInfoTests.cs

using Salandual.Core;

namespace Salandual.Core.Tests;

public sealed class ProductInfoTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("salandual-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Identifier_IsAscii()
    {
        Assert.True(ProductInfo.Identifier.All(char.IsAscii));
        Assert.True(ProductInfo.ExecutableName.All(char.IsAscii));
    }

    [Theory]
    [InlineData("0.1.0", "v0.1")]
    [InlineData("0.1.2", "v0.1.2")]
    [InlineData("1.0.0", "v1.0")]
    [InlineData("0.2.0-beta.1", "v0.2-beta.1")]
    [InlineData("0.1", "v0.1")]
    [InlineData("0", "v0")]
    public void FormatVersion_LeavesOutAPatchOfZero(string version, string expected) => Assert.Equal(expected, ProductInfo.FormatVersion(version));

    [Fact]
    public void Version_IsTheFirstRelease() => Assert.Equal("v0.1", ProductInfo.DisplayVersion);

    [Fact]
    public void DataDirectory_DefaultsToRoamingAppData()
    {
        var app = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var roaming = Path.Combine(_root, "roaming");

        Assert.Equal(Path.Combine(roaming, "Salandual"), ProductInfo.GetDataDirectory(app, roaming));
    }

    [Fact]
    public void DataDirectory_IsNextToExe_WhenPortableFlagExists()
    {
        var app = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        File.WriteAllText(Path.Combine(app, ProductInfo.PortableFlagFileName), "");

        Assert.Equal(Path.Combine(app, "Data"), ProductInfo.GetDataDirectory(app, Path.Combine(_root, "roaming")));
    }
}
