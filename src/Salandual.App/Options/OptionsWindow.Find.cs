// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Find.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Salandual.App.Find;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    private void AddFind()
    {
        const string page = "compare";
        var kw = AppStrings.Opt_Find_Kw;
        var find = AppStrings.OptSec_Find;
        Add(page, find, AppStrings.Opt_FindDefaults, AppStrings.Opt_FindDefaults_Desc, SearchDefaultsEditor, wide: true, keywords: kw);
        Add(page, find, AppStrings.Opt_FindResultsPanel, AppStrings.Opt_FindResultsPanel_Desc,
            () => Combo<ResultsPanel>([(ResultsPanel.Active, AppStrings.Opt_FindResultsActive), (ResultsPanel.Other, AppStrings.Opt_FindResultsOther)], Current.Find.ResultsPanel,
                panel => UpdateFind(f => f with { ResultsPanel = panel }), minWidth: 160), keywords: kw);
        Add(page, find, AppStrings.Opt_FindSaved, AppStrings.Opt_FindSaved_Desc, SavedSearchesEditor, wide: true, keywords: kw);
        Add(page, find, AppStrings.Opt_FindFocusResults, AppStrings.Opt_FindFocusResults_Desc,
            () => Toggle(Current.Find.FocusResults, on => UpdateFind(f => f with { FocusResults = on })), keywords: kw);
        Add(page, find, AppStrings.Opt_FindViewAtMatch, AppStrings.Opt_FindViewAtMatch_Desc,
            () => Toggle(Current.Find.ViewAtMatch, on => UpdateFind(f => f with { ViewAtMatch = on })), keywords: kw);
        Add(page, find, AppStrings.Opt_FindViewerText, AppStrings.Opt_FindViewerText_Desc,
            () => Toggle(Current.Find.ViewerKeepsText, on => UpdateFind(f => f with { ViewerKeepsText = on })), keywords: kw);
        Add(page, find, AppStrings.Opt_FindSkipped, AppStrings.Find_SkippedHelp, SkippedFoldersEditor, wide: true, keywords: kw);
        var kept = Current.Find.Named.Count + Current.Find.LookIn.Count + Current.Find.Texts.Count;
        Add(page, find, AppStrings.Opt_FindHistory, Loc.Format(AppStrings.Opt_FindHistory_Desc, Loc.Plural(kept, AppStrings.Count_Items_One, AppStrings.Count_Items_Other)), () =>
        {
            var clear = new Button { Content = AppStrings.Opt_FindHistory_Clear, IsEnabled = kept > 0 };
            clear.Click += (_, _) =>
            {
                UpdateFind(f => f with { Named = [], LookIn = [], Texts = [] });
                RebuildLater();
            };
            return clear;
        }, keywords: kw);
    }

    private void AddCompare()
    {
        const string page = "compare";
        var kw = AppStrings.Opt_CompareDirs_Kw;
        var section = AppStrings.OptSec_CompareDirs;
        Add(page, section, AppStrings.Opt_CompareSelect, AppStrings.Opt_CompareSelect_Desc,
            () => Toggle(Current.Compare.SelectInPanels, on => UpdateCompare(c => c with { SelectInPanels = on })), keywords: kw);
        Add(page, section, AppStrings.Opt_CompareList, AppStrings.Opt_CompareList_Desc,
            () => Toggle(Current.Compare.ShowList, on => UpdateCompare(c => c with { ShowList = on })), keywords: kw);
        Add(page, section, AppStrings.Opt_CompareMarks, AppStrings.Opt_CompareMarks_Desc,
            () => Toggle(Current.Compare.ShowMarks, on => UpdateCompare(c => c with { ShowMarks = on })), keywords: kw);
        Add(page, section, AppStrings.Opt_CompareBar, AppStrings.Opt_CompareBar_Desc,
            () => Toggle(Current.Compare.ShowBar, on => UpdateCompare(c => c with { ShowBar = on })), keywords: kw);

        _compareStarts.Clear();
        Add(page, section, AppStrings.Opt_CompareListStart, AppStrings.Opt_CompareListStart_Desc, () => Combo<Salandual.Core.Compare.CompareView>(
            [(Salandual.Core.Compare.CompareView.All, AppStrings.Opt_CompareListAll), (Salandual.Core.Compare.CompareView.Different, AppStrings.Opt_CompareListDifferentChip), (Salandual.Core.Compare.CompareView.Same, AppStrings.Opt_CompareListSame)],
            Current.Compare.ListView, view => UpdateCompare(c => c with { ListView = view }), minWidth: 180).Also(CompareStart), keywords: kw);
        Add(page, section, AppStrings.Opt_CompareKindStart, AppStrings.Opt_CompareKindStart_Desc, () => Combo(
            [.. Enum.GetValues<Salandual.Core.Compare.DifferenceFilter>().Select(k => (k, Compare.DifferencesWindow.KindName(k)))],
            Current.Compare.ListKind, kind => UpdateCompare(c => c with { ListKind = kind }), minWidth: 300).Also(CompareStart), keywords: kw);
        Add(page, section, AppStrings.Opt_CompareNames, AppStrings.Opt_CompareNames_Desc, () =>
        {
            var names = new TextBox { Text = Current.Compare.ListNames, Width = 220, PlaceholderText = AppStrings.Compare_NamesHint }.Also(CompareStart);
            Avalonia.Automation.AutomationProperties.SetName(names, AppStrings.Opt_CompareNames);
            names.TextChanged += (_, _) => UpdateCompare(c => c with { ListNames = (names.Text ?? "").Trim() });
            var not = new CheckBox { Content = AppStrings.Opt_CompareNamesNot, IsChecked = Current.Compare.ListNamesNot }.Also(CompareStart);
            not.IsCheckedChanged += (_, _) => UpdateCompare(c => c with { ListNamesNot = not.IsChecked == true });
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { names, not } };
        }, keywords: kw);
        Add(page, section, AppStrings.Opt_CompareRemember, AppStrings.Opt_CompareRemember_Desc, () => Toggle(Current.Compare.ListRemember, on =>
        {
            UpdateCompare(c => c with { ListRemember = on });
            foreach (var control in _compareStarts)
            {
                control.IsEnabled = !on;
            }
        }), keywords: kw);
    }

    private readonly List<Control> _compareStarts = [];

    private void CompareStart(Control control)
    {
        control.IsEnabled = !Current.Compare.ListRemember;
        _compareStarts.Add(control);
    }

    private void UpdateCompare(Func<CompareSettings, CompareSettings> change) => Settings.Update(s => s with { Compare = change(s.Compare) });

    private void UpdateFind(Func<FindSettings, FindSettings> change) => Settings.Update(s => s with { Find = change(s.Find) });

    internal FindWindow? SearchDefaultsWindow { get; private set; }

    private Control SearchDefaultsEditor()
    {
        var line = Text(SearchDefaultsText(Current.Find.Defaults), 12.5, wrap: true);
        var change = new Button { Content = AppStrings.Opt_FindDefaults_Change };
        change.Click += async (_, _) =>
        {
            if (SearchDefaultsWindow is { } open)
            {
                open.Activate();
                return;
            }

            var request = new FindRequest("")
            {
                LocalDrives = () => [.. Salandual.Platform.Windows.FileSystem.Drives.List().Where(d => d.Kind is Salandual.Platform.Windows.FileSystem.DriveKind.Fixed or Salandual.Platform.Windows.FileSystem.DriveKind.Removable).Select(d => d.Root)],
            };
            var window = SearchDefaultsWindow = new FindWindow(Settings, request, editDefaults: true);
            try
            {
                await window.ShowDialog(this);
            }
            finally
            {
                SearchDefaultsWindow = null;
            }

            RebuildLater();
        };
        var builtIn = new Button { Content = AppStrings.Find_BuiltIn, IsEnabled = !Current.Find.Defaults.IsBuiltIn() };
        ToolTip.SetTip(builtIn, AppStrings.Find_BuiltInTip);
        builtIn.Click += (_, _) =>
        {
            UpdateFind(f => f with { Defaults = new SearchDefaults() });
            RebuildLater();
        };
        return new StackPanel { Spacing = 8, Children = { line, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { change, builtIn } } } };
    }

    internal static string SearchDefaultsText(SearchDefaults d, bool brief = false)
    {
        var parts = new List<string> { d.Named.Length > 0 ? d.Named : "*.*", d.LookIn.Length > 0 ? d.LookIn : AppStrings.Find_ActivePanelFolder };
        if (!brief || d.Depth is not null)
        {
            parts.Add(d.Depth switch { null => AppStrings.Find_DepthAll, 0 => AppStrings.Find_DepthNone, var n => Loc.Format(AppStrings.Find_DownTo, n) });
        }
        var has = d.Texts.Where(t => t.Contains && t.Text.Length > 0).Select(t => "“" + t.Text + "”").ToList();
        var not = d.Texts.Where(t => !t.Contains && t.Text.Length > 0).Select(t => "“" + t.Text + "”").ToList();
        if (has.Count > 0)
        {
            parts.Add(Loc.Format(AppStrings.Find_SumContaining, string.Join(d.AllTexts ? AppStrings.Find_TitleAnd : AppStrings.Find_TitleOr, has)));
        }

        if (not.Count > 0)
        {
            parts.Add(Loc.Format(AppStrings.Find_SumNot, string.Join(", ", not)));
        }

        foreach (var (on, name) in new[] { (d.InsideZip, AppStrings.Find_InsideArchives), (d.MatchCase, AppStrings.Find_MatchCase), (d.WholeWords, AppStrings.Find_WholeWords) })
        {
            if (on)
            {
                parts.Add(name);
            }
        }

        if (!d.Encodings.HasFlag(Salandual.Core.Search.SearchEncodings.Auto))
        {
            parts.Add(FindWindow.EncodingsText(d.Encodings));
        }

        if (FindWindow.AdvancedText(d) is { } advanced)
        {
            parts.Add(advanced);
        }
        else if (!brief)
        {
            parts.Add(AppStrings.Find_NoAdvanced);
        }

        return string.Join(" · ", parts);
    }

    private Control SavedSearchesEditor()
    {
        var saved = Current.Find.Saved;
        var list = new StackPanel { Spacing = 2 };
        if (saved.Count == 0)
        {
            list.Children.Add(Text(AppStrings.Opt_FindSavedNone, 12.5, brush: "Brush.TertiaryText", wrap: true));
        }

        for (var i = 0; i < saved.Count; i++)
        {
            var index = i;
            var search = saved[i];
            var name = Text(search.Name, 13);
            var what = Text(FindWindow.SavedText(search.Search), 12, brush: "Brush.TertiaryText");
            what.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            var key = Text(i < 9 ? "Ctrl+" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "", 11.5, brush: "Brush.TertiaryText");
            key.VerticalAlignment = VerticalAlignment.Center;
            var up = Small("↑", AppStrings.Opt_ViewerMask_Up, () => Move(index, -1), i > 0);
            var down = Small("↓", AppStrings.Opt_ViewerMask_Down, () => Move(index, 1), i < saved.Count - 1);
            var rename = Small("✎", AppStrings.Find_SavedRename, () => _ = RenameAsync(search.Name));
            var remove = Small("✕", AppStrings.Find_SavedRemove, () =>
            {
                FindWindow.RemoveSaved(Settings, search.Name);
                RebuildLater();
            });
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 4, Margin = new Thickness(0, 2) };
            row.Children.Add(new StackPanel { Children = { name, what }, VerticalAlignment = VerticalAlignment.Center });
            var column = 1;
            foreach (var control in new Control[] { key, up, down, rename, remove })
            {
                Grid.SetColumn(control, column++);
                row.Children.Add(control);
            }

            list.Children.Add(row);
        }

        return list;

        void Move(int index, int by)
        {
            UpdateFind(f =>
            {
                var items = f.Saved.ToList();
                (items[index], items[index + by]) = (items[index + by], items[index]);
                return f with { Saved = items };
            });
            RebuildLater();
        }

        async Task RenameAsync(string old)
        {
            var dialog = new Dialogs.SaveSearchDialog(old);
            if (await dialog.ShowDialog<string?>(this) is { } name && name != old)
            {
                FindWindow.RenameSaved(Settings, old, name);
                RebuildLater();
            }
        }

        static Button Small(string text, string tip, Action run, bool enabled = true)
        {
            var button = new Button { Content = text, Padding = new Thickness(8, 3), IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(button, tip);
            Avalonia.Automation.AutomationProperties.SetName(button, tip);
            button.Click += (_, _) => run();
            return button;
        }
    }

    private Control SkippedFoldersEditor()
    {
        var list = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        foreach (var folder in Current.Find.SkippedFolders)
        {
            var chip = new Button { Content = folder + "  ✕", Padding = new Thickness(10, 3), CornerRadius = new CornerRadius(4) };
            ToolTip.SetTip(chip, AppStrings.Find_RemoveSkipped);
            chip.Click += (_, _) =>
            {
                UpdateFind(f => f with { SkippedFolders = [.. f.SkippedFolders.Where(x => x != folder)] });
                RebuildLater();
            };
            list.Children.Add(chip);
        }

        var add = new TextBox { Width = 240, PlaceholderText = AppStrings.Find_AddSkipped };
        Avalonia.Automation.AutomationProperties.SetName(add, AppStrings.Find_AddSkipped);
        add.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (add.Text ?? "").Trim() is { Length: > 0 } folder)
            {
                UpdateFind(f => f with { SkippedFolders = [.. f.SkippedFolders, folder] });
                e.Handled = true;
                RebuildLater();
            }
        };
        list.Children.Add(add);
        return list;
    }
}
