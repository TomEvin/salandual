// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Sharing/SharedSettingsService.cs

using System.Text.Json.Nodes;
using Avalonia.Threading;
using Salandual.Core;
using Salandual.Core.Settings;

namespace Salandual.App.Sharing;

internal sealed record SharingTaken(string Pc, SharingMerge Merge, AppSettings Before);

internal sealed record SharingPending(string Pc, SharingMerge Merge, SharedCopy Copy, AppSettings Mine);

internal sealed class SharedSettingsService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly string _dataDirectory;
    private readonly DispatcherTimer _writeSoon = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _readSoon = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private FileSystemWatcher? _watcher;
    private string? _watching;
    private bool _applying;
    private DateTime? _lastLocalChangeUtc;
    private string? _problem;

    public SharedSettingsService(SettingsService settings, string dataDirectory)
    {
        _settings = settings;
        _dataDirectory = dataDirectory;
        _writeSoon.Tick += (_, _) =>
        {
            _writeSoon.Stop();
            WriteNow();
        };
        _readSoon.Tick += (_, _) =>
        {
            _readSoon.Stop();
            Check();
        };
        _settings.Changed += OnSettingsChanged;
    }

    public event EventHandler<SharingTaken>? Taken;

    public event EventHandler<SharingPending>? Asking;

    public event EventHandler<string>? Problem;

    public SharingPending? Pending { get; private set; }

    public (string Pc, DateTime When, IReadOnlyList<SettingChange> Changes)? LastTaken { get; private set; }

    public static string Pc => Environment.MachineName;

    private SharingSettings Sharing => _settings.Current.Sharing;

    private IReadOnlySet<SettingsPart> Parts => Sharing.Parts.ToHashSet();

    public string? FilePath => Sharing.IsOn ? Path.Combine(Sharing.Folder, SettingsSharing.FileName) : null;

    public void Start()
    {
        Watch();
        Check();
    }

    public static (SharedCopy Copy, SettingsImport Import)? Peek(string folder, AppSettings mine, IReadOnlySet<SettingsPart> parts)
    {
        var path = Path.Combine(folder, SettingsSharing.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var copy = SettingsSharing.Read(File.ReadAllText(path));
        var merge = SettingsSharing.Merge(mine, null, copy, parts, null);
        return (copy, SettingsSharing.AsImport(mine, merge, path, copy));
    }

    public void TurnOn(SharingSettings sharing, SharedCopy? read)
    {
        Pending = null;
        _settings.Update(s => s with { Sharing = sharing });
        SettingsSharing.SaveBase(_dataDirectory, read?.Settings);
        _lastLocalChangeUtc = DateTime.UtcNow;
        Watch();
        WriteNow();
    }

    public void Change(SharingSettings sharing)
    {
        _settings.Update(s => s with { Sharing = sharing });
        _lastLocalChangeUtc = DateTime.UtcNow;
        WriteNow();
    }

    public void TurnOff()
    {
        Pending = null;
        _settings.Update(s => s with { Sharing = s.Sharing with { Folder = "" } });
        SettingsSharing.SaveBase(_dataDirectory, null);
        Watch();
    }

    public void CheckSoon()
    {
        if (Sharing.IsOn)
        {
            _readSoon.Stop();
            _readSoon.Start();
        }
    }

    public void Check()
    {
        if (FilePath is not { } path)
        {
            return;
        }

        string text;
        try
        {
            if (!File.Exists(path))
            {
                WriteNow();
                return;
            }

            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        SharedCopy copy;
        try
        {
            copy = SettingsSharing.Read(text);
        }
        catch (InvalidDataException ex)
        {
            Report(ex.Message);
            return;
        }

        _problem = null;
        var lastRead = SettingsSharing.LoadBase(_dataDirectory);
        if (SettingsSharing.Same(copy.Settings, lastRead))
        {
            WriteIfMine(lastRead, copy);
            return;
        }

        var mine = _settings.Current;
        var merge = SettingsSharing.Merge(mine, lastRead, copy, Parts, _lastLocalChangeUtc);
        if (!merge.HasChanges)
        {
            SettingsSharing.SaveBase(_dataDirectory, copy.Settings);
            WriteIfMine(copy.Settings, copy);
            return;
        }

        if (Sharing.AskFirst)
        {
            var asked = Pending is { } before && before.Merge.Changes.SequenceEqual(merge.Changes);
            Pending = new SharingPending(copy.Pc, merge, copy, mine);
            if (!asked)
            {
                Asking?.Invoke(this, Pending);
            }

            WriteIfMine(lastRead, copy);
            return;
        }

        Take(merge, copy, mine);
    }

    public SharingTaken? TakePending(AppSettings? chosen = null)
    {
        if (Pending is not { } pending)
        {
            return null;
        }

        Pending = null;
        var mine = _settings.Current;
        var merge = SettingsSharing.Merge(mine, SettingsSharing.LoadBase(_dataDirectory), pending.Copy, Parts, _lastLocalChangeUtc);
        return Take(merge, pending.Copy, mine, chosen);
    }

    public void Undo(SharingTaken taken) => _settings.Update(s => SettingsSharing.Restore(s, taken.Before, taken.Merge.Paths));

    public void Flush()
    {
        if (_writeSoon.IsEnabled)
        {
            _writeSoon.Stop();
            WriteNow();
        }
    }

    private SharingTaken Take(SharingMerge merge, SharedCopy copy, AppSettings mine, AppSettings? chosen = null)
    {
        _applying = true;
        try
        {
            _settings.Update(s => chosen ?? SettingsSharing.Restore(s, merge.Local, merge.Paths));
        }
        finally
        {
            _applying = false;
        }

        SettingsSharing.SaveBase(_dataDirectory, copy.Settings);
        WriteIfMine(copy.Settings, copy);
        var taken = new SharingTaken(copy.Pc, merge, mine);
        LastTaken = (copy.Pc, DateTime.Now, merge.Changes);
        Taken?.Invoke(this, taken);
        return taken;
    }

    private void WriteIfMine(JsonObject? lastRead, SharedCopy copy)
    {
        var after = SettingsSharing.FileAfter(_settings.Current, lastRead, copy, Parts);
        if (!SettingsSharing.Same(after, copy.Settings))
        {
            Write(after, saveBase: Pending is null);
        }
    }

    private void WriteNow()
    {
        if (FilePath is not { } path)
        {
            return;
        }

        var lastRead = SettingsSharing.LoadBase(_dataDirectory);
        SharedCopy? copy = null;
        try
        {
            copy = File.Exists(path) ? SettingsSharing.Read(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
        }

        if (copy is not null && lastRead is not null && !SettingsSharing.Same(copy.Settings, lastRead) && Pending is null)
        {
            Check();
            return;
        }

        Write(SettingsSharing.FileAfter(_settings.Current, copy is null ? null : lastRead, copy, Parts), saveBase: Pending is null);
    }

    private void Write(JsonObject shared, bool saveBase)
    {
        if (FilePath is not { } path)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Sharing.Folder);
            var temp = path + ".writing";
            File.WriteAllText(temp, SettingsSharing.Write(shared, Pc, DateTime.UtcNow, ProductInfo.DisplayName + " " + ProductInfo.DisplayVersion));
            File.Move(temp, path, overwrite: true);
            if (saveBase)
            {
                SettingsSharing.SaveBase(_dataDirectory, SettingsSharing.Read(File.ReadAllText(path)).Settings);
            }

            _problem = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Report(ex.Message);
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Old.Sharing.Folder != e.New.Sharing.Folder)
        {
            Dispatcher.UIThread.Post(Watch);
        }

        if (_applying || !e.New.Sharing.IsOn)
        {
            return;
        }

        var parts = e.New.Sharing.Parts.ToHashSet();
        if (!SettingsSharing.Same(SettingsSharing.Only(e.Old, parts), SettingsSharing.Only(e.New, parts)))
        {
            _lastLocalChangeUtc = DateTime.UtcNow;
            Dispatcher.UIThread.Post(() =>
            {
                _writeSoon.Stop();
                _writeSoon.Start();
            });
        }
    }

    private void Watch()
    {
        var folder = Sharing.IsOn ? Sharing.Folder : null;
        if (folder == _watching)
        {
            return;
        }

        _watcher?.Dispose();
        _watcher = null;
        _watching = folder;
        if (folder is null || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(folder, SettingsSharing.FileName) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            _watcher.Changed += (_, _) => Dispatcher.UIThread.Post(CheckSoon);
            _watcher.Created += (_, _) => Dispatcher.UIThread.Post(CheckSoon);
            _watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(CheckSoon);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _watcher = null;
        }
    }

    private void Report(string message)
    {
        if (_problem != message)
        {
            _problem = message;
            Problem?.Invoke(this, message);
        }
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _writeSoon.Stop();
        _readSoon.Stop();
        _watcher?.Dispose();
    }
}
