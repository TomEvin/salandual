// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/FileListSettings.cs

using Salandual.Core.Listing;

namespace Salandual.Core.Settings;

public enum FileListTarget
{
    Clipboard,
    Viewer,
    File,
}

public sealed record FileListSettings
{
    public const int MaxMine = 50;

    public List<FileListTemplate> Mine { get; set; } = [];

    public string First { get; set; } = "";

    public string Line { get; set; } = "$(FileName)$(CRLF)";

    public FileListTarget Target { get; set; }

    public string FileName { get; set; } = "files.txt";

    public bool Append { get; set; }

    public bool IncludeInside { get; set; }

    public string Mask { get; set; } = "";

    public bool FoldersToo { get; set; }

    internal FileListSettings Normalize() => this with
    {
        Mine = (Mine ?? []).Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Name) && m.Line is not null)
            .Select(m => m with { Name = m.Name.Trim(), First = m.First ?? "" })
            .DistinctBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxMine).ToList(),
        First = First ?? "",
        Line = Line ?? "",
        Mask = (Mask ?? "").Trim(),
        FileName = string.IsNullOrWhiteSpace(FileName) ? "files.txt" : FileName.Trim(),
        Target = Enum.IsDefined(Target) ? Target : FileListTarget.Clipboard,
    };
}
