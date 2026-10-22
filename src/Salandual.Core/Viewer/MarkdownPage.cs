// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/MarkdownPage.cs

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Salandual.Core.Resources;

namespace Salandual.Core.Viewer;

public sealed record MarkdownHeading(int Level, string Text, string Id);

public sealed record PageColors(
    string Background,
    string Text,
    string SecondaryText,
    string TertiaryText,
    string Accent,
    string Border,
    string BorderStrong,
    string Surface,
    string Selection,
    string Match,
    string CurrentMatch)
{
    public string Danger { get; init; } = "#C42B1C";

    public string Warning { get; init; } = "#9D5D00";

    public string Success { get; init; } = "#0F7B0F";

    public static PageColors Light { get; } = new("#FFFFFF", "#1B1B1B", "#5C5C5C", "#8A8A8A", "#0F6CBD", "#E1E1E1", "#C8C8C8", "#F6F8FA", "#DCEBFA", "#FCE9A6", "#F7C04A");
}

public sealed record PageOptions
{
    public PageColors Colors { get; init; } = PageColors.Light;

    public bool Dark { get; init; }

    public bool FullWidth { get; init; }

    public double Zoom { get; init; } = 100;

    public bool WebImages { get; init; } = true;

    public bool HideCode { get; init; }

    public bool FoldOutputs { get; init; } = true;

    public bool SourceLines { get; init; }
}

public sealed partial class MarkdownPage
{
    public const string ShowWebImagesLink = "salandual://show-web-images";

    public const int ReadingWidth = 820;

    private const int WordsPerMinute = 230;

    internal static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .DisableHtml()
        .Build();

    internal MarkdownPage(string html, IReadOnlyList<MarkdownHeading> headings, int words, int webImages, NotebookInfo? notebook = null)
    {
        Html = html;
        Headings = headings;
        Words = words;
        WebImages = webImages;
        Notebook = notebook;
    }

    public NotebookInfo? Notebook { get; }

    public string Html { get; }

    public IReadOnlyList<MarkdownHeading> Headings { get; }

    public int Words { get; }

    public int WebImages { get; }

    public int ReadingMinutes => Words == 0 ? 0 : Math.Max(1, (int)Math.Round(Words / (double)WordsPerMinute, MidpointRounding.AwayFromZero));

    public static MarkdownPage Render(string markdown, PageOptions options)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        var headings = new List<MarkdownHeading>();
        CollectHeadings(document, headings, null);
        var webImages = WebImagesIn(document, options);
        if (options.SourceLines)
        {
            MarkSourceLines(document);
        }

