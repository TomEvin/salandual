// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Search/SearchResults.cs

using Salandual.Core.Resources;

namespace Salandual.Core.Search;

public enum ResultColumn
{
    Name,
    Folder,
    Size,
    Modified,
}

public enum ResultMode
{
    New,

    Add,

    Keep,

    Remove,
}

public static class SearchResults
{
    public static IComparer<SearchHit> Sorter(ResultColumn column, bool descending) => Comparer<SearchHit>.Create((a, b) =>
    {
        if (a.IsDirectory != b.IsDirectory)
        {
            return a.IsDirectory ? -1 : 1;
        }

        var result = column switch
        {
            ResultColumn.Folder => Compare(a.Folder, b.Folder),
            ResultColumn.Size => a.Size.CompareTo(b.Size),
            ResultColumn.Modified => a.LastWriteUtcTicks.CompareTo(b.LastWriteUtcTicks),
            _ => 0,
        };
        if (result == 0)
        {
            result = Compare(a.Name, b.Name);
        }

        if (result == 0)
        {
            result = Compare(a.Folder, b.Folder);
        }

        return descending ? -result : result;
    });

    private static int Compare(string a, string b) => StringComparer.CurrentCultureIgnoreCase.Compare(a, b);

    public static List<SearchHit> Combine(IReadOnlyList<SearchHit> before, IReadOnlyList<SearchHit> found, ResultMode mode)
    {
        var paths = new HashSet<string>(found.Select(h => h.FullPath), StringComparer.OrdinalIgnoreCase);
        switch (mode)
        {
            case ResultMode.Add:
                var seen = new HashSet<string>(before.Select(h => h.FullPath), StringComparer.OrdinalIgnoreCase);
                return [.. before, .. found.Where(h => seen.Add(h.FullPath))];
            case ResultMode.Keep:
                var kept = found.ToDictionary(h => h.FullPath, StringComparer.OrdinalIgnoreCase);
                return [.. before.Where(h => paths.Contains(h.FullPath)).Select(h => kept[h.FullPath])];
            case ResultMode.Remove:
                return [.. before.Where(h => !paths.Contains(h.FullPath))];
            default:
                return [.. found];
        }
    }

    public static string CommonFolder(IEnumerable<string> folders)
    {
        string[]? common = null;
        foreach (var folder in folders)
        {
            var parts = folder.TrimEnd('\\').Split('\\');
            if (common is null)
            {
                common = parts;
                continue;
            }

            var same = 0;
            while (same < common.Length && same < parts.Length && string.Equals(common[same], parts[same], StringComparison.OrdinalIgnoreCase))
            {
                same++;
            }

            common = common[..same];
        }

        if (common is null || common.Length == 0)
        {
            return "";
        }

        var path = string.Join('\\', common);
        return common.Length == 1 && path.EndsWith(':') ? path + "\\" : path;
    }
}

public sealed record RegexPiece(string Pattern, string Description, bool Ready);

public static class RegexPieces
{
    public static IReadOnlyList<RegexPiece> All =>
    [
        new(".", Strings.Regex_AnyCharacter, false),
        new("^", Strings.Regex_LineStart, false),
        new("$", Strings.Regex_LineEnd, false),
        new("[abc]", Strings.Regex_OneOf, false),
        new("[^abc]", Strings.Regex_NoneOf, false),
        new("[0-9]", Strings.Regex_Range, false),
        new("*", Strings.Regex_ZeroOrMore, false),
        new("+", Strings.Regex_OneOrMore, false),
        new("?", Strings.Regex_Optional, false),
        new("|", Strings.Regex_Or, false),
        new("()", Strings.Regex_Group, false),
        new(@"\", Strings.Regex_Literal, false),
        new(@"\b\w+\b", Strings.Regex_Word, true),
        new(@"\d+", Strings.Regex_Number, true),
        new(@"[A-Z]{2,4}-\d+", Strings.Regex_Id, true),
        new(@"[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", Strings.Regex_Guid, true),
        new(@"\d{4}-\d{2}-\d{2}", Strings.Regex_Date, true),
        new(@"[\w.+-]+@[\w-]+\.[\w.]+", Strings.Regex_Email, true),
        new(@"\b\d{1,3}(\.\d{1,3}){3}\b", Strings.Regex_Ip, true),
        new(@"https?://\S+", Strings.Regex_Link, true),
        new(@"^\s*$", Strings.Regex_EmptyLine, true),
        new(@"\bTODO\b.*", Strings.Regex_Todo, true),
    ];

    public static IReadOnlyList<RegexPiece> Replacements =>
    [
        new("$0", Strings.Regex_WholeMatch, false),
        new("$1", Loc.Format(Strings.Regex_GroupNumber, 1), false),
        new("$2", Loc.Format(Strings.Regex_GroupNumber, 2), false),
        new("$3", Loc.Format(Strings.Regex_GroupNumber, 3), false),
        new("${name}", Strings.Regex_NamedGroup, false),
        new("$$", Strings.Regex_Dollar, false),
    ];

    public static (string Text, int Caret) Insert(string text, int selectionStart, int selectionEnd, RegexPiece piece)
    {
        selectionStart = Math.Clamp(selectionStart, 0, text.Length);
        selectionEnd = Math.Clamp(selectionEnd, selectionStart, text.Length);
        var selected = text[selectionStart..selectionEnd];
        var put = piece.Pattern == "()" ? "(" + selected + ")" : piece.Pattern;
        var caret = selectionStart + (piece.Pattern == "()" && selected.Length == 0 ? 1 : put.Length);
        return (text[..selectionStart] + put + text[selectionEnd..], caret);
    }
}
