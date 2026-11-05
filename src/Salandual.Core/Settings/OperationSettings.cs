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

public enum ProgressShow { Window, Chip }

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

    public string TemplatesFolder { get; set; } = "";

    public bool Verify { get; set; }

    public bool MaskInSubfolders { get; set; }

    public EnterAction Enter { get; set; } = EnterAction.StartNow;

    public ProgressShow Progress { get; set; } = ProgressShow.Window;

    public bool ProgressBlocks { get; set; }

    public SameDiskRule SameDisk { get; set; } = SameDiskRule.HardDiskOnly;

    public bool CloseWhenDone { get; set; }

    public bool RecycleByDefault { get; set; } = true;

    public bool MoveIntoNewFolder { get; set; } = true;

    public bool NotifyWhenDone { get; set; } = true;

    public UnpackFolderRule UnpackFolder { get; set; } = UnpackFolderRule.WhenNeeded;

    public bool UnpackKeepFolders { get; set; } = true;

    public int UnpackAskRatio { get; set; } = 100;

    public int UnpackAskMb { get; set; } = 1024;

    public PackFirstFormat PackFirstFormat { get; set; } = PackFirstFormat.Zip;

    public PackFormat PackLastFormat { get; set; } = PackFormat.Zip;

    public PackLevel PackLevel { get; set; } = PackLevel.Normal;

    public bool PackStoreCompressed { get; set; } = true;

    public PackWhere PackWhere { get; set; } = PackWhere.BesideFiles;

    public PackExisting PackExisting { get; set; } = PackExisting.Ask;

    public ArchiveEditSave ArchiveEditSave { get; set; } = ArchiveEditSave.Ask;

    public PackFormat FirstPackFormat() => PackFirstFormat == PackFirstFormat.LastUsed ? PackLastFormat : (PackFormat)(int)PackFirstFormat;

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
        TemplatesFolder = TemplatesFolder?.Trim() ?? "",
        SpeedLimitMb = double.IsFinite(SpeedLimitMb) ? Math.Clamp(SpeedLimitMb, 0.1, 100_000) : 10,
        IfExists = Enum.IsDefined(IfExists) ? IfExists : ExistingFilePolicy.Ask,
        Progress = Enum.IsDefined(Progress) ? Progress : ProgressShow.Window,
        SameDisk = Enum.IsDefined(SameDisk) ? SameDisk : SameDiskRule.HardDiskOnly,
        UnpackFolder = Enum.IsDefined(UnpackFolder) ? UnpackFolder : UnpackFolderRule.WhenNeeded,
        UnpackAskRatio = Math.Clamp(UnpackAskRatio, 2, 1_000_000),
        UnpackAskMb = Math.Clamp(UnpackAskMb, 1, 100_000_000),
        PackFirstFormat = Enum.IsDefined(PackFirstFormat) ? PackFirstFormat : PackFirstFormat.Zip,
        PackLastFormat = Enum.IsDefined(PackLastFormat) ? PackLastFormat : PackFormat.Zip,
        PackLevel = Enum.IsDefined(PackLevel) ? PackLevel : PackLevel.Normal,
        PackWhere = Enum.IsDefined(PackWhere) ? PackWhere : PackWhere.BesideFiles,
        PackExisting = Enum.IsDefined(PackExisting) ? PackExisting : PackExisting.Ask,
        ArchiveEditSave = Enum.IsDefined(ArchiveEditSave) ? ArchiveEditSave : ArchiveEditSave.Ask,
    };
}
