// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/ArchiveUnpacker.cs

using Salandual.Core.Diagnostics;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;

namespace Salandual.Archives;

public sealed record UnpackSource(ArchiveContents Contents, string ArchivePath, string FileOnDisk, string Folder, IReadOnlyList<string> Names)
{
    public string ArchiveName => Path.GetFileName(ArchivePath);
}

public sealed record UnpackOptions
{
    public ExistingFilePolicy IfExists { get; init; } = ExistingFilePolicy.Ask;
    public bool KeepTimes { get; init; } = true;
    public bool KeepAttributes { get; init; } = true;

    public bool KeepFolders { get; init; } = true;

    public string? Mask { get; init; }

    public UnpackFolderRule Folder { get; init; } = UnpackFolderRule.Never;

    public bool AskOnErrors { get; init; } = true;

    public long UnknownSizeLimit { get; init; } = 1L << 30;
}

public sealed record UnpackEntry(ArchiveItem Item, string Relative);

public sealed record UnpackPlan(IReadOnlyList<UnpackEntry> Entries, long TotalSize, int Files, int TopFolders, int TopFiles)
{
    public bool NeedsFolder => TopFolders + TopFiles > 1;
}

public sealed record UnpackSuspicion(long Unpacked, int Files, long Packed, double Ratio, bool MoreThanFree, ArchiveItem? Largest);

public static class ArchiveUnpacker
{
    public static UnpackPlan Plan(UnpackSource source, UnpackOptions options)
    {
        var contents = source.Contents;
        var prefix = source.Folder.Trim('\\');
        var chosen = new Dictionary<string, ArchiveItem>(StringComparer.OrdinalIgnoreCase);

        void Take(ArchiveItem item)
        {
            if (!chosen.TryGetValue(item.Path, out var known) || known.Index < item.Index)
            {
                chosen[item.Path] = item;
            }
        }

        if (source.Names.Count == 0)
        {
            foreach (var item in contents.Items.Where(i => prefix.Length == 0 || i.Path.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                Take(item);
            }
        }
        else
        {
            foreach (var name in source.Names)
            {
                var inner = prefix.Length == 0 ? name : prefix + "\\" + name;
                if (contents.IsFolder(inner))
                {
                    if (contents.Find(inner) is { } own)
                    {
                        Take(own);
                    }

                    foreach (var item in contents.Items.Where(i => i.Path.StartsWith(inner + "\\", StringComparison.OrdinalIgnoreCase)))
                    {
                        Take(item);
                    }
                }
                else if (contents.Find(inner) is { } item)
                {
                    Take(item);
                }
            }
        }

        var mask = string.IsNullOrWhiteSpace(options.Mask) ? null : FileMask.Parse(options.Mask);
        var entries = new List<UnpackEntry>();
        foreach (var item in chosen.Values.OrderBy(i => i.Index))
        {
            var relative = prefix.Length == 0 ? item.Path : item.Path[(prefix.Length + 1)..];
            if (!options.KeepFolders)
            {
                if (item.IsDirectory)
                {
                    continue;
                }

                relative = item.Name;
            }

            if (!item.IsDirectory && mask is not null && !mask.IsMatch(item.Name))
            {
                continue;
            }

            entries.Add(new UnpackEntry(item, relative));
        }

        var tops = entries.Where(e => ArchiveSafety.Check(e.Item.RawPath, out _) is null).Select(e => (Top: e.Relative.Split('\\')[0], Folder: e.Item.IsDirectory || e.Relative.Contains('\\', StringComparison.Ordinal)))
            .GroupBy(t => t.Top, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Any(t => t.Folder))
            .ToList();
        var files = entries.Where(e => !e.Item.IsDirectory).ToList();
        return new UnpackPlan(entries, files.Sum(e => e.Item.Size), files.Count, tops.Count(f => f), tops.Count(f => !f));
    }

    public static string? FolderFor(UnpackPlan plan, UnpackFolderRule rule, string archiveName) => rule switch
    {
        UnpackFolderRule.Always => ArchivePaths.FolderNameFor(archiveName),
        UnpackFolderRule.WhenNeeded when plan.NeedsFolder => ArchivePaths.FolderNameFor(archiveName),
        _ => null,
    };

    public static UnpackSuspicion? Suspicion(UnpackPlan plan, long packed, int ratio, int megabytes, long? freeBytes)
    {
        var size = plan.TotalSize;
        var times = packed > 0 ? (double)size / packed : 0;
        var bomb = size > (long)megabytes * 1024 * 1024 && times > ratio;
        var tooBig = freeBytes is { } free && size > free;
        return bomb || tooBig
            ? new UnpackSuspicion(size, plan.Files, packed, times, tooBig, plan.Entries.Where(e => !e.Item.IsDirectory).Select(e => e.Item).MaxBy(i => i.Size))
            : null;
    }

    public static OperationJob Unpack(IOperationFileSystem fs, UnpackSource source, string target, UnpackOptions options, ArchivePasswordQuestion? askPassword)
    {
        var from = fs.VolumeOf(source.FileOnDisk);
        var to = fs.VolumeOf(target);
        return new OperationJob(OperationKind.Unpack, Loc.Format(Strings.Job_Unpacking, source.ArchiveName), from.TrimEnd('\\') + " → " + to.TrimEnd('\\'),
            new[] { from, to }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), job => new Unpacker(fs, job, source, target, options, askPassword).Run())
        {
            Subject = source.ArchiveName,
        };
    }

