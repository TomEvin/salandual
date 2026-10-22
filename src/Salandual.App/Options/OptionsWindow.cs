// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

internal sealed record OptionsCategory(string Id, string Title, string Description, string? Milestone = null, string? Later = null);

internal sealed record SettingItem(string Category, string Section, string? Title, string? Description, Func<Control> Build,
    bool Wide = false, string Keywords = "", bool SearchOnly = false, string? Badge = null, Func<Control>? Below = null);

public sealed partial class OptionsWindow : Window
{
    private static string _lastCategory = "general";

    private readonly AppServices _services;
    private readonly IReadOnlyList<OptionsCategory> _categories;
    private readonly List<SettingItem> _items = [];
    private readonly Dictionary<(string Category, string Section), Func<Control>> _asides = [];
    private readonly TextBox _search;
    private readonly TextBlock _searchHint;
    private readonly ListBox _nav;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _page;
    private readonly Button _reset;
    private readonly Dictionary<SettingItem, Control> _built = [];
    private readonly Dictionary<string, (TextBlock Title, Border Count, string Brush)> _navRows = [];
    private string _category = _lastCategory;
    private bool _syncingNav;

    private readonly DispatcherTimer _searchPause = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public OptionsWindow()
        : this((Application.Current as App)?.Services ?? AppServices.CreateIsolated())
    {
    }

    public OptionsWindow(AppServices services)
    {
        Closing += (_, _) => SettleMapSite();
        _services = services;
        Title = AppStrings.Opt_Title;
        Width = 1000;
        Height = 740;
        MinWidth = 760;
        MinHeight = 480;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.WindowBackground");

        _categories =
        [
            new("general", AppStrings.OptCat_General, AppStrings.OptCat_General_Desc),
            new("appearance", AppStrings.OptCat_Appearance, AppStrings.OptCat_Appearance_Desc),
            new("themes", AppStrings.OptCat_Themes, AppStrings.OptCat_Themes_Desc),
            new("layout", AppStrings.OptCat_Layout, AppStrings.OptCat_Layout_Desc),
            new("panels", AppStrings.OptCat_Panels, AppStrings.OptCat_Panels_Desc),
            new("keyboard", AppStrings.OptCat_Keyboard, AppStrings.OptCat_Keyboard_Desc),
            new("confirm", AppStrings.OptCat_Confirm, AppStrings.OptCat_Confirm_Desc),
            new("hotpaths", AppStrings.OptCat_HotPaths, AppStrings.OptCat_HotPaths_Desc),
            new("operations", AppStrings.OptCat_Operations, AppStrings.OptCat_Operations_Desc),
            new("viewers", AppStrings.OptCat_Viewers, AppStrings.OptCat_Viewers_Desc),
            new("editors", AppStrings.OptCat_Editors, AppStrings.OptCat_Editors_Desc),
            new("compare", AppStrings.OptCat_Compare, AppStrings.OptCat_Compare_Desc),
            new("advanced", AppStrings.OptCat_Advanced, AppStrings.OptCat_Advanced_Desc),
        ];


        BuildItems();

        _searchHint = Text("Ctrl+F", 11, brush: "Brush.TertiaryText");
        _searchHint.Margin = new Thickness(0, 0, 8, 0);
        _search = new TextBox { PlaceholderText = AppStrings.Opt_Search, Margin = new Thickness(12, 12, 12, 8), InnerRightContent = _searchHint };
        _search.Classes.Add("clearButton");
        _search.TextChanged += (_, _) => OnSearchChanged();
        _searchPause.Tick += (_, _) => SearchNow();
        _search.KeyDown += OnSearchKeyDown;

        _nav = new ListBox { Margin = new Thickness(6, 0, 6, 12), Background = Brushes.Transparent };
        foreach (var c in _categories)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            var brush = c.Milestone is null ? "Brush.Text" : "Brush.SecondaryText";
            var title = Text(c.Title, 14, brush: brush);
            row.Children.Add(title);
            if (c.Milestone is not null)
            {
                var badge = Badge(c.Milestone);
                Grid.SetColumn(badge, 1);
                row.Children.Add(badge);
            }

            var count = new Border
            {
                CornerRadius = new CornerRadius(999),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false,
                Child = Text("", 11, brush: "Brush.SecondaryText"),
            }.Res(Border.BorderBrushProperty, "Brush.BorderStrong");
            Grid.SetColumn(count, 2);
            row.Children.Add(count);
            _navRows[c.Id] = (title, count, brush);

            _nav.Items.Add(new ListBoxItem { Content = row, Tag = c.Id, Padding = new Thickness(10, 7) });
        }

