// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Editing/JsonText.cs

using System.Text;
using Salandual.Core.Resources;

namespace Salandual.Core.Editing;

public static class JsonText
{
    private const int SnippetLength = 30;

    public static bool AllowsComments(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.EndsWith(".jsonc", StringComparison.Ordinal)
            || lower is "settings.json" or "launch.json" or "tasks.json" or "keybindings.json" or "extensions.json" or "devcontainer.json" or ".devcontainer.json" or "argv.json"
            || ((lower.StartsWith("tsconfig", StringComparison.Ordinal) || lower.StartsWith("jsconfig", StringComparison.Ordinal)) && lower.EndsWith(".json", StringComparison.Ordinal));
    }

    public static CheckResult Check(string text, bool allowComments) => Read(text, allowComments, duplicates: true, folds: true);

    public static string? Format(string text, string unit, string newline, bool allowComments, string prefix = "")
    {
        if (string.IsNullOrWhiteSpace(text) || Read(text, allowComments, duplicates: false, folds: false).Problems.Count > 0)
        {
            return null;
        }

        var output = new StringBuilder(text.Length + (text.Length / 4));
        var scanner = new Scanner(text);
        var depth = 0;
        var pendingBreak = false;
        var any = false;
        void Break()
        {
            output.Append(newline).Append(prefix);
            for (var i = 0; i < depth; i++)
            {
                output.Append(unit);
            }
        }

        while (scanner.Next(out var t))
        {
            switch (t.Kind)
            {
                case Kind.BeginObject or Kind.BeginArray:
                    if (pendingBreak)
                    {
                        Break();
                        pendingBreak = false;
                    }

                    output.Append(text[t.Start]);
                    var ahead = scanner;
                    if (ahead.Next(out var next) && next.Kind == (t.Kind == Kind.BeginObject ? Kind.EndObject : Kind.EndArray))
                    {
                        output.Append(text[next.Start]);
                        scanner = ahead;
                    }
                    else
                    {
                        depth++;
                        pendingBreak = true;
                    }

                    break;
                case Kind.EndObject or Kind.EndArray:
                    depth = Math.Max(0, depth - 1);
                    Break();
                    pendingBreak = false;
                    output.Append(text[t.Start]);
                    break;
                case Kind.Comma:
                    output.Append(',');
                    pendingBreak = true;
                    break;
                case Kind.Colon:
                    output.Append(": ");
                    break;
                case Kind.LineComment:
                    if (!t.NewlineBefore && any)
                    {
                        output.Append(' ');
                    }
                    else if (any)
                    {
                        Break();
                    }

                    output.Append(text, t.Start, t.Length);
                    pendingBreak = true;
                    break;
                case Kind.BlockComment:
                    if (t.NewlineBefore && any)
                    {
                        Break();
                        output.Append(text, t.Start, t.Length);
                        pendingBreak = true;
                    }
                    else
                    {
                        if (pendingBreak)
                        {
                            Break();
                            pendingBreak = false;
                        }
                        else if (output.Length > 0 && output[^1] != ' ')
                        {
                            output.Append(' ');
                        }

                        output.Append(text, t.Start, t.Length).Append(' ');
                    }

                    break;
                default:
                    if (pendingBreak)
                    {
                        Break();
                        pendingBreak = false;
                    }

                    output.Append(text, t.Start, t.Length);
                    break;
            }

            any = true;
        }

        if (text.EndsWith('\n'))
        {
            output.Append(newline);
        }

        return output.ToString();
    }

    public static string? Minify(string text, bool allowComments)
    {
        if (string.IsNullOrWhiteSpace(text) || Read(text, allowComments, duplicates: false, folds: false).Problems.Count > 0)
        {
            return null;
        }

        var output = new StringBuilder(text.Length);
        var scanner = new Scanner(text);
        while (scanner.Next(out var t))
        {
            if (t.Kind == Kind.LineComment)
            {
                var body = text.AsSpan(t.Start + 2, t.Length - 2).TrimEnd();
                if (body.Contains("*/", StringComparison.Ordinal))
                {
                    output.Append(text, t.Start, t.Length).Append('\n');
                }
                else
                {
                    output.Append("/*").Append(body).Append("*/");
                }
            }
            else
            {
                output.Append(text, t.Start, t.Length);
            }
        }

        return output.ToString();
    }