    public static OperationJob Test(IOperationFileSystem fs, ArchiveContents contents, string archiveName, string fileOnDisk, ArchivePasswordQuestion? askPassword)
    {
        var volume = fs.VolumeOf(fileOnDisk);
        return new OperationJob(OperationKind.Test, Loc.Format(Strings.Job_Testing, archiveName), volume.TrimEnd('\\'), [volume], job =>
        {
            var files = contents.Items.Where(i => !i.IsDirectory && !i.IsLink).ToList();
            var total = files.Sum(i => i.Size);
            var done = 0;
            job.Report(OperationPhase.Working, 0, total, 0, files.Count, archiveName, force: true);
            var results = ArchiveReader.Extract(contents, [.. files.Select(i => i.Index)], _ => null, askPassword, archiveName,
                bytes =>
                {
                    job.Checkpoint();
                    job.Report(OperationPhase.Working, Math.Min(bytes, total), total, done, files.Count, archiveName);
                },
                job.CancellationToken,
                (_, _) => done++,
                test: true);
            var bad = 0;
            foreach (var item in files)
            {
                if (results.GetValueOrDefault(item.Index, ExtractResult.Ok) is var result and not ExtractResult.Ok)
                {
                    job.AddIssue(item.Path, ArchiveSafety.Text(result));
                    bad++;
                }
            }

            if (bad > 0)
            {
                AppLog.Warn(Loc.Format(Strings.Log_TestDamaged, bad));
            }

            job.Report(OperationPhase.Working, total, total, files.Count, files.Count, null, force: true);
        }) { Subject = archiveName };
    }

    private static ItemInfo Info(ArchiveItem item)
    {
        var time = item.ModifiedUtc ?? DateTime.MinValue;
        return new ItemInfo(item.Name, item.Attributes & ~(FileAttributes.Encrypted | FileAttributes.Directory), item.Size, time, time);
    }

    private sealed class Unpacker(IOperationFileSystem fs, OperationJob job, UnpackSource source, string target, UnpackOptions options, ArchivePasswordQuestion? askPassword)
    {
        private const FileAttributes Blocking = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;
        private const FileAttributes Kept = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;

        private readonly Dictionary<int, (ArchiveItem Item, string Dest)> _files = [];
        private readonly Dictionary<int, string> _written = [];
        private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
        private ExistingFilePolicy _policy = options.IfExists;
        private (int Index, GuardStream Stream, string Path)? _open;
        private GuardStream? _grown;
        private string? _grownName;
        private bool _skipErrors;
        private long _totalBytes, _doneBytes, _before;
        private int _totalItems, _doneItems, _conflicts, _refused;

