// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/OccupiedSpaceDialog.cs

using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Find;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

internal sealed record SpaceVolume(string Name, long Total, long Free, long Cluster);

internal sealed record OccupiedSpaceSources(Func<SpaceVolume?> Volume, Func<long> PresetCluster, Func<string, long>? Allocated, Func<string, bool> Copy);

public sealed class OccupiedSpaceDialog : Window
{
    private const int Biggest = 5;

    private readonly string _what;
    private readonly IReadOnlyList<FileListItem> _items;
    private readonly DisplayFormat _format;
    private readonly OccupiedSpaceSources _sources;
    private readonly CancellationTokenSource _cancel = new();
    private readonly TextBlock _big = OptionsUi.Text("", 22, FontWeight.SemiBold);
    private readonly TextBlock _bytes = OptionsUi.Text("", 12, brush: "Brush.TertiaryText");
    private readonly TextBlock _onDisk = Value();
    private readonly TextBlock _files = Value();
    private readonly TextBlock _folders = Value();
    private readonly TextBlock _compression = Value();
    private readonly TextBlock _online = Value();
    private readonly TextBlock _compressionLabel;
    private readonly TextBlock _onlineLabel;
    private readonly TextBlock _counting;
    private readonly SpaceChart _chart = new() { IsVisible = false };
    private readonly StackPanel _insideRows = new() { Spacing = 2 };
    private readonly StackPanel _outsideRows = new() { Spacing = 2 };
    private readonly TextBlock _outsideHeading = OptionsUi.Text("", 13, FontWeight.SemiBold);
    private readonly StackPanel _outside;
    private readonly Grid _lists;
    private readonly ComboBox _cluster;
    private readonly TextBlock _estimate = OptionsUi.Text("", 12.5);
    private readonly Button _copy;
    private SpaceVolume? _volume;

