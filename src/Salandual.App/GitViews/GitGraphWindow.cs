// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/GitViews/GitGraphWindow.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Git;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.GitViews;

internal sealed record GraphItem(GitCommit Commit, GitGraphRow Row, IReadOnlyList<GitGraphLine> Above, int Lanes, bool IsHead);

internal sealed class GitGraphWindow : Window
{
    private const double RowHeight = 24;

    private const double MinAuthorWidth = 50;

    private const double MaxAuthorWidth = 180;

    private double _authorWidth = MinAuthorWidth;
    private double _dateWidth = double.NaN;

    private readonly Func<GitLogQuery, Task<GitLog?>> _read;
    private readonly Func<Task<IReadOnlyList<GitTag>?>>? _tags;
    private int _branchesShown;
    private readonly Func<string, Task<IReadOnlyList<GitCommitChange>?>> _changes;
    private readonly Action<GitCommit> _open;
    private readonly Action<GitCommit, GitCommitChange> _diff;
    private readonly DisplayFormat _format;
    private readonly ListBox _commits = new() { SelectionMode = SelectionMode.Single };
    private readonly ListBox _files = new() { SelectionMode = SelectionMode.Single };
    private readonly ComboBox _branches = new() { MinWidth = 110, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _firstParent = new() { Content = AppStrings.Git_Graph_FirstParent, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summary = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _meta = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _body = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _count = new() { FontSize = 12 };
    private readonly TextBlock _status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private int _shownVersion;

    public GitGraphWindow(SettingsService settings, string repository, Func<GitLogQuery, Task<GitLog?>> read, Func<string, Task<IReadOnlyList<GitCommitChange>?>> changes, Action<GitCommit> open, Action<GitCommit, GitCommitChange> diff, Func<Task<IReadOnlyList<GitTag>?>>? tags = null)
    {
        _read = read;
        _tags = tags;
        _changes = changes;
        _open = open;
        _diff = diff;
        _format = DisplayFormat.From(settings.Current.Panels, CultureInfo.CurrentCulture);
        Title = Loc.Format(AppStrings.Git_Graph_Title, repository);
        Width = 1100;
        Height = 680;
        MinWidth = 640;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");
        Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(ListBoxItem.MinHeightProperty, RowHeight) },
        });

