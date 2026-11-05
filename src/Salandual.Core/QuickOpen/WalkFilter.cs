// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/QuickOpen/WalkFilter.cs

namespace Salandual.Core.QuickOpen;

public sealed class WalkFilter(bool skipHidden, bool skipSystem, bool skipWindowsFolders, IReadOnlyList<string> skipFolders)
{
    public bool SkipHidden { get; } = skipHidden;

    public bool SkipSystem { get; } = skipSystem;

    public bool SkipWindowsFolders { get; } = skipWindowsFolders;

    public IReadOnlyList<string> SkipFolders { get; } = skipFolders;

    private readonly HashSet<string> _names = new(skipFolders.Where(IsName), StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _paths = [.. skipFolders.Where(IsFullPath).Concat(skipWindowsFolders ? WindowsFolders : [])];

    public static IReadOnlyList<string> WindowsFolders { get; } = [.. new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    }.Where(p => p.Length > 0).Select(p => Path.TrimEndingDirectorySeparator(p)).Distinct(StringComparer.OrdinalIgnoreCase)];

    public static WalkFilter None { get; } = new(false, false, false, []);

    public FileAttributes Attributes => FileAttributes.ReparsePoint
        | (SkipHidden ? FileAttributes.Hidden : 0)
        | (SkipSystem ? FileAttributes.System : 0);

    public bool SkipsFolder(string full, string name)
        => _names.Contains(name) || _paths.Exists(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));

    public static string? Clean(string? line)
    {
        var text = (line ?? "").Trim().Trim('"').Trim();
        if (text.Length == 0 || text.Length > 260 || text.IndexOfAny(['*', '?', '<', '>', '|', '"']) >= 0)
        {
            return null;
        }

        if (IsName(text))
        {
            return text is "." or ".." ? null : text;
        }

        var trimmed = text.Length > 3 ? text.TrimEnd('\\') : text;
        return IsFullPath(trimmed) ? trimmed : null;
    }

    public static List<string> CleanList(IEnumerable<string>? lines)
        => [.. (lines ?? []).Select(Clean).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(200)];

    private static bool IsName(string text) => text.IndexOfAny(['\\', '/', ':']) < 0;

    private static bool IsFullPath(string text)
        => text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] == '\\' && text.IndexOf('/') < 0 && text.IndexOf(':', 2) < 0;
}
