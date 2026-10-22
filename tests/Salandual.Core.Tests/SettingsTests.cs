// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/SettingsTests.cs

using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-settings-").FullName;

    private string FilePath => Path.Combine(_dir, SettingsStore.FileName);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var result = SettingsStore.InDirectory(_dir).Load();

        Assert.Equal(SettingsLoadStatus.NotFound, result.Status);
        Assert.Equal(24, result.Settings.Layout.RowHeight);
        Assert.Equal(ScheduleMode.Windows, result.Settings.Appearance.Schedule.Mode);
        Assert.True(result.CanSave);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_AllSections()
    {
        var store = SettingsStore.InDirectory(_dir);
        var original = new AppSettings
        {
            Appearance = new AppearanceSettings
            {
                Schedule = new ThemeSchedule { Mode = ScheduleMode.None, Active = "mine" },
                Themes = [new ColorTheme { Id = "mine", Name = "Mine", Dark = true, Accent = ThemeAccent.Custom, CustomAccent = "#C239B3", Colors = new() { [ColorTokens.Text] = "#EEEEEE" }, Background = new BackgroundImageSettings { Path = "Balaton naplemente.jpg", Fit = BackgroundFit.Tile, Opacity = 0.3 } }],
                PanelFont = new FontSetting { Family = "Calibri", Size = 14 },
            },
            Layout = new LayoutSettings { RowHeight = 26, Strip = StripMode.Shared, Bars = new BarVisibility { Toolbar = false } },
            Panels = new PanelSettings { ShowHiddenFiles = true },
            Advanced = new AdvancedSettings { Rendering = RenderingMode.Software },
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        var s = loaded.Settings;
        Assert.Equal("mine", s.Appearance.Schedule.Active);
        Assert.Equal("#C239B3", s.Appearance.Themes[0].CustomAccent);
        Assert.Equal("#EEEEEE", s.Appearance.Themes[0].Colors[ColorTokens.Text]);
        Assert.Equal("Balaton naplemente.jpg", s.Appearance.Themes[0].Background.Path);
        Assert.Equal(BackgroundFit.Tile, s.Appearance.Themes[0].Background.Fit);
        Assert.Equal(new FontSetting { Family = "Calibri", Size = 14 }, s.Appearance.PanelFont);
        Assert.Equal(26, s.Layout.RowHeight);
        Assert.Equal(StripMode.Shared, s.Layout.Strip);
        Assert.False(s.Layout.Bars.Toolbar);
        Assert.True(s.Panels.ShowHiddenFiles);
        Assert.Equal(RenderingMode.Software, s.Advanced.Rendering);
    }

    [Fact]
    public void File_IsReadableJson_WithEnumNames()
    {
        SettingsStore.InDirectory(_dir).Save(new AppSettings { Appearance = AppearanceSettings.Fixed("nord") });
        var json = File.ReadAllText(FilePath);

        Assert.Contains("\"schemaVersion\": 8", json, StringComparison.Ordinal);
        Assert.Contains("\"mode\": \"None\"", json, StringComparison.Ordinal);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void FileWithByteOrderMark_Loads()
    {
        File.WriteAllText(FilePath, """{ "schemaVersion": 1, "layout": { "rowHeight": 28, "menuButton": "Hamburger" } }""", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = SettingsStore.InDirectory(_dir).Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(28, result.Settings.Layout.RowHeight);
        Assert.Equal(MenuButtonStyle.Hamburger, result.Settings.Layout.MenuButton);
    }

    [Fact]
    public void CorruptFile_IsKeptAside_AndDefaultsAreUsed()
    {
        File.WriteAllText(FilePath, "{ this is not json");

        var result = SettingsStore.InDirectory(_dir).Load();

        Assert.Equal(SettingsLoadStatus.Corrupt, result.Status);
        Assert.True(result.CanSave);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(result.Error);
        Assert.NotNull(result.BackupPath);

        Assert.True(File.Exists(result.BackupPath));
        Assert.False(File.Exists(FilePath));
        Assert.Equal(24, result.Settings.Layout.RowHeight);
    }

    [Fact]
    public void FileThatCantBeOpened_StartsWithDefaults_AndIsNeverSavedOver()
    {
        const string mine = """{ "schemaVersion": 7, "layout": { "rowHeight": 28 } }""";
        File.WriteAllText(FilePath, mine);
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            using var service = SettingsService.Open(_dir, TimeSpan.FromMilliseconds(1));
            Assert.Equal(SettingsLoadStatus.Unreadable, service.LoadResult.Status);
            Assert.IsAssignableFrom<IOException>(service.LoadResult.Error);
            Assert.False(service.LoadResult.CanSave);
            Assert.Equal(24, service.Current.Layout.RowHeight);

            service.Update(s => s with { Layout = s.Layout with { RowHeight = 22 } });
            service.Flush();
        }

        Assert.Equal(mine, File.ReadAllText(FilePath));
    }

    [Fact]
    public void BrokenFile_ThatCantBeMovedAside_IsLeftAlone_AndNeverSavedOver()
    {
        const string broken = "{ \"layout\": { \"rowHeight\": 28 ";
        File.WriteAllText(FilePath, broken);
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var service = SettingsService.Open(_dir, TimeSpan.FromMilliseconds(1));
            Assert.Equal(SettingsLoadStatus.Corrupt, service.LoadResult.Status);
            Assert.Null(service.LoadResult.BackupPath);
            Assert.IsAssignableFrom<System.Text.Json.JsonException>(service.LoadResult.Error);
            Assert.False(service.LoadResult.CanSave);

            service.Update(s => s with { Layout = s.Layout with { RowHeight = 22 } });
            service.Flush();
        }

        Assert.Equal(broken, File.ReadAllText(FilePath));
        Assert.Empty(Directory.GetFiles(_dir, "*.broken-*"));
    }

    [Fact]
    public void FailedSave_IsReportedOnce_UntilASaveWorksAgain()
    {
        Directory.CreateDirectory(FilePath);
        using var service = SettingsService.Open(_dir, TimeSpan.FromHours(1));
        var failed = 0;
        service.SaveFailed += (_, _) => failed++;

        service.Update(s => s with { Layout = s.Layout with { RowHeight = 22 } });
        service.Flush();
        service.Update(s => s with { Layout = s.Layout with { RowHeight = 26 } });
        service.Flush();
        Assert.Equal(1, failed);
        Assert.NotNull(service.LastSaveError);

        Directory.Delete(FilePath);
        service.Flush();
        Assert.Null(service.LastSaveError);
        Assert.Equal(26, SettingsStore.InDirectory(_dir).Load().Settings.Layout.RowHeight);
    }

    [Fact]
    public void NewerVersion_IsRead_ButNeverOverwritten()
    {
        const string newer = """{ "schemaVersion": 99, "layout": { "rowHeight": 28 }, "futureSection": { "x": 1 } }""";
        File.WriteAllText(FilePath, newer);

        using var service = SettingsService.Open(_dir, TimeSpan.FromMilliseconds(1));
        Assert.Equal(SettingsLoadStatus.NewerVersion, service.LoadResult.Status);
        Assert.Equal(28, service.Current.Layout.RowHeight);

        service.Update(s => s with { Layout = s.Layout with { RowHeight = 22 } });
        service.Flush();

        Assert.Equal(newer, File.ReadAllText(FilePath));
    }

    [Fact]
    public void OlderFile_Loads_AndIsWrittenBackAtTheCurrentVersion()
    {
        File.WriteAllText(FilePath, """
            { "schemaVersion": 1,
              "layout": { "rowHeight": 28 } }
            """);

        using (var service = SettingsService.Open(_dir, TimeSpan.FromHours(1)))
        {
            Assert.Equal(SettingsLoadStatus.Loaded, service.LoadResult.Status);
            Assert.Equal(1, service.LoadResult.MigratedFrom);
            var s = service.Current;
            Assert.Equal(28, s.Layout.RowHeight);
        }

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"schemaVersion\": 8", json, StringComparison.Ordinal);
        Assert.Null(SettingsStore.InDirectory(_dir).Load().MigratedFrom);
    }

    private static readonly string[] TreeRows = ["*.json;*.jsonc;*.jsonl;*.geojson", "*.yaml;*.yml", "*.xml;*.xaml;*.csproj;*.slnx;*.props;*.config;*.resx"];

    [Fact]
    public void Schema5_AddsTheTreeRows_AboveTheCatchAll_KeepingARowYouMadeForOneOfThem()
    {
        File.WriteAllText(FilePath, """
            { "schemaVersion": 4,
              "viewers": { "masks": [
                { "mask": "*.ipynb", "view": "Notebook", "alternate": "Text" },
                { "mask": "*.YML;*.log", "view": "Text", "alternate": "Hex" },
                { "mask": "*.*", "view": "Automatic", "alternate": "Hex" } ] } }
            """);

        var viewers = SettingsStore.InDirectory(_dir).Load().Settings.Viewers;

        Assert.Equal(["*.ipynb", "*.YML;*.log", TreeRows[0], TreeRows[2], HtmlRow, "*.*"], viewers.Masks.Select(m => m.Mask));
        Assert.Equal(ViewerKind.Tree, viewers.KindFor("settings.json", alternate: false));
        Assert.Equal(ViewerKind.Text, viewers.KindFor("settings.json", alternate: true));
        Assert.Equal(ViewerKind.Text, viewers.KindFor("compose.yml", alternate: false));
        Assert.Equal(ViewerKind.Tree, viewers.KindFor("App.csproj", alternate: false));
    }

    [Theory]
    [InlineData(13, 12)]
    [InlineData(14, 14)]
    [InlineData(12, 12)]
    public void Schema8_ThePanelFontsOldDefault13_Becomes12(double before, double after)
    {
        File.WriteAllText(FilePath, $$"""{ "schemaVersion": 7, "appearance": { "panelFont": { "family": "Calibri", "size": {{before}} }, "monoFont": { "size": 13 } } }""");
        var s = SettingsStore.InDirectory(_dir).Load().Settings;
        Assert.Equal(after, s.Appearance.PanelFont.Size);
        Assert.Equal("Calibri", s.Appearance.PanelFont.Family);
        Assert.Equal(13, s.Appearance.MonoFont.Size);
        Assert.Equal(12, new AppSettings().Appearance.PanelFont.Size);
    }

    [Fact]
    public void Schema7_AddsSlnx_NextToCsproj_InTheViewersAndTheEditorsTables()

    {
        File.WriteAllText(FilePath, """
            { "schemaVersion": 6,
              "viewers": { "masks": [
                { "mask": "*.xml;*.CSPROJ;*.props", "view": "Tree", "alternate": "Text" },
                { "mask": "*.*", "view": "Automatic", "alternate": "Hex" } ] },
              "editors": { "masks": [
                { "mask": "*.slnx;*.txt", "colors": "None" },
                { "mask": "*.xml;*.csproj", "colors": "Xml" },
                { "mask": "*.*" } ] } }
            """);

        var s = SettingsStore.InDirectory(_dir).Load().Settings;
        Assert.Equal(["*.xml;*.CSPROJ;*.slnx;*.props", "*.*"], s.Viewers.Masks.Select(m => m.Mask));
        Assert.Equal(ViewerKind.Tree, s.Viewers.KindFor("Salandual.slnx", alternate: false));
        Assert.Equal(["*.slnx;*.txt", "*.xml;*.csproj", "*.*"], s.Editors.Masks.Select(m => m.Mask));
    }

    private const string HtmlRow = "*.html;*.htm;*.xhtml;*.mht;*.mhtml";

    [Fact]
    public void Schema6_AddsTheHtmlRow_AboveTheCatchAll_UnlessARowAlreadyNamesHtml()
    {
        File.WriteAllText(FilePath, """
            { "schemaVersion": 5,
              "viewers": { "masks": [
                { "mask": "*.json", "view": "Tree", "alternate": "Text" },
                { "mask": "*.*", "view": "Automatic", "alternate": "Hex" } ] } }
            """);

        var viewers = SettingsStore.InDirectory(_dir).Load().Settings.Viewers;
        Assert.Equal(["*.json", HtmlRow, "*.*"], viewers.Masks.Select(m => m.Mask));
        Assert.Equal(ViewerKind.Html, viewers.KindFor("Report.HTM", alternate: false));
        Assert.Equal(ViewerKind.Text, viewers.KindFor("index.html", alternate: true));

        File.WriteAllText(FilePath, """
            { "schemaVersion": 5, "viewers": { "masks": [ { "mask": "*.htm", "view": "Text", "alternate": "Hex" }, { "mask": "*.*" } ] } }
            """);
        Assert.Equal(["*.htm", "*.*"], SettingsStore.InDirectory(_dir).Load().Settings.Viewers.Masks.Select(m => m.Mask));
    }

    [Fact]
    public void HtmlPages_OpenUpTo5MB_WithScriptsAndWebContentOn()
    {
        var v = new ViewerSettings();
        Assert.Equal(ViewerKind.Html, v.KindFor("page.mht", alternate: false));
        Assert.Equal(5L * 1024 * 1024, v.LargestBytes("index.html", ViewerKind.Html));
        Assert.True(v.HtmlScripts);
        Assert.True(v.HtmlWebContent);
    }

    [Fact]
    public void TheTree_OpensJsonAndXmlUpTo50MB_AndYamlUpTo20MB()
    {
        var v = new ViewerSettings();
        Assert.Equal(ViewerKind.Tree, v.KindFor("a.yaml", alternate: false));
        Assert.Equal(50L * 1024 * 1024, v.LargestBytes("data.json", ViewerKind.Tree));
        Assert.Equal(50L * 1024 * 1024, v.LargestBytes("App.config", ViewerKind.Tree));
        Assert.Equal(20L * 1024 * 1024, v.LargestBytes("compose.yaml", ViewerKind.Tree));
        Assert.Equal(20, ViewerSettings.DefaultLargestMB(ViewerKind.Tree, "*.yaml;*.yml"));
        Assert.Equal(TreeOpening.FirstLevel, v.TreeOpens);
        Assert.Equal(Salandual.Core.Viewer.DataPathStyle.Dollar, v.TreePath);
    }

    [Fact]
    public void Schema3_AddsTheNotebookRow_AboveTheCatchAll_UnlessATableAlreadyHasOne()
    {
        File.WriteAllText(FilePath, """
            { "schemaVersion": 2,
              "viewers": { "masks": [
                { "mask": "*.md;*.markdown", "view": "Markdown", "alternate": "Text" },
                { "mask": "*.log", "view": "Text", "alternate": "Hex" },
                { "mask": "*.*", "view": "Automatic", "alternate": "Hex" } ] } }
            """);

        var masks = SettingsStore.InDirectory(_dir).Load().Settings.Viewers.Masks;

        Assert.Equal(["*.md;*.markdown", "*.log", "*.ipynb", .. TreeRows, HtmlRow, "*.*"], masks.Select(m => m.Mask));
        Assert.Equal(ViewerKind.Notebook, masks[2].View);
        Assert.Equal(ViewerKind.Text, masks[2].Alternate);

        File.WriteAllText(FilePath, """
            { "schemaVersion": 2, "viewers": { "masks": [ { "mask": "*.IPYNB", "view": "Text", "alternate": "Hex" }, { "mask": "*.*" } ] } }
            """);
        masks = SettingsStore.InDirectory(_dir).Load().Settings.Viewers.Masks;
        Assert.Equal(["*.IPYNB", .. TreeRows, HtmlRow, "*.*"], masks.Select(m => m.Mask));
        Assert.Equal(ViewerKind.Text, masks[0].View);
        Assert.Equal(ViewerKind.Notebook, new ViewerSettings().KindFor("analysis.ipynb", alternate: false));
    }

    [Fact]
    public void Comments_AndTrailingCommas_AreAccepted()
    {
        File.WriteAllText(FilePath, """
            {
              // edited by hand
              "layout": { "rowHeight": 22, },
            }
            """);

        var result = SettingsStore.InDirectory(_dir).Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(22, result.Settings.Layout.RowHeight);
    }

    [Fact]
    public void PartialFile_KeepsDefaults_ForEverythingMissing()
    {
        File.WriteAllText(FilePath, """{ "layout": { "rowHeight": 28 }, "appearance": null }""");

        var s = SettingsStore.InDirectory(_dir).Load().Settings;

        Assert.Equal(28, s.Layout.RowHeight);
        Assert.True(s.Layout.Bars.FunctionKeyBar);
        Assert.True(s.Panels.NumericSort);
        Assert.Equal(12, s.Appearance.PanelFont.Size);
        Assert.Equal(13, s.Appearance.MonoFont.Size);
        Assert.Equal("Cascadia Mono", s.Appearance.MonoFont.Family);
        Assert.Equal(PanelSettings.DefaultInformationLine, s.Panels.InformationLine);
    }

    [Fact]
    public void Normalize_ClampsAndDropsInvalidValues()
    {
        var s = new AppSettings
        {
            Appearance = new AppearanceSettings
            {
                Themes =
                [
                    new ColorTheme { Id = "blank", Background = new BackgroundImageSettings { Opacity = 5, Path = "  " } },
                    new ColorTheme { Id = "outside", Background = new BackgroundImageSettings { Path = @"..\..\elsewhere\photo.jpg" } },
                ],
                PanelFont = new FontSetting { Family = "  ", Size = 99 },
            },
            Layout = new LayoutSettings { RowHeight = 99 },
        }.Normalize();

        Assert.Equal(BackgroundImageSettings.MaxOpacity, s.Appearance.Themes[0].Background.Opacity);
        Assert.Null(s.Appearance.Themes[0].Background.Path);
        Assert.Equal("photo.jpg", s.Appearance.Themes[1].Background.Path);
        Assert.Null(s.Appearance.PanelFont.Family);
        Assert.Equal(FontSetting.MaxSize, s.Appearance.PanelFont.Size);
        Assert.Equal(LayoutSettings.MaxRowHeight, s.Layout.RowHeight);
    }

    [Fact]
    public void Appearance_Equivalence_ComparesFontsAndLayoutStaysOut()
    {
        var a = new AppearanceSettings().Normalize();
        Assert.True(a.IsEquivalentTo(new AppearanceSettings().Normalize()));
        Assert.False(a.IsEquivalentTo(a with { PanelFont = new FontSetting { Family = "Calibri" } }));

        var s1 = new AppSettings().Normalize();
        var s2 = (s1 with { Layout = s1.Layout with { RowHeight = 28 } }).Normalize();
        Assert.True(s1.Appearance.IsEquivalentTo(s2.Appearance));
    }

    [Fact]
    public void Service_AppliesImmediately_AndSavesAfterThePause()
    {
        using var service = SettingsService.Open(_dir, TimeSpan.FromMinutes(5));
        SettingsChangedEventArgs? seen = null;
        service.Changed += (_, e) => seen = e;

        service.Update(s => s with { Appearance = AppearanceSettings.Fixed("nord") });

        Assert.Equal("nord", service.Current.Appearance.Schedule.Active);
        Assert.Equal("nord", seen?.New.Appearance.Schedule.Active);
        Assert.False(File.Exists(FilePath));

        service.Flush();
        Assert.Equal("nord", SettingsStore.InDirectory(_dir).Load().Settings.Appearance.Schedule.Active);
    }

    [Fact]
    public void Schema3_DropsTheOldColours_KeepsTheFonts()
    {
        File.WriteAllText(FilePath, """
            { "schemaVersion": 3,
              "appearance": { "theme": "Dark", "accentMode": "Custom", "customAccent": "#C239B3", "colorScheme": "Custom",
                              "darkColorOverrides": { "Text": "#EEEEEE" }, "codeScheme": "dracula", "panelFont": { "family": "Calibri", "size": 14 } } }
            """);
        var loaded = SettingsStore.InDirectory(_dir).Load();
        Assert.Equal(3, loaded.MigratedFrom);
        Assert.Equal(ScheduleMode.Windows, loaded.Settings.Appearance.Schedule.Mode);
        Assert.Empty(loaded.Settings.Appearance.Themes);
        Assert.Equal("Calibri", loaded.Settings.Appearance.PanelFont.Family);
    }

    [Theory]
    [InlineData(null, RenderingMode.Gpu)]
    [InlineData("""{ "advanced": { "rendering": "Software" } }""", RenderingMode.Software)]
    [InlineData("""{ "advanced": { "rendering": "software", }, }""", RenderingMode.Software)]
    [InlineData("""{ /* c */ "layout": { "rendering": "Software" }, "advanced": { "x": [1, {"rendering": "Software"}], "rendering": "Gpu" } }""", RenderingMode.Gpu)]
    [InlineData("""{ "panels": { "advanced": { "rendering": "Software" } } }""", RenderingMode.Gpu)]
    [InlineData("""{ "advanced": { "rendering": "Banana" } }""", RenderingMode.Gpu)]
    [InlineData("""{ "advanced": { "rendering": 1 } }""", RenderingMode.Gpu)]
    [InlineData("not json {", RenderingMode.Gpu)]
    public void PeekRendering_ReadsOnlyThatValue(string? json, RenderingMode expected)
    {
        if (json is not null)
        {
            File.WriteAllText(FilePath, json);
        }

        Assert.Equal(expected, SettingsStore.InDirectory(_dir).PeekRendering());
    }

    [Fact]
    public void PeekRendering_MatchesFullLoad_ForSavedFile()
    {
        var store = SettingsStore.InDirectory(_dir);
        store.Save(new AppSettings { Advanced = new AdvancedSettings { Rendering = RenderingMode.Software } });
        Assert.Equal(store.Load().Settings.Advanced.Rendering, store.PeekRendering());
    }

    [Fact]
    public void Peek_ReadsTheFoldersAtStart_AsTheFullLoadDoes()
    {
        var store = SettingsStore.InDirectory(_dir);
        Assert.Equal(SettingsPeek.Defaults, store.Peek());
        store.Save(new AppSettings { General = new GeneralSettings { StartFolders = StartFolders.These, StartLeft = @"D:\Projects ""new""", StartRight = @"\\server\share\é" } });
        var general = store.Load().Settings.General;
        Assert.Equal(new SettingsPeek(RenderingMode.Gpu, StartFolders.These, general.StartLeft, general.StartRight), store.Peek());

        File.WriteAllText(FilePath, """{ "general": { "startFolders": "Elsewhere", "startLeft": "C:\\A\u00e9" } }""");
        Assert.Equal(new SettingsPeek(RenderingMode.Gpu, StartFolders.WhereYouLeft, @"C:\Aé", ""), store.Peek());
    }

    [Theory]
    [InlineData(StartFolders.WhereYouLeft, null, null, @"C:\", @"C:\Users\you")]
    [InlineData(StartFolders.WhereYouLeft, @"D:\Last", @"E:\Last", @"D:\Last", @"E:\Last")]
    [InlineData(StartFolders.Home, @"D:\Last", @"E:\Last", @"C:\Users\you", @"C:\Users\you")]
    [InlineData(StartFolders.These, @"D:\Last", @"E:\Last", @"F:\Mine", @"E:\Last")]
    public void FoldersAtStart(StartFolders choice, string? lastLeft, string? lastRight, string left, string right)
        => Assert.Equal((left, right), GeneralSettings.FoldersAtStart(choice, @" F:\Mine ", "", lastLeft, lastRight, (@"C:\", @"C:\Users\you"), @"C:\Users\you"));

    [Fact]
    public void StartWithWindows_IsOnOnlyForThisProgram()
    {
        var start = new Salandual.Core.MemoryStartWithWindows();
        const string here = @"D:\Portable\Salandual\salandual.exe";
        Assert.False(Salandual.Core.StartWithWindowsState.IsOn(start, here));
        Assert.Null(Salandual.Core.StartWithWindowsState.Other(start, here));
        start.Register(@"d:\portable\salandual\SALANDUAL.EXE");
        Assert.True(Salandual.Core.StartWithWindowsState.IsOn(start, here));
        start.Register(@"E:\Moved from\salandual.exe");
        Assert.False(Salandual.Core.StartWithWindowsState.IsOn(start, here));
        Assert.Equal(@"E:\Moved from\salandual.exe", Salandual.Core.StartWithWindowsState.Other(start, here));
    }
}
