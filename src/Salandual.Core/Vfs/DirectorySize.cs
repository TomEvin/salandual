// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Vfs/DirectorySize.cs

namespace Salandual.Core.Vfs;

public static class DirectorySize
{
    public static async Task<long> ComputeAsync(IFileSystemProvider provider, string path, CancellationToken cancellationToken)
    {
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            DirectoryListing listing;
            try
            {
                listing = await provider.ListAsync(dir, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && dir != path)
            {
                continue;
            }

            foreach (var e in listing.Entries)
            {
                if (!e.IsDirectory)
                {
                    total += e.Size;
                }
                else if ((e.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(provider.Combine(dir, e.Name));
                }
            }
        }

        return total;
    }
}
