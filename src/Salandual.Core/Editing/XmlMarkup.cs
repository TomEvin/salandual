// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Editing/XmlMarkup.cs

using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Salandual.Core.Editing;

public static partial class XmlMarkup
{
    internal static XmlReaderSettings SafeSettings() => new()
    {
        DtdProcessing = DtdProcessing.Parse,
        XmlResolver = null,
        MaxCharactersFromEntities = 10_000_000,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = true,
    };

    public static CheckResult Check(string text)
    {
        var folds = Folds(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CheckResult([], folds);
        }

        try
        {
            using var reader = XmlReader.Create(new StringReader(text), SafeSettings());
            while (reader.Read())
            {
            }
        }
        catch (XmlException e)
        {
            return new CheckResult([TextProblem.AtLine(text, e.LineNumber, e.LinePosition, Message(e))], folds);
        }

        return new CheckResult([], folds);
    }

    internal static string Message(XmlException e) => WherePattern().Replace(e.Message, "").Trim();

    [GeneratedRegex(@"\s*Line \d+, position \d+\.\s*$")]
    private static partial Regex WherePattern();

    public static string? Format(string text, string unit)
    {
        if (string.IsNullOrWhiteSpace(text) || Check(text).Problems.Count > 0)
        {
            return null;
        }

        var items = Scan(text);
        var output = new StringBuilder(text.Length + (text.Length / 8));
        var stack = new Stack<(string Name, bool Preserve)>();
        var lineStarts = new List<int>();
        var newIndent = new List<string>();
        var oldIndent = new List<string>();
        var index = 0;
        var at = 0;
        while (at <= text.Length)
        {
            var lineEnd = at;
            while (lineEnd < text.Length && text[lineEnd] is not ('\r' or '\n'))
            {
                lineEnd++;
            }

            var breakEnd = lineEnd < text.Length && text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? lineEnd + 2 : Math.Min(text.Length, lineEnd + 1);
            while (index < items.Count && items[index].End <= at)
            {
                Apply(items[index++]);
            }

            var wsEnd = at;
            while (wsEnd < lineEnd && text[wsEnd] is ' ' or '\t')
            {
                wsEnd++;
            }

            var old = text[at..wsEnd];
            string indent;
            var inside = index < items.Count && items[index].Start < at ? items[index] : (Item?)null;
            if (inside is { Kind: ItemKind.Text } textItem && !string.IsNullOrWhiteSpace(text[textItem.Start..at]))
            {
                indent = old;
            }
            else if (inside is { Kind: not ItemKind.Text } construct)
            {
                var startLine = lineStarts.BinarySearch(construct.Start);
                startLine = startLine >= 0 ? startLine : ~startLine - 1;
                indent = startLine >= 0 && old.StartsWith(oldIndent[startLine], StringComparison.Ordinal) ? newIndent[startLine] + old[oldIndent[startLine].Length..] : old;
            }
            else if (stack.Count > 0 && stack.Peek().Preserve)
            {
                indent = old;
            }
            else if (wsEnd == lineEnd)
            {
                indent = "";
            }
            else
            {
                var depth = stack.Count - (string.CompareOrdinal(text, wsEnd, "</", 0, 2) == 0 ? 1 : 0);
                indent = string.Concat(Enumerable.Repeat(unit, Math.Max(0, depth)));
            }

            lineStarts.Add(at);
            oldIndent.Add(old);
            newIndent.Add(indent);
            output.Append(indent).Append(text, wsEnd, breakEnd - wsEnd);
            if (breakEnd == lineEnd)
            {
                break;
            }

            at = breakEnd;
        }

        return output.ToString();

        void Apply(Item item)
        {
            if (item.Kind == ItemKind.StartTag)
            {
                stack.Push((item.Name, item.Preserve || (stack.Count > 0 && stack.Peek().Preserve && !item.Default)));
            }
            else if (item.Kind == ItemKind.EndTag && stack.Any(s => s.Name == item.Name))
            {
                while (stack.Count > 0 && stack.Pop().Name != item.Name)
                {
                }
            }
        }
    }

