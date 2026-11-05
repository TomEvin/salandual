// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/DriveHealthUiTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Dialogs;
using Salandual.App.Wizard;
using Salandual.Core;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Tests;

public sealed class DriveHealthUiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-health-").FullName;
    private readonly NoShellAccess _shell = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly NvmeHealth Nvme = new(0, 41, 100, 10, 4, 26_100_000_000_000, 18_400_000_000_000, 1042, 2310, 23, 0, 0);

    private static DriveHealth Ata(int moved, int pending)
    {
        var list = new List<SmartAttribute> { new(0x05, 198, 198, moved), new(0x09, 58, 58, 31_400), new(0x0C, 99, 99, 1877), new(0xC2, 112, 100, 38), new(0xC5, 200, 200, pending), new(0xC6, 200, 200, 0) };
        for (var id = 0xD0; list.Count < 30; id++)
        {
            list.Add(new SmartAttribute(id, 100, 100, id));
        }

        return new DriveHealth(new DriveLink(LinkKind.Sata, 2, 0, 3, 0), null, null, list, false, false);
    }

    private async Task<(MainWindow, DriveInfoDialog)> Open(DriveHealth? health)
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.ShellAccess = _shell;
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(_dir);
        Dispatcher.UIThread.RunJobs();
        w.StartActivity();
        var info = new DriveInfoDialog(_dir, Core.Listing.DisplayFormat.Default, w.Activity) { ReadHealth = _ => health };
        info.Show(w);
        await info.Reading;
        Dispatcher.UIThread.RunJobs();
        return (w, info);
    }

    private static async Task ShowHealth(DriveInfoDialog info)
    {
        info.ShowTab(2);
        await info.HealthReading;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task CtrlTab_GoesRoundAllThreeTabs()
    {
        var (w, info) = await Open(new DriveHealth(new DriveLink(LinkKind.Pcie, 4, 4, 4, 4), new DriveTemperature(41, 82, 85), Nvme, [], null, false));
        try
        {
            Assert.Equal(0, info.Tab);
            info.KeyPress(Key.Tab, RawInputModifiers.Control, PhysicalKey.Tab, null);
            Assert.Equal(1, info.Tab);
            info.KeyPress(Key.Tab, RawInputModifiers.Control, PhysicalKey.Tab, null);
            Assert.Equal(2, info.Tab);
            await info.HealthReading;
            Dispatcher.UIThread.RunJobs();
            Assert.True(info.HealthScroll.IsEffectivelyVisible);
            info.KeyPress(Key.Tab, RawInputModifiers.Control, PhysicalKey.Tab, null);
            Assert.Equal(0, info.Tab);
        }
        finally
        {
            info.Close();
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task Nvme_Good_WithTheLinkAndTheLife()
    {
        var (w, info) = await Open(new DriveHealth(new DriveLink(LinkKind.Pcie, 4, 4, 4, 4), new DriveTemperature(41, 82, 85), Nvme, [], null, false));
        try
        {
            await ShowHealth(info);
            Assert.Equal("Good", info.HealthTitle);
            var texts = Texts(info);
            Assert.Contains("No warnings from the drive. 4 % of its rated life used.", texts);
            Assert.Contains("PCIe 4.0 ×4", texts);
            Assert.Contains("41 °C (warning 82 °C, critical 85 °C)", texts);
            Assert.DoesNotContain("slower than it can", texts);
            Capture(info, "drivehealth-nvme.png");
        }
        finally
        {
            info.Close();
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task HardDisk_Watch_EveryAttributeInAListThatScrolls_TheDialogNoBigger()
    {
        var (w, info) = await Open(Ata(8, 2));
        try
        {
            var height = info.Bounds.Height;
            await ShowHealth(info);
            Assert.Equal("Watch this drive", info.HealthTitle);
            var texts = Texts(info);
            Assert.Contains("8 sectors were moved to spare ones and 2 wait to be checked. Keep a copy of what matters on it.", texts);
            Assert.Contains("SATA II (3 Gb/s)", texts);
            Assert.Contains("SATA III (6 Gb/s)", texts);
            Assert.Contains("slower than it can", texts);
            Assert.Contains("All attributes (30)", texts);
            Assert.Contains("Waiting sectors", texts);
            Assert.True(info.HealthScroll.Extent.Height > info.HealthScroll.Viewport.Height);
            Assert.True(info.Bounds.Height <= height + 1);
            Capture(info, "drivehealth-hdd.png");
        }
        finally
        {
            info.Close();
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task Usb_NoData_AdministratorsOnly_AndFailing()
    {
        foreach (var (health, title) in new (DriveHealth?, string)[]
        {
            (new DriveHealth(new DriveLink(LinkKind.Usb), null, null, [], null, false), "No health data"),
            (new DriveHealth(null, new DriveTemperature(36, 0, 0), null, [], null, true), "Windows shows this drive's health only to administrators"),
            (null, "No health data"),
            (Ata(0, 0) with { PredictsFailure = true }, "Replace this drive soon"),
            (new DriveHealth(null, null, Nvme with { CriticalWarning = 1 }, [], null, false), "Replace this drive soon"),
        })
        {
            var (w, info) = await Open(health);
            try
            {
                await ShowHealth(info);
                Assert.Equal(title, info.HealthTitle);
                if (health?.Link?.Kind == LinkKind.Usb)
                {
                    Assert.Contains("The USB adapter doesn't pass the drive's health on.", Texts(info));
                    Capture(info, "drivehealth-usb.png");
                }
                else if (health?.AdminOnly == true)
                {
                    Assert.Contains("36 °C", Texts(info));
                }
            }
            finally
            {
                info.Close();
                w.Close();
            }
        }
    }

    [AvaloniaFact]
    public async Task Dark_ReadsWell()
    {
        var (w, info) = await Open(Ata(8, 2));
        try
        {
            ((App)Avalonia.Application.Current!).Theme.Apply(Core.Settings.AppearanceSettings.Fixed(Core.Settings.ThemeCatalog.SalandualDarkId));
            await ShowHealth(info);
            Capture(info, "drivehealth-dark.png");
        }
        finally
        {
            ((App)Avalonia.Application.Current!).Theme.Apply(Core.Settings.AppearanceSettings.Fixed(Core.Settings.ThemeCatalog.SalandualLightId));
            info.Close();
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheRealDrive_IsReadWithoutAdministratorRights()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.ShellAccess = _shell;
        w.Show();
        var info = new DriveInfoDialog(_dir, Core.Listing.DisplayFormat.Default);
        info.Show(w);
        try
        {
            await info.Reading;
            await ShowHealth(info);
            Assert.NotNull(info.HealthTitle);
            Assert.Equal(DriveHealth.Read(Path.GetPathRoot(_dir)!)?.State ?? HealthState.NoData, info.Health?.State ?? HealthState.NoData);
        }
        finally
        {
            info.Close();
            w.Close();
        }
    }

    [Fact]
    public void Hours_InDaysOrYears()
    {
        Assert.Equal("30 hours", DriveInfoDialog.Hours(30));
        Assert.EndsWith(" hours (96 days)", DriveInfoDialog.Hours(2310));
        Assert.Matches(@"^31\D?400 hours \(3\D6 years\)$", DriveInfoDialog.Hours(31_400));
    }

    private static List<string> Texts(Window w) => [.. w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

    private static void Capture(Window dialog, string name)
    {
        var frame = dialog.CaptureRenderedFrame();
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }
}
