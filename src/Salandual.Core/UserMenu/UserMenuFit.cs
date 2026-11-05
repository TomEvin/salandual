// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/UserMenu/UserMenuFit.cs

using Salandual.Core.Listing;
using Salandual.Core.Resources;

namespace Salandual.Core.UserMenu;

public static class UserMenuFit
{
    private static readonly Dictionary<UserMenuFileKind, HashSet<string>> _kinds = new()
    {
        [UserMenuFileKind.Images] = Set(".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".webp", ".tif", ".tiff", ".heic", ".heif", ".avif", ".jxl", ".svg", ".ico", ".icns", ".psd", ".tga", ".dds",
            ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".raf", ".pef", ".srw"),
        [UserMenuFileKind.Audio] = Set(".mp3", ".wav", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".ape", ".mka", ".mid", ".midi"),
        [UserMenuFileKind.Video] = Set(".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".mpg", ".mpeg", ".m2ts", ".mts", ".ts", ".vob", ".flv", ".3gp", ".ogv"),
        [UserMenuFileKind.Text] = Set(".txt", ".md", ".markdown", ".log", ".csv", ".tsv", ".json", ".jsonc", ".yaml", ".yml", ".xml", ".html", ".htm", ".css", ".js", ".ts", ".ini", ".cfg", ".conf", ".toml",
            ".cs", ".c", ".h", ".cpp", ".hpp", ".py", ".ps1", ".sh", ".bat", ".cmd", ".sql", ".rtf"),
        [UserMenuFileKind.Archives] = Set(".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".zst", ".iso", ".cab", ".wim", ".dmg", ".001"),
        [UserMenuFileKind.Programs] = Set(".exe", ".com", ".msi", ".bat", ".cmd", ".ps1", ".appx", ".msix"),
    };

    private static HashSet<string> Set(params string[] extensions) => new(extensions, StringComparer.OrdinalIgnoreCase);

    public static string? WhyNot(UserMenuItem item, IReadOnlyList<UserMenuEntry> selected)
    {
        if (!item.IsRunnable)
        {
            return null;
        }

        var count = selected.Count;
        var countFits = item.Count switch
        {
            UserMenuCount.One => count == 1,
            UserMenuCount.Two => count == 2,
            UserMenuCount.AtLeastTwo => count >= 2,
            _ => true,
        };
        if (!countFits)
        {
            return item.Count switch
            {
                UserMenuCount.One => Strings.UserMenu_ForOne,
                UserMenuCount.Two => Strings.UserMenu_ForTwo,
                _ => Strings.UserMenu_ForAtLeastTwo,
            };
        }

        var conditions = item.ShowsFor != UserMenuShowsFor.Both || item.Named != "*" || item.FileKind != UserMenuFileKind.Any;
        if (count == 0)
        {
            return conditions ? Strings.UserMenu_ForItems : null;
        }

        if (item.ShowsFor == UserMenuShowsFor.Files && selected.Any(e => e.IsDirectory))
        {
            return Strings.UserMenu_ForFiles;
        }

        if (item.ShowsFor == UserMenuShowsFor.Folders && selected.Any(e => !e.IsDirectory))
        {
            return Strings.UserMenu_ForFolders;
        }

        if (item.Named != "*" && FileMask.Parse(item.Named) is { } mask && selected.Any(e => !mask.IsMatch(e.Name)))
        {
            return Loc.Format(Strings.UserMenu_ForNamed, item.Named);
        }

        if (item.FileKind != UserMenuFileKind.Any && selected.Any(e => e.IsDirectory || !_kinds[item.FileKind].Contains(Path.GetExtension(e.Name))))
        {
            return Loc.Format(Strings.UserMenu_ForKind, KindName(item.FileKind));
        }

        return null;
    }

    public static string MaskOf(UserMenuFileKind kind) => _kinds.TryGetValue(kind, out var set) ? string.Join(";", set.Select(e => "*" + e)) : "*.*";

    public static string KindName(UserMenuFileKind kind) => kind switch
    {
        UserMenuFileKind.Images => Strings.UserMenu_Kind_Images,
        UserMenuFileKind.Audio => Strings.UserMenu_Kind_Audio,
        UserMenuFileKind.Video => Strings.UserMenu_Kind_Video,
        UserMenuFileKind.Text => Strings.UserMenu_Kind_Text,
        UserMenuFileKind.Archives => Strings.UserMenu_Kind_Archives,
        UserMenuFileKind.Programs => Strings.UserMenu_Kind_Programs,
        _ => Strings.UserMenu_Kind_Any,
    };
}
