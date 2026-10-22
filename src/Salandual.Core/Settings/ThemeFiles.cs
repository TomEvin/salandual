// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ThemeFiles.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Settings;

public static class ThemeFiles
{
    public const long MaxImageBytes = 64L * 1024 * 1024;

    public static readonly IReadOnlyList<string> ImagePatterns = ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif", "*.bmp"];

    public static string? Folder(string dataDirectory, string themeId)
        => IsSafeId(themeId) ? Path.Combine(dataDirectory, "themes", themeId) : null;

    public static string? ImagePath(string dataDirectory, ColorTheme theme)
        => theme.Background.Path is { } name && Folder(dataDirectory, theme.Id) is { } folder ? Path.Combine(folder, name) : null;

    public static string? SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var file = Path.GetFileName(name.Trim());
        return file.Length == 0 || file is "." or ".." || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? null : file;
    }

    public static bool IsSafeId(string id)
        => id.Length is > 0 and <= 64 && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    public static string CopyImage(string dataDirectory, string themeId, string source)
    {
        var folder = Folder(dataDirectory, themeId) ?? throw new IOException(Strings.ThemeFiles_BadTheme);
        var info = new FileInfo(source);
        if (!info.Exists)
        {
            throw new FileNotFoundException(Strings.ThemeFiles_NotFound, source);
        }

        if (info.Length > MaxImageBytes)
        {
            throw new IOException(Loc.Format(Strings.ThemeFiles_TooLarge, MaxImageBytes / (1024 * 1024)));
        }

        Directory.CreateDirectory(folder);
        var stem = Path.GetFileNameWithoutExtension(info.Name);
        var extension = info.Extension.ToLowerInvariant();
        var name = SafeFileName(stem + extension) ?? "background" + extension;
        for (var n = 2; File.Exists(Path.Combine(folder, name)); n++)
        {
            name = $"{stem} ({n}){extension}";
        }

        File.Copy(source, Path.Combine(folder, name));
        return name;
    }

    public static void DeleteImagesExcept(string dataDirectory, string themeId, string? keep)
    {
        if (Folder(dataDirectory, themeId) is not { } folder || !Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (!string.Equals(Path.GetFileName(file), keep, StringComparison.OrdinalIgnoreCase) && IsImage(file))
            {
                TryDelete(file);
            }
        }
    }

    public static bool CopyImageTo(string dataDirectory, ColorTheme from, string toId)
    {
        if (ImagePath(dataDirectory, from) is not { } source || !File.Exists(source) || Folder(dataDirectory, toId) is not { } folder)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(folder);
            File.Copy(source, Path.Combine(folder, from.Background.Path!), overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void DeleteFolder(string dataDirectory, string themeId)
    {
        if (Folder(dataDirectory, themeId) is not { } folder || !Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            TryDelete(file);
        }

        try
        {
            Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal static bool IsImage(string file)
        => ImagePatterns.Any(p => file.EndsWith(p[1..], StringComparison.OrdinalIgnoreCase));

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
