// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Pages.cs

using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Commands;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{
    private static readonly string[] _dateCultures = ["en-US", "en-GB", "hu-HU", "de-DE", "fr-FR", "es-ES", "it-IT", "pl-PL", "cs-CZ", "ja-JP", "zh-CN"];

    private static readonly DateTime _sampleDate = new(2026, 8, 14, 18, 25, 12, DateTimeKind.Local);

    private void AddLayout()
    {
        const string layout = "layout";
        Add(layout, AppStrings.OptSec_Rows, AppStrings.Opt_RowHeight, AppStrings.Opt_RowHeight_Desc, RowHeightPicker, wide: true, keywords: AppStrings.Opt_RowHeight_Kw);

        Add(layout, AppStrings.OptSec_Drives, AppStrings.Bar_DriveStrip, AppStrings.Opt_DriveStrip_Desc, () => EnabledWhile(ChoiceCards<StripMode>(
            [
                (StripMode.PerPanel, AppStrings.Opt_PerPanel, AppStrings.Opt_StripPerPanel_Desc, () => MiniWindow(["panes"], paneStrips: true)),
                (StripMode.Shared, AppStrings.Opt_Shared, AppStrings.Opt_StripShared_Desc, () => MiniWindow(["strip", "panes"])),
            ],
            Current.Layout.Strip, mode => UpdateLayout(l => l with { Strip = mode })), s => s.Layout.Bars.DriveStrip, dim: true), wide: true, keywords: AppStrings.Opt_DriveStrip_Kw);

        void Bar(string title, string? description, Func<BarVisibility, bool> get, Func<BarVisibility, bool, BarVisibility> set, string keywords = "")
            => Add(layout, AppStrings.OptSec_Bars, title, description, () => Toggle(get(Current.Layout.Bars), on => UpdateLayout(l => l with { Bars = set(l.Bars, on) })), keywords: AppStrings.Opt_ShowHide_Kw + " " + keywords);

        void LaterBar(string title, string milestone)
            => Add(layout, AppStrings.OptSec_Bars, title, Loc.Format(AppStrings.Opt_ArrivesIn, CommandText.Milestone(milestone)), () => Toggle(true, _ => { }, enabled: false), keywords: AppStrings.Opt_ShowHide_Kw);

        var titleBar = AppStrings.OptSec_TitleBar;
        Add(layout, titleBar, AppStrings.Opt_Menus, AppStrings.Opt_Menus_Desc, MenuPlacementPicker,
            wide: true, keywords: AppStrings.Opt_Menus_Kw);
        Add(layout, titleBar, AppStrings.Opt_MenuButton, AppStrings.Opt_MenuButton_Desc,
            () => EnabledWhile(Segmented([(MenuButtonStyle.AppIcon, AppStrings.Opt_MenuButton_AppIcon), (MenuButtonStyle.Hamburger, AppStrings.Opt_MenuButton_Hamburger)], Current.Layout.MenuButton,
                v => UpdateLayout(l => l with { MenuButton = v })), s => !s.Layout.Bars.MenuBar), keywords: AppStrings.Opt_MenuButton_Kw);
        Add(layout, titleBar, AppStrings.Opt_WindowTitle, AppStrings.Opt_WindowTitle_Desc, WindowTitlePicker,
            wide: true, keywords: AppStrings.Opt_WindowTitle_Kw);
        Add(layout, titleBar, AppStrings.Opt_TitleVersion, AppStrings.Opt_TitleVersion_Desc,
            () => Toggle(Current.Layout.TitleShowsVersion, on => UpdateLayout(l => l with { TitleShowsVersion = on })), keywords: AppStrings.Opt_TitleVersion_Kw);

        Aside(layout, AppStrings.OptSec_Bars, () => new BarsPreview(Settings));
        Bar(AppStrings.Bar_DriveStrip, AppStrings.Bar_DriveStrip_Desc, b => b.DriveStrip, (b, on) => b with { DriveStrip = on }, AppStrings.Bar_DriveStrip_Kw);
        Bar(AppStrings.Bar_HotPathBar, AppStrings.Bar_HotPathBar_Desc, b => b.HotPathBar, (b, on) => b with { HotPathBar = on }, AppStrings.Bar_HotPathBar_Kw);
        Bar(AppStrings.Bar_DirectoryLine, AppStrings.Bar_DirectoryLine_Desc, b => b.DirectoryLine, (b, on) => b with { DirectoryLine = on }, AppStrings.Bar_DirectoryLine_Kw);
        Bar(AppStrings.Bar_ColumnHeader, AppStrings.Bar_ColumnHeader_Desc, b => b.ColumnHeader, (b, on) => b with { ColumnHeader = on }, AppStrings.Bar_ColumnHeader_Kw);
        Bar(AppStrings.Bar_InformationLine, AppStrings.Bar_InformationLine_Desc, b => b.InformationLine, (b, on) => b with { InformationLine = on }, AppStrings.Bar_InformationLine_Kw);
        Bar(AppStrings.Bar_FunctionKeyBar, AppStrings.Bar_FunctionKeyBar_Desc, b => b.FunctionKeyBar, (b, on) => b with { FunctionKeyBar = on }, AppStrings.Bar_FunctionKeyBar_Kw);
        Bar(AppStrings.Bar_MiddleToolbar, AppStrings.Bar_MiddleToolbar_Desc, b => b.MiddleToolbar, (b, on) => b with { MiddleToolbar = on }, AppStrings.Bar_MiddleToolbar_Kw);
        LaterBar(AppStrings.Bar_Toolbar, "Phase 3");
        LaterBar(AppStrings.Bar_UserMenuBar, "Phase 3");
        LaterBar(AppStrings.Bar_CommandLine, "Phase 3");
        LaterBar(AppStrings.Bar_OperationsIndicator, "M3");

        Add(layout, AppStrings.OptSec_Bars, AppStrings.Opt_TabBar, AppStrings.Opt_TabBar_Desc, () => Segmented(
            [(TabBarMode.WhenNeeded, AppStrings.Opt_TabBar_WhenNeeded), (TabBarMode.Always, AppStrings.Common_Always), (TabBarMode.Never, AppStrings.Common_Never)],
            Current.Layout.TabBar, _ => { }, enabled: false), wide: true, keywords: AppStrings.Opt_TabBar_Kw);
        Add(layout, AppStrings.OptSec_Bars, AppStrings.Opt_CustomizeToolbar, AppStrings.Opt_CustomizeToolbar_Desc, () => ActionButton(AppStrings.Common_Customize, () => { }, enabled: false), keywords: AppStrings.Opt_CustomizeToolbar_Kw);

        var dirLine = AppStrings.Bar_DirectoryLine;
        Add(layout, dirLine, AppStrings.Opt_ChangeDirectory, AppStrings.Opt_ChangeDirectory_Desc,
            () => Segmented([(ChangeDirectoryStyle.InBar, AppStrings.Opt_ChangeDirectory_InBar), (ChangeDirectoryStyle.Dialog, AppStrings.Opt_ChangeDirectory_Dialog)], Current.Layout.DirectoryLine.ChangeDirectory,
                v => UpdateLayout(l => l with { DirectoryLine = l.DirectoryLine with { ChangeDirectory = v } })), keywords: AppStrings.Opt_ChangeDirectory_Kw);
        Add(layout, dirLine, AppStrings.Opt_WorkingDirectories, AppStrings.Opt_WorkingDirectories_Desc,
            () => Segmented([(WorkingDirectoriesScope.PerPanel, AppStrings.Opt_PerPanel), (WorkingDirectoriesScope.Shared, AppStrings.Opt_BothPanels)], Current.Layout.DirectoryLine.WorkingDirectories,
                v => UpdateLayout(l => l with { DirectoryLine = l.DirectoryLine with { WorkingDirectories = v } })), keywords: AppStrings.Opt_WorkingDirectories_Kw);
        Add(layout, dirLine, AppStrings.Opt_MaximizeButton, AppStrings.Opt_MaximizeButton_Desc,
            () => EnabledWhile(Toggle(Current.Layout.DirectoryLine.MaximizeButton, on => UpdateLayout(l => l with { DirectoryLine = l.DirectoryLine with { MaximizeButton = on } })), s => s.Layout.Bars.DirectoryLine),
            keywords: AppStrings.Opt_MaximizeButton_Kw);
    }

    private Control MenuPlacementPicker()
    {
        var layout = Current.Layout;
        var current = !layout.Bars.MenuBar ? 2 : layout.MenuRow == MenuRow.OwnRow ? 1 : 0;
        return ChoiceCards<int>(
            [
                (0, AppStrings.Opt_Menus_InTitleBar, AppStrings.Opt_Menus_InTitleBar_Desc, () => MiniWindow(["title", "panes"], titleMarks: 5)),
                (1, AppStrings.Opt_Menus_OwnRow, AppStrings.Opt_Menus_OwnRow_Desc, () => MiniWindow(["title", "menu", "panes"])),
                (2, AppStrings.Opt_Menus_BehindButton, AppStrings.Opt_Menus_BehindButton_Desc, () => MiniWindow(["title", "panes"], logoChevron: true)),
            ],
            current,
            choice =>
            {
                UpdateLayout(l => choice switch
                {
                    0 => l with { MenuRow = MenuRow.InTitleBar, Bars = l.Bars with { MenuBar = true } },
                    1 => l with { MenuRow = MenuRow.OwnRow, Bars = l.Bars with { MenuBar = true } },
                    _ => l with { Bars = l.Bars with { MenuBar = false } },
                });
            });
    }

    private Control WindowTitlePicker()
    {
        var samplePath = AppStrings.Sample_WindowTitlePath;
        var presets = Core.Panels.WindowTitle.Presets;
        var template = new TextBox { Text = Current.Layout.WindowTitle, MinWidth = 320 };
        template.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(template, AppStrings.Opt_WindowTitle_Template);
        var preview = Text("", 13);
        var combo = new ComboBox { MinWidth = 220 };
        foreach (var (name, _) in presets)
        {
            combo.Items.Add(new ComboBoxItem { Content = name });
        }

        combo.Items.Add(new ComboBoxItem { Content = AppStrings.Common_Custom });

        var syncing = false;
        string Render(string text) => Core.Panels.WindowTitle.Expand(text, samplePath, AppBuild.TitleName(Current.Layout.TitleShowsVersion));
        void Show(string text)
        {
            preview.Text = Render(text);
            syncing = true;
            var index = presets.ToList().FindIndex(p => p.Template == text);
            combo.SelectedIndex = index >= 0 ? index : presets.Count;
            syncing = false;
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (!syncing && combo.SelectedIndex >= 0 && combo.SelectedIndex < presets.Count)
            {
                template.Text = presets[combo.SelectedIndex].Template;
            }
            else if (!syncing && combo.SelectedIndex == presets.Count)
            {
                template.Focus();
            }
        };
        template.TextChanged += (_, _) =>
        {
            var text = template.Text ?? "";
            Show(text);
            if (text.Trim().Length > 0)
            {
                UpdateLayout(l => l with { WindowTitle = text });
            }
        };
        Show(Current.Layout.WindowTitle);

        EventHandler<SettingsChangedEventArgs> changed = (_, _) => preview.Text = Render(template.Text ?? "");
        preview.AttachedToVisualTree += (_, _) => Settings.Changed += changed;
        preview.DetachedFromVisualTree += (_, _) => Settings.Changed -= changed;

        var top =new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { combo, template } };
        var help = Secondary(AppStrings.Opt_WindowTitle_Help);
        preview.TextTrimming = TextTrimming.CharacterEllipsis;
        preview.FontSize = 12;
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = Controls.TitleBarParts.Height };
        var logo = Controls.TitleBarParts.Logo();
        logo.Margin = new Thickness(10, 0, 8, 0);
        bar.Children.Add(logo);
        Grid.SetColumn(preview, 1);
        bar.Children.Add(preview);
        var caps = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var glyph in new[] { "Icon.CaptionMinimize", "Icon.CaptionMaximize", "Icon.CaptionClose" })
        {
            var path = Salandual.App.Theme.AppIcons.Glyph(glyph, 10, 10, 1, "Brush.Text", round: false);
            path.HorizontalAlignment = HorizontalAlignment.Center;
            caps.Children.Add(new Border { Width = 46, Child = path });
        }

        Grid.SetColumn(caps, 2);
        bar.Children.Add(caps);
        var sample = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ClipToBounds = true,
            Child = bar,
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        Avalonia.Automation.AutomationProperties.SetName(sample, AppStrings.Opt_WindowTitle_Preview);
        return new StackPanel { Spacing = 8, Children = { top, help, sample } };
    }

    private Control RowHeightPicker()
    {
        var current = Current.Layout.RowHeight;
        var value = Text("", 14, FontWeight.SemiBold);
        var slider = new Slider
        {
            Minimum = LayoutSettings.MinRowHeight,
            Maximum = LayoutSettings.MaxRowHeight,
            Value = current,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Width = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var stops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var stopButtons = new List<(Avalonia.Controls.Primitives.ToggleButton Button, int Height)>();
        foreach (var (name, height) in LayoutSettings.RowHeightStops)
        {
            var b = new Avalonia.Controls.Primitives.ToggleButton { Content = name, Padding = new Thickness(12, 4) };
            b.Click += (_, _) =>
            {
                slider.Value = height;
                slider.Focus();
            };
            stopButtons.Add((b, height));
            stops.Children.Add(b);
        }

        void Show(int px)
        {
            var name = LayoutSettings.RowHeightStops.FirstOrDefault(s => s.Height == px).Name ?? AppStrings.Common_Custom;
            value.Text = string.Create(CultureInfo.CurrentCulture, $"{px} px · {name}");
            foreach (var (button, height) in stopButtons)
            {
                button.IsChecked = height == px;
            }
        }

        Show(current);
        slider.ValueChanged += (_, e) =>
        {
            var px = (int)Math.Round(e.NewValue);
            Show(px);
            UpdateLayout(l => l with { RowHeight = px });
        };

        var left = new StackPanel { Spacing = 10 };
        left.Children.Add(value);
        left.Children.Add(slider);
        left.Children.Add(stops);

        var preview = new PreviewPanel(Settings, rows: 3, showInfo: false) { Width = 330, VerticalAlignment = VerticalAlignment.Top };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(left);
        Grid.SetColumn(preview, 1);
        grid.Children.Add(preview);
        return grid;
    }

    private void AddPanels()
    {
        const string panels = "panels";
        Add(panels, AppStrings.OptSec_Listing, AppStrings.Opt_ShowHidden, AppStrings.Opt_ShowHidden_Desc, () =>
            Toggle(Current.Panels.ShowHiddenFiles, on => UpdatePanels(p => p with { ShowHiddenFiles = on })), keywords: AppStrings.Opt_ShowHidden_Kw);
        Add(panels, AppStrings.OptSec_Listing, AppStrings.Opt_ExtColumn, AppStrings.Opt_ExtColumn_Desc, () =>
            Toggle(Current.Panels.ExtensionColumn, on => UpdatePanels(p => p with { ExtensionColumn = on })), keywords: AppStrings.Opt_ExtColumn_Kw);
        Add(panels, AppStrings.OptSec_Sorting, AppStrings.Opt_NumericSort, AppStrings.Opt_NumericSort_Desc, () =>
            Toggle(Current.Panels.NumericSort, on => UpdatePanels(p => p with { NumericSort = on })), keywords: AppStrings.Opt_NumericSort_Kw);
        Add(panels, AppStrings.OptSec_Selection, AppStrings.Opt_NumPlusFolders, AppStrings.Opt_NumPlusFolders_Desc, () =>
            Toggle(Current.Panels.SelectFoldersWithNumPlus, on => UpdatePanels(p => p with { SelectFoldersWithNumPlus = on })), keywords: AppStrings.Opt_NumPlusFolders_Kw);
        Add(panels, AppStrings.OptSec_Renaming, AppStrings.Opt_ClickToRename, AppStrings.Opt_ClickToRename_Desc, () =>
            Toggle(Current.Panels.ClickToRename, on => UpdatePanels(p => p with { ClickToRename = on })), keywords: AppStrings.Opt_ClickToRename_Kw);

        Add(panels, AppStrings.OptSec_QuickSearch, AppStrings.Opt_QuickSearch, AppStrings.Opt_QuickSearch_Desc, () =>
            Segmented([(QuickSearchStart.Letters, AppStrings.Opt_QuickSearch_Letters), (QuickSearchStart.CtrlAltLetter, AppStrings.Opt_QuickSearch_CtrlAlt)],
                Current.Panels.QuickSearch, start => UpdatePanels(p => p with { QuickSearch = start })), keywords: AppStrings.Opt_QuickSearch_Kw);

        Add(panels, AppStrings.OptSec_Mouse, AppStrings.Opt_RightClick, AppStrings.Opt_RightClick_Desc, () =>

            Combo([(RightClickMenu.Quick, AppStrings.Opt_RightClick_Quick), (RightClickMenu.Windows, AppStrings.Opt_RightClick_Windows)], Current.Mouse.RightClick,
                v => UpdateMouse(m => m with { RightClick = v }), 300), keywords: AppStrings.Opt_RightClick_Kw);
        Add(panels, AppStrings.OptSec_Mouse, AppStrings.Opt_RightUnselected, AppStrings.Opt_RightUnselected_Desc, RightUnselectedPicker, wide: true, keywords: AppStrings.Opt_RightUnselected_Kw);
        Add(panels, AppStrings.OptSec_Mouse, AppStrings.Opt_AltEnter, AppStrings.Opt_AltEnter_Desc, () =>
            Combo([(PropertiesWindow.Salandual, AppStrings.Opt_AltEnter_Salandual), (PropertiesWindow.Windows, AppStrings.Opt_AltEnter_Windows)], Current.Mouse.AltEnter,
                v => UpdateMouse(m => m with { AltEnter = v }), 300), keywords: AppStrings.Opt_AltEnter_Kw);
        Add(panels, AppStrings.OptSec_Mouse, AppStrings.Opt_PlainDrag, AppStrings.Opt_PlainDrag_Desc, () =>
            Combo([(PlainDrag.AlwaysCopies, AppStrings.Opt_PlainDrag_Copies), (PlainDrag.WindowsRule, AppStrings.Opt_PlainDrag_Windows)], Current.Mouse.PlainDrag,
                v => UpdateMouse(m => m with { PlainDrag = v }), 300), keywords: AppStrings.Opt_PlainDrag_Kw);
        Add(panels, AppStrings.OptSec_Mouse, AppStrings.Opt_AfterDrop, null, () =>
            Combo([(AfterDrop.StartAtOnce, AppStrings.Opt_AfterDrop_Start), (AfterDrop.ShowDialog, AppStrings.Opt_AfterDrop_Dialog)], Current.Mouse.AfterDrop,
                v => UpdateMouse(m => m with { AfterDrop = v }), 300), keywords: AppStrings.Opt_AfterDrop_Kw);
        Add(panels, AppStrings.OptSec_Mouse, AppStrings.Opt_ChipDrop, AppStrings.Opt_ChipDrop_Desc, () =>
            Combo([(DriveChipDrop.FolderItOpens, AppStrings.Opt_ChipDrop_Folder), (DriveChipDrop.DriveRoot, AppStrings.Opt_ChipDrop_Root)], Current.Mouse.DriveChipDrop,
                v => UpdateMouse(m => m with { DriveChipDrop = v }), 300), keywords: AppStrings.Opt_ChipDrop_Kw);
        Add(panels, AppStrings.OptSec_Mouse, AppStrings.Opt_Pinned, AppStrings.Opt_Pinned_Desc, PinnedList, wide: true, keywords: AppStrings.Opt_Pinned_Kw);

        Add(panels, AppStrings.OptSec_SizesDates, AppStrings.Opt_SizeUnits, null, SizeUnitsPicker, wide: true, keywords: AppStrings.Opt_SizeUnits_Kw);
        Add(panels, AppStrings.OptSec_SizesDates, AppStrings.Opt_DateTime, AppStrings.Opt_DateTime_Desc, DateFormatPicker, keywords: AppStrings.Opt_DateTime_Kw);
        Add(panels, AppStrings.OptSec_SizesDates, AppStrings.Opt_Time, AppStrings.Opt_Time_Desc, TimeFormatPicker, keywords: AppStrings.Opt_Time_Kw);

        Add(panels, AppStrings.Bar_InformationLine, AppStrings.Opt_InfoTemplate, AppStrings.Opt_InfoTemplate_Desc, () =>
        {
            var box = new TextBox { Text = Current.Panels.InformationLine, AcceptsReturn = false }.Res(TextBox.FontFamilyProperty, "Font.Mono");
            box.TextChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(box.Text))
                {
                    UpdatePanels(p => p with { InformationLine = box.Text });
                }
            };
            var reset = ActionButton(AppStrings.Common_Reset, () => box.Text = PanelSettings.DefaultInformationLine);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            grid.Children.Add(box);
            Grid.SetColumn(reset, 1);
            grid.Children.Add(reset);
            return grid;
        }, wide: true, keywords: AppStrings.Opt_InfoTemplate_Kw);
    }

    private Control SizeUnitsPicker()
    {
        var example = Secondary("");
        void Show(SizeUnits units)
        {
            var f = new DisplayFormat(units, PanelSettings.DateFormatIso, CultureInfo.CurrentCulture);
            example.Text = Loc.Format(AppStrings.Opt_SizeExample, f.Size(25_690_112), f.Size(2_048));
        }

        Show(Current.Panels.SizeUnits);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Segmented(
            [(SizeUnits.Windows, AppStrings.Opt_SizeUnits_Windows), (SizeUnits.Binary, "KiB, MiB"), (SizeUnits.Decimal, AppStrings.Opt_SizeUnits_Decimal), (SizeUnits.Bytes, AppStrings.Opt_SizeUnits_Bytes)],
            Current.Panels.SizeUnits,
            units =>
            {
                Show(units);
                UpdatePanels(p => p with { SizeUnits = units });
            }));
        panel.Children.Add(example);
        return panel;
    }

    private Control DateFormatPicker()
    {
        string Sample(string format)
        {
            var f = new DisplayFormat(SizeUnits.Windows, format, CultureInfo.CurrentCulture, Current.Panels.TimeFormat);
            return f.DateAndTime(_sampleDate);
        }

        var options = new List<(string Value, string Label)>
        {
            (PanelSettings.DateFormatIso, Loc.Format(AppStrings.Opt_Date_Iso, Sample(PanelSettings.DateFormatIso))),
            (PanelSettings.DateFormatSystem, Loc.Format(AppStrings.Opt_Date_Windows, Sample(PanelSettings.DateFormatSystem))),
        };
        foreach (var name in _dateCultures)
        {
            var culture = CultureInfo.GetCultureInfo(name);
            options.Add((culture.Name, $"{culture.NativeName} · {Sample(culture.Name)}"));
        }

        var current = Current.Panels.DateFormat;
        if (!options.Any(o => o.Value == current))
        {
            options.Add((current, $"{CultureInfo.GetCultureInfo(current).NativeName} · {Sample(current)}"));
        }

        return Combo(options, current, format => UpdatePanels(p => p with { DateFormat = format }), 300);
    }

    private Control TimeFormatPicker()
    {
        var example = Secondary("");
        void Show(TimeFormat time)
        {
            var f = new DisplayFormat(SizeUnits.Windows, Current.Panels.DateFormat, CultureInfo.CurrentCulture, time);
            example.Text = Loc.Format(AppStrings.Opt_Time_Example, f.DateAndTime(_sampleDate), f.TimeWithSeconds(_sampleDate));
        }

        Show(Current.Panels.TimeFormat);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Segmented(
            [(TimeFormat.Windows, AppStrings.Opt_Time_Windows), (TimeFormat.Hours24, AppStrings.Opt_Time_24), (TimeFormat.Hours12, AppStrings.Opt_Time_12)],
            Current.Panels.TimeFormat,
            time =>
            {
                Show(time);
                UpdatePanels(p => p with { TimeFormat = time });
                RebuildLater();
            }));
        panel.Children.Add(example);
        return panel;
    }


    private void AddAdvanced()
    {
        const string advanced = "advanced";
        Add(advanced, "", AppStrings.Opt_Rendering, AppStrings.Opt_Rendering_Desc, () =>
        {
            var note = Text(AppStrings.Opt_Rendering_Restart, 12, brush: "Brush.Warning");
            note.IsVisible = Current.Advanced.Rendering != _services.InitialRendering;
            var panel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
            panel.Children.Add(Segmented([(RenderingMode.Gpu, AppStrings.Opt_Rendering_Gpu), (RenderingMode.Software, AppStrings.Opt_Rendering_Software)], Current.Advanced.Rendering, mode =>
            {
                Settings.Update(s => s with { Advanced = s.Advanced with { Rendering = mode } });
                note.IsVisible = mode != _services.InitialRendering;
            }));
            panel.Children.Add(note);
            return panel;
        }, keywords: AppStrings.Opt_Rendering_Kw);

        Add(advanced, "", AppStrings.Opt_SettingsFolder, SettingsFolderDescription(), () => ActionButton(AppStrings.Common_OpenFolder, () =>
        {
            Directory.CreateDirectory(_services.DataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _services.DataDirectory + "\"") { UseShellExecute = false });
        }), keywords: AppStrings.Opt_SettingsFolder_Kw);

        var yours = AppStrings.OptSec_YourSettings;
        Add(advanced, yours, AppStrings.Opt_ExportSettings, AppStrings.Opt_ExportSettings_Desc, () => ActionButton(AppStrings.Opt_ExportSettings_Button, () => _ = SettingsFiles.ExportAsync()), keywords: AppStrings.Opt_YourSettings_Kw);
        Add(advanced, yours, AppStrings.Opt_ImportSettings, AppStrings.Opt_ImportSettings_Desc, () => ActionButton(AppStrings.Opt_ImportSettings_Button, () => _ = SettingsFiles.ImportAsync()), keywords: AppStrings.Opt_YourSettings_Kw);
        Add(advanced, yours, AppStrings.Opt_ResetAll, AppStrings.Opt_ResetAll_Desc, () => ActionButton(AppStrings.Opt_ResetAll_Button, () => _ = SettingsFiles.ResetAllAsync()), keywords: AppStrings.Opt_YourSettings_Kw);
    }

    private (string Text, Action? Undo)? _settingsNotice;

    internal SettingsFiles SettingsFiles => new(_services, this, (text, undo) =>
    {
        _settingsNotice = (text, undo);
        ShowCategory("advanced");
    });

    internal TextBlock? SettingsNoticeText { get; private set; }

    internal Button? SettingsNoticeUndo { get; private set; }

    private Control? SettingsNotice()
    {
        SettingsNoticeText = null;
        SettingsNoticeUndo = null;
        if (_settingsNotice is not var (notice, undo))
        {
            return null;
        }

        var text = Text(notice, 12.5, wrap: true);
        text.VerticalAlignment = VerticalAlignment.Center;
        SettingsNoticeText = text;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (undo is not null)
        {
            SettingsNoticeUndo = ActionButton(AppStrings.Common_Undo, () =>
            {
                _settingsNotice = null;
                undo();
            });
            buttons.Children.Add(SettingsNoticeUndo);
        }

        var close = new Button { Content = AppIcons.Glyph("Icon.Cross", 10, 10, 1.4), Width = 28, Height = 28, Padding = default, Background = Brushes.Transparent, BorderThickness = default };
        ToolTip.SetTip(close, AppStrings.Common_Close);
        Avalonia.Automation.AutomationProperties.SetName(close, AppStrings.Common_Close);
        close.Click += (_, _) =>
        {
            _settingsNotice = null;
            ShowCategory("advanced");
        };
        buttons.Children.Add(close);
        var line = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        line.Children.Add(buttons);
        line.Children.Add(text);
        return new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 4, 4, 4), Margin = new Thickness(0, -12, 0, 20), Child = line }
            .Res(Border.BackgroundProperty, "Brush.InfoBackground");
    }


    private string SettingsFolderDescription()
    {
        var text = Loc.Format(AppStrings.Opt_SettingsFolder_Desc, _services.DataDirectory);
        if (SettingsFileMessages.ForLoad(Settings.LoadResult, Settings.FilePath) is { } problem)
        {
            text += " " + problem;
        }

        if (Settings.LastSaveError is { } error)
        {
            text += " " + SettingsFileMessages.ForSave(error, Settings.FilePath);
        }

        return text;
    }
}

