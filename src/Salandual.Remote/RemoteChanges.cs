// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/RemoteChanges.cs

using Salandual.Core.Operations;
using Salandual.Core.Resources;

namespace Salandual.Remote;

public enum PermissionsScope
{
    Itself,
    Files,
    Folders,
    All,
}

public static class RemoteChanges
{
    public static OperationJob Delete(RemoteFileSystem remote, ServerAddress folder, IReadOnlyList<(string Name, bool IsDirectory)> items)
    {
        var what = FileOperations.Count([.. items.Select(i => new OperationSource(i.Name, i.IsDirectory))]);
        return new OperationJob(OperationKind.Delete, Loc.Format(Strings.Job_DeletingIn, what, folder.Host), folder.Host, [folder.Root],
            job => new Deleter(remote, job, folder, items).Run())
        {
            Subject = items.Count == 1 ? items[0].Name : null,
        };
    }

    public static OperationJob SetPermissions(RemoteFileSystem remote, ServerAddress folder, IReadOnlyList<(string Name, bool IsDirectory)> items, int mode, PermissionsScope scope)
    {
        var what = FileOperations.Count([.. items.Select(i => new OperationSource(i.Name, i.IsDirectory))]);
        return new OperationJob(OperationKind.Change, Loc.Format(Strings.Job_Permissions, what, Convert.ToString(mode & 0xFFF, 8)), folder.Host, [folder.Root],
            job => new Permissions(remote, job, folder, items, mode, scope).Run())
        {
            Subject = items.Count == 1 ? items[0].Name : null,
        };
    }

    public static OperationJob MoveWithin(RemoteFileSystem remote, ServerAddress folder, IReadOnlyList<(string Name, bool IsDirectory)> items, ServerAddress target, ExistingFilePolicy ifExists)
    {
        var what = FileOperations.Count([.. items.Select(i => new OperationSource(i.Name, i.IsDirectory))]);
        return new OperationJob(OperationKind.Move, Loc.Format(Strings.Job_Moving, what), folder.Path + " → " + target.Path, [folder.Root],
            job => new Mover(remote, job, folder, items, target, ifExists).Run())
        {
            Subject = items.Count == 1 ? items[0].Name : null,
        };
    }

    private abstract class Walker(RemoteFileSystem remote, OperationJob job, TransferSettings? settings = null) : RemoteWork(job, settings ?? TransferSettings.Default)
    {
        protected RemoteFileSystem Remote => remote;

        protected IReadOnlyList<RemoteItem>? List(ServerAddress folder)
        {
            IReadOnlyList<RemoteItem>? items = null;
            Try(folder.ToString(), () => items = Wait(remote.ListItemsAsync(folder, Job.CancellationToken)));
            return items;
        }

        protected IEnumerable<RemoteItem> Chosen(ServerAddress folder, IReadOnlyList<(string Name, bool IsDirectory)> items)
        {
            var listing = List(folder);
            if (listing is null)
            {
                yield break;
            }

            foreach (var (name, _) in items)
            {
                if (listing.FirstOrDefault(i => i.Entry.Name == name) is { } item)
                {
                    yield return item;
                }
                else
                {
                    Job.AddIssue(folder.Child(name).ToString(), Strings.Issue_Gone);
                }
            }
        }

        protected static bool IsFolder(RemoteItem item) => item.Entry.IsDirectory && (item.Entry.Attributes & FileAttributes.ReparsePoint) == 0;

        protected void Below(ServerAddress folder, List<(ServerAddress Path, bool IsFolder)> into)
        {
            foreach (var item in List(folder) ?? [])
            {
                Job.Checkpoint();
                var path = folder.Child(item.Entry.Name);
                var isFolder = IsFolder(item);
                into.Add((path, isFolder));
                if (isFolder)
                {
                    Job.Report(OperationPhase.Scanning, 0, 0, 0, into.Count, path.ToString());
                    Below(path, into);
                }
            }
        }
    }

    private sealed class Deleter(RemoteFileSystem remote, OperationJob job, ServerAddress folder, IReadOnlyList<(string Name, bool IsDirectory)> items) : Walker(remote, job)
    {
        public void Run()
        {
            Job.Report(OperationPhase.Scanning, 0, 0, 0, 0, folder.ToString(), force: true);
            var all = new List<(ServerAddress Path, bool IsFolder)>();
            foreach (var item in Chosen(folder, items).ToList())
            {
                var path = folder.Child(item.Entry.Name);
                var isFolder = IsFolder(item);
                all.Add((path, isFolder));
                if (isFolder)
                {
                    Below(path, all);
                }
            }

            var order = all.Where(a => !a.IsFolder).Concat(all.Where(a => a.IsFolder).OrderByDescending(a => a.Path.Path.Count(c => c == '/'))).ToList();
            TotalFiles = order.Count;
            var failed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (path, isFolder) in order)
            {
                Job.Checkpoint();
                if (isFolder && failed.Any(f => f.StartsWith(path.Path + "/", StringComparison.Ordinal)))
                {
                    failed.Add(path.Path);
                    continue;
                }

                Job.Report(OperationPhase.Working, 0, 0, DoneFiles, TotalFiles, path.Name);
                if (Try(path.ToString(), () => Wait(Remote.DeleteAsync(path, isFolder, Job.CancellationToken))))
                {
                    DoneFiles++;
                }
                else
                {
                    failed.Add(path.Path);
                }
            }

