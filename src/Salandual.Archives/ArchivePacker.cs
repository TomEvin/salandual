// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/ArchivePacker.cs

using Salandual.Core.Diagnostics;
using Salandual.Core.Operations;
using Salandual.Core.Resources;

namespace Salandual.Archives;

public sealed record PackRequest(string Folder, IReadOnlyList<string> Names, string Target, PackSettings Settings, PackExisting IfThere = PackExisting.Replace, bool DeleteAfter = false,
    ArchiveTarget? Into = null, ExistingFilePolicy IfInside = ExistingFilePolicy.Ask)
{
    public string ArchiveName => Path.GetFileName(Target);
}

public sealed record ArchiveTarget(string ArchivePath, string Inner, Func<CancellationToken, ArchiveContents> Read)
{
    public string ArchiveName => Path.GetFileName(ArchivePath);

    public string Below(string name) => Inner.Length == 0 ? name : Inner + "\\" + name;
}

public static class ArchivePacker
{
    public static OperationJob Pack(IOperationFileSystem fs, PackRequest request, ArchivePasswordQuestion? askPassword)
    {
        var from = fs.VolumeOf(request.Folder);
        var to = fs.VolumeOf(request.Target);
        var route = string.Equals(from, to, StringComparison.OrdinalIgnoreCase) ? request.Folder : from.TrimEnd('\\') + " → " + to.TrimEnd('\\');
        var title = Loc.Format(Strings.Job_Packing, Loc.Plural(request.Names.Count, Strings.Pack_Items_One, Strings.Pack_Items_Other), request.ArchiveName);
        return new OperationJob(OperationKind.Pack, title, route, new[] { from, to }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            job => new Packer(fs, job, request, askPassword).Run())
        {
            Subject = request.ArchiveName,
        };
    }

    public static List<PackInput> Collect(IOperationFileSystem fs, string folder, IReadOnlyList<string> names, Func<string, bool> leaveOut, Action<string, string> problem, CancellationToken cancellation)
    {
        var inputs = new List<PackInput>();

        void Add(string path, string inside, ItemInfo info)
        {
            if (leaveOut(path))
            {
                return;
            }

            inputs.Add(new PackInput(path, inside, info.IsDirectory, info.IsDirectory ? 0 : info.Size, info.LastWriteUtc, info.CreatedUtc, info.Attributes));
            if (!info.IsDirectory)
            {
                return;
            }

            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                problem(path, Strings.Pack_LinkNotFollowed);
                return;
            }

            IReadOnlyList<ItemInfo> children;
            try
            {
                children = fs.List(path, cancellation);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problem(path, ex.Message);
                return;
            }

            foreach (var child in children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                cancellation.ThrowIfCancellationRequested();
                Add(fs.Combine(path, child.Name), inside + "\\" + child.Name, child);
            }
        }

        foreach (var name in names)
        {
            var path = fs.Combine(folder, name);
            if (fs.Stat(path) is { } info)
            {
                Add(path, name, info);
            }
            else
            {
                problem(path, Strings.Pack_NotThere);
            }
        }

