// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ConfirmationSettings.cs

using Salandual.Core.Operations;

namespace Salandual.Core.Settings;

public sealed record ConfirmationSettings
{
    public bool Delete { get; set; } = true;

    public bool DeleteNonEmptyFolder { get; set; } = true;

    public bool DeleteHiddenOrSystem { get; set; } = true;

    public bool ReplaceHiddenOrSystem { get; set; } = true;

    public bool MergeFolders { get; set; } = true;

    public bool CreateTargetFolder { get; set; }

    public bool DragDropMenu { get; set; }

    public bool CloseApp { get; set; }

    public bool CloseWithJobs { get; set; } = true;

    public bool CloseSearch { get; set; }

    public bool RemoveFileListLine { get; set; } = true;

    public bool CloseUnsavedFileList { get; set; } = true;

    public bool DeleteLeftoverTemp { get; set; } = true;

    public DeleteChecks DeleteChecks => new(DeleteNonEmptyFolder, DeleteHiddenOrSystem);

    public TransferOptions Apply(TransferOptions options) => options with { ConfirmMerge = MergeFolders, ConfirmReplaceHiddenOrSystem = ReplaceHiddenOrSystem };
}

public enum StartFolders
{
    WhereYouLeft,

    Home,

    These,
}

public sealed record GeneralSettings
{
    public bool SingleInstance { get; set; } = true;

    public StartFolders StartFolders { get; set; } = StartFolders.WhereYouLeft;

    public string StartLeft { get; set; } = "";

    public string StartRight { get; set; } = "";

    public static (string Left, string Right) FoldersAtStart(StartFolders choice, string startLeft, string startRight, string? lastLeft, string? lastRight, (string Left, string Right) defaults, string home)
    {
        var left = lastLeft ?? defaults.Left;
        var right = lastRight ?? defaults.Right;
        return choice switch
        {
            StartFolders.Home => (home, home),
            StartFolders.These => (startLeft.Trim() is { Length: > 0 } l ? l : left, startRight.Trim() is { Length: > 0 } r ? r : right),
            _ => (left, right),
        };
    }
}

