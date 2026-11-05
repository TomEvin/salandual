// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.KeyEditor.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Input;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.Resources;
using Salandual.Core.UserMenu;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public partial class OptionsWindow
{
    private sealed record KeyListRow(Control Container, Func<string> Text, Func<bool> Changed, Func<bool> Yours);

    private readonly Dictionary<string, Action> _keyRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (KeyChord Chord, string To, List<CommandKeys> Before)> _reassigned = new(StringComparer.Ordinal);
    private string? _openKeyRow;
    private Action? _keyListChanged;

    internal Border? KeyCapture { get; private set; }

    internal TextBlock? KeyCaptureMessage { get; private set; }

    internal Button? KeyCapturePrimary { get; private set; }

    internal string? OpenKeyRowCommand => _openKeyRow;

    private string MapTitle => KeyMaps.Title(_services.Commands.KeyMap);

    private string TitleOf(string id) => _services.Commands.TryGet(id, out var c) ? c.Title : id;

    private static string Keys(IEnumerable<KeyChord> chords) => string.Join("  ", chords) is { Length: > 0 } keys ? keys : "—";

    private KeyListRow KeyRow(CommandDefinition command)
    {
        var commands = _services.Commands;
        var container = new Border { Focusable = true, CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        Avalonia.Automation.AutomationProperties.SetName(container, command.Title);
        ToolTip.SetTip(container, AppStrings.Opt_Keys_RowTip);
        void Fill()
        {
            var open = _openKeyRow == command.Id;
            container.Tag = open ? "open" : null;
            container.Cursor = open ? Cursor.Default : new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(container, open ? null : AppStrings.Opt_Keys_RowTip);
            container.Child = open ? OpenKeyRowControl(command) : ClosedKeyRow(command);
        }

        _keyRows[command.Id] = Fill;
        _keyRowControls[command.Id] = container;
        container.PointerEntered += (_, _) => container.Res(Border.BackgroundProperty, _openKeyRow == command.Id ? "Brush.PanelBackground" : "Brush.Hover");
        container.PointerExited += (_, _) => container.Background = Brushes.Transparent;
        container.PointerPressed += (_, e) =>
        {
            if (_openKeyRow != command.Id && e.GetCurrentPoint(container).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                OpenKeyRow(command.Id);
            }
        };
        container.KeyDown += (_, e) =>
        {
            if (_openKeyRow != command.Id && e.KeyModifiers == Avalonia.Input.KeyModifiers.None && e.Key is Key.Enter or Key.F2)
            {
                e.Handled = true;
                OpenKeyRow(command.Id);
            }
        };
        Fill();
        return new KeyListRow(
            container,
            () => string.Join(' ', command.Title, CommandText.Category(command.Category), command.Id, string.Join(' ', commands.ChordsFor(command.Id))),
            () => commands.IsYours(command.Id) || commands.TakenFrom(command.Id).Count > 0 || WasKeys(command, [.. commands.ChordsFor(command.Id)]) is not null,
            () => commands.IsYours(command.Id));
    }

    private Control ClosedKeyRow(CommandDefinition command)
    {
        var commands = _services.Commands;
        var built = MainWindow.IsBuilt(command.Id);
        var chords = commands.ChordsFor(command.Id).ToList();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4) };
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(command.Title, 13, brush: built ? "Brush.Text" : "Brush.TertiaryText"));
        if (!built)
        {
            text.Children.Add(Text(Loc.Format(AppStrings.Opt_NotYet, CommandText.Milestone(command.PlannedFor)), 11, brush: "Brush.TertiaryText"));
        }

        var yours = commands.IsYours(command.Id);
        if (yours)
        {
            text.Children.Add(Text(Loc.Format(AppStrings.Opt_Keys_Yours, MapTitle, Keys(commands.MapChordsFor(command.Id))), 11, brush: "Brush.SecondaryText"));
        }

        var taken = commands.TakenFrom(command.Id);
        if (taken.Count > 0)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var said = Text(string.Join(" · ", taken.Select(t => Loc.Format(AppStrings.Opt_Keys_Taken, t.Chord, TitleOf(t.To)))), 11, brush: "Brush.SecondaryText");
            said.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(said);
            if (_reassigned.TryGetValue(command.Id, out var undo))
            {
                var button = new Button { Content = AppStrings.Common_Undo, Padding = new Thickness(6, 0), FontSize = 11, MinHeight = 0 };
                Avalonia.Automation.AutomationProperties.SetName(button, AppStrings.Common_Undo + " " + command.Title);
                button.Click += (_, _) =>
                {
                    _reassigned.Remove(command.Id);
                    SetYourKeys(undo.Before);
                };
                line.Children.Add(button);
            }

            text.Children.Add(line);
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

        if (!yours && WasKeys(command, chords) is { } was)
        {
            var wasText = Text(was, 11, brush: "Brush.TertiaryText");
            wasText.VerticalAlignment = VerticalAlignment.Center;
            wasText.Margin = new Thickness(8, 0, 0, 0);
            keys.Children.Add(wasText);
        }

        keys.Opacity = built ? 1 : 0.6;
        Grid.SetColumn(keys, 1);
        grid.Children.Add(keys);
        var edge = taken.Count > 0 ? "Brush.Warning" : yours || WasKeys(command, chords) is not null ? "Brush.Accent" : null;
        return edge is null
            ? new Border { Padding = new Thickness(6, 0), Child = grid }
            : new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 0, 6, 0), Child = grid }.Res(Border.BorderBrushProperty, edge);
    }

    private Control OpenKeyRowControl(CommandDefinition command)
    {
        var commands = _services.Commands;
        var chords = commands.ChordsFor(command.Id).ToList();
        var head = new DockPanel();
        var where = Secondary(Loc.Format(AppStrings.Opt_Keys_At, CommandText.Category(command.Category), MapTitle, Keys(commands.MapChordsFor(command.Id))));
        where.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(where, Dock.Right);
        head.Children.Add(where);
        head.Children.Add(Text(command.Title, 13, FontWeight.SemiBold));

        var keys = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        var label = Text(AppStrings.Opt_Keys_Keys, 12.5);
        label.VerticalAlignment = VerticalAlignment.Center;
        keys.Children.Add(label);
        foreach (var chord in chords)
        {
            var remove = new Button
            {
                Content = chord + "  ✕",
                Padding = new Thickness(8, 1),
                CornerRadius = new CornerRadius(4),
                MinHeight = 0,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            }.Res(Button.FontFamilyProperty, "Font.Mono");
            var removeText = Loc.Format(AppStrings.Opt_Keys_Remove, chord);
            ToolTip.SetTip(remove, removeText);
            Avalonia.Automation.AutomationProperties.SetName(remove, removeText);
            remove.Click += (_, _) => SetYourKeys(YourKeys.Remove(commands, Current.Keyboard.Keys, command.Id, chord), focusCapture: true);
            keys.Children.Add(remove);
        }

        var pressed = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var box = new Border
        {
            Focusable = true,
            Width = 240,
            Height = 28,
            Padding = new Thickness(6, 0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            Child = pressed,
            Cursor = new Cursor(StandardCursorType.Ibeam),
        }.Res(Border.BorderBrushProperty, "Brush.BorderStrong").Res(Border.BackgroundProperty, "Brush.PanelBackground");
        Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Opt_Keys_Press);
        box.GotFocus += (_, _) => box.Res(Border.BorderBrushProperty, "Brush.Accent");
        box.LostFocus += (_, _) => box.Res(Border.BorderBrushProperty, "Brush.BorderStrong");
        box.PointerPressed += (_, e) =>
        {
            box.Focus();
            e.Handled = true;
        };
        keys.Children.Add(box);

        var message = Text("", 12.5, wrap: true);
        message.VerticalAlignment = VerticalAlignment.Center;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var say = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        say.Children.Add(buttons);
        say.Children.Add(message);
        var sayBox = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 4, 4), Child = say, IsVisible = false };

        KeyChord? pending = null;
        Action? primary = null;
        void Show()
        {
            pressed.Children.Clear();
            buttons.Children.Clear();
            primary = null;
            KeyCapturePrimary = null;
            if (pending is not { } chord)
            {
                var hint = Text(AppStrings.Opt_Keys_Press, 12.5, brush: "Brush.TertiaryText");
                hint.VerticalAlignment = VerticalAlignment.Center;
                pressed.Children.Add(hint);
                sayBox.IsVisible = false;
                return;
            }

            foreach (var part in Parts(chord))
            {
                pressed.Children.Add(KeyCap(part));
            }

            var (text, warn, action, actionText) = Judge(command, chord);
            message.Text = text;
            sayBox.Res(Border.BackgroundProperty, warn ? "Brush.WarningBackground" : "Brush.InfoBackground");
            sayBox.IsVisible = true;
            ShowOpenRow();
            if (action is not null)
            {
                primary = action;
                var go = ActionButton(actionText!, action);
                go.Classes.Add("accent");
                KeyCapturePrimary = go;
                buttons.Children.Add(go);
            }

            buttons.Children.Add(ActionButton(AppStrings.Common_Cancel, () =>
            {
                pending = null;
                Show();
                box.Focus();
            }));
        }

        box.KeyDown += (_, e) =>
        {
            var none = e.KeyModifiers == Avalonia.Input.KeyModifiers.None;
            if (none && e.Key == Key.Tab)
            {
                return;
            }

            if (none && e.Key == Key.Escape)
            {
                e.Handled = true;
                if (pending is not null)
                {
                    pending = null;
                    Show();
                }
                else
                {
                    CloseKeyRow();
                }

                return;
            }

            if (none && e.Key == Key.Enter && pending is not null)
            {
                e.Handled = true;
                primary?.Invoke();
                return;
            }

            if (!KeyMapping.TryGetChord(e.Key, e.KeyModifiers, out var chord))
            {
                return;
            }

            e.Handled = true;
            pending = chord;
            Show();
        };
        Show();
        KeyCapture = box;
        KeyCaptureMessage = message;

        var restore = ActionButton(Loc.Format(AppStrings.Opt_Keys_Restore, MapTitle), () => SetYourKeys(YourKeys.Reset(Current.Keyboard.Keys, command.Id), focusCapture: true),
            enabled: commands.IsYours(command.Id));
        var done = ActionButton(AppStrings.Opt_Keys_Done, CloseKeyRow);
        done.Classes.Add("accent");
        var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { restore, done } };
        return new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 4),
            Child = new StackPanel { Spacing = 8, Children = { head, keys, sayBox, foot } },
        }.Res(Border.BorderBrushProperty, "Brush.Accent").Res(Border.BackgroundProperty, "Brush.PanelBackground");
    }

    private (string Text, bool Warn, Action? Action, string? ActionText) Judge(CommandDefinition command, KeyChord chord)
    {
        var commands = _services.Commands;
        if (commands.ChordsFor(command.Id).Contains(chord))
        {
            return (Loc.Format(AppStrings.Opt_Keys_Own, chord), false, null, null);
        }

        if (YourKeys.WhyNot(commands, command.Id, chord) is var why and not KeyRefusal.None)
        {
            return (Refusal(why, chord), true, null, null);
        }

        var holder = YourKeys.Holder(commands, command.Id, chord);
        if (holder.Command is { } other && commands.TryGet(other, out var otherCommand))
        {
            return (Loc.Format(AppStrings.Opt_Keys_TakenAsk, chord, otherCommand.Title, CommandText.Category(otherCommand.Category)), true,
                () => AssignKey(command.Id, chord), Loc.Format(AppStrings.Opt_Keys_Reassign, command.Title));
        }

        if (UserMenuItemWith(chord) is { } item)
        {
            return (Loc.Format(AppStrings.Opt_Keys_UserMenu, chord, item.PlainName), true,
                () => AssignKey(command.Id, chord, fromUserMenu: true), Loc.Format(AppStrings.Opt_Keys_Reassign, command.Title));
        }

        return holder.NotYet is { } notYet
            ? (Loc.Format(AppStrings.Opt_Keys_NotYet, chord, MapTitle, notYet.Title), true, () => AssignKey(command.Id, chord), AppStrings.Opt_Keys_Assign)
            : (Loc.Format(AppStrings.Opt_Keys_Free, chord), false, () => AssignKey(command.Id, chord), AppStrings.Opt_Keys_Assign);
    }

    private static string Refusal(KeyRefusal why, KeyChord chord) => why switch
    {
        KeyRefusal.Types => Loc.Format(AppStrings.Opt_Keys_No_Types, chord),
        KeyRefusal.Selects => AppStrings.Opt_Keys_No_Selects,
        KeyRefusal.Moves => Loc.Format(AppStrings.Opt_Keys_No_Moves, chord),
        KeyRefusal.Opens => AppStrings.Opt_Keys_No_Opens,
        KeyRefusal.Closes => AppStrings.Opt_Keys_No_Closes,
        KeyRefusal.Switches => AppStrings.Opt_Keys_No_Switches,
        KeyRefusal.Deletes => AppStrings.Opt_Keys_No_Deletes,
        _ => Loc.Format(AppStrings.Opt_Keys_No_Menu, chord, YourKeys.MenuOf(chord.Key) ?? chord.Key),
    };

    private UserMenuItem? UserMenuItemWith(KeyChord chord)
        => Current.UserMenu.All().FirstOrDefault(i => i.IsRunnable && i.Key is { } key && KeyChord.TryParse(key, out var own) && own == chord);

    private void AssignKey(string id, KeyChord chord, bool fromUserMenu = false)
    {
        var before = Current.Keyboard.Keys;
        var (keys, from) = YourKeys.Assign(_services.Commands, before, id, chord);
        if (fromUserMenu)
        {
            Settings.Update(s => s with { UserMenu = s.UserMenu with { Items = WithoutKey(s.UserMenu.Items, chord) } });
        }

        if (from is not null)
        {
            _reassigned[from] = (chord, id, before);
        }

        SetYourKeys(keys, focusCapture: true);
    }

    private static List<UserMenuItem> WithoutKey(List<UserMenuItem> items, KeyChord chord)
        => [.. items.Select(i => i.Kind == UserMenuItemKind.Submenu ? i with { Items = WithoutKey(i.Items, chord) }
            : i.Key is { } key && KeyChord.TryParse(key, out var own) && own == chord ? i with { Key = null } : i)];

    private void SetYourKeys(List<CommandKeys> keys, bool focusCapture = false)
    {
        Settings.Update(s => s with { Keyboard = s.Keyboard with { Keys = keys } });
        _services.Commands.UseKeyMap(Current.Keyboard.KeyMap, Current.Keyboard.Keys);
        foreach (var fill in _keyRows.Values)
        {
            fill();
        }

        _keyListChanged?.Invoke();
        _yourKeysLine?.Invoke();
        if (focusCapture)
        {
            Dispatcher.UIThread.Post(() => KeyCapture?.Focus(), DispatcherPriority.Loaded);
            ShowOpenRow();
        }
    }

    internal void OpenKeyRow(string id)
    {
        if (!_services.Commands.TryGet(id, out _))
        {
            return;
        }

        var before = _openKeyRow;
        _openKeyRow = id;
        if (_category != "keyboard" || IsSearching || !_keyRows.ContainsKey(id))
        {
            if (IsSearching)
            {
                _search.Text = "";
            }

            ShowCategory("keyboard");
        }
        else
        {
            if (before is not null && _keyRows.TryGetValue(before, out var close))
            {
                close();
            }

            _keyRows[id]();
        }

        Dispatcher.UIThread.Post(() =>
        {
            KeyCapture?.Focus();
            ShowOpenRow();
        }, DispatcherPriority.Loaded);
    }

    private void ShowOpenRow()
    {
        if (_openKeyRow is { } id && _keyRowControls.TryGetValue(id, out var row))
        {
            Dispatcher.UIThread.Post(() => row.BringIntoView(), DispatcherPriority.Loaded);
        }
    }

    private void CloseKeyRow()
    {
        if (_openKeyRow is not { } id)
        {
            return;
        }

        _openKeyRow = null;
        KeyCapture = null;
        KeyCaptureMessage = null;
        KeyCapturePrimary = null;
        if (_keyRows.TryGetValue(id, out var fill))
        {
            fill();
            var row = _keyRowControls.GetValueOrDefault(id);
            Dispatcher.UIThread.Post(() => row?.Focus(), DispatcherPriority.Loaded);
        }
    }

    private readonly Dictionary<string, Control> _keyRowControls = new(StringComparer.Ordinal);

    private Action? _yourKeysLine;

    private Control YourKeysLine()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        void Fill()
        {
            var commands = _services.Commands;
            var count = commands.Yours.Count(k => commands.TryGet(k.Command, out _));
            line.Children.Clear();
            line.IsVisible = count > 0;
            if (count == 0)
            {
                return;
            }

            var text = Text(count == 1 ? AppStrings.Opt_Keys_YoursCountOne : Loc.Format(AppStrings.Opt_Keys_YoursCount, count), 12.5);
            text.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(text);
            line.Children.Add(ActionButton(Loc.Format(AppStrings.Opt_Keys_RestoreAll, MapTitle), () => _ = RestoreAllKeysAsync()));
        }

        _yourKeysLine = Fill;
        Fill();
        return line;
    }

    internal Dialogs.ConfirmDialog? RestoreKeysQuestion { get; private set; }

    private async Task RestoreAllKeysAsync()
    {
        var commands = _services.Commands;
        var yours = commands.Yours.Where(k => commands.TryGet(k.Command, out _)).ToList();
        var list = string.Join("\n", yours.Select(k => "• " + TitleOf(k.Command) + ": " + Keys(k.Chords())));
        var ask = RestoreKeysQuestion = new Dialogs.ConfirmDialog(Loc.Format(AppStrings.Opt_Keys_RestoreAll_Title, MapTitle),
            Loc.Format(AppStrings.Opt_Keys_RestoreAll_Text, MapTitle) +"\n\n" + list, AppStrings.Opt_Keys_RestoreAll_Yes, AppStrings.Common_Cancel);
        var yes = await ask.ShowDialog<bool>(this);
        RestoreKeysQuestion = null;
        if (yes)
        {
            RestoreAllKeys();
        }
    }

    internal void RestoreAllKeys()
    {
        var before = Current.Keyboard.Keys;
        var map = MapTitle;
        _reassigned.Clear();
        _openKeyRow = null;
        SetYourKeys([]);
        _keyMapNotice = (before.Count == 1 ? Loc.Format(AppStrings.Opt_Keys_RestoredOne, map) : Loc.Format(AppStrings.Opt_Keys_Restored, map, before.Count), () =>
        {
            Settings.Update(s => s with { Keyboard = s.Keyboard with { Keys = before } });
            _services.Commands.UseKeyMap(Current.Keyboard.KeyMap, Current.Keyboard.Keys);
        });
        Rebuild();
    }
}
