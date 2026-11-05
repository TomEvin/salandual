// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/WindowsSearchIndex.cs

using System.Runtime.InteropServices;
using Salandual.Core.QuickOpen;

namespace Salandual.Platform.Windows.FileSystem;

public sealed unsafe partial class WindowsSearchIndex : IFileNameIndex
{
    private const int MaxPath = 1024;

    public FileIndexKind Kind => FileIndexKind.WindowsIndex;

    public bool IsAvailable()
    {
        var manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager == 0)
        {
            return false;
        }

        try
        {
            var service = OpenServiceW(manager, "WSearch", ServiceQueryStatus);
            if (service == 0)
            {
                return false;
            }

            try
            {
                ServiceStatus status;
                return QueryServiceStatus(service, &status) && status.CurrentState == ServiceRunning;
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public IReadOnlyList<string> CoveredFolders()
    {
        var found = new List<string>();
        var initialized = CoInitializeEx(null, CoinitMultithreaded) >= 0;
        void* manager = null, catalog = null, scopes = null, rules = null;
        try
        {
            var clsid = ClsidSearchManager;
            var iid = IidSearchManager;
            if (CoCreateInstance(&clsid, null, ClsctxAll, &iid, &manager) < 0 || manager is null)
            {
                return found;
            }

            fixed (char* name = "SystemIndex")
            {
                if (((delegate* unmanaged[Stdcall]<void*, char*, void**, int>)Vtbl(manager)[10])(manager, name, &catalog) < 0 || catalog is null)
                {
                    return found;
                }
            }

            if (((delegate* unmanaged[Stdcall]<void*, void**, int>)Vtbl(catalog)[28])(catalog, &scopes) < 0 || scopes is null
                || ((delegate* unmanaged[Stdcall]<void*, void**, int>)Vtbl(scopes)[10])(scopes, &rules) < 0 || rules is null)
            {
                return found;
            }

            var excluded = new List<string>();
            while (true)
            {
                void* rule = null;
                uint fetched = 0;
                if (((delegate* unmanaged[Stdcall]<void*, uint, void**, uint*, int>)Vtbl(rules)[3])(rules, 1, &rule, &fetched) != 0 || fetched == 0 || rule is null)
                {
                    break;
                }

                try
                {
                    char* url = null;
                    int included = 0;
                    if (((delegate* unmanaged[Stdcall]<void*, char**, int>)Vtbl(rule)[3])(rule, &url) >= 0 && url is not null)
                    {
                        var text = new string(url);
                        CoTaskMemFree(url);
                        if (((delegate* unmanaged[Stdcall]<void*, int*, int>)Vtbl(rule)[4])(rule, &included) >= 0 && FolderOf(text) is { } folder)
                        {
                            (included != 0 ? found : excluded).Add(folder);
                        }
                    }
                }
                finally
                {
                    Release(rule);
                }
            }

            return [.. found.Where(f => !excluded.Contains(f, StringComparer.OrdinalIgnoreCase) && Directory.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
        finally
        {
            Release(rules);
            Release(scopes);
            Release(catalog);
            Release(manager);
            if (initialized)
            {
                CoUninitialize();
            }
        }
    }

    internal static string? FolderOf(string url)
    {
        const string File = "file:///";
        if (!url.StartsWith(File, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = url[File.Length..].Replace('/', '\\');
        if (path.EndsWith('*'))
        {
            path = path[..^1];
        }

        path = path.Length > 3 ? path.TrimEnd('\\') : path;
        return path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' && path.IndexOfAny(['*', '?', '{']) < 0 ? path : null;
    }

    public IReadOnlyList<WalkedEntry> Find(string typed, int max, CancellationToken cancel)
    {
        if (FileNamePattern.Letters(typed).Length == 0 || cancel.IsCancellationRequested)
        {
            return [];
        }

        var sql = $"SELECT TOP {Math.Clamp(max, 1, 5000)} System.ItemPathDisplay, System.FileAttributes FROM SystemIndex WHERE SCOPE='file:' AND System.FileName LIKE {FileNamePattern.Like(typed)}";
        var found = new List<WalkedEntry>();
        var initialized = CoInitializeEx(null, CoinitMultithreaded) >= 0;
        void* init = null, session = null, command = null, rowset = null, accessor = null;
        nuint handle = 0;
        try
        {
            Guid clsid;
            if (CLSIDFromProgID("Search.CollatorDSO", &clsid) < 0)
            {
                return found;
            }

            var iid = IidDbInitialize;
            if (CoCreateInstance(&clsid, null, ClsctxInproc, &iid, &init) < 0 || init is null
                || ((delegate* unmanaged[Stdcall]<void*, int>)Vtbl(init)[3])(init) < 0)
            {
                return found;
            }

            void* creator = null;
            iid = IidDbCreateSession;
            if (QueryInterface(init, &iid, &creator) < 0)
            {
                return found;
            }

            iid = IidDbCreateCommand;
            var hr = ((delegate* unmanaged[Stdcall]<void*, void*, Guid*, void**, int>)Vtbl(creator)[3])(creator, null, &iid, &session);
            Release(creator);
            iid = IidCommandText;
            if (hr < 0 || session is null
                || ((delegate* unmanaged[Stdcall]<void*, void*, Guid*, void**, int>)Vtbl(session)[3])(session, null, &iid, &command) < 0 || command is null)
            {
                return found;
            }

            var dialect = DbGuidDefault;
            fixed (char* text = sql)
            {
                if (((delegate* unmanaged[Stdcall]<void*, Guid*, char*, int>)Vtbl(command)[7])(command, &dialect, text) < 0)
                {
                    return found;
                }
            }

            iid = IidRowset;
            nint affected = 0;
            if (cancel.IsCancellationRequested
                || ((delegate* unmanaged[Stdcall]<void*, void*, Guid*, void*, nint*, void**, int>)Vtbl(command)[4])(command, null, &iid, null, &affected, &rowset) < 0 || rowset is null)
            {
                return found;
            }

            iid = IidAccessor;
            if (QueryInterface(rowset, &iid, &accessor) < 0)
            {
                return found;
            }

            const int PathAt = 0, AttrAt = 16 + (2 * MaxPath), RowSize = AttrAt + 24;
            var bindings = stackalloc DbBinding[2];
            bindings[0] = new DbBinding { Ordinal = 1, ObStatus = PathAt, ObLength = PathAt + 8, ObValue = PathAt + 16, Part = DbPartAll, MaxLen = 2 * MaxPath, Type = DbTypeWStr };
            bindings[1] = new DbBinding { Ordinal = 2, ObStatus = AttrAt, ObLength = AttrAt + 8, ObValue = AttrAt + 16, Part = DbPartAll, MaxLen = 4, Type = DbTypeUi4 };
            var bindStatus = stackalloc uint[2];
            if (((delegate* unmanaged[Stdcall]<void*, uint, nuint, DbBinding*, nuint, nuint*, uint*, int>)Vtbl(accessor)[4])(accessor, DbAccessorRowData, 2, bindings, RowSize, &handle, bindStatus) < 0)
            {
                return found;
            }

            var rows = stackalloc nuint[64];
            var row = stackalloc byte[RowSize];
            while (!cancel.IsCancellationRequested && found.Count < max)
            {
                nuint got = 0;
                var rowsAt = rows;
                hr = ((delegate* unmanaged[Stdcall]<void*, nuint, nint, nint, nuint*, nuint**, int>)Vtbl(rowset)[5])(rowset, 0, 0, 64, &got, &rowsAt);
                if (hr < 0 || got == 0)
                {
                    break;
                }

                for (nuint i = 0; i < got; i++)
                {
                    new Span<byte>(row, RowSize).Clear();
                    if (((delegate* unmanaged[Stdcall]<void*, nuint, nuint, void*, int>)Vtbl(rowset)[4])(rowset, rowsAt[i], handle, row) < 0
                        || *(uint*)(row + PathAt) != DbStatusOk)
                    {
                        continue;
                    }

                    var length = (int)Math.Min(*(nuint*)(row + PathAt + 8), 2 * MaxPath) / 2;
                    var path = new string((char*)(row + PathAt + 16), 0, length);
                    var attributes = *(uint*)(row + AttrAt) == DbStatusOk ? (FileAttributes)(*(uint*)(row + AttrAt + 16)) : 0;
                    var name = Path.GetFileName(path);
                    var folder = Path.GetDirectoryName(path);
                    if (name.Length > 0 && folder is { Length: > 0 } && Path.IsPathFullyQualified(path))
                    {
                        found.Add(new WalkedEntry(name, folder, attributes.HasFlag(FileAttributes.Directory),
                            (attributes & (FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000)) != 0));
                    }
                }

                ((delegate* unmanaged[Stdcall]<void*, nuint, nuint*, void*, void*, void*, int>)Vtbl(rowset)[6])(rowset, got, rowsAt, null, null, null);
                if (rowsAt != rows)
                {
                    CoTaskMemFree(rowsAt);
                }

                if (hr != 0)
                {
                    break;
                }
            }

            return found;
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            return found;
        }
        finally
        {
            if (handle != 0 && accessor is not null)
            {
                ((delegate* unmanaged[Stdcall]<void*, nuint, uint*, int>)Vtbl(accessor)[6])(accessor, handle, null);
            }

            Release(accessor);
            Release(rowset);
            Release(command);
            Release(session);
            Release(init);
            if (initialized)
            {
                CoUninitialize();
            }
        }
    }

    private static void** Vtbl(void* com) => *(void***)com;

    private static int QueryInterface(void* com, Guid* iid, void** result)
        => ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)Vtbl(com)[0])(com, iid, result);

    private static void Release(void* com)
    {
        if (com is not null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)Vtbl(com)[2])(com);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DbBinding
    {
        public nuint Ordinal;
        public nuint ObValue;
        public nuint ObLength;
        public nuint ObStatus;
        public nint TypeInfo;
        public nint Object;
        public nint BindExt;
        public uint Part;
        public uint MemOwner;
        public uint ParamIo;
        public nuint MaxLen;
        public uint Flags;
        public ushort Type;
        public byte Precision;
        public byte Scale;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    private const uint CoinitMultithreaded = 0, ClsctxInproc = 1, ClsctxAll = 1 | 4;
    private const uint DbPartAll = 1 | 2 | 4, DbAccessorRowData = 2, DbStatusOk = 0;
    private const ushort DbTypeWStr = 130, DbTypeUi4 = 19;
    private const uint ScManagerConnect = 1, ServiceQueryStatus = 4, ServiceRunning = 4;

    private static readonly Guid ClsidSearchManager = new("7D096C5F-AC08-4F1F-BEB7-5C22C517CE39");
    private static readonly Guid IidSearchManager = new("AB310581-AC80-11D1-8DF3-00C04FB6EF69");
    private static readonly Guid IidDbInitialize = new("0C733A8B-2A1C-11CE-ADE5-00AA0044773D");
    private static readonly Guid IidDbCreateSession = new("0C733A5D-2A1C-11CE-ADE5-00AA0044773D");
    private static readonly Guid IidDbCreateCommand = new("0C733A1D-2A1C-11CE-ADE5-00AA0044773D");
    private static readonly Guid IidCommandText = new("0C733A27-2A1C-11CE-ADE5-00AA0044773D");
    private static readonly Guid IidRowset = new("0C733A7C-2A1C-11CE-ADE5-00AA0044773D");
    private static readonly Guid IidAccessor = new("0C733A8C-2A1C-11CE-ADE5-00AA0044773D");
    private static readonly Guid DbGuidDefault = new("C8B521FB-5CF3-11CE-ADE5-00AA0044773D");

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoInitializeEx(void* reserved, uint flags);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** result);

    [LibraryImport("ole32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CLSIDFromProgID(string progId, Guid* clsid);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void CoTaskMemFree(void* p);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint OpenSCManagerW(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint OpenServiceW(nint manager, string name, uint access);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatus(nint service, ServiceStatus* status);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);
}
