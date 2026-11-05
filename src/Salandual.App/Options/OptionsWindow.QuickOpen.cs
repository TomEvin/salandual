// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.QuickOpen.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.Core.QuickOpen;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public partial class OptionsWindow
{
    private static readonly int[] WalkEntryChoices = [50_000, 100_000, 250_000, 500_000, 1_000_000, 2_000_000, 5_000_000];
    private static readonly int[] WalkSecondChoices = [3, 5, 10, 15, 30, 60];

    private void UpdateQuickOpen(Func<QuickOpenSettings, QuickOpenSettings> change) => Settings.Update(s => s with { QuickOpen = change(s.QuickOpen) });

    private void AddQuickOpenDrives(string page, string section)
    {
        var kw = AppStrings.Opt_QuickDrives_Kw;
        Add(page, section, AppStrings.Opt_QuickFilesFrom, AppStrings.Opt_QuickFilesFrom_Desc, () => Combo(
            [
                (QuickOpenFilesFrom.Best, AppStrings.Opt_QuickFilesFrom_Best),
                (QuickOpenFilesFrom.NoEverything, AppStrings.Opt_QuickFilesFrom_NoEverything),
                (QuickOpenFilesFrom.WalkOnly, AppStrings.Opt_QuickFilesFrom_WalkOnly),
                (QuickOpenFilesFrom.Off, AppStrings.Opt_QuickFilesFrom_Off),
            ], Current.QuickOpen.FilesFrom, from =>
            {
                UpdateQuickOpen(q => q with { FilesFrom = from });
                RebuildLater();
            }, minWidth: 220), keywords: kw);
        Add(page, section, AppStrings.Opt_QuickSources, AppStrings.Opt_QuickSources_Desc, QuickSourcesText, keywords: kw, sub: true);
        Add(page, section, AppStrings.Opt_QuickDrives, AppStrings.Opt_QuickDrives_Desc, QuickDrivesPicker, keywords: kw, sub: true);
        Add(page, section, AppStrings.Opt_QuickWalkEntries, AppStrings.Opt_QuickWalkEntries_Desc, () => Combo(
            [.. WalkEntryChoices.Select(n => (n, n.ToString("N0", CultureInfo.CurrentCulture)))], Nearest(WalkEntryChoices, Current.QuickOpen.WalkEntries),
            n => UpdateQuickOpen(q => q with { WalkEntries = n }), minWidth: 140), keywords: kw, sub: true);
        Add(page, section, AppStrings.Opt_QuickWalkSeconds, AppStrings.Opt_QuickWalkSeconds_Desc, () => Combo(
            [.. WalkSecondChoices.Select(n => (n, Loc.Format(AppStrings.Opt_QuickWalkSeconds_Value, n)))], Nearest(WalkSecondChoices, Current.QuickOpen.WalkSeconds),
            n => UpdateQuickOpen(q => q with { WalkSeconds = n }), minWidth: 140), keywords: kw, sub: true);
        Add(page, section, AppStrings.Opt_QuickSkipHidden, AppStrings.Opt_QuickSkipHidden_Desc,
            () => Toggle(Current.QuickOpen.SkipHidden, on => UpdateQuickOpen(q => q with { SkipHidden = on })), keywords: kw);
        Add(page, section, AppStrings.Opt_QuickSkipSystem, AppStrings.Opt_QuickSkipSystem_Desc,
            () => Toggle(Current.QuickOpen.SkipSystem, on => UpdateQuickOpen(q => q with { SkipSystem = on })), keywords: kw);
        Add(page, section, AppStrings.Opt_QuickSkipWindows, AppStrings.Opt_QuickSkipWindows_Desc,
            () => Toggle(Current.QuickOpen.SkipWindowsFolders, on => UpdateQuickOpen(q => q with { SkipWindowsFolders = on })), keywords: kw);
        Add(page, section, AppStrings.Opt_QuickSkipFolders, AppStrings.Opt_QuickSkipFolders_Desc, () => new Panel(), keywords: kw, below: QuickSkipFoldersEditor);
    }

    private static int Nearest(int[] choices, int value) => choices.MinBy(c => Math.Abs((long)c - value));

    private Control QuickSourcesText()
    {
        var text = Text(AppStrings.Opt_QuickSources_Reading, 12, brush: "Brush.SecondaryText", wrap: true);
        text.MaxWidth = 360;
        var sources = _services.FileNames;
        var settings = Current.QuickOpen;
        _ = Task.Run(() =>
        {
            var everything = settings.FilesFrom == QuickOpenFilesFrom.Best && sources.Everything?.IsAvailable() == true;
            var index = settings.FilesFrom is QuickOpenFilesFrom.Best or QuickOpenFilesFrom.NoEverything && sources.WindowsIndex?.IsAvailable() == true
                ? sources.WindowsIndex!.CoveredFolders() : null;
            var drives = sources.LocalDrives().Select(d => d[..2].ToUpperInvariant()).Where(d => !settings.SkipDrives.Contains(d)).ToList();
            var lines = new List<string>
            {
                everything ? AppStrings.Opt_QuickSource_EverythingOn : AppStrings.Opt_QuickSource_EverythingOff,
                index is null ? AppStrings.Opt_QuickSource_IndexOff : Loc.Format(AppStrings.Opt_QuickSource_IndexOn, string.Join(", ", index)),
            };
            if (!everything)
            {
                lines.Add(drives.Count == 0 ? AppStrings.Opt_QuickSource_WalkNone : Loc.Format(AppStrings.Opt_QuickSource_Walk, string.Join(' ', drives)));
            }

            return string.Join('\n', lines);
        }).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                text.Text = t.Result;
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
        return text;
    }

    private Control QuickDrivesPicker()
    {
        var drives = _services.FileNames.LocalDrives().Select(d => d[..2].ToUpperInvariant()).Distinct().ToList();
        if (drives.Count == 0)
        {
            return Text(AppStrings.Opt_QuickDrives_None, 12, brush: "Brush.SecondaryText");
        }

        var panel = new WrapPanel();
        foreach (var drive in drives)
        {
            var button = new ToggleButton
            {
                Content = drive,
                IsChecked = !Current.QuickOpen.SkipDrives.Contains(drive),
                MinWidth = 40,
                Margin = new Thickness(0, 0, 4, 4),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            Avalonia.Automation.AutomationProperties.SetName(button, $"{AppStrings.Opt_QuickDrives} {drive}");
            button.IsCheckedChanged += (_, _) => UpdateQuickOpen(q => q with
            {
                SkipDrives = button.IsChecked == true ? [.. q.SkipDrives.Where(d => d != drive)] : [.. q.SkipDrives.Where(d => d != drive), drive],
            });
            panel.Children.Add(button);
        }

        return panel;
    }

    private Control QuickSkipFoldersEditor()
    {
        var list = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        foreach (var folder in Current.QuickOpen.SkipFolders)
        {
            var chip = new Button { Content = folder + "  ✕", Padding = new Thickness(10, 3), CornerRadius = new CornerRadius(4) };
            ToolTip.SetTip(chip, AppStrings.Opt_QuickSkipFolders_Remove);
            chip.Click += (_, _) =>
            {
                UpdateQuickOpen(q => q with { SkipFolders = [.. q.SkipFolders.Where(x => x != folder)] });
                RebuildLater();
            };
            list.Children.Add(chip);
        }

        var add = new TextBox { Width = 280, PlaceholderText = AppStrings.Opt_QuickSkipFolders_Add };
        Avalonia.Automation.AutomationProperties.SetName(add, AppStrings.Opt_QuickSkipFolders_Add);
        add.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            if (WalkFilter.Clean(add.Text) is { } folder)
            {
                UpdateQuickOpen(q => q with { SkipFolders = WalkFilter.CleanList([.. q.SkipFolders, folder]) });
                RebuildLater();
            }
            else if ((add.Text ?? "").Trim().Length > 0)
            {
                ToolTip.SetTip(add, AppStrings.Opt_QuickSkipFolders_Bad);
                ToolTip.SetIsOpen(add, true);
                DispatcherTimer.RunOnce(() => ToolTip.SetIsOpen(add, false), TimeSpan.FromSeconds(3));
            }
        };
        list.Children.Add(add);
        return list;
    }
}
