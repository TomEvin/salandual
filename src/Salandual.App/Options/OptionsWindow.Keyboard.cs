// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Keyboard.cs

using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Input;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.Resources;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public partial class OptionsWindow
{
    private Border? _keyBox;
    private IClipboardAccess? _textClipboard;
    private Action<string>? _viewFile;

    internal IClipboardAccess? TextClipboard
    {
        get => _textClipboard;
        set
        {
            _textClipboard = value;
            CheatSheetHooksChanged();
        }
    }

    internal Action<string>? ViewFile
    {
        get => _viewFile;
        set
        {
            _viewFile = value;
            CheatSheetHooksChanged();
        }
    }

    private void CheatSheetHooksChanged()
    {
        if (_category == "keyboard" && !IsSearching)
        {
            Rebuild();
        }
    }

    internal TextBlock? KeyAnswer { get; private set; }

    internal Border? KeyBox => _keyBox;

    internal TextBlock? CheatSheetNote { get; private set; }

    private void AddKeyboard()
    {
        const string page = "keyboard";
        _services.Commands.UseKeyMap(Current.Keyboard.KeyMap);
        Add(page, "", AppStrings.Opt_KeyMap, AppStrings.Opt_KeyMap_Desc, () => new Panel(), keywords: AppStrings.Opt_KeyMap_Kw, below: KeyMapChoice);
        Add(page, "", AppStrings.Opt_WhichKey, AppStrings.Opt_WhichKey_Desc, WhichKey, keywords: AppStrings.Opt_WhichKey_Kw);
        Add(page, "", AppStrings.Opt_CheatSheet, AppStrings.Opt_CheatSheet_Desc, CheatSheetButtons, keywords: AppStrings.Opt_CheatSheet_Kw);
        Add(page, AppStrings.OptSec_AllCommands, null, null, ShortcutList, wide: true);

        var (tc, _) = _services.Commands.KeysOf(KeyMapName.TotalCommander);
        foreach (var command in _services.Commands.Commands)
        {
            var keys = string.Join(" ", command.DefaultChords.Concat(tc.Where(p => p.Value == command.Id).Select(p => p.Key)).Distinct());
            Add(page, "", command.Title, CategoryLine(command), () =>
            {
                var chords = string.Join("  ", _services.Commands.ChordsFor(command.Id));
                return Text(chords.Length > 0 ? chords : "—", 13, FontWeight.SemiBold);
            }, keywords: keys + " " + AppStrings.Opt_Shortcut_Kw + " " + command.Id, searchOnly: true);
        }
    }

    private string? WasKeys(CommandDefinition command, IReadOnlyList<KeyChord> chords)
        => _services.Commands.KeyMap == KeyMapName.Salamander || command.DefaultChords.SequenceEqual(chords)
            ? null
            : Loc.Format(AppStrings.Opt_KeyMap_Was, command.DefaultChords.Count > 0 ? string.Join("  ", command.DefaultChords) : "—");

    private static string CategoryLine(CommandDefinition command)
        => MainWindow.IsBuilt(command.Id)
            ? CommandText.Category(command.Category)
            : Loc.Format(AppStrings.Opt_CategoryNotYet, CommandText.Category(command.Category), CommandText.Milestone(command.PlannedFor));

    private IEnumerable<(string Category, List<CommandDefinition> Commands)> CommandGroups()
        => _services.Commands.Commands
            .GroupBy(c => c.Category)
            .Select(g => (g.Key, g.OrderBy(c => c.Title, StringComparer.CurrentCulture).ToList()));

    private Control WhichKey()
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var hint = Text(AppStrings.Opt_WhichKey_Press, 13, brush: "Brush.TertiaryText");
        hint.VerticalAlignment = VerticalAlignment.Center;
        keys.Children.Add(hint);
        var box = new Border
        {
            Focusable = true,
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Right,
            Height = 30,
            Padding = new Thickness(6, 0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            Child = keys,
            Cursor = new Cursor(StandardCursorType.Ibeam),
        }.Res(Border.BorderBrushProperty, "Brush.BorderStrong").Res(Border.BackgroundProperty, "Brush.PanelBackground");
        Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Opt_WhichKey);
        box.GotFocus += (_, _) => box.Res(Border.BorderBrushProperty, "Brush.Accent");
        box.LostFocus += (_, _) => box.Res(Border.BorderBrushProperty, "Brush.BorderStrong");
        box.PointerPressed += (_, e) =>
        {
            box.Focus();
            e.Handled = true;
        };
        var answer = Text("", 13, brush: "Brush.SecondaryText", wrap: true);
        answer.MaxWidth = 360;
        answer.MinHeight = 18;
        answer.TextAlignment = TextAlignment.Right;
        answer.HorizontalAlignment = HorizontalAlignment.Right;
        box.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers == Avalonia.Input.KeyModifiers.None && e.Key is Key.Tab or Key.Escape
                || !KeyMapping.TryGetChord(e.Key, e.KeyModifiers, out var chord))
            {
                return;
            }

            e.Handled = true;
            keys.Children.Clear();
            foreach (var part in Parts(chord))
            {
                keys.Children.Add(KeyCap(part));
            }

            answer.Text = Answer(chord);
        };
        _keyBox = box;
        KeyAnswer = answer;
        return new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, Children = { box, answer } };
    }

    private static IEnumerable<string> Parts(KeyChord chord)
    {
        if ((chord.Modifiers & Core.Commands.KeyModifiers.Ctrl) != 0)
        {
            yield return "Ctrl";
        }

        if ((chord.Modifiers & Core.Commands.KeyModifiers.Alt) != 0)
        {
            yield return "Alt";
        }

        if ((chord.Modifiers & Core.Commands.KeyModifiers.Shift) != 0)
        {
            yield return "Shift";
        }

        yield return chord.Key;
    }

    internal string Answer(KeyChord chord)
    {
        if (_services.Commands.NotYet(chord) is { } notYet)
        {
            return Loc.Format(AppStrings.Opt_WhichKey_NotYet, notYet.Title, KeyMaps.Title(_services.Commands.KeyMap));
        }

        if (_services.Commands.Resolve(chord) is not { } id || !_services.Commands.TryGet(id, out var command))
        {
            return AppStrings.Opt_WhichKey_Free;
        }

        var group = CommandText.Category(command.Category);
        return MainWindow.IsBuilt(id)
            ? Loc.Format(AppStrings.Opt_WhichKey_Answer, command.Title, group)
            : Loc.Format(AppStrings.Opt_WhichKey_NotYet, command.Title, group);
    }

    private Control CheatSheetButtons()
    {
        var note = Text("", 12, brush: "Brush.SecondaryText");
        note.VerticalAlignment = VerticalAlignment.Center;
        note.IsVisible = false;
        CheatSheetNote = note;
        var copy = ActionButton(AppStrings.Common_Copy, () =>
        {
            var copied = TextClipboard!.SetText(CheatSheet());
            note.Text = copied ? AppStrings.Opt_CheatSheet_Copied : AppStrings.Opt_CheatSheet_Busy;
            note.IsVisible = true;
        }, enabled: TextClipboard is not null);
        var view = ActionButton(AppStrings.Opt_CheatSheet_View, () =>
        {
            try
            {
                var file = Path.Combine(_services.Temp.NewFolder("keys"), AppStrings.Opt_CheatSheet_FileName);
                File.WriteAllText(file, CheatSheet(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                ViewFile!(file);
                note.IsVisible = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                note.Text = Loc.Format(AppStrings.Opt_CheatSheet_Failed, ex.Message);
                note.IsVisible = true;
            }
        }, enabled: ViewFile is not null);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { note, copy, view } };
    }

    internal string CheatSheet()
    {
        var commands = _services.Commands;
        var width = commands.Commands.Max(c => c.Title.Length) + 2;
        var sb = new StringBuilder();
        sb.Append(Loc.Format(AppStrings.Opt_CheatSheet_Heading, Core.ProductInfo.Version, KeyMaps.Title(commands.KeyMap))).Append("\r\n");
        foreach (var group in CommandGroups())
        {
            sb.Append("\r\n").Append(CommandText.Category(group.Category)).Append("\r\n");
            foreach (var command in group.Commands)
            {
                var chords = string.Join("  ", commands.ChordsFor(command.Id));
                sb.Append("  ").Append(command.Title.PadRight(width)).Append(chords.Length > 0 ? chords : "—");
                if (!MainWindow.IsBuilt(command.Id))
                {
                    sb.Append("   (").Append(Loc.Format(AppStrings.Opt_NotYet, CommandText.Milestone(command.PlannedFor))).Append(')');
                }

                sb.Append("\r\n");
            }
        }

        if (commands.NotYetKeys.Count > 0)
        {
            sb.Append("\r\n").Append(Loc.Format(AppStrings.Opt_KeyMap_NotYetGroup, KeyMaps.Title(commands.KeyMap))).Append("\r\n");
            foreach (var key in commands.NotYetKeys)
            {
                sb.Append("  ").Append(key.Title.PadRight(width)).Append(key.Chord).Append("\r\n");
            }
        }

        return sb.ToString();
    }

    private Control ShortcutList()
    {
        var filter = new TextBox { PlaceholderText = AppStrings.Opt_Shortcuts_Filter };
        filter.Classes.Add("clearButton");

        var count = Secondary("");
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(12, 0, 0, 0);
        var top = new DockPanel();
        DockPanel.SetDock(count, Dock.Right);
        top.Children.Add(count);
        top.Children.Add(filter);

        var list = new StackPanel();
        var groups = new List<(Control Heading, List<(Control Row, string Text, bool Changed)> Rows)>();
        var commands = _services.Commands;
        var total = 0;
        foreach (var group in CommandGroups())
        {
            var heading = new Border
            {
                BorderThickness = new Thickness(0, groups.Count == 0 ? 0 : 1, 0, 0),
                Padding = new Thickness(0, 10, 0, 4),
                Margin = new Thickness(0, groups.Count == 0 ? 0 : 6, 0, 0),
                Child = Text(CommandText.Category(group.Category), 12, FontWeight.SemiBold, "Brush.SecondaryText"),
            }.Res(Border.BorderBrushProperty, "Brush.Border");
            list.Children.Add(heading);
            var rows = new List<(Control Row, string Text, bool Changed)>();
            foreach (var command in group.Commands)
            {
                var built = MainWindow.IsBuilt(command.Id);
                var chords = commands.ChordsFor(command.Id).ToList();
                var was = WasKeys(command, chords);
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4) };
                var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(Text(command.Title, 13, brush: built ? "Brush.Text" : "Brush.TertiaryText"));
                if (!built)
                {
                    text.Children.Add(Text(Loc.Format(AppStrings.Opt_NotYet, CommandText.Milestone(command.PlannedFor)), 11, brush: "Brush.TertiaryText"));
                }

                grid.Children.Add(text);
                var keys = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 320 };
                foreach (var chord in chords)
                {
                    keys.Children.Add(KeyCap(chord.ToString()));
                }

                if (chords.Count == 0)
                {
                    keys.Children.Add(KeyCap("—"));
                }

                if (was is not null)
                {
                    var wasText = Text(was, 11, brush: "Brush.TertiaryText");
                    wasText.VerticalAlignment = VerticalAlignment.Center;
                    wasText.Margin = new Thickness(8, 0, 0, 0);
                    keys.Children.Add(wasText);
                }

                keys.Opacity = built ? 1 : 0.6;
                Grid.SetColumn(keys, 1);
                grid.Children.Add(keys);
                list.Children.Add(Changed(grid, was is not null));
                rows.Add((list.Children[^1], string.Join(' ', command.Title, CommandText.Category(command.Category), command.Id, string.Join(' ', chords)), was is not null));
            }

            total += rows.Count;
            groups.Add((heading, rows));
        }

        if (commands.NotYetKeys.Count > 0)
        {
            var heading = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 10, 0, 4),
                Margin = new Thickness(0, 6, 0, 0),
                Child = Text(Loc.Format(AppStrings.Opt_KeyMap_NotYetGroup, KeyMaps.Title(commands.KeyMap)), 12, FontWeight.SemiBold, "Brush.SecondaryText"),
            }.Res(Border.BorderBrushProperty, "Brush.Border");
            list.Children.Add(heading);
            var rows = new List<(Control Row, string Text, bool Changed)>();
            foreach (var key in commands.NotYetKeys)
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4) };
                var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(Text(key.Title, 13, brush: "Brush.TertiaryText"));
                text.Children.Add(Text(AppStrings.Opt_KeyMap_NotYetKey, 11, brush: "Brush.TertiaryText"));
                grid.Children.Add(text);
                var cap = KeyCap(key.Chord.ToString());
                cap.Opacity = 0.6;
                Grid.SetColumn(cap, 1);
                grid.Children.Add(cap);
                list.Children.Add(Changed(grid, true));
                rows.Add((list.Children[^1], string.Join(' ', key.Title, key.Chord), true));
            }

            total += rows.Count;
            groups.Add((heading, rows));
        }

        ToggleSwitch? onlyChanged = null;
        OnlyChangedKeys = null;
        if (commands.KeyMap != KeyMapName.Salamander)
        {
            onlyChanged = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            onlyChanged.IsCheckedChanged += (_, _) => Apply();
            OnlyChangedKeys = onlyChanged;
        }

        void Apply()
        {
            var q = filter.Text?.Trim() ?? "";
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            var only = onlyChanged?.IsChecked == true;
            var shown = 0;
            foreach (var (heading, rows) in groups)
            {
                var any = false;
                foreach (var (row, text, changed) in rows)
                {
                    row.IsVisible = (!only || changed) && (q.Length == 0 || compare.IndexOf(text, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
                    any |= row.IsVisible;
                    shown += row.IsVisible ? 1 : 0;
                }

                heading.IsVisible = any;
            }

            count.Text = q.Length == 0 && !only ? Loc.Format(AppStrings.Opt_Shortcuts_All, total) : Loc.Format(AppStrings.Opt_Shortcuts_Count, shown, total);
        }

        filter.TextChanged += (_, _) => Apply();
        Apply();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(top);
        if (onlyChanged is not null)
        {
            var label = Text(AppStrings.Opt_KeyMap_OnlyChanged, 13);
            label.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { onlyChanged, label } });
        }

        panel.Children.Add(list);
        return panel;
    }

    internal ToggleSwitch? OnlyChangedKeys { get; private set; }

    private static Control Changed(Control row, bool changed)
        => changed
            ? new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 0, 0, 0), Child = row }.Res(Border.BorderBrushProperty, "Brush.Accent")
            : row;


    private static Border KeyCap(string key) => new Border
    {
        CornerRadius = new CornerRadius(4),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(6, 1),
        Margin = new Thickness(4, 2, 0, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = key, FontSize = 12 }.Res(TextBlock.FontFamilyProperty, "Font.Mono").Res(TextBlock.ForegroundProperty, "Brush.Text"),
    }.Res(Border.BorderBrushProperty, "Brush.BorderStrong").Res(Border.BackgroundProperty, "Brush.WindowBackground");
}
