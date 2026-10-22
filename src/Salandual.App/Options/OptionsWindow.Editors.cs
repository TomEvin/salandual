// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Editors.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Salandual.App.Editor;
using Salandual.App.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    internal Grid? EditorMaskGrid { get; private set; }

    private void AddEditors()
    {
        const string page = "editors";
        var kw = AppStrings.Opt_Editors_Kw;
        Add(page, AppStrings.OptSec_EditorMasks, null, null, EditorMaskTable, wide: true, keywords: kw);

        var window = AppStrings.OptSec_ViewerWindow;
        Add(page, window, AppStrings.Opt_EditorClose, AppStrings.Opt_EditorClose_Desc, () => Segmented(
            [(EditorClose.CtrlF4, "Ctrl+F4"), (EditorClose.CtrlF4OrEsc, AppStrings.Opt_EditorClose_Esc)],
            Current.Editors.Close, v => UpdateEditors(x => x with { Close = v })), keywords: kw);
        Add(page, window, AppStrings.Opt_EditorDisk, AppStrings.Opt_EditorDisk_Desc, () => Segmented(
            [(EditorDiskChange.Ask, AppStrings.Opt_EditorDisk_Ask), (EditorDiskChange.Reload, AppStrings.Opt_EditorDisk_Reload)],
            Current.Editors.DiskChange, v => UpdateEditors(x => x with { DiskChange = v })), keywords: kw);

        var text = AppStrings.OptSec_EditorText;
        Add(page, text, AppStrings.Opt_MonoFont, AppStrings.Opt_EditorFont_Desc, MonoFontPicker, wide: true, keywords: kw);
        Add(page, text, AppStrings.Opt_EditorIndent, AppStrings.Opt_EditorIndent_Desc, () => Segmented(
            [(IndentStyle.Spaces2, Core.Resources.Loc.Format(AppStrings.Editor_Spaces, 2)), (IndentStyle.Spaces4, Core.Resources.Loc.Format(AppStrings.Editor_Spaces, 4)), (IndentStyle.Tabs, AppStrings.Editor_Tabs)],
            Current.Editors.Indent, v => UpdateEditors(x => x with { Indent = v })), keywords: kw);
        Add(page, text, AppStrings.Opt_ViewerWrap, null, () => Toggle(Current.Editors.Wrap, on => UpdateEditors(x => x with { Wrap = on })), keywords: kw);
        Add(page, text, AppStrings.Viewer_LineNumbers, null, () => Toggle(Current.Editors.LineNumbers, on => UpdateEditors(x => x with { LineNumbers = on })), keywords: kw);
        Add(page, text, AppStrings.Opt_EditorCurrentLine, null, () => Toggle(Current.Editors.HighlightCurrentLine, on => UpdateEditors(x => x with { HighlightCurrentLine = on })), keywords: kw);
        Add(page, text, AppStrings.Editor_ShowSpaces, null, () => Toggle(Current.Editors.ShowSpaces, on => UpdateEditors(x => x with { ShowSpaces = on })), keywords: kw);
        Add(page, text, AppStrings.Editor_Strip, AppStrings.Opt_EditorStrip_Desc, () => Toggle(Current.Editors.Strip, on => UpdateEditors(x => x with { Strip = on })), keywords: kw);

        var saving = AppStrings.OptSec_EditorSaving;
        Add(page, saving, AppStrings.Opt_EditorEncoding, AppStrings.Opt_EditorEncoding_Desc, () => Combo(
            [.. TextEncodings.All.Select(e => (e.Id, e.Name))], Current.Editors.NewEncoding, id => UpdateEditors(x => x with { NewEncoding = id }), 220), keywords: kw);
        Add(page, saving, AppStrings.Opt_EditorEol, null, () => Segmented(
            [(LineEnding.CrLf, "CRLF"), (LineEnding.Lf, "LF")], Current.Editors.NewLineEnding, v => UpdateEditors(x => x with { NewLineEnding = v })), keywords: kw);
        Add(page, saving, AppStrings.Opt_EditorTrim, null, () => Toggle(Current.Editors.TrimOnSave, on => UpdateEditors(x => x with { TrimOnSave = on })), keywords: kw);
        Add(page, saving, AppStrings.Opt_EditorFinal, null, () => Segmented(
            [(FinalNewline.Keep, AppStrings.Opt_EditorFinal_Keep), (FinalNewline.Always, AppStrings.Common_Always)],
            Current.Editors.FinalNewline, v => UpdateEditors(x => x with { FinalNewline = v })), keywords: kw);
        Add(page, saving, AppStrings.Opt_EditorBackup, AppStrings.Opt_EditorBackup_Desc, () => Toggle(Current.Editors.Backup, on => UpdateEditors(x => x with { Backup = on })), keywords: kw);

        var checking = AppStrings.OptSec_EditorChecking;
        Add(page, checking, AppStrings.Opt_EditorCheck, AppStrings.Opt_EditorCheck_Desc, () => Toggle(Current.Editors.Check, on => UpdateEditors(x => x with { Check = on })), keywords: kw);
        Add(page, checking, AppStrings.Opt_EditorFolding, AppStrings.Opt_EditorFolding_Desc, () => Toggle(Current.Editors.Folding, on => UpdateEditors(x => x with { Folding = on })), keywords: kw);
        Add(page, checking, AppStrings.Opt_EditorFormatWith, AppStrings.Opt_EditorFormatWith_Desc, () => Segmented(
            [(FormatIndent.Spaces2, AppStrings.Opt_EditorFormat_2), (FormatIndent.Spaces4, AppStrings.Opt_EditorFormat_4), (FormatIndent.Tabs, AppStrings.Editor_Tabs), (FormatIndent.AsFile, AppStrings.Opt_EditorFormat_AsFile)],
            Current.Editors.FormatIndent, v => UpdateEditors(x => x with { FormatIndent = v })), keywords: kw);

        var preview = AppStrings.OptSec_EditorPreview;
        Add(page, preview, AppStrings.Opt_EditorPreviewOpens, AppStrings.Opt_EditorPreviewOpens_Desc, PreviewOpensChecks, keywords: kw);
        Add(page, preview, AppStrings.Opt_EditorPreviewPlace, AppStrings.Opt_EditorPreviewPlace_Desc, () => Segmented(
            [(false, AppStrings.Opt_EditorPreviewBeside), (true, AppStrings.Opt_EditorPreviewBelow)],
            Current.Editors.PreviewBelow, v => UpdateEditors(x => x with { PreviewBelow = v, PreviewSpots = [] })), keywords: kw);
        Add(page, preview, AppStrings.Editor_ScrollTogether, AppStrings.Editor_ScrollTogetherTip, () => Toggle(Current.Editors.PreviewSync, on => UpdateEditors(x => x with { PreviewSync = on })), keywords: kw);
        Add(page, preview, AppStrings.Opt_EditorPreviewLive, AppStrings.Opt_EditorPreviewLive_Desc, () => MegabyteBox(Current.Editors.PreviewLiveUpToMB, "1", AppStrings.Opt_EditorPreviewLive, min: 0,
            v => UpdateEditors(x => x with { PreviewLiveUpToMB = v ?? 1 })), keywords: kw);
    }

    private void UpdateEditors(Func<EditorSettings, EditorSettings> change) => Settings.Update(s => s with { Editors = change(s.Editors) });

    private Control PreviewOpensChecks()
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (kind, label) in new[] { (PreviewKind.Markdown, AppStrings.Opt_EditorPreview_Markdown), (PreviewKind.Notebook, AppStrings.Opt_EditorPreview_Notebook), (PreviewKind.Svg, AppStrings.Opt_EditorPreview_Svg), (PreviewKind.Html, AppStrings.Opt_EditorPreview_Html) })
        {
            var box = new CheckBox { Content = label, IsChecked = Current.Editors.PreviewOpens.Contains(kind), Margin = new Thickness(0, 0, 16, 0) };
            box.IsCheckedChanged += (_, _) => UpdateEditors(x => x with
            {
                PreviewOpens = box.IsChecked == true ? [.. x.PreviewOpens.Where(k => k != kind), kind] : [.. x.PreviewOpens.Where(k => k != kind)],
            });
            row.Children.Add(box);
        }

        return row;
    }

    private static string OpensLabel(SyntaxLanguage colors)
        => Core.Resources.Loc.Format(AppStrings.Opt_EditorMask_Internal, colors == SyntaxLanguage.None ? AppStrings.Opt_EditorMask_Plain : EditorWindow.LanguageName(colors));

    private Control EditorMaskTable()
    {
        var masks = Current.Editors.Masks;
        var grid = new Grid { RowSpacing = 6, ColumnSpacing = 8 };
        EditorMaskGrid = grid;
        var headers = new Control[]
        {
            Text(AppStrings.Opt_ViewerMask_Mask, 12, brush: "Brush.SecondaryText"),
            Text(AppStrings.Opt_EditorMask_Opens, 12, brush: "Brush.SecondaryText"),
            Text(AppStrings.Opt_EditorMask_ColorsUpTo, 12, brush: "Brush.SecondaryText"),
            Text(AppStrings.Opt_ViewerMask_Largest, 12, brush: "Brush.SecondaryText"),
        };
        grid.Children.AddRange(headers);
        var rows = new List<Control[]>();
        var externalRows = new HashSet<Control[]>();

        void Change(int index, Func<EditorMask, EditorMask> change)
            => UpdateEditors(v => v with { Masks = [.. v.Masks.Select((m, i) => i == index ? change(m) : m)] });

        void Structure(Func<List<EditorMask>, List<EditorMask>> change)
        {
            UpdateEditors(v => v with { Masks = change([.. v.Masks]) });
            RebuildLater();
        }

        for (var i = 0; i < masks.Count; i++)
        {
            var index = i;
            var row = masks[i];
            var mask = new TextBox { Text = row.Mask, MinWidth = 120, VerticalContentAlignment = VerticalAlignment.Center }.Res(TextBox.FontFamilyProperty, "Font.Mono");
            Avalonia.Automation.AutomationProperties.SetName(mask, AppStrings.Opt_ViewerMask_Mask);
            void Commit()
            {
                if (!string.IsNullOrWhiteSpace(mask.Text) && mask.Text != Current.Editors.Masks.ElementAtOrDefault(index)?.Mask)
                {
                    Change(index, m => m with { Mask = mask.Text.Trim() });
                }
            }

            mask.LostFocus += (_, _) => Commit();
            mask.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit();
                    e.Handled = true;
                }
            };

            var opens = new ComboBox { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Stretch };
            Avalonia.Automation.AutomationProperties.SetName(opens, AppStrings.Opt_EditorMask_Opens);
            foreach (var colors in EditorSettings.ColorChoices)
            {
                opens.Items.Add(new ComboBoxItem { Content = OpensLabel(colors), Tag = colors });
            }

            opens.Items.Add(new ComboBoxItem { Content = AppStrings.Opt_EditorMask_External, Tag = ExternalTag });
            var external = row.Program is not null;
            opens.SelectedIndex = external ? opens.Items.Count - 1 : Array.IndexOf(EditorSettings.ColorChoices, row.Colors);
            opens.SelectionChanged += (_, _) =>
            {
                var now = Current.Editors.Masks.ElementAtOrDefault(index);
                if (opens.SelectedItem is ComboBoxItem { Tag: SyntaxLanguage colors } && (colors != now?.Colors || now.Program is not null))
                {
                    Structure(l =>
                    {
                        l[index] = l[index] with { Colors = colors, Program = null };
                        return l;
                    });
                }
                else if (opens.SelectedItem is ComboBoxItem { Tag: ExternalTag } && now?.Program is null)
                {
                    Structure(l =>
                    {
                        l[index] = l[index] with { Program = ExampleProgram };
                        return l;
                    });
                }
            };

            Control opensCell = opens;
            if (external)
            {
                opens.HorizontalAlignment = HorizontalAlignment.Left;
                mask.VerticalAlignment = VerticalAlignment.Top;
                opensCell = new StackPanel { Spacing = 4, Children = { opens, ProgramBox(row.Program!, v => Change(index, m => m with { Program = v })) } };
            }

            Control Dash() => Text("—", 12, brush: "Brush.TertiaryText").Also(t => t.VerticalAlignment = VerticalAlignment.Center);
            var colorsUpTo = external ? new Panel()
                : row.Colors == SyntaxLanguage.None
                ? Dash()
                : MegabyteBox(row.ColorsUpToMB, EditorSettings.DefaultColorsUpToMB(row.Colors).ToString(CultureInfo.CurrentCulture), AppStrings.Opt_EditorMask_ColorsUpTo, min: 1,
                    v => Change(index, m => m with { ColorsUpToMB = v }));
            var largest = external
                ? new Panel()
                : MegabyteBox(row.LargestMB, EditorSettings.DefaultLargestMB.ToString(CultureInfo.CurrentCulture), AppStrings.Opt_ViewerMask_Largest, min: 0,
                    v => Change(index, m => m with { LargestMB = v }));

            var up = MaskTool("Icon.ArrowUp", AppStrings.Opt_ViewerMask_Up, index > 0, () => Structure(l =>
            {
                (l[index - 1], l[index]) = (l[index], l[index - 1]);
                return l;
            }));
            var down = MaskTool("Icon.ArrowDown", AppStrings.Opt_ViewerMask_Down, index < masks.Count - 1, () => Structure(l =>
            {
                (l[index + 1], l[index]) = (l[index], l[index + 1]);
                return l;
            }));
            var remove = MaskTool("Icon.Cross", AppStrings.Common_Remove, masks.Count > 1, () => Structure(l =>
            {
                l.RemoveAt(index);
                return l;
            }));
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { up, down, remove } };
            var cells = new Control[] { mask, opensCell, colorsUpTo, largest, tools };
            rows.Add(cells);
            if (external)
            {
                externalRows.Add(cells);
            }
            grid.Children.AddRange(cells);
        }

        bool? narrowNow = null;
        void Place(Control c, int row, int column, int span = 1)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, column);
            Grid.SetColumnSpan(c, span);
        }

        double Widest(int column) => rows.Where(r => column != 1 || !externalRows.Contains(r)).Select(r => r[column].DesiredSize.Width).DefaultIfEmpty(0).Max();

        void Lay(double width)
        {
            var opensWidth = Math.Max(160, Widest(1));
            var narrow = width < 120 + opensWidth + Widest(2) + Widest(3) + Widest(4) + (4 * 8);
            if (narrowNow == narrow)
            {
                if (!narrow && grid.ColumnDefinitions.Count == 5)
                {
                    grid.ColumnDefinitions[1].MinWidth = opensWidth;
                }

                return;
            }

            narrowNow = narrow;
            grid.ColumnDefinitions = narrow
                ? new ColumnDefinitions("*,Auto,Auto")
                : [new ColumnDefinition(5, GridUnitType.Star) { MinWidth = 120 }, new ColumnDefinition(3, GridUnitType.Star) { MinWidth = opensWidth }, new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)];
            grid.RowDefinitions.Clear();
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var r = 0;
            if (narrow)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                r++;
                Place(headers[0], 0, 0, 3);
                Place(headers[1], r, 0);
                Place(headers[2], r, 1);
                Place(headers[3], r, 2);
            }
            else
            {
                for (var c = 0; c < headers.Length; c++)
                {
                    Place(headers[c], 0, c);
                }
            }

            foreach (var cells in rows)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                r++;
                if (narrow)
                {
                    Place(cells[0], r, 0, 2);
                    Place(cells[4], r, 2);
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    r++;
                    Place(cells[1], r, 0, externalRows.Contains(cells) ? 3 : 1);
                    Place(cells[2], r, 1);
                    Place(cells[3], r, 2);
                    cells[1].Margin = new Thickness(0, 0, 0, 10);
                }
                else
                {
                    for (var c = 0; c < cells.Length; c++)
                    {
                        Place(cells[c], r, c);
                    }

                    if (externalRows.Contains(cells))
                    {
                        Grid.SetColumnSpan(cells[1], 3);
                    }

                    cells[1].Margin = default;
                }
            }
        }

        Lay(double.PositiveInfinity);
        grid.SizeChanged += (_, e) => Dispatcher.UIThread.Post(() => Lay(e.NewSize.Width));

        var add = new Button { Content = AppStrings.Opt_ViewerMask_Add, Padding = new Thickness(12, 4) };
        add.Click += (_, _) => Structure(l =>
        {
            l.Insert(Math.Max(0, l.Count - 1), new EditorMask { Mask = "*.txt" });
            return l;
        });

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(grid);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add } });
        panel.Children.Add(Secondary(AppStrings.Opt_EditorMasks_Help));
        panel.Children.Add(Secondary(AppStrings.Opt_EditorMask_LimitsHelp));
        return panel;
    }

    private const string ExternalTag = "external";

    private const string ExampleProgram = "notepad.exe \"$(FullName)\"";

    private Control ProgramBox(string value, Action<string> changed)
    {
        var box = new TextBox { Text = value, MinWidth = 200, VerticalContentAlignment = VerticalAlignment.Center }.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Opt_EditorMask_Program);
        ToolTip.SetTip(box, AppStrings.Opt_EditorMask_ProgramTip);
        void Commit()
        {
            var text = (box.Text ?? "").Trim();
            if (text.Length == 0 || Core.Editing.ExternalCommand.Parse(text) is null)
            {
                box.Text = value;
                return;
            }

            if (text != value)
            {
                value = text;
                changed(text);
            }
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };

        var insert = new Button { Content = AppStrings.Opt_EditorMask_InsertVariable, Padding = new Thickness(8, 4), FontSize = 12 };
        var menu = new MenuFlyout();
        foreach (var variable in Core.Editing.ExternalCommand.Variables)
        {
            var item = new MenuItem { Header = variable };
            item.Click += (_, _) =>
            {
                var at = Math.Clamp(box.CaretIndex, 0, (box.Text ?? "").Length);
                box.Text = (box.Text ?? "").Insert(at, variable);
                box.CaretIndex = at + variable.Length;
                Commit();
            };
            menu.Items.Add(item);
        }

        insert.Flyout = menu;
        var row = new DockPanel();
        DockPanel.SetDock(insert, Dock.Right);
        insert.Margin = new Thickness(4, 0, 0, 0);
        row.Children.Add(insert);
        row.Children.Add(box);
        return row;
    }

    private Control MegabyteBox(long? value, string standard, string name, long min, Action<long?> changed)
    {
        var culture = CultureInfo.CurrentCulture;
        var box = new TextBox
        {
            Text = value?.ToString(culture) ?? "",
            PlaceholderText = standard,
            Width = 80,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetName(box, name);
        void Commit()
        {
            long? typed = string.IsNullOrWhiteSpace(box.Text) ? null
                : long.TryParse(box.Text, NumberStyles.Integer, culture, out var mb) && mb >= min ? mb : value;
            box.Text = typed?.ToString(culture) ?? "";
            if (typed != value)
            {
                value = typed;
                changed(typed);
            }
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { box, Text(AppStrings.Opt_MB, 12, brush: "Brush.SecondaryText").Also(t => t.VerticalAlignment = VerticalAlignment.Center) } };
    }

    private static Button MaskTool(string icon, string tip, bool enabled, Action click)
    {
        var b = new Button { Content = Salandual.App.Theme.AppIcons.Glyph(icon, 14, 14, 1.3, "Brush.Text"), Padding = new Thickness(6), Background = Avalonia.Media.Brushes.Transparent, Opacity = enabled ? 1 : 0, IsHitTestVisible = enabled, Focusable = enabled };
        ToolTip.SetTip(b, tip);
        Avalonia.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
}
