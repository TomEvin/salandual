// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/HtmlCleaner.cs

using System.Collections.Frozen;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Salandual.Core.Viewer;

internal static partial class HtmlCleaner
{
    private static readonly FrozenSet<string> Allowed = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "a", "abbr", "b", "bdi", "bdo", "blockquote", "br", "caption", "cite", "code", "col", "colgroup", "dd", "del", "details", "dfn",
        "div", "dl", "dt", "em", "figcaption", "figure", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "i", "img", "input", "ins", "kbd",
        "li", "mark", "ol", "p", "pre", "q", "rp", "rt", "ruby", "s", "samp", "small", "span", "strike", "strong", "sub", "summary",
        "sup", "table", "tbody", "td", "tfoot", "th", "thead", "time", "tr", "tt", "u", "ul", "var", "wbr");

    private static readonly FrozenSet<string> Dropped = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "script", "style", "template", "noscript", "iframe", "frame", "frameset", "object", "embed", "applet", "svg", "math",
        "textarea", "select", "button", "title", "head", "xmp", "noembed", "noframes", "plaintext", "canvas", "audio", "video", "map");

    private static readonly FrozenSet<string> Void = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "br", "col", "hr", "img", "input", "wbr", "area", "base", "link", "meta", "param", "source", "track", "embed");

    private static readonly FrozenSet<string> Classes = FrozenSet.Create(StringComparer.Ordinal,
        "contains-task-list", "task-list-item", "task-list-item-checkbox", "footnotes", "footnote-ref", "footnote-back-ref", "math");

    public static string Clean(string html, bool webImages, bool fragment = false)
    {
        var output = new StringBuilder(html.Length);
        var open = new List<string>();
        var i = 0;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                Text(output, html.AsSpan(i));
                break;
            }

            Text(output, html.AsSpan(i, lt - i));
            i = lt;
            if (i + 1 >= html.Length)
            {
                output.Append("&lt;");
                break;
            }

            var next = html[i + 1];
            if (next == '!' || next == '?')
            {
                var end = html.StartsWith("<!--", StringComparison.Ordinal) ? html.IndexOf("-->", i + 4, StringComparison.Ordinal) : html.IndexOf('>', i);
                i = end < 0 ? html.Length : end + (html[end] == '-' ? 3 : 1);
                continue;
            }

            var closing = next == '/';
            var nameStart = i + (closing ? 2 : 1);
            var nameEnd = nameStart;
            while (nameEnd < html.Length && (char.IsAsciiLetterOrDigit(html[nameEnd]) || html[nameEnd] == '-'))
            {
                nameEnd++;
            }

            if (nameEnd == nameStart || !char.IsAsciiLetter(html[nameStart]))
            {
                output.Append("&lt;");
                i++;
                continue;
            }

            var name = html[nameStart..nameEnd].ToLowerInvariant();
            var tagEnd = TagEnd(html, nameEnd);
            var attributes = closing ? "" : html[nameEnd..Math.Min(tagEnd, html.Length)];
            i = tagEnd < html.Length ? tagEnd + 1 : html.Length;
            if (!closing && Dropped.Contains(name))
            {
                if (!attributes.TrimEnd().EndsWith('/') && !Void.Contains(name))
                {
                    var close = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                    i = close < 0 ? html.Length : TagEnd(html, close + 2 + name.Length) + 1;
                }

                continue;
            }

            if (!Allowed.Contains(name))
            {
                continue;
            }

            if (closing)
            {
                var at = open.LastIndexOf(name);
                if (at >= 0)
                {
                    for (var k = open.Count - 1; k >= at; k--)
                    {
                        output.Append("</").Append(open[k]).Append('>');
                    }

                    open.RemoveRange(at, open.Count - at);
                }
                else if (fragment && !Void.Contains(name))
                {
                    output.Append("</").Append(name).Append('>');
                }

                continue;
            }

            if (StartTag(name, attributes, webImages) is not { } tag)
            {
                continue;
            }

            output.Append(tag);
            if (!Void.Contains(name) && !fragment)
            {
                open.Add(name);
            }
        }

        for (var k = open.Count - 1; k >= 0; k--)
        {
            output.Append("</").Append(open[k]).Append('>');
        }

        return output.ToString();
    }

    private static int TagEnd(string html, int from)
    {
        char quote = '\0';
        for (var i = from; i < html.Length; i++)
        {
            var c = html[i];
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
                return i;
            }
        }

        return html.Length;
    }

    private static void Text(StringBuilder output, ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c == '>')
            {
                output.Append("&gt;");
            }
            else
            {
                output.Append(c);
            }
        }
    }

    private static string? StartTag(string name, string attributeText, bool webImages)
    {
        var kept = new List<(string Name, string Value)>();
        foreach (Match m in AttributePattern().Matches(attributeText))
        {
            var attribute = m.Groups[1].Value.ToLowerInvariant();
            var value = WebUtility.HtmlDecode(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value);
            if (Attribute(name, attribute, value) is { } clean)
            {
                kept.Add((attribute, clean));
            }
        }

        if (name == "input")
        {
            if (!kept.Any(a => a.Name == "type" && a.Value.Equals("checkbox", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            kept.RemoveAll(a => a.Name == "disabled");
            kept.Add(("disabled", "disabled"));
        }

        if (name == "img")
        {
            var src = kept.FirstOrDefault(a => a.Name == "src").Value;
            if (src is null)
            {
                return null;
            }

            if (MarkdownPage.IsWeb(src) && !webImages)
            {
                string? Get(string n) => kept.FirstOrDefault(a => a.Name == n).Value;
                return MarkdownPage.Placeholder(src, Get("alt") ?? "", Get("width"), Get("height"));
            }
        }

        var tag = new StringBuilder("<").Append(name);
        foreach (var (attribute, value) in kept)
        {
            tag.Append(' ').Append(attribute).Append("=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
        }

        return tag.Append('>').ToString();
    }

    private static string? Attribute(string tag, string name, string value)
    {
        switch (name)
        {
            case "title" or "lang" or "dir" or "alt":
                return value;
            case "id":
                return IdPattern().IsMatch(value) ? value : null;
            case "class":
                var classes = string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(Classes.Contains));
                return classes.Length > 0 ? classes : null;
            case "style" when tag is "td" or "th":
                return AlignPattern().Match(value) is { Success: true } align ? "text-align: " + align.Groups[1].Value.ToLowerInvariant() : null;
            case "colspan" or "rowspan" or "span" or "start" or "value" when value.Length is > 0 and < 6 && value.All(char.IsAsciiDigit):
                return value;
            case "width" or "height" when tag is "img" or "col" or "td" or "th" or "table" && SizePattern().IsMatch(value):
                return value;
            case "align" when value.ToLowerInvariant() is "left" or "right" or "center" or "justify":
                return value.ToLowerInvariant();
            case "valign" when value.ToLowerInvariant() is "top" or "middle" or "bottom" or "baseline":
                return value.ToLowerInvariant();
            case "scope" when tag == "th":
                return value;
            case "type" when tag is "ol" or "input":
                return value;
            case "checked" or "disabled" when tag == "input":
                return name;
            case "open" when tag == "details":
                return "open";
            case "href" when tag == "a":
                return SafeAddress(value, pictures: false);
            case "src" when tag == "img":
                return SafeAddress(value, pictures: true);
            default:
                return null;
        }
    }

    private static string? SafeAddress(string value, bool pictures)
    {
        var address = value.Trim();
        if (address.Length == 0)
        {
            return null;
        }

        if (pictures && DataImagePattern().IsMatch(address))
        {
            return address;
        }

        if (MarkdownPage.IsWeb(address))
        {
            return address;
        }

        if (address.Any(char.IsControl))
        {
            return null;
        }

        var colon = address.IndexOf(':');
        var stop = address.IndexOfAny(['/', '?', '#', '\\']);
        return colon < 0 || (stop >= 0 && stop < colon) ? address : null;
    }

    [GeneratedRegex("""([A-Za-z_:][-A-Za-z0-9_:.]*)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+)))?""")]
    private static partial Regex AttributePattern();

    [GeneratedRegex("^[A-Za-z0-9_:.-]{1,200}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^\s*text-align\s*:\s*(left|right|center)\s*;?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AlignPattern();

    [GeneratedRegex(@"^\d{1,5}(?:px|%)?$")]
    private static partial Regex SizePattern();

    [GeneratedRegex("^data:image/(?:png|jpeg|jpg|gif|webp|bmp|svg\\+xml);base64,[A-Za-z0-9+/=\\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex DataImagePattern();
}
