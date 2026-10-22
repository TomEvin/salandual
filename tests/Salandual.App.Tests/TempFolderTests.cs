// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/TempFolderTests.cs

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Salandual.App.Tests;

public sealed class TempFolderTests
{
    private static string Crashed(MainWindow w)
    {
        var folder = Path.Combine(w.Services.Temp.Root, "run-9-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "page-1.html"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(folder, "image-2.png"), new byte[1024]);
        Directory.SetCreationTimeUtc(folder, DateTime.UtcNow.AddHours(-1));
        return folder;
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
    public async Task LeftFromBefore_AskedOverThePanels_DeleteRemovesThem()
    {
        var w = new MainWindow();
        w.Show();
        Assert.StartsWith(w.Services.DataDirectory, w.Services.Temp.Root, StringComparison.OrdinalIgnoreCase);
        var folder = Crashed(w);

        await w.CheckLeftoverTempAsync();
        Assert.Equal("Salandual's temporary folder holds 2 files (3 KB) left from before. Delete them?", w.NoticeMessage);
        Assert.Equal(["Delete", "Show the folder", "Not now"], w.NoticeButtons.Select(b => (string?)b.Content));

        w.NoticeButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !Directory.Exists(folder));
        await Until(() => w.NoticeMessage is not null);
        Assert.Equal("Deleted 2 files (3 KB) from Salandual's temporary folder.", w.NoticeMessage);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ShowTheFolder_TakesThePanelThere_NotNowKeepsThem()
    {
        var w = new MainWindow();
        w.Show();
        var folder = Crashed(w);
        await w.CheckLeftoverTempAsync();
        w.NoticeButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => string.Equals(w.ActivePanel.Panel.Path, w.Services.Temp.Root, StringComparison.OrdinalIgnoreCase));

        await w.CheckLeftoverTempAsync();
        w.NoticeButtons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(w.NoticeMessage);
        Assert.True(Directory.Exists(folder));
        Directory.Delete(folder, recursive: true);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task NotAsked_WhenOptionsSaySo_TheyGoAtOnce()
    {
        var w = new MainWindow();
        w.Show();
        var folder = Crashed(w);
        w.Services.Settings.Update(s => s with { Confirmations = s.Confirmations with { DeleteLeftoverTemp = false } });
        try
        {
            await w.CheckLeftoverTempAsync();
            Assert.False(Directory.Exists(folder));
            Assert.Null(w.NoticeMessage);
        }
        finally
        {
            w.Services.Settings.Update(s => s with { Confirmations = s.Confirmations with { DeleteLeftoverTemp = true } });
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task NothingLeft_NothingAsked()
    {
        var w = new MainWindow();
        w.Show();
        await w.CheckLeftoverTempAsync();
        Assert.Null(w.NoticeMessage);
        w.CloseNow();
    }
}
