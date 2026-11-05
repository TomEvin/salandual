// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/QuickOpen/FileNameIndex.cs

using System.Text;

namespace Salandual.Core.QuickOpen;

public enum FileIndexKind { Everything, WindowsIndex }

public interface IFileNameIndex
{
    FileIndexKind Kind { get; }

    bool IsAvailable();

    IReadOnlyList<string> CoveredFolders();

    IReadOnlyList<WalkedEntry> Find(string typed, int max, CancellationToken cancel);
}

public sealed class FileNameSources(IFileNameIndex? everything, IFileNameIndex? windowsIndex, Func<IReadOnlyList<string>> localDrives)
{
    public static FileNameSources None { get; } = new(null, null, () => []);

    public IFileNameIndex? Everything { get; } = everything;

    public IFileNameIndex? WindowsIndex { get; } = windowsIndex;

    public IReadOnlyList<string> LocalDrives() => localDrives();
}

public static class FileNamePattern
{
    public static string Letters(string typed)
    {
        var letters = new StringBuilder();
        foreach (var c in typed)
        {
            if (!char.IsWhiteSpace(c) && !char.IsControl(c) && letters.Length < 64)
            {
                letters.Append(c);
            }
        }

        return letters.ToString();
    }

    public static string Wildcards(string typed)
    {
        var text = new StringBuilder("*");
        foreach (var c in Letters(typed))
        {
            if (c is not ('*' or '?' or '"' or '|' or '<' or '>' or '!'))
            {
                text.Append(c).Append('*');
            }
        }

        return text.Length == 1 ? "" : text.ToString();
    }

    public static string Like(string typed)
    {
        var text = new StringBuilder("'%");
        foreach (var c in Letters(typed))
        {
            _ = c switch
            {
                '%' or '_' or '[' => text.Append('[').Append(c).Append(']'),
                '\'' => text.Append("''"),
                _ => text.Append(c),
            };
            text.Append('%');
        }

        return text.Append('\'').ToString();
    }
}

public sealed class MemoryFileNameIndex(FileIndexKind kind, IEnumerable<string> paths, IEnumerable<string>? covered = null, bool available = true) : IFileNameIndex
{
    private readonly List<string> _paths = [.. paths];

    public FileIndexKind Kind => kind;

    public bool Available { get; set; } = available;

    public int Asked { get; private set; }

    public bool IsAvailable() => Available;

    public IReadOnlyList<string> CoveredFolders() => [.. covered ?? []];

    public IReadOnlyList<WalkedEntry> Find(string typed, int max, CancellationToken cancel)
    {
        Asked++;
        return [.. _paths
            .Select(p => (Path: p.TrimEnd('\\'), IsFolder: p.EndsWith('\\')))
            .Where(p => QuickMatch.Match(FileNamePattern.Letters(typed), Path.GetFileName(p.Path)) is not null)
            .Take(max)
            .Select(p => new WalkedEntry(Path.GetFileName(p.Path), Path.GetDirectoryName(p.Path) ?? "", p.IsFolder))];
    }
}
