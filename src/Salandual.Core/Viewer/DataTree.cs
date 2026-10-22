// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/DataTree.cs

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Salandual.Core.Editing;
using Salandual.Core.Resources;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Salandual.Core.Viewer;

public enum DataFormat { Json, JsonLines, Yaml, Xml }

public enum DataKind : byte { Object, Array, String, Number, Boolean, Null, Element, Attribute, Text, Alias }

public enum DataPathStyle { Dollar, Dotted, Slash }

public sealed class DataNode
{
    internal List<DataNode>? List;

    internal string? Own;

    internal int ValueStart;

    internal int ValueLength;

    internal int Start;

    internal int End = -1;

    public string? Name { get; internal set; }

    public int Index { get; internal set; }

    public DataKind Kind { get; internal set; }

    public DataNode? Parent { get; internal set; }

    public int Line { get; internal set; }

    public int Offset => Start;

    public IReadOnlyList<DataNode> Children => (IReadOnlyList<DataNode>?)List ?? [];

    public int Count => List?.Count ?? 0;

    public bool CanOpen => List is { Count: > 0 };

    public bool IsBranch => Kind is DataKind.Object or DataKind.Array || (Kind == DataKind.Element && Own is null);

    internal void Add(DataNode child)
    {
        child.Parent = this;
        (List ??= []).Add(child);
    }
}

public sealed partial class DataTree
{
    private readonly string _text;

    private DataTree(string text, DataFormat format)
    {
        _text = text;
        Format = format;
        Root = new DataNode { Kind = DataKind.Null, Line = 1 };
    }

    public DataFormat Format { get; }

    public DataNode Root { get; private set; }

    public TextProblem? Problem { get; private set; }

    public int Values { get; private set; }

    public int Levels { get; private set; }

