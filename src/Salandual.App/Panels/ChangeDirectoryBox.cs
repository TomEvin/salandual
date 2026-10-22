// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/ChangeDirectoryBox.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Panels;

public sealed record ChangeDirectoryContext(
    string BasePath,
    IReadOnlyList<string> Recent,
    bool RecentShared,
    string LeftPath,
    string RightPath,
    IReadOnlyList<HotPath> HotPaths,
    bool ShowHidden);

public sealed record ChangeDirectoryTarget(string Folder, string? FocusName);

public sealed class ChangeDirectoryBox
{
    private const int MaxFolders = 8;
    private const int MaxRecent = 9;

    private readonly StackPanel _rows = new();
    private readonly List<Row> _shown = [];
    private ChangeDirectoryContext _context = new("", [], false, "", "", [], false);
    private int _selected = -1;
    private string? _typedBeforeChoosing;
    private bool _untouched;
    private string? _setText;
    private string? _error;
    private int _version;
    private bool _going;
    private MenuFlyout? _browseMenu;

    private enum RowKind { Folder, Recent }

    private sealed record Row(RowKind Kind, string Path, int Typed = 0);

    public ChangeDirectoryBox()
    {
        Input = new TextBox
        {
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0),
            MinHeight = 0,
        };
        Input.Res(TextBox.FontFamilyProperty, "Font.Mono");
        foreach (var focus in new[] { false, true })
        {
            Input.Styles.Add(new Style(x => (focus ? x.OfType<TextBox>().Class("error").Class(":focus") : x.OfType<TextBox>().Class("error")).Template().OfType<Border>().Name("PART_BorderElement"))
            {
                Setters = { new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Brush.Danger")) },
            });
        }

        Avalonia.Automation.AutomationProperties.SetName(Input, AppStrings.Cd_InputName);
        Input.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Input.TextChanged += (_, _) =>
        {
            if (Input.Text == _setText)
            {
                _setText = null;
                return;
            }

            _error = null;
            Input.Classes.Remove("error");
            _selected = -1;
            _typedBeforeChoosing = null;
            Fill(untouched: false);
        };

        BrowseButton = new Button { Content = AppStrings.Cd_BrowseMenu, Padding = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, Focusable = false };
        BrowseButton.Click += (_, _) => ShowBrowseMenu();

        List = new Border
        {
            Padding = new Thickness(4),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 8 24 0 #30000000"),
            Child = new ScrollViewer { Content = _rows, MaxHeight = 360, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
        }.Res(Border.BackgroundProperty, "Brush.PanelBackground").Res(Border.BorderBrushProperty, "Brush.BorderStrong");
    }

    public TextBox Input { get; }

    public Button BrowseButton { get; }

    public Border List { get; }

    public event EventHandler<ChangeDirectoryTarget>? Accepted;

    public event EventHandler? Cancelled;

    public bool IsBusyElsewhere { get; private set; }

    internal string? Error => _error;

    internal IReadOnlyList<(string Kind, string Path)> Suggestions => [.. _shown.Select(r => (r.Kind.ToString(), r.Path))];

    internal int SelectedIndex => _selected;

    internal MenuFlyout? BrowseMenu => _browseMenu;

    public void Open(ChangeDirectoryContext context)
    {
        _context = context;
        _error = null;
        _going = false;
        Input.Classes.Remove("error");
        SetText(context.BasePath);
        Input.SelectAll();
        _selected = -1;
        Fill(untouched: true);
    }

    public void ShowRecent()
    {
        _selected = -1;
        Fill(untouched: true);
        Input.Focus();
    }

    private void SetText(string text)
    {
        _setText = text;
        Input.Text = text;
        Input.CaretIndex = text.Length;
    }

    private void Fill(bool untouched)
    {
        _untouched = untouched;
        var version = ++_version;
        var text = Input.Text ?? "";
        var recent = _context.Recent.Where(r => !string.Equals(r, _context.BasePath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (untouched || text.Trim().Length == 0)
        {
            Show([.. recent.Take(MaxRecent).Select(r => new Row(RowKind.Recent, r))]);
            return;
        }

        var matching = recent.Where(r => r.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
        if (PathInput.ForCompletion(text, _context.BasePath) is not { } split)
        {
            Show([.. matching.Select(r => new Row(RowKind.Recent, r))]);
            return;
        }

        var showHidden = _context.ShowHidden;
        _ = Task.Run(() => PathInput.Complete(split.Folder, split.Prefix, showHidden, MaxFolders)).ContinueWith(t =>
        {
            if (version != _version)
            {
                return;
            }

            var folders = t.IsCompletedSuccessfully ? t.Result : [];
            var rows = folders.Select(f => new Row(RowKind.Folder, f, split.Prefix.Length)).ToList();
            rows.AddRange(matching.Where(r => !folders.Contains(r, StringComparer.OrdinalIgnoreCase)).Select(r => new Row(RowKind.Recent, r)));
            Show(rows);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Show(List<Row> rows)
    {
        _shown.Clear();
        _shown.AddRange(rows);
        if (_selected >= rows.Count)
        {
            _selected = -1;
        }

        Draw();
    }

    private void Draw()
    {
        _rows.Children.Clear();
        if (_error is not null)
        {
            var error = new Border
            {
                Margin = new Thickness(2, 2, 2, 4),
                Padding = new Thickness(10, 6),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Child = OptionsUi.Text("⚠ " + _error, 12, brush: "Brush.Danger", wrap: true),
            }.Res(Border.BorderBrushProperty, "Brush.Danger");
            _rows.Children.Add(error);
        }

        RowKind? last = null;
        for (var i = 0; i < _shown.Count; i++)
        {
            var row = _shown[i];
            if (row.Kind != last)
            {
                var title = row.Kind == RowKind.Folder ? AppStrings.Copy_Group_Folders : _context.RecentShared ? AppStrings.Cd_RecentFolders : AppStrings.Cd_RecentHere;
                var header = OptionsUi.Text(title.ToUpperInvariant(), 11, FontWeight.SemiBold, "Brush.TertiaryText");
                header.Margin = new Thickness(10, 6, 10, 2);
                _rows.Children.Add(header);
                last = row.Kind;
            }

            var note = i == 0 && row.Kind == RowKind.Folder ? AppStrings.Cd_TabCompletes
                : row.Kind == RowKind.Recent && _untouched && i < MaxRecent ? $"Alt+{i + 1}"
                : null;
            _rows.Children.Add(RowControl(row, i, note));
        }

        var hints = new WrapPanel { Margin = new Thickness(10, 6, 10, 2) };
        foreach (var (key, what) in new[] { ("Enter", AppStrings.Cd_Hint_Go), ("↑↓", AppStrings.Cd_Hint_Choose), ("Tab", AppStrings.Cd_Hint_Complete), ("Esc", AppStrings.Cd_Hint_Cancel) })
        {
            var hint = OptionsUi.Text($"{key} {what}", 11, brush: "Brush.TertiaryText");
            hint.Margin = new Thickness(0, 0, 14, 0);
            hints.Children.Add(hint);
        }

        _rows.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 0) }.Res(Border.BackgroundProperty, "Brush.Border"));
        _rows.Children.Add(hints);
    }

    private Control RowControl(Row row, int index, string? note)
    {
        var name = WindowsPath.GetName(row.Path);
        var parent = row.Path[..^name.Length];
        var path = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.PrefixCharacterEllipsis };
        path.Res(TextBlock.FontFamilyProperty, "Font.Mono");
        if (row.Kind == RowKind.Folder)
        {
            var typed = Math.Min(row.Typed, name.Length);
            path.Inlines = [new Run(parent), new Run(name[..typed]) { FontWeight = FontWeight.Bold }, new Run(name[typed..])];
        }
        else
        {
            path.Text = row.Path;
        }

        var icon = AppIcons.Glyph(row.Kind == RowKind.Folder ? "Icon.Folder" : "Icon.History", 14, 14, 1.2);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(icon);
        Grid.SetColumn(path, 1);
        grid.Children.Add(path);
        if (note is not null)
        {
            var hint = OptionsUi.Text(note, 11, brush: "Brush.TertiaryText");
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }

        var selected = index == _selected;
        var border = new Border { Height = 28, Padding = new Thickness(10, 0), CornerRadius = new CornerRadius(4), Child = grid, Cursor = new Cursor(StandardCursorType.Hand) };
        if (selected)
        {
            border.Res(Border.BackgroundProperty, "Brush.SelectionBackground");
            path.Res(TextBlock.ForegroundProperty, "Brush.SelectedText");
        }
        else
        {
            border.Background = Brushes.Transparent;
            border.PointerEntered += (_, _) => border.Res(Border.BackgroundProperty, "Brush.Hover");
            border.PointerExited += (_, _) => border.Background = Brushes.Transparent;
        }

        border.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Go(row.Path);
        };
        return border;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape:
                Cancelled?.Invoke(this, EventArgs.Empty);
                return;
            case Key.Enter:
                Go(Input.Text ?? "");
                return;
            case Key.Up or Key.Down when mods == KeyModifiers.None:
                Choose(e.Key == Key.Down ? 1 : -1);
                return;
            case Key.Tab when mods == KeyModifiers.None:
                Complete();
                return;
            case Key.Right when mods == KeyModifiers.None && Input.CaretIndex == (Input.Text ?? "").Length && Input.SelectionStart == Input.SelectionEnd && Complete():
                return;
            case >= Key.D1 and <= Key.D9 when mods == KeyModifiers.Alt:
                var k = e.Key - Key.D1;
                if (k < _shown.Count)
                {
                    Go(_shown[k].Path);
                }

                return;
        }

        e.Handled = false;
    }

    private void Choose(int step)
    {
        if (_shown.Count == 0)
        {
            return;
        }

        if (_selected < 0)
        {
            _typedBeforeChoosing = Input.Text;
        }

        _selected = Math.Clamp(_selected + step, -1, _shown.Count - 1);
        SetText(_selected >= 0 ? _shown[_selected].Path : _typedBeforeChoosing ?? "");
        Draw();
    }

    private bool Complete()
    {
        var row = _selected >= 0 ? _shown[_selected] : _shown.FirstOrDefault();
        if (row is not { Kind: RowKind.Folder })
        {
            return false;
        }

        SetText(row.Path.EndsWith('\\') ? row.Path : row.Path + "\\");
        _selected = -1;
        Fill(untouched: false);
        return true;
    }

    internal async void Go(string text)
    {
        if (_going)
        {
            return;
        }

        var target = PathInput.Resolve(text, _context.BasePath);
        if (target is null)
        {
            ShowError(AppStrings.Cd_TypeAFolder);
            return;
        }

        _going = true;
        var (kind, real) = await Task.Run(() => PathInput.Probe(target));
        _going = false;
        switch (kind)
        {
            case PathInput.Kind.Folder:
                Accepted?.Invoke(this, new ChangeDirectoryTarget(real, ChildOnTheWay(real, _context.BasePath)));
                break;
            case PathInput.Kind.File when WindowsPath.GetParent(real) is { } folder:
                Accepted?.Invoke(this, new ChangeDirectoryTarget(folder, WindowsPath.GetName(real)));
                break;
            default:
                var root = WindowsPath.GetRoot(target);
                var rootMissing = root is null || !await Task.Run(() => PathInput.Probe(root).Kind == PathInput.Kind.Folder);
                var typed = text.Trim().Trim('"');
                ShowError(target.StartsWith(@"\\", StringComparison.Ordinal) && rootMissing ? Loc.Format(AppStrings.Cd_CantReach, root ?? target)
                    : rootMissing ? Loc.Format(AppStrings.Cd_NoDrive, root?.TrimEnd('\\') ?? target)
                    : string.Equals(typed, target, StringComparison.OrdinalIgnoreCase) ? Loc.Format(AppStrings.Cd_NotFound, target)
                    : Loc.Format(AppStrings.Cd_NotFoundHere, typed, target));
                break;
        }
    }

    internal static string? ChildOnTheWay(string target, string from)
    {
        var prefix = target.EndsWith('\\') ? target : target + "\\";
        if (!from.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || from.Length == prefix.Length)
        {
            return null;
        }

        var rest = from[prefix.Length..];
        var cut = rest.IndexOf('\\');
        return cut < 0 ? rest : rest[..cut];
    }

    private void ShowError(string message)
    {
        _error = message;
        if (!Input.Classes.Contains("error"))
        {
            Input.Classes.Add("error");
        }

        Draw();
        Input.Focus();
    }

    private void ShowBrowseMenu()
    {
        var menu = _browseMenu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Item(object header, Action run, string? note = null)
        {
            var item = new MenuItem { Header = note is null ? header : RowWithNote(header, note) };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }

        void Put(string path)
        {
            SetText(path);
            Input.Focus();
            Input.SelectAll();
            _error = null;
            Input.Classes.Remove("error");
            Fill(untouched: false);
        }

        Item(AppStrings.Common_Browse, () => _ = PickFolderAsync(), AppStrings.Cd_FolderPicker);
        menu.Items.Add(new Separator());
        Item(Mono(Loc.Format(AppStrings.Cd_LeftPanel, _context.LeftPath)), () => Put(_context.LeftPath));
        Item(Mono(Loc.Format(AppStrings.Cd_RightPanel, _context.RightPath)), () => Put(_context.RightPath));
        if (_context.HotPaths.Count > 0)
        {
            menu.Items.Add(new Separator());
            var header = OptionsUi.Text(AppStrings.Cd_HotPathsHeader, 11, FontWeight.SemiBold, "Brush.TertiaryText");
            menu.Items.Add(new MenuItem { Header = header, IsEnabled = false, Focusable = false, Padding = new Thickness(10, 6, 10, 0), MinHeight = 0 });
            foreach (var hp in _context.HotPaths)
            {
                var number = OptionsUi.Text(hp.Slot <= 10 ? (hp.Slot % 10).ToString(System.Globalization.CultureInfo.InvariantCulture) : hp.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture), 13, brush: "Brush.TertiaryText");
                number.MinWidth = 18;
                number.TextAlignment = TextAlignment.Right;
                var path = HotPathSettings.Expand(hp.Path);
                Item(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { number, OptionsUi.Text(hp.Name, 13) } }, () => Put(path), hp.Path);
            }
        }

