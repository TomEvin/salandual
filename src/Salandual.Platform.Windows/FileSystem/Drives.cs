// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/Drives.cs

using System.Runtime.InteropServices;

namespace Salandual.Platform.Windows.FileSystem;

public enum DriveKind { Fixed, Removable, Optical, Network, Other }

public sealed record DriveDetails(string Root, DriveKind Kind, bool Ready, string? Label, long Free, long Total, string? Share)
{
    public char Letter => char.ToUpperInvariant(Root[0]);
}

public static partial class Drives
{
    public static IReadOnlyList<(string Root, DriveKind Kind)> List()
        => [.. DriveInfo.GetDrives().Select(d => (d.Name, KindOf(d.DriveType)))];

    public static async Task<IReadOnlyList<DriveDetails>> ReadAsync(TimeSpan timeout)
    {
        var drives = List();
        var tasks = drives.Select(d => Task.Run(() => Read(d.Root, d.Kind))).ToList();
        var all = Task.WhenAll(tasks);
        await Task.WhenAny(all, Task.Delay(timeout)).ConfigureAwait(false);
        return [.. tasks.Select((t, i) => t.IsCompletedSuccessfully ? t.Result : new DriveDetails(drives[i].Root, drives[i].Kind, false, null, 0, 0, null))];
    }

    private static DriveDetails Read(string root, DriveKind kind)
    {
        var share = kind == DriveKind.Network ? ShareOf(root) : null;
        try
        {
            var info = new DriveInfo(root);
            if (!info.IsReady)
            {
                return new DriveDetails(root, kind, false, null, 0, 0, share);
            }

            var label = info.VolumeLabel;
            return new DriveDetails(root, kind, true, label.Length > 0 ? label : null, info.AvailableFreeSpace, info.TotalSize, share);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DriveDetails(root, kind, false, null, 0, 0, share);
        }
    }

    private static DriveKind KindOf(DriveType type) => type switch
    {
        DriveType.Fixed => DriveKind.Fixed,
        DriveType.Removable => DriveKind.Removable,
        DriveType.CDRom => DriveKind.Optical,
        DriveType.Network => DriveKind.Network,
        _ => DriveKind.Other,
    };

    public static unsafe string? ShareOf(string root)
    {
        var local = root.TrimEnd('\\');
        var length = 512;
        var buffer = stackalloc char[length];
        return WNetGetConnectionW(local, buffer, ref length) == 0 ? new string(buffer) : null;
    }

    public static string? KnownFolder(string id)
    {
        var path = id switch
        {
            "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "Downloads" => DownloadsFolder(),
            "Cloud" => Environment.GetEnvironmentVariable("OneDrive") ?? Environment.GetEnvironmentVariable("OneDriveConsumer") ?? Environment.GetEnvironmentVariable("OneDriveCommercial"),
            _ => null,
        };
        return string.IsNullOrEmpty(path) || !Directory.Exists(path) ? null : path;
    }

    private static unsafe string? DownloadsFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(&id, 0, IntPtr.Zero, out var path) != 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    [LibraryImport("mpr.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int WNetGetConnectionW(string localName, char* remoteName, ref int length);

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHGetKnownFolderPath(Guid* id, uint flags, IntPtr token, out IntPtr path);
}
