// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.KeyMap.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Dialogs;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Commands;
using Salandual.Core.Resources;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public partial class OptionsWindow
{
    private (string Text, Action Undo)? _keyMapNotice;

    internal IReadOnlyList<Border> KeyMapCards { get; private set; } = [];

    internal TextBlock? KeyMapNoticeText { get; private set; }

    internal Button? KeyMapNoticeUndo { get; private set; }

    private static readonly KeyMapName[] _keyMaps = [KeyMapName.Salamander, KeyMapName.TotalCommander];

    private Control KeyMapChoice()
    {
        var commands = _services.Commands;
        var cards = new UniformGrid { Columns = 2, ColumnSpacing = 10, Margin = new Thickness(0, 4, 0, 0) };
        var list = new List<Border>();
        foreach (var name in _keyMaps)
        {
            var card = KeyMapCard(commands, name);
            list.Add(card);
            cards.Children.Add(card);
        }

        KeyMapCards = list;
        var panel = new StackPanel { Spacing = 10, Children = { cards } };
        if (KeyMapNotice() is { } notice)
        {
            panel.Children.Add(notice);
        }

        return panel;
    }

    private Border KeyMapCard(CommandRegistry commands, KeyMapName name)
    {
        var active = commands.KeyMap == name;
        var (map, _) = commands.KeysOf(name);
        var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(active ? 4 : 1), VerticalAlignment = VerticalAlignment.Center }
            .Res(Border.BorderBrushProperty, active ? "Brush.Accent" : "Brush.SecondaryText");
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { dot, Text(KeyMaps.Title(name), 13, FontWeight.SemiBold) } };
        var description = name == KeyMapName.Salamander
            ? AppStrings.Opt_KeyMap_Salamander_Desc
            : Loc.Format(AppStrings.Opt_KeyMap_TotalCommander_Desc, commands.Differences(KeyMapName.Salamander, name).Count);

        var keys = new UniformGrid { Columns = 2, ColumnSpacing = 4, RowSpacing = 2, Margin = new Thickness(0, 6, 0, 0) };
        for (var n = 1; n <= 10; n++)
        {
            var chord = new KeyChord(Core.Commands.KeyModifiers.None, "F" + n.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var id = map.TryGetValue(chord, out var found) ? found : null;
            var built = id is not null && MainWindow.IsBuilt(id);
            var label = id is null ? "—" : commands[id].ShortLabel ?? commands[id].Title;
            var line = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            line.Inlines!.Add(new Avalonia.Controls.Documents.Run(n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ") { FontWeight = FontWeight.SemiBold });
            line.Inlines.Add(new Avalonia.Controls.Documents.Run(label));
            line.Res(TextBlock.ForegroundProperty, built ? "Brush.Text" : "Brush.TertiaryText");
            keys.Children.Add(new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(4, 1), Child = line }
                .Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.WindowBackground"));
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(active ? 2 : 1),
            Padding = new Thickness(active ? 11 : 12, active ? 9 : 10),
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true,
            Tag = name,
            Child = new StackPanel { Spacing = 2, Children = { title, Secondary(description, 12), keys } },
        }.Res(Border.BorderBrushProperty, active ? "Brush.Accent" : "Brush.BorderStrong").Res(Border.BackgroundProperty, "Brush.PanelBackground");
        Avalonia.Automation.AutomationProperties.SetName(card, KeyMaps.Title(name));
        card.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                _ = ChooseKeyMapAsync(name);
            }
        };
        card.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)
            {
                e.Handled = true;
                _ = ChooseKeyMapAsync(name);
            }
        };
        return card;
    }

    private async Task ChooseKeyMapAsync(KeyMapName name)
    {
        if (_services.Commands.KeyMap == name)
        {
            return;
        }

        var dialog = SettingsTransferDialog.ForKeyMap(_services.Commands, name, Current, MainWindow.IsBuilt);
        if (await dialog.ShowDialog<object?>(this) is IReadOnlySet<string> ticked)
        {
            UseKeyMap(name, ticked);
        }
    }

    internal void UseKeyMap(KeyMapName name, IReadOnlySet<string> ticked)
    {
        var before = Current;
        var changed = _services.Commands.Differences(_services.Commands.KeyMap, name).Count;
        var (quick, right) = SettingsTransferDialog.GoesWith(name, before);
        var with = new List<string>();
        Settings.Update(s =>
        {
            s = s with { Keyboard = s.Keyboard with { KeyMap = name } };
            if (ticked.Contains(SettingsTransferDialog.QuickSearchTick) && s.Panels.QuickSearch != quick)
            {
                s = s with { Panels = s.Panels with { QuickSearch = quick } };
                with.Add(Loc.Format(AppStrings.KeyMapDlg_QuickSearch, SettingsTransferDialog.QuickSearchName(quick)));
            }

            if (ticked.Contains(SettingsTransferDialog.RightButtonTick) && s.Mouse.RightClickUnselected != right)
            {
                s = s with { Mouse = s.Mouse with { RightClickUnselected = right } };
                with.Add(Loc.Format(AppStrings.KeyMapDlg_RightClick, SettingsTransferDialog.RightClickName(right)));
            }

            return s;
        });
        _services.Commands.UseKeyMap(name);
        var title = KeyMaps.Title(name);
        var text = with.Count == 0
            ? Loc.Format(AppStrings.Opt_KeyMap_Done, title, changed)
            : Loc.Format(AppStrings.Opt_KeyMap_DoneWith, title, changed, string.Join(" " + AppStrings.Opt_KeyMap_And + " ", with));
        _keyMapNotice = (text, () =>
        {
            Settings.Update(s => s with
            {
                Keyboard = s.Keyboard with { KeyMap = before.Keyboard.KeyMap },
                Panels = s.Panels with { QuickSearch = before.Panels.QuickSearch },
                Mouse = s.Mouse with { RightClickUnselected = before.Mouse.RightClickUnselected },
            });
            _services.Commands.UseKeyMap(before.Keyboard.KeyMap);
        });
        ShowCategory("keyboard");
    }

    private Control? KeyMapNotice()
    {
        KeyMapNoticeText = null;
        KeyMapNoticeUndo = null;
        if (_keyMapNotice is not var (notice, undo))
        {
            return null;
        }

        var text = Text(notice, 12.5, wrap: true);
        text.VerticalAlignment = VerticalAlignment.Center;
        KeyMapNoticeText = text;
        KeyMapNoticeUndo = ActionButton(AppStrings.Common_Undo, () =>
        {
            _keyMapNotice = null;
            undo();
            ShowCategory("keyboard");
        });
        var close = new Button { Content = AppIcons.Glyph("Icon.Cross", 10, 10, 1.4), Width = 28, Height = 28, Padding = default, Background = Brushes.Transparent, BorderThickness = default };
        ToolTip.SetTip(close, AppStrings.Common_Close);
        Avalonia.Automation.AutomationProperties.SetName(close, AppStrings.Common_Close);
        close.Click += (_, _) =>
        {
            _keyMapNotice = null;
            ShowCategory("keyboard");
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { KeyMapNoticeUndo, close } };
        var line = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        line.Children.Add(buttons);
        line.Children.Add(text);
        return new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 4, 4, 4), Child = line }.Res(Border.BackgroundProperty, "Brush.InfoBackground");
    }
}
