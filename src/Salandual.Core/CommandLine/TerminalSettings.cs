// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/CommandLine/TerminalSettings.cs

using System.Text.Json;

namespace Salandual.Core.CommandLine;

public static class TerminalSettings
{
    public const long MaxFileSize = 4 * 1024 * 1024;

    private static readonly JsonDocumentOptions _options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static (IReadOnlyList<TerminalProfile> Profiles, string? Default) Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, _options);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ([], null);
            }

            var list = root.TryGetProperty("profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Object && profiles.TryGetProperty("list", out var inner) ? inner : profiles;
            var result = new List<TerminalProfile>();
            if (list.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in list.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.Object
                        && Text(p, "guid") is { } id && Text(p, "name") is { } name
                        && !(p.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True))
                    {
                        result.Add(new TerminalProfile(id, name));
                    }
                }
            }

            var defaultId = Text(root, "defaultProfile");
            return (result, defaultId);
        }
        catch (JsonException)
        {
            return ([], null);
        }
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}
