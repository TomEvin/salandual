// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/VsCodeThemesTests.cs

using System.Globalization;
using System.IO.Compression;
using System.Text;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class VsCodeThemesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-vscode-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly DateTime _now = new(2026, 10, 16, 21, 10, 0);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void VsCodeTheme_BecomesAWholeTheme_WithIncludeAlphaAndDefaults()
    {
        Write("base.json", """
            { "type": "dark", "colors": { "editor.background": "#202020", "sideBar.background": "#181818" },
              "tokenColors": [ { "scope": "string", "settings": { "foreground": "#CE9178" } } ] }
            """);
        var path = Write("mine.json", """
            { "name": "Mine", "include": "./base.json",
              "colors": { "editor.foreground": "#D4D4D4", "list.inactiveSelectionBackground": "#FFFFFF80", "statusBar.background": "#00000000" },
              "tokenColors": [ { "scope": ["comment"], "settings": { "foreground": "#6A9955" } },
                               { "scope": "support.type.property-name", "settings": { "foreground": "#9CDCFE" } } ] }
            """);

        var theme = VsCodeThemes.ToTheme(path, out var error);

        Assert.Null(error);
        Assert.NotNull(theme);
        Assert.Equal("Mine", theme.Name);
        Assert.True(theme.Dark);
        var c = theme.Colors;
        Assert.Equal("#202020", c[ColorTokens.PanelBackground]);
        Assert.Equal("#181818", c[ColorTokens.WindowBackground]);
        Assert.Equal("#D4D4D4", c[ColorTokens.Text]);
        Assert.Equal("#909090", c[ColorTokens.SelectionBackground]);
        Assert.Equal("#252526", c[ColorTokens.ColumnHeader]);
        Assert.False(c.ContainsKey(ColorTokens.InformationLine));
        Assert.Equal("#CE9178", c[ColorTokens.SyntaxString]);
        Assert.Equal("#6A9955", c[ColorTokens.SyntaxComment]);
        Assert.Equal("#9CDCFE", c[ColorTokens.SyntaxKey]);
        Assert.Equal("#D4D4D4", c[ColorTokens.SyntaxNumber]);
        Assert.All(c.Keys, k => Assert.True(ColorTokens.IsKnown(k), k));
    }

    [Fact]
    public void VsCodeTheme_LightFromItsBackground_AndAnIncludeElsewhereIsNotRead()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_dir, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "x.json"), """{ "colors": { "editor.foreground": "#FF0000" } }""");
        var path = Write("light.json", """{ "include": "./outside/x.json", "colors": { "editor.background": "#FAFAFA" } }""");

        var theme = VsCodeThemes.ToTheme(path, out _)!;

        Assert.False(theme.Dark);
        Assert.Equal("#333333", theme.Colors[ColorTokens.Text]);
        Assert.Null(VsCodeThemes.ToTheme(Write("empty.json", "{}"), out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Ids_ArePlainNames()
    {
        Assert.Equal("vscode-dark-modern", VsCodeThemes.IdFor("Dark Modern"));
        Assert.Equal("vscode-dark-visual-studio", VsCodeThemes.IdFor("Dark (Visual Studio)"));
        Assert.Equal("vscode-theme", VsCodeThemes.IdFor("★"));
        Assert.True(ThemeFiles.IsSafeId(VsCodeThemes.IdFor(new string('x', 200))));
    }

    [Fact]
    public void OwnThemes_AreWrittenFromVsCode()
    {
        var file = Path.Combine(RepoRoot(), "src", "Salandual.Core", "Settings", "ThemeCatalog.VsCode.cs");
        if (Environment.GetEnvironmentVariable("SALANDUAL_WRITE_VSCODE_THEMES") == "1")
        {
            File.WriteAllText(file, Generate());
        }

        var own = ThemeCatalog.Shipped.Where(t => t.Id.StartsWith("code-", StringComparison.Ordinal)).ToList();
        Assert.Equal(19, own.Count);
        Assert.Contains(own, t => t.Name == "Dark Modern" && t.Dark);
        Assert.Contains(own, t => t.Name == "Light High Contrast" && !t.Dark);
        Assert.All(own, t =>
        {
            Assert.Equal(ThemeOrigin.Shipped, t.Origin);
            Assert.True(ThemeFiles.IsSafeId(t.Id));
            Assert.All(t.Colors, kv => Assert.True(ColorTokens.IsKnown(kv.Key) && ColorMath.TryParseHex(kv.Value, out _), kv.Key));
            Assert.True(t.Colors.ContainsKey(ColorTokens.PanelBackground) && t.Colors.ContainsKey(ColorTokens.SyntaxKeyword), t.Name);
        });
        Assert.Equal(ThemeCatalog.Shipped.Count, ThemeCatalog.Shipped.Select(t => t.Id).Distinct().Count());
    }

    private static string Generate()
    {
        var themes = VsCodeThemes.Installed().Where(t => t.Source == "VS Code").ToList();
        Assert.Equal(19, themes.Count);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $$"""
            // SPDX-License-Identifier: GPL-3.0-or-later

            namespace Salandual.Core.Settings;

            // Written by VsCodeThemesTests.OwnThemes_AreWrittenFromVsCode (SALANDUAL_WRITE_VSCODE_THEMES=1) from the theme files
            // of VS Code {{VsCodeThemes.InstalledVersion()}}, read with VsCodeThemes.ToTheme. Don't edit by hand: write it again.
            public static partial class ThemeCatalog
            {
                /// <summary>VS Code's own themes, shipped for PCs without VS Code (microsoft/vscode, MIT).</summary>
                private static IEnumerable<ColorTheme> VsCodeOwn() =>
                [

            """);
        foreach (var vs in themes)
        {
            var theme = VsCodeThemes.ToTheme(vs.Path, out var error, vs.UiTheme, vs.Label);
            Assert.True(theme is not null, error);
            var colors = string.Join(' ', ColorTokens.All.Where(t => theme.Colors.ContainsKey(t.Token)).Select(t => $"{t.Token}={theme.Colors[t.Token]}"));
            var id = VsCodeThemes.IdFor(vs.Label).Replace("vscode-", "code-", StringComparison.Ordinal);
            text.Append(CultureInfo.InvariantCulture, $"        VsShip(\"{id}\", \"{vs.Label}\", dark: {(vs.Dark ? "true" : "false")},\n            \"{colors}\"),\n");
        }

        text.Append("    ];\n}\n");
        return text.ToString().ReplaceLineEndings("\n");
    }

    [Fact]
    public void ThemeFile_ExportAndImport_KeepsColoursSettingsAndImage()
    {
        var a = new AppearanceSettings();
        var mine = ThemeEditing.Duplicate(ThemeCatalog.Find(a, "nord"), a, _now) with
        {
            Accent = ThemeAccent.Custom,
            CustomAccent = "#FF8800",
            CodeBackground = CodeBackground.Own,
            Background = new BackgroundImageSettings { Path = "photo.png", Placement = BackgroundPlacement.Window, Fit = BackgroundFit.Tile, Opacity = 0.3, Blur = true },
        };
        var image = Path.Combine(_dir, "photo.png");
        File.WriteAllBytes(image, [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3]);
        var file = Path.Combine(_dir, ThemePackage.FileName(mine));
        ThemePackage.Export(mine, image, file);
        Assert.Equal("Nord (copy).saldtheme", Path.GetFileName(file));

        var data = Directory.CreateDirectory(Path.Combine(_dir, "data")).FullName;
        a = ThemeEditing.Add(a, mine);
        var imported = Assert.Single(ThemePackage.Import(file, a, data, _now));

        Assert.Equal("Nord (copy) 2", imported.Name);
        Assert.NotEqual(mine.Id, imported.Id);
        Assert.Equal(ThemeOrigin.Mine, imported.Origin);
        Assert.Equal(mine.Colors, imported.Colors);
        Assert.Equal(mine.Colors, imported.Original);
        Assert.Equal((ThemeAccent.Custom, "#FF8800", CodeBackground.Own), (imported.Accent, imported.CustomAccent, imported.CodeBackground));
        Assert.Equal(mine.Background, imported.Background);
        Assert.Equal("Nord (copy).saldtheme", imported.ImportedFrom);
        Assert.Equal(File.ReadAllBytes(image), File.ReadAllBytes(ThemeFiles.ImagePath(data, imported)!));
    }

    [Fact]
    public void ThemeFile_OnlyItsOwnImageIsTaken_AndBadFilesAreRefused()
    {
        var data = Directory.CreateDirectory(Path.Combine(_dir, "data")).FullName;
        var a = new AppearanceSettings();

        var sneaky = Zip("sneaky.saldtheme", ("theme.json", """
            { "format": "salandual-theme", "version": 1, "name": "Sneaky", "dark": false, "colors": { "Text": "#112233", "Nope": "#000000" },
              "background": { "file": "../../evil.png" } }
            """), ("../../evil.png", "x"), ("other.exe", "x"));
        var theme = Assert.Single(ThemePackage.Import(sneaky, a, data, _now));
        Assert.Null(theme.Background.Path);
        Assert.Equal(new Dictionary<string, string> { [ColorTokens.Text] = "#112233" }, theme.Colors);
        Assert.False(File.Exists(Path.Combine(_dir, "evil.png")));
        Assert.DoesNotContain(Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories), f => f.EndsWith(".exe", StringComparison.Ordinal) || f.EndsWith("evil.png", StringComparison.Ordinal));

        Assert.Throws<InvalidDataException>(() => ThemePackage.Import(Zip("none.saldtheme", ("readme.txt", "hi")), a, data, _now));
        Assert.Throws<InvalidDataException>(() => ThemePackage.Import(Zip("newer.saldtheme", ("theme.json", """{ "format": "salandual-theme", "version": 2 }""")), a, data, _now));
        Assert.Throws<InvalidDataException>(() => ThemePackage.Import(Zip("big.saldtheme", ("theme.json", new string(' ', 2 * 1024 * 1024))), a, data, _now));
        Assert.Throws<InvalidDataException>(() => ThemePackage.Import(Write("broken.saldtheme", "not a zip"), a, data, _now));
        Assert.Throws<InvalidDataException>(() => ThemePackage.Import(Write("text.json", "hello"), a, data, _now));
        Assert.Throws<FileNotFoundException>(() => ThemePackage.Import(Path.Combine(_dir, "missing.json"), a, data, _now));
    }

    [Fact]
    public void ColourFile_FromBeforeThemes_MakesALightAndADarkTheme()
    {
        var path = Write("my-colours.json", """
            { "format": "salandual-colors", "version": 1,
              "light": { "Text": "#112233", "Unknown": "#000000" }, "dark": { "PanelBackground": "#101010" } }
            """);

        var themes = ThemePackage.Import(path, new AppearanceSettings(), _dir, _now);

        Assert.Equal(["my-colours (light)", "my-colours (dark)"], themes.Select(t => t.Name));
        Assert.Equal([false, true], themes.Select(t => t.Dark));
        Assert.Equal("#112233", themes[0].Colors[ColorTokens.Text]);
        Assert.Equal(ColorTokens.SalandualLight[ColorTokens.PanelBackground], themes[0].Colors[ColorTokens.PanelBackground]);
        Assert.Equal("#101010", themes[1].Colors[ColorTokens.PanelBackground]);
        Assert.NotEqual(themes[0].Id, themes[1].Id);
        Assert.Throws<InvalidDataException>(() => ThemePackage.Import(Write("empty.json", """{ "format": "salandual-colors", "light": {} }"""), new AppearanceSettings(), _dir, _now));
    }

    [Fact]
    public void VsCodeImports_AndTheCopyMadeOnFirstUse_UpdatesOnRequest()
    {
        var path = Write("ocean.json", """{ "name": "Ocean", "type": "dark", "colors": { "editor.background": "#001122" } }""");
        var a = new AppearanceSettings();

        var imported = Assert.Single(ThemePackage.Import(path, a, _dir, _now));
        Assert.Equal(("Ocean", ThemeOrigin.Mine, "ocean.json"), (imported.Name, imported.Origin, imported.ImportedFrom));

        var vs = new VsCodeTheme("Ocean", "vs-dark", path, "someone.ocean");
        var copy = ThemePackage.CopyFromVsCode(vs, _now);
        Assert.Equal(("vscode-ocean", ThemeOrigin.VsCode, "Ocean", path), (copy.Id, copy.Origin, copy.CopiedFrom, copy.SourcePath));
        Assert.True(copy.ReadOnly);
        Assert.Equal("#001122", copy.Colors[ColorTokens.PanelBackground]);

        File.WriteAllText(path, """{ "name": "Ocean", "type": "dark", "colors": { "editor.background": "#002233" } }""");
        var later = _now.AddDays(1);
        var updated = ThemePackage.UpdateFromVsCode(copy, later);
        Assert.Equal(("#002233", later, copy.Id), (updated.Colors[ColorTokens.PanelBackground], updated.CopiedOn, updated.Id));

        File.Delete(path);
        Assert.Throws<InvalidDataException>(() => ThemePackage.UpdateFromVsCode(copy, later));
    }

    private string Zip(string name, params (string Entry, string Text)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, text) in entries)
        {
            using var stream = zip.CreateEntry(entry).Open();
            stream.Write(Encoding.UTF8.GetBytes(text));
        }

        return path;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Salandual.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (Salandual.slnx) not found.");
    }
}