    public static IReadOnlyList<FoldRange> Folds(string text)
    {
        var lines = new LineMap(text);
        var folds = new List<FoldRange>();
        var open = new Stack<Item>();
        foreach (var item in Scan(text))
        {
            switch (item.Kind)
            {
                case ItemKind.StartTag:
                    open.Push(item);
                    break;
                case ItemKind.EndTag when open.Any(o => o.Name == item.Name):
                    Item start;
                    while ((start = open.Pop()).Name != item.Name)
                    {
                    }

                    var between = lines.Line(item.Start) - lines.Line(start.End);
                    if (between >= 1)
                    {
                        folds.Add(new FoldRange(start.End, item.Start, between - 1));
                    }

                    break;
                case ItemKind.Comment:
                    var inner = lines.Line(item.End) - lines.Line(item.Start);
                    if (inner >= 1 && item.End - item.Start > 7)
                    {
                        folds.Add(new FoldRange(item.Start + 4, item.End - 3, inner - 1));
                    }

                    break;
            }
        }

        folds.Sort((a, b) => a.Start.CompareTo(b.Start));
        return folds;
    }

    private enum ItemKind : byte { Text, StartTag, EmptyTag, EndTag, Comment, CData, Instruction, Declaration }

    private readonly record struct Item(ItemKind Kind, int Start, int End, string Name = "", bool Preserve = false, bool Default = false);

    private static List<Item> Scan(string text)
    {
        var items = new List<Item>();
        var at = 0;
        while (at < text.Length)
        {
            if (text[at] != '<')
            {
                var next = text.IndexOf('<', at);
                next = next < 0 ? text.Length : next;
                items.Add(new Item(ItemKind.Text, at, next));
                at = next;
                continue;
            }

            int End(string close, int from)
            {
                var i = text.IndexOf(close, from, StringComparison.Ordinal);
                return i < 0 ? text.Length : i + close.Length;
            }

            var start = at;
            if (string.CompareOrdinal(text, at, "<!--", 0, 4) == 0)
            {
                at = End("-->", at + 4);
                items.Add(new Item(ItemKind.Comment, start, at));
            }
            else if (string.CompareOrdinal(text, at, "<![CDATA[", 0, 9) == 0)
            {
                at = End("]]>", at + 9);
                items.Add(new Item(ItemKind.CData, start, at));
            }
            else if (string.CompareOrdinal(text, at, "<?", 0, 2) == 0)
            {
                at = End("?>", at + 2);
                items.Add(new Item(ItemKind.Instruction, start, at));
            }
            else if (string.CompareOrdinal(text, at, "<!", 0, 2) == 0)
            {
                at = TagEnd(text, at + 2, brackets: true);
                items.Add(new Item(ItemKind.Declaration, start, at));
            }
            else if (string.CompareOrdinal(text, at, "</", 0, 2) == 0)
            {
                var name = Name(text, at + 2);
                at = TagEnd(text, at + 2, brackets: false);
                items.Add(new Item(ItemKind.EndTag, start, at, name));
            }
            else if (at + 1 < text.Length && (char.IsLetter(text[at + 1]) || text[at + 1] is '_' or ':'))
            {
                var name = Name(text, at + 1);
                at = TagEnd(text, at + 1, brackets: false);
                var tag = text.AsSpan(start, at - start);
                if (tag.EndsWith("/>", StringComparison.Ordinal))
                {
                    items.Add(new Item(ItemKind.EmptyTag, start, at, name));
                }
                else
                {
                    var space = SpacePattern().Match(text, start, at - start);
                    items.Add(new Item(ItemKind.StartTag, start, at, name, space.Success && space.Groups[1].Value == "preserve", space.Success && space.Groups[1].Value == "default"));
                }
            }
            else
            {
                var next = text.IndexOf('<', at + 1);
                next = next < 0 ? text.Length : next;
                items.Add(new Item(ItemKind.Text, at, next));
                at = next;
            }
        }

        return items;
    }

    [GeneratedRegex("""\sxml:space\s*=\s*["'](preserve|default)["']""")]
    private static partial Regex SpacePattern();

    private static string Name(string text, int at)
    {
        var end = at;
        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not ('>' or '/'))
        {
            end++;
        }

        return text[at..end];
    }

    private static int TagEnd(string text, int at, bool brackets)
    {
        var depth = 0;
        for (var i = at; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '"' or '\'':
                    var close = text.IndexOf(text[i], i + 1);
                    if (close < 0)
                    {
                        return text.Length;
                    }

                    i = close;
                    break;
                case '[' when brackets:
                    depth++;
                    break;
                case ']' when brackets:
                    depth--;
                    break;
                case '>' when depth <= 0:
                    return i + 1;
            }
        }

        return text.Length;
    }
}
