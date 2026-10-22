// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/AppServices.cs

using Salandual.Core;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App;

public sealed class AppServices(string dataDirectory, RenderingMode initialRendering, Task<SettingsService> settings, Task<CommandRegistry> commands) : IDisposable
{
    public LocalFileSystemProvider Files { get; } = new();

    public (string Left, string Right) InitialPaths { get; private set; } = DefaultPaths;

    public int InitialActive { get; private set; }

    public SessionState Session { get; private set; } = SessionState.Empty;

    public SessionStore? SessionStore { get; private set; }

    internal static (string Left, string Right) DefaultPaths => (
        Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\",
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public (Task<DirectoryListing> Left, Task<DirectoryListing> Right) Preloaded { get; private set; }

    public void StartPreloading()
    {
        var (left, right) = InitialPaths;
        Preloaded = (Task.Run(() => ListAndWarm(left)), Task.Run(() => ListAndWarm(right)));
    }

    private DirectoryListing ListAndWarm(string path)
    {
        var listing = Files.List(path);
        EntrySorter.ForCurrentCulture(numericSort: true).Sort((FileEntry[])listing.Entries.Clone(), SortSpec.Default);
        _ = DateTime.UtcNow.ToLocalTime();
        return listing;
    }

    public string DataDirectory { get; } = dataDirectory;

    public TempFolder Temp { get; private init; } = new(Path.Combine(Path.GetTempPath(), ProductInfo.Identifier));

    public RenderingMode InitialRendering { get; } = initialRendering;

    public SettingsService Settings => settings.Result;

    public CommandRegistry Commands => commands.Result;

    public static AppServices CreateDefault(StartOptions? options = null, string? dataDirectory = null)
    {
        dataDirectory ??= DefaultDataDirectory();
        Diagnostics.StartupTrace.Mark("data-dir");
        var peek = SettingsStore.InDirectory(dataDirectory).Peek();
        var settings = Task.Run(() =>
        {
            var service = SettingsService.Open(dataDirectory);
            Diagnostics.StartupTrace.Mark("settings-loaded");
            return service;
        });
        var commands = Task.Run(CommandRegistry.CreateDefault);
        var services = new AppServices(dataDirectory, peek.Rendering, settings, commands) { StartWithWindows = new Platform.Windows.Shell.RunAtSignIn() };

        var sessionStore = SessionStore.InDirectory(dataDirectory);
        var session = sessionStore.Load();
        Diagnostics.StartupTrace.Mark("session");
        services.SessionStore = sessionStore;
        services.Session = session;
        var (startLeft, startRight) = GeneralSettings.FoldersAtStart(peek.StartFolders, peek.StartLeft, peek.StartRight, session.Left, session.Right, DefaultPaths, DefaultPaths.Right);
        options ??= StartOptions.None;
        services.InitialPaths = options.Resolve(startLeft, startRight);
        services.InitialActive = options.Panel != 0 ? options.ActiveIndex : session.Active;

        services.StartPreloading();
        return services;
    }

    public static string DefaultDataDirectory() => ProductInfo.GetDataDirectory(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static AppServices CreateIsolated(SettingsLoadResult? loaded = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "salandual-isolated-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var settings = new SettingsService(SettingsStore.InDirectory(dir), loaded ?? new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.NotFound));

        return new AppServices(dir, RenderingMode.Gpu, Task.FromResult(settings), Task.FromResult(CommandRegistry.CreateDefault()))
        {
            IsIsolated = true,
            Temp = new TempFolder(Path.Combine(dir, "temp")),
            ProgramPath = Path.Combine(dir, "salandual.exe"),
        };

    }

    public bool IsIsolated { get; private init; }

    public IStartWithWindows StartWithWindows { get; private init; } = new MemoryStartWithWindows();

    public string ProgramPath { get; private init; } = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "salandual.exe");

    public void Dispose()
    {
        Settings.Dispose();
        Temp.Dispose();
    }
}
