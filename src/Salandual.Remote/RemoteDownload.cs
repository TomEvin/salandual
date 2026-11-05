// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/RemoteDownload.cs

using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;

namespace Salandual.Remote;

public sealed record TransferSettings(
    ExistingFilePolicy IfExists = ExistingFilePolicy.Ask,
    bool KeepTimes = true,
    string? Filter = null,
    bool SkipEmptyFolders = false,
    long SpeedLimit = 0,
    bool Verify = false,
    bool Move = false)
{
    public static TransferSettings Default { get; } = new();

    public static TransferSettings From(TransferOptions options, bool move) => new(options.IfExists, options.KeepFolderTimes, options.Filter, options.SkipEmptyFolders, options.SpeedLimit, options.Verify, move);
}

public sealed record DownloadRequest(ServerAddress Folder, IReadOnlyList<(string Name, bool IsDirectory)> Items, string Target, TransferSettings? Settings = null)
{
    public TransferSettings Options => Settings ?? TransferSettings.Default;
}

public sealed record UploadRequest(string Folder, IReadOnlyList<(string Name, bool IsDirectory)> Items, ServerAddress Target, TransferSettings? Settings = null)
{
    public TransferSettings Options => Settings ?? TransferSettings.Default;
}

public static class RemoteDownload
{
    public static OperationJob Start(IOperationFileSystem fs, RemoteFileSystem remote, DownloadRequest request)
    {
        var to = fs.VolumeOf(request.Target);
        var what = FileOperations.Count([.. request.Items.Select(i => new OperationSource(i.Name, i.IsDirectory))]);
        var move = request.Options.Move;
        return new OperationJob(move ? OperationKind.Move : OperationKind.Copy, Loc.Format(move ? Strings.Job_Moving : Strings.Job_Copying, what), request.Folder.Host + " → " + to.TrimEnd('\\'), [to, request.Folder.Root],
            job => new Downloader(fs, remote, job, request).Run())
        {
            Subject = request.Items.Count == 1 ? request.Items[0].Name : null,
            SpeedLimit = request.Options.SpeedLimit,
        };
    }

    public static OperationJob Upload(IOperationFileSystem fs, RemoteFileSystem remote, UploadRequest request)
    {
        var from = fs.VolumeOf(request.Folder);
        var what = FileOperations.Count([.. request.Items.Select(i => new OperationSource(i.Name, i.IsDirectory))]);
        var move = request.Options.Move;
        return new OperationJob(move ? OperationKind.Move : OperationKind.Copy, Loc.Format(move ? Strings.Job_Moving : Strings.Job_Copying, what), from.TrimEnd('\\') + " → " + request.Target.Host, [from, request.Target.Root],
            job => new Uploader(fs, remote, job, request).Run())
        {
            Subject = request.Items.Count == 1 ? request.Items[0].Name : null,
            SpeedLimit = request.Options.SpeedLimit,
        };
    }

    public static void MarkAsDownloaded(string path, ServerAddress from) => RemoteWork.Mark(path, from);

    public static string? SafeLocalName(string name)
    {
        if (name.Length is 0 or > 255 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ')
            || name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0 || name.Any(char.IsControl))
        {
            return null;
        }

        var stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
               || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsAsciiDigit(stem[3])
            ? null
            : name;
    }

    private sealed class Downloader(IOperationFileSystem fs, RemoteFileSystem remote, OperationJob job, DownloadRequest request) : RemoteWork(job, request.Options)
    {
        private readonly List<(ServerAddress From, string To, long Size, DateTime Time)> _files = [];
        private readonly List<(ServerAddress From, string To, DateTime Time, bool HasFiles)> _folders = [];

