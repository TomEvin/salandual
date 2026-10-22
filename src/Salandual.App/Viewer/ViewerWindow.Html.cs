// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Html.cs

using System.Globalization;
using Avalonia.Controls;
using Salandual.App.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Viewer;

internal sealed partial class ViewerWindow
{
    private bool _isHtml;
    private bool _htmlScriptsHere;
    private bool _htmlWebHere;
    private bool _htmlHasScripts;
    private string? _htmlShown;
    private string? _htmlTitle;

    internal bool IsHtml => _isHtml;

    internal bool HtmlScripts => Options.HtmlScripts != _htmlScriptsHere;

    internal bool HtmlWebContent => Options.HtmlWebContent != _htmlWebHere;

    internal bool HtmlHasScripts => _htmlHasScripts;

    internal string? HtmlTitle => _htmlTitle;

    private string HtmlKey => string.Create(CultureInfo.InvariantCulture, $"{_path}|{HtmlScripts}|{HtmlWebContent}");

    private async Task RenderHtml(IPageView page, bool keepScroll)
    {
        var path = _path;
        var d = _document;
        double? scroll = null;
        if (keepScroll && _pageReady && double.TryParse(await page.RunAsync(PageScript.Call("where")), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            scroll = y;
        }

        var hasScripts = d is not null && await Task.Run(() => HasScripts(d.Text(0, Math.Min(d.Length, 32L * 1024 * 1024))));
        if (!string.Equals(path, _path, StringComparison.Ordinal) || !ReferenceEquals(d, _document) || _closing.IsCancellationRequested)
        {
            return;
        }

        _htmlHasScripts = hasScripts;
        _htmlShown = HtmlKey;
        _htmlTitle = null;
        _restoreScroll = scroll;
        if (_pageShown.Task.IsCompleted)
        {
            _pageShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        page.ShowHtml(path, null, HtmlScripts, HtmlWebContent);
        UpdateScriptsBar();
        UpdateStatus();
    }

    internal static bool HasScripts(string text)
        => text.Contains("<script", StringComparison.OrdinalIgnoreCase) || EventHandlerAttribute().IsMatch(text);

    [System.Text.RegularExpressions.GeneratedRegex(@"<[a-zA-Z][^>]*\son[a-z]+\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex EventHandlerAttribute();

    private void UpdateScriptsBar()
    {
        if (_isHtml && _pageMode && _htmlHasScripts && !HtmlScripts)
        {
            ShowBar(AppStrings.Viewer_ScriptsOff, AppStrings.Viewer_RunScriptsHere, ToggleHtmlScripts);
        }
        else if (_barAction == ToggleHtmlScripts)
        {
            HideLimitBar();
        }
    }

    internal void ToggleHtmlScripts()
    {
        _htmlScriptsHere = !_htmlScriptsHere;
        RenderPage(keepScroll: true);
    }

    internal void ToggleHtmlWeb()
    {
        _htmlWebHere = !_htmlWebHere;
        RenderPage(keepScroll: true);
    }

    private async Task ReadHtmlTitle(IPageView page)
    {
        _htmlTitle = await page.RunAsync("document.title") is { Length: > 0 } title ? title.Trim() : null;
    }

    private void HtmlStatus(List<Border> left)
    {
        if (_htmlTitle is { Length: > 0 } title)
        {
            Status(left, title);
        }

        Status(left, HtmlScripts ? AppStrings.Viewer_ScriptsRun : AppStrings.Viewer_ScriptsDontRun);
        if (!HtmlWebContent)
        {
            Status(left, AppStrings.Viewer_WebContentOff);
        }
    }

    private void HtmlMenuItems(Func<string, string?, Action, bool, MenuItem> item, Func<string, bool, Action, MenuItem> check)
    {
        var clipboard = _request.Clipboard is not null;
        item(AppStrings.Common_Copy, "Ctrl+C", () => _ = CopyPageSelectionAsync(), clipboard);
        item(AppStrings.Viewer_CopySource, null, CopyMarkdown, clipboard && _document is not null);
        check(AppStrings.Viewer_RunScripts, HtmlScripts, ToggleHtmlScripts);
        check(AppStrings.Viewer_ShowWebContent, HtmlWebContent, ToggleHtmlWeb);
        item(AppStrings.Viewer_Back, "Alt+Left", GoBack, _back.Count > 0);
        item(AppStrings.Viewer_OpenInBrowser, null, () => _ = OpenInProgramAsync(), _request.OpenFile is not null);
    }

    private static bool IsPageKind(ViewerKind kind) => kind is ViewerKind.Markdown or ViewerKind.Notebook or ViewerKind.Html;
}
