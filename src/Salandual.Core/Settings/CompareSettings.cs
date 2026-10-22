// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/CompareSettings.cs

using Salandual.Core.Compare;

namespace Salandual.Core.Settings;

public sealed record CompareSettings
{
    public CompareDirectoriesOptions Directories { get; set; } = new();

    public bool SelectInPanels { get; set; } = true;

    public bool ShowList { get; set; } = true;

    public bool ShowMarks { get; set; } = true;

    public bool ShowBar { get; set; } = true;

    public CompareView ListView { get; set; } = CompareView.Different;

    public DifferenceFilter ListKind { get; set; } = DifferenceFilter.Differences;

    public string ListNames { get; set; } = "";

    public bool ListNamesNot { get; set; }

    public bool ListRemember { get; set; }

    public FileCompareSettings Files { get; set; } = new();

    internal CompareSettings Normalize() => this with { Directories = (Directories ?? new()).Normalize(), ListNames = ListNames ?? "", Files = (Files ?? new()).Normalize() };
}

public sealed record FileCompareSettings
{
    public const int MaxContext = 50;

    public bool IgnoreWhiteSpace { get; set; }

    public bool IgnoreCase { get; set; }

    public bool OnlyDifferences { get; set; }

    public int Context { get; set; } = 3;

    public bool CodeColors { get; set; } = true;

    public bool UseProgram { get; set; }

    public string Program { get; set; } = "";

    public string ProgramArguments { get; set; } = CompareProgram.DefaultArguments;

    internal FileCompareSettings Normalize() => this with
    {
        Context = Math.Clamp(Context, 0, MaxContext),
        Program = Program ?? "",
        ProgramArguments = ProgramArguments ?? CompareProgram.DefaultArguments,
    };
}

public sealed record CompareDirectoriesOptions
{
    public bool Newer { get; set; } = true;

    public bool Bigger { get; set; } = true;

    public bool Attributes { get; set; }

    public bool Content { get; set; }

    public bool IgnoreTwoSeconds { get; set; } = true;

    public bool IgnoreOneHour { get; set; }

    public bool FoldersOnOneSide { get; set; } = true;

    public bool Subfolders { get; set; }

    public bool SubfolderAttributes { get; set; }

    public bool LeaveOutFiles { get; set; }

    public string LeaveOutFilesMask { get; set; } = "*.tmp;*.bak";

    public bool LeaveOutFolders { get; set; } = true;

    public string LeaveOutFoldersMask { get; set; } = ".git;node_modules";

    internal CompareDirectoriesOptions Normalize() => this with
    {
        LeaveOutFilesMask = LeaveOutFilesMask ?? "",
        LeaveOutFoldersMask = LeaveOutFoldersMask ?? "",
    };
}