    internal OccupiedSpaceDialog(string what, IReadOnlyList<FileListItem> items, DisplayFormat format, OccupiedSpaceSources sources)
    {
        _what = what;
        _items = items;
        _format = format;
        _sources = sources;
        Title = AppStrings.Space_Title;
        Width = 660;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");
        Styles.Add(FindStyles.Create());

        _compressionLabel = Label(AppStrings.Space_Compression);
        _onlineLabel = Label(AppStrings.Space_Online);
        var numbers = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 8, 0, 0) };
        Row(numbers, 0, Label(AppStrings.Space_OnDisk), _onDisk);
        Row(numbers, 1, Label(AppStrings.Space_Files), _files);
        Row(numbers, 2, Label(AppStrings.Space_Folders), _folders);
        Row(numbers, 3, _compressionLabel, _compression);
        Row(numbers, 4, _onlineLabel, _online);
        _compressionLabel.IsVisible = _compression.IsVisible = _onlineLabel.IsVisible = _online.IsVisible = false;
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _big, _bytes, numbers } };
        _counting = OptionsUi.Text(AppStrings.Space_Counting, 12.5, brush: "Brush.SecondaryText");
        _counting.HorizontalAlignment = HorizontalAlignment.Center;
        _counting.VerticalAlignment = VerticalAlignment.Center;
        var chartBox = new Panel { Width = 230, Height = 236, Children = { _counting, _chart } };
        Avalonia.Automation.AutomationProperties.SetName(_chart, AppStrings.Space_ChartName);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,230") };
        top.Children.Add(left);
        Grid.SetColumn(chartBox, 2);
        top.Children.Add(chartBox);

        var inside = Section(OptionsUi.Text(AppStrings.Space_Inside, 13, FontWeight.SemiBold), _insideRows);
        _outside = Section(_outsideHeading, _outsideRows, OptionsUi.Text(Loc.Format(AppStrings.Space_AtLeast, SpaceChart.OuterMinDegrees), 12, brush: "Brush.TertiaryText", wrap: true));
        _lists = new Grid { ColumnDefinitions = new ColumnDefinitions("*,18,*"), IsVisible = false };
        _lists.Children.Add(inside);
        Grid.SetColumn(_outside, 2);
        _lists.Children.Add(_outside);

        _cluster = new ComboBox { MinWidth = 90, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in OccupiedSpace.Clusters)
        {
            _cluster.Items.Add(DisplayFormat.Default.Size(c));
        }

        Avalonia.Automation.AutomationProperties.SetName(_cluster, AppStrings.Space_Clusters);
        _cluster.SelectedIndex = OccupiedSpace.IndexOf(4096);
        _cluster.SelectionChanged += (_, _) => ShowEstimate();
        _estimate.VerticalAlignment = VerticalAlignment.Center;
        var clusters = Section(OptionsUi.Text(AppStrings.Space_Clusters, 13, FontWeight.SemiBold), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { _cluster, _estimate } });

        var body = new StackPanel { Spacing = 14, Margin = new Thickness(20, 16, 20, 18) };
        body.Children.Add(OptionsUi.Text(what, 12.5, brush: "Brush.SecondaryText", wrap: true));
        body.Children.Add(top);
        body.Children.Add(_lists);
        body.Children.Add(clusters);

        _copy = new Button { Content = AppStrings.Common_Copy, MinWidth = 80, Padding = new Thickness(14, 5), IsEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(_copy, AppStrings.Space_CopyTip);
        _copy.Click += (_, _) => Copy();
        var close = new Button { Content = WithKey(AppStrings.Common_Close, "Enter"), MinWidth = 96, Padding = new Thickness(14, 5) };
        close.Classes.Add("accent");
        close.Click += (_, _) => Close();
        var buttons = new DockPanel { Children = { _copy, close } };
        DockPanel.SetDock(_copy, Dock.Left);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = buttons,
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = Controls.TitleBarParts.ForDialog(this, root);
        ShowTotals(SpaceTotals.None);
        Opened += (_, _) =>
        {
            close.Focus();
            _ = CountAsync();
        };
        Closed += (_, _) => _cancel.Cancel();
    }

    internal SpaceResult? Result { get; private set; }

    internal SpaceVolume? Volume => _volume;

    internal ComboBox ClusterBox => _cluster;

    internal Button CopyButton => _copy;

    internal SpaceChart Chart => _chart;

    internal bool IsCounting => Result is null;

    internal IReadOnlyList<(string Label, string Value)> Numbers =>
    [
        (AppStrings.Space_Size, _big.Text ?? ""),
        (AppStrings.Space_OnDisk, _onDisk.Text ?? ""), (AppStrings.Space_Files, _files.Text ?? ""), (AppStrings.Space_Folders, _folders.Text ?? ""),
        .. _compression.IsVisible ? [(AppStrings.Space_Compression, _compression.Text ?? "")] : Array.Empty<(string, string)>(),
        .. _online.IsVisible ? [(AppStrings.Space_Online, _online.Text ?? "")] : Array.Empty<(string, string)>(),
    ];

    internal IReadOnlyList<(string Name, string Size, string Share)> InsideRows => Rows(_insideRows);

    internal IReadOnlyList<(string Name, string Size, string Share)> OutsideRows => _outside.IsVisible ? Rows(_outsideRows) : [];

    internal string Estimate => string.Concat(_estimate.Inlines?.OfType<Run>().Select(r => r.Text) ?? []);

    private static IReadOnlyList<(string, string, string)> Rows(StackPanel rows)
        => [.. rows.Children.OfType<Grid>().Select(g => (((TextBlock)g.Children[1]).Text ?? "", ((TextBlock)g.Children[2]).Text ?? "", ((TextBlock)g.Children[3]).Text ?? ""))];

    private async Task CountAsync()
    {
        try
        {
            var (volume, preset) = await Task.Run(() => (_sources.Volume(), _sources.PresetCluster()));
            _volume = volume;
            _cluster.SelectedIndex = OccupiedSpace.IndexOf(OccupiedSpace.Nearest(preset > 0 ? preset : volume?.Cluster ?? 0));
            var cluster = volume?.Cluster ?? 0;
            var result = await Task.Run(() => OccupiedSpace.Count(_items, cluster, _sources.Allocated, t => Dispatcher.UIThread.Post(() =>
            {
                if (Result is null)
                {
                    ShowTotals(t);
                }
            }), _cancel.Token));
            Result = result;
            ShowTotals(result.Totals);
            ShowChart(result);
            _copy.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ShowTotals(SpaceTotals t)
    {
        _big.Text = _format.Size(t.Bytes);
        _bytes.Text = Loc.Format(AppStrings.Space_Bytes, t.Bytes);
        _onDisk.Text = _format.Size(t.OnDisk);
        _files.Text = t.Files.ToString("N0", CultureInfo.CurrentCulture);
        _folders.Text = t.Folders.ToString("N0", CultureInfo.CurrentCulture);
        _compressionLabel.IsVisible = _compression.IsVisible = t.CompressedFiles > 0;
        _compression.Text = Loc.Format(AppStrings.Space_Saves, _format.Size(t.CompressionSaves), Loc.Plural(t.CompressedFiles, AppStrings.Space_Files_One, AppStrings.Space_Files_Other));
        _onlineLabel.IsVisible = _online.IsVisible = t.OnlineFiles > 0;
        _online.Text = Loc.Format(AppStrings.Space_NotHere, _format.Size(t.OnlineBytes), Loc.Plural(t.OnlineFiles, AppStrings.Space_Files_One, AppStrings.Space_Files_Other));
        ShowEstimate(t);
    }

    private void ShowEstimate() => ShowEstimate(Result?.Totals);

    private void ShowEstimate(SpaceTotals? t)
    {
        _estimate.Inlines ??= [];
        _estimate.Inlines.Clear();
        if (t is null || _cluster.SelectedIndex < 0)
        {
            return;
        }

        Rich.Add(_estimate.Inlines, AppStrings.Space_WouldTake, null, new Run(_format.Size(t.EstimateFor(OccupiedSpace.Clusters[_cluster.SelectedIndex]))) { FontWeight = FontWeight.SemiBold });
    }

    private void ShowChart(SpaceResult result)
    {
        var dark = this.TryFindResource("Brush.PanelBackground", ActualThemeVariant, out var b) && b is ISolidColorBrush bg && Luminance(bg.Color) < 0.5;
        var palette = SpaceChart.Palette(dark);
        var total = Math.Max(1, result.Totals.Bytes);

        var (biggest, rest, restIsFiles, restCount) = result.Slices(Biggest);
        var inner = new List<(string Name, long Bytes, Color Color)>();
        for (var i = 0; i < biggest.Count; i++)
        {
            inner.Add((biggest[i].Name, biggest[i].Bytes, palette.Parts[i]));
        }

        if (rest > 0)
        {
            var restName = restIsFiles && biggest.All(p => p.IsDirectory) ? AppStrings.Space_RestFiles : Loc.Plural(restCount, AppStrings.Space_RestItems_One, AppStrings.Space_RestItems_Other);
            inner.Add((restName, rest, palette.Others));
        }

        _insideRows.Children.Clear();
        foreach (var (name, bytes, color) in inner)
        {
            _insideRows.Children.Add(Legend(color, name, _format.Size(bytes), Percent(bytes * 100.0 / total, "0")));
        }

        var outer = new List<(string Name, long Bytes, Color Color)>();
        if (_volume is { Total: > 0 } v)
        {
            var used = Math.Max(0, v.Total - v.Free);
            var mine = Math.Min(result.Totals.OnDisk, used);
            outer.Add((AppStrings.Space_ThisSelection, mine, palette.Selection));
            outer.Add((Loc.Format(AppStrings.Space_OtherFiles, v.Name), used - mine, palette.Other));
            outer.Add((AppStrings.Space_Free, v.Free, palette.Free));
            _outsideHeading.Text = Loc.Format(AppStrings.Space_Outside, v.Name);
            _outsideRows.Children.Clear();
            foreach (var (name, bytes, color) in outer)
            {
                _outsideRows.Children.Add(Legend(color, name, _format.Size(bytes), Percent(bytes * 100.0 / v.Total, "0.0")));
            }
        }

        _outside.IsVisible = outer.Count > 0;
        _chart.Show(
            [.. inner.Select(s => new SpaceSlice(s.Bytes, s.Color, Loc.Format(AppStrings.Space_TipInside, s.Name, _format.Size(s.Bytes), Percent(s.Bytes * 100.0 / total, "0"))))],
            _volume is { Total: > 0 } d ? [.. outer.Select(s => new SpaceSlice(s.Bytes, s.Color, Loc.Format(AppStrings.Space_TipOutside, s.Name, _format.Size(s.Bytes), Percent(s.Bytes * 100.0 / d.Total, "0.0"), d.Name, _format.Size(d.Total))))] : [],
            _format.Size(result.Totals.Bytes),
            _volume is { Total: > 0 } t ? Loc.Format(AppStrings.Space_OfDisk, _format.Size(t.Total)) : "");
        _counting.IsVisible = false;
        _chart.IsVisible = inner.Count > 0;
        _lists.IsVisible = true;
    }

    internal bool Copy()
    {
        var text = new StringBuilder();
        text.AppendLine(_what);
        text.AppendLine(Loc.Format(AppStrings.Space_CopyLine, AppStrings.Space_Size, Loc.Format(AppStrings.Space_CopySize, _big.Text, _bytes.Text)));
        foreach (var (label, value) in Numbers.Skip(1))
        {
            text.AppendLine(Loc.Format(AppStrings.Space_CopyLine, label, value));
        }

        text.AppendLine(Loc.Format(AppStrings.Space_CopyLine, AppStrings.Space_Clusters, Estimate));
        text.AppendLine();
        text.AppendLine(AppStrings.Space_Inside);
        foreach (var (name, size, share) in InsideRows)
        {
            text.AppendLine(Loc.Format(AppStrings.Space_CopyRow, name, size, share));
        }

        if (OutsideRows.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(_outsideHeading.Text);
            foreach (var (name, size, share) in OutsideRows)
            {
                text.AppendLine(Loc.Format(AppStrings.Space_CopyRow, name, size, share));
            }
        }

        var copied = _sources.Copy(text.ToString());
        _copy.Content = copied ? AppStrings.Space_Copied : AppStrings.Space_CopyBusy;

        DispatcherTimer.RunOnce(() => _copy.Content = AppStrings.Common_Copy, TimeSpan.FromSeconds(2));
        return copied;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key is Key.Escape or Key.Enter && !_cluster.IsDropDownOpen)
        {
            Close();
            e.Handled = true;
        }
    }

    private static string Percent(double value, string format) => Loc.Format(AppStrings.Space_Percent, value.ToString(format, CultureInfo.CurrentCulture));

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

    private static TextBlock Value() => OptionsUi.Text("", 12.5);

    private static TextBlock Label(string text) => OptionsUi.Text(text, 12.5, brush: "Brush.SecondaryText");

    private static void Row(Grid grid, int row, TextBlock label, TextBlock value)
    {
        label.Margin = new Thickness(0, 3, 16, 3);
        value.Margin = new Thickness(0, 3);
        Grid.SetRow(label, row);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(label);
        grid.Children.Add(value);
    }

    private static Grid Legend(Color color, string name, string size, string share)
    {
        var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
        var label = OptionsUi.Text(name, 12.5);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var amount = OptionsUi.Text(size, 12.5, brush: "Brush.SecondaryText");
        amount.HorizontalAlignment = HorizontalAlignment.Right;
        var part = OptionsUi.Text(share, 12.5, brush: "Brush.SecondaryText");
        part.HorizontalAlignment = HorizontalAlignment.Right;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("14,8,*,8,Auto,8,52"), Margin = new Thickness(0, 1) };
        grid.Children.Add(swatch);
        Grid.SetColumn(label, 2);
        grid.Children.Add(label);
        Grid.SetColumn(amount, 4);
        grid.Children.Add(amount);
        Grid.SetColumn(part, 6);
        grid.Children.Add(part);
        return grid;
    }

    private static StackPanel Section(TextBlock heading, params Control[] rows)
    {
        var section = new StackPanel { Spacing = 6, Children = { heading } };
        section.Children.AddRange(rows);
        return section;
    }

    private static StackPanel WithKey(string label, string key) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children = { new TextBlock { Text = label }, new TextBlock { Text = key, FontSize = 11, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center } },
    };
}
