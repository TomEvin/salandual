// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/SettingsTransfer.cs

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Salandual.Core.Resources;

namespace Salandual.Core.Settings;

public enum SettingsPart
{
    Appearance,
    Themes,
    Layout,
    Panels,
    Keyboard,
    HotPaths,
    Operations,
    Confirmations,
    Viewers,
    Editors,
    Compare,
    General,
    Advanced,

    History,

    Programs,
}

public sealed record SettingChange(SettingsPart Part, string Name, string Old, string New);

public sealed record ExportChoice(bool Themes = true, bool HotPaths = true, bool History = false);

public sealed record ResetChoice(bool KeepThemes = true, bool KeepHotPaths = true, bool KeepHistory = true);

public sealed class SettingsImport
{
    private readonly JsonObject _current;
    private readonly JsonObject _imported;

    internal SettingsImport(string file, DateTime? exported, string? exportedBy, JsonObject current, JsonObject imported, IReadOnlyList<SettingChange> changes, IReadOnlySet<SettingsPart> named)
    {
        Named = named;
        File = file;
        Exported = exported;
        ExportedBy = exportedBy;
        _current = current;
        _imported = imported;
        Changes = changes;
    }

    public string File { get; }

    public DateTime? Exported { get; }

    public string? ExportedBy { get; }

    public IReadOnlyList<SettingChange> Changes { get; }

    public IReadOnlySet<SettingsPart> Named { get; }

    public IReadOnlyList<SettingsPart> Same => [.. Named.Except(Changes.Select(c => c.Part)).Where(p => p != SettingsPart.Programs).Order()];

    public IReadOnlyList<SettingsPart> Parts => [.. Changes.Select(c => c.Part).Distinct().Order()];

    public AppSettings Apply(IReadOnlySet<SettingsPart> parts)
    {
        var result = (JsonObject)_current.DeepClone();
        foreach (var (path, part) in SettingsTransfer.Leaves(_current, _imported))
        {
            if (part is { } p && parts.Contains(p))
            {
                SettingsTransfer.Set(result, path, SettingsTransfer.Get(_imported, path));
            }
        }

        if (!parts.Contains(SettingsPart.Programs))
        {
            SettingsTransfer.KeepEditorPrograms(result, _current);
        }
        else if (!parts.Contains(SettingsPart.Editors))
        {
            SettingsTransfer.TakeEditorPrograms(result, _imported);
        }

        return SettingsTransfer.ToSettings(result);
    }
}

public static class SettingsTransfer
{
    public const string Extension = ".saldset";
    public const string Format = "salandual-settings";

    public const long MaxFileSize = 8 * 1024 * 1024;

    private static readonly (string Path, SettingsPart? Part)[] _places =
    [
        ("schemaVersion", null),
        ("appearance", SettingsPart.Appearance),
        ("appearance.themes", SettingsPart.Themes),
        ("appearance.schedule", SettingsPart.Themes),
        ("layout", SettingsPart.Layout),
        ("layout.panelSplit", null),
        ("panels", SettingsPart.Panels),
        ("mouse", SettingsPart.Panels),
        ("keyboard", SettingsPart.Keyboard),
        ("hotPaths", SettingsPart.HotPaths),
        ("driveMenu", SettingsPart.HotPaths),
        ("operations", SettingsPart.Operations),
        ("confirmations", SettingsPart.Confirmations),
        ("viewers", SettingsPart.Viewers),
        ("editors", SettingsPart.Editors),
        ("compare", SettingsPart.Compare),
        ("compare.files.useProgram", SettingsPart.Programs),
        ("compare.files.program", SettingsPart.Programs),
        ("compare.files.programArguments", SettingsPart.Programs),
        ("find", SettingsPart.Compare),
        ("find.named", SettingsPart.History),
        ("find.lookIn", SettingsPart.History),
        ("find.texts", SettingsPart.History),
        ("find.saved", SettingsPart.History),
        ("find.windowWidth", null),
        ("find.windowHeight", null),
        ("find.windowMaximized", null),
        ("fileList", SettingsPart.History),
        ("general", SettingsPart.General),
        ("advanced", SettingsPart.Advanced),
        ("history", SettingsPart.History),
    ];

