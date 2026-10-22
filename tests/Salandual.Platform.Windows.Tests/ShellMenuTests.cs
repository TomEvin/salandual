// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Platform.Windows.Tests/ShellMenuTests.cs

using Salandual.Platform.Windows.Shell;

namespace Salandual.Platform.Windows.Tests;

public sealed class ShellMenuTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-menu-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static IEnumerable<ShellMenuItem> All(IEnumerable<ShellMenuItem> items)
        => items.SelectMany(i => new[] { i }.Concat(All(i.Children)));

    [Fact]
    public async Task FileMenu_HasTheShellVerbs()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "a");
        using var menu = await ShellMenu.BuildAsync(_dir, ["a.txt"], extended: false);

        Assert.NotEmpty(menu.Items);
        var verbs = All(menu.Items).Select(i => i.Verb).Where(v => v is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("properties", verbs);
        Assert.Contains("delete", verbs);
        Assert.All(menu.Items.Where(i => !i.IsSeparator), i => Assert.False(string.IsNullOrEmpty(i.PlainText)));
        Assert.True(menu.IsFor(_dir, ["A.TXT"], extended: false));
        Assert.False(menu.IsFor(_dir, ["a.txt"], extended: true));
    }

    [Fact]
    public void ManagedShellExtensions_AreTheOnesServedByMscoree()
    {
        Assert.False(ManagedShellExtensions.IsManaged(Guid.NewGuid()));
        Assert.False(ManagedShellExtensions.IsManaged(new Guid("00021401-0000-0000-C000-000000000046")));
    }

    [Fact]
    public async Task SeveralItems_AndTheFolderBackground()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "b");
        using var two = await ShellMenu.BuildAsync(_dir, ["a.txt", "b.txt"], extended: false);
        Assert.Contains(All(two.Items), i => i.Verb == "properties");

        using var background = await ShellMenu.BuildAsync(_dir, [], extended: false);
        Assert.NotEmpty(background.Items);
    }

    [Fact]
    public async Task ExtendedMenu_HasAtLeastAsManyItems()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "a");
        using var normal = await ShellMenu.BuildAsync(_dir, ["a.txt"], extended: false);
        using var extended = await ShellMenu.BuildAsync(_dir, ["a.txt"], extended: true);
        Assert.True(All(extended.Items).Count() >= All(normal.Items).Count());
    }

    [Fact]
    public async Task MissingItem_Throws()
        => await Assert.ThrowsAnyAsync<Exception>(() => ShellMenu.BuildAsync(_dir, ["not here.txt"], extended: false));

    [Theory]
    [InlineData("&Open", "Open")]
    [InlineData("Save && close", "Save & close")]
    [InlineData("Cu&t\tCtrl+X", "Cut")]
    public void StripAccessKey(string text, string plain) => Assert.Equal(plain, ShellMenu.StripAccessKey(text));
}
