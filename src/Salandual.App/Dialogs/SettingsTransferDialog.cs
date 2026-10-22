// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/SettingsTransferDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Commands;
using Salandual.Core.Resources;

using Salandual.Core.Settings;

namespace Salandual.App.Dialogs;

public sealed class SettingsTransferDialog : Window
{
    private readonly List<(CheckBox Box, object Value)> _ticks = [];
    private readonly Func<object?> _result;

    private SettingsTransferDialog(string title, string lead, Func<SettingsTransferDialog, IEnumerable<Control>> rows, string note, string primary, Func<IReadOnlyList<object>, object?> result)
    {
        Title = title;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 720;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.WindowBackground");

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 6, 20, 16) };
        body.Children.Add(OptionsUi.Secondary(lead, 13));
        StackPanel? list = null;
        foreach (var row in rows(this))
        {
            if (row is TextBlock { Tag: Heading })
            {
                row.Margin = new Thickness(0, 4, 0, -4);
                body.Children.Add(row);
                list = null;
                continue;
            }

            if (list is null)
            {
                list = new StackPanel();
                body.Children.Add(new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = list }
                    .Res(Border.BackgroundProperty, "Brush.PanelBackground").Res(Border.BorderBrushProperty, "Brush.Border"));
            }
            else
            {
                list.Children.Add(new Border { Height = 1 }.Res(Border.BackgroundProperty, "Brush.Border"));
            }

            list.Children.Add(row);
        }

        Primary = new Button { Content = primary, Padding = new Thickness(16, 5) };
        Primary.Classes.Add("accent");
        Primary.Click += (_, _) => Close(_result!());
        var cancel = new Button { Content = AppStrings.Common_Cancel, Padding = new Thickness(16, 5) };
        cancel.Click += (_, _) => Close(null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { cancel, Primary } };
        var noteText = OptionsUi.Secondary(note, 12);
        noteText.VerticalAlignment = VerticalAlignment.Center;
        noteText.Margin = new Thickness(0, 0, 16, 0);
        var footerRow = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        footerRow.Children.Add(buttons);
        footerRow.Children.Add(noteText);
        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerRow }
            .Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = TitleBarParts.ForDialog(this, root);
        _result = () => result([.. _ticks.Where(t => t.Box.IsChecked == true).Select(t => t.Value)]);
        Opened += (_, _) => Primary.Focus();
    }

    internal Button Primary { get; }

    internal IReadOnlyList<(CheckBox Box, object Value)> Ticks => _ticks;

    public static SettingsTransferDialog ForExport(AppSettings settings) => new(
        AppStrings.Xfer_Export_Title,
        Loc.Format(AppStrings.Xfer_Export_Lead, SettingsTransfer.Extension),
        d =>
        [
            d.Tick(null, AppStrings.Xfer_All, AppStrings.Xfer_All_Desc, on: true, enabled: false, right: AppStrings.Xfer_Always),
            d.Tick(SettingsPart.Themes, AppStrings.Xfer_Themes, Loc.Plural(settings.Appearance.Themes.Count, AppStrings.Xfer_Themes_One, AppStrings.Xfer_Themes_Other), on: true),
            d.Tick(SettingsPart.HotPaths, AppStrings.Xfer_HotPaths, Loc.Plural(settings.HotPaths.Paths.Count, AppStrings.Xfer_HotPaths_One, AppStrings.Xfer_HotPaths_Other), on: true),
            d.Tick(SettingsPart.History, AppStrings.Xfer_History, AppStrings.Xfer_History_Desc, on: false),
        ],
        AppStrings.Xfer_Export_Note,
        AppStrings.Xfer_SaveAs,
        ticked => new ExportChoice(ticked.Contains(SettingsPart.Themes), ticked.Contains(SettingsPart.HotPaths), ticked.Contains(SettingsPart.History)));

    public static SettingsTransferDialog ForImport(SettingsImport import)
    {
        var lead = import is { Exported: { } when, ExportedBy: { } by }
            ? Loc.Format(AppStrings.Xfer_Import_Lead, Path.GetFileName(import.File), when.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), by)
            : Loc.Format(AppStrings.Xfer_Import_LeadPlain, Path.GetFileName(import.File));

        SettingsTransferDialog? dialog = null;
        dialog = new SettingsTransferDialog(
            AppStrings.Xfer_Import_Title,
            lead,
            d =>
            {
                var rows = new List<Control>();
                foreach (var part in import.Parts)
                {
                    var changes = import.Changes.Where(c => c.Part == part).ToList();
                    var programs = part == SettingsPart.Programs;
                    rows.Add(d.Tick(part, PartTitle(part), null, on: !programs,
                        right: programs ? Loc.Plural(changes.Count, AppStrings.Xfer_Programs_One, AppStrings.Xfer_Programs_Other) : Loc.Plural(changes.Count, AppStrings.Xfer_Changes_One, AppStrings.Xfer_Changes_Other),
                        detail: Summary(changes, programs ? int.MaxValue : 3), warning: programs ? AppStrings.Xfer_Programs_Warn : null));
                }

                if (import.Same.Count > 0)
                {
                    rows.Add(d.Tick(null, string.Join(", ", import.Same.Select(PartTitle)), AppStrings.Xfer_Same, on: false, enabled: false));
                }

                return rows;
            },
            AppStrings.Xfer_KeptNote,
            "",
            ticked => ticked.Cast<SettingsPart>().ToHashSet());
        void Count()
        {
            var parts = dialog._ticks.Where(t => t.Box.IsChecked == true).Select(t => t.Value).OfType<SettingsPart>().ToHashSet();
            var n = import.Changes.Count(c => parts.Contains(c.Part));
            dialog.Primary.Content = Loc.Plural(n, AppStrings.Xfer_ImportButton_One, AppStrings.Xfer_ImportButton_Other);
            dialog.Primary.IsEnabled = n > 0;
        }

        foreach (var (box, _) in dialog._ticks)
        {
            box.IsCheckedChanged += (_, _) => Count();
        }

        Count();
        return dialog;
    }

    public static SettingsTransferDialog ForResetAll(AppSettings settings) => new(
        AppStrings.Xfer_Reset_Title,
        AppStrings.Xfer_Reset_Lead,
        d =>
        [
            d.Tick(SettingsPart.Themes, AppStrings.Xfer_Themes, Loc.Plural(settings.Appearance.Themes.Count, AppStrings.Xfer_KeepThemes_One, AppStrings.Xfer_KeepThemes_Other), on: true),
            d.Tick(SettingsPart.HotPaths, AppStrings.Xfer_KeepHotPaths, Loc.Plural(settings.HotPaths.Paths.Count, AppStrings.Xfer_HotPathCount_One, AppStrings.Xfer_HotPathCount_Other), on: true),
            d.Tick(SettingsPart.History, AppStrings.Xfer_KeepHistory, null, on: true),
        ],
        AppStrings.Xfer_KeptNote,
        AppStrings.Xfer_ResetButton,
        ticked => new ResetChoice(ticked.Contains(SettingsPart.Themes), ticked.Contains(SettingsPart.HotPaths), ticked.Contains(SettingsPart.History)));

    public const string QuickSearchTick = "quickSearch", RightButtonTick = "rightButton";

    public static (QuickSearchStart QuickSearch, RightClickUnselected RightClick) GoesWith(KeyMapName name, AppSettings settings)
        => name == KeyMapName.TotalCommander
            ? (QuickSearchStart.CtrlAltLetter, RightClickUnselected.RightButtonSelects)
            : (QuickSearchStart.Letters, settings.Mouse.RightClickUnselected == RightClickUnselected.RightButtonSelects ? RightClickUnselected.MenuForItem : settings.Mouse.RightClickUnselected);

    internal static string QuickSearchName(QuickSearchStart start) => start == QuickSearchStart.CtrlAltLetter ? AppStrings.Opt_QuickSearch_CtrlAlt : AppStrings.Opt_QuickSearch_Letters;

    internal static string RightClickName(RightClickUnselected choice) => choice switch
    {
        RightClickUnselected.SelectOnlyItem => AppStrings.Opt_RightUnselected_B,
        RightClickUnselected.RightButtonSelects => AppStrings.Opt_RightUnselected_C,
        _ => AppStrings.Opt_RightUnselected_A,
    };

    public static SettingsTransferDialog ForKeyMap(CommandRegistry commands, KeyMapName name, AppSettings settings, Func<string, bool> isBuilt)
    {
        var from = commands.KeyMap;
        var title = KeyMaps.Title(name);
        var changes = commands.Differences(from, name);
        var notYet = commands.KeysOf(name).NotYet;
        var lead = name == KeyMapName.Salamander
            ? Loc.Format(AppStrings.KeyMapDlg_LeadBack, changes.Count)
            : Loc.Format(AppStrings.KeyMapDlg_Lead, changes.Count, title);
        var (quick, right) = GoesWith(name, settings);
        return new SettingsTransferDialog(
            Loc.Format(AppStrings.KeyMapDlg_Title, title),
            lead,
            d =>
            {
                var rows = new List<Control> { Changes(commands, changes, from, name, isBuilt) };
                var goes = new List<Control>();
                if (settings.Panels.QuickSearch != quick)
                {
                    goes.Add(d.Tick(QuickSearchTick, Loc.Format(AppStrings.KeyMapDlg_QuickSearch, QuickSearchName(quick)),
                        quick == QuickSearchStart.CtrlAltLetter ? AppStrings.KeyMapDlg_QuickSearch_CtrlAlt_Desc : AppStrings.KeyMapDlg_QuickSearch_Desc,
                        on: true, right: Loc.Format(AppStrings.KeyMapDlg_Now, QuickSearchName(settings.Panels.QuickSearch))));
                }

                if (settings.Mouse.RightClickUnselected != right)
                {
                    goes.Add(d.Tick(RightButtonTick, Loc.Format(AppStrings.KeyMapDlg_RightClick, RightClickName(right)), AppStrings.KeyMapDlg_RightClick_Desc,
                        on: true, right: Loc.Format(AppStrings.KeyMapDlg_Now, RightClickName(settings.Mouse.RightClickUnselected))));
                }

                if (goes.Count > 0)
                {
                    rows.Add(SectionHeading(Loc.Format(AppStrings.KeyMapDlg_GoesWith, title)));
                    rows.AddRange(goes);
                }

                if (notYet.Count > 0)
                {
                    rows.Add(SectionHeading(AppStrings.KeyMapDlg_NotYet));
                    var examples = string.Join(", ", notYet.Take(5).Select(k => k.Title + " (" + k.Chord + ")"));
                    var text = OptionsUi.Secondary(Loc.Format(AppStrings.KeyMapDlg_NotYet_Desc, notYet.Count, title, examples), 12.5);
                    text.Margin = new Thickness(12, 8);
                    rows.Add(text);
                }

                return rows;
            },
            AppStrings.KeyMapDlg_Note,
            Loc.Format(AppStrings.KeyMapDlg_Use, title),
            ticked => ticked.OfType<string>().ToHashSet());
    }

    private static Control Changes(CommandRegistry commands, IReadOnlyList<(KeyChord Chord, string? Before, string? After)> changes, KeyMapName from, KeyMapName to, Func<string, bool> isBuilt)
    {
        const int Shown = 8;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*"), ColumnSpacing = 16, RowSpacing = 4, Margin = new Thickness(12, 8) };
        var (_, fromNotYet) = commands.KeysOf(from);
        var (_, toNotYet) = commands.KeysOf(to);
        void Cell(int row, int column, Control control)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        TextBlock Does(string? target, KeyChord chord, IReadOnlyList<NotYetKey> notYet)
        {
            var (text, dim) = target switch
            {
                null => ("—", true),
                "" => (Loc.Format(AppStrings.KeyMapDlg_NotYetCell, notYet.First(k => k.Chord == chord).Title), true),
                _ when !isBuilt(target) => (Loc.Format(AppStrings.KeyMapDlg_NotYetCell, commands[target].Title), true),
                _ => (commands[target].Title, false),
            };
            return OptionsUi.Text(text, 13, brush: dim ? "Brush.TertiaryText" : "Brush.Text", wrap: true);
        }

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Cell(0, 0, OptionsUi.Text(AppStrings.KeyMapDlg_Key, 11, FontWeight.SemiBold, "Brush.SecondaryText"));
        Cell(0, 1, OptionsUi.Text(Loc.Format(AppStrings.KeyMapDlg_NowColumn, KeyMaps.Title(from)), 11, FontWeight.SemiBold, "Brush.SecondaryText"));
        Cell(0, 2, OptionsUi.Text(Loc.Format(AppStrings.KeyMapDlg_ThenColumn, KeyMaps.Title(to)), 11, FontWeight.SemiBold, "Brush.SecondaryText"));
        var row = 1;
        static bool Works(string? id, Func<string, bool> built) => id is { Length: > 0 } && built(id);
        foreach (var (chord, before, after) in changes.OrderBy(c => Works(c.Before, isBuilt) && Works(c.After, isBuilt) ? 0 : Works(c.After, isBuilt) ? 1 : 2).Take(Shown))
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var key = OptionsUi.Text(chord.ToString(), 12).Res(TextBlock.FontFamilyProperty, "Font.Mono");
            Cell(row, 0, key);
            Cell(row, 1, Does(before, chord, fromNotYet));
            Cell(row, 2, Does(after, chord, toNotYet));
            row++;
        }

        if (changes.Count > Shown)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var more = OptionsUi.Secondary(Loc.Format(AppStrings.KeyMapDlg_More, changes.Count - Shown), 12);
            Grid.SetColumnSpan(more, 3);
            Cell(row, 0, more);
        }

        return grid;
    }

    internal static string PartTitle(SettingsPart part) => part switch
    {
        SettingsPart.Appearance => AppStrings.OptCat_Appearance,
        SettingsPart.Themes => AppStrings.OptCat_Themes,
        SettingsPart.Layout => AppStrings.OptCat_Layout,
        SettingsPart.Panels => AppStrings.OptCat_Panels,
        SettingsPart.Keyboard => AppStrings.OptCat_Keyboard,

        SettingsPart.HotPaths => AppStrings.OptCat_HotPaths,
        SettingsPart.Operations => AppStrings.OptCat_Operations,
        SettingsPart.Confirmations => AppStrings.OptCat_Confirm,
        SettingsPart.Viewers => AppStrings.OptCat_Viewers,
        SettingsPart.Editors => AppStrings.OptCat_Editors,
        SettingsPart.Compare => AppStrings.OptCat_Compare,
        SettingsPart.General => AppStrings.OptCat_General,
        SettingsPart.Advanced => AppStrings.OptCat_Advanced,
        SettingsPart.History => AppStrings.Xfer_History,
        _ => AppStrings.Xfer_Programs,
    };

    private static TextBlock Summary(IReadOnlyList<SettingChange> changes, int shown)
    {
        var text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.FontFamilyProperty, "Font.Mono").Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        for (var i = 0; i < Math.Min(shown, changes.Count); i++)
        {
            if (i > 0)
            {
                text.Inlines!.Add(new Run(shown == int.MaxValue ? "\n" : " · "));
            }

            var c = changes[i];
            text.Inlines!.Add(new Run(c.Name + " "));
            text.Inlines.Add(new Run(c.Old) { TextDecorations = TextDecorations.Strikethrough });
            text.Inlines.Add(new Run(" → " + c.New));
        }

        if (changes.Count > shown)
        {
            text.Inlines!.Add(new Run(" · " + Loc.Format(AppStrings.Xfer_AndMore, changes.Count - shown)));
        }

        return text;
    }

    private const string Heading = "heading";

    private static TextBlock SectionHeading(string text)
    {
        var heading = OptionsUi.Text(text, 13, FontWeight.SemiBold);
        heading.Tag = Heading;
        return heading;
    }

    private Control Tick(object? value, string title, string? description, bool on, bool enabled = true, string? right = null, Control? detail = null, string? warning = null)
    {
        var box = new CheckBox { IsChecked = on, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Top, MinWidth = 0, Margin = new Thickness(0, -4, 0, 0) };
        Avalonia.Automation.AutomationProperties.SetName(box, title);
        if (value is not null)
        {
            _ticks.Add((box, value));
        }


        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(OptionsUi.Text(title, 13, brush: enabled ? "Brush.Text" : "Brush.SecondaryText", wrap: true));
        if (description is not null)
        {
            text.Children.Add(OptionsUi.Secondary(description, 12));
        }

        if (detail is not null)
        {
            text.Children.Add(detail);
        }

        if (warning is not null)
        {
            text.Children.Add(OptionsUi.Text(warning, 12, brush: "Brush.Warning", wrap: true));
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 4, Margin = new Thickness(12, 8) };
        grid.Children.Add(box);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (right is not null)
        {
            var count = OptionsUi.Secondary(right, 12);
            count.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(count, 2);
            grid.Children.Add(count);
        }

        grid.Background = Brushes.Transparent;
        grid.PointerPressed += (_, e) =>
        {
            if (box.IsEnabled && e.Source is not CheckBox && e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed)
            {
                box.IsChecked = box.IsChecked != true;
                e.Handled = true;
            }
        };
        return grid;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
