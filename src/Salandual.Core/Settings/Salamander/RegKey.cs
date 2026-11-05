// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/Salamander/RegKey.cs

using System.Globalization;
using System.Text;

namespace Salandual.Core.Settings.Salamander;

public sealed record RegValue(string? Text, uint? Number);

public sealed class RegKey
{
    public Dictionary<string, RegValue> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, RegKey> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public RegKey? Key(string path)
    {
        var key = this;
        foreach (var name in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!key.Keys.TryGetValue(name, out var next))
            {
                return null;
            }

            key = next;
        }

        return key;
    }

    public RegKey Make(string path)
    {
        var key = this;
        foreach (var name in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!key.Keys.TryGetValue(name, out var next))
            {
                next = new RegKey();
                key.Keys[name] = next;
            }

            key = next;
        }

        return key;
    }

    public string? Text(string name) => Values.TryGetValue(name, out var v) ? v.Text : null;

    public uint? Number(string name) => Values.TryGetValue(name, out var v) ? v.Number : null;

    public bool? Flag(string name) => Number(name) is { } n ? n != 0 : null;

    public IEnumerable<RegKey> Numbered()
    {
        for (var i = 1; Keys.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out var key); i++)
        {
            yield return key;
        }
    }

    public static bool IsSecret(string name)
        => name.Equals("Password Manager", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("Password", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Anonymous Password", StringComparison.OrdinalIgnoreCase);
}

public static class SalamanderRoots
{
    public static readonly IReadOnlyList<(string Key, string Name)> Known =
    [
        (@"Software\Open Salamander\5.0", "Open Salamander 5.0"),
        (@"Software\Altap\Altap Salamander 4.0", "Altap Salamander 4.0"),
        (@"Software\Altap\Altap Salamander 3.08", "Altap Salamander 3.08"),
        (@"Software\Altap\Altap Salamander 3.07", "Altap Salamander 3.07"),
        (@"Software\Altap\Altap Salamander 3.06", "Altap Salamander 3.06"),
        (@"Software\Altap\Altap Salamander 3.05", "Altap Salamander 3.05"),
        (@"Software\Altap\Altap Salamander 3.04", "Altap Salamander 3.04"),
        (@"Software\Altap\Altap Salamander 3.03", "Altap Salamander 3.03"),
        (@"Software\Altap\Altap Salamander 3.02", "Altap Salamander 3.02"),
        (@"Software\Altap\Altap Salamander 3.01", "Altap Salamander 3.01"),
        (@"Software\Altap\Altap Salamander 3.0", "Altap Salamander 3.0"),
    ];

    public static string? NameOf(string key)
    {
        foreach (var (known, name) in Known)
        {
            if (string.Equals(key.TrimEnd('\\'), known, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }
}

public sealed record SalamanderInstall(string Name, string Key);

public interface ISalamanderSettings
{
    IReadOnlyList<SalamanderInstall> Find();

    RegKey? Read(SalamanderInstall install);
}

public sealed class MemorySalamanderSettings : ISalamanderSettings
{
    public List<(SalamanderInstall Install, RegKey Root)> Installs { get; } = [];

    public IReadOnlyList<SalamanderInstall> Find() => [.. Installs.Select(i => i.Install)];

    public RegKey? Read(SalamanderInstall install) => Installs.FirstOrDefault(i => i.Install == install).Root;
}

public static class RegFile
{
    public const long MaxFileSize = 32 * 1024 * 1024;

    public static (string Name, RegKey Root) Read(string file)
    {
        var info = new FileInfo(file);
        if (info.Length > MaxFileSize)
        {
            throw new InvalidDataException(Resources.Strings.SalImp_TooBig);
        }

        return Parse(Decode(File.ReadAllBytes(file)));
    }

    internal static string Decode(byte[] bytes)
    {
        if (bytes is [0xFF, 0xFE, ..])
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        return Encoding.GetEncoding(codePage is > 0 ? codePage : 1252).GetString(bytes);
    }

    public static (string Name, RegKey Root) Parse(string text)
    {
        using var reader = new StringReader(text);
        var header = reader.ReadLine()?.Trim();
        if (header is not ("REGEDIT4" or "Windows Registry Editor Version 5.00"))
        {
            throw new InvalidDataException(Resources.Strings.SalImp_NotReg);
        }

        var unicode = header != "REGEDIT4";
        var roots = new Dictionary<string, RegKey>(StringComparer.OrdinalIgnoreCase);
        RegKey? key = null;
        string? line;
        while ((line = ReadLogicalLine(reader)) is not null)
        {
            if (line.Length == 0 || line[0] == ';')
            {
                continue;
            }

            if (line[0] == '[')
            {
                key = OpenKey(line, roots);
                continue;
            }

            if (key is not null)
            {
                ReadValue(line, key, unicode);
            }
        }

        foreach (var (known, name) in SalamanderRoots.Known)
        {
            if (roots.TryGetValue(known, out var root) && root.Keys.Count + root.Values.Count > 0)
            {
                return (name, root);
            }
        }

        throw new InvalidDataException(Resources.Strings.SalImp_NoSalamander);
    }

    private static string? ReadLogicalLine(StringReader reader)
    {
        var line = reader.ReadLine();
        if (line is null)
        {
            return null;
        }

        line = line.TrimStart();
        if (!line.StartsWith('"') && !line.StartsWith('@') || !line.TrimEnd().EndsWith('\\'))
        {
            return line.TrimEnd();
        }

        var sb = new StringBuilder(line.TrimEnd()[..^1]);
        while (reader.ReadLine() is { } next)
        {
            var part = next.Trim();
            if (part.EndsWith('\\'))
            {
                sb.Append(part[..^1]);
                continue;
            }

            sb.Append(part);
            break;
        }

        return sb.ToString();
    }

    private static RegKey? OpenKey(string line, Dictionary<string, RegKey> roots)
    {
        var end = line.LastIndexOf(']');
        if (end < 1 || line.StartsWith("[-", StringComparison.Ordinal))
        {
            return null;
        }

        var path = line[1..end];
        foreach (var hive in (ReadOnlySpan<string>)[@"HKEY_CURRENT_USER\", @"HKCU\"])
        {
            if (path.StartsWith(hive, StringComparison.OrdinalIgnoreCase))
            {
                path = path[hive.Length..];
                break;
            }
        }

        foreach (var (known, _) in SalamanderRoots.Known)
        {
            if (path.Equals(known, StringComparison.OrdinalIgnoreCase) || path.StartsWith(known + "\\", StringComparison.OrdinalIgnoreCase))
            {
                var rest = path.Length > known.Length ? path[(known.Length + 1)..] : "";
                if (rest.Split('\\').Any(RegKey.IsSecret))
                {
                    return null;
                }

                if (!roots.TryGetValue(known, out var root))
                {
                    root = new RegKey();
                    roots[known] = root;
                }

                return root.Make(rest);
            }
        }

        return null;
    }

    private static void ReadValue(string line, RegKey key, bool unicode)
    {
        string name;
        int at;
        if (line.StartsWith('@'))
        {
            name = "";
            at = 1;
        }
        else if (ReadQuoted(line, 0) is var (quoted, after))
        {
            name = quoted;
            at = after;
        }
        else
        {
            return;
        }

        if (at >= line.Length || line[at] != '=' || RegKey.IsSecret(name))
        {
            return;
        }

        var data = line[(at + 1)..].Trim();
        if (data.StartsWith('"'))
        {
            if (ReadQuoted(data, 0) is var (text, _))
            {
                key.Values[name] = new RegValue(text, null);
            }
        }
        else if (data.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(data.AsSpan(6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
            {
                key.Values[name] = new RegValue(null, n);
            }
        }
        else if (data.StartsWith("hex(2):", StringComparison.OrdinalIgnoreCase) || data.StartsWith("hex(1):", StringComparison.OrdinalIgnoreCase))
        {
            if (Hex(data[7..]) is { } bytes)
            {
                var text = unicode ? Encoding.Unicode.GetString(bytes) : Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage is > 0 and var cp ? cp : 1252).GetString(bytes);
                key.Values[name] = new RegValue(text.TrimEnd('\0'), null);
            }
        }

    }

    private static (string Text, int After)? ReadQuoted(string line, int start)
    {
        if (start >= line.Length || line[start] != '"')
        {
            return null;
        }

        var sb = new StringBuilder();
        for (var i = start + 1; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                sb.Append(line[++i]);
            }
            else if (c == '"')
            {
                return (sb.ToString(), i + 1);
            }
            else
            {
                sb.Append(c);
            }
        }

        return null;
    }

    private static byte[]? Hex(string text)
    {
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bytes = new byte[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return null;
            }
        }

        return bytes;
    }
}