    private static readonly JsonDocumentOptions _documentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static SettingsPart? PartOf(string path)
    {
        var best = -1;
        SettingsPart? part = null;
        foreach (var (place, p) in _places)
        {
            if ((path == place || path.StartsWith(place + ".", StringComparison.Ordinal)) && place.Length > best)
            {
                best = place.Length;
                part = p;
            }
        }

        return best < 0 ? SettingsPart.Advanced : part;
    }

    public static string SuggestedName(DateTime now) => Loc.Format(Strings.Transfer_FileName, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) + Extension;

    public static string Export(AppSettings settings, ExportChoice choice, DateTime now, string exportedBy)
    {
        var all = ToJson(settings);
        var file = new JsonObject
        {
            ["format"] = Format,
            ["exported"] = now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            ["exportedBy"] = exportedBy,
        };
        foreach (var (key, value) in all)
        {
            file[key] = value?.DeepClone();
        }

        foreach (var (path, part) in Leaves(all, all))
        {
            if (part is null && path != "schemaVersion"
                || part == SettingsPart.Themes && !choice.Themes && path == "appearance.themes"
                || part == SettingsPart.HotPaths && !choice.HotPaths
                || part == SettingsPart.History && !choice.History)
            {
                Remove(file, path);
            }
        }

        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static SettingsImport Read(string file, AppSettings current)
    {
        var info = new FileInfo(file);
        if (info.Length > MaxFileSize)
        {
            throw new InvalidDataException(Strings.Transfer_TooBig);
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(System.IO.File.ReadAllBytes(file), documentOptions: _documentOptions) as JsonObject
                ?? throw new InvalidDataException(Strings.Transfer_NotSettings);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Loc.Format(Strings.Transfer_Broken, (ex.LineNumber ?? 0) + 1), ex);
        }

        var format = root["format"] is JsonValue f && f.TryGetValue<string>(out var s) ? s : null;
        if (format is not null && format != Format || format is null && root["schemaVersion"] is null)
        {
            throw new InvalidDataException(Strings.Transfer_NotSettings);
        }

        DateTime? exported = root["exported"] is JsonValue e && e.TryGetValue<string>(out var when)
            && DateTime.TryParse(when, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
        var exportedBy = root["exportedBy"] is JsonValue b && b.TryGetValue<string>(out var by) ? by : null;
        foreach (var key in (ReadOnlySpan<string>)["format", "exported", "exportedBy"])
        {
            root.Remove(key);
        }

        var version = root["schemaVersion"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 1;
        AppSettings imported;
        try
        {
            var upgraded = SettingsMigrations.Apply((JsonObject)root.DeepClone(), version);
            imported = (upgraded.Deserialize(SettingsJsonContext.Default.AppSettings) ?? new AppSettings()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            throw new InvalidDataException(Loc.Format(Strings.Transfer_Broken, (ex is JsonException { LineNumber: { } line } ? line : 0) + 1), ex);
        }

        var mine = ToJson(current);
        var theirs = ToJson(imported);
        var present = SettingsMigrations.Apply(root, version);
        var limited = (JsonObject)mine.DeepClone();
        var named = new HashSet<SettingsPart>();
        foreach (var (path, part) in Leaves(mine, theirs))
        {
            if (part is { } p && Has(present, path))
            {
                Set(limited, path, Get(theirs, path));
                named.Add(p);
            }
        }

        return new SettingsImport(file, exported, exportedBy, mine, limited, Describe(mine, limited), named);
    }

    public static AppSettings ResetAll(AppSettings current, ResetChoice choice)
    {
        var mine = ToJson(current);
        var result = ToJson(new AppSettings());
        foreach (var (path, part) in Leaves(mine, result))
        {
            var keep = part is null
                || choice.KeepThemes && part == SettingsPart.Themes && path == "appearance.themes"
                || choice.KeepHotPaths && part == SettingsPart.HotPaths && path == "hotPaths.paths"
                || choice.KeepHistory && part == SettingsPart.History;
            if (keep)
            {
                Set(result, path, Get(mine, path));
            }
        }

        return ToSettings(result);
    }

    public static string KeepCopy(AppSettings settings, string dataDirectory, string what, DateTime now)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, $"settings.before-{what}-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json");
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
        return path;
    }

    private static List<SettingChange> Describe(JsonObject mine, JsonObject theirs)
    {
        var changes = new List<SettingChange>();
        foreach (var (path, part) in Leaves(mine, theirs))
        {
            var (a, b) = (Get(mine, path), Get(theirs, path));
            if (part is not { } p || JsonNode.DeepEquals(a, b))
            {
                continue;
            }

            switch (path)
            {
                case "appearance.themes":
                    changes.AddRange(ThemeChanges(a as JsonArray, b as JsonArray));
                    break;
                case "editors.masks":
                    var (withoutA, withoutB) = (WithoutPrograms(a), WithoutPrograms(b));
                    if (!JsonNode.DeepEquals(withoutA, withoutB))
                    {
                        changes.Add(new(p, Name(path), Value(withoutA), Value(withoutB)));
                    }

                    changes.AddRange(EditorProgramChanges(a as JsonArray, b as JsonArray));
                    break;
                default:
                    changes.Add(new(p, Name(path), Value(a), Value(b)));
                    break;
            }
        }

        return changes;
    }

    private static IEnumerable<SettingChange> ThemeChanges(JsonArray? mine, JsonArray? theirs)
    {
        static Dictionary<string, JsonNode> ById(JsonArray? list) => (list ?? []).OfType<JsonObject>()
            .Where(t => t["id"] is JsonValue).GroupBy(t => t["id"]!.ToString()).ToDictionary(g => g.Key, g => (JsonNode)g.First());
        static string NameOf(JsonNode t) => t["name"]?.ToString() ?? "";
        var (a, b) = (ById(mine), ById(theirs));
        foreach (var (id, theme) in b)
        {
            if (!a.TryGetValue(id, out var old))
            {
                yield return new(SettingsPart.Themes, NameOf(theme), Strings.Transfer_None, Strings.Transfer_Added);
            }
            else if (!JsonNode.DeepEquals(old, theme))
            {
                yield return new(SettingsPart.Themes, NameOf(theme), Strings.Transfer_Before, Strings.Transfer_Changed);
            }
        }
    }

    private static IEnumerable<SettingChange> EditorProgramChanges(JsonArray? mine, JsonArray? theirs)
    {
        foreach (var row in (theirs ?? []).OfType<JsonObject>())
        {
            var mask = row["mask"]?.ToString() ?? "";
            var program = row["program"]?.ToString();
            var before = ProgramFor(mine, mask);
            if (!string.IsNullOrEmpty(program) && program != before)
            {
                yield return new(SettingsPart.Programs, Loc.Format(Strings.Transfer_EditorFor, mask), before ?? Strings.Transfer_None, program);
            }
        }
    }

    private static string? ProgramFor(JsonArray? rows, string mask)
        => (rows ?? []).OfType<JsonObject>().FirstOrDefault(r => r["mask"]?.ToString() == mask)?["program"]?.ToString();

    private static JsonNode? WithoutPrograms(JsonNode? masks)
    {
        if (masks?.DeepClone() is not JsonArray copy)
        {
            return masks;
        }

        foreach (var row in copy.OfType<JsonObject>())
        {
            row.Remove("program");
        }

        return copy;
    }

    internal static void KeepEditorPrograms(JsonObject result, JsonObject current)
    {
        if (Get(result, "editors.masks") is JsonArray rows)
        {
            var mine = Get(current, "editors.masks") as JsonArray;
            foreach (var row in rows.OfType<JsonObject>())
            {
                row["program"] = ProgramFor(mine, row["mask"]?.ToString() ?? "");
            }
        }
    }

    internal static void TakeEditorPrograms(JsonObject result, JsonObject imported)
    {
        if (Get(result, "editors.masks") is JsonArray rows)
        {
            var theirs = Get(imported, "editors.masks") as JsonArray;
            foreach (var row in rows.OfType<JsonObject>())
            {
                if (ProgramFor(theirs, row["mask"]?.ToString() ?? "") is { Length: > 0 } program)
                {
                    row["program"] = program;
                }
            }
        }
    }

    private static string Name(string path)
    {
        switch (path)
        {
            case "compare.files.useProgram":
                return Strings.Transfer_CompareUseProgram;
            case "compare.files.program":
                return Strings.Transfer_CompareProgram;
            case "compare.files.programArguments":
                return Strings.Transfer_CompareArguments;
        }

        var segments = path.Split('.');
        var words = new StringBuilder();
        foreach (var segment in segments.Length > 1 ? segments[1..] : segments)
        {
            foreach (var c in segment)
            {
                if (char.IsUpper(c) && words.Length > 0)
                {
                    words.Append(' ');
                }

                words.Append(words.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            }

            words.Append(' ');
        }

        return words.ToString().TrimEnd();
    }

    private static string Value(JsonNode? node) => node switch
    {
        null => Strings.Transfer_None,
        JsonArray list => Loc.Plural(list.Count, Strings.Transfer_Items_One, Strings.Transfer_Items_Other),
        JsonObject => Strings.Transfer_Changed,
        JsonValue v when v.GetValueKind() == JsonValueKind.True => Strings.Transfer_On,
        JsonValue v when v.GetValueKind() == JsonValueKind.False => Strings.Transfer_Off,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length == 0 ? Strings.Transfer_None : s.Length > 48 ? s[..47] + "…" : Words(s),
        _ => node.ToJsonString(),
    };

    private static string Words(string value)
    {
        if (value.Length < 2 || !char.IsUpper(value[0]) || !value.All(char.IsAsciiLetterOrDigit))
        {
            return value;
        }

        var words = new StringBuilder().Append(value[0]);
        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c) || char.IsDigit(c) && !char.IsDigit(value[i - 1]))
            {
                words.Append(' ');
            }

            words.Append(char.IsUpper(c) ? char.ToLowerInvariant(c) : c);
        }

