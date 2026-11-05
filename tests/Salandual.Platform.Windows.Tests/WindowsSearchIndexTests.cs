// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/WindowsSearchIndexTests.cs

using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Tests;

public sealed class WindowsSearchIndexTests
{
    [Theory]
    [InlineData(@"file:///C:\Users\*", @"C:\Users")]
    [InlineData("file:///D:/Work/", @"D:\Work")]
    [InlineData(@"file:///E:\", @"E:\")]
    [InlineData(@"file:///C:\Users\*\AppData\*", null)]
    [InlineData(@"iehistory://{S-1-5-21}\", null)]
    [InlineData(@"mapi16://{S-1-5-21}/", null)]
    public void ARule_GivesItsFolder(string url, string? folder)
        => Assert.Equal(folder, WindowsSearchIndex.FolderOf(url));

    [Fact]
    public void TheRealIndex_GivesFullPaths_WithTheLettersInOrder()
    {
        var index = new WindowsSearchIndex();
        if (!index.IsAvailable())
        {
            return;
        }

        Assert.All(index.CoveredFolders(), f => Assert.True(Path.IsPathFullyQualified(f)));
        var found = index.Find("e", 5, CancellationToken.None);
        Assert.InRange(found.Count, 0, 5);
        Assert.All(found, e =>
        {
            Assert.True(Path.IsPathFullyQualified(e.Folder));
            Assert.Contains("e", e.Name, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Empty(index.Find("zq'x%_[;--", 5, CancellationToken.None));
    }
}
