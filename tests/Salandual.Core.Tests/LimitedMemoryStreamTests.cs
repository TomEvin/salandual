// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/LimitedMemoryStreamTests.cs

using Salandual.Core.Search;

namespace Salandual.Core.Tests;

public sealed class LimitedMemoryStreamTests
{
    [Fact]
    public void EveryWayOfWriting_KeepsToTheLimit()
    {
        using var under = new LimitedMemoryStream(8);
        under.Write([1, 2, 3], 0, 3);
        under.Write((ReadOnlySpan<byte>)[4, 5]);
        under.WriteByte(6);
        new MemoryStream([7, 8]).CopyTo(under);
        Assert.False(under.Overflow);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], under.ToArray());

        under.WriteByte(9);
        Assert.True(under.Overflow);
        Assert.Equal(8, under.Length);
    }
}
