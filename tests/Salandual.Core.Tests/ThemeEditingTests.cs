// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/ThemeEditingTests.cs

using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class ThemeEditingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-themes-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly DateTime _now = new(2026, 10, 16, 21, 10, 0);

    [Fact]
    public void Duplicate_CopiesEverything_AndIsYours()
    {
        var a = new AppearanceSettings();
        var nord = ThemeCatalog.Find(a, "nord");
        var copy = ThemeEditing.Duplicate(nord, a, _now);

        Assert.Equal("Nord (copy)", copy.Name);
        Assert.Equal(ThemeOrigin.Mine, copy.Origin);
        Assert.False(copy.ReadOnly);
        Assert.True(copy.Dark);
        Assert.Equal(nord.Colors, copy.Colors);
        Assert.Equal(nord.Colors, copy.Original);
        Assert.Equal("Nord", copy.CopiedFrom);
        Assert.Equal(_now, copy.CopiedOn);
        Assert.True(ThemeFiles.IsSafeId(copy.Id));
        Assert.Equal(ColorTokens.Resolve(nord), ColorTokens.Resolve(copy));

        a = ThemeEditing.Add(a, copy);
        Assert.Equal("Nord (copy) 2", ThemeEditing.Duplicate(nord, a, _now).Name);
        var fresh = ThemeEditing.New(a, _now);
        Assert.Equal("New theme", fresh.Name);
        Assert.Equal(ThemeCatalog.BuiltIn[0].Colors, fresh.Colors);
        Assert.NotEqual(copy.Id, fresh.Id);
    }

    [Fact]
    public void Colours_SetResetAndFill()
    {
        var a = new AppearanceSettings();
        var t = ThemeEditing.New(a, _now);

        var changed = ThemeEditing.SetColor(t, ColorTokens.PanelBackground, new Rgb(0xFF, 0xF8, 0xE7));
        Assert.True(ThemeEditing.IsChanged(changed, ColorTokens.PanelBackground));
        Assert.Equal("#FFFFFF", ThemeEditing.ResetColor(changed, ColorTokens.PanelBackground).Colors[ColorTokens.PanelBackground]);

        changed = ThemeEditing.SetColor(t, ColorTokens.SelectedText, new Rgb(0xAA, 0, 0));
        Assert.True(ThemeEditing.IsChanged(changed, ColorTokens.SelectedText));
        Assert.False(ThemeEditing.ResetColor(changed, ColorTokens.SelectedText).Colors.ContainsKey(ColorTokens.SelectedText));
        Assert.False(ThemeEditing.IsChanged(t, ColorTokens.SelectedText));

        var dark = ThemeEditing.SetColor(ThemeEditing.SetColor(t, ColorTokens.PanelBackground, new Rgb(0x20, 0x20, 0x28)), ColorTokens.Text, new Rgb(0xEE, 0xEE, 0xEE)) with { Dark = true };
        var filled = ThemeEditing.FillFromMain(dark, ColorTokens.Resolve(dark));
        Assert.Equal(5, filled.Colors.Count);
        Assert.Equal("#202028", filled.Colors[ColorTokens.PanelBackground]);
        var resolved = ColorTokens.Resolve(filled);
        Assert.Equal(ColorMath.Mix(new Rgb(0xEE, 0xEE, 0xEE), new Rgb(0x20, 0x20, 0x28), 0.32), resolved[ColorTokens.SecondaryText]);
    }

    [Fact]
    public void ChangeRenameRemove_OnlyYourOwn_AndTheScheduleFallsBack()
    {
        var a = new AppearanceSettings();
        var mine = ThemeEditing.Duplicate(ThemeCatalog.Find(a, "dracula"), a, _now);
        a = ThemeEditing.Add(a, mine) with { Schedule = new ThemeSchedule { Mode = ScheduleMode.Windows, Light = ThemeCatalog.SalandualLightId, Dark = mine.Id } };

        Assert.Equal("Nord 2", ThemeEditing.Rename(mine, " Nord ", a).Name);
        Assert.Equal(mine.Name, ThemeEditing.Rename(mine, "   ", a).Name);
        Assert.Equal("Night", ThemeEditing.Rename(mine, "Night", a).Name);

        var renamed = ThemeEditing.Change(a, mine.Id, t => t with { Name = "Night" });
        Assert.Equal("Night", renamed.Themes[0].Name);
        Assert.Same(a.Themes[0].Colors, ThemeEditing.Change(a, "dracula", t => t with { Name = "x" }).Themes[0].Colors);

        var removed = new AppSettings { Appearance = ThemeEditing.Remove(a, mine.Id) }.Normalize().Appearance;
        Assert.Empty(removed.Themes);
        Assert.Equal(ThemeCatalog.SalandualDarkId, removed.Schedule.Dark);
    }

    [Fact]
    public void Files_CopyImage_NumbersTheSameName_AndStayInTheFolder()
    {
        var source = Path.Combine(_dir, "Fjord.JPG");
        File.WriteAllBytes(source, [1, 2, 3]);
        var data = Path.Combine(_dir, "data");

        Assert.Equal("Fjord.jpg", ThemeFiles.CopyImage(data, "mine-1", source));
        Assert.Equal("Fjord (2).jpg", ThemeFiles.CopyImage(data, "mine-1", source));
        Assert.True(File.Exists(Path.Combine(data, "themes", "mine-1", "Fjord (2).jpg")));

        ThemeFiles.DeleteImagesExcept(data, "mine-1", "Fjord (2).jpg");
        Assert.Equal(["Fjord (2).jpg"], Directory.EnumerateFiles(Path.Combine(data, "themes", "mine-1")).Select(Path.GetFileName));

        var theme = new ColorTheme { Id = "mine-1", Background = new BackgroundImageSettings { Path = "Fjord (2).jpg" } };
        Assert.True(ThemeFiles.CopyImageTo(data, theme, "mine-2"));
        Assert.True(File.Exists(Path.Combine(data, "themes", "mine-2", "Fjord (2).jpg")));

        ThemeFiles.DeleteFolder(data, "mine-1");
        Assert.False(Directory.Exists(Path.Combine(data, "themes", "mine-1")));

        Assert.Null(ThemeFiles.Folder(data, @"..\escape"));
        Assert.Null(ThemeFiles.Folder(data, ""));
        Assert.Throws<IOException>(() => ThemeFiles.CopyImage(data, "../up", source));
        Assert.Equal("photo.jpg", ThemeFiles.SafeFileName(@"..\..\photo.jpg"));
        Assert.Null(ThemeFiles.SafeFileName(".."));
        Assert.Throws<FileNotFoundException>(() => ThemeFiles.CopyImage(data, "mine-3", Path.Combine(_dir, "missing.png")));
    }

    [Fact]
    public void Files_RefuseAnImageThatIsTooLarge()
    {
        var big = Path.Combine(_dir, "huge.png");
        using (var f = File.Create(big))
        {
            f.SetLength(ThemeFiles.MaxImageBytes + 1);
        }

        var ex = Assert.Throws<IOException>(() => ThemeFiles.CopyImage(Path.Combine(_dir, "data"), "mine-1", big));
        Assert.Contains("64", ex.Message, StringComparison.Ordinal);
    }
}
