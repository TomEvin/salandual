// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/MarkdownPageTests.cs

using Salandual.Core.Viewer;

namespace Salandual.Core.Tests;

public sealed class MarkdownPageTests
{
    private const string Readme = """
        # Salandual

        A two-panel file manager, in the spirit of [Open Salamander](https://github.com/OpenSalamander/salamander).

        ## Getting started

        | Key | Does |
        |-----|------|
        | `F3` | View |

        ### Still to do

        - [x] Viewer
        - [ ] Editor

        ![A sunset](docs/sunset.png)
        ![The main window](https://example.org/shot.png){width=400 height=225}

        See the [licence](#licence).[^1]

        ## Licence

        [^1]: The plan changes after each check.
        """;

    [Fact]
    public void Headings_AreListed_WithTheirLevelsAndTheIdsThePageGivesThem()
    {
        var page = MarkdownPage.Render(Readme, new PageOptions());

        Assert.Equal(
            [new MarkdownHeading(1, "Salandual", "salandual"), new MarkdownHeading(2, "Getting started", "getting-started"), new MarkdownHeading(3, "Still to do", "still-to-do"), new MarkdownHeading(2, "Licence", "licence")],
            page.Headings);
        Assert.Contains("id=\"getting-started\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGitHubExtras_AreThere_TablesTaskListsFootnotesAndLinks()
    {
        var html = MarkdownPage.Render(Readme, new PageOptions()).Html;

        Assert.Contains("<table>", html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"footnotes\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://github.com/OpenSalamander/salamander\"", html, StringComparison.Ordinal);
        Assert.Contains("<img src=\"docs/sunset.png\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewingNeverRunsAnything_RawHtmlIsText_AndThePageAllowsNoScripts()
    {
        var html = MarkdownPage.Render("Hello <script>alert(1)</script> <img src=x onerror=alert(2)>", new PageOptions()).Html;

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WebImages_LoadByDefault_AndWhenOff_LeaveAPlaceholderOfTheirSize()
    {
        var on = MarkdownPage.Render(Readme, new PageOptions());
        Assert.Equal(1, on.WebImages);
        Assert.Contains("<img src=\"https://example.org/shot.png\"", on.Html, StringComparison.Ordinal);
        Assert.Contains("img-src file: data: https: http:", on.Html, StringComparison.Ordinal);

        var off = MarkdownPage.Render(Readme, new PageOptions { WebImages = false });
        Assert.Equal(1, off.WebImages);
        Assert.DoesNotContain("<img src=\"https://", off.Html, StringComparison.Ordinal);
        Assert.Contains("img-src file: data:;", off.Html, StringComparison.Ordinal);
        Assert.Contains("class=\"webimage\" style=\"width:400px;height:225px\"", off.Html, StringComparison.Ordinal);
        Assert.Contains("<b>The main window</b>", off.Html, StringComparison.Ordinal);
        Assert.Contains("example.org", off.Html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{MarkdownPage.ShowWebImagesLink}\"", off.Html, StringComparison.Ordinal);
        Assert.Contains("<img src=\"docs/sunset.png\"", off.Html, StringComparison.Ordinal);

        var unsized = MarkdownPage.Render("![](https://example.org/a.png)", new PageOptions { WebImages = false }).Html;
        Assert.Contains("style=\"width:320px;height:180px\"", unsized, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePage_UsesTheViewersColours_WidthAndZoom()
    {
        var colors = PageColors.Light with { Background = "#272727", Text = "#F2F2F2", Accent = "#479EF5" };
        var html = MarkdownPage.Render("# Hi", new PageOptions { Colors = colors, Dark = true, Zoom = 125 }).Html;
        Assert.Contains("--bg: #272727", html, StringComparison.Ordinal);
        Assert.Contains("--accent: #479EF5", html, StringComparison.Ordinal);
        Assert.Contains("color-scheme: dark", html, StringComparison.Ordinal);
        Assert.Contains("zoom: 1.25", html, StringComparison.Ordinal);
        Assert.Contains($"max-width: {MarkdownPage.ReadingWidth}px", html, StringComparison.Ordinal);

        Assert.Contains("max-width: none", MarkdownPage.Render("# Hi", new PageOptions { FullWidth = true }).Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Words_AndReadingTime_ForTheStatusLine()
    {
        Assert.Equal(0, MarkdownPage.Render("", new PageOptions()).Words);
        var page = MarkdownPage.Render("# Árvíztűrő tükörfúrógép\n\nIt's a **two-panel** file manager.", new PageOptions());
        Assert.Equal(7, page.Words);
        Assert.Equal(1, page.ReadingMinutes);
        Assert.Equal(4, MarkdownPage.Render(string.Join(' ', Enumerable.Repeat("word", 920)), new PageOptions()).ReadingMinutes);
    }
}
