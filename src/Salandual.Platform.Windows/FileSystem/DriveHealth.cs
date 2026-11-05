// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/DriveHealth.cs

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Salandual.Platform.Windows.FileSystem;

public enum HealthState { Good, Watch, Failing, NoData, AdminOnly }

public enum LinkKind { Pcie, Sata, Usb }

public sealed record DriveLink(LinkKind Kind, int Generation = 0, int Lanes = 0, int MaxGeneration = 0, int MaxLanes = 0)
{
    public bool Slower => Generation > 0 && (Generation < MaxGeneration || Lanes < MaxLanes);
}

public sealed record DriveTemperature(int Now, int Warning, int Critical);

public sealed record NvmeHealth(
    int CriticalWarning, int Temperature, int Spare, int SpareThreshold, int Used,
    long BytesRead, long BytesWritten, long PowerCycles, long Hours, long UnsafeShutdowns, long MediaErrors, long ErrorLogEntries);

public readonly record struct SmartAttribute(int Id, int Value, int Worst, long Raw)
{
    public long Count => Id switch
    {
        0xBE or 0xC2 => Raw & 0xFF,
        0x09 => Raw & 0xFFFFFFFF,
        _ => Raw,
    };
}

public sealed partial record DriveHealth(
    DriveLink? Link, DriveTemperature? Temperature, NvmeHealth? Nvme, IReadOnlyList<SmartAttribute> Attributes, bool? PredictsFailure, bool AdminOnly)
{
    private const uint QueryProperty = 0x2D1400;
    private const uint DeviceNumber = 0x2D1080;
    private const uint PredictFailureCode = 0x2D1100;
    private const int AccessDenied = 5;

    public static readonly int[] WatchedAttributes = [0x05, 0xBB, 0xC5, 0xC6];

    public HealthState State
    {
        get
        {
            if (Nvme is { } n)
            {
                return n.CriticalWarning != 0 || (n.SpareThreshold > 0 && n.Spare < n.SpareThreshold) ? HealthState.Failing
                    : n.Used >= 90 || n.MediaErrors > 0 ? HealthState.Watch
                    : HealthState.Good;
            }

            if (PredictsFailure == true)
            {
                return HealthState.Failing;
            }

            if (Attributes.Count > 0 || PredictsFailure == false)
            {
                return Attributes.Any(a => WatchedAttributes.Contains(a.Id) && a.Count > 0) ? HealthState.Watch : HealthState.Good;
            }

            return AdminOnly ? HealthState.AdminOnly : HealthState.NoData;
        }
    }

    public long? Count(int id) => Attributes.FirstOrDefault(a => a.Id == id) is { Id: > 0 } a ? a.Count : null;

    public static DriveHealth? Read(string root)
    {
        if (root.Length < 2 || root[1] != ':')
        {
            return null;
        }

        using var handle = CreateFileW($@"\\.\{root[..2]}", 0, 3 , IntPtr.Zero, 3 , 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var device = Query(handle, 0 , 1024, out _);
        var bus = device is null ? 0 : BitConverter.ToInt32(device, 28);
        var temperature = Query(handle, 51 , 512, out _) is { } t ? ParseTemperature(t) : null;
        var denied = false;

        NvmeHealth? nvme = null;
        DriveLink? link = null;
        var attributes = (IReadOnlyList<SmartAttribute>)[];
        bool? predicts = null;
        if (bus == 17)
        {
            nvme = ProtocolData(handle, 3 , 2 , 2 , 512, out var error) is { } log ? ParseNvme(log) : null;
            denied = error == AccessDenied;
            link = PcieLink(handle);
        }
        else if (bus is 11 or 3)
        {
            if (ProtocolData(handle, 2 , 1 , 0, 512, out var error) is { } identify)
            {
                WipeIdentity(identify);
                link = SataLink(BitConverter.ToUInt16(identify, 152), BitConverter.ToUInt16(identify, 154));
            }

            var data = new byte[516];
            if (DeviceIoControl(handle, PredictFailureCode, null, 0, data, data.Length, out _, IntPtr.Zero))
            {
                predicts = BitConverter.ToUInt32(data, 0) != 0;
                attributes = ParseAttributes(data.AsSpan(4));
            }
            else
            {
                denied = error == AccessDenied || Marshal.GetLastWin32Error() == AccessDenied;
            }
        }
        else if (bus == 7)
        {
            link = new DriveLink(LinkKind.Usb);
        }

        return new DriveHealth(link, temperature, nvme, attributes, predicts, denied);
    }

    public static DriveTemperature? ParseTemperature(byte[] d)
        => d.Length >= 28 && BitConverter.ToUInt16(d, 12) > 0
            ? new DriveTemperature(BitConverter.ToInt16(d, 26), BitConverter.ToInt16(d, 10), BitConverter.ToInt16(d, 8))
            : null;

    public static NvmeHealth ParseNvme(byte[] l)
    {
        static long C(byte[] b, int at) => (long)Math.Min(BitConverter.ToUInt64(b, at), long.MaxValue);
        static long Bytes(byte[] b, int at) => (long)UInt128.Min((UInt128)BitConverter.ToUInt64(b, at) * 512_000, long.MaxValue);
        var kelvin = BitConverter.ToUInt16(l, 1);
        return new NvmeHealth(l[0], kelvin > 0 ? kelvin - 273 : 0, l[3], l[4], l[5],
            Bytes(l, 32), Bytes(l, 48), C(l, 112), C(l, 128), C(l, 144), C(l, 160), C(l, 176));
    }

    public static IReadOnlyList<SmartAttribute> ParseAttributes(ReadOnlySpan<byte> smart)
    {
        var list = new List<SmartAttribute>();
        for (var at = 2; at + 12 <= smart.Length && at < 2 + 30 * 12; at += 12)
        {
            if (smart[at] == 0)
            {
                continue;
            }

            long raw = 0;
            for (var i = 5; i >= 0; i--)
            {
                raw = (raw << 8) | smart[at + 5 + i];
            }

            list.Add(new SmartAttribute(smart[at], smart[at + 3], smart[at + 4], raw));
        }

        return list;
    }

    public static DriveLink? SataLink(ushort word76, ushort word77)
    {
        if (word76 is 0 or 0xFFFF)
        {
            return null;
        }

        var max = (word76 & 8) != 0 ? 3 : (word76 & 4) != 0 ? 2 : (word76 & 2) != 0 ? 1 : 0;
        var now = (word77 >> 1) & 7;
        return new DriveLink(LinkKind.Sata, now is >= 1 and <= 3 ? now : 0, 0, max, 0);
    }

    private static void WipeIdentity(byte[] identify)
    {
        Array.Clear(identify, 20, 20);
        Array.Clear(identify, 216, 8);
    }

    private static byte[]? Query(SafeFileHandle handle, int property, int size, out int error)
    {
        var query = new byte[12];
        BitConverter.TryWriteBytes(query.AsSpan(0), property);
        var result = new byte[size];
        if (DeviceIoControl(handle, QueryProperty, query, query.Length, result, result.Length, out _, IntPtr.Zero))
        {
            error = 0;
            return result;
        }

        error = Marshal.GetLastWin32Error();
        return null;
    }

    private static byte[]? ProtocolData(SafeFileHandle handle, int protocol, int dataType, int value, int length, out int error)
    {
        var query = new byte[8 + 40 + length];
        BitConverter.TryWriteBytes(query.AsSpan(0), 50);
        BitConverter.TryWriteBytes(query.AsSpan(8), protocol);
        BitConverter.TryWriteBytes(query.AsSpan(12), dataType);
        BitConverter.TryWriteBytes(query.AsSpan(16), value);
        BitConverter.TryWriteBytes(query.AsSpan(24), 40);
        BitConverter.TryWriteBytes(query.AsSpan(28), length);
        var result = new byte[query.Length];
        if (!DeviceIoControl(handle, QueryProperty, query, query.Length, result, result.Length, out _, IntPtr.Zero))
        {
            error = Marshal.GetLastWin32Error();
            return null;
        }

        error = 0;
        var at = 8 + BitConverter.ToInt32(result, 8 + 16);
        var got = Math.Min(BitConverter.ToInt32(result, 8 + 20), length);
        return at > 8 && got > 0 && at + got <= result.Length ? result.AsSpan(at, got).ToArray() : null;
    }

    private static readonly Guid DiskInterface = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
    private static readonly Guid PciProperties = new("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");

    private static DriveLink? PcieLink(SafeFileHandle volume)
    {
        var number = new byte[12];
        if (!DeviceIoControl(volume, DeviceNumber, null, 0, number, number.Length, out _, IntPtr.Zero))
        {
            return null;
        }

        var disk = BitConverter.ToInt32(number, 4);
        var guid = DiskInterface;
        var set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12 );
        if (set == -1)
        {
            return null;
        }

        try
        {
            for (uint i = 0; ; i++)
            {
                var iface = new SpDeviceInterfaceData { Size = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface))
                {
                    return null;
                }

                var info = new SpDevinfoData { Size = Marshal.SizeOf<SpDevinfoData>() };
                if (DevicePath(set, ref iface, ref info) is not { } path)
                {
                    continue;
                }

                using (var h = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid || !DeviceIoControl(h, DeviceNumber, null, 0, number, number.Length, out _, IntPtr.Zero) || BitConverter.ToInt32(number, 4) != disk)
                    {
                        continue;
                    }
                }

                var node = info.DevInst;
                for (var level = 0; level < 4 && CM_Get_Parent(out node, node, 0) == 0; level++)
                {
                    if (LinkProperty(node, 9) is { } speed)
                    {
                        return new DriveLink(LinkKind.Pcie, speed, LinkProperty(node, 10) ?? 0, LinkProperty(node, 11) ?? 0, LinkProperty(node, 12) ?? 0);
                    }
                }

                return null;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string? DevicePath(nint set, ref SpDeviceInterfaceData iface, ref SpDevinfoData info)
    {
        SetupDiGetDeviceInterfaceDetailW(set, ref iface, IntPtr.Zero, 0, out var size, IntPtr.Zero);
        if (size <= 0 || size > 4096)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            return SetupDiGetDeviceInterfaceDetailW(set, ref iface, buffer, size, out _, ref info) ? Marshal.PtrToStringUni(buffer + 4) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int? LinkProperty(uint node, uint id)
    {
        var key = new DevPropKey { Fmtid = PciProperties, Pid = id };
        var value = 0u;
        var size = 4u;
        return CM_Get_DevNode_PropertyW(node, ref key, out _, ref value, ref size, 0) == 0 && value is > 0 and < 64 ? (int)value : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int Size;
        public Guid ClassGuid;
        public uint DevInst;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid Fmtid;
        public uint Pid;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, byte[]? inBuffer, int inBufferSize, [Out] byte[] outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(nint set, IntPtr info, ref Guid interfaceGuid, uint index, ref SpDeviceInterfaceData data);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(nint set, ref SpDeviceInterfaceData data, IntPtr detail, int detailSize, out int requiredSize, IntPtr info);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(nint set, ref SpDeviceInterfaceData data, IntPtr detail, int detailSize, out int requiredSize, ref SpDevinfoData info);

    [LibraryImport("setupapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(nint set);

    [LibraryImport("cfgmgr32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CM_Get_Parent(out uint parent, uint node, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CM_Get_DevNode_PropertyW(uint node, ref DevPropKey key, out uint type, ref uint buffer, ref uint size, uint flags);
}
