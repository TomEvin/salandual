// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/SearchTests.cs

using System.IO.Compression;
using System.Text;
using Salandual.Core.Search;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Core.Tests;

public sealed class SearchTests
{
    private static async Task<(List<SearchHit> Hits, List<SearchLogEntry> Log, SearchSummary Summary)> Find(MemoryFiles files, SearchCriteria criteria)
    {
        var hits = new List<SearchHit>();
        var log = new List<SearchLogEntry>();
        var summary = await new FileSearch(files, files.Open).RunAsync(
            criteria with { LookIn = criteria.LookIn.Count == 0 ? [@"C:\Work"] : criteria.LookIn },
            new SearchCallbacks { Found = hits.Add, Log = log.Add }, TestContext.Current.CancellationToken);
        return (hits, log, summary);
    }

    private static string[] Names(IEnumerable<SearchHit> hits) => [.. hits.Select(h => h.Name).Order(StringComparer.Ordinal)];

    private static ContentResult Content(string text, params SearchText[] rows) => Content(Encoding.UTF8.GetBytes(text), new SearchCriteria { Texts = rows });

    private static ContentResult Content(byte[] bytes, SearchCriteria criteria)
    {
        var matcher = ContentMatcher.Create(criteria, out var problem);
        Assert.Null(problem);
        return matcher!.Match(new MemoryStream(bytes));
    }

    [Theory]
    [InlineData("dog", "hotdog.jpg", true)]
    [InlineData("dog", "dogs.txt", true)]
    [InlineData("dog", "cat.txt", false)]
    [InlineData("dog.", "dog", true)]
    [InlineData("dog.", "dogs", false)]
    [InlineData("dog.", "hotdog.jpg", false)]
    [InlineData("*.md;*.txt", "notes.txt", true)]
    [InlineData("*.md;*.txt", "notes.doc", false)]
    [InlineData("readme;*.txt", "README.md", true)]
    [InlineData("*.*|*.bak", "old.bak", false)]
    [InlineData("|*.bak", "a.cs", true)]
    [InlineData("", "anything", true)]
    public void A_name_without_wildcards_finds_names_containing_it(string named, string name, bool expected)
        => Assert.Equal(expected, NameRule.ToMask(named).IsMatch(name));

