// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/DriveInfoDialog.Health.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Resources;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.App.Dialogs;

public sealed partial class DriveInfoDialog
{
    private const int HealthTab = 2;
    private readonly ScrollViewer _healthScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private DispatcherTimer? _healthTimer;
    private bool _healthReading;

    internal Func<string, DriveHealth?> ReadHealth { get; init; } = DriveHealth.Read;

    internal DriveHealth? Health { get; private set; }

    internal Task HealthReading { get; private set; } = Task.CompletedTask;

    internal ScrollViewer HealthScroll => _healthScroll;

    internal string? HealthTitle { get; private set; }

    protected override void OnClosed(EventArgs e)
    {
        _healthTimer?.Stop();
        base.OnClosed(e);
    }

    private ScrollViewer HealthPart()
    {
        _healthScroll.Margin = new Thickness(0, 12, 0, 0);
        _healthScroll.Content ??= OptionsUi.Text(AppStrings.Drive_Health_Reading, 13, brush: "Brush.SecondaryText");
        return _healthScroll;
    }

    private void HealthShown(bool shown)
    {
        if (!shown)
        {
            _healthTimer?.Stop();
            return;
        }

        _healthScroll.MaxHeight = Math.Max(_tabBody.MinHeight - 12, 240);
        _healthTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background, (_, _) => _ = RefreshHealthAsync());
        _healthTimer.Start();
        HealthReading = RefreshHealthAsync();
    }

    internal async Task RefreshHealthAsync()
    {
        if (_healthReading || _details is not { } d)
        {
            return;
        }

        _healthReading = true;
        try
        {
            var root = d.Root;
            var read = ReadHealth;
            var health = d.Kind == DriveKind.Network || DiskSpace.IsMadeUp(d.Total) ? null : await Task.Run(() => read(root));
            Health = health;
            var offset = _healthScroll.Offset;
            _healthScroll.Content = HealthContent(health);
            _healthScroll.Offset = offset;
        }
        finally
        {
            _healthReading = false;
        }
    }

    private StackPanel HealthContent(DriveHealth? h)
    {
        var part = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 14, 0) };
        var state = h?.State ?? HealthState.NoData;
        part.Children.Add(Summary(h, state));
        if (h is not null && Tiles(h) is { } tiles)
        {
            part.Children.Add(tiles);
        }

        var table = new StackPanel();
        table.Children.Add(Section(AppStrings.Drive_Health_Connection, first: true));
        var link = h?.Link;
        table.Children.Add(HealthRow(AppStrings.Drive_Health_Connection, LinkText(link), link is { Slower: true } ? AppStrings.Drive_Health_Slower : null));
        if (link is { MaxGeneration: > 0 })
        {
            table.Children.Add(HealthRow(AppStrings.Drive_Health_CanDo, LinkText(link with { Generation = link.MaxGeneration, Lanes = link.MaxLanes })));
        }

        if (h is not null)
        {
            SmartRows(table, h);
        }

        part.Children.Add(table);
        if (h is { Attributes.Count: > 0 })
        {
            part.Children.Add(AttributeList(h.Attributes));
        }

        part.Children.Add(OptionsUi.Text(AppStrings.Drive_Health_Note, 11, brush: "Brush.TertiaryText", wrap: true));
        return part;
    }

    private Panel Summary(DriveHealth? h, HealthState state)
    {
        var (key, glyph, title, text) = state switch
        {
            HealthState.Good => ("Brush.Success", "Icon.Check", AppStrings.Drive_Health_Good,
                h!.Nvme is { } n ? Loc.Format(AppStrings.Drive_Health_GoodNvme, n.Used) : AppStrings.Drive_Health_GoodAta),
            HealthState.Watch => ("Brush.Warning", WarningGlyph, AppStrings.Drive_Health_Watch, WatchText(h!)),
            HealthState.Failing => ("Brush.Danger", WarningGlyph, AppStrings.Drive_Health_Failing, AppStrings.Drive_Health_FailingText),
            HealthState.AdminOnly => ("Brush.SecondaryText", ShieldGlyph, AppStrings.Drive_Health_AdminOnly, AppStrings.Drive_Health_AdminOnlyText),
            _ => ("Brush.SecondaryText", "Icon.Info", AppStrings.Drive_Health_NoData,
                h?.Link?.Kind == LinkKind.Usb ? AppStrings.Drive_Health_NoDataUsb : AppStrings.Drive_Health_NoDataOther),
        };
        HealthTitle = title;
        var icon = AppIcons.Glyph(glyph, 16, 16, brush: key);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 0, 0);
        var words = new StackPanel { Children = { OptionsUi.Text(title, 13, FontWeight.SemiBold), OptionsUi.Text(text, 12, brush: "Brush.SecondaryText", wrap: true) } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Margin = new Thickness(12, 8) };
        row.Children.Add(icon);
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        var tint = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.14 }.Res(Border.BackgroundProperty, key);
        return new Panel { Children = { tint, row } };
    }

    private static string WatchText(DriveHealth h) => h.Nvme switch
    {
        { MediaErrors: > 0 } n => Loc.Format(AppStrings.Drive_Health_WatchErrors, n.MediaErrors),
        { } n => Loc.Format(AppStrings.Drive_Health_WatchLife, n.Used),
        _ when h.Count(0x05) > 0 || h.Count(0xC5) > 0 => Loc.Format(AppStrings.Drive_Health_WatchSectors, h.Count(0x05) ?? 0, h.Count(0xC5) ?? 0),
        _ => Loc.Format(AppStrings.Drive_Health_WatchErrors, (h.Count(0xBB) ?? 0) + (h.Count(0xC6) ?? 0)),
    };

    private Grid? Tiles(DriveHealth h)
    {
        var temperature = h.Temperature?.Now ?? (int?)h.Nvme?.Temperature ?? (int?)h.Count(0xC2) ?? (int?)h.Count(0xBE);
        var critical = h.Temperature is { Critical: > 0 } t ? t.Critical : 80;
        var degrees = temperature is { } c ? Loc.Format(AppStrings.Drive_Health_Degrees, c) : "—";
        (string Label, string Value, double? Meter, bool Watch)[] tiles;
        if (h.Nvme is { } n)
        {
            tiles =
            [
                (AppStrings.Drive_Health_LifeUsed, Loc.Format(AppStrings.Drive_Activity_Percent, n.Used), Math.Min(n.Used, 100), n.Used >= 90),
                (AppStrings.Drive_Health_Temperature, degrees, temperature is { } c1 ? Math.Clamp(100.0 * c1 / critical, 0, 100) : null, false),
                (AppStrings.Drive_Health_PoweredOn, Loc.Format(AppStrings.Drive_Health_HoursShort, n.Hours), null, false),
                (AppStrings.Drive_Health_Written, _format.Size(n.BytesWritten), null, false),
            ];
        }
        else if (h.Attributes.Count > 0)
        {
            static string Count(long? v) => v is { } x ? Loc.Format("{0:N0}", x) : "—";
            tiles =
            [
                (AppStrings.Drive_Health_TileMoved, Count(h.Count(0x05)), null, h.Count(0x05) > 0),
                (AppStrings.Drive_Health_Temperature, degrees, temperature is { } c1 ? Math.Clamp(100.0 * c1 / critical, 0, 100) : null, false),
                (AppStrings.Drive_Health_PoweredOn, h.Count(0x09) is { } hours ? Loc.Format(AppStrings.Drive_Health_HoursShort, hours) : "—", null, false),
                (AppStrings.Drive_Health_TileWaiting, Count(h.Count(0xC5)), null, h.Count(0xC5) > 0),
            ];
        }
        else
        {
            return null;
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 8 };
        for (var i = 0; i < tiles.Length; i++)
        {
            var (label, value, meter, watch) = tiles[i];
            var tile = new StackPanel { Children = { OptionsUi.Text(label, 11, brush: "Brush.SecondaryText"), OptionsUi.Text(value, 17, FontWeight.SemiBold, watch ? "Brush.Warning" : "Brush.Text") } };
            if (meter is { } m)
            {
                var track = new Grid { Height = 4, Margin = new Thickness(0, 3, 0, 0), ColumnDefinitions = new ColumnDefinitions($"{m.ToString(System.Globalization.CultureInfo.InvariantCulture)}*,{(100 - m).ToString(System.Globalization.CultureInfo.InvariantCulture)}*") };
                track.Children.Add(new Border { CornerRadius = new CornerRadius(2) }.Res(Border.BackgroundProperty, "Brush.Pressed"));
                Grid.SetColumnSpan(track.Children[0], 2);
                track.Children.Add(new Border { CornerRadius = new CornerRadius(2) }.Res(Border.BackgroundProperty, "Brush.Accent"));
                tile.Children.Add(track);
            }

            Grid.SetColumn(tile, i);
            grid.Children.Add(tile);
        }

        return grid;
    }

    private void SmartRows(StackPanel table, DriveHealth h)
    {
        if (h is { Nvme: null, Attributes.Count: 0, PredictsFailure: null, Temperature: null })
        {
            return;
        }

        table.Children.Add(Section(AppStrings.Drive_Health_Smart));
        var limits = h.Temperature;
        string Degrees(int c) => limits is { Warning: > 0, Critical: > 0 } l
            ? Loc.Format(AppStrings.Drive_Health_DegreesLimits, c, l.Warning, l.Critical)
            : Loc.Format(AppStrings.Drive_Health_Degrees, c);

        if (h.Nvme is { } n)
        {
            table.Children.Add(HealthRow(AppStrings.Drive_Health_Warnings, NvmeWarnings(n.CriticalWarning)));
            table.Children.Add(HealthRow(AppStrings.Drive_Health_LifeUsed, Loc.Format(AppStrings.Drive_Health_LifeUsedValue, n.Used), n.Used >= 90 ? AppStrings.Drive_Health_WatchBadge : null));
            table.Children.Add(HealthRow(AppStrings.Drive_Health_Spare, Loc.Format(AppStrings.Drive_Health_SpareValue, n.Spare, n.SpareThreshold)));
            table.Children.Add(HealthRow(AppStrings.Drive_Health_Temperature, Degrees(limits?.Now ?? n.Temperature)));
            table.Children.Add(HealthRow(AppStrings.Drive_Health_PoweredOn, Hours(n.Hours)));
            table.Children.Add(HealthRow(AppStrings.Drive_Health_PowerCycles, Loc.Format("{0:N0}", n.PowerCycles)));
            table.Children.Add(HealthRow(AppStrings.Drive_Health_UnsafeShutdowns, Loc.Format("{0:N0}", n.UnsafeShutdowns)));
            table.Children.Add(HealthRow(AppStrings.Drive_Health_MediaErrors, Loc.Format("{0:N0}", n.MediaErrors), n.MediaErrors > 0 ? AppStrings.Drive_Health_WatchBadge : null));
            table.Children.Add(HealthRow(AppStrings.Drive_Health_ErrorLog, Loc.Format("{0:N0}", n.ErrorLogEntries)));
            table.Children.Add(HealthRow(AppStrings.Drive_Health_Read, _format.Size(n.BytesRead)));
            table.Children.Add(HealthRow(AppStrings.Drive_Health_Written, _format.Size(n.BytesWritten)));
            return;
        }

        if (h.Attributes.Count > 0 || h.PredictsFailure is not null)
        {
            table.Children.Add(HealthRow(AppStrings.Drive_Health_Warnings, h.PredictsFailure == true ? AppStrings.Drive_Health_PredictsFailure : AppStrings.Drive_Health_None));
            foreach (var id in DriveHealth.WatchedAttributes)
            {
                if (h.Count(id) is { } count)
                {
                    table.Children.Add(HealthRow(AttributeName(id), Loc.Format("{0:N0}", count), count > 0 ? AppStrings.Drive_Health_WatchBadge : null));
                }
            }

            if ((h.Temperature?.Now ?? h.Count(0xC2) ?? h.Count(0xBE)) is { } c)
            {
                table.Children.Add(HealthRow(AppStrings.Drive_Health_Temperature, Degrees((int)c)));
            }

            if (h.Count(0x09) is { } hours)
            {
                table.Children.Add(HealthRow(AppStrings.Drive_Health_PoweredOn, Hours(hours)));
            }

            if (h.Count(0x0C) is { } cycles)
            {
                table.Children.Add(HealthRow(AppStrings.Drive_Health_PowerCycles, Loc.Format("{0:N0}", cycles)));
            }

            return;
        }

        if (limits is not null)
        {
            table.Children.Add(HealthRow(AppStrings.Drive_Health_Temperature, Degrees(limits.Now)));
        }
    }

    private static StackPanel AttributeList(IReadOnlyList<SmartAttribute> attributes)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*,52,52,Auto") };
        void Cell(int row, int column, string text, string brush = "Brush.Text", bool right = false, bool bold = false)
        {
            var t = OptionsUi.Text(text, 12, bold ? FontWeight.SemiBold : FontWeight.Normal, brush);
            t.Margin = new Thickness(0, 3, 6, 3);
            if (right)
            {
                t.HorizontalAlignment = HorizontalAlignment.Right;
            }

            Grid.SetRow(t, row);
            Grid.SetColumn(t, column);
            grid.Children.Add(t);
        }

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] heads = [AppStrings.Drive_Health_Id, AppStrings.Drive_Health_Attribute, AppStrings.Drive_Health_Value, AppStrings.Drive_Health_Worst, AppStrings.Drive_Health_Raw];
        for (var i = 0; i < heads.Length; i++)
        {
            Cell(0, i, heads[i], "Brush.SecondaryText", right: i == 4, bold: true);
        }

        for (var r = 0; r < attributes.Count; r++)
        {
            var a = attributes[r];
            var brush = DriveHealth.WatchedAttributes.Contains(a.Id) && a.Count > 0 ? "Brush.Warning" : "Brush.Text";
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Cell(r + 1, 0, a.Id.ToString("X2", System.Globalization.CultureInfo.InvariantCulture), brush);
            Cell(r + 1, 1, AttributeName(a.Id), brush);
            Cell(r + 1, 2, a.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), brush);
            Cell(r + 1, 3, a.Worst.ToString(System.Globalization.CultureInfo.InvariantCulture), brush);
            Cell(r + 1, 4, Loc.Format("{0:N0}", a.Count), brush, right: true);
        }

        return new StackPanel { Children = { Section(Loc.Format(AppStrings.Drive_Health_AllAttributes, attributes.Count)), grid } };
    }

    internal static string AttributeName(int id) => id switch
    {
        0x01 => AppStrings.Drive_Smart_01,
        0x03 => AppStrings.Drive_Smart_03,
        0x04 => AppStrings.Drive_Smart_04,
        0x05 => AppStrings.Drive_Smart_05,
        0x07 => AppStrings.Drive_Smart_07,
        0x09 => AppStrings.Drive_Smart_09,
        0x0A => AppStrings.Drive_Smart_0A,
        0x0C => AppStrings.Drive_Smart_0C,
        0xAD => AppStrings.Drive_Smart_AD,
        0xB1 => AppStrings.Drive_Smart_B1,
        0xBB => AppStrings.Drive_Smart_BB,
        0xBE => AppStrings.Drive_Smart_BE,
        0xC0 => AppStrings.Drive_Smart_C0,
        0xC1 => AppStrings.Drive_Smart_C1,
        0xC2 => AppStrings.Drive_Smart_C2,
        0xC4 => AppStrings.Drive_Smart_C4,
        0xC5 => AppStrings.Drive_Smart_C5,
        0xC6 => AppStrings.Drive_Smart_C6,
        0xC7 => AppStrings.Drive_Smart_C7,
        0xE7 => AppStrings.Drive_Smart_E7,
        0xE9 => AppStrings.Drive_Smart_E9,
        0xF1 => AppStrings.Drive_Smart_F1,
        0xF2 => AppStrings.Drive_Smart_F2,
        _ => AppStrings.Drive_Smart_Other,
    };

    private static string NvmeWarnings(int bits)
    {
        if (bits == 0)
        {
            return AppStrings.Drive_Health_None;
        }

        string[] names = [AppStrings.Drive_Health_WarnSpare, AppStrings.Drive_Health_WarnTemperature, AppStrings.Drive_Health_WarnReliability, AppStrings.Drive_Health_WarnReadOnly, AppStrings.Drive_Health_WarnBackup];
        return string.Join(", ", names.Where((_, i) => (bits & (1 << i)) != 0));
    }

    internal static string LinkText(DriveLink? link) => link switch
    {
        { Kind: LinkKind.Usb } => AppStrings.Drive_Health_Usb,
        { Kind: LinkKind.Pcie, Generation: > 0, Lanes: > 0 } l => Loc.Format(AppStrings.Drive_Health_Pcie, l.Generation, l.Lanes),
        { Kind: LinkKind.Pcie, Generation: > 0 } l => Loc.Format(AppStrings.Drive_Health_PcieGen, l.Generation),
        { Kind: LinkKind.Sata, Generation: >= 1 and <= 3 } l => Loc.Format(AppStrings.Drive_Health_Sata, new[] { "I", "II", "III" }[l.Generation - 1], new[] { "1.5", "3", "6" }[l.Generation - 1]),
        _ => AppStrings.Drive_Health_Unknown,
    };

    internal static string Hours(long hours)
    {
        var days = hours / 24;
        return days < 2 ? Loc.Format(AppStrings.Drive_Health_Hours, hours)
            : days < 365 ? Loc.Format(AppStrings.Drive_Health_HoursDays, hours, days)
            : Loc.Format(AppStrings.Drive_Health_HoursYears, hours, days / 365.25);
    }

    private static TextBlock Section(string title, bool first = false)
    {
        var t = OptionsUi.Text(title, 12, FontWeight.SemiBold, "Brush.SecondaryText");
        t.Margin = new Thickness(0, first ? 0 : 12, 0, 2);
        return t;
    }

    private static StackPanel HealthRow(string key, string value, string? badge = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { OptionsUi.Text(value, 13) } };
        if (badge is not null)
        {
            var tint = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.16 }.Res(Border.BackgroundProperty, "Brush.Warning");
            var text = OptionsUi.Text(badge, 11, brush: "Brush.Warning");
            text.Margin = new Thickness(6, 1);
            content.Children.Add(new Panel { VerticalAlignment = VerticalAlignment.Center, Children = { tint, text } });
        }

        return new StackPanel { Children = { new Border { Height = 1 }.Res(Border.BackgroundProperty, "Brush.Border"), TableRow(key, content) } };
    }
}