        IsBusyElsewhere = true;
        menu.Closed += (_, _) =>
        {
            IsBusyElsewhere = false;
            if (ReferenceEquals(_browseMenu, menu))
            {
                _browseMenu = null;
            }

            if (!_picking)
            {
                Input.Focus();
            }
        };
        menu.ShowAt(BrowseButton);
    }

    private bool _picking;

    private async Task PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(Input) is not { } top)
        {
            return;
        }

        _picking = IsBusyElsewhere = true;
        try
        {
            var start = await top.StorageProvider.TryGetFolderFromPathAsync(_context.BasePath);
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = AppStrings.Opt_ChangeDirectory, SuggestedStartLocation = start });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            {
                Accepted?.Invoke(this, new ChangeDirectoryTarget(path, null));
                return;
            }
        }
        finally
        {
            _picking = IsBusyElsewhere = false;
        }

        Input.Focus();
    }

    private static TextBlock Mono(string text)
        => new TextBlock { Text = text, FontSize = 12, MaxWidth = 480, TextTrimming = TextTrimming.PrefixCharacterEllipsis }.Res(TextBlock.FontFamilyProperty, "Font.Mono");

    private static Grid RowWithNote(object left, string note)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinWidth = 300 };
        grid.Children.Add(left as Control ?? OptionsUi.Text(left.ToString() ?? "", 13));
        var right = new TextBlock { Text = note, FontSize = 12, MaxWidth = 280, Margin = new Thickness(24, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }
}
