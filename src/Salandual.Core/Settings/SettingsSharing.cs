// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/SettingsSharing.cs

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Salandual.Core.Settings;

public sealed record SharingSettings
{
    public static readonly SettingsPart[] Shareable =
    [
        SettingsPart.Appearance, SettingsPart.Themes, SettingsPart.Layout, SettingsPart.Panels, SettingsPart.Keyboard, SettingsPart.HotPaths,
        SettingsPart.Operations, SettingsPart.Confirmations, SettingsPart.Viewers, SettingsPart.Editors, SettingsPart.Compare, SettingsPart.General,
        SettingsPart.History, SettingsPart.Programs,
    ];

    public string Folder { get; set; } = "";

    public List<SettingsPart> Parts { get; set; } = [.. Shareable.Except([SettingsPart.History, SettingsPart.Programs])];

    public bool AskFirst { get; set; }

    public bool IsOn => !string.IsNullOrWhiteSpace(Folder);

    internal SharingSettings Normalize() => this with
    {
        Folder = (Folder ?? "").Trim(),
        Parts = [.. (Parts ?? []).Where(p => Shareable.Contains(p)).Distinct().Order()],
    };
}

public sealed record SharedCopy(JsonObject Settings, string Pc, DateTime? WrittenUtc);

public sealed record SharingMerge(AppSettings Local, IReadOnlyList<SettingChange> Changes, IReadOnlyList<string> Paths, int BothChanged, int KeptMine)
{
    public bool HasChanges => Paths.Count > 0;
}

public static class SettingsSharing
{
    public const string FileName = "Salandual settings" + SettingsTransfer.Extension;

    public const string BaseFileName = "shared-base.json";

    public static string Write(JsonObject shared, string pc, DateTime nowUtc, string writtenBy)
    {
        var file = new JsonObject
        {
            ["format"] = SettingsTransfer.Format,
            ["exported"] = nowUtc.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            ["exportedBy"] = writtenBy,
            ["pc"] = pc,
            ["written"] = nowUtc.ToString("o", CultureInfo.InvariantCulture),
        };
        foreach (var (key, value) in shared)
        {
            file[key] = value?.DeepClone();
        }

        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static JsonObject Only(AppSettings settings, IReadOnlySet<SettingsPart> parts)
    {
        var all = SettingsTransfer.ToJson(settings);
        var result = new JsonObject { ["schemaVersion"] = AppSettings.CurrentSchemaVersion };
        foreach (var (path, part) in SettingsTransfer.Leaves(all, all))
        {
            if (part is { } p && parts.Contains(p))
            {
                SettingsTransfer.Set(result, path, SettingsTransfer.Get(all, path));
            }
        }

        if (!parts.Contains(SettingsPart.Programs) && SettingsTransfer.Get(result, "editors.masks") is { } masks)
        {
            SettingsTransfer.Set(result, "editors.masks", SettingsTransfer.WithoutPrograms(masks));
        }

        return result;
    }

    public static SharedCopy Read(string text)
    {
        if (text.Length > SettingsTransfer.MaxFileSize)
        {
            throw new InvalidDataException(Resources.Strings.Transfer_TooBig);
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: SettingsTransfer.DocumentOptions) as JsonObject ?? throw new InvalidDataException(Resources.Strings.Transfer_NotSettings);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Resources.Strings.Transfer_NotSettings, ex);
        }

        if (root["format"] is not JsonValue f || !f.TryGetValue<string>(out var format) || format != SettingsTransfer.Format)
        {
            throw new InvalidDataException(Resources.Strings.Transfer_NotSettings);
        }

