// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Operations/FileOperations.cs

using Salandual.Core.Listing;
using Salandual.Core.Resources;

namespace Salandual.Core.Operations;

public sealed record ConflictPreview(string Source, string Target, ItemInfo SourceInfo, ItemInfo TargetInfo)
{
    public bool SourceIsNewer => SourceInfo.LastWriteUtc > TargetInfo.LastWriteUtc;
}

public sealed record ConflictScan(int Count, IReadOnlyList<ConflictPreview> Samples, bool Complete)
{
    public static ConflictScan None { get; } = new(0, [], true);
}

public static class FileOperations
{
    public static OperationJob Copy(IOperationFileSystem fs, IReadOnlyList<OperationSource> sources, TransferTarget target, TransferOptions? options = null)
        => Transfer(fs, OperationKind.Copy, sources, target, options ?? TransferOptions.Default);

    public static OperationJob Move(IOperationFileSystem fs, IReadOnlyList<OperationSource> sources, TransferTarget target, TransferOptions? options = null)
        => Transfer(fs, OperationKind.Move, sources, target, options ?? TransferOptions.Default);

    public static OperationJob Duplicate(IOperationFileSystem fs, IReadOnlyList<OperationSource> sources, string folder, TransferOptions? options = null)
        => Transfer(fs, OperationKind.Copy, sources, new TransferTarget(folder) { RenameOntoItself = true }, (options ?? TransferOptions.Default) with { IfExists = ExistingFilePolicy.Skip }, Strings.Job_Duplicating);