    public static DataFormat? FormatFor(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".json" or ".jsonc" or ".geojson" or ".webmanifest" => DataFormat.Json,
            ".jsonl" or ".ndjson" => DataFormat.JsonLines,
            ".yaml" or ".yml" => DataFormat.Yaml,
            ".xml" or ".xaml" or ".axaml" or ".csproj" or ".slnx" or ".vbproj" or ".vcxproj" or ".fsproj" or ".props" or ".targets" or ".config" or ".resx" or ".nuspec" or ".xsd" or ".xsl" or ".xslt" or ".plist" or ".svg" => DataFormat.Xml,
            _ => null,
        };
    }

    public static DataTree Read(string text, DataFormat format, bool allowComments = false, CancellationToken token = default)
    {
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var tree = new DataTree(text, format);
        switch (format)
        {
            case DataFormat.Json:
                tree.ReadJson(allowComments, token);
                break;
            case DataFormat.JsonLines:
                tree.ReadJsonLines(token);
                break;
            case DataFormat.Yaml:
                tree.ReadYaml(token);
                break;
            default:
                tree.ReadXml(token);
                break;
        }

        if (tree.Problem is null)
        {
            tree.Count();
        }

        return tree;
    }

    public string Value(DataNode node)
    {
        if (node.Own is { } own)
        {
            return own;
        }

        if (node.IsBranch || node.ValueLength <= 0)
        {
            return "";
        }

        return node.Kind == DataKind.String
            ? Unescape(_text.AsSpan(node.ValueStart + 1, Math.Max(0, node.ValueLength - 2)))
            : _text.Substring(node.ValueStart, node.ValueLength);
    }

    public string Shown(DataNode node, int max = 300)
    {
        string text;
        if (node.Own is null && node.Kind == DataKind.String && Format is DataFormat.Json or DataFormat.JsonLines)
        {
            var inner = Unescape(_text.AsSpan(node.ValueStart + 1, Math.Max(0, Math.Min(node.ValueLength - 2, max * 2))));
            text = "\"" + inner + (node.ValueLength - 2 > max * 2 ? "…" : "\"");
        }
        else
        {
            text = Value(node);
        }

        if (text.Length > max)
        {
            text = string.Concat(text.AsSpan(0, max), "…");
        }

        return text.ReplaceLineEndings("⏎").Replace('\t', ' ');
    }

    public string CopyValue(DataNode node) => node.IsBranch ? Branch(node) : Value(node);

    public string Branch(DataNode node)
    {
        if (node.End < 0 || node.End <= node.Start || node.End > _text.Length)
        {
            return Value(node);
        }

        var slice = _text[node.Start..node.End].TrimEnd();
        var lineStart = node.Start;
        while (lineStart > 0 && _text[lineStart - 1] is not ('\n' or '\r'))
        {
            lineStart--;
        }

        var indent = 0;
        while (lineStart + indent < node.Start && _text[lineStart + indent] is ' ' or '	')
        {
            indent++;
        }

        if (indent == 0 || !slice.Contains('\n', StringComparison.Ordinal))
        {
            return slice;
        }

        var lines = slice.Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var n = 0;
            while (n < indent && n < lines[i].Length && lines[i][n] is ' ' or '\t')
            {
                n++;
            }

            lines[i] = lines[i][n..];
        }

        return string.Join('\n', lines);
    }

    public string PathOf(DataNode node, DataPathStyle style)
    {
        var chain = new List<DataNode>();
        for (var at = node; at.Parent is not null; at = at.Parent)
        {
            chain.Add(at);
        }

        chain.Reverse();
        var path = new StringBuilder();
        if (Format == DataFormat.Xml)
        {
            path.Append('/').Append(Root.Name);
            if (Root.Kind == DataKind.Element && Root.Index > 0)
            {
                path.Append('[').Append(Root.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
            }

            foreach (var n in chain)
            {
                switch (n.Kind)
                {
                    case DataKind.Attribute: path.Append("/@").Append(n.Name); break;
                    case DataKind.Text: path.Append("/text()"); break;
                    default:
                        path.Append('/').Append(n.Name);
                        if (n.Index > 0)
                        {
                            path.Append('[').Append(n.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
                        }

                        break;
                }
            }

            return path.ToString();
        }

        if (style == DataPathStyle.Dollar)
        {
            path.Append('$');
        }

        foreach (var n in chain)
        {
            var key = n.Name;
            if (style == DataPathStyle.Slash)
            {
                path.Append('/').Append(key is null ? n.Index.ToString(CultureInfo.InvariantCulture) : key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
            }
            else if (key is null)
            {
                path.Append('[').Append(n.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
            else if (PlainKey().IsMatch(key))
            {
                if (path.Length > 0)
                {
                    path.Append('.');
                }

                path.Append(key);
            }
            else
            {
                path.Append("['").Append(key.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)).Append("']");
            }
        }

        return path.Length == 0 && style == DataPathStyle.Slash ? "/" : path.ToString();
    }

    [GeneratedRegex(@"^[A-Za-z_$][A-Za-z0-9_$]*$")]
    private static partial Regex PlainKey();

    public IEnumerable<DataNode> Walk()
    {
        var stack = new Stack<DataNode>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            if (node.List is { } list)
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    stack.Push(list[i]);
                }
            }
        }
    }

    public List<DataNode> Find(Regex match, CancellationToken token = default)
    {
        var found = new List<DataNode>();
        var n = 0;
        foreach (var node in Walk())
        {
            if ((++n & 0xFFF) == 0)
            {
                token.ThrowIfCancellationRequested();
            }

            if ((node.Name is { } name && match.IsMatch(name)) || (!node.IsBranch && match.IsMatch(Value(node))))
            {
                found.Add(node);
            }
        }

        return found;
    }

    private void Count()
    {
        var values = 0;
        var levels = 0;
        var stack = new Stack<(DataNode Node, int Depth)>();
        stack.Push((Root, 0));
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            levels = Math.Max(levels, depth);
            if (!node.IsBranch)
            {
                values++;
            }

            if (node.List is { } list)
            {
                foreach (var child in list)
                {
                    stack.Push((child, depth + 1));
                }
            }
        }

        Values = values;
        Levels = levels;
    }

    private void ReadJson(bool allowComments, CancellationToken token)
    {
        if (JsonText.FirstMistake(_text, allowComments) is { } mistake)
        {
            Problem = mistake;
            return;
        }

        var lines = new LineCounter(_text);
        if (BuildJson(_text, 0, lines, token) is { } root)
        {
            Root = root;
        }
    }

    private void ReadJsonLines(CancellationToken token)
    {
        var root = new DataNode { Kind = DataKind.Array, Line = 1, Start = 0, End = _text.Length };
        var line = 0;
        for (var at = 0; at <= _text.Length;)
        {
            token.ThrowIfCancellationRequested();
            line++;
            var end = _text.IndexOfAny(['\r', '\n'], at);
            if (end < 0)
            {
                end = _text.Length;
            }

            var record = _text[at..end];
            if (!string.IsNullOrWhiteSpace(record))
            {
                if (JsonText.FirstMistake(record, allowComments: false) is { } mistake)
                {
                    Problem = new TextProblem(at + mistake.Offset, mistake.Length, line, mistake.Column, mistake.Message);
                    return;
                }

                var lines = new LineCounter(record) { First = line };
                if (BuildJson(record, at, lines, token) is { } node)
                {
                    node.Index = root.Count;
                    root.Add(node);
                }
            }

            if (end >= _text.Length)
            {
                break;
            }

            at = end + (_text[end] == '\r' && end + 1 < _text.Length && _text[end + 1] == '\n' ? 2 : 1);
        }

        Root = root;
    }

    private static DataNode? BuildJson(string text, int shift, LineCounter lines, CancellationToken token)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var stack = new List<(DataNode Node, bool ExpectKey)>();
        string? key = null;
        DataNode? root = null;
        var scanner = new JsonText.Scanner(text);
        var n = 0;
        while (scanner.Next(out var t))
        {
            if ((++n & 0xFFFF) == 0)
            {
                token.ThrowIfCancellationRequested();
            }

            if (t.Kind is JsonText.Kind.Comma or JsonText.Kind.Colon or JsonText.Kind.LineComment or JsonText.Kind.BlockComment)
            {
                continue;
            }

            if (t.Kind is JsonText.Kind.EndObject or JsonText.Kind.EndArray)
            {
                var (closed, _) = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                closed.End = shift + t.Start + 1;
                Completed();
                continue;
            }

            if (stack.Count > 0 && stack[^1].ExpectKey && t.Kind == JsonText.Kind.String)
            {
                var name = Unescape(text.AsSpan(t.Start + 1, t.Length - 2));
                if (!keys.TryGetValue(name, out var same))
                {
                    keys[name] = same = name;
                }

                key = same;
                stack[^1] = (stack[^1].Node, false);
                continue;
            }

            var node = new DataNode
            {
                Kind = t.Kind switch
                {
                    JsonText.Kind.BeginObject => DataKind.Object,
                    JsonText.Kind.BeginArray => DataKind.Array,
                    JsonText.Kind.String => DataKind.String,
                    JsonText.Kind.Number => DataKind.Number,
                    _ => text[t.Start] == 'n' ? DataKind.Null : DataKind.Boolean,
                },
                Line = lines.LineAt(t.Start),
                Start = shift + t.Start,
            };
            if (stack.Count > 0)
            {
                var parent = stack[^1].Node;
                if (parent.Kind == DataKind.Object)
                {
                    node.Name = key;
                    key = null;
                }
                else
                {
                    node.Index = parent.Count;
                }

                parent.Add(node);
            }
            else
            {
                root ??= node;
            }

            if (node.Kind is DataKind.Object or DataKind.Array)
            {
                stack.Add((node, node.Kind == DataKind.Object));
            }
            else
            {
                node.ValueStart = shift + t.Start;
                node.ValueLength = t.Length;
                node.End = shift + t.Start + t.Length;
                Completed();
            }
        }

        return root;

        void Completed()
        {
            if (stack.Count > 0 && stack[^1].Node.Kind == DataKind.Object)
            {
                stack[^1] = (stack[^1].Node, true);
            }
        }
    }

    internal static string Unescape(ReadOnlySpan<char> s)
    {
        if (s.IndexOf('\\') < 0)
        {
            return s.ToString();
        }

        var b = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c != '\\' || i + 1 >= s.Length)
            {
                b.Append(c);
                continue;
            }

            var e = s[++i];
            switch (e)
            {
                case 'n': b.Append('\n'); break;
                case 'r': b.Append('\r'); break;
                case 't': b.Append('\t'); break;
                case 'b': b.Append('\b'); break;
                case 'f': b.Append('\f'); break;
                case 'u' when i + 4 < s.Length && int.TryParse(s.Slice(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code):
                    b.Append((char)code);
                    i += 4;
                    break;
                default: b.Append(e); break;
            }
        }

        return b.ToString();
    }

    private sealed class YamlFrame(DataNode node, bool isMap, bool isKey)
    {
        public DataNode Node { get; } = node;

        public bool IsMap { get; } = isMap;

        public bool IsKey { get; } = isKey;

        public bool ExpectKey { get; set; } = true;

        public string? Key { get; set; }
    }

    private void ReadYaml(CancellationToken token)
    {
        var documents = new List<DataNode>();
        var stack = new List<YamlFrame>();
        long readTo = 0;
        try
        {
            var parser = new Parser(new StringReader(_text));
            var n = 0;
            while (parser.MoveNext())
            {
                if ((++n & 0xFFF) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                var e = parser.Current!;
                readTo = e.End.Index;
                switch (e)
                {
                    case Scalar s:
                        Place(new DataNode { Kind = Resolve(s), Own = s.Value, Line = LineOf(s.Start), Start = Offset(s.Start), End = Offset(s.End) }, s.Value);
                        break;
                    case AnchorAlias a:
                        Place(new DataNode { Kind = DataKind.Alias, Own = "*" + a.Value.Value, Line = LineOf(a.Start), Start = Offset(a.Start), End = Offset(a.End) }, "*" + a.Value.Value);
                        break;
                    case MappingStart or SequenceStart:
                        var isMap = e is MappingStart;
                        var node = new DataNode { Kind = isMap ? DataKind.Object : DataKind.Array, Line = LineOf(e.Start), Start = AfterProperties(Offset(e.Start)) };
                        var isKey = Place(node, isMap ? "{…}" : "[…]");
                        stack.Add(new YamlFrame(node, isMap, isKey));
                        break;
                    case MappingEnd or SequenceEnd:
                        stack[^1].Node.End = Offset(e.End);
                        stack.RemoveAt(stack.Count - 1);
                        break;
                }
            }
        }
        catch (YamlException e)
        {
            Problem = TextProblem.AtLine(_text, (int)Math.Clamp(e.Start.Line, 1, int.MaxValue), (int)Math.Clamp(e.Start.Column, 1, int.MaxValue), YamlText.Message(e));
            return;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            Problem = TextProblem.At(_text, (int)Math.Min(_text.Length, readTo), 1, Strings.Yaml_Unreadable);
            return;
        }

        if (documents.Count == 1)
        {
            Root = documents[0];
            Root.Name = null;
        }
        else if (documents.Count > 1)
        {
            Root = new DataNode { Kind = DataKind.Array, Line = 1, Start = 0, End = _text.Length };
            foreach (var document in documents)
            {
                document.Index = Root.Count;
                Root.Add(document);
            }
        }

        bool Place(DataNode node, string keyText)
        {
            if (stack.Count == 0)
            {
                documents.Add(node);
                return false;
            }

            var top = stack[^1];
            if (top.IsMap && top.ExpectKey)
            {
                top.Key = keyText;
                top.ExpectKey = false;
                return true;
            }

            if (top.IsMap)
            {
                node.Name = top.Key;
                top.Key = null;
                top.ExpectKey = true;
            }
            else
            {
                node.Index = top.Node.Count;
            }

            if (!top.IsKey)
            {
                top.Node.Add(node);
            }

            return false;
        }

        int AfterProperties(int at)
        {
            while (at < _text.Length && _text[at] is '&' or '!')
            {
                while (at < _text.Length && !char.IsWhiteSpace(_text[at]))
                {
                    at++;
                }

                while (at < _text.Length && char.IsWhiteSpace(_text[at]))
                {
                    at++;
                }
            }

            return at;
        }

        static int LineOf(Mark mark) => (int)Math.Clamp(mark.Line, 1, int.MaxValue);

        int Offset(Mark mark) => (int)Math.Clamp(mark.Index, 0, _text.Length);
    }

    private static DataKind Resolve(Scalar s)
    {
        if (s.Style != ScalarStyle.Plain)
        {
            return DataKind.String;
        }

        var v = s.Value;
        if (v is "" or "~" or "null" or "Null" or "NULL")
        {
            return DataKind.Null;
        }

        if (v is "true" or "True" or "TRUE" or "false" or "False" or "FALSE")
        {
            return DataKind.Boolean;
        }

        return YamlNumber().IsMatch(v) ? DataKind.Number : DataKind.String;
    }

    [GeneratedRegex(@"^([-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?|0x[0-9a-fA-F]+|0o[0-7]+|[-+]?\.(inf|Inf|INF)|\.(nan|NaN|NAN))$")]
    private static partial Regex YamlNumber();

    private void ReadXml(CancellationToken token)
    {
        var map = new LineMap(_text);
        var settings = XmlMarkup.SafeSettings();
        var stack = new List<DataNode>();
        DataNode? root = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(_text), settings);
            var info = (IXmlLineInfo)reader;
            var n = 0;
            while (reader.Read())
            {
                if ((++n & 0xFFF) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        var element = new DataNode { Kind = DataKind.Element, Name = reader.Name, Line = info.LineNumber, Start = Math.Max(0, At(info) - 1) };
                        if (stack.Count > 0)
                        {
                            stack[^1].Add(element);
                        }
                        else
                        {
                            root ??= element;
                        }

                        if (reader.MoveToFirstAttribute())
                        {
                            do
                            {
                                element.Add(new DataNode { Kind = DataKind.Attribute, Name = reader.Name, Own = reader.Value, Line = info.LineNumber, Start = At(info) });
                            }
                            while (reader.MoveToNextAttribute());

                            reader.MoveToElement();
                        }

                        if (reader.IsEmptyElement)
                        {
                            element.End = TagEnd(element.Start);
                            Finish(element);
                        }
                        else
                        {
                            stack.Add(element);
                        }

                        break;
                    case XmlNodeType.EndElement:
                        var closed = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                        closed.End = TagEnd(Math.Max(0, At(info) - 2));
                        Finish(closed);
                        break;
                    case XmlNodeType.Text or XmlNodeType.CDATA:
                        if (stack.Count > 0)
                        {
                            stack[^1].Add(new DataNode { Kind = DataKind.Text, Own = reader.Value, Line = info.LineNumber, Start = At(info) });
                        }

                        break;
                }
            }
        }
        catch (XmlException e)
        {
            Problem = TextProblem.AtLine(_text, e.LineNumber, e.LinePosition, XmlMarkup.Message(e));
            return;
        }

        if (root is null)
        {
            Problem = TextProblem.At(_text, 0, 1, Strings.Xml_NoRoot);
            return;
        }

        Root = root;

        int At(IXmlLineInfo info) => Math.Min(_text.Length, map.Start(info.LineNumber) + Math.Max(0, info.LinePosition - 1));
    }

    private static void Finish(DataNode element)
    {
        if (element.List is null)
        {
            element.Own = "";
            return;
        }

        if (element.List is [{ Kind: DataKind.Text } only])
        {
            element.Own = only.Own;
            element.List = null;
            return;
        }

        Dictionary<string, int>? counts = null;
        foreach (var child in element.List)
        {
            if (child.Kind == DataKind.Element)
            {
                counts ??= new Dictionary<string, int>(StringComparer.Ordinal);
                counts[child.Name!] = counts.GetValueOrDefault(child.Name!) + 1;
            }
        }

        if (counts is null || counts.Values.All(c => c == 1))
        {
            return;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var child in element.List)
        {
            if (child.Kind == DataKind.Element && counts[child.Name!] > 1)
            {
                child.Index = seen[child.Name!] = seen.GetValueOrDefault(child.Name!) + 1;
            }
        }
    }

    private int TagEnd(int from)
    {
        var quote = '\0';
        for (var i = from + 1; i < _text.Length; i++)
        {
            var c = _text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i + 1;
            }
        }

        return _text.Length;
    }

    private sealed class LineCounter(string text)
    {
        private int _at;
        private int _line;

        public int First { get; init; } = 1;

        public int LineAt(int offset)
        {
            for (; _at < offset && _at < text.Length; _at++)
            {
                var c = text[_at];
                if (c == '\n' || (c == '\r' && (_at + 1 >= text.Length || text[_at + 1] != '\n')))
                {
                    _line++;
                }
            }

            return First + _line;
        }
    }
}
