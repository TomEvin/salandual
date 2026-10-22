// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Viewer/NotebookPage.cs

using System.Globalization;
using System.Text;
using System.Text.Json;
using Markdig;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Salandual.Core.Resources;

namespace Salandual.Core.Viewer;

public sealed record NotebookInfo(
    string Kernel,
    string? Language,
    string? LanguageVersion,
    int CodeCells,
    int MarkdownCells,
    int OtherCells,
    string Format,
    IReadOnlyList<string> CellCode,
    string AllCode)
{
    public int Cells => CodeCells + MarkdownCells + OtherCells;

    public string CellsText => Loc.Plural(Cells, Strings.Nb_Cells_One, Strings.Nb_Cells_Other, CodeCells, MarkdownCells);
}

public sealed class NotebookFormatException(string message, Exception? inner = null) : FormatException(message, inner);

public static class NotebookPage
{
    public const int FoldLines = 30;

    public const int OutputLimit = 5 * 1024 * 1024;

    public const long OutputsLimit = 50L * 1024 * 1024;

    private static readonly MarkdownPipeline CellPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .Build();

    private static readonly string[] Pictures = ["image/svg+xml", "image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp"];

    private sealed class Context(PageOptions options, string language, bool outputs)
    {
        public PageOptions Options { get; } = options;

        public string Language { get; } = language;

        public bool Outputs { get; } = outputs;

        public int WebImages { get; set; }

        public List<MarkdownHeading> Headings { get; } = [];

