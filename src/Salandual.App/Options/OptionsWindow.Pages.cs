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
using Salandual.Core.Panels;
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
        void Bar(string section, string title, string? description, Func<BarVisibility, bool> get, Func<BarVisibility, bool, BarVisibility> set, string keywords = "")
            => Add(layout, section, title, description, () => Toggle(get(Current.Layout.Bars), on => UpdateLayout(l => l with { Bars = set(l.Bars, on) })), keywords: AppStrings.Opt_ShowHide_Kw + " " + keywords);

        var titleBar = AppStrings.OptSec_TitleBar;
        Add(layout, titleBar, AppStrings.Opt_Menus, AppStrings.Opt_Menus_Desc, MenuPlacementPicker, keywords: AppStrings.Opt_Menus_Kw);
        Add(layout, titleBar, AppStrings.Opt_MenuButton, AppStrings.Opt_MenuButton_Desc,
            () => EnabledWhile(Segmented([(MenuButtonStyle.AppIcon, AppStrings.Opt_MenuButton_AppIcon), (MenuButtonStyle.Hamburger, AppStrings.Opt_MenuButton_Hamburger)], Current.Layout.MenuButton,
                v => UpdateLayout(l => l with { MenuButton = v })), s => !s.Layout.Bars.MenuBar), keywords: AppStrings.Opt_MenuButton_Kw, sub: true);
        Add(layout, titleBar, AppStrings.Opt_WindowTitle, AppStrings.Opt_WindowTitle_Desc, WindowTitlePicker,
            wide: true, keywords: AppStrings.Opt_WindowTitle_Kw);
        Add(layout, titleBar, AppStrings.Opt_TitleVersion, AppStrings.Opt_TitleVersion_Desc,
            () => Toggle(Current.Layout.TitleShowsVersion, on => UpdateLayout(l => l with { TitleShowsVersion = on })), keywords: AppStrings.Opt_TitleVersion_Kw, sub: true);

        var panel = AppStrings.OptSec_EachPanel;
        Aside(layout, panel, () => new BarsPreview(Settings) { Width = 200 });
        Add(layout, panel, AppStrings.Bar_DriveStrip, AppStrings.Opt_DriveStrip_Desc, DriveStripPicker, keywords: AppStrings.Opt_DriveStrip_Kw + " " + AppStrings.Bar_DriveStrip_Kw);
        Bar(panel, AppStrings.Bar_DirectoryLine, AppStrings.Bar_DirectoryLine_Desc, b => b.DirectoryLine, (b, on) => b with { DirectoryLine = on }, AppStrings.Bar_DirectoryLine_Kw);
        Add(layout, panel, AppStrings.Opt_ChangeDirectory, AppStrings.Opt_ChangeDirectory_Desc,
            () => Segmented([(ChangeDirectoryStyle.InBar, AppStrings.Opt_ChangeDirectory_InBar), (ChangeDirectoryStyle.Dialog, AppStrings.Opt_ChangeDirectory_Dialog)], Current.Layout.DirectoryLine.ChangeDirectory,
                v => UpdateLayout(l => l with { DirectoryLine = l.DirectoryLine with { ChangeDirectory = v } })), keywords: AppStrings.Opt_ChangeDirectory_Kw, sub: true);
        Add(layout, panel, AppStrings.Opt_WorkingDirectories, AppStrings.Opt_WorkingDirectories_Desc,
            () => Segmented([(WorkingDirectoriesScope.PerPanel, AppStrings.Opt_PerPanel), (WorkingDirectoriesScope.Shared, AppStrings.Opt_BothPanels)], Current.Layout.DirectoryLine.WorkingDirectories,
                v => UpdateLayout(l => l with { DirectoryLine = l.DirectoryLine with { WorkingDirectories = v } })), keywords: AppStrings.Opt_WorkingDirectories_Kw, sub: true);
        Add(layout, panel, AppStrings.Opt_FoldersKept, AppStrings.Opt_FoldersKept_Desc, FoldersKeptPicker, keywords: AppStrings.Opt_FoldersKept_Kw, sub: true);
        Add(layout, panel, AppStrings.Opt_MaximizeButton, AppStrings.Opt_MaximizeButton_Desc,
            () => EnabledWhile(Toggle(Current.Layout.DirectoryLine.MaximizeButton, on => UpdateLayout(l => l with { DirectoryLine = l.DirectoryLine with { MaximizeButton = on } })), s => s.Layout.Bars.DirectoryLine),
            keywords: AppStrings.Opt_MaximizeButton_Kw, sub: true);

        Add(layout, panel, AppStrings.Opt_BackForward, AppStrings.Opt_BackForward_Desc,
            () => Segmented([(BackForwardPlace.Bar, AppStrings.Opt_BackForward_Bar), (BackForwardPlace.DirectoryLines, AppStrings.Opt_BackForward_Lines), (BackForwardPlace.Both, AppStrings.Opt_BackForward_Both), (BackForwardPlace.Off, AppStrings.Opt_BackForward_Off)],
                Current.Layout.BackForward.Place, v => UpdateBackForward(b => b with { Place = v })), keywords: AppStrings.Opt_BackForward_Kw);
        Add(layout, panel, AppStrings.Opt_StepsListOpens, AppStrings.Opt_StepsListOpens_Desc,
            () => EnabledWhile(Segmented([(StepsListOpens.Button, AppStrings.Opt_StepsListOpens_Button), (StepsListOpens.Hold, AppStrings.Opt_StepsListOpens_Hold)],
                Current.Layout.BackForward.ListOpens, v => UpdateBackForward(b => b with { ListOpens = v })), s => s.Layout.BackForward.Place != BackForwardPlace.Off),
            keywords: AppStrings.Opt_StepsListOpens_Kw, sub: true);
        Add(layout, panel, AppStrings.Opt_StepsListed, AppStrings.Opt_StepsListed_Desc,
            () => EnabledWhile(Segmented([(StepsListed.ThisPanel, AppStrings.Opt_StepsListed_ThisPanel), (StepsListed.BothPanels, AppStrings.Opt_BothPanels)],
                Current.Layout.BackForward.Listed, v => UpdateBackForward(b => b with { Listed = v })), s => s.Layout.BackForward.Place != BackForwardPlace.Off),
            keywords: AppStrings.Opt_StepsListed_Kw, sub: true);
        Add(layout, panel, AppStrings.Opt_StepsKept, AppStrings.Opt_StepsKept_Desc, StepsKeptPicker, keywords: AppStrings.Opt_StepsKept_Kw, sub: true);
        Add(layout, panel, AppStrings.Opt_MouseSideButtons, AppStrings.Opt_MouseSideButtons_Desc,
            () => Toggle(Current.Layout.BackForward.MouseButtons, on => UpdateBackForward(b => b with { MouseButtons = on })), keywords: AppStrings.Opt_MouseSideButtons_Kw, sub: true);
        Bar(panel, AppStrings.Bar_ColumnHeader, AppStrings.Bar_ColumnHeader_Desc, b => b.ColumnHeader, (b, on) => b with { ColumnHeader = on }, AppStrings.Bar_ColumnHeader_Kw);
        Add(layout, panel, AppStrings.Opt_RowHeight, AppStrings.Opt_RowHeight_Desc, RowHeightPicker, wide: true, keywords: AppStrings.Opt_RowHeight_Kw);
        Bar(panel, AppStrings.Bar_InformationLine, AppStrings.Bar_InformationLine_Desc, b => b.InformationLine, (b, on) => b with { InformationLine = on }, AppStrings.Bar_InformationLine_Kw);

        var under = AppStrings.OptSec_BetweenAndUnder;
        Bar(under, AppStrings.Bar_MiddleToolbar, AppStrings.Bar_MiddleToolbar_Desc, b => b.MiddleToolbar, (b, on) => b with { MiddleToolbar = on }, AppStrings.Bar_MiddleToolbar_Kw);
        Bar(under, AppStrings.Bar_FunctionKeyBar, AppStrings.Bar_FunctionKeyBar_Desc, b => b.FunctionKeyBar, (b, on) => b with { FunctionKeyBar = on }, AppStrings.Bar_FunctionKeyBar_Kw);
        Add(layout, under, AppStrings.Opt_BarHeight, AppStrings.Opt_BarHeight_Desc,
            () => Segmented([(BarHeight.Lower, AppStrings.Opt_BarHeight_Lower), (BarHeight.Normal, AppStrings.Opt_BarHeight_Normal), (BarHeight.Taller, AppStrings.Opt_BarHeight_Taller)],
                Current.Layout.BarHeight, v => UpdateLayout(l => l with { BarHeight = v })), keywords: AppStrings.Opt_BarHeight_Kw);
        Add(layout, under, AppStrings.Opt_ComingLater, AppStrings.Opt_ComingLater_Desc, () => new Panel(), keywords: AppStrings.Opt_ComingLater_Kw, badge: CommandText.Milestone("Phase 3"));
    }

    private Control DriveStripPicker()
    {
        var layout = Current.Layout;
        StripMode? current = layout.Bars.DriveStrip ? layout.Strip : null;
        return Tips(Segmented<StripMode?>([(null, AppStrings.Common_Off), (StripMode.PerPanel, AppStrings.Opt_Strip_InEachPanel), (StripMode.Shared, AppStrings.Opt_Strip_OneForBoth)], current,
            mode => UpdateLayout(l => mode is { } m ? l with { Strip = m, Bars = l.Bars with { DriveStrip = true } } : l with { Bars = l.Bars with { DriveStrip = false } })),
            null, AppStrings.Opt_StripPerPanel_Desc, AppStrings.Opt_StripShared_Desc);
    }

    private static Control Tips(Control segmented, params string?[] tips)
    {
        var buttons = ((Panel)segmented).Children;
        for (var i = 0; i < tips.Length && i < buttons.Count; i++)
        {
            ToolTip.SetTip(buttons[i], tips[i]);
        }

        return segmented;
    }

    private Control MenuPlacementPicker()
    {
        var layout = Current.Layout;
        var current = !layout.Bars.MenuBar ? 2 : layout.MenuRow == MenuRow.OwnRow ? 1 : 0;
        return Tips(Segmented<int>(
            [(0, AppStrings.Opt_Menus_InTitleBar), (1, AppStrings.Opt_Menus_OwnRow), (2, AppStrings.Opt_Menus_BehindButton)],
            current,
            choice =>
            {
                UpdateLayout(l => choice switch
                {
                    0 => l with { MenuRow = MenuRow.InTitleBar, Bars = l.Bars with { MenuBar = true } },
                    1 => l with { MenuRow = MenuRow.OwnRow, Bars = l.Bars with { MenuBar = true } },
                    _ => l with { Bars = l.Bars with { MenuBar = false } },
                });
            }),
            AppStrings.Opt_Menus_InTitleBar_Desc, AppStrings.Opt_Menus_OwnRow_Desc, AppStrings.Opt_Menus_BehindButton_Desc);
    }

    private Control WindowTitlePicker()
    {
        var samplePath = AppStrings.Sample_WindowTitlePath;
        var presets = Core.Panels.WindowTitle.Presets;
        var template = new TextBox { Text = Current.Layout.WindowTitle, MinWidth = 160 };
        template.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(template, AppStrings.Opt_WindowTitle_Template);
        var preview = Text("", 13);
        var combo = new ComboBox { MinWidth = 180 };
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

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { combo, template } };
        Grid.SetColumn(template, 1);
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

    private void UpdateBackForward(Func<BackForwardSettings, BackForwardSettings> change)
        => UpdateLayout(l => l with { BackForward = change(l.BackForward) });

    private Control StepsKeptPicker()
    {
        var value = Text("", 13, FontWeight.SemiBold);
        var slider = new Slider
        {
            Minimum = BackForwardSettings.MinStepsKept,
            Maximum = BackForwardSettings.MaxStepsKept,
            Value = Current.Layout.BackForward.StepsKept,
            TickFrequency = 10,
            IsSnapToTickEnabled = true,
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Avalonia.Automation.AutomationProperties.SetName(slider, AppStrings.Opt_StepsKept);
        void Show(int n) => value.Text = Loc.Format(AppStrings.Opt_StepsKept_Value, n);
        Show(Current.Layout.BackForward.StepsKept);
        slider.ValueChanged += (_, e) =>
        {
            var n = (int)Math.Round(e.NewValue);
            Show(n);
            UpdateBackForward(b => b with { StepsKept = n });
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { slider, value } };
    }

    private Control FoldersKeptPicker()
    {
        var value = Text("", 13, FontWeight.SemiBold);
        var slider = new Slider
        {
            Minimum = WorkingDirectories.MinLimit,
            Maximum = WorkingDirectories.MaxLimit,
            Value = Current.Layout.DirectoryLine.FoldersKept,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Avalonia.Automation.AutomationProperties.SetName(slider, AppStrings.Opt_FoldersKept);
        void Show(int n) => value.Text = Loc.Format(AppStrings.Opt_FoldersKept_Value, n);
        Show(Current.Layout.DirectoryLine.FoldersKept);
        slider.ValueChanged += (_, e) =>
        {
            var n = (int)Math.Round(e.NewValue);
            Show(n);
            UpdateLayout(l => l with { DirectoryLine = l.DirectoryLine with { FoldersKept = n } });
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { slider, value } };
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

        var preview = new PreviewPanel(Settings, rows: 3, showInfo: false) { Width = 330, HorizontalAlignment = HorizontalAlignment.Left };
        left.Children.Add(preview);
        return left;
    }

    private void AddPanels()
    {
        const string panels = "panels";
        AddPanelLook(panels);
        AddTabs(panels);
    }

    internal const string HighlightPage = "highlight";

    private void AddHighlighting()
    {
        Add(HighlightPage, AppStrings.OptSec_HighlightRules, null, AppStrings.Opt_HighlightRules_Desc, () => new HighlightTable(Settings), wide: true, keywords: AppStrings.Opt_HighlightRules_Kw);
        Add(HighlightPage, AppStrings.OptSec_FolderLooks, null, AppStrings.Opt_FolderLooks_Desc, () => new FolderLookTable(Settings, _services.FolderLooks), wide: true, keywords: AppStrings.Opt_FolderLooks_Kw);
    }

    private static IEnumerable<int> FlatAskChoices(int current)
        => new[] { 10_000, 50_000, 100_000, 500_000, 1_000_000, current }.Where(n => n > 0).Distinct().Order().Append(0);

    private void AddViews()
    {
        const string panels = MainWindow.ViewsPage;
        Add(panels, AppStrings.OptSec_Views, AppStrings.Opt_NewPanelView, AppStrings.Opt_NewPanelView_Desc, () =>
            Combo([.. PanelViews.All.Select(v => (v, Panels.DirectoryLine.ViewName(v)))], Current.Panels.NewPanelView, v => UpdatePanels(p => p with { NewPanelView = v })),
            keywords: AppStrings.Opt_NewPanelView_Kw);
        Add(panels, AppStrings.OptSec_Views, AppStrings.Opt_AltWheelViews, AppStrings.Opt_AltWheelViews_Desc, () =>
            Toggle(Current.Panels.AltWheelViews, on => UpdatePanels(p => p with { AltWheelViews = on })), keywords: AppStrings.Opt_AltWheelViews_Kw);
        Add(panels, AppStrings.OptSec_Views, AppStrings.Opt_ViewButton, AppStrings.Opt_ViewButton_Desc, () =>
            Toggle(Current.Panels.ViewButton, on => UpdatePanels(p => p with { ViewButton = on })), keywords: AppStrings.Opt_ViewButton_Kw);
        Add(panels, AppStrings.OptSec_Thumbnails, AppStrings.Opt_ThumbSize, AppStrings.Opt_ThumbSize_Desc, ThumbnailSizePicker, keywords: AppStrings.Opt_ThumbSize_Kw);
        Add(panels, AppStrings.OptSec_Thumbnails, AppStrings.Opt_ThumbsOf, AppStrings.Opt_ThumbsOf_Desc, () =>
            Segmented([(ThumbnailFiles.All, AppStrings.Opt_ThumbsOf_All), (ThumbnailFiles.PicturesAndVideos, AppStrings.Opt_ThumbsOf_Pictures)],
                Current.Panels.ThumbnailsOf, v => UpdatePanels(p => p with { ThumbnailsOf = v })), keywords: AppStrings.Opt_ThumbsOf_Kw);
        Add(panels, AppStrings.OptSec_Tiles, AppStrings.Opt_TileLines, AppStrings.Opt_TileLines_Desc, () =>
            Segmented([(TileLines.SizeAndDate, AppStrings.Opt_TileLines_SizeDate), (TileLines.TypeAndSize, AppStrings.Opt_TileLines_TypeSize)],
                Current.Panels.TileLines, v => UpdatePanels(p => p with { TileLines = v })), keywords: AppStrings.Opt_TileLines_Kw);
        Add(panels, AppStrings.OptSec_ColumnsView, AppStrings.Opt_ColumnsWidth, AppStrings.Opt_ColumnsWidth_Desc, () =>
            Combo([.. ColumnsWidthChoices(Current.Panels.ColumnsWidth).Select(n => (n, Loc.Format(AppStrings.Opt_ColumnsWidth_Chars, n)))],
                Current.Panels.ColumnsWidth, v => UpdatePanels(p => p with { ColumnsWidth = v })), keywords: AppStrings.Opt_ColumnsView_Kw);
        Add(panels, AppStrings.OptSec_ColumnsView, AppStrings.Opt_ColumnsPreview, AppStrings.Opt_ColumnsPreview_Desc, () =>
            Toggle(Current.Panels.ColumnsPreview, on => UpdatePanels(p => p with { ColumnsPreview = on })), keywords: AppStrings.Opt_ColumnsView_Kw);
        Add(panels, AppStrings.OptSec_ColumnsView, AppStrings.Opt_PeekOnHover, AppStrings.Opt_PeekOnHover_Desc, () =>
            Toggle(Current.Panels.PeekOnHover, on => UpdatePanels(p => p with { PeekOnHover = on })), keywords: AppStrings.Opt_ColumnsView_Kw);
        Add(panels, AppStrings.OptSec_ViewColumns, null, AppStrings.Opt_ViewColumns_Desc, () => new ViewColumnsEditor(Settings), wide: true, keywords: AppStrings.Opt_ViewColumns_Kw);

        Add(panels, AppStrings.OptSec_Git, AppStrings.Opt_GitColors, AppStrings.Opt_GitColors_Desc, () =>
            Toggle(Current.Panels.Git.ColorNames, on => UpdatePanels(p => p with { Git = p.Git with { ColorNames = on } })), keywords: AppStrings.Opt_Git_Kw);
        Add(panels, AppStrings.OptSec_Git, AppStrings.Opt_GitColumn, AppStrings.Opt_GitColumn_Desc, () =>
            Toggle(Current.Panels.Git.ColumnInRepositories, on => UpdatePanels(p => p with { Git = p.Git with { ColumnInRepositories = on } })), keywords: AppStrings.Opt_Git_Kw);
        Add(panels, AppStrings.OptSec_Git, AppStrings.Opt_GitNetwork, AppStrings.Opt_GitNetwork_Desc, () =>
            Toggle(Current.Panels.Git.NetworkDrives, on => UpdatePanels(p => p with { Git = p.Git with { NetworkDrives = on } })), keywords: AppStrings.Opt_Git_Kw);
    }

    private static IEnumerable<int> ColumnsWidthChoices(int current)
        => new[] { 16, 20, 24, 28, 32, 40, 48, 64, current }.Distinct().Order();

    private void AddPanelLook(string panels)
    {
        Add(panels, AppStrings.OptSec_FocusSelection, null, AppStrings.Opt_FocusPreview, () => new PreviewPanel(Settings, rows: 5, showInfo: false, focusApart: true), wide: true, keywords: AppStrings.Opt_FocusLook_Kw);
        Add(panels, AppStrings.OptSec_FocusSelection, AppStrings.Opt_FocusLook, AppStrings.Opt_FocusLook_Desc, () =>
            Segmented([(MarkLook.WholeRow, AppStrings.Opt_Look_WholeRow), (MarkLook.NameOnly, AppStrings.Opt_Look_NameOnly)],
                Current.Panels.FocusLook, v => UpdatePanels(p => p with { FocusLook = v })), keywords: AppStrings.Opt_FocusLook_Kw);
        Add(panels, AppStrings.OptSec_FocusSelection, AppStrings.Opt_SelectionLook, AppStrings.Opt_SelectionLook_Desc, () =>
            Segmented([(MarkLook.WholeRow, AppStrings.Opt_Look_WholeRow), (MarkLook.NameOnly, AppStrings.Opt_Look_NameOnly)],
                Current.Panels.SelectionLook, v => UpdatePanels(p => p with { SelectionLook = v })), keywords: AppStrings.Opt_SelectionLook_Kw);
        Add(panels, AppStrings.OptSec_FocusSelection, AppStrings.Opt_FocusBackground, AppStrings.Opt_FocusBackground_Desc, () =>
            Segmented<MarkLook?>([(null, AppStrings.Common_Off), (MarkLook.WholeRow, AppStrings.Opt_Look_WholeRow), (MarkLook.NameOnly, AppStrings.Opt_Look_NameOnly)],
                Current.Panels.FocusBackground ? Current.Panels.FocusBackgroundLook : null,
                v => UpdatePanels(p => v is { } look ? p with { FocusBackground = true, FocusBackgroundLook = look } : p with { FocusBackground = false })), keywords: AppStrings.Opt_FocusBackground_Kw);

        Add(panels, AppStrings.OptSec_Listing, AppStrings.Opt_ShowHidden, AppStrings.Opt_ShowHidden_Desc, () =>
            Toggle(Current.Panels.ShowHiddenFiles, on => UpdatePanels(p => p with { ShowHiddenFiles = on })), keywords: AppStrings.Opt_ShowHidden_Kw);
        Add(panels, AppStrings.OptSec_Listing, AppStrings.Opt_HideDotFiles, AppStrings.Opt_HideDotFiles_Desc, () =>
            Toggle(Current.Panels.HideDotFiles, on => UpdatePanels(p => p with { HideDotFiles = on })), keywords: AppStrings.Opt_HideDotFiles_Kw);
        Add(panels, AppStrings.OptSec_Listing, AppStrings.Opt_ExtColumn, AppStrings.Opt_ExtColumn_Desc, () =>
            Toggle(DetailedShowsExtension, on => UpdatePanels(p => DetailedShowsExtension == on ? p : p with
            {
                ColumnViews = ColumnViews.With(p.ColumnViews, ColumnViews.Toggle(ColumnViews.For(p.ColumnViews, PanelView.Detailed), PanelColumn.Extension)),
            })), keywords: AppStrings.Opt_ExtColumn_Kw);
        Add(panels, AppStrings.OptSec_Sorting, AppStrings.Opt_NumericSort, AppStrings.Opt_NumericSort_Desc, () =>
            Toggle(Current.Panels.NumericSort, on => UpdatePanels(p => p with { NumericSort = on })), keywords: AppStrings.Opt_NumericSort_Kw);
        Add(panels, AppStrings.OptSec_Selection, AppStrings.Opt_NumPlusFolders, AppStrings.Opt_NumPlusFolders_Desc, () =>
            Toggle(Current.Panels.SelectFoldersWithNumPlus, on => UpdatePanels(p => p with { SelectFoldersWithNumPlus = on })), keywords: AppStrings.Opt_NumPlusFolders_Kw);
        Add(panels, AppStrings.OptSec_Renaming, AppStrings.Opt_ClickToRename, AppStrings.Opt_ClickToRename_Desc, () =>
            Toggle(Current.Panels.ClickToRename, on => UpdatePanels(p => p with { ClickToRename = on })), keywords: AppStrings.Opt_ClickToRename_Kw);

        Add(panels, AppStrings.OptSec_Filter, AppStrings.Opt_FilterFolders, AppStrings.Opt_FilterFolders_Desc, () =>
            Segmented([(false, AppStrings.Opt_FilterFolders_Stays), (true, AppStrings.Opt_FilterFolders_ThisOnly)],
                Current.Panels.FilterThisFolderOnly, v => UpdatePanels(p => p with { FilterThisFolderOnly = v })), keywords: AppStrings.Opt_Filter_Kw);
        Add(panels, AppStrings.OptSec_Filter, AppStrings.Opt_FilterTyping, AppStrings.Opt_FilterTyping_Desc, () =>
            Segmented([(false, AppStrings.Opt_FilterTyping_Search), (true, AppStrings.Opt_FilterTyping_Filter)],
                Current.Panels.QuickFilterFirst, v => UpdatePanels(p => p with { QuickFilterFirst = v })), keywords: AppStrings.Opt_FilterTyping_Kw);
        Add(panels, AppStrings.OptSec_Filter, AppStrings.Opt_HiddenNames, AppStrings.Opt_HiddenNames_Desc, () =>
            Segmented([(false, AppStrings.Opt_HiddenNames_Leave), (true, AppStrings.Opt_HiddenNames_Stay)],
                Current.Panels.HiddenNamesStay, v => UpdatePanels(p => p with { HiddenNamesStay = v })), keywords: AppStrings.Opt_HiddenNames_Kw);

        Add(panels, AppStrings.OptSec_Flat, AppStrings.Opt_FlatFolders, AppStrings.Opt_FlatFolders_Desc, () =>
            Segmented([(FlatFolderShows.Column, AppStrings.Opt_FlatFolders_Column), (FlatFolderShows.Headings, AppStrings.Opt_FlatFolders_Headings), (FlatFolderShows.InName, AppStrings.Opt_FlatFolders_InName)],
                Current.Panels.FlatFolders, v => UpdatePanels(p => p with { FlatFolders = v })), keywords: AppStrings.Opt_Flat_Kw);
        Add(panels, AppStrings.OptSec_Flat, AppStrings.Opt_FlatAsk, AppStrings.Opt_FlatAsk_Desc, () =>
            Combo([.. FlatAskChoices(Current.Panels.FlatAskAbove).Select(n => (n, n == 0 ? AppStrings.Opt_FlatAsk_Never : Loc.Format(AppStrings.Opt_FlatAsk_Above, n)))],
                Current.Panels.FlatAskAbove, v => UpdatePanels(p => p with { FlatAskAbove = v })), keywords: AppStrings.Opt_Flat_Kw);

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

        Add(panels, AppStrings.OptSec_SizesDates, null, AppStrings.Opt_LookPreview, () => new PreviewPanel(Settings, rows: 5, showInfo: true), wide: true, keywords: AppStrings.Opt_LookPreview_Kw);
        Add(panels, AppStrings.OptSec_SizesDates, AppStrings.Opt_SizeStyle, AppStrings.Opt_SizeStyle_Desc, SizeStylePicker, wide: true, keywords: AppStrings.Opt_SizeStyle_Kw);
        Add(panels, AppStrings.OptSec_SizesDates, AppStrings.Opt_SizeUnits, AppStrings.Opt_SizeUnits_Desc, SizeUnitsPicker, wide: true, keywords: AppStrings.Opt_SizeUnits_Kw);
        Add(panels, AppStrings.OptSec_SizesDates, AppStrings.Opt_Grouping, null, () =>
            Segmented([(DigitGrouping.Windows, AppStrings.Opt_Grouping_Windows), (DigitGrouping.Space, AppStrings.Opt_Grouping_Space), (DigitGrouping.None, AppStrings.Opt_Grouping_None)],
                Current.Panels.DigitGrouping, v => { UpdatePanels(p => p with { DigitGrouping = v }); RebuildLater(); }), keywords: AppStrings.Opt_Grouping_Kw);
        Add(panels, AppStrings.OptSec_SizesDates, AppStrings.Opt_InfoSizes, AppStrings.Opt_InfoSizes_Desc, () =>
            Segmented([(false, AppStrings.Opt_InfoSizes_Short), (true, AppStrings.Opt_InfoSizes_Panel)],
                Current.Panels.InfoSizesAsPanel, v => UpdatePanels(p => p with { InfoSizesAsPanel = v })), keywords: AppStrings.Opt_InfoSizes_Kw);
        Add(panels, AppStrings.OptSec_SizesDates, AppStrings.Opt_FolderSize, AppStrings.Opt_FolderSize_Desc, () =>
            Segmented([(FolderSizeText.Folder, Core.Resources.Strings.InfoLine_Folder), (FolderSizeText.Dir, AppStrings.Panel_Dir), (FolderSizeText.Blank, AppStrings.Opt_FolderSize_Blank)],
                Current.Panels.FolderSize, v => UpdatePanels(p => p with { FolderSize = v })), keywords: AppStrings.Opt_FolderSize_Kw);
        Add(panels, AppStrings.OptSec_SizesDates, AppStrings.Opt_DateTime, AppStrings.Opt_DateTime_Desc, DateFormatPicker, keywords: AppStrings.Opt_DateTime_Kw);
        Add(panels, AppStrings.OptSec_SizesDates, AppStrings.Opt_Time, AppStrings.Opt_Time_Desc, TimeFormatPicker, keywords: AppStrings.Opt_Time_Kw);
        Add(panels, AppStrings.OptSec_Columns, AppStrings.Opt_Alignment, AppStrings.Opt_Alignment_Desc, () =>
            Segmented([(ColumnAlignment.NumbersRight, AppStrings.Opt_Alignment_Right), (ColumnAlignment.AllLeft, AppStrings.Opt_Alignment_Left)],
                Current.Panels.ColumnAlignment, v => UpdatePanels(p => p with { ColumnAlignment = v })), keywords: AppStrings.Opt_Alignment_Kw);
        Add(panels, AppStrings.OptSec_Columns, AppStrings.Opt_LongNames, AppStrings.Opt_LongNames_Desc, () =>
            Segmented([(LongNames.CutEnd, AppStrings.Opt_LongNames_End), (LongNames.CutMiddle, AppStrings.Opt_LongNames_Middle)],
                Current.Panels.LongNames, v => UpdatePanels(p => p with { LongNames = v })), keywords: AppStrings.Opt_LongNames_Kw);
        Add(panels, AppStrings.OptSec_Columns, AppStrings.Opt_RowStripes, AppStrings.Opt_RowStripes_Desc, () =>
            Toggle(Current.Panels.RowStripes, on => UpdatePanels(p => p with { RowStripes = on })), keywords: AppStrings.Opt_RowStripes_Kw);


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

    private Control ThumbnailSizePicker()
    {
        var value = Text("", 14, FontWeight.SemiBold);
        var slider = new Slider
        {
            Minimum = PanelSettings.MinThumbnailSize,
            Maximum = PanelSettings.MaxThumbnailSize,
            Value = Current.Panels.ThumbnailSize,
            TickFrequency = 16,
            IsSnapToTickEnabled = true,
            Width = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Avalonia.Automation.AutomationProperties.SetName(slider, AppStrings.Opt_ThumbSize);
        var sample = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = AppStrings.Opt_ThumbSize_Sample, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap },
        }.Res(Border.BackgroundProperty, "Brush.Hover").Res(Border.BorderBrushProperty, "Brush.Border");
        void Show(int px)
        {
            value.Text = string.Create(CultureInfo.CurrentCulture, $"{px} px");
            sample.Width = sample.Height = px;
        }

        Show(Current.Panels.ThumbnailSize);
        slider.ValueChanged += (_, e) =>
        {
            var px = (int)Math.Round(e.NewValue);
            Show(px);
            UpdatePanels(p => p with { ThumbnailSize = px });
        };
        return new StackPanel { Spacing = 10, Children = { value, slider, sample } };
    }

    private bool DetailedShowsExtension => ColumnViews.For(Current.Panels.ColumnViews, PanelView.Detailed).Columns.Any(c => c.Column == PanelColumn.Extension && c.Shown);

    private Control SizeUnitsPicker()
    {
        var example = Secondary("");
        void Show(SizeUnits units)
        {
            var f = new DisplayFormat(units, PanelSettings.DateFormatIso, CultureInfo.CurrentCulture, style: Current.Panels.SizeStyle, grouping: Current.Panels.DigitGrouping);
            example.Text = Loc.Format(AppStrings.Opt_SizeExample, f.ColumnSize(25_690_112), f.ColumnSize(2_048));
        }

        Show(Current.Panels.SizeUnits);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Segmented(
            [(SizeUnits.Windows, AppStrings.Opt_SizeUnits_Windows), (SizeUnits.Binary, AppStrings.Opt_SizeUnits_Binary), (SizeUnits.Decimal, AppStrings.Opt_SizeUnits_Decimal)],
            Current.Panels.SizeUnits,
            units =>
            {
                Show(units);
                UpdatePanels(p => p with { SizeUnits = units });
                RebuildLater();
            }));
        panel.Children.Add(example);
        return panel;
    }

    private Control SizeStylePicker()
    {
        string Sample(SizeStyle style) => new DisplayFormat(Current.Panels.SizeUnits, PanelSettings.DateFormatIso, CultureInfo.CurrentCulture, style: style, grouping: Current.Panels.DigitGrouping).ColumnSize(1_263_616);
        return Segmented(
            [(SizeStyle.Short, Loc.Format(AppStrings.Opt_SizeStyle_Short, Sample(SizeStyle.Short))), (SizeStyle.Kilobytes, Loc.Format(AppStrings.Opt_SizeStyle_Kilobytes, Sample(SizeStyle.Kilobytes))),
                (SizeStyle.Bytes, Loc.Format(AppStrings.Opt_SizeStyle_Bytes, Sample(SizeStyle.Bytes)))],
            Current.Panels.SizeStyle,
            style =>
            {
                UpdatePanels(p => p with { SizeStyle = style });
                RebuildLater();
            });
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
            (PanelSettings.DateFormatRelative, Loc.Format(AppStrings.Opt_Date_Relative, new DisplayFormat(SizeUnits.Windows, PanelSettings.DateFormatRelative, CultureInfo.CurrentCulture, Current.Panels.TimeFormat).ColumnDate(DateTime.Now.AddHours(-2), DateTime.Now))),
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
        var sharing = Current.Sharing;
        if (sharing.IsOn)
        {
            Add(advanced, yours, AppStrings.Share_On_Title, SharedDescription(sharing), () => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { ActionButton(AppStrings.Share_Change, () => _ = ShareAsync()), ActionButton(AppStrings.Share_Stop, StopSharing) },
            }, keywords: AppStrings.Share_Kw);
        }
        else
        {
            Add(advanced, yours, AppStrings.Share_Title, AppStrings.Share_Off_Desc, () => ActionButton(AppStrings.Share_Button, () => _ = ShareAsync()), keywords: AppStrings.Share_Kw);
        }

        Add(advanced, yours, AppStrings.Opt_ExportSettings, AppStrings.Opt_ExportSettings_Desc, () => ActionButton(AppStrings.Opt_ExportSettings_Button, () => _ = SettingsFiles.ExportAsync()), keywords: AppStrings.Opt_YourSettings_Kw);
        Add(advanced, yours, AppStrings.Opt_ImportSettings, AppStrings.Opt_ImportSettings_Desc, () => ActionButton(AppStrings.Opt_ImportSettings_Button, () => _ = SettingsFiles.ImportAsync()), keywords: AppStrings.Opt_YourSettings_Kw);
        Add(advanced, yours, AppStrings.Opt_ImportSalamander, AppStrings.Opt_ImportSalamander_Desc, () => ActionButton(AppStrings.Opt_ImportSalamander_Button, () => _ = SettingsFiles.ImportSalamanderAsync()), keywords: AppStrings.Opt_ImportSalamander_Kw);
        Add(advanced, yours, AppStrings.Opt_ResetAll, AppStrings.Opt_ResetAll_Desc, () => ActionButton(AppStrings.Opt_ResetAll_Button, () => _ = SettingsFiles.ResetAllAsync()), keywords: AppStrings.Opt_YourSettings_Kw);
    }

    private string SharedDescription(SharingSettings sharing)
    {
        var parts = sharing.Parts.ToHashSet();
        var shareable = SharingSettings.Shareable.ToHashSet();
        var what = parts.SetEquals(shareable) ? AppStrings.Share_Parts_All
            : parts.SetEquals(new SharingSettings().Parts) ? AppStrings.Share_Parts_Default
            : string.Join(", ", Dialogs.ShareSettingsDialog.Groups.Where(g => g.Parts.All(parts.Contains)).Select(g => g.Label));
        var last = _services.Sharing.LastTaken is { } taken
            ? Loc.Format(AppStrings.Share_LastFrom, taken.Pc, taken.When.ToString("t", System.Globalization.CultureInfo.CurrentCulture), string.Join(", ", taken.Changes.Take(3).Select(c => c.Name)))
            : "";
        return Loc.Format(AppStrings.Share_On_Desc, sharing.Folder, last, what, sharing.AskFirst ? AppStrings.Share_Mode_Ask : AppStrings.Share_Mode_AtOnce);
    }

    internal async Task ShareAsync()
    {
        var before = Current.Sharing;
        var dialog = new Dialogs.ShareSettingsDialog(before, CloudFolders.OnThisPc());
        ShareDialog = dialog;
        SharingSettings? chosen;
        try
        {
            chosen = await dialog.ShowDialog<SharingSettings?>(this);
        }
        finally
        {
            ShareDialog = null;
        }

        if (chosen is null)
        {
            return;
        }

        SharedCopy? read = null;
        var sameFolder = before.IsOn && string.Equals(before.Folder, chosen.Folder, StringComparison.OrdinalIgnoreCase);
        if (!sameFolder)
        {
            try
            {
                if (Sharing.SharedSettingsService.Peek(chosen.Folder, Current, chosen.Parts.ToHashSet()) is var (copy, import))
                {
                    if (import.Changes.Count > 0)
                    {
                        if (await Dialogs.SettingsTransferDialog.ForImport(import).ShowDialog<object?>(this) is not IReadOnlySet<SettingsPart> parts)
                        {
                            return;
                        }

                        Settings.Update(_ => import.Apply(parts));
                    }

                    read = copy;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _settingsNotice = (Loc.Format(AppStrings.Share_Problem, chosen.Folder, ex.Message), null);
                RebuildLater();
                return;
            }
        }

        if (sameFolder)
        {
            _services.Sharing.Change(chosen);
        }
        else
        {
            _services.Sharing.TurnOn(chosen, read);
        }

        RebuildLater();
    }

    private void StopSharing()
    {
        _services.Sharing.TurnOff();
        RebuildLater();
    }

    internal Dialogs.ShareSettingsDialog? ShareDialog { get; private set; }

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
