// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/DiskActivityUiTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.App.Wizard;
using Salandual.Core;

namespace Salandual.App.Tests;

public sealed class DiskActivityUiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-activity-").FullName;
    private readonly NoShellAccess _shell = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Drive => DiskActivity.DriveOf(_dir)!;

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.ShellAccess = _shell;
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(_dir);
        Dispatcher.UIThread.RunJobs();
        w.StartActivity();
        return w;
    }

    [AvaloniaFact]
    public async Task Start_SamplesThePanelsDrives_WithoutAnyChange()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.ShellAccess = _shell;
        w.Show();
        try
        {
            await w.ActivePanel.Panel.NavigateAsync(_dir);
            Dispatcher.UIThread.RunJobs();
            w.StartActivity();
            Assert.Contains(Drive, w.Activity.Wanted);

        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task AnotherProgramInFront_GoesOnReading_ShowsNothingNew_MinimizedPauses()
    {
        var w = await Open();
        try
        {
            var inFront = true;
            var minimized = false;
            w.Activity.InFront = () => inFront;
            w.Activity.IsMinimized = () => minimized;
            Source(w).Values[Drive] = new DiskActivitySample(40, 0, 0);
            w.Activity.CheckInFront();
            w.Activity.SampleNow();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(40, w.ActivePanel.DirectoryLine.Activity?.Percent);

            inFront = false;
            w.Activity.CheckInFront();
            Assert.True(w.Activity.IsBehind);
            Assert.False(w.Activity.IsPaused);
            Source(w).Values[Drive] = new DiskActivitySample(90, 0, 0);
            w.Activity.SampleNow();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(40, w.ActivePanel.DirectoryLine.Activity?.Percent);
            Assert.Equal(2, w.Activity.History.Since(Drive, DateTime.MinValue).Count);

            inFront = true;
            w.Activity.CheckInFront();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(90, w.ActivePanel.DirectoryLine.Activity?.Percent);

            minimized = true;
            w.Activity.CheckInFront();
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.Activity.IsPaused);
            Assert.Null(w.ActivePanel.DirectoryLine.Activity);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task GraphLooks_ShowTheLast30Seconds_WithTheirSummaryInTheTooltip()
    {
        var w = await Open();
        try
        {
            var now = new DateTime(2026, 10, 29, 12, 0, 0);
            w.Activity.Now = () => now;
            w.Activity.SetPaused(false);
            int[] busy = [3, 2, 4, 3, 2, 5, 4, 3, 8, 30, 72, 95, 100, 98, 88, 92, 85, 70, 64, 58, 66, 71, 60, 55, 49, 62, 58, 64, 60, 62];
            foreach (var b in busy)
            {
                Source(w).Values[Drive] = new DiskActivitySample(b, 1_000_000, 0);
                now = now.AddSeconds(1);
                w.Activity.SampleNow();
            }

            var line = w.ActivePanel.DirectoryLine;
            foreach (var (look, name) in new[] { (ActivityLook.Columns, "activity-columns.png"), (ActivityLook.Line, "activity-graph-line.png") })
            {
                w.Services.Settings.Update(s => s with { DriveMenu = s.DriveMenu with { ActivityLook = look } });
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(look, line.ActivityLook);
                Assert.Equal(30, line.ActivityRecent?.Count);
                Assert.Equal(62, line.ActivityRecent![29]);
                Capture(w, name);
                Assert.True(line.ActivityBounds.Width > 60);
                Assert.Contains("The last 30 seconds: at most 100 %", line.ActivityTip());
            }

            w.Services.Settings.Update(s => s with { DriveMenu = s.DriveMenu with { ActivityLook = ActivityLook.Bar } });
            Dispatcher.UIThread.RunJobs();
            Assert.Null(line.ActivityRecent);
            Assert.DoesNotContain("30 seconds", line.ActivityTip());
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task DriveInformation_ActivityTabFirst_ChartsOfFifteenMinutes_DetailsSecond()
    {
        var w = await Open();
        try
        {
            var now = new DateTime(2026, 10, 29, 12, 0, 0);
            w.Activity.Now = () => now;
            w.Activity.SetPaused(false);
            for (var s = 0; s < 600; s += 2)
            {
                if (s is > 200 and < 300)
                {
                    now = now.AddSeconds(2);
                    continue;
                }

                Source(w).Values[Drive] = new DiskActivitySample(s is > 400 and < 450 ? 90 : 5, s * 100_000, s * 20_000);
                now = now.AddSeconds(2);
                w.Activity.SampleNow();
            }

            var info = new Dialogs.DriveInfoDialog(_dir, Core.Listing.DisplayFormat.Default, w.Activity);
            info.Show(w);
            await info.Reading;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, info.Tab);
            Assert.NotNull(info.BusyChart);
            Assert.NotNull(info.SpeedChart);
            Assert.Equal(info.BusyChart!.Points.Count, w.Activity.History.Since(Drive, DateTime.MinValue).Count);
            Assert.StartsWith("busy 5 %", info.ActivityNowText);
            Assert.Contains(info.BusyChart.Gaps(), g => g.To - g.From > 0.08);
            Assert.Equal(598 * 100_000, info.SpeedChart!.SpeedMax);
            Assert.True(info.DetailsTable is { IsVisible: false });
            Assert.Contains("busy", info.BusyChart.TipAt(info.BusyChart.Bounds.Width - 3));
            CaptureDialog(info, "driveinfo-activity.png");

            Assert.Equal("5 %", info.NowValues[0].Text);
            var writing = info.NowValues[2].TranslatePoint(default, info)!.Value;
            Source(w).Values[Drive] = new DiskActivitySample(77, 0, 0);
            now = now.AddSeconds(1);
            w.Activity.SampleNow();
            Dispatcher.UIThread.RunJobs();
            info.CaptureRenderedFrame();
            Assert.StartsWith("busy 77 %", info.ActivityNowText);
            Assert.Equal("0 B/s", info.NowValues[1].Text);
            Assert.Equal(writing, info.NowValues[2].TranslatePoint(default, info)!.Value);

            var height = info.Bounds.Height;
            info.ShowTab(1);
            Dispatcher.UIThread.RunJobs();
            Assert.True(info.DetailsTable!.IsVisible);
            Assert.False(info.BusyChart.IsVisible && info.BusyChart.IsEffectivelyVisible);
            CaptureDialog(info, "driveinfo-details.png");
            info.ShowTab(0);
            Dispatcher.UIThread.RunJobs();
            Assert.True(info.Bounds.Height >= height - 1);
            info.Close();
        }
        finally
        {
            w.Close();
        }
    }

    private static void CaptureDialog(Window dialog, string name)
    {
        var frame = dialog.CaptureRenderedFrame();
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task AMissedReading_KeepsTheLastOne_ForTwoSamples()
    {
        var w = await Open();
        try
        {
            w.Activity.SetPaused(false);
            Source(w).Values[Drive] = new DiskActivitySample(55, 0, 0);
            w.Activity.SampleNow();
            Source(w).Values.Clear();
            w.Activity.SampleNow();
            w.Activity.SampleNow();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(55, w.ActivePanel.DirectoryLine.Activity?.Percent);
            w.Activity.SampleNow();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(w.ActivePanel.DirectoryLine.Activity);
        }
        finally
        {
            w.Close();
        }
    }

    private static MemoryDiskActivitySource Source(MainWindow w) => (MemoryDiskActivitySource)w.Services.DiskActivitySource;

    private static void Capture(Window w, string name)
    {
        var frame = w.CaptureRenderedFrame();
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task DirectoryLine_ShowsThePanelsDrive_WithTheSpeedsInTheTooltip()
    {
        var w = await Open();
        try
        {
            Assert.Contains(Drive, w.Activity.Wanted);
            Source(w).Values[Drive] = new DiskActivitySample(61.7, 140_000_000, 12_000_000);
            w.Activity.SampleNow();
            Dispatcher.UIThread.RunJobs();
            var line = w.ActivePanel.DirectoryLine;
            Assert.Equal(62, line.Activity?.Percent);
            Capture(w, "activity-line.png");
            Assert.NotEqual(default, line.ActivityBounds);
            Assert.True(line.ActivityBounds.Right <= line.FreeSpaceBounds.X || line.FreeSpaceBounds == default);
            var tip = line.ActivityTip()!;
            Assert.StartsWith(Drive + " busy 62 %", tip);
            Assert.Contains("reading ", tip);
            Assert.Contains("Drive information", tip);

            Assert.Null(w.ActivePanel.Strip.Activity);
            Assert.Null(w.SharedStrip.Activity);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task NoReading_NothingDrawn_RatherThanZero()
    {
        var w = await Open();
        try
        {
            w.Activity.SampleNow();
            Dispatcher.UIThread.RunJobs();
            var line = w.ActivePanel.DirectoryLine;
            Assert.Null(line.Activity);
            w.CaptureRenderedFrame();
            Assert.Equal(default, line.ActivityBounds);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task InTheBackground_NothingShown_AndNothingSampled()
    {
        var w = await Open();
        try
        {
            w.Activity.SetPaused(false);
            Source(w).Values[Drive] = new DiskActivitySample(40, 0, 0);
            w.Activity.SampleNow();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(w.ActivePanel.DirectoryLine.Activity);

            w.Activity.SetPaused(true);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(w.ActivePanel.DirectoryLine.Activity);
            Assert.True(w.Activity.IsPaused);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task Switches_LineOff_BarOn()
    {
        var w = await Open();
        try
        {
            var settings = w.Services.Settings;
            settings.Update(s => s with { DriveMenu = s.DriveMenu with { ActivityInLine = false } });
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(w.Activity.Wanted);
            Source(w).Values[Drive] = new DiskActivitySample(50, 0, 0);
            w.Activity.SampleNow();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(w.ActivePanel.DirectoryLine.Activity);

            settings.Update(s => s with { DriveMenu = s.DriveMenu with { ActivityInStrip = true } });
            Dispatcher.UIThread.RunJobs();
            var strip = w.SharedStrip.IsVisible ? w.SharedStrip : w.ActivePanel.Strip;
            for (var i = 0; i < 100 && !strip.Items.Any(item => item.Kind == StripItemKind.Drive); i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }

            var drives = strip.Items.Where(item => item.Kind == StripItemKind.Drive && !item.IsNetwork).ToList();
            Assert.NotEmpty(drives);
            Assert.Equal(drives.Select(d => DiskActivity.DriveOf(d.Target)).Order(), w.Activity.Wanted.Order());
            foreach (var d in drives)
            {
                Source(w).Values[DiskActivity.DriveOf(d.Target)!] = new DiskActivitySample(75, 0, 0);
            }

            w.Activity.SampleNow();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(strip.Activity);
            Capture(w, "activity-strip.png");
            var bar = strip.ActivityBar(drives[0]);
            Assert.NotNull(bar);
            Assert.True(bar.Value.Height <= 2);
            Assert.Null(w.ActivePanel.DirectoryLine.Activity);
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public void FirstSteps_TheTick_SetsTheLineSwitch()
    {
        var services = AppServices.CreateIsolated();
        var c = FirstStepsChoices.From(services.Settings.Current, startsWithWindows: false, windowsDark: false);
        Assert.True(c.DiskActivity);
        Assert.False((c with { DiskActivity = false }).ApplyTo(services.Settings.Current, DateTime.Now).DriveMenu.ActivityInLine);
    }
}
