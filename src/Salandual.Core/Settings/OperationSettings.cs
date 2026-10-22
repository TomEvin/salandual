// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/OperationSettings.cs

using Salandual.Core.Operations;

namespace Salandual.Core.Settings;

public enum EnterAction { StartNow, AddToQueue }

public sealed record OperationSettings
{
    public ExistingFilePolicy IfExists { get; set; } = ExistingFilePolicy.Ask;
    public bool KeepFolderTimes { get; set; } = true;
    public bool KeepAttributes { get; set; } = true;
    public bool KeepStreams { get; set; } = true;
    public bool FilterEnabled { get; set; }
    public string Filter { get; set; } = "*.*";
    public bool SkipEmptyFolders { get; set; }
    public bool LimitSpeed { get; set; }

    public double SpeedLimitMb { get; set; } = 10;

    public bool Verify { get; set; }

    public bool MaskInSubfolders { get; set; }

    public EnterAction Enter { get; set; } = EnterAction.StartNow;

    public bool RecycleByDefault { get; set; } = true;

    public bool MoveIntoNewFolder { get; set; } = true;

    public bool NotifyWhenDone { get; set; } = true;

    public TransferOptions ToOptions() => new()
    {
        IfExists = IfExists,
        KeepFolderTimes = KeepFolderTimes,
        KeepAttributes = KeepAttributes,
        KeepStreams = KeepStreams,
        Filter = FilterEnabled && !string.IsNullOrWhiteSpace(Filter) ? Filter : null,
        SkipEmptyFolders = SkipEmptyFolders,
        SpeedLimit = LimitSpeed ? (long)(SpeedLimitMb * 1024 * 1024) : 0,
        Verify = Verify,
    };

    internal OperationSettings Normalize() => this with
    {
        Filter = string.IsNullOrWhiteSpace(Filter) ? "*.*" : Filter.Trim(),
        SpeedLimitMb = double.IsFinite(SpeedLimitMb) ? Math.Clamp(SpeedLimitMb, 0.1, 100_000) : 10,
        IfExists = Enum.IsDefined(IfExists) ? IfExists : ExistingFilePolicy.Ask,
    };
}