            Job.Report(OperationPhase.Working, 0, 0, DoneFiles, TotalFiles, null, force: true);
        }
    }

    private sealed class Permissions(RemoteFileSystem remote, OperationJob job, ServerAddress folder, IReadOnlyList<(string Name, bool IsDirectory)> items, int mode, PermissionsScope scope) : Walker(remote, job)
    {
        public void Run()
        {
            Job.Report(OperationPhase.Scanning, 0, 0, 0, 0, folder.ToString(), force: true);
            var all = new List<ServerAddress>();
            foreach (var item in Chosen(folder, items).ToList())
            {
                var path = folder.Child(item.Entry.Name);
                all.Add(path);
                if (scope != PermissionsScope.Itself && IsFolder(item))
                {
                    var below = new List<(ServerAddress Path, bool IsFolder)>();
                    Below(path, below);
                    all.AddRange(below.Where(b => scope == PermissionsScope.All || (scope == PermissionsScope.Files) != b.IsFolder).Select(b => b.Path));
                }
            }

            TotalFiles = all.Count;
            foreach (var path in all)
            {
                Job.Checkpoint();
                Job.Report(OperationPhase.Working, 0, 0, DoneFiles, TotalFiles, path.Name);
                if (Try(path.ToString(), () => Wait(Remote.SetPermissionsAsync(path, mode, Job.CancellationToken))))
                {
                    DoneFiles++;
                }
            }

            Job.Report(OperationPhase.Working, 0, 0, DoneFiles, TotalFiles, null, force: true);
        }
    }

    private sealed class Mover(RemoteFileSystem remote, OperationJob job, ServerAddress folder, IReadOnlyList<(string Name, bool IsDirectory)> items, ServerAddress target, ExistingFilePolicy ifExists)
        : Walker(remote, job, new TransferSettings(ifExists))
    {
        public void Run()
        {
            if (target.Path == folder.Path)
            {
                throw new IOException(Strings.Server_SameFolder);
            }

            if (items.Any(i => i.IsDirectory && (target.Path + "/").StartsWith(folder.Child(i.Name).Path + "/", StringComparison.Ordinal)))
            {
                throw new IOException(Strings.Issue_FolderMovedIntoItself);
            }

            var chosen = Chosen(folder, items).ToList();
            var there = List(target)?.ToDictionary(i => i.Entry.Name, StringComparer.Ordinal);
            if (there is null)
            {
                return;
            }

            TotalFiles = chosen.Count;
            foreach (var item in chosen)
            {
                Job.Checkpoint();
                var from = folder.Child(item.Entry.Name);
                var to = target.Child(item.Entry.Name);
                Job.Report(OperationPhase.Working, 0, 0, DoneFiles, TotalFiles, item.Entry.Name);
                if (there.TryGetValue(item.Entry.Name, out var existing))
                {
                    if (item.Entry.IsDirectory || existing.Entry.IsDirectory)
                    {
                        Job.AddIssue(from.ToString(), Strings.Issue_FolderInTarget);
                        continue;
                    }

                    var mine = new ItemInfo(item.Entry.Name, FileAttributes.Normal, item.Entry.Size, item.Entry.LastWriteUtc, item.Entry.LastWriteUtc);
                    var theirs = new ItemInfo(existing.Entry.Name, FileAttributes.Normal, existing.Entry.Size, existing.Entry.LastWriteUtc, existing.Entry.LastWriteUtc);
                    var answer = Decide(from.ToString(), mine, to.ToString(), theirs, canResume: false);
                    if (answer == ExistingFilePolicy.Skip)
                    {
                        continue;
                    }

                    if (answer == ExistingFilePolicy.KeepBoth)
                    {
                        to = target.Child(UniqueName(item.Entry.Name, there.ContainsKey));
                    }
                    else if (!Try(to.ToString(), () => Wait(Remote.DeleteAsync(to, isFolder: false, Job.CancellationToken))))
                    {
                        continue;
                    }
                }

                var moved = to;
                if (Try(from.ToString(), () => Wait(Remote.RenameAsync(from, moved, Job.CancellationToken))))
                {
                    DoneFiles++;
                    there[moved.Name] = item;
                }
            }

            Job.Report(OperationPhase.Working, 0, 0, DoneFiles, TotalFiles, null, force: true);
        }
    }
}
