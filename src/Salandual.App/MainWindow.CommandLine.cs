// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.CommandLine.cs

using Avalonia.Input;
using Avalonia.Interactivity;
using Salandual.App.Controls;
using Salandual.App.Input;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.CommandLine;
using Salandual.Core.Commands;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using AvaloniaModifiers = Avalonia.Input.KeyModifiers;

namespace Salandual.App;

public partial class MainWindow
{
    internal const string CommandLinePage = "commandline";

    private bool _commandLineInUse;

    private string? _typedBeforeHistory;

    private readonly List<string> _riskyNames = [];

    internal CommandLineBar CommandLineBox => CommandLineRow;

    private void InitCommandLine()
    {
        var bar = CommandLineRow;
        bar.Box.AddHandler(KeyDownEvent, OnCommandLineKey, RoutingStrategies.Tunnel);
        bar.Box.TextChanged += (_, _) => UpdateCommandLineWarning();
        bar.ShellClicked += (_, _) => _ = OpenOptionsAsync(CommandLinePage);
        bar.HistoryChosen += (_, index) =>
        {
            TakeHistory(index);
            bar.HideHistory();
            bar.Box.Focus();
        };
        foreach (var host in _panels)
        {
            host.View.TypingTarget = new CommandLineTyping(this);
        }
    }

    private void UpdateCommandLine()
    {
        var s = _services.Settings.Current;
        var bar = CommandLineRow;
        bar.IsVisible = s.Layout.Bars.CommandLine || _commandLineInUse;
        bar.Prompt = (CommandFolderOf(ActivePanel) ?? "") + ">";
        var found = _services.Shells.Found;
        var runsIn = CommandShell.RunsIn(s.CommandLine, found);
        bar.ShellButton.Content = ShellName(runsIn);
        Avalonia.Controls.ToolTip.SetTip(bar.ShellButton, Loc.Format(AppStrings.CommandLine_ShellTip, ShellName(runsIn), ShellName(CommandShell.Effective(s.CommandLine, found))));
    }

    internal static string ShellName(CommandLineShell shell) => shell switch
    {
        CommandLineShell.PowerShell7 => AppStrings.Shell_PowerShell7,
        CommandLineShell.WindowsPowerShell => AppStrings.Shell_WindowsPowerShell,
        _ => AppStrings.Shell_CommandPrompt,
    };

    internal static string ShellName(ShellKind shell) => shell switch
    {
        ShellKind.Terminal => AppStrings.Shell_Terminal,
        ShellKind.PowerShell7 => AppStrings.Shell_PowerShell7,
        ShellKind.WindowsPowerShell => AppStrings.Shell_WindowsPowerShell,
        ShellKind.CommandPrompt => AppStrings.Shell_CommandPrompt,
        _ => AppStrings.Shell_Program,
    };

    private string? CommandFolderOf(PanelHost host)
    {
        var path = host.Panel.Results is null && host.Panel.Path.Length > 0 ? SessionPath(host) : "";
        return path.Length > 0 && Path.IsPathFullyQualified(path) ? path : null;
    }

    private string? CommandFolder(PanelHost host)
    {
        if (CommandFolderOf(host) is { } folder)
        {
            return folder;
        }

        ShowNotice(AppStrings.CommandLine_NoFolder);
        return null;
    }

    internal void ToggleCommandLine()
    {
        var box = CommandLineRow.Box;
        if (box.IsKeyboardFocusWithin)
        {
            LeaveCommandLine(clear: false);
            return;
        }

        ShowCommandLineForUse();
        box.Focus();
        box.CaretIndex = box.Text?.Length ?? 0;
    }

    private void ShowCommandLineForUse()
    {
        if (!_services.Settings.Current.Layout.Bars.CommandLine)
        {
            _commandLineInUse = true;
        }

        CommandLineRow.IsVisible = true;
    }

