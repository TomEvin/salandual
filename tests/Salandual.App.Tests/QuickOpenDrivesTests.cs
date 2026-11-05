// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/QuickOpenDrivesTests.cs

using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.Core.QuickOpen;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class QuickOpenDrivesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-quick-drives-").FullName;
    private readonly string _panel, _driveA, _driveB;

    public QuickOpenDrivesTests()
    {
        _panel = Make("Panel");
        _driveA = Make("DriveA");
        _driveB = Make("DriveB");
        Write(_panel, "budget.xlsx");
        Write(_driveA, "Docs", "budget-plan.pdf");
        Write(_driveA, "Archive", "budget.csv");
        Write(_driveA, "node_modules", "budget-lib.js");
        Write(_driveA, "Skipme", "budget-skip.txt");
        Write(_driveA, "Secret", "budget-secret.txt");
        File.SetAttributes(Path.Join(_driveA, "Secret"), FileAttributes.Directory | FileAttributes.Hidden);
        Write(_driveB, "Photos", "budget-photo.jpg");
        Write(_driveB, "Indexed", "budget-indexed.txt");
        for (var i = 0; i < 12; i++)
        {
            Write(_driveB, "Many", $"budget-{i:00}.txt");
        }
    }

    public void Dispose()
    {
        File.SetAttributes(Path.Join(_driveA, "Secret"), FileAttributes.Directory);
        Directory.Delete(_dir, recursive: true);
    }

    private string Make(string name) => Directory.CreateDirectory(Path.Join(_dir, name)).FullName;

    private static void Write(params string[] parts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Join(parts))!);
        File.WriteAllText(Path.Join(parts), "");
    }

    private MemoryFileNameIndex WindowsIndex() => new(FileIndexKind.WindowsIndex, [Path.Join(_driveB, "Indexed", "budget-indexed.txt")], [Path.Join(_driveB, "Indexed")]);

    private async Task<MainWindow> Open(FileNameSources sources, Func<QuickOpenSettings, QuickOpenSettings>? settings = null)
    {
        var w = new MainWindow { Width = 1100, Height = 640 };
        w.ShellAccess = new NoShellAccess();
        w.Services.FileNames = sources;
        w.Services.Settings.Update(s => s with { QuickOpen = (settings ?? (q => q))(new QuickOpenSettings { SkipFolders = ["node_modules", Path.Join(_driveA, "Skipme")] }) });
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(_panel);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Close(MainWindow w)
    {
        w.Close();
        w.Services.FileNames = FileNameSources.None;
        w.Services.Settings.Update(s => s with { QuickOpen = new QuickOpenSettings() });
    }

    private static void Press(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
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

    private static void Capture(MainWindow w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            for (var k = 0; k < 10; k++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    private static async Task OpenBox(MainWindow w)
    {
        Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Until(() => w.QuickDriveStart is { IsCompleted: true });
        Dispatcher.UIThread.RunJobs();
        await Until(() => w.QuickDriveWalk is null or { IsCompleted: true });
        Dispatcher.UIThread.RunJobs();
    }

    private static void Type(MainWindow w, string text)
    {
        w.QuickOpen!.Input.Text = text;
        w.QuickOpen.Input.CaretIndex = text.Length;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Typing_FindsFilesOnTheOtherDrives_NotWhatIsLeftOut_EnterGoesToOne()
    {
        var w = await Open(new FileNameSources(null, WindowsIndex(), () => [_driveA, _driveB]));
        try
        {
            await OpenBox(w);
            Assert.Equal(FileIndexKind.WindowsIndex, w.QuickIndex?.Kind);
            Assert.True(w.QuickDrives!.Finished);
            Assert.DoesNotContain(w.QuickDrives.Entries, e => e.Folder.StartsWith(Path.Join(_driveB, "Indexed"), StringComparison.OrdinalIgnoreCase));

            Type(w, "budget");
            var box = w.QuickOpen!;
            await Until(() => box.Headings.Contains("Files on all drives"));
            Assert.Equal("Files in " + _panel, box.Headings[0]);
            Assert.Single(box.Items, i => i.Name == "budget.xlsx");

            Type(w, "/budget");
            await Until(() => box.Items.Any(i => i.Name == "budget-indexed.txt") && box.Items.Any(i => i.Name == "budget-plan.pdf"));
            Assert.Single(box.Items, i => i.Name == "budget.xlsx");
            Assert.Contains(box.Items, i => i.Name == "budget.csv" && i.Detail == Path.Join(_driveA, "Archive"));
            Assert.DoesNotContain(box.Items, i => i.Name is "budget-lib.js" or "budget-skip.txt" or "budget-secret.txt");
            Assert.Contains("Windows index", box.Note);
            Assert.EndsWith("walked", box.Note);
            Capture(w, "quick-open-drives.png");

            Type(w, "budget-plan");
            await Until(() => box.SelectedItem?.Name == "budget-plan.pdf");
            Press(w, Key.Enter);
            await Until(() => w.ActivePanel.Panel.Path == Path.Join(_driveA, "Docs"));
            Assert.Equal("budget-plan.pdf", w.ActivePanel.Panel.FocusedEntry?.Name);
            Assert.Empty(((NoShellAccess)w.ShellAccess).Opened);
        }
        finally
        {
            Close(w);
        }
    }

    [AvaloniaFact]
    public async Task MoreThanEight_AllFilesRow_OpensFilesOnly()
    {
        var w = await Open(new FileNameSources(null, null, () => [_driveA, _driveB]));
        try
        {
            await OpenBox(w);
            Type(w, "budget");
            var box = w.QuickOpen!;
            await Until(() => box.Items.Any(i => i.Kind == QuickKind.More));
            var more = box.Items.Single(i => i.Kind == QuickKind.More);
            Assert.Equal("All files…", more.Name);
            Assert.Equal("16 found", more.Detail);
            Assert.Equal(8, box.Items.Count(i => i.Kind == QuickKind.File && i.Detail.StartsWith(_dir, StringComparison.OrdinalIgnoreCase) && !i.Detail.StartsWith(_panel, StringComparison.Ordinal)));

            box.SelectedIndex = box.Items.ToList().IndexOf(more);
            Press(w, Key.Enter);
            Assert.True(box.IsVisible);
            Assert.Equal("/budget", box.Input.Text);
            Assert.Equal(QuickScope.Files, box.ChosenScope);
            await Until(() => box.Items.Count(i => i.Kind == QuickKind.File) == 17);
            Assert.DoesNotContain(box.Items, i => i.Kind == QuickKind.Command);
        }
        finally
        {
            Close(w);
        }
    }

    [AvaloniaFact]
    public async Task CutShort_KeepSearching_GoesOn_AndClosingKeepsWhereItStopped()
    {
        var w = await Open(new FileNameSources(null, null, () => [_driveA, _driveB]));
        w.QuickWalkLimit = 5;
        try
        {
            await OpenBox(w);
            var walk = w.QuickDrives!;
            Assert.Equal(7, walk.Count);
            Assert.False(walk.Finished);
            Type(w, "budget");
            var box = w.QuickOpen!;
            Assert.StartsWith("Stopped at the limit", box.Note);
            var keep = box.Items.Single(i => i.Name == "Keep searching");
            Assert.EndsWith("not finished", keep.Detail);

            box.SelectedIndex = box.Items.ToList().IndexOf(keep);
            Press(w, Key.Enter);
            Assert.True(box.IsVisible);
            await Until(() => w.QuickDriveWalk is { IsCompleted: true } && walk.Count == 20);

            Press(w, Key.Escape);
            await OpenBox(w);
            Assert.Same(walk, w.QuickDrives);
            Assert.Equal(21, walk.Count);
            Assert.True(walk.Finished);
        }
        finally
        {
            Close(w);
        }
    }

    [AvaloniaFact]
    public async Task Everything_WhenRunning_AnswersAlone_UnlessOptionsSayNot_AndOffFindsNoOtherDrive()
    {
        var everything = new MemoryFileNameIndex(FileIndexKind.Everything, [Path.Join(_driveB, "Photos", "budget-photo.jpg"), Path.Join(_driveB, "Photos") + "\\"]);
        var sources = new FileNameSources(everything, WindowsIndex(), () => [_driveA, _driveB]);
        var w = await Open(sources);
        try
        {
            await OpenBox(w);
            Assert.Equal(FileIndexKind.Everything, w.QuickIndex?.Kind);
            Assert.Null(w.QuickDrives);
            Type(w, "budget");
            var box = w.QuickOpen!;
            await Until(() => box.Items.Any(i => i.Name == "budget-photo.jpg"));
            Assert.DoesNotContain(box.Items, i => i.Name == "budget-plan.pdf");
            Assert.Equal("Everything", box.Note);
            Type(w, "photos");
            await Until(() => box.Items.Any(i => i is { Kind: QuickKind.Folder, Name: "Photos" }));
            Press(w, Key.Escape);

            everything.Available = false;
            await OpenBox(w);
            Assert.Equal(FileIndexKind.WindowsIndex, w.QuickIndex?.Kind);
            Assert.NotNull(w.QuickDrives);
            Press(w, Key.Escape);

            everything.Available = true;
            w.Services.Settings.Update(s => s with { QuickOpen = s.QuickOpen with { FilesFrom = QuickOpenFilesFrom.NoEverything } });
            await OpenBox(w);
            Assert.Equal(FileIndexKind.WindowsIndex, w.QuickIndex?.Kind);
            Press(w, Key.Escape);

            w.Services.Settings.Update(s => s with { QuickOpen = s.QuickOpen with { FilesFrom = QuickOpenFilesFrom.Off } });
            var asked = everything.Asked;
            Press(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
            Type(w, "budget");
            await Until(() => w.QuickWalk is { IsCompleted: true });
            Dispatcher.UIThread.RunJobs();
            Assert.Null(w.QuickIndex);
            Assert.DoesNotContain("Files on all drives", box.Headings);
            Assert.Contains(box.Items, i => i.Name == "budget.xlsx");
            Assert.Equal(asked, everything.Asked);
        }
        finally
        {
            Close(w);
        }
    }

    [AvaloniaFact]
    public async Task OptionsShowWhatEachSourceCovers_AndTheFolderListTakesNamesAndPaths()
    {
        var w = await Open(new FileNameSources(null, WindowsIndex(), () => [_driveA, _driveB]));
        try
        {
            var options = new Options.OptionsWindow(w.Services);
            Assert.Contains(options.Items, i => i.Title == "Files on all drives");
            Assert.Contains(options.Items, i => i.Title == "Leave out hidden files and folders");
            Assert.Contains(options.Items, i => i.Title == "Folders left out");
            Assert.Equal(["node_modules", Path.Join(_driveA, "Skipme")], w.Services.Settings.Current.QuickOpen.SkipFolders);

            w.Services.Settings.Update(s => s with { QuickOpen = s.QuickOpen with { SkipFolders = ["Docs", "bad*name", "x:relative", @"D:\Backups\"] } });
            Assert.Equal(["Docs", @"D:\Backups"], w.Services.Settings.Current.QuickOpen.SkipFolders);
        }
        finally
        {
            Close(w);
        }
    }
}
