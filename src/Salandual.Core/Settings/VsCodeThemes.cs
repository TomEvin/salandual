// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/VsCodeThemes.cs

using System.Text.Json;

namespace Salandual.Core.Settings;

public sealed record VsCodeTheme(string Label, string UiTheme, string Path, string Source)
{
    public bool Dark => UiTheme is "vs-dark" or "hc-black";

    public string Id => VsCodeThemes.IdFor(Label);
}

public static class VsCodeThemes
{
    public const long MaxFileBytes = 4L * 1024 * 1024;

    private static readonly (string Token, string[] Keys)[] _workbench =
    [
        (ColorTokens.PanelBackground, ["editor.background"]),
        (ColorTokens.WindowBackground, ["sideBar.background", "activityBar.background"]),
        (ColorTokens.Text, ["editor.foreground", "foreground"]),
        (ColorTokens.SecondaryText, ["descriptionForeground"]),
        (ColorTokens.ColumnHeader, ["editorGroupHeader.tabsBackground", "sideBarSectionHeader.background"]),
        (ColorTokens.InformationLine, ["statusBar.background"]),
        (ColorTokens.InformationLineText, ["statusBar.foreground"]),
        (ColorTokens.SelectionBackground, ["list.inactiveSelectionBackground"]),
        (ColorTokens.SelectedText, ["list.highlightForeground"]),
        (ColorTokens.FocusOutline, ["focusBorder"]),
        (ColorTokens.Hover, ["list.hoverBackground"]),
        (ColorTokens.Border, ["panel.border", "editorGroup.border", "contrastBorder"]),
        (ColorTokens.Accent, ["button.background", "focusBorder"]),
        (ColorTokens.Danger, ["errorForeground", "editorError.foreground"]),
        (ColorTokens.Warning, ["editorWarning.foreground"]),
        (ColorTokens.Success, ["gitDecoration.addedResourceForeground", "terminal.ansiGreen"]),
        (ColorTokens.ViewerLineNumbers, ["editorLineNumber.foreground"]),
        (ColorTokens.ViewerOffsets, ["editorLineNumber.foreground"]),
        (ColorTokens.ViewerCaret, ["editorCursor.foreground"]),
        (ColorTokens.ViewerMatch, ["editor.findMatchHighlightBackground"]),
        (ColorTokens.ViewerCurrentMatch, ["editor.findMatchBackground"]),
        (ColorTokens.SyntaxBackground, ["editor.background"]),
        (ColorTokens.SyntaxText, ["editor.foreground", "foreground"]),
    ];

