// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Vfs/PlacesFileSystem.cs

using System.Collections.Concurrent;
using System.Diagnostics;
using Salandual.Core.Resources;

namespace Salandual.Core.Vfs;

public sealed class PlacesFileSystem(IFileSystemProvider inner, IShellPlaces places) : IFileSystemProvider
{
    private readonly ConcurrentDictionary<string, Dictionary<string, PlaceItem>> _listed = new(StringComparer.OrdinalIgnoreCase);

    public IFileSystemProvider Inner => inner;

    public IShellPlaces Places => places;

    public ProviderCapabilities Capabilities => inner.Capabilities;

    public Func<string, Task<bool>>? SignIn { get; set; }

    public Func<IReadOnlyList<string>>? OpenedBefore { get; set; }

    public bool ServersGoUpToNetwork { get; set; } = true;

    public async Task<DirectoryListing> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ListOnceAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && AsksForPassword(path, ex) && SignIn is { } signIn)
        {
            var share = ShellPlace.ShareOf(path) ?? ShellPlace.ServerOf(path)!;
            if (!await signIn(share).ConfigureAwait(false))
            {
                throw;
            }

            return await ListOnceAsync(path, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static bool AsksForPassword(string path, Exception ex)
    {
        if (ShellPlace.ServerOf(path) is null)
        {
            return false;
        }

        var code = ex.HResult & 0xFFFF;
        var top = ShellPlace.IsServer(path) || ShellPlace.IsShareRoot(path);
        return ex is UnauthorizedAccessException ? top : code is LogonFailure or InvalidPassword or NotAuthenticated || (top && code == AccessDenied);
    }

    private const int AccessDenied = 5, InvalidPassword = 86, NotAuthenticated = 1244, LogonFailure = 1326;

    private async Task<DirectoryListing> ListOnceAsync(string path, CancellationToken cancellationToken)
    {
        if (!ShellPlace.IsPlace(path) && !ShellPlace.IsServer(path))
        {
            return await inner.ListAsync(path, cancellationToken).ConfigureAwait(false);
        }

        if (ShellPlace.Is(path, ShellPlace.RecycleBin) && ShellPlace.Inner(path).Length > 0)
        {
            throw new IOException(Strings.Place_RestoreFirst);
        }

        var started = Stopwatch.GetTimestamp();
        var items = await places.ListAsync(path, cancellationToken).ConfigureAwait(false);
        if (ShellPlace.Is(path, ShellPlace.Network) && ShellPlace.Inner(path).Length == 0)
        {
            items = WithOpenedBefore(items);
        }

        var byName = new Dictionary<string, PlaceItem>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<FileEntry>(items.Count);
        foreach (var item in items)
        {
            var name = item.Entry.Name;
            for (var n = 2; byName.ContainsKey(name); n++)
            {
                name = Numbered(item.Entry.Name, n, item.Entry.IsDirectory);
            }

            var entry = item.Entry with { Name = name };
            byName[name] = item with { Entry = entry };
            entries.Add(entry);
        }

        _listed[path] = byName;
        return new DirectoryListing(path, [.. entries], Stopwatch.GetElapsedTime(started)) { KeepOrder = ShellPlace.KeepsOrder(path) };
    }

    private List<PlaceItem> WithOpenedBefore(IReadOnlyList<PlaceItem> found)
    {
        var items = found.ToList();
        var known = new HashSet<string>(found.Select(i => i.Target.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
        foreach (var folder in OpenedBefore?.Invoke() ?? [])
        {
            if (ShellPlace.ServerOf(folder) is { } server && known.Add(server))
            {
                items.Add(new PlaceItem(new FileEntry(server[2..], 0, 0, FileAttributes.Directory), server) { IsComputer = true, Comment = Strings.Place_OpenedBefore });
            }
        }

        return items;
    }

    public IReadOnlyCollection<PlaceItem>? ListedItems(string place) => _listed.TryGetValue(place, out var items) ? items.Values : null;

    public PlaceItem? Item(string place, string name)
        => _listed.TryGetValue(place, out var items) && items.TryGetValue(name, out var item) ? item : null;

    public string? NameOf(string place, string target)
        => _listed.TryGetValue(place, out var items)
            ? items.Values.FirstOrDefault(i => string.Equals(i.Target.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))?.Entry.Name
            : null;

    public string? GetParent(string path)
    {
        if (ShellPlace.IsPlace(path))
        {
            return ShellPlace.Inner(path).Length > 0 ? ShellPlace.RootOf(path) : null;
        }

        if (ShellPlace.IsServer(path))
        {
            return ServersGoUpToNetwork ? ShellPlace.Network : null;
        }

        return ShellPlace.IsShareRoot(path) ? ShellPlace.ServerOf(path) : inner.GetParent(path);
    }

    public string? GetRoot(string path) => ShellPlace.IsPlace(path) ? ShellPlace.RootOf(path) : ShellPlace.IsServer(path) ? path.TrimEnd('\\') : inner.GetRoot(path);

    public string GetName(string path)
    {
        if (ShellPlace.IsPlace(path))
        {
            return ShellPlace.ShortName(path);
        }

        if (ShellPlace.IsServer(path) || ShellPlace.IsShareRoot(path))
        {
            var trimmed = path.TrimEnd('\\');
            return trimmed[(trimmed.LastIndexOf('\\') + 1)..];
        }

        return inner.GetName(path);
    }

    public string Combine(string directory, string name)
    {
        if (ShellPlace.IsServer(directory))
        {
            return directory.TrimEnd('\\') + "\\" + name;
        }

        if (!ShellPlace.IsPlace(directory))
        {
            return inner.Combine(directory, name);
        }

        return !ShellPlace.Is(directory, ShellPlace.RecycleBin) && Item(directory, name) is { } item ? item.Target : directory.TrimEnd('\\') + "\\" + name;
    }

    public IDisposable Watch(string path, Action<DirectoryChange> onChange, Action<Exception> onError)
        => ShellPlace.IsPlace(path) || ShellPlace.IsServer(path) ? new Nothing() : inner.Watch(path, onChange, onError);

    public IDisposable? WatchTree(string path, Action<IReadOnlyCollection<string>?> onChange, Action<Exception> onError)
        => ShellPlace.IsPlace(path) || ShellPlace.IsServer(path) ? null : inner.WatchTree(path, onChange, onError);

    private static string Numbered(string name, int n, bool folder)
    {
        var dot = folder ? -1 : name.LastIndexOf('.');
        var number = " (" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        return dot > 0 ? name[..dot] + number + name[dot..] : name + number;
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
