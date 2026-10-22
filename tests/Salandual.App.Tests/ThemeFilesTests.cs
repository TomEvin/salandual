// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ThemeFilesTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class ThemeFilesTests : IDisposable
{
    private static App CurrentApp => (App)Application.Current!;

    private static SettingsService Settings => CurrentApp.Services.Settings;

    private static string Data => CurrentApp.Services.DataDirectory;

    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-theme-files-").FullName;

    public ThemeFilesTests() => Settings.Update(_ => new AppSettings());

    public void Dispose()
    {
        OptionsWindow.VsCodeSource = () => [];
        foreach (var t in Settings.Current.Appearance.Themes)
        {
            ThemeFiles.DeleteFolder(Data, t.Id);
        }

        Settings.Update(_ => new AppSettings());
        Dispatcher.UIThread.RunJobs();
        Directory.Delete(_dir, recursive: true);
    }

    private static OptionsWindow Open()
    {
        var w = new OptionsWindow { Width = 1100, Height = 900 };
        w.Show();
        w.ShowCategory("themes");
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Button ButtonNamed(Control root, string text)
        => root.GetLogicalDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && (b.Content as string == text || (b.Content as TextBlock)?.Text == text));

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    private static Button Card(OptionsWindow w, string id) => w.ThemeCards.First(c => (string?)c.Tag == id);

    private (VsCodeTheme Theme, string Path) FakeVsCode(string background)
    {
        var path = Path.Combine(_dir, "ocean-color-theme.json");
        File.WriteAllText(path, $$"""{ "name": "Ocean", "type": "dark", "colors": { "editor.background": "{{background}}" } }""");
        var vs = new VsCodeTheme("Ocean", "vs-dark", path, "someone.ocean");
        OptionsWindow.VsCodeSource = () => [vs];
        return (vs, path);
    }

    [AvaloniaFact]
    public void VsCodeTheme_FirstUseCopiesIt_AndUpdateBringsItsNewColours()
    {
        var (_, path) = FakeVsCode("#001122");
        var w = Open();

        Click(Card(w, "vscode-ocean"));
        Assert.Empty(Settings.Current.Appearance.Themes);
        Capture(w, "themes-vscode-read.png");
        Assert.Equal(ColorMath.ParseHex("#001122"), w.ThemeBigPreview!.Colors![ColorTokens.PanelBackground]);
        Assert.Contains(w.Page.GetLogicalDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith("Read from your VS Code.", StringComparison.Ordinal) == true);

        Click(ButtonNamed(w.Page, "Use in dark mode"));
        var copy = Assert.Single(Settings.Current.Appearance.Themes);
        Assert.Equal(("vscode-ocean", ThemeOrigin.VsCode, path), (copy.Id, copy.Origin, copy.SourcePath));
        Assert.Equal("vscode-ocean", Settings.Current.Appearance.Schedule.Dark);
        Assert.Contains(w.Page.GetLogicalDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith("Copied into Salandual the first time it was used", StringComparison.Ordinal) == true);
        Assert.Single(w.ThemeCards, c => (string?)c.Tag == "vscode-ocean");
        Capture(w, "themes-vscode-copied.png");

        File.WriteAllText(path, """{ "name": "Ocean", "type": "dark", "colors": { "editor.background": "#003344" } }""");
        Assert.Equal("#001122", Settings.Current.Appearance.Themes[0].Colors[ColorTokens.PanelBackground]);
        Click(ButtonNamed(w.Page, "Update from VS Code"));
        Assert.Equal("#003344", Settings.Current.Appearance.Themes[0].Colors[ColorTokens.PanelBackground]);
        Assert.Equal("Ocean has the colours it has in VS Code now.", w.ThemeNoticeText!.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void Export_ThenImport_MakesATheme_OfYours()
    {
        var a = Settings.Current.Appearance;
        var mine = ThemeEditing.Duplicate(ThemeCatalog.Find(a, "dracula"), a, DateTime.Now);
        Settings.Update(s => s with { Appearance = ThemeEditing.Add(s.Appearance, mine) });
        var w = Open();

        var file = Path.Combine(_dir, "Dracula (copy).saldtheme");
        w.ExportTheme(mine, file);
        Dispatcher.UIThread.RunJobs();
        Assert.True(File.Exists(file));
        Assert.Equal("Saved Dracula (copy).saldtheme.", w.ThemeNoticeText!.Text);

        w.ImportThemeFile(file);
        Dispatcher.UIThread.RunJobs();
        var imported = Settings.Current.Appearance.Themes[^1];
        Assert.Equal(("Dracula (copy) 2", ThemeOrigin.Mine, "Dracula (copy).saldtheme"), (imported.Name, imported.Origin, imported.ImportedFrom));
        Assert.Equal(mine.Colors, imported.Colors);
        Assert.Equal("Imported Dracula (copy) 2 into My themes.", w.ThemeNoticeText!.Text);
        Assert.Contains("picked", Card(w, imported.Id).Classes);

        w.ExportTheme(ThemeCatalog.Find(a, "code-dark-modern"), Path.Combine(_dir, "modern.saldtheme"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(File.Exists(Path.Combine(_dir, "modern.saldtheme")));
        var bad = Path.Combine(_dir, "notes.json");
        File.WriteAllText(bad, "hello");
        w.ImportThemeFile(bad);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("notes.json couldn't be imported:", w.ThemeNoticeText!.Text, StringComparison.Ordinal);
        Assert.Equal(2, Settings.Current.Appearance.Themes.Count);
        w.Close();
    }

    [AvaloniaFact]
    public void ImportMenu_ListsYourVsCode_AndVsCodesOwnThemesAreShipped()
    {
        var (vs, _) = FakeVsCode("#102030");
        var w = Open();

        var menu = w.ThemeImportMenu!;
        var headers = menu.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Equal(["A Salandual theme file…", "A VS Code theme file…", "A colour file from before themes…", "From your VS Code", "Ocean"], headers);

        w.ImportFromVsCode(vs);
        Dispatcher.UIThread.RunJobs();
        var theme = Assert.Single(Settings.Current.Appearance.Themes);
        Assert.Equal(("Ocean", ThemeOrigin.Mine, false), (theme.Name, theme.Origin, theme.ReadOnly));
        Assert.Equal("#102030", theme.Colors[ColorTokens.PanelBackground]);

        foreach (var id in new[] { "code-dark-modern", "code-light-modern", "code-solarized-light", "code-dark-high-contrast", "code-abyss" })
        {
            Click(Card(w, id));
            Capture(w, $"themes-{id}.png");
        }

        Assert.Contains(w.ThemeCards, c => (string?)c.Tag == "code-dark-modern");
        Assert.Contains(w.ThemeCards, c => (string?)c.Tag == "code-solarized-light");
        w.Close();
    }
}
