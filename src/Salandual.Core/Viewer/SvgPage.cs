// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/SvgPage.cs

using System.Text;
using System.Xml;
using Salandual.Core.Editing;

namespace Salandual.Core.Viewer;

public sealed class SvgFormatException(string message, int line, int column) : FormatException(message)
{
    public int Line { get; } = line;

    public int Column { get; } = column;
}

public static class SvgPage
{
    private static readonly HashSet<string> Dropped = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "foreignObject", "iframe", "embed", "object", "audio", "video", "handler", "listener",
    };

    public static string Render(string svg, PageOptions options)
    {
        var body = Clean(svg);
        const string css = """
            main.svg { max-width: none; min-height: calc(100vh - 32px); box-sizing: border-box; padding: 16px; display: grid; place-items: center; }
            .board { display: grid; place-items: center; width: 100%; min-height: calc(100vh - 32px); border-radius: 6px;
              background: repeating-conic-gradient(var(--surface) 0 25%, var(--bg) 0 50%) 0 0 / 16px 16px; }
            .board > svg { max-width: 92%; max-height: calc(100vh - 64px); height: auto; }
            """;
        return MarkdownPage.Document("<div class=\"board\">" + body + "</div>", options with { FullWidth = true }, css: css).Replace("<main>", "<main class=\"svg\">", StringComparison.Ordinal);
    }

    internal static string Clean(string svg)
    {
        var output = new StringBuilder(svg.Length);
        var settings = XmlMarkup.SafeSettings();
        settings.IgnoreComments = true;
        settings.IgnoreWhitespace = false;
        try
        {
            using var reader = XmlReader.Create(new StringReader(svg), settings);
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, ConformanceLevel = ConformanceLevel.Fragment }))
            {
                var root = true;
                var atNext = false;
                while (atNext || reader.Read())
                {
                    atNext = false;
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                            if (Dropped.Contains(reader.LocalName) || (reader.LocalName is "set" or "animate" && IsLinkAnimation(reader)))
                            {
                                if (!reader.IsEmptyElement)
                                {
                                    reader.Skip();
                                    atNext = !reader.EOF;
                                }

                                break;
                            }

                            if (root && reader.LocalName != "svg")
                            {
                                throw new SvgFormatException(Resources.Strings.Svg_NotSvg, 1, 1);
                            }

                            root = false;
                            var empty = reader.IsEmptyElement;
                            var name = reader.LocalName == "a" ? "g" : reader.LocalName;
                            writer.WriteStartElement(reader.Prefix, name, reader.NamespaceURI);
                            if (reader.MoveToFirstAttribute())
                            {
                                do
                                {
                                    if (Keep(reader))
                                    {
                                        writer.WriteAttributeString(reader.Prefix, reader.LocalName, reader.NamespaceURI, reader.Value);
                                    }
                                }
                                while (reader.MoveToNextAttribute());
                                reader.MoveToElement();
                            }

                            if (empty)
                            {
                                writer.WriteEndElement();
                            }

                            break;
                        case XmlNodeType.EndElement:
                            writer.WriteFullEndElement();
                            break;
                        case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                            writer.WriteString(reader.Value);
                            break;
                        case XmlNodeType.CDATA:
                            writer.WriteCData(reader.Value);
                            break;
                    }
                }

                if (root)
                {
                    throw new SvgFormatException(Resources.Strings.Svg_NotSvg, 1, 1);
                }
            }

            return output.ToString();
        }
        catch (XmlException e)
        {
            throw new SvgFormatException(XmlMarkup.Message(e), e.LineNumber, e.LinePosition);
        }
    }

    private static bool Keep(XmlReader reader)
    {
        if (reader.Prefix == "xmlns" || reader.LocalName == "xmlns")
        {
            return true;
        }

        if (reader.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (reader.LocalName is "href" or "src")
        {
            var value = reader.Value.Trim();
            return value.StartsWith('#') || value.StartsWith("data:image/png", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("data:image/jpeg", StringComparison.OrdinalIgnoreCase) || value.StartsWith("data:image/gif", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("data:image/webp", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool IsLinkAnimation(XmlReader reader)
    {
        var target = reader.GetAttribute("attributeName");
        return target is not null && (target.EndsWith("href", StringComparison.OrdinalIgnoreCase) || target.StartsWith("on", StringComparison.OrdinalIgnoreCase));
    }
}