    public enum SortRefusal { None, Mistake, Comments, NotObject, TooDeep }

    public static string? SortKeys(string text, string unit, string newline, string prefix, out SortRefusal refusal)
    {
        refusal = SortRefusal.Mistake;
        if (string.IsNullOrWhiteSpace(text) || Read(text, allowComments: true, duplicates: false, folds: false).Problems.Count > 0)
        {
            return null;
        }

        var scanner = new Scanner(text);
        var tokens = new List<Token>();
        while (scanner.Next(out var t))
        {
            if (t.Kind is Kind.LineComment or Kind.BlockComment)
            {
                refusal = SortRefusal.Comments;
                return null;
            }

            tokens.Add(t);
        }

        if (tokens.Count == 0 || tokens[0].Kind is not (Kind.BeginObject or Kind.BeginArray))
        {
            refusal = SortRefusal.NotObject;
            return null;
        }

        var index = 0;
        var compact = new StringBuilder(text.Length);
        if (!WriteSorted(text, tokens, ref index, compact, 0))
        {
            refusal = SortRefusal.TooDeep;
            return null;
        }

        refusal = SortRefusal.None;
        var formatted = Format(compact.ToString(), unit, newline, allowComments: true, prefix)!;
        return text.EndsWith('\n') && !formatted.EndsWith('\n') ? formatted + newline : formatted;
    }

    private const int SortDepth = 512;

    private static bool WriteSorted(string text, List<Token> tokens, ref int index, StringBuilder output, int depth)
    {
        if (depth > SortDepth)
        {
            return false;
        }

        var t = tokens[index++];
        if (t.Kind == Kind.BeginArray)
        {
            output.Append('[');
            var first = true;
            while (tokens[index].Kind != Kind.EndArray)
            {
                if (tokens[index].Kind == Kind.Comma)
                {
                    index++;
                    continue;
                }

                if (!first)
                {
                    output.Append(',');
                }

                if (!WriteSorted(text, tokens, ref index, output, depth + 1))
                {
                    return false;
                }

                first = false;
            }

            index++;
            output.Append(']');
            return true;
        }

        if (t.Kind != Kind.BeginObject)
        {
            output.Append(text, t.Start, t.Length);
            return true;
        }

        var members = new List<(string Key, string Value)>();
        while (tokens[index].Kind != Kind.EndObject)
        {
            if (tokens[index].Kind == Kind.Comma)
            {
                index++;
                continue;
            }

            var key = tokens[index++];
            index++;
            var value = new StringBuilder();
            if (!WriteSorted(text, tokens, ref index, value, depth + 1))
            {
                return false;
            }

            members.Add((text.Substring(key.Start, key.Length), value.ToString()));
        }

        index++;
        members.Sort((a, b) =>
        {
            var c = string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(b.Key, a.Key);
        });
        output.Append('{');
        for (var i = 0; i < members.Count; i++)
        {
            if (i > 0)
            {
                output.Append(',');
            }

            output.Append(members[i].Key).Append(':').Append(members[i].Value);
        }

        output.Append('}');
        return true;
    }

    private enum State : byte { Value, KeyOrEnd, Key, Colon, ValueOrEnd, CommaOrEnd, Done }

    private sealed class Frame(bool isObject, int open)
    {
        public bool IsObject { get; } = isObject;

        public int Open { get; } = open;

        public State State { get; set; } = isObject ? State.KeyOrEnd : State.ValueOrEnd;

        public Dictionary<string, int>? Keys { get; set; }

        public int LastComma { get; set; } = -1;
    }

    internal static TextProblem? FirstMistake(string text, bool allowComments) => Read(text, allowComments, duplicates: false, folds: false).Problems is [var first, ..] ? first : null;

