// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/FindSettings.cs

using Salandual.Core.Search;

namespace Salandual.Core.Settings;

public sealed record FindSettings
{
    public static readonly IReadOnlyList<string> DefaultSkippedFolders = ["System Volume Information", "$Recycle.Bin"];

    public List<string> Named { get; set; } = [];

    public List<string> LookIn { get; set; } = [];

    public List<string> Texts { get; set; } = [];

    public SearchDefaults Defaults { get; set; } = new();

    public List<string> SkippedFolders { get; set; } = [.. DefaultSkippedFolders];

    public bool FocusResults { get; set; } = true;

    public bool ViewAtMatch { get; set; } = true;

    public bool ViewerKeepsText { get; set; } = true;

    public double WindowWidth { get; set; } = 960;

    public double WindowHeight { get; set; } = 680;

    public bool WindowMaximized { get; set; }

    public List<SavedSearch> Saved { get; set; } = [];

    public ResultsPanel ResultsPanel { get; set; }

    public DuplicateKeys DuplicateKeys { get; set; } = DuplicateKeys.Size | DuplicateKeys.Content;

    public const int MaxSaved = 50;

    internal FindSettings Normalize() => this with
    {
        Named = HistorySettings.Push(Named, "").Where(s => s.Length > 0).ToList(),
        LookIn = HistorySettings.Push(LookIn, "").Where(s => s.Length > 0).ToList(),
        Texts = HistorySettings.Push(Texts, "").Where(s => s.Length > 0).ToList(),
        Defaults = (Defaults ?? new()).Normalize(),
        Saved = (Saved ?? []).Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => s with { Name = s.Name.Trim(), Search = (s.Search ?? new()).Normalize() })
            .DistinctBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxSaved).ToList(),
        ResultsPanel = Enum.IsDefined(ResultsPanel) ? ResultsPanel : ResultsPanel.Active,
        DuplicateKeys = (DuplicateKeys & DuplicateKeys.All) == DuplicateKeys.None ? DuplicateKeys.Size | DuplicateKeys.Content : DuplicateKeys & DuplicateKeys.All,
        SkippedFolders = (SkippedFolders ?? [.. DefaultSkippedFolders]).Select(s => s?.Trim() ?? "").Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        WindowWidth = Math.Clamp(double.IsFinite(WindowWidth) ? WindowWidth : 960, 560, 10_000),
        WindowHeight = Math.Clamp(double.IsFinite(WindowHeight) ? WindowHeight : 680, 380, 10_000),
    };
}

public sealed record SavedSearch(string Name, SearchDefaults Search);

public enum ResultsPanel
{
    Active,
    Other,
}

[Flags]
public enum DuplicateKeys
{
    None = 0,
    Name = 1,
    Size = 2,
    Content = 4,
    All = Name | Size | Content,
}

public enum ModifiedRule
{
    Any,
    Within,
    Between,
}

public sealed record SearchDefaults
{
    public static readonly SearchDefaults BuiltIn = new();

    public string Named { get; set; } = "*.*";

    public string LookIn { get; set; } = "";

    public List<SearchText> Texts { get; set; } = [];

    public bool AllTexts { get; set; } = true;

    public int? Depth { get; set; }

    public int Levels { get; set; } = 2;

    public bool InsideZip { get; set; }

    public bool MatchCase { get; set; }

    public bool WholeWords { get; set; }

    public SearchEncodings Encodings { get; set; } = SearchEncodings.Auto;

    public string MinSize { get; set; } = "";

    public string MaxSize { get; set; } = "";

    public int SizeUnit { get; set; } = 1;

    public ModifiedRule Modified { get; set; }

    public int WithinCount { get; set; } = 30;

    public int WithinUnit { get; set; } = 1;

    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    public FileAttributes MustHave { get; set; }

    public FileAttributes MustNotHave { get; set; }

    public bool IsBuiltIn() => Texts.Count == 0 && this with { Texts = BuiltIn.Texts } == BuiltIn;

    internal SearchDefaults Normalize() => this with
    {
        Named = Named ?? "*.*",
        LookIn = LookIn?.Trim() ?? "",
        Texts = (Texts ?? []).Where(t => t is not null).Select(t => t with { Text = t.Text ?? "" }).Take(20).ToList(),
        Depth = Depth is { } d ? Math.Clamp(d, 0, 100) : null,
        Levels = Math.Clamp(Levels, 1, 100),
        Encodings = Encodings == SearchEncodings.None ? SearchEncodings.Auto : Encodings,
        MinSize = MinSize?.Trim() ?? "",
        MaxSize = MaxSize?.Trim() ?? "",
        SizeUnit = Math.Clamp(SizeUnit, 0, 3),
        WithinCount = Math.Clamp(WithinCount, 1, 9999),
        WithinUnit = Math.Clamp(WithinUnit, 0, 3),
        MustNotHave = MustNotHave & ~MustHave,
    };
}