    private static readonly Dictionary<string, (string? Dark, string? Light, string? HcDark, string? HcLight)> _defaults = new(StringComparer.Ordinal)
    {
        ["foreground"] = ("#CCCCCC", "#616161", "#FFFFFF", "#292929"),
        ["descriptionForeground"] = ("#CCCCCCB3", "#717171", "#FFFFFFB3", "#292929B3"),
        ["focusBorder"] = ("#007FD4", "#0090F1", "#F38518", "#006BBD"),
        ["contrastBorder"] = (null, null, "#6FC3DF", "#0F4A85"),
        ["errorForeground"] = ("#F48771", "#A1260D", "#F48771", "#B5200D"),
        ["editor.background"] = ("#1E1E1E", "#FFFFFF", "#000000", "#FFFFFF"),
        ["editor.foreground"] = ("#BBBBBB", "#333333", "#FFFFFF", "#292929"),
        ["editorLineNumber.foreground"] = ("#858585", "#237893", "#FFFFFF", "#292929"),
        ["editorCursor.foreground"] = ("#AEAFAD", "#000000", "#FFFFFF", "#0F4A85"),
        ["editor.findMatchBackground"] = ("#515C6A", "#A8AC94", null, null),
        ["editor.findMatchHighlightBackground"] = ("#EA5C0055", "#EA5C0055", null, null),
        ["editorWarning.foreground"] = ("#CCA700", "#BF8803", "#FFD370", "#895503"),
        ["editorError.foreground"] = ("#F14C4C", "#E51400", "#F48771", "#B5200D"),
        ["list.hoverBackground"] = ("#2A2D2E", "#F0F0F0", null, null),
        ["list.inactiveSelectionBackground"] = ("#37373D", "#E4E6F1", null, null),
        ["list.highlightForeground"] = ("#2AAAFF", "#0066BF", "#F38518", "#0F4A85"),
        ["sideBar.background"] = ("#252526", "#F3F3F3", "#000000", "#FFFFFF"),
        ["editorGroupHeader.tabsBackground"] = ("#252526", "#F3F3F3", null, null),
        ["statusBar.background"] = ("#007ACC", "#007ACC", null, null),
        ["statusBar.foreground"] = ("#FFFFFF", "#FFFFFF", "#FFFFFF", "#292929"),
        ["panel.border"] = ("#80808059", "#80808059", "#6FC3DF", "#0F4A85"),
        ["button.background"] = ("#0E639C", "#007ACC", null, "#0F4A85"),
        ["gitDecoration.addedResourceForeground"] = ("#81B88B", "#587C0C", "#A1E3AD", "#374E06"),
    };
    private static readonly (string Token, string[] Scopes)[] _probes =
    [
        (ColorTokens.SyntaxKey, ["source.json", "meta.structure.dictionary.json", "support.type.property-name.json"]),
        (ColorTokens.SyntaxTag, ["text.html.basic", "meta.tag.structure.any.html", "entity.name.tag.html"]),
        (ColorTokens.SyntaxString, ["source.json", "meta.structure.dictionary.value.json", "string.quoted.double.json"]),
        (ColorTokens.SyntaxNumber, ["source.json", "meta.structure.dictionary.value.json", "constant.numeric.json"]),
        (ColorTokens.SyntaxKeyword, ["source.json", "meta.structure.dictionary.value.json", "constant.language.json"]),
        (ColorTokens.SyntaxPunctuation, ["text.html.basic", "meta.tag.structure.any.html", "punctuation.definition.tag.begin.html"]),
        (ColorTokens.SyntaxComment, ["source.js", "comment.line.double-slash.js"]),
        (ColorTokens.SyntaxAttribute, ["text.html.basic", "meta.tag.structure.any.html", "entity.other.attribute-name.html"]),
        (ColorTokens.SyntaxHeading, ["text.html.markdown", "markup.heading.markdown", "entity.name.section.markdown"]),
        (ColorTokens.SyntaxLink, ["text.html.markdown", "meta.link.inline.markdown", "markup.underline.link.markdown"]),
        (ColorTokens.SyntaxFunction, ["source.js", "meta.function-call.js", "entity.name.function.js"]),
    ];

    private static readonly JsonDocumentOptions _json = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public static IReadOnlyList<VsCodeTheme> Installed(string? localAppData = null, string? userProfile = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var themes = new List<VsCodeTheme>();
        if (AppFolder(localAppData) is { } app)
        {
            foreach (var ext in SafeDirectories(System.IO.Path.Combine(app, "extensions")).Where(d => System.IO.Path.GetFileName(d).StartsWith("theme-", StringComparison.OrdinalIgnoreCase)))
            {
                themes.AddRange(FromExtension(ext, "VS Code"));
            }
        }

        foreach (var ext in SafeDirectories(System.IO.Path.Combine(userProfile, ".vscode", "extensions")))
        {
            var name = System.IO.Path.GetFileName(ext);
            var at = name.LastIndexOf('-');
            themes.AddRange(FromExtension(ext, at > 0 ? name[..at] : name));
        }

        return [.. themes.GroupBy(t => t.Label, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase)];
    }

    public static string IdFor(string label)
    {
        var slug = new string([.. label.ToLowerInvariant().Select(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? c : '-')]);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        var id = "vscode-" + (slug.Length == 0 ? "theme" : slug);
        return id.Length <= 64 ? id : id[..64].TrimEnd('-');
    }

