// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Torrent.cs

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.FileSystem;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal enum TorrentPage { None, Summary, Files }

internal sealed partial class ViewerWindow
{
    private static readonly Regex WebAddress = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly ScrollViewer _torrentHost = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true };
    private readonly Grid _filesHost = new() { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };
    private readonly ListBox _filesList = new() { Focusable = true };
    private readonly TextBox _filesFilter = new() { Width = 260, MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _filesFoot = new() { FontSize = 12, Margin = new Thickness(12, 6), TextWrapping = TextWrapping.Wrap };
    private readonly HashSet<string> _openFolders = new(StringComparer.Ordinal);
    private ToggleButton _torrentModeButton = null!;
    private ToggleButton _filesModeButton = null!;
    private Button _copyMagnet = null!;
    private Button _copyHash = null!;
    private (string Path, TorrentInfo Info, TorrentFolder Top)? _torrent;
    private TorrentPage _torrentPage;
    private bool _isTorrent;
    private bool _torrentReading;
    private List<TorrentRow> _rows = [];

    internal TorrentInfo? Torrent => _torrent is { } t && string.Equals(t.Path, _path, StringComparison.Ordinal) ? t.Info : null;

    internal TorrentPage TorrentPageShown => _torrentPage;

    internal IReadOnlyList<TorrentRow> TorrentRows => _rows;

    internal TextBox TorrentFilter => _filesFilter;

    internal ScrollViewer TorrentSummaryHost => _torrentHost;

    private ToggleButton BuildTorrentModeButton()
    {
        _torrentModeButton = Mode(AppStrings.Torrent_Mode, null);
        _torrentModeButton.Click += (_, _) => ShowTorrentPage(TorrentPage.Summary);
        return _torrentModeButton;
    }

    private ToggleButton BuildFilesModeButton()
    {
        _filesModeButton = Mode(AppStrings.Torrent_FilesMode, null);
        _filesModeButton.Click += (_, _) => ShowTorrentPage(TorrentPage.Files);
        BuildFilesHost();
        return _filesModeButton;
    }

    private Button BuildCopyMagnetButton()
    {
        _copyMagnet = Tool(Label("Icon.Copy", AppStrings.Torrent_CopyMagnet, null), AppStrings.Torrent_CopyMagnetTip);
        _copyMagnet.Click += (_, _) => CopyMagnetLink();
        return _copyMagnet;
    }

    private Button BuildCopyHashButton()
    {
        _copyHash = Tool(Label(null, AppStrings.Torrent_CopyHash, null), AppStrings.Torrent_CopyHashTip);
        _copyHash.Click += (_, _) => CopyInfoHash();
        return _copyHash;
    }

    internal static bool IsTorrentName(string name) => name.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);

    private async Task LoadTorrent(string path, bool keepPlace)
    {
        var wasPage = _torrentPage;
        var wasTree = _treeMode;
        var wasHex = _hexMode;
        var token = _closing.Token;
        _torrentReading = true;
        UpdateStatus();
        TorrentInfo info;
        DataTree tree;
        try
        {
            (info, tree) = await Task.Run(() =>
            {
                var read = TorrentInfo.ReadFile(WindowsPath.ToExtended(path), token);
                return (read, DataTree.FromBencode(read.Root, DateText, token));
            }, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (TorrentFileException e)
        {
            if (string.Equals(path, _path, StringComparison.Ordinal))
            {
                NotATorrent(e.Message);
            }

            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (string.Equals(path, _path, StringComparison.Ordinal))
            {
                NotATorrent(Loc.Format(AppStrings.Viewer_CantOpen, WindowsPath.GetName(path), e.Message));
            }

            return;
        }
        finally
        {
            _torrentReading = false;
        }

        if (!string.Equals(path, _path, StringComparison.Ordinal))
        {
            return;
        }

        var top = TorrentFolder.Build(info);
        if (_torrent is not { } before || !string.Equals(before.Path, path, StringComparison.Ordinal))
        {
            _openFolders.Clear();
            _openFolders.Add("");
            _filesFilter.Text = "";
        }

        _torrent = (path, info, top);
        _treeRead = (path, tree);
        TreeProblem = null;
        _isTree = true;
        _treeView.Show(tree, 2);
        _torrentHost.Content = BuildTorrentSummary(info);
        TorrentLoadedForCheck(path);
        RebuildFileRows();
        if (keepPlace && wasTree)
        {
            ShowTreeMode();
        }
        else if (keepPlace && wasPage == TorrentPage.None)
        {
            SetMode(wasHex);
            UpdateStatus();
        }
        else
        {
            ShowTorrentPage(keepPlace && wasPage == TorrentPage.Files ? TorrentPage.Files : TorrentPage.Summary);
        }
    }

    private void NotATorrent(string why)
    {
        _torrentReading = false;
        _torrent = null;
        _treeRead = null;
        _isTree = false;
        SetMode(true, 0);
        ShowNote(why);
        UpdateToolbar();
        UpdateStatus();
    }

    private string DateText(DateTimeOffset when)
    {
        var format = DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture);
        var local = when.LocalDateTime;
        return format.Date(local) + " " + format.Time(local);
    }

    internal void ShowTorrentPage(TorrentPage page)
    {
        if (!_isTorrent)
        {
            return;
        }

        if (Torrent is null)
        {
            _ = LoadAsync(_path, ViewerKind.Torrent);
            return;
        }

        if (_findBar.IsVisible)
        {
            CloseFind();
        }

        _torrentPage = page;
        _treeMode = false;
        _hexMode = false;
        _imageMode = false;
        _mediaMode = false;
        LeavePage();
        _message.IsVisible = false;
        _content.IsVisible = true;
        _content.Content = page == TorrentPage.Files ? _filesHost : _torrentHost;
        _vertical.IsVisible = false;
        _horizontal.IsVisible = false;
        UpdateToolbar();
        UpdateStatus();
        Dispatcher.UIThread.Post(() => (page == TorrentPage.Files ? _filesList : (Control)_torrentHost).Focus(), DispatcherPriority.Input);
    }

    private void UpdateTorrentTools()
    {
        var read = Torrent is not null;
        _torrentModeButton.IsVisible = _isTorrent && (read || _torrentReading);
        _filesModeButton.IsVisible = _isTorrent && (read || _torrentReading);
        _torrentModeButton.IsChecked = _torrentPage == TorrentPage.Summary;
        _filesModeButton.IsChecked = _torrentPage == TorrentPage.Files;
        _copyMagnet.IsVisible = _copyHash.IsVisible = _isTorrent && read;
        _copyMagnet.IsEnabled = _copyHash.IsEnabled = _request.Clipboard is not null;
        _checkButton.IsVisible = _isTorrent && read && _request.TorrentFolder is not null;
        _checkButton.IsEnabled = true;
        SetModeLabel(_treeModeButton, _isTorrent ? AppStrings.Torrent_Structure : AppStrings.Viewer_Tree);
    }

    internal void CopyMagnetLink() => CopyTorrentText(t => t.MagnetLink(), AppStrings.Torrent_MagnetCopied);

    internal void CopyInfoHash() => CopyTorrentText(t => t.InfoHash, AppStrings.Torrent_HashCopied);

    internal void CopyFileList()
    {
        CopyTorrentText(t =>
        {
            var text = new StringBuilder();
            foreach (var file in ListedFiles(t))
            {
                text.Append(TorrentNames.Shown(file.Path)).Append('\t').Append(file.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }

            return text.ToString();
        }, AppStrings.Torrent_ListCopied);
    }

    private void CopyTorrentText(Func<TorrentInfo, string> text, string done)
    {
        if (_request.Clipboard is { } clipboard && Torrent is { } torrent)
        {
            ShowStatusNote(clipboard.SetText(text(torrent)) ? done : AppStrings.Viewer_ClipboardBusy);
        }
    }

    private Control BuildTorrentSummary(TorrentInfo info)
    {
        var format = DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture);
        var page = new StackPanel { Margin = new Thickness(24, 20), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(new SelectableTextBlock { Text = info.Name.Length > 0 ? TorrentNames.Shown(info.Name) : AppStrings.Torrent_NoName, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Brush.Text"));
        page.Children.Add(new TextBlock { Text = Subtitle(info, format), FontSize = 12, Margin = new Thickness(0, 2, 0, 14), TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText"));

        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 16, RowSpacing = 4 };
        void Row(string name, Control value)
        {
            var at = rows.RowDefinitions.Count;
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = name, FontSize = 13, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
            Grid.SetRow(label, at);
            Grid.SetRow(value, at);
            Grid.SetColumn(value, 1);
            rows.Children.Add(label);
            rows.Children.Add(value);
        }

        void TextRow(string name, string value, bool mono = false) => Row(name, Value(value, mono));

        if (info.PieceCount > 0)
        {
            TextRow(AppStrings.Torrent_Pieces, Loc.Format(AppStrings.Torrent_PiecesValue, info.PieceCount.ToString("N0", CultureInfo.CurrentCulture), format.Size(info.PieceLength)));
        }

        if (info.HashV1 is { } v1)
        {
            TextRow(AppStrings.Torrent_HashV1, Grouped(TorrentInfo.Hex(v1)), mono: true);
        }

        if (info.HashV2 is { } v2)
        {
            TextRow(AppStrings.Torrent_HashV2, Grouped(TorrentInfo.Hex(v2)), mono: true);
        }

        TextRow(AppStrings.Torrent_Private, info.IsPrivate ? AppStrings.Torrent_PrivateYes : AppStrings.Torrent_PrivateNo);
        if (info.CreatedBy is { Length: > 0 } by)
        {
            TextRow(AppStrings.Torrent_MadeBy, TorrentNames.Shown(by));
        }

        if (info.CreatedOn is { } on)
        {
            TextRow(AppStrings.Torrent_MadeOn, DateText(on));
        }

        if (info.Comment is { Length: > 0 } comment)
        {
            Row(AppStrings.Torrent_Comment, WithLinks(TorrentNames.Shown(comment)));
        }

        if (info.Source is { Length: > 0 } source)
        {
            TextRow(AppStrings.Torrent_Source, TorrentNames.Shown(source));
        }

        page.Children.Add(rows);

        var trackers = info.Trackers.Sum(t => t.Count);
        page.Children.Add(Section(trackers == 0 ? AppStrings.Torrent_TrackersNone
            : Loc.Format(AppStrings.Torrent_TrackersHead, trackers, Loc.Plural(info.Trackers.Count, AppStrings.Torrent_Tiers_One, AppStrings.Torrent_Tiers_Other))));
        if (trackers == 0)
        {
            page.Children.Add(Note(AppStrings.Torrent_NoTrackers));
        }
        else
        {
            var list = new StackPanel();
            for (var tier = 0; tier < info.Trackers.Count; tier++)
            {
                foreach (var url in info.Trackers[tier])
                {
                    list.Children.Add(ListRow(Loc.Format(AppStrings.Torrent_Tier, tier + 1), url, Protocol(url)));
                }
            }

            page.Children.Add(Boxed(list));
        }

        if (info.WebSeeds.Count > 0)
        {
            page.Children.Add(Section(Loc.Format(AppStrings.Torrent_WebSeedsHead, info.WebSeeds.Count)));
            var seeds = new StackPanel();
            foreach (var url in info.WebSeeds)
            {
                seeds.Children.Add(ListRow(null, url, Protocol(url)));
            }

            page.Children.Add(Boxed(seeds));
        }

        if (info.StrangeNames is var strange and > 0)
        {
            page.Children.Add(new TextBlock { Text = Loc.Plural(strange, AppStrings.Torrent_Strange_One, AppStrings.Torrent_Strange_Other), FontSize = 12, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Brush.Warning"));
        }

        page.Children.Add(Note(AppStrings.Torrent_Offline));
        return page;

        Control Value(string text, bool mono)
        {
            var block = new SelectableTextBlock { Text = text, FontSize = mono ? 12 : 13, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Brush.Text");
            if (mono)
            {
                block.Res(TextBlock.FontFamilyProperty, "Font.Mono");
            }

            return block;
        }

        Control ListRow(string? tier, string url, string kind)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(10, 3) };
            if (tier is not null)
            {
                grid.Children.Add(new TextBlock { Text = tier, FontSize = 12, MinWidth = 50, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText"));
            }

            var address = WithLinks(TorrentNames.Shown(url), 12);
            Grid.SetColumn(address, 1);
            grid.Children.Add(address);
            var tag = TagChip(kind, warn: false);
            Grid.SetColumn(tag, 2);
            grid.Children.Add(tag);
            return grid;
        }
    }

    private string Subtitle(TorrentInfo info, DisplayFormat format)
    {
        var version = info.Version switch
        {
            TorrentVersion.V2 => AppStrings.Torrent_V2,
            TorrentVersion.Hybrid => AppStrings.Torrent_Hybrid,
            _ => AppStrings.Torrent_V1,
        };
        return Loc.Format(AppStrings.Torrent_Subtitle, format.Size(info.TotalSize), Loc.Plural(info.FileCount, AppStrings.Torrent_Files_One, AppStrings.Torrent_Files_Other), version);
    }

    internal static string Grouped(string hex)
    {
        var text = new StringBuilder(hex.Length + (hex.Length / 4));
        for (var i = 0; i < hex.Length; i += 4)
        {
            text.Append(i == 0 ? "" : " ").Append(hex.AsSpan(i, Math.Min(4, hex.Length - i)));
        }

        return text.ToString();
    }

    private static string Protocol(string url)
    {
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        var scheme = colon > 0 ? url[..colon].ToLowerInvariant() : "";
        return scheme switch
        {
            "udp" => "UDP",
            "http" => AppStrings.Torrent_Http,
            "https" => "HTTPS",
            "ws" => AppStrings.Torrent_WebSocket,
            "wss" => "WebSocket",
            _ => scheme.Length > 0 ? scheme.ToUpperInvariant() : AppStrings.Torrent_UnknownAddress,
        };
    }

    private Control WithLinks(string text, double size = 13)
    {
        var block = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.Text");
        var inlines = block.Inlines ??= [];
        var at = 0;
        foreach (Match m in WebAddress.Matches(text))
        {
            if (m.Index > at)
            {
                inlines.Add(new Run(text[at..m.Index]));
            }

            var address = m.Value.TrimEnd('.', ',', ')', ';', '!', '?');
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var link = new TextBlock { Text = address, FontSize = size, TextDecorations = TextDecorations.Underline, Cursor = new Cursor(StandardCursorType.Hand) }.Res(TextBlock.ForegroundProperty, "Brush.Accent");
                ToolTip.SetTip(link, AppStrings.Torrent_LinkTip);
                link.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(link).Properties.IsLeftButtonPressed)
                    {
                        _request.OpenWeb?.Invoke(uri);
                        e.Handled = true;
                    }
                };
                inlines.Add(new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.TextBottom });
            }
            else
            {
                inlines.Add(new Run(address));
            }

            at = m.Index + address.Length;
        }

        if (at < text.Length)
        {
            inlines.Add(new Run(text[at..]));
        }

        return block;
    }

    private static TextBlock Section(string text)
        => new TextBlock { Text = text.ToUpper(CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 18, 0, 6) }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");

    private static TextBlock Note(string text)
        => new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");

    private static Border Boxed(Control child)
        => new Border { BorderThickness = new Thickness(1), CornerRadius = Corners.Chip, Padding = new Thickness(0, 3), Child = child }.Res(Border.BorderBrushProperty, "Brush.Border");

    private static Border TagChip(string text, bool warn)
        => new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = Corners.Chip,
            Padding = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 11 }.Res(TextBlock.ForegroundProperty, warn ? "Brush.Warning" : "Brush.SecondaryText"),
        }.Res(Border.BorderBrushProperty, warn ? "Brush.Warning" : "Brush.BorderStrong");

    private void BuildFilesHost()
    {
        _filesFilter.PlaceholderText = AppStrings.Torrent_FilterHint;
        _filesFilter.TextChanged += (_, _) => RebuildFileRows();
        var copy = new Button { Content = AppStrings.Torrent_CopyList, MinHeight = 28, Padding = new Thickness(10, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        copy.Click += (_, _) => CopyFileList();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 8), Children = { _filesFilter, copy, _onlyIncomplete } };
        _filesHost.Children.Add(bar);
        _onlyIncomplete.IsCheckedChanged += (_, _) => RebuildFileRows();
        BuildCheckBar();
        Grid.SetRow(_checkBar, 1);
        _filesHost.Children.Add(_checkBar);


        var head = _filesHead;
        head.ColumnDefinitions = Columns();
        head.ColumnSpacing = 8;
        head.Children.Add(HeadText(AppStrings.Torrent_ColName, 0, HorizontalAlignment.Left));
        head.Children.Add(HeadText(AppStrings.Torrent_ColSize, 1, HorizontalAlignment.Right));
        head.Children.Add(HeadText(AppStrings.Torrent_ColShare, 2, HorizontalAlignment.Right));
        Grid.SetRow(head, 2);
        _filesHost.Children.Add(head);

        _filesList.Margin = new Thickness(4, 0);
        _filesList.Styles.Add(new Style(x => x.OfType<ListBoxItem>()) { Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 1)), new Setter(ListBoxItem.MinHeightProperty, 26.0), new Setter(ListBoxItem.CornerRadiusProperty, Corners.Chip) } });
        _filesList.ItemTemplate = new FuncDataTemplate<TorrentRow>((row, _) => row is null ? new Panel() : RowControl(row), supportsRecycling: false);
        _filesList.DoubleTapped += (_, _) => ToggleSelectedFolder();
        Grid.SetRow(_filesList, 3);
        _filesHost.Children.Add(_filesList);
        _filesFoot.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        Grid.SetRow(_filesFoot, 4);
        _filesHost.Children.Add(_filesFoot);

        static TextBlock HeadText(string text, int column, HorizontalAlignment align)
        {
            var t = new TextBlock { Text = text, FontSize = 12, HorizontalAlignment = align }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
            Grid.SetColumn(t, column);
            return t;
        }
    }

    private Grid RowGrid() => new() { ColumnDefinitions = Columns(), ColumnSpacing = 8 };

    private Control RowControl(TorrentRow row)
    {
        var grid = RowGrid();
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(row.Depth * 18, 0, 0, 0) };
        name.Children.Add(new TextBlock { Text = row.IsFolder ? (row.IsOpen ? "▾" : "▸") : "", Width = 10, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText"));
        name.Children.Add(AppIcons.Glyph(row.IsFolder ? "Icon.Folder" : "Icon.File", 16, 16));
        name.Children.Add(new TextBlock { Text = row.Shown, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }.Res(TextBlock.ForegroundProperty, row.Issue == TorrentNameIssue.None ? "Brush.Text" : "Brush.Danger"));
        if (row.Issue != TorrentNameIssue.None)
        {
            name.Children.Add(TagChip(row.Issue switch
            {
                TorrentNameIssue.Reserved => AppStrings.Torrent_Issue_Reserved,
                TorrentNameIssue.HiddenMark => AppStrings.Torrent_Issue_Hidden,
                _ => AppStrings.Torrent_Issue_NotAllowed,
            }, warn: true));
        }

        grid.Children.Add(name);
        var format = DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture);
        var size = new TextBlock { Text = format.Size(row.Size), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.Text");
        Grid.SetColumn(size, 1);
        grid.Children.Add(size);
        var share = new TextBlock { Text = ShareText(row.Size, Torrent?.TotalSize ?? 0), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        Grid.SetColumn(share, 2);
        if (CheckCell(row) is { } cell)
        {
            Grid.SetColumn(cell, 3);
            grid.Children.Add(cell);
        }

        grid.Children.Add(share);

        return grid;
    }

    private void RebuildFileRows()
    {
        if (_torrent is not { } t)
        {
            _rows = [];
            _filesList.ItemsSource = _rows;
            return;
        }

        var selected = _filesList.SelectedItem is TorrentRow s ? s.Key : null;
        var rows = new List<TorrentRow>();
        var filter = _filesFilter.Text?.Trim() ?? "";
        if (filter.Length > 0 || OnlyIncomplete)
        {
            foreach (var file in ListedFiles(t.Info))
            {
                rows.Add(new TorrentRow(file.Path, TorrentNames.Shown(file.Path), 0, false, false, file.Length, file.Issue));
            }
        }
        else if (t.Info.IsSingleFile && t.Info.Files.Count == 1)
        {
            var only = t.Info.Files[0];
            rows.Add(new TorrentRow(only.Path, TorrentNames.Shown(only.Name), 0, false, false, only.Length, only.Issue));
        }
        else
        {
            Add(t.Top, 0);
        }

        _rows = rows;
        _filesList.ItemsSource = rows;
        var again = selected is null ? -1 : rows.FindIndex(r => r.Key == selected);
        _filesList.SelectedIndex = rows.Count == 0 ? -1 : Math.Max(0, again);
        UpdateFilesFoot(t.Info, filter.Length > 0 || OnlyIncomplete ? rows.Count : (int?)null);

        void Add(TorrentFolder folder, int depth)
        {
            var open = _openFolders.Contains(folder.Key);
            rows.Add(new TorrentRow(folder.Key, TorrentNames.Shown(folder.Name.Length > 0 ? folder.Name : AppStrings.Torrent_NoName), depth, true, open, folder.Size, TorrentNames.Check(folder.Name)));
            if (!open)
            {
                return;
            }

            foreach (var sub in folder.Folders)
            {
                Add(sub, depth + 1);
            }

            foreach (var file in folder.Files)
            {
                rows.Add(new TorrentRow(file.Path, TorrentNames.Shown(file.Name), depth + 1, false, false, file.Length, file.Issue));
            }
        }
    }

    private IEnumerable<TorrentFile> ListedFiles(TorrentInfo info)
    {
        var filter = _filesFilter.Text?.Trim() ?? "";
        var onlyIncomplete = OnlyIncomplete;
        return info.Files.Where(f => !f.IsPadding && (filter.Length == 0 || TorrentNames.Shown(f.Path).Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            && (!onlyIncomplete || !_checked.TryGetValue(f.Path, out var result) || result.State != TorrentFileState.Complete));
    }

    private void UpdateFilesFoot(TorrentInfo info, int? matching)
    {
        var files = Loc.Plural(info.FileCount, AppStrings.Torrent_Files_One, AppStrings.Torrent_Files_Other);
        var text = matching is { } n
            ? Loc.Format(AppStrings.Torrent_FilesMatching, Loc.Plural(n, AppStrings.Torrent_Files_One, AppStrings.Torrent_Files_Other), files)
            : info.FolderCount > 0 ? Loc.Format(AppStrings.Torrent_FilesIn, files, Loc.Plural(info.FolderCount, AppStrings.Torrent_Folders_One, AppStrings.Torrent_Folders_Other)) : files;
        if (info.PaddingCount > 0)
        {
            text += " · " + Loc.Plural(info.PaddingCount, AppStrings.Torrent_Padding_One, AppStrings.Torrent_Padding_Other);
        }

        if (info.StrangeNames is var strange and > 0)
        {
            text += " · " + Loc.Plural(strange, AppStrings.Torrent_StrangeShort_One, AppStrings.Torrent_StrangeShort_Other);
        }

        _filesFoot.Text = text;
    }

    private void ToggleSelectedFolder()
    {
        if (_filesList.SelectedItem is TorrentRow { IsFolder: true } row)
        {
            if (!_openFolders.Remove(row.Key))
            {
                _openFolders.Add(row.Key);
            }

            RebuildFileRows();
        }
    }

    private bool TorrentKey(Key key, KeyModifiers mods)
    {
        var plain = mods == KeyModifiers.None;
        var ctrl = mods == KeyModifiers.Control;
        if (_torrentPage == TorrentPage.Summary)
        {
            const double step = 40;
            var page = Math.Max(step, _torrentHost.Viewport.Height - step);
            switch (key)
            {
                case Key.Up when plain: _torrentHost.Offset = _torrentHost.Offset.WithY(Math.Max(0, _torrentHost.Offset.Y - step)); return true;
                case Key.Down when plain: _torrentHost.Offset = _torrentHost.Offset.WithY(_torrentHost.Offset.Y + step); return true;
                case Key.PageUp when plain: _torrentHost.Offset = _torrentHost.Offset.WithY(Math.Max(0, _torrentHost.Offset.Y - page)); return true;
                case Key.PageDown when plain: _torrentHost.Offset = _torrentHost.Offset.WithY(_torrentHost.Offset.Y + page); return true;
                case Key.Home when plain || ctrl: _torrentHost.Offset = default; return true;
                case Key.End when plain || ctrl: _torrentHost.Offset = new Vector(0, double.MaxValue); return true;
                case Key.F or Key.G or Key.A or Key.S when ctrl: return true;
                case Key.C when ctrl: return false;
                case Key.F7 or Key.F2 or Key.F8 when plain: return true;
                default: return false;
            }
        }

        var count = _rows.Count;
        var at = _filesList.SelectedIndex;
        var rows = Math.Max(1, (int)(_filesList.Bounds.Height / 26) - 1);
        void Select(int i)
        {
            if (count > 0)
            {
                _filesList.SelectedIndex = Math.Clamp(i, 0, count - 1);
                _filesList.ScrollIntoView(_filesList.SelectedIndex);
            }
        }

        switch (key)
        {
            case Key.F when ctrl:
            case Key.F7 when plain:
                _filesFilter.Focus();
                _filesFilter.SelectAll();
                return true;
            case Key.Escape when plain && !string.IsNullOrEmpty(_filesFilter.Text):
                _filesFilter.Text = "";
                _filesList.Focus();
                return true;
            case Key.Up when plain: Select(at - 1); return true;
            case Key.Down when plain: Select(at + 1); return true;
            case Key.PageUp when plain: Select(at - rows); return true;
            case Key.PageDown when plain: Select(at + rows); return true;
            case Key.Home when plain || ctrl: Select(0); return true;
            case Key.End when plain || ctrl: Select(count - 1); return true;
            case Key.Enter when plain: ToggleSelectedFolder(); return true;
            case Key.Right when plain:
                if (_filesList.SelectedItem is TorrentRow { IsFolder: true, IsOpen: false } closed)
                {
                    _openFolders.Add(closed.Key);
                    RebuildFileRows();
                }

                return true;
            case Key.Left when plain:
                if (_filesList.SelectedItem is TorrentRow { IsFolder: true, IsOpen: true } open)
                {
                    _openFolders.Remove(open.Key);
                    RebuildFileRows();
                }
                else if (_filesList.SelectedItem is TorrentRow { Depth: > 0 } inner)
                {
                    Select(_rows.FindLastIndex(at, r => r.IsFolder && r.Depth == inner.Depth - 1));
                }

                return true;
            case Key.C when ctrl: CopyFileList(); return true;
            case Key.G or Key.A or Key.S when ctrl: return true;
            case Key.F2 or Key.F8 when plain: return true;
            default: return false;
        }
    }

    private bool FilterKey(Key key, KeyModifiers mods)
    {
        if (mods != KeyModifiers.None)
        {
            return false;
        }

        switch (key)
        {
            case Key.Escape:
                if (!string.IsNullOrEmpty(_filesFilter.Text))
                {
                    _filesFilter.Text = "";
                }
                else
                {
                    _filesList.Focus();
                }

                return true;
            case Key.Down or Key.Enter:
                _filesList.Focus();
                return true;
            default:
                return false;
        }
    }

    internal static string ShareText(long size, long total)
    {
        var percent = total > 0 ? 100.0 * size / total : 0;
        return Loc.Format(AppStrings.Torrent_Share, percent.ToString(percent >= 10 || size == 0 ? "0" : "0.#", CultureInfo.CurrentCulture));
    }

    private void TorrentMenuItems(Func<string, string?, Action, bool, MenuItem> item)
    {
        var can = Torrent is not null && _request.Clipboard is not null;
        if (!OnToolbar(_copyMagnet))
        {
            item(AppStrings.Torrent_CopyMagnet, null, CopyMagnetLink, can);
        }

        if (!OnToolbar(_copyHash))
        {
            item(AppStrings.Torrent_CopyHash, null, CopyInfoHash, can);
        }

        if (!OnToolbar(_checkButton) && _request.TorrentFolder is not null)
        {
            item(AppStrings.Torrent_Check, null, ShowCheckFolder, Torrent is not null);
        }

        if (_torrentPage == TorrentPage.Files)
        {
            item(AppStrings.Torrent_CopyList, "Ctrl+C", CopyFileList, can);
        }
    }

    private void UpdateTorrentStatus()
    {
        var left = new List<Border>();
        var right = new List<Border>();
        if (_torrentReading)
        {
            Status(left, AppStrings.Torrent_Reading, busy: true);
        }
        else if (Torrent is { } info)
        {
            var format = DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture);
            Status(left, info.Name.Length > 0 ? TorrentNames.Shown(info.Name) : AppStrings.Torrent_NoName, strong: true);
            Status(left, Subtitle(info, format));
            Status(right, AppStrings.Torrent_Offline_Short);
            if (_document is { } d)
            {
                Status(right, format.Size(d.Length));
            }
        }

        ShowStatus(left, right);
    }
}

internal sealed record TorrentRow(string Key, string Shown, int Depth, bool IsFolder, bool IsOpen, long Size, TorrentNameIssue Issue);

internal sealed class TorrentFolder
{
    private TorrentFolder(string key, string name)
    {
        Key = key;
        Name = name;
    }

    public string Key { get; }

    public string Name { get; }

    public List<TorrentFolder> Folders { get; } = [];

    public List<TorrentFile> Files { get; } = [];

    public long Size { get; private set; }

    public static TorrentFolder Build(TorrentInfo info)
    {
        var top = new TorrentFolder("", info.Name);
        var folders = new Dictionary<string, TorrentFolder>(StringComparer.Ordinal) { [""] = top };
        foreach (var file in info.Files.Where(f => !f.IsPadding))
        {
            var folder = top;
            var key = "";
            for (var i = 0; i < file.Parts.Count - 1; i++)
            {
                key = key.Length == 0 ? file.Parts[i] : key + "\\" + file.Parts[i];
                if (!folders.TryGetValue(key, out var sub))
                {
                    sub = folders[key] = new TorrentFolder(key, file.Parts[i]);
                    folder.Folders.Add(sub);
                }

                folder = sub;
            }

            folder.Files.Add(file);
        }

        top.Finish();
        return top;
    }

    private long Finish()
    {
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
        Folders.Sort((a, b) => comparer.Compare(a.Name, b.Name));
        Files.Sort((a, b) => comparer.Compare(a.Name, b.Name));
        Size = Folders.Sum(f => f.Finish()) + Files.Sum(f => f.Length);
        return Size;
    }
}
