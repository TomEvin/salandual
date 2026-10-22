// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/NotebookPageTests.cs

using System.Text.RegularExpressions;
using Salandual.Core.Viewer;

namespace Salandual.Core.Tests;

public sealed partial class NotebookPageTests
{
    private const string Dot = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static readonly string Sample = $$"""
        {
          "nbformat": 4, "nbformat_minor": 5,
          "metadata": {
            "kernelspec": { "display_name": "Python 3 (ipykernel)", "language": "python", "name": "python3" },
            "language_info": { "name": "python", "version": "3.12.7" }
          },
          "cells": [
            { "cell_type": "markdown", "metadata": {}, "source": ["# Sales\n", "\n", "Line<br>two <script>document.title = 1</script> ![dot](attachment:dot.png)"],
              "attachments": { "dot.png": { "image/png": "{{Dot}}" } } },
            { "cell_type": "code", "execution_count": 1, "metadata": {}, "source": ["import pandas as pd\n", "print(\"Rows\")  # count"],
              "outputs": [
                { "output_type": "stream", "name": "stdout", "text": ["Rows: "] },
                { "output_type": "stream", "name": "stdout", "text": ["12,480\n"] },
                { "output_type": "stream", "name": "stderr", "text": "careful\n" } ] },
            { "cell_type": "markdown", "metadata": {}, "source": "## Sales" },
            { "cell_type": "code", "execution_count": 2, "metadata": {}, "source": "df.head()",
              "outputs": [ { "output_type": "execute_result", "execution_count": 2, "metadata": {}, "data": {
                "text/plain": "   amount",
                "text/html": "<div><style scoped>.dataframe { color: red }</style><table border=\"1\" class=\"dataframe\" onclick=\"x()\"><thead><tr style=\"text-align: right;\"><th></th><th colspan=\"2\">amount</th></tr></thead><tbody><tr><th>KM</th><td>48</td></tr></tbody></table></div>" } } ] },
            { "cell_type": "code", "execution_count": 3, "metadata": {}, "source": "plot()",
              "outputs": [
                { "output_type": "display_data", "metadata": { "image/png": { "width": 320 } }, "data": { "image/png": "{{Dot}}\n", "text/plain": "<Figure>" } },
                { "output_type": "display_data", "metadata": {}, "data": { "image/svg+xml": ["<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"] } },
                { "output_type": "display_data", "metadata": {}, "data": { "text/markdown": "**Done**" } } ] },
            { "cell_type": "code", "execution_count": 4, "metadata": {}, "source": "df['x']",
              "outputs": [ { "output_type": "error", "ename": "KeyError", "evalue": "'x'",
                "traceback": ["\u001b[0;31mKeyError\u001b[0m  Traceback", "\u001b[38;5;124m'x'\u001b[39m <here>"] } ] },
            { "cell_type": "code", "execution_count": 5, "metadata": {}, "source": "w.IntSlider()",
              "outputs": [
                { "output_type": "execute_result", "execution_count": 5, "metadata": {}, "data": {
                  "application/vnd.jupyter.widget-view+json": { "model_id": "a1" }, "text/plain": "IntSlider(value=3)" } },
                { "output_type": "display_data", "metadata": {}, "data": { "text/html": "<div id=\"p\"></div><script>Plotly.newPlot()</script>", "application/vnd.plotly.v1+json": {} } } ] },
            { "cell_type": "raw", "metadata": {}, "source": "raw <text>" },
            { "cell_type": "code", "execution_count": null, "metadata": {}, "source": "", "outputs": [] }
          ]
        }
        """;

    private static MarkdownPage Render(string json, PageOptions? options = null) => NotebookPage.Render(json, options ?? new PageOptions());

