// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/ArchiveContents.cs

using Salandual.Core.Vfs;

namespace Salandual.Archives;

public sealed record ArchiveItem(int Index, string Path, string RawPath, bool IsDirectory, long Size, long PackedSize,
    DateTime? ModifiedUtc, FileAttributes Attributes, bool Encrypted, bool IsLink, string? LinkTarget = null, bool HardLink = false)
{
    public string Name => Path[(Path.LastIndexOf('\\') + 1)..];
}

public sealed class ArchiveContents
{
    private readonly Dictionary<string, List<FileEntry>> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ArchiveItem> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, ArchiveItem> _byIndex = [];

    internal ArchiveContents(string archivePath, string formatName, Guid formatId, IReadOnlyList<ArchiveItem> items, long fileLength, DateTime fileWriteUtc, string? password)
    {
        ArchivePath = archivePath;
        FormatName = formatName;
        FormatId = formatId;
        Items = items;
        FileLength = fileLength;
        FileWriteUtc = fileWriteUtc;
        Password = password;
        _folders[""] = [];
        foreach (var item in items)
        {
            _byIndex[item.Index] = item;
            Add(item);
        }
    }

    public string ArchivePath { get; }

    public string FormatName { get; }

    internal Guid FormatId { get; }

    public IReadOnlyList<ArchiveItem> Items { get; }

    public long FileLength { get; }

    public DateTime FileWriteUtc { get; }

    internal string? Password { get; set; }

    public bool HasEncryptedItems => Items.Any(i => i.Encrypted);

    public ArchivePasswordQuestion KnownPasswordFirst(ArchivePasswordQuestion? ask)
        => (name, wrong) => !wrong && Password is { } known ? known : ask?.Invoke(name, wrong);

    public bool NamesHidden { get; internal set; }

    public int FileCount => Items.Count(i => !i.IsDirectory);

    public int FolderCount => _folders.Count - 1;

    public long TotalSize => Items.Sum(i => i.Size);

    public FileEntry[]? List(string folder)
        => _folders.TryGetValue(Normalize(folder), out var entries) ? [.. entries] : null;

    public ArchiveItem? Find(string path) => _byPath.GetValueOrDefault(Normalize(path));

    public ArchiveItem? ItemAt(int index) => _byIndex.GetValueOrDefault(index);

    public bool IsFolder(string path) => _folders.ContainsKey(Normalize(path));

    public IEnumerable<ArchiveItem> FilesBelow(string folder)
    {
        var prefix = Normalize(folder);
        return Items.Where(i => !i.IsDirectory && (prefix.Length == 0 || i.Path.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase)));
    }

    private static string Normalize(string path) => path.Trim('\\');

    private void Add(ArchiveItem item)
    {
        if (item.Path.Length == 0)
        {
            return;
        }

        var cut = item.Path.LastIndexOf('\\');
        var parent = cut < 0 ? "" : item.Path[..cut];
        var folder = EnsureFolder(parent);
        var ticks = (item.ModifiedUtc ?? FileWriteUtc).Ticks;
        if (item.IsDirectory)
        {
            EnsureFolder(item.Path);
            var at = folder.FindIndex(e => e.IsDirectory && string.Equals(e.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            var entry = new FileEntry(item.Name, 0, ticks, item.Attributes | FileAttributes.Directory);
            if (at >= 0)
            {
                folder[at] = entry;
            }
            else
            {
                folder.Add(entry);
            }
        }
        else
        {
            if (_byPath.ContainsKey(item.Path))
            {
                folder.RemoveAll(e => !e.IsDirectory && string.Equals(e.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            }

            folder.Add(new FileEntry(item.Name, item.Size, ticks, item.Attributes));
        }

        _byPath[item.Path] = item;
    }

    private List<FileEntry> EnsureFolder(string path)
    {
        if (_folders.TryGetValue(path, out var existing))
        {
            return existing;
        }

        var cut = path.LastIndexOf('\\');
        var parent = EnsureFolder(cut < 0 ? "" : path[..cut]);
        var name = path[(cut + 1)..];
        if (!parent.Exists(e => e.IsDirectory && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            parent.Add(new FileEntry(name, 0, FileWriteUtc.Ticks, FileAttributes.Directory));
        }

        var list = new List<FileEntry>();
        _folders[path] = list;
        return list;
    }
}