    private static CheckResult Read(string text, bool allowComments, bool duplicates, bool folds)
    {
        var lines = new LineMap(text);
        var found = new List<FoldRange>();
        var problems = new List<TextProblem>();
        var stack = new Stack<Frame>();
        var root = State.Value;
        Token previous = default;
        var scanner = new Scanner(text);
        CheckResult Fail(int offset, int length, string message)
        {
            problems.Insert(0, TextProblem.At(text, offset, length, message));
            return new CheckResult(problems, found);
        }

        string Snippet(Token t)
        {
            var s = text.Substring(t.Start, Math.Min(t.Length, SnippetLength));
            return t.Length > SnippetLength ? s + "…" : s;
        }

        while (scanner.Next(out var t))
        {
            if (t.Error is { } error)
            {
                return Fail(t.ErrorOffset, t.ErrorLength, error);
            }

            if (t.Kind is Kind.LineComment or Kind.BlockComment)
            {
                if (!allowComments)
                {
                    return Fail(t.Start, Math.Min(t.Length, 2), Strings.Json_Comment);
                }

                continue;
            }

            var top = stack.Count > 0 ? stack.Peek() : null;
            var state = top?.State ?? root;
            var isValue = t.Kind is Kind.String or Kind.Number or Kind.Word or Kind.BeginObject or Kind.BeginArray;
            var isEnd = t.Kind is Kind.EndObject or Kind.EndArray;

            var canClose = state is State.KeyOrEnd or State.ValueOrEnd or State.CommaOrEnd or State.Key || (state == State.Value && top is { IsObject: false });
            if (isEnd && top is not null && canClose)
            {
                var wanted = top.IsObject ? Kind.EndObject : Kind.EndArray;
                if (t.Kind != wanted)
                {
                    return Fail(t.Start, 1, Loc.Format(Strings.Json_Mismatch, text[t.Start], top.IsObject ? '{' : '[', lines.Line(top.Open), top.IsObject ? '}' : ']'));
                }

                if (state is State.Key or State.Value)
                {
                    if (!allowComments)
                    {
                        return Fail(top.LastComma, 1, Strings.Json_TrailingComma);
                    }
                }

                stack.Pop();
                var openLine = lines.Line(top.Open);
                var closeLine = lines.Line(t.Start);
                if (folds && closeLine - openLine >= 1)
                {
                    found.Add(new FoldRange(top.Open + 1, t.Start, closeLine - openLine - 1));
                }

                AfterValue();
                previous = t;
                continue;
            }

            switch (state)
            {
                case State.Value or State.ValueOrEnd when isValue:
                    StartValue(t);
                    break;
                case State.Value or State.ValueOrEnd:
                    return Fail(t.Start, Math.Max(1, t.Length), isEnd && top is null ? Loc.Format(Strings.Json_NothingToClose, text[t.Start]) : Strings.Json_ExpectedValue);
                case State.KeyOrEnd or State.Key when t.Kind == Kind.String:
                    if (duplicates)
                    {
                        top!.Keys ??= new Dictionary<string, int>(StringComparer.Ordinal);
                        var key = text.Substring(t.Start, t.Length);
                        if (top.Keys.TryGetValue(key, out var firstAt))
                        {
                            problems.Add(TextProblem.At(text, t.Start, t.Length, Loc.Format(Strings.Json_Duplicate, Snippet(t), lines.Line(firstAt))));
                        }
                        else
                        {
                            top.Keys[key] = t.Start;
                        }
                    }

                    top!.State = State.Colon;
                    break;
                case State.KeyOrEnd or State.Key:
                    return Fail(t.Start, Math.Max(1, t.Length), Strings.Json_ExpectedName);
                case State.Colon when t.Kind == Kind.Colon:
                    top!.State = State.Value;
                    break;
                case State.Colon:
                    return Fail(previous.Start, previous.Length, Loc.Format(Strings.Json_MissingColon, Snippet(previous)));
                case State.CommaOrEnd when t.Kind == Kind.Comma:
                    top!.State = top.IsObject ? State.Key : State.Value;
                    top.LastComma = t.Start;
                    break;
                case State.CommaOrEnd when isValue:
                    return Fail(t.Start, Math.Max(1, t.Length), Loc.Format(Strings.Json_MissingComma, Snippet(previous), lines.Line(previous.Start), Snippet(t)));
                case State.CommaOrEnd:
                    return Fail(t.Start, Math.Max(1, t.Length), Loc.Format(Strings.Json_BadChar, Snippet(t)));
                case State.Done:
                    return Fail(t.Start, Math.Max(1, t.Length), isEnd ? Loc.Format(Strings.Json_NothingToClose, text[t.Start]) : Loc.Format(Strings.Json_AfterEnd, Snippet(t)));
            }

            previous = t;
        }

        if (stack.Count > 0)
        {
            var open = stack.Peek();
            return Fail(open.Open, 1, Loc.Format(Strings.Json_Unclosed, open.IsObject ? '{' : '[', lines.Line(open.Open), open.IsObject ? '}' : ']'));
        }

        return new CheckResult(problems, found);

        void StartValue(Token t)
        {
            if (t.Kind is Kind.BeginObject or Kind.BeginArray)
            {
                if (stack.Count > 0)
                {
                    stack.Peek().State = State.CommaOrEnd;
                }
                else
                {
                    root = State.Done;
                }

                stack.Push(new Frame(t.Kind == Kind.BeginObject, t.Start));
                return;
            }

            AfterValue();
        }

        void AfterValue()
        {
            if (stack.Count > 0)
            {
                stack.Peek().State = State.CommaOrEnd;
            }
            else
            {
                root = State.Done;
            }
        }
    }