        public void Run()
        {
            Job.Report(OperationPhase.Scanning, 0, 0, 0, 0, request.Folder.ToString(), force: true);
            var listing = List(request.Folder);
            if (listing is null)
            {
                return;
            }

            foreach (var (name, _) in request.Items)
            {
                if (listing.FirstOrDefault(i => i.Entry.Name == name) is not { } item)
                {
                    Job.AddIssue(request.Folder.Child(name).ToString(), Strings.Issue_Gone);
                    continue;
                }

                Walk(request.Folder, item, request.Target);
            }

            TotalBytes = _files.Sum(f => f.Size);
            TotalFiles = _files.Count;
            Job.Report(OperationPhase.Working, 0, TotalBytes, 0, TotalFiles, null, force: true);

            foreach (var folder in _folders.Where(f => f.HasFiles || !Options.SkipEmptyFolders))
            {
                Try(folder.To, () => EnsureFolder(folder.To));
            }

            foreach (var file in _files)
            {
                Job.Checkpoint();
                Download(file.From, file.To, file.Size, file.Time);
            }

            if (Options.KeepTimes)
            {
                foreach (var folder in _folders.OrderByDescending(f => f.To.Length).Where(f => fs.Stat(f.To) is not null))
                {
                    Quietly(() => fs.SetTimes(folder.To, folder.Time, folder.Time));
                }
            }

            if (Options.Move)
            {
                foreach (var folder in _folders.OrderByDescending(f => f.From.Path.Length))
                {
                    QuietlyAsync(() => remote.DeleteAsync(folder.From, isFolder: true, Job.CancellationToken));
                }
            }

            Job.Report(OperationPhase.Working, DoneBytes, TotalBytes, DoneFiles, TotalFiles, null, force: true);
        }

        private IReadOnlyList<RemoteItem>? List(ServerAddress folder)
        {
            IReadOnlyList<RemoteItem>? items = null;
            Try(folder.ToString(), () => items = Wait(remote.ListItemsAsync(folder, Job.CancellationToken)));
            return items;
        }

