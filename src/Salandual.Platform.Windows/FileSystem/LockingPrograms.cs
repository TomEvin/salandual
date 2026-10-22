// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/LockingPrograms.cs

using System.Runtime.InteropServices;
using Salandual.Core.Resources;

namespace Salandual.Platform.Windows.FileSystem;

public static unsafe partial class LockingPrograms
{
    public static IReadOnlyList<string> Of(string path)
    {
        var key = stackalloc char[33];
        if (RmStartSession(out var session, 0, key) != 0)
        {
            return [];
        }

        try
        {
            var display = WindowsPath.FromExtended(path);
            fixed (char* p = display)
            {
                var files = stackalloc char*[1];
                files[0] = p;
                if (RmRegisterResources(session, 1, files, 0, null, 0, null) != 0)
                {
                    return [];
                }
            }

            uint count = 0;
            var result = RmGetList(session, out var needed, ref count, null, out _);
            if (result != ErrorMoreData || needed == 0)
            {
                return [];
            }

            var infos = new ProcessInfo[needed];
            count = needed;
            fixed (ProcessInfo* list = infos)
            {
                if (RmGetList(session, out _, ref count, list, out _) != 0)
                {
                    return [];
                }
            }

            var names = new List<string>();
            for (var i = 0; i < count; i++)
            {
                fixed (char* name = infos[i].AppName)
                {
                    var text = new string(name).Trim();
                    if (text.Length > 0 && !names.Contains(text, StringComparer.OrdinalIgnoreCase))
                    {
                        names.Add(text);
                    }
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return [];
        }
        finally
        {
            _ = RmEndSession(session);
        }
    }

    public static string? Describe(params string[] paths)
    {
        foreach (var path in paths)
        {
            var names = Of(path);
            if (names.Count > 0)
            {
                return Loc.Format(Strings.Win_InUseBy, Join(names));
            }
        }

        return null;
    }

    private static string Join(IReadOnlyList<string> names)
        => names.Count == 1 ? names[0] : Loc.Format(Strings.List_LastTwo, string.Join(Strings.List_Separator, names.Take(names.Count - 1)), names[^1]);

    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        public uint ProcessId;
        public uint StartTimeLow, StartTimeHigh;
        public fixed char AppName[256];
        public fixed char ServiceShortName[64];
        public int ApplicationType;
        public uint AppStatus;
        public uint SessionId;
        public int Restartable;
    }

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmStartSession(out uint session, int flags, char* sessionKey);

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmRegisterResources(uint session, uint fileCount, char** files, uint applicationCount, void* applications, uint serviceCount, char** services);

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmGetList(uint session, out uint needed, ref uint count, ProcessInfo* list, out uint rebootReasons);

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmEndSession(uint session);
}
