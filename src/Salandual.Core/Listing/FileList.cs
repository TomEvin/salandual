// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Listing/FileList.cs

using System.Text;
using Salandual.Core.Resources;
using Salandual.Core.Vfs;

namespace Salandual.Core.Listing;

public sealed record FileListItem(string FullName, long Size, DateTime LastWriteUtc, FileAttributes Attributes, DateTime? CreatedUtc = null)
{
    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;

    public static FileListItem From(string folder, FileEntry entry) => new(folder.EndsWith('\\') ? folder + entry.Name : folder + "\\" + entry.Name, entry.Size, entry.LastWriteUtc, entry.Attributes);

    public string Name => FullName.LastIndexOf('\\') is var cut and >= 0 ? FullName[(cut + 1)..] : FullName;
}

public sealed record FileListContext(DisplayFormat Format, string From = "", Func<string, string>? ShortPath = null)
{
    public static FileListContext Default { get; } = new(DisplayFormat.Default);
}

public sealed class FileListLine
{
    public static readonly IReadOnlyList<IReadOnlyList<string>> Groups =
    [
        ["FileName", "FileNamePart", "FileExtension", "FileDOSName", "RelativeName"],
        ["FullName", "FullPath", "FolderName", "Drive", "Path", "DOSPath"],
        ["FileSize", "FileSizeShort"],
        ["FileDate", "FileTime", "FileDateTime", "Created"],
        ["FileAttributes", "Type", "Number", "Count"],
        ["CRLF", "LF", "CR", "TAB"],
    ];

    public static readonly IReadOnlyList<string> Variables = [.. Groups.SelectMany(g => g)];

    private static readonly HashSet<string> _noFile = new(StringComparer.OrdinalIgnoreCase) { "CRLF", "LF", "CR", "TAB", "Count" };

    private static readonly HashSet<string> _known = new(Variables, StringComparer.OrdinalIgnoreCase);

    private readonly List<Part> _parts;

    private FileListLine(List<Part> parts) => _parts = parts;

    public bool NamesAFile => _parts.Any(p => p.Variable is { } v && !_noFile.Contains(v));

    public bool EndsWithBreak => _parts.Count > 0 && _parts[^1] is { Variable: { } v } && (v.Equals("CRLF", StringComparison.OrdinalIgnoreCase) || v.Equals("LF", StringComparison.OrdinalIgnoreCase) || v.Equals("CR", StringComparison.OrdinalIgnoreCase));

    public static FileListLine? Parse(string text, out string? error, out int errorAt, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var parts = new List<Part>();
        var literal = new StringBuilder();
        error = null;
        errorAt = -1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '$')
            {
                literal.Append(c);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '$')
            {
                literal.Append('$');
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '[')
            {
                var close = text.IndexOf(']', i + 2);
                if (close < 0)
                {
                    (error, errorAt) = (Loc.Format(Strings.FileList_Unclosed, "$[", "]"), i);
                    return null;
                }

                var name = text[(i + 2)..close].Trim();
                if (environment(name) is not { } value)
                {
                    (error, errorAt) = (Loc.Format(Strings.FileList_NoEnvironment, name), i);
                    return null;
                }

                literal.Append(value);
                i = close;
                continue;
            }

            if (i + 1 >= text.Length || text[i + 1] != '(')
            {
                (error, errorAt) = (Strings.FileList_LoneDollar, i);
                return null;
            }

            var end = text.IndexOf(')', i + 2);
            if (end < 0)
            {
                (error, errorAt) = (Loc.Format(Strings.FileList_Unclosed, "$(", ")"), i);
                return null;
            }

            var inside = text[(i + 2)..end];
            var colon = inside.IndexOf(':');
            var variable = (colon < 0 ? inside : inside[..colon]).Trim();
            if (!_known.Contains(variable))
            {
                (error, errorAt) = (Loc.Format(Strings.FileList_UnknownVariable, "$(" + variable + ")"), i);
                return null;
            }

