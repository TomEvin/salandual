// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Toolbar.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.FileSystem;
using AppIcons = Salandual.App.Theme.AppIcons;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal sealed partial class ViewerWindow
{
    private const int CopyLimit = 32 * 1024 * 1024;

    private readonly Border _findBar = new();
    private readonly TextBox _findBox = new() { Width = 300, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ToggleButton _matchCase = Flag("Aa", AppStrings.Viewer_MatchCase);
    private readonly ToggleButton _wholeWords = Flag("W", AppStrings.Viewer_WholeWords);
    private readonly ToggleButton _regex = Flag(".*", AppStrings.Viewer_Regex);
    private readonly ToggleButton _findTextMode = Mode(AppStrings.Viewer_FindText, null);
    private readonly ToggleButton _findHexMode = Mode(AppStrings.Viewer_FindHexBytes, null);
    private readonly TextBlock _hits = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 80 };
    private readonly DispatcherTimer _findDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _findCancel;

    private Button _previousFile = null!;
    private Button _nextFile = null!;
    private TextBlock _counter = null!;
    private ToggleButton _textMode = null!;
    private ToggleButton _hexModeButton = null!;
    private Button _encoding = null!;
    private ToggleButton _wrap = null!;
    private ToggleButton _numbers = null!;
    private Button _characters = null!;
    private Button _bytesPerRow = null!;
    private Button _groups = null!;
    private FitRow _textTools = null!;
    private FitRow _hexTools = null!;
    private ToggleButton _findToggle = null!;
    private Button _goTo = null!;
    private Button _more = null!;
    private Border _afterModes = null!;
    private TextBox? _goToBox;
    private TextBlock? _goToNote;

    internal string HitsText => _hits.Text ?? "";

    internal TextBox FindBox => _findBox;

    internal Button FindPieces { get; private set; } = null!;

    internal ToggleButton RegexFlag => _regex;

    internal bool FindBarAtBottom => DockPanel.GetDock(_findBar) == Dock.Bottom;

    internal TextBlock Counter => _counter;

    internal FitRow ToolbarLeft { get; private set; } = null!;

    internal FitRow ToolbarRight { get; private set; } = null!;

    internal Task Finding { get; private set; } = Task.CompletedTask;

    private Control BuildToolbar()
    {
        _previousFile = Tool(Glyph("Icon.ChevronLeft"), AppStrings.Viewer_PreviousTip);
        _previousFile.Click += (_, _) => _ = StepFileAsync(-1);
        _nextFile = Tool(Glyph("Icon.ChevronRight"), AppStrings.Viewer_NextTip);
        _nextFile.Click += (_, _) => _ = StepFileAsync(1);
        _counter = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");

        _textMode = Mode(AppStrings.Viewer_Text, "F5");
        _hexModeButton = Mode(AppStrings.Viewer_Hex, "F4");
        _imageModeButton = Mode(AppStrings.Viewer_Image, null);
        _textMode.Click += (_, _) => SetMode(false);
        _imageModeButton.Click += (_, _) => ShowImage();
        _hexModeButton.Click += (_, _) => _ = ShowHexAsync();
        var modes = _modes = Segment(BuildPageModeButton(), BuildTreeModeButton(), _textMode, _imageModeButton, BuildMediaModeButton(), _hexModeButton);

        _encoding = Drop(AppStrings.Viewer_EncodingTip);
        _encoding.Click += (_, _) => ShowEncodingMenu(_encoding, characters: false);
        _wrap = Toggle(Label("Icon.Wrap", AppStrings.Viewer_Wrap, "F2"), AppStrings.Viewer_WrapTip);
        _wrap.Click += (_, _) => UpdateViewer(v => v with { Wrap = _wrap.IsChecked == true });
        _numbers = Toggle(Label("Icon.LineNumbers", AppStrings.Viewer_LineNumbers, null), AppStrings.Viewer_LineNumbers);
        _numbers.Click += (_, _) => UpdateViewer(v => v with { LineNumbers = _numbers.IsChecked == true });
        _textTools = Row(_encoding, _wrap, _numbers);

        _characters = Drop(AppStrings.Viewer_CharactersTip);
        _characters.Click += (_, _) => ShowEncodingMenu(_characters, characters: true);
        _bytesPerRow = Drop(AppStrings.Viewer_BytesTip);
        _bytesPerRow.Click += (_, _) => ShowChoiceMenu(_bytesPerRow, ViewerSettings.BytesPerRowChoices, Options.HexBytesPerRow, BytesLabel, n => UpdateViewer(v => v with { HexBytesPerRow = n }));
        _groups = Drop(AppStrings.Viewer_GroupsTip);
        _groups.Click += (_, _) => ShowChoiceMenu(_groups, ViewerSettings.GroupChoices, Options.HexGroup, GroupLabel, n => UpdateViewer(v => v with { HexGroup = n }));
        _hexTools = Row(_characters, _bytesPerRow, _groups);

        _findToggle = Toggle(Label("Icon.Find", AppStrings.Viewer_Find, "Ctrl+F"), AppStrings.Viewer_FindTip);
        _findToggle.Click += (_, _) =>
        {
            if (_findBar.IsVisible)
            {
                CloseFind();
            }
            else
            {
                OpenFind();
            }
        };
        _goTo = Tool(Label("Icon.GoTo", AppStrings.Viewer_GoTo, "Ctrl+G"), AppStrings.Viewer_GoToTip);
        _goTo.Click += (_, _) => OpenGoTo();
        _more = Tool(MoreContent(), AppStrings.Viewer_MoreTip);
        _more.Click += (_, _) => ShowMoreMenu();

        var imageTools = BuildImageTools();
        var left = ToolbarLeft = Row(_previousFile, _counter, _nextFile, Separator(), modes, _afterModes = Separator(), _textTools, _hexTools, imageTools, BuildPageTools(), BuildTreeTools());
        var right = ToolbarRight = Row(_infoButton, _copyImage, BuildCopyValueButton(), BuildContentsButton(), _findToggle, _goTo, _more);
        WatchOverflow();
        var dock = new DockPanel { Margin = new Thickness(8, 4) };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(left);
        return new Border { Child = dock, BorderThickness = new Thickness(0, 0, 0, 1) }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
    }

    private void UpdateToolbar()
    {
        if (_textMode is null)
        {
            return;
        }

        var list = _request.Siblings;
        var navigable = _siblingIndex >= 0 && list.Count > 0;
        _previousFile.IsEnabled = navigable && _siblingIndex > 0;
        _nextFile.IsEnabled = navigable && _siblingIndex < list.Count - 1;
        _counter.Text = navigable ? Loc.Format(AppStrings.Viewer_Position, _siblingIndex + 1, list.Count, _request.Kind == ViewerKind.Tree ? TreeKindName() : KindName(_request.Kind)) : "";
        _counter.IsVisible = navigable;
        _previousFile.IsVisible = _nextFile.IsVisible = navigable;

        var textual = !_imageMode && !_mediaMode && !_pageMode && !_treeMode;
        _treeModeButton.IsVisible = _isTree;
        _treeModeButton.IsChecked = _treeMode;
        _treeTools.IsVisible = _treeMode;
        _copyValue.IsVisible = _treeMode;
        _copyValue.IsEnabled = _treeView.Selected is not null && _request.Clipboard is not null;
        ToolTip.SetTip(_findToggle, _treeMode ? AppStrings.Viewer_FindTreeTip : AppStrings.Viewer_FindTip);
        OrderModes();
        SetModeLabel(_textMode, _isNotebook ? AppStrings.Viewer_Json : AppStrings.Viewer_Text);
        _textMode.IsVisible = !_isImage && !_isMedia;
        _imageModeButton.IsVisible = _isImage;
        _mediaModeButton.IsVisible = _isMedia;
        _pageModeButton.IsVisible = _isMarkdown;
        _pageModeButton.IsChecked = _pageMode;
        _pageTools.IsVisible = _pageMode;
        _pageZoomBox.Percent = _pageZoom;
        _contentsButton.IsVisible = _pageMode && !_isHtml;
        _contentsButton.IsChecked = Options.MarkdownContents;
        _findKinds.IsVisible = !_pageMode && !_treeMode;
        _textMode.IsChecked = !_hexMode && textual;
        _imageModeButton.IsChecked = _imageMode;
        _mediaModeButton.IsChecked = _mediaMode;
        _hexModeButton.IsChecked = _hexMode && textual;
        _textTools.IsVisible = !_hexMode && textual;
        _hexTools.IsVisible = _hexMode && textual;
        _imageTools.IsVisible = _imageMode;
        _copyImage.IsVisible = _imageMode;
        _copyImage.IsEnabled = _imageView.Image is not null;
        _findToggle.IsVisible = textual || _pageMode || _treeMode;
        _goTo.IsVisible = textual;
        _afterModes.IsVisible = !_mediaMode;
        UpdateImageTools();
        _wrap.IsChecked = Options.Wrap;
        _numbers.IsChecked = Options.LineNumbers;
        _findToggle.IsChecked = _findBar.IsVisible;
        SetDropText(_encoding, _document?.Encoding.Name ?? TextEncodings.Default.Name);
        SetDropText(_characters, _hex.CharactersName);
        SetDropText(_bytesPerRow, BytesLabel(Options.HexBytesPerRow));
        SetDropText(_groups, GroupLabel(Options.HexGroup));
        ToolTip.SetTip(_goTo, _hexMode ? AppStrings.Viewer_GoToHexTip : AppStrings.Viewer_GoToTip);
        UpdateInfo();
    }

    private void OrderModes()
    {
        var row = (StackPanel)_modes.Child!;
        ToggleButton[] used = [_pageModeButton, _treeModeButton, _textMode, _imageModeButton, _mediaModeButton, _hexModeButton];
        ToggleButton[] wanted = Options.ViewButtons == ViewButtonOrder.KeyOrder
            ? [_imageModeButton, _mediaModeButton, _hexModeButton, _textMode, _treeModeButton, _pageModeButton]
            : used;
        if (!row.Children.SequenceEqual(wanted))
        {
            row.Children.Clear();
            row.Children.AddRange(wanted);
        }
    }

    private static void SetModeLabel(ToggleButton mode, string label)
    {
        if ((mode.Content as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault() is { } text && text.Text != label)
        {
            text.Text = label;
        }
    }

    internal IReadOnlyList<string> ModeOrder
        => [.. ((StackPanel)_modes.Child!).Children.Where(c => c.IsVisible).Select(c => ((c as ToggleButton)?.Content as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "")];

    private static string BytesLabel(int n) => n == 0 ? AppStrings.Viewer_BytesFit : Loc.Format(AppStrings.Viewer_BytesPerRow, n);

    private static string GroupLabel(int n) => Loc.Format(AppStrings.Viewer_Groups, n);

    private static string KindName(ViewerKind kind) => kind switch
    {
        ViewerKind.Image => AppStrings.Viewer_Kind_Images,
        ViewerKind.Media => AppStrings.Viewer_Kind_Media,
        ViewerKind.Markdown => AppStrings.Viewer_Kind_Markdown,
        ViewerKind.Notebook => AppStrings.Viewer_Kind_Notebooks,
        ViewerKind.Html => AppStrings.Viewer_Kind_Html,
        _ => AppStrings.Viewer_Kind_Text,
    };

    private void ShowEncodingMenu(Control at, bool characters)
    {
        if (_document is not { } d)
        {
            return;
        }

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        if (!characters)
        {
            var detected = new MenuItem { Header = Loc.Format(AppStrings.Viewer_DetectedItem, d.Guess.Encoding.Name) };
            detected.Click += (_, _) => SetEncoding(d.Guess.Encoding);
            menu.Items.Add(detected);
            menu.Items.Add(new Separator());
        }
        foreach (var encoding in TextEncodings.All)
        {
            if (characters && !encoding.SingleByte)
            {
                continue;
            }

            var item = new MenuItem { Header = encoding.Name, ToggleType = MenuItemToggleType.Radio, IsChecked = encoding.Id == (characters ? Find(_hex.CharactersName) : d.Encoding.Id) };
            item.Click += (_, _) =>
            {
                if (characters)
                {
                    _hex.Characters = encoding;
                    UpdateToolbar();
                    UpdateStatus();
                }
                else
                {
                    SetEncoding(encoding);
                }
            };
            menu.Items.Add(item);
        }

        menu.ShowAt(at);

        static string? Find(string name) => TextEncodings.All.FirstOrDefault(e => e.Name == name)?.Id;
    }

    private static void ShowChoiceMenu(Control at, int[] choices, int current, Func<int, string> label, Action<int> choose)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var n in choices)
        {
            var item = new MenuItem { Header = label(n), ToggleType = MenuItemToggleType.Radio, IsChecked = n == current };
            item.Click += (_, _) => choose(n);
            menu.Items.Add(item);
        }

        menu.ShowAt(at);
    }

    internal MenuFlyout BuildMoreMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        MenuItem Item(string header, string? key, Action click, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGesture = key is null ? null : KeyGesture.Parse(key), IsEnabled = enabled };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }

        MenuItem Check(string header, bool on, Action click)
        {
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }

        if (OverflowItems() is { Count: > 0 } overflow)
        {
            menu.Items.Add(new MenuItem { Header = AppStrings.Viewer_NotOnToolbar, IsEnabled = false });
            foreach (var item in overflow)
            {
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
        }

        var selection = Current.HasSelection;
        if (_imageMode)
        {
            if (!OnToolbar(_copyImage))
            {
                Item(AppStrings.Viewer_CopyImage, "Ctrl+C", () => _ = CopyImageAsync(), _imageView.Image is not null);
            }

            Item(AppStrings.Viewer_FlipHorizontally, "H", () => _imageView.Flip(horizontally: true), _imageView.Image is not null);
            Item(AppStrings.Viewer_FlipVertically, "V", () => _imageView.Flip(horizontally: false), _imageView.Image is not null);
        }
        else if (_mediaMode)
        {
            Item(AppStrings.Viewer_MediaCopy, "Ctrl+C", CopyMediaText, Media is not null);
            Item(AppStrings.Viewer_MediaOpen, Options.MediaEnter == MediaEnter.OpenInDefaultApp ? "Enter" : null, () => _ = OpenInProgramAsync(), _request.OpenFile is not null);
        }
        else if (_pageMode && _isHtml)
        {
            HtmlMenuItems(Item, Check);
        }
        else if (_pageMode)
        {
            var clipboard = _request.Clipboard is not null;
            Item(AppStrings.Common_Copy, "Ctrl+C", () => _ = CopyPageSelectionAsync(), clipboard);
            if (_isNotebook)
            {
                Item(AppStrings.Viewer_CopyAllCode, null, CopyAllCode, clipboard && Page?.Notebook is not null);
                Item(AppStrings.Viewer_CopyJson, null, CopyMarkdown, clipboard && _document is not null);
            }
            else
            {
                Item(AppStrings.Viewer_CopyMarkdown, null, CopyMarkdown, clipboard && _document is not null);
                Item(AppStrings.Viewer_CopyHtml, null, CopyPageHtml, clipboard && Page is not null);
            }
            if (!Options.MarkdownWebImages && Page is { WebImages: > 0 })
            {
                Check(AppStrings.Viewer_WebImagesHere, _webImagesHere, () =>
                {
                    _webImagesHere = !_webImagesHere;
                    RenderPage(keepScroll: true);
                });
            }

            Item(AppStrings.Viewer_Back, "Alt+Left", GoBack, _back.Count > 0);
            Item(AppStrings.Viewer_MediaOpen, null, () => _ = OpenInProgramAsync(), _request.OpenFile is not null);
        }
        else if (_treeMode)
        {
            foreach (var item in TreeItems(withFolding: true, besideToolbar: true))
            {
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
        }
        else
        {
            Item(AppStrings.Common_Copy, "Ctrl+C", () => _ = CopyAsync(), selection);
            Item(AppStrings.Viewer_SaveSelection, "Ctrl+S", () => _ = SaveSelectionAsync(), selection);
        }

        Item(AppStrings.Viewer_Reload, "Ctrl+R", Reload);
        Item(AppStrings.Viewer_ShowInPanel, null, () => _request.ShowInPanel?.Invoke(_path), _request.ShowInPanel is not null);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = AppStrings.Viewer_QuickSwitches, IsEnabled = false });
        if (_imageMode)
        {
            var canvas = new MenuItem { Header = AppStrings.Viewer_CanvasColor };
            foreach (var value in new[] { CanvasColor.Dark, CanvasColor.Black, CanvasColor.Grey, CanvasColor.Magenta, CanvasColor.Theme })
            {
                var item = new MenuItem { Header = CanvasLabel(value), ToggleType = MenuItemToggleType.Radio, IsChecked = Options.ImageCanvas == value };
                item.Click += (_, _) => UpdateViewer(v => v with { ImageCanvas = value });
                canvas.Items.Add(item);
            }

            canvas.Items.Add(new Separator());
            var custom = new MenuItem { Header = AppStrings.Viewer_Canvas_Custom, ToggleType = MenuItemToggleType.Radio, IsChecked = Options.ImageCanvas == CanvasColor.Custom, IsEnabled = _request.OpenOptions is not null };
            custom.Click += (_, _) => _request.OpenOptions?.Invoke("viewers");
            canvas.Items.Add(custom);
            menu.Items.Add(canvas);

            var zoom = new MenuItem { Header = AppStrings.Opt_ImageZoom };
            foreach (var (label, value) in new[] { (AppStrings.Viewer_Fit, ImageZoom.Fit), (AppStrings.Viewer_Width, ImageZoom.Width), (AppStrings.Viewer_Actual, ImageZoom.Actual) })
            {
                var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.Radio, IsChecked = Options.ImageZoom == value };
                item.Click += (_, _) => UpdateViewer(v => v with { ImageZoom = value });
                zoom.Items.Add(item);
            }

            menu.Items.Add(zoom);
            Check(AppStrings.Viewer_InfoInWindow, Options.PhotoInfoPlace == InfoPlace.Window,
                () => UpdateViewer(v => v with { PhotoInfoPlace = v.PhotoInfoPlace == InfoPlace.Window ? InfoPlace.Panel : InfoPlace.Window, PhotoInfo = true }));
        }
        else if (_mediaMode)
        {
            Check(AppStrings.Viewer_MediaEnterOpens, Options.MediaEnter == MediaEnter.OpenInDefaultApp,
                () => UpdateViewer(v => v with { MediaEnter = v.MediaEnter == MediaEnter.OpenInDefaultApp ? MediaEnter.Nothing : MediaEnter.OpenInDefaultApp }));
            Check(AppStrings.Opt_VideoFrame, Options.VideoFrame, () => UpdateViewer(v => v with { VideoFrame = !v.VideoFrame }));
        }
        else if (_pageMode && _isHtml)
        {
            Check(AppStrings.Viewer_FindAtBottom, Options.FindBar == FindBarPosition.Bottom,
                () => UpdateViewer(v => v with { FindBar = v.FindBar == FindBarPosition.Top ? FindBarPosition.Bottom : FindBarPosition.Top }));
        }
        else if (_pageMode)
        {
            if (_isNotebook)
            {
                Check(AppStrings.Viewer_HideCode, Options.NotebookHideCode, () => UpdateViewer(v => v with { NotebookHideCode = !v.NotebookHideCode }));
            }

            Check(AppStrings.Viewer_FullWidth, Options.MarkdownFullWidth, () => UpdateViewer(v => v with { MarkdownFullWidth = !v.MarkdownFullWidth }));
            Check(AppStrings.Opt_MdWebImages, Options.MarkdownWebImages, () => UpdateViewer(v => v with { MarkdownWebImages = !v.MarkdownWebImages }));
            Check(AppStrings.Viewer_FindAtBottom, Options.FindBar == FindBarPosition.Bottom,
                () => UpdateViewer(v => v with { FindBar = v.FindBar == FindBarPosition.Top ? FindBarPosition.Bottom : FindBarPosition.Top }));
        }
        else if (_treeMode)
        {
            Check(AppStrings.Viewer_TreeColors, Options.TextColors, () => UpdateViewer(v => v with { TextColors = !v.TextColors }));
            Check(AppStrings.Viewer_FindAtBottom, Options.FindBar == FindBarPosition.Bottom,
                () => UpdateViewer(v => v with { FindBar = v.FindBar == FindBarPosition.Top ? FindBarPosition.Bottom : FindBarPosition.Top }));
        }
        else
        {
            if (!_hexMode && Salandual.Core.Viewer.SyntaxRules.LanguageFor(WindowsPath.GetName(_path)) != Salandual.Core.Viewer.SyntaxLanguage.None)
            {
                Check(AppStrings.Viewer_Colors, Options.TextColors, () => UpdateViewer(v => v with { TextColors = !v.TextColors }));

                var themes = new MenuItem { Header = AppStrings.Viewer_Themes, IsEnabled = _request.OpenOptions is not null };
                themes.Click += (_, _) => _request.OpenOptions?.Invoke(OptionsWindow.ColorsPage);
                menu.Items.Add(themes);
            }

            Check(AppStrings.Viewer_FindAtBottom, Options.FindBar == FindBarPosition.Bottom,
                () => UpdateViewer(v => v with { FindBar = v.FindBar == FindBarPosition.Top ? FindBarPosition.Bottom : FindBarPosition.Top }));
        }

        if (_hexMode && !_imageMode && !_mediaMode)
        {
            var numbers = new MenuItem { Header = AppStrings.Viewer_Numbers };
            foreach (var (label, asDecimal) in new[] { (AppStrings.Viewer_AllHex, false), (AppStrings.Viewer_AllDecimal, true) })
            {
                var all = new MenuItem { Header = label };
                all.Click += (_, _) => SetAllFormats(asDecimal);
                numbers.Items.Add(all);
            }

            numbers.Items.Add(new Separator());
            foreach (var (which, label) in new[] { (HexNumber.Offset, AppStrings.Viewer_NumbersOffset), (HexNumber.Selection, AppStrings.Viewer_NumbersSelection), (HexNumber.Size, AppStrings.Viewer_NumbersSize) })
            {
                var item = new MenuItem { Header = label };
                var formats = which == HexNumber.Offset ? new[] { SizeFormat.Hex, SizeFormat.Decimal } : [SizeFormat.Hex, SizeFormat.Decimal, SizeFormat.Units];
                foreach (var format in formats)
                {
                    var choice = new MenuItem { Header = FormatLabel(format), ToggleType = MenuItemToggleType.Radio, IsChecked = FormatOf(which) == format };
                    choice.Click += (_, _) => SetFormat(which, format);
                    item.Items.Add(choice);
                }

                numbers.Items.Add(item);
            }

            menu.Items.Add(numbers);
            var colors = new MenuItem { Header = AppStrings.Viewer_ByteColors };
            foreach (var (label, value) in new[] { (AppStrings.Viewer_ByteColors_Two, ByteColors.Two), (AppStrings.Viewer_ByteColors_ByKind, ByteColors.ByKind) })
            {
                var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.Radio, IsChecked = Options.HexByteColors == value };
                item.Click += (_, _) => UpdateViewer(v => v with { HexByteColors = value });
                colors.Items.Add(item);
            }

            colors.Items.Add(new Separator());
            var change = new MenuItem { Header = AppStrings.Viewer_ByteColors_Change, IsEnabled = _request.OpenOptions is not null };
            change.Click += (_, _) => _request.OpenOptions?.Invoke(OptionsWindow.ColorsPage);
            colors.Items.Add(change);
            menu.Items.Add(colors);
        }

        Check(AppStrings.Viewer_FullPathTitle, Options.Title == ViewerTitle.FullPath,
            () => UpdateViewer(v => v with { Title = v.Title == ViewerTitle.FullPath ? ViewerTitle.NameAndFolder : ViewerTitle.FullPath }));
        menu.Items.Add(new Separator());
        Item(AppStrings.Viewer_Options, null, () => _request.OpenOptions?.Invoke("viewers"), _request.OpenOptions is not null);
        return menu;
    }

    private void ShowMoreMenu() => BuildMoreMenu().ShowAt(_more);

    private void BuildFindBar()
    {
        FindPieces = Pieces(_regex, () => _findBox);
        _findBox.InnerRightContent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0), Children = { _matchCase, _wholeWords, _regex, FindPieces } };
        Avalonia.Automation.AutomationProperties.SetName(_findBox, AppStrings.Viewer_Find);
        _findBox.TextChanged += (_, _) =>
        {
            _findDelay.Stop();
            _findDelay.Start();
        };
        _findDelay.Tick += (_, _) =>
        {
            _findDelay.Stop();
            Finding = FindAsync(forward: true, from: Current.HasSelection ? Current.SelectionStart : Current.TopOffset);
        };
        foreach (var flag in new[] { _matchCase, _wholeWords, _regex })
        {
            flag.Click += (_, _) => Finding = FindAsync(forward: true, from: Current.HasSelection ? Current.SelectionStart : Current.TopOffset);
        }

        _findTextMode.IsChecked = true;
        _findTextMode.Click += (_, _) => SetFindHex(false);
        _findHexMode.Click += (_, _) => SetFindHex(true);

        var up = Tool(Label("Icon.ArrowUp", null, "Shift+F3"), AppStrings.Viewer_FindPreviousTip);
        up.Click += (_, _) => Finding = FindAsync(forward: false);
        var down = Tool(Label("Icon.ArrowDown", null, "F3"), AppStrings.Viewer_FindNextTip);
        down.Click += (_, _) => Finding = FindAsync(forward: true);
        var help = new TextBlock { Text = AppStrings.Viewer_FindHelp, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis }
            .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
        var close = Tool(Glyph("Icon.Cross"), AppStrings.Viewer_FindCloseTip);
        close.Click += (_, _) => CloseFind();

        var left = Row(_findBox, _findKinds = Segment(_findTextMode, _findHexMode), _hits, up, down);
        left.Spacing = 8;
        var dock = new DockPanel { Margin = new Thickness(8, 6) };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(left, Dock.Left);
        dock.Children.Add(close);
        dock.Children.Add(left);
        dock.Children.Add(help);
        help.Margin = new Thickness(12, 0);
        _findBar.Child = dock;
        _findBar.IsVisible = false;
        _findBar.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
    }

    private void PlaceFindBar()
    {
        var bottom = Options.FindBar == FindBarPosition.Bottom;
        DockPanel.SetDock(_findBar, bottom ? Dock.Bottom : Dock.Top);
        _findBar.BorderThickness = bottom ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 1);
    }

    private void SetFindHex(bool hex, bool search = true)
    {
        _findTextMode.IsChecked = !hex;
        _findHexMode.IsChecked = hex;
        foreach (var flag in new[] { _matchCase, _wholeWords, _regex })
        {
            flag.IsEnabled = !hex;
        }

        if (search)
        {
            Finding = FindAsync(forward: true, from: Current.HasSelection ? Current.SelectionStart : Current.TopOffset);
        }
    }

    private FindQuery? Query => string.IsNullOrEmpty(_findBox.Text) ? null
        : new FindQuery(_findBox.Text, _findHexMode.IsChecked == true, _matchCase.IsChecked == true, _wholeWords.IsChecked == true, _regex.IsChecked == true);

    internal void OpenFind()
    {
        if (!_findBar.IsVisible && !_pageMode && !_treeMode && _document is { } d && Current.HasSelection && !_hexMode && Current.SelectionEnd - Current.SelectionStart <= 200)
        {
            var selected = d.Text(Current.SelectionStart, Current.SelectionEnd);
            if (!selected.Contains('\n', StringComparison.Ordinal) && !selected.Contains('\r', StringComparison.Ordinal))
            {
                _findBox.Text = selected;
            }
        }

        _findBar.IsVisible = true;
        UpdateToolbar();
        UpdateHighlights();
        if (_pageMode)
        {
            NativeFocus.TakeBack(this);
        }

        _findBox.Focus();
        _findBox.SelectAll();
    }

    internal void CloseFind()
    {
        _findBar.IsVisible = false;
        CancelFind();
        UpdateToolbar();
        UpdateHighlights();
        if (_pageMode && _page is { } page)
        {
            _ = page.RunAsync(PageScript.Call("clear"));
            _pageCell.Focus();
            page.FocusPage();
            return;
        }

        if (_treeMode)
        {
            ClearTreeHits();
            _treeView.Focus();
            return;
        }

        Current.Focus();
    }

    private void CancelFind()
    {
        _findDelay.Stop();
        _findCancel?.Cancel();
        _findCancel = null;
    }

    private void RefreshFind()
    {
        _hits.Text = "";
        UpdateHighlights();
    }

    private void UpdateHighlights()
    {
        var query = _findBar.IsVisible ? Query : null;
        _text.Highlight = query?.ToRegex();
        _hex.HighlightBytes = query is null ? null
            : query.HexBytes ? query.Bytes
            : !query.IsRegex && _document is { } d ? d.Encoding.Encoding.GetBytes(query.Text) : null;
        _text.InvalidateVisual();
        _hex.InvalidateVisual();
    }

    internal Task FindAsync(bool forward, long? from = null)
    {
        Finding = Find(forward, from);
        return Finding;
    }

    private async Task Find(bool forward, long? from)
    {
        if (_pageMode && _findBar.IsVisible)
        {
            await FindInPage(forward, restart: from is not null);
            return;
        }

        if (_treeMode && _findBar.IsVisible)
        {
            await FindInTree(forward, restart: from is not null);
            return;
        }

        if (!_findBar.IsVisible || Query is not { } query)
        {
            if (!_findBar.IsVisible)
            {
                OpenFind();
            }

            UpdateHighlights();
            _hits.Text = "";
            return;
        }

        UpdateHighlights();
        if (_document is not { } d)
        {
            return;
        }

        if (!query.IsValid)
        {
            _hits.Text = AppStrings.Viewer_BadQuery;
            _hits.Res(TextBlock.ForegroundProperty, "Brush.Danger");
            return;
        }

        CancelFind();
        var cancel = new CancellationTokenSource();
        _findCancel = cancel;
        var token = cancel.Token;
        var lines = d.Lines;
        var view = Current;
        var start = from ?? (forward ? (view.HasSelection ? view.SelectionStart + 1 : view.Caret) : view.SelectionStart);
        _hits.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _hits.Text = AppStrings.Viewer_Searching;

        FindHit? hit;
        try
        {
            hit = await Task.Run(() => forward ? ViewerFind.Next(lines, query, start, token) : ViewerFind.Previous(lines, query, start, token), token);
            hit ??= await Task.Run(() => forward ? ViewerFind.Next(lines, query, lines.Start, token) : ViewerFind.Previous(lines, query, lines.Length + 1, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || !ReferenceEquals(_document, d))
        {
            return;
        }

        if (hit is not { } h)
        {
            _hits.Text = AppStrings.Viewer_NoHits;
            _hits.Res(TextBlock.ForegroundProperty, "Brush.Danger");
            return;
        }

        Current.Select(h.Start, h.End);
        Current.SelectionIsMatch = true;
        Current.Reveal(h.Start);
        _hits.Text = AppStrings.Viewer_CountingHits;
        try
        {
            var (total, before, capped) = await Task.Run(() => ViewerFind.Count(lines, query, h.Start, token: token), token);
            if (!token.IsCancellationRequested)
            {
                _hits.Text = Loc.Format(capped ? AppStrings.Viewer_HitsMany : AppStrings.Viewer_Hits, (before + 1).ToString("N0", CultureInfo.CurrentCulture), total.ToString("N0", CultureInfo.CurrentCulture));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool _findTextGiven;

    private async Task LoadThenFind(string path, ViewerKind kind)
    {
        await Load(path, kind, keepPlace: false);
        if (_request.FindText is not { } query || _imageMode || _mediaMode || !string.Equals(path, _path, StringComparison.Ordinal))
        {
            return;
        }

        if (_pageMode)
        {
            await Task.WhenAny(PageShown, Task.Delay(TimeSpan.FromSeconds(10)));
        }

        var given = _request.KeepFindText && _findTextGiven && _findBar.IsVisible;
        if (!given)
        {
            _findBox.Text = query.Text;
            _findDelay.Stop();
            _matchCase.IsChecked = query.MatchCase;
            _wholeWords.IsChecked = query.WholeWords;
            _regex.IsChecked = query.IsRegex;
            SetFindHex(query.HexBytes, search: false);
            _findBar.IsVisible = true;
            UpdateToolbar();
            UpdateHighlights();
            _findTextGiven = true;
        }

        if (_request.GoToFirstMatch)
        {
            await FindAsync(forward: true, from: _document?.Lines.Start ?? 0);
        }

        if (!_request.KeepFindText)
        {
            _findBar.IsVisible = false;
            _findBox.Text = "";
            _findDelay.Stop();
            UpdateToolbar();
            UpdateHighlights();
        }

        if (!_pageMode && !_treeMode)
        {
            Current.Focus();
        }
    }

    internal (ToggleButton Hex, ToggleButton Dec)? GoToBase { get; private set; }

    internal TextBox? GoToBox => _goToBox;

    internal void OpenGoTo(bool extend = false)
    {
        _goToBox = new TextBox { Width = 260 };
        _goToNote = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        var box = _goToBox;
        var content = new StackPanel { Spacing = 6 };
        bool? hexDigits = null;
        if (_hexMode)
        {
            var hex = Mode(AppStrings.Viewer_Hex, null);
            var dec = Mode(AppStrings.Viewer_Dec, null);
            var prefix = new TextBlock { Text = "0x", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }
                .Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
            box.Width = 200;
            box.InnerLeftContent = prefix;
            ToolTip.SetTip(hex, AppStrings.Viewer_GoToBaseTip);
            ToolTip.SetTip(dec, AppStrings.Viewer_GoToBaseTip);
            void SetBase(bool toHex, bool convert)
            {
                if (convert && hexDigits != toHex && !string.IsNullOrWhiteSpace(box.Text))
                {
                    box.Text = GoToTarget.Rebase(box.Text, toHex);
                    box.CaretIndex = box.Text.Length;
                }

                hexDigits = toHex;
                hex.IsChecked = toHex;
                dec.IsChecked = !toHex;
                prefix.IsVisible = toHex;
                box.PlaceholderText = toHex ? AppStrings.Viewer_GoToHexPrompt : AppStrings.Viewer_GoToDecPrompt;
                box.Focus();
            }

            hex.Click += (_, _) => SetBase(true, convert: true);
            dec.Click += (_, _) => SetBase(false, convert: true);
            SetBase(FormatOf(HexNumber.Offset) != SizeFormat.Decimal, convert: false);
            GoToBase = (hex, dec);
            content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Segment(hex, dec), box } });
        }
        else
        {
            box.PlaceholderText = AppStrings.Viewer_GoToPrompt;
            GoToBase = null;
            content.Children.Add(box);
        }

        content.Children.Add(_goToNote);
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight, Content = content };
        box.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (await GoToAsync(box.Text ?? "", extend, hexDigits))
                {
                    flyout.Hide();
                    Current.Focus();
                }
            }
        };
        flyout.Opened += (_, _) => box.Focus();
        flyout.ShowAt(_goTo);
    }

    internal async Task<bool> GoToAsync(string text, bool extend = false, bool? hexDigits = null)
    {
        if (_document is not { } d)
        {
            return false;
        }

        if (GoToTarget.Parse(text, _hexMode, _hexMode ? hexDigits : null) is not { } target)
        {
            Note(hexDigits switch
            {
                true when _hexMode => AppStrings.Viewer_GoToBadHex,
                false when _hexMode => AppStrings.Viewer_GoToBadDec,
                _ => AppStrings.Viewer_GoToBad,
            });
            return false;
        }

        long offset;
        if (target.IsOffset)
        {
            offset = Math.Clamp(target.Value, 0, d.Length);
        }
        else
        {
            var line = target.Value - 1;
            long? start;
            while ((start = d.Index.StartOf(line)) is null)
            {
                if (d.Index.IsComplete)
                {
                    line = Math.Max(0, d.Index.Count - 1);
                    continue;
                }

                Note(AppStrings.Viewer_GoToWaiting);
                await Task.Delay(100);
                if (!ReferenceEquals(_document, d))
                {
                    return false;
                }
            }

            offset = start.Value;
        }

        Current.Select(extend ? (Current.HasSelection ? Current.Anchor : Current.Caret) : offset, offset);
        Current.Reveal(offset);
        return true;

        void Note(string note)
        {
            if (_goToNote is not null)
            {
                _goToNote.Text = note;
            }
        }
    }

    internal Task CopyAsync() => CopyAsync(_hexMode ? CopyFormat.HexBytes : CopyFormat.Text);

    internal Task CopyAsync(CopyFormat format)
    {
        var view = Current;
        if (_document is not { } d || !view.HasSelection || _request.Clipboard is not { } clipboard)
        {
            return Task.CompletedTask;
        }

        var length = view.SelectionEnd - view.SelectionStart;
        if (length > CopyLimit)
        {
            _hits.Text = "";
            ShowStatusNote(AppStrings.Viewer_TooBigToCopy);
            return Task.CompletedTask;
        }

        var text = format == CopyFormat.Text ? d.Text(view.SelectionStart, view.SelectionEnd) : SelectionCopy.Format(d.Read(view.SelectionStart, view.SelectionEnd), format);

        clipboard.SetText(text);
        return Task.CompletedTask;
    }

    private async Task SaveSelectionAsync()
    {
        var view = Current;
        if (_document is not { } d || !view.HasSelection)
        {
            return;
        }

        var (start, end) = (view.SelectionStart, view.SelectionEnd);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = AppStrings.Viewer_SaveSelection,
            SuggestedFileName = WindowsPath.GetName(_path),
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } target)
        {
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                using var output = new FileStream(WindowsPath.ToExtended(target), FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[1024 * 1024];
                for (var at = start; at < end;)
                {
                    var n = d.Bytes.Source.Read(at, buffer.AsSpan(0, (int)Math.Min(buffer.Length, end - at)));
                    if (n == 0)
                    {
                        break;
                    }

                    output.Write(buffer, 0, n);
                    at += n;
                }
            });
            ShowStatusNote(Loc.Format(AppStrings.Viewer_Saved, WindowsPath.GetName(target)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatusNote(Loc.Format(AppStrings.Viewer_SaveFailed, e.Message));
        }
    }

    private void ShowStatusNote(string text)
    {
        UpdateStatus();
        var note = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) }.Res(TextBlock.ForegroundProperty, "Brush.Accent");
        _statusLeft.Children.Insert(0, new Border { Child = note });
    }
}
