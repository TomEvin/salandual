// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/PreviousVersions.cs

using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Salandual.Platform.Windows.FileSystem;

public sealed record PreviousVersion(DateTime Taken, string Path, long Size, DateTime Modified);

public static unsafe partial class PreviousVersions
{
    public static IReadOnlyList<PreviousVersion>? List(string path, CancellationToken cancel = default)
    {
        var share = SharePath(WindowsPath.FromExtended(path));
        if (share is null)
        {
            return null;
        }

        string[] tokens;
        try
        {
            tokens = Snapshots(share.Value.Root);
        }
        catch (IOException)
        {
            return null;
        }

        var versions = new List<PreviousVersion>();
        foreach (var token in tokens)
        {
            cancel.ThrowIfCancellationRequested();
            if (ParseToken(token) is not { } taken)
            {
                continue;
            }

            var old = share.Value.Root.TrimEnd('\\') + "\\" + token + share.Value.Below;
            FileSystemInfo info = Directory.Exists(old) ? new DirectoryInfo(old) : new FileInfo(old);
            if (!info.Exists)
            {
                continue;
            }

            versions.Add(new PreviousVersion(taken, old, info is FileInfo f ? f.Length : 0, info.LastWriteTime));
        }

        return [.. versions
            .OrderByDescending(v => v.Taken)
            .GroupBy(v => (v.Modified, v.Size))
            .Select(g => g.Last())
            .OrderByDescending(v => v.Taken)];
    }

    public static DateTime? ParseToken(string token)
        => DateTime.TryParseExact(token, "'@GMT-'yyyy.MM.dd-HH.mm.ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)
            ? utc.ToLocalTime()
            : null;

    public static (string Root, string Below)? SharePath(string path)
    {
        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            var unc = UncPath.For(path);
            if (!unc.StartsWith(@"\\", StringComparison.Ordinal))
            {
                unc = @"\\localhost\" + char.ToUpperInvariant(path[0]) + "$" + path[2..];
            }

            path = unc;
        }

        if (!path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return null;
        }

        var server = path.IndexOf('\\', 2);
        if (server < 0)
        {
            return null;
        }

        var shareEnd = path.IndexOf('\\', server + 1);
        var root = shareEnd < 0 ? path : path[..shareEnd];
        var below = shareEnd < 0 ? "" : path[shareEnd..];
        return (root, below);
    }

    private static string[] Snapshots(string shareRoot)
    {
        using var handle = Open(shareRoot + "\\");
        var size = 16 + 4096;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = new byte[size];
            fixed (byte* b = buffer)
            {
                uint returned;
                if (!DeviceIoControl(handle, FsctlSrvEnumerateSnapshots, null, 0, b, (uint)size, &returned, null))
                {
                    throw new IOException(null, Marshal.GetLastPInvokeError());
                }

                var count = *(uint*)b;
                var countReturned = *(uint*)(b + 4);
                var arraySize = *(uint*)(b + 8);
                if (count > countReturned && arraySize + 12 > size)
                {
                    size = (int)arraySize + 16;
                    continue;
                }

                var text = new string((char*)(b + 12), 0, (int)Math.Min(arraySize / 2, (uint)(size - 12) / 2));
                return text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        return [];
    }

    private static SafeFileHandle Open(string path)
    {
        var handle = CreateFileW(path, FileReadAttributes | Synchronize, 7 , null, 3 , FileFlagBackupSemantics, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException(null, error);
        }

        return handle;
    }

    private const uint FsctlSrvEnumerateSnapshots = 0x00144064;
    private const uint FileReadAttributes = 0x80;
    private const uint Synchronize = 0x100000;
    private const uint FileFlagBackupSemantics = 0x02000000;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, void* security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize, void* output, uint outputSize, uint* returned, void* overlapped);
}