        _branches.ItemsSource = tags is null ? new[] { AppStrings.Git_Graph_All, AppStrings.Git_Graph_ThisOne } : new[] { AppStrings.Git_Graph_All, AppStrings.Git_Graph_ThisOne, AppStrings.Git_Graph_GoToTag };
        _branches.SelectedIndex = 0;
        _branches.SelectionChanged += (_, _) =>
        {
            if (_branches.SelectedIndex == 2)
            {
                _branches.SelectedIndex = _branchesShown;
                _ = ChooseTagAsync();
                return;
            }

            if (_branches.SelectedIndex != _branchesShown)
            {
                _branchesShown = _branches.SelectedIndex;
                Loading = LoadAsync();
            }
        };
        _firstParent.IsCheckedChanged += (_, _) => Loading = LoadAsync();
        var options = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(12, 8),
            Children = { new TextBlock { Text = AppStrings.Git_Graph_Branches, VerticalAlignment = VerticalAlignment.Center }, _branches, _firstParent },
        };

        ScrollViewer.SetHorizontalScrollBarVisibility(_commits, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        _commits.ItemTemplate = new FuncDataTemplate<GraphItem>((item, _) => item is null ? new Panel() : CommitRow(item));
        _commits.SelectionChanged += (_, _) => _ = ShowCommitAsync();
        _commits.DoubleTapped += (_, _) => OpenSelected();
        _commits.AddHandler(KeyDownEvent, OnCommitsKey, RoutingStrategies.Tunnel);
        Avalonia.Automation.AutomationProperties.SetName(_commits, AppStrings.Git_Graph_Commits);

        _files.ItemTemplate = new FuncDataTemplate<GitCommitChange>((change, _) => change is null ? new Panel() : FileRow(change));
        _files.DoubleTapped += (_, _) => DiffSelected();
        _files.AddHandler(KeyDownEvent, OnFilesKey, RoutingStrategies.Tunnel);
        Avalonia.Automation.AutomationProperties.SetName(_files, AppStrings.Git_Graph_Files);
        _meta.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _count.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _status.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");

        var details = new DockPanel { Margin = new Thickness(12, 4, 12, 8) };
        var head = new StackPanel { Spacing = 6, Children = { _summary, _meta, _body, _count } };
        DockPanel.SetDock(head, Dock.Top);
        details.Children.Add(head);
        details.Children.Add(new Border { Margin = new Thickness(0, 8, 0, 0), Child = _files });

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,Auto,2*") };
        split.Children.Add(_commits);
        var splitter = new GridSplitter { Width = 1, ResizeDirection = GridResizeDirection.Columns }.Res(GridSplitter.BackgroundProperty, "Brush.Border");
        Grid.SetColumn(splitter, 1);
        split.Children.Add(splitter);
        Grid.SetColumn(details, 2);
        split.Children.Add(details);

        var status = new Border { Padding = new Thickness(12, 4), BorderThickness = new Thickness(0, 1, 0, 0), Child = _status }
            .Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.WindowBackground");
        var top = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = options }.Res(Border.BorderBrushProperty, "Brush.Border");
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(status);
        dock.Children.Add(split);
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { Salandual.App.Theme.AppIcons.Glyph("Icon.GitBranch", 16, 16, 1.3), new TextBlock { Text = Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center } } };
        Grid.SetColumn(title.Children[1], 1);
        Content = Controls.TitleBarParts.ForWindow(this, title, dock);
        Loading = LoadAsync();
        Opened += (_, _) => _commits.Focus();
    }

    internal Task Loading { get; private set; }

    internal Task ShowingCommit { get; private set; } = Task.CompletedTask;

    internal IReadOnlyList<GraphItem> Items => _commits.ItemsSource as IReadOnlyList<GraphItem> ?? [];

    internal ListBox CommitList => _commits;

    internal ListBox FileList => _files;

    internal ComboBox BranchesBox => _branches;

    internal CheckBox FirstParentBox => _firstParent;

    internal string DetailsText => string.Join("\n", new[] { _summary.Text, _meta.Text, _body.Text, _count.Text }.Where(t => !string.IsNullOrEmpty(t)));

    internal string StatusText => _status.Text ?? "";

    private async Task LoadAsync()
    {
        var version = ++_shownVersion;
        var keep = (_commits.SelectedItem as GraphItem)?.Commit.Sha;
        var log = await _read(new GitLogQuery(AllBranches: _branches.SelectedIndex == 0, FirstParentOnly: _firstParent.IsChecked == true));
        if (version != _shownVersion)
        {
            return;
        }

        if (log is null)
        {
            _commits.ItemsSource = Array.Empty<GraphItem>();
            _status.Text = AppStrings.Git_ChangesUnreadable;
            return;
        }

        var rows = GitGraph.Layout(log.Commits);
        var lanes = GitGraph.Width(rows);
        var items = new GraphItem[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var commit = log.Commits[i];
            items[i] = new GraphItem(commit, rows[i], i > 0 ? rows[i - 1].Lines : [], lanes, commit.Refs.Any(r => r.IsCurrent));
        }

        (_authorWidth, _dateWidth) = ColumnWidths(log.Commits);
        _commits.ItemsSource = items;
        var at = keep is null ? -1 : Array.FindIndex(items, it => it.Commit.Sha == keep);
        _commits.SelectedIndex = items.Length == 0 ? -1 : Math.Max(0, at);
        _status.Text = items.Length == 0 ? AppStrings.Git_History_NoCommits
            : string.Join(" · ", new[] { log.More ? Loc.Format(AppStrings.Git_History_BarMore, items.Length) : Loc.Plural(items.Length, AppStrings.Git_History_Bar_One, AppStrings.Git_History_Bar_Other), AppStrings.Git_Graph_Keys });
        await ShowingCommit;
    }

    private Control CommitRow(GraphItem item)
    {
        var c = item.Commit;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), Height = RowHeight, ColumnSpacing = 8 };
        grid.Children.Add(new GitGraphCell(item.Row, item.Above, item.IsHead, item.Lanes) { Height = RowHeight });
        var sha = new TextBlock { Text = c.Short, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        Grid.SetColumn(sha, 1);
        grid.Children.Add(sha);
        var message = new DockPanel { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        foreach (var r in c.Refs)
        {
            var chip = RefChip(r);
            chip.Margin = new Thickness(0, 0, 5, 0);
            DockPanel.SetDock(chip, Dock.Left);
            message.Children.Add(chip);
        }

        message.Children.Add(new TextBlock { Text = c.Summary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(message, 2);
        grid.Children.Add(message);
        var author = new TextBlock { Text = c.Author, FontSize = 12, Width = _authorWidth, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        Grid.SetColumn(author, 3);
        grid.Children.Add(author);
        var local = c.When.LocalDateTime;
        var date = new TextBlock { Text = When(c), FontSize = 12, Width = _dateWidth, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        Grid.SetColumn(date, 4);
        grid.Children.Add(date);
        ToolTip.SetTip(grid, c.Author + " · " + c.Summary);
        return grid;
    }

    private string When(GitCommit c) => _format.Date(c.When.LocalDateTime) + " " + _format.Time(c.When.LocalDateTime);

    private (double Author, double Date) ColumnWidths(IReadOnlyList<GitCommit> commits)
    {
        var typeface = new Typeface(FontFamily);
        double Width(string text) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, null).WidthIncludingTrailingWhitespace;
        var author = commits.Select(c => c.Author).Distinct(StringComparer.Ordinal).Select(Width).DefaultIfEmpty(0).Max();
        var date = commits.Select(When).Distinct(StringComparer.Ordinal).Select(Width).DefaultIfEmpty(0).Max();
        return (Math.Clamp(Math.Ceiling(author) + 4, MinAuthorWidth, MaxAuthorWidth), Math.Ceiling(date) + 4);
    }

    internal (double Author, double Date) ColumnWidthsShown => (_authorWidth, _dateWidth);

    private static Control RefChip(GitRef r)
    {
        var token = r.Kind switch { GitRefKind.Branch => "Brush.Accent", GitRefKind.Remote => "Brush.SecondaryText", _ => "Brush.Warning" };
        var text = new TextBlock { Text = r.Name, FontSize = 11, FontWeight = r.IsCurrent ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, token);
        return new Border { BorderThickness = new Thickness(1), CornerRadius = Salandual.App.Theme.Corners.Chip, Padding = new Thickness(5, 0), Child = text, VerticalAlignment = VerticalAlignment.Center }
            .Res(Border.BorderBrushProperty, token);
    }

    private Control FileRow(GitCommitChange change)
    {
        var token = change.State switch { GitState.Added => "Brush.GitStaged", GitState.Deleted => "Brush.GitDeleted", _ => "Brush.GitChanged" };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12, Margin = new Thickness(6, 2) };
        grid.Children.Add(new TextBlock { Text = change.Path, TextTrimming = TextTrimming.PathSegmentEllipsis }.Res(TextBlock.ForegroundProperty, token));
        var lines = new TextBlock { Text = Lines(change), FontSize = 12 }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);
        var what = new TextBlock { Text = change.State switch { GitState.Added => AppStrings.Git_Graph_Added, GitState.Deleted => AppStrings.Git_Graph_Deleted, _ => AppStrings.Git_Graph_Changed }, FontSize = 12 }.Res(TextBlock.ForegroundProperty, token);
        Grid.SetColumn(what, 2);
        grid.Children.Add(what);
        return grid;
    }

    private string Lines(GitCommitChange c)
        => string.Join(" ", new[] { c.Added is > 0 ? "+" + _format.Number(c.Added.Value) : "", c.Removed is > 0 ? "−" + _format.Number(c.Removed.Value) : "" }.Where(s => s.Length > 0));

    private Task ShowCommitAsync() => ShowingCommit = ShowCommitCoreAsync();

    private async Task ShowCommitCoreAsync()
    {
        if (_commits.SelectedItem is not GraphItem item)
        {
            _summary.Text = _meta.Text = _body.Text = _count.Text = "";
            _files.ItemsSource = null;
            return;
        }

        var c = item.Commit;
        _summary.Text = c.Summary;
        var local = c.When.LocalDateTime;
        var parents = c.Parents.Count == 0 ? AppStrings.Git_NoParent
            : c.Parents.Count == 1 ? Loc.Format(AppStrings.Git_Parent_One, Short(c.Parents[0]))
            : Loc.Format(AppStrings.Git_Parents_Other, string.Join(", ", c.Parents.Select(Short)));
        _meta.Text = string.Join(" · ", new[] { c.Short, c.Author + " <" + c.Email + ">", _format.Date(local) + " " + _format.Time(local), parents });
        var body = c.Message.Length > c.Summary.Length ? c.Message[c.Summary.Length..].Trim() : "";
        _body.Text = body;
        _body.IsVisible = body.Length > 0;
        _count.Text = "";
        _files.ItemsSource = null;
        var changes = await _changes(c.Sha);
        if (!ReferenceEquals(_commits.SelectedItem, item))
        {
            return;
        }

        _files.ItemsSource = changes ?? [];
        var files = changes?.Count ?? 0;
        var added = changes?.Sum(x => x.Added ?? 0) ?? 0;
        var removed = changes?.Sum(x => x.Removed ?? 0) ?? 0;
        _count.Text = Loc.Plural(files, AppStrings.Git_Graph_Changed_One, AppStrings.Git_Graph_Changed_Other) + (added + removed > 0 ? ", +" + _format.Number(added) + " −" + _format.Number(removed) : "");
    }

    private static string Short(string sha) => sha[..Math.Min(7, sha.Length)];

    private void OnCommitsKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            OpenSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Right && e.KeyModifiers == KeyModifiers.None && _files.ItemCount > 0)
        {
            _files.SelectedIndex = Math.Max(0, _files.SelectedIndex);
            _files.Focus();
            e.Handled = true;
        }
    }

    private void OnFilesKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.F3 or Key.Enter)
        {
            DiffSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.None)
        {
            _commits.Focus();
            e.Handled = true;
        }
    }

    internal void OpenSelected()
    {
        if (_commits.SelectedItem is GraphItem item)
        {
            _open(item.Commit);
        }
    }

    internal ContextMenu? TagMenu { get; private set; }

    private async Task ChooseTagAsync()
    {
        if (_tags is null)
        {
            return;
        }

        var tags = await _tags();
        var menu = new ContextMenu();
        if (tags is not { Count: > 0 })
        {
            menu.Items.Add(new MenuItem { Header = AppStrings.Git_Tags_None, IsEnabled = false });
        }
        else
        {
            foreach (var tag in tags.Take(60))
            {
                var item = new MenuItem { Header = tag.Name + "  ·  " + tag.Commit.Short + "  ·  " + _format.Date(tag.When.LocalDateTime) };
                var chosen = tag;
                item.Click += (_, _) => _ = GoToCommitAsync(chosen.Commit.Sha);
                menu.Items.Add(item);
            }
        }

        menu.PlacementTarget = _branches;
        menu.Placement = PlacementMode.Bottom;
        menu.Closed += (_, _) => TagMenu = null;
        TagMenu = menu;
        menu.Open(_branches);
    }

    internal async Task GoToCommitAsync(string sha)
    {
        await Loading;
        if (Array.FindIndex(Items.ToArray(), i => i.Commit.Sha == sha) < 0 && _branches.SelectedIndex != 0)
        {
            _branches.SelectedIndex = 0;
            await Loading;
        }

        var at = Array.FindIndex(Items.ToArray(), i => i.Commit.Sha == sha);
        if (at >= 0)
        {
            _commits.SelectedIndex = at;
            _commits.ScrollIntoView(at);
            _commits.Focus();
        }
        else
        {
            _status.Text = AppStrings.Git_Graph_TagNotListed;
        }
    }

    internal void DiffSelected()
    {
        if (_commits.SelectedItem is GraphItem item && _files.SelectedItem is GitCommitChange change)
        {
            _diff(item.Commit, change);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
