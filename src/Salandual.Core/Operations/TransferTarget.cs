// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Operations/TransferTarget.cs

namespace Salandual.Core.Operations;

public sealed record OperationSource(string Path, bool IsDirectory);

public sealed record TransferTarget(string Folder, string? Mask = null, string? NewName = null, bool CreatesFolder = false)
{
    public bool RenameOntoItself { get; init; }

    public bool MaskInSubfolders { get; init; }

    public static TransferTarget Resolve(IOperationFileSystem fs, string path, int sourceCount)
    {
        var endsWithSeparator = path.Length > 3 && (path.EndsWith('\\') || path.EndsWith('/'));
        var trimmed = endsWithSeparator ? path.TrimEnd('\\', '/') : path;
        var parent = fs.GetParent(trimmed);
        var last = fs.GetName(trimmed);

        if (parent is not null && OperationMask.IsMask(last))
        {
            return new TransferTarget(parent, Mask: OperationMask.IsIdentity(last) ? null : last, CreatesFolder: fs.Stat(parent) is not { IsDirectory: true });
        }

        var existing = fs.Stat(trimmed);
        if (existing is { IsDirectory: true } || parent is null)
        {
            return new TransferTarget(trimmed);
        }

        if (endsWithSeparator || sourceCount > 1)
        {
            return new TransferTarget(trimmed, CreatesFolder: existing is null);
        }

        return new TransferTarget(parent, NewName: last, CreatesFolder: fs.Stat(parent) is not { IsDirectory: true });
    }

    public string NameFor(string name, bool isDirectory, bool selected = true)
        => selected && NewName is not null ? NewName
        : isDirectory || (!selected && !MaskInSubfolders) ? name
        : OperationMask.Apply(name, Mask);
}
