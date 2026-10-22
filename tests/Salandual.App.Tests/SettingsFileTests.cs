// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/SettingsFileTests.cs

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class SettingsFileTests
{
    private const string File = @"C:\Users\me\AppData\Roaming\Salandual\settings.json";

    private static MainWindow Started(SettingsLoadResult loaded)
    {
        var w = new MainWindow(AppServices.CreateIsolated(loaded));
        w.Show();
        Directory.CreateDirectory(Path.GetDirectoryName(w.Services.Settings.FilePath)!);
        w.ShowSettingsFileProblem();
        return w;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task FileThatCantBeOpened_SaidAtStart_ShowTheFolderTakesThePanelThere()
    {
        var inUse = new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020));
        var w = Started(new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Unreadable, Error: inUse));
        var path = w.Services.Settings.FilePath;
        Assert.Equal($"Salandual's settings file {path} couldn't be opened (another program has it open), so Salandual started with its defaults and won't save over the file until it starts again.", w.NoticeMessage);
        Assert.Equal(["Show the folder", "OK"], w.NoticeButtons.Select(b => (string?)b.Content));

        w.NoticeButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => string.Equals(w.ActivePanel.Panel.Path, Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase));
        w.CloseNow();
    }

    [AvaloniaFact]
    public void NothingWrong_NothingSaid()
    {
        var w = Started(new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Loaded));
        Assert.Null(w.NoticeMessage);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheTemporaryFolderQuestion_WaitsUntilTheSettingsFileNoticeIsAnswered()
    {
        var w = Started(new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.NewerVersion));
        var said = w.NoticeMessage;
        Assert.StartsWith("Salandual's settings file ", said);
        var folder = Path.Combine(w.Services.Temp.Root, "run-9-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllBytes(Path.Combine(folder, "page-1.html"), new byte[2048]);
        Directory.SetCreationTimeUtc(folder, DateTime.UtcNow.AddHours(-1));
        try
        {
            await w.CheckLeftoverTempAsync();
            Assert.Equal(said, w.NoticeMessage);

            w.NoticeButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Salandual's temporary folder holds 1 file (2 KB) left from before. Delete them?", w.NoticeMessage);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public void AFailedSave_IsSaidOnce()
    {
        var w = Started(new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Loaded));
        var path = w.Services.Settings.FilePath;
        w.ShowSettingsSaveFailed(new UnauthorizedAccessException());
        Assert.Equal($"Salandual couldn't save its settings to {path} (no permission). It tries again with the next change and when it closes.", w.NoticeMessage);

        w.NoticeButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        w.ShowSettingsSaveFailed(new UnauthorizedAccessException());
        Assert.Null(w.NoticeMessage);
        w.CloseNow();
    }

    [Fact]
    public void EachCase_SaysWhatHappened_WhereAndWhy()
    {
        var broken = new System.Text.Json.JsonException("x", null, lineNumber: 3, bytePositionInLine: 7);
        Assert.Equal(
            $"Salandual's settings file {File} wasn't valid (a mistake on line 4), so Salandual started with its defaults. Your file was kept beside it as settings.json.broken-20261019-180000.",
            SettingsFileMessages.ForLoad(new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Corrupt, BackupPath: File + ".broken-20261019-180000", Error: broken), File));
        Assert.Equal(
            $"Salandual's settings file {File} isn't valid (a mistake on line 4) and couldn't be moved aside, so Salandual started with its defaults and won't save over the file until it starts again.",
            SettingsFileMessages.ForLoad(new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Corrupt, Error: broken), File));
        Assert.Equal(
            $"Salandual's settings file {File} couldn't be opened (no permission), so Salandual started with its defaults and won't save over the file until it starts again.",
            SettingsFileMessages.ForLoad(new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Unreadable, Error: new UnauthorizedAccessException()), File));
        Assert.Equal(
            $"Salandual's settings file {File} is from a newer Salandual. It was read as far as possible, and changes made now won't be saved over it.",
            SettingsFileMessages.ForLoad(new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.NewerVersion), File));
        Assert.Equal(
            $"Salandual couldn't save its settings to {File} (The disk is full). It tries again with the next change and when it closes.",
            SettingsFileMessages.ForSave(new IOException("The disk is full."), File));
        Assert.Null(SettingsFileMessages.ForLoad(new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.NotFound), File));
    }
}
