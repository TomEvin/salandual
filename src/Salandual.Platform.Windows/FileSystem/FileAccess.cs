// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/FileAccess.cs

using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Salandual.Core.Resources;

namespace Salandual.Platform.Windows.FileSystem;

public enum AccessLevel { FullControl, Change, ReadAndRun, Read, Write, Special }

public sealed record AccessEntry(string Who, bool IsYou, AccessLevel Level, bool Allowed, bool Inherited);

public sealed record AccessInfo(string? Owner, IReadOnlyList<AccessEntry> Entries, bool CanRead, bool CanChange, bool CanDelete, bool CanChangePermissions);

public static unsafe partial class FileAccessReader
{
    public static AccessInfo Read(string path)
    {
        var extended = WindowsPath.ToExtended(path);
        var isDir = Directory.Exists(extended);
        FileSystemSecurity security = isDir
            ? new DirectoryInfo(extended).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)
            : new FileInfo(extended).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);

        using var me = WindowsIdentity.GetCurrent();
        var mySids = new HashSet<SecurityIdentifier>([me.User!, .. me.Groups?.OfType<SecurityIdentifier>() ?? []]);
        var entries = new List<AccessEntry>();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly))
            {
                continue;
            }

            var sid = (SecurityIdentifier)rule.IdentityReference;
            entries.Add(new AccessEntry(Name(sid), sid == me.User, Level(rule.FileSystemRights), rule.AccessControlType == AccessControlType.Allow, rule.IsInherited));
        }

        var merged = entries
            .GroupBy(e => (e.Who, e.Allowed, e.Inherited))
            .Select(g => g.OrderBy(e => e.Level).First())
            .OrderBy(e => e.Allowed)
            .ThenBy(e => e.Inherited)
            .ToList();

        string? owner = null;
        try
        {
            owner = security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier o ? Name(o) : null;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
        }

        var granted = Granted(extended);
        var parent = WindowsPath.GetParent(path);
        var parentGranted = parent is null ? 0u : Granted(WindowsPath.ToExtended(parent));
        return new AccessInfo(
            owner,
            merged,
            (granted & FileGenericRead) == FileGenericRead,
            (granted & FileGenericWrite) == FileGenericWrite,
            (granted & Delete) != 0 || (parentGranted & FileDeleteChild) != 0,
            (granted & WriteDac) != 0);
    }

    private static string Name(SecurityIdentifier sid)
    {
        try
        {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return sid.Value;
        }
    }

    public static AccessLevel Level(FileSystemRights rights)
    {
        var mask = (uint)rights;
        if ((mask & GenericAll) != 0)
        {
            mask |= (uint)FileSystemRights.FullControl;
        }

        if ((mask & GenericRead) != 0)
        {
            mask |= (uint)FileSystemRights.Read;
        }

        if ((mask & GenericWrite) != 0)
        {
            mask |= (uint)FileSystemRights.Write;
        }

        if ((mask & GenericExecute) != 0)
        {
            mask |= (uint)FileSystemRights.ReadAndExecute;
        }

        bool Has(FileSystemRights r) => (mask & (uint)r) == (uint)r;
        return Has(FileSystemRights.FullControl) ? AccessLevel.FullControl
            : Has(FileSystemRights.Modify) ? AccessLevel.Change
            : Has(FileSystemRights.ReadAndExecute) ? AccessLevel.ReadAndRun
            : Has(FileSystemRights.Read) ? AccessLevel.Read
            : Has(FileSystemRights.Write) ? AccessLevel.Write
            : AccessLevel.Special;
    }

    public static string LevelText(AccessLevel level) => level switch
    {
        AccessLevel.FullControl => Strings.Access_FullControl,
        AccessLevel.Change => Strings.Access_Change,
        AccessLevel.ReadAndRun => Strings.Access_ReadAndRun,
        AccessLevel.Read => Strings.Access_Read,
        AccessLevel.Write => Strings.Access_Write,
        _ => Strings.Access_Special,
    };

    private static uint Granted(string extendedPath)
    {
        uint needed = 0;
        const uint What = OwnerSecurityInformation | GroupSecurityInformation | DaclSecurityInformation;
        _ = GetFileSecurityW(extendedPath, What, null, 0, &needed);
        if (needed == 0)
        {
            return 0;
        }

        var descriptor = new byte[needed];
        nint process = 0, token = 0;
        fixed (byte* sd = descriptor)
        {
            if (!GetFileSecurityW(extendedPath, What, sd, needed, &needed))
            {
                return 0;
            }

            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TokenQuery | TokenDuplicate, &process) || !DuplicateToken(process, 2 , &token))
                {
                    return 0;
                }

                var mapping = new GenericMapping { Read = FileGenericRead, Write = FileGenericWrite, Execute = FileGenericExecute, All = FileAllAccess };
                var privileges = stackalloc byte[256];
                uint privilegesLength = 256, granted = 0;
                int status = 0;
                return AccessCheck(sd, token, MaximumAllowed, &mapping, privileges, &privilegesLength, &granted, &status) && status != 0 ? granted : 0;
            }
            finally
            {
                if (token != 0)
                {
                    CloseHandle(token);
                }

                if (process != 0)
                {
                    CloseHandle(process);
                }
            }
        }
    }

    private const uint GenericAll = 0x10000000;
    private const uint GenericExecute = 0x20000000;
    private const uint GenericWrite = 0x40000000;
    private const uint GenericRead = 0x80000000;
    private const uint FileGenericRead = 0x120089;
    private const uint FileGenericWrite = 0x120116;
    private const uint FileGenericExecute = 0x1200A0;
    private const uint FileAllAccess = 0x1F01FF;
    private const uint FileDeleteChild = 0x40;
    private const uint Delete = 0x10000;
    private const uint WriteDac = 0x40000;
    private const uint MaximumAllowed = 0x02000000;
    private const uint OwnerSecurityInformation = 0x1;
    private const uint GroupSecurityInformation = 0x2;
    private const uint DaclSecurityInformation = 0x4;
    private const uint TokenQuery = 0x8;
    private const uint TokenDuplicate = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct GenericMapping
    {
        public uint Read;
        public uint Write;
        public uint Execute;
        public uint All;
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileSecurityW(string path, uint what, byte* descriptor, uint length, uint* needed);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, nint* token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateToken(nint token, int level, nint* duplicate);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AccessCheck(byte* descriptor, nint token, uint desired, GenericMapping* mapping, byte* privileges, uint* privilegesLength, uint* granted, int* status);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
