// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/OptionsWindow.Viewers.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

public sealed partial class OptionsWindow
{

    private void AddViewers()
    {
        const string page = "viewers";
        Add(page, AppStrings.OptSec_ViewerMasks, null, null, ViewerMaskTable, wide: true, keywords: AppStrings.Opt_ViewerMasks_Kw);

        var window = AppStrings.OptSec_ViewerWindow;
        Add(page, window, AppStrings.Opt_ViewerTitle, AppStrings.Opt_ViewerTitle_Desc, () => Segmented(
            [(ViewerTitle.NameAndFolder, AppStrings.Opt_ViewerTitle_NameFolder), (ViewerTitle.FullPath, AppStrings.Opt_ViewerTitle_FullPath)],
            Current.Viewers.Title, v => UpdateViewers(x => x with { Title = v })), keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, window, AppStrings.Opt_FindBar, AppStrings.Opt_FindBar_Desc, () => Segmented(
            [(FindBarPosition.Top, AppStrings.Opt_FindBar_Top), (FindBarPosition.Bottom, AppStrings.Opt_FindBar_Bottom)],
            Current.Viewers.FindBar, v => UpdateViewers(x => x with { FindBar = v })), keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, window, AppStrings.Opt_ViewerClose, AppStrings.Opt_ViewerClose_Desc, () => Segmented(
            [(ViewerClose.EscOrAltF4, AppStrings.Opt_ViewerClose_Esc), (ViewerClose.AltF4Only, AppStrings.Opt_ViewerClose_AltF4)],
            Current.Viewers.Close, v => UpdateViewers(x => x with { Close = v })), keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, window, AppStrings.Opt_ViewButtons, AppStrings.Opt_ViewButtons_Desc, () => Segmented(
            [(ViewButtonOrder.MostUsedFirst, AppStrings.Opt_ViewButtons_MostUsed), (ViewButtonOrder.KeyOrder, AppStrings.Opt_ViewButtons_Keys)],
            Current.Viewers.ViewButtons, v => UpdateViewers(x => x with { ViewButtons = v })), keywords: AppStrings.Opt_Viewers_Kw);

        var text = AppStrings.OptSec_TextHex;
        Add(page, text, AppStrings.Opt_OpenAs, AppStrings.Opt_OpenAs_Desc, () => Segmented(
            [(ViewerOpenAs.Automatic, AppStrings.Opt_Automatic), (ViewerOpenAs.Text, AppStrings.ViewerKind_Text), (ViewerOpenAs.Hex, AppStrings.ViewerKind_Hex)],
            Current.Viewers.OpenAs, v => UpdateViewers(x => x with { OpenAs = v })), keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, text, AppStrings.Opt_ViewerEncoding, AppStrings.Opt_ViewerEncoding_Desc, EncodingPicker, keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, text, AppStrings.Opt_ViewerWrap, AppStrings.Opt_ViewerWrap_Desc,
            () => Toggle(Current.Viewers.Wrap, on => UpdateViewers(x => x with { Wrap = on })), keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, text, AppStrings.Viewer_LineNumbers, null,
            () => Toggle(Current.Viewers.LineNumbers, on => UpdateViewers(x => x with { LineNumbers = on })), keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, text, AppStrings.Opt_TextColors, AppStrings.Opt_TextColors_Desc,
            () => Toggle(Current.Viewers.TextColors, on => UpdateViewers(x => x with { TextColors = on })), keywords: AppStrings.Opt_TextColors_Kw);
        Add(page, text, AppStrings.Opt_ExactColors, AppStrings.Opt_ExactColors_Desc, ExactColorsEditor, keywords: AppStrings.Opt_TextColors_Kw);
        Add(page, text, AppStrings.Opt_TabWidth, null, () => Segmented(
            [.. ViewerSettings.TabWidthChoices.Select(n => (n, n.ToString(System.Globalization.CultureInfo.CurrentCulture)))],
            Current.Viewers.TabWidth, v => UpdateViewers(x => x with { TabWidth = v })), keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, text, AppStrings.Opt_HexBytes, AppStrings.Opt_HexBytes_Desc, () => Segmented(
            [.. ViewerSettings.BytesPerRowChoices.Select(n => (n, n == 0 ? AppStrings.Opt_HexBytes_Fit : n.ToString(System.Globalization.CultureInfo.CurrentCulture)))],
            Current.Viewers.HexBytesPerRow, v => UpdateViewers(x => x with { HexBytesPerRow = v })), keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, text, AppStrings.Opt_HexGroup, null, () => Segmented(
            [.. ViewerSettings.GroupChoices.Select(n => (n, n.ToString(System.Globalization.CultureInfo.CurrentCulture)))],
            Current.Viewers.HexGroup, v => UpdateViewers(x => x with { HexGroup = v })), keywords: AppStrings.Opt_Viewers_Kw);

        Control HexOrDecimal(bool current, Func<ViewerSettings, bool, ViewerSettings> set) => Segmented(
            [(false, AppStrings.Opt_HexOffsets_Hex), (true, AppStrings.Opt_HexOffsets_Decimal)], current, v => UpdateViewers(x => set(x, v)));
        Control SizeChoice(SizeFormat current, Func<ViewerSettings, SizeFormat, ViewerSettings> set) => Segmented(
            [(SizeFormat.Hex, AppStrings.Opt_HexOffsets_Hex), (SizeFormat.Decimal, AppStrings.Opt_HexOffsets_Decimal), (SizeFormat.Units, AppStrings.Viewer_FormatUnits)], current, v => UpdateViewers(x => set(x, v)));
        Add(page, text, AppStrings.Opt_HexOffsets, AppStrings.Opt_HexOffsets_Desc,
            () => HexOrDecimal(Current.Viewers.DecimalOffsets, (x, v) => x with { DecimalOffsets = v }), keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, text, AppStrings.Opt_HexSelection, AppStrings.Opt_HexValues_Desc,
            () => SizeChoice(Current.Viewers.SelectionFormat, (x, v) => x with { SelectionFormat = v }), keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, text, AppStrings.Opt_HexSize, AppStrings.Opt_HexValues_Desc,
            () => SizeChoice(Current.Viewers.SizeFormat, (x, v) => x with { SizeFormat = v }), keywords: AppStrings.Opt_Viewers_Kw);
        Add(page, text, AppStrings.Opt_ByteColors, AppStrings.Opt_ByteColors_Desc, () => Segmented(
            [(ByteColors.Two, AppStrings.Opt_ByteColors_Two), (ByteColors.ByKind, AppStrings.Opt_ByteColors_ByKind)],
            Current.Viewers.HexByteColors, v => UpdateViewers(x => x with { HexByteColors = v })), keywords: AppStrings.Opt_ByteColors_Kw);

        var images = AppStrings.OptSec_Images;
        Add(page, images, AppStrings.Opt_Canvas, AppStrings.Opt_Canvas_Desc, CanvasPicker, keywords: AppStrings.Opt_Images_Kw);
        Add(page, images, AppStrings.Opt_ImageZoom, null, () => Segmented(
            [(ImageZoom.Fit, AppStrings.Viewer_Fit), (ImageZoom.Width, AppStrings.Viewer_Width), (ImageZoom.Actual, AppStrings.Viewer_Actual)],
            Current.Viewers.ImageZoom, v => UpdateViewers(x => x with { ImageZoom = v })), keywords: AppStrings.Opt_Images_Kw);
        Add(page, images, AppStrings.Opt_PhotoInfo, AppStrings.Opt_PhotoInfo_Desc, () => Segmented(
            [(InfoPlace.Panel, AppStrings.Opt_PhotoInfo_Panel), (InfoPlace.Window, AppStrings.Opt_PhotoInfo_Window)],
            Current.Viewers.PhotoInfoPlace, v => UpdateViewers(x => x with { PhotoInfoPlace = v })), keywords: AppStrings.Opt_Images_Kw);
        Add(page, images, AppStrings.Opt_MapSite, AppStrings.Opt_MapSite_Desc, MapSitePicker, keywords: AppStrings.Opt_Images_Kw);

        Add(page, AppStrings.OptSec_MediaFiles, AppStrings.Opt_MediaEnter, AppStrings.Opt_MediaEnter_Desc, () => Segmented(
            [(MediaEnter.OpenInDefaultApp, AppStrings.Viewer_MediaOpen), (MediaEnter.Nothing, AppStrings.Opt_MediaEnter_Nothing)],
            Current.Viewers.MediaEnter, v => UpdateViewers(x => x with { MediaEnter = v })), keywords: AppStrings.Opt_Media_Kw);
        Add(page, AppStrings.OptSec_MediaFiles, AppStrings.Opt_VideoFrame, AppStrings.Opt_VideoFrame_Desc,
            () => Toggle(Current.Viewers.VideoFrame, on => UpdateViewers(x => x with { VideoFrame = on })), keywords: AppStrings.Opt_Media_Kw);

        var markdown = AppStrings.OptSec_Markdown;
        Add(page, markdown, AppStrings.Opt_MdWidth, AppStrings.Opt_MdWidth_Desc, () => Segmented(
            [(false, AppStrings.Opt_MdWidth_Reading), (true, AppStrings.Viewer_FullWidth)],
            Current.Viewers.MarkdownFullWidth, v => UpdateViewers(x => x with { MarkdownFullWidth = v })), keywords: AppStrings.Opt_Md_Kw);
        Add(page, markdown, AppStrings.Opt_MdWebImages, AppStrings.Opt_MdWebImages_Desc,
            () => Toggle(Current.Viewers.MarkdownWebImages, on => UpdateViewers(x => x with { MarkdownWebImages = on })), keywords: AppStrings.Opt_Md_Kw);
        Add(page, markdown, AppStrings.Opt_MdContents, AppStrings.Opt_MdContents_Desc,
            () => Toggle(Current.Viewers.MarkdownContents, on => UpdateViewers(x => x with { MarkdownContents = on })), keywords: AppStrings.Opt_Md_Kw);

        var notebooks = AppStrings.OptSec_Notebooks;
        Add(page, notebooks, null, AppStrings.Opt_NbNote, () => new Panel(), wide: true, keywords: AppStrings.Opt_Nb_Kw);
        Add(page, notebooks, AppStrings.Opt_NbCode, AppStrings.Opt_NbCode_Desc, () => Segmented(
            [(false, AppStrings.Opt_NbCode_Shown), (true, AppStrings.Opt_NbCode_Hidden)],
            Current.Viewers.NotebookHideCode, v => UpdateViewers(x => x with { NotebookHideCode = v })), keywords: AppStrings.Opt_Nb_Kw);
        Add(page, notebooks, AppStrings.Opt_NbOutputs, AppStrings.Opt_NbOutputs_Desc, () => Segmented(
            [(true, AppStrings.Opt_NbOutputs_Folded), (false, AppStrings.Opt_NbOutputs_Whole)],
            Current.Viewers.NotebookFoldOutputs, v => UpdateViewers(x => x with { NotebookFoldOutputs = v })), keywords: AppStrings.Opt_Nb_Kw);

        var html = AppStrings.OptSec_Html;
        Add(page, html, AppStrings.Opt_HtmlScripts, AppStrings.Opt_HtmlScripts_Desc, () => Segmented(
            [(false, AppStrings.Opt_HtmlScripts_Off), (true, AppStrings.Opt_HtmlScripts_Run)],
            Current.Viewers.HtmlScripts, v => UpdateViewers(x => x with { HtmlScripts = v })), keywords: AppStrings.Opt_Html_Kw);
        Add(page, html, AppStrings.Opt_HtmlWeb, AppStrings.Opt_HtmlWeb_Desc,
            () => Toggle(Current.Viewers.HtmlWebContent, on => UpdateViewers(x => x with { HtmlWebContent = on })), keywords: AppStrings.Opt_Html_Kw);

        var trees = AppStrings.OptSec_Tree;
        Add(page, trees, AppStrings.Opt_TreeOpens, null, () => Segmented(
            [(TreeOpening.FirstLevel, AppStrings.Opt_TreeOpens_First), (TreeOpening.AllFolded, AppStrings.Opt_TreeOpens_Folded)],
            Current.Viewers.TreeOpens, v => UpdateViewers(x => x with { TreeOpens = v })), keywords: AppStrings.Opt_Tree_Kw);
        Add(page, trees, AppStrings.Opt_TreePath, AppStrings.Opt_TreePath_Desc, () => Segmented(
            [(DataPathStyle.Dollar, AppStrings.Opt_TreePath_Dollar), (DataPathStyle.Dotted, AppStrings.Opt_TreePath_Dotted), (DataPathStyle.Slash, AppStrings.Opt_TreePath_Slash)],
            Current.Viewers.TreePath, v => UpdateViewers(x => x with { TreePath = v })), keywords: AppStrings.Opt_Tree_Kw);

        Add(page, "", null, AppStrings.Opt_ViewersLater, () => new Panel(), wide: true);
    }