public partial class OptionsWindow
{
    private void UpdateMouse(Func<MouseSettings, MouseSettings> change) => Settings.Update(s => s with { Mouse = change(s.Mouse) });

    private Control RightUnselectedPicker()
    {
        var panel = new StackPanel { Spacing = 4 };
        foreach (var (value, title, detail) in new[]
        {
            (RightClickUnselected.MenuForItem, AppStrings.Opt_RightUnselected_A, AppStrings.Opt_RightUnselected_A_Desc),
            (RightClickUnselected.SelectOnlyItem, AppStrings.Opt_RightUnselected_B, AppStrings.Opt_RightUnselected_B_Desc),
            (RightClickUnselected.RightButtonSelects, AppStrings.Opt_RightUnselected_C, AppStrings.Opt_RightUnselected_C_Desc),
        })
        {
            var radio = new RadioButton
            {
                GroupName = "rightUnselected",
                IsChecked = Current.Mouse.RightClickUnselected == value,
                Content = new StackPanel { Children = { Text(title, 13), Secondary(detail) } },
            };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                {
                    UpdateMouse(m => m with { RightClickUnselected = value });
                }
            };
            panel.Children.Add(radio);
        }

        return panel;
    }

    private Control PinnedList()
    {
        var panel = new StackPanel { Spacing = 4 };
        void Rebuild()
        {
            panel.Children.Clear();
            var pins = Current.Mouse.Pinned;
            if (pins.Count == 0)
            {
                panel.Children.Add(Secondary(AppStrings.Opt_Pinned_None));
                return;
            }

            for (var i = 0; i < pins.Count; i++)
            {
                var index = i;
                var row = new DockPanel();
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                buttons.Children.Add(ActionButton(AppStrings.Opt_Pinned_Up, () => Move(index, -1), enabled: index > 0));
                buttons.Children.Add(ActionButton(AppStrings.Opt_Pinned_Down, () => Move(index, 1), enabled: index < pins.Count - 1));
                buttons.Children.Add(ActionButton(AppStrings.Opt_Pinned_Unpin, () =>
                {
                    UpdateMouse(m => m with { Pinned = [.. m.Pinned.Where((_, j) => j != index)] });
                    Rebuild();
                }));
                DockPanel.SetDock(buttons, Dock.Right);
                row.Children.Add(buttons);
                var title = Text(pins[i].Title, 13);
                title.VerticalAlignment = VerticalAlignment.Center;
                ToolTip.SetTip(title, string.Join(" › ", pins[i].Path));
                row.Children.Add(title);
                panel.Children.Add(row);
            }
        }

        void Move(int index, int step)
        {
            UpdateMouse(m =>
            {
                var list = m.Pinned.ToList();
                (list[index], list[index + step]) = (list[index + step], list[index]);
                return m with { Pinned = list };
            });
            Rebuild();
        }

        Rebuild();
        return panel;
    }
}
