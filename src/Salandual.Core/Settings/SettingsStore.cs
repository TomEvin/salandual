// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/SettingsStore.cs

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Salandual.Core.Settings;

public enum SettingsLoadStatus
{
    Loaded,

    NotFound,

    Corrupt,

    Unreadable,

    NewerVersion,
}

public sealed record SettingsLoadResult(AppSettings Settings, SettingsLoadStatus Status, int? MigratedFrom = null, string? BackupPath = null, Exception? Error = null)
{
    public bool CanSave => Status switch
    {
        SettingsLoadStatus.NewerVersion or SettingsLoadStatus.Unreadable => false,
        SettingsLoadStatus.Corrupt => BackupPath is not null,
        _ => true,
    };
}

public sealed class SettingsStore(string filePath)
{
    public const string FileName = "settings.json";

    private static readonly JsonDocumentOptions _documentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public string FilePath { get; } = filePath;

    public static SettingsStore InDirectory(string dataDirectory) => new(Path.Combine(dataDirectory, FileName));

    public SettingsLoadResult Load()
    {
        if (!File.Exists(FilePath))
        {
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.NotFound);
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Unreadable, Error: ex);
        }

        try
        {
            if (bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            {
                bytes = bytes[3..];
            }

            var version = ReadSchemaVersion(bytes);
            AppSettings? settings;
            if (version >= AppSettings.CurrentSchemaVersion)
            {
                settings = JsonSerializer.Deserialize(bytes, SettingsJsonContext.Default.AppSettings);
            }
            else
            {
                var root = JsonNode.Parse(bytes, documentOptions: _documentOptions) as JsonObject
                    ?? throw new JsonException("The settings file does not contain a JSON object.");
                settings = SettingsMigrations.Apply(root, version).Deserialize(SettingsJsonContext.Default.AppSettings);
            }

            settings ??= new AppSettings();
            var status = version > AppSettings.CurrentSchemaVersion ? SettingsLoadStatus.NewerVersion : SettingsLoadStatus.Loaded;
            return new SettingsLoadResult(settings.Normalize(), status, version < AppSettings.CurrentSchemaVersion ? version : null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            string? backup = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            try
            {
                File.Move(FilePath, backup, overwrite: true);
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
                backup = null;
            }

            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Corrupt, BackupPath: backup, Error: ex);
        }
    }

    public RenderingMode PeekRendering() => Peek().Rendering;

    public SettingsPeek Peek()
    {
        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SettingsPeek.Defaults;
        }

        var values = Scan(text, ["advanced.rendering", "general.startFolders", "general.startLeft", "general.startRight"]);
        var rendering = values.TryGetValue("advanced.rendering", out var r) && r.Equals("Software", StringComparison.OrdinalIgnoreCase) ? RenderingMode.Software : RenderingMode.Gpu;
        var folders = values.TryGetValue("general.startFolders", out var f) && Enum.TryParse<StartFolders>(f, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : StartFolders.WhereYouLeft;
        return new SettingsPeek(rendering, folders, values.GetValueOrDefault("general.startLeft", ""), values.GetValueOrDefault("general.startRight", ""));
    }

    private static Dictionary<string, string> Scan(string text, string[] wanted)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var s = text.AsSpan();
        var depth = 0;
        string? section = null;
        for (var i = 0; i < s.Length && found.Count < wanted.Length; i++)
        {
            switch (s[i])
            {
                case '{' or '[':
                    depth++;
                    break;
                case '}' or ']':
                    depth--;
                    section = depth >= 2 ? section : null;
                    break;
                case '/' when i + 1 < s.Length && s[i + 1] == '/':
                    i = s[i..].IndexOf('\n') is var nl and >= 0 ? i + nl : s.Length;
                    break;
                case '/' when i + 1 < s.Length && s[i + 1] == '*':
                    i = s[(i + 2)..].IndexOf("*/") is var end and >= 0 ? i + 2 + end + 1 : s.Length;
                    break;
                case '"':
                    var str = ReadString(s, ref i);
                    var isKey = NextToken(s, i + 1) is var colon && colon < s.Length && s[colon] == ':';
                    if (!isKey)
                    {
                        break;
                    }

                    if (depth == 1)
                    {
                        section = str.ToString();
                    }
                    else if (depth == 2 && section is not null && Array.IndexOf(wanted, section + "." + str.ToString()) is var w and >= 0)
                    {
                        var value = NextToken(s, colon + 1);
                        if (value < s.Length && s[value] == '"')
                        {
                            found[wanted[w]] = Decode(ReadString(s, ref value));
                            i = value;
                        }
                    }

                    break;
            }
        }

        return found;

        static ReadOnlySpan<char> ReadString(ReadOnlySpan<char> s, ref int i)
        {
            var start = ++i;
            while (i < s.Length && s[i] != '"')
            {
                i += s[i] == '\\' ? 2 : 1;
            }

            return s[start..Math.Min(i, s.Length)];
        }

        static int NextToken(ReadOnlySpan<char> s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }

            return i;
        }

        static string Decode(ReadOnlySpan<char> raw)
        {
            if (raw.IndexOf('\\') < 0)
            {
                return raw.ToString();
            }

            var sb = new System.Text.StringBuilder(raw.Length);
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] != '\\' || i + 1 >= raw.Length)
                {
                    sb.Append(raw[i]);
                    continue;
                }

                var e = raw[++i];
                if (e == 'u' && i + 4 < raw.Length && ushort.TryParse(raw.Slice(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                {
                    sb.Append((char)code);
                    i += 4;
                }
                else
                {
                    sb.Append(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', 'b' => '\b', 'f' => '\f', _ => e });
                }
            }

            return sb.ToString();
        }
    }

    private static int ReadSchemaVersion(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("The settings file does not contain a JSON object.");
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isVersion = reader.ValueTextEquals("schemaVersion"u8);
            reader.Read();
            if (isVersion)
            {
                return reader.GetInt32();
            }

            reader.Skip();
        }

        return 1;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
        File.Move(temp, FilePath, overwrite: true);
    }
}

