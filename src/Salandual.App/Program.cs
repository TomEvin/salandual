// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Program.cs

using Avalonia;
using Salandual.App.Diagnostics;
using Salandual.Core.Settings;

namespace Salandual.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        StartupTrace.Mark("main");
        var dataDirectory = AppServices.DefaultDataDirectory();

        if (Platform.Windows.Shell.SingleInstance.TryHandOff(Platform.Windows.Shell.SingleInstance.NameFor(dataDirectory), Environment.CurrentDirectory, args))
        {
            return 0;
        }

        StartupTrace.Mark("single-instance");
        StartupWarmup.Start();
        using var services = AppServices.CreateDefault(Core.StartOptions.Parse(args, Environment.CurrentDirectory), dataDirectory);
        StartupTrace.Mark("services");
        return BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(AppServices.CreateIsolated());

    internal static AppBuilder BuildAvaloniaApp(AppServices services)
    {
        var gpu = services.InitialRendering == RenderingMode.Gpu;
        return AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = gpu ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software] : [Win32RenderingMode.Software],
                CompositionMode = [Win32CompositionMode.DirectComposition, Win32CompositionMode.RedirectionSurface],
            })
            .AfterPlatformServicesSetup(_ => StartupTrace.Mark("platform-services"))
            .AfterSetup(_ => StartupTrace.Mark("platform-setup"))
            .LogToTrace();
    }
}