    [Fact]
    public void Skipped_folders_by_name_at_the_top_of_a_drive_by_ending_and_by_path()
    {
        var rules = new SkipRules(["node_modules", @"\System Volume Information", @"Local\Temp", @"D:\Backup\"]);
        Assert.True(rules.IsSkipped(@"C:\a\b\node_modules", "node_modules", @"C:\a\b", @"C:\"));
        Assert.True(rules.IsSkipped(@"C:\System Volume Information", "System Volume Information", @"C:\", @"C:\"));
        Assert.False(rules.IsSkipped(@"C:\x\System Volume Information", "System Volume Information", @"C:\x", @"C:\"));
        Assert.True(rules.IsSkipped(@"C:\Users\me\AppData\Local\Temp", "Temp", @"C:\Users\me\AppData\Local", @"C:\"));
        Assert.False(rules.IsSkipped(@"C:\Temp", "Temp", @"C:\", @"C:\"));
        Assert.True(rules.IsSkipped(@"D:\Backup", "Backup", @"D:\", @"D:\"));
        Assert.False(rules.IsSkipped(@"E:\Backup", "Backup", @"E:\", @"E:\"));
    }

    [Fact]
    public void Contains_and_doesnt_contain_anywhere_in_the_file_all_or_any()
    {
        const string text = "Run deploy.ps1\nFriday: test first\nno draft here\n";
        Assert.True(Content(text, new SearchText("deploy")).IsMatch);
        Assert.False(Content(text, new SearchText("deploy"), new SearchText("draft", Contains: false)).IsMatch);
        Assert.True(Content(text, new SearchText("deploy"), new SearchText("friday")).IsMatch);
        Assert.True(Content(text, new SearchText("deploy"), new SearchText("release", Contains: false)).IsMatch);
        Assert.True(Content("only release", new SearchText("deploy", Contains: false)).IsMatch);

        var any = new SearchCriteria { Texts = [new("missing"), new("Friday")], AllTexts = false };
        Assert.True(Content(Encoding.UTF8.GetBytes(text), any).IsMatch);
        var all = any with { AllTexts = true };
        Assert.False(Content(Encoding.UTF8.GetBytes(text), all).IsMatch);
    }

    [Fact]
    public void The_matching_line_with_how_many_more_lines_match()
    {
        var result = Content("first\nRun deploy.ps1 now\nsecond deploy\n\tthird DEPLOY\n", new SearchText("deploy"));
        Assert.True(result.IsMatch);
        Assert.Equal(2, result.Line!.LineNumber);
        Assert.Equal("Run deploy.ps1 now", result.Line.Text);
        Assert.Equal(4, result.Line.Start);
        Assert.Equal(6, result.Line.Length);
        Assert.Equal(2, result.Line.MoreLines);
    }

    [Fact]
    public void The_byte_order_mark_is_not_part_of_the_first_line()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("deploy now")).ToArray();
        Assert.Equal("deploy now", Content(bytes, new SearchCriteria { Texts = [new("deploy")] }).Line!.Text);
    }

    [Fact]
    public void A_long_line_shows_the_part_around_the_match()
    {
        var line = new string('x', 5000) + "needle" + new string('y', 5000);
        var result = Content(line, new SearchText("needle"));
        Assert.True(result.Line!.Text.Length <= 240);
        Assert.Equal("needle", result.Line.Text.Substring(result.Line.Start, result.Line.Length));
    }

    [Fact]
    public void Match_case_whole_words_and_regular_expressions()
    {
        var bytes = Encoding.UTF8.GetBytes("Deployment of ABC-123 on 2026-10-17");
        Assert.True(Content(bytes, new SearchCriteria { Texts = [new("deploy")] }).IsMatch);
        Assert.False(Content(bytes, new SearchCriteria { Texts = [new("deploy")], MatchCase = true }).IsMatch);
        Assert.False(Content(bytes, new SearchCriteria { Texts = [new("Deploy")], WholeWords = true }).IsMatch);
        Assert.True(Content(bytes, new SearchCriteria { Texts = [new(@"[A-Z]{2,4}-\d+", TextKind.Regex)] }).IsMatch);

        Assert.Null(ContentMatcher.Create(new SearchCriteria { Texts = [new("ok"), new("(broken", TextKind.Regex)] }, out var problem));
        Assert.Equal(1, problem!.Row);
    }

    [Fact]
    public void Hex_with_text_in_quotes()
    {
        Assert.Equal(new byte[] { 1, 2, (byte)'t', (byte)'x', 0 }, HexPattern.Parse("01 02 \"tx\" 00", out _));
        Assert.Equal(new byte[] { 0x4D, 0x5A }, HexPattern.Parse("4d5a", out _));
        Assert.Null(HexPattern.Parse("4", out var odd));
        Assert.NotNull(odd);
        Assert.Null(HexPattern.Parse("\"open", out var quote));
        Assert.NotNull(quote);

        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, (byte)'P', (byte)'E', 0, 0 };
        var result = Content(exe, new SearchCriteria { Texts = [new("\"PE\" 00 00", TextKind.Hex)] });
        Assert.True(result.IsMatch);
        Assert.Equal(6, result.Line!.Offset);
    }

    [Fact]
    public void Encodings_each_files_own_or_the_ones_ticked()
    {
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hello Wörld")).ToArray();
        Assert.True(Content(utf16, new SearchCriteria { Texts = [new("wörld")] }).IsMatch);

        var noMark = Encoding.Unicode.GetBytes("plain unicode text with no mark at all");
        Assert.True(Content(noMark, new SearchCriteria { Texts = [new("unicode")], Encodings = SearchEncodings.Utf16LE }).IsMatch);
        Assert.False(Content(noMark, new SearchCriteria { Texts = [new("unicode")], Encodings = SearchEncodings.Utf8 }).IsMatch);

        var latin = Encoding.Latin1.GetBytes("café");
        Assert.True(Content(latin, new SearchCriteria { Texts = [new("café")], Encodings = SearchEncodings.Utf8 | SearchEncodings.Ansi }).IsMatch);
    }

    [Fact]
    public async Task Subfolders_all_none_or_down_to_some_levels()
    {
        var files = new MemoryFiles()
            .File(@"C:\Work\a.txt", "")
            .File(@"C:\Work\one\b.txt", "")
            .File(@"C:\Work\one\two\c.txt", "")
            .File(@"C:\Work\one\two\three\d.txt", "");
        var criteria = new SearchCriteria { Named = "*.txt" };
        Assert.Equal(["a.txt", "b.txt", "c.txt", "d.txt"], Names((await Find(files, criteria)).Hits));
        Assert.Equal(["a.txt"], Names((await Find(files, criteria with { Depth = 0 })).Hits));
        Assert.Equal(["a.txt", "b.txt", "c.txt"], Names((await Find(files, criteria with { Depth = 2 })).Hits));
    }

    [Fact]
    public async Task Folders_are_found_by_name_but_not_by_content()
    {
        var files = new MemoryFiles().Folder(@"C:\Work\docs").File(@"C:\Work\docs\docs.md", "the docs");
        Assert.Equal(["docs", "docs.md"], Names((await Find(files, new SearchCriteria { Named = "docs" })).Hits));
        Assert.Equal(["docs.md"], Names((await Find(files, new SearchCriteria { Named = "docs", Texts = [new("the")] })).Hits));
    }

    [Fact]
    public async Task Skipped_folders_and_links_are_not_looked_into_and_are_in_the_log()
    {
        var files = new MemoryFiles()
            .File(@"C:\Work\node_modules\x.js", "")
            .File(@"C:\Work\src\y.js", "")
            .Folder(@"C:\Work\link", FileAttributes.ReparsePoint)
            .File(@"C:\Work\link\z.js", "");
        var (hits, log, _) = await Find(files, new SearchCriteria { Named = "*.js", SkippedFolders = ["node_modules"] });
        Assert.Equal(["y.js"], Names(hits));
        Assert.Contains(log, e => e.Kind == SearchLogKind.Skipped && e.Path.EndsWith("node_modules", StringComparison.Ordinal));
        Assert.Contains(log, e => e.Kind == SearchLogKind.Skipped && e.Path.EndsWith("link", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_folder_that_cant_be_read_is_in_the_log_and_the_search_goes_on()
    {
        var files = new MemoryFiles().File(@"C:\Work\locked\a.txt", "").File(@"C:\Work\open\b.txt", "");
        files.Unreadable.Add(@"C:\Work\locked");
        var (hits, log, summary) = await Find(files, new SearchCriteria { Named = "*.txt" });
        Assert.Equal(["b.txt"], Names(hits));
        Assert.Single(log, e => e.Kind == SearchLogKind.Error && e.Path == @"C:\Work\locked");
        Assert.False(summary.Cancelled);
    }

    [Fact]
    public async Task Size_date_and_attributes()
    {
        var files = new MemoryFiles()
            .File(@"C:\Work\small.txt", "12")
            .File(@"C:\Work\big.txt", new string('x', 2000), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .File(@"C:\Work\hidden.txt", "123", attributes: FileAttributes.Hidden | FileAttributes.Archive)
            .Folder(@"C:\Work\folder");
        Assert.Equal(["big.txt"], Names((await Find(files, new SearchCriteria { MinSize = 1000 })).Hits));
        Assert.Equal(["hidden.txt", "small.txt"], Names((await Find(files, new SearchCriteria { MaxSize = 100 })).Hits));
        Assert.Equal(["big.txt"], Names((await Find(files, new SearchCriteria { Named = "*.txt", ModifiedTo = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) })).Hits));
        Assert.Equal(["folder", "hidden.txt", "small.txt"], Names((await Find(files, new SearchCriteria { ModifiedFrom = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc) })).Hits));
        var hidden = new Dictionary<FileAttributes, AttributeRule> { [FileAttributes.Hidden] = AttributeRule.Must };
        Assert.Equal(["hidden.txt"], Names((await Find(files, new SearchCriteria { Attributes = hidden })).Hits));
        var notHidden = new Dictionary<FileAttributes, AttributeRule> { [FileAttributes.Hidden] = AttributeRule.MustNot };
        Assert.Equal(["big.txt", "folder", "small.txt"], Names((await Find(files, new SearchCriteria { Attributes = notHidden })).Hits));

        var folders = new Dictionary<FileAttributes, AttributeRule> { [FileAttributes.Directory] = AttributeRule.Must };
        Assert.Equal(["folder"], Names((await Find(files, new SearchCriteria { Attributes = folders })).Hits));
        var noFolders = new Dictionary<FileAttributes, AttributeRule> { [FileAttributes.Directory] = AttributeRule.MustNot };
        Assert.Equal(["big.txt", "hidden.txt", "small.txt"], Names((await Find(files, new SearchCriteria { Attributes = noFolders })).Hits));
    }

    [Fact]
    public async Task Files_only_in_the_cloud_are_not_read_for_their_content()
    {
        var files = new MemoryFiles()
            .File(@"C:\Work\here.txt", "invoice")
            .File(@"C:\Work\cloud.txt", "invoice", attributes: FileAttributes.Archive | (FileAttributes)0x00400000);
        var (hits, log, _) = await Find(files, new SearchCriteria { Texts = [new("invoice")] });
        Assert.Equal(["here.txt"], Names(hits));
        Assert.DoesNotContain(@"C:\Work\cloud.txt", files.Opened);
        Assert.Contains(log, e => e.Kind == SearchLogKind.NotRead && e.Path == @"C:\Work\cloud.txt");
        Assert.Equal(["cloud.txt", "here.txt"], Names((await Find(files, new SearchCriteria { Named = "*.txt" })).Hits));
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                var entry = zip.CreateEntry(name);
                if (!name.EndsWith('/'))
                {
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write(text);
                }
            }
        }

        return memory.ToArray();
    }

    [Fact]
    public async Task Inside_zip_files_names_and_contents_when_asked()
    {
        var zip = Zip(("docs/readme.txt", "install the invoice tool"), ("docs/", ""), ("other.bin", "nothing"), ("../evil.txt", "invoice"));
        var files = new MemoryFiles().File(@"C:\Work\pack.zip", zip).File(@"C:\Work\plain.txt", "invoice");
        Assert.Equal(["plain.txt"], Names((await Find(files, new SearchCriteria { Texts = [new("invoice")] })).Hits));

        var (hits, _, _) = await Find(files, new SearchCriteria { Texts = [new("invoice")], InsideZip = true });
        Assert.Equal(["plain.txt", "readme.txt"], Names(hits));
        var inside = hits.Single(h => h.Name == "readme.txt");
        Assert.Equal(@"C:\Work\pack.zip", inside.Archive);
        Assert.Equal(@"C:\Work\pack.zip\docs\readme.txt", inside.FullPath);
        Assert.Equal(1, inside.Line!.LineNumber);

        var byName = await Find(files, new SearchCriteria { Named = "doc", InsideZip = true });
        Assert.Equal(["docs"], Names(byName.Hits));
    }

    [Fact]
    public async Task Stopping_ends_the_search()
    {
        var files = new MemoryFiles().File(@"C:\Work\a.txt", "");
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        var summary = await new FileSearch(files, files.Open).RunAsync(new SearchCriteria { LookIn = [@"C:\Work"] }, new SearchCallbacks(), stop.Token);
        Assert.True(summary.Cancelled);
    }

    [Fact]
    public async Task Refining_keeps_or_removes_what_also_matches()
    {
        var files = new MemoryFiles().File(@"C:\Work\car.txt", "car").File(@"C:\Work\both.txt", "car and John").File(@"C:\Work\john.txt", "John");
        var (cars, _, _) = await Find(files, new SearchCriteria { Texts = [new("car")] });
        var search = new FileSearch(files, files.Open);
        var john = search.Filter(new SearchCriteria { Texts = [new("John")] }, cars, new SearchCallbacks(), TestContext.Current.CancellationToken);

        Assert.Equal(["both.txt"], Names(SearchResults.Combine(cars, john, ResultMode.Keep)));
        Assert.Equal(["car.txt"], Names(SearchResults.Combine(cars, john, ResultMode.Remove)));
        var (johns, _, _) = await Find(files, new SearchCriteria { Texts = [new("John")] });
        Assert.Equal(["both.txt", "car.txt", "john.txt"], Names(SearchResults.Combine(cars, johns, ResultMode.Add)));
        Assert.Equal(["both.txt", "john.txt"], Names(SearchResults.Combine(cars, johns, ResultMode.New)));
    }

    [Fact]
    public void Results_sort_with_folders_first()
    {
        var hits = new List<SearchHit>
        {
            new(@"C:\b", "zeta.txt", 10, 3, FileAttributes.Archive),
            new(@"C:\a", "alpha.txt", 30, 1, FileAttributes.Archive),
            new(@"C:\a", "folder", 0, 2, FileAttributes.Directory),
        };
        hits.Sort(SearchResults.Sorter(ResultColumn.Name, descending: false));
        Assert.Equal(["folder", "alpha.txt", "zeta.txt"], hits.Select(h => h.Name));
        hits.Sort(SearchResults.Sorter(ResultColumn.Size, descending: true));
        Assert.Equal(["folder", "alpha.txt", "zeta.txt"], hits.Select(h => h.Name));
        hits.Sort(SearchResults.Sorter(ResultColumn.Folder, descending: true));
        Assert.Equal(["folder", "zeta.txt", "alpha.txt"], hits.Select(h => h.Name));
    }

    [Fact]
    public void Regex_pieces_go_in_at_the_caret_and_a_group_around_the_selection()
    {
        var pieces = RegexPieces.All;
        Assert.Contains(pieces, p => p.Pattern == "^" && !p.Ready);
        Assert.Contains(pieces, p => p.Pattern == @"\d+" && p.Ready);
        Assert.All(pieces.Where(p => p.Ready), p => _ = new System.Text.RegularExpressions.Regex(p.Pattern));

        Assert.Equal(("deploy.*", 8), RegexPieces.Insert("deploy", 6, 6, pieces.First(p => p.Pattern == ".")) is var (t, c) ? (t + "*", c + 1) : default);
        Assert.Equal(("a(bc)d", 5), RegexPieces.Insert("abcd", 1, 3, pieces.First(p => p.Pattern == "()")));
        Assert.Equal(("x()", 2), RegexPieces.Insert("x", 1, 1, pieces.First(p => p.Pattern == "()")));
    }

    [Fact]
    public void Search_settings_start_with_the_two_skipped_folders_and_the_built_in_defaults_and_stay_in_range()
    {
        var find = new AppSettings().Normalize().Find;
        Assert.Equal(["System Volume Information", "$Recycle.Bin"], find.SkippedFolders);
        Assert.True(find.Defaults.IsBuiltIn());
        Assert.False((find.Defaults with { Named = "*.md" }).IsBuiltIn());
        Assert.Equal("*.*", find.Defaults.Named);
        Assert.Equal("", find.Defaults.LookIn);
        Assert.Equal(SearchEncodings.Auto, find.Defaults.Encodings);
        Assert.Null(find.Defaults.Depth);
        Assert.False(new AppSettings().Confirmations.CloseSearch);

        var odd = new AppSettings { Find = new FindSettings { Defaults = new() { Depth = 500, Levels = 0, Encodings = SearchEncodings.None, SizeUnit = 9, MustHave = FileAttributes.Hidden, MustNotHave = FileAttributes.Hidden | FileAttributes.System }, SkippedFolders = ["a", " A ", ""] } }.Normalize().Find;
        Assert.Equal(100, odd.Defaults.Depth);
        Assert.Equal(1, odd.Defaults.Levels);
        Assert.Equal(SearchEncodings.Auto, odd.Defaults.Encodings);
        Assert.Equal(3, odd.Defaults.SizeUnit);
        Assert.Equal(FileAttributes.System, odd.Defaults.MustNotHave);
        Assert.Equal(["a"], odd.SkippedFolders);
    }

    [Fact]
    public void Search_defaults_are_saved_and_read_back()
    {
        var dir = Directory.CreateTempSubdirectory("salandual-search-defaults-").FullName;
        try
        {
            var path = Path.Join(dir, "settings.json");
            var defaults = new SearchDefaults
            {
                Named = "*.md", LookIn = @"C:Projects", Texts = [new("deploy"), new("4D 5A", TextKind.Hex, Contains: false)], AllTexts = false,
                Depth = 2, InsideZip = true, MatchCase = true, Encodings = SearchEncodings.Ansi | SearchEncodings.Utf8,
                MinSize = "30", SizeUnit = 2, Modified = ModifiedRule.Between, From = new DateTime(2026, 9, 1), MustHave = FileAttributes.Archive, MustNotHave = FileAttributes.System,
            };
            new SettingsStore(path).Save(new AppSettings { Find = new FindSettings { Defaults = defaults } });
            var back = new SettingsStore(path).Load().Settings.Find.Defaults;
            Assert.Equal(defaults.Texts, back.Texts);
            Assert.Equal(defaults with { Texts = back.Texts }, back);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Hex_input_keeps_hex_digits_in_pairs()
    {
        Assert.Equal("4D 5A 90", HexInput.Format("4d5a90"));
        Assert.Equal("4D 5A 9", HexInput.Format("4D 5a, 9z"));
        Assert.Equal("", HexInput.Format("xyz"));
        Assert.Equal(0, HexInput.CaretAfter(0));
        Assert.Equal(2, HexInput.CaretAfter(2));
        Assert.Equal(4, HexInput.CaretAfter(3));
        Assert.Equal(3, HexInput.DigitsBefore("4D 5A", 4));
        Assert.Equal("64 65 70 6C 6F 79", HexInput.FromText("deploy"));
        Assert.Equal("4D 5A", HexInput.FromText("4d 5a"));
        Assert.Equal("deploy", HexInput.ToText("64 65 70 6C 6F 79"));
        Assert.Null(HexInput.ToText("4D 5"));
        Assert.Null(HexInput.ToText("00 01"));
    }

    [Fact]
    public void The_common_folder_of_results()
    {
        Assert.Equal(@"C:\a", SearchResults.CommonFolder([@"C:\a\b", @"C:\a\c\d", @"C:\A"]));
        Assert.Equal(@"C:\", SearchResults.CommonFolder([@"C:\a", @"C:\b"]));
        Assert.Equal("", SearchResults.CommonFolder([@"C:\a", @"D:\a"]));
        Assert.Equal(@"C:\a\b", SearchResults.CommonFolder([@"C:\a\b"]));
    }

    private static MemoryFiles DuplicateFiles() => new MemoryFiles()
        .File(@"C:\Work\a\photo.jpg", "same bytes", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc))
        .File(@"C:\Work\b\IMG_1.jpg", "same bytes", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc))
        .File(@"C:\Work\b\other.jpg", "diff bytes", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))
        .File(@"C:\Work\a\notes.txt", "short", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc))
        .File(@"C:\Work\b\notes.txt", "a longer text", new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc))
        .File(@"C:\Work\b\empty1.log", "")
        .File(@"C:\Work\a\empty2.log", "");

    private static async Task<IReadOnlyList<DuplicateGroup>> FindDuplicates(MemoryFiles files, DuplicateKeys keys)
    {
        var (groups, summary) = await Duplicates.FindAsync(new FileSearch(files, files.Open), new SearchCriteria { LookIn = [@"C:\Work"] }, keys, new SearchCallbacks(), null, TestContext.Current.CancellationToken);
        Assert.False(summary.Cancelled);
        return groups;
    }

    [Fact]
    public async Task Duplicates_by_size_and_content_read_only_files_of_the_same_size()
    {
        var files = DuplicateFiles();
        var groups = await FindDuplicates(files, DuplicateKeys.Size | DuplicateKeys.Content);
        Assert.Equal(2, groups.Count);
        var photos = groups[0];
        Assert.Equal(["photo.jpg", "IMG_1.jpg"], photos.Files.Select(f => f.Name));
        Assert.True(photos.SameContent);
        Assert.Equal(10, photos.Size);
        Assert.Equal(10, photos.Freeable);
        Assert.Equal(["empty1.log", "empty2.log"], groups[1].Files.Select(f => f.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(files.Opened, p => p.EndsWith("notes.txt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicates_follow_what_is_lit()
    {
        var byName = await FindDuplicates(DuplicateFiles(), DuplicateKeys.Name);
        var notes = Assert.Single(byName);
        Assert.Equal("notes.txt", notes.Name);
        Assert.Null(notes.Size);
        Assert.Equal(0, notes.Freeable);

        var bySize = await FindDuplicates(DuplicateFiles(), DuplicateKeys.Size);
        Assert.Equal(3, Assert.Single(bySize, g => g.Size == 10).Files.Count);
        Assert.Empty(await FindDuplicates(DuplicateFiles(), DuplicateKeys.Name | DuplicateKeys.Size));
        Assert.Empty(Duplicates.Group([], DuplicateKeys.None));
    }

    [Fact]
    public async Task Duplicates_stopped_say_nothing_is_the_same()
    {
        var files = DuplicateFiles();
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        var (groups, summary) = await Duplicates.FindAsync(new FileSearch(files, files.Open), new SearchCriteria { LookIn = [@"C:\Work"] }, DuplicateKeys.Size | DuplicateKeys.Content, new SearchCallbacks(), null, stop.Token);
        Assert.True(summary.Cancelled);
        Assert.Empty(groups);
    }

    [Fact]
    public void Replacement_pieces_for_the_editor()
    {
        Assert.Equal(["$0", "$1", "$2", "$3", "${name}", "$$"], RegexPieces.Replacements.Select(p => p.Pattern));
        Assert.Equal(("($1)", 3), RegexPieces.Insert("()", 1, 1, RegexPieces.Replacements[1]));
    }

    [Fact]
    public void Saved_searches_keep_their_order_and_one_of_each_name()
    {
        var settings = new AppSettings
        {
            Find = new FindSettings
            {
                Saved = [new SavedSearch(" Big ", new SearchDefaults { Named = "*.mp4" }), new SavedSearch("big", new SearchDefaults()), new SavedSearch("", new SearchDefaults()), new SavedSearch("Logs", null!)],
                DuplicateKeys = DuplicateKeys.None,
                ResultsPanel = (ResultsPanel)7,
            },
        }.Normalize();
        Assert.Equal(["Big", "Logs"], settings.Find.Saved.Select(s => s.Name));
        Assert.Equal("*.mp4", settings.Find.Saved[0].Search.Named);
        Assert.Equal(DuplicateKeys.Size | DuplicateKeys.Content, settings.Find.DuplicateKeys);
        Assert.Equal(ResultsPanel.Active, settings.Find.ResultsPanel);
    }
}
