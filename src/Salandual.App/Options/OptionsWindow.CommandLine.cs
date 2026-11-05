// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.CommandLine.cs

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Salandual.App.Resources;
using Salandual.Core.CommandLine;
using Salandual.Core.Resources;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    private const string ChooseShell = "\u0001choose";

    private void AddCommandLine()
    {
        const string page = MainWindow.CommandLinePage;
        var kw = AppStrings.Opt_CommandLine_Kw;

        var shell = AppStrings.OptSec_CommandShell;
        Add(page, shell, AppStrings.Opt_ShellOpens, AppStrings.Opt_ShellOpens_Desc, ShellPicker, keywords: kw);
        Add(page, shell, AppStrings.Opt_TerminalProfile, AppStrings.Opt_TerminalProfile_Desc,
            () => EnabledWhile(TerminalProfilePicker(), s => CommandShell.Effective(s.CommandLine, _services.Shells.Found) == ShellKind.Terminal), keywords: kw, sub: true);

        var line = AppStrings.OptSec_CommandLine;
        Add(page, line, AppStrings.Opt_CommandLineShown, AppStrings.Opt_CommandLineShown_Desc,
            () => Toggle(Current.Layout.Bars.CommandLine, on => UpdateLayout(l => l with { Bars = l.Bars with { CommandLine = on } })), keywords: AppStrings.Opt_ShowHide_Kw + " " + kw);
        Add(page, line, AppStrings.Opt_CommandRunsIn, AppStrings.Opt_CommandRunsIn_Desc, RunsInPicker, keywords: kw);
        Add(page, line, AppStrings.Opt_CommandEnds, AppStrings.Opt_CommandEnds_Desc, () => Segmented(
            [(false, AppStrings.Opt_CommandEnds_Keep), (true, AppStrings.Opt_CommandEnds_Close)],
            Current.CommandLine.CloseWhenDone, close => UpdateCommandLine(c => c with { CloseWhenDone = close })), keywords: kw);
        Add(page, line, AppStrings.Opt_CommandsRemembered, AppStrings.Opt_CommandsRemembered_Desc, RememberedPicker, keywords: kw);
        Add(page, line, AppStrings.Opt_PanelTyping, AppStrings.Opt_PanelTyping_Desc, () => Segmented(
            [(PanelTyping.QuickSearch, AppStrings.Opt_PanelTyping_QuickSearch), (PanelTyping.CommandLine, AppStrings.Opt_PanelTyping_CommandLine)],
            Current.CommandLine.Typing, v => UpdateCommandLine(c => c with { Typing = v })), keywords: kw + " " + AppStrings.Opt_PanelTyping_Kw);
        Add(page, line, null, AppStrings.Opt_CommandLineSafety, () => new Panel(), wide: true, keywords: kw);
    }

    private void UpdateCommandLine(Func<CommandLineSettings, CommandLineSettings> change) => Settings.Update(s => s with { CommandLine = change(s.CommandLine) });

    private Control ShellPicker()
    {
        var found = _services.Shells.Found;
        var settings = Current.CommandLine;
        var items = new List<(string Key, string Label)>
        {
            ("", Loc.Format(AppStrings.Opt_ShellFound, MainWindow.ShellName(CommandShell.Effective(settings with { Shell = null }, found)))),
        };
        if (found.Terminal is not null)
        {
            items.Add((nameof(ShellKind.Terminal), AppStrings.Shell_Terminal));
        }

        if (found.PowerShell7 is not null)
        {
            items.Add((nameof(ShellKind.PowerShell7), AppStrings.Shell_PowerShell7));
        }

        items.Add((nameof(ShellKind.WindowsPowerShell), AppStrings.Shell_WindowsPowerShell));
        items.Add((nameof(ShellKind.CommandPrompt), AppStrings.Shell_CommandPrompt));
        if (settings.Program.Length > 0)
        {
            items.Add((nameof(ShellKind.Program), Path.GetFileName(settings.Program)));
        }

        items.Add((ChooseShell, AppStrings.Opt_ShellChoose));
        var current = settings.Shell is { } kind && items.Any(i => i.Key == kind.ToString()) ? kind.ToString() : "";
        var combo = Combo(items, current, async key =>
        {
            if (key == ChooseShell)
            {
                await ChooseShellProgramAsync();
                return;
            }

            UpdateCommandLine(c => c with { Shell = key.Length == 0 ? null : Enum.Parse<ShellKind>(key) });
            RebuildLater();
        }, minWidth: 260);
        Avalonia.Automation.AutomationProperties.SetName(combo, AppStrings.Opt_ShellOpens);
        ToolTip.SetTip(combo, settings.Shell == ShellKind.Program ? settings.Program : null);
        return combo;
    }

    private async Task ChooseShellProgramAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppStrings.Opt_ShellChoose,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(AppStrings.Opt_ComparePrograms) { Patterns = ["*.exe"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            UpdateCommandLine(c => c with { Shell = ShellKind.Program, Program = path });
        }

        RebuildLater();
    }

    private Control TerminalProfilePicker()
    {
        var (profiles, defaultId) = _services.Shells.TerminalProfiles();
        var defaultName = profiles.FirstOrDefault(p => string.Equals(p.Id, defaultId, StringComparison.OrdinalIgnoreCase))?.Name;
        var items = new List<(string Key, string Label)> { ("", defaultName is null ? AppStrings.Opt_TerminalDefault : Loc.Format(AppStrings.Opt_TerminalDefaultNamed, defaultName)) };
        items.AddRange(profiles.Select(p => (p.Id, p.Name)));
        var chosen = Current.CommandLine.TerminalProfile;
        if (chosen is not null && !items.Any(i => i.Key == chosen))
        {
            items.Add((chosen, AppStrings.Opt_TerminalGone));
        }

        var combo = Combo(items, chosen ?? "", key => UpdateCommandLine(c => c with { TerminalProfile = key.Length == 0 ? null : key }), minWidth: 260);
        Avalonia.Automation.AutomationProperties.SetName(combo, AppStrings.Opt_TerminalProfile);
        return combo;
    }

    private Control RunsInPicker()
    {
        var found = _services.Shells.Found;
        var settings = Current.CommandLine;
        var items = new List<(string Key, string Label)>
        {
            ("", Loc.Format(AppStrings.Opt_CommandRunsInShell, MainWindow.ShellName(CommandShell.RunsIn(settings with { RunIn = null }, found)))),
        };
        if (found.PowerShell7 is not null)
        {
            items.Add((nameof(CommandLineShell.PowerShell7), AppStrings.Shell_PowerShell7));
        }

        items.Add((nameof(CommandLineShell.WindowsPowerShell), AppStrings.Shell_WindowsPowerShell));
        items.Add((nameof(CommandLineShell.CommandPrompt), AppStrings.Shell_CommandPrompt));
        var current = settings.RunIn is { } runIn && items.Any(i => i.Key == runIn.ToString()) ? runIn.ToString() : "";
        var combo = Combo(items, current, key => UpdateCommandLine(c => c with { RunIn = key.Length == 0 ? null : Enum.Parse<CommandLineShell>(key) }), minWidth: 260);
        Avalonia.Automation.AutomationProperties.SetName(combo, AppStrings.Opt_CommandRunsIn);
        return combo;
    }

    private Control RememberedPicker()
    {
        int[] counts = [0, 10, 20, 30, 50, 100];
        var now = Current.CommandLine.Remembered;
        var options = counts.Union([now]).Order().Select(n => (n, n == 0 ? AppStrings.Opt_CommandsRemembered_None : n.ToString(Loc.TextCulture))).ToList();
        var combo = Combo(options, now, n => Settings.Update(s => s with
        {
            CommandLine = s.CommandLine with { Remembered = n },
            History = s.History with { Commands = [.. s.History.Commands.Take(n)] },
        }), minWidth: 100);
        Avalonia.Automation.AutomationProperties.SetName(combo, AppStrings.Opt_CommandsRemembered);
        var forget = ActionButton(AppStrings.Opt_CommandsForget, () =>
        {
            Settings.Update(s => s with { History = s.History with { Commands = [] } });
            RebuildLater();
        }, enabled: Current.History.Commands.Count > 0);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { combo, forget } };
    }
}
