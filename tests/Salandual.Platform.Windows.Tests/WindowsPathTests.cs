// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/WindowsPathTests.cs

using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Tests;

public sealed class WindowsPathTests
{
    [Theory]
    [InlineData(@"C:\Data", @"\\?\C:\Data")]
    [InlineData(@"C:", @"\\?\C:\")]
    [InlineData(@"C:\", @"\\?\C:\")]
    [InlineData(@"C:/a/b", @"\\?\C:\a\b")]
    [InlineData(@"C:\name.", @"\\?\C:\name.")]
    [InlineData(@"C:\a \b ", @"\\?\C:\a \b ")]
    [InlineData(@"\\server\share\x", @"\\?\UNC\server\share\x")]
    [InlineData(@"\\?\C:\already", @"\\?\C:\already")]
    public void ToExtended_KeepsNamesExact(string path, string expected)
        => Assert.Equal(expected, WindowsPath.ToExtended(path));

    [Theory]
    [InlineData("relative")]
    [InlineData(@"\rooted-no-drive")]
    [InlineData(@"\\.\device")]
    public void ToExtended_RejectsNonQualified(string path)
        => Assert.Throws<ArgumentException>(() => WindowsPath.ToExtended(path));

    [Theory]
    [InlineData(@"\\?\C:\Data", @"C:\Data")]
    [InlineData(@"\\?\UNC\server\share", @"\\server\share")]
    [InlineData(@"D:\x", @"D:\x")]
    public void FromExtended_GivesDisplayPath(string path, string expected)
        => Assert.Equal(expected, WindowsPath.FromExtended(path));

    [Theory]
    [InlineData(@"C:\", null)]
    [InlineData(@"C:", null)]
    [InlineData(@"C:\a", @"C:\")]
    [InlineData(@"C:\a\", @"C:\")]
    [InlineData(@"C:\a\b", @"C:\a")]
    [InlineData(@"C:\a\name.", @"C:\a")]
    [InlineData(@"\\server\share", null)]
    [InlineData(@"\\server\share\", null)]
    [InlineData(@"\\server\share\x", @"\\server\share")]
    [InlineData(@"\\server\share\x\y", @"\\server\share\x")]
    [InlineData(@"\\?\C:\a\b", @"C:\a")]
    [InlineData("relative", null)]
    public void GetParent(string path, string? expected)
        => Assert.Equal(expected, WindowsPath.GetParent(path));

    [Theory]
    [InlineData(@"C:\a\b", "b")]
    [InlineData(@"C:\a\b\", "b")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"\\server\share", @"\\server\share")]
    [InlineData(@"C:\a\trailing dot.", "trailing dot.")]
    public void GetName(string path, string expected)
        => Assert.Equal(expected, WindowsPath.GetName(path));

    [Theory]
    [InlineData(@"C:\", "a", @"C:\a")]
    [InlineData(@"C:\x", "a", @"C:\x\a")]
    [InlineData(@"\\server\share", "a", @"\\server\share\a")]
    public void Join(string dir, string name, string expected)
        => Assert.Equal(expected, WindowsPath.Join(dir, name));
}