            int? width = null;
            var max = false;
            if (colon >= 0)
            {
                var w = inside[(colon + 1)..].Trim();
                if (w.Equals("max", StringComparison.OrdinalIgnoreCase))
                {
                    max = true;
                }
                else if (int.TryParse(w, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 9999)
                {
                    width = n;
                }
                else
                {
                    (error, errorAt) = (Strings.FileList_BadWidth, i + 2 + colon);
                    return null;
                }
            }

            if (literal.Length > 0)
            {
                parts.Add(new Part(literal.ToString(), null, null, false));
                literal.Clear();
            }

            parts.Add(new Part(null, Variables.First(v =>v.Equals(variable, StringComparison.OrdinalIgnoreCase)), width, max));
            i = end;
        }

        if (literal.Length > 0)
        {
            parts.Add(new Part(literal.ToString(), null, null, false));
        }

        return new FileListLine(parts);
    }

    public string Expand(IReadOnlyList<FileListItem> items, FileListContext context, IReadOnlyList<FileListItem>? widthsOf = null)
    {
        widthsOf ??= items;
        var widths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var part in _parts.Where(p => p.Max))
        {
            widths[part.Variable!] = widthsOf.Count == 0 ? 0 : widthsOf.Select((item, i) => Value(part.Variable!, item, i + 1, widthsOf.Count, context).Length).Max();
        }

        var text = new StringBuilder();
        for (var i = 0; i < items.Count; i++)
        {
            Write(text, items[i], i + 1, widthsOf.Count, context, widths);
        }

        return text.ToString();
    }

    public string ExpandOnce(int count = 0)
    {
        var text = new StringBuilder();
        Write(text, null, 0, count, FileListContext.Default, []);
        return text.ToString();
    }

    private void Write(StringBuilder text, FileListItem? item, int number, int count, FileListContext context, Dictionary<string, int> widths)
    {
        foreach (var part in _parts)
        {
            if (part.Literal is { } literal)
            {
                text.Append(literal);
                continue;
            }

            var value = item is null && !_noFile.Contains(part.Variable!) ? "" : Value(part.Variable!, item, number, count, context);
            var width = part.Max ? widths[part.Variable!] : part.Width;
            if (width is { } w)
            {
                value = value.Length > w ? value[..w] : value.PadRight(w);
            }

            text.Append(value);
        }
    }

    private static string Value(string variable, FileListItem? item, int number, int count, FileListContext context)
    {
        switch (variable)
        {
            case "CRLF":
                return "\r\n";
            case "LF":
                return "\n";
            case "CR":
                return "\r";
            case "TAB":
                return "\t";
            case "Count":
                return count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var i = item!;
        var format = context.Format;
        var cut = i.FullName.LastIndexOf('\\');
        var name = cut < 0 ? i.FullName : i.FullName[(cut + 1)..];
        var folder = cut < 0 ? "" : cut == 2 && i.FullName[1] == ':' ? i.FullName[..3] : i.FullName[..cut];
        var dot = i.IsDirectory ? -1 : name.LastIndexOf('.');
        var local = i.LastWriteUtc.ToLocalTime();
        return variable switch
        {
            "FileName" => name,
            "FileNamePart" => dot > 0 ? name[..dot] : name,
            "FileExtension" => dot > 0 ? name[(dot + 1)..] : "",
            "FileDOSName" => context.ShortPath is { } shortName && shortName(i.FullName) is var s && s.LastIndexOf('\\') is var at and >= 0 ? s[(at + 1)..] : name,
            "RelativeName" => Relative(i.FullName, context.From),
            "FileSize" => i.IsDirectory ? Strings.FileList_FolderSize : i.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "FileSizeShort" => i.IsDirectory ? Strings.FileList_FolderSize : format.Size(i.Size),
            "FileDate" => format.Date(local),
            "FileTime" => format.Time(local),
            "FileDateTime" => local.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            "Created" => Created(i) is { } made ? format.Date(made) + " " + format.Time(made) : "",
            "FileAttributes" => DisplayFormat.Attributes(i.Attributes) is { Length: > 0 } a ? a : "-",
            "Type" => i.IsDirectory ? Strings.FileList_TypeFolder : Strings.FileList_TypeFile,
            "Number" => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "FullName" => i.FullName,
            "FullPath" => folder,
            "FolderName" => FolderName(folder),
            "Drive" => Drive(folder),
            "Path" => PathPart(folder),
            "DOSPath" => PathPart(context.ShortPath is { } shortPath && folder.Length > 0 ? shortPath(folder) : folder),
            _ => "",
        };
    }

    private static string Relative(string fullName, string from)
    {
        if (from.Length == 0)
        {
            return fullName;
        }

        var start = from.EndsWith('\\') ? from : from + "\\";
        return fullName.StartsWith(start, StringComparison.OrdinalIgnoreCase) ? fullName[start.Length..] : fullName;
    }

    private static string FolderName(string folder)
    {
        var trimmed = folder.TrimEnd('\\');
        var drive = Drive(folder);
        return trimmed.Length <= drive.Length ? drive : trimmed[(trimmed.LastIndexOf('\\') + 1)..];
    }

    private static DateTime? Created(FileListItem item)
    {
        if (item.CreatedUtc is { } known)
        {
            return known.ToLocalTime();
        }

        try
        {
            var info = item.IsDirectory ? (FileSystemInfo)new DirectoryInfo(item.FullName) : new FileInfo(item.FullName);
            return info.Exists ? info.CreationTimeUtc.ToLocalTime() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string Drive(string folder)
    {
        if (folder.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var server = folder.IndexOf('\\', 2);
            var share = server < 0 ? -1 : folder.IndexOf('\\', server + 1);
            return share < 0 ? folder : folder[..share];
        }

        return folder.Length >= 2 && folder[1] == ':' ? folder[..2] : "";
    }

    private static string PathPart(string folder)
    {
        var rest = folder[Drive(folder).Length..];
        if (!rest.StartsWith('\\'))
        {
            rest = "\\" + rest;
        }

        return rest.EndsWith('\\') ? rest : rest + "\\";
    }

    private sealed record Part(string? Literal, string? Variable, int? Width, bool Max);
}

public static class FileListScan
{
    public static List<FileListItem> Named(IReadOnlyList<FileListItem> items, FileMask? mask, bool foldersToo)
        => mask is null ? [.. items] : [.. items.Where(i => i.IsDirectory ? foldersToo : mask.IsMatch(i.Name))];

    public static List<FileListItem> Inside(IReadOnlyList<FileListItem> selected, bool showHidden, Action<int>? progress, CancellationToken cancel)
    {
        var all = new List<FileListItem>();
        foreach (var item in selected)
        {
            all.Add(item);
            if (item.IsDirectory && (item.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                Walk(item.FullName, all, showHidden, progress, cancel);
            }
        }

        return all;
    }

    private static void Walk(string folder, List<FileListItem> all, bool showHidden, Action<int>? progress, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        List<FileSystemInfo> entries;
        try
        {
            var skip = showHidden ? (FileAttributes)0 : FileAttributes.Hidden | FileAttributes.System;
            entries = [.. new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = skip, RecurseSubdirectories = false })];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return;
        }

        var files = entries.Where(e => (e.Attributes & FileAttributes.Directory) == 0).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
        var folders = entries.Where(e => (e.Attributes & FileAttributes.Directory) != 0).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var e in files.Concat(folders))
        {
            all.Add(new FileListItem(e.FullName, e is FileInfo f ? f.Length : 0, e.LastWriteTimeUtc, e.Attributes, e.CreationTimeUtc));
            if ((all.Count & 255) == 0)
            {
                progress?.Invoke(all.Count);
            }

            if (e is DirectoryInfo && (e.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                Walk(e.FullName, all, showHidden, progress, cancel);
            }
        }
    }
}

public sealed record FileListTemplate(string Name, string First, string Line)
{
    public static IReadOnlyList<FileListTemplate> BuiltIn { get; } =
    [
        new(Strings.FileList_Names, "", "$(FileName)$(CRLF)"),
        new(Strings.FileList_FullNames, "", "$(FullName)$(CRLF)"),
        new(Strings.FileList_NameSizeDate, "", "$(FileName:max)$(TAB)$(FileSize)$(TAB)$(FileDate)$(CRLF)"),
        new(Strings.FileList_Csv, Strings.FileList_CsvHeading, "\"$(FileName)\";$(FileSize);$(FileDate);\"$(FullPath)\"$(CRLF)"),
        new(Strings.FileList_Markdown, Strings.FileList_MarkdownHeading + "$(CRLF)|---|--:|---|", "| $(FileName) | $(FileSize) | $(FileDate) |$(CRLF)"),
    ];

    public bool SameLines(string first, string line) => First == first && Line == line;
}
