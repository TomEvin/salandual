// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Editing/CommentStyle.cs

using System.Text;
using Salandual.Core.Viewer;

namespace Salandual.Core.Editing;

public sealed record CommentStyle(string Start, string? End = null)
{
    private static readonly CommentStyle Slashes = new("//");
    private static readonly CommentStyle Hash = new("#");
    private static readonly CommentStyle Markup = new("<!--", "-->");

    public static CommentStyle? For(SyntaxLanguage colors, string name)
    {
        switch (colors)
        {
            case SyntaxLanguage.Json or SyntaxLanguage.JavaScript or SyntaxLanguage.CFamily:
                return Slashes;
            case SyntaxLanguage.Yaml or SyntaxLanguage.Python or SyntaxLanguage.Shell or SyntaxLanguage.PowerShell:
                return Hash;
            case SyntaxLanguage.Xml or SyntaxLanguage.Html or SyntaxLanguage.Markdown:
                return Markup;
            case SyntaxLanguage.Css:
                return new("/*", "*/");
            case SyntaxLanguage.Sql:
                return new("--");
        }

        var extension = Path.GetExtension(name).ToLowerInvariant();
        return extension switch
        {
            ".cs" or ".js" or ".mjs" or ".ts" or ".tsx" or ".jsx" or ".java" or ".c" or ".h" or ".cpp" or ".hpp" or ".cc" or ".go" or ".rs" or ".kt" or ".swift" or ".dart" or ".scss" or ".less" or ".php" or ".fs" => Slashes,
            ".py" or ".ps1" or ".psm1" or ".psd1" or ".sh" or ".bash" or ".rb" or ".pl" or ".r" or ".toml" or ".conf" or ".cfg" or ".properties" or ".gitignore" or ".gitattributes" or ".editorconfig" or ".dockerignore" or ".env" => Hash,
            ".ini" or ".inf" or ".reg" => new(";"),
            ".sql" or ".lua" or ".hs" => new("--"),
            ".bat" or ".cmd" => new("REM"),
            ".vb" or ".vbs" => new("'"),
            ".css" => new("/*", "*/"),
            ".xml" or ".xaml" or ".axaml" or ".csproj" or ".slnx" or ".vbproj" or ".fsproj" or ".vcxproj" or ".nuspec" or ".props" or ".targets" or ".config" or ".resx" or ".svg" or ".html" or ".htm" or ".md" => Markup,
            _ => Path.GetFileName(name).Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(name).Equals("Makefile", StringComparison.OrdinalIgnoreCase) ? Hash : null,
        };
    }

    public string Toggle(string lines)
    {
        if (End is null)
        {
            return ToggleLines(lines);
        }

        var first = 0;
        while (first < lines.Length && char.IsWhiteSpace(lines[first]))
        {
            first++;
        }

        var last = lines.Length;
        while (last > first && char.IsWhiteSpace(lines[last - 1]))
        {
            last--;
        }

        if (first >= last)
        {
            return lines[..first] + Start + " " + End + lines[last..];
        }

        var inner = lines[first..last];
        if (inner.StartsWith(Start, StringComparison.Ordinal) && inner.EndsWith(End, StringComparison.Ordinal) && inner.Length >= Start.Length + End.Length)
        {
            var body = inner[Start.Length..^End.Length];
            if (body.StartsWith(' '))
            {
                body = body[1..];
            }

            if (body.EndsWith(' '))
            {
                body = body[..^1];
            }

            return lines[..first] + body + lines[last..];
        }

        return lines[..first] + Start + " " + inner + " " + End + lines[last..];
    }

    private string ToggleLines(string lines)
    {
        var parts = new List<(string Text, string Break)>();
        var at = 0;
        while (at < lines.Length)
        {
            var end = at;
            while (end < lines.Length && lines[end] is not ('\r' or '\n'))
            {
                end++;
            }

            var breakEnd = end < lines.Length && lines[end] == '\r' && end + 1 < lines.Length && lines[end + 1] == '\n' ? end + 2 : Math.Min(lines.Length, end + 1);
            parts.Add((lines[at..end], lines[end..breakEnd]));
            at = breakEnd;
        }

        if (parts.Count == 0)
        {
            return Start + " ";
        }

        static int Lead(string s)
        {
            var i = 0;
            while (i < s.Length && s[i] is ' ' or '\t')
            {
                i++;
            }

            return i;
        }

        var withText = parts.Where(p => Lead(p.Text) < p.Text.Length).ToList();
        var output = new StringBuilder(lines.Length + (parts.Count * (Start.Length + 1)));
        if (withText.Count > 0 && withText.All(p => p.Text.AsSpan(Lead(p.Text)).StartsWith(Start, StringComparison.Ordinal)))
        {
            foreach (var (text, lineBreak) in parts)
            {
                var lead = Lead(text);
                if (lead < text.Length)
                {
                    var rest = text[(lead + Start.Length)..];
                    output.Append(text, 0, lead).Append(rest.StartsWith(' ') ? rest[1..] : rest);
                }
                else
                {
                    output.Append(text);
                }

                output.Append(lineBreak);
            }

            return output.ToString();
        }

        var column = withText.Count > 0 ? withText.Min(p => Lead(p.Text)) : Lead(parts[0].Text);
        foreach (var (text, lineBreak) in parts)
        {
            if (Lead(text) < text.Length || withText.Count == 0)
            {
                var at2 = Math.Min(column, text.Length);
                output.Append(text, 0, at2).Append(Start).Append(' ').Append(text, at2, text.Length - at2);
            }
            else
            {
                output.Append(text);
            }

            output.Append(lineBreak);
        }

        return output.ToString();
    }
}
