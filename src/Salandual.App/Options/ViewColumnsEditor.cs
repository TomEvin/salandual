// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/ViewColumnsEditor.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

internal sealed class ViewColumnsEditor : Grid
{
    private static readonly PanelView[] _views = [PanelView.Detailed, PanelView.Types, PanelView.Own1, PanelView.Own2, PanelView.Own3];

    private readonly SettingsService _settings;
    private readonly ListBox _list = new() { Background = Brushes.Transparent, MinWidth = 200 };
    private readonly StackPanel _detail = new() { Spacing = 10 };
    private PanelView _view = PanelView.Detailed;

    public ViewColumnsEditor(SettingsService settings)
    {
        _settings = settings;
        RowDefinitions = new RowDefinitions("Auto,Auto");
        RowSpacing = 12;
        _list.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new WrapPanel());
        var listBorder = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Top, Child = _list }
            .Res(Border.BorderBrushProperty, "Brush.Border");
        Children.Add(listBorder);
        SetRow(_detail, 1);
        Children.Add(_detail);
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedIndex >= 0 && _views[_list.SelectedIndex] != _view)
            {
                _view = _views[_list.SelectedIndex];
                ShowDetail();
            }
        };
        FillList();
        ShowDetail();
    }

    internal void Select(PanelView view) => _list.SelectedIndex = Array.IndexOf(_views, view);

    private IReadOnlyList<ColumnView> Kept => _settings.Current.Panels.ColumnViews;

    private ColumnView Setup => ColumnViews.For(Kept, _view);

    private void FillList()
    {
        var selected = Array.IndexOf(_views, _view);
        _list.Items.Clear();
        foreach (var view in _views)
        {
            var name = PanelViews.IsOwn(view)
                ? ColumnViews.IsMade(Kept, view) ? ColumnViews.For(Kept, view).Name : AppStrings.Views_Empty
                : DirectoryLine.ViewName(view);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(Text(name, 13, brush: PanelViews.IsOwn(view) && !ColumnViews.IsMade(Kept, view) ? "Brush.TertiaryText" : "Brush.Text"));
            var key = Text(Loc(AppStrings.Views_Key, PanelViews.Command(view)[^1]), 12, brush: "Brush.TertiaryText");
            SetColumn(key, 1);
            row.Children.Add(key);
            _list.Items.Add(new ListBoxItem { Content = row, Padding = new Thickness(8, 5) });
        }

        _list.SelectedIndex = selected;
    }

    private static string Loc(string format, char digit) => string.Format(CultureInfo.CurrentCulture, format, digit);

    private void Change(Func<ColumnView, ColumnView> change)
    {
        var view = _view;
        _settings.Update(s => s with { Panels = s.Panels with { ColumnViews = ColumnViews.With(s.Panels.ColumnViews, change(ColumnViews.For(s.Panels.ColumnViews, view))) } });
        ShowDetail();
    }

    private void ShowDetail()
    {
        _detail.Children.Clear();
        var setup = Setup;
        var own = PanelViews.IsOwn(_view);
        var made = !own || ColumnViews.IsMade(Kept, _view);
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        if (own)
        {
            var name = new TextBox { Text = setup.Name, Width = 180, PlaceholderText = AppStrings.Views_NamePlaceholder };
            Avalonia.Automation.AutomationProperties.SetName(name, AppStrings.Views_Name);
            name.LostFocus += (_, _) => Rename(name.Text);
            name.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter)
                {
                    Rename(name.Text);
                    e.Handled = true;
                }
            };
            bar.Children.Add(name);
            bar.Children.Add(Secondary(AppStrings.Views_StartFrom, 13));
            bar.Children.Add(Combo<PanelView?>([(null, AppStrings.Views_StartFromPick), (PanelView.Detailed, DirectoryLine.ViewName(PanelView.Detailed)), (PanelView.Types, DirectoryLine.ViewName(PanelView.Types))],
                null, from =>
                {
                    if (from is { } v)
                    {
                        Change(now => ColumnViews.For(Kept, v) with { View = _view, Name = now.Name.Length > 0 ? now.Name : DefaultName() });
                        FillList();
                    }
                }, 140));
            bar.Children.Add(ActionButton(AppStrings.Views_Delete, () =>
            {
                var view = _view;
                _settings.Update(s => s with { Panels = s.Panels with { ColumnViews = ColumnViews.Without(s.Panels.ColumnViews, view) } });
                FillList();
                ShowDetail();
            }, enabled: made));
        }
        else
        {
            bar.Children.Add(ActionButton(AppStrings.Views_Default, () =>
            {
                var view = _view;
                _settings.Update(s => s with { Panels = s.Panels with { ColumnViews = ColumnViews.Without(s.Panels.ColumnViews, view) } });
                ShowDetail();
            }));
        }

        var smartName = new CheckBox { Content = AppStrings.Columns_SmartName, IsChecked = setup.SmartName, IsEnabled = made };
        smartName.IsCheckedChanged += (_, _) => Change(v => v with { SmartName = smartName.IsChecked == true });
        bar.Children.Add(smartName);
        bar.Children.Add(ActionButton(AppStrings.Views_FitAll, () => Change(v => v with { Columns = [.. v.Columns.Select(c => c with { Width = 0 })], SmartName = true }), enabled: made));
        _detail.Children.Add(bar);
        if (own && !made)
        {
            _detail.Children.Add(Secondary(AppStrings.Views_MakeHint));
        }

        _detail.Children.Add(ColumnTable(setup, made));
    }

    private string DefaultName() => Loc(AppStrings.Views_DefaultName, PanelViews.Command(_view)[^1]);

    private void Rename(string? text)
    {
        var name = (text ?? "").Trim();
        if (name.Length == 0 || name == Setup.Name)
        {
            return;
        }

        Change(v => v with { Name = name });
        FillList();
    }

    private Border ColumnTable(ColumnView setup, bool enabled)
    {
        var rows = new StackPanel { IsEnabled = enabled };
        Grid.SetIsSharedSizeScope(rows, true);
        rows.Children.Add(Row(Secondary(AppStrings.Views_ColumnHead, 11), Secondary(AppStrings.Views_WidthHead, 11), null, null, header: true));
        for (var i = 0; i < setup.Columns.Count; i++)
        {
            var c = setup.Columns[i];
            var column = c.Column;
            var tick = new CheckBox { IsChecked = c.Shown, IsEnabled = column != PanelColumn.Name, MinWidth = 0, Padding = new Thickness(0) };
            Avalonia.Automation.AutomationProperties.SetName(tick, ColumnNames.Of(column));
            tick.IsCheckedChanged += (_, _) =>
            {
                if (tick.IsChecked != ColumnViews.For(Kept, _view).Columns.First(x => x.Column == column).Shown)
                {
                    Change(v => ColumnViews.Toggle(v, column));
                }
            };
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { tick, Text(ColumnNames.Of(column), 13) } };
            if (column == PanelColumn.Name)
            {
                label.Children.Add(Secondary(AppStrings.Columns_Always, 12));
            }

            Control width;
            if (column == PanelColumn.Name)
            {
                width = WidthBox(setup.SmartName ? 0 : setup.NameWidth, w => Change(v => w > 0 ? ColumnViews.SetWidth(v, PanelColumn.Name, w) : v with { SmartName = true }));
                width.IsEnabled = !setup.SmartName;
            }
            else
            {
                width = WidthBox(c.Width, w => Change(v => ColumnViews.SetWidth(v, column, w)));
            }

            var up = Arrow("Icon.ArrowUp", AppStrings.Views_MoveUp, () => Change(v => ColumnViews.Move(v, column, -1)), column != PanelColumn.Name && i > 1);
            var down = Arrow("Icon.ArrowDown", AppStrings.Views_MoveDown, () => Change(v => ColumnViews.Move(v, column, 1)), column != PanelColumn.Name && i < setup.Columns.Count - 1);
            rows.Children.Add(Row(label, width, up, down, header: false));
        }

        return new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = rows, ClipToBounds = true }.Res(Border.BorderBrushProperty, "Brush.Border");
    }

    private static Grid Row(Control label, Control width, Control? up, Control? down, bool header)
    {
        var grid = new Grid { ColumnSpacing = 6, Margin = new Thickness(10, header ? 4 : 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        foreach (var group in new[] { "w", "u", "d" })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = group });
        }

        grid.Children.Add(label);
        SetColumn(width, 1);
        grid.Children.Add(width);
        if (up is not null && down is not null)
        {
            SetColumn(up, 2);
            SetColumn(down, 3);
            grid.Children.Add(up);
            grid.Children.Add(down);
        }

        return grid;
    }

    private static TextBox WidthBox(int width, Action<int> changed)
    {
        var box = new TextBox { Text = width > 0 ? width.ToString(CultureInfo.CurrentCulture) : "", Width = 80, PlaceholderText = AppStrings.Columns_Fits, HorizontalAlignment = HorizontalAlignment.Left };
        Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Views_WidthHead);
        void Take()
        {
            var text = (box.Text ?? "").Trim();
            var value = text.Length == 0 ? 0 : int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var n) ? n : -1;
            if (value >= 0 && value != width)
            {
                changed(value);
            }
        }

        box.LostFocus += (_, _) => Take();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                Take();
                e.Handled = true;
            }
        };
        return box;
    }

    private static Button Arrow(string icon, string tip, Action click, bool enabled)
    {
        var button = new Button { Content = AppIcons.Create(icon), Padding = new Thickness(6, 4), IsEnabled = enabled };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
    }
}