        var pc = root["pc"] is JsonValue p && p.TryGetValue<string>(out var name) ? name : "";
        DateTime? written = root["written"] is JsonValue w && w.TryGetValue<string>(out var when)
            && DateTime.TryParse(when, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d.ToUniversalTime() : null;
        foreach (var key in (ReadOnlySpan<string>)["format", "exported", "exportedBy", "pc", "written"])
        {
            root.Remove(key);
        }

        var version = root["schemaVersion"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 1;
        JsonObject present, normal;
        try
        {
            present = SettingsMigrations.Apply(root, version);
            normal = SettingsTransfer.ToJson(SettingsTransfer.ToSettings((JsonObject)present.DeepClone()));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            throw new InvalidDataException(Resources.Strings.Transfer_NotSettings, ex);
        }

        var settings = new JsonObject { ["schemaVersion"] = AppSettings.CurrentSchemaVersion };
        foreach (var (path, part) in SettingsTransfer.Leaves(normal, normal))
        {
            if (part is not null && SettingsTransfer.Has(present, path))
            {
                SettingsTransfer.Set(settings, path, SettingsTransfer.Get(normal, path));
            }
        }

        return new SharedCopy(settings, pc, written);
    }

    public static SharingMerge Merge(AppSettings mine, JsonObject? lastRead, SharedCopy theirs, IReadOnlySet<SettingsPart> parts, DateTime? myLastChangeUtc)
    {
        var my = SettingsTransfer.ToJson(mine);
        var local = (JsonObject)my.DeepClone();
        var paths = new List<string>();
        var (both, keptMine) = (0, 0);
        foreach (var (path, part) in SettingsTransfer.Leaves(my, theirs.Settings))
        {
            if (part is not { } p || !parts.Contains(p) || !SettingsTransfer.Has(theirs.Settings, path))
            {
                continue;
            }

            var (t, m) = (SettingsTransfer.Get(theirs.Settings, path), Comparable(path, SettingsTransfer.Get(my, path), parts));
            if (JsonNode.DeepEquals(t, m))
            {
                continue;
            }

            var known = lastRead is not null && SettingsTransfer.Has(lastRead, path);
            var b = known ? SettingsTransfer.Get(lastRead!, path) : null;
            if (known && JsonNode.DeepEquals(t, b))
            {
                continue;
            }

            if (known && !JsonNode.DeepEquals(m, b))
            {
                both++;
                if (myLastChangeUtc is { } mineAt && (theirs.WrittenUtc is not { } theirsAt || mineAt > theirsAt))
                {
                    keptMine++;
                    continue;
                }
            }

            SettingsTransfer.Set(local, path, t);
            paths.Add(path);
        }

        if (!parts.Contains(SettingsPart.Programs))
        {
            SettingsTransfer.KeepEditorPrograms(local, my);
        }

        var result = SettingsTransfer.ToSettings(local);
        return new SharingMerge(result, SettingsTransfer.Describe(my, SettingsTransfer.ToJson(result)), paths, both, keptMine);
    }

    public static JsonObject FileAfter(AppSettings mine, JsonObject? lastRead, SharedCopy? theirs, IReadOnlySet<SettingsPart> parts)
    {
        var my = Only(mine, parts);
        if (theirs is null || lastRead is null)
        {
            return my;
        }

        var result = (JsonObject)theirs.Settings.DeepClone();
        foreach (var (path, _) in SettingsTransfer.Leaves(my, my))
        {
            var m = SettingsTransfer.Get(my, path);
            if (!SettingsTransfer.Has(lastRead, path) || !JsonNode.DeepEquals(m, SettingsTransfer.Get(lastRead, path)))
            {
                SettingsTransfer.Set(result, path, m);
            }
        }

        return result;
    }

    public static SettingsImport AsImport(AppSettings mine, SharingMerge merge, string file, SharedCopy theirs)
    {
        var my = SettingsTransfer.ToJson(mine);
        var their = SettingsTransfer.ToJson(merge.Local);
        return new SettingsImport(file, theirs.WrittenUtc?.ToLocalTime(), theirs.Pc, my, their, merge.Changes, merge.Changes.Select(c => c.Part).ToHashSet());
    }

    public static AppSettings Restore(AppSettings current, AppSettings before, IEnumerable<string> paths)
    {
        var (now, then) = (SettingsTransfer.ToJson(current), SettingsTransfer.ToJson(before));
        foreach (var path in paths)
        {
            SettingsTransfer.Set(now, path, SettingsTransfer.Get(then, path));
        }

        return SettingsTransfer.ToSettings(now);
    }

    public static bool IsSchedule(SettingChange change)
        => change.Part == SettingsTransfer.PartOf("appearance.schedule") && change.Name.StartsWith(SettingsTransfer.Name("appearance.schedule"), StringComparison.Ordinal);

    public static bool IsHotPathList(SettingChange change)
        => change.Part == SettingsPart.HotPaths && change.Name == SettingsTransfer.Name("hotPaths.paths");

    public static bool Same(JsonObject? a, JsonObject? b) => a is not null && b is not null && JsonNode.DeepEquals(a, b);

    private static JsonNode? Comparable(string path, JsonNode? mine, IReadOnlySet<SettingsPart> parts)
        => path == "editors.masks" && !parts.Contains(SettingsPart.Programs) ? SettingsTransfer.WithoutPrograms(mine) : mine;

    public static JsonObject? LoadBase(string dataDirectory)
    {
        try
        {
            var path = Path.Combine(dataDirectory, BaseFileName);
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void SaveBase(string dataDirectory, JsonObject? shared)
    {
        var path = Path.Combine(dataDirectory, BaseFileName);
        if (shared is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(path, shared.ToJsonString());
    }
}
