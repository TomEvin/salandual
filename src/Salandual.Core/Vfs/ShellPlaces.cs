// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Vfs/ShellPlaces.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Vfs;

public static class ShellPlace
{
    public const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

    public const string Home = "::{679F85CB-0220-4080-B29B-5540CC05AAB6}";

    public const string Libraries = "::{031E4825-7B94-4DC3-B131-E946B44C8DD5}";

    public const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    public const string Network = "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";

    public const string Shares = "::{Salandual.SharedFolders}";

    public static IReadOnlyList<string> All { get; } = [ThisPc, Home, Libraries, RecycleBin, Network, Shares];

    public static bool IsPlace(string? path) => path is not null && path.StartsWith("::{", StringComparison.Ordinal);

    public static string? RootOf(string path)
    {
        if (!IsPlace(path))
        {
            return null;
        }

        var cut = path.IndexOf('\\');
        var root = cut < 0 ? path : path[..cut];
        return All.FirstOrDefault(p => string.Equals(p, root, StringComparison.OrdinalIgnoreCase)) ?? root;
    }

    public static string Inner(string path)
    {
        var cut = path.IndexOf('\\');
        return cut < 0 ? "" : path[(cut + 1)..].TrimEnd('\\');
    }

    public static bool Is(string path, string place) => string.Equals(RootOf(path), place, StringComparison.Ordinal);

    public static bool IsServer(string? path)
    {
        if (path is null || !path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        var name = path.AsSpan(2).TrimEnd('\\');
        return name.Length > 0 && name.IndexOf('\\') < 0 && name is not "?" and not "." && name.IndexOfAny('/', ':', '*') < 0;
    }

    public static string? ServerOf(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return null;
        }

        var cut = path.IndexOf('\\', 2);
        var server = cut < 0 ? path.TrimEnd('\\') : path[..cut];
        return server.Length > 2 ? server : null;
    }

    public static bool IsShareRoot(string path)
        => ServerOf(path) is { } server && path.TrimEnd('\\') is var p && p.Length > server.Length + 1 && p.IndexOf('\\', server.Length + 1) < 0;

    public static string? ShareOf(string path)
    {
        if (ServerOf(path) is not { } server || path.Length <= server.Length + 1)
        {
            return null;
        }

        var cut = path.IndexOf('\\', server.Length + 1);
        var share = cut < 0 ? path.TrimEnd('\\') : path[..cut];
        return share.Length > server.Length + 1 ? share : null;
    }

    public static string Label(string path) => RootOf(path) switch
    {
        ThisPc => Strings.Place_ThisPc,
        Home => Strings.Place_Home,
        Libraries => Strings.Place_Libraries,
        RecycleBin => Strings.Place_RecycleBin,
        Network => Strings.Place_Network,
        Shares => Strings.Place_Shares,
        _ => path,
    };

    public static string Display(string path)
        => !IsPlace(path) ? path : Inner(path) is { Length: > 0 } inner ? Label(path) + " › " + inner : Label(path);

    public static string ShortName(string path) => Inner(path) is { Length: > 0 } inner ? inner : Label(path);

    public static bool KeepsOrder(string path) => RootOf(path) is ThisPc or Home or Network && Inner(path).Length == 0;

    public static string? OfSetting(string id) => id switch
    {
        Settings.DriveMenuSettings.ThisPc => ThisPc,
        Settings.DriveMenuSettings.Home => Home,
        Settings.DriveMenuSettings.Libraries => Libraries,
        Settings.DriveMenuSettings.RecycleBin => RecycleBin,
        Settings.DriveMenuSettings.Network => Network,
        _ => null,
    };
}

public sealed record PlaceItem(FileEntry Entry, string Target)
{
    public string? From { get; init; }

    public bool IsDrive { get; init; }

    public long Free { get; init; }

    public long Total { get; init; }

    public string? Comment { get; init; }

    public bool IsComputer { get; init; }
}

public sealed record NetworkDrive(string Letter, string Remote, int OpenFiles = -1);

public enum DisconnectResult { Done, FilesOpen }

public interface IShellPlaces
{
    Task<IReadOnlyList<PlaceItem>> ListAsync(string place, CancellationToken cancellationToken = default);

    Task RestoreAsync(IReadOnlyList<string> targets, nint owner);

