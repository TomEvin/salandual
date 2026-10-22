// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/OperationsCaptureTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.Core.Operations;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class OperationsCaptureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("salandual-opscap-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static async Task Until(Func<bool> condition, int tries = 500)
    {
        for (var i = 0; i < tries && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static void Key(TopLevel w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        try
        {
            w.KeyRelease(key, mods, PhysicalKey.None, null);
        }
        catch (ObjectDisposedException)
        {
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void Save(TopLevel w, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task Capture_NewFolderRenameToolbarWhenDone()
    {
        var photos = Directory.CreateDirectory(Path.Join(_root, "Pictures", "Nyaralás 2026")).FullName;
        var backup = Directory.CreateDirectory(Path.Join(_root, "Backup", "Photos")).FullName;
        Directory.CreateDirectory(Path.Join(photos, "Balaton"));
        foreach (var (name, size) in new[] { ("IMG_0412.HEIC", 3_400_000), ("IMG_0413.HEIC", 2_900_000), ("IMG_0414.HEIC", 3_900_000), ("jegyzetek.md", 3_000) })
        {
            File.WriteAllBytes(Path.Join(photos, name), new byte[size]);
        }

        var w = new MainWindow { Width = 1280, Height = 760 };
        w.Show();
        Assert.True(await w.LeftPanel.Panel.NavigateAsync(photos));
        Assert.True(await w.RightPanel.Panel.NavigateAsync(backup));
        w.LeftPanel.View.Focus();
        var panel = w.LeftPanel.Panel;
        int Index(string name) => panel.Items.ToList().FindIndex(e => e.Name == name);
        var app = (App)Avalonia.Application.Current!;
        foreach (var dark in new[] { false, true })
        {
            var suffix = dark ? "dark" : "light";
            app.Theme.Apply(AppearanceSettings.Fixed(dark ? ThemeCatalog.SalandualDarkId : ThemeCatalog.SalandualLightId));
            Dispatcher.UIThread.RunJobs();

            panel.SelectWhere(_ => true, select: false);
            foreach (var name in new[] { "IMG_0412.HEIC", "IMG_0413.HEIC", "IMG_0414.HEIC" })
            {
                panel.Toggle(Index(name));
            }

            Dispatcher.UIThread.RunJobs();
            Save(w, $"m33-toolbar-{suffix}.png");
            Key(w, Avalonia.Input.Key.F7);
            await Until(() => w.OpenOperationDialog is NewFolderDialog);
            var newFolder = (NewFolderDialog)w.OpenOperationDialog!;
            newFolder.NameBox.Text = "Válogatás";
            Save(newFolder, $"m33-newfolder-{suffix}.png");
            Key(newFolder, Avalonia.Input.Key.Escape);
            await Until(() => w.OpenOperationDialog is null);

            panel.SelectWhere(_ => true, select: false);
            panel.SetFocus(Index("IMG_0413.HEIC"));
            Key(w, Avalonia.Input.Key.F2);
            w.LeftPanel.Rename!.Box.Text = "IMG_0413.jpg";
            Save(w, $"m33-rename-warning-{suffix}.png");
            Key(w, Avalonia.Input.Key.Escape);
            Key(w, Avalonia.Input.Key.Escape);

            w.ToggleOperationsPanel();
            w.OpsPanel!.WhenDoneBox.SelectedIndex = w.OpsPanel.WhenDoneBox.Items.IndexOf("Shut down");
            Save(w, $"m33-whendone-{suffix}.png");
            w.ToggleOperationsPanel();
            w.RunPowerAction = _ => true;
            panel.SetFocus(Index("jegyzetek.md"));
            w.Run("File.Duplicate", Core.Commands.PanelTarget.Active);
            await Until(() => w.OpenCountdown is not null);
            for (var i = 0; i < 18; i++)
            {
                w.OpenCountdown!.Tick();
            }

            Save(w.OpenCountdown!, $"m33-countdown-{suffix}.png");
            Key(w.OpenCountdown!, Avalonia.Input.Key.Escape);
            await Until(() => w.OpenCountdown is null);
            File.Delete(Path.Join(photos, "jegyzetek (2).md"));
        }

        app.Theme.Apply(new AppearanceSettings());
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Capture_CopyConflictPanelGraphDelete()
    {
        var photos = Directory.CreateDirectory(Path.Join(_root, "Pictures", "Nyaralás 2026")).FullName;
        var backup = Directory.CreateDirectory(Path.Join(_root, "Backup", "Photos")).FullName;
        Directory.CreateDirectory(Path.Join(photos, "Edited"));
        File.WriteAllBytes(Path.Join(photos, "Edited", "IMG_0412-edit.jpg"), new byte[5_600_000]);
        foreach (var (name, size) in new[] { ("IMG_0412.HEIC", 3_400_000), ("IMG_0413.HEIC", 2_900_000), ("IMG_0414.HEIC", 3_900_000) })
        {
            File.WriteAllBytes(Path.Join(photos, name), new byte[size]);
        }

        File.WriteAllBytes(Path.Join(backup, "IMG_0413.HEIC"), new byte[2_600_000]);
        File.SetLastWriteTimeUtc(Path.Join(backup, "IMG_0413.HEIC"), new DateTime(2026, 8, 14, 16, 23, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Join(photos, "IMG_0413.HEIC"), new DateTime(2026, 8, 14, 16, 23, 0, DateTimeKind.Utc));

        var w = new MainWindow { Width = 1280, Height = 760 };
        w.Show();
        Assert.True(await w.LeftPanel.Panel.NavigateAsync(photos));
        Assert.True(await w.RightPanel.Panel.NavigateAsync(backup));
        w.LeftPanel.View.Focus();
        foreach (var name in new[] { "IMG_0412.HEIC", "IMG_0413.HEIC", "IMG_0414.HEIC" })
        {
            var panel = w.LeftPanel.Panel;
            panel.Toggle(panel.Items.ToList().FindIndex(e => e.Name == name));
        }

        Dispatcher.UIThread.RunJobs();
        var app = (App)Avalonia.Application.Current!;
        foreach (var dark in new[] { false, true })
        {
            var suffix = dark ? "dark" : "light";
            app.Theme.Apply(AppearanceSettings.Fixed(dark ? ThemeCatalog.SalandualDarkId : ThemeCatalog.SalandualLightId));
            Dispatcher.UIThread.RunJobs();

            Key(w, Avalonia.Input.Key.F5);
            await Until(() => w.OpenOperationDialog is CopyMoveDialog);
            var copy = (CopyMoveDialog)w.OpenOperationDialog!;
            await Until(() => copy.Preview is not null);
            Save(copy, $"m32-copy-{suffix}.png");
            copy.ShowOptions();
            Save(copy, $"m32-copy-options-{suffix}.png");
            Key(copy, Avalonia.Input.Key.Escape);
            await Until(() => w.OpenOperationDialog is null);
        }

        app.Theme.Apply(AppearanceSettings.Fixed(ThemeCatalog.SalandualLightId));
        Dispatcher.UIThread.RunJobs();

        Key(w, Avalonia.Input.Key.F5);
        await Until(() => w.OpenOperationDialog is CopyMoveDialog);
        var start = (CopyMoveDialog)w.OpenOperationDialog!;
        await Until(() => start.Preview is not null);
        Key(start, Avalonia.Input.Key.Enter);
        await Until(() => w.OpenConflictDialog is not null);
        Save(w.OpenConflictDialog!, "m32-conflict-light.png");
        w.OpenConflictDialog!.MinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => w.Chip?.Look == "ask");
        w.ToggleOperationsPanel();
        Save(w, "m32-panel-question-light.png");
        Assert.True(w.Operations.Jobs.Count > 0);
        var job = w.Operations.Jobs[0];
        job.Answer(new OperationAnswer(AnswerKind.Skip));
        await Until(() => job.IsFinished);
        w.ToggleOperationsPanel();

        app.Theme.Apply(AppearanceSettings.Fixed(ThemeCatalog.SalandualDarkId));
        await File.WriteAllBytesAsync(Path.Join(photos, "video.mov"), new byte[70 * 1024 * 1024], TestContext.Current.CancellationToken);
        await w.LeftPanel.Panel.RefreshAsync();
        var big = FileOperations.Copy(w.Operations.Files, [new OperationSource(Path.Join(photos, "video.mov"), false)], new TransferTarget(backup), new TransferOptions { SpeedLimit = 24L << 20 });
        w.Operations.Add(big, toQueue: false);
        w.ToggleOperationsPanel();
        await Task.Delay(1800, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        Save(w, "m32-panel-graph-dark.png");
        big.Cancel();
        await Until(() => big.IsFinished);
        w.ToggleOperationsPanel();

        app.Theme.Apply(AppearanceSettings.Fixed(ThemeCatalog.SalandualLightId));
        w.LeftPanel.Panel.SetFocus(w.LeftPanel.Panel.Items.ToList().FindIndex(e => e.Name == "IMG_0412.HEIC"));
        Key(w, Avalonia.Input.Key.F5);
        await Until(() => w.OpenOperationDialog is CopyMoveDialog);
        var onto = (CopyMoveDialog)w.OpenOperationDialog!;
        onto.To.Text = photos + "\\";
        await Until(() => onto.OntoItselfBox is not null);
        Save(onto, "m32-copy-onto-itself-light.png");
        Key(onto, Avalonia.Input.Key.Escape);
        await Until(() => w.OpenOperationDialog is null);

        app.Theme.Apply(AppearanceSettings.Fixed(ThemeCatalog.SalandualDarkId));
        var panelModel = w.LeftPanel.Panel;
        panelModel.Toggle(panelModel.Items.ToList().FindIndex(e => e.Name == "Edited"));
        Key(w, Avalonia.Input.Key.Delete, RawInputModifiers.Shift);
        await Until(() => w.OpenOperationDialog is DeleteDialog);
        var delete = (DeleteDialog)w.OpenOperationDialog!;
        await delete.Counting;
        Save(delete, "m32-delete-permanent-dark.png");
        Key(delete, Avalonia.Input.Key.Escape);
        await Until(() => w.OpenOperationDialog is null);

        app.Theme.Apply(new AppearanceSettings());
        w.CloseNow();
    }
}
