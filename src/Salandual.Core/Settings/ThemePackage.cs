// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ThemePackage.cs

using System.IO.Compression;
using System.Text.Json;
using Salandual.Core.Resources;

namespace Salandual.Core.Settings;

public static class ThemePackage
{
    public const string Extension = ".saldtheme";
    public const string Format = "salandual-theme";

    public const string ColorFileFormat = "salandual-colors";

    public const long MaxFileBytes = 80L * 1024 * 1024;

    public const long MaxJsonBytes = 1024 * 1024;

    public const int MaxEntries = 512;

    private const string ThemeEntry = "theme.json";

    private static readonly JsonDocumentOptions _json = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 16 };

    public static string FileName(ColorTheme theme)
    {
        var name = string.Concat(theme.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim().TrimEnd('.');
        return (name.Length == 0 ? "theme" : name) + Extension;
    }

    public static void Export(ColorTheme theme, string? imagePath, string destination)
    {
        var image = imagePath is not null && File.Exists(imagePath) ? ThemeFiles.SafeFileName(Path.GetFileName(imagePath)) : null;
        var temporary = destination + ".part";
        try
        {
            using (var file = File.Create(temporary))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (var entry = zip.CreateEntry(ThemeEntry, CompressionLevel.Optimal).Open())
                {
                    entry.Write(Json(theme, image));
                }

                if (image is not null)
                {
                    using var entry = zip.CreateEntry(image, CompressionLevel.NoCompression).Open();
                    using var source = File.OpenRead(imagePath!);
                    source.CopyTo(entry);
                }
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static byte[] Json(ColorTheme theme, string? image)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", 1);
            w.WriteString("name", theme.Name);
            w.WriteBoolean("dark", theme.Dark);
            w.WriteStartObject("colors");
            foreach (var (token, _) in ColorTokens.All)
            {
                if (theme.Colors.TryGetValue(token, out var hex))
                {
                    w.WriteString(token, hex);
                }
            }

            w.WriteEndObject();
            w.WriteString("accent", theme.Accent.ToString());
            if (theme.CustomAccent is { } custom)
            {
                w.WriteString("customAccent", custom);
            }

            w.WriteString("codeBackground", theme.CodeBackground.ToString());
            if (image is not null)
            {
                var b = theme.Background;
                w.WriteStartObject("background");
                w.WriteString("file", image);
                w.WriteString("placement", b.Placement.ToString());
                w.WriteString("fit", b.Fit.ToString());
                w.WriteString("position", b.Position.ToString());
                w.WriteNumber("opacity", Math.Round(b.Opacity, 3));
                w.WriteBoolean("blur", b.Blur);
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static IReadOnlyList<ColorTheme> Import(string path, AppearanceSettings appearance, string dataDirectory, DateTime now)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException(Strings.ThemeFile_NotFound, path);
        }

        if (info.Length > MaxFileBytes)
        {
            throw new InvalidDataException(Loc.Format(Strings.ThemeFile_TooLarge, MaxFileBytes / (1024 * 1024)));
        }

        var from = info.Name;
        Span<byte> magic = stackalloc byte[4];
        using (var probe = File.OpenRead(path))
        {
            if (probe.ReadAtLeast(magic, 4, throwOnEndOfStream: false) == 4 && magic is [(byte)'P', (byte)'K', 3, 4])
            {
                return [FromPackage(probe, appearance, dataDirectory, now, from)];
            }
        }

        if (info.Extension.Equals(Extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Strings.ThemeFile_Damaged);
        }

        if (info.Length > VsCodeThemes.MaxFileBytes)
        {
            throw new InvalidDataException(Strings.ThemeFile_NotATheme);
        }

        string? format;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), _json);
            format = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String
                ? f.GetString()
                : null;
            if (format == ColorFileFormat)
            {
                return FromColorFile(doc.RootElement, appearance, now, Path.GetFileNameWithoutExtension(path), from);
            }
        }
        catch (JsonException)
        {
            throw new InvalidDataException(Strings.ThemeFile_NotATheme);
        }

        if (format == Format)
        {
            throw new InvalidDataException(Strings.ThemeFile_NotATheme);
        }

        var theme = VsCodeThemes.ToTheme(path, out var error) ?? throw new InvalidDataException(error ?? Strings.ThemeFile_NotATheme);
        return [Yours(theme, appearance, now, from)];
    }

    public static ColorTheme FromVsCode(VsCodeTheme vs, AppearanceSettings appearance, DateTime now)
    {
        var theme = VsCodeThemes.ToTheme(vs.Path, out var error, vs.UiTheme, vs.Label) ?? throw new InvalidDataException(error ?? Strings.ThemeFile_NotATheme);
        return Yours(theme, appearance, now, vs.Label);
    }

    public static ColorTheme CopyFromVsCode(VsCodeTheme vs, DateTime now)
    {
        var theme = VsCodeThemes.ToTheme(vs.Path, out var error, vs.UiTheme, vs.Label) ?? throw new InvalidDataException(error ?? Strings.ThemeFile_NotATheme);
        return theme with { Id = vs.Id, Origin = ThemeOrigin.VsCode, CopiedFrom = vs.Label, CopiedOn = now, SourcePath = vs.Path, Original = new(theme.Colors, StringComparer.Ordinal) };
    }

    public static ColorTheme UpdateFromVsCode(ColorTheme copy, DateTime now)
    {
        if (copy.SourcePath is not { } path || !File.Exists(path))
        {
            throw new InvalidDataException(Strings.ThemeFile_GoneFromVsCode);
        }

        var theme = VsCodeThemes.ToTheme(path, out var error, copy.Dark ? "vs-dark" : "vs", copy.Name) ?? throw new InvalidDataException(error ?? Strings.ThemeFile_NotATheme);
        return copy with { Colors = theme.Colors, Original = new(theme.Colors, StringComparer.Ordinal), CopiedOn = now };
    }

    private static ColorTheme FromPackage(Stream file, AppearanceSettings appearance, string dataDirectory, DateTime now, string from)
    {
        file.Position = 0;
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException(Strings.ThemeFile_Damaged);
        }

        using (zip)
        {
            if (zip.Entries.Count > MaxEntries)
            {
                throw new InvalidDataException(Strings.ThemeFile_Damaged);
            }

            var entry = zip.Entries.FirstOrDefault(e => e.FullName == ThemeEntry) ?? throw new InvalidDataException(Strings.ThemeFile_NotATheme);
            using var buffer = new MemoryStream();
            using (var json = entry.Open())
            {
                CopyLimited(json, buffer, MaxJsonBytes, Strings.ThemeFile_Damaged);
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(buffer.ToArray(), _json);
            }
            catch (JsonException)
            {
                throw new InvalidDataException(Strings.ThemeFile_Damaged);
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || Text(root, "format") != Format)
                {
                    throw new InvalidDataException(Strings.ThemeFile_NotATheme);
                }

                if (root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var v) && v > 1)
                {
                    throw new InvalidDataException(Strings.ThemeFile_Newer);
                }

                var theme = Yours(new ColorTheme
                {
                    Name = Text(root, "name") ?? "",
                    Dark = root.TryGetProperty("dark", out var dark) && dark.ValueKind == JsonValueKind.True,
                    Colors = root.TryGetProperty("colors", out var colors) && colors.ValueKind == JsonValueKind.Object ? ReadColors(colors) : [],
                    Accent = Enum(root, "accent", ThemeAccent.Theme),
                    CustomAccent = Text(root, "customAccent"),
                    CodeBackground = Enum(root, "codeBackground", CodeBackground.Panels),
                }, appearance, now, from);

                if (root.TryGetProperty("background", out var bg) && bg.ValueKind == JsonValueKind.Object
                    && ThemeFiles.SafeFileName(Text(bg, "file")) is { } name && ThemeFiles.IsImage(name)
                    && zip.Entries.FirstOrDefault(e => e.FullName == name) is { } image)
                {
                    CopyImage(image, dataDirectory, theme.Id, name);
                    theme = theme with
                    {
                        Background = new BackgroundImageSettings
                        {
                            Path = name,
                            Placement = Enum(bg, "placement", BackgroundPlacement.Panels),
                            Fit = Enum(bg, "fit", BackgroundFit.Fill),
                            Position = Enum(bg, "position", BackgroundPosition.Center),
                            Opacity = bg.TryGetProperty("opacity", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetDouble() : 0.15,
                            Blur = bg.TryGetProperty("blur", out var blur) && blur.ValueKind == JsonValueKind.True,
                        },
                    };
                }

                return theme.Normalize();
            }
        }
    }

    private static void CopyImage(ZipArchiveEntry image, string dataDirectory, string themeId, string name)
    {
        var folder = ThemeFiles.Folder(dataDirectory, themeId) ?? throw new IOException(Strings.ThemeFiles_BadTheme);
        try
        {
            Directory.CreateDirectory(folder);
            using var source = image.Open();
            using var target = File.Create(Path.Combine(folder, name));
            CopyLimited(source, target, ThemeFiles.MaxImageBytes, Loc.Format(Strings.ThemeFiles_TooLarge, ThemeFiles.MaxImageBytes / (1024 * 1024)));
        }
        catch
        {
            ThemeFiles.DeleteFolder(dataDirectory, themeId);
            throw;
        }
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static ColorTheme Yours(ColorTheme theme, AppearanceSettings appearance, DateTime now, string from)
        => theme with
        {
            Id = ThemeEditing.NewId(appearance),
            Name = ThemeEditing.UniqueName(theme.Name.Trim().Length == 0 ? Path.GetFileNameWithoutExtension(from) : theme.Name.Trim(), appearance),
            Origin = ThemeOrigin.Mine,
            Original = new(theme.Colors, StringComparer.Ordinal),
            ImportedFrom = from,
            CopiedFrom = null,
            CopiedOn = now,
            SourcePath = null,
        };

    private static List<ColorTheme> FromColorFile(JsonElement root, AppearanceSettings appearance, DateTime now, string stem, string from)
    {
        var result = new List<ColorTheme>();
        foreach (var (variant, dark, basis, name) in new[]
        {
            ("light", false, ColorTokens.SalandualLight, Strings.ThemeFile_ColorFileLight),
            ("dark", true, ColorTokens.SalandualDark, Strings.ThemeFile_ColorFileDark),
        })
        {
            if (!root.TryGetProperty(variant, out var colors) || colors.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var read = ReadColors(colors);
            if (read.Count == 0)
            {
                continue;
            }

            var all = new Dictionary<string, string>(basis, StringComparer.Ordinal);
            foreach (var (token, hex) in read)
            {
                all[token] = hex;
            }

            var theme = Yours(new ColorTheme { Name = Loc.Format(name, stem), Dark = dark, Colors = all }, appearance, now, from);
            result.Add(theme);
            appearance = ThemeEditing.Add(appearance, theme);
        }

        return result.Count > 0 ? result : throw new InvalidDataException(Strings.ThemeFile_NoColors);
    }

    private static Dictionary<string, string> ReadColors(JsonElement colors)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in colors.EnumerateObject())
        {
            if (ColorTokens.IsKnown(p.Name) && p.Value.ValueKind == JsonValueKind.String && ColorMath.TryParseHex(p.Value.GetString(), out var c))
            {
                result[p.Name] = ColorMath.ToHex(c);
            }
        }

        return result;
    }

    private static T Enum<T>(JsonElement root, string name, T fallback)
        where T : struct, System.Enum
        => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && System.Enum.TryParse<T>(p.GetString(), ignoreCase: true, out var v) && System.Enum.IsDefined(v) ? v : fallback;

    private static void CopyLimited(Stream from, Stream to, long limit, string tooLarge)
    {
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = from.Read(buffer)) > 0)
        {
            total += n;
            if (total > limit)
            {
                throw new InvalidDataException(tooLarge);
            }

            to.Write(buffer, 0, n);
        }
    }
}
