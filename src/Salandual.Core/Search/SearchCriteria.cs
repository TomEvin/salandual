// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Search/SearchCriteria.cs

namespace Salandual.Core.Search;

public enum TextKind
{
    Text,
    Regex,

    Hex,
}

public sealed record SearchText(string Text, TextKind Kind = TextKind.Text, bool Contains = true);

[Flags]
public enum SearchEncodings
{
    None = 0,

    Auto = 1,
    Ansi = 2,
    Oem = 4,
    Utf8 = 8,
    Utf16LE = 16,
    Utf16BE = 32,
}

public enum AttributeRule
{
    Either,
    Must,
    MustNot,
}

public sealed record SearchCriteria
{
    public string Named { get; init; } = "*.*";

    public IReadOnlyList<string> LookIn { get; init; } = [];

    public int? Depth { get; init; }

    public bool InsideZip { get; init; }

    public IReadOnlyList<SearchText> Texts { get; init; } = [];

    public bool AllTexts { get; init; } = true;

    public bool MatchCase { get; init; }

    public bool WholeWords { get; init; }

    public SearchEncodings Encodings { get; init; } = SearchEncodings.Auto;

    public long? MinSize { get; init; }

    public long? MaxSize { get; init; }

    public DateTime? ModifiedFrom { get; init; }

    public DateTime? ModifiedTo { get; init; }

    public IReadOnlyDictionary<FileAttributes, AttributeRule> Attributes { get; init; } = new Dictionary<FileAttributes, AttributeRule>();

    public IReadOnlyList<string> SkippedFolders { get; init; } = [];

    public bool HasTexts => Texts.Any(t => t.Text.Length > 0);
}

public sealed record SearchHit(string Folder, string Name, long Size, long LastWriteUtcTicks, FileAttributes Attributes, string? Archive = null, MatchLine? Line = null)
{
    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;

    public string FullPath => Folder.Length == 0 ? Name : Folder.EndsWith('\\') ? Folder + Name : Folder + "\\" + Name;

    public DateTime LastWriteUtc => new(LastWriteUtcTicks, DateTimeKind.Utc);
}

public sealed record MatchLine(long LineNumber, string Text, int Start, int Length, int MoreLines, bool AtLeast = false, long? Offset = null);

public readonly record struct SearchProgress(int Folders, int Files, string CurrentFolder);

public enum SearchLogKind
{
    Error,

    Skipped,

    NotRead,
}

public sealed record SearchLogEntry(SearchLogKind Kind, string Path, string Message);