    private void LeaveCommandLine(bool clear)
    {
        var bar = CommandLineRow;
        bar.HideHistory();
        _typedBeforeHistory = null;
        if (clear)
        {
            bar.Box.Text = "";
        }

        if (_commandLineInUse && string.IsNullOrEmpty(bar.Box.Text))
        {
            _commandLineInUse = false;
            bar.IsVisible = _services.Settings.Current.Layout.Bars.CommandLine;
        }

        ActivePanel.View.Focus();
    }

    private void OnCommandLineKey(object? sender, KeyEventArgs e)
    {
        var bar = CommandLineRow;
        var mods = e.KeyModifiers;
        if (bar.IsHistoryOpen && mods == AvaloniaModifiers.None)
        {
            switch (e.Key)
            {
                case Key.Up or Key.Down:
                    TakeHistory(bar.HistorySelected + (e.Key == Key.Up ? 1 : -1));
                    e.Handled = true;
                    return;
                case Key.Delete:
                    RemoveHistory(bar.HistorySelected);
                    e.Handled = true;
                    return;
                case Key.Escape:
                    bar.HideHistory();
                    bar.Box.Text = _typedBeforeHistory ?? bar.Box.Text;
                    _typedBeforeHistory = null;
                    e.Handled = true;
                    return;
            }
        }

        if (mods == AvaloniaModifiers.Control && e.Key is Key.Up or Key.Down)
        {
            TakeHistory(bar.IsHistoryOpen ? bar.HistorySelected + (e.Key == Key.Up ? 1 : -1) : e.Key == Key.Up ? 0 : -1);
            e.Handled = true;
            return;
        }

        if (mods == AvaloniaModifiers.Alt && e.Key == Key.Down)
        {
            if (bar.IsHistoryOpen)
            {
                bar.HideHistory();
            }
            else
            {
                TakeHistory(0);
            }

            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when mods is AvaloniaModifiers.None or AvaloniaModifiers.Alt:
                bar.HideHistory();
                RunCommandLine(alternate: mods == AvaloniaModifiers.Alt);
                e.Handled = true;
                return;
            case Key.Enter when mods == AvaloniaModifiers.Shift:
                Run("Navigation.Execute", PanelTarget.Active);
                e.Handled = true;
                return;
            case Key.Escape when mods == AvaloniaModifiers.None:
                LeaveCommandLine(clear: true);
                e.Handled = true;
                return;
        }

        if (EditsText(e.Key, mods) || !KeyMapping.TryGetChord(e.Key, mods, out var chord))
        {
            return;
        }

        if (_services.Commands.Resolve(chord) is { } id)
        {
            e.Handled = true;
            Run(id, PanelTarget.Active, chord);
        }
        else if (TryUserMenuKey(ActivePanel, chord))
        {
            e.Handled = true;
        }
    }

    private static bool EditsText(Key key, AvaloniaModifiers mods)
    {
        var ctrl = (mods & AvaloniaModifiers.Control) != 0;
        if ((mods & (AvaloniaModifiers.Alt | AvaloniaModifiers.Meta)) != 0)
        {
            return false;
        }

        return key switch
        {
            Key.Left or Key.Right or Key.Home or Key.End or Key.Back or Key.Delete => true,
            Key.Insert => mods is AvaloniaModifiers.Control or AvaloniaModifiers.Shift,
            Key.A or Key.C or Key.V or Key.X or Key.Z or Key.Y => ctrl && (mods & AvaloniaModifiers.Shift) == 0,
            Key.Space or Key.Add or Key.Subtract or Key.Multiply or Key.Divide or Key.Decimal => !ctrl,
            >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9 or >= Key.Oem1 and <= Key.Oem102 => !ctrl,
            _ => false,
        };
    }

