// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Editing/YamlText.cs

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Salandual.Core.Resources;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Salandual.Core.Editing;

public static partial class YamlText
{
    public static CheckResult Check(string text)
    {
        var folds = Folds(text);
        var problems = new List<TextProblem>();
        var lines = new LineMap(text);
        var readTo = 0L;
        try
        {
            var stack = new Stack<(bool Mapping, bool ExpectKey, Dictionary<string, int>? Keys)>();
            var parser = new Parser(new StringReader(text));
            while (parser.MoveNext())
            {
                var e = parser.Current!;
                readTo = e.End.Index;
                if (e is MappingEnd or SequenceEnd)
                {
                    stack.Pop();
                    continue;
                }

                if (e is not (Scalar or AnchorAlias or MappingStart or SequenceStart))
                {
                    continue;
                }

                if (stack.Count > 0 && stack.Peek().Mapping)
                {
                    var (mapping, expectKey, keys) = stack.Pop();
                    if (expectKey && e is Scalar { Value: var key } && key != "<<")
                    {
                        keys ??= new Dictionary<string, int>(StringComparer.Ordinal);
                        var offset = (int)Math.Min(text.Length, e.Start.Index);
                        if (keys.TryGetValue(key, out var first))
                        {
                            problems.Add(TextProblem.At(text, offset, (int)(e.End.Index - e.Start.Index), Loc.Format(Strings.Yaml_Duplicate, Short(key), lines.Line(first))));
                        }
                        else
                        {
                            keys[key] = offset;
                        }
                    }

                    stack.Push((mapping, !expectKey, keys));
                }

                if (e is MappingStart)
                {
                    stack.Push((true, true, null));
                }
                else if (e is SequenceStart)
                {
                    stack.Push((false, false, null));
                }
            }
        }
        catch (YamlException e)
        {
            problems.Insert(0, TextProblem.AtLine(text, (int)Math.Clamp(e.Start.Line, 1, int.MaxValue), (int)Math.Clamp(e.Start.Column, 1, int.MaxValue), Message(e)));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            problems.Insert(0, TextProblem.At(text, (int)Math.Min(text.Length, readTo), 1, Strings.Yaml_Unreadable));
        }

        return new CheckResult(problems, folds);
    }

    private static string Short(string key) => key.Length > 30 ? key[..30] + "…" : key;

    internal static string Message(YamlException e) => WherePattern().Replace(e.Message, "").Trim();

    [GeneratedRegex(@"^\([^)]*\)\s*-\s*\([^)]*\):\s*")]
    private static partial Regex WherePattern();

    public static IReadOnlyList<FoldRange> Folds(string text)
    {
        var folds = new List<FoldRange>();
        var open = new Stack<(int Indent, int Line, int LineEnd)>();
        var lineNumber = 0;
        var lastContent = (Line: 0, End: 0);
        var at = 0;
        void Close(int indent)
        {
            while (open.Count > 0 && open.Peek().Indent >= indent)
            {
                var o = open.Pop();
                if (lastContent.Line > o.Line)
                {
                    folds.Add(new FoldRange(o.LineEnd, lastContent.End, lastContent.Line - o.Line));
                }
            }
        }

        while (at < text.Length)
        {
            lineNumber++;
            var end = at;
            while (end < text.Length && text[end] is not ('\r' or '\n'))
            {
                end++;
            }

            var indent = 0;
            while (at + indent < end && text[at + indent] == ' ')
            {
                indent++;
            }

            var first = at + indent < end ? text[at + indent] : '\0';
            if (first is not ('\0' or '#'))
            {
                Close(indent);
                open.Push((indent, lineNumber, end));
                lastContent = (lineNumber, end);
            }

            at = end < text.Length && text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : end + 1;
        }

        Close(0);
        folds.Sort((a, b) => a.Start.CompareTo(b.Start));
        return folds;
    }

