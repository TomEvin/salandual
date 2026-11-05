// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/QuickOpenBox.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.QuickOpen;

namespace Salandual.App.Controls;

public sealed record QuickGroup(string Title, IReadOnlyList<QuickItem> Items);

public enum QuickAction
{
    Open,

    OtherPanel,

    NewTab,

    View,

    Edit,

    ChangeKey,
}

public sealed class QuickOpenBox : Border
{
    private static readonly QuickScope[] Scopes = [QuickScope.All, QuickScope.Commands, QuickScope.Files, QuickScope.Path, QuickScope.Tabs, QuickScope.Settings];

    private readonly StackPanel _lines = new();
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 0, 8, 6) };
    private readonly TextBlock _foot = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _note = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _key = new() { FontSize = 11, Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(QuickItem Item, Border Row)> _rows = [];
    private IReadOnlyList<QuickGroup> _groups = [];
    private int _selected = -1;

    public QuickOpenBox()
    {
        Width = 620;
        MaxWidth = 620;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        BorderThickness = new Thickness(1);
        CornerRadius = Corners.Chip;
        BoxShadow = BoxShadows.Parse("0 8 32 0 #30000000");
        IsVisible = false;
        this.Res(BackgroundProperty, "Brush.PanelBackground").Res(BorderBrushProperty, "Brush.BorderStrong");
        _key.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");

        Input = new TextBox
        {
            Margin = new Thickness(8),
            MinHeight = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn = false,
            PlaceholderText = AppStrings.Quick_Placeholder,
            InnerLeftContent = AppIcons.Glyph("Icon.Find", 16, 16).Also(g => g.Margin = new Thickness(8, 0, 2, 0)),
            InnerRightContent = _key,
        };
        Avalonia.Automation.AutomationProperties.SetName(Input, AppStrings.Quick_Name);
        Input.TextChanged += (_, _) =>
        {
            UpdateChips();
            QueryChanged?.Invoke(this, Query);
        };
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        foreach (var scope in Scopes)
        {
            var chip = new Border
            {
                Padding = new Thickness(6, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = Corners.Chip,
                Tag = scope,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = ChipText(scope), FontSize = 11 },
            };
            ToolTip.SetTip(chip, AppStrings.Quick_ChipTip);
            chip.PointerPressed += (_, e) =>
            {
                ChooseScope(scope);
                e.Handled = true;
            };
            _chips.Children.Add(chip);
        }

        _scroll = new ScrollViewer { Content = _lines, MaxHeight = 380, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 5) };
        foot.Children.Add(_foot.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText"));
        foot.Children.Add(_note.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText").Also(n => Grid.SetColumn(n, 1)));
        var footBorder = new Border { Child = foot, BorderThickness = new Thickness(0, 1, 0, 0) }
            .Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.WindowBackground");

        Child = new DockPanel
        {
            Children =
            {
                Input.Also(i => DockPanel.SetDock(i, Dock.Top)),
                _chips.Also(c => DockPanel.SetDock(c, Dock.Top)),
                footBorder.Also(f => DockPanel.SetDock(f, Dock.Bottom)),
                _scroll,
            },
        };
        UpdateChips();
        UpdateFoot();
    }

    public TextBox Input { get; }

    public QuickOpenQuery Query => QuickOpenQuery.Parse(Input.Text);

    public event EventHandler<QuickOpenQuery>? QueryChanged;

    public event EventHandler<(QuickItem Item, QuickAction Action)>? Chosen;

    public event EventHandler? CloseRequested;

    public event EventHandler? CompleteRequested;

    public IReadOnlyList<QuickGroup> Groups
    {
        get => _groups;
        set
        {
            var was = _selected > 0 ? SelectedItem : null;
            _groups = value;
            Build();
            if (was is not null && _rows.FindIndex(r => r.Item.Kind == was.Kind && r.Item.Target == was.Target) is var at and > 0)
            {
                Select(at);
            }
        }
    }

    public string KeyText
    {
        get => _key.Text ?? "";
        set => _key.Text = value;
    }

    public bool FileOpens { get; set; }

    public string Note
    {
        get => _note.Text ?? "";
        set => _note.Text = value;
    }

    public IReadOnlyList<QuickItem> Items => [.. _rows.Select(r => r.Item)];

    public QuickItem? SelectedItem => _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Item : null;

    public int SelectedIndex
    {
        get => _selected;
        set => Select(value);
    }

    public IReadOnlyList<string> Headings => [.. _groups.Where(g => g.Items.Count > 0).Select(g => g.Title)];

    public QuickScope ChosenScope => Query.Scope;

    public string FootText => _foot.Text ?? "";

    public void Reset()
    {
        Input.Text = "";
        Note = "";
        UpdateChips();
    }

    public void ChooseScope(QuickScope scope)
    {
        Input.Text = Query.In(scope);
        Input.CaretIndex = Input.Text.Length;
        Input.Focus();
    }

    private static string ChipText(QuickScope scope) => scope switch
    {
        QuickScope.Commands => "> " + AppStrings.Quick_Commands,
        QuickScope.Files => "/ " + AppStrings.Quick_Files,
        QuickScope.Path => "\\ " + AppStrings.Quick_Path,
        QuickScope.Tabs => "@ " + AppStrings.Quick_Tabs,
        QuickScope.Settings => ", " + AppStrings.Quick_Settings,
        _ => AppStrings.Quick_All,
    };

    private void UpdateChips()
    {
        var on = Query.Scope;
        foreach (var chip in _chips.Children.OfType<Border>())
        {
            var mine = (QuickScope)chip.Tag! == on;
            chip.Res(Border.BorderBrushProperty, mine ? "Brush.Accent" : "Brush.Border");
            ((TextBlock)chip.Child!).Res(TextBlock.ForegroundProperty, mine ? "Brush.Accent" : "Brush.SecondaryText");
        }
    }

    private void Build()
    {
        _lines.Children.Clear();
        _rows.Clear();
        foreach (var group in _groups.Where(g => g.Items.Count > 0))
        {
            _lines.Children.Add(new TextBlock { Text = group.Title, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 6, 12, 2) }
                .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText"));
            foreach (var item in group.Items)
            {
                var row = Row(item);
                _rows.Add((item, row));
                _lines.Children.Add(row);
            }
        }

        if (_rows.Count == 0 && Input.Text?.Trim().Length > 0)
        {
            _lines.Children.Add(new TextBlock { Text = AppStrings.Quick_Nothing, FontSize = 12, Margin = new Thickness(12, 8) }.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText"));
        }

        _selected = -1;
        Select(0);
    }

    private Border Row(QuickItem item)
    {
        var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        var hits = item.Hits.ToHashSet();
        var run = new System.Text.StringBuilder();
        var hit = false;
        void Flush()
        {
            if (run.Length == 0)
            {
                return;
            }

            var r = new Run(run.ToString());
            if (hit)
            {
                r.FontWeight = FontWeight.SemiBold;
                r.Bind(Run.ForegroundProperty, this.GetResourceObservable("Brush.Accent"));
            }

            name.Inlines!.Add(r);
            run.Clear();
        }

        for (var i = 0; i < item.Name.Length; i++)
        {
            if (hits.Contains(i) != hit)
            {
                Flush();
                hit = !hit;
            }

            run.Append(item.Name[i]);
        }

        Flush();
        if (item.Detail.Length > 0)
        {
            name.Inlines!.Add(new Run("   " + item.Detail) { FontSize = 12 }.Also(r => r.Bind(Run.ForegroundProperty, this.GetResourceObservable("Brush.TertiaryText"))));
        }

        if (!item.Enabled)
        {
            name.Opacity = 0.55;
        }

        var key = new TextBlock { Text = item.Key, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) }
            .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(AppIcons.Glyph(item.OnlineOnly ? "Icon.QuickCloud" : GlyphOf(item.Kind), 16, 16).Also(g => g.VerticalAlignment = VerticalAlignment.Center));
        grid.Children.Add(name.Also(n => Grid.SetColumn(n, 1)));
        grid.Children.Add(key.Also(k => Grid.SetColumn(k, 2)));
        var row = new Border { Child = grid, Padding = new Thickness(12, 3), BorderThickness = new Thickness(2, 0, 0, 0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
        row.PointerPressed += (_, e) =>
        {
            Select(_rows.FindIndex(r => r.Row == row));
            if (e.ClickCount >= 1 && e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                Chosen?.Invoke(this, (item, QuickAction.Open));
            }

            e.Handled = true;
        };
        return row;
    }

    internal static string GlyphOf(QuickKind kind) => kind switch
    {
        QuickKind.Command => "Icon.QuickCommand",
        QuickKind.File => "Icon.File",
        QuickKind.Tab => "Icon.QuickTab",
        QuickKind.Setting => "Icon.Gear",
        QuickKind.More => "Icon.More",
        _ => "Icon.Folder",
    };

    private void Select(int index)
    {
        if (_selected >= 0 && _selected < _rows.Count)
        {
            var old = _rows[_selected].Row;
            old.Background = Brushes.Transparent;
            old.BorderBrush = Brushes.Transparent;
        }

        _selected = _rows.Count == 0 ? -1 : Math.Clamp(index, 0, _rows.Count - 1);
        if (_selected >= 0)
        {
            var row = _rows[_selected].Row;
            row.Res(Border.BackgroundProperty, "Brush.SelectionBackground").Res(Border.BorderBrushProperty, "Brush.Accent");
            row.BringIntoView();
        }

        UpdateFoot();
    }

    private void UpdateFoot()
        => _foot.Text = SelectedItem?.Kind == QuickKind.File ? (FileOpens ? AppStrings.Quick_FootFileOpen : AppStrings.Quick_FootFile)
            : SelectedItem?.Kind is QuickKind.Folder or QuickKind.Path ? AppStrings.Quick_FootFolder
            : SelectedItem?.Kind == QuickKind.Command ? AppStrings.Quick_FootCommand
            : AppStrings.Quick_Foot;

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        switch (e.Key)
        {
            case Key.Down when mods == KeyModifiers.None:
                Select(_selected + 1);
                break;
            case Key.Up when mods == KeyModifiers.None:
                Select(_selected - 1);
                break;
            case Key.PageDown when mods == KeyModifiers.None:
                Select(_selected + 10);
                break;
            case Key.PageUp when mods == KeyModifiers.None:
                Select(_selected - 10);
                break;
            case Key.Enter when SelectedItem is { } item:
                Chosen?.Invoke(this, (item, mods switch
                {
                    KeyModifiers.Control => QuickAction.OtherPanel,
                    KeyModifiers.Alt => QuickAction.NewTab,
                    _ => QuickAction.Open,
                }));
                break;
            case Key.F3 when mods == KeyModifiers.None && SelectedItem is { Kind: QuickKind.File } file:
                Chosen?.Invoke(this, (file, QuickAction.View));
                break;
            case Key.F4 when mods == KeyModifiers.None && SelectedItem is { Kind: QuickKind.File } file:
                Chosen?.Invoke(this, (file, QuickAction.Edit));
                break;
            case Key.K when mods == KeyModifiers.Control && SelectedItem is { Kind: QuickKind.Command } command:
                Chosen?.Invoke(this, (command, QuickAction.ChangeKey));
                break;
            case Key.Escape:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Key.Tab when Query.Scope == QuickScope.Path && mods == KeyModifiers.None && SelectedItem is { Kind: QuickKind.Path }:
                CompleteRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Key.Tab when mods is KeyModifiers.None or KeyModifiers.Shift:
                var at = Array.IndexOf(Scopes, Query.Scope);
                var step = mods == KeyModifiers.Shift ? Scopes.Length - 1 : 1;
                ChooseScope(Scopes[(at + step) % Scopes.Length]);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
