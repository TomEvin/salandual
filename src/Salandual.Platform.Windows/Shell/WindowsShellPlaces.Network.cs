// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/WindowsShellPlaces.Network.cs

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Salandual.Core.Vfs;

namespace Salandual.Platform.Windows.Shell;

public sealed partial class WindowsShellPlaces
{
    private static readonly TimeSpan _discovery = TimeSpan.FromSeconds(8);

    private static async Task<IReadOnlyList<PlaceItem>> NetworkAsync(CancellationToken cancellationToken)
    {
        var computers = OnStaThread(() => Enumerate(ShellPlace.Network, ComputerAt));
        var drives = Task.Run(NetworkDriveItems, cancellationToken);
        var items = new List<PlaceItem>();
        if (await Task.WhenAny(computers, Task.Delay(_discovery, cancellationToken)).ConfigureAwait(false) == computers && computers.IsCompletedSuccessfully)
        {
            items.AddRange(computers.Result.OrderBy(i => i.Entry.Name, StringComparer.CurrentCultureIgnoreCase));
        }

        cancellationToken.ThrowIfCancellationRequested();
        items.AddRange(await drives.ConfigureAwait(false));
        return items;
    }

    private static PlaceItem? ComputerAt(string path, bool folder)
        => ShellPlace.IsServer(path)
            ? new PlaceItem(new FileEntry(path.TrimEnd('\\')[2..], 0, 0, FileAttributes.Directory), path.TrimEnd('\\')) { IsComputer = true }
            : null;

    private static List<PlaceItem> NetworkDriveItems()
        => [.. ReadNetworkDrives(openFiles: false).Select(d => new PlaceItem(
            new FileEntry(d.Letter + " " + LastPart(d.Remote), 0, 0, FileAttributes.Directory), d.Letter + "\\") { Comment = d.Remote })];

    private static string LastPart(string remote)
    {
        var trimmed = remote.TrimEnd('\\');
        return trimmed[(trimmed.LastIndexOf('\\') + 1)..];
    }

    private static Task<T> OnStaThread<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Network discovery",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static unsafe List<PlaceItem> ServerShares(string server)
    {
        server = server.TrimEnd('\\');
        var items = new List<PlaceItem>();
        nint buffer = 0;
        uint resume = 0;
        int code;
        do
        {
            code = NetShareEnum(server, 1, out buffer, MaxPreferredLength, out var read, out _, ref resume);
            try
            {
                if (code is not (0 or MoreData))
                {
                    throw Failure(code);
                }

                for (var i = 0; i < read; i++)
                {
                    var info = ((ShareInfo1*)buffer)[i];
                    var name = Marshal.PtrToStringUni(info.NetName);
                    if ((info.Type & ShareTypeMask) != ShareDisk || !IsShareName(name))
                    {
                        continue;
                    }

                    var hidden = name.EndsWith('$') ? FileAttributes.Hidden : 0;
                    var comment = Marshal.PtrToStringUni(info.Remark);
                    items.Add(new PlaceItem(new FileEntry(name, 0, 0, FileAttributes.Directory | hidden), server + "\\" + name) { Comment = comment is { Length: > 0 } ? comment : null });
                }
            }
            finally
            {
                if (buffer != 0)
                {
                    NetApiBufferFree(buffer);
                    buffer = 0;
                }
            }
        }
        while (code == MoreData);

        return items;
    }