    public static string? Format(string text, int step)
    {
        if (string.IsNullOrWhiteSpace(text) || Events(text) is not { } before)
        {
            return null;
        }

        var lines = SplitLines(text);
        var indents = new int[lines.Count];
        var oldIndents = new int[lines.Count];
        var kinds = new LineKind[lines.Count];
        var stack = new List<(int Old, int New)> { (0, 0) };
        var block = (On: false, ParentOld: 0, Delta: 0, Explicit: false, ParentNew: 0, Started: false);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Text;
            var lead = 0;
            while (lead < line.Length && line[lead] == ' ')
            {
                lead++;
            }

            oldIndents[i] = lead;
            var blank = lead == line.Length || string.IsNullOrWhiteSpace(line[lead..]);
            if (block.On && (blank || lead > block.ParentOld))
            {
                kinds[i] = LineKind.Kept;
                if (!blank)
                {
                    if (!block.Started)
                    {
                        var target = block.Explicit ? block.ParentNew + (lead - block.ParentOld) : block.ParentNew + step;
                        block.Delta = target - lead;
                        block.Started = true;
                    }

                    indents[i] = lead + block.Delta;
                    kinds[i] = LineKind.Moved;
                }

                continue;
            }

            block.On = false;
            if (blank)
            {
                kinds[i] = LineKind.Kept;
                continue;
            }

            if (line[lead] == '#')
            {
                kinds[i] = LineKind.Comment;
                continue;
            }

            if (lead == 0 && (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("...", StringComparison.Ordinal) || line[0] == '%'))
            {
                stack.RemoveRange(1, stack.Count - 1);
            }

            while (stack.Count > 1 && stack[^1].Old > lead)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            int newLead;
            if (stack[^1].Old == lead)
            {
                newLead = stack[^1].New;
            }
            else
            {
                newLead = stack[^1].New + step;
                stack.Add((lead, newLead));
            }

            indents[i] = newLead;
            kinds[i] = LineKind.Moved;

            var pos = lead;
            var newPos = newLead;
            var lastDash = -1;
            var lastDashNew = -1;
            while (pos < line.Length && line[pos] == '-' && pos + 1 < line.Length && line[pos + 1] == ' ')
            {
                var gap = 1;
                while (pos + gap < line.Length && line[pos + gap] == ' ')
                {
                    gap++;
                }

                lastDash = pos;
                lastDashNew = newPos;
                pos += gap;
                newPos += gap;
                if (pos < line.Length)
                {
                    stack.Add((pos, newPos));
                }
            }

            var match = BlockPattern().Match(line, pos);
            if (match.Success)
            {
                var direct = match.Groups["key"].Length == 0 && lastDash >= 0;
                block = (true, direct ? lastDash : pos, 0, match.Groups["n"].Success, direct ? lastDashNew : newPos, false);
            }
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (kinds[i] != LineKind.Comment)
            {
                continue;
            }

            int Near(int from, int by)
            {
                for (var j = from; j >= 0 && j < lines.Count; j += by)
                {
                    if (kinds[j] == LineKind.Moved)
                    {
                        return j;
                    }
                }

                return -1;
            }

            var next = Near(i + 1, 1);
            var previous = Near(i - 1, -1);
            if (next >= 0 && oldIndents[next] == oldIndents[i])
            {
                indents[i] = indents[next];
            }
            else if (previous >= 0 && oldIndents[previous] == oldIndents[i])
            {
                indents[i] = indents[previous];
            }
            else
            {
                indents[i] = oldIndents[i];
            }

            kinds[i] = LineKind.Moved;
        }

        var output = new StringBuilder(text.Length + (text.Length / 4));
        for (var i = 0; i < lines.Count; i++)
        {
            var (line, end) = lines[i];
            if (kinds[i] == LineKind.Moved)
            {
                if (indents[i] < 0)
                {
                    return null;
                }

                output.Append(' ', indents[i]).Append(line, oldIndents[i], line.Length - oldIndents[i]);
            }
            else
            {
                output.Append(line);
            }

            output.Append(end);
        }

        var formatted = output.ToString();
        return Events(formatted) is { } after && after.SequenceEqual(before) ? formatted : null;
    }

    private enum LineKind : byte { Kept, Moved, Comment }

    [GeneratedRegex(@"\G(?<key>(?:[^#\s][^#]*?:\s+))?(?:[&!]\S*\s+)*[|>](?<n>[1-9])?[+-]?(?:(?<n>[1-9])?)\s*(?:#.*)?$")]
    private static partial Regex BlockPattern();

    private static List<(string Text, string End)> SplitLines(string text)
    {
        var lines = new List<(string, string)>();
        var at = 0;
        while (at <= text.Length)
        {
            var end = at;
            while (end < text.Length && text[end] is not ('\r' or '\n'))
            {
                end++;
            }

            var breakEnd = end < text.Length && text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : Math.Min(text.Length, end + 1);
            lines.Add((text[at..end], text[end..breakEnd]));
            if (breakEnd == end)
            {
                break;
            }

            at = breakEnd;
        }

        return lines;
    }

    private static List<string>? Events(string text)
    {
        var events = new List<string>();
        try
        {
            var parser = new Parser(new StringReader(text));
            while (parser.MoveNext())
            {
                events.Add(parser.Current switch
                {
                    Scalar s => string.Create(CultureInfo.InvariantCulture, $"s|{s.Anchor}|{s.Tag}|{s.Value}"),
                    AnchorAlias a => "a|" + a.Value,
                    MappingStart m => string.Create(CultureInfo.InvariantCulture, $"m|{m.Anchor}|{m.Tag}"),
                    SequenceStart q => string.Create(CultureInfo.InvariantCulture, $"q|{q.Anchor}|{q.Tag}"),
                    var other => other?.GetType().Name ?? "",
                });
            }
        }
        catch (Exception e) when (e is YamlException or InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }

        return events;
    }
}
