// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Vfs/FileEntry.cs

namespace Salandual.Core.Vfs;

public readonly record struct FileEntry(string Name, long Size, long LastWriteUtcTicks, FileAttributes Attributes)
{
    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;

    public bool IsHidden => (Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    public DateTime LastWriteUtc => new(LastWriteUtcTicks, DateTimeKind.Utc);

    public int ExtensionStart
    {
        get
        {
            if (IsDirectory)
            {
                return Name.Length;
            }

            var dot = Name.LastIndexOf('.');
            var start = Name.LastIndexOf('\\') + 1;
            return dot > start && dot < Name.Length - 1 ? dot + 1 : Name.Length;
        }
    }

    public ReadOnlySpan<char> BaseName
    {
        get
        {
            var start = ExtensionStart;
            return start == Name.Length ? Name.AsSpan() : Name.AsSpan(0, start - 1);
        }
    }

    public ReadOnlySpan<char> Extension => Name.AsSpan(ExtensionStart);
}
