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

    internal static bool EnterActs(ConfirmGroup group) => ((Current as App)?.Services.Settings.Current.Confirmations ?? new ConfirmationSettings()).EnterActs(group);


    public ThemeService Theme { get; private set; } = null!;

    public BackgroundImageService BackgroundImage { get; } = new();

    public Panels.IconCache Icons { get; } = new();

    public Panels.ThumbnailCache Thumbnails { get; } = new();

    public Panels.ColumnFacts ColumnFacts { get; } = new();

    public Panels.GitStates Git => _git ??= new Panels.GitStates(
        _services.GitReader,
        () => _services.Settings.Current.Panels.Git.NetworkDrives,
        _services.IsIsolated ? static () => [] : static () => [.. Platform.Windows.FileSystem.Clouds.OnThisPc([]).Select(c => c.Path)]);

    private Panels.GitStates? _git;

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

    private void HandleProblems(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var started = System.Diagnostics.Process.GetCurrentProcess().StartTime;
        var problems = new ProblemHandler(_services.Log, () => AppFacts.Line(_services.InitialRendering),
            () => [.. desktop.Windows.Where(w => w != desktop.MainWindow && !string.IsNullOrEmpty(w.Title)).Select(w => w.Title!)], started);
        _services.Problems = problems;
        var asking = false;
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            if (problems.Crashed)
            {
                return;
            }

            if (problems.Handle(e.Exception) == Core.Diagnostics.ProblemKind.Crash)
            {
                Environment.Exit(1);
            }

            if (!asking && desktop.MainWindow is MainWindow main && main.IsVisible)
            {
                asking = true;
                var report = problems.LastReport;
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        await main.AskAfterProblemAsync(e.Exception, report);
                    }
                    finally
                    {
                        asking = false;
                    }
                });
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                problems.HandleFatal(ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            problems.HandleUnobserved(e.Exception);
            e.SetObserved();
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StartupTrace.Mark("framework-ready");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(_services);
            desktop.ShutdownRequested += (_, _) =>
            {
                _services.Sharing.Flush();
                _services.Settings.Flush();
            };
            if (!_services.IsIsolated)
            {
                HandleProblems(desktop);
                desktop.Exit += (_, _) => _services.Log.Write(Core.Diagnostics.LogLevel.Info, Salandual.App.Resources.AppStrings.Log_Closed);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
