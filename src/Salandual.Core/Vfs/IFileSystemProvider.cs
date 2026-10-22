// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Vfs/IFileSystemProvider.cs

namespace Salandual.Core.Vfs;

[Flags]
public enum ProviderCapabilities
{
    None = 0,
    List = 1,
    Read = 2,
    Write = 4,
    Rename = 8,
    Delete = 16,
    Attributes = 32,
    Watch = 64,
}

public sealed record DirectoryListing(string Path, FileEntry[] Entries, TimeSpan Elapsed);

public sealed record DirectoryChange(string Path, int ChangeCount, bool Overflowed);

public interface IFileSystemProvider
{
    ProviderCapabilities Capabilities { get; }

    Task<DirectoryListing> ListAsync(string path, CancellationToken cancellationToken = default);

    string? GetParent(string path);

    string? GetRoot(string path);

    string GetName(string path);

    string Combine(string directory, string name);

    IDisposable Watch(string path, Action<DirectoryChange> onChange, Action<Exception> onError);
}
