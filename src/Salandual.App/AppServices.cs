// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/AppServices.cs

using Salandual.Core;
using Salandual.Core.Commands;
using Salandual.Core.Diagnostics;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App;

public sealed class AppServices(string dataDirectory, RenderingMode initialRendering, Task<SettingsService> settings, Task<CommandRegistry> commands) : IDisposable
{
    public LocalFileSystemProvider Files { get; } = new();

    private Archives.ArchiveFileSystem? _archives;

    public Archives.ArchiveFileSystem Archives => _archives ??= new(Files, () => Temp.Own);

    private PlacesFileSystem? _panelFiles;

    public PlacesFileSystem PanelFiles => _panelFiles ??= new(Servers, Places);

    private Remote.RemoteFileSystem? _servers;

    public Remote.RemoteFileSystem Servers => _servers ??= new(Archives, Connector, Secrets, TrustedServers);

    public Remote.IServerConnector Connector { get; private init; } = new Remote.MemoryServers();

    public ISecretStore Secrets { get; private init; } = new MemorySecretStore();

    public Remote.TrustedServers TrustedServers { get; private init; } = Remote.TrustedServers.InMemory();

    public IShellPlaces Places { get; private init; } = new MemoryShellPlaces();

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
        var listing = ShellPlace.IsPlace(path) || ShellPlace.IsServer(path) ? PanelFiles.ListAsync(path).GetAwaiter().GetResult() : Files.List(path);
        EntrySorter.ForCurrentCulture(numericSort: true).Sort((FileEntry[])listing.Entries.Clone(), SortSpec.Default);
        _ = DateTime.UtcNow.ToLocalTime();
        return listing;
    }

    public string DataDirectory { get; } = dataDirectory;

    public LanguageChoice Language { get; private init; } = LanguageChoice.English;

    public IReadOnlyList<string> LanguageFolders { get; internal init; } = LanguageFiles.Folders(dataDirectory, AppContext.BaseDirectory);

    public string OwnLanguageFolder => LanguageFolders[^1];

    internal static IReadOnlyList<System.Resources.ResourceManager> Originals => [TextLanguage.Original(Strings.ResourceManager), TextLanguage.Original(Resources.AppStrings.ResourceManager)];

    internal static LanguageChoice UseLanguage(string id, IReadOnlyList<string> folders)
    {
        var choice = TextLanguage.Choose(id, folders, Originals);
        SwitchLanguage(choice);
        return choice;
    }

    internal static void SwitchLanguage(LanguageChoice choice)
    {
        var originals = Originals;
        TextLanguage.Install(typeof(Strings), originals[0], choice);
        TextLanguage.Install(typeof(Resources.AppStrings), originals[1], choice);
    }

    private Panels.FolderLooks? _folderLooks;

    public Panels.FolderLooks FolderLooks => _folderLooks ??= new(DataDirectory);

    private Sharing.SharedSettingsService? _sharing;

    internal Sharing.SharedSettingsService Sharing => _sharing ??= new(Settings, DataDirectory);

    public TempFolder Temp { get; private init; } = new(Path.Combine(Path.GetTempPath(), ProductInfo.Identifier));

    public RenderingMode InitialRendering { get; } = initialRendering;

    public SettingsService Settings => settings.Result;

    public CommandRegistry Commands => commands.Result;

    public static AppServices CreateDefault(StartOptions? options = null, string? dataDirectory = null)
    {
        dataDirectory ??= DefaultDataDirectory();
        Diagnostics.StartupTrace.Mark("data-dir");
        var peek = SettingsStore.InDirectory(dataDirectory).Peek();
        var language = UseLanguage(peek.Language, LanguageFiles.Folders(dataDirectory, AppContext.BaseDirectory));
        Diagnostics.StartupTrace.Mark("language");
        var settings = Task.Run(() =>
        {
            var service = SettingsService.Open(dataDirectory);
            Diagnostics.StartupTrace.Mark("settings-loaded");
            return service;
        });
        var commands = Task.Run(CommandRegistry.CreateDefault);
        var services = new AppServices(dataDirectory, peek.Rendering, settings, commands)
        {
            Language = language,
            LanguageFolders = LanguageFiles.Folders(dataDirectory, AppContext.BaseDirectory),
            StartWithWindows = new Platform.Windows.Shell.RunAtSignIn(),
            Salamander = new Platform.Windows.Shell.SalamanderRegistry(),
            Starter = new Platform.Windows.Shell.WindowsProgramStarter(),
            Shells = new Platform.Windows.Shell.CommandShells(),
            DiskActivitySource = new DiskActivityCounters(),
            GitCommands = new Git.GitProgram(),
            RecycleFile = RecycleForReal,
            Places = new Platform.Windows.Shell.WindowsShellPlaces(),
            Connector = new Remote.ServerConnector(),
            Secrets = new Platform.Windows.Shell.CredentialStore(),
            TrustedServers = new Remote.TrustedServers(Path.Combine(dataDirectory, Remote.TrustedServers.FileName), Remote.TrustedServers.UserKnownHosts),
            FileNames = new Core.QuickOpen.FileNameSources(new EverythingIndex(), new WindowsSearchIndex(), Drives.LocalWalkable),
            Log = new AppLog(Path.Combine(dataDirectory, LogsFolderName)),
        };
        AppLog.Current = services.Log;

        var sessionStore = SessionStore.InDirectory(dataDirectory);
        var session = sessionStore.Load();
        Diagnostics.StartupTrace.Mark("session");
        services.SessionStore = sessionStore;
        services.Session = session;
        var (startLeft, startRight) = GeneralSettings.FoldersAtStart(peek.StartFolders, peek.StartLeft, peek.StartRight, session.Left, session.Right, DefaultPaths, DefaultPaths.Right);
        options ??= StartOptions.None;
        var (left, right) = options.Resolve(startLeft, startRight);
        services.InitialPaths = (StartFolder(left, LocalTab(session, 0) ?? DefaultPaths.Left), StartFolder(right, LocalTab(session, 1) ?? DefaultPaths.Right));
        services.InitialActive = options.Panel != 0 ? options.ActiveIndex : session.Active;

        services.StartPreloading();
        return services;
    }

    private static string StartFolder(string path, string instead) => Remote.ServerAddress.IsServerPath(path) ? instead : path;

    internal static string? LocalTab(SessionState session, int side)
        => session.Tabs.Where(t => t.Side == side).Select(t => t.Path).FirstOrDefault(p => p.Length > 0 && !Remote.ServerAddress.IsServerPath(p));

    public static string DefaultDataDirectory() => ProductInfo.GetDataDirectory(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    internal void UseSession(SessionState session, (string Left, string Right) paths)
    {
        Session = session;
        InitialPaths = paths;
    }

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

    public const string LogsFolderName = "Logs";

    public AppLog Log { get; private init; } = new(null);

    internal Diagnostics.ProblemHandler? Problems { get; set; }

    public bool IsIsolated { get; private init; }

    public IStartWithWindows StartWithWindows { get; private init; } = new MemoryStartWithWindows();

    public Core.Settings.Salamander.ISalamanderSettings Salamander { get; private init; } = new Core.Settings.Salamander.MemorySalamanderSettings();

    public Core.UserMenu.IProgramStarter Starter { get; private init; } = new Core.UserMenu.MemoryProgramStarter();

    public Core.CommandLine.ICommandShells Shells { get; private init; } = new Core.CommandLine.MemoryCommandShells();

    public IDiskActivitySource DiskActivitySource { get; private init; } = new MemoryDiskActivitySource();

    public Core.Git.IGitReader GitReader { get; private init; } = new Git.LibGit2Reader();

    private Func<string, (bool Done, string? Why)>? _recycle;

    public Func<string, (bool Done, string? Why)> RecycleFile { get => _recycle ?? RecycleInTests; internal set => _recycle = value; }

    internal List<string> RecycledInTests { get; } = [];

    private (bool, string?) RecycleInTests(string path)
    {
        lock (RecycledInTests)
        {
            RecycledInTests.Add(path);
        }

        File.Delete(path);
        return (true, null);
    }

    private static (bool, string?) RecycleForReal(string path)
    {
        var files = new LocalOperations();
        if (!files.CanRecycle(path, out var reason))
        {
            return (false, reason);
        }

        return files.Recycle(path) == Core.Operations.RecycleResult.Kept ? (false, null) : (true, null);
    }

    public Core.Git.IGitCommands GitCommands { get; internal set; } = new Core.Git.MemoryGitCommands();

    public Core.QuickOpen.FileNameSources FileNames { get; internal set; } = Core.QuickOpen.FileNameSources.None;

    public string ProgramPath { get; private init; } = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "salandual.exe");

    public void Dispose()
    {
        _sharing?.Dispose();
        _servers?.DisconnectAllAsync().Wait(TimeSpan.FromSeconds(2));
        Settings.Dispose();
        _archives?.Forget();
        Temp.Dispose();
    }
}
