// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/FileDetails.cs

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Salandual.Platform.Windows.FileSystem;

public sealed record FileDetails(
    string Path,
    bool IsDirectory,
    string TypeName,
    string? OpensWith,
    long Size,
    long SizeOnDisk,
    DateTime Created,
    DateTime Modified,
    DateTime Accessed,
    FileAttributes Attributes,
    string? LinkTarget,
    string? Description,
    string? Version,
    bool FromInternet);

public readonly record struct FolderCount(long Files, long Folders, long Bytes, long BytesOnDisk, bool Done);

public static unsafe partial class FileDetailsReader
{
    public static FileDetails Read(string path)
    {
        var extended = WindowsPath.ToExtended(path);
        FileSystemInfo info = Directory.Exists(extended) ? new DirectoryInfo(extended) : new FileInfo(extended);
        info.Refresh();
        if (!info.Exists)
        {
            throw new FileNotFoundException(null, path);
        }

        var isDir = info is DirectoryInfo;
        var size = info is FileInfo f ? f.Length : 0;
        string? linkTarget = null;
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            try
            {
                linkTarget = info.LinkTarget is { } t ? WindowsPath.FromExtended(t) : null;
            }
            catch (IOException)
            {
            }
        }

        string? description = null, version = null;
        if (!isDir && HasVersionResource(path))
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(extended);
                description = Clean(v.FileDescription) ?? Clean(v.ProductName);
                version = Clean(v.ProductVersion) ?? Clean(v.FileVersion);
            }
            catch (IOException)
            {
            }
        }

        return new FileDetails(
            WindowsPath.FromExtended(path),
            isDir,
            TypeName(path, info.Attributes),
            isDir ? null : OpensWith(path),
            size,
            isDir ? 0 : SizeOnDisk(path, size),
            info.CreationTime,
            info.LastWriteTime,
            info.LastAccessTime,
            info.Attributes,
            linkTarget,
            description,
            version,
            !isDir && IsFromInternet(path));
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool HasVersionResource(string path)
        => System.IO.Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll" or ".sys" or ".ocx" or ".cpl" or ".scr" or ".msi" or ".mui" or ".drv";

    public static string TypeName(string path, FileAttributes attributes)
    {
        var info = default(ShFileInfo);
        var flags = ShgfiTypeName | ShgfiUseFileAttributes;
        return SHGetFileInfoW(path, (uint)attributes, &info, (uint)sizeof(ShFileInfo), flags) != 0 ? new string(info.TypeName) : "";
    }

    public static string? OpensWith(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        if (ext.Length == 0 || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var buffer = stackalloc char[260];
        uint length = 260;
        return AssocQueryStringW(AssocfNoTruncate | AssocfInitIgnoreUnknown, AssocStrFriendlyAppName, ext, null, buffer, &length) == 0 && length > 1
            ? new string(buffer)
            : null;
    }

    public static long SizeOnDisk(string path, long size)
    {
        uint high;
        var low = GetCompressedFileSizeW(WindowsPath.ToExtended(path), &high);
        var actual = low == uint.MaxValue && Marshal.GetLastPInvokeError() != 0 ? size : ((long)high << 32) | low;
        var cluster = ClusterSize(path);
        return cluster <= 0 ? actual : (actual + cluster - 1) / cluster * cluster;
    }

    public static long Allocated(string path)
    {
        uint high;
        var low = GetCompressedFileSizeW(WindowsPath.ToExtended(path), &high);
        return low == uint.MaxValue && Marshal.GetLastPInvokeError() != 0 ? -1 : ((long)high << 32) | low;
    }

    private static readonly Dictionary<string, long> _clusters = new(StringComparer.OrdinalIgnoreCase);

    private static long ClusterSize(string path)
    {
        var root = WindowsPath.GetRoot(path);
        if (root is null)
        {
            return 0;
        }

        lock (_clusters)
        {
            if (_clusters.TryGetValue(root, out var known))
            {
                return known;
            }
        }

        var cluster = GetDiskFreeSpaceW(root.EndsWith('\\') ? root : root + "\\", out var sectorsPerCluster, out var bytesPerSector, out _, out _)
            ? (long)sectorsPerCluster * bytesPerSector
            : 0;
        lock (_clusters)
        {
            _clusters[root] = cluster;
        }

        return cluster;
    }

    public static bool IsFromInternet(string path)
    {
        try
        {
            using var reader = new StreamReader(WindowsPath.ToExtended(path) + ":Zone.Identifier");
            for (var i = 0; i < 20 && reader.ReadLine() is { } line; i++)
            {
                if (line.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase))
                {
                    return line.AsSpan(7).Trim() is "3" or "4";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    public static void Unblock(string path) => File.Delete(WindowsPath.ToExtended(path) + ":Zone.Identifier");

    public static void SetSimpleAttributes(string path, FileAttributes simple)
    {
        const FileAttributes Mask = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive | FileAttributes.System;
        var extended = WindowsPath.ToExtended(path);
        var current = File.GetAttributes(extended);
        var next = (current & ~Mask) | (simple & Mask);
        if (next != current)
        {
            File.SetAttributes(extended, next & ~FileAttributes.Directory);
        }
    }

    public static FolderCount CountFolder(string path, Action<FolderCount> progress, CancellationToken cancel)
    {
        long files = 0, folders = 0, bytes = 0, onDisk = 0;
        var cluster = ClusterSize(path);
        var pending = new Stack<string>();
        pending.Push(WindowsPath.ToExtended(path));
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };
        var last = Stopwatch.StartNew();
        while (pending.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (entry is DirectoryInfo d)
                {
                    folders++;
                    if (!d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        pending.Push(d.FullName);
                    }
                }
                else if (entry is FileInfo f)
                {
                    files++;
                    bytes += f.Length;
                    onDisk += cluster <= 0 ? f.Length : (f.Length + cluster - 1) / cluster * cluster;
                }

                if (last.ElapsedMilliseconds >= 150)
                {
                    progress(new FolderCount(files, folders, bytes, onDisk, false));
                    last.Restart();
                }
            }
        }

        var result = new FolderCount(files, folders, bytes, onDisk, true);
        progress(result);
        return result;
    }

    private const uint ShgfiTypeName = 0x400;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint AssocfNoTruncate = 0x20;
    private const uint AssocfInitIgnoreUnknown = 0x400;
    private const uint AssocStrFriendlyAppName = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        public fixed char DisplayName[260];
        public fixed char TypeName[80];
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SHGetFileInfoW(string path, uint attributes, ShFileInfo* info, uint size, uint flags);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AssocQueryStringW(uint flags, uint str, string assoc, string? extra, char* output, uint* length);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetCompressedFileSizeW(string path, uint* high);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
}