    internal static bool IsShareName([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? name)
        => name is { Length: > 0 and <= 80 } && name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0']) < 0 && name is not ("." or "..");

    private static List<PlaceItem> LocalShares()
    {
        var items = new List<PlaceItem>();
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Shares", writable: false);
        foreach (var name in key?.GetValueNames() ?? [])
        {
            if (key!.GetValue(name) is string[] lines && ParseShare(lines) is ({ } path, var remark) && IsShareName(name))
            {
                items.Add(new PlaceItem(new FileEntry(name, 0, 0, FileAttributes.Directory), path) { From = path, Comment = remark });
            }
        }

        return [.. items.OrderBy(i => i.Entry.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public static (string? Path, string? Remark) ParseShare(IEnumerable<string> lines)
    {
        string? path = null, remark = null, type = null;
        foreach (var line in lines)
        {
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var value = line[(eq + 1)..];
            switch (line[..eq])
            {
                case "Path": path = value; break;
                case "Remark": remark = value.Length > 0 ? value : null; break;
                case "Type": type = value; break;
            }
        }

        return type is null or "0" && path is not null && Path.IsPathFullyQualified(path) ? (path, remark) : (null, null);
    }

    public Task<bool> SignInAsync(string share, nint owner) => Task.Run(() =>
    {
        var server = ShellPlace.IsServer(share);
        var code = AddConnection(owner, null, server ? share.TrimEnd('\\') + @"\IPC$" : share.TrimEnd('\\'), server ? ResourceAny : ResourceDisk, ConnectInteractive | ConnectPrompt | ConnectTemporary);
        return code switch
        {
            0 => true,
            Cancelled => false,
            _ => throw Failure(code),
        };
    });

    public Task<IReadOnlyList<NetworkDrive>> NetworkDrivesAsync() => Task.Run<IReadOnlyList<NetworkDrive>>(() => ReadNetworkDrives(openFiles: true));

    public IReadOnlyList<string> FreeLetters()
    {
        var used = GetLogicalDrives();
        var free = new List<string>();
        for (var c = 'Z'; c >= 'D'; c--)
        {
            if ((used & (1u << (c - 'A'))) == 0 && RemoteOf(c + ":") is null)
            {
                free.Add(c + ":");
            }
        }

        return free;
    }

    public Task ConnectAsync(string letter, string remote, bool reconnect, bool otherUser, nint owner) => Task.Run(() =>
    {
        var flags = ConnectInteractive | (reconnect ? ConnectUpdateProfile : ConnectTemporary) | (otherUser ? ConnectPrompt : 0);
        var code = AddConnection(owner, letter, remote.TrimEnd('\\'), ResourceDisk, flags);
        if (code == Cancelled)
        {
            throw new OperationCanceledException();
        }

        if (code != 0)
        {
            throw Failure(code);
        }
    });

    public Task<DisconnectResult> DisconnectAsync(string letter, bool force) => Task.Run(() =>
    {
        var code = WNetCancelConnection2W(letter, ConnectUpdateProfile, force);
        return code switch
        {
            0 => DisconnectResult.Done,
            OpenFiles or DeviceInUse => DisconnectResult.FilesOpen,
            _ => throw Failure(code),
        };
    });

    private static List<NetworkDrive> ReadNetworkDrives(bool openFiles)
    {
        var drives = new List<NetworkDrive>();
        for (var c = 'A'; c <= 'Z'; c++)
        {
            if (RemoteOf(c + ":") is { } remote)
            {
                drives.Add(new NetworkDrive(c + ":", remote, openFiles ? OpenFilesOn(c + ":") : -1));
            }
        }

        return drives;
    }

    private static unsafe string? RemoteOf(string letter)
    {
        var buffer = stackalloc char[1024];
        var length = 1024u;
        var code = WNetGetConnectionW(letter, buffer, ref length);
        return code is 0 or ConnectionUnavailable ? new string(buffer) : null;
    }

    private static unsafe int OpenFilesOn(string letter)
    {
        if (NetUseGetInfo(null, letter, 1, out var buffer) != 0 || buffer == 0)
        {
            return -1;
        }

        try
        {
            return (int)((UseInfo1*)buffer)->RefCount;
        }
        finally
        {
            NetApiBufferFree(buffer);
        }
    }

    private static unsafe int AddConnection(nint owner, string? letter, string remote, uint type, uint flags)
    {
        fixed (char* local = letter, name = remote)
        {
            var resource = new NetResource { Type = type, LocalName = (nint)local, RemoteName = (nint)name };
            return WNetAddConnection3W(owner, &resource, null, null, flags);
        }
    }

    private static Exception Failure(int code)
        => code == AccessDenied ? new UnauthorizedAccessException(new Win32Exception(code).Message) : new IOException(new Win32Exception(code).Message, unchecked((int)0x80070000) | code);

    [StructLayout(LayoutKind.Sequential)]
    private struct ShareInfo1
    {
        public nint NetName;
        public uint Type;
        public nint Remark;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UseInfo1
    {
        public nint Local;
        public nint Remote;
        public nint Password;
        public uint Status;
        public uint AsgType;
        public uint RefCount;
        public uint UseCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NetResource
    {
        public uint Scope;
        public uint Type;
        public uint DisplayType;
        public uint Usage;
        public nint LocalName;
        public nint RemoteName;
        public nint Comment;
        public nint Provider;
    }

    private const int AccessDenied = 5, MoreData = 234, Cancelled = 1223, ConnectionUnavailable = 1201, OpenFiles = 2401, DeviceInUse = 2404;
    private const uint MaxPreferredLength = uint.MaxValue, ShareTypeMask = 0xFF, ShareDisk = 0;
    private const uint ResourceAny = 0, ResourceDisk = 1;
    private const uint ConnectUpdateProfile = 0x1, ConnectTemporary = 0x4, ConnectInteractive = 0x8, ConnectPrompt = 0x10;

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NetShareEnum(string server, uint level, out nint buffer, uint maxLength, out uint read, out uint total, ref uint resume);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NetUseGetInfo(string? server, string useName, uint level, out nint buffer);

    [LibraryImport("netapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NetApiBufferFree(nint buffer);

    [LibraryImport("mpr.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int WNetAddConnection3W(nint owner, NetResource* resource, string? password, string? user, uint flags);

    [LibraryImport("mpr.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int WNetCancelConnection2W(string name, uint flags, [MarshalAs(UnmanagedType.Bool)] bool force);

    [LibraryImport("mpr.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int WNetGetConnectionW(string localName, char* remoteName, ref uint length);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetLogicalDrives();
}
