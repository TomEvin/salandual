// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/SyntaxRules.cs

using System.Collections.Frozen;

namespace Salandual.Core.Viewer;

public enum SyntaxLanguage { None, Json, Yaml, Xml, Html, Markdown, Css, JavaScript, Python, CFamily, Shell, Sql, PowerShell }

public enum SyntaxKind : byte
{
    Key,
    String,
    Number,
    Keyword,
    Punctuation,
    Comment,
    Tag,
    Attribute,
    Heading,
    Emphasis,
    Strong,
    Code,
    Link,
    Quote,
    Marker,

    Function,

    Escape,
}

public readonly record struct SyntaxSpan(int Start, int Length, SyntaxKind Kind);

public static class SyntaxRules
{
    private static readonly Dictionary<string, SyntaxLanguage> _byExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = SyntaxLanguage.Json, [".jsonc"] = SyntaxLanguage.Json, [".json5"] = SyntaxLanguage.Json, [".jsonl"] = SyntaxLanguage.Json,
        [".ndjson"] = SyntaxLanguage.Json, [".geojson"] = SyntaxLanguage.Json, [".ipynb"] = SyntaxLanguage.Json, [".har"] = SyntaxLanguage.Json,
        [".webmanifest"] = SyntaxLanguage.Json,
        [".yaml"] = SyntaxLanguage.Yaml, [".yml"] = SyntaxLanguage.Yaml,
        [".xml"] = SyntaxLanguage.Xml, [".xaml"] = SyntaxLanguage.Xml, [".axaml"] = SyntaxLanguage.Xml, [".csproj"] = SyntaxLanguage.Xml, [".slnx"] = SyntaxLanguage.Xml,
        [".vbproj"] = SyntaxLanguage.Xml, [".fsproj"] = SyntaxLanguage.Xml, [".vcxproj"] = SyntaxLanguage.Xml, [".props"] = SyntaxLanguage.Xml,
        [".targets"] = SyntaxLanguage.Xml, [".config"] = SyntaxLanguage.Xml, [".resx"] = SyntaxLanguage.Xml, [".nuspec"] = SyntaxLanguage.Xml,
        [".manifest"] = SyntaxLanguage.Xml, [".xsd"] = SyntaxLanguage.Xml, [".xsl"] = SyntaxLanguage.Xml, [".xslt"] = SyntaxLanguage.Xml,
        [".svg"] = SyntaxLanguage.Xml, [".plist"] = SyntaxLanguage.Xml, [".gpx"] = SyntaxLanguage.Xml, [".kml"] = SyntaxLanguage.Xml,
        [".rss"] = SyntaxLanguage.Xml, [".atom"] = SyntaxLanguage.Xml, [".wsdl"] = SyntaxLanguage.Xml, [".xlf"] = SyntaxLanguage.Xml,
        [".html"] = SyntaxLanguage.Html, [".htm"] = SyntaxLanguage.Html, [".xhtml"] = SyntaxLanguage.Html,
        [".md"] = SyntaxLanguage.Markdown, [".markdown"] = SyntaxLanguage.Markdown, [".mdown"] = SyntaxLanguage.Markdown, [".mkd"] = SyntaxLanguage.Markdown,
        [".css"] = SyntaxLanguage.Css,
        [".js"] = SyntaxLanguage.JavaScript, [".mjs"] = SyntaxLanguage.JavaScript, [".cjs"] = SyntaxLanguage.JavaScript,
    };

    public static SyntaxLanguage LanguageFor(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot >= 0 && _byExtension.TryGetValue(fileName[dot..], out var language) ? language : SyntaxLanguage.None;
    }

    public static SyntaxLanguage LanguageForFence(ReadOnlySpan<char> info) => info.ToString().ToLowerInvariant() switch
    {
        "json" or "jsonc" or "json5" or "jsonl" or "geojson" => SyntaxLanguage.Json,
        "yaml" or "yml" => SyntaxLanguage.Yaml,
        "xml" or "xaml" or "csproj" or "msbuild" or "svg" or "xsd" or "xslt" or "plist" => SyntaxLanguage.Xml,
        "html" or "htm" or "xhtml" or "vue" => SyntaxLanguage.Html,
        "css" or "scss" or "less" => SyntaxLanguage.Css,
        "js" or "javascript" or "jsx" or "mjs" or "ts" or "typescript" or "tsx" or "node" => SyntaxLanguage.JavaScript,
        "py" or "python" or "python3" or "ipython" => SyntaxLanguage.Python,
        "c" or "h" or "cpp" or "c++" or "cs" or "csharp" or "c#" or "java" or "go" or "rust" or "rs" or "kotlin" or "swift" or "scala" or "dart" or "fsharp" or "f#" => SyntaxLanguage.CFamily,
        "sh" or "bash" or "shell" or "zsh" or "console" => SyntaxLanguage.Shell,
        "sql" or "tsql" or "mysql" or "postgresql" or "sqlite" => SyntaxLanguage.Sql,
        "ps" or "ps1" or "powershell" or "pwsh" => SyntaxLanguage.PowerShell,
        _ => SyntaxLanguage.None,
    };

    public static int Tokenize(SyntaxLanguage language, string line, int state, List<SyntaxSpan> spans)
        => Tokenize(language, line, 0, line.Length, state, spans);

    private static int Tokenize(SyntaxLanguage language, string s, int from, int to, int state, List<SyntaxSpan> spans) => language switch
    {
        SyntaxLanguage.Json => Json(s, from, to, state, spans),
        SyntaxLanguage.Yaml => Yaml(s, from, to, state, spans),
        SyntaxLanguage.Xml => Markup(s, from, to, state, spans, html: false),
        SyntaxLanguage.Html => Markup(s, from, to, state, spans, html: true),
        SyntaxLanguage.Markdown => from == 0 && to == s.Length ? Markdown(s, state, spans) : 0,
        SyntaxLanguage.Css => Css(s, from, to, state, spans),
        SyntaxLanguage.None => 0,
        _ => Code(_profiles[language], s, from, to, state, spans),
    };

    private const int JsonInComment = 1;

    private static int Json(string s, int i, int to, int state, List<SyntaxSpan> spans)
    {
        if (state == JsonInComment)
        {
            var end = Find(s, "*/", i, to);
            if (end < 0)
            {
                Add(spans, i, to - i, SyntaxKind.Comment);
                return JsonInComment;
            }

            Add(spans, i, end + 2 - i, SyntaxKind.Comment);
            i = end + 2;
        }

        while (i < to)
        {
            var c = s[i];
            if (c == '"')
            {
                var end = StringEnd(s, i, to, '"');
                var after = SkipSpaces(s, end, to);
                if (after < to && s[after] == ':')
                {
                    Add(spans, i, end - i, SyntaxKind.Key);
                }
                else
                {
                    AddString(spans, s, i, end, SyntaxKind.String);
                }

                i = end;
            }
            else if (c is '{' or '}' or '[' or ']' or ',' or ':')
            {
                Add(spans, i, 1, SyntaxKind.Punctuation);
                i++;
            }
            else if (c == '/' && i + 1 < to && s[i + 1] == '/')
            {
                Add(spans, i, to - i, SyntaxKind.Comment);
                return 0;
            }
            else if (c == '/' && i + 1 < to && s[i + 1] == '*')
            {
                var end = Find(s, "*/", i + 2, to);
                if (end < 0)
                {
                    Add(spans, i, to - i, SyntaxKind.Comment);
                    return JsonInComment;
                }

                Add(spans, i, end + 2 - i, SyntaxKind.Comment);
                i = end + 2;
            }
            else if (c == '-' || char.IsAsciiDigit(c))
            {
                var end = i + 1;
                while (end < to && (char.IsAsciiLetterOrDigit(s[end]) || s[end] is '.' or '+' or '-'))
                {
                    end++;
                }

                Add(spans, i, end - i, SyntaxKind.Number);
                i = end;
            }
            else if (char.IsAsciiLetter(c))
            {
                var end = WordEnd(s, i, to);
                if (s.AsSpan(i, end - i) is "true" or "false" or "null")
                {
                    Add(spans, i, end - i, SyntaxKind.Keyword);
                }

                i = end;
            }
            else
            {
                i++;
            }
        }

        return 0;
    }

    private const int YamlMaxState = 0xFFFFF;

    private static int Yaml(string s, int from, int to, int state, List<SyntaxSpan> spans)
    {
        var indent = SkipSpaces(s, from, to);
        if (state > 0)
        {
            if (indent >= to || indent - from > state - 1)
            {
                if (indent < to)
                {
                    Add(spans, indent, to - indent, SyntaxKind.String);
                }

                return state;
            }
        }

        if (indent >= to)
        {
            return 0;
        }

        if (s[indent] == '#')
        {
            Add(spans, indent, to - indent, SyntaxKind.Comment);
            return 0;
        }

        if (indent == from && to - from >= 3 && (string.CompareOrdinal(s, from, "---", 0, 3) == 0 || string.CompareOrdinal(s, from, "...", 0, 3) == 0) && (to - from == 3 || s[from + 3] == ' '))
        {
            Add(spans, from, 3, SyntaxKind.Punctuation);
            return YamlValue(s, from + 3, to, spans, keyColumn: 0);
        }

        var i = indent;
        while (i < to && s[i] == '-' && (i + 1 == to || s[i + 1] == ' '))
        {
            Add(spans, i, 1, SyntaxKind.Punctuation);
            i = SkipSpaces(s, i + 1, to);
        }

        if (i >= to)
        {
            return 0;
        }

        var keyColumn = i - from;
        var colon = YamlKeyEnd(s, i, to);
        if (colon > i)
        {
            Add(spans, i, colon - i, SyntaxKind.Key);
            Add(spans, colon, 1, SyntaxKind.Punctuation);
            i = colon + 1;
        }

        return YamlValue(s, i, to, spans, keyColumn);
    }

    private static int YamlKeyEnd(string s, int i, int to)
    {
        if (s[i] is '"' or '\'')
        {
            var end = StringEnd(s, i, to, s[i], backslash: s[i] == '"');
            var after = SkipSpaces(s, end, to);
            return after < to && s[after] == ':' && (after + 1 == to || s[after + 1] == ' ') ? after : -1;
        }

        if (s[i] is '{' or '[' or '&' or '*' or '!' or '|' or '>' or '%' or '@' or '`')
        {
            return -1;
        }

        for (var k = i; k < to; k++)
        {
            if (s[k] == ':' && (k + 1 == to || s[k + 1] == ' '))
            {
                return k;
            }

            if (s[k] == '#' && k > i && s[k - 1] == ' ')
            {
                return -1;
            }
        }

        return -1;
    }

    private static int YamlValue(string s, int i, int to, List<SyntaxSpan> spans, int keyColumn)
    {
        i = SkipSpaces(s, i, to);
        if (i < to && s[i] is '|' or '>')
        {
            var end = i + 1;
            while (end < to && (s[end] is '+' or '-' || char.IsAsciiDigit(s[end])))
            {
                end++;
            }

            var rest = SkipSpaces(s, end, to);
            if (rest == to || s[rest] == '#')
            {
                Add(spans, i, end - i, SyntaxKind.Punctuation);
                if (rest < to)
                {
                    Add(spans, rest, to - rest, SyntaxKind.Comment);
                }

                return Math.Min(keyColumn + 1, YamlMaxState);
            }
        }

        var flow = 0;
        while (i < to)
        {
            var c = s[i];
            if (c == ' ')
            {
                i++;
            }
            else if (c == '#' && i > 0 && s[i - 1] == ' ')
            {
                Add(spans, i, to - i, SyntaxKind.Comment);
                break;
            }
            else if (c is '"' or '\'')
            {
                var end = StringEnd(s, i, to, c, backslash: c == '"');
                var after = SkipSpaces(s, end, to);
                if (flow > 0 && after < to && s[after] == ':')
                {
                    Add(spans, i, end - i, SyntaxKind.Key);
                }
                else
                {
                    AddString(spans, s, i, end, SyntaxKind.String, escapes: c == '"');
                }

                i = end;
            }
            else if (c is '{' or '[' && (flow > 0 || IsValueStart(s, i)))
            {
                Add(spans, i, 1, SyntaxKind.Punctuation);
                flow++;
                i++;
            }
            else if (flow > 0 && c is '}' or ']' or ',')
            {
                Add(spans, i, 1, SyntaxKind.Punctuation);
                if (c != ',')
                {
                    flow--;
                }

                i++;
            }
            else if (c is '&' or '*' or '!' && IsValueStart(s, i))
            {
                var end = i + 1;
                while (end < to && s[end] != ' ' && !(flow > 0 && s[end] is ',' or ']' or '}'))
                {
                    end++;
                }

                Add(spans, i, end - i, SyntaxKind.Marker);
                i = end;
            }
            else
            {
                var end = i;
                while (end < to && !(s[end] == '#' && s[end - 1] == ' ') && !(flow > 0 && s[end] is ',' or ']' or '}'))
                {
                    if (flow > 0 && s[end] == ':' && (end + 1 == to || s[end + 1] == ' '))
                    {
                        break;
                    }

                    end++;
                }

                var length = s.AsSpan(i, end - i).TrimEnd().Length;
                if (flow > 0 && end < to && s[end] == ':')
                {
                    Add(spans, i, length, SyntaxKind.Key);
                    Add(spans, end, 1, SyntaxKind.Punctuation);
                    i = end + 1;
                    continue;
                }

                Add(spans, i, length, YamlScalar(s.AsSpan(i, length)));
                i = end;
            }
        }

        return 0;
    }

    private static bool IsValueStart(string s, int i) => i == 0 || s[i - 1] is ' ' or '[' or '{' or ',' or ':';

    private static SyntaxKind YamlScalar(ReadOnlySpan<char> word)
    {
        if (word is "true" or "false" or "True" or "False" or "TRUE" or "FALSE" or "null" or "Null" or "NULL" or "~")
        {
            return SyntaxKind.Keyword;
        }

        if (word is ".inf" or "-.inf" or "+.inf" or ".nan" or ".NaN" or ".Inf")
        {
            return SyntaxKind.Number;
        }

        var digits = word.Length > 0 && word[0] is '-' or '+' ? word[1..] : word;
        if (digits.Length == 0 || !char.IsAsciiDigit(digits[0]))
        {
            return SyntaxKind.String;
        }

        if (digits.StartsWith("0x") || digits.StartsWith("0o"))
        {
            return digits[2..].ContainsAnyExcept("0123456789abcdefABCDEF_") ? SyntaxKind.String : SyntaxKind.Number;
        }

        var dots = 0;
        for (var k = 0; k < digits.Length; k++)
        {
            var ch = digits[k];
            if (ch == '.')
            {
                dots++;
            }
            else if (ch is '+' or '-' ? digits[k - 1] is not ('e' or 'E') : !char.IsAsciiDigit(ch) && ch is not ('_' or 'e' or 'E'))
            {
                return SyntaxKind.String;
            }
        }

        return dots <= 1 ? SyntaxKind.Number : SyntaxKind.String;
    }

    private const int MText = 0;
    private const int MComment = 1;
    private const int MCData = 2;
    private const int MTag = 3;
    private const int MQuote2 = 4;
    private const int MQuote1 = 5;
    private const int MScript = 6;
    private const int MStyle = 7;
    private const int MDeclaration = 8;
    private const int RawScript = 1 << 4;
    private const int RawStyle = 2 << 4;
    private const int InnerShift = 6;

    private static int Markup(string s, int from, int to, int state, List<SyntaxSpan> spans, bool html)
    {
        var where = state & 0xF;
        var raw = state & 0x30;
        var inner = state >>> InnerShift;
        var i = from;
        while (i <= to)
        {
            switch (where)
            {
                case MComment:
                {
                    var end = Find(s, "-->", i, to);
                    if (end < 0)
                    {
                        Add(spans, i, to - i, SyntaxKind.Comment);
                        return MComment;
                    }

                    Add(spans, i, end + 3 - i, SyntaxKind.Comment);
                    i = end + 3;
                    where = MText;
                    continue;
                }

                case MCData:
                {
                    var end = Find(s, "]]>", i, to);
                    if (end < 0)
                    {
                        Add(spans, i, to - i, SyntaxKind.String);
                        return MCData;
                    }

                    Add(spans, i, end - i, SyntaxKind.String);
                    Add(spans, end, 3, SyntaxKind.Punctuation);
                    i = end + 3;
                    where = MText;
                    continue;
                }

                case MScript or MStyle:
                {
                    var close = where == MScript ? "</script" : "</style";
                    var end = Find(s, close, i, to, ignoreCase: true);
                    var language = where == MScript ? SyntaxLanguage.JavaScript : SyntaxLanguage.Css;
                    var after = Tokenize(language, s, i, end < 0 ? to : end, inner, spans);
                    if (end < 0)
                    {
                        return where | (after << InnerShift);
                    }

                    i = end;
                    inner = 0;
                    where = MText;
                    continue;
                }

                case MQuote2 or MQuote1:
                {
                    var quote = where == MQuote2 ? '"' : '\'';
                    var end = s.IndexOf(quote, i, to - i);
                    if (end < 0)
                    {
                        AddString(spans, s, i, to, SyntaxKind.String, escapes: false, entities: true);
                        return where | raw;
                    }

                    AddString(spans, s, i, end + 1, SyntaxKind.String, escapes: false, entities: true);
                    i = end + 1;
                    where = MTag;
                    continue;
                }

                case MTag or MDeclaration:
                {
                    while (i < to)
                    {
                        var c = s[i];
                        if (c == '>')
                        {
                            Add(spans, i, 1, SyntaxKind.Punctuation);
                            i++;
                            where = raw == RawScript ? MScript : raw == RawStyle ? MStyle : MText;
                            raw = 0;
                            break;
                        }

                        if ((c is '/' or '?') && i + 1 < to && s[i + 1] == '>')
                        {
                            Add(spans, i, 2, SyntaxKind.Punctuation);
                            i += 2;
                            where = MText;
                            raw = 0;
                            break;
                        }

                        if (c is '"' or '\'')
                        {
                            var end = s.IndexOf(c, i + 1, to - i - 1);
                            if (end < 0)
                            {
                                AddString(spans, s, i, to, SyntaxKind.String, escapes: false, entities: true);
                                return (c == '"' ? MQuote2 : MQuote1) | raw;
                            }

                            AddString(spans, s, i, end + 1, SyntaxKind.String, escapes: false, entities: true);
                            i = end + 1;
                        }
                        else if (IsNameChar(c))
                        {
                            var end = i;
                            while (end < to && IsNameChar(s[end]))
                            {
                                end++;
                            }

                            Add(spans, i, end - i, SyntaxKind.Attribute);
                            i = end;
                        }
                        else
                        {
                            i++;
                        }
                    }

                    if (where is MTag or MDeclaration)
                    {
                        return where | raw;
                    }

                    continue;
                }

                default:
                {
                    if (i >= to)
                    {
                        return MText;
                    }

                    var lt = s.IndexOf('<', i, to - i);
                    var amp = s.IndexOf('&', i, to - i);
                    if (amp >= 0 && (lt < 0 || amp < lt))
                    {
                        var length = EntityLength(s, amp, to);
                        if (length > 0)
                        {
                            Add(spans, amp, length, SyntaxKind.Escape);
                        }

                        i = amp + Math.Max(1, length);
                        continue;
                    }

                    if (lt < 0)
                    {
                        return MText;
                    }

                    i = lt;
                    if (to - i >= 4 && string.CompareOrdinal(s, i, "<!--", 0, 4) == 0)
                    {
                        where = MComment;
                        continue;
                    }

                    if (to - i >= 9 && string.CompareOrdinal(s, i, "<![CDATA[", 0, 9) == 0)
                    {
                        Add(spans, i, 9, SyntaxKind.Punctuation);
                        i += 9;
                        where = MCData;
                        continue;
                    }

                    var start = i;
                    var open = i + 1 < to && s[i + 1] == '/' ? 2 : 1;
                    var declaration = i + 1 < to && s[i + 1] is '!' or '?';
                    if (declaration)
                    {
                        open = 2;
                    }

                    var nameStart = start + open;
                    var nameEnd = nameStart;
                    while (nameEnd < to && IsNameChar(s[nameEnd]))
                    {
                        nameEnd++;
                    }

                    if (nameEnd == nameStart && !declaration)
                    {
                        i = start + 1;
                        continue;
                    }

                    Add(spans, start, open, SyntaxKind.Punctuation);
                    Add(spans, nameStart, nameEnd - nameStart, SyntaxKind.Tag);
                    if (html && open == 1)
                    {
                        var name = s.AsSpan(nameStart, nameEnd - nameStart);
                        raw = name.Equals("script", StringComparison.OrdinalIgnoreCase) ? RawScript : name.Equals("style", StringComparison.OrdinalIgnoreCase) ? RawStyle : 0;
                    }

                    i = nameEnd;
                    where = declaration && s[start + 1] == '!' ? MDeclaration : MTag;
                    continue;
                }
            }
        }

        return where | raw;
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is ':' or '_' or '-' or '.';

    private static int EntityLength(string s, int amp, int to)
    {
        var semi = s.IndexOf(';', amp, Math.Min(to - amp, 12));
        return semi > amp + 1 && !s.AsSpan(amp + 1, semi - amp - 1).ContainsAny(" <&\"'") ? semi + 1 - amp : 0;
    }

    private static int Css(string s, int from, int to, int state, List<SyntaxSpan> spans)
    {
        var comment = (state & 1) != 0;
        var depth = (state >>> 1) & 0xF;
        var i = from;
        if (comment)
        {
            var end = Find(s, "*/", i, to);
            if (end < 0)
            {
                Add(spans, i, to - i, SyntaxKind.Comment);
                return state;
            }

            Add(spans, i, end + 2 - i, SyntaxKind.Comment);
            i = end + 2;
        }

        var value = false;
        var selector = CssSelectorAhead(s, i, to, depth);
        while (i < to)
        {
            var c = s[i];
            if (c == '/' && i + 1 < to && s[i + 1] == '*')
            {
                var end = Find(s, "*/", i + 2, to);
                if (end < 0)
                {
                    Add(spans, i, to - i, SyntaxKind.Comment);
                    return 1 | (depth << 1);
                }

                Add(spans, i, end + 2 - i, SyntaxKind.Comment);
                i = end + 2;
            }
            else if (c is '"' or '\'')
            {
                var end = StringEnd(s, i, to, c);
                AddString(spans, s, i, end, SyntaxKind.String);
                i = end;
            }
            else if (c == '{')
            {
                Add(spans, i, 1, SyntaxKind.Punctuation);
                depth = Math.Min(depth + 1, 15);
                value = false;
                i++;
                selector = CssSelectorAhead(s, i, to, depth);
            }
            else if (c == '}')
            {
                Add(spans, i, 1, SyntaxKind.Punctuation);
                depth = Math.Max(depth - 1, 0);
                value = false;
                i++;
                selector = CssSelectorAhead(s, i, to, depth);
            }
            else if (c == ';')
            {
                Add(spans, i, 1, SyntaxKind.Punctuation);
                value = false;
                i++;
                selector = CssSelectorAhead(s, i, to, depth);
            }
            else if (c == '@')
            {
                var end = CssWordEnd(s, i + 1, to);
                Add(spans, i, end - i, SyntaxKind.Keyword);
                i = end;
            }
            else if (selector)
            {
                if (c is '.' or '#' or ':' && i + 1 < to && (CssWordChar(s[i + 1]) || s[i + 1] == ':'))
                {
                    var end = i + 1;
                    while (end < to && s[end] == ':')
                    {
                        end++;
                    }

                    end = CssWordEnd(s, end, to);
                    Add(spans, i, end - i, SyntaxKind.Attribute);
                    i = end;
                }
                else if (c == '[')
                {
                    var end = s.IndexOf(']', i, to - i);
                    end = end < 0 ? to : end + 1;
                    Add(spans, i, end - i, SyntaxKind.Attribute);
                    i = end;
                }
                else if (char.IsLetter(c) || c == '*')
                {
                    var end = c == '*' ? i + 1 : CssWordEnd(s, i, to);
                    Add(spans, i, end - i, SyntaxKind.Tag);
                    i = end;
                }
                else
                {
                    i++;
                }
            }
            else if (!value && (char.IsLetter(c) || c == '-'))
            {
                var end = CssWordEnd(s, i, to);
                var colon = SkipSpaces(s, end, to);
                if (colon < to && s[colon] == ':')
                {
                    Add(spans, i, end - i, SyntaxKind.Key);
                    value = true;
                    i = colon + 1;
                }
                else
                {
                    i = Math.Max(end, i + 1);
                }
            }
            else if (value && (char.IsAsciiDigit(c) || (c is '.' or '-' or '+' && i + 1 < to && char.IsAsciiDigit(s[i + 1]))))
            {
                var end = i + 1;
                while (end < to && (char.IsAsciiLetterOrDigit(s[end]) || s[end] is '.' or '%'))
                {
                    end++;
                }

                Add(spans, i, end - i, SyntaxKind.Number);
                i = end;
            }
            else if (value && c == '#' && i + 1 < to && char.IsAsciiHexDigit(s[i + 1]))
            {
                var end = CssWordEnd(s, i + 1, to);
                Add(spans, i, end - i, SyntaxKind.Number);
                i = end;
            }
            else if (value && c == '!')
            {
                var end = CssWordEnd(s, i + 1, to);
                Add(spans, i, end - i, SyntaxKind.Keyword);
                i = end;
            }
            else if (value && (char.IsLetter(c) || c == '-'))
            {
                var end = CssWordEnd(s, i, to);
                var kind = end < to && s[end] == '(' ? SyntaxKind.Function : end - i > 2 && s[i] == '-' && s[i + 1] == '-' ? SyntaxKind.Key : SyntaxKind.String;
                Add(spans, i, end - i, kind);
                i = end;
            }
            else
            {
                i++;
            }
        }

        return depth << 1;
    }

    private static bool CssSelectorAhead(string s, int i, int to, int depth)
    {
        if (depth == 0)
        {
            return true;
        }

        for (var k = i; k < to; k++)
        {
            switch (s[k])
            {
                case '{':
                    return true;
                case ';' or '}':
                    return false;
                case '/' when k + 1 < to && s[k + 1] == '*':
                    return false;
            }
        }

        var colon = s.IndexOf(':', i, to - i);
        return colon < 0;
    }

    private static bool CssWordChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_';

    private static int CssWordEnd(string s, int i, int to)
    {
        while (i < to && CssWordChar(s[i]))
        {
            i++;
        }

        return i;
    }

    private sealed record CodeProfile(FrozenSet<string> Keywords, FrozenSet<string> Constants, string? LineComment, bool BlockComments, bool Backticks, bool TripleQuotes, bool HashComment, bool Variables, bool HashBlock = false);

    private static FrozenSet<string> Words(string words, bool ignoreCase = false)
        => words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static readonly FrozenDictionary<SyntaxLanguage, CodeProfile> _profiles = new Dictionary<SyntaxLanguage, CodeProfile>
    {
        [SyntaxLanguage.JavaScript] = new(Words(
            "break case catch class const continue debugger default delete do else export extends finally for function if import in instanceof let new return super switch this throw try typeof var void while with yield async await of from as static get set interface type enum implements private public protected readonly declare namespace abstract keyof"),
            Words("true false null undefined NaN Infinity"), "//", true, true, false, false, false),
        [SyntaxLanguage.Python] = new(Words(
            "and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield match case self"),
            Words("True False None"), null, false, false, true, true, false),
        [SyntaxLanguage.CFamily] = new(Words(
            "if else for foreach while do return class struct record interface enum new public private protected internal static void int long short byte double float decimal bool boolean char string object var val def import package using namespace try catch finally throw switch case break continue this base super fun let mut fn impl trait match where async await go func readonly const sealed override virtual abstract partial get set init required in out ref is as typeof sizeof default yield"),
            Words("true false null nullptr nil"), "//", true, false, false, false, false),
        [SyntaxLanguage.Shell] = new(Words(
            "if then else elif fi for while until do done case esac function in return export local select echo exit set unset source alias"),
            Words("true false"), null, false, false, false, true, true),
        [SyntaxLanguage.Sql] = new(Words(
            "select from where and or not insert into values update set delete create table drop alter join left right inner outer full cross on group by order having limit offset as distinct union all case when then else end is like in between with exists asc desc view index primary key foreign references top",
            ignoreCase: true), Words("null true false", ignoreCase: true), "--", true, false, false, false, false),
        [SyntaxLanguage.PowerShell] = new(Words(
            "begin break catch class continue data do dynamicparam else elseif end exit filter finally for foreach from function if in param process return switch throw trap try until using while -eq -ne -gt -ge -lt -le -like -notlike -match -notmatch -and -or -not",
            ignoreCase: true), Words("$true $false $null", ignoreCase: true), null, false, false, false, true, true, HashBlock: true),
    }.ToFrozenDictionary();

    private static int Code(CodeProfile p, string s, int from, int to, int state, List<SyntaxSpan> spans)
    {
        var i = from;
        if (state == 2)
        {
            var end = Close(s, i, to, '`', backslash: true);
            if (end < 0)
            {
                Template(p, s, i, to, spans);
                return 2;
            }

            Template(p, s, i, end, spans);
            i = end;
        }
        else if (state != 0)
        {
            var (close, kind) = state switch
            {
                1 => ("*/", SyntaxKind.Comment),
                3 => ("\"\"\"", SyntaxKind.String),
                4 => ("'''", SyntaxKind.String),
                _ => ("#>", SyntaxKind.Comment),
            };
            var end = Find(s, close, i, to);
            if (end < 0)
            {
                Add(spans, i, to - i, kind);
                return state;
            }

            Add(spans, i, end + close.Length - i, kind);
            i = end + close.Length;
        }

        while (i < to)
        {
            var c = s[i];
            if (p.LineComment is { } line && c == line[0] && to - i >= line.Length && string.CompareOrdinal(s, i, line, 0, line.Length) == 0)
            {
                Add(spans, i, to - i, SyntaxKind.Comment);
                return 0;
            }

            if (p.HashBlock && c == '<' && i + 1 < to && s[i + 1] == '#')
            {
                var end = Find(s, "#>", i + 2, to);
                if (end < 0)
                {
                    Add(spans, i, to - i, SyntaxKind.Comment);
                    return 5;
                }

                Add(spans, i, end + 2 - i, SyntaxKind.Comment);
                i = end + 2;
            }
            else if (p.HashComment && c == '#' && (i == from || s[i - 1] is ' ' or '\t' or ';'))
            {
                Add(spans, i, to - i, SyntaxKind.Comment);
                return 0;
            }
            else if (p.BlockComments && c == '/' && i + 1 < to && s[i + 1] == '*')
            {
                var end = Find(s, "*/", i + 2, to);
                if (end < 0)
                {
                    Add(spans, i, to - i, SyntaxKind.Comment);
                    return 1;
                }

                Add(spans, i, end + 2 - i, SyntaxKind.Comment);
                i = end + 2;
            }
            else if (p.TripleQuotes && c is '"' or '\'' && to - i >= 3 && s[i + 1] == c && s[i + 2] == c)
            {
                var triple = new string(c, 3);
                var end = Find(s, triple, i + 3, to);
                if (end < 0)
                {
                    Add(spans, i, to - i, SyntaxKind.String);
                    return c == '"' ? 3 : 4;
                }

                Add(spans, i, end + 3 - i, SyntaxKind.String);
                i = end + 3;
            }
            else if (p.Backticks && c == '`')
            {
                var end = Close(s, i + 1, to, '`', backslash: true);
                if (end < 0)
                {
                    Template(p, s, i, to, spans);
                    return 2;
                }

                Template(p, s, i, end, spans);
                i = end;
            }
            else if (c is '"' or '\'')
            {
                var end = StringEnd(s, i, to, c);
                AddString(spans, s, i, end, SyntaxKind.String);
                i = end;
            }
            else if (p.Variables && c == '$' && i + 1 < to && (char.IsLetter(s[i + 1]) || s[i + 1] is '_' or '{'))
            {
                var end = i + 1;
                if (s[end] == '{')
                {
                    var close = s.IndexOf('}', end, to - end);
                    end = close < 0 ? to : close + 1;
                }
                else
                {
                    while (end < to && (char.IsLetterOrDigit(s[end]) || s[end] is '_' or ':'))
                    {
                        end++;
                    }
                }

                var word = s.AsSpan(i, end - i);
                Add(spans, i, end - i, p.Constants.Contains(word.ToString()) ? SyntaxKind.Keyword : SyntaxKind.Key);
                i = end;
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < to && char.IsAsciiDigit(s[i + 1]) && (i == from || !CodeWordChar(s[i - 1]))))
            {
                if (i > from && CodeWordChar(s[i - 1]))
                {
                    i++;
                    continue;
                }

                var end = i + 1;
                while (end < to && (char.IsAsciiLetterOrDigit(s[end]) || s[end] is '.' or '_'))
                {
                    end++;
                }

                Add(spans, i, end - i, SyntaxKind.Number);
                i = end;
            }
            else if (char.IsLetter(c) || c is '_' || (c == '-' && p.Variables && i + 1 < to && char.IsLetter(s[i + 1]) && (i == from || s[i - 1] == ' ')))
            {
                var end = i + 1;
                while (end < to && CodeWordChar(s[end]))
                {
                    end++;
                }

                var word = s.Substring(i, end - i);
                if (p.Constants.Contains(word))
                {
                    Add(spans, i, end - i, SyntaxKind.Keyword);
                }
                else if (p.Keywords.Contains(word))
                {
                    Add(spans, i, end - i, SyntaxKind.Keyword);
                }
                else if (SkipSpaces(s, end, to) is var after && after < to && s[after] == '(' && c != '-')
                {
                    Add(spans, i, end - i, SyntaxKind.Function);
                }

                i = end;
            }
            else
            {
                i++;
            }
        }

        return 0;
    }

    private static void Template(CodeProfile p, string s, int start, int end, List<SyntaxSpan> spans)
    {
        var piece = start;
        for (var k = start; k < end - 1; k++)
        {
            if (s[k] == '\\')
            {
                k++;
                continue;
            }

            if (s[k] != '$' || s[k + 1] != '{')
            {
                continue;
            }

            var depth = 0;
            var close = -1;
            for (var j = k + 2; j < end; j++)
            {
                if (s[j] == '{')
                {
                    depth++;
                }
                else if (s[j] == '}' && depth-- == 0)
                {
                    close = j;
                    break;
                }
            }

            if (close < 0)
            {
                break;
            }

            AddString(spans, s, piece, k, SyntaxKind.String);
            Add(spans, k, 2, SyntaxKind.Punctuation);
            Code(p, s, k + 2, close, 0, spans);
            Add(spans, close, 1, SyntaxKind.Punctuation);
            k = close;
            piece = close + 1;
        }

        AddString(spans, s, piece, end, SyntaxKind.String);
    }

    private static bool CodeWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    private const int MdBackticks = 1;
    private const int MdTildes = 2;
    private const int MdComment = 3;
    private const int MdLanguageShift = 6;
    private const int MdInnerShift = 11;

    private static int Markdown(string s, int state, List<SyntaxSpan> spans)
    {
        var kind = state & 3;
        var indent = SkipSpaces(s, 0, s.Length);
        if (kind is MdBackticks or MdTildes)
        {
            var fence = kind == MdBackticks ? '`' : '~';
            var run = Run(s, indent, fence);
            if (indent <= 3 && run >= ((state >>> 2) & 0xF) && SkipSpaces(s, indent + run, s.Length) == s.Length)
            {
                Add(spans, indent, run, SyntaxKind.Punctuation);
                return 0;
            }

            var language = (SyntaxLanguage)((state >>> MdLanguageShift) & 0x1F);
            var after = language is SyntaxLanguage.None or SyntaxLanguage.Markdown ? 0 : Tokenize(language, s, 0, s.Length, state >>> MdInnerShift, spans);
            return (state & ((1 << MdInnerShift) - 1)) | (Math.Min(after, 0xFFFFF) << MdInnerShift);
        }

        if (kind == MdComment)
        {
            var end = s.IndexOf("-->", StringComparison.Ordinal);
            if (end < 0)
            {
                Add(spans, 0, s.Length, SyntaxKind.Comment);
                return MdComment;
            }

            Add(spans, 0, end + 3, SyntaxKind.Comment);
            return Inline(s, end + 3, s.Length, spans, null);
        }

        if (indent >= s.Length)
        {
            return 0;
        }

        if (indent <= 3 && s[indent] is '`' or '~')
        {
            var run = Run(s, indent, s[indent]);
            if (run >= 3 && (s[indent] == '~' || s.IndexOf('`', indent + run) < 0))
            {
                Add(spans, indent, s.Length - indent, SyntaxKind.Punctuation);
                var info = s.AsSpan(indent + run).Trim();
                var space = info.IndexOfAny(' ', '{');
                var language = LanguageForFence(space < 0 ? info : info[..space]);
                return (s[indent] == '`' ? MdBackticks : MdTildes) | (Math.Min(run, 15) << 2) | ((int)language << MdLanguageShift);
            }
        }

        if (indent <= 3 && s[indent] == '#')
        {
            var hashes = Run(s, indent, '#');
            if (hashes <= 6 && (indent + hashes == s.Length || s[indent + hashes] == ' '))
            {
                return Inline(s, indent, s.Length, spans, SyntaxKind.Heading);
            }
        }

        if (indent <= 3 && s[indent] == '>')
        {
            Add(spans, indent, s.Length - indent, SyntaxKind.Quote);
            return 0;
        }

        if (indent <= 3 && s[indent] is '-' or '*' or '_' or '=' && IsRule(s, indent))
        {
            Add(spans, indent, s.Length - indent, SyntaxKind.Punctuation);
            return 0;
        }

        if (indent <= 3 && s[indent] == '<' && indent + 1 < s.Length && (char.IsLetter(s[indent + 1]) || s[indent + 1] is '/' or '!'))
        {
            var after = Markup(s, indent, s.Length, 0, spans, html: true);
            return (after & 0xF) == MComment ? MdComment : 0;
        }

        var i = indent;
        if (s[i] is '-' or '*' or '+' && i + 1 < s.Length && s[i + 1] == ' ')
        {
            Add(spans, i, 1, SyntaxKind.Marker);
            i += 2;
        }
        else if (char.IsAsciiDigit(s[i]))
        {
            var end = i;
            while (end < s.Length && char.IsAsciiDigit(s[end]))
            {
                end++;
            }

            if (end < s.Length - 1 && s[end] is '.' or ')' && s[end + 1] == ' ' && end - i <= 9)
            {
                Add(spans, i, end + 1 - i, SyntaxKind.Marker);
                i = end + 2;
            }
        }

        if (i > indent && i + 2 < s.Length && s[i] == '[' && s[i + 2] == ']' && s[i + 1] is ' ' or 'x' or 'X')
        {
            Add(spans, i, 3, SyntaxKind.Marker);
            i += 3;
        }

        return Inline(s, i, s.Length, spans, null);
    }

    private static bool IsRule(string s, int indent)
    {
        var mark = s[indent];
        var count = 0;
        for (var k = indent; k < s.Length; k++)
        {
            if (s[k] == mark)
            {
                count++;
            }
            else if (s[k] != ' ')
            {
                return false;
            }
        }

        return count >= 3;
    }

    private static int Inline(string s, int from, int to, List<SyntaxSpan> spans, SyntaxKind? ground)
    {
        var gap = from;
        void Fill(int until)
        {
            if (ground is { } g && until > gap)
            {
                Add(spans, gap, until - gap, g);
            }
        }

        var i = from;
        while (i < to)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < to)
            {
                i += 2;
            }
            else if (c == '`')
            {
                var run = Run(s, i, '`');
                var end = Find(s, new string('`', run), i + run, to);
                if (end < 0)
                {
                    i += run;
                    continue;
                }

                Fill(i);
                Add(spans, i, end + run - i, SyntaxKind.Code);
                i = gap = end + run;
            }
            else if (c == '<' && to - i >= 4 && string.CompareOrdinal(s, i, "<!--", 0, 4) == 0)
            {
                Fill(i);
                var end = Find(s, "-->", i + 4, to);
                if (end < 0)
                {
                    Add(spans, i, to - i, SyntaxKind.Comment);
                    return MdComment;
                }

                Add(spans, i, end + 3 - i, SyntaxKind.Comment);
                i = gap = end + 3;
            }
            else if (c == '<' && s.IndexOf('>', i, to - i) is var gt and > 0 && s.AsSpan(i + 1, gt - i - 1) is var inside && (inside.StartsWith("http://") || inside.StartsWith("https://") || inside.StartsWith("mailto:")))
            {
                Fill(i);
                Add(spans, i, gt + 1 - i, SyntaxKind.Link);
                i = gap = gt + 1;
            }
            else if (c == '<' && i + 1 < to && (char.IsAsciiLetter(s[i + 1]) || (s[i + 1] == '/' && i + 2 < to && char.IsAsciiLetter(s[i + 2]))) && s.IndexOf('>', i, to - i) is var close and > 0)
            {
                Fill(i);
                Markup(s, i, close + 1, 0, spans, html: true);
                i = gap = close + 1;
            }
            else if (c == '[' || (c == '!' && i + 1 < to && s[i + 1] == '['))
            {
                var open = c == '!' ? i + 1 : i;
                var end = LinkEnd(s, open, to);
                if (end > 0)
                {
                    Fill(i);
                    Add(spans, i, end - i, SyntaxKind.Link);
                    i = gap = end;
                    continue;
                }

                i++;
            }
            else if (c is '*' or '_')
            {
                var run = Math.Min(Run(s, i, c), 3);
                var inner = i + run;
                if (inner >= to || s[inner] == ' ' || (c == '_' && i > from && char.IsLetterOrDigit(s[i - 1])))
                {
                    i += Run(s, i, c);
                    continue;
                }

                var marker = new string(c, run);
                var end = Find(s, marker, inner, to);
                if (end < 0 || s[end - 1] == ' ' || (c == '_' && end + run < to && char.IsLetterOrDigit(s[end + run])))
                {
                    i += run;
                    continue;
                }

                Fill(i);
                var kind = run >= 2 ? SyntaxKind.Strong : SyntaxKind.Emphasis;
                Add(spans, i, run, kind);
                Inline(s, inner, end, spans, kind);
                Add(spans, end, run, kind);
                i = gap = end + run;
            }
            else
            {
                i++;
            }
        }

        Fill(to);
        return 0;
    }

    private static int LinkEnd(string s, int open, int to)
    {
        var depth = 0;
        for (var k = open; k < to; k++)
        {
            if (s[k] == '\\')
            {
                k++;
            }
            else if (s[k] == '[')
            {
                depth++;
            }
            else if (s[k] == ']' && --depth == 0)
            {
                if (k + 1 < to && s[k + 1] is '(' or '[')
                {
                    var closing = s[k + 1] == '(' ? ')' : ']';
                    var end = s.IndexOf(closing, k + 2, to - k - 2);
                    return end < 0 ? -1 : end + 1;
                }

                return -1;
            }
        }

        return -1;
    }

    private static void Add(List<SyntaxSpan> spans, int start, int length, SyntaxKind kind)
    {
        if (length > 0)
        {
            spans.Add(new SyntaxSpan(start, length, kind));
        }
    }

    private static void AddString(List<SyntaxSpan> spans, string s, int start, int end, SyntaxKind kind, bool escapes = true, bool entities = false)
    {
        var piece = start;
        for (var k = start; k < end; k++)
        {
            var length = escapes && s[k] == '\\' && k + 1 < end ? (s[k + 1] == 'u' && k + 5 < end ? 6 : 2)
                : entities && s[k] == '&' ? EntityLength(s, k, end) : 0;
            if (length > 0)
            {
                Add(spans, piece, k - piece, kind);
                Add(spans, k, length, SyntaxKind.Escape);
                k += length - 1;
                piece = k + 1;
            }
        }

        Add(spans, piece, end - piece, kind);
    }

    private static int Find(string s, string what, int from, int to, bool ignoreCase = false)
    {
        if (from > to)
        {
            return -1;
        }

        var at = s.IndexOf(what, from, to - from, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        return at;
    }

    private static int SkipSpaces(string s, int i, int to)
    {
        while (i < to && s[i] is ' ' or '\t')
        {
            i++;
        }

        return i;
    }

    private static int WordEnd(string s, int i, int to)
    {
        while (i < to && char.IsAsciiLetterOrDigit(s[i]))
        {
            i++;
        }

        return i;
    }

    private static int Run(string s, int i, char c)
    {
        var n = 0;
        while (i + n < s.Length && s[i + n] == c)
        {
            n++;
        }

        return n;
    }

    private static int StringEnd(string s, int i, int to, char quote, bool backslash = true)
        => Close(s, i + 1, to, quote, backslash) is var end and >= 0 ? end : to;

    private static int Close(string s, int k, int to, char quote, bool backslash)
    {
        for (; k < to; k++)
        {
            if (backslash && s[k] == '\\')
            {
                k++;
            }
            else if (s[k] == quote)
            {
                if (!backslash && quote == '\'' && k + 1 < to && s[k + 1] == '\'')
                {
                    k++;
                    continue;
                }

                return k + 1;
            }
        }

        return -1;
    }
}
