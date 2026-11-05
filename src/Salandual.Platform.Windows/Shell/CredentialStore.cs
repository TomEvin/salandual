// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/CredentialStore.cs

using System.Runtime.InteropServices;
using System.Text;
using Salandual.Core.Settings;

namespace Salandual.Platform.Windows.Shell;

public sealed unsafe partial class CredentialStore : ISecretStore
{
    private const uint Generic = 1, LocalMachine = 2;

    public const int MaxSecretChars = 1024;

    public string? Read(string target)
    {
        if (!CredReadW(target, Generic, 0, out var credential))
        {
            return null;
        }

        try
        {
            var c = (Credential*)credential;
            return c->CredentialBlobSize == 0 ? "" : Encoding.Unicode.GetString(c->CredentialBlob, (int)c->CredentialBlobSize);
        }
        finally
        {
            CredFree(credential);
        }
    }

    public bool Write(string target, string user, string secret)
    {
        if (secret.Length > MaxSecretChars)
        {
            return false;
        }

        var bytes = Encoding.Unicode.GetBytes(secret);
        fixed (char* name = target)
        fixed (char* userName = user)
        fixed (byte* blob = bytes)
        {
            var credential = new Credential
            {
                Type = Generic,
                TargetName = name,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = LocalMachine,
                UserName = userName,
            };
            try
            {
                return CredWriteW(&credential, 0);
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
    }

    public bool Delete(string target) => CredDeleteW(target, Generic, 0);

    public IReadOnlyList<string> List(string prefix)
    {
        if (!CredEnumerateW(prefix + "*", 0, out var count, out var list))
        {
            return [];
        }

        try
        {
            var names = new List<string>();
            var items = (Credential**)list;
            for (var i = 0; i < count; i++)
            {
                if (items[i]->Type == Generic && new string(items[i]->TargetName) is { } name && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }

            return names;
        }
        finally
        {
            CredFree(list);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public char* TargetName;
        public char* Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public byte* CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public char* TargetAlias;
        public char* UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredReadW(string target, uint type, uint flags, out nint credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWriteW(Credential* credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDeleteW(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredEnumerateW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredEnumerateW(string filter, uint flags, out int count, out nint credentials);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void CredFree(nint buffer);
}
