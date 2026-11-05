// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Resources/Languages.cs

using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Salandual.Core.Settings;

namespace Salandual.Core.Resources;

public enum LanguageProblemKind
{
    Missing,

    NotInEnglish,

    Empty,

    Braces,

    NotText,

    Unknown,

    NotTranslated,
}

public sealed record LanguageProblem(string Key, LanguageProblemKind Kind, string Placeholders = "", string? English = null, string? Path = null);

public sealed record LanguageFile(string Path, string FileName, string Code, string? Changes, string Name, IReadOnlyDictionary<string, string> Texts, string? Unreadable = null, IReadOnlyList<string>? NotTexts = null, LanguageHeader? Header = null, int? NewerFormat = null)
{
    public string Id => Changes is null ? Code : Code + "." + Changes;
}

public static partial class LanguageFiles
{
    public const string FolderName = "languages";

    public const string Format = FormatName + " 1";

    public const int FormatVersion = 1;

    private const string FormatName = "Salandual language file";

    public const long MaxBytes = 16L * 1024 * 1024;

    public const int MaxFiles = 64;

    public const int MaxChangesName = 40;

    public static IReadOnlyList<string> Folders(string dataDirectory, string programDirectory)
    {
        var data = System.IO.Path.Combine(dataDirectory, FolderName);
        var program = System.IO.Path.Combine(programDirectory, FolderName);
        return string.Equals(System.IO.Path.GetFullPath(data), System.IO.Path.GetFullPath(program), StringComparison.OrdinalIgnoreCase) ? [data] : [program, data];
    }

