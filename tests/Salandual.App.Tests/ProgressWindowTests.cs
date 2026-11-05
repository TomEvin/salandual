// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ProgressWindowTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Operations;
using Salandual.Core.Operations;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class ProgressWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-progress-").FullName;
    private readonly List<ManualResetEventSlim> _gates = [];

    public void Dispose()
    {
        foreach (var gate in _gates)
        {
            gate.Set();
        }

        Directory.Delete(_dir, recursive: true);
    }

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(_dir);
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static async Task Until(Func<bool> condition, int tries = 500)
    {
        for (var i = 0; i < tries && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private (OperationJob Job, ManualResetEventSlim Gate) Job(OperationKind kind = OperationKind.Copy, string title = "Copying 3 files", string? current = null)
    {
        var gate = new ManualResetEventSlim();
        _gates.Add(gate);
        var job = new OperationJob(kind, title, "C: → D:", [$"V{_gates.Count}"], j =>
        {
            j.Report(OperationPhase.Working, 1 << 20, 4 << 20, 1, 3, current ?? "a.txt", force: true);
            gate.Wait(j.CancellationToken);
            j.Report(OperationPhase.Working, 4 << 20, 4 << 20, 3, 3, null, force: true);
        });
        return (job, gate);
    }

    private static void Capture(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame();
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task AJobYouStart_OpensItsWindow_InTheBackgroundAndBack_QueuedAndChipOnlyDont()
    {
        var w = await Open();
        try
        {
            w.ProgressDelay = TimeSpan.Zero;
            var (job, gate) = Job();
            w.Operations.Add(job, toQueue: false);
            await Until(() => w.ProgressWindows.ContainsKey(job));
            var window = w.ProgressWindows[job];
            Assert.False(window.CloseWhenDoneBox.IsChecked);
            await Until(() => job.State == JobState.Running);
            window.Refresh();
            Assert.Equal("25 % · Copying 3 files", window.Title);
            Assert.Equal("file 2 of 3", window.View.SideText);
            Capture(window, "progress-copy.png");

            Click(window.BackgroundButton);
            await Until(() => !w.ProgressWindows.ContainsKey(job));
            Assert.False(job.IsFinished);
            w.Operations.ShowWindow(job);
            await Until(() => w.ProgressWindows.ContainsKey(job));

            var (second, gate2) = Job(title: "Moving 1 file");
            w.Operations.Add(second, toQueue: false);
            await Until(() => w.ProgressWindows.ContainsKey(second));
            Assert.Equal(2, w.ProgressWindows.Count);
            var (queued, gate3) = Job(title: "Copying later");
            w.Operations.Add(queued, toQueue: true);
            w.Services.Settings.Update(s => s with { Operations = s.Operations with { Progress = ProgressShow.Chip } });
            var (chip, gate4) = Job(title: "Copying quietly");
            w.Operations.Add(chip, toQueue: false);
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.False(w.ProgressWindows.ContainsKey(queued));
            Assert.False(w.ProgressWindows.ContainsKey(chip));
            foreach (var g in new[] { gate, gate2, gate3, gate4 })
            {
                g.Set();
            }

            await Until(() => w.Operations.Jobs.All(j => j.IsFinished));
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task AQuickJob_NeverFlashesAWindow()
    {
        var w = await Open();
        try
        {
            var job = new OperationJob(OperationKind.Copy, "Copying 1 file", "C: → D:", ["Q"], j => j.Report(OperationPhase.Working, 1, 1, 1, 1, null, force: true));
            w.Operations.Add(job, toQueue: false);
            await Until(() => job.IsFinished);
            for (var i = 0; i < 70; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Empty(w.ProgressWindows);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task WhenDone_ItStaysWithTheSummary_OrClosesWhenTicked_AndTheTickIsRemembered()
    {
        var w = await Open();
        try
        {
            w.ProgressDelay = TimeSpan.Zero;
            var (job, gate) = Job();
            w.Operations.Add(job, toQueue: false);
            await Until(() => w.ProgressWindows.ContainsKey(job));
            var window = w.ProgressWindows[job];
            gate.Set();
            await Until(() => job.IsFinished);
            window.Refresh();
            Assert.True(window.HasEnded);
            Assert.StartsWith("✓ Done", window.EndText, StringComparison.Ordinal);
            Assert.Equal(window.EndText, window.View.SideText);
            Assert.Equal("Copying 3 files", window.Title);
            Assert.True(window.CloseButton.IsVisible);
            Assert.False(window.CancelButton.IsVisible);
            Capture(window, "progress-done.png");
            Click(window.CloseButton);
            await Until(() => w.ProgressWindows.Count == 0);

            var (next, gate2) = Job(title: "Copying 2 files");
            w.Operations.Add(next, toQueue: false);
            await Until(() => w.ProgressWindows.ContainsKey(next));
            w.ProgressWindows[next].CloseWhenDoneBox.IsChecked = true;
            Assert.True(w.Services.Settings.Current.Operations.CloseWhenDone);
            gate2.Set();
            await Until(() => w.ProgressWindows.Count == 0);

            var (third, _) = Job(title: "Copying 3 more");
            w.Services.Settings.Update(s => s with { Operations = s.Operations with { CloseWhenDone = false } });
            w.Operations.Add(third, toQueue: false);
            await Until(() => w.ProgressWindows.ContainsKey(third));
            var last = w.ProgressWindows[third];
            await Until(() => third.State == JobState.Running);
            Click(last.CancelButton);
            await Until(() => third.IsFinished);
            last.Refresh();
            Assert.Equal("Cancelled", last.EndText);
            last.Close();
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task TheWindow_KeepsItsHeight_SmallFileBigFileAndTheEnd()
    {
        var w = await Open();
        try
        {
            w.ProgressDelay = TimeSpan.Zero;
            var big = new ManualResetEventSlim();
            var end = new ManualResetEventSlim();
            _gates.Add(big);
            _gates.Add(end);
            var job = new OperationJob(OperationKind.Copy, "Copying 2 files", "C: → D:", ["H"], j =>
            {
                j.Report(OperationPhase.Working, 1 << 20, 300L << 20, 0, 2, "small.txt", force: true);
                big.Wait(j.CancellationToken);
                j.Report(OperationPhase.Working, 100L << 20, 300L << 20, 1, 2, "video.mov", force: true, currentDone: 99L << 20, currentTotal: 299L << 20);
                end.Wait(j.CancellationToken);
                j.Report(OperationPhase.Working, 300L << 20, 300L << 20, 2, 2, null, force: true);
            });
            w.Operations.Add(job, toQueue: false);
            await Until(() => w.ProgressWindows.ContainsKey(job));
            var window = w.ProgressWindows[job];
            await Until(() => job.State == JobState.Running);

            double Height()
            {
                window.Refresh();
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
                return window.Bounds.Height;
            }

            var small = Height();
            big.Set();
            await Until(() => job.Progress.CurrentTotal > 0);
            Assert.Equal(small, Height());
            end.Set();
            await Until(() => job.IsFinished);
            Assert.Equal(small, Height());
            Assert.True(window.HasEnded);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task ADeleteThatStaysOnOneItem_SaysWhatItIsOn()
    {
        var w = await Open();
        try
        {
            w.ProgressDelay = TimeSpan.Zero;
            var file = Path.Join(_dir, "disk-image.iso");
            File.WriteAllText(file, "x");
            var (job, gate) = Job(OperationKind.Delete, "Deleting 1 item permanently", file);
            w.Operations.Add(job, toQueue: false);
            await Until(() => w.ProgressWindows.ContainsKey(job));
            var window = w.ProgressWindows[job];
            await Until(() => job.State == JobState.Running);
            var now = DateTime.UtcNow;
            window.Now = () => now;
            window.Refresh();
            Assert.Null(window.StuckText);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            var height = window.Bounds.Height;
            now = now.AddSeconds(2);
            window.Refresh();
            Assert.Null(window.StuckText);
            now = now.AddSeconds(3);
            window.Refresh();
            Assert.StartsWith("Still on disk-image.iso after 5 s.", window.StuckText, StringComparison.Ordinal);
            Capture(window, "progress-delete.png");
            Assert.Equal(height, window.Bounds.Height);
            gate.Set();
            await Until(() => job.IsFinished);
            window.Refresh();
            Assert.Null(window.StuckText);
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task Blocking_IsAChoice()
    {
        var w = await Open();
        try
        {
            w.ProgressDelay = TimeSpan.Zero;
            w.Services.Settings.Update(s => s with { Operations = s.Operations with { ProgressBlocks = true } });
            var (job, gate) = Job();
            w.Operations.Add(job, toQueue: false);
            await Until(() => w.ProgressWindows.ContainsKey(job));
            Assert.True(w.ProgressWindows[job].IsVisible);
            gate.Set();
            await Until(() => job.IsFinished);
            w.ProgressWindows[job].Close();
        }
        finally
        {
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task FreeSpace_HasItsOwnSwitch_AlsoInTheRightClickMenu()
    {
        var w = await Open();
        try
        {
            w.Width = 2400;
            var line = w.ActivePanel.DirectoryLine;
            await Until(() => line.Space is not null);
            line.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame();
            Assert.True(line.FreeSpaceBounds.Width > 0);

            w.ShowSpaceMenu(w.ActivePanel);
            var items = w.SpaceMenu!.Items.OfType<MenuItem>().ToList();
            Assert.Equal(["Drive information…", "Show the free space", "Show the disk activity", "Options…"], items.Select(i => (string)i.Header!));
            Assert.True(items[1].IsChecked);
            items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            w.SpaceMenu.Close();
            Assert.False(w.Services.Settings.Current.DriveMenu.FreeSpaceInLine);
            Assert.True(w.Services.Settings.Current.DriveMenu.ActivityInLine);
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame();
            Assert.Equal(0, line.FreeSpaceBounds.Width);

            w.Services.Settings.Update(s => s with { DriveMenu = s.DriveMenu with { FreeSpaceInLine = true } });
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame();
            Assert.True(line.FreeSpaceBounds.Width > 0);
        }
        finally
        {
            w.CloseNow();
        }
    }
}