        public void Run()
        {
            var plan = Plan(source, options);
            var root = FolderFor(plan, options.Folder, source.ArchiveName) is { } sub ? Path.Combine(target, sub) : target;
            var folders = new List<(ArchiveItem Item, string Dest)>();
            var links = new List<(ArchiveItem Item, string Dest)>();

            foreach (var entry in plan.Entries)
            {
                var item = entry.Item;
                if (ArchiveSafety.Check(item.RawPath, out var part) is { } refusal)
                {
                    Refuse(item, ArchiveSafety.Text(refusal, part));
                    continue;
                }

                var dest = Path.Combine([root, .. entry.Relative.Split('\\').Select(ArchivePaths.SafeFileName)]);
                if (!ArchiveSafety.IsInside(dest, root) && !(item.IsDirectory && SameFolder(dest, root)))
                {
                    Refuse(item, ArchiveSafety.Text(Refusal.LeavesTarget, item.RawPath));
                    continue;
                }

                if (item.IsLink)
                {
                    links.Add((item, dest));
                }
                else if (item.IsDirectory)
                {
                    folders.Add((item, dest));
                }
                else
                {
                    _files[item.Index] = (item, dest);
                    _totalBytes += item.Size;
                }
            }

            _totalItems = _files.Count + links.Count;
            if (_refused > 0)
            {
                AppLog.Warn(Loc.Format(Strings.Log_UnpackRefused, _refused));
            }

            job.Report(OperationPhase.Working, 0, _totalBytes, 0, _totalItems, source.ArchiveName, force: true);
            if (!Try(root, () => EnsureFolder(root)))
            {
                return;
            }

            foreach (var (_, dest) in folders)
            {
                Try(dest, () => EnsureFolder(dest));
            }

            if (_files.Count > 0)
            {
                IReadOnlyDictionary<int, ExtractResult> results;
                try
                {
                    results = ArchiveReader.Extract(source.Contents, [.. _files.Keys], Open, askPassword, source.ArchiveName, null, job.CancellationToken, Done);
                }
                catch (Exception) when (_grown is not null)
                {
                    results = new Dictionary<int, ExtractResult>();
                }
                finally
                {
                    CloseOpen(keep: false);
                }

                if (_grown is not null)
                {
                    throw new IOException(Loc.Format(Strings.Unpack_Grew, _grownName));
                }

                foreach (var (index, (item, _)) in _files)
                {
                    if (results.GetValueOrDefault(index, ExtractResult.Ok) is var result and not ExtractResult.Ok)
                    {
                        job.AddIssue(item.Path, ArchiveSafety.Text(result));
                    }
                }
            }

            foreach (var (item, dest) in links)
            {
                job.Checkpoint();
                MakeLink(item, dest, root);
                _doneItems++;
            }

            if (options.KeepTimes)
            {
                foreach (var (item, dest) in folders.OrderByDescending(f => f.Dest.Length))
                {
                    if (item.ModifiedUtc is { } time)
                    {
                        Quietly(() => fs.SetTimes(dest, time, time));
                    }
                }
            }

            job.Report(OperationPhase.Working, _doneBytes, _totalBytes, _doneItems, _totalItems, null, force: true);
        }

        private void Refuse(ArchiveItem item, string reason)
        {
            job.AddIssue(item.IsLink && item.LinkTarget is { } to ? item.RawPath + " → " + to : item.RawPath, reason);
            _refused++;
        }

        private static bool SameFolder(string a, string b)
            => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

        private Stream? Open(int index)
        {
            job.Checkpoint();
            CloseOpen(keep: false);
            var (item, dest) = _files[index];
            _before = _doneBytes;
            if (!Try(item.Path, () => EnsureFolder(Path.GetDirectoryName(dest)!)))
            {
                return null;
            }

            if (fs.Stat(dest) is { } existing)
            {
                if (_written.ContainsValue(dest))
                {
                    dest = UniqueName(dest);
                }
                else if (existing.IsDirectory)
                {
                    job.AddIssue(item.Path, Strings.Issue_FolderInTarget);
                    return null;
                }
                else
                {
                    switch (Decide(item, dest, existing))
                    {
                        case ExistingFilePolicy.Skip:
                            return null;
                        case ExistingFilePolicy.KeepBoth:
                            dest = UniqueName(dest);
                            break;
                        default:
                            if ((existing.Attributes & Blocking) != 0 && !Try(dest, () => fs.SetAttributes(dest, FileAttributes.Normal)))
                            {
                                return null;
                            }

                            break;
                    }
                }
            }

            GuardStream? stream = null;
            var path = dest;
            if (!Try(item.Path, () => stream = new GuardStream(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16),
                    item.Size > 0 ? item.Size + (item.Size >> 3) + (1 << 20) : options.UnknownSizeLimit, Written)))
            {
                return null;
            }

            _open = (index, stream!, path);
            return stream;
        }

        private void Written(long total)
        {
            job.Checkpoint();
            _doneBytes = _before + total;
            job.Report(OperationPhase.Working, _doneBytes, _totalBytes, _doneItems, _totalItems, _open is { } o ? _files[o.Index].Item.Path : null);
        }

        private void Done(int index, bool ok)
        {
            if (_open is { } open && open.Index == index)
            {
                if (open.Stream.Grew)
                {
                    _grown = open.Stream;
                    _grownName = _files[index].Item.Name;
                }

                CloseOpen(keep: ok && !open.Stream.Grew);
            }

            if (ok)
            {
                _doneItems++;
            }
        }

