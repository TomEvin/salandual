// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Archives/ArchiveFileSystem.cs

using System.Diagnostics;
using Salandual.Core.Vfs;

namespace Salandual.Archives;

public sealed record ArchiveProgress(string Name, long Done, long Total);

public sealed record ArchiveLocation(string ArchivePath, string Inner, ArchiveContents Contents)
{
    public required string FileOnDisk { get; init; }
}

public sealed class ArchiveFileSystem : IFileSystemProvider
{
    private const int Kept = 16;

    private readonly IFileSystemProvider _local;
    private readonly Func<string> _tempFolder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Opened> _opened = [];
    private readonly Dictionary<string, string> _kept = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Opened(string Display, ArchiveContents Contents, string FileOnDisk, string? TempCopy);

    public ArchiveFileSystem(IFileSystemProvider local, Func<string> tempFolder)
    {
        _local = local;
        _tempFolder = tempFolder;
    }

    public ArchivePasswordQuestion? AskPassword { get; set; }

    public event Action<ArchiveProgress>? Progress;

    public ProviderCapabilities Capabilities => _local.Capabilities;

    public async Task<DirectoryListing> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        var trimmed = path.Length > 3 ? path.TrimEnd('\\') : path;
        if (LocateCached(trimmed) is null)
        {
            try
            {
                return await _local.ListAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && ex is not ArchiveException)
            {
                if (FileAlong(trimmed) is null)
                {
                    throw;
                }
            }
        }

        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var location = Resolve(trimmed, cancellationToken);
            var entries = location.Contents.List(location.Inner) ?? throw new DirectoryNotFoundException(trimmed);
            return new DirectoryListing(trimmed, entries, sw.Elapsed);
        }, cancellationToken).ConfigureAwait(false);
    }

    public string? GetParent(string path) => _local.GetParent(path);

    public string? GetRoot(string path) => _local.GetRoot(path);

    public string GetName(string path) => _local.GetName(path);

    public string Combine(string directory, string name) => _local.Combine(directory, name);

    public IDisposable Watch(string path, Action<DirectoryChange> onChange, Action<Exception> onError)
    {
        if (LocateCached(path) is not { } location)
        {
            return _local.Watch(path, onChange, onError);
        }

        var file = location.FileOnDisk;
        var folder = Path.GetDirectoryName(file) ?? file;
        var length = location.Contents.FileLength;
        return _local.Watch(folder, change =>
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length != length || info.LastWriteTimeUtc != location.Contents.FileWriteUtc)
            {
                onChange(change with { Path = path });
            }
        }, onError);
    }

    public IDisposable? WatchTree(string path, Action<IReadOnlyCollection<string>?> onChange, Action<Exception> onError)
        => LocateCached(path) is null ? _local.WatchTree(path, onChange, onError) : null;

    public ArchiveLocation? LocateCached(string path)
    {
        lock (_opened)
        {
            Opened? best = null;
            string? inner = null;
            foreach (var opened in _opened)
            {
                if (ArchivePaths.Inside(path, opened.Display) is { } i && (best is null || opened.Display.Length > best.Display.Length))
                {
                    best = opened;
                    inner = i;
                }
            }

            return best is null ? null : new ArchiveLocation(best.Display, inner!, best.Contents) { FileOnDisk = best.FileOnDisk };
        }
    }

    public Task<ArchiveLocation> LocateAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run(() => Resolve(path.Length > 3 ? path.TrimEnd('\\') : path, cancellationToken), cancellationToken);

    public Task<string> CopyOutAsync(string path, string folder, Action<long>? progress = null, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var (location, item) = FileAt(path, cancellationToken);
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, ArchivePaths.SafeFileName(item.Name));
            Unpack(location, item, target, done =>
            {
                progress?.Invoke(done);
                Report(item.Name, done, item.Size);
            }, cancellationToken);
            return target;
        }, cancellationToken);

    public Stream OpenRead(string path, CancellationToken cancellationToken = default)
    {
        if (GetParent(path) is not { } parent || LocateCached(parent) is null)
        {
            return new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        }

        var (location, item) = FileAt(path, cancellationToken);
        var folder = Directory.CreateDirectory(_tempFolder()).FullName;
        var target = Path.Combine(folder, "compare-" + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        Unpack(location, item, target, null, cancellationToken);
        return new FileStream(target, FileMode.Open, System.IO.FileAccess.Read, FileShare.Delete, 1 << 16, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
    }

    private (ArchiveLocation Location, ArchiveItem Item) FileAt(string path, CancellationToken cancellationToken)
    {
        var parent = GetParent(path) ?? throw new FileNotFoundException(path);
        var location = Resolve(parent, cancellationToken);
        var inner = location.Inner.Length == 0 ? GetName(path) : location.Inner + "\\" + GetName(path);
        var item = location.Contents.Find(inner);
        return item is null || item.IsDirectory ? throw new FileNotFoundException(path) : (location, item);
    }

    public void Leave(IReadOnlyCollection<string> stillOpen)
    {
        lock (_opened)
        {
            foreach (var opened in _opened.Where(o => !stillOpen.Any(p => ArchivePaths.Inside(p, o.Display) is not null)).ToList())
            {
                opened.Contents.Password = null;
                _kept.Remove(opened.FileOnDisk);
                if (opened.Contents.NamesHidden)
                {
                    DeleteCopy(opened);
                    _opened.Remove(opened);
                }
            }
        }
    }

    public void KeepPassword(ArchiveLocation location)
    {
        var fileOnDisk = location.FileOnDisk;
        if (location.Contents.Password is not { } password)
        {
            return;
        }

        lock (_opened)
        {
            _kept[fileOnDisk] = password;
        }
    }

    private string? TakeKept(string file)
    {
        lock (_opened)
        {
            return _kept.Remove(file, out var password) ? password : null;
        }
    }

    private void Report(string name, long done, long total) => Progress?.Invoke(new ArchiveProgress(name, done, total));

    public void Forget()
    {
        lock (_opened)
        {
            foreach (var opened in _opened)
            {
                DeleteCopy(opened);
            }

            _opened.Clear();
        }
    }

    private static string? FileAlong(string path)
    {
        for (var p = path; p.Length > 0; p = Path.GetDirectoryName(p) ?? "")
        {
            if (File.Exists(p))
            {
                return p;
            }

            if (Directory.Exists(p))
            {
                return null;
            }
        }

        return null;
    }

    private ArchiveLocation Resolve(string path, CancellationToken cancellationToken)
    {
        _gate.Wait(cancellationToken);
        try
        {
            var opened = Fresh(Best(path));
            if (opened is null)
            {
                var file = FileAlong(path) ?? throw new DirectoryNotFoundException(path);
                var name = Path.GetFileName(file);
                var kept = TakeKept(file);
                var contents = ArchiveReader.Open(file, name, (n, wrong) => !wrong && kept is { } k ? k : AskPassword?.Invoke(n, wrong), cancellation: cancellationToken, progress: (done, total) => Report(name, done, total));
                contents.Password ??= kept;
                opened = Remember(new Opened(file, contents, file, null));
            }

            while (true)
            {
                var inner = ArchivePaths.Inside(path, opened.Display) ?? "";
                if (opened.Contents.IsFolder(inner))
                {
                    return new ArchiveLocation(opened.Display, inner, opened.Contents) { FileOnDisk = opened.FileOnDisk };
                }

                var item = FileOnTheWay(opened.Contents, inner) ?? throw new DirectoryNotFoundException(path);
                var display = opened.Display + "\\" + item.Path;
                opened = Best(display) is { } known && known.Display.Equals(display, StringComparison.OrdinalIgnoreCase)
                    ? known
                    : OpenInside(opened, item, display, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ArchiveItem? FileOnTheWay(ArchiveContents contents, string inner)
    {
        for (var p = inner; p.Length > 0; p = p.LastIndexOf('\\') is var cut and >= 0 ? p[..cut] : "")
        {
            if (contents.Find(p) is { IsDirectory: false } item)
            {
                return item;
            }
        }

        return null;
    }

    private Opened OpenInside(Opened outer, ArchiveItem item, string display, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(_tempFolder(), "Archives", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, ArchivePaths.SafeFileName(item.Name));
        var location = new ArchiveLocation(outer.Display, "", outer.Contents) { FileOnDisk = outer.FileOnDisk };
        try
        {
            Unpack(location, item, copy, done => Report(item.Name, done, item.Size), cancellationToken);
            var contents = ArchiveReader.Open(copy, item.Name, AskPassword, cancellation: cancellationToken, progress: (done, total) => Report(item.Name, done, total));
            return Remember(new Opened(display, contents, outer.FileOnDisk, copy));
        }
        catch
        {
            TryDelete(folder);
            throw;
        }
    }

    private void Unpack(ArchiveLocation location, ArchiveItem item, string target, Action<long>? progress, CancellationToken cancellationToken)
    {
        var name = GetName(location.ArchivePath);
        IReadOnlyDictionary<int, ExtractResult> results;
        using (var stream = new FileStream(target, FileMode.Create, System.IO.FileAccess.Write, FileShare.None, 1 << 16))
        {
            try
            {
                results = ArchiveReader.Extract(location.Contents, [item.Index], _ => stream, AskPassword, name, progress, cancellationToken);
            }
            catch
            {
                stream.Dispose();
                File.Delete(target);
                throw;
            }
        }

        var result = results.GetValueOrDefault(item.Index, ExtractResult.Damaged);
        if (result != ExtractResult.Ok)
        {
            File.Delete(target);
            throw new ArchiveException(result switch
            {
                ExtractResult.WrongPassword => ArchiveProblem.WrongPassword,
                ExtractResult.UnsupportedMethod => ArchiveProblem.UnsupportedMethod,
                _ => ArchiveProblem.Damaged,
            }, item.Name);
        }

        if (item.ModifiedUtc is { } time)
        {
            File.SetLastWriteTimeUtc(target, time);
        }
    }

    private Opened? Best(string path)
    {
        lock (_opened)
        {
            return _opened.Where(o => ArchivePaths.Inside(path, o.Display) is not null).MaxBy(o => o.Display.Length);
        }
    }

    private Opened? Fresh(Opened? opened)
    {
        if (opened is null)
        {
            return null;
        }

        var info = new FileInfo(opened.FileOnDisk);
        var top = Top(opened);
        if (info.Exists && info.Length == top.Contents.FileLength && info.LastWriteTimeUtc == top.Contents.FileWriteUtc)
        {
            Touch(opened);
            return opened;
        }

        lock (_opened)
        {
            foreach (var stale in _opened.Where(o => o.FileOnDisk.Equals(opened.FileOnDisk, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                DeleteCopy(stale);
                _opened.Remove(stale);
            }
        }

        return null;
    }

    private Opened Top(Opened opened)
    {
        lock (_opened)
        {
            return _opened.FirstOrDefault(o => o.TempCopy is null && o.Display.Equals(opened.FileOnDisk, StringComparison.OrdinalIgnoreCase)) ?? opened;
        }
    }

    private void Touch(Opened opened)
    {
        lock (_opened)
        {
            if (_opened.Remove(opened))
            {
                _opened.Add(opened);
            }
        }
    }

    private Opened Remember(Opened opened)
    {
        lock (_opened)
        {
            _opened.RemoveAll(o => o.Display.Equals(opened.Display, StringComparison.OrdinalIgnoreCase));
            _opened.Add(opened);
            while (_opened.Count > Kept)
            {
                DeleteCopy(_opened[0]);
                _opened.RemoveAt(0);
            }
        }

        return opened;
    }

    private static void DeleteCopy(Opened opened)
    {
        if (opened.TempCopy is { } copy && Path.GetDirectoryName(copy) is { } folder)
        {
            TryDelete(folder);
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