    private void UpdateViewers(Func<ViewerSettings, ViewerSettings> change) => Settings.Update(s => s with { Viewers = change(s.Viewers) });

    private Control ExactColorsEditor()
    {
        var box = new TextBox { Text = Current.Viewers.ExactColorsMB.ToString(System.Globalization.CultureInfo.CurrentCulture), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
        Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Opt_ExactColors);
        box.LostFocus += (_, _) =>
        {
            if (int.TryParse(box.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out var mb) && mb > 0)
            {
                UpdateViewers(v => v with { ExactColorsMB = mb });
            }

            box.Text = Current.Viewers.ExactColorsMB.ToString(System.Globalization.CultureInfo.CurrentCulture);
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, Text(AppStrings.Opt_MB, 13).Also(t => t.VerticalAlignment = VerticalAlignment.Center) } };
    }

    private bool _customChosen;

    private MapSite _mapSiteBefore = MapSite.OpenStreetMap;

    private static bool CustomReady(ViewerSettings v) => !string.IsNullOrWhiteSpace(v.MapCustomName) && MapLinks.IsValidTemplate(v.MapCustomUrl);

    internal void SettleMapSite() => _customChosen = false;

    private Control MapSitePicker()
    {
        var v = Current.Viewers;
        var custom = _customChosen || v.MapSite == MapSite.Custom;
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(Segmented(
            [(MapSite.OpenStreetMap, AppStrings.Map_OpenStreetMap), (MapSite.GoogleMaps, AppStrings.Map_GoogleMaps), (MapSite.Custom, AppStrings.Opt_MapSite_Custom)],
            custom ? MapSite.Custom : v.MapSite, site =>
            {
                if (site == MapSite.Custom && Current.Viewers.MapSite != MapSite.Custom)
                {
                    _mapSiteBefore = Current.Viewers.MapSite;
                }

                _customChosen = site == MapSite.Custom;
                if (site != MapSite.Custom || CustomReady(Current.Viewers))
                {
                    UpdateViewers(x => x with { MapSite = site });
                }

                RebuildLater();
            }));
        if (!custom)
        {
            return panel;
        }

        _customChosen = true;
        var name = new TextBox { Text = v.MapCustomName, Width = 320, PlaceholderText = AppStrings.Opt_MapCustomName_Hint };
        var url = new TextBox { Text = v.MapCustomUrl, Width = 320, PlaceholderText = AppStrings.Opt_MapCustomUrl_Placeholder }.Res(TextBox.FontFamilyProperty, "Font.Mono");
        Avalonia.Automation.AutomationProperties.SetName(name, AppStrings.Opt_MapCustomName);
        Avalonia.Automation.AutomationProperties.SetName(url, AppStrings.Opt_MapCustomUrl);
        var help = Text(AppStrings.Opt_MapCustomUrl_Help, 12, brush: "Brush.SecondaryText");
        help.TextWrapping = TextWrapping.Wrap;
        help.Width = 320;
        void Changed()
        {
            if (Current.Viewers.MapCustomName != (name.Text ?? "") || Current.Viewers.MapCustomUrl != (url.Text ?? ""))
            {
                UpdateViewers(x => x with { MapCustomName = name.Text ?? "", MapCustomUrl = url.Text ?? "" });
            }

            var now = Current.Viewers;
            if (CustomReady(now) != (now.MapSite == MapSite.Custom))
            {
                UpdateViewers(x => x with { MapSite = CustomReady(now) ? MapSite.Custom : _mapSiteBefore == MapSite.Custom ? MapSite.OpenStreetMap : _mapSiteBefore });
            }

            var badAddress = !string.IsNullOrWhiteSpace(url.Text) && !MapLinks.IsValidTemplate(url.Text);
            help.Text = badAddress ? AppStrings.Opt_MapCustomUrl_Bad : CustomReady(now) ? AppStrings.Opt_MapCustomUrl_Help : AppStrings.Opt_MapCustom_NotYet;
            help.Res(TextBlock.ForegroundProperty, badAddress ? "Brush.Danger" : "Brush.SecondaryText");
        }

        foreach (var box in new[] { name, url })
        {
            box.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty)
                {
                    Changed();
                }
            };
        }
        Changed();
        panel.Children.Add(Labeled(AppStrings.Opt_MapCustomName, name));
        panel.Children.Add(Labeled(AppStrings.Opt_MapCustomUrl, url));
        panel.Children.Add(help);
        return panel;

        Control Labeled(string label, Control box)
        {
            var text = Text(label, 13);
            text.VerticalAlignment = VerticalAlignment.Center;
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { text, box } };
        }
    }

    private Control CanvasPicker()
    {
        var v = Current.Viewers;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        Button Choice(CanvasColor canvas, IBrush look)
        {
            var swatch = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Background = look, BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)) };
            var button = new Button { Padding = new Thickness(3), MinWidth = 0, Content = swatch, Background = Brushes.Transparent };
            if (v.ImageCanvas == canvas)
            {
                button.Res(Button.BorderBrushProperty, "Brush.Text");
                button.BorderThickness = new Thickness(2);
            }

            var label = Salandual.App.Viewer.ViewerWindow.CanvasLabel(canvas);
            ToolTip.SetTip(button, label);
            Avalonia.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) =>
            {
                UpdateViewers(x => x with { ImageCanvas = canvas });
                RebuildLater();
            };
            return button;
        }

        row.Children.Add(Choice(CanvasColor.Dark, new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C))));
        row.Children.Add(Choice(CanvasColor.Black, Brushes.Black));
        row.Children.Add(Choice(CanvasColor.Grey, new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A))));
        row.Children.Add(Choice(CanvasColor.Magenta, new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0xFF))));
        row.Children.Add(Choice(CanvasColor.Theme, new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.White, 0.5), new GradientStop(Color.FromRgb(0x27, 0x27, 0x27), 0.5) },
        }));
        var custom = ColorMath.TryParseHex(v.CanvasCustom, out var c) ? c : new Rgb(0x1C, 0x1C, 0x1C);
        var more = ColorButton(custom, color =>
        {
            UpdateViewers(x => x with { ImageCanvas = CanvasColor.Custom, CanvasCustom = ColorMath.ToHex(color) });
            RebuildLater();
        }, AppStrings.Viewer_Canvas_Custom);
        if (v.ImageCanvas == CanvasColor.Custom)
        {
            more.Res(Button.BorderBrushProperty, "Brush.Text");
            more.BorderThickness = new Thickness(2);
        }
        else
        {
            more.Content = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = new ConicGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0xE7, 0x4C, 0x3C), 0), new GradientStop(Color.FromRgb(0xF1, 0xC4, 0x0F), 0.2),
                        new GradientStop(Color.FromRgb(0x2E, 0xCC, 0x71), 0.4), new GradientStop(Color.FromRgb(0x34, 0x98, 0xDB), 0.6),
                        new GradientStop(Color.FromRgb(0x9B, 0x59, 0xB6), 0.8), new GradientStop(Color.FromRgb(0xE7, 0x4C, 0x3C), 1),
                    },
                },
            };
        }

        ToolTip.SetTip(more, AppStrings.Viewer_Canvas_Custom);
        row.Children.Add(more);
        return row;
    }

    private Control EncodingPicker()
    {
        var combo = new ComboBox { MinWidth = 200 };
        combo.Items.Add(new ComboBoxItem { Content = AppStrings.Opt_Automatic, Tag = null });
        foreach (var e in TextEncodings.All)
        {
            combo.Items.Add(new ComboBoxItem { Content = e.Name, Tag = e.Id });
        }

        combo.SelectedIndex = Math.Max(0, combo.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string?)i.Tag == Current.Viewers.Encoding));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item)
            {
                UpdateViewers(x => x with { Encoding = (string?)item.Tag });
            }
        };
        return combo;
    }

    private static string KindLabel(ViewerKind kind) => kind switch
    {
        ViewerKind.Automatic => AppStrings.ViewerKind_Automatic,
        ViewerKind.Text => AppStrings.ViewerKind_Text,
        ViewerKind.Hex => AppStrings.ViewerKind_Hex,
        ViewerKind.Image => AppStrings.ViewerKind_Image,
        ViewerKind.Media => AppStrings.ViewerKind_Media,
        ViewerKind.Notebook => AppStrings.ViewerKind_Notebook,
        ViewerKind.Tree => AppStrings.ViewerKind_Tree,
        ViewerKind.Html => AppStrings.ViewerKind_Html,
        _ => AppStrings.ViewerKind_Markdown,
    };

    internal Grid? ViewerMaskGrid { get; private set; }

    private Control ViewerMaskTable()
    {
        var masks = Current.Viewers.Masks;
        var grid = new Grid { RowSpacing = 6, ColumnSpacing = 8 };
        ViewerMaskGrid = grid;
        var maskHeader = Text(AppStrings.Opt_ViewerMask_Mask, 12, brush: "Brush.SecondaryText");
        var viewHeader = Text(AppStrings.Opt_ViewerMask_View, 12, brush: "Brush.SecondaryText");
        var alternateHeader = Text(AppStrings.Opt_ViewerMask_Alternate, 12, brush: "Brush.SecondaryText");
        var largestHeader = Text(AppStrings.Opt_ViewerMask_Largest, 12, brush: "Brush.SecondaryText");
        grid.Children.Add(maskHeader);
        grid.Children.Add(viewHeader);
        grid.Children.Add(alternateHeader);
        grid.Children.Add(largestHeader);
        var rows = new List<(Control Mask, Control View, Control Alternate, Control Largest, Control Tools, Control? Note)>();

        void Change(int index, Func<ViewerMask, ViewerMask> change)
            => UpdateViewers(v => v with { Masks = [.. v.Masks.Select((m, i) => i == index ? change(m) : m)] });

        void Structure(Func<List<ViewerMask>, List<ViewerMask>> change)
        {
            UpdateViewers(v => v with { Masks = change([.. v.Masks]) });
            RebuildLater();
        }

        ComboBox KindBox(ViewerKind current, Action<ViewerKind> chosen)
        {
            var box = new ComboBox { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var kind in Enum.GetValues<ViewerKind>())
            {
                box.Items.Add(new ComboBoxItem { Content = KindLabel(kind), Tag = kind });
            }

            box.SelectedIndex = (int)current;
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is ComboBoxItem { Tag: ViewerKind kind })
                {
                    chosen(kind);
                }
            };
            return box;
        }

        for (var i = 0; i < masks.Count; i++)
        {
            var index = i;
            var mask = new TextBox { Text = masks[i].Mask, MinWidth = 120, VerticalContentAlignment = VerticalAlignment.Center }.Res(TextBox.FontFamilyProperty, "Font.Mono");
            Avalonia.Automation.AutomationProperties.SetName(mask, AppStrings.Opt_ViewerMask_Mask);
            void Commit()
            {
                if (!string.IsNullOrWhiteSpace(mask.Text) && mask.Text != Current.Viewers.Masks.ElementAtOrDefault(index)?.Mask)
                {
                    Change(index, m => m with { Mask = mask.Text.Trim() });
                    RebuildLater();
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
            var view = KindBox(masks[i].View, k => Change(index, m => m with { View = k }));
            var alternate = KindBox(masks[i].Alternate, k => Change(index, m => m with { Alternate = k }));
            var largest = LargestBox(masks[i], m => Change(index, _ => m));

            var up = Tool("Icon.ArrowUp", AppStrings.Opt_ViewerMask_Up, index > 0, () => Structure(l =>
            {
                (l[index - 1], l[index]) = (l[index], l[index - 1]);
                return l;
            }));
            var down = Tool("Icon.ArrowDown", AppStrings.Opt_ViewerMask_Down, index < masks.Count - 1, () => Structure(l =>
            {
                (l[index + 1], l[index]) = (l[index], l[index + 1]);
                return l;
            }));
            var remove = Tool("Icon.Cross", AppStrings.Common_Remove, masks.Count > 1, () => Structure(l =>
            {
                l.RemoveAt(index);
                return l;
            }));
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { up, down, remove } };
            TextBlock? note = null;

            if (Current.Viewers.TakenAbove(index) is { Count: > 0 } taken)
            {
                note = Text(string.Join("  ", taken.GroupBy(t => t.Row).Select(g => Loc.Format(AppStrings.Opt_ViewerMask_Taken, string.Join(";", g.Select(t => t.Mask)), g.Key + 1))), 12, brush: "Brush.Warning", wrap: true);
                note.Margin = new Thickness(2, -2, 0, 4);
                Avalonia.Automation.AutomationProperties.SetName(note, AppStrings.Opt_ViewerMask_TakenName);
            }

            rows.Add((mask, view, alternate, largest, tools, note));
            foreach (var c in new Control?[] { mask, view, alternate, largest, tools, note })
            {
                if (c is not null)
                {
                    grid.Children.Add(c);
                }
            }
        }

        bool? narrowNow = null;
        void Place(Control c, int row, int column, int span = 1)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, column);
            Grid.SetColumnSpan(c, span);
        }

        double Widest(Func<(Control Mask, Control View, Control Alternate, Control Largest, Control Tools, Control? Note), Control> pick)
            => rows.Count == 0 ? 0 : rows.Max(r => pick(r).DesiredSize.Width);

        void Lay(double width)
        {
            var (viewWidth, alternateWidth) = (Math.Max(120, Widest(r => r.View)), Math.Max(120, Widest(r => r.Alternate)));
            var narrow = width < 120 + viewWidth + alternateWidth + Widest(r => r.Largest) + Widest(r => r.Tools) + (4 * 8);
            if (narrowNow == narrow)
            {
                if (!narrow && grid.ColumnDefinitions.Count == 5)
                {
                    grid.ColumnDefinitions[1].MinWidth = viewWidth;
                    grid.ColumnDefinitions[2].MinWidth = alternateWidth;
                }

                return;
            }

            narrowNow = narrow;
            grid.ColumnDefinitions = narrow
                ? new ColumnDefinitions("*,*,Auto")
                : [new ColumnDefinition(5, GridUnitType.Star) { MinWidth = 120 }, new ColumnDefinition(4, GridUnitType.Star) { MinWidth = viewWidth }, new ColumnDefinition(2, GridUnitType.Star) { MinWidth = alternateWidth }, new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)];
            grid.RowDefinitions.Clear();
            var r = 0;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (narrow)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                r++;
                Place(maskHeader, 0, 0, 3);
                Place(viewHeader, r, 0);
                Place(alternateHeader, r, 1);
                Place(largestHeader, r, 2);
            }
            else
            {
                Place(maskHeader, 0, 0);
                Place(viewHeader, 0, 1);
                Place(alternateHeader, 0, 2);
                Place(largestHeader, 0, 3);
            }

            foreach (var (mask, view, alternate, largest, tools, note) in rows)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                r++;
                if (narrow)
                {
                    Place(mask, r, 0, 2);
                    Place(tools, r, 2);
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    r++;
                    Place(view, r, 0);
                    Place(alternate, r, 1);
                    Place(largest, r, 2);
                    largest.Margin = new Thickness(0, 0, 0, 10);
                }
                else
                {
                    Place(mask, r, 0);
                    Place(view, r, 1);
                    Place(alternate, r, 2);
                    Place(largest, r, 3);
                    Place(tools, r, 4);
                    largest.Margin = default;
                }

                if (note is not null)
                {
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    Place(note, ++r, 0, 3);
                }
            }
        }

        Lay(double.PositiveInfinity);
        grid.SizeChanged += (_, e) => Dispatcher.UIThread.Post(() => Lay(e.NewSize.Width));

        var add = new Button { Content = AppStrings.Opt_ViewerMask_Add, Padding = new Thickness(12, 4) };
        add.Click += (_, _) => Structure(l =>
        {
            l.Insert(Math.Max(0, l.Count - 1), new ViewerMask { Mask = "*.txt" });
            return l;
        });

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(grid);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add } });
        panel.Children.Add(Secondary(AppStrings.Opt_ViewerMasks_Help));
        panel.Children.Add(Secondary(AppStrings.Opt_ViewerMask_LargestHelp));
        return panel;

        Control LargestBox(ViewerMask row, Action<ViewerMask> changed)
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var standard = ViewerSettings.DefaultLargestMB(row.View, row.Mask);
            var box = new TextBox
            {
                Text = row.LargestMB?.ToString(culture) ?? "",
                PlaceholderText = standard > 0 ? standard.ToString(culture) : AppStrings.Opt_NoLimit,
                Width = 80,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            Avalonia.Automation.AutomationProperties.SetName(box, AppStrings.Opt_ViewerMask_Largest);
            void Commit()
            {
                long? value = string.IsNullOrWhiteSpace(box.Text) ? null
                    : long.TryParse(box.Text, System.Globalization.NumberStyles.Integer, culture, out var mb) && mb >= 0 ? mb : row.LargestMB;
                box.Text = value?.ToString(culture) ?? "";
                if (value != row.LargestMB)
                {
                    changed(row with { LargestMB = value });
                    row = row with { LargestMB = value };
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

        static Button Tool(string icon, string tip, bool enabled, Action click)
        {
            var b = new Button { Content = Salandual.App.Theme.AppIcons.Glyph(icon, 14, 14, 1.3, "Brush.Text"), Padding = new Thickness(6), Background = Avalonia.Media.Brushes.Transparent, Opacity = enabled ? 1 : 0, IsHitTestVisible = enabled, Focusable = enabled };
            ToolTip.SetTip(b, tip);
            Avalonia.Automation.AutomationProperties.SetName(b, tip);
            b.Click += (_, _) => click();
            return b;
        }
    }
}