    public static ColorTheme? ToTheme(string path, out string? error, string? uiTheme = null, string? label = null)
    {
        error = null;
        try
        {
            var rules = new List<(string Selector, string Color)>();
            var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var (name, type) = Load(path, rules, colors, depth: 0);
            if (rules.Count == 0 && colors.Count == 0)
            {
                error = Resources.Strings.Colors_NotAVsCodeTheme;
                return null;
            }

            var kind = uiTheme ?? type switch
            {
                "light" => "vs",
                "dark" => "vs-dark",
                "hc" or "hcDark" or "hc-black" => "hc-black",
                "hcLight" or "hc-light" => "hc-light",
                _ => colors.TryGetValue("editor.background", out var bg) && Parse(bg) is { } b && ColorMath.Luminance(b.Color) > 0.5 ? "vs" : "vs-dark",
            };
            return new ColorTheme
            {
                Name = label ?? name ?? System.IO.Path.GetFileNameWithoutExtension(path),
                Dark = kind is "vs-dark" or "hc-black",
                Colors = Colors(kind, colors, rules),
                Origin = ThemeOrigin.Mine,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            error = e.Message;
            return null;
        }
    }

    private static Dictionary<string, string> Colors(string kind, Dictionary<string, string> workbench, List<(string Selector, string Color)> rules)
    {
        string? Value(string key)
        {
            if (workbench.TryGetValue(key, out var v))
            {
                return v;
            }

            if (!_defaults.TryGetValue(key, out var d))
            {
                return null;
            }

            return kind switch { "vs" => d.Light, "hc-black" => d.HcDark, "hc-light" => d.HcLight, _ => d.Dark };
        }

        var under = Parse(Value("editor.background") ?? "")?.Color ?? (kind is "vs" or "hc-light" ? new Rgb(255, 255, 255) : new Rgb(30, 30, 30));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (token, keys) in _workbench)
        {
            foreach (var key in keys)
            {
                if (Value(key) is { } value && Parse(value) is { Alpha: > 0 } c)
                {
                    result[token] = ColorMath.ToHex(ColorMath.Mix(under, c.Color, c.Alpha));
                    break;
                }
            }
        }

        foreach (var (fore, back, ratio) in new[]
        {
            (ColorTokens.Text, ColorTokens.PanelBackground, 4.5),
            (ColorTokens.SelectedText, ColorTokens.SelectionBackground, 3.0),
            (ColorTokens.InformationLineText, ColorTokens.InformationLine, 3.0),
        })
        {
            if (ColorMath.TryParseHex(result.GetValueOrDefault(fore) ?? result.GetValueOrDefault(ColorTokens.Text), out var fc)
                && ColorMath.TryParseHex(result.GetValueOrDefault(back) ?? result.GetValueOrDefault(ColorTokens.PanelBackground), out var bc)
                && ColorMath.Contrast(fc, bc) < ratio)
            {
                var toward = ColorMath.Luminance(bc) < 0.2 ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);
                var t = 0.0;
                while (t < 1 && ColorMath.Contrast(ColorMath.Mix(fc, toward, t), bc) < ratio)
                {
                    t += 0.05;
                }

                result[fore] = ColorMath.ToHex(ColorMath.Mix(fc, toward, t));
            }
        }

        var text = result.GetValueOrDefault(ColorTokens.SyntaxText);
        foreach (var (token, scopes) in _probes)
        {
            if ((Match(rules, scopes) ?? text) is { } c)
            {
                result[token] = c;
            }
        }

