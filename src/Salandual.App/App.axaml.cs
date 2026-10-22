// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/App.axaml.cs

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Salandual.App.Diagnostics;
using Salandual.App.Theme;
using Salandual.Core.Settings;

namespace Salandual.App;

public partial class App : Application
{
    private readonly AppServices _services;

    public App()
        : this(AppServices.CreateIsolated())
    {
    }

    public App(AppServices services) => _services = services;

    public AppServices Services => _services;

    internal static Core.Settings.TempFolder Temp => ((App)Current!).Services.Temp;


    public ThemeService Theme { get; private set; } = null!;

    public BackgroundImageService BackgroundImage { get; } = new();

    public Panels.IconCache Icons { get; } = new();

    public override void Initialize()
    {
        StartupTrace.Mark("avalonia-platform");
        AvaloniaXamlLoader.Load(this);

        Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Avalonia.Controls.Window>())
        {
            Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Window.IconProperty, new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri(AppBuild.IconUri)))) },
        });
        StartupTrace.Mark("app-xaml");
        Theme = new ThemeService(this, _services.Settings);
        StartupTrace.Mark("settings-ready");
        Theme.Start();
        StartBackgroundImage();
        StartupTrace.Mark("theme");
    }

    private void StartBackgroundImage()
    {
        void Apply()
        {
            var theme = Theme.ActiveTheme;
            BackgroundImage.Apply(theme.Background with { Path = ThemeFiles.ImagePath(_services.DataDirectory, theme) });
        }

        Apply();
        Theme.Applied += (_, _) => Apply();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StartupTrace.Mark("framework-ready");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(_services);
            desktop.ShutdownRequested += (_, _) => _services.Settings.Flush();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
