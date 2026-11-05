// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/DriveMedia.cs

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace Salandual.Platform.Windows.FileSystem;

public enum DriveMedium { Unknown, HardDisk, Ssd, Nvme, UsbStick, External, SdCard, CardReader, Optical, Network }

public static partial class DriveMedia
{
    private static readonly ConcurrentDictionary<string, bool> _waitsOn = new(StringComparer.OrdinalIgnoreCase);

    public static DriveMedium Of(string root)
    {
        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return DriveMedium.Network;
        }

        if (root.Length < 2 || root[1] != ':')
        {
            return DriveMedium.Unknown;
        }

        var dir = root[..2] + "\\";
        var type = GetDriveTypeW(dir);
        if (type == 4)
        {
            return DriveMedium.Network;
        }

        if (type == 5)
        {
            return DriveMedium.Optical;
        }

        var (bus, product) = Bus(root);
        return Classify(bus, type == 2, SeekPenalty(root), product);
    }

    public static DriveMedium Classify(int bus, bool removable, bool? seekPenalty, string? product) => bus switch
    {
        12 or 13 => DriveMedium.SdCard,
        17 => DriveMedium.Nvme,
        7 when product is { } p && (p.Contains("card", StringComparison.OrdinalIgnoreCase) || p.Contains("reader", StringComparison.OrdinalIgnoreCase)
            || p.Contains("SD/", StringComparison.OrdinalIgnoreCase) || p.Contains("MMC", StringComparison.OrdinalIgnoreCase)) => DriveMedium.CardReader,
        7 => removable ? DriveMedium.UsbStick : DriveMedium.External,
        _ when removable => DriveMedium.UsbStick,
        _ => seekPenalty switch { true => DriveMedium.HardDisk, false => DriveMedium.Ssd, _ => DriveMedium.Unknown },
    };

    public static bool WaitsOn(string volume) => _waitsOn.GetOrAdd(volume, v =>
        v.StartsWith(@"\\", StringComparison.Ordinal) || v.Length < 2 || v[1] != ':' || GetDriveTypeW(v[..2] + "\\") == 4 || SeekPenalty(v) != false);

    public static bool? SeekPenalty(string root)
    {
        using var handle = Open(root);
        if (handle.IsInvalid)
        {
            return null;
        }

        var query = new StoragePropertyQuery { PropertyId = 7 , QueryType = 0 };
        if (!DeviceIoControl(handle, IoctlQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(), out SeekPenaltyDescriptor result, Marshal.SizeOf<SeekPenaltyDescriptor>(), out _, IntPtr.Zero))
        {
            return null;
        }

        return result.IncursSeekPenalty != 0;
    }

    private const uint IoctlQueryProperty = 0x2D1400;

    private static (int Bus, string? Product) Bus(string root)
    {
        using var handle = Open(root);
        if (handle.IsInvalid)
        {
            return (0, null);
        }

        var query = new StoragePropertyQuery { PropertyId = 0 , QueryType = 0 };
        var buffer = new byte[1024];
        if (!DeviceIoControl(handle, IoctlQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(), buffer, buffer.Length, out var got, IntPtr.Zero) || got < 32)
        {
            return (0, null);
        }

        var bus = BitConverter.ToInt32(buffer, 28);
        var productAt = BitConverter.ToInt32(buffer, 16);
        string? product = null;
        if (productAt > 0 && productAt < got)
        {
            var end = Array.IndexOf(buffer, (byte)0, productAt);
            product = Encoding.ASCII.GetString(buffer, productAt, (end < 0 ? got : end) - productAt).Trim();
        }

        return (bus, product);
    }

    private static Microsoft.Win32.SafeHandles.SafeFileHandle Open(string root)
        => CreateFileW($@"\\.\{root[..2]}", 0, 3 , IntPtr.Zero, 3 , 0, IntPtr.Zero);

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

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveTypeW(string rootPathName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle device, uint ioControlCode, ref StoragePropertyQuery inBuffer, int inBufferSize, out SeekPenaltyDescriptor outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle device, uint ioControlCode, ref StoragePropertyQuery inBuffer, int inBufferSize, [Out] byte[] outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);
}
