// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/HotPathTable.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

internal sealed class HotPathTable : StackPanel
{
    private readonly SettingsService _settings;
    private readonly Func<string?>? _panelFolder;
    private readonly ListBox _list;
    private readonly (TextBlock Name, TextBlock Path, CheckBox Strip, CheckBox Menu)[] _rows = new (TextBlock, TextBlock, CheckBox, CheckBox)[HotPathSettings.SlotCount];
    private readonly TextBox _name;
    private readonly TextBox _path;
    private readonly TextBlock _keys;
    private readonly Button _up, _down, _clear;
    private bool _refreshing;

    public HotPathTable(SettingsService settings, Func<string?>? panelFolder)
    {
        _settings = settings;
        _panelFolder = panelFolder;
        Spacing = 10;

        var header = RowGrid();
        header.Margin = new Thickness(12, 0, 12, 0);
        AddCells(header, Head("#", right: true), Head(AppStrings.HotTable_Keys), Head(AppStrings.Common_Name), Head(AppStrings.HotTable_Folder), Head(AppStrings.HotTable_Bar, center: true), Head(AppStrings.Opt_Places_Menu, center: true));

        _list = new ListBox { Height = 248, Padding = new Thickness(2), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) }
            .Res(ListBox.BorderBrushProperty, "Brush.Border").Res(ListBox.BackgroundProperty, "Brush.PanelBackground");
        Avalonia.Automation.AutomationProperties.SetName(_list, AppStrings.Copy_Group_HotPaths);
        for (var slot = 1; slot <= HotPathSettings.SlotCount; slot++)
        {
            var s = slot;
            var grid = RowGrid();
            var number = Text(slot.ToString(CultureInfo.InvariantCulture), 13, brush: "Brush.TertiaryText");
            number.TextAlignment = TextAlignment.Right;
            Control keys = HotPathSettings.KeyOf(slot) is { } key ? Kbd(key) : new Control();
            var name = Text("", 13);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var path = Text("", 12, brush: "Brush.SecondaryText");
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            var strip = Box(Loc.Format(AppStrings.HotTable_InBar, slot), on => Change(s, hp => hp with { InStrip = on }));
            var menu = Box(Loc.Format(AppStrings.HotTable_InMenu, slot), on => Change(s, hp => hp with { InMenu = on }));
            AddCells(grid, number, keys, name, path, strip, menu);
            _rows[slot - 1] = (name, path, strip, menu);
            _list.Items.Add(new ListBoxItem { Content = grid, Padding = new Thickness(10, 1), MinHeight = 0 });
        }

        _list.SelectionChanged += (_, _) => FillEditor();

        _name = new TextBox { PlaceholderText = AppStrings.Common_Name };
        _path = new TextBox { PlaceholderText = AppStrings.HotTable_Folder };
        _path.Bind(TextBox.FontFamilyProperty, _path.GetResourceObservable("Font.Mono"));
        Avalonia.Automation.AutomationProperties.SetName(_name, AppStrings.Common_Name);
        Avalonia.Automation.AutomationProperties.SetName(_path, AppStrings.HotTable_Folder);
        _name.TextChanged += (_, _) => EditName();
        _path.TextChanged += (_, _) => EditPath();
        _keys = Text("", 13, brush: "Brush.SecondaryText");

        var editor = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*"), RowDefinitions = new RowDefinitions("Auto,Auto,8,Auto,Auto") };
        Place(editor, Label(AppStrings.Common_Name), 0, 0);
        Place(editor, _name, 1, 0);
        Place(editor, Label(AppStrings.HotTable_Keys), 0, 2);
        Place(editor, _keys, 1, 2);
        Place(editor, Label(AppStrings.HotTable_Folder), 3, 0, span: 3);
        var pathRow = new DockPanel();
        if (_panelFolder is not null)
        {
            var here = new Button { Content = AppStrings.HotTable_ThisPanel, Padding = new Thickness(12, 4), Margin = new Thickness(8, 0, 0, 0) };
            here.Click += (_, _) => UseFolder(_panelFolder());
            DockPanel.SetDock(here, Dock.Right);
            pathRow.Children.Add(here);
        }

        pathRow.Children.Add(_path);
        Place(editor, pathRow, 4, 0, span: 3);