    Task DeleteForGoodAsync(IReadOnlyList<string> targets, nint owner);

    Task CopyOutAsync(IReadOnlyList<string> targets, string folder, bool move, nint owner);

    Task EmptyAsync(nint owner);

    Task<bool> SignInAsync(string share, nint owner);

    Task<IReadOnlyList<NetworkDrive>> NetworkDrivesAsync();

    IReadOnlyList<string> FreeLetters();

    Task ConnectAsync(string letter, string remote, bool reconnect, bool otherUser, nint owner);

    Task<DisconnectResult> DisconnectAsync(string letter, bool force);
}

public sealed class MemoryShellPlaces : IShellPlaces
{
    public Dictionary<string, List<PlaceItem>> Items { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [ShellPlace.ThisPc] = [],
        [ShellPlace.Home] = [],
        [ShellPlace.Libraries] = [],
        [ShellPlace.RecycleBin] = [],
        [ShellPlace.Network] = [],
        [ShellPlace.Shares] = [],
    };

    public List<NetworkDrive> Drives { get; } = [];

    public List<string> Letters { get; } = ["Z:", "Y:", "X:"];

    public HashSet<string> Locked { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool SignInAnswer { get; set; } = true;

    public List<(string Action, string[] Targets, string? Folder)> Done { get; } = [];

    public Task<IReadOnlyList<PlaceItem>> ListAsync(string place, CancellationToken cancellationToken = default)
        => Locked.Contains(place) ? Task.FromException<IReadOnlyList<PlaceItem>>(new UnauthorizedAccessException(place))
            : Items.TryGetValue(place, out var items)
            ? Task.FromResult<IReadOnlyList<PlaceItem>>([.. items])
            : Task.FromException<IReadOnlyList<PlaceItem>>(new IOException(Strings.Place_CantOpen));

    public Task RestoreAsync(IReadOnlyList<string> targets, nint owner) => Take("restore", targets, null);

    public Task DeleteForGoodAsync(IReadOnlyList<string> targets, nint owner) => Take("delete", targets, null);

    public Task CopyOutAsync(IReadOnlyList<string> targets, string folder, bool move, nint owner)
        => move ? Take("move", targets, folder) : Record("copy", targets, folder);

    public Task EmptyAsync(nint owner)
    {
        Done.Add(("empty", [], null));
        Items[ShellPlace.RecycleBin].Clear();
        return Task.CompletedTask;
    }

    public Task<bool> SignInAsync(string share, nint owner)
    {
        Done.Add(("signin", [share], null));
        if (SignInAnswer)
        {
            Locked.Remove(share);
        }

        return Task.FromResult(SignInAnswer);
    }

    public Task<IReadOnlyList<NetworkDrive>> NetworkDrivesAsync() => Task.FromResult<IReadOnlyList<NetworkDrive>>([.. Drives]);

    public IReadOnlyList<string> FreeLetters() => [.. Letters];

    public Task ConnectAsync(string letter, string remote, bool reconnect, bool otherUser, nint owner)
    {
        Done.Add(("connect", [letter, remote, reconnect ? "reconnect" : "once", otherUser ? "other" : "me"], null));
        Drives.Add(new NetworkDrive(letter, remote, 0));
        Letters.Remove(letter);
        return Task.CompletedTask;
    }

    public Task<DisconnectResult> DisconnectAsync(string letter, bool force)
    {
        if (Drives.Find(d => d.Letter == letter) is not { } drive)
        {
            return Task.FromException<DisconnectResult>(new IOException(letter));
        }

        if (drive.OpenFiles > 0 && !force)
        {
            return Task.FromResult(DisconnectResult.FilesOpen);
        }

        Done.Add(("disconnect", [letter, force ? "force" : ""], null));
        Drives.Remove(drive);
        return Task.FromResult(DisconnectResult.Done);
    }

    private Task Take(string action, IReadOnlyList<string> targets, string? folder)
    {
        Items[ShellPlace.RecycleBin].RemoveAll(i => targets.Contains(i.Target, StringComparer.OrdinalIgnoreCase));
        return Record(action, targets, folder);
    }

    private Task Record(string action, IReadOnlyList<string> targets, string? folder)
    {
        Done.Add((action, [.. targets], folder));
        return Task.CompletedTask;
    }
}
