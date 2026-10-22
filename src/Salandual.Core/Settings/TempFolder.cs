// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/TempFolder.cs

namespace Salandual.Core.Settings;

public sealed record TempLeftovers(IReadOnlyList<string> Entries, long Files, long Bytes)
{
    public static TempLeftovers None { get; } = new([], 0, 0);
}

public sealed class TempFolder(string root) : IDisposable
{
    public const string LockName = ".running";

    public const string ClosedName = ".closed";

    private static readonly TimeSpan _starting = TimeSpan.FromMinutes(2);

    private readonly Lock _gate = new();
    private FileStream? _lock;
    private string? _own;

    public string Root { get; } = root;

    public string Own
    {
        get
        {
            lock (_gate)
            {
                if (_own is null)
                {
                    var own = Path.Combine(Root, "run-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
                    Directory.CreateDirectory(own);
                    _lock = new FileStream(Path.Combine(own, LockName), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                    _own = own;
                }

                return _own;
            }
        }
    }

    public string NewFile(string prefix, string extension) => Path.Combine(Own, prefix + "-" + Guid.NewGuid().ToString("N") + extension);

    public string NewFolder(string prefix)
    {
        var folder = Path.Combine(Own, prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public void DeleteClosed()
    {
        foreach (var folder in Others())
        {
            if (File.Exists(Path.Combine(folder, ClosedName)) && !IsRunning(folder))
            {
                TryDelete(folder);
            }
        }
    }

    public TempLeftovers FindLeftovers()
    {
        if (!Directory.Exists(Root))
        {
            return TempLeftovers.None;
        }

        var entries = new List<string>();
        long files = 0, bytes = 0;
        IEnumerable<FileSystemInfo> all;
        try
        {
            all = [.. new DirectoryInfo(Root).EnumerateFileSystemInfos("*", Options)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return TempLeftovers.None;
        }

        foreach (var entry in all)
        {
            if (entry is FileInfo file)
            {
                (files, bytes) = (files + 1, bytes + file.Length);
                entries.Add(file.FullName);
            }
            else if (!PathsEqual(entry.FullName, _own) && (entry.Attributes & FileAttributes.ReparsePoint) == 0 && !IsRunning(entry.FullName)
                && !(DateTime.UtcNow - entry.CreationTimeUtc < _starting && !File.Exists(Path.Combine(entry.FullName, LockName))))
            {
                var (f, b) = Count(entry.FullName);
                (files, bytes) = (files + f, bytes + b);
                entries.Add(entry.FullName);
            }
            else if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                entries.Add(entry.FullName);
            }
        }

        return new TempLeftovers(entries, files, bytes);
    }

    public long Delete(TempLeftovers leftovers)
    {
        long stay = 0;
        foreach (var entry in leftovers.Entries)
        {
            if (!IsInside(entry) || (Directory.Exists(entry) && IsRunning(entry)))
            {
                continue;
            }

            if (!TryDelete(entry))
            {
                stay += File.Exists(entry) ? 1 : Count(entry).Files;
            }
        }

        return stay;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_own is not { } own)
            {
                return;
            }

            for (var attempt = 0; attempt < 5; attempt++)
            {
                foreach (var entry in Children(own))
                {
                    if (!entry.Name.Equals(LockName, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(entry.FullName);
                    }
                }

                if (Children(own).All(e => e.Name.Equals(LockName, StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }

                Thread.Sleep(100);
            }

            var empty = Children(own).All(e => e.Name.Equals(LockName, StringComparison.OrdinalIgnoreCase));
            if (!empty)
            {
                try
                {
                    File.WriteAllText(Path.Combine(own, ClosedName), "");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }

            _lock?.Dispose();
            _lock = null;
            if (empty)
            {
                TryDelete(own);
            }

            _own = null;
        }
    }

    private static EnumerationOptions Options => new() { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };

    private IEnumerable<string> Others()
    {
        try
        {
            return [.. Directory.EnumerateDirectories(Root, "*", Options).Where(d => !PathsEqual(d, _own))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static List<FileSystemInfo> Children(string folder)
    {
        try
        {
            return [.. new DirectoryInfo(folder).EnumerateFileSystemInfos("*", Options)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsRunning(string folder)
    {
        var path = Path.Combine(folder, LockName);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private bool IsInside(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root)) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.Length > root.Length;
    }

    private static bool PathsEqual(string a, string? b) => b is not null && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private static (long Files, long Bytes) Count(string folder)
    {
        long files = 0, bytes = 0;
        var pending = new Stack<string>([folder]);
        while (pending.Count > 0)
        {
            foreach (var entry in Children(pending.Pop()))
            {
                if (entry is FileInfo f)
                {
                    if (!f.Name.Equals(LockName, StringComparison.OrdinalIgnoreCase) && !f.Name.Equals(ClosedName, StringComparison.OrdinalIgnoreCase))
                    {
                        (files, bytes) = (files + 1, bytes + f.Length);
                    }
                }
                else if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(entry.FullName);
                }
            }
        }

        return (files, bytes);
    }

    private static bool TryDelete(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }

                File.Delete(path);
                return true;
            }

            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                var ok = true;
                foreach (var entry in Children(path))
                {
                    ok &= TryDelete(entry.FullName);
                }

                if (!ok)
                {
                    return false;
                }
            }

            Directory.Delete(path, recursive: false);
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
