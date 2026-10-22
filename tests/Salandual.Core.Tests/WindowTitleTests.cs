// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/WindowTitleTests.cs

using Salandual.Core.Panels;

namespace Salandual.Core.Tests;

public sealed class WindowTitleTests
{
    private const string Pictures = @"C:\Users\user\Pictures\Nyaralás 2026";

    [Theory]
    [InlineData(WindowTitle.Default, Pictures, "Nyaralás 2026 — Salandual")]
    [InlineData("$(Path) — $(App)", Pictures, @"C:\Users\user\Pictures\Nyaralás 2026 — Salandual")]
    [InlineData("$(App) — $(Path)", Pictures, @"Salandual — C:\Users\user\Pictures\Nyaralás 2026")]
    [InlineData("$(Folder)", Pictures, "Nyaralás 2026")]
    [InlineData("$(Drive) $(Folder) · $(App)", Pictures, "C: Nyaralás 2026 · Salandual")]
    [InlineData(WindowTitle.Default, @"C:\", "C: — Salandual")]
    [InlineData(WindowTitle.Default, @"D:\Backup\", "Backup — Salandual")]
    [InlineData(WindowTitle.Default, @"\\nas\photos\2025", "2025 — Salandual")]
    [InlineData("$(Folder) $(Nope)", Pictures, "Nyaralás 2026 $(Nope)")]
    [InlineData("   ", Pictures, "Salandual")]
    public void Expand(string template, string path, string expected) => Assert.Equal(expected, WindowTitle.Expand(template, path));

    [Fact]
    public void NoFolderYet_IsTheAppName() => Assert.Equal("Salandual", WindowTitle.Expand(WindowTitle.Default, ""));

    [Fact]
    public void Presets_StartWithTheDefault() => Assert.Equal(WindowTitle.Default, WindowTitle.Presets[0].Template);
}