        var body = Maths(document.ToHtml(Pipeline));
        var words = WordPattern().Count(Markdown.ToPlainText(markdown, Pipeline));
        return new MarkdownPage(Document(body, options), headings, words, webImages);
    }

    internal static void MarkSourceLines(MarkdownDocument document)
    {
        foreach (var block in document.Descendants<Block>())
        {
            if (block is not (ListBlock or Markdig.Extensions.Tables.TableRow or Markdig.Extensions.Tables.TableCell))
            {
                block.GetAttributes().AddProperty("data-line", block.Line.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    internal static void CollectHeadings(MarkdownDocument document, List<MarkdownHeading> headings, HashSet<string>? used)
    {
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            var attributes = heading.GetAttributes();
            if (attributes.Id is not { Length: > 0 } id)
            {
                continue;
            }

            if (used is not null)
            {
                var unique = id;
                for (var n = 1; !used.Add(unique); n++)
                {
                    unique = string.Create(CultureInfo.InvariantCulture, $"{id}-{n}");
                }

                attributes.Id = id = unique;
            }

            headings.Add(new MarkdownHeading(heading.Level, InlineText(heading.Inline).Trim(), id));
        }
    }

    internal static int WebImagesIn(MarkdownDocument document, PageOptions options)
    {
        var webImages = 0;
        foreach (var image in document.Descendants<LinkInline>().Where(l => l.IsImage).ToList())
        {
            if (!IsWeb(image.Url))
            {
                continue;
            }

            webImages++;
            if (!options.WebImages)
            {
                image.ReplaceBy(new HtmlInline(Placeholder(image)));
            }
        }

        return webImages;
    }

    internal static bool IsWeb(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string InlineText(ContainerInline? inline)
    {
        var text = new StringBuilder();
        void Walk(Inline? node)
        {
            for (; node is not null; node = node.NextSibling)
            {
                switch (node)
                {
                    case LiteralInline literal: text.Append(literal.Content); break;
                    case CodeInline code: text.Append(code.Content); break;
                    case ContainerInline container: Walk(container.FirstChild); break;
                }
            }
        }

        Walk(inline?.FirstChild);
        return text.ToString();
    }

    private static string Placeholder(LinkInline image)
    {
        var attributes = image.TryGetAttributes();
        string? Value(string name) => attributes?.Properties?.FirstOrDefault(p => p.Key == name).Value;
        return Placeholder(image.Url ?? "", InlineText(image).Trim(), Value("width"), Value("height"));
    }

    internal static string Placeholder(string url, string alt, string? givenWidth, string? givenHeight)
    {
        static string? Size(string? v) => v is not null && int.TryParse(v.Trim().TrimEnd('p', 'x'), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is > 0 and < 10000
            ? n.ToString(CultureInfo.InvariantCulture) + "px" : null;
        var width = Size(givenWidth) ?? "320px";
        var height = Size(givenHeight) ?? "180px";
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";
        var caption = alt.Length > 0 ? $"<b>{Encode(alt)}</b>" : "";
        return $"<span class=\"webimage\" style=\"width:{width};height:{height}\" title=\"{Encode(url)}\">{PictureIcon}{caption}"
            + $"<span>{Encode(string.Format(CultureInfo.CurrentCulture, Strings.Md_WebImage, host))}</span>"
            + $"<a href=\"{ShowWebImagesLink}\">{Encode(Strings.Md_ShowWebImages)}</a></span>";
    }

    private const string PictureIcon = "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><circle cx=\"9\" cy=\"10\" r=\"2\"/><path d=\"m4 18 5-5 4 4 3-3 4 4\"/></svg>";

    internal static string Encode(string text) => WebUtility.HtmlEncode(text);

    internal static string Document(string body, PageOptions o, string bodyClass = "", string css = "")
    {
        var c = o.Colors;
        var images = o.WebImages ? "file: data: https: http:" : "file: data:";
        var width = o.FullWidth ? "none" : ReadingWidth.ToString(CultureInfo.InvariantCulture) + "px";
        var zoom = (Math.Clamp(o.Zoom, 5, 6400) / 100.0).ToString("0.####", CultureInfo.InvariantCulture);
        return $$"""
            <!doctype html>
            <html><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src {{images}}; style-src 'unsafe-inline'">
            <style>
            :root { color-scheme: {{(o.Dark ? "dark" : "light")}};
              --bg: {{c.Background}}; --fg: {{c.Text}}; --fg2: {{c.SecondaryText}}; --fg3: {{c.TertiaryText}}; --accent: {{c.Accent}};
              --border: {{c.Border}}; --border2: {{c.BorderStrong}}; --surface: {{c.Surface}}; --select: {{c.Selection}};
              --match: {{c.Match}}; --match-cur: {{c.CurrentMatch}}; --danger: {{c.Danger}}; --warning: {{c.Warning}}; --success: {{c.Success}}; }
            html { background: var(--bg); }
            body { margin: 0; background: var(--bg); color: var(--fg); font: 15px/1.6 "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif; zoom: {{zoom}}; overflow-wrap: break-word; }
            main { max-width: {{width}}; margin: 0 auto; padding: 24px 32px 48px; }
            ::selection { background: var(--select); }
            h1, h2, h3, h4, h5, h6 { line-height: 1.3; margin: 24px 0 10px; font-weight: 600; scroll-margin-top: 12px; }
            h1 { font-size: 1.75em; margin-top: 0; padding-bottom: 8px; border-bottom: 1px solid var(--border); }
            h2 { font-size: 1.35em; padding-bottom: 6px; border-bottom: 1px solid var(--border); }
            h3 { font-size: 1.1em; } h4, h5, h6 { font-size: 1em; } h6 { color: var(--fg2); }
            p, ul, ol, dl, table, pre, blockquote, details { margin: 0 0 12px; }
            a { color: var(--accent); text-decoration: none; } a:hover { text-decoration: underline; }
            code, kbd, samp { font: 0.87em "Cascadia Mono", Consolas, monospace; background: var(--surface); border: 1px solid var(--border); border-radius: 3px; padding: 0 4px; }
            pre { font: 13px/20px "Cascadia Mono", Consolas, monospace; background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 10px 14px; overflow: auto; }
            pre code { background: none; border: 0; padding: 0; font: inherit; }
            blockquote { border-left: 3px solid var(--border2); padding: 0 12px; color: var(--fg2); margin-left: 0; }
            table { border-collapse: collapse; display: block; overflow: auto; max-width: 100%; }
            th, td { border: 1px solid var(--border); padding: 5px 12px; }
            th { background: var(--surface); font-weight: 600; }
            hr { border: 0; border-top: 1px solid var(--border); margin: 20px 0; }
            img { max-width: 100%; }
            .math { font-family: "Cambria Math", Cambria, serif; }
            ul.contains-task-list { list-style: none; padding-left: 4px; }
            .task-list-item input { margin: 0 8px 0 0; vertical-align: -1px; accent-color: var(--accent); }
            .footnotes { font-size: 0.87em; color: var(--fg2); }
            .footnotes hr { margin: 28px 0 12px; }
            .webimage { display: inline-grid; place-content: center; justify-items: center; gap: 2px; max-width: 100%; box-sizing: border-box;
              border: 1px dashed var(--border2); border-radius: 4px; background: var(--surface); color: var(--fg2); font-size: 12px; line-height: 1.4; text-align: center; padding: 8px; vertical-align: top; }
            .webimage svg { width: 28px; height: 28px; stroke: var(--fg3); fill: none; stroke-width: 1.4; stroke-linecap: round; stroke-linejoin: round; }
            .webimage b { color: var(--fg); font-weight: 600; font-size: 13px; }
            mark { background: var(--match); color: inherit; border-radius: 2px; }
            mark.current { background: var(--match-cur); outline: 1px solid var(--fg3); }
            {{css}}
            </style></head>
            <body class="{{bodyClass}}"><main>
            {{body}}
            </main></body></html>
            """;
    }

    internal static string Maths(string html)
        => DisplayMathPattern().Replace(InlineMathPattern().Replace(html, "<span class=\"math\">$$$1$$</span>"), "<div class=\"math\">$$$$$1$$$$</div>");

    [GeneratedRegex(@"<span class=""math"">\\\((.*?)\\\)</span>", RegexOptions.Singleline)]
    private static partial Regex InlineMathPattern();

    [GeneratedRegex(@"<div class=""math"">\s*\\\[(.*?)\\\]\s*</div>", RegexOptions.Singleline)]
    private static partial Regex DisplayMathPattern();

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*")]
    private static partial Regex WordPattern();
}
