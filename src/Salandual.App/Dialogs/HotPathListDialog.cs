// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/HotPathListDialog.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Dialogs;

public enum HotPathListAction { Go, GoOther, Edit, Options }

public sealed record HotPathListResult(HotPathListAction Action, int Slot = 0);

public sealed class HotPathListDialog : Window
{
    private readonly SettingsService _settings;
    private readonly Action<HotPath>? _cleared;
    private readonly TextBox _filter;
    private readonly ListBox _list;
    private readonly TextBlock _count;
    private readonly TextBlock _empty;
    private List<HotPath> _rows = [];

    public HotPathListDialog(SettingsService settings, Action<HotPath>? cleared = null)
    {
        _settings = settings;
        _cleared = cleared;
        Title = AppStrings.Copy_Group_HotPaths;
        Width = 640;
        Height = 420;
        MinWidth = 420;
        MinHeight = 240;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        _filter = new TextBox { PlaceholderText = AppStrings.HotList_FilterHint, MinWidth = 220, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(_filter, AppStrings.HotList_Filter);
        _filter.TextChanged += (_, _) => Fill(0);
        _filter.AddHandler(KeyDownEvent, OnFilterKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _count = OptionsUi.Text("", 12, brush: "Brush.TertiaryText");
        _count.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel { Margin = new Thickness(12, 4, 16, 10) };
        _count.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(_count, Dock.Right);
        head.Children.Add(_count);
        head.Children.Add(_filter);

        _list = new ListBox { Background = Brushes.Transparent, Padding = new Thickness(4) };
        Avalonia.Automation.AutomationProperties.SetName(_list, AppStrings.Copy_Group_HotPaths);
        _list.DoubleTapped += (_, _) => Finish(HotPathListAction.Go);
        _list.AddHandler(KeyDownEvent, OnFilterKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _empty = OptionsUi.Text("", 13, brush: "Brush.SecondaryText");
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.Margin = new Thickness(0, 24);
        var body = new Panel { Children = { _list, _empty } };

        var edit = new Button { Content = AppStrings.HotList_EditAll, Padding = new Thickness(12, 4) };
        edit.Click += (_, _) => Close(new HotPathListResult(HotPathListAction.Options));
        var hints = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (key, text) in new[] { ("Enter", AppStrings.Cd_Hint_Go), ("Ctrl+Enter", AppStrings.HotList_Hint_Other), ("F2", AppStrings.HotList_Hint_Edit), ("Del", AppStrings.HotList_Hint_Clear), ("Esc", AppStrings.HotList_Hint_Close) })
        {
            hints.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Margin = new Thickness(0, 2, 14, 2),
                Children = { Kbd(key), OptionsUi.Text(text, 12, brush: "Brush.SecondaryText") },
            });
        }

        var foot = new DockPanel();
        DockPanel.SetDock(edit, Dock.Right);
        foot.Children.Add(edit);
        foot.Children.Add(hints);
        var footer = new Border
        {
            Padding = new Thickness(16, 8),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = foot,
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var headBorder = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = head }.Res(Border.BorderBrushProperty, "Brush.Border");
        var root = new DockPanel();
        DockPanel.SetDock(headBorder, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(headBorder);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = Controls.TitleBarParts.ForDialog(this, root);

        Fill(0);
        Opened += (_, _) => _filter.Focus();
    }

    internal TextBox FilterBox => _filter;

    internal IReadOnlyList<HotPath> Rows => _rows;

    internal int SelectedIndex => _list.SelectedIndex;

    private static Border Kbd(string key) => new Border
    {
        Padding = new Thickness(5, 0),
        CornerRadius = new CornerRadius(3),
        BorderThickness = new Thickness(1),
        Child = OptionsUi.Text(key, 11, brush: "Brush.SecondaryText"),
    }.Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.PanelBackground");

    internal static List<HotPath> Filter(IEnumerable<HotPath> paths, string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return [.. paths];
        }

        var number = int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
        return [.. paths.Where(p => (number is { } slot && (p.Slot == slot || (query.Length == 1 && p.Slot == HotPathSettings.SlotOfDigit(slot))))
            || p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || p.Path.Contains(query, StringComparison.OrdinalIgnoreCase))];
    }

    private void Fill(int select)
    {
        var all = _settings.Current.HotPaths.Paths;
        _rows = Filter(all, _filter.Text ?? "");
        _count.Text = Loc.Format(AppStrings.Common_XOfY, all.Count, HotPathSettings.SlotCount);
        _list.Items.Clear();
        foreach (var hp in _rows)
        {
            _list.Items.Add(new ListBoxItem { Content = RowOf(hp), Padding = new Thickness(8, 5) });
        }

        _list.SelectedIndex = _rows.Count == 0 ? -1 : Math.Clamp(select, 0, _rows.Count - 1);
        _empty.IsVisible = _rows.Count == 0;
        _empty.Text = all.Count == 0
            ? AppStrings.HotList_None
            : Loc.Format(AppStrings.HotList_NoMatch, _filter.Text);
    }

    private static Control RowOf(HotPath hp)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,64,170,*") };
        var number = OptionsUi.Text(hp.Slot.ToString(CultureInfo.InvariantCulture), 13, brush: "Brush.TertiaryText");
        number.TextAlignment = TextAlignment.Right;
        number.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(number);
        if (HotPathSettings.KeyOf(hp.Slot) is { } key)
        {
            var kbd = Kbd(key);
            kbd.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(kbd, 1);
            grid.Children.Add(kbd);
        }

        var name = OptionsUi.Text(hp.Name, 13);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(0, 0, 16, 0);
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);
        var path = OptionsUi.Text(hp.Path, 12, brush: "Brush.SecondaryText");
        path.TextTrimming = TextTrimming.CharacterEllipsis;
        path.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(path, 3);
        grid.Children.Add(path);
        return grid;
    }

    private void OnFilterKey(object? sender, KeyEventArgs e)
    {
        var index = _list.SelectedIndex;
        switch (e.Key)
        {
            case Key.Down:
                _list.SelectedIndex = Math.Min(index + 1, _rows.Count - 1);
                break;
            case Key.Up:
                _list.SelectedIndex = Math.Max(index - 1, 0);
                break;
            case Key.Enter:
                Finish(e.KeyModifiers.HasFlag(KeyModifiers.Control) ? HotPathListAction.GoOther : HotPathListAction.Go);
                break;
            case Key.F2:
                Finish(HotPathListAction.Edit);
                break;
            case Key.Delete when string.IsNullOrEmpty(_filter.Text) && index >= 0:
                var removed = _rows[index];
                _settings.Update(s => s with { HotPaths = s.HotPaths.Without(removed.Slot) });
                Fill(index);
                _cleared?.Invoke(removed);
                break;
            case Key.Escape:
                Close(null);
                break;
            default:
                return;
        }

        if (_list.SelectedIndex >= 0)
        {
            _list.ScrollIntoView(_list.SelectedIndex);
        }

        e.Handled = true;
        if (!ReferenceEquals(sender, _filter))
        {
            _filter.Focus();
        }
    }

    private void Finish(HotPathListAction action)
    {
        if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _rows.Count)
        {
            Close(new HotPathListResult(action, _rows[_list.SelectedIndex].Slot));
        }
    }
}