    public static OperationJob Delete(IOperationFileSystem fs, IReadOnlyList<OperationSource> sources, bool recycle, bool? refusedPermanently = null, DeleteChecks? checks = null)
    {
        var volumes = sources.Select(s => fs.VolumeOf(s.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var subject = Count(sources);
        var title = Loc.Format(recycle ? Strings.Job_Deleting : Strings.Job_DeletingPermanently, subject);
        return new OperationJob(OperationKind.Delete, title, Route(volumes[0]), volumes, job => new Deleter(fs, job, sources, recycle, refusedPermanently, checks ?? DeleteChecks.None).Run()) { Subject = subject };
    }

    public static ConflictScan FindConflicts(IOperationFileSystem fs, IReadOnlyList<OperationSource> sources, TransferTarget target, string? filter, CancellationToken cancellationToken)
        => TransferPreview.Scan(fs, OperationKind.Copy, sources, target, filter, cancellationToken).Conflicts;

    private static OperationJob Transfer(IOperationFileSystem fs, OperationKind kind, IReadOnlyList<OperationSource> sources, TransferTarget target, TransferOptions options, string? titleFormat = null)
    {
        var from = fs.VolumeOf(sources[0].Path);
        var to = fs.VolumeOf(target.Folder);
        var volumes = sources.Select(s => fs.VolumeOf(s.Path)).Append(to).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var subject = Count(sources);
        var title = Loc.Format(titleFormat ?? (kind == OperationKind.Copy ? Strings.Job_Copying : Strings.Job_Moving), subject);
        var job = new OperationJob(kind, title, Route(from) + " → " + Route(to), volumes, job => new Transferrer(fs, job, kind, sources, target, options).Run()) { Subject = subject };
        job.SpeedLimit = options.SpeedLimit;
        return job;
    }

    public static string Count(IReadOnlyList<OperationSource> sources)
    {
        var folders = sources.Count(s => s.IsDirectory);
        var files = sources.Count - folders;
        var fileText = Loc.Plural(files, Strings.Count_Files_One, Strings.Count_Files_Other);
        var folderText = Loc.Plural(folders, Strings.Count_Folders_One, Strings.Count_Folders_Other);
        return folders == 0 ? fileText : files == 0 ? folderText : Loc.Format(Strings.Count_FoldersAndFiles, folderText, fileText);
    }

    private static string Route(string volume) => volume.TrimEnd('\\');

    internal static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    internal static string UniqueName(IOperationFileSystem fs, string path, bool isDirectory)
    {
        var folder = fs.GetParent(path)!;
        var name = fs.GetName(path);
        var dot = isDirectory ? -1 : name.LastIndexOf('.');
        var (stem, ext) = dot > 0 ? (name[..dot], name[dot..]) : (name, "");
        for (var i = 2; ; i++)
        {
            var candidate = fs.Combine(folder, $"{stem} ({i}){ext}");
            if (fs.Stat(candidate) is null)
            {
                return candidate;
            }
        }
    }

    private abstract class Worker(OperationJob job)
    {
        private bool _skipErrors;
        private readonly Dictionary<CheckKind, bool> _checkedAll = [];

        protected OperationJob Job { get; } = job;

        protected bool Try(string path, string action, Action work)
        {
            while (true)
            {
                Job.Checkpoint();
                try
                {
                    work();
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (_skipErrors)
                    {
                        Job.AddIssue(path, ex.Message);
                        return false;
                    }

                    var answer = Job.Ask(new ErrorQuestion(path, action, ex.Message));
                    if (answer.Kind == AnswerKind.Retry)
                    {
                        continue;
                    }

                    _skipErrors |= answer.ForAll;
                    Job.AddIssue(path, ex.Message);
                    return false;
                }
            }
        }

        protected bool Confirm(CheckKind kind, string path)
        {
            if (_checkedAll.TryGetValue(kind, out var all))
            {
                return all;
            }

            var answer = Job.Ask(new CheckQuestion(kind, path));
            var yes = answer.Kind == AnswerKind.Yes;
            if (answer.ForAll)
            {
                _checkedAll[kind] = yes;
            }

            return yes;
        }

        protected bool Try<T>(string path, string action, Func<T> work, out T result)
        {
            T value = default!;
            var ok = Try(path, action, () => value = work());
            result = value;
            return ok;
        }
    }

    private sealed class Transferrer(IOperationFileSystem fs, OperationJob job, OperationKind kind, IReadOnlyList<OperationSource> sources, TransferTarget target, TransferOptions options) : Worker(job)
    {
        private const FileAttributes Blocking = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;

        private readonly List<Item> _plan = [];
        private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
        private readonly FileMask? _filter = string.IsNullOrWhiteSpace(options.Filter) ? null : FileMask.Parse(options.Filter);
        private readonly string _verb = kind == OperationKind.Copy ? "Copy" : "Move";
        private ExistingFilePolicy _policy = options.IfExists;
        private long _totalBytes, _doneBytes, _limitBytes;
        private int _totalItems, _doneItems, _conflicts, _limitVersion = -1;
        private TimeSpan _limitTime;

        private sealed record Item(string Source, string Target, ItemInfo Info, bool WholeMove);

        public void Run()
        {
            Scan();
            foreach (var item in _plan)
            {
                Job.Checkpoint();
                if (item.WholeMove)
                {
                    MoveWhole(item.Source, item.Target, item.Info);
                    _doneItems++;
                }
                else if (item.Info.IsDirectory)
                {
                    if (!options.SkipEmptyFolders)
                    {
                        Try(item.Source, _verb, () => EnsureFolder(item.Target));
                    }

                    _doneItems++;
                }
                else
                {
                    TransferFile(item);
                }

                Report(item.Source);
            }

            FinishFolders();
            Job.Report(OperationPhase.Working, _doneBytes, _totalBytes, _doneItems, _totalItems, null, force: true);
        }

        private void Report(string? current) => Job.Report(OperationPhase.Working, _doneBytes, _totalBytes, _doneItems, _totalItems, current);

        private void Scan()
        {
            foreach (var source in sources)
            {
                Job.Checkpoint();
                if (!Try(source.Path, "Read", () => fs.Stat(source.Path), out var stat))
                {
                    continue;
                }

                if (stat is not { } info)
                {
                    Job.AddIssue(source.Path, Strings.Issue_Gone);
                    continue;
                }

                var dest = fs.Combine(target.Folder, target.NameFor(info.Name, info.IsDirectory));
                if (kind == OperationKind.Move && string.Equals(source.Path, dest, StringComparison.Ordinal))
                {
                    continue;
                }

                if (kind == OperationKind.Copy && SamePath(source.Path, dest))
                {
                    if (!target.RenameOntoItself)
                    {
                        Job.AddIssue(source.Path, Strings.Issue_CopyOntoItself);
                        continue;
                    }

                    dest = UniqueName(fs, dest, info.IsDirectory);
                }

                if (info.IsDirectory && IsInside(dest, source.Path))
                {
                    Job.AddIssue(source.Path, kind == OperationKind.Copy ? Strings.Issue_FolderCopiedIntoItself : Strings.Issue_FolderMovedIntoItself);
                    continue;
                }

                if (options.ConfirmMerge && info.IsDirectory && !info.IsLink && !SamePath(source.Path, dest) && fs.Stat(dest) is { IsDirectory: true } && !Confirm(CheckKind.MergeFolder, dest))
                {
                    continue;
                }

                Job.AddTarget(dest);

                if (kind == OperationKind.Move && SameVolume(source.Path, target.Folder))
                {
                    _plan.Add(new Item(source.Path, dest, info, WholeMove: true));
                    _totalItems++;
                }
                else if (info.IsDirectory && !info.IsLink)
                {
                    ScanFolder(source.Path, dest, info);
                }
                else if (info.IsDirectory)
                {
                    AddLink(source.Path, dest, info);
                }
                else if (_filter is null || target.NewName is not null || _filter.IsMatch(info.Name))
                {
                    AddFile(source.Path, dest, info);
                }

                Job.Report(OperationPhase.Scanning, 0, _totalBytes, 0, _totalItems, source.Path);
            }
        }

        private void ScanFolder(string source, string dest, ItemInfo info)
        {
            _plan.Add(new Item(source, dest, info, WholeMove: false));
            _totalItems++;
            if (!Try(source, "Read", () => fs.List(source, Job.CancellationToken), out var children))
            {
                return;
            }

            Job.Report(OperationPhase.Scanning, 0, _totalBytes, 0, _totalItems, source);
            foreach (var child in children)
            {
                var path = fs.Combine(source, child.Name);
                if (child.IsDirectory && child.IsLink)
                {
                    AddLink(path, fs.Combine(dest, child.Name), child);
                }
                else if (child.IsDirectory)
                {
                    ScanFolder(path, fs.Combine(dest, child.Name), child);
                }
                else if (_filter is null || _filter.IsMatch(child.Name))
                {
                    AddFile(path, fs.Combine(dest, target.NameFor(child.Name, isDirectory: false, selected: false)), child);
                }
            }
        }

        private void AddLink(string source, string dest, ItemInfo info)
        {
            if (kind == OperationKind.Move)
            {
                Job.AddIssue(source, Strings.Issue_FolderLinkLeft);
                return;
            }

            _plan.Add(new Item(source, dest, info, WholeMove: false));
            _totalItems++;
            Job.AddIssue(source, Strings.Issue_FolderLinkEmpty);
        }

        private void AddFile(string source, string dest, ItemInfo info)
        {
            if (options.MaxFileSize is { } max && info.Size > max)
            {
                Job.AddIssue(source, Strings.Issue_TooLargeForFat);
                return;
            }

            _plan.Add(new Item(source, dest, info, WholeMove: false));
            _totalBytes += info.Size;
            _totalItems++;
        }

        private bool SameVolume(string a, string b)
        {
            var volume = fs.VolumeOf(a);
            return string.Equals(volume, fs.VolumeOf(b), StringComparison.OrdinalIgnoreCase)
                && (!volume.StartsWith(@"\\", StringComparison.Ordinal) || SameShare(a, b));
        }

        private static bool SameShare(string a, string b)
        {
            static string Share(string p)
            {
                var server = p.IndexOf('\\', 2);
                var share = server < 0 ? -1 : p.IndexOf('\\', server + 1);
                return share < 0 ? p : p[..share];
            }

            return a.StartsWith(@"\\", StringComparison.Ordinal) && b.StartsWith(@"\\", StringComparison.Ordinal)
                && string.Equals(Share(a), Share(b), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInside(string path, string folder)
            => SamePath(path, folder)
            || (path.Length > folder.Length && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                && (folder.EndsWith('\\') || path[folder.Length] == '\\'));

        private void TransferFile(Item item)
        {
            var size = item.Info.Size;
            var before = _doneBytes;
            var dest = item.Target;
            var overwrite = false;

            if (!Try(item.Source, _verb, () => EnsureFolder(fs.GetParent(dest)!)))
            {
                Skipped();
                return;
            }

            if (fs.Stat(dest) is { } existing)
            {
                if (existing.IsDirectory)
                {
                    Job.AddIssue(item.Source, Strings.Issue_FolderInTarget);
                    Skipped();
                    return;
                }

                var asked = _policy == ExistingFilePolicy.Ask;
                switch (Decide(item.Source, item.Info, dest, existing))
                {
                    case ExistingFilePolicy.Skip:
                        Skipped();
                        return;
                    case ExistingFilePolicy.KeepBoth:
                        dest = UniqueName(fs, dest, isDirectory: false);
                        break;
                    default:
                        if (!asked && options.ConfirmReplaceHiddenOrSystem && (existing.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 && !Confirm(CheckKind.ReplaceHiddenOrSystem, dest))
                        {
                            Skipped();
                            return;
                        }

                        overwrite = true;
                        if ((existing.Attributes & Blocking) != 0 && !Try(dest, "Overwrite", () => fs.SetAttributes(dest, FileAttributes.Normal)))
                        {
                            Skipped();
                            return;
                        }

                        break;
                }
            }

            var attempt = 0;
            Try(item.Source, _verb, () =>
            {
                var replace = overwrite || (attempt++ > 0 && fs.Stat(dest) is not null);
                fs.CopyFile(item.Source, dest, replace, copied =>
                {
                    Job.Checkpoint();
                    var done = Math.Min(copied, size);
                    _doneBytes = before + done;
                    Limit();
                    Job.Report(OperationPhase.Working, _doneBytes, _totalBytes, _doneItems, _totalItems, item.Source, currentDone: done, currentTotal: size);
                });

                if (!options.KeepAttributes)
                {
                    fs.SetAttributes(dest, FileAttributes.Archive);
                }

                if (!options.KeepStreams)
                {
                    fs.RemoveStreams(dest);
                }

                if (options.Verify && !item.Info.IsOnlineOnly && !fs.ContentEquals(item.Source, dest, Job.CancellationToken))
                {
                    throw new IOException(Strings.Issue_VerifyFailed);
                }

                if (kind == OperationKind.Move)
                {
                    if ((item.Info.Attributes & Blocking) != 0)
                    {
                        fs.SetAttributes(item.Source, FileAttributes.Normal);
                    }

                    fs.DeleteFile(item.Source);
                }
            });

            _doneBytes = before + size;
            _doneItems++;

            void Skipped()
            {
                _doneBytes = before + size;
                _doneItems++;
            }
        }

        private ExistingFilePolicy Decide(string source, ItemInfo info, string dest, ItemInfo existing)
        {
            var policy = _policy;
            if (policy == ExistingFilePolicy.Ask)
            {
                var answer = Job.Ask(new ConflictQuestion(source, info, dest, existing, ++_conflicts));
                policy = answer.Kind switch
                {
                    AnswerKind.Overwrite => ExistingFilePolicy.Overwrite,
                    AnswerKind.OverwriteOlder => ExistingFilePolicy.OverwriteOlder,
                    AnswerKind.KeepBoth => ExistingFilePolicy.KeepBoth,
                    _ => ExistingFilePolicy.Skip,
                };
                if (answer.ForAll)
                {
                    _policy = policy;
                }
            }

            return policy == ExistingFilePolicy.OverwriteOlder
                ? existing.LastWriteUtc < info.LastWriteUtc ? ExistingFilePolicy.Overwrite : ExistingFilePolicy.Skip
                : policy;
        }

        private void Limit()
        {
            var limit = Job.SpeedLimit;
            if (Job.SpeedLimitVersion != _limitVersion)
            {
                _limitVersion = Job.SpeedLimitVersion;
                _limitBytes = _doneBytes;
                _limitTime = Job.Elapsed;
            }

            if (limit <= 0)
            {
                return;
            }

            var due = TimeSpan.FromSeconds((double)(_doneBytes - _limitBytes) / limit);
            var ahead = due - (Job.Elapsed - _limitTime);
            if (ahead > TimeSpan.FromMilliseconds(15))
            {
                Job.CancellationToken.WaitHandle.WaitOne(ahead < TimeSpan.FromSeconds(1) ? ahead : TimeSpan.FromSeconds(1));
                Job.CancellationToken.ThrowIfCancellationRequested();
            }
        }

        private void EnsureFolder(string path)
        {
            if (_folders.Contains(path))
            {
                return;
            }

            if (fs.Stat(path) is { } existing)
            {
                if (!existing.IsDirectory)
                {
                    throw new IOException($"A file named “{fs.GetName(path)}” is in the way of the folder.");
                }
            }
            else
            {
                if (fs.GetParent(path) is { } parent)
                {
                    EnsureFolder(parent);
                }

                fs.CreateDirectory(path);
            }

            _folders.Add(path);
        }

        private void FinishFolders()
        {
            for (var i = _plan.Count - 1; i >= 0; i--)
            {
                var item = _plan[i];
                if (item.WholeMove || !item.Info.IsDirectory)
                {
                    continue;
                }

                if (_folders.Contains(item.Target))
                {
                    if (options.KeepAttributes && (item.Info.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly)) != 0)
                    {
                        Try(item.Target, "Set the attributes of", () => fs.SetAttributes(item.Target, item.Info.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly | FileAttributes.Archive)));
                    }

                    if (options.KeepFolderTimes)
                    {
                        Try(item.Target, "Set the times of", () => fs.SetTimes(item.Target, item.Info.CreatedUtc, item.Info.LastWriteUtc));
                    }
                }

                if (kind == OperationKind.Move)
                {
                    RemoveIfEmpty(item.Source, item.Info);
                }
            }
        }

        private void RemoveIfEmpty(string folder, ItemInfo info)
        {
            try
            {
                if (fs.List(folder, Job.CancellationToken).Count > 0)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            Try(folder, "Remove the folder", () =>
            {
                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    fs.SetAttributes(folder, FileAttributes.Directory);
                }

                fs.DeleteDirectory(folder);
            });
        }

        private void MoveWhole(string source, string dest, ItemInfo info)
        {
            Job.Checkpoint();
            if (SamePath(source, dest))
            {
                Try(source, "Rename", () => fs.Move(source, dest, replace: false));
                return;
            }

            if (!Try(source, "Move", () => fs.Stat(dest), out var found))
            {
                return;
            }

            if (found is not { } existing)
            {
                Try(source, "Move", () =>
                {
                    EnsureFolder(fs.GetParent(dest)!);
                    fs.Move(source, dest, replace: false);
                });
                return;
            }

            if (info.IsDirectory && existing.IsDirectory && !info.IsLink)
            {
                if (!Try(source, "Read", () => fs.List(source, Job.CancellationToken), out var children))
                {
                    return;
                }

                foreach (var child in children)
                {
                    var name = target.NameFor(child.Name, child.IsDirectory, selected: false);
                    MoveWhole(fs.Combine(source, child.Name), fs.Combine(dest, name), child);
                    Report(source);
                }

                RemoveIfEmpty(source, info);
                return;
            }

            if (info.IsDirectory || existing.IsDirectory)
            {
                Job.AddIssue(source, Strings.Issue_FileAndFolderSameName);
                return;
            }

            switch (Decide(source, info, dest, existing))
            {
                case ExistingFilePolicy.Skip:
                    return;
                case ExistingFilePolicy.KeepBoth:
                    var unique = UniqueName(fs, dest, isDirectory: false);
                    Try(source, "Move", () => fs.Move(source, unique, replace: false));
                    return;
                default:
                    Try(source, "Move", () =>
                    {
                        if ((existing.Attributes & Blocking) != 0)
                        {
                            fs.SetAttributes(dest, FileAttributes.Normal);
                        }

                        fs.Move(source, dest, replace: true);
                    });
                    return;
            }
        }
    }

    private sealed class Deleter(IOperationFileSystem fs, OperationJob job, IReadOnlyList<OperationSource> all, bool recycle, bool? refusedPermanently, DeleteChecks checks) : Worker(job)
    {
        private int _total, _done;

        public void Run()
        {
            var sources = Confirmed();
            var permanent = new List<OperationSource>();
            var recyclable = new List<OperationSource>();
            if (recycle)
            {
                var refused = new List<(string Path, string Reason)>();
                foreach (var source in sources)
                {
                    if (fs.CanRecycle(source.Path, out var reason))
                    {
                        recyclable.Add(source);
                    }
                    else
                    {
                        refused.Add((source.Path, reason));
                    }
                }

                if (refused.Count > 0)
                {
                    var answer = refusedPermanently switch
                    {
                        true => new OperationAnswer(AnswerKind.DeletePermanently),
                        false => new OperationAnswer(AnswerKind.Skip),
                        null => Job.Ask(new PermanentDeleteQuestion(refused)),
                    };
                    if (answer.Kind == AnswerKind.DeletePermanently)
                    {
                        permanent.AddRange(sources.Where(s => refused.Any(r => r.Path == s.Path)));
                    }
                    else
                    {
                        foreach (var (path, reason) in refused)
                        {
                            Job.AddIssue(path, Loc.Format(Strings.Issue_NotDeletedBecause, reason));
                        }
                    }
                }
            }
            else
            {
                permanent.AddRange(sources);
            }

            _total = recyclable.Count + permanent.Count;
            foreach (var source in recyclable)
            {
                Job.Report(OperationPhase.Working, 0, 0, _done, _total, source.Path, force: true);
                if (Try(source.Path, "Delete", () => fs.Recycle(source.Path), out var result))
                {
                    if (result == RecycleResult.DeletedPermanently)
                    {
                        Job.AddIssue(source.Path, Strings.Issue_DeletedPermanently);
                    }
                    else if (result == RecycleResult.Kept)
                    {
                        Job.AddIssue(source.Path, Strings.Issue_NotDeleted);
                    }
                }

                _done++;
            }

            foreach (var source in permanent)
            {
                if (Try(source.Path, "Delete", () => fs.Stat(source.Path), out var info) && info is { } found)
                {
                    DeleteTree(source.Path, found);
                }

                _done++;
                Job.Report(OperationPhase.Working, 0, 0, _done, _total, source.Path);
            }

            Job.Report(OperationPhase.Working, 0, 0, _done, _total, null, force: true);
        }

        private List<OperationSource> Confirmed()
        {
            if (checks is { NonEmptyFolders: false, HiddenOrSystem: false })
            {
                return [.. all];
            }

            var kept = new List<OperationSource>();
            foreach (var source in all)
            {
                Job.Checkpoint();
                if (fs.Stat(source.Path) is { } info)
                {
                    if (checks.HiddenOrSystem && (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 && !Confirm(CheckKind.DeleteHiddenOrSystem, source.Path))
                    {
                        continue;
                    }

                    if (checks.NonEmptyFolders && info.IsDirectory && !info.IsLink && HasChildren(source.Path) && !Confirm(CheckKind.DeleteNonEmptyFolder, source.Path))
                    {
                        continue;
                    }
                }

                kept.Add(source);
            }

            return kept;
        }

        private bool HasChildren(string path)
        {
            try
            {
                return fs.List(path, Job.CancellationToken).Count > 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        private void DeleteTree(string path, ItemInfo info)
        {
            Job.Checkpoint();
            if (info.IsDirectory && !info.IsLink)
            {
                if (!Try(path, "Read", () => fs.List(path, Job.CancellationToken), out var children))
                {
                    return;
                }

                foreach (var child in children)
                {
                    DeleteTree(fs.Combine(path, child.Name), child);
                }

                Job.Report(OperationPhase.Working, 0, 0, _done, _total, path);
            }

            Try(path, "Delete", () =>
            {
                if ((info.Attributes & FileAttributes.ReadOnly) != 0 || (!info.IsDirectory && (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0))
                {
                    fs.SetAttributes(path, info.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal);
                }

                if (info.IsDirectory)
                {
                    fs.DeleteDirectory(path);
                }
                else
                {
                    fs.DeleteFile(path);
                }
            });
        }
    }
}