        _up = ActionButton(AppStrings.HotTable_MoveUp, () => Move(-1));
        _down = ActionButton(AppStrings.HotTable_MoveDown, () => Move(1));
        _clear = ActionButton(AppStrings.Common_Clear, Clear);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _up, _down, _clear } };

        Children.Add(header);
        Children.Add(_list);
        Children.Add(editor);
        Children.Add(tools);

        Refresh();
        _list.SelectedIndex = 0;
    }

    internal ListBox List => _list;

    internal TextBox NameBox => _name;

    internal TextBox PathBox => _path;

    private int Slot => _list.SelectedIndex + 1;

    private HotPathSettings HotPaths => _settings.Current.HotPaths;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _settings.Changed += OnSettingsChanged;
        Refresh();
        FillEditor();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Old.HotPaths, e.New.HotPaths) || e.Old.Layout.Bars.HotPathBar != e.New.Layout.Bars.HotPathBar)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        _refreshing = true;
        for (var slot = 1; slot <= HotPathSettings.SlotCount; slot++)
        {
            var hp = HotPaths[slot];
            var (name, path, strip, menu) = _rows[slot - 1];
            name.Text = hp?.Name ?? AppStrings.Hot_Empty_Slot;
            name.FontStyle = hp is null ? FontStyle.Italic : FontStyle.Normal;
            name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable(hp is null ? "Brush.TertiaryText" : "Brush.Text"));
            path.Text = hp?.Path ?? "";
            strip.IsVisible = menu.IsVisible = hp is not null;
            strip.IsEnabled = _settings.Current.Layout.Bars.HotPathBar;
            strip.IsChecked = hp?.InStrip ?? false;
            menu.IsChecked = hp?.InMenu ?? false;
        }

        _refreshing = false;
        UpdateTools();
    }

    private void FillEditor()
    {
        if (_list.SelectedIndex < 0)
        {
            return;
        }

        _refreshing = true;
        var hp = HotPaths[Slot];
        _name.Text = hp?.Name ?? "";
        _path.Text = hp?.Path ?? "";
        _keys.Text = HotPathSettings.KeyOf(Slot) is { } key
            ? Loc.Format(AppStrings.HotTable_KeyHelp, key, Slot % 10)
            : AppStrings.HotTable_NoKey;
        _refreshing = false;
        UpdateTools();
    }

    private void UpdateTools()
    {
        var hp = HotPaths[Slot];
        _up.IsEnabled = Slot > 1 && (hp is not null || HotPaths[Slot - 1] is not null);
        _down.IsEnabled = Slot < HotPathSettings.SlotCount && (hp is not null || HotPaths[Slot + 1] is not null);
        _clear.IsEnabled = hp is not null;
    }

    private void EditName()
    {
        if (_refreshing || HotPaths[Slot] is not { } hp)
        {
            return;
        }

        Change(Slot, _ => hp with { Name = _name.Text ?? "" }, normalizeName: false);
    }

    private void EditPath()
    {
        var path = (_path.Text ?? "").Trim();
        if (_refreshing || path.Length == 0)
        {
            return;
        }

        var slot = Slot;
        var name = string.IsNullOrWhiteSpace(_name.Text) ? HotPathSettings.DefaultName(HotPathSettings.Expand(path)) : _name.Text.Trim();
        var hp = HotPaths[slot] is { } old ? old with { Path = path } : new HotPath(slot, name, path);
        _settings.Update(s => s with { HotPaths = s.HotPaths.With(hp) });
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            _refreshing = true;
            _name.Text = name;
            _refreshing = false;
        }
    }

    private void UseFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        if (HotPaths[Slot] is null || string.IsNullOrWhiteSpace(_name.Text))
        {
            _refreshing = true;
            _name.Text = HotPathSettings.DefaultName(folder);
            _refreshing = false;
        }

        _path.Text = folder;
    }

    private void Change(int slot, Func<HotPath, HotPath> change, bool normalizeName = true)
    {
        if (_refreshing || HotPaths[slot] is not { } hp)
        {
            return;
        }

        var changed = change(hp);
        if (normalizeName && string.IsNullOrWhiteSpace(changed.Name))
        {
            changed = changed with { Name = HotPathSettings.DefaultName(changed.Path) };
        }

        _settings.Update(s => s with { HotPaths = s.HotPaths.With(changed) });
    }

    private void Move(int by)
    {
        var from = Slot;
        var to = from + by;
        _settings.Update(s => s with { HotPaths = s.HotPaths.Swap(from, to) });
        _list.SelectedIndex = to - 1;
        _list.ScrollIntoView(_list.SelectedIndex);
    }

    private void Clear()
    {
        var slot = Slot;
        _settings.Update(s => s with { HotPaths = s.HotPaths.Without(slot) });
        FillEditor();
    }

    private static Grid RowGrid() => new() { ColumnDefinitions = new ColumnDefinitions("24,64,150,*,48,48"), ColumnSpacing = 8, Height = 28 };

    private static void AddCells(Grid grid, params Control[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }
    }

    private static void Place(Grid grid, Control control, int row, int column, int span = 1)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, span);
        grid.Children.Add(control);
    }

    private static TextBlock Head(string text, bool right = false, bool center = false)
    {
        var head = Text(text, 12, FontWeight.SemiBold, "Brush.SecondaryText");
        head.TextAlignment = right ? TextAlignment.Right : center ? TextAlignment.Center : TextAlignment.Left;
        return head;
    }

    private static TextBlock Label(string text)
    {
        var label = Text(text, 12, FontWeight.SemiBold, "Brush.SecondaryText");
        label.Margin = new Thickness(0, 0, 0, 4);
        return label;
    }

    private CheckBox Box(string name, Action<bool> changed)
    {
        var box = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 0, MinHeight = 0, VerticalAlignment = VerticalAlignment.Center, Padding = default };
        Avalonia.Automation.AutomationProperties.SetName(box, name);
        box.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                changed(box.IsChecked == true);
            }
        };
        return box;
    }

    internal static Border Kbd(string key) => new Border
    {
        Padding = new Thickness(5, 0),
        CornerRadius = new CornerRadius(3),
        BorderThickness = new Thickness(1),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Child = Text(key, 11, brush: "Brush.SecondaryText"),
    }.Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.WindowBackground");
}
