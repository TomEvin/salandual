// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Editor/EditorWindow.Preview.cs

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Viewer;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.FileSystem;
using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Editor;

internal sealed partial class EditorWindow
{
    private readonly Grid _body = new();
    private readonly Border _previewPane = new() { IsVisible = false };
    private readonly GridSplitter _previewSplitter = new() { IsVisible = false, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
    private readonly Panel _pageCell = new() { ClipToBounds = true };
    private readonly Border _previewNote = new() { IsVisible = false, BorderThickness = new Thickness(0, 0, 0, 1) };
    private readonly TextBlock _previewTitle = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Avalonia.Controls.Shapes.Ellipse _previewDot = new() { Width = 7, Height = 7, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pageWaiting = new() { Margin = new Thickness(16), FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _previewDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Control _textArea = null!;
    private ToggleButton _previewToggle = null!;
    private ToggleButton _syncToggle = null!;
    private Button _placeButton = null!;
    private Button _sideButton = null!;
    private IPageView? _page;
    private bool _previewOn;
    private bool _pageLoaded;
    private string? _pageHead;
    private int _previewVersion;
    private bool _previewDimmed;
    private bool _previewOpenedOnce;
    private bool _syncPending;
    private double? _previewScroll;

    internal PreviewKind? PreviewKindNow
    {
        get
        {
            if (_language == SyntaxLanguage.None)
            {
                return null;
            }

            var name = WindowsPath.GetName(_path);
            return name.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase) && _language == SyntaxLanguage.Json ? PreviewKind.Notebook
                : name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && _language == SyntaxLanguage.Xml ? PreviewKind.Svg
                : _language == SyntaxLanguage.Markdown ? PreviewKind.Markdown
                : _language == SyntaxLanguage.Html ? PreviewKind.Html
                : null;
        }
    }

    internal bool PreviewOpen => _previewOn;

    internal bool PreviewBelow => _previewOn && _body.RowDefinitions.Count == 3;

    internal IPageView? PreviewPage => _page;

    internal Border PreviewNote => _previewNote;

    internal Task PreviewRendering { get; private set; } = Task.CompletedTask;

    internal ToggleButton PreviewToggle => _previewToggle;

    private Control BuildBody(Control textArea)
    {
        _textArea = textArea;
        _body.Children.Add(textArea);
        _body.Children.Add(_previewSplitter);
        _body.Children.Add(_previewPane);
        _previewSplitter.Background = Brushes.Transparent;
        _previewSplitter.DragCompleted += (_, e) =>
        {
            if (e.Vector != default)
            {
                RememberSpot();
            }
        };
        _previewSplitter.DoubleTapped += (_, _) => HalfAndHalf();

        _syncToggle = new ToggleButton { Content = AppStrings.Editor_ScrollTogether, Padding = new Thickness(8, 3), FontSize = 12, Focusable = false };
        _syncToggle.Classes.Add("vtool");
        ToolTip.SetTip(_syncToggle, AppStrings.Editor_ScrollTogetherTip);
        _syncToggle.Click += (_, _) => UpdateEditor(o => o with { PreviewSync = _syncToggle.IsChecked == true });
        _placeButton = new Button { Padding = new Thickness(8, 3), FontSize = 12, Focusable = false };
        _placeButton.Classes.Add("vtool");
        _placeButton.Click += (_, _) => MovePreview(!PreviewBelow);
        _sideButton = Tool(Glyph("Icon.SwapSides"), AppStrings.Editor_PreviewToLeftTip);
        _sideButton.Focusable = false;
        _sideButton.Click += (_, _) => SwapPreviewSide();
        var viewer = new Button { Content = Label(null, AppStrings.Editor_OpenInViewer, "F3"), Padding = new Thickness(8, 3), FontSize = 12, Focusable = false };
        viewer.Classes.Add("vtool");
        ToolTip.SetTip(viewer, AppStrings.Editor_ViewTip);
        viewer.Click += (_, _) => ViewFile();
        var close = Tool(Glyph("Icon.Cross"), AppStrings.Editor_PreviewHide);
        close.Click += (_, _) => ShowPreview(false);

        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _previewTitle, _previewDot } };
        _previewTitle.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _syncToggle, _placeButton, _sideButton, viewer, close } };
        var header = new DockPanel { Margin = new Thickness(10, 3, 4, 3) };
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        header.Children.Add(title);
        var headerBar = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        _pageWaiting.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _pageCell.Children.Add(_pageWaiting);
        _pageCell.Res(Panel.BackgroundProperty, "Brush.PanelBackground");
        var dock = new DockPanel();
        DockPanel.SetDock(headerBar, Dock.Top);
        DockPanel.SetDock(_previewNote, Dock.Top);
        dock.Children.Add(headerBar);
        dock.Children.Add(_previewNote);
        dock.Children.Add(_pageCell);
        _previewPane.Child = dock;

        _previewDelay.Tick += (_, _) =>
        {
            _previewDelay.Stop();
            PreviewRendering = RenderPreviewAsync(force: false);
        };
        _editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => SyncLater();
        LayoutPreview();
        return _body;
    }

    private Control BuildPreviewToggle()
    {
        _previewToggle = Toggle(Label("Icon.Preview", AppStrings.Editor_Preview, "F6"), AppStrings.Editor_PreviewTip);
        _previewToggle.Click += (_, _) => ShowPreview(!_previewOn);
        return _previewToggle;
    }

    internal void ShowPreview(bool on)
    {
        on &= PreviewKindNow is not null;
        if (on == _previewOn)
        {
            UpdateToolbar();
            return;
        }

        _previewOn = on;
        if (on && _page is null && _request.PageView?.Invoke() is { } page)
        {
            _page = page;
            page.Loaded += () => Dispatcher.UIThread.Post(OnPreviewLoaded);
            page.Navigating += uri => Dispatcher.UIThread.Post(() => OnPreviewLink(uri));
            page.Message += json => Dispatcher.UIThread.Post(() => OnPreviewMessage(json));
            _pageCell.Children.Insert(0, page.Control);
            SizePage();
        }

        _pageWaiting.Text = _page is null ? AppStrings.Editor_PreviewNoBrowser : AppStrings.Editor_PreviewStarting;
        LayoutPreview();
        UpdateToolbar();
        if (on)
        {
            PreviewRendering = RenderPreviewAsync(force: true);
        }
    }

    private void SizePage()
    {
        if (_page?.Control is { } c)
        {
            c.Width = _pageLoaded ? double.NaN : 1;
            c.Height = _pageLoaded ? double.NaN : 1;
            c.HorizontalAlignment = _pageLoaded ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            c.VerticalAlignment = _pageLoaded ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        }

        _pageWaiting.IsVisible = !_pageLoaded;
    }

    private void LayoutPreview()
    {
        _previewPane.IsVisible = _previewSplitter.IsVisible = _previewOn;
        if (!_previewOn || PreviewKindNow is not { } kind)
        {
            _body.ColumnDefinitions = new ColumnDefinitions("*");
            _body.RowDefinitions = new RowDefinitions("*");
            Place(_textArea, 0, 0);
            Place(_previewSplitter, 0, 0);
            Place(_previewPane, 0, 0);
            return;
        }

        var spot = Options.SpotFor(kind);
        var share = Math.Clamp(spot.Share, 0.2, 0.8);
        var text = new GridLength(1 - share, GridUnitType.Star);
        var preview = new GridLength(share, GridUnitType.Star);
        var (textAt, previewAt) = spot.Before ? (2, 0) : (0, 2);
        if (spot.Below)
        {
            _body.ColumnDefinitions = new ColumnDefinitions("*");
            _body.RowDefinitions = spot.Before
                ? [new RowDefinition(preview) { MinHeight = 80 }, new RowDefinition(new GridLength(5)), new RowDefinition(text) { MinHeight = 80 }]
                : [new RowDefinition(text) { MinHeight = 80 }, new RowDefinition(new GridLength(5)), new RowDefinition(preview) { MinHeight = 80 }];
            Place(_textArea, textAt, 0);
            Place(_previewSplitter, 1, 0);
            Place(_previewPane, previewAt, 0);
            _previewSplitter.ResizeDirection = GridResizeDirection.Rows;
            _previewPane.BorderThickness = spot.Before ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0);
        }
        else
        {
            _body.RowDefinitions = new RowDefinitions("*");
            _body.ColumnDefinitions = spot.Before
                ? [new ColumnDefinition(preview) { MinWidth = 160 }, new ColumnDefinition(new GridLength(5)), new ColumnDefinition(text) { MinWidth = 160 }]
                : [new ColumnDefinition(text) { MinWidth = 160 }, new ColumnDefinition(new GridLength(5)), new ColumnDefinition(preview) { MinWidth = 160 }];
            Place(_textArea, 0, textAt);
            Place(_previewSplitter, 0, 1);
            Place(_previewPane, 0, previewAt);
            _previewSplitter.ResizeDirection = GridResizeDirection.Columns;
            _previewPane.BorderThickness = spot.Before ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0);
        }

        ToolTip.SetTip(_sideButton, spot.Below
            ? (spot.Before ? AppStrings.Editor_PreviewToBottomTip : AppStrings.Editor_PreviewToTopTip)
            : (spot.Before ? AppStrings.Editor_PreviewToRightTip : AppStrings.Editor_PreviewToLeftTip));

        _previewPane.Res(Border.BorderBrushProperty, "Brush.Border");
        _placeButton.Content = spot.Below ? AppStrings.Editor_PreviewBeside : AppStrings.Editor_PreviewBelow;
        ToolTip.SetTip(_placeButton, spot.Below ? AppStrings.Editor_PreviewBesideTip : AppStrings.Editor_PreviewBelowTip);
        _syncToggle.IsChecked = Options.PreviewSync;
        _previewTitle.Inlines!.Clear();
        _previewTitle.Inlines.Add(new Avalonia.Controls.Documents.Run(AppStrings.Editor_Preview) { FontWeight = FontWeight.SemiBold });
        _previewTitle.Inlines.Add(new Avalonia.Controls.Documents.Run(" · " + (kind switch
        {
            PreviewKind.Notebook => AppStrings.Editor_PreviewNotebook,
            PreviewKind.Svg => AppStrings.Editor_PreviewPicture,
            PreviewKind.Html => AppStrings.Editor_PreviewHtml,
            _ => AppStrings.Editor_PreviewMarkdown,
        })));
        UpdateDot(busy: false);

        static void Place(Control c, int row, int column)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, column);
        }
    }

    private void SetShare(double share)
    {
        var before = PreviewKindNow is { } kind && Options.SpotFor(kind).Before;
        var (text, preview) = before ? (2, 0) : (0, 2);
        if (_body.ColumnDefinitions.Count == 3)
        {
            _body.ColumnDefinitions[text].Width = new GridLength(1 - share, GridUnitType.Star);
            _body.ColumnDefinitions[preview].Width = new GridLength(share, GridUnitType.Star);
        }
        else if (_body.RowDefinitions.Count == 3)
        {
            _body.RowDefinitions[text].Height = new GridLength(1 - share, GridUnitType.Star);
            _body.RowDefinitions[preview].Height = new GridLength(share, GridUnitType.Star);
        }
    }

    internal void SwapPreviewSide()
    {
        if (PreviewKindNow is not { } kind)
        {
            return;
        }

        SaveSpot(Options.SpotFor(kind) with { Before = !Options.SpotFor(kind).Before });
        LayoutPreview();
    }

    internal bool PreviewBefore => Grid.GetColumn(_textArea) == 2 || Grid.GetRow(_textArea) == 2;

    internal void HalfAndHalf()
    {
        if (PreviewKindNow is not { } kind)
        {
            return;
        }

        SetShare(0.5);
        SaveSpot(Options.SpotFor(kind) with { Share = 0.5 });
    }

    private void RememberSpot()
    {
        if (PreviewKindNow is not { } kind)
        {
            return;
        }

        var below = PreviewBelow;
        var total = below ? _textArea.Bounds.Height + _previewPane.Bounds.Height : _textArea.Bounds.Width + _previewPane.Bounds.Width;
        var share = total > 0 ? (below ? _previewPane.Bounds.Height : _previewPane.Bounds.Width) / total : 0.5;
        SaveSpot(new PreviewSpot { Kind = kind, Below = below, Before = Options.SpotFor(kind).Before, Share = Math.Clamp(share, 0.2, 0.8) });
    }

    internal void MovePreview(bool below)
    {
        if (PreviewKindNow is not { } kind)
        {
            return;
        }

        SaveSpot(Options.SpotFor(kind) with { Below = below });
        LayoutPreview();
    }

    private void SaveSpot(PreviewSpot spot) => UpdateEditor(o => o with { PreviewSpots = [.. o.PreviewSpots.Where(s => s.Kind != spot.Kind), spot] });

    private void UpdateDot(bool busy)
    {
        _previewDot.Res(Avalonia.Controls.Shapes.Shape.FillProperty, busy ? "Brush.Warning" : "Brush.Success");
        ToolTip.SetTip(_previewDot, busy ? AppStrings.Editor_PreviewUpdating : IsLive ? AppStrings.Editor_PreviewLive : AppStrings.Editor_PreviewOnSave);
    }

    private bool IsLive => Options.PreviewLiveUpToMB > 0 && _editor.Document.TextLength <= Options.PreviewLiveUpToMB * 1024 * 1024;

    private void PreviewLater()
    {
        if (!_previewOn)
        {
            return;
        }

        if (!IsLive)
        {
            ShowBigNote();
            return;
        }

        UpdateDot(busy: true);
        _previewDelay.Stop();
        _previewDelay.Interval = TimeSpan.FromMilliseconds(PreviewKindNow == PreviewKind.Html ? 1000 : 250);
        _previewDelay.Start();
    }

    internal void PreviewAfterSave()
    {
        if (_previewOn)
        {
            PreviewRendering = RenderPreviewAsync(force: true);
        }
    }

    private PageOptions PreviewOptions(PreviewKind kind)
    {
        var viewers = _settings.Current.Viewers;
        return new PageOptions
        {
            Colors = PageLook.Colors(this),
            Dark = PageLook.IsDark(this),
            WebImages = viewers.MarkdownWebImages,
            HideCode = viewers.NotebookHideCode,
            FoldOutputs = viewers.NotebookFoldOutputs,
            SourceLines = kind == PreviewKind.Markdown,
        };
    }

    private async Task RenderPreviewAsync(bool force)
    {
        _previewDelay.Stop();
        if (!_previewOn || PreviewKindNow is not { } kind)
        {
            return;
        }

        if (!force && !IsLive)
        {
            ShowBigNote();
            return;
        }

        if (kind == PreviewKind.Html)
        {
            await ShowHtmlPreviewAsync();
            return;
        }

        var version = ++_previewVersion;
        UpdateDot(busy: true);
        var snapshot = _editor.Document.CreateSnapshot();
        var options = PreviewOptions(kind);
        string? html = null;
        string? error = null;
        try
        {
            html = await Task.Run(() =>
            {
                var text = snapshot.Text;
                return kind switch
                {
                    PreviewKind.Notebook => NotebookPage.Render(text, options).Html,
                    PreviewKind.Svg => SvgPage.Render(text, options),
                    _ => MarkdownPage.Render(text, options).Html,
                };
            });
        }
        catch (NotebookFormatException e)
        {
            error = Loc.Format(AppStrings.Editor_PreviewMistake, e.Message);
        }
        catch (SvgFormatException e)
        {
            error = Loc.Format(AppStrings.Editor_PreviewSvgMistake, e.Line, e.Message);
        }

        if (version != _previewVersion || !_previewOn)
        {
            return;
        }

        UpdateDot(busy: false);
        if (_page is not { } page)
        {
            return;
        }

        if (error is not null)
        {
            ShowPreviewNote(error, null);
            _previewDimmed = true;
            await page.RunAsync(PageScript.Call("dim", true));
            return;
        }

        if (IsLive)
        {
            _previewNote.IsVisible = false;
        }
        else
        {
            ShowBigNote();
        }

        if (_previewDimmed)
        {
            _previewDimmed = false;
            await page.RunAsync(PageScript.Call("dim", false));
        }

        var main = html!.IndexOf("<main", StringComparison.Ordinal);
        var bodyStart = main < 0 ? -1 : html.IndexOf('>', main) + 1;
        var bodyEnd = html.LastIndexOf("</main>", StringComparison.Ordinal);
        if (bodyStart > 0 && bodyEnd >= bodyStart)
        {
            var head = html[..bodyStart];
            if (_pageLoaded && head == _pageHead && await page.RunAsync(PageScript.Call("replace", html[bodyStart..bodyEnd])) == "ok")
            {
                SyncLater();
                return;
            }

            _pageHead = head;
        }

        var folder = WindowsPath.GetParent(_path) ?? _path;
        _pageLoaded = false;
        page.Show(html, new Uri(folder.EndsWith('\\') ? folder : folder + "\\"));
    }

    private async Task ShowHtmlPreviewAsync()
    {
        ++_previewVersion;
        UpdateDot(busy: false);
        if (_page is not { } page)
        {
            return;
        }

        if (IsLive)
        {
            _previewNote.IsVisible = false;
        }
        else
        {
            ShowBigNote();
        }

        _previewScroll = _pageLoaded && double.TryParse(await page.RunAsync(PageScript.Call("where")), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) ? y : null;
        var viewers = _settings.Current.Viewers;
        _pageLoaded = false;
        _pageHead = null;
        page.ShowHtml(_path, _editor.Document.Text, viewers.HtmlScripts, viewers.HtmlWebContent);
    }

    private void OnPreviewLoaded()
    {
        _pageLoaded = true;
        SizePage();
        if (_previewScroll is { } y && !Options.PreviewSync)
        {
            _ = _page?.RunAsync(PageScript.Call("to", y));
        }

        _previewScroll = null;
        SyncLater();
    }

    private void ShowBigNote()
        => ShowPreviewNote(Loc.Format(AppStrings.Editor_PreviewBig, Size(_editor.Document.TextLength), Options.PreviewLiveUpToMB), (AppStrings.Editor_PreviewUpdateNow, () => PreviewRendering = RenderPreviewAsync(force: true)));

    private void ShowPreviewNote(string text, (string Label, Action Run)? action)
    {
        var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.Text");
        var row = new DockPanel { Margin = new Thickness(10, 5) };
        if (action is { } a)
        {
            var b = LinkButton(a.Label, a.Run);
            b.Padding = new Thickness(10, 2);
            b.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(b, Dock.Right);
            row.Children.Add(b);
        }

        row.Children.Add(block);
        _previewNote.Child = row;
        _previewNote.Res(Border.BackgroundProperty, action is null ? "Brush.WarningBackground" : "Brush.InfoBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        _previewNote.IsVisible = true;
    }

    private void SyncLater()
    {
        if (_syncPending || !_previewOn || !_pageLoaded || !Options.PreviewSync)
        {
            return;
        }

        _syncPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _syncPending = false;
            SyncPreview();
        }, DispatcherPriority.Background);
    }

    internal void SyncPreview()
    {
        if (_page is not { } page || !_pageLoaded || !Options.PreviewSync)
        {
            return;
        }

        var view = _editor.TextArea.TextView;
        if (PreviewKindNow == PreviewKind.Markdown)
        {
            var top = view.GetDocumentLineByVisualTop(view.VerticalOffset)?.LineNumber ?? 1;
            _ = page.RunAsync(PageScript.Call("line", top - 1));
        }
        else
        {
            var max = view.DocumentHeight - view.Bounds.Height;
            _ = page.RunAsync(PageScript.Call("fraction", max > 0 ? Math.Clamp(view.VerticalOffset / max, 0, 1) : 0.0));
        }
    }

    private void OnPreviewMessage(string json)
    {
        try
        {
            using var message = JsonDocument.Parse(json);
            var root = message.RootElement;
            var kind = root.TryGetProperty("t", out var t) ? t.GetString() : null;
            if (kind == "line" && root.TryGetProperty("line", out var l) && l.TryGetInt32(out var line))
            {
                var target = Math.Clamp(line + 1, 1, _editor.Document.LineCount);
                _editor.CaretOffset = _editor.Document.GetLineByNumber(target).Offset;
                _editor.Select(_editor.CaretOffset, 0);
                _editor.ScrollTo(target, 1);
                _editor.Focus();
            }
            else if (kind == "menu")
            {
                ShowPreviewMenu(root);
            }
            else if (kind == "key" && ViewerWindow.PageKeyName(root.TryGetProperty("key", out var k) ? k.GetString() : null) is { } key)
            {
                bool Flag(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
                var mods = (Flag("ctrl") ? KeyModifiers.Control : 0) | (Flag("alt") ? KeyModifiers.Alt : 0) | (Flag("shift") ? KeyModifiers.Shift : 0);
                RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = mods, Source = this });
            }
        }
        catch (JsonException)
        {
        }
    }

    internal void OnPreviewLink(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        {
            _request.OpenWeb?.Invoke(uri);
        }
        else if (uri.IsFile && uri.Fragment.Length > 1)
        {
            _ = _page?.RunAsync(PageScript.Call("go", Uri.UnescapeDataString(uri.Fragment[1..])));
        }
    }

    private void OpenPreviewByItself()
    {
        if (!_previewOpenedOnce && PreviewKindNow is { } kind && Options.PreviewOpens.Contains(kind))
        {
            _previewOpenedOnce = true;
            ShowPreview(true);
        }
    }

    private void ClosePreviewPage()
    {
        _previewDelay.Stop();
        _page?.Dispose();
        _page = null;
    }
}
