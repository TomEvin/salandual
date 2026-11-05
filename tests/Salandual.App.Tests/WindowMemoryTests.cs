// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/WindowMemoryTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class WindowMemoryTests : IDisposable
{
    private static SettingsService Settings => ((App)Application.Current!).Services.Settings;

    public WindowMemoryTests() => Settings.Update(_ => new AppSettings());

    public void Dispose()
    {
        Settings.Update(_ => new AppSettings());
        Dispatcher.UIThread.RunJobs();
    }

    private static WindowPlace? Kept(SettingsService settings, string window) => settings.Current.Windows.For(window);

    [AvaloniaFact]
    public void Options_OpensAtItsSize_AndKeepsTheNewOne()
    {
        var first = new OptionsWindow();
        Assert.Equal((1000, 740), (first.Width, first.Height));
        first.Show();
        Dispatcher.UIThread.RunJobs();
        first.Width = 1180;
        first.Height = 820;
        Dispatcher.UIThread.RunJobs();
        first.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new WindowPlace { Width = 1180, Height = 820 }, Kept(Settings, WindowSettings.Options));

        var again = new OptionsWindow();
        Assert.Equal((1180, 820), (again.Width, again.Height));
        Assert.Equal(WindowState.Normal, again.WindowState);
        Assert.Equal(WindowStartupLocation.CenterOwner, again.WindowStartupLocation);
        again.Show();
        again.WindowState = WindowState.Maximized;
        Dispatcher.UIThread.RunJobs();
        again.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new WindowPlace { Width = 1180, Height = 820, Maximized = true }, Kept(Settings, WindowSettings.Options));

        var maximized = new OptionsWindow();
        Assert.Equal(WindowState.Maximized, maximized.WindowState);
        Assert.Equal((1180, 820), (maximized.Width, maximized.Height));
        maximized.Close();
    }

    [AvaloniaFact]
    public void AWindowSmallerThanItsMinimum_OpensAtTheMinimum()
    {
        Settings.Update(s => s with { Windows = s.Windows.With(WindowSettings.Options, new WindowPlace { Width = 300, Height = 200 }) });
        var w = new OptionsWindow();
        Assert.Equal((w.MinWidth, w.MinHeight), (w.Width, w.Height));
        w.Close();
    }

    [AvaloniaFact]
    public void TheMainWindow_KeepsItsPlace_AndComesBackOnScreen_WhenItsMonitorIsGone()
    {
        using var services = AppServices.CreateIsolated();
        var main = new MainWindow(services);
        Assert.Equal(WindowStartupLocation.CenterScreen, main.WindowStartupLocation);
        main.Show();
        Dispatcher.UIThread.RunJobs();
        main.Position = new PixelPoint(40, 30);
        main.Width = 1100;
        main.Height = 700;
        Dispatcher.UIThread.RunJobs();
        main.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new WindowPlace { Width = 1100, Height = 700, X = 40, Y = 30 }, Kept(services.Settings, WindowSettings.Main));

        var again = new MainWindow(services);
        Assert.Equal(WindowStartupLocation.Manual, again.WindowStartupLocation);
        Assert.Equal(new PixelPoint(40, 30), again.Position);
        Assert.Equal((1100, 700), (again.Width, again.Height));
        again.Close();

        services.Settings.Update(s => s with { Windows = s.Windows.With(WindowSettings.Main, new WindowPlace { Width = 1100, Height = 700, X = 90_000, Y = 30, Maximized = true }) });
        var gone = new MainWindow(services);
        Assert.Equal(WindowStartupLocation.CenterScreen, gone.WindowStartupLocation);
        Assert.Equal(WindowState.Maximized, gone.WindowState);
        gone.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