    internal enum Kind : byte { BeginObject, EndObject, BeginArray, EndArray, Comma, Colon, String, Number, Word, LineComment, BlockComment, Bad }

    internal readonly record struct Token(Kind Kind, int Start, int Length, bool NewlineBefore, string? Error = null, int ErrorOffset = 0, int ErrorLength = 0);

    internal struct Scanner(string text)
    {
        private int _at = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

        public bool Next(out Token token)
        {
            var newline = false;
            while (_at < text.Length && text[_at] is ' ' or '\t' or '\r' or '\n')
            {
                newline |= text[_at] is '\r' or '\n';
                _at++;
            }

            if (_at >= text.Length)
            {
                token = default;
                return false;
            }

            var start = _at;
            var c = text[_at];
            switch (c)
            {
                case '{': token = One(Kind.BeginObject, start, newline); break;
                case '}': token = One(Kind.EndObject, start, newline); break;
                case '[': token = One(Kind.BeginArray, start, newline); break;
                case ']': token = One(Kind.EndArray, start, newline); break;
                case ',': token = One(Kind.Comma, start, newline); break;
                case ':': token = One(Kind.Colon, start, newline); break;
                case '"': token = ReadString(start, newline); break;
                case '\'':
                    _at++;
                    while (_at < text.Length && text[_at] is not ('\'' or '\r' or '\n'))
                    {
                        _at++;
                    }

                    _at = Math.Min(text.Length, _at + 1);
                    token = new Token(Kind.Bad, start, _at - start, newline, Strings.Json_SingleQuote, start, 1);
                    break;
                case '/' when _at + 1 < text.Length && text[_at + 1] == '/':
                    while (_at < text.Length && text[_at] is not ('\r' or '\n'))
                    {
                        _at++;
                    }

                    token = new Token(Kind.LineComment, start, _at - start, newline);
                    break;
                case '/' when _at + 1 < text.Length && text[_at + 1] == '*':
                    var close = text.IndexOf("*/", _at + 2, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        _at = text.Length;
                        token = new Token(Kind.Bad, start, _at - start, newline, Strings.Json_CommentOpen, start, 2);
                    }
                    else
                    {
                        _at = close + 2;
                        token = new Token(Kind.BlockComment, start, _at - start, newline);
                    }

                    break;
                case '-' or (>= '0' and <= '9'):
                    while (_at < text.Length && text[_at] is (>= '0' and <= '9') or '-' or '+' or '.' or 'e' or 'E')
                    {
                        _at++;
                    }

                    token = IsNumber(text.AsSpan(start, _at - start))
                        ? new Token(Kind.Number, start, _at - start, newline)
                        : new Token(Kind.Bad, start, _at - start, newline, Loc.Format(Strings.Json_BadNumber, text[start.._at]), start, _at - start);
                    break;
                default:
                    if (char.IsLetter(c) || c is '_' or '$')
                    {
                        while (_at < text.Length && (char.IsLetterOrDigit(text[_at]) || text[_at] is '_' or '$'))
                        {
                            _at++;
                        }

                        var word = text.AsSpan(start, _at - start);
                        token = word is "true" or "false" or "null"
                            ? new Token(Kind.Word, start, _at - start, newline)
                            : new Token(Kind.Bad, start, _at - start, newline, Loc.Format(Strings.Json_BadWord, word.Length > SnippetLength ? string.Concat(word[..SnippetLength], "…") : word.ToString()), start, _at - start);
                    }
                    else
                    {
                        _at += char.IsSurrogatePair(text, _at) ? 2 : 1;
                        token = new Token(Kind.Bad, start, _at - start, newline, Loc.Format(Strings.Json_BadChar, text[start.._at]), start, _at - start);
                    }

                    break;
            }

            return true;
        }