        return result;
    }

    private static (string? Name, string? Type) Load(string path, List<(string, string)> rules, Dictionary<string, string> colors, int depth)
    {
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new IOException(Resources.Strings.Colors_NotAVsCodeTheme);
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path), _json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        string? Text(string property) => root.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        var (name, type) = (Text("name"), Text("type"));

        if (depth < 5 && root.TryGetProperty("include", out var include) && include.ValueKind == JsonValueKind.String
            && System.IO.Path.GetDirectoryName(path) is { } folder)
        {
            var included = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, include.GetString()!));
            if (string.Equals(System.IO.Path.GetDirectoryName(included), System.IO.Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)
                && File.Exists(included) && included.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                type ??= Load(included, rules, colors, depth + 1).Type;
            }
        }

        if (root.TryGetProperty("colors", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in c.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    colors[p.Name] = p.Value.GetString()!;
                }
            }
        }

        if (root.TryGetProperty("tokenColors", out var tokens) && tokens.ValueKind == JsonValueKind.Array)
        {
            foreach (var rule in tokens.EnumerateArray())
            {
                if (rule.ValueKind != JsonValueKind.Object
                    || !rule.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object
                    || !settings.TryGetProperty("foreground", out var fg) || fg.ValueKind != JsonValueKind.String
                    || Hex(fg.GetString()!) is not { } color)
                {
                    continue;
                }

                if (!rule.TryGetProperty("scope", out var scope))
                {
                    continue;
                }

                IEnumerable<string> selectors = scope.ValueKind switch
                {
                    JsonValueKind.String => scope.GetString()!.Split(','),
                    JsonValueKind.Array => scope.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!),
                    _ => [],
                };
                foreach (var selector in selectors)
                {
                    if (selector.Trim() is { Length: > 0 } s && !s.Contains(" - ", StringComparison.Ordinal))
                    {
                        rules.Add((s, color));
                    }
                }
            }
        }

        return (name, type);
    }

    private static string? Match(List<(string Selector, string Color)> rules, string[] scopes)
    {
        string? best = null;
        (int Depth, int Length, int Parts) bestScore = (-1, 0, 0);
        foreach (var (selector, color) in rules)
        {
            var parts = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p != ">").ToArray();
            if (parts.Length == 0)
            {
                continue;
            }

            var depth = scopes.Length - 1;
            while (depth >= 0 && !Prefix(parts[^1], scopes[depth]))
            {
                depth--;
            }

            if (depth < 0)
            {
                continue;
            }

            var at = depth - 1;
            var ok = true;
            for (var k = parts.Length - 2; k >= 0 && ok; k--)
            {
                while (at >= 0 && !Prefix(parts[k], scopes[at]))
                {
                    at--;
                }

                ok = at >= 0;
                at--;
            }

            var score = (depth, parts[^1].Length, parts.Length);
            if (ok && score.CompareTo(bestScore) >= 0)
            {
                best = color;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool Prefix(string selector, string scope)
        => scope.StartsWith(selector, StringComparison.Ordinal) && (scope.Length == selector.Length || scope[selector.Length] == '.');

    private static (Rgb Color, double Alpha)? Parse(string value)
    {
        var v = value.Trim();
        if (v.Length is 4 or 5 && v[0] == '#')
        {
            v = string.Concat(v[1..].Select(ch => new string(ch, 2))).Insert(0, "#");
        }

        var alpha = 1.0;
        if (v.Length == 9 && v[0] == '#')
        {
            if (!int.TryParse(v[7..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var a))
            {
                return null;
            }

            alpha = a / 255.0;
            v = v[..7];
        }

        return ColorMath.TryParseHex(v, out var c) ? (c, alpha) : null;
    }

    private static string? Hex(string value)
    {
        var v = value.Trim();
        if (v.Length == 4 && v[0] == '#')
        {
            v = "#" + v[1] + v[1] + v[2] + v[2] + v[3] + v[3];
        }
        else if (v.Length == 9 && v[0] == '#')
        {
            v = v[..7];
        }

        return ColorMath.TryParseHex(v, out var c) ? ColorMath.ToHex(c) : null;
    }

    private static IEnumerable<VsCodeTheme> FromExtension(string folder, string source)
    {
        var package = System.IO.Path.Combine(folder, "package.json");
        if (!File.Exists(package) || File.Exists(System.IO.Path.Combine(folder, ".obsolete")))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(package), _json);
            if (!doc.RootElement.TryGetProperty("contributes", out var contributes) || contributes.ValueKind != JsonValueKind.Object
                || !contributes.TryGetProperty("themes", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var nls = Nls(folder);
            var result = new List<VsCodeTheme>();
            foreach (var t in list.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object || !t.TryGetProperty("path", out var p) || p.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, p.GetString()!));
                if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                {
                    continue;
                }

                var label = t.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : System.IO.Path.GetFileNameWithoutExtension(file);
                if (label.StartsWith('%') && label.EndsWith('%') && nls.TryGetValue(label.Trim('%'), out var named))
                {
                    label = named;
                }

                var ui = t.TryGetProperty("uiTheme", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : "vs-dark";
                result.Add(new VsCodeTheme(label, ui is "vs" or "vs-dark" or "hc-black" or "hc-light" ? ui : "vs-dark", file, source));
            }

            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static Dictionary<string, string> Nls(string folder)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = System.IO.Path.Combine(folder, "package.nls.json");
        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), _json);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    result[p.Name] = p.Value.GetString()!;
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
        }

        return result;
    }

    internal static string? InstalledVersion(string? localAppData = null)
        => AppFolder(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) is { } app ? Version(app).ToString() : null;

    private static string? AppFolder(string localAppData)
    {
        var root = System.IO.Path.Combine(localAppData, "Programs", "Microsoft VS Code");
        var candidates = SafeDirectories(root).Select(d => System.IO.Path.Combine(d, "resources", "app")).Append(System.IO.Path.Combine(root, "resources", "app"))
            .Where(d => File.Exists(System.IO.Path.Combine(d, "package.json")));
        return candidates.OrderByDescending(d => Version(d)).FirstOrDefault();
    }

    private static Version Version(string app)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(app, "package.json")), _json);
            return doc.RootElement.TryGetProperty("version", out var v) && System.Version.TryParse(v.GetString()?.Split('-')[0], out var parsed) ? parsed : new Version(0, 0);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new Version(0, 0);
        }
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
