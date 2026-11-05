// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/Shell/WindowsShellPlaces.cs

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Salandual.Core.Resources;
using Salandual.Core.Vfs;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Platform.Windows.Shell;

public sealed record RecycledItem(string OriginalPath, long Size, DateTime DeletedUtc);

public sealed partial class WindowsShellPlaces : IShellPlaces
{
    private static readonly string[] _yourFolders = ["Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos"];

    public async Task<IReadOnlyList<PlaceItem>> ListAsync(string place, CancellationToken cancellationToken = default)
    {
        if (ShellPlace.IsServer(place))
        {
            return await Task.Run(() => ServerShares(place), cancellationToken).ConfigureAwait(false);
        }

        var root = ShellPlace.RootOf(place);
        var inner = ShellPlace.Inner(place);
        return root switch
        {
            ShellPlace.ThisPc when inner.Length == 0 => await ThisPcAsync().ConfigureAwait(false),
            ShellPlace.Home when inner.Length == 0 => await ShellThread.Run(() => Enumerate(ShellPlace.Home, HomeItemAt)).ConfigureAwait(false),
            ShellPlace.Libraries => await Task.Run(() => inner.Length == 0 ? LibraryList() : LibraryFolders(inner), cancellationToken).ConfigureAwait(false),
            ShellPlace.RecycleBin when inner.Length == 0 => await Task.Run(RecycleBinItems, cancellationToken).ConfigureAwait(false),
            ShellPlace.Network when inner.Length == 0 => await NetworkAsync(cancellationToken).ConfigureAwait(false),
            ShellPlace.Shares when inner.Length == 0 => await Task.Run(LocalShares, cancellationToken).ConfigureAwait(false),
            _ => throw new IOException(Strings.Place_CantOpen),
        };
    }

    private static async Task<IReadOnlyList<PlaceItem>> ThisPcAsync()
    {
        var items = new List<PlaceItem>();
        foreach (var id in _yourFolders)
        {
            if (Drives.KnownFolder(id) is { } path)
            {
                items.Add(new PlaceItem(new FileEntry(WindowsPath.GetName(path), 0, 0, FileAttributes.Directory), path));
            }
        }

        foreach (var d in await Drives.ReadAsync(TimeSpan.FromSeconds(1.5)).ConfigureAwait(false))
        {
            var letter = d.Root.TrimEnd('\\');
            var name = d.Label is { Length: > 0 } label ? letter + " " + label : letter;
            items.Add(new PlaceItem(new FileEntry(name, d.Total, 0, FileAttributes.Directory), d.Root) { IsDrive = true, Free = d.Free, Total = d.Total });
        }

        return items;
    }

    private static unsafe List<PlaceItem> Enumerate(string place, Func<string, bool, PlaceItem?> item)
    {
        var items = new List<PlaceItem>();
        nint pidl = 0;
        void* folder = null;
        void* list = null;
        try
        {
            ShellItems.Check(SHParseDisplayName(ParseName(place), null, &pidl, 0, null));
            var iid = IidShellFolder;
            ShellItems.Check(SHBindToObject(null, pidl, null, &iid, &folder));
            if (((delegate* unmanaged[Stdcall]<void*, nint, uint, void**, int>)(*(void***)folder)[4])(folder, 0, ShcontfFolders | ShcontfNonFolders, &list) != 0 || list is null)
            {
                return items;
            }

            nint child;
            uint fetched;
            while (((delegate* unmanaged[Stdcall]<void*, uint, nint*, uint*, int>)(*(void***)list)[3])(list, 1, &child, &fetched) == 0 && fetched == 1)
            {
                try
                {
                    if (ParsingName(folder, child) is { } path && item(path, IsFolder(folder, child)) is { } found)
                    {
                        items.Add(found);
                    }
                }
                finally
                {
                    CoTaskMemFree(child);
                }
            }
        }
        finally
        {
            ShellItems.Release(list);
            ShellItems.Release(folder);
            if (pidl != 0)
            {
                CoTaskMemFree(pidl);
            }
        }

        return items;
    }