    [Fact]
    public void ThePage_HasTheKernelStrip_TheCells_AndTheirPrompts()
    {
        var page = Render(Sample);
        var info = page.Notebook!;

        Assert.Equal("Python 3 (ipykernel)", info.Kernel);
        Assert.Equal("3.12.7", info.LanguageVersion);
        Assert.Equal("4.5", info.Format);
        Assert.Equal((6, 2, 1), (info.CodeCells, info.MarkdownCells, info.OtherCells));
        Assert.Equal("9 cells: 6 code, 2 Markdown", info.CellsText);
        Assert.Contains("<b>Python 3 (ipykernel)</b> · Python 3.12.7", page.Html, StringComparison.Ordinal);
        Assert.Contains("In [1]:", page.Html, StringComparison.Ordinal);
        Assert.Contains("In [ ]:", page.Html, StringComparison.Ordinal);
        Assert.Contains("Out[2]:", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Out[3]:", page.Html, StringComparison.Ordinal);
        Assert.Contains("<pre class=\"src raw\">raw &lt;text&gt;</pre>", page.Html, StringComparison.Ordinal);

        Assert.Equal([(1, "Sales", "sales"), (2, "Sales", "sales-1")], page.Headings.Select(h => (h.Level, h.Text, h.Id)));
        Assert.Contains("id=\"sales-1\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Copy_TheCellsCode_AndAllCodeAsOneScript_WithTheMarkdownAsComments()
    {
        var info = Render(Sample).Notebook!;

        Assert.Equal(6, info.CellCode.Count);
        Assert.Equal("import pandas as pd\nprint(\"Rows\")  # count", info.CellCode[0]);
        Assert.StartsWith("# # Sales\n#\n# Line<br>two", info.AllCode, StringComparison.Ordinal);
        Assert.Contains("\nimport pandas as pd\nprint(\"Rows\")  # count\n\n# ## Sales\n\ndf.head()\n", info.AllCode, StringComparison.Ordinal);
        Assert.EndsWith("w.IntSlider()\n", info.AllCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Code_IsHighlighted_ForTheNotebooksLanguage()
    {
        var html = Render(Sample).Html;

        Assert.Contains("<span class=\"kw\">import</span> pandas <span class=\"kw\">as</span> pd", html, StringComparison.Ordinal);
        Assert.Contains("print(<span class=\"st\">&quot;Rows&quot;</span>)  <span class=\"cm\"># count</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Outputs_PrintedText_IsOne_Stderr_AndErrors_InTheirColours()
    {
        var html = Render(Sample).Html;

        Assert.Contains("<pre class=\"out\">Rows: 12,480</pre>", html, StringComparison.Ordinal);
        Assert.Contains("<pre class=\"out stderr\">careful</pre>", html, StringComparison.Ordinal);
        Assert.Contains("<pre class=\"out err\"><span class=\"af1\">KeyError</span>  Traceback\n<span style=\"color:#AF0000;\">&#39;x&#39;</span> &lt;here&gt;</pre>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlOutputs_AreShown_CleanedAsJupyterCleansThem()
    {
        var html = Render(Sample).Html;

        Assert.Contains("<div class=\"html\"><div><table><thead><tr><th></th><th colspan=\"2\">amount</th></tr></thead><tbody><tr><th>KM</th><td>48</td></tr></tbody></table></div></div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(".dataframe", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onclick", html, StringComparison.Ordinal);

        Assert.Contains("Line<br>two", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pictures_AreInThePage_AnSvgOnlyAsAPicture_AndMarkdownOutputsAreRendered()
    {
        var html = Render(Sample).Html;

        Assert.Contains($"<img class=\"pic\" src=\"data:image/png;base64,{Dot}\" alt=\"\" width=\"320\">", html, StringComparison.Ordinal);
        Assert.Contains("src=\"data:image/svg+xml;base64,", html, StringComparison.Ordinal);
        Assert.Contains($"src=\"data:image/png;base64,{Dot}\" alt=\"dot\"", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Done</strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WidgetsAndScripts_AreLeftOut_WithANote_AndTheirTextForm()
    {
        var html = Render(Sample).Html;

        Assert.Contains("<b>Interactive output (a widget) isn't shown:</b> Salandual never runs a notebook. Its text form: <code>IntSlider(value=3)</code>", html.Replace("&#39;", "'", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("<b>Interactive output isn't shown:</b>", html.Replace("&#39;", "'", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("Plotly", html, StringComparison.Ordinal);

        Assert.Contains("content=\"default-src 'none'; img-src file: data: https: http:; style-src 'unsafe-inline'\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void LongOutputs_AreFoldedAfter30Lines_UnlessSetToWhole()
    {
        var lines = string.Join(",", Enumerable.Range(1, 45).Select(n => $"\"line {n}\\n\""));
        var json = $$"""{ "nbformat": 4, "metadata": {}, "cells": [ { "cell_type": "code", "execution_count": 1, "source": "go()", "outputs": [ { "output_type": "stream", "name": "stdout", "text": [{{lines}}] } ] } ] }""";

        var folded = Render(json).Html;
        Assert.Contains("<pre class=\"out folded\">line 1\n", folded, StringComparison.Ordinal);
        Assert.Contains("line 30<span class=\"rest\">\nline 31\n", folded, StringComparison.Ordinal);
        Assert.Contains("data-more=\"… 15 more lines\" data-all=\"All 45 lines\" data-show=\"Show all\" data-hide=\"Fold\"><span>… 15 more lines</span>", folded, StringComparison.Ordinal);

        var whole = Render(json, new PageOptions { FoldOutputs = false }).Html;
        Assert.DoesNotContain("folded", whole.Replace(".folded", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("line 45</pre>", whole, StringComparison.Ordinal);
    }

    [Fact]
    public void HideCode_MarksThePage_SoOnlyMarkdownAndOutputsShow()
    {
        Assert.Contains("<body class=\"\">", Render(Sample).Html, StringComparison.Ordinal);
        var hidden = Render(Sample, new PageOptions { HideCode = true }).Html;
        Assert.Contains("<body class=\"hidecode\">", hidden, StringComparison.Ordinal);
        Assert.Contains(".hidecode .cell.code > .pr.i, .hidecode .cell.code > .cb.in, .hidecode .cell.code.noout { display: none; }", hidden, StringComparison.Ordinal);
    }

    [Fact]
    public void WebPictures_InOutputs_FollowTheWebImagesSetting()
    {
        const string json = """{ "cells": [ { "cell_type": "code", "source": "", "outputs": [ { "output_type": "display_data", "data": { "text/html": "<img src=\"https://example.org/a.png\" alt=\"chart\" width=\"200\" height=\"100\">" } } ] } ] }""";

        Assert.Contains("<img src=\"https://example.org/a.png\" alt=\"chart\" width=\"200\" height=\"100\">", Render(json).Html, StringComparison.Ordinal);
        var off = Render(json, new PageOptions { WebImages = false }).Html;
        Assert.Contains("class=\"webimage\" style=\"width:200px;height:100px\"", off, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=\"https://", off, StringComparison.Ordinal);
    }

    [Fact]
    public void Maths_StaysAsWritten()
    {
        const string json = """{ "cells": [ { "cell_type": "markdown", "source": "Euler: $e^{i\\pi} + 1 = 0$\n\n$$\nx_1 * x_2\n$$" } ] }""";

        var html = Render(json).Html;
        Assert.Contains("<span class=\"math\">$e^{i\\pi} + 1 = 0$</span>", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"math\">$$x_1 * x_2$$</div>", html.Replace("\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("<span class=\"math\">$a_1$</span>", MarkdownPage.Render("See $a_1$.", new PageOptions()).Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Nbformat3_Worksheets_AreRead()
    {
        const string json = """
            { "nbformat": 3, "nbformat_minor": 0, "metadata": { "name": "" }, "worksheets": [ { "cells": [
              { "cell_type": "heading", "level": 2, "source": ["Old"] },
              { "cell_type": "code", "language": "python", "prompt_number": 7, "input": ["x = 1"],
                "outputs": [ { "output_type": "pyout", "prompt_number": 7, "text": ["1"], "png": "QUJD" } ] } ] } ] }
            """;

        var page = Render(json);
        Assert.Equal("3.0", page.Notebook!.Format);
        Assert.Equal("Old", Assert.Single(page.Headings).Text);
        Assert.Contains("In [7]:", page.Html, StringComparison.Ordinal);
        Assert.Contains("Out[7]:", page.Html, StringComparison.Ordinal);
        Assert.Contains("src=\"data:image/png;base64,QUJD\"", page.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "metadata": {} }""")]
    [InlineData("[1, 2]")]
    public void NotANotebook_Throws_SoTheViewerShowsText(string json)
        => Assert.Throws<NotebookFormatException>(() => Render(json));

    [Fact]
    public void Cleaner_KeepsTablesAndFormatting_DropsWhatRunsOrStyles()
    {
        const string dirty = """
            <p align="center" onmouseover="x()">Hi <b>there</b><style>p { }</style><script>alert(1)</script></p>
            <a href="javascript:alert(1)">bad</a> <a href="https://example.org/" target="_blank">good</a> <a href="other.ipynb#top">here</a>
            <iframe src="https://example.org/"></iframe><form><input type="text"></form><input type="checkbox" checked>
            <img src="data:image/png;base64,QUJD" onerror="x()"> <img src="data:text/html;base64,QUJD"> <img src="pics/a.png">
            <table><tr><td rowspan="2" valign="top" style="color:red">1</td><td style="text-align: right">2</td></tr></table>
            <font color="red">kept text</font> 1 < 2 <!-- a comment -->
            """;

        var clean = HtmlCleaner.Clean(dirty, webImages: true);

        Assert.Contains("<p align=\"center\">Hi <b>there</b></p>", clean, StringComparison.Ordinal);
        Assert.Contains("<a>bad</a> <a href=\"https://example.org/\">good</a> <a href=\"other.ipynb#top\">here</a>", clean, StringComparison.Ordinal);
        Assert.Contains("<input type=\"checkbox\" checked=\"checked\" disabled=\"disabled\">", clean, StringComparison.Ordinal);
        Assert.Contains("<img src=\"data:image/png;base64,QUJD\">  <img src=\"pics/a.png\">", clean, StringComparison.Ordinal);
        Assert.Contains("<td rowspan=\"2\" valign=\"top\">1</td><td style=\"text-align: right\">2</td>", clean, StringComparison.Ordinal);
        Assert.Contains("kept text 1 &lt; 2", clean, StringComparison.Ordinal);
        foreach (var gone in new[] { "script", "<style", "color:red", "iframe", "form", "onerror", "onmouseover", "javascript", "data:text", "font", "comment", "target" })
        {
            Assert.DoesNotContain(gone, clean, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Cleaner_ClosesWhatAnOutputLeftOpen_ButLeavesACellsPiecesOpen()
    {
        Assert.Equal("<div><table><tr><td>1</td></tr></table></div>", HtmlCleaner.Clean("<div><table><tr><td>1</div>", webImages: true));
        Assert.Equal("<div align=\"center\">", HtmlCleaner.Clean("<div align=\"center\">", webImages: true, fragment: true));
        Assert.Equal("</div>", HtmlCleaner.Clean("</div>", webImages: true, fragment: true));
    }

    [Theory]
    [InlineData("python", "x = rb'a\\'b' + 0x1F  # done", "x = <span class=\"st\">rb&#39;a\\&#39;b&#39;</span> + <span class=\"nu\">0x1F</span>  <span class=\"cm\"># done</span>")]
    [InlineData("python", "%matplotlib inline\n!pip list", "<span class=\"mg\">%matplotlib inline</span>\n<span class=\"mg\">!pip list</span>")]
    [InlineData("python", "s = \"\"\"a\nb\"\"\"", "s = <span class=\"st\">&quot;&quot;&quot;a\nb&quot;&quot;&quot;</span>")]
    [InlineData("r", "x <- c(1, NA) # r", "x &lt;- c(<span class=\"nu\">1</span>, <span class=\"kw\">NA</span>) <span class=\"cm\"># r</span>")]
    [InlineData("julia", "#= a =# function f() end", "<span class=\"cm\">#= a =#</span> <span class=\"kw\">function</span> f() <span class=\"kw\">end</span>")]
    [InlineData("sql", "SELECT 1 -- one", "<span class=\"kw\">SELECT</span> <span class=\"nu\">1</span> <span class=\"cm\">-- one</span>")]
    [InlineData("brainfuck", "+[>\"x\"<]", "+[&gt;<span class=\"st\">&quot;x&quot;</span>&lt;]")]
    public void Highlighter_KnowsTheUsualLanguages(string language, string code, string html)
        => Assert.Equal(html, CodeHighlighter.Highlight(code, language));

    [Fact]
    public void TerminalColours_BecomeSpans_OtherCodesGo_AndACarriageReturnOverwrites()
    {
        var ansi = new AnsiText();
        Assert.Equal("<span class=\"af2 abo\">ok</span> <span style=\"color:#102030;background:#00005F;\">rgb</span>",
            ansi.ToHtml("\u001b[1;32mok\u001b[0m \u001b[38;2;16;32;48;48;5;17mrgb\u001b[m\u001b[2K\u001b]0;title\u0007"));

        var folded = new AnsiText();
        Assert.Equal("<span class=\"af1\">a</span>", folded.ToHtml("\u001b[31ma"));
        Assert.Equal("<span class=\"af1\">b</span>", folded.ToHtml("b"));

        Assert.Equal("<span style=\"color:#CF6666;\">s</span>", new AnsiText(dark: true).ToHtml("\u001b[38;5;124ms"));
        Assert.Equal("<span style=\"color:#FFD700;\">s</span>", new AnsiText(dark: true).ToHtml("\u001b[38;5;220ms"));

        Assert.Equal("100%\ndone", AnsiText.Overwrites(" 10%\r 50%\r100%\r\ndone"));
        Assert.DoesNotMatch(Escapes(), ansi.ToHtml("\u001b[?25l\u001b[Kx"));
    }

    [GeneratedRegex("\u001b")]
    private static partial Regex Escapes();
}
