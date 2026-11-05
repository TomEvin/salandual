// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/UnpackReportDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Operations;

namespace Salandual.App.Dialogs;

internal sealed class UnpackReportDialog : Window
{
    public UnpackReportDialog(string title, string heading, string text, string reasonHeader, IReadOnlyList<OperationIssue> issues, string? footnote, Func<string, Task> copy, string? entryHeader = null)
    {
        Title = title;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");
        Issues = issues;

        var table = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        void Cell(string value, int row, int column, bool head)
        {
            var t = head
                ? OptionsUi.Text(value, 12, FontWeight.SemiBold, "Brush.SecondaryText")
                : OptionsUi.Text(value, 12, wrap: true);
            if (!head && column == 0)
            {
                t.Res(TextBlock.FontFamilyProperty, "Font.Mono");
            }

            var cell = new Border { Padding = new Thickness(4, 5), BorderThickness = new Thickness(0, 0, 0, 1), Child = t }.Res(Border.BorderBrushProperty, "Brush.Border");
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            table.Children.Add(cell);
        }

        table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Cell(entryHeader ?? AppStrings.UnpackReport_Entry, 0, 0, head: true);
        Cell(reasonHeader, 0, 1, head: true);
        for (var i = 0; i < issues.Count; i++)
        {
            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Cell(issues[i].Path, i + 1, 0, head: false);
            Cell(issues[i].Message, i + 1, 1, head: false);
        }

        var body = new DockPanel { Margin = new Thickness(20, 8, 20, 16) };
        var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8), Children = { OptionsUi.Text(heading, 15, FontWeight.SemiBold, wrap: true), OptionsUi.Text(text, 13, brush: "Brush.SecondaryText", wrap: true) } };
        DockPanel.SetDock(top, Dock.Top);
        body.Children.Add(top);
        body.Children.Add(new ScrollViewer { Content = table, MaxHeight = 380, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

        var copyList = new Button { Content = AppStrings.UnpackReport_CopyList, Padding = new Thickness(14, 5) };
        copyList.Click += async (_, _) => await copy(string.Join(Environment.NewLine, issues.Select(i => i.Path + "\t" + i.Message)));
        var close = new Button { Content = AppStrings.Common_Close, Padding = new Thickness(14, 5) };
        close.Classes.Add("accent");
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { copyList, close } };
        var row = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        if (footnote is not null)
        {
            row.Children.Add(OptionsUi.Text(footnote, 12, brush: "Brush.SecondaryText").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        }

        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = row }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) => close.Focus();
    }

    internal IReadOnlyList<OperationIssue> Issues { get; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key is Key.Escape or Key.Enter)
        {
            Close();
            e.Handled = true;
        }
    }
}