    private void TakeHistory(int index)
    {
        var bar = CommandLineRow;
        var commands = _services.Settings.Current.History.Commands;
        if (commands.Count == 0)
        {
            ShowNotice(AppStrings.CommandLine_NoHistory);
            return;
        }

        if (index < 0)
        {
            bar.HideHistory();
            bar.Box.Text = _typedBeforeHistory ?? "";
            _typedBeforeHistory = null;
        }
        else
        {
            index = Math.Min(index, commands.Count - 1);
            _typedBeforeHistory ??= bar.Box.Text ?? "";
            if (!bar.IsHistoryOpen)
            {
                bar.ShowHistory(HistoryRows(commands), index);
            }
            else
            {
                bar.Select(index);
            }

            bar.Box.Text = commands[index].Text;
        }

        bar.Box.CaretIndex = bar.Box.Text?.Length ?? 0;
    }

    private IReadOnlyList<(string Text, string Where)> HistoryRows(IReadOnlyList<CommandHistoryEntry> commands)
    {
        var here = CommandFolderOf(ActivePanel);
        return [.. commands.Select(c => (c.Text, string.Equals(c.Folder, here, StringComparison.OrdinalIgnoreCase) ? AppStrings.CommandLine_Here : c.Folder))];
    }

    private void RemoveHistory(int index)
    {
        var commands = _services.Settings.Current.History.Commands;
        if (index < 0 || index >= commands.Count)
        {
            return;
        }

        var text = commands[index].Text;
        _services.Settings.Update(s => s with { History = s.History with { Commands = [.. s.History.Commands.Where(c => c.Text != text)] } });
        var left = _services.Settings.Current.History.Commands;
        var bar = CommandLineRow;
        if (left.Count == 0)
        {
            bar.HideHistory();
            bar.Box.Text = _typedBeforeHistory ?? "";
            _typedBeforeHistory = null;
            return;
        }

        var next = Math.Min(index, left.Count - 1);
        bar.ShowHistory(HistoryRows(left), next);
        bar.Box.Text = left[next].Text;
        bar.Box.CaretIndex = bar.Box.Text.Length;
    }

    internal void RunCommandLine(bool alternate)
    {
        var bar = CommandLineRow;
        var host = ActivePanel;
        var text = (bar.Box.Text ?? "").Trim();
        _typedBeforeHistory = null;
        if (text.Length == 0)
        {
            Run("Navigation.Execute", PanelTarget.Active);
            return;
        }

        if (CommandFolder(host) is not { } folder)
        {
            return;
        }

        if (CommandShell.TryChangeDirectory(text, folder, out var target, out var drive))
        {
            RememberCommand(text, folder);
            CommandRan();
            if (drive)
            {
                GoToDrive(host, target);
            }
            else
            {
                _ = host.Panel.NavigateAsync(target);
            }

            return;
        }

        var settings = _services.Settings.Current.CommandLine;
        var found = _services.Shells.Found;
        var shell = CommandShell.RunsIn(settings, found);
        var start = CommandShell.Run(shell, found, text, folder, keepOpen: settings.CloseWhenDone == alternate);
        if (!_services.Shells.Start(start, out var detail))
        {
            ShowNotice(Loc.Format(AppStrings.CommandLine_DidNotStart, ShellName(shell), detail ?? ""));
            return;
        }

        RememberCommand(text, folder);
        CommandRan();
    }

    private void CommandRan()
    {
        var bar = CommandLineRow;
        bar.Box.Text = "";
        _riskyNames.Clear();
        UpdateCommandLineWarning();
        if (_commandLineInUse)
        {
            LeaveCommandLine(clear: false);
        }
    }

    private void RememberCommand(string text, string folder)
        => _services.Settings.Update(s => s with
        {
            History = s.History with { Commands = HistorySettings.Push(s.History.Commands, new CommandHistoryEntry(text, folder), s.CommandLine.Remembered) },
        });