        private bool Walk(ServerAddress folder, RemoteItem item, string into)
        {
            Job.Checkpoint();
            var from = folder.Child(item.Entry.Name);
            if (SafeLocalName(item.Entry.Name) is not { } name)
            {
                Job.AddIssue(from.ToString(), Strings.Server_BadName);
                return false;
            }

            var to = Path.Combine(into, name);
            if (item.Entry.IsDirectory)
            {
                if ((item.Entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Job.AddIssue(from.ToString(), Strings.Server_LinkedFolder);
                    return false;
                }

                var index = _folders.Count;
                _folders.Add((from, to, item.Entry.LastWriteUtc, false));
                Job.Report(OperationPhase.Scanning, 0, 0, 0, _files.Count, from.ToString());
                var any = false;
                foreach (var inner in List(from) ?? [])
                {
                    any |= Walk(from, inner, to);
                }

                _folders[index] = _folders[index] with { HasFiles = any };
                return any;
            }

            if (!Matches(name))
            {
                return false;
            }

            _files.Add((from, to, item.Entry.Size, item.Entry.LastWriteUtc));
            return true;
        }

        private void Download(ServerAddress from, string to, long size, DateTime time)
        {
            var before = DoneBytes;
            var resume = false;
            var replace = false;
            if (fs.Stat(to) is { } existing)
            {
                if (existing.IsDirectory)
                {
                    Job.AddIssue(from.ToString(), Strings.Issue_FolderInTarget);
                    DoneBytes += size;
                    return;
                }

                var info = new ItemInfo(from.Name, FileAttributes.Normal, size, time, time);
                var answer = Decide(from.ToString(), info, to, existing, canResume: existing.Size > 0 && existing.Size < size);
                switch (answer)
                {
                    case ExistingFilePolicy.Skip:
                        DoneBytes += size;
                        return;
                    case ExistingFilePolicy.KeepBoth:
                        to = UniqueName(to, p => fs.Stat(p) is not null);
                        break;
                    case null:
                        resume = true;
                        break;
                    default:
                        replace = true;
                        break;
                }

                if (!resume && (existing.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    Try(to, () => fs.SetAttributes(to, FileAttributes.Normal));
                }
            }

            var ok = Try(from.ToString(), () =>
            {
                DoneBytes = before;
                var offset = resume ? fs.Stat(to)?.Size ?? 0 : 0;
                var writeTo = replace ? to + ".salandual-part" : to;
                var completed = false;
                try
                {
                    using (var file = new FileStream(writeTo, offset > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        file.Seek(offset, SeekOrigin.Begin);
                        file.SetLength(offset);
                        if (offset == 0)
                        {
                            Mark(writeTo, from);
                        }

                        Wait(remote.DownloadAsync(from, file, offset, bytes => Progress(before + offset + bytes, offset + bytes, size, from.Name), Job.CancellationToken));
                    }

                    if (replace)
                    {
                        File.Move(writeTo, to, overwrite: true);
                    }

                    completed = true;
                }
                finally
                {
                    if (!completed && replace)
                    {
                        Quietly(() => File.Delete(writeTo));
                    }
                }

                if ((Options.Verify || Options.Move) && size > 0 && fs.Stat(to)?.Size is { } got && got != size)
                {
                    throw new IOException(Loc.Format(Strings.Server_SizeDiffers, got, size));
                }
            });
            if (!ok)
            {
                DoneBytes = before + size;
                return;
            }

            if (Options.KeepTimes && time > DateTime.MinValue.AddDays(1))
            {
                Quietly(() => fs.SetTimes(to, time, time));
            }

            if (Options.Move)
            {
                Try(from.ToString(), () => Wait(remote.DeleteAsync(from, isFolder: false, Job.CancellationToken)));
            }

            DoneFiles++;
            DoneBytes = before + size;
            Job.Report(OperationPhase.Working, DoneBytes, TotalBytes, DoneFiles, TotalFiles, null);
        }

        private void EnsureFolder(string path)
        {
            if (fs.Stat(path) is { } existing)
            {
                if (!existing.IsDirectory)
                {
                    throw new IOException(Loc.Format(Strings.Unpack_FileInTheWay, Path.GetFileName(path)));
                }

                return;
            }

            if (Path.GetDirectoryName(path) is { } parent && !string.Equals(parent, path, StringComparison.OrdinalIgnoreCase))
            {
                EnsureFolder(parent);
            }

            fs.CreateDirectory(path);
        }
    }

    private sealed class Uploader(IOperationFileSystem fs, RemoteFileSystem remote, OperationJob job, UploadRequest request) : RemoteWork(job, request.Options)
    {
        private readonly List<(string From, ServerAddress To, long Size, DateTime Time)> _files = [];
        private readonly List<(string From, ServerAddress To, bool HasFiles)> _folders = [];
        private readonly Dictionary<string, Dictionary<string, RemoteItem>?> _there = new(StringComparer.Ordinal);

        public void Run()
        {
            Job.Report(OperationPhase.Scanning, 0, 0, 0, 0, request.Folder, force: true);
            foreach (var (name, _) in request.Items)
            {
                var path = fs.Combine(request.Folder, name);
                if (fs.Stat(path) is not { } info)
                {
                    Job.AddIssue(path, Strings.Issue_Gone);
                    continue;
                }

                Walk(path, info, request.Target);
            }

            TotalBytes = _files.Sum(f => f.Size);
            TotalFiles = _files.Count;
            Job.Report(OperationPhase.Working, 0, TotalBytes, 0, TotalFiles, null, force: true);

            foreach (var folder in _folders.Where(f => f.HasFiles || !Options.SkipEmptyFolders))
            {
                Try(folder.To.ToString(), () => EnsureFolder(folder.To));
            }

            foreach (var file in _files)
            {
                Job.Checkpoint();
                Upload(file.From, file.To, file.Size, file.Time);
            }

            if (Options.Move)
            {
                foreach (var folder in _folders.OrderByDescending(f => f.From.Length))
                {
                    Quietly(() => fs.DeleteDirectory(folder.From));
                }
            }

            Job.Report(OperationPhase.Working, DoneBytes, TotalBytes, DoneFiles, TotalFiles, null, force: true);
        }

        private bool Walk(string path, ItemInfo info, ServerAddress into)
        {
            Job.Checkpoint();
            var to = into.Child(info.Name);
            if (info.IsDirectory)
            {
                if (info.IsLink)
                {
                    Job.AddIssue(path, Strings.Server_LinkedFolder);
                    return false;
                }

                var index = _folders.Count;
                _folders.Add((path, to, false));
                Job.Report(OperationPhase.Scanning, 0, 0, 0, _files.Count, path);
                IReadOnlyList<ItemInfo> inner = [];
                Try(path, () => inner = fs.List(path, Job.CancellationToken));
                var any = false;
                foreach (var item in inner)
                {
                    any |= Walk(fs.Combine(path, item.Name), item, to);
                }

                _folders[index] = _folders[index] with { HasFiles = any };
                return any;
            }

            if (!Matches(info.Name))
            {
                return false;
            }

            _files.Add((path, to, info.Size, info.LastWriteUtc));
            return true;
        }

        private Dictionary<string, RemoteItem>? There(ServerAddress folder)
        {
            var key = folder.ToString();
            if (!_there.TryGetValue(key, out var items))
            {
                try
                {
                    items = Wait(remote.ListItemsAsync(folder, Job.CancellationToken)).GroupBy(i => i.Entry.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                }
                catch (DirectoryNotFoundException)
                {
                    items = null;
                }

                _there[key] = items;
            }

            return items;
        }

        private void EnsureFolder(ServerAddress folder)
        {
            if (folder.Parent() is not { } parent)
            {
                return;
            }

            var siblings = There(parent);
            if (siblings is null)
            {
                EnsureFolder(parent);
                siblings = There(parent);
            }

            if (siblings?.TryGetValue(folder.Name, out var existing) == true)
            {
                if (!existing.Entry.IsDirectory)
                {
                    throw new IOException(Loc.Format(Strings.Unpack_FileInTheWay, folder.Name));
                }

                return;
            }

            Wait(remote.CreateFolderAsync(folder, Job.CancellationToken));
            siblings?.Add(folder.Name, new RemoteItem(new Core.Vfs.FileEntry(folder.Name, 0, DateTime.UtcNow.Ticks, FileAttributes.Directory), ""));
            _there[folder.ToString()] = new Dictionary<string, RemoteItem>(StringComparer.Ordinal);
        }

        private void Upload(string from, ServerAddress to, long size, DateTime time)
        {
            var before = DoneBytes;
            var resume = false;
            var folder = to.Parent()!;
            if (There(folder) is { } siblings && siblings.TryGetValue(to.Name, out var existing))
            {
                if (existing.Entry.IsDirectory)
                {
                    Job.AddIssue(from, Strings.Issue_FolderInTarget);
                    DoneBytes += size;
                    return;
                }

                var there = new ItemInfo(to.Name, FileAttributes.Normal, existing.Entry.Size, existing.Entry.LastWriteUtc, existing.Entry.LastWriteUtc);
                var mine = new ItemInfo(fs.GetName(from), FileAttributes.Normal, size, time, time);
                switch (Decide(from, mine, to.ToString(), there, canResume: existing.Entry.Size > 0 && existing.Entry.Size < size))
                {
                    case ExistingFilePolicy.Skip:
                        DoneBytes += size;
                        return;
                    case ExistingFilePolicy.KeepBoth:
                        to = folder.Child(UniqueName(to.Name, n => siblings.ContainsKey(n)));
                        break;
                    case null:
                        resume = true;
                        break;
                }
            }

            var ok = Try(from, () =>
            {
                DoneBytes = before;
                var offset = resume ? Wait(remote.StatAsync(to, Job.CancellationToken))?.Entry.Size ?? 0 : 0;
                using (var file = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16))
                {
                    file.Seek(Math.Min(offset, file.Length), SeekOrigin.Begin);
                    Wait(remote.UploadAsync(to, file, offset, bytes => Progress(before + offset + bytes, offset + bytes, size, to.Name), Job.CancellationToken));
                }

                if (Options.Verify || Options.Move)
                {
                    var got = Wait(remote.StatAsync(to, Job.CancellationToken))?.Entry.Size ?? -1;
                    if (got != size)
                    {
                        throw new IOException(Loc.Format(Strings.Server_SizeDiffers, Math.Max(0, got), size));
                    }
                }
            });
            if (There(folder) is { } after)
            {
                after[to.Name] = new RemoteItem(new Core.Vfs.FileEntry(to.Name, size, DateTime.UtcNow.Ticks, FileAttributes.Normal), "");
            }

            if (!ok)
            {
                DoneBytes = before + size;
                return;
            }

            if (Options.KeepTimes && time > DateTime.MinValue.AddDays(1))
            {
                Wait(remote.SetTimeAsync(to, time, Job.CancellationToken));
            }

            if (Options.Move)
            {
                Try(from, () =>
                {
                    if (fs.Stat(from) is { } mine && (mine.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        fs.SetAttributes(from, mine.Attributes & ~FileAttributes.ReadOnly);
                    }

                    fs.DeleteFile(from);
                });
            }

            DoneFiles++;
            DoneBytes = before + size;
            Job.Report(OperationPhase.Working, DoneBytes, TotalBytes, DoneFiles, TotalFiles, null);
        }
    }
}

internal abstract class RemoteWork(OperationJob job, TransferSettings options)
{
    private readonly FileMask? _filter = string.IsNullOrWhiteSpace(options.Filter) ? null : FileMask.Parse(options.Filter);
    private ExistingFilePolicy _policy = options.IfExists;
    private bool _resumeAll, _skipErrors;
    private int _conflicts;
    private long _limit = options.SpeedLimit, _limitBytes;
    private TimeSpan _limitTime;

    protected OperationJob Job => job;

    protected TransferSettings Options => options;

    protected long DoneBytes { get; set; }

    protected long TotalBytes { get; set; }

    protected int DoneFiles { get; set; }

    protected int TotalFiles { get; set; }

    protected bool Matches(string name) => _filter is null || _filter.IsMatch(name);

    protected void Progress(long doneBytes, long fileDone, long fileSize, string name)
    {
        Job.Checkpoint();
        DoneBytes = doneBytes;
        Job.Report(OperationPhase.Working, doneBytes, TotalBytes, DoneFiles, TotalFiles, name, currentDone: fileDone, currentTotal: fileSize);
        Limit();
    }

    private void Limit()
    {
        var limit = Job.SpeedLimit;
        if (limit != _limit)
        {
            _limit = limit;
            _limitBytes = DoneBytes;
            _limitTime = Job.Elapsed;
        }

        if (limit <= 0)
        {
            return;
        }

        var due = TimeSpan.FromSeconds((double)(DoneBytes - _limitBytes) / limit);
        var ahead = due - (Job.Elapsed - _limitTime);
        if (ahead > TimeSpan.FromMilliseconds(15))
        {
            Job.CancellationToken.WaitHandle.WaitOne(ahead < TimeSpan.FromSeconds(1) ? ahead : TimeSpan.FromSeconds(1));
            Job.CancellationToken.ThrowIfCancellationRequested();
        }
    }

    protected ExistingFilePolicy? Decide(string source, ItemInfo sourceInfo, string target, ItemInfo targetInfo, bool canResume)
    {
        if (_resumeAll && canResume)
        {
            return null;
        }

        var policy = _policy;
        if (policy == ExistingFilePolicy.Ask)
        {
            var answer = Job.Ask(new ConflictQuestion(source, sourceInfo, target, targetInfo, ++_conflicts, canResume));
            if (answer.Kind == AnswerKind.Resume && canResume)
            {
                _resumeAll |= answer.ForAll;
                return null;
            }

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
            ? targetInfo.LastWriteUtc < sourceInfo.LastWriteUtc ? ExistingFilePolicy.Overwrite : ExistingFilePolicy.Skip
            : policy;
    }

    protected bool Try(string path, Action work)
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
                if (!_skipErrors)
                {
                    var answer = Job.Ask(new ErrorQuestion(path, Job.Kind.ToString(), ex.Message));
                    if (answer.Kind == AnswerKind.Retry)
                    {
                        continue;
                    }

                    _skipErrors |= answer.ForAll;
                }

                Job.AddIssue(path, ex.Message);
                return false;
            }
        }
    }

    protected static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    protected static void Wait(Task task) => task.GetAwaiter().GetResult();

    protected static string UniqueName(string path, Func<string, bool> taken)
    {
        var cut = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1;
        var (folder, name) = (path[..cut], path[cut..]);
        var dot = name.LastIndexOf('.');
        var (stem, ext) = dot > 0 ? (name[..dot], name[dot..]) : (name, "");
        for (var i = 2; ; i++)
        {
            var candidate = folder + $"{stem} ({i}){ext}";
            if (!taken(candidate))
            {
                return candidate;
            }
        }
    }

    internal static void Mark(string path, ServerAddress from)
        => Quietly(() => File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=" + from.Scheme + from.Host + "/\r\n"));

    protected static void Quietly(Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    protected static void QuietlyAsync(Func<Task> work) => Quietly(() => work().GetAwaiter().GetResult());
}