        return inputs;
    }

    public static string NewName(IOperationFileSystem fs, string target)
    {
        var folder = Path.GetDirectoryName(target) ?? "";
        var name = Path.GetFileName(target);
        var ext = name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase) ? name[^7..] : Path.GetExtension(name);
        var stem = name[..^ext.Length];
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (fs.Stat(candidate) is null && fs.Stat(candidate + ".001") is null)
            {
                return candidate;
            }
        }
    }

    public static string FirstFile(PackRequest request) => request.Settings.VolumeSize > 0 ? request.Target + ".001" : request.Target;

    public static string? WhyNotChangeable(ArchiveLocation location)
    {
        var name = Path.GetFileName(location.ArchivePath);
        if (!string.Equals(location.ArchivePath, location.FileOnDisk, StringComparison.OrdinalIgnoreCase))
        {
            return Loc.Format(Strings.Change_Inside, name);
        }

        if (VolumeStream.ArchiveName(location.FileOnDisk) != Path.GetFileName(location.FileOnDisk) || location.FileOnDisk.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) && location.FileOnDisk.Contains(".part", StringComparison.OrdinalIgnoreCase))
        {
            return Loc.Format(Strings.Change_Split, name);
        }

        return ArchiveWriter.CanWrite(location.Contents.FormatName) ? null : Loc.Format(Strings.Change_Format, name, FormatName(location.Contents.FormatName));
    }

    public static string FormatName(string format) => format switch
    {
        "Rar" or "Rar5" => "RAR",
        "gzip" => "GZ",
        "bzip2" => "BZ2",
        "7z" => "7z",
        _ => format.ToUpperInvariant(),
    };

    public static PackSettings SettingsFor(ArchiveContents contents, PackLevel? level = null, bool storeCompressed = true)
    {
        var format = ArchiveWriter.FormatOf(contents.FormatName) ?? throw new ArchiveException(ArchiveProblem.CantChange, Path.GetFileName(contents.ArchivePath));
        return new PackSettings(format, level ?? LevelOf(contents), contents.Password, contents.NamesHidden, 0, storeCompressed);
    }

    public static PackLevel LevelOf(ArchiveContents contents)
        => contents.FormatName == "zip" && contents.Items.Where(i => !i.IsDirectory && i.Size > 0).All(i => i.PackedSize >= i.Size) && contents.Items.Any(i => !i.IsDirectory && i.Size > 0)
            ? PackLevel.Store
            : PackLevel.Normal;

    private static ArchiveContents ReadToChange(ArchiveTarget target, OperationJob job, ArchivePasswordQuestion? askPassword)
    {
        var contents = target.Read(job.CancellationToken);
        if (ArchivePacker.WhyNotChangeable(new ArchiveLocation(target.ArchivePath, target.Inner, contents) { FileOnDisk = target.ArchivePath }) is { } why)
        {
            throw new IOException(why);
        }

        if (contents.Password is null && contents.Items.Where(i => i.Encrypted && !i.IsDirectory).MinBy(i => i.Size) is { } smallest)
        {
            var result = ArchiveReader.Extract(contents, [smallest.Index], _ => null, askPassword, target.ArchiveName, null, job.CancellationToken, test: true);
            if (result.GetValueOrDefault(smallest.Index, ExtractResult.Ok) == ExtractResult.WrongPassword || contents.Password is null)
            {
                throw new ArchiveException(ArchiveProblem.PasswordCancelled, target.ArchiveName);
            }
        }

        return contents;
    }

    public static OperationJob Change(IOperationFileSystem fs, ArchiveTarget target, string title, Func<ArchiveContents, ArchiveEdit> edit, ArchivePasswordQuestion? askPassword)
    {
        var volume = fs.VolumeOf(target.ArchivePath);
        return new OperationJob(OperationKind.Change, title, Path.GetDirectoryName(target.ArchivePath) ?? volume, [volume], job =>
        {
            job.Report(OperationPhase.Scanning, 0, 0, 0, 0, target.ArchiveName, force: true);
            var contents = ReadToChange(target, job, askPassword);
            var change = edit(contents);
            var total = contents.FileLength + change.Add.Sum(i => i.Size);
            var temp = target.ArchivePath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            ArchiveWriter.Update(contents, temp, change, SettingsFor(contents),
                new WriteCallbacks((done, all) =>
                {
                    job.Checkpoint();
                    job.Report(OperationPhase.Working, Math.Min(done, Math.Max(all, total)), Math.Max(all, total), 0, 0, target.ArchiveName);
                }, job.CancellationToken), askPassword, target.ArchiveName);
            try
            {
                job.Checkpoint();
                File.Move(temp, target.ArchivePath, overwrite: true);
            }
            catch
            {
                File.Delete(temp);
                throw;
            }

            job.Report(OperationPhase.Working, total, total, 0, 0, null, force: true);
        })
        {
            Subject = target.ArchiveName,
        };
    }

    public static HashSet<int> EntriesOf(ArchiveContents contents, string inner, IEnumerable<string> names)
    {
        var indices = new HashSet<int>();
        foreach (var name in names)
        {
            var path = inner.Length == 0 ? name : inner + "\\" + name;
            foreach (var item in contents.Items.Where(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase) || i.Path.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                indices.Add(item.Index);
            }
        }

        return indices;
    }

    public static ArchiveEdit Renaming(ArchiveContents contents, string inner, string oldName, string newName)
    {
        var from = inner.Length == 0 ? oldName : inner + "\\" + oldName;
        var to = inner.Length == 0 ? newName : inner + "\\" + newName;
        var rename = new Dictionary<int, string>();
        foreach (var item in contents.Items)
        {
            if (string.Equals(item.Path, from, StringComparison.OrdinalIgnoreCase))
            {
                rename[item.Index] = to;
            }
            else if (item.Path.StartsWith(from + "\\", StringComparison.OrdinalIgnoreCase))
            {
                rename[item.Index] = to + item.Path[from.Length..];
            }
        }

        return new ArchiveEdit(new HashSet<int>(), rename, []);
    }

    public static ArchiveEdit NewFolder(string inner, string name)
    {
        var now = DateTime.UtcNow;
        var path = inner.Length == 0 ? name : inner + "\\" + name;
        return ArchiveEdit.Adding([new PackInput("", path, true, 0, now, now, FileAttributes.Directory)]);
    }

    private sealed class Packer(IOperationFileSystem fs, OperationJob job, PackRequest request, ArchivePasswordQuestion? askPassword)
    {
        private readonly PackSettings _settings = request.Settings;
        private bool _skipErrors, _leftOut;
        private long _total;
        private int _files;
        private int _conflicts;
        private ExistingFilePolicy _policy = request.IfInside;

        public void Run()
        {
            job.Report(OperationPhase.Scanning, 0, 0, 0, 0, null, force: true);
            var temp = request.Target + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            var inputs = Collect(fs, request.Folder, request.Names, IsArchive, (path, reason) =>
            {
                job.AddIssue(path, reason);
                _leftOut = true;
            }, job.CancellationToken);
            if (request.Into is { Inner.Length: > 0 } into)
            {
                inputs = [.. inputs.Select(i => i with { PathInArchive = into.Below(i.PathInArchive) })];
            }
            _files = inputs.Count(i => !i.IsDirectory);
            _total = inputs.Sum(i => i.Size);
            job.Report(OperationPhase.Working, 0, _total, 0, _files, request.ArchiveName, force: true);

            var existing = request.Into is { } target ? ReadToChange(target, job, askPassword)
                : request.IfThere == PackExisting.Add && fs.Stat(request.Target) is { IsDirectory: false } ? Open() : null;
            var callbacks = new WriteCallbacks(Progress, job.CancellationToken, OpenFailed);
            IReadOnlyList<string> written;
            if (existing is not null)
            {
                var edit = Merge(existing, inputs);
                var settings = request.Into is not null ? SettingsFor(existing, _settings.Level, _settings.StoreCompressed) : _settings with
                {
                    Format = ArchiveWriter.FormatOf(existing.FormatName) ?? throw new ArchiveException(ArchiveProblem.CantChange, request.ArchiveName),
                    Password = _settings.Password ?? existing.Password,
                    HideNames = _settings.HideNames || existing.NamesHidden,
                    VolumeSize = 0,
                };
                written = ArchiveWriter.Update(existing, temp, edit, settings, callbacks, askPassword, request.ArchiveName);
            }
            else
            {
                written = ArchiveWriter.Create(temp, inputs, _settings, callbacks, request.ArchiveName);
            }

            List<string> finals = written.Count == 1 && _settings.VolumeSize == 0 || existing is not null
                ? [request.Target]
                : [.. written.Select((_, i) => request.Target + "." + (i + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture))];
            var moved = 0;
            try
            {
                job.Checkpoint();
                if (existing is null && request.IfThere == PackExisting.Replace)
                {
                    RecycleOld(finals);
                }

                for (; moved < written.Count; moved++)
                {
                    File.Move(written[moved], finals[moved], overwrite: existing is not null);
                }
            }
            catch
            {
                foreach (var left in written.Skip(moved))
                {
                    Quietly(() => File.Delete(left));
                }

                throw;
            }

            job.Report(OperationPhase.Working, _total, _total, _files, _files, null, force: true);

            var tested = false;
            if (_settings.Password is not null && _settings.CanEncrypt || request.DeleteAfter)
            {
                tested = Test(finals[0]);
            }

            if (request.DeleteAfter)
            {
                if (_leftOut || !tested)
                {
                    job.AddIssue(request.Target, Strings.Pack_FilesKept);
                }
                else
                {
                    DeleteSources();
                }
            }
        }

        private bool IsArchive(string path)
        {
            if (!path.StartsWith(request.Target, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rest = path[request.Target.Length..];
            return rest.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(rest, @"^\.(\d{3}|[0-9a-f]{8}\.tmp)$");
        }

        private ArchiveContents Open()
        {
            try
            {
                return ArchiveReader.Open(request.Target, request.ArchiveName, askPassword, searchInside: false, job.CancellationToken);
            }
            catch (ArchiveException ex) when (ex.Problem is ArchiveProblem.NotAnArchive)
            {
                throw new ArchiveException(ArchiveProblem.CantChange, request.ArchiveName);
            }
        }

        private ArchiveEdit Merge(ArchiveContents existing, List<PackInput> inputs)
        {
            var remove = new HashSet<int>();
            var add = new List<PackInput>();
            var taken = new HashSet<string>(existing.Items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var input in inputs)
            {
                var there = existing.Find(input.PathInArchive);
                if (there is null && !(input.IsDirectory && existing.IsFolder(input.PathInArchive)))
                {
                    add.Add(input);
                    taken.Add(input.PathInArchive);
                    continue;
                }

                if (input.IsDirectory || there is null || there.IsDirectory)
                {
                    if (!input.IsDirectory)
                    {
                        job.AddIssue(input.SourcePath, Strings.Pack_FolderInArchive);
                        _leftOut = true;
                    }

                    continue;
                }

                switch (Decide(input, there))
                {
                    case ExistingFilePolicy.Overwrite:
                        remove.Add(there.Index);
                        add.Add(input);
                        break;
                    case ExistingFilePolicy.KeepBoth:
                        var path = UniquePath(input.PathInArchive, taken);
                        taken.Add(path);
                        add.Add(input with { PathInArchive = path });
                        break;
                    default:
                        _total -= input.Size;
                        _files--;
                        break;
                }
            }

            return new ArchiveEdit(remove, new Dictionary<int, string>(), add);
        }

        private ExistingFilePolicy Decide(PackInput input, ArchiveItem there)
        {
            var policy = _policy;
            var time = there.ModifiedUtc ?? DateTime.MinValue;
            var existingInfo = new ItemInfo(there.Name, there.Attributes & ~(FileAttributes.Encrypted | FileAttributes.Directory), there.Size, time, time);
            if (policy == ExistingFilePolicy.Ask)
            {
                var info = new ItemInfo(Path.GetFileName(input.SourcePath), input.Attributes, input.Size, input.CreatedUtc, input.ModifiedUtc);
                var answer = job.Ask(new ConflictQuestion(input.SourcePath, info, Path.Combine(request.Target, there.Path), existingInfo, ++_conflicts));
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
                ? time < input.ModifiedUtc ? ExistingFilePolicy.Overwrite : ExistingFilePolicy.Skip
                : policy;
        }

        private static string UniquePath(string path, HashSet<string> taken)
        {
            var cut = path.LastIndexOf('\\');
            var (folder, name) = cut < 0 ? ("", path) : (path[..(cut + 1)], path[(cut + 1)..]);
            var dot = name.LastIndexOf('.');
            var (stem, ext) = dot > 0 ? (name[..dot], name[dot..]) : (name, "");
            for (var i = 2; ; i++)
            {
                var candidate = $"{folder}{stem} ({i}){ext}";
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private void Progress(long done, long total)
        {
            job.Checkpoint();
            var all = Math.Max(_total, total);
            job.Report(OperationPhase.Working, Math.Min(done, all), all, 0, _files, request.ArchiveName);
        }

        private bool OpenFailed(PackInput input, Exception ex)
        {
            if (!_skipErrors)
            {
                var answer = job.Ask(new ErrorQuestion(input.PathInArchive, "Pack", ex.Message));
                if (answer.Kind == AnswerKind.Retry)
                {
                    return false;
                }

                _skipErrors |= answer.ForAll;
            }

            job.AddIssue(Path.Combine(request.Folder, input.PathInArchive), ex.Message);
            _leftOut = true;
            return true;
        }

        private void RecycleOld(IReadOnlyList<string> finals)
        {
            foreach (var path in finals.Where(p => fs.Stat(p) is { IsDirectory: false }))
            {
                if (!fs.CanRecycle(path, out _) || fs.Recycle(path) != RecycleResult.Recycled)
                {
                    fs.DeleteFile(path);
                }
            }
        }

        private bool Test(string first)
        {
            job.Report(OperationPhase.Working, 0, _total, 0, _files, request.ArchiveName, force: true);
            try
            {
                var contents = ArchiveReader.Open(first, request.ArchiveName, askPassword, searchInside: false, job.CancellationToken);
                var files = contents.Items.Where(i => !i.IsDirectory).ToList();
                var results = ArchiveReader.Extract(contents, [.. files.Select(i => i.Index)], _ => null, askPassword, request.ArchiveName,
                    bytes =>
                    {
                        job.Checkpoint();
                        job.Report(OperationPhase.Working, Math.Min(bytes, _total), _total, 0, _files, request.ArchiveName);
                    },
                    job.CancellationToken, test: true);
                var bad = 0;
                foreach (var item in files)
                {
                    if (results.GetValueOrDefault(item.Index, ExtractResult.Ok) is var result and not ExtractResult.Ok)
                    {
                        job.AddIssue(item.Path, ArchiveSafety.Text(result));
                        bad++;
                    }
                }

                return bad == 0;
            }
            catch (ArchiveException ex) when (ex.Problem is ArchiveProblem.PasswordCancelled or ArchiveProblem.WrongPassword)
            {
                job.AddIssue(request.Target, Strings.Pack_NotTested);
                return false;
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

        private void DeleteSources()
        {
            foreach (var name in request.Names)
            {
                job.Checkpoint();
                var path = fs.Combine(request.Folder, name);
                try
                {
                    if (!fs.CanRecycle(path, out var reason))
                    {
                        job.AddIssue(path, reason);
                    }
                    else if (fs.Recycle(path) != RecycleResult.Recycled)
                    {
                        job.AddIssue(path, Strings.Pack_NotRecycled);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    job.AddIssue(path, ex.Message);
                }
            }

            AppLog.Note(Loc.Format(Strings.Log_PackDeleted, request.Names.Count));
        }
    }
}
