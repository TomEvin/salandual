// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/VolumeInfo.cs

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Salandual.Core.Resources;

namespace Salandual.Platform.Windows.FileSystem;

public sealed record VolumeDetails(
    string Root,
    DriveKind Kind,
    bool? SolidState,
    string? Label,
    string FileSystem,
    uint SerialNumber,
    int MaxNameLength,
    long ClusterSize,
    long Free,
    long Total,
    VolumeFeatures Features,
    string? Share)
{
    public string Serial => $"{SerialNumber >> 16:X4}-{SerialNumber & 0xFFFF:X4}";

    public int MaxLabelLength => FileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase) ? 11 : 32;

    public bool IsReadOnly => (Features & VolumeFeatures.ReadOnly) != 0;
}

[Flags]
public enum VolumeFeatures
{
    None = 0,
    CaseSensitive = 0x1,
    UnicodeNames = 0x4,
    Acls = 0x8,
    Compression = 0x10,
    Quotas = 0x20,
    SparseFiles = 0x40,
    ReparsePoints = 0x80,
    ObjectIds = 0x10000,
    Encryption = 0x20000,
    NamedStreams = 0x40000,
    ReadOnly = 0x80000,
    HardLinks = 0x400000,
    BlockCloning = 0x8000000,
}

public static partial class VolumeInfo
{
    public static VolumeDetails? Read(string path)
    {
        if (WindowsPath.GetRoot(path) is not { } root)
        {
            return null;
        }

        var dir = root.EndsWith('\\') ? root : root + "\\";
        Span<char> label = stackalloc char[261];
        Span<char> fs = stackalloc char[261];
        if (!GetVolumeInformationW(dir, ref MemoryMarshal.GetReference(label), label.Length, out var serial, out var maxName, out var flags, ref MemoryMarshal.GetReference(fs), fs.Length))
        {
            return null;
        }

        long cluster = 0;
        if (GetDiskFreeSpaceW(dir, out var sectorsPerCluster, out var bytesPerSector, out _, out _))
        {
            cluster = (long)sectorsPerCluster * bytesPerSector;
        }

        DiskSpace.TryGet(dir, out var free, out var total);
        var kind = dir.StartsWith(@"\\", StringComparison.Ordinal) ? DriveKind.Network : GetDriveTypeW(dir) switch
        {
            3 => DriveKind.Fixed,
            2 => DriveKind.Removable,
            5 => DriveKind.Optical,
            4 => DriveKind.Network,
            _ => DriveKind.Other,
        };
        var labelText = new string(label[..label.IndexOf('\0')]);
        var share = kind == DriveKind.Network ? (dir.StartsWith(@"\\", StringComparison.Ordinal) ? root : Drives.ShareOf(root)) : null;
        return new VolumeDetails(root, kind, kind == DriveKind.Fixed ? IsSolidState(root) : null, labelText.Length > 0 ? labelText : null,
            new string(fs[..fs.IndexOf('\0')]), serial, (int)maxName, cluster, free, total, (VolumeFeatures)flags, share);
    }

    public static void SetLabel(string root, string label)
    {
        var dir = root.EndsWith('\\') ? root : root + "\\";
        if (!SetVolumeLabelW(dir, label.Length == 0 ? null : label))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 5)
            {
                throw new UnauthorizedAccessException(Strings.Win_RenameDriveNeedsAdmin);
            }

            throw new Win32Exception(error);
        }
    }

    public static async Task<bool> SetLabelElevatedAsync(string root, string label)
    {
        if (root.Length < 2 || !char.IsAsciiLetter(root[0]) || root[1] != ':')
        {
            return false;
        }

        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "label.exe"))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = label.Length == 0 ? $"{root[..2]}" : $"{root[..2]} {label}",
        };
        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public static void ShowProperties(IntPtr owner, string root)
        => SHObjectProperties(owner, 2 , root.EndsWith('\\') ? root : root + "\\", null);

    private static bool? IsSolidState(string root)
    {
        using var handle = CreateFileW($@"\\.\{root[..2]}", 0, 3 , IntPtr.Zero, 3 , 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var query = new StoragePropertyQuery { PropertyId = 7 , QueryType = 0 };
        if (!DeviceIoControl(handle, 0x2D1400 , ref query, Marshal.SizeOf<StoragePropertyQuery>(), out var result, Marshal.SizeOf<SeekPenaltyDescriptor>(), out _, IntPtr.Zero))
        {
            return null;
        }

        return result.IncursSeekPenalty == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SeekPenaltyDescriptor
    {
        public int Version;
        public int Size;
        public byte IncursSeekPenalty;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformationW(string rootPathName, ref char volumeNameBuffer, int volumeNameSize, out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags, ref char fileSystemNameBuffer, int fileSystemNameSize);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceW(string rootPathName, out uint sectorsPerCluster, out uint bytesPerSector, out uint numberOfFreeClusters, out uint totalNumberOfClusters);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveTypeW(string rootPathName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetVolumeLabelW(string rootPathName, string? volumeName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle device, uint ioControlCode, ref StoragePropertyQuery inBuffer, int inBufferSize, out SeekPenaltyDescriptor outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHObjectProperties(IntPtr owner, uint type, string objectName, string? propertyPage);
}
