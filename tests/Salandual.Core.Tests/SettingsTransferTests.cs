// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/SettingsTransferTests.cs

using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class SettingsTransferTests : IDisposable
{
    private static readonly DateTime _now = new(2026, 10, 19, 19, 30, 12);

    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-transfer-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static AppSettings Mine() => new()
    {
        Appearance = new AppearanceSettings { PanelFont = new FontSetting { Size = 14 }, Themes = [new ColorTheme { Id = "mine-1", Name = "Night owl", Dark = true }] },
        Panels = new PanelSettings { SizeUnits = SizeUnits.Bytes, TimeFormat = TimeFormat.Hours12 },
        HotPaths = new HotPathSettings { Paths = [new HotPath(1, "Photos", @"E:\Photos")] },
        History = new HistorySettings { CopyTargets = [@"D:\Backup"] },
        Find = new FindSettings { Texts = ["secret"], WindowWidth = 1234 },
        Compare = new CompareSettings { Files = new FileCompareSettings { UseProgram = true, Program = @"C:\Tools\WinMerge\WinMergeU.exe" } },
        Layout = new LayoutSettings { PanelSplit = 0.3 },
    };

    private string Export(AppSettings settings, ExportChoice choice)
    {
        var file = Path.Combine(_dir, SettingsTransfer.SuggestedName(_now));
        File.WriteAllText(file, SettingsTransfer.Export(settings, choice, _now, "Salandual 0.1"));
        return file;
    }

    [Fact]
    public void Export_NamesTheFile_AndLeavesOutTypingHistoryAndWindowSizes_ByDefault()
    {
        Assert.Equal("Salandual settings 2026-10-19.saldset", SettingsTransfer.SuggestedName(_now));
        var text = File.ReadAllText(Export(Mine(), new ExportChoice()));
        Assert.Contains("\"format\": \"salandual-settings\"", text);
        Assert.Contains("Night owl", text);
        Assert.Contains("Photos", text);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("Backup", text);
        Assert.DoesNotContain("1234", text);
        Assert.DoesNotContain("panelSplit", text);

        var without = File.ReadAllText(Export(Mine(), new ExportChoice(Themes: false, HotPaths: false, History: true)));
        Assert.DoesNotContain("Night owl", without);
        Assert.DoesNotContain("Photos", without);
        Assert.Contains("secret", without);
    }

    [Fact]
    public void Import_ShowsWhatChanges_PartByPart_AndTakesOnlyTheTickedParts()
    {
        var file = Export(Mine(), new ExportChoice(History: true));
        var import = SettingsTransfer.Read(file, new AppSettings());

        Assert.Equal(new DateTime(2026, 10, 19, 19, 30, 12), import.Exported);
        Assert.Equal("Salandual 0.1", import.ExportedBy);
        Assert.Equal([SettingsPart.Appearance, SettingsPart.Themes, SettingsPart.Panels, SettingsPart.HotPaths, SettingsPart.History, SettingsPart.Programs], import.Parts);
        Assert.Contains(new SettingChange(SettingsPart.Appearance, "Panel font size", "12", "14"), import.Changes);
        Assert.Contains(new SettingChange(SettingsPart.Panels, "Time format", "Windows", "Hours 12"), import.Changes);
        Assert.Contains(new SettingChange(SettingsPart.Themes, "Night owl", "—", "added"), import.Changes);
        Assert.Contains(new SettingChange(SettingsPart.Programs, "The other compare program", "—", @"C:\Tools\WinMerge\WinMergeU.exe"), import.Changes);

        var some = import.Apply(new HashSet<SettingsPart> { SettingsPart.Appearance, SettingsPart.Panels });
        Assert.Equal(14, some.Appearance.PanelFont.Size);
        Assert.Equal(TimeFormat.Hours12, some.Panels.TimeFormat);
        Assert.Empty(some.Appearance.Themes);
        Assert.Empty(some.HotPaths.Paths);
        Assert.Equal("", some.Compare.Files.Program);
        Assert.False(some.Compare.Files.UseProgram);
        Assert.Equal(new FindSettings().WindowWidth, some.Find.WindowWidth);

        var all = import.Apply(import.Parts.ToHashSet());
        Assert.Equal("Night owl", Assert.Single(all.Appearance.Themes).Name);
        Assert.Equal(@"C:\Tools\WinMerge\WinMergeU.exe", all.Compare.Files.Program);
        Assert.Equal(["secret"], all.Find.Texts);
    }

    [Fact]
    public void Import_AFileWithoutAPart_LeavesThatPartAlone()
    {
        var file = Export(new AppSettings { Panels = new PanelSettings { SizeUnits = SizeUnits.Decimal } }, new ExportChoice(Themes: false, HotPaths: false));
        var current = Mine();
        var import = SettingsTransfer.Read(file, current);

        Assert.DoesNotContain(SettingsPart.Themes, import.Parts);
        Assert.DoesNotContain(SettingsPart.HotPaths, import.Parts);
        Assert.DoesNotContain(SettingsPart.History, import.Parts);
        var after = import.Apply(import.Parts.Where(p => p != SettingsPart.Programs).ToHashSet());
        Assert.Contains(SettingsPart.Layout, import.Same);
        Assert.Equal(SizeUnits.Decimal, after.Panels.SizeUnits);
        Assert.Equal("Night owl", Assert.Single(after.Appearance.Themes).Name);
        Assert.Equal("Photos", Assert.Single(after.HotPaths.Paths).Name);
        Assert.Equal(["secret"], after.Find.Texts);
        Assert.Equal(@"C:\Tools\WinMerge\WinMergeU.exe", after.Compare.Files.Program);
    }

    [Fact]
    public void Import_AnEditorProgram_IsAProgram_NotPartOfTheEditorsTable()
    {
        var theirs = new AppSettings { Editors = new EditorSettings { Masks = [new EditorMask { Mask = "*.md", Program = "code.exe \"$(FullName)\"" }, new EditorMask()] } };
        var import = SettingsTransfer.Read(Export(theirs, new ExportChoice()), new AppSettings());

        Assert.Contains(new SettingChange(SettingsPart.Programs, "Editor for *.md", "—", "code.exe \"$(FullName)\""), import.Changes);
        var table = import.Apply(new HashSet<SettingsPart> { SettingsPart.Editors });
        Assert.Equal(["*.md", "*.*"], table.Editors.Masks.Select(m => m.Mask));
        Assert.All(table.Editors.Masks, m => Assert.Null(m.Program));
    }

    [Fact]
    public void Import_ASettingsJsonFromBefore_IsUpgradedLikeOne()
    {
        var file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, """{ "schemaVersion": 7, "appearance": { "panelFont": { "size": 13 } }, "layout": { "rowHeight": 28 } }""");
        var import = SettingsTransfer.Read(file, new AppSettings());

        Assert.Null(import.Exported);
        Assert.Equal([new SettingChange(SettingsPart.Layout, "Row height", "24", "28")], import.Changes);
    }

    [Theory]
    [InlineData("""{ "hello": 1 }""", "The file isn't a Salandual settings file.")]
    [InlineData("""{ "format": "salandual-theme" }""", "The file isn't a Salandual settings file.")]
    [InlineData("{\n \"format\": \"salandual-settings\",\n \"layout\": { \"rowHeight\": 28 \n", "The file isn't valid (a mistake on line 4).")]
    [InlineData("[1, 2]", "The file isn't a Salandual settings file.")]
    public void Import_NotASettingsFile_SaysWhy(string text, string message)
    {
        var file = Path.Combine(_dir, "x.saldset");
        File.WriteAllText(file, text);
        Assert.Equal(message, Assert.Throws<InvalidDataException>(() => SettingsTransfer.Read(file, new AppSettings())).Message);
    }

    [Fact]
    public void Import_TooBig_IsRefused()
    {
        var file = Path.Combine(_dir, "big.saldset");
        using (var s = File.Create(file))
        {
            s.SetLength(SettingsTransfer.MaxFileSize + 1);
        }

        Assert.Equal("The file is too big to be a settings file.", Assert.Throws<InvalidDataException>(() => SettingsTransfer.Read(file, new AppSettings())).Message);
    }

    [Fact]
    public void ResetAll_KeepsWhatIsTicked_AndTheWindowSizes()
    {
        var kept = SettingsTransfer.ResetAll(Mine(), new ResetChoice());
        Assert.Equal(FontSetting.DefaultPanelSize, kept.Appearance.PanelFont.Size);
        Assert.Equal(SizeUnits.Windows, kept.Panels.SizeUnits);
        Assert.Equal("", kept.Compare.Files.Program);
        Assert.Equal("Night owl", Assert.Single(kept.Appearance.Themes).Name);
        Assert.Equal("Photos", Assert.Single(kept.HotPaths.Paths).Name);
        Assert.Equal(["secret"], kept.Find.Texts);
        Assert.Equal(1234, kept.Find.WindowWidth);
        Assert.Equal(0.3, kept.Layout.PanelSplit);

        var none = SettingsTransfer.ResetAll(Mine(), new ResetChoice(KeepThemes: false, KeepHotPaths: false, KeepHistory: false));
        Assert.Empty(none.Appearance.Themes);
        Assert.Empty(none.HotPaths.Paths);
        Assert.Empty(none.Find.Texts);
        Assert.Empty(none.History.CopyTargets);
    }

    [Fact]
    public void KeepCopy_WritesTheSettingsBesideSettingsJson()
    {
        var path = SettingsTransfer.KeepCopy(Mine(), _dir, "import", _now);
        Assert.Equal(Path.Combine(_dir, "settings.before-import-20261019-193012.json"), path);
        Assert.Equal(14, new SettingsStore(path).Load().Settings.Appearance.PanelFont.Size);
    }
}