        return words.ToString();
    }

    internal static JsonObject ToJson(AppSettings settings) => (JsonObject)JsonSerializer.SerializeToNode(settings, SettingsJsonContext.Default.AppSettings)!;

    internal static AppSettings ToSettings(JsonObject json) => (json.Deserialize(SettingsJsonContext.Default.AppSettings) ?? new AppSettings()).Normalize();

    internal static IEnumerable<(string Path, SettingsPart? Part)> Leaves(JsonObject a, JsonObject b)
    {
        var found = new List<(string, SettingsPart?)>();
        Walk(a, b, "");
        return found;

        void Walk(JsonNode? x, JsonNode? y, string path)
        {
            if (x is JsonObject ox && y is JsonObject oy && !IsDictionaryLeaf(path))
            {
                foreach (var key in ox.Select(p => p.Key).Union(oy.Select(p => p.Key)))
                {
                    Walk(ox[key], oy[key], path.Length == 0 ? key : path + "." + key);
                }
            }
            else
            {
                found.Add((path, PartOf(path)));
            }
        }
    }

    private static bool IsDictionaryLeaf(string path) => path.EndsWith("Overrides", StringComparison.Ordinal);

    internal static JsonNode? Get(JsonObject root, string path)
    {
        JsonNode? node = root;
        foreach (var key in path.Split('.'))
        {
            node = node is JsonObject o ? o[key] : null;
        }

        return node;
    }

    private static bool Has(JsonObject root, string path)
    {
        JsonNode? node = root;
        foreach (var key in path.Split('.'))
        {
            if (node is not JsonObject o || !o.ContainsKey(key))
            {
                return false;
            }

            node = o[key];
        }

        return true;
    }

    internal static void Set(JsonObject root, string path, JsonNode? value)
    {
        var keys = path.Split('.');
        var node = root;
        foreach (var key in keys[..^1])
        {
            if (node[key] is not JsonObject next)
            {
                next = [];
                node[key] = next;
            }

            node = next;
        }

        node[keys[^1]] = value?.DeepClone();
    }

    private static void Remove(JsonObject root, string path)
    {
        var keys = path.Split('.');
        var parent = keys.Length == 1 ? root : Get(root, string.Join('.', keys[..^1])) as JsonObject;
        parent?.Remove(keys[^1]);
    }
}
