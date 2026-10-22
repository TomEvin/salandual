// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/SettingsFilesTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.Core.Commands;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class SettingsFilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-settings-files-").FullName;

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        var shared = AppServices.CreateIsolated().DataDirectory;
        foreach (var kept in Directory.Exists(shared) ? Directory.GetFiles(shared, "settings.before-*.json") : [])
        {
            File.Delete(kept);
        }
    }

    private static AppSettings Mine() => new()
    {
        Appearance = new AppearanceSettings { PanelFont = new FontSetting { Size = 14 }, Themes = [new ColorTheme { Id = "mine-1", Name = "Night owl", Dark = true }] },
        Panels = new PanelSettings { TimeFormat = TimeFormat.Hours12 },
        HotPaths = new HotPathSettings { Paths = [new HotPath(1, "Photos", @"E:\Photos")] },
        Compare = new CompareSettings { Files = new FileCompareSettings { UseProgram = true, Program = @"C:\Tools\WinMerge\WinMergeU.exe" } },
    };

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Advanced_HasYourSettings_AndSearchFindsThem()
    {
        var w = new OptionsWindow(AppServices.CreateIsolated());
        w.Show();
        w.ShowCategory("advanced");
        var titles = w.Items.Where(i => i.Category == "advanced" && i.Section == "Your settings").Select(i => i.Title);
        Assert.Equal(["Export settings", "Import settings", "Reset all settings"], titles);
        Assert.Contains(w.Find("backup"), i => i.Title == "Export settings");
        Assert.Contains(w.Find("time 12-hour"), i => i.Title == "Time");
        w.Close();
    }

    [Fact]
    public void TheOptionsMenu_HasThem_WithoutKeys()
    {
        var options = MainMenu.Create().First(n => n.Children?.Any(c => c.CommandId == "App.Options") == true);
        var ids = MainMenu.Flatten(options.Children!).Select(n => n.CommandId).OfType<string>().ToList();
        Assert.Equal(["App.Options", "Options.ExportSettings", "Options.ImportSettings", "Options.ResetAllSettings"], ids.Take(4));
        var commands = CommandRegistry.CreateDefault();
        foreach (var id in ids.Skip(1).Take(3))
        {
            Assert.Empty(commands.ChordsFor(id));
        }

        Assert.Equal("Export settings…", commands["Options.ExportSettings"].Title);
    }

    [AvaloniaFact]
    public void Export_StartsWithTypingHistoryOff_AndSaysWhereItSaved()
    {
        var services = AppServices.CreateIsolated();
        services.Settings.Update(_ => Mine());
        var dialog = SettingsTransferDialog.ForExport(services.Settings.Current);
        Assert.Equal([(SettingsPart.Themes, true), (SettingsPart.HotPaths, true), (SettingsPart.History, false)], dialog.Ticks.Select(t => ((SettingsPart)t.Value, t.Box.IsChecked == true)));
        Assert.Equal("Save as…", dialog.Primary.Content);

        string? said = null;
        var file = Path.Combine(_dir, "mine.saldset");
        new SettingsFiles(services, dialog, (text, _) => said = text).ExportTo(file, new ExportChoice());
        Assert.Equal("Saved your settings to mine.saldset.", said);
        Assert.Contains("Night owl", File.ReadAllText(file));
    }

    [AvaloniaFact]
    public void Import_ShowsEachPart_ProgramsUnticked_ThenUndo()
    {
        var file = Path.Combine(_dir, "from-the-other-pc.saldset");
        File.WriteAllText(file, SettingsTransfer.Export(Mine(), new ExportChoice(), new DateTime(2026, 10, 12, 9, 0, 0), "Salandual 0.1"));
        var services = AppServices.CreateIsolated();
        (string Text, Action? Undo) said = default;
        var files = new SettingsFiles(services, new Window(), (text, undo) => said = (text, undo));

        var import = files.Read(file)!;
        var dialog = SettingsTransferDialog.ForImport(import);
        var ticks = dialog.Ticks.ToDictionary(t => (SettingsPart)t.Value, t => t.Box);
        Assert.Equal([SettingsPart.Appearance, SettingsPart.Themes, SettingsPart.Panels, SettingsPart.HotPaths, SettingsPart.Programs], ticks.Keys);
        Assert.False(ticks[SettingsPart.Programs].IsChecked);
        Assert.True(ticks[SettingsPart.Appearance].IsChecked);
        var all = import.Changes.Count(c => c.Part != SettingsPart.Programs);
        Assert.Equal($"Import {all} changes", dialog.Primary.Content);
        ticks[SettingsPart.Themes].IsChecked = false;
        Assert.Equal($"Import {all - 1} changes", dialog.Primary.Content);

        files.Import(import, new HashSet<SettingsPart> { SettingsPart.Appearance, SettingsPart.Panels });
        Assert.Equal(14, services.Settings.Current.Appearance.PanelFont.Size);
        Assert.Equal(TimeFormat.Hours12, services.Settings.Current.Panels.TimeFormat);
        Assert.Equal("", services.Settings.Current.Compare.Files.Program);
        Assert.Equal("Imported 2 changes from from-the-other-pc.saldset.", said.Text);
        Assert.NotEmpty(Directory.GetFiles(services.DataDirectory, "settings.before-import-*.json"));

        said.Undo!();
        Assert.Equal(FontSetting.DefaultPanelSize, services.Settings.Current.Appearance.PanelFont.Size);
        Assert.Equal("Your settings are back as they were.", said.Text);
    }

    [AvaloniaFact]
    public void Import_NotASettingsFile_OrTheSameAsYours_IsSaid()
    {
        var services = AppServices.CreateIsolated();
        string? said = null;
        var files = new SettingsFiles(services, new Window(), (text, _) => said = text);

        var notOne = Path.Combine(_dir, "notes.saldset");
        File.WriteAllText(notOne, "{ \"hello\": 1 }");
        Assert.Null(files.Read(notOne));
        Assert.Equal("Couldn't import notes.saldset: The file isn't a Salandual settings file.", said);

        var same = Path.Combine(_dir, "same.saldset");
        File.WriteAllText(same, SettingsTransfer.Export(new AppSettings(), new ExportChoice(), DateTime.Now, "Salandual 0.1"));
        Assert.Null(files.Read(same));
        Assert.Equal("same.saldset has the same settings as yours: nothing to import.", said);
    }

    [AvaloniaFact]
    public void ResetAll_KeepsTheTicked_AndUndoBringsAllBack()
    {
        var services = AppServices.CreateIsolated();
        services.Settings.Update(_ => Mine());
        var dialog = SettingsTransferDialog.ForResetAll(services.Settings.Current);
        Assert.All(dialog.Ticks, t => Assert.True(t.Box.IsChecked));
        Assert.Equal("Reset all", dialog.Primary.Content);

        (string Text, Action? Undo) said = default;
        new SettingsFiles(services, dialog, (text, undo) => said = (text, undo)).ResetAll(new ResetChoice());
        var now = services.Settings.Current;
        Assert.Equal(FontSetting.DefaultPanelSize, now.Appearance.PanelFont.Size);
        Assert.Equal("Night owl", Assert.Single(now.Appearance.Themes).Name);
        Assert.Equal("Photos", Assert.Single(now.HotPaths.Paths).Name);
        Assert.Equal("All settings are back to their defaults; what you ticked stayed.", said.Text);
        Assert.NotEmpty(Directory.GetFiles(services.DataDirectory, "settings.before-reset-*.json"));

        said.Undo!();
        Assert.Equal(14, services.Settings.Current.Appearance.PanelFont.Size);
    }

    [AvaloniaFact]
    public void FromOptions_WhatHappenedIsSaidOnTheAdvancedPage_WithUndo()
    {
        var services = AppServices.CreateIsolated();
        services.Settings.Update(_ => Mine());
        var w = new OptionsWindow(services);
        w.Show();
        w.SettingsFiles.ResetAll(new ResetChoice());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("advanced", w.CurrentCategory);
        Assert.Equal("All settings are back to their defaults; what you ticked stayed.", w.SettingsNoticeText!.Text);

        Click(w.SettingsNoticeUndo!);
        Assert.Equal(14, services.Settings.Current.Appearance.PanelFont.Size);
        Assert.Equal("Your settings are back as they were.", w.SettingsNoticeText!.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void Pictures()
    {
        var services = AppServices.CreateIsolated();
        services.Settings.Update(_ => Mine());
        var w = new OptionsWindow(services) { Width = 1000, Height = 740 };
        w.Show();
        w.ShowCategory("panels");
        var time = w.Items.First(i => i.Title == "Time");
        w.ShowCategory("panels", time);
        Dispatcher.UIThread.RunJobs();
        Capture(w, "xfer-time.png");
        w.SettingsFiles.ResetAll(new ResetChoice());
        Dispatcher.UIThread.RunJobs();
        Capture(w, "xfer-advanced.png");
        w.Close();

        var file = Path.Combine(_dir, "Salandual settings 2026-10-12.saldset");
        File.WriteAllText(file, SettingsTransfer.Export(Mine(), new ExportChoice(), new DateTime(2026, 10, 12, 9, 0, 0), "Salandual 0.1"));
        foreach (var (dialog, name) in new[]
        {
            (SettingsTransferDialog.ForExport(Mine()), "xfer-export.png"),
            (SettingsTransferDialog.ForImport(SettingsTransfer.Read(file, new AppSettings())), "xfer-import.png"),
            (SettingsTransferDialog.ForResetAll(Mine()), "xfer-reset.png"),
        })
        {
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            Capture(dialog, name);
            dialog.Close();
        }
    }

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
}
