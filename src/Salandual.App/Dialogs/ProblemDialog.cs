// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ProblemDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core;
using Salandual.Core.Resources;

namespace Salandual.App.Dialogs;

internal enum ProblemChoice
{
    GoOn,
    Restart,
    ShowReport,
}

internal sealed class ProblemDialog : Window
{
    public ProblemDialog(Exception problem)
    {
        Title = ProductInfo.DisplayName;
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var where = Diagnostics.ProblemHandler.Where(problem);
        var what = new StackPanel { Children = { OptionsUi.Text(problem.Message, 12, wrap: true).Also(t => t.FontFamily = Salandual.App.Options.OptionsUi.MonoFamily) } };
        if (where.Length > 0)
        {
            what.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.Problem_Where, where), 12, brush: "Brush.SecondaryText", wrap: true).Also(t => t.FontFamily = Salandual.App.Options.OptionsUi.MonoFamily));
        }

        What = what;
        var body = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(18, 14, 18, 16),
            Children =
            {
                OptionsUi.Text(Core.Resources.Strings.Report_WentOn, 16, FontWeight.SemiBold),
                OptionsUi.Secondary(AppStrings.Problem_Text, 13),
                new Border { BorderThickness = new Thickness(1), CornerRadius = Corners.Chip, Padding = new Thickness(10, 8), Child = what }
                    .Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.WindowBackground"),
                OptionsUi.Secondary(AppStrings.Problem_Kept, 13).Also(t => t.Margin = new Thickness(0, 2, 0, 0)),
            },
        };

        ShowReport = Button(AppStrings.Problem_ShowReport, ProblemChoice.ShowReport);
        Restart = Button(AppStrings.Problem_Restart, ProblemChoice.Restart);
        GoOn = Button(AppStrings.Problem_GoOn, ProblemChoice.GoOn);
        GoOn.Classes.Add("accent");
        GoOn.IsDefault = true;
        var footer = new DockPanel();
        DockPanel.SetDock(ShowReport, Dock.Left);
        footer.Children.Add(ShowReport);
        footer.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { Restart, GoOn } });
        var foot = new Border { Padding = new Thickness(18, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footer }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) => GoOn.Focus();
    }

    internal Control What { get; }

    internal Button ShowReport { get; }

    internal Button Restart { get; }

    internal Button GoOn { get; }

    private Button Button(string text, ProblemChoice choice)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 5) };
        b.Click += (_, _) => Close(choice);
        return b;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close(ProblemChoice.GoOn);
            e.Handled = true;
        }
    }
}