internal static class SettingsMigrations
{
    private static readonly Dictionary<int, Func<JsonObject, JsonObject>> _steps = new()
    {
        [2] = AddNotebookMask,
        [3] = DropColorsBeforeThemes,
        [4] = AddTreeMasks,
        [5] = AddHtmlMask,
        [6] = AddSlnx,
        [7] = PanelFont12,
    };

    private static JsonObject PanelFont12(JsonObject root)
    {
        if (root["appearance"] is JsonObject appearance && appearance["panelFont"] is JsonObject font
            && font["size"] is JsonValue size && size.TryGetValue<double>(out var value) && value == 13)
        {
            font["size"] = FontSetting.DefaultPanelSize;
        }

        return root;
    }

    private static JsonObject DropColorsBeforeThemes(JsonObject root)
    {
        if (root["appearance"] is JsonObject appearance)
        {
            foreach (var key in (ReadOnlySpan<string>)["theme", "accentMode", "customAccent", "colorScheme", "lightColorOverrides", "darkColorOverrides", "codeScheme", "codeBackground", "codeImportedFrom"])
            {
                appearance.Remove(key);
            }
        }

        return root;
    }

    private static JsonObject AddNotebookMask(JsonObject root)
    {
        AddMask(root, "*.ipynb", ViewerKind.Notebook, ".ipynb");
        return root;
    }

    private static JsonObject AddTreeMasks(JsonObject root)
    {
        AddMask(root, "*.json;*.jsonc;*.jsonl;*.geojson", ViewerKind.Tree, ".json");
        AddMask(root, "*.yaml;*.yml", ViewerKind.Tree, ".yaml", ".yml");
        AddMask(root, "*.xml;*.xaml;*.csproj;*.props;*.config;*.resx", ViewerKind.Tree, ".xml");
        return root;
    }

    private static JsonObject AddHtmlMask(JsonObject root)
    {
        AddMask(root, "*.html;*.htm;*.xhtml;*.mht;*.mhtml", ViewerKind.Html, ".html", ".htm");
        return root;
    }

    private static JsonObject AddSlnx(JsonObject root)
    {
        foreach (var table in (ReadOnlySpan<string>)["viewers", "editors"])
        {
            if (root[table] is not JsonObject settings || settings["masks"] is not JsonArray masks)
            {
                continue;
            }

            var rows = masks.OfType<JsonObject>().Where(r => r["mask"]?.GetValueKind() == JsonValueKind.String).ToList();
            if (rows.Any(r => r["mask"]!.GetValue<string>().Contains(".slnx", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var row in rows)
            {
                var parts = row["mask"]!.GetValue<string>().Split(';').ToList();
                var at = parts.FindIndex(p => p.Trim().Equals("*.csproj", StringComparison.OrdinalIgnoreCase));
                if (at >= 0)
                {
                    parts.Insert(at + 1, "*.slnx");
                    row["mask"] = string.Join(';', parts);
                    break;
                }
            }
        }

        return root;
    }

    private static void AddMask(JsonObject root, string mask, ViewerKind view, params string[] names)
    {
        if (root["viewers"] is not JsonObject viewers || viewers["masks"] is not JsonArray masks)
        {
            return;
        }

        var rows = masks.OfType<JsonObject>().ToList();
        if (rows.Any(r => r["mask"]?.GetValueKind() == JsonValueKind.String && r["mask"]!.GetValue<string>() is var m && names.Any(n => m.Contains(n, StringComparison.OrdinalIgnoreCase))))
        {
            return;
        }

        var row = new JsonObject { ["mask"] = mask, ["view"] = view.ToString(), ["alternate"] = nameof(ViewerKind.Text) };
        var catchAll = rows.FindIndex(r => r["mask"]?.GetValueKind() == JsonValueKind.String && r["mask"]!.GetValue<string>().Trim() is "*.*" or "*");
        if (catchAll >= 0)
        {
            masks.Insert(masks.IndexOf(rows[catchAll]), row);
        }
        else
        {
            masks.Add((JsonNode)row);
        }
    }

    public static JsonObject Apply(JsonObject root, int fromVersion)
    {
        for (var v = fromVersion; v < AppSettings.CurrentSchemaVersion; v++)
        {
            if (_steps.TryGetValue(v, out var step))
            {
                root = step(root);
            }
        }

        return root;
    }
}

public sealed record SettingsPeek(RenderingMode Rendering, StartFolders StartFolders, string StartLeft, string StartRight)
{
    public static SettingsPeek Defaults { get; } = new(RenderingMode.Gpu, StartFolders.WhereYouLeft, "", "");
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
