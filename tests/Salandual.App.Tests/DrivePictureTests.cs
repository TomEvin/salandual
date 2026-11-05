// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/DrivePictureTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.Core.Operations;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Tests;

public sealed class DrivePictureTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-drivekind-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Defaults_OnlyAHardDiskWaits_AndTheRing()
    {
        Assert.Equal(SameDiskRule.HardDiskOnly, new OperationSettings().SameDisk);
        Assert.Equal(DriveSpaceLook.Ring, new DriveMenuSettings().DriveInfoLook);
        Assert.Equal(DriveIconLook.Plain, new DriveMenuSettings().DriveListIcon);
    }

    [AvaloniaFact]
    public void EveryKind_IsDrawn_EmptyAndFilling()
    {
        var kinds = Enum.GetValues<DriveMedium>().Where(DrivePicture.CanDraw).ToList();
        Assert.Equal(9, kinds.Count);
        var grid = new WrapPanel { Margin = new Thickness(12), ItemSpacing = 12, LineSpacing = 12 };
        foreach (var kind in kinds)
        {
            grid.Children.Add(new DrivePicture { Medium = kind, Width = 160, Height = 88 });
        }

        foreach (var (kind, used, level) in kinds.Select((k, i) => (k, 0.15 + (i * 0.1), i % 3 == 0 ? SpaceLevel.AlmostFull : i % 3 == 1 ? SpaceLevel.Normal : SpaceLevel.GettingFull)))
        {
            grid.Children.Add(new DrivePicture { Medium = kind, Fill = true, Fraction = used, Level = level, Width = 160, Height = 88, Animate = false });
        }

        var w = new Window { Width = 900, Height = 340, Content = grid };
        w.Res(Window.BackgroundProperty, "Brush.PanelBackground");
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Capture(w, "drivekinds.png");
        Assert.All(grid.Children, c => Assert.True(c.Bounds.Width > 0));
        w.Close();
    }

    [AvaloniaFact]
    public void EveryKind_In3D_EmptyFillingAndBusy()
    {
        var kinds = Enum.GetValues<DriveMedium>().Where(DrivePicture3D.CanDraw).ToList();
        var app = (App)Application.Current!;
        foreach (var dark in new[] { false, true })
        {
            app.Theme.Apply(AppearanceSettings.Fixed(dark ? ThemeCatalog.SalandualDarkId : ThemeCatalog.SalandualLightId));
            var grid = new WrapPanel { Margin = new Thickness(12), ItemSpacing = 12, LineSpacing = 12 };
            foreach (var kind in kinds)
            {
                grid.Children.Add(new DrivePicture3D { Medium = kind, Width = 180, Height = 120, Animate = false });
            }

            foreach (var (kind, i) in kinds.Select((k, i) => (k, i)))
            {
                var level = i % 3 == 0 ? SpaceLevel.AlmostFull : i % 3 == 1 ? SpaceLevel.Normal : SpaceLevel.GettingFull;
                grid.Children.Add(new DrivePicture3D { Medium = kind, Fill = true, Fraction = 0.3 + (i * 0.07), Level = level, Busy = true, Animate = false, Width = 180, Height = 120 });
            }

            var w = new Window { Width = 1000, Height = 560, Content = grid };
            w.Res(Window.BackgroundProperty, "Brush.PanelBackground");
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Capture(w, dark ? "drive3d-dark.png" : "drive3d.png");
            Assert.All(grid.Children, c => Assert.True(c.Bounds.Width > 0));
            Assert.All(grid.Children.OfType<DrivePicture3D>(), p => Assert.False(p.IsMoving));
            w.Close();
        }

        app.Theme.Apply(AppearanceSettings.Fixed(ThemeCatalog.SalandualLightId));
    }

    [AvaloniaFact]
    public void ThePicture3D_Moves_OnlyWhileBusy_OrComingIn()
    {
        var p = new DrivePicture3D { Medium = DriveMedium.Nvme, Fill = true, Fraction = 0.6, Width = 200, Height = 130 };
        var w = new Window { Width = 300, Height = 200, Content = p };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(p.IsMoving);
        Assert.True(p.ShownFraction < 0.6);
        p.Busy = true;
        Assert.True(p.IsMoving);
        p.Animate = false;
        Assert.False(p.IsMoving);
        w.Close();
        Assert.False(p.IsMoving);
    }

    [AvaloniaFact]
    public void TheFlatPicture_Grows_LikeThe3DOne()
    {
        var p = new DrivePicture { Medium = DriveMedium.Ssd, Fill = true, Fraction = 0.6, Width = 200, Height = 110 };
        var w = new Window { Width = 300, Height = 200, Content = p };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(p.IsMoving);
        Assert.True(p.ShownFraction < 0.6);
        p.Animate = false;
        Assert.False(p.IsMoving);
        Assert.Equal(0.6, p.ShownFraction);
        w.Close();
    }

    [AvaloniaFact]
    public void TheDriveList_Icons_AsChosen()
    {
        foreach (var medium in Enum.GetValues<DriveMedium>())
        {
            var details = new DriveDetails("Q:\\", DriveKind.Fixed, true, null, 0, 0, null, medium);
            Assert.IsNotType<DrivePicture>(MainWindow.DriveIcon(false, details, DriveIconLook.Plain));
            Assert.Equal(DrivePicture.CanDraw(medium), MainWindow.DriveIcon(false, details, DriveIconLook.Picture) is DrivePicture { Medium: var m } && m == medium);
            var threeD = MainWindow.DriveIcon(false, details, DriveIconLook.Picture3D) as DrivePicture3D;
            Assert.Equal(DrivePicture3D.CanDraw(medium), threeD is not null);
            Assert.False(threeD?.IsMoving ?? false);
        }

        Assert.IsNotType<DrivePicture>(MainWindow.DriveIcon(false, null, DriveIconLook.Picture));
    }

    [AvaloniaFact]
    public async Task DriveInformation_Picture3D()
    {
        var w = new MainWindow { Width = 900, Height = 600 };
        w.Show();
        try
        {
            var info = new Dialogs.DriveInfoDialog(_dir, Core.Listing.DisplayFormat.Default, null, DriveSpaceLook.Picture3D);
            info.Show(w);
            await info.Reading;
            Dispatcher.UIThread.RunJobs();
            var medium = info.Details!.Medium;
            if (DrivePicture3D.CanDraw(medium))
            {
                Assert.NotNull(info.Picture3D);
                Assert.Null(info.Picture);
                Assert.Null(info.Ring);
                Assert.True(info.Picture3D!.Fill);
            }
            else
            {
                Assert.NotNull(info.Ring);
            }

            await Task.Delay(1200);
            Dispatcher.UIThread.RunJobs();
            CaptureDialog(info, "driveinfo-3d.png");
            info.Close();
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task DriveInformation_Picture_FillsWithTheUsedSpace_WhenWindowsTellsTheKind()
    {
        var w = new MainWindow { Width = 900, Height = 600 };
        w.Show();
        try
        {
            var info = new Dialogs.DriveInfoDialog(_dir, Core.Listing.DisplayFormat.Default, null, DriveSpaceLook.Picture);
            info.Show(w);
            await info.Reading;
            Dispatcher.UIThread.RunJobs();
            var medium = info.Details!.Medium;
            if (DrivePicture.CanDraw(medium))
            {
                Assert.NotNull(info.Picture);
                Assert.Null(info.Ring);
                Assert.True(info.Picture!.Fill);
            }
            else
            {
                Assert.NotNull(info.Ring);
            }

            CaptureDialog(info, "driveinfo-picture.png");
            info.Close();

            var ring = new Dialogs.DriveInfoDialog(_dir, Core.Listing.DisplayFormat.Default);
            ring.Show(w);
            await ring.Reading;
            Assert.NotNull(ring.Ring);
            Assert.Null(ring.Picture);
            ring.Close();
        }
        finally
        {
            w.Close();
        }
    }

    [AvaloniaFact]
    public async Task AWaitingJob_HasStartNow_InThePanel()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        var gate = new ManualResetEventSlim();
        OperationJob Job(string title) => new(OperationKind.Copy, title, "", [@"Q:\"], j =>
        {
            while (!gate.Wait(10))
            {
                j.Checkpoint();
            }
        });
        try
        {
            var queue = w.Operations.Queue;
            queue.SameDisk = SameDiskRule.Wait;
            var a = Job("a");
            var b = Job("b");
            queue.Add(a);
            queue.Add(b);
            await Until(() => a.State == JobState.Running && b.State == JobState.Waiting);
            w.ToggleOperationsPanel();
            await Until(() => w.OpsPanel!.Views.ContainsKey(b));
            Dispatcher.UIThread.RunJobs();
            var view = w.OpsPanel!.Views[b];
            Assert.True(view.StartAnywayLink.IsVisible);
            Assert.False(w.OpsPanel.Views[a].StartAnywayLink.IsVisible);

            view.StartAnywayLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => b.State == JobState.Running);
            Dispatcher.UIThread.RunJobs();
            await Until(() => !view.StartAnywayLink.IsVisible);
            gate.Set();
            await Task.WhenAll(a.Completion, b.Completion);
        }
        finally
        {
            gate.Set();
            w.CloseNow();
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static void Capture(Window w, string name) => CaptureDialog(w, name);

    private static void CaptureDialog(Window dialog, string name)
    {
        var frame = dialog.CaptureRenderedFrame();
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }
}
