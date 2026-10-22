// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Operations/OperationModel.cs

namespace Salandual.Core.Operations;

public enum OperationKind { Copy, Move, Delete }

public enum ExistingFilePolicy
{
    Ask,
    OverwriteOlder,
    Overwrite,
    Skip,
    KeepBoth,
}

public sealed record TransferOptions
{
    public ExistingFilePolicy IfExists { get; init; } = ExistingFilePolicy.Ask;

    public bool KeepFolderTimes { get; init; } = true;

    public bool KeepAttributes { get; init; } = true;

    public bool KeepStreams { get; init; } = true;

    public string? Filter { get; init; }

    public bool SkipEmptyFolders { get; init; }

    public long SpeedLimit { get; init; }

    public bool Verify { get; init; }

    public bool ConfirmMerge { get; init; }

    public bool ConfirmReplaceHiddenOrSystem { get; init; }

    public long? MaxFileSize { get; init; }

    public static TransferOptions Default { get; } = new();
}

public readonly record struct ItemInfo(string Name, FileAttributes Attributes, long Size, DateTime CreatedUtc, DateTime LastWriteUtc)
{
    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;

    public bool IsLink => (Attributes & FileAttributes.ReparsePoint) != 0;

    public bool IsOnlineOnly => ((int)Attributes & (RecallOnDataAccess | (int)FileAttributes.Offline)) != 0;

    private const int RecallOnDataAccess = 0x00400000;
}

public sealed record VolumeFacts(long? FreeBytes, string? FileSystem, bool ReadOnly, int MaxPathLength)
{
    public static VolumeFacts Unknown { get; } = new(null, null, false, int.MaxValue);

    public long? MaxFileSize => string.Equals(FileSystem, "FAT32", StringComparison.OrdinalIgnoreCase) || string.Equals(FileSystem, "FAT", StringComparison.OrdinalIgnoreCase)
        ? uint.MaxValue
        : null;
}

public enum RecycleResult
{
    Recycled,
    DeletedPermanently,
    Kept,
}

public interface IOperationFileSystem
{
    string Combine(string directory, string name);

    string GetName(string path);

    string? GetParent(string path);

    string VolumeOf(string path);

    ItemInfo? Stat(string path);

    VolumeFacts Describe(string path);

    IReadOnlyList<ItemInfo> List(string directory, CancellationToken cancellationToken);

    void CreateDirectory(string path);

    void CopyFile(string source, string target, bool overwrite, Action<long> progress);

    void Move(string source, string target, bool replace);

    void DeleteFile(string path);

    void DeleteDirectory(string path);

    void SetAttributes(string path, FileAttributes attributes);

    void SetTimes(string path, DateTime createdUtc, DateTime lastWriteUtc);

    void RemoveStreams(string path);

    bool ContentEquals(string a, string b, CancellationToken cancellationToken);

    bool CanRecycle(string path, out string reason);

    RecycleResult Recycle(string path);
}

public abstract record OperationQuestion;

public sealed record ConflictQuestion(string Source, ItemInfo SourceInfo, string Target, ItemInfo TargetInfo, int ConflictsSoFar) : OperationQuestion;

public sealed record ErrorQuestion(string Path, string Action, string Message) : OperationQuestion;

public sealed record PermanentDeleteQuestion(IReadOnlyList<(string Path, string Reason)> Items) : OperationQuestion;

public enum CheckKind { DeleteNonEmptyFolder, DeleteHiddenOrSystem, ReplaceHiddenOrSystem, MergeFolder }

public sealed record CheckQuestion(CheckKind Kind, string Path) : OperationQuestion;

public sealed record DeleteChecks(bool NonEmptyFolders, bool HiddenOrSystem)
{
    public static DeleteChecks None { get; } = new(false, false);
}

public enum AnswerKind { Overwrite, OverwriteOlder, Skip, KeepBoth, Retry, DeletePermanently, Cancel, Yes }

public sealed record OperationAnswer(AnswerKind Kind, bool ForAll = false);

public sealed record OperationIssue(string Path, string Message);

public enum OperationPhase { Scanning, Working }

public readonly record struct SpeedSample(double Fraction, double BytesPerSecond, double ItemsPerSecond);

public sealed record OperationProgress(
    OperationPhase Phase,
    long DoneBytes,
    long TotalBytes,
    int DoneItems,
    int TotalItems,
    string? Current,
    double BytesPerSecond,
    double AverageBytesPerSecond = 0,
    double PeakBytesPerSecond = 0,
    double ItemsPerSecond = 0,
    double AverageItemsPerSecond = 0,
    long CurrentDone = 0,
    long CurrentTotal = 0)
{
    public static OperationProgress Start { get; } = new(OperationPhase.Scanning, 0, 0, 0, 0, null, 0);

    public bool CountsFiles => TotalItems > 0 && (TotalBytes == 0 || TotalBytes / TotalItems < 256 * 1024);

    public double Fraction => TotalBytes > 0
        ? Math.Clamp((double)DoneBytes / TotalBytes, 0, 1)
        : TotalItems > 0 ? Math.Clamp((double)DoneItems / TotalItems, 0, 1) : 0;

    public TimeSpan? Remaining
    {
        get
        {
            if (CountsFiles)
            {
                return AverageItemsPerSecond > 0.1 && TotalItems > DoneItems ? TimeSpan.FromSeconds((TotalItems - DoneItems) / AverageItemsPerSecond) : null;
            }

            var speed = AverageBytesPerSecond > 1 ? AverageBytesPerSecond : BytesPerSecond;
            return speed > 1 && TotalBytes > DoneBytes ? TimeSpan.FromSeconds((TotalBytes - DoneBytes) / speed) : null;
        }
    }
}