    private static PlaceItem? HomeItemAt(string path, bool folder)
        => Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\?\", StringComparison.Ordinal) ? HomeItem(path, folder) : null;

    private static PlaceItem HomeItem(string path, bool folder)
    {
        var entry = new FileEntry(WindowsPath.GetName(path).TrimEnd('\\'), 0, 0, folder ? FileAttributes.Directory : FileAttributes.Normal);
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            try
            {
                FileSystemInfo info = folder ? new DirectoryInfo(path) : new FileInfo(path);
                if (info.Exists)
                {
                    entry = entry with { Size = info is FileInfo f ? f.Length : 0, LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks, Attributes = info.Attributes & ~(FileAttributes.Hidden | FileAttributes.System) };
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return new PlaceItem(entry, path) { From = WindowsPath.GetParent(path) };
    }

    private static string ParseName(string path) => ShellPlace.IsPlace(path) ? "shell:" + path : path;

    private static unsafe string? ParsingName(void* folder, nint child)
    {
        var strret = stackalloc byte[StrRetSize];
        if (((delegate* unmanaged[Stdcall]<void*, nint, uint, byte*, int>)(*(void***)folder)[11])(folder, child, ShgdnForParsing, strret) < 0)
        {
            return null;
        }

        var buffer = stackalloc char[1024];
        return StrRetToBufW(strret, child, buffer, 1024) < 0 ? null : new string(buffer);
    }

    private static unsafe bool IsFolder(void* folder, nint child)
    {
        var attributes = SfgaoFolder | SfgaoStream;
        ((delegate* unmanaged[Stdcall]<void*, uint, nint*, uint*, int>)(*(void***)folder)[9])(folder, 1, &child, &attributes);
        return (attributes & SfgaoFolder) != 0 && (attributes & SfgaoStream) == 0;
    }

    private static string LibrariesFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Libraries");

    private static List<PlaceItem> LibraryList()
    {
        var items = new List<PlaceItem>();
        if (!Directory.Exists(LibrariesFolder))
        {
            return items;
        }

        foreach (var file in Directory.EnumerateFiles(LibrariesFolder, "*.library-ms"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var folders = ReadLibrary(file);
            var target = folders.Count == 1 ? folders[0] : ShellPlace.Libraries + "\\" + name;
            items.Add(new PlaceItem(new FileEntry(name, 0, File.GetLastWriteTimeUtc(file).Ticks, FileAttributes.Directory), target));
        }

        return items;
    }

    private static List<PlaceItem> LibraryFolders(string library)
    {
        var file = Path.Combine(LibrariesFolder, library + ".library-ms");
        if (!File.Exists(file))
        {
            throw new DirectoryNotFoundException(Strings.Place_CantOpen);
        }

        return [.. ReadLibrary(file).Select(path => new PlaceItem(new FileEntry(WindowsPath.GetName(path).TrimEnd('\\'), 0, 0, FileAttributes.Directory), path) { From = WindowsPath.GetParent(path) })];
    }

    private static List<string> ReadLibrary(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            return [.. LibraryLocations(XDocument.Load(stream), KnownFolderPath)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return [];
        }
    }

    public static IEnumerable<string> LibraryLocations(XDocument library, Func<Guid, string?> knownFolder)
    {
        var located = new List<(string Path, bool Default)>();
        foreach (var connector in library.Descendants().Where(e => e.Name.LocalName == "searchConnectorDescription"))
        {
            var url = connector.Descendants().FirstOrDefault(e => e.Name.LocalName == "url")?.Value.Trim();
            var isDefault = connector.Elements().Any(e => e.Name.LocalName == "isDefaultSaveLocation" && e.Value.Trim() == "true");
            string? path = null;
            if (url is null)
            {
                continue;
            }

            if (url.StartsWith("knownfolder:", StringComparison.OrdinalIgnoreCase))
            {
                path = Guid.TryParse(url["knownfolder:".Length..], out var id) ? knownFolder(id) : null;
            }
            else if (Path.IsPathFullyQualified(url))
            {
                path = url;
            }

            if (path is { Length: > 0 } && !located.Any(l => string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                located.Add((path, isDefault));
            }
        }

        return located.OrderByDescending(l => l.Default).Select(l => l.Path);
    }

    private static unsafe string? KnownFolderPath(Guid id)
    {
        if (SHGetKnownFolderPath(&id, 0, 0, out var path) != 0)
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

    private static List<PlaceItem> RecycleBinItems()
    {
        var items = new List<PlaceItem>();
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null)
        {
            return items;
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable))
        {
            var bin = Path.Combine(drive.Name, "$Recycle.Bin", sid);
            try
            {
                if (!drive.IsReady || !Directory.Exists(bin))
                {
                    continue;
                }

                foreach (var index in Directory.EnumerateFiles(bin, "$I*"))
                {
                    var data = new FileInfo(index).Length is > 0 and < 70000 ? File.ReadAllBytes(index) : null;
                    var target = Path.Combine(bin, "$R" + Path.GetFileName(index)[2..]);
                    if (data is null || ParseIndex(data) is not { } item)
                    {
                        continue;
                    }

                    var folder = Directory.Exists(target);
                    if (!folder && !File.Exists(target))
                    {
                        continue;
                    }

                    var entry = new FileEntry(WindowsPath.GetName(item.OriginalPath), item.Size, item.DeletedUtc.Ticks, folder ? FileAttributes.Directory : FileAttributes.Normal);
                    items.Add(new PlaceItem(entry, target) { From = WindowsPath.GetParent(item.OriginalPath) });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return items;
    }

    public static RecycledItem? ParseIndex(ReadOnlySpan<byte> data)
    {
        if (data.Length < 28)
        {
            return null;
        }

        var version = BinaryPrimitives.ReadInt64LittleEndian(data);
        var size = BinaryPrimitives.ReadInt64LittleEndian(data[8..]);
        var deleted = BinaryPrimitives.ReadInt64LittleEndian(data[16..]);
        ReadOnlySpan<byte> name;
        if (version == 1)
        {
            name = data[24..Math.Min(data.Length, 24 + 520)];
        }
        else if (version == 2)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);
            if (length <= 0 || 28L + (length * 2L) > data.Length)
            {
                return null;
            }

            name = data.Slice(28, length * 2);
        }
        else
        {
            return null;
        }

        var path = MemoryMarshal.Cast<byte, char>(name[..(name.Length & ~1)]);
        var end = path.IndexOf('\0');
        var original = new string(end < 0 ? path : path[..end]);
        if (original.Length == 0 || deleted < 0)
        {
            return null;
        }

        return new RecycledItem(original, Math.Max(0, size), deleted == 0 ? DateTime.MinValue : DateTime.FromFileTimeUtc(deleted));
    }

    public unsafe Task RestoreAsync(IReadOnlyList<string> targets, nint owner) => ShellThread.Run(() => WithBinItems(targets, (binFolder, binPidl, children) =>
    {
        var bin = (void*)binFolder;
        void* menu = null;
        var popup = CreatePopupMenu();
        try
        {
            var iid = ShellItems.IidContextMenu;
            fixed (nint* c = children)
            {
                ShellItems.Check(((delegate* unmanaged[Stdcall]<void*, nint, uint, nint*, Guid*, uint*, void**, int>)(*(void***)bin)[10])(bin, owner, (uint)children.Length, c, &iid, null, &menu));
            }

            ShellItems.Check(((delegate* unmanaged[Stdcall]<void*, nint, uint, uint, uint, uint, int>)(*(void***)menu)[3])(menu, popup, 0, 1, 0x7FFF, 0));
            var verb = "undelete\0"u8;
            fixed (byte* v = verb)
            {
                var info = new InvokeInfo { Size = (uint)sizeof(InvokeInfo), Owner = owner, Verb = (nint)v, Show = 1 };
                ShellItems.Check(((delegate* unmanaged[Stdcall]<void*, InvokeInfo*, int>)(*(void***)menu)[4])(menu, &info));
            }
        }
        finally
        {
            DestroyMenu(popup);
            ShellItems.Release(menu);
        }
    }));

    public Task DeleteForGoodAsync(IReadOnlyList<string> targets, nint owner)
        => ShellThread.Run(() => WithBinItems(targets, (bin, binPidl, children) => Operate(bin, binPidl, children, owner, null, Delete)));

    public Task CopyOutAsync(IReadOnlyList<string> targets, string folder, bool move, nint owner)
        => ShellThread.Run(() => WithBinItems(targets, (bin, binPidl, children) => Operate(bin, binPidl, children, owner, folder, move ? Move : Copy)));

    public Task EmptyAsync(nint owner) => ShellThread.Run(() =>
    {
        var hr = SHEmptyRecycleBinW(owner, null, SherbNoConfirmation);
        if (hr < 0 && hr != unchecked((int)0x8000FFFF))
        {
            ShellItems.Check(hr);
        }
    });

    private const int Delete = 0, Copy = 1, Move = 2;

    private static unsafe void WithBinItems(IReadOnlyList<string> targets, Action<nint, nint, nint[]> work)
    {
        var wanted = new HashSet<string>(targets.Select(Path.GetFileName).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        var found = new List<nint>();
        nint pidl = 0;
        void* bin = null;
        void* list = null;
        try
        {
            ShellItems.Check(SHParseDisplayName(ParseName(ShellPlace.RecycleBin), null, &pidl, 0, null));
            var iid = IidShellFolder;
            ShellItems.Check(SHBindToObject(null, pidl, null, &iid, &bin));
            if (((delegate* unmanaged[Stdcall]<void*, nint, uint, void**, int>)(*(void***)bin)[4])(bin, 0, ShcontfFolders | ShcontfNonFolders | ShcontfIncludeHidden, &list) == 0 && list is not null)
            {
                nint child;
                uint fetched;
                while (((delegate* unmanaged[Stdcall]<void*, uint, nint*, uint*, int>)(*(void***)list)[3])(list, 1, &child, &fetched) == 0 && fetched == 1)
                {
                    if (ParsingName(bin, child) is { } path && wanted.Contains(Path.GetFileName(path)))
                    {
                        found.Add(child);
                    }
                    else
                    {
                        CoTaskMemFree(child);
                    }
                }
            }

            if (found.Count == 0)
            {
                throw new IOException(Strings.Place_BinItemsGone);
            }

            work((nint)bin, pidl, [.. found]);
        }
        finally
        {
            foreach (var child in found)
            {
                CoTaskMemFree(child);
            }

            ShellItems.Release(list);
            ShellItems.Release(bin);
            if (pidl != 0)
            {
                CoTaskMemFree(pidl);
            }
        }
    }

    private static unsafe void Operate(nint bin, nint binPidl, nint[] children, nint owner, string? folder, int what)
    {
        void* array = null;
        void* operation = null;
        void* destination = null;
        try
        {
            fixed (nint* c = children)
            {
                ShellItems.Check(SHCreateShellItemArray(binPidl, (void*)bin, (uint)children.Length, c, &array));
            }

            var clsid = ClsidFileOperation;
            var iid = IidFileOperation;
            ShellItems.Check(CoCreateInstance(&clsid, null, ClsctxAll, &iid, &operation));
            var flags = what == Delete ? FofNoConfirmation : FofAllowUndo | FofNoConfirmMkdir;
            ShellItems.Check(((delegate* unmanaged[Stdcall]<void*, uint, int>)(*(void***)operation)[5])(operation, flags));
            if (owner != 0)
            {
                ((delegate* unmanaged[Stdcall]<void*, nint, int>)(*(void***)operation)[9])(operation, owner);
            }

            if (what == Delete)
            {
                ShellItems.Check(((delegate* unmanaged[Stdcall]<void*, void*, int>)(*(void***)operation)[19])(operation, array));
            }
            else
            {
                var iidItem = IidShellItem;
                ShellItems.Check(SHCreateItemFromParsingName(folder!, null, &iidItem, &destination));
                var slot = what == Move ? 15 : 17;
                ShellItems.Check(((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*(void***)operation)[slot])(operation, array, destination));
            }

            var hr = ((delegate* unmanaged[Stdcall]<void*, int>)(*(void***)operation)[21])(operation);
            if (hr < 0 && hr != CopyEngineUserCancelled)
            {
                ShellItems.Check(hr);
            }
        }
        finally
        {
            ShellItems.Release(destination);
            ShellItems.Release(operation);
            ShellItems.Release(array);
        }
    }

    public static unsafe Task OpenInExplorerAsync(string folder, IReadOnlyList<string> names) => ShellThread.Run(() =>
    {
        var pidls = new List<nint>();
        nint parent = 0;
        try
        {
            ShellItems.Check(SHParseDisplayName(ParseName(folder), null, &parent, 0, null));
            foreach (var name in names)
            {
                nint pidl;
                if (SHParseDisplayName(WindowsPath.Join(folder, name), null, &pidl, 0, null) >= 0)
                {
                    pidls.Add(pidl);
                }
            }

            if (pidls.Count == 0)
            {
                var info = new ShellExecuteIdList { Size = (uint)sizeof(ShellExecuteIdList), Mask = SeeMaskIdList, IdList = parent, Show = 1 };
                if (!ShellExecuteExW(&info))
                {
                    throw new IOException(Marshal.GetLastPInvokeErrorMessage());
                }

                return;
            }

            var items = pidls.ToArray();
            fixed (nint* p = items)
            {
                ShellItems.Check(SHOpenFolderAndSelectItems(parent, (uint)items.Length, p, 0));
            }
        }
        finally
        {
            foreach (var pidl in pidls)
            {
                CoTaskMemFree(pidl);
            }

            if (parent != 0)
            {
                CoTaskMemFree(parent);
            }
        }
    });

    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeInfo
    {
        public uint Size;
        public uint Mask;
        public nint Owner;
        public nint Verb;
        public nint Parameters;
        public nint Directory;
        public int Show;
        public uint HotKey;
        public nint Icon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShellExecuteIdList
    {
        public uint Size;
        public uint Mask;
        public nint Owner;
        public nint Verb;
        public nint File;
        public nint Parameters;
        public nint Directory;
        public int Show;
        public nint InstApp;
        public nint IdList;
        public nint Class;
        public nint ClassKey;
        public uint HotKey;
        public nint IconOrMonitor;
        public nint Process;
    }

    private const int StrRetSize = 8 + 520;
    private const uint ShcontfFolders = 0x20, ShcontfNonFolders = 0x40, ShcontfIncludeHidden = 0x80;
    private const uint ShgdnForParsing = 0x8000;
    private const uint SfgaoFolder = 0x20000000, SfgaoStream = 0x00400000;
    private const uint SherbNoConfirmation = 0x1;
    private const uint SeeMaskIdList = 0x4;
    private const uint ClsctxAll = 0x17;
    private const uint FofNoConfirmation = 0x10, FofAllowUndo = 0x40, FofNoConfirmMkdir = 0x200;
    private const int CopyEngineUserCancelled = unchecked((int)0x80270000);

    private static readonly Guid IidShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid ClsidFileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid IidFileOperation = new("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8");

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHParseDisplayName(string name, void* bindContext, nint* pidl, uint sfgaoIn, uint* sfgaoOut);

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHBindToObject(void* folder, nint pidl, void* bindContext, Guid* iid, void** result);

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHCreateShellItemArray(nint parent, void* folder, uint count, nint* children, void** result);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHCreateItemFromParsingName(string path, void* bindContext, Guid* iid, void** result);

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHOpenFolderAndSelectItems(nint folder, uint count, nint* items, uint flags);

    [LibraryImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(nint owner, string? root, uint flags);

    [LibraryImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ShellExecuteExW(ShellExecuteIdList* info);

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHGetKnownFolderPath(Guid* id, uint flags, nint token, out nint path);

    [LibraryImport("shlwapi.dll")]
    private static unsafe partial int StrRetToBufW(byte* strret, nint pidl, char* buffer, uint size);

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** result);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint p);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);
}
