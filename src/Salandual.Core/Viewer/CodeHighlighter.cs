// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/CodeHighlighter.cs

using System.Collections.Frozen;
using System.Net;
using System.Text;

namespace Salandual.Core.Viewer;

internal static class CodeHighlighter
{
    private sealed record Language(FrozenSet<string> Keywords, string? LineComment, bool BlockComments, bool HashBlock, bool Backticks, bool TripleQuotes, bool Magics);

    private static FrozenSet<string> Words(string words, bool ignoreCase = false)
        => words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static readonly Language Python = new(Words(
        "False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield match case"),
        "#", false, false, false, true, true);

    private static readonly Language R = new(Words(
        "if else repeat while function for next break TRUE FALSE NULL Inf NaN NA NA_integer_ NA_real_ NA_character_ in library require return"),
        "#", false, false, false, false, false);

    private static readonly Language Julia = new(Words(
        "baremodule begin break catch const continue do else elseif end export false finally for function global if import let local macro module quote return struct true try using while mutable abstract type where in isa nothing"),
        "#", false, true, false, true, false);

    private static readonly Language Script = new(Words(
        "break case catch class const continue debugger default delete do else export extends false finally for function if import in instanceof let new null return super switch this throw true try typeof var void while with yield async await of interface type enum implements private public protected readonly as from undefined"),
        "//", true, false, true, false, false);

    private static readonly Language Sql = new(Words(
        "select from where and or not insert into values update set delete create table drop alter join left right inner outer full cross on group by order having limit offset as distinct union all case when then else end is null like in between with exists asc desc view index primary key",
        ignoreCase: true), "--", true, false, false, false, false);

    private static readonly Language Shell = new(Words(
        "if then else elif fi for while until do done case esac function in return export local select"),
        "#", false, false, false, false, false);

    private static readonly Language CFamily = new(Words(
        "if else for while do return class struct new public private protected internal static void int long double float bool boolean char string true false null var val def object import package using namespace try catch finally throw switch case break continue this fun let mut fn impl trait enum match where async await go func"),
        "//", true, false, false, false, false);

    private static readonly Language Other = new(FrozenSet<string>.Empty, null, false, false, false, false, false);

    private static Language For(string language) => language.ToLowerInvariant() switch
    {
        "python" or "python3" or "python2" or "ipython" or "ipython3" or "py" or "sage" => Python,
        "r" => R,
        "julia" => Julia,
        "javascript" or "typescript" or "js" or "ts" or "node" or "deno" => Script,
        "sql" => Sql,
        "bash" or "sh" or "shell" or "zsh" => Shell,
        "c" or "c++" or "cpp" or "c#" or "csharp" or "f#" or "java" or "scala" or "kotlin" or "go" or "rust" or "swift" => CFamily,
        _ => Other,
    };

    public static string Highlight(string code, string language)
    {
        var lang = For(language);
        var html = new StringBuilder(code.Length + (code.Length / 4));
        void Span(string kind, int from, int to) => html.Append("<span class=\"").Append(kind).Append("\">").Append(WebUtility.HtmlEncode(code[from..to])).Append("</span>");
        int LineEnd(int from) => code.IndexOf('\n', from) is var n and >= 0 ? n : code.Length;

        var i = 0;
        var lineStart = true;
        while (i < code.Length)
        {
            var c = code[i];
            if (lineStart && lang.Magics && (c == '%' || c == '!'))
            {
                var end = LineEnd(i);
                Span("mg", i, end);
                i = end;
                continue;
            }

            if (c == '\n')
            {
                html.Append('\n');
                lineStart = true;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                html.Append(c);
                i++;
                continue;
            }

            lineStart = false;
            if (lang.HashBlock && c == '#' && At(code, i + 1, '='))
            {
                var end = code.IndexOf("=#", i + 2, StringComparison.Ordinal);
                end = end < 0 ? code.Length : end + 2;
                Span("cm", i, end);
                i = end;
            }
            else if (lang.LineComment is { } comment && code.AsSpan(i).StartsWith(comment, StringComparison.Ordinal))
            {
                var end = LineEnd(i);
                Span("cm", i, end);
                i = end;
            }
            else if (lang.BlockComments && c == '/' && At(code, i + 1, '*'))
            {
                var end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? code.Length : end + 2;
                Span("cm", i, end);
                i = end;
            }
            else if (c is '"' or '\'' || (c == '`' && lang.Backticks))
            {
                var end = StringEnd(code, i, lang);
                Span("st", i, end);
                i = end;
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < code.Length && char.IsAsciiDigit(code[i + 1])))
            {
                var end = i + 1;
                while (end < code.Length && (char.IsAsciiLetterOrDigit(code[end]) || code[end] is '.' or '_'
                    || (code[end] is '+' or '-' && code[end - 1] is 'e' or 'E' && !code[i..end].StartsWith("0x", StringComparison.OrdinalIgnoreCase))))
                {
                    end++;
                }

                Span("nu", i, end);
                i = end;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var end = i + 1;
                while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_'))
                {
                    end++;
                }

                var word = code[i..end];
                if (lang == Python && end < code.Length && code[end] is '"' or '\'' && word.Length <= 2 && word.All(ch => "rbfuRBFU".Contains(ch)))
                {
                    var stringEnd = StringEnd(code, end, lang);
                    Span("st", i, stringEnd);
                    i = stringEnd;
                    continue;
                }

                if (lang.Keywords.Contains(word))
                {
                    Span("kw", i, end);
                }
                else
                {
                    html.Append(WebUtility.HtmlEncode(word));
                }

                i = end;
            }
            else
            {
                html.Append(WebUtility.HtmlEncode(c.ToString()));
                i++;
            }
        }

        return html.ToString();
    }

    private static bool At(string text, int index, char c) => index < text.Length && text[index] == c;

    private static int StringEnd(string code, int quoteAt, Language lang)
    {
        var quote = code[quoteAt];
        if (lang.TripleQuotes && quote != '`' && code.AsSpan(quoteAt).StartsWith(new string(quote, 3), StringComparison.Ordinal))
        {
            var close = code.IndexOf(new string(quote, 3), quoteAt + 3, StringComparison.Ordinal);
            return close < 0 ? code.Length : close + 3;
        }

        for (var i = quoteAt + 1; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '\\')
            {
                i++;
            }
            else if (c == quote)
            {
                return i + 1;
            }
            else if (c == '\n' && quote != '`' && lang != R && lang != Julia)
            {
                return i;
            }
        }

        return code.Length;
    }
}