        private Token One(Kind kind, int start, bool newline)
        {
            _at++;
            return new Token(kind, start, 1, newline);
        }

        private Token ReadString(int start, bool newline)
        {
            _at++;
            string? error = null;
            int errorAt = 0, errorLength = 0;
            while (true)
            {
                if (_at >= text.Length || text[_at] is '\r' or '\n')
                {
                    return new Token(Kind.Bad, start, _at - start, newline, error ?? Strings.Json_StringOpen, error is null ? start : errorAt, error is null ? 1 : errorLength);
                }

                var c = text[_at];
                if (c == '"')
                {
                    _at++;
                    return new Token(error is null ? Kind.String : Kind.Bad, start, _at - start, newline, error, errorAt, errorLength);
                }

                if (c == '\\')
                {
                    var length = 2;
                    var valid = _at + 1 < text.Length && text[_at + 1] is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't';
                    if (_at + 1 < text.Length && text[_at + 1] == 'u')
                    {
                        length = 2;
                        while (length < 6 && _at + length < text.Length && char.IsAsciiHexDigit(text[_at + length]))
                        {
                            length++;
                        }

                        valid = length == 6;
                    }

                    if (!valid && error is null)
                    {
                        errorAt = _at;
                        errorLength = Math.Min(length, text.Length - _at);
                        error = Loc.Format(Strings.Json_BadEscape, text.Substring(_at, errorLength));
                    }

                    _at = Math.Min(text.Length, _at + length);
                    continue;
                }

                if (c < ' ' && error is null)
                {
                    errorAt = _at;
                    errorLength = 1;
                    error = Strings.Json_ControlChar;
                }

                _at++;
            }
        }

        private static bool IsNumber(ReadOnlySpan<char> s)
        {
            var i = 0;
            if (i < s.Length && s[i] == '-')
            {
                i++;
            }

            if (i >= s.Length || !char.IsAsciiDigit(s[i]))
            {
                return false;
            }

            if (s[i] == '0')
            {
                i++;
            }
            else
            {
                while (i < s.Length && char.IsAsciiDigit(s[i]))
                {
                    i++;
                }
            }

            if (i < s.Length && s[i] == '.')
            {
                i++;
                if (i >= s.Length || !char.IsAsciiDigit(s[i]))
                {
                    return false;
                }

                while (i < s.Length && char.IsAsciiDigit(s[i]))
                {
                    i++;
                }
            }

            if (i < s.Length && s[i] is 'e' or 'E')
            {
                i++;
                if (i < s.Length && s[i] is '+' or '-')
                {
                    i++;
                }

                if (i >= s.Length || !char.IsAsciiDigit(s[i]))
                {
                    return false;
                }

                while (i < s.Length && char.IsAsciiDigit(s[i]))
                {
                    i++;
                }
            }

            return i == s.Length;
        }
    }
}
