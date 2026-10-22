// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/LocalOperations.cs

using System.Globalization;
using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Salandual.Core.Operations;
using Salandual.Core.Resources;

namespace Salandual.Platform.Windows.FileSystem;

public sealed unsafe partial class LocalOperations : IOperationFileSystem
{
    private const long UnbufferedFrom = 256L * 1024 * 1024;

    private static readonly EnumerationOptions _options = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        BufferSize = 64 * 1024,
    };

    public Func<nint>? OwnerWindow { get; set; }

    public string Combine(string directory, string name) => WindowsPath.Join(directory, name);

    public string GetName(string path) => WindowsPath.GetName(path);

    public string? GetParent(string path) => WindowsPath.GetParent(path);

    public string VolumeOf(string path)
    {
        var root = WindowsPath.GetRoot(path) ?? path;
        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var server = root.IndexOf('\\', 2);
            return (server < 0 ? root : root[..server]).ToLowerInvariant();
        }

        return root.ToUpperInvariant();
    }

    public ItemInfo? Stat(string path)
    {
        if (!GetFileAttributesExW(WindowsPath.ToExtended(path), 0, out var data))
        {
            var error = Marshal.GetLastPInvokeError();
            return error is 2 or 3 ? null : throw Win32.Error(error);
        }

        return new ItemInfo(
            WindowsPath.GetName(path),
            (FileAttributes)data.Attributes,
            (long)(((ulong)data.SizeHigh << 32) | data.SizeLow),
            DateTime.FromFileTimeUtc((long)(((ulong)data.CreatedHigh << 32) | data.CreatedLow)),
            DateTime.FromFileTimeUtc((long)(((ulong)data.WrittenHigh << 32) | data.WrittenLow)));
    }

    public VolumeFacts Describe(string path)
    {
        var root = WindowsPath.GetRoot(path);
        if (root is null)
        {
            return VolumeFacts.Unknown;
        }

        long? free = null;
        if (GetDiskFreeSpaceExW(WindowsPath.Join(WindowsPath.FromExtended(path), ""), out var available, out var total, out _))
        {
            free = DiskSpace.IsMadeUp(total > long.MaxValue ? long.MaxValue : (long)total) ? null : (long)available;
        }

        string? fileSystem = null;
        var readOnly = false;
        var label = stackalloc char[261];
        var fs = stackalloc char[261];
        var volumeRoot = root.EndsWith('\\') ? root : root + "\\";
        if (GetVolumeInformationW(volumeRoot, label, 261, out _, out _, out var flags, fs, 261))
        {
            fileSystem = new string(fs);
            readOnly = (flags & FileReadOnlyVolume) != 0;
        }

        return new VolumeFacts(free, fileSystem, readOnly, MaxExtendedPath);
    }

    private static Exception InUse(Exception error, params string[] paths)
    {
        if (error is IOException io && (io.HResult & 0xFFFF) is 32 or 33 && LockingPrograms.Describe(paths) is { } message)
        {
            return new IOException(message, io.HResult);
        }

        return error;
    }

    public IReadOnlyList<ItemInfo> List(string directory, CancellationToken cancellationToken)
    {
        var count = 0;
        try
        {
            return new FileSystemEnumerable<ItemInfo>(WindowsPath.ToExtended(directory), (ref FileSystemEntry e) =>
            {
                if ((++count & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return new ItemInfo(e.FileName.ToString(), e.Attributes, e.IsDirectory ? 0 : e.Length, e.CreationTimeUtc.UtcDateTime, e.LastWriteTimeUtc.UtcDateTime);
            }, _options).ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            throw Win32.Error(3);
        }
    }

    public void CreateDirectory(string path)
    {
        if (!CreateDirectoryW(WindowsPath.ToExtended(path), IntPtr.Zero))
        {
            throw Win32.Error(Marshal.GetLastPInvokeError());
        }
    }

    public void CopyFile(string source, string target, bool overwrite, Action<long> progress)
    {
        var state = new CopyState(progress);
        var handle = GCHandle.Alloc(state);
        try
        {
            var size = Stat(source)?.Size ?? 0;
            var parameters = new CopyFile2ExtendedParameters
            {
                Size = (uint)sizeof(CopyFile2ExtendedParameters),
                CopyFlags = (overwrite ? 0u : CopyFileFailIfExists) | (size >= UnbufferedFrom ? CopyFileNoBuffering : 0u),
                ProgressRoutine = &OnCopyProgress,
                CallbackContext = GCHandle.ToIntPtr(handle),
            };

            var hr = CopyFile2(WindowsPath.ToExtended(source), WindowsPath.ToExtended(target), &parameters);
            if (state.Error is { } error)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
            }

            if (hr < 0)
            {
                throw hr == RequestAborted ? new OperationCanceledException() : InUse(Win32.FromHResult(hr), source, target);
            }
        }
        finally
        {
            handle.Free();
        }
    }

    public void Move(string source, string target, bool replace)
    {
        if (!MoveFileExW(WindowsPath.ToExtended(source), WindowsPath.ToExtended(target), replace ? MoveFileReplaceExisting : 0))
        {
            throw InUse(Win32.Error(Marshal.GetLastPInvokeError()), source, target);
        }
    }

    public void DeleteFile(string path)
    {
        if (!DeleteFileW(WindowsPath.ToExtended(path)))
        {
            throw InUse(Win32.Error(Marshal.GetLastPInvokeError()), path);
        }
    }

    public void DeleteDirectory(string path)
    {
        if (!RemoveDirectoryW(WindowsPath.ToExtended(path)))
        {
            throw Win32.Error(Marshal.GetLastPInvokeError());
        }
    }

    public void SetAttributes(string path, FileAttributes attributes)
    {
        if (!SetFileAttributesW(WindowsPath.ToExtended(path), (uint)attributes))
        {
            throw Win32.Error(Marshal.GetLastPInvokeError());
        }
    }

    public void SetTimes(string path, DateTime createdUtc, DateTime lastWriteUtc)
    {
        using var handle = CreateFileW(WindowsPath.ToExtended(path), FileWriteAttributes, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw Win32.Error(Marshal.GetLastPInvokeError());
        }

        var created = createdUtc.ToFileTimeUtc();
        var written = lastWriteUtc.ToFileTimeUtc();
        if (!SetFileTime(handle, &created, null, &written))
        {
            throw Win32.Error(Marshal.GetLastPInvokeError());
        }
    }

    public void RemoveStreams(string path)
    {
        var extended = WindowsPath.ToExtended(path);
        var names = new List<string>();
        FindStreamData data;
        var find = FindFirstStreamW(extended, 0, &data, 0);
        if (find == -1)
        {
            return;
        }

        try
        {
            do
            {
                var name = new string(data.StreamName);
                if (name != "::$DATA")
                {
                    names.Add(name);
                }
            }
            while (FindNextStreamW(find, &data));
        }
        finally
        {
            FindClose(find);
        }

        foreach (var name in names)
        {
            if (!DeleteFileW(extended + name))
            {
                throw Win32.Error(Marshal.GetLastPInvokeError());
            }
        }
    }

    public bool ContentEquals(string a, string b, CancellationToken cancellationToken)
    {
        const int Chunk = 1024 * 1024;
        using var fa = new FileStream(WindowsPath.ToExtended(a), FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        using var fb = new FileStream(WindowsPath.ToExtended(b), FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        if (fa.Length != fb.Length)
        {
            return false;
        }

        var ba = new byte[Chunk];
        var bb = new byte[Chunk];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var na = fa.ReadAtLeast(ba, Chunk, throwOnEndOfStream: false);
            var nb = fb.ReadAtLeast(bb, Chunk, throwOnEndOfStream: false);
            if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb)))
            {
                return false;
            }

            if (na == 0)
            {
                return true;
            }
        }
    }

    public bool CanRecycle(string path, out string reason)
    {
        reason = "";
        var display = WindowsPath.FromExtended(path);
        var root = WindowsPath.GetRoot(display);
        if (root is null || root.StartsWith(@"\\", StringComparison.Ordinal) || GetDriveTypeW(root) == DriveRemote)
        {
            reason = Strings.Recycle_NetworkDrive;
            return false;
        }

        if (GetDriveTypeW(root) == DriveRemovable)
        {
            reason = Strings.Recycle_RemovableDrive;
            return false;
        }

        if (display.Length >= 260)
        {
            reason = Strings.Recycle_PathTooLong;
            return false;
        }

        foreach (var part in display[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.EndsWith('.') || part.EndsWith(' '))
            {
                reason = Strings.Recycle_TrailingDotOrSpace;
                return false;
            }

            if (FileNames.IsReserved(part))
            {
                reason = Strings.Recycle_ReservedName;
                return false;
            }
        }

        return true;
    }

    public RecycleResult Recycle(string path)
    {
        try
        {
            return RecycleBin.Recycle(WindowsPath.FromExtended(path), OwnerWindow?.Invoke() ?? 0);
        }
        catch (IOException ex)
        {
            throw InUse(ex, path);
        }
    }

    private sealed class CopyState(Action<long> progress)
    {
        public Action<long> Progress { get; } = progress;

        public Exception? Error { get; set; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnCopyProgress(CopyFile2Message* message, IntPtr context)
    {
        var state = (CopyState)GCHandle.FromIntPtr(context).Target!;
        try
        {
            switch (message->Type)
            {
                case MessageChunkFinished:
                    state.Progress((long)message->ChunkTotalBytesTransferred);
                    break;
                case MessageStreamFinished:
                    state.Progress((long)message->StreamTotalBytesTransferred);
                    break;
            }

            return ProgressContinue;
        }
        catch (Exception ex)
        {
            state.Error = ex;
            return ProgressCancel;
        }
    }

    private const uint CopyFileFailIfExists = 0x1;
    private const uint CopyFileNoBuffering = 0x1000;
    private const int MessageChunkFinished = 2;
    private const int MessageStreamFinished = 4;
    private const int ProgressContinue = 0;
    private const int ProgressCancel = 1;
    private const int RequestAborted = unchecked((int)0x800704D3);
    private const uint MoveFileReplaceExisting = 0x1;
    private const uint FileWriteAttributes = 0x100;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint DriveRemovable = 2;
    private const uint DriveRemote = 4;
    private const uint FileReadOnlyVolume = 0x00080000;
    private const int MaxExtendedPath = 32000;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceExW(string directoryName, out ulong freeBytesAvailableToCaller, out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformationW(string rootPathName, char* volumeName, int volumeNameSize, out uint serialNumber, out uint maxComponentLength, out uint fileSystemFlags, char* fileSystemName, int fileSystemNameSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyFile2ExtendedParameters
    {
        public uint Size;
        public uint CopyFlags;
        public int* Cancel;
        public delegate* unmanaged[Stdcall]<CopyFile2Message*, IntPtr, int> ProgressRoutine;
        public IntPtr CallbackContext;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct CopyFile2Message
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(72)] public ulong ChunkTotalBytesTransferred;
        [FieldOffset(56)] public ulong StreamTotalBytesTransferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeData
    {
        public uint Attributes;
        public uint CreatedLow, CreatedHigh;
        public uint AccessedLow, AccessedHigh;
        public uint WrittenLow, WrittenHigh;
        public uint SizeHigh, SizeLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FindStreamData
    {
        public long StreamSize;
        public fixed char StreamName[296];
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CopyFile2(string existingFileName, string newFileName, CopyFile2ExtendedParameters* extendedParameters);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileAttributesExW(string fileName, int infoLevel, out FileAttributeData data);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectoryW(string pathName, IntPtr securityAttributes);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileExW(string existingFileName, string newFileName, uint flags);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteFileW(string fileName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveDirectoryW(string pathName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileAttributesW(string fileName, uint attributes);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileTime(Microsoft.Win32.SafeHandles.SafeFileHandle file, long* creationTime, long* lastAccessTime, long* lastWriteTime);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint FindFirstStreamW(string fileName, int infoLevel, FindStreamData* data, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindNextStreamW(nint find, FindStreamData* data);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindClose(nint find);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveTypeW(string rootPathName);
}

internal static class Win32
{
    public static Exception Error(int code)
    {
        var message = code switch
        {
            2 => Strings.Issue_Gone,
            3 => Strings.Win_FolderGone,
            5 => Strings.Win_AccessDenied,
            15 or 21 => Strings.Win_NotReady,
            19 => Strings.Win_WriteProtected,
            32 => Strings.Win_InUse,
            33 => Strings.Win_Locked,
            39 or 112 => Strings.Win_DiskFull,
            53 or 64 or 67 or 1231 => Strings.Win_NetworkUnreachable,
            80 or 183 => Strings.Win_AlreadyExists,
            123 => Strings.Win_InvalidName,
            145 => Strings.Win_FolderNotEmpty,
            206 => Strings.Win_PathTooLong,
            1920 => Strings.Win_CantOpenOnlineOnly,
            _ => Loc.Format(Strings.Win_OtherError, Marshal.GetPInvokeErrorMessage(code).TrimEnd('.', '\r', '\n'), code),
        };

        return code == 5 ? new UnauthorizedAccessException(message) : new IOException(message, unchecked((int)0x80070000) | code);
    }

    public static Exception FromHResult(int hr)
        => (hr & 0x1FFF0000) == 0x00070000 ? Error(hr & 0xFFFF) : new IOException(Loc.Format(Strings.Win_HResult, hr.ToString("X8", CultureInfo.InvariantCulture)), hr);
}
