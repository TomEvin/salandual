// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/UnpackLimitDialog.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Archives;
using Salandual.Core.Listing;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

internal sealed class UnpackLimitDialog : Window
{
    public UnpackLimitDialog(string archiveName, UnpackSuspicion suspicion, string drive, long? free, DisplayFormat format)
    {
        Title = AppStrings.Unpack_Unpack;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var heading = OptionsUi.Text(Loc.Format(AppStrings.UnpackLimit_Heading, archiveName, format.Size(suspicion.Unpacked)), 15, FontWeight.SemiBold, wrap: true);
        var why = suspicion.Ratio > 1 && !suspicion.MoreThanFree
            ? Loc.Format(AppStrings.UnpackLimit_Ratio, format.Size(suspicion.Packed), Times(suspicion.Ratio))
            : Loc.Format(AppStrings.UnpackLimit_Space, drive);
        var text = new StackPanel { Spacing = 4, Children = { heading, OptionsUi.Text(why, 13, brush: "Brush.SecondaryText", wrap: true) } };
        var sign = AppIcons.Create("Icon.Warning", "Brush.Warning") ?? new Control();
        sign.Width = sign.Height = 22;
        sign.VerticalAlignment = VerticalAlignment.Top;
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        head.Children.Add(sign);
        Grid.SetColumn(text, 1);
        head.Children.Add(text);

        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), RowSpacing = 6, ColumnSpacing = 12, Margin = new Thickness(34, 12, 0, 0) };
        void Row(string label, string value)
        {
            var r = rows.RowDefinitions.Count;
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = OptionsUi.Text(label, 13, brush: "Brush.SecondaryText");
            var v = OptionsUi.Text(value, 13, wrap: true);
            Grid.SetRow(l, r);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            rows.Children.Add(l);
            rows.Children.Add(v);
        }

        Row(AppStrings.UnpackLimit_Unpacked, Loc.Format(AppStrings.UnpackLimit_InFiles, format.Size(suspicion.Unpacked), Loc.Plural(suspicion.Files, AppStrings.Archive_Files_One, AppStrings.Archive_Files_Other)));
        if (free is { } room)
        {
            Row(Loc.Format(AppStrings.UnpackLimit_FreeOn, drive), format.Size(room));
        }

        if (suspicion.Largest is { } largest)
        {
            Row(AppStrings.UnpackLimit_Largest, largest.PackedSize > 0
                ? Loc.Format(AppStrings.UnpackLimit_LargestPacked, largest.Name, format.Size(largest.Size), format.Size(largest.PackedSize))
                : Loc.Format(AppStrings.UnpackLimit_LargestSize, largest.Name, format.Size(largest.Size)));
        }

        var body = new StackPanel { Margin = new Thickness(20, 8, 20, 16) };
        body.Children.Add(head);
        body.Children.Add(rows);
        body.Children.Add(OptionsUi.Text(AppStrings.UnpackLimit_Stops, 12, brush: "Brush.TertiaryText", wrap: true).Also(t => t.Margin = new Thickness(34, 12, 0, 0)));

        Continue = new Button { Content = AppStrings.UnpackLimit_Continue, Padding = new Thickness(14, 5) };
        Continue.Click += (_, _) => Close(true);
        var stop = new Button { Content = AppStrings.UnpackLimit_DontUnpack, Padding = new Thickness(14, 5) };
        stop.Classes.Add("accent");
        stop.Click += (_, _) => Close(false);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { Continue, stop } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) => stop.Focus();
    }

    internal Button Continue { get; }

    internal static string Times(double ratio) => ratio switch
    {
        >= 1e9 => Loc.Format(AppStrings.UnpackLimit_TimesBillion, Math.Floor(ratio / 1e9).ToString("0", CultureInfo.CurrentCulture)),
        >= 1e6 => Loc.Format(AppStrings.UnpackLimit_TimesMillion, Math.Floor(ratio / 1e6).ToString("0", CultureInfo.CurrentCulture)),
        _ => Loc.Format(AppStrings.UnpackLimit_Times, Math.Floor(ratio).ToString("N0", CultureInfo.CurrentCulture)),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }
}