        public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);
    }

    public static MarkdownPage Render(string json, PageOptions options)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 1024, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            throw new NotebookFormatException(e.Message, e);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Cells(root) is not { } cells)
            {
                throw new NotebookFormatException(Strings.Nb_NoCells);
            }

            var meta = Child(root, "metadata");
            var kernelspec = Child(meta, "kernelspec");
            var languageInfo = Child(meta, "language_info");
            var language = Str(kernelspec, "language") ?? Str(languageInfo, "name") ?? Str(meta, "language");
            var kernel = Str(kernelspec, "display_name") ?? Str(kernelspec, "name") ?? (language is null ? Strings.Nb_UnknownKernel : Capital(language));
            var version = Str(languageInfo, "version");
            var languageName = Str(languageInfo, "name") ?? language;
            var format = Int(root, "nbformat") is { } major
                ? Int(root, "nbformat_minor") is { } minor ? string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}") : major.ToString(CultureInfo.InvariantCulture)
                : "4";

            var c = new Context(options, language ?? "python", json.Length <= OutputsLimit);
            var body = new StringBuilder(json.Length / 2);
            var code = new List<string>();
            var all = new StringBuilder();
            var comment = CommentFor(c.Language);
            int codeCells = 0, markdownCells = 0, otherCells = 0;
            foreach (var cell in cells.EnumerateArray())
            {
                if (cell.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var source = Text(cell, "source") ?? Text(cell, "input") ?? "";
                switch (Str(cell, "cell_type"))
                {
                    case "markdown":
                    case "heading":
                        markdownCells++;
                        if (Str(cell, "cell_type") == "heading")
                        {
                            source = new string('#', Math.Clamp(Int(cell, "level") ?? 1, 1, 6)) + " " + source;
                        }

                        body.Append("<div class=\"cell mdc\"><div class=\"pr\"></div><div class=\"cb\">")
                            .Append(Markdown(source, Child(cell, "attachments"), c, contents: true)).Append("</div></div>\n");
                        foreach (var line in source.TrimEnd().Split('\n'))
                        {
                            all.Append(comment).Append(line.Length > 0 ? " " : "").Append(line.TrimEnd('\r')).Append('\n');
                        }

                        all.Append('\n');
                        break;
                    case "code":
                        codeCells++;
                        CodeCell(body, cell, source, code.Count, c);
                        code.Add(source);
                        if (source.Trim().Length > 0)
                        {
                            all.Append(source.TrimEnd()).Append("\n\n");
                        }

                        break;
                    default:
                        otherCells++;
                        body.Append("<div class=\"cell raw\"><div class=\"pr\"></div><div class=\"cb\"><pre class=\"src raw\">")
                            .Append(MarkdownPage.Encode(source)).Append("</pre></div></div>\n");
                        break;
                }
            }

            var info = new NotebookInfo(kernel, language, version, codeCells, markdownCells, otherCells, format, code, all.ToString().TrimEnd() + "\n");
            var head = new StringBuilder("<div class=\"nbhead\"><span><b>").Append(MarkdownPage.Encode(kernel)).Append("</b>");
            if (version is { Length: > 0 } && languageName is { Length: > 0 })
            {
                head.Append(" · ").Append(MarkdownPage.Encode(Capital(languageName) + " " + version));
            }

            head.Append("</span><span>").Append(MarkdownPage.Encode(info.CellsText)).Append("</span><span>").Append(MarkdownPage.Encode(Strings.Nb_Saved)).Append("</span>");
            if (!c.Outputs)
            {
                head.Append("<span class=\"warn\">").Append(MarkdownPage.Encode(Strings.Nb_OutputsLeftOut)).Append("</span>");
            }

            head.Append("</div>\n");
            var html = MarkdownPage.Document("<div class=\"nb\">\n" + head + body + "</div>", options, options.HideCode ? "hidecode" : "", Css);
            return new MarkdownPage(html, c.Headings, 0, c.WebImages, info);
        }
    }

    private static JsonElement? Cells(JsonElement root)
    {
        if (root.TryGetProperty("cells", out var cells) && cells.ValueKind == JsonValueKind.Array)
        {
            return cells;
        }

        if (root.TryGetProperty("worksheets", out var sheets) && sheets.ValueKind == JsonValueKind.Array && sheets.GetArrayLength() > 0
            && sheets[0].ValueKind == JsonValueKind.Object && sheets[0].TryGetProperty("cells", out cells) && cells.ValueKind == JsonValueKind.Array)
        {
            return cells;
        }

        return null;
    }

    private static void CodeCell(StringBuilder body, JsonElement cell, string source, int index, Context c)
    {
        var outputs = c.Outputs && cell.TryGetProperty("outputs", out var o) && o.ValueKind == JsonValueKind.Array ? o : default;
        var hasOutputs = outputs.ValueKind == JsonValueKind.Array && outputs.GetArrayLength() > 0;
        var count = Int(cell, "execution_count") ?? Int(cell, "prompt_number");
        var language = Str(cell, "language") ?? c.Language;
        body.Append("<div class=\"cell code").Append(hasOutputs ? "" : " noout").Append(source.Trim().Length == 0 && !hasOutputs ? " empty" : "")
            .Append("\" data-cell=\"").Append(index.ToString(CultureInfo.InvariantCulture)).Append("\">")
            .Append("<div class=\"pr i\">").Append(MarkdownPage.Encode(Loc.Format(Strings.Nb_In, count?.ToString(CultureInfo.CurrentCulture) ?? " "))).Append("</div>")
            .Append("<div class=\"cb in\"><pre class=\"src\">").Append(CodeHighlighter.Highlight(source.TrimEnd('\n', '\r'), language)).Append("</pre></div>");
        if (hasOutputs)
        {
            Outputs(body, outputs, c);
        }

        body.Append("</div>\n");
    }

    private static void Outputs(StringBuilder body, JsonElement outputs, Context c)
    {
        string? streamName = null;
        var stream = new StringBuilder();
        void Flush()
        {
            if (streamName is not null)
            {
                Output(body, null, TextOutput(stream.ToString(), streamName == "stderr" ? "stderr" : "", c));
                streamName = null;
                stream.Clear();
            }
        }

        foreach (var output in outputs.EnumerateArray())
        {
            if (output.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = Str(output, "output_type");
            if (type == "stream")
            {
                var name = Str(output, "name") ?? Str(output, "stream") ?? "stdout";
                if (name != streamName)
                {
                    Flush();
                    streamName = name;
                }

                stream.Append(Text(output, "text") ?? "");
                continue;
            }

            Flush();
            switch (type)
            {
                case "error" or "pyerr":
                    var traceback = output.TryGetProperty("traceback", out var t) && t.ValueKind == JsonValueKind.Array
                        ? string.Join('\n', t.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.String).Select(l => l.GetString()))
                        : "";
                    if (traceback.Length == 0)
                    {
                        traceback = $"{Str(output, "ename")}: {Str(output, "evalue")}";
                    }

                    Output(body, null, TextOutput(traceback, "err", c));
                    break;
                case "execute_result" or "pyout" or "display_data" or "update_display_data":
                    var bundle = Bundle(output);
                    var prompt = type is "execute_result" or "pyout" && (Int(output, "execution_count") ?? Int(output, "prompt_number")) is { } n
                        ? Loc.Format(Strings.Nb_Out, n.ToString(CultureInfo.CurrentCulture)) : null;
                    Output(body, prompt, Rich(bundle, Child(output, "metadata"), c));
                    break;
            }
        }

        Flush();
    }

    private static void Output(StringBuilder body, string? prompt, string html)
        => body.Append("<div class=\"pr o\">").Append(MarkdownPage.Encode(prompt ?? "")).Append("</div><div class=\"cb\">").Append(html).Append("</div>");

    private static Dictionary<string, string> Bundle(JsonElement output)
    {
        var bundle = new Dictionary<string, string>(StringComparer.Ordinal);
        if (output.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            foreach (var form in data.EnumerateObject())
            {
                bundle[form.Name] = Value(form.Value);
            }

            return bundle;
        }

        foreach (var (key, mime) in new[] { ("text", "text/plain"), ("html", "text/html"), ("markdown", "text/markdown"), ("latex", "text/latex"), ("png", "image/png"), ("jpeg", "image/jpeg"), ("svg", "image/svg+xml"), ("json", "application/json"), ("javascript", "application/javascript") })
        {
            if (output.TryGetProperty(key, out var value))
            {
                bundle[mime] = Value(value);
            }
        }

        return bundle;
    }

    private static string Rich(Dictionary<string, string> bundle, JsonElement? metadata, Context c)
    {
        bundle.TryGetValue("text/plain", out var plain);
        if (bundle.Sum(f => (long)f.Value.Length) > OutputLimit)
        {
            var mb = bundle.Sum(f => (long)f.Value.Length) / (1024.0 * 1024.0);
            return Note(Loc.Format(Strings.Nb_OutputTooBig, mb), null, runs: false);
        }

        if (bundle.ContainsKey("application/vnd.jupyter.widget-view+json"))
        {
            return Note(Strings.Nb_Widget, plain, runs: true);
        }

        var picture = Pictures.FirstOrDefault(bundle.ContainsKey);
        if (bundle.TryGetValue("text/html", out var html))
        {
            if (!Interactive(html))
            {
                return "<div class=\"html\">" + HtmlCleaner.Clean(html, c.Options.WebImages) + "</div>";
            }

            if (picture is null)
            {
                return Note(Strings.Nb_Interactive, plain, runs: true);
            }
        }

        if (picture is null && bundle.TryGetValue("text/markdown", out var markdown))
        {
            return Markdown(markdown, null, c, contents: false);
        }

        if (picture is not null)
        {
            return Picture(picture, bundle[picture], metadata);
        }

        if (bundle.TryGetValue("text/latex", out var latex))
        {
            return "<pre class=\"out\">" + MarkdownPage.Encode(latex) + "</pre>";
        }

        if (plain is not null)
        {
            return TextOutput(plain, "", c);
        }

        if (bundle.TryGetValue("application/json", out var json))
        {
            return TextOutput(json, "", c);
        }

        return bundle.Keys.Any(k => k == "application/javascript" || k.StartsWith("application/vnd.", StringComparison.Ordinal))
            ? Note(Strings.Nb_Interactive, null, runs: true)
            : Note(Loc.Format(Strings.Nb_UnknownOutput, string.Join(", ", bundle.Keys)), null, runs: false);
    }

    private static bool Interactive(string html)
        => html.Contains("<script", StringComparison.OrdinalIgnoreCase) || html.Contains("<iframe", StringComparison.OrdinalIgnoreCase);

    private static string Note(string title, string? textForm, bool runs)
    {
        var note = new StringBuilder("<div class=\"nocell\"><span class=\"ic\">⚠</span><span><b>").Append(MarkdownPage.Encode(title)).Append("</b>");
        if (runs)
        {
            note.Append(' ').Append(MarkdownPage.Encode(Strings.Nb_NeverRuns));
        }

        if (textForm?.Trim() is { Length: > 0 } text)
        {
            note.Append(' ').Append(MarkdownPage.Encode(Strings.Nb_TextForm)).Append(" <code>")
                .Append(MarkdownPage.Encode(text.Length > 2000 ? text[..2000] + "…" : text)).Append("</code>");
        }

        return note.Append("</span></div>").ToString();
    }

    private static string Picture(string mime, string data, JsonElement? metadata)
    {
        var size = "";
        if (metadata is { } m && Child(m, mime) is { } own)
        {
            foreach (var dimension in new[] { "width", "height" })
            {
                if (own.TryGetProperty(dimension, out var v) && v.TryGetDouble(out var d) && d is > 0 and < 100_000)
                {
                    size += string.Create(CultureInfo.InvariantCulture, $" {dimension}=\"{Math.Round(d)}\"");
                }
            }
        }

        return $"<img class=\"pic\" src=\"{DataAddress(mime, data)}\" alt=\"\"{size}>";
    }

    private static string DataAddress(string mime, string data)
    {
        string base64;
        if (mime == "image/svg+xml")
        {
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
        }
        else
        {
            var clean = new StringBuilder(data.Length);
            foreach (var ch in data)
            {
                if (char.IsAsciiLetterOrDigit(ch) || ch is '+' or '/' or '=')
                {
                    clean.Append(ch);
                }
            }

            base64 = clean.ToString();
        }

        return $"data:{mime};base64,{base64}";
    }

    private static string TextOutput(string text, string kind, Context c)
    {
        text = AnsiText.Overwrites(text);
        if (text.EndsWith('\n'))
        {
            text = text[..^1];
        }

        var ansi = new AnsiText(c.Options.Dark);
        var classes = kind.Length > 0 ? "out " + kind : "out";
        var lines = text.Split('\n');
        if (!c.Options.FoldOutputs || lines.Length <= FoldLines)
        {
            return $"<pre class=\"{classes}\">{ansi.ToHtml(text)}</pre>";
        }

        var shown = string.Join('\n', lines[..FoldLines]);
        var rest = string.Join('\n', lines[FoldLines..]);
        var more = MarkdownPage.Encode(Loc.Plural(lines.Length - FoldLines, Strings.Nb_MoreLines_One, Strings.Nb_MoreLines_Other));
        var whole = MarkdownPage.Encode(Loc.Format(Strings.Nb_AllLines, lines.Length));
        var show = MarkdownPage.Encode(Strings.Nb_ShowAll);
        var fold = MarkdownPage.Encode(Strings.Nb_Fold);
        return $"<pre class=\"{classes} folded\">{ansi.ToHtml(shown)}<span class=\"rest\">\n{ansi.ToHtml(rest)}</span></pre>"
            + $"<div class=\"fold\" data-more=\"{more}\" data-all=\"{whole}\" data-show=\"{show}\" data-hide=\"{fold}\"><span>{more}</span><span>·</span><button type=\"button\" class=\"unfold\">{show}</button></div>";
    }

    private static string Markdown(string source, JsonElement? attachments, Context c, bool contents)
    {
        var document = Markdig.Markdown.Parse(source, CellPipeline);
        if (attachments is { } files)
        {
            foreach (var image in document.Descendants<LinkInline>().Where(l => l.IsImage && l.Url?.StartsWith("attachment:", StringComparison.Ordinal) == true))
            {
                if (Child(files, Uri.UnescapeDataString(image.Url!["attachment:".Length..])) is { } file
                    && file.EnumerateObject().FirstOrDefault(f => f.Name.StartsWith("image/", StringComparison.Ordinal)) is { Name.Length: > 0 } form)
                {
                    image.Url = DataAddress(form.Name, Value(form.Value));
                }
            }
        }

        foreach (var inline in document.Descendants<HtmlInline>().ToList())
        {
            inline.Tag = HtmlCleaner.Clean(inline.Tag ?? "", c.Options.WebImages, fragment: true);
        }

        foreach (var block in document.Descendants<HtmlBlock>().ToList())
        {
            var cleaned = HtmlCleaner.Clean(block.Lines.ToString(), c.Options.WebImages, fragment: true);
            var lines = new StringLineGroup(4);
            foreach (var line in cleaned.Split('\n'))
            {
                lines.Add(new StringSlice(line));
            }

            block.Lines = lines;
        }

        MarkdownPage.CollectHeadings(document, contents ? c.Headings : [], c.Ids);
        c.WebImages += MarkdownPage.WebImagesIn(document, c.Options);
        return MarkdownPage.Maths(document.ToHtml(CellPipeline));
    }

    private static string CommentFor(string language) => language.ToLowerInvariant() switch
    {
        "javascript" or "typescript" or "js" or "ts" or "c" or "c++" or "cpp" or "c#" or "csharp" or "f#" or "java" or "scala" or "kotlin" or "go" or "rust" or "swift" => "//",
        "sql" or "haskell" or "lua" => "--",
        "matlab" or "octave" => "%",
        _ => "#",
    };

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static JsonElement? Child(JsonElement? parent, string name)
        => parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static string? Str(JsonElement? parent, string name)
        => parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static int? Int(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static string? Text(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Array ? Value(v) : null;

    private static string Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Array => string.Concat(value.EnumerateArray().Select(l => l.ValueKind == JsonValueKind.String ? l.GetString() : l.GetRawText())),
        _ => value.GetRawText(),
    };

    private static readonly string Css = BuildCss();

    private static string BuildCss()
    {
        var css = new StringBuilder("""
            .nbhead { display: flex; flex-wrap: wrap; gap: 6px 14px; align-items: center; font-size: 12px; color: var(--fg2); border: 1px solid var(--border); border-radius: 6px; background: var(--surface); padding: 6px 12px; margin: 0 0 18px; }
            .nbhead b { color: var(--fg); font-weight: 600; }
            .nbhead .warn { color: var(--warning); }
            .cell { display: grid; grid-template-columns: 62px minmax(0, 1fr); column-gap: 10px; margin: 0 0 14px; }
            .cell > .pr { grid-column: 1; font: 12px/20px "Cascadia Mono", Consolas, monospace; color: var(--fg3); text-align: right; white-space: nowrap; padding-top: 9px; }
            .cell > .pr.o { padding-top: 4px; color: var(--warning); }
            .cell > .cb { grid-column: 2; min-width: 0; }
            .cell.mdc > .cb > :last-child { margin-bottom: 0; }
            .nb pre.src { font: 13px/20px "Cascadia Mono", Consolas, monospace; background: var(--surface); border: 1px solid var(--border); border-radius: 6px; padding: 8px 12px; white-space: pre; overflow: auto; margin: 0; min-height: 20px; }
            .src .kw { color: var(--accent); font-weight: 600; } .src .st { color: var(--success); } .src .nu { color: var(--warning); } .src .cm { color: var(--fg3); font-style: italic; } .src .mg { color: var(--fg2); }
            .src.raw { color: var(--fg2); }
            .nb pre.out { font: 13px/20px "Cascadia Mono", Consolas, monospace; white-space: pre; overflow: auto; padding: 4px 0; margin: 0; background: none; border: 0; border-radius: 0; }
            .nb pre.out.stderr { background: color-mix(in srgb, var(--danger) 8%, transparent); padding: 4px 10px; }
            .nb pre.out.err { background: color-mix(in srgb, var(--danger) 8%, transparent); border-left: 3px solid var(--danger); padding: 6px 10px; border-radius: 0 4px 4px 0; }
            .folded .rest { display: none; }
            .fold { display: flex; gap: 8px; align-items: center; font-size: 12px; color: var(--fg2); border-top: 1px dashed var(--border2); padding-top: 4px; margin-top: 2px; }
            .fold button { font: inherit; color: var(--accent); background: none; border: 0; padding: 0; cursor: pointer; }
            .fold button:hover { text-decoration: underline; }
            .nb .html { overflow: auto; padding: 4px 0; }
            .nb .html table { display: table; border-collapse: collapse; font-size: 13px; margin: 4px 0; }
            .nb .html th, .nb .html td { border: 0; border-bottom: 1px solid var(--border); padding: 3px 12px; text-align: right; vertical-align: middle; font-variant-numeric: tabular-nums; background: none; }
            .nb .html tr > :first-child { text-align: left; }
            .nb .html thead th { border-bottom: 1px solid var(--border2); font-weight: 600; }
            .nb .html tbody th { font-weight: 600; }
            .nb .html tbody tr:hover { background: var(--surface); }
            img.pic { display: block; max-width: 100%; height: auto; background: #fff; border: 1px solid var(--border); border-radius: 4px; margin: 4px 0; }
            .nocell { display: flex; gap: 8px; align-items: flex-start; font-size: 12px; color: var(--fg2); border: 1px dashed var(--border2); border-radius: 4px; padding: 6px 10px; margin: 4px 0; }
            .nocell .ic { color: var(--warning); }
            .nocell code { font-size: 12px; white-space: pre-wrap; }
            .hidecode .cell.code > .pr.i, .hidecode .cell.code > .cb.in, .hidecode .cell.code.noout { display: none; }
            @media (max-width: 560px) { .cell { grid-template-columns: minmax(0, 1fr); } .cell > .pr { grid-column: 1; text-align: left; padding: 0; } .cell > .cb { grid-column: 1; } }
            .abo { font-weight: 700; } .ait { font-style: italic; } .aun { text-decoration: underline; }
            .af0, .af8 { color: var(--fg2); } .af7, .af15 { color: var(--fg3); }

            """);
        for (var n = 0; n < AnsiText.Named.Length; n++)
        {
            if (n is not (0 or 7 or 8 or 15))
            {
                css.Append(CultureInfo.InvariantCulture, $".af{n} {{ color: {AnsiText.Named[n]}; }} ");
            }

            css.Append(CultureInfo.InvariantCulture, $".ab{n} {{ background: {AnsiText.Named[n]}; }} ");
        }

        return css.ToString();
    }
}