    public static bool TryParseId(string id, out string code, out string? changes)
    {
        code = "";
        changes = null;
        var dot = id.IndexOf('.', StringComparison.Ordinal);
        var head = dot < 0 ? id : id[..dot];
        var tail = dot < 0 ? null : id[(dot + 1)..];
        if (!CodePattern().IsMatch(head) || !KnownCulture(head) || (tail is not null && !ChangesPattern().IsMatch(tail)))
        {
            return false;
        }

        if (tail is null && string.Equals(head, GeneralSettings.English, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        code = CultureInfo.GetCultureInfo(head).Name;
        changes = tail?.ToLowerInvariant();
        return true;
    }

    public static bool IsLanguageFileName(string fileName)
        => fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && fileName.Length < 80
            && TryParseId(fileName[..^5], out _, out _);

    [GeneratedRegex(@"^Salandual language file ([0-9]{1,4})$")]
    private static partial Regex FormatPattern();

    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8}){0,2}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,39})$")]
    private static partial Regex ChangesPattern();

    private static bool KnownCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true).Name.Length > 0;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    public static (IReadOnlyList<LanguageFile> Files, IReadOnlyList<string> NotUnderstood) List(IEnumerable<string> folders)
    {
        var found = new Dictionary<string, LanguageFile>(StringComparer.OrdinalIgnoreCase);
        var odd = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            string[] files;
            try
            {
                files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var path in files.Order(StringComparer.OrdinalIgnoreCase).Take(MaxFiles))
            {
                var name = System.IO.Path.GetFileName(path);
                if (!IsLanguageFileName(name))
                {
                    odd.Add(name);
                }
                else if (TryParseId(name[..^5], out var code, out var changes) && !found.ContainsKey(Id(code, changes)))
                {
                    found[Id(code, changes)] = Read(path);
                }
            }
        }

        return ([.. found.Values.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Changes, StringComparer.OrdinalIgnoreCase)], [.. odd]);
    }

    private static string Id(string code, string? changes) => changes is null ? code : code + "." + changes;

    public static string? Find(string id, IEnumerable<string> folders)
        => TryParseId(id, out var code, out var changes) ? folders.Select(f => System.IO.Path.Combine(f, Id(code, changes) + ".json")).FirstOrDefault(File.Exists) : null;

    public static LanguageFile Read(string path)
    {
        var fileName = System.IO.Path.GetFileName(path);
        if (!TryParseId(System.IO.Path.GetFileNameWithoutExtension(path), out var code, out var changes))
        {
            code = GeneralSettings.English;
        }

        var ownName = OwnName(code);
        LanguageFile Unreadable(string why) => new(path, fileName, code, changes, ownName, new Dictionary<string, string>(), why);
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxBytes)
            {
                return Unreadable(Strings.Language_TooBig);
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Unreadable(ex.Message);
        }

        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("format", out var format))
            {
                var said = format.ValueKind == JsonValueKind.String ? format.GetString() ?? "" : "";
                var match = FormatPattern().Match(said.Trim());
                if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
                {
                    return Unreadable(Strings.Language_NotOurs);
                }

                if (version > FormatVersion)
                {
                    return Unreadable(Loc.Format(Strings.Language_Newer, version)) with { NewerFormat = version };
                }
            }

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("texts", out var texts) || texts.ValueKind != JsonValueKind.Object)
            {
                return Unreadable(Strings.Language_NoTexts);
            }

            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 and <= 100 } s ? s.Trim() : ownName;
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            var notTexts = new List<string>();
            foreach (var entry in texts.EnumerateObject())
            {
                var text = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
                if (text is null)
                {
                    notTexts.Add(entry.Name);
                }
                else
                {
                    found[entry.Name] = text;
                }
            }

            string Field(string field) => root.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: <= 200 } t ? t.Trim() : "";
            var header = new LanguageHeader(name, Field("author"), Field("contact"), Field("copyright"), Field("license"), Field("forVersion"), Field("updated"));
            return new LanguageFile(path, fileName, code, changes, name, found, null, notTexts, header);
        }
        catch (JsonException ex)
        {
            return Unreadable(Loc.Format(Strings.Language_NotJson, ex.LineNumber + 1));
        }
    }

    public static string OwnName(string code)
    {
        var culture = CultureInfo.GetCultureInfo(code);
        var name = culture.NativeName;
        return name.Length == 0 ? code : culture.TextInfo.ToUpper(name[0]) + name[1..];
    }

    public static string TextCultureOf(string code)
    {
        try
        {
            return CultureInfo.CreateSpecificCulture(code).Name is { Length: > 0 } name ? name : code;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    public static (IReadOnlyDictionary<string, string> Usable, IReadOnlyList<LanguageProblem> Problems) Check(LanguageFile file, Func<string, string?> english)
    {
        var usable = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<LanguageProblem>();
        foreach (var key in file.NotTexts ?? [])
        {
            problems.Add(new LanguageProblem(key, english(key) is null ? LanguageProblemKind.Unknown : LanguageProblemKind.NotText, "", english(key), file.Path));
        }

        foreach (var (key, text) in file.Texts.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var en = english(key);
            if (en is null)
            {
                problems.Add(new LanguageProblem(key, LanguageProblemKind.Unknown, Path: file.Path));
                continue;
            }

            if (CheckText(key, text, en) is { } problem)
            {
                problems.Add(problem with { Path = file.Path });
                continue;
            }

            usable[key] = text;
        }

        return (usable, problems);
    }

    public static LanguageProblem? CheckText(string key, string text, string english)
    {
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(english))
        {
            return new LanguageProblem(key, LanguageProblemKind.Empty, "", english);
        }

        var theirs = Placeholders(english);
        var ours = Placeholders(text);
        var extra = ours.Except(theirs).ToList();
        if (extra.Count > 0)
        {
            return new LanguageProblem(key, LanguageProblemKind.NotInEnglish, Join(extra), english);
        }

        var missing = theirs.Except(ours).Where(n => !(n == 0 && key.EndsWith("_One", StringComparison.Ordinal))).ToList();
        if (missing.Count > 0)
        {
            return new LanguageProblem(key, LanguageProblemKind.Missing, Join(missing), english);
        }

        return Fills(english) && !Fills(text) ? new LanguageProblem(key, LanguageProblemKind.Braces, "", english) : null;
    }

    private static string Join(IEnumerable<int> numbers) => string.Join(", ", numbers.Order().Select(n => "{" + n.ToString(CultureInfo.InvariantCulture) + "}"));

    public static IReadOnlySet<int> Placeholders(string text)
        => Placeholder().Matches(text).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToHashSet();

    private static bool Fills(string text)
    {
        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, text, _samples);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static readonly object?[] _samples = [.. Enumerable.Range(0, 20).Select(i => (object)i)];

    [GeneratedRegex(@"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    public static void Write(string path, LanguageHeader header, IEnumerable<KeyValuePair<string, string>> texts)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        json.WriteStartObject();
        json.WriteString("format", Format);
        json.WriteString("name", header.Name);
        json.WriteString("author", header.Author);
        json.WriteString("contact", header.Contact);
        json.WriteString("copyright", header.Copyright);
        json.WriteString("license", header.License);
        json.WriteString("forVersion", header.ForVersion);
        json.WriteString("updated", header.Updated);
        json.WriteString("updatedFormat", LanguageHeader.UpdatedFormat);
        json.WriteStartObject("texts");
        foreach (var (key, text) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            json.WriteString(key, text);
        }

        json.WriteEndObject();
        json.WriteEndObject();
    }
}

public sealed record LanguageHeader(string Name, string Author, string Contact, string Copyright, string License, string ForVersion, string Updated)
{
    public const string UpdatedFormat = "YYYY-MM-DD";

    public const string DateFormat = "yyyy-MM-dd";

    public static string Date(DateTime day) => day.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateTime? ReadDate(string? text)
        => DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}

public sealed record LanguageChoice(string Id, IReadOnlyDictionary<string, string> Texts, LanguageFile? Language, LanguageFile? Changes, IReadOnlyList<LanguageProblem> Problems, string? Missing = null)
{
    public static LanguageChoice English { get; } = new(GeneralSettings.English, new Dictionary<string, string>(), null, null, []);

    public int Broken => Problems.Count(p => p.Kind is not (LanguageProblemKind.Unknown or LanguageProblemKind.NotTranslated));

    public LanguageFile? Unreadable => Language?.Unreadable is not null ? Language : Changes?.Unreadable is not null ? Changes : null;

    public string FileName => (Changes is { } c && (Language is null || Problems.Any(p => p.Path == c.Path)) ? c : Language)?.FileName ?? Missing ?? Id;
}

public static class TextLanguage
{
    public static LanguageChoice Current { get; private set; } = LanguageChoice.English;

    public static ResourceManager Original(ResourceManager manager) => manager is LayeredResourceManager layered ? layered.Inner : manager;

    public static string? EnglishOf(string key, IEnumerable<ResourceManager> originals)
        => originals.Select(r => Original(r).GetString(key, CultureInfo.InvariantCulture)).FirstOrDefault(t => t is not null);

    public static LanguageChoice Choose(string id, IEnumerable<string> folders, IReadOnlyList<ResourceManager> originals)
    {
        if (id == GeneralSettings.English)
        {
            return LanguageChoice.English;
        }

        if (!LanguageFiles.TryParseId(id, out var code, out var changes))
        {
            return LanguageChoice.English with { Id = id, Missing = id + ".json" };
        }

        var list = folders.ToList();
        string? English(string key) => EnglishOf(key, originals);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<LanguageProblem>();
        LanguageFile? language = null;
        if (code != GeneralSettings.English)
        {
            if (LanguageFiles.Find(code, list) is not { } path)
            {
                return LanguageChoice.English with { Id = id, Missing = code + ".json" };
            }

            language = LanguageFiles.Read(path);
            if (language.Unreadable is not null)
            {
                return LanguageChoice.English with { Id = id, Language = language };
            }

            var (usable, found) = LanguageFiles.Check(language, English);
            foreach (var (key, text) in usable)
            {
                texts[key] = text;
            }

            problems.AddRange(found);
            texts.TryAdd(nameof(Strings.Text_Culture), LanguageFiles.TextCultureOf(code));
        }

        LanguageFile? mine = null;
        string? missing = null;
        if (changes is not null)
        {
            if (LanguageFiles.Find(id, list) is { } path)
            {
                mine = LanguageFiles.Read(path);
                if (mine.Unreadable is null)
                {
                    var (usable, found) = LanguageFiles.Check(mine, English);
                    foreach (var (key, text) in usable)
                    {
                        texts[key] = text;
                    }

                    problems.AddRange(found);
                }
            }
            else
            {
                missing = id + ".json";
            }
        }

        if (texts.TryGetValue(nameof(Strings.Text_Culture), out var chosen) && !KnownCulture(chosen))
        {
            texts[nameof(Strings.Text_Culture)] = LanguageFiles.TextCultureOf(code);
        }

        return new LanguageChoice(id, texts, language, mine, problems, missing);
    }

    private static bool KnownCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true).Name.Length > 0;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    public static void Install([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields)] Type resources, ResourceManager original, LanguageChoice choice)
    {
        var field = resources.GetField("resourceMan", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The resource class has no resourceMan field.");
        field.SetValue(null, new LayeredResourceManager(Original(original), choice));
        Current = choice;
    }

    private sealed class LayeredResourceManager(ResourceManager inner, LanguageChoice choice) : ResourceManager
    {
        public ResourceManager Inner { get; } = inner;

        public LanguageChoice Choice { get; } = choice;

        public override string BaseName => Inner.BaseName;

        public override string? GetString(string name) => GetString(name, null);

        public override string? GetString(string name, CultureInfo? culture)
            => Choice.Texts.TryGetValue(name, out var text) ? text : Inner.GetString(name, CultureInfo.InvariantCulture);

        public override object? GetObject(string name) => Inner.GetObject(name, CultureInfo.InvariantCulture);

        public override object? GetObject(string name, CultureInfo? culture) => Inner.GetObject(name, CultureInfo.InvariantCulture);
    }
}