        private void CloseOpen(bool keep)
        {
            if (_open is not { } open)
            {
                return;
            }

            _open = null;
            if (open.Stream.Grew)
            {
                _grown = open.Stream;
                _grownName = _files[open.Index].Item.Name;
                keep = false;
            }

            Quietly(open.Stream.Dispose);
            if (!keep)
            {
                Quietly(() => File.Delete(open.Path));
                return;
            }

            _written[open.Index] = open.Path;
            var item = _files[open.Index].Item;
            if (options.KeepTimes && item.ModifiedUtc is { } time)
            {
                Quietly(() => fs.SetTimes(open.Path, time, time));
            }

            if (options.KeepAttributes && (item.Attributes & Kept) is var attributes and not 0)
            {
                Quietly(() => fs.SetAttributes(open.Path, attributes));
            }
        }

        private void MakeLink(ArchiveItem item, string dest, string root)
        {
            var display = item.RawPath + " → " + item.LinkTarget;
            if (item.HardLink)
            {
                var wanted = ArchivePaths.Shown(item.LinkTarget!);
                if (_files.FirstOrDefault(f => string.Equals(f.Value.Item.Path, wanted, StringComparison.OrdinalIgnoreCase)) is { Value.Item: not null } file
                    && _written.TryGetValue(file.Key, out var copyFrom))
                {
                    Try(item.Path, () =>
                    {
                        EnsureFolder(Path.GetDirectoryName(dest)!);
                        File.Copy(copyFrom, dest, overwrite: false);
                    });
                }
                else
                {
                    job.AddIssue(display, Strings.Unpack_HardLinkMissing);
                }

                return;
            }

            if (ArchiveSafety.LinkTargetInside(item.LinkTarget!, dest, root) is not { } inside)
            {
                job.AddIssue(display, ArchiveSafety.Text(Refusal.LinkOutside, ""));
                _refused++;
                return;
            }

            try
            {
                EnsureFolder(Path.GetDirectoryName(dest)!);
                if (Directory.Exists(Path.Combine(Path.GetDirectoryName(dest)!, inside)))
                {
                    Directory.CreateSymbolicLink(dest, inside);
                }
                else
                {
                    File.CreateSymbolicLink(dest, inside);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                job.AddIssue(display, Strings.Unpack_LinkNotAllowed);
            }
        }

        private ExistingFilePolicy Decide(ArchiveItem item, string dest, ItemInfo existing)
        {
            var policy = _policy;
            var info = Info(item);
            if (policy == ExistingFilePolicy.Ask)
            {
                var answer = job.Ask(new ConflictQuestion(Path.Combine(source.ArchivePath, item.Path), info, dest, existing, ++_conflicts));
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

        private bool Try(string path, Action work)
        {
            while (true)
            {
                job.Checkpoint();
                try
                {
                    work();
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (!_skipErrors && options.AskOnErrors)
                    {
                        var answer = job.Ask(new ErrorQuestion(path, "Unpack", ex.Message));
                        if (answer.Kind == AnswerKind.Retry)
                        {
                            continue;
                        }

                        _skipErrors |= answer.ForAll;
                    }

                    job.AddIssue(path, ex.Message);
                    return false;
                }
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
                    throw new IOException(Loc.Format(Strings.Unpack_FileInTheWay, Path.GetFileName(path)));
                }
            }
            else
            {
                if (Path.GetDirectoryName(path) is { } parent && !string.Equals(parent, path, StringComparison.OrdinalIgnoreCase))
                {
                    EnsureFolder(parent);
                }

                fs.CreateDirectory(path);
            }

            _folders.Add(path);
        }

        private string UniqueName(string path)
        {
            var folder = Path.GetDirectoryName(path)!;
            var name = Path.GetFileName(path);
            var dot = name.LastIndexOf('.');
            var (stem, ext) = dot > 0 ? (name[..dot], name[dot..]) : (name, "");
            for (var i = 2; ; i++)
            {
                var candidate = Path.Combine(folder, $"{stem} ({i}){ext}");
                if (fs.Stat(candidate) is null)
                {
                    return candidate;
                }
            }
        }

        private static void Quietly(Action work)
        {
            try
            {
                work();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class GuardStream(FileStream file, long limit, Action<long> written) : Stream
    {
        private long _count;

        public bool Grew { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _count;

        public override long Position
        {
            get => _count;
            set => throw new NotSupportedException();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_count + buffer.Length > limit)
            {
                Grew = true;
                throw new IOException(Strings.Unpack_GrewShort);
            }

            file.Write(buffer);
            _count += buffer.Length;
            written(_count);
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Flush() => file.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                file.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