    private void InsertForCommandLine(string id)
    {
        var shortNames = id.EndsWith("Dos", StringComparison.Ordinal);
        string? text;
        if (id is "CommandLine.InsertName" or "CommandLine.InsertDosName")
        {
            var panel = ActivePanel.Panel;
            if (panel.FocusedEntry is not { } entry)
            {
                return;
            }

            text = panel.IsUp(panel.Focus) ? ".." : entry.Name;
            if (shortNames && text != ".." && CommandFolderOf(ActivePanel) is { } folder)
            {
                text = Path.GetFileName(ShortPath(Path.Join(folder, text)));
            }
        }
        else
        {
            var host = id.Contains("Left", StringComparison.Ordinal) ? _panels[0] : id.Contains("Right", StringComparison.Ordinal) ? _panels[1] : ActivePanel;
            text = CommandFolderOf(host);
            if (text is not null && shortNames)
            {
                text = ShortPath(text);
            }
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var shell = CommandShell.RunsIn(_services.Settings.Current.CommandLine, _services.Shells.Found);
        var quoted = CommandShell.Quote(text, shell);
        if (CommandShell.IsRisky(text, shell))
        {
            _riskyNames.Add(quoted);
        }

        InsertIntoCommandLine(quoted);
    }

    private string ShortPath(string path) => _services.IsIsolated ? path : ShortNames.Of(path);

    private void InsertIntoCommandLine(string text)
    {
        ShowCommandLineForUse();
        var box = CommandLineRow.Box;
        var current = box.Text ?? "";
        var at = Math.Clamp(box.CaretIndex, 0, current.Length);
        var insert = (at > 0 && current[at - 1] != ' ' ? " " : "") + text + " ";
        box.Text = current.Insert(at, insert);
        box.CaretIndex = at + insert.Length;
    }

    private void UpdateCommandLineWarning()
    {
        var text = CommandLineRow.Box.Text ?? "";
        _riskyNames.RemoveAll(n => !text.Contains(n, StringComparison.Ordinal));
        var shell = CommandShell.RunsIn(_services.Settings.Current.CommandLine, _services.Shells.Found);
        CommandLineRow.Warning = _riskyNames.Count == 0 ? null
            : shell == CommandLineShell.CommandPrompt ? AppStrings.CommandLine_RiskyCmd : AppStrings.CommandLine_RiskyPowerShell;
    }

    internal void OpenCommandShell(PanelHost host, bool elevated)
    {
        if (CommandFolder(host) is not { } folder)
        {
            return;
        }

        var settings = _services.Settings.Current.CommandLine;
        var found = _services.Shells.Found;
        var start = CommandShell.Open(settings, found, folder, elevated, out var note);
        if (!_services.Shells.Start(start, out var detail))
        {
            ShowNotice(Loc.Format(AppStrings.CommandLine_DidNotStart, ShellName(CommandShell.Effective(settings, found)), detail ?? ""));
        }
        else if (note == ShellNote.OwnFolder)
        {
            ShowNotice(Loc.Format(AppStrings.CommandLine_OwnFolder, ShellName(CommandShell.Effective(settings, found))));
        }
    }

    private sealed class CommandLineTyping(MainWindow window) : ITypingTarget
    {
        public bool Wants
        {
            get
            {
                var s = window._services.Settings.Current;
                return s.CommandLine.Typing == PanelTyping.CommandLine && window.CommandLineRow.IsVisible;
            }
        }

        public bool HasText => !string.IsNullOrEmpty(window.CommandLineRow.Box.Text);

        public void Type(string text)
        {
            var box = window.CommandLineRow.Box;
            var current = box.Text ?? "";
            var at = Math.Clamp(box.CaretIndex, 0, current.Length);
            box.Text = current.Insert(at, text);
            box.CaretIndex = at + text.Length;
        }

        public void Key(Key key)
        {
            var box = window.CommandLineRow.Box;
            var current = box.Text ?? "";
            switch (key)
            {
                case Avalonia.Input.Key.Back when current.Length > 0:
                    var at = Math.Clamp(box.CaretIndex, 1, current.Length);
                    box.Text = current.Remove(at - 1, 1);
                    box.CaretIndex = at - 1;
                    break;
                case Avalonia.Input.Key.Space:
                    Type(" ");
                    break;
                case Avalonia.Input.Key.Enter:
                    window.RunCommandLine(alternate: false);
                    break;
                case Avalonia.Input.Key.Escape:
                    box.Text = "";
                    break;
            }
        }
    }
}
