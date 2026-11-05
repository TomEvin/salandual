// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Platform.Windows/FileSystem/PathInput.cs

using Salandual.Core.Settings;

namespace Salandual.Platform.Windows.FileSystem;

public static class PathInput
{
    public static bool IsServerAddress(string text)
        => text.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase);

    private static bool HasPassword(string text)
    {
        var rest = text[(text.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var authority = rest.IndexOf('/') is var slash and >= 0 ? rest[..slash] : rest;
        var at = authority.LastIndexOf('@');
        return at > 0 && authority[..at].Contains(':');
    }

    private static string OnServer(string basePath, string typed)
    {
        var start = basePath.IndexOf('/', basePath.IndexOf("://", StringComparison.Ordinal) + 3);
        var root = start < 0 ? basePath : basePath[..start];
        var folder = start < 0 ? "/" : basePath[start..];
        typed = typed.Replace('\\', '/');
        var parts = (typed.StartsWith('/') ? typed : folder.TrimEnd('/') + "/" + typed).Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        var kept = new List<string>();
        foreach (var part in parts)
        {
            if (part == "..")
            {
                if (kept.Count > 0)
                {
                    kept.RemoveAt(kept.Count - 1);
                }
            }
            else if (part != ".")
            {
                kept.Add(part);
            }
        }

        return root + "/" + string.Join('/', kept);
    }

    public static string? Resolve(string text, string basePath)
    {
        var t = text.Trim().Trim('"').Trim();
        if (t.Length == 0)
        {
            return null;
        }

        if (IsServerAddress(t))
        {
            return HasPassword(t) ? null : t;
        }

        if (IsServerAddress(basePath) && !(t.Length >= 2 && t[1] == ':') && !t.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return OnServer(basePath, t);
        }

        t = HotPathSettings.Expand(t).Replace('/', '\\');
        if (t == "~" || t.StartsWith(@"~\", StringComparison.Ordinal))
        {
            t = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + t[1..];
        }

        string root;
        string rest;
        if (t.Length >= 2 && char.IsAsciiLetter(t[0]) && t[1] == ':')
        {
            root = char.ToUpperInvariant(t[0]) + @":\";
            rest = t[2..];
        }
        else if (t.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = t[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return parts.Length == 0 ? null : @"\\" + parts[0];
            }

            root = $@"\\{parts[0]}\{parts[1]}";
            rest = string.Join('\\', parts.Skip(2));
        }
        else if (WindowsPath.GetRoot(basePath) is { } baseRoot)
        {
            root = baseRoot;
            rest = t.StartsWith('\\') ? t : basePath[Math.Min(baseRoot.Length, basePath.Length)..] + @"\" + t;
        }
        else
        {
            return null;
        }

        var names = new List<string>();
        foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (names.Count > 0)
                {
                    names.RemoveAt(names.Count - 1);
                }

                continue;
            }

            names.Add(part);
        }

        return names.Count == 0 ? root : WindowsPath.Join(root, string.Join('\\', names));
    }

    public static (string Folder, string Prefix)? ForCompletion(string text, string basePath)
    {
        if (Resolve(text, basePath) is not { } full)
        {
            return null;
        }

        var typed = text.Trim().Trim('"').Replace('/', '\\');
        var last = typed[(typed.LastIndexOf('\\') + 1)..];
        if (typed.EndsWith('\\') || last is "." or ".." || (typed.Length == 2 && typed[1] == ':') || WindowsPath.GetParent(full) is not { } parent)
        {
            return (full, "");
        }

        return (parent, WindowsPath.GetName(full));
    }

    public static IReadOnlyList<string> Complete(string folder, string prefix, bool showHidden, int max)
    {
        var names = new List<string>();
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
                MatchType = MatchType.Simple,
                AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
                RecurseSubdirectories = false,
            };
            var pattern = prefix.AsSpan().IndexOfAny('*', '?') >= 0 ? "*" : prefix + "*";
            foreach (var dir in Directory.EnumerateDirectories(WindowsPath.ToExtended(folder), pattern, options))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }

                if (names.Count >= 5000)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }

        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        return [.. names.Take(max).Select(n => WindowsPath.Join(folder, n))];
    }

    public enum Kind { Missing, Folder, File }

    public static (Kind Kind, string Path) Probe(string path)
    {
        try
        {
            var extended = WindowsPath.ToExtended(path);
            var kind = Directory.Exists(extended) ? Kind.Folder : File.Exists(extended) ? Kind.File : Kind.Missing;
            return (kind, kind == Kind.Missing ? path : ActualCase(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (Kind.Missing, path);
        }
    }

    private static string ActualCase(string path)
    {
        if (WindowsPath.GetRoot(path) is not { } root)
        {
            return path;
        }

        var current = root;
        foreach (var part in path[Math.Min(root.Length, path.Length)..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            string? found = null;
            try
            {
                var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, MatchType = MatchType.Simple, IgnoreInaccessible = true, AttributesToSkip = 0 };
                found = Directory.EnumerateFileSystemEntries(WindowsPath.ToExtended(current), part, options).Select(Path.GetFileName).FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }

            current = WindowsPath.Join(current, found ?? part);
        }

        return current;
    }
}
