// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.FileSystem;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal enum HexNumber { Offset, Selection, Size }

internal sealed record ViewerRequest(string Path, ViewerKind Kind)
{
    public IReadOnlyList<string> Siblings { get; init; } = [];

    public Action<string>? ShowInPanel { get; init; }

    public Action<string>? OpenOptions { get; init; }

    public Func<Uri, bool>? OpenWeb { get; init; }

    public Func<string, Task>? OpenFile { get; init; }

    public Func<string, int, Task<Salandual.Platform.Windows.Shell.IconPixels?>>? Thumbnail { get; init; }

    public IClipboardAccess? Clipboard { get; init; }

    public Func<IPageView>? PageView { get; init; }

    public Action<string>? OpenViewer { get; init; }

    public Salandual.Core.Viewer.FindQuery? FindText { get; init; }

    public bool GoToFirstMatch { get; init; }

    public bool KeepFindText { get; init; }
}

internal sealed partial class ViewerWindow : Window
{
    private readonly SettingsService _settings;
    private readonly ViewerRequest _request;
    private readonly TextContentView _text = new();
    private readonly HexContentView _hex = new();
    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 1, AllowAutoHide = false };
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, Minimum = 0, AllowAutoHide = false, IsVisible = false };
    private readonly ContentControl _content = new();
    private readonly TextBlock _message = new() { Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly StackPanel _statusLeft = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _statusRight = new() { Orientation = Orientation.Horizontal };
    private readonly Run _titleName = new();
    private readonly Run _titleFolder = new();
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Control _toolbar;
    private readonly Border _status;
    private ViewerDocument? _document;
    private string _path;
    private bool _hexMode;
    private bool _syncingScroll;
    private int _siblingIndex = -1;

    public ViewerWindow(SettingsService settings, ViewerRequest request)
    {
        _settings = settings;
        _request = request;
        _path = request.Path;
        var remembered = settings.Current.Viewers;
        Width = remembered.WindowWidth;
        Height = remembered.WindowHeight;

        MinWidth = 520;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.Manual;
        this.Res(BackgroundProperty, "Brush.WindowBackground");
        Styles.Add(ViewerStyles.Create());

        var titleText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        titleText.Inlines!.Add(_titleName);
        titleText.Inlines.Add(_titleFolder);
        titleText.Res(TextBlock.ForegroundProperty, "Brush.Text");
        _titleFolder.Bind(Run.ForegroundProperty, titleText.GetResourceObservable("Brush.SecondaryText"));
        var titleIcon = Salandual.App.Theme.AppIcons.Glyph("Icon.File", 16, 16, 1.2);
        Grid.SetColumn(titleText, 1);
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { titleIcon, titleText } };

        BuildInfo();
        var toolbar = _toolbar = BuildToolbar();
        BuildFindBar();
        var limitBar = BuildLimitBar();
        BuildImagePane();
        _imageHost.Children.Add(_imagePane);
        var status = _status = new Border
        {
            Height = 26,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new DockPanel { Children = { _statusRight, _statusLeft } },
        }.Res(Border.BackgroundProperty, "Brush.InformationLine").Res(Border.BorderBrushProperty, "Brush.Border");
        DockPanel.SetDock(_statusRight, Dock.Right);

        var area = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("*,Auto") };
        BuildTreeHost();
        area.Children.Add(_content);
        area.Children.Add(BuildPageHost());
        area.Children.Add(_message);
        Grid.SetColumn(_vertical, 1);
        area.Children.Add(_vertical);
        Grid.SetRow(_horizontal, 1);
        area.Children.Add(_horizontal);
        _message.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");

        var dock = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(limitBar, Dock.Top);
        dock.Children.Add(toolbar);
        dock.Children.Add(status);
        dock.Children.Add(limitBar);
        dock.Children.Add(_findBar);
        dock.Children.Add(area);
        Content = TitleBarParts.ForWindow(this, title, dock);
        PlaceFindBar();

        _hex.OffsetHeaderClicked += () => UpdateViewer(v => v with { DecimalOffsets = !v.DecimalOffsets });
        foreach (var view in new ContentView[] { _text, _hex })
        {
            view.ViewChanged += OnViewChanged;
        }

        _vertical.ValueChanged += (_, e) =>
        {
            if (!_syncingScroll)
            {
                Current.ScrollFraction = e.NewValue;
            }
        };
        _horizontal.ValueChanged += (_, e) =>
        {
            if (!_syncingScroll)
            {
                _text.HorizontalOffset = e.NewValue;
            }
        };

        InitContextMenu();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _settings.Changed += OnSettingsChanged;
        var normalSize = new Size(Width, Height);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ClientSizeProperty && WindowState == WindowState.Normal)
            {
                normalSize = ClientSize;
            }
        };
        Closing += (_, _) =>
        {
            CloseInfoWindow();
            var maximized = WindowState is WindowState.Maximized or WindowState.FullScreen;
            UpdateViewer(v => v with { WindowMaximized = maximized, WindowWidth = normalSize.Width, WindowHeight = normalSize.Height });
        };
        Closed += (_, _) =>
        {
            _settings.Changed -= OnSettingsChanged;
            _statusTimer.Stop();
            CancelFind();
            _closing.Cancel();
            _imageView.Clear();
            _cover?.Dispose();
            _page?.Dispose();
            _text.StopColors();
            _document?.Dispose();
        };
        ActualThemeVariantChanged += (_, _) => RefreshPage();

        ApplySettings();
        _siblingIndex = IndexOfPath(request.Siblings, request.Path);
        Opened += (_, _) => _ = LoadAsync(request.Path, request.Kind);
    }

    internal ContentView Current => _hexMode ? _hex : _text;

    internal TextContentView TextView => _text;

    internal HexContentView HexView => _hex;

    internal ViewerDocument? Document => _document;

    internal bool IsHex => _hexMode;

    internal string FilePath => _path;

    internal Task Loading { get; private set; } = Task.CompletedTask;

    private ViewerSettings Options => _settings.Current.Viewers;

    internal Task LoadAsync(string path, ViewerKind kind, bool keepPlace = false)
    {
        Loading = keepPlace || _request.FindText is null ? Load(path, kind, keepPlace) : LoadThenFind(path, kind);
        return Loading;
    }

    private async Task Load(string path, ViewerKind kind, bool keepPlace)
    {
        var place = keepPlace ? Current.TopOffset : 0;
        var encoding = keepPlace ? _document?.Encoding.Id : Options.Encoding;
        CancelFind();
        CancelImage();
        var image = kind == ViewerKind.Image || (keepPlace && _isImage);
        var imageBytes = image && keepPlace && !_imageMode && _document is not null;
        var media = !image && (kind == ViewerKind.Media || (keepPlace && _isMedia));
        var mediaBytes = media && keepPlace && !_mediaMode && _document is not null;
        var markdown = !image && !media && _request.PageView is not null && (kind is ViewerKind.Markdown or ViewerKind.Notebook or ViewerKind.Html || (keepPlace && _isMarkdown));
        var notebook = markdown && (kind == ViewerKind.Notebook || (keepPlace && _isNotebook));
        var html = markdown && !notebook && (kind == ViewerKind.Html || (keepPlace && _isHtml));
        var treeFormat = !image && !media && !markdown ? TreeFormat(path) : null;
        var tree = treeFormat is not null && (kind == ViewerKind.Tree || (keepPlace && _treeMode));
        if (!string.Equals(path, _path, StringComparison.Ordinal))
        {
            _webImagesHere = false;
            _htmlScriptsHere = _htmlWebHere = false;
            _currentHeading = null;
            _anywayPath = null;
        }

        HideLimitBar();
        if ((image && !imageBytes) || ((markdown || tree) && !keepPlace))
        {
            if (await OverLimit(path, image ? ViewerKind.Image : notebook ? ViewerKind.Notebook : html ? ViewerKind.Html : tree ? ViewerKind.Tree : ViewerKind.Markdown) is { } instead)
            {
                image = markdown = notebook = html = tree = false;
                kind = instead;
            }
        }

        _path = path;
        _isImage = image;
        _isMedia = media;
        _isMarkdown = markdown;
        _isNotebook = notebook;
        _isHtml = html;
        _isTree = treeFormat is not null;
        if (!string.Equals(path, _treeRead?.Path, StringComparison.Ordinal))
        {
            _treeRead = null;
            TreeProblem = null;
        }

        if (!tree)
        {
            _treeMode = false;
        }
        if (!markdown && _pageMode)
        {
            LeavePage();
        }

        UpdateTitle();
        if (image && !imageBytes)
        {
            await LoadImage(path, keepTurn: keepPlace);
            return;
        }

        if (!image)
        {
            ExitFullScreen();
            _imageView.Clear();
        }

        if (media && !mediaBytes)
        {
            await LoadMedia(path);
            return;
        }

        if (!await OpenDocument(path, encoding))
        {
            return;
        }

        if (markdown && (!keepPlace || _pageMode))
        {
            _statusTimer.Start();
            ShowPageMode();
            RenderPage(keepScroll: keepPlace);
            await PageRendering;
            return;
        }

        if (tree)
        {
            _statusTimer.Start();
            await LoadTree(path, keepPlace);
            return;
        }

        var hex = keepPlace ? _hexMode : image || media || kind switch
        {
            ViewerKind.Hex => true,
            ViewerKind.Text or ViewerKind.Markdown or ViewerKind.Notebook or ViewerKind.Tree or ViewerKind.Html => false,
            _ => Options.OpenAs switch { ViewerOpenAs.Hex => true, ViewerOpenAs.Text => false, _ => _document!.Guess.IsBinary },
        };
        SetMode(hex, place);
        _statusTimer.Start();
        UpdateToolbar();
        UpdateStatus();
        RefreshFind();
    }

    private async Task<bool> OpenDocument(string path, string? encoding)
    {
        ViewerDocument document;
        try
        {
            document = await Task.Run(() => new ViewerDocument(path, new FileByteSource(WindowsPath.ToExtended(path)), encoding));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ShowMessage(Loc.Format(AppStrings.Viewer_CantOpen, WindowsPath.GetName(path), e.Message));
            return false;
        }

        if (!string.Equals(path, _path, StringComparison.Ordinal))
        {
            document.Dispose();
            return false;
        }

        var old = _document;
        _document = document;
        document.IndexProgress += () => Dispatcher.UIThread.Post(OnIndexProgress);
        _message.IsVisible = false;
        _content.IsVisible = true;
        _text.SetColors(ColorsFor(path), ExactColorsBytes, now: false);
        _text.Document = document;
        _hex.Document = document;
        _hex.Characters = document.Encoding;
        old?.Dispose();
        return true;
    }

    private SyntaxLanguage ColorsFor(string path) => Options.TextColors ? SyntaxRules.LanguageFor(WindowsPath.GetName(path)) : SyntaxLanguage.None;

    private long ExactColorsBytes => (long)Options.ExactColorsMB * 1024 * 1024;

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _message.IsVisible = true;
        _content.IsVisible = false;
        _statusTimer.Stop();
        _statusLeft.Children.Clear();
        _statusRight.Children.Clear();
    }

    internal void SetMode(bool hex, long? at = null)
    {
        var top = at ?? Current.TopOffset;
        var (anchor, caret, match) = (Current.Anchor, Current.Caret, Current.SelectionIsMatch);
        _hexMode = hex;
        _imageMode = false;
        _mediaMode = false;
        _treeMode = false;
        LeavePage();
        _vertical.IsVisible = true;
        _content.Content = Current;
        Current.Select(anchor, caret);
        Current.SelectionIsMatch = match;
        Current.ShowAt(top);
        _horizontal.IsVisible = !hex && !_text.Wrap;
        UpdateToolbar();
        UpdateHighlights();
        Dispatcher.UIThread.Post(() => Current.Focus(), DispatcherPriority.Input);
    }

    private void OnViewChanged()
    {
        if (_document is null)
        {
            return;
        }

        _syncingScroll = true;
        try
        {
            var view = Current;
            _vertical.ViewportSize = view.ViewportFraction;
            _vertical.LargeChange = view.ViewportFraction;
            _vertical.SmallChange = view.ViewportFraction / 20;
            _vertical.Value = view.ScrollFraction;
            if (!_hexMode && !_text.Wrap)
            {
                _horizontal.Maximum = _text.HorizontalExtent;
                _horizontal.ViewportSize = _text.TextAreaWidth;
                _horizontal.LargeChange = _text.TextAreaWidth;
                _horizontal.SmallChange = 40;
                _horizontal.Value = _text.HorizontalOffset;
            }
        }
        finally
        {
            _syncingScroll = false;
        }

        UpdateStatus();
    }

    private void OnIndexProgress()
    {
        _text.InvalidateVisual();
        UpdateStatus();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!_closing.IsCancellationRequested)
        {
            ApplySettings();
        }
    });

    private void ApplySettings()
    {
        var o = Options;
        if (_document is not null)
        {
            _text.SetColors(ColorsFor(_path), ExactColorsBytes);
        }

        _treeView.Colors = o.TextColors;
        _treeView.InvalidateVisual();
        if (_text.Wrap != o.Wrap)
        {
            _text.Wrap = o.Wrap;
        }

        if (_text.LineNumbers != o.LineNumbers)
        {
            _text.LineNumbers = o.LineNumbers;
        }

        if (_text.TabWidth != o.TabWidth)
        {
            _text.TabWidth = o.TabWidth;
        }

        if (_hex.BytesPerRowSetting != o.HexBytesPerRow)
        {
            _hex.BytesPerRowSetting = o.HexBytesPerRow;
        }

        if (_hex.Group != o.HexGroup)
        {
            _hex.Group = o.HexGroup;
        }

        if (_hex.DecimalOffsets != o.DecimalOffsets)
        {
            _hex.DecimalOffsets = o.DecimalOffsets;
        }

        if (_hex.ByteColors != o.HexByteColors)
        {
            _hex.ByteColors = o.HexByteColors;
        }

        _text.Reset();
        _horizontal.IsVisible = !_hexMode && !_imageMode && !_pageMode && !o.Wrap;
        ApplyCanvas();
        RefreshPage();
        PlaceFindBar();
        UpdateVideoFrame();
        UpdateTitle();
        UpdateToolbar();
        UpdateStatus();
    }

    private void UpdateViewer(Func<ViewerSettings, ViewerSettings> change) => _settings.Update(s => s with { Viewers = change(s.Viewers) });

    private void UpdateTitle()
    {
        var name = WindowsPath.GetName(_path);
        var folder = WindowsPath.GetParent(_path) ?? "";
        if (Options.Title == ViewerTitle.FullPath)
        {
            Title = _path;
            _titleName.Text = _path;
            _titleFolder.Text = "";
        }
        else
        {
            Title = Loc.Format(AppStrings.Viewer_Title, name, folder);
            _titleName.Text = name;
            _titleFolder.Text = "  — " + folder;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        var ctrl = mods == KeyModifiers.Control;
        var plain = mods == KeyModifiers.None;
        var shift = mods == KeyModifiers.Shift;
        var inText = e.Source is TextBox;
        if (e.Source is TextBox { Tag: ZoomBox })
        {
            return;
        }

        if ((_imageMode && !inText && ImageKey(e.Key, mods)) || (_mediaMode && !inText && MediaKey(e.Key, mods)) || (_pageMode && !inText && PageKey(e.Key, mods)) || (_treeMode && !inText && TreeKey(e.Key, mods)))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape when plain:
                if (_findBar.IsVisible)
                {
                    CloseFind();
                }
                else if (Options.Close == ViewerClose.EscOrAltF4)
                {
                    Close();
                }

                break;
            case Key.F4 when mods == KeyModifiers.Alt:
                Close();
                break;
            case Key.F when ctrl:
            case Key.F7 when plain:
                OpenFind();
                break;
            case Key.F3 when plain || shift:
                _ = FindAsync(forward: plain);
                break;
            case Key.Enter when inText && ReferenceEquals(e.Source, _findBox) && (plain || shift):
                _ = FindAsync(forward: plain);
                break;
            case Key.G when ctrl:
                OpenGoTo();
                break;
            case Key.C when ctrl && !inText:
                _ = CopyAsync();
                break;
            case Key.S when ctrl:
                _ = SaveSelectionAsync();
                break;
            case Key.R when ctrl:
                Reload();
                break;
            case Key.A when ctrl && !inText:
                Current.SelectAll();
                break;
            case Key.F4 when plain && !inText:
            case Key.H when ctrl && !inText:
                _ = ShowHexAsync();
                break;
            case Key.F5 when plain && !inText:
            case Key.T when ctrl && !inText:
                if (_isImage)
                {
                    ShowImage();
                }
                else if (_isMedia)
                {
                    ShowMedia();
                }
                else
                {
                    SetMode(false);
                }

                break;
            case Key.F6 when plain && !inText && _isMarkdown:
                ShowPage();
                break;
            case Key.F6 when plain && !inText && _isTree:
                ShowTree();
                break;
            case Key.Left when mods == KeyModifiers.Alt && _isMarkdown:
                GoBack();
                break;
            case Key.F2 when plain:
                UpdateViewer(v => v with { Wrap = !v.Wrap });
                break;
            case Key.F8 when plain || shift:
                StepEncoding(plain ? 1 : -1);
                break;
            case Key.Space when plain && !inText:
                _ = StepFileAsync(1);
                break;
            case Key.Back when plain && !inText:
                _ = StepFileAsync(-1);
                break;
            default:
                if (inText || !Navigate(e.Key, mods))
                {
                    return;
                }

                break;
        }

        e.Handled = true;
    }

    private bool Navigate(Key key, KeyModifiers mods)
    {
        var view = Current;
        var ctrl = (mods & KeyModifiers.Control) != 0;
        var extend = (mods & KeyModifiers.Shift) != 0;
        var page = Math.Max(1, (int)(view.Bounds.Height / (_hexMode ? _hex.RowHeight : _text.RowHeight)) - 1);
        if (_hexMode)
        {
            var per = _hex.BytesPerRow;
            switch (key)
            {
                case Key.Left: _hex.MoveCaret(-1, extend); return true;
                case Key.Right: _hex.MoveCaret(1, extend); return true;
                case Key.Up: _hex.MoveCaret(-per, extend); return true;
                case Key.Down: _hex.MoveCaret(per, extend); return true;
                case Key.PageUp: _hex.MoveCaret(-(long)per * page, extend); return true;
                case Key.PageDown: _hex.MoveCaret((long)per * page, extend); return true;
                case Key.Home when ctrl: _hex.MoveCaret(long.MinValue / 2, extend); return true;
                case Key.End when ctrl: _hex.MoveCaret(long.MaxValue / 2, extend); return true;
                case Key.Home: _hex.MoveCaret(-(_hex.Caret % per), extend); return true;
                case Key.End: _hex.MoveCaret(per - 1 - (_hex.Caret % per), extend); return true;
                default: return false;
            }
        }

        switch (key)
        {
            case Key.Up: _text.ScrollRows(-1); return true;
            case Key.Down: _text.ScrollRows(1); return true;
            case Key.PageUp: _text.ScrollRows(-page); return true;
            case Key.PageDown: _text.ScrollRows(page); return true;
            case Key.Home when ctrl: _text.ScrollToTop(); return true;
            case Key.End when ctrl: _text.ScrollToEnd(); return true;
            case Key.Home: _text.HorizontalOffset = 0; return true;
            case Key.Left: _text.ScrollColumns(-4); return true;
            case Key.Right: _text.ScrollColumns(4); return true;
            default: return false;
        }
    }

    private static int IndexOfPath(IReadOnlyList<string> paths, string path)
    {
        for (var i = 0; i < paths.Count; i++)
        {
            if (string.Equals(paths[i], path, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    internal async Task StepFileAsync(int direction)
    {
        var list = _request.Siblings;
        var next = _siblingIndex + direction;
        if (_siblingIndex < 0 || next < 0 || next >= list.Count)
        {
            return;
        }

        _siblingIndex = next;
        _lastStep = direction;
        await LoadAsync(list[next], _settings.Current.Viewers.KindFor(WindowsPath.GetName(list[next]), alternate: false));
    }

    private void Reload()
    {
        if (_document is not null || _message.IsVisible || _imageMode || _mediaMode)
        {
            _rendered = null;
            _htmlShown = null;
            _ = LoadAsync(_path, ViewerKind.Automatic, keepPlace: _document is not null || _imageMode || _mediaMode);
        }
    }

    private void StepEncoding(int direction)
    {
        if (_document is { } d)
        {
            SetEncoding(TextEncodings.Step(d.Encoding, direction));
        }
    }

    private void SetEncoding(TextEncoding encoding)
    {
        if (_document is not { } d)
        {
            return;
        }

        var top = Current.TopOffset;
        d.SetEncoding(encoding);
        _hex.Characters = encoding;
        _text.Reset();
        _text.ShowAt(top);
        RefreshFind();
        UpdateToolbar();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_imageMode)
        {
            UpdateImageStatus();
            return;
        }

        if (_mediaMode)
        {
            UpdateMediaStatus();
            return;
        }

        if (_pageMode)
        {
            UpdatePageStatus();
            return;
        }

        if (_treeMode || _treeReading)
        {
            UpdateTreeStatus();
            return;
        }

        if (_document is not { } d)
        {
            return;
        }

        var left = new List<Border>();
        var right = new List<Border>();
        if (d.Index.IsComplete)
        {
            _statusTimer.Stop();
        }

        var format = DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture);
        var view = Current;
        if (_hexMode)
        {
            var caret = view.Caret;
            Status(left, Loc.Format(AppStrings.Viewer_OffsetStatus, Shown(caret, FormatOf(HexNumber.Offset), 8)), click: at => ShowValueMenu(at, caret, HexNumber.Offset));
            if (view.HasSelection)
            {
                var selected = view.SelectionEnd - view.SelectionStart;
                Status(left, Options.SelectionFormat == SizeFormat.Units ? Loc.Format(AppStrings.Viewer_Selected, Shown(selected, SizeFormat.Units)) : Loc.Format(selected == 1 ? AppStrings.Viewer_BytesSelected_One : AppStrings.Viewer_BytesSelected_Other, Shown(selected, Options.SelectionFormat)),
                    click: at => ShowValueMenu(at, selected, HexNumber.Selection));
            }

            Status(right, Loc.Format(AppStrings.Viewer_RowStatus, _hex.BytesPerRow),
                click: at => ShowChoiceMenu(at, ViewerSettings.BytesPerRowChoices, Options.HexBytesPerRow, BytesLabel, n => UpdateViewer(v => v with { HexBytesPerRow = n })));
            Status(right, GroupLabel(_hex.Group),
                click: at => ShowChoiceMenu(at, ViewerSettings.GroupChoices, Options.HexGroup, GroupLabel, n => UpdateViewer(v => v with { HexGroup = n })));
            Status(right, Loc.Format(AppStrings.Viewer_HexCharacters, _hex.CharactersName), click: at => ShowEncodingMenu(at, characters: true));
            Status(right, Options.SizeFormat == SizeFormat.Units ? Shown(d.Length, SizeFormat.Units) : Loc.Format(d.Length == 1 ? AppStrings.Viewer_Bytes_One : AppStrings.Viewer_Bytes_Other, Shown(d.Length, Options.SizeFormat)),
                click: at => ShowValueMenu(at, d.Length, HexNumber.Size));
            ShowStatus(left, right);
            return;
        }

        var line = d.Index.NumberOf(_text.CaretLineStart);
        Status(left, Loc.Format(AppStrings.Viewer_LnCol, line is { } n ? (n + 1).ToString("N0", CultureInfo.CurrentCulture) : "…", _text.CaretColumn));
        if (view.HasSelection)
        {
            var bytes = view.SelectionEnd - view.SelectionStart;
            var count = bytes <= 1024 * 1024 ? d.Text(view.SelectionStart, view.SelectionEnd).Length : bytes;
            Status(left, Loc.Format(AppStrings.Viewer_Selected, count.ToString("N0", CultureInfo.CurrentCulture)));
        }

        Status(left, d.Index.IsComplete
            ? Loc.Plural(d.Index.Count, AppStrings.Viewer_Lines_One, AppStrings.Viewer_Lines_Other)
            : Loc.Format(AppStrings.Viewer_Counting, (int)(d.Index.Fraction * 100)), busy: !d.Index.IsComplete);

        Status(right, d.IsDetected ? Loc.Format(AppStrings.Viewer_Detected, d.Encoding.Name) : d.Encoding.Name, strong: true, tip: AppStrings.Viewer_EncodingTip);
        if (d.LineEnding is { } ending)
        {
            Status(right, ending);
        }

        Status(right, Loc.Format(AppStrings.Viewer_Tab, _text.TabWidth));
        Status(right, format.Size(d.Length));
        ShowStatus(left, right);
    }

    private void ShowStatus(List<Border> left, List<Border> right)
    {
        static string Texts(IEnumerable<Control> cells) => string.Join("0001", cells.OfType<Border>().Select(c => (c.Child as TextBlock)?.Text));
        if (Texts(_statusLeft.Children) == Texts(left) && Texts(_statusRight.Children) == Texts(right))
        {
            return;
        }

        _statusLeft.Children.Clear();
        _statusLeft.Children.AddRange(left);
        _statusRight.Children.Clear();
        _statusRight.Children.AddRange(right);
    }

    internal SizeFormat FormatOf(HexNumber which) => which switch
    {
        HexNumber.Offset => Options.DecimalOffsets ? SizeFormat.Decimal : SizeFormat.Hex,
        HexNumber.Selection => Options.SelectionFormat,
        _ => Options.SizeFormat,
    };

    internal void SetFormat(HexNumber which, SizeFormat format) => UpdateViewer(v => which switch
    {
        HexNumber.Offset => v with { DecimalOffsets = format == SizeFormat.Decimal },
        HexNumber.Selection => v with { SelectionFormat = format },
        _ => v with { SizeFormat = format },
    });

    internal void SetAllFormats(bool asDecimal)
    {
        var format = asDecimal ? SizeFormat.Decimal : SizeFormat.Hex;
        UpdateViewer(v => v with { DecimalOffsets = asDecimal, SelectionFormat = format, SizeFormat = format });
    }

    private string Shown(long value, SizeFormat format, int pad = 0) => format switch
    {
        SizeFormat.Decimal => value.ToString("N0", CultureInfo.CurrentCulture),
        SizeFormat.Units => DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture).Size(value),
        _ => "0x" + value.ToString("X" + pad, CultureInfo.InvariantCulture),
    };

    private string Copied(long value, SizeFormat format) => format switch
    {
        SizeFormat.Decimal => value.ToString(CultureInfo.InvariantCulture),
        SizeFormat.Units => Shown(value, format),
        _ => "0x" + value.ToString("X", CultureInfo.InvariantCulture),
    };

    private void ShowValueMenu(Control at, long value, HexNumber which)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedLeft };
        var current = FormatOf(which);
        var formats = which == HexNumber.Offset ? new[] { SizeFormat.Hex, SizeFormat.Decimal } : [SizeFormat.Hex, SizeFormat.Decimal, SizeFormat.Units];
        foreach (var format in formats)
        {
            var item = new MenuItem { Header = Loc.Format(AppStrings.Viewer_ValueAs, FormatLabel(format), Shown(value, format)), ToggleType = MenuItemToggleType.Radio, IsChecked = format == current };
            item.Click += (_, _) => SetFormat(which, format);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var copy = new MenuItem { Header = AppStrings.Viewer_CopyValue, IsEnabled = _request.Clipboard is not null };
        copy.Click += (_, _) => _request.Clipboard?.SetText(Copied(value, current));
        menu.Items.Add(copy);
        menu.ShowAt(at);
        LastStatusMenu = menu;
    }

    internal static string FormatLabel(SizeFormat format) => format switch
    {
        SizeFormat.Decimal => AppStrings.Opt_HexOffsets_Decimal,
        SizeFormat.Units => AppStrings.Viewer_FormatUnits,
        _ => AppStrings.Opt_HexOffsets_Hex,
    };

    internal MenuFlyout? LastStatusMenu { get; private set; }

    internal IEnumerable<Border> StatusCells => _statusLeft.Children.Concat(_statusRight.Children).OfType<Border>();

    private static void Status(List<Border> row, string text, bool busy = false, bool strong = false, string? tip = null, Action<Control>? click = null)
    {
        var block = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }
            .Res(TextBlock.ForegroundProperty, busy ? "Brush.InformationLineWarning" : strong ? "Brush.InformationLineText" : "Brush.InformationLineDimText");
        var cell = new Border { Padding = new Thickness(12, 0), BorderThickness = new Thickness(0, 0, 1, 0), Child = block }
            .Res(Border.BorderBrushProperty, "Brush.Border");
        if (click is not null)
        {
            cell.Cursor = new Cursor(StandardCursorType.Hand);
            cell.Background = Brushes.Transparent;
            cell.PointerEntered += (_, _) => cell.Res(Border.BackgroundProperty, "Brush.InformationLineHover");
            cell.PointerExited += (_, _) => cell.Background = Brushes.Transparent;
            cell.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                {
                    click(cell);
                }
            };
            cell.Tag = click;
        }

        if (tip is not null)
        {
            ToolTip.SetTip(cell, tip);
        }

        row.Add(cell);
    }
}
