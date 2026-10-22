// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Operations/TransferPreview.cs

using Salandual.Core.Listing;

namespace Salandual.Core.Operations;

public sealed record TransferPreview
{
    public ConflictScan Conflicts { get; init; } = ConflictScan.None;

    public int Files { get; init; }

    public long Bytes { get; init; }

    public bool Complete { get; init; } = true;

    public IReadOnlyList<string> OntoItself { get; init; } = [];

    public IReadOnlyList<(string From, string To)> MaskRenames { get; init; } = [];

    public int MaskRenameCount { get; init; }

    public IReadOnlyList<(string From, string To)> MaskInsideRenames { get; init; } = [];

    public int MaskInsideCount { get; init; }

    public IReadOnlyList<string> Links { get; init; } = [];

    public IReadOnlyList<(string Path, long Size)> TooBig { get; init; } = [];

    public int TooLongPaths { get; init; }

    public int OnlineOnlyFiles { get; init; }

    public long OnlineOnlyBytes { get; init; }

    public VolumeFacts Target { get; init; } = VolumeFacts.Unknown;

    public long NeededBytes { get; init; }

    public bool NotEnoughSpace => Target.FreeBytes is { } free && NeededBytes > free;

    private const int Samples = 5;

    public static TransferPreview Scan(IOperationFileSystem fs, OperationKind kind, IReadOnlyList<OperationSource> sources, TransferTarget target, string? filter, CancellationToken cancellationToken)
    {
        var mask = string.IsNullOrWhiteSpace(filter) ? null : FileMask.Parse(filter);
        var conflicts = new List<ConflictPreview>();
        var onto = new List<string>();
        var maskRenames = new List<(string, string)>();
        var inside = new List<(string, string)>();
        var links = new List<string>();
        var tooBig = new List<(string, long)>();
        int conflictCount = 0, files = 0, maskCount = 0, insideCount = 0, tooLong = 0, online = 0;
        long bytes = 0, onlineBytes = 0;
        var complete = true;

        var facts = Describe(fs, target.Folder);
        var sameDisk = kind == OperationKind.Move && sources.Count > 0
            && string.Equals(fs.VolumeOf(sources[0].Path), fs.VolumeOf(target.Folder), StringComparison.OrdinalIgnoreCase);

        var listings = new Dictionary<string, Dictionary<string, ItemInfo>?>(StringComparer.OrdinalIgnoreCase);
        ItemInfo? Existing(string folder, string name)
        {
            if (!listings.TryGetValue(folder, out var items))
            {
                items = null;
                if (fs.Stat(folder) is { IsDirectory: true })
                {
                    items = fs.List(folder, cancellationToken).ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
                }

                listings[folder] = items;
            }

            return items is not null && items.TryGetValue(name, out var info) ? info : null;
        }

        void File(string source, ItemInfo info, string destFolder, string destName)
        {
            files++;
            bytes += info.Size;
            if (info.IsOnlineOnly)
            {
                online++;
                onlineBytes += info.Size;
            }

            if (facts.MaxFileSize is { } max && info.Size > max && tooBig.Count < 50)
            {
                tooBig.Add((source, info.Size));
            }

            if (fs.Combine(destFolder, destName).Length > facts.MaxPathLength)
            {
                tooLong++;
            }

            if (Existing(destFolder, destName) is { IsDirectory: false } existing)
            {
                conflictCount++;
                if (conflicts.Count < Samples)
                {
                    conflicts.Add(new ConflictPreview(source, fs.Combine(destFolder, destName), info, existing));
                }
            }
        }

        void Folder(string source, string dest, string relative)
        {
            foreach (var child in fs.List(source, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = fs.Combine(source, child.Name);
                var rel = relative + "\\" + child.Name;
                if (child.IsDirectory && child.IsLink)
                {
                    links.Add(path);
                }
                else if (child.IsDirectory)
                {
                    Folder(path, fs.Combine(dest, child.Name), rel);
                }
                else if (mask is null || mask.IsMatch(child.Name))
                {
                    var renamed = OperationMask.Apply(child.Name, target.Mask);
                    if (!string.Equals(renamed, child.Name, StringComparison.Ordinal))
                    {
                        insideCount++;
                        if (inside.Count < 4)
                        {
                            inside.Add((rel.TrimStart('\\'), renamed));
                        }
                    }

                    File(path, child, dest, target.NameFor(child.Name, isDirectory: false, selected: false));
                }
            }
        }

        try
        {
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (fs.Stat(source.Path) is not { } info)
                {
                    continue;
                }

                var name = target.NameFor(info.Name, info.IsDirectory);
                var dest = fs.Combine(target.Folder, name);
                if (kind == OperationKind.Copy ? FileOperations.SamePath(source.Path, dest) : string.Equals(source.Path, dest, StringComparison.Ordinal))
                {
                    onto.Add(source.Path);
                    continue;
                }

                if (!info.IsDirectory && !string.Equals(name, info.Name, StringComparison.Ordinal))
                {
                    maskCount++;
                    if (maskRenames.Count < 4)
                    {
                        maskRenames.Add((info.Name, name));
                    }
                }

                if (info.IsDirectory && info.IsLink)
                {
                    links.Add(source.Path);
                }
                else if (info.IsDirectory)
                {
                    if (!sameDisk || Existing(target.Folder, name) is not null)
                    {
                        Folder(source.Path, dest, info.Name);
                    }
                }
                else if (mask is null || target.NewName is not null || mask.IsMatch(info.Name))
                {
                    File(source.Path, info, target.Folder, name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            complete = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            complete = false;
        }

        return new TransferPreview
        {
            Conflicts = new ConflictScan(conflictCount, conflicts, complete),
            Files = files,
            Bytes = bytes,
            Complete = complete,
            OntoItself = onto,
            MaskRenames = maskRenames,
            MaskRenameCount = maskCount,
            MaskInsideRenames = inside,
            MaskInsideCount = insideCount,
            Links = links,
            TooBig = tooBig,
            TooLongPaths = tooLong,
            OnlineOnlyFiles = online,
            OnlineOnlyBytes = onlineBytes,
            Target = facts,
            NeededBytes = sameDisk ? 0 : bytes,
        };
    }

    private static VolumeFacts Describe(IOperationFileSystem fs, string folder)
    {
        for (var path = folder; path is not null; path = fs.GetParent(path))
        {
            try
            {
                if (fs.Stat(path) is { IsDirectory: true })
                {
                    return fs.Describe(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return VolumeFacts.Unknown;
            }
        }

        return VolumeFacts.Unknown;
    }
}