        _nav.SelectionChanged += (_, _) =>
        {
            if (!_syncingNav && _nav.SelectedItem is ListBoxItem { Tag: string id })
            {
                _search.Text = "";
                ShowCategory(id);
            }
        };

        var left = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top);
        left.Children.Add(_search);
        left.Children.Add(_nav);

        _page = new StackPanel { Margin = new Thickness(28, 20, 28, 24), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Stretch };
        _scroll = new ScrollViewer { Content = _page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _scroll.Focusable = true;
        _scroll.FocusAdorner = null;
        _scroll.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.Handled)
            {
                _scroll.Focus();
            }
        }, RoutingStrategies.Bubble);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("240,1,*") };
        body.Children.Add(left);
        var divider = new Border().Res(Border.BackgroundProperty, "Brush.Border");
        Grid.SetColumn(divider, 1);
        body.Children.Add(divider);
        Grid.SetColumn(_scroll, 2);
        body.Children.Add(_scroll);

        _reset = new Button { Padding = new Thickness(12, 5) };
        _reset.Flyout = ResetFlyout();
        var close = new Button { Content = AppStrings.Common_Close, Padding = new Thickness(20, 5) };
        close.Classes.Add("accent");
        close.Click += (_, _) => Close();
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(16, 10) };
        footer.Children.Add(_reset);
        var applies = Text(AppStrings.Opt_AppliesNow, 12, brush: "Brush.SecondaryText");
        applies.Margin = new Thickness(0, 0, 16, 0);
        Grid.SetColumn(applies, 2);
        footer.Children.Add(applies);
        Grid.SetColumn(close, 3);
        footer.Children.Add(close);
        var footerBar = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = footer }.Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footerBar, Dock.Bottom);
        root.Children.Add(footerBar);
        root.Children.Add(body);
        Content = Controls.TitleBarParts.ForDialog(this, root);

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        ShowCategory(_category);
        Opened += (_, _) => (_nav.ContainerFromIndex(_nav.SelectedIndex) ?? (Control)_nav).Focus();
    }

    private SettingsService Settings => _services.Settings;

    private AppSettings Current => Settings.Current;

    internal string CurrentCategory => _category;

    internal bool IsSearching => !string.IsNullOrWhiteSpace(_search.Text);

    internal TextBox SearchBox => _search;

    internal StackPanel Page => _page;

    internal ScrollViewer PageScroll => _scroll;

    internal IReadOnlyList<SettingItem> Items => _items;

    internal const string ColorsPage = "themes";

    internal void ShowCategory(string id, SettingItem? scrollTo = null)
    {
        if (_category == "viewers" && id != "viewers")
        {
            SettleMapSite();
        }

        _searchPause.Stop();
        _category = id;
        _lastCategory = id;
        SyncNav(id);
        ShowNavCounts(null);
        var category = _categories.First(c => c.Id == id);
        _page.Children.Clear();
        _built.Clear();
        var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 20) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(Text(category.Title, 22, FontWeight.SemiBold));
        if (category.Milestone is not null)
        {
            title.Children.Add(Badge(category.Milestone));
        }

        header.Children.Add(title);
        header.Children.Add(Secondary(category.Description, 13));
        _page.Children.Add(header);

        if (category.Later is not null)
        {
            _page.Children.Add(Card(null, [Row(Loc.Format(AppStrings.Opt_ArrivesInTitle, Core.Commands.CommandText.Milestone(category.Milestone!)), category.Later, null, wide: true)]));
        }

        foreach (var section in _items.Where(i => i.Category == id && !i.SearchOnly).GroupBy(i => i.Section))
        {
            var aside = _asides.TryGetValue((id, section.Key), out var buildAside) ? buildAside() : null;
            _page.Children.Add(Card(section.Key.Length > 0 ? section.Key : null, section.Select(BuildRow), aside));
            if (id == "advanced" && section.Key == AppStrings.OptSec_YourSettings && SettingsNotice() is { } notice)
            {
                _page.Children.Add(notice);
            }
        }

        var resettable = Reset(id) is not null;
        _reset.IsVisible = resettable;
        _reset.Content = Loc.Format(AppStrings.Opt_ResetCategory, category.Title);
        if (scrollTo is not null && _built.TryGetValue(scrollTo, out var target))
        {
            Dispatcher.UIThread.Post(() => target.BringIntoView(), DispatcherPriority.Loaded);
        }
        else
        {
            _scroll.Offset = default;
        }
    }

    private Control BuildRow(SettingItem item)
    {
        var row = Row(item.Title, item.Description, item.Build(), item.Wide, badge: item.Badge, below: item.Below?.Invoke());
        _built[item] = row;
        return row;
    }

    internal void Rebuild()
    {
        var offset = _scroll.Offset;
        if (IsSearching)
        {
            ShowResults(_search.Text!);
        }
        else
        {
            ShowCategory(_category);
        }

        UpdateLayout();
        _scroll.Offset = offset;
    }

    private void RebuildLater() => Dispatcher.UIThread.Post(Rebuild);

    private void SyncNav(string? id)
    {
        _syncingNav = true;
        _nav.SelectedItem = id is null ? null : _nav.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string?)i.Tag == id);
        _syncingNav = false;
    }

    private void OnSearchChanged()
    {
        if (string.IsNullOrEmpty(_search.Text))
        {
            _search.InnerRightContent = _searchHint;
        }
        else
        {
            _search.ClearValue(TextBox.InnerRightContentProperty);
        }

        if (IsSearching)

        {
            _searchPause.Stop();
            _searchPause.Start();
        }
        else
        {
            ShowCategory(_category);
        }
    }

    internal void SearchNow()
    {
        _searchPause.Stop();
        if (IsSearching)
        {
            ShowResults(_search.Text!);
        }
    }

    internal IReadOnlyList<SettingItem> Find(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        bool Matches(string haystack) => words.All(w => compare.IndexOf(haystack, w, options) >= 0);

        bool StartsAWord(string haystack)
        {
            var parts = haystack.Split([' ', '\\', '/', '-', '_', '.', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
            return words.All(w => parts.Any(p => compare.IsPrefix(p, w, options)));
        }

        var found = _items.Where(item =>
        {
            var category = _categories.First(c => c.Id == item.Category).Title;
            return Matches(string.Join(' ', item.Title, item.Description, item.Keywords, item.Section, category));
        }).Concat(FoundThemes(StartsAWord)).Concat(FoundHotPaths(StartsAWord));

        var order = _categories.Select(c => c.Id).ToList();
        return [.. found.OrderBy(item => order.IndexOf(item.Category))];
    }

    private void ShowNavCounts(IReadOnlyList<SettingItem>? found)
    {
        foreach (var (id, (title, count, brush)) in _navRows)
        {
            var n = found?.Count(i => i.Category == id) ?? 0;
            count.IsVisible = n > 0;
            ((TextBlock)count.Child!).Text = n.ToString(CultureInfo.CurrentCulture);
            title.Res(TextBlock.ForegroundProperty, found is not null && n == 0 ? "Brush.TertiaryText" : brush);
        }
    }

    internal string? NavCount(string id) => _navRows[id].Count is { IsVisible: true } count ? ((TextBlock)count.Child!).Text : null;

    private void ShowResults(string query)
    {
        SyncNav(null);
        _reset.IsVisible = false;
        _page.Children.Clear();
        _built.Clear();
        var found = Find(query);
        ShowNavCounts(found);
        var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(Text(Loc.Format(AppStrings.Opt_ResultsFor, query.Trim()), 22, FontWeight.SemiBold));
        header.Children.Add(Secondary(found.Count == 0
            ? AppStrings.Opt_NoResults
            : Loc.Plural(found.Count, AppStrings.Opt_Found_One, AppStrings.Opt_Found_Other), 13));
        _page.Children.Add(header);
        if (found.Count > 0)
        {
            _page.Children.Add(Card(null, found.Select(item =>
            {
                var where = _categories.First(c => c.Id == item.Category).Title + (item.Section.Length > 0 ? " › " + item.Section : "");
                var row = Row(item.Title ?? item.Section, item.Description, item.Build(), item.Wide, where, item.Badge, item.Below?.Invoke());
                _built[item] = row;
                return row;
            })));
        }

        _scroll.Offset = default;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && IsSearching && Find(_search.Text!) is [var first, ..])
        {
            _search.Text = "";
            ShowCategory(first.Category, first);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _nav.Focus();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is Visual source && _keyBox is not null && (source == _keyBox || _keyBox.IsVisualAncestorOf(source)))
        {
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)

        {
            _search.Focus();
            _search.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && IsSearching && !IsDropDownOpen(e.Source))
        {
            _search.Text = "";
            e.Handled = true;
        }
    }

    private static bool IsDropDownOpen(object? source) => source is ComboBox { IsDropDownOpen: true };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private Func<AppSettings, AppSettings>? Reset(string id) => id switch
    {
        "appearance" => s => s with { Appearance = new() { Schedule = s.Appearance.Schedule, Themes = s.Appearance.Themes } },
        "themes" => s => s with { Appearance = s.Appearance with { Schedule = new() } },
        "layout" => s => s with { Layout = new() { PanelSplit = s.Layout.PanelSplit } },
        "panels" => s => s with { Panels = new() },
        "operations" => s => s with { Operations = new() },
        "confirm" => s => s with { Confirmations = new() },
        "general" => s => s with { General = new() },
        "viewers" => s => s with { Viewers = new() },
        "compare" => s => s with { Find = new() { Named = s.Find.Named, LookIn = s.Find.LookIn, Texts = s.Find.Texts, WindowWidth = s.Find.WindowWidth, WindowHeight = s.Find.WindowHeight, WindowMaximized = s.Find.WindowMaximized } },
        "hotpaths" => s => s with { HotPaths = new() { Paths = s.HotPaths.Paths }, DriveMenu = new() },
        "advanced" => s => s with { Advanced = new() },
        _ => null,
    };

    private Flyout ResetFlyout()
    {
        var flyout = new Flyout { Placement = PlacementMode.TopEdgeAlignedLeft };
        var text = Secondary("", 13);
        text.MaxWidth = 300;
        var confirm = new Button { Content = AppStrings.Common_Reset, Padding = new Thickness(16, 5) };
        confirm.Classes.Add("accent");
        confirm.Click += (_, _) =>
        {
            flyout.Hide();
            if (Reset(_category) is { } reset)
            {
                Settings.Update(reset);
                RebuildLater();
            }
        };
        flyout.Opening += (_, _) => text.Text = Loc.Format(AppStrings.Opt_ResetConfirm, _categories.First(c => c.Id == _category).Title);
        flyout.Content = new StackPanel { Spacing = 12, Children = { text, confirm } };
        return flyout;
    }

    private void BuildItems()
    {
        AddAppearance();
        AddThemes();
        AddLayout();
        AddPanels();
        AddKeyboard();
        AddHotPaths();
        AddOperations();
        AddConfirmations();
        AddViewers();
        AddEditors();
        AddCompareFiles();
        AddCompare();
        AddFind();
        AddGeneral();
        AddAdvanced();
    }

    private T EnabledWhile<T>(T control, Func<AppSettings, bool> enabled, bool dim = false)
        where T : Control
    {
        void Apply()
        {
            var on = enabled(Current);
            control.IsEnabled = on;
            if (dim)
            {
                control.Opacity = on ? 1 : 0.45;
            }
        }

        EventHandler<SettingsChangedEventArgs> changed = (_, _) => Apply();
        control.AttachedToVisualTree += (_, _) =>
        {
            Settings.Changed += changed;
            Apply();
        };
        control.DetachedFromVisualTree += (_, _) => Settings.Changed -= changed;
        Apply();
        return control;
    }

    private void Add(string category, string section, string? title, string? description, Func<Control> build, bool wide = false, string keywords = "", bool searchOnly = false, string? badge = null, Func<Control>? below = null)
        => _items.Add(new SettingItem(category, section, title, description, build, wide, keywords, searchOnly, badge, below));


    private void Aside(string category, string section, Func<Control> build) => _asides[(category, section)] = build;

    private void UpdateAppearance(Func<AppearanceSettings, AppearanceSettings> change) => Settings.Update(s => s with { Appearance = change(s.Appearance) });

    private void UpdateLayout(Func<LayoutSettings, LayoutSettings> change) => Settings.Update(s => s with { Layout = change(s.Layout) });

    private void UpdatePanels(Func<PanelSettings, PanelSettings> change) => Settings.Update(s => s with { Panels = change(s.Panels) });
}
