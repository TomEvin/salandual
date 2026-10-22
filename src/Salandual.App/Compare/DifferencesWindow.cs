// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Compare/DifferencesWindow.cs

using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Salandual.App.Find;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Compare;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.App.Compare;

internal sealed record DifferencesRequest
{
    public Action<CompareNode> ShowInPanels { get; init; } = _ => { };

    public Action SelectInPanels { get; init; } = () => { };

    public Func<bool> PanelsShowThem { get; init; } = () => false;

    public Action Refresh { get; init; } = () => { };

    public Action Stop { get; init; } = () => { };

    public Action<string> View { get; init; } = _ => { };

    public Action<string, string> CompareFiles { get; init; } = (_, _) => { };

    public Action OpenOptions { get; init; } = () => { };
}

internal sealed class DifferencesWindow : Window
{
    private const string Columns = "*,76,132,11,*,76,132";

    private readonly DifferencesRequest _request;
    private readonly SettingsService _settings;
    private readonly DisplayFormat _format;
    private readonly AvaloniaList<Row> _rows = [];
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Single };
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _count = new() { FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _allChip = new();
    private readonly Button _differentChip = new();
    private readonly Button _kindArrow = new();
    private readonly Button _sameChip = new();
    private readonly Button _notChip = new();
    private readonly TextBox _names = new();
    private readonly Button _clearNames = new();
    private readonly Button _expand = new();
    private readonly Button _showInPanels = new();
    private readonly Button _selectInPanels = new();
    private readonly Button _refresh = new();
    private readonly Button _stop = new();
    private readonly TextBlock _leftTitle = HeaderPath();
    private readonly TextBlock _rightTitle = HeaderPath();
    private readonly TextBlock _status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _notice = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _empty = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly TextBlock _enterKey = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Text = AppStrings.Compare_KeyCompareFiles };
    private readonly TextBlock _titleText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.PathSegmentEllipsis };
    private CompareView _view;
    private DifferenceFilter _kind;
    private bool _not;
    private CompareListFilter _filter = CompareListFilter.Differences;
    private CompareTree? _tree;

    public DifferencesWindow(SettingsService settings, DifferencesRequest request, string left, string right)
    {
        _request = request;
        _settings = settings;
        _format = DisplayFormat.From(settings.Current.Panels, CultureInfo.CurrentCulture);
        Width = 1280;
        Height = 640;
        MinWidth = 640;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Res(BackgroundProperty, "Brush.WindowBackground");
        Styles.Add(FindStyles.Create());
        SetFolders(left, right);
        var compare = settings.Current.Compare;
        _view = compare.ListView;
        _kind = compare.ListKind;
        _not = compare.ListNamesNot;
        _names.Text = compare.ListNames;
        _filter = FilterNow();

        var dock = new DockPanel();
        var tools = BuildTools();
        var header = BuildHeader();
        var status = BuildStatus();
        var keys = BuildKeys();
        DockPanel.SetDock(tools, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(keys, Dock.Bottom);
        DockPanel.SetDock(status, Dock.Bottom);
        dock.Children.Add(tools);
        dock.Children.Add(header);
        dock.Children.Add(keys);
        dock.Children.Add(status);

        _list.ItemsSource = _rows;
        _list.ItemTemplate = new FuncDataTemplate<Row>((row, _) => row is null ? new Panel() : RowControl(row));
        _list.Res(ListBox.BackgroundProperty, "Brush.PanelBackground");
        _list.SelectionChanged += (_, _) => UpdateKeys();
        _list.DoubleTapped += (_, e) =>
        {
            if (Focused is not null)
            {
                Enter();
                e.Handled = true;
            }
        };
        Avalonia.Automation.AutomationProperties.SetName(_list, AppStrings.Compare_ListTip);
        _empty.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        dock.Children.Add(new Panel { Children = { _list, _empty } });
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { AppIcons.Glyph("Icon.Panels", 16, 16, 1.3), _titleText } };
        Grid.SetColumn(_titleText, 1);
        _titleText.Res(TextBlock.ForegroundProperty, "Brush.Text");
        Content = Controls.TitleBarParts.ForWindow(this, title, dock);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Activated += (_, _) => UpdatePanelsButton();
        Opened += (_, _) => _list.Focus();
    }

    internal sealed record Row(CompareNode Node, int Depth);

    internal string LeftRoot { get; private set; } = "";

    internal string RightRoot { get; private set; } = "";

    internal IReadOnlyList<Row> Rows => _rows;

    internal ListBox List => _list;

    internal CompareListFilter Filter => _filter;

    internal string CountText => _count.Text ?? "";

    internal string NoticeText => _notice.IsVisible ? _notice.Text ?? "" : "";

    internal string StatusText => _status.Text ?? "";

    internal string EnterText => _enterKey.Text ?? "";

    internal Button ExpandButton => _expand;

    internal Button SelectInPanelsButton => _selectInPanels;

    internal Button AllChip => _allChip;

    internal Button DifferentChip => _differentChip;

    internal Button KindArrow => _kindArrow;

    internal Button SameChip => _sameChip;

    internal Button NotChip => _notChip;

    internal TextBox NamesBox => _names;

    internal Button ClearNamesButton => _clearNames;

    internal TextBlock? Legend { get; private set; }

    internal Button? OptionsButton { get; private set; }

    internal Row? Focused => _list.SelectedItem as Row;

    internal void SetFolders(string left, string right)
    {
        LeftRoot = left;
        RightRoot = right;
        Title = _titleText.Text = Loc.Format(AppStrings.Compare_ListTitle, left, right);
        _leftTitle.Text = left;
        _rightTitle.Text = right;
        ToolTip.SetTip(_leftTitle, left);
        ToolTip.SetTip(_rightTitle, right);
    }

    internal void ShowComparing(CompareProgress progress)
    {
        _stop.IsVisible = true;
        _refresh.IsVisible = false;
        _count.Text = AppStrings.Compare_BarComparing + "  " + Loc.Format(AppStrings.Compare_BarProgress, progress.Files.ToString("N0", CultureInfo.CurrentCulture), progress.Folders.ToString("N0", CultureInfo.CurrentCulture));
        _status.Text = progress.CurrentFolder ?? "";
    }

    internal void Show(DirectoryCompareResult result, CompareDirectoriesOptions options)
    {
        _stop.IsVisible = false;
        _refresh.IsVisible = true;
        _tree = CompareTree.From(result);
        UpdateCounts();
        _status.Text = StatusLine(result, options);
        Rebuild();
        if (_list.SelectedIndex < 0 && _rows.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        UpdatePanelsButton();
    }

    internal static string KindName(DifferenceFilter kind) => kind switch
    {
        DifferenceFilter.Differences => AppStrings.Compare_KindDifferences,
        DifferenceFilter.NoOrphans => AppStrings.Compare_KindNoOrphans,
        DifferenceFilter.DifferencesButNoOrphans => AppStrings.Compare_KindDiffNoOrphans,
        DifferenceFilter.Orphans => AppStrings.Compare_KindOrphans,
        DifferenceFilter.LeftNewer => AppStrings.Compare_KindLeftNewer,
        DifferenceFilter.RightNewer => AppStrings.Compare_KindRightNewer,
        DifferenceFilter.LeftNewerAndLeftOrphans => AppStrings.Compare_KindLeftNewerOrphans,
        DifferenceFilter.RightNewerAndRightOrphans => AppStrings.Compare_KindRightNewerOrphans,
        DifferenceFilter.LeftOrphans => AppStrings.Compare_KindLeftOrphans,
        _ => AppStrings.Compare_KindRightOrphans,
    };

    private CompareListFilter FilterNow() => new(_view, _kind, Salandual.Core.Listing.FileMask.Parse(_names.Text), _not);

    private int Count(CompareView view, DifferenceFilter kind) => _tree?.Count(_filter with { View = view, Kind = kind }) ?? 0;

    private void UpdateCounts()
    {
        _count.Text = Loc.Plural(Count(CompareView.Different, DifferenceFilter.Differences), AppStrings.Compare_ListCount_One, AppStrings.Compare_ListCount_Other);
        _allChip.Content = Loc.Format(AppStrings.Compare_ChipAll, Count(CompareView.All, _kind));
        _differentChip.Content = Loc.Format(AppStrings.Compare_ChipDifferent, KindName(_kind), Count(CompareView.Different, _kind));
        _sameChip.Content = Loc.Format(AppStrings.Compare_ChipSame, Count(CompareView.Same, _kind));
        _allChip.Classes.Set("on", _view == CompareView.All);
        _differentChip.Classes.Set("on", _view == CompareView.Different);
        _kindArrow.Classes.Set("on", _view == CompareView.Different);
        _sameChip.Classes.Set("on", _view == CompareView.Same);
        _notChip.Classes.Set("on", _not);
        _clearNames.IsVisible = !string.IsNullOrEmpty(_names.Text);
    }

    private static string StatusLine(DirectoryCompareResult result, CompareDirectoriesOptions o)
    {
        var by = new List<string>();
        if (o.Newer)
        {
            by.Add(AppStrings.Compare_ByDate);
        }

        if (o.Bigger)
        {
            by.Add(AppStrings.Compare_BySize);
        }

        if (o.Attributes)
        {
            by.Add(AppStrings.Compare_ByAttributes);
        }

        if (o.Content)
        {
            by.Add(AppStrings.Compare_ByContent);
        }

        var parts = new List<string> { by.Count > 0 ? Loc.Format(AppStrings.Compare_StatusBy, string.Join(AppStrings.Find_TitleAnd, by)) : AppStrings.Compare_ByNames };
        if (o.Subfolders)
        {
            parts.Add(AppStrings.Compare_StatusInside);
        }

        if (o.Newer && o.IgnoreTwoSeconds)
        {
            parts.Add(AppStrings.Compare_StatusTwoSeconds);
        }

        if (o.Newer && o.IgnoreOneHour)
        {
            parts.Add(AppStrings.Compare_StatusOneHour);
        }

        var line = string.Join("; ", parts);
        if (result.NotRead > 0)
        {
            line += "  ·  " + Loc.Format(AppStrings.Compare_BarNotRead, result.NotRead);
        }

        if (result.Problems.Count > 0)
        {
            line += "  ·  " + Loc.Format(AppStrings.Compare_BarProblems, result.Problems.Count);
        }

        if (result.Cancelled)
        {
            line += "  ·  " + AppStrings.Compare_BarStopped;
        }

        return line;
    }

    internal void SetView(CompareView view)
    {
        _view = view;
        ApplyFilter();
    }

    internal void SetKind(DifferenceFilter kind)
    {
        _kind = kind;
        _view = CompareView.Different;
        ApplyFilter();
    }

    internal void SetNot(bool not)
    {
        _not = not;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _filter = FilterNow();
        if (_settings.Current.Compare.ListRemember)
        {
            var names = (_names.Text ?? "").Trim();
            _settings.Update(s => s with { Compare = s.Compare with { ListView = _view, ListKind = _kind, ListNames = names, ListNamesNot = _not } });
        }

        UpdateCounts();
        Rebuild();
        if (_list.SelectedIndex < 0 && _rows.Count > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    internal void ExpandOrCollapseAll()
    {
        if (_tree is null)
        {
            return;
        }

        var folders = _tree.Folders(_filter).Select(n => n.Path).ToList();
        if (folders.All(_expanded.Contains))
        {
            _expanded.Clear();
        }
        else
        {
            _expanded.UnionWith(folders);
        }

        Rebuild();
    }

    internal void Toggle(CompareNode node)
    {
        if (!_expanded.Remove(node.Path))
        {
            _expanded.Add(node.Path);
        }

        Rebuild();
    }

    private void Rebuild()
    {
        var keep = Focused?.Node.Path;
        _rows.Clear();
        if (_tree is not null)
        {
            Add(_tree.Roots, 0);
        }

        var at = keep is null ? -1 : _rows.ToList().FindIndex(r => string.Equals(r.Node.Path, keep, StringComparison.OrdinalIgnoreCase));
        _list.SelectedIndex = at;
        _empty.IsVisible = _tree is not null && _rows.Count == 0;
        _empty.Text = _filter is { View: CompareView.Different, Kind: DifferenceFilter.Differences, Names: null } ? AppStrings.Compare_ListSame : AppStrings.Compare_ListEmpty;
        UpdateExpandButton();
        UpdateKeys();

        void Add(IEnumerable<CompareNode> nodes, int depth)
        {
            foreach (var node in nodes.Where(n => CompareTree.Shows(n, _filter)))
            {
                _rows.Add(new Row(node, depth));
                if (_expanded.Contains(node.Path))
                {
                    Add(node.Children, depth + 1);
                }
            }
        }
    }

    private bool HasRows(CompareNode node) => node.Children.Any(c => CompareTree.Shows(c, _filter));

    private void UpdateExpandButton()
    {
        var folders = _tree?.Folders(_filter).ToList() ?? [];
        var all = folders.Count > 0 && folders.All(n => _expanded.Contains(n.Path));
        _expand.Content = (all ? "▾ " : "▸ ") + (all ? AppStrings.Compare_CollapseAll : AppStrings.Compare_ExpandAll);
        ToolTip.SetTip(_expand, all ? AppStrings.Compare_CollapseAllTip : AppStrings.Compare_ExpandAllTip);
        _expand.IsEnabled = folders.Count > 0;
    }

    private void UpdateKeys()
    {
        var text = Focused?.Node is not { } node ? AppStrings.Compare_KeyCompareFiles
            : node.IsDirectory ? AppStrings.Compare_KeyExpandFolder
            : node.Item.Left is not null && node.Item.Right is not null ? AppStrings.Compare_KeyCompareFiles : AppStrings.Find_KeyView;
        if (_enterKey.Text == text)
        {
            return;
        }

        _enterKey.Text = text;
        for (Visual? v = _enterKey; v is Layoutable layoutable; v = v.GetVisualParent())
        {
            layoutable.InvalidateMeasure();
            if (v is Border)
            {
                break;
            }
        }
    }

    internal void UpdatePanelsButton() => _selectInPanels.IsVisible = _tree is not null && _request.PanelsShowThem();

    internal void Enter()
    {
        if (Focused?.Node is not { } node)
        {
            return;
        }

        if (node.IsDirectory)
        {
            if (HasRows(node))
            {
                Toggle(node);
            }
        }
        else if (node.Item.Left is not null && node.Item.Right is not null)
        {
            _request.CompareFiles(Path.Join(LeftRoot, node.Path), Path.Join(RightRoot, node.Path));
        }
        else
        {
            View();
        }
    }

    internal void View()
    {
        if (Focused?.Node is { IsDirectory: false } node)
        {
            _request.View(Path.Join(node.Item.Left is not null ? LeftRoot : RightRoot, node.Path));
        }
    }

    private void ShowNotice(string text)
    {
        _notice.Text = text;
        _notice.IsVisible = true;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key is >= Key.D0 and <= Key.D9)
        {
            SetKind((DifferenceFilter)((e.Key - Key.D1 + 10) % 10));
            e.Handled = true;
            return;
        }

        if (ReferenceEquals(e.Source, _names) && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            if (string.IsNullOrEmpty(_names.Text))
            {
                _list.Focus();
            }
            else
            {
                _names.Text = "";
            }

            e.Handled = true;
            return;
        }

        if (e.Source is TextBox)
        {
            return;
        }

        _notice.IsVisible = false;
        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        var plain = e.KeyModifiers == KeyModifiers.None;
        var node = Focused?.Node;
        switch (e.Key)
        {
            case Key.Right when plain:
                if (node is { IsDirectory: true } && HasRows(node))
                {
                    if (_expanded.Contains(node.Path))
                    {
                        _list.SelectedIndex++;
                    }
                    else
                    {
                        Toggle(node);
                    }
                }

                break;
            case Key.Left when plain:
                if (node is not null && _expanded.Contains(node.Path))
                {
                    Toggle(node);
                }
                else if (Focused is { Depth: > 0 } row)
                {
                    var at = _list.SelectedIndex;
                    while (at > 0 && _rows[at].Depth >= row.Depth)
                    {
                        at--;
                    }

                    _list.SelectedIndex = at;
                }

                break;
            case Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown when plain && _rows.Count > 0:
                var page = Math.Max(1, (int)(_list.Bounds.Height / 24) - 1);
                var index = _list.SelectedIndex;
                _list.SelectedIndex = e.Key switch
                {
                    Key.Up => Math.Max(0, index - 1),
                    Key.Down => Math.Min(_rows.Count - 1, index + 1),
                    Key.Home => 0,
                    Key.End => _rows.Count - 1,
                    Key.PageUp => Math.Max(0, index - page),
                    _ => Math.Min(_rows.Count - 1, index + page),
                };
                break;
            case Key.O when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                _request.OpenOptions();
                break;
            case Key.E when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                ExpandOrCollapseAll();
                break;
            case Key.Enter when plain:
                Enter();
                break;
            case Key.F3 when plain:
                View();
                break;
            case Key.J when ctrl && node is not null:
                _request.ShowInPanels(node);
                break;
            case Key.R when ctrl:
                _request.Refresh();
                break;
            case Key.Escape when plain:
                if (_stop.IsVisible)
                {
                    _request.Stop();
                }
                else
                {
                    Close();
                }

                break;
            default:
                return;
        }

        e.Handled = true;
        if (_list.SelectedItem is { } item)
        {
            _list.ScrollIntoView(item);
        }

        if (!_list.IsKeyboardFocusWithin)
        {
            _list.Focus();
        }
    }

    private Control BuildTools()
    {
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        chips.Children.Add(_count);
        foreach (var chip in new[] { _allChip, _differentChip, _kindArrow, _sameChip, _notChip })
        {
            chip.Classes.Add("chip");
        }

        _allChip.Click += (_, _) => SetView(CompareView.All);
        _differentChip.Click += (_, _) => SetView(CompareView.Different);
        _sameChip.Click += (_, _) => SetView(CompareView.Same);
        _differentChip.CornerRadius = new CornerRadius(4, 0, 0, 4);
        _kindArrow.CornerRadius = new CornerRadius(0, 4, 4, 0);
        _kindArrow.Padding = new Thickness(2, 0);
        _kindArrow.Content = AppIcons.Glyph("Icon.ChevronDown", 16, 16, 1.4);
        ToolTip.SetTip(_kindArrow, AppStrings.Compare_KindTip);
        Avalonia.Automation.AutomationProperties.SetName(_kindArrow, AppStrings.Compare_KindTip);
        _kindArrow.Click += (_, _) => OpenKindMenu();
        var different = new StackPanel { Orientation = Orientation.Horizontal, Spacing = -1, Children = { _differentChip, _kindArrow } };
        _notChip.Content = AppStrings.Compare_Not;
        ToolTip.SetTip(_notChip, AppStrings.Compare_NotTip);
        _notChip.Click += (_, _) => SetNot(!_not);
        _names.Width = 160;
        _names.MinHeight = 24;
        _names.Height = 26;
        _names.FontSize = 12;
        _names.Padding = new Thickness(8, 3, 2, 3);
        _names.VerticalContentAlignment = VerticalAlignment.Center;
        _names.PlaceholderText = AppStrings.Compare_NamesHint;
        Avalonia.Automation.AutomationProperties.SetName(_names, AppStrings.Compare_NamesHint);
        _names.TextChanged += (_, _) => ApplyFilter();
        _clearNames.Content = new TextBlock { Text = "✕", FontSize = 10, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _clearNames.Classes.Add("ftool");
        _clearNames.Padding = new Thickness(6, 0);
        _clearNames.VerticalAlignment = VerticalAlignment.Stretch;
        ToolTip.SetTip(_clearNames, AppStrings.Compare_ClearNamesTip);
        Avalonia.Automation.AutomationProperties.SetName(_clearNames, AppStrings.Compare_ClearNamesTip);
        _clearNames.Click += (_, _) =>
        {
            _names.Text = "";
            _names.Focus();
        };
        _names.InnerRightContent = _clearNames;
        chips.Children.Add(_allChip);
        chips.Children.Add(different);
        chips.Children.Add(_sameChip);
        UpdateCounts();

        _expand.Classes.Add("chip");
        _expand.Click += (_, _) => ExpandOrCollapseAll();
        Tool(_showInPanels, AppStrings.Compare_ShowInPanels, "Ctrl+J", AppStrings.Compare_ShowInPanelsTip, () =>
        {
            if (Focused?.Node is { } node)
            {
                _request.ShowInPanels(node);
            }
        });
        Tool(_selectInPanels, AppStrings.Compare_SelectInPanelsButton, null, AppStrings.Compare_SelectInPanelsTip, () => _request.SelectInPanels());
        _selectInPanels.IsVisible = false;
        Tool(_refresh, null, "Ctrl+R", AppStrings.Compare_RefreshListTip, () => _request.Refresh());
        _stop.Content = Loc.Format(AppStrings.Find_Stop, "Esc");
        _stop.Classes.Add("fstop");
        _stop.Padding = new Thickness(14, 4);
        _stop.IsVisible = false;
        _stop.Click += (_, _) => _request.Stop();
        var separator = new Border { Width = 1, Height = 22, Margin = new Thickness(4, 0) }.Res(Border.BackgroundProperty, "Brush.BorderStrong");
        var separator2 = new Border { Width = 1, Height = 22, Margin = new Thickness(4, 0) }.Res(Border.BackgroundProperty, "Brush.BorderStrong");
        foreach (var button in new[] { _allChip, _differentChip, _kindArrow, _sameChip, _notChip, _expand, _showInPanels, _selectInPanels, _refresh, _stop, _clearNames })
        {
            button.Focusable = false;
        }

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { _expand, separator, _showInPanels, _selectInPanels, _refresh, _stop, separator2, _notChip, _names } };
        var dock = new DockPanel { Margin = new Thickness(12, 6, 8, 6) };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(chips);
        return new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = dock }.Res(Border.BorderBrushProperty, "Brush.Border");

        static void Tool(Button button, string? text, string? key, string tip, Action click)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (text is null)
            {
                content.Children.Add(AppIcons.Glyph("Icon.RotateRight", 16, 16, 1.3));
            }
            else
            {
                content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            }

            if (key is not null)
            {
                content.Children.Add(new TextBlock { Text = key, FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
            }

            button.Content = content;
            button.Classes.Add("ftool");
            ToolTip.SetTip(button, tip);
            Avalonia.Automation.AutomationProperties.SetName(button, text ?? tip);
            button.Click += (_, _) => click();
        }
    }

    internal MenuFlyout OpenKindMenu()
    {
        var menu = KindMenu();
        menu.ShowAt(_kindArrow);
        return menu;
    }

    private MenuFlyout KindMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var kind in Enum.GetValues<DifferenceFilter>())
        {
            var count = new TextBlock { Text = Loc.Format(AppStrings.Compare_KindCount, Count(CompareView.Different, kind)), Margin = new Thickness(6, 0, 0, 0) }
                .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
            var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = KindName(kind), FontWeight = kind == _kind ? FontWeight.SemiBold : FontWeight.Normal }, count } };
            var item = new MenuItem { Header = header, InputGesture = new KeyGesture(Key.D0 + (((int)kind + 1) % 10), KeyModifiers.Control), Tag = kind };
            if (kind == _kind && _view == CompareView.Different)
            {
                item.Icon = AppIcons.Glyph("Icon.Check", 14, 14, 1.5, "Brush.Accent");
            }

            item.Click += (_, _) => SetKind(kind);
            menu.Items.Add(item);
            if (kind is DifferenceFilter.DifferencesButNoOrphans or DifferenceFilter.Orphans or DifferenceFilter.RightNewer or DifferenceFilter.RightNewerAndRightOrphans)
            {
                menu.Items.Add(new Separator());
            }
        }

        return menu;
    }

    private static TextBlock HeaderPath() => new() { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.PathSegmentEllipsis, Margin = new Thickness(8, 0) };

    private Control BuildHeader()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 28 };
        var cells = new (Control Control, int Column)[]
        {
            (_leftTitle, 0), (Title(AppStrings.Find_ColSize), 1), (Title(AppStrings.Find_ColModified), 2), (Gap(), 3),
            (_rightTitle, 4), (Title(AppStrings.Find_ColSize), 5), (Title(AppStrings.Find_ColModified), 6),
        };
        foreach (var (control, column) in cells)
        {
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        return new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = grid }.Res(Border.BackgroundProperty, "Brush.PanelBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        static TextBlock Title(string text) => new TextBlock { Text = text, FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) }
            .Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
    }

    private static Border Gap() => new Border { BorderThickness = new Thickness(1, 0) }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

    private Control BuildStatus()
    {
        _status.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _notice.Res(TextBlock.ForegroundProperty, "Brush.Warning");
        _notice.IsVisible = false;
        var legend = OptionsUi.Text(AppStrings.Compare_Legend, 12, brush: "Brush.SecondaryText");
        legend.VerticalAlignment = VerticalAlignment.Center;
        Legend = legend;
        var dock = new DockPanel { Margin = new Thickness(12, 4) };
        DockPanel.SetDock(legend, Dock.Right);
        DockPanel.SetDock(_notice, Dock.Right);
        _notice.Margin = new Thickness(12, 0);
        dock.Children.Add(legend);
        dock.Children.Add(_notice);
        dock.Children.Add(_status);
        return new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = dock }.Res(Border.BorderBrushProperty, "Brush.Border");
    }

    private Control BuildKeys()
    {
        var keys = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 16, LineSpacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4) };
        foreach (var (key, what) in new (string[], TextBlock)[]
        {
            (["Enter"], _enterKey),
            (["Ctrl+J"], Label(AppStrings.Compare_ShowInPanels)),
            (["Ctrl+1", "Ctrl+0"], Label(AppStrings.Compare_KeyKinds)),
            (["Ctrl+Shift+E"], Label(AppStrings.Compare_KeyExpandAll)),
            (["F3"], Label(AppStrings.Find_KeyView)),
            (["Ctrl+R"], Label(AppStrings.Compare_KeyRefresh)),
        })
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            foreach (var k in key)
            {
                item.Children.Add(new Border { BorderThickness = new Thickness(1, 1, 1, 2), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0), Child = new TextBlock { Text = k, FontSize = 11, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace") } }
                    .Res(Border.BorderBrushProperty, "Brush.BorderStrong"));
            }

            what.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
            item.Children.Add(what);
            keys.Children.Add(item);
        }

        var gear = new Button { Content = AppIcons.Glyph("Icon.Gear", 16, 16, 1.3), Padding = new Thickness(6, 4), Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        gear.Classes.Add("ftool");
        ToolTip.SetTip(gear, AppStrings.Compare_OptionsTip);
        Avalonia.Automation.AutomationProperties.SetName(gear, AppStrings.Compare_OptionsTip);
        gear.Click += (_, _) => _request.OpenOptions();
        OptionsButton = gear;
        var dock = new DockPanel { Children = { gear, keys }, Margin = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(gear, Dock.Right);
        return new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = dock }.Res(Border.BorderBrushProperty, "Brush.Border");

        static TextBlock Label(string text) => new() { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    }

    private Control RowControl(Row row)
    {
        var node = row.Node;
        var d = node.Item;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 24 };
        var brush = d.Kind is DifferenceKind.Same or DifferenceKind.CountedSame ? "Brush.TertiaryText" : !CompareTree.Matches(node, _filter) ? "Brush.SecondaryText" : "Brush.Text";
        Side(d.Left, 0, CompareSide.Left, LeftRoot);
        grid.Children.Add(Gap().At(3));
        Side(d.Right, 4, CompareSide.Right, RightRoot);
        return grid;

        void Side(FileEntry? entry, int column, CompareSide side, string root)
        {
            if (entry is not { } e)
            {
                var hatch = new Hatch();
                Grid.SetColumn(hatch, column);
                Grid.SetColumnSpan(hatch, 3);
                grid.Children.Add(hatch);
                return;
            }

            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(6 + (row.Depth * 18), 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            if (node.IsDirectory && HasRows(node))
            {
                var open = _expanded.Contains(node.Path);
                var toggle = new Border { Width = 16, Height = 16, Background = Brushes.Transparent, Child = AppIcons.Glyph(open ? "Icon.ChevronDown" : "Icon.ChevronRight", 10, 10, 1.4), Cursor = new Cursor(StandardCursorType.Hand) };
                toggle.PointerPressed += (_, args) =>
                {
                    _list.SelectedItem = row;
                    Toggle(node);
                    args.Handled = true;
                };
                name.Children.Add(toggle);
            }
            else
            {
                name.Children.Add(new Border { Width = 16 });
            }

            var mark = new TextBlock { Text = Mark(d, side), Width = 12, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                .Res(TextBlock.ForegroundProperty, "Brush.Accent");
            ToolTip.SetTip(mark, MarkTip(d, side));
            name.Children.Add(mark);
            name.Children.Add(new EntryIcon(Path.Join(root, d.Subfolder), e));
            name.Children.Add(Cell(e.Name, brush, left: true));
            Grid.SetColumn(name, column);
            grid.Children.Add(name);
            grid.Children.Add(Cell(e.IsDirectory ? "" : _format.Size(e.Size), brush).At(column + 1));
            var local = e.LastWriteUtc.ToLocalTime();
            grid.Children.Add(Cell(_format.Date(local) + " " + _format.Time(local), brush).At(column + 2));
        }

        static TextBlock Cell(string text, string brush, bool left = false) => new TextBlock
        {
            Text = text,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = left ? TextAlignment.Left : TextAlignment.Right,
            Margin = left ? default : new Thickness(8, 0),
        }.Res(TextBlock.ForegroundProperty, brush);
    }

    internal static string? MarkTip(CompareDifference d, CompareSide side)
    {
        if (Mark(d, side).Length == 0)
        {
            return null;
        }

        if (d.Kind == DifferenceKind.NewerOrBigger && (side == CompareSide.Left ? (d.Left, d.Right) : (d.Right, d.Left)) is ({ } e, { } o))
        {
            var newer = e.LastWriteUtcTicks > o.LastWriteUtcTicks;
            var bigger = e.Size > o.Size;
            return newer && bigger ? AppStrings.Compare_MarkNewerBigger : bigger ? AppStrings.Compare_MarkBigger : AppStrings.Compare_MarkNewer;
        }

        return d.Kind switch
        {
            DifferenceKind.OnlyLeft => AppStrings.Compare_MarkOnlyLeft,
            DifferenceKind.OnlyRight => AppStrings.Compare_MarkOnlyRight,
            _ => d.Inside ? AppStrings.Compare_MarkInside : AppStrings.Compare_MarkDifferent,
        };
    }

    internal static string Mark(CompareDifference d, CompareSide side) => d.Kind switch
    {
        DifferenceKind.OnlyLeft => side == CompareSide.Left ? "←" : "",
        DifferenceKind.OnlyRight => side == CompareSide.Right ? "→" : "",
        DifferenceKind.NewerOrBigger => d.Selected.HasFlag(side) ? "▲" : "",
        DifferenceKind.Different => "≠",
        _ => "",
    };
}

internal sealed class Hatch : Control
{
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var brush = this.FindResource("Brush.Border") as IBrush ?? Brushes.Gray;
        var pen = new Pen(brush, 1);
        using (context.PushClip(bounds))
        {
            for (var x = -bounds.Height; x < bounds.Width; x += 7)
            {
                context.DrawLine(pen, new Point(x, bounds.Height), new Point(x + bounds.Height, 0));
            }
        }
    }
}

internal sealed class EntryIcon : Control
{
    private readonly string _folder;
    private readonly FileEntry _entry;
    private Panels.IconCache? _cache;

    public EntryIcon(string folder, FileEntry entry)
    {
        _folder = folder;
        _entry = entry;
        Width = 16;
        Height = 16;
        VerticalAlignment = VerticalAlignment.Center;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _cache = (Application.Current as App)?.Icons;
        if (_cache is not null)
        {
            _cache.Changed += OnChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_cache is not null)
        {
            _cache.Changed -= OnChanged;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (_cache?.Get(_folder, _entry, (int)Math.Round(16 * scale)) is { } bitmap)
        {
            context.DrawImage(bitmap, new Rect(0, 0, 16, 16));
            return;
        }

        var brush = this.FindResource("Brush.SecondaryText") as IBrush ?? Brushes.Gray;
        AppIcons.Draw(context, _entry.IsDirectory ? "Icon.Folder" : "Icon.File", new Point(0, 0), new Pen(brush, 1.2));
    }
}
