// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/UserMenuAskDialog.cs

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.UserMenu;

namespace Salandual.App.Dialogs;

internal sealed class UserMenuAskDialog : Window
{
    private const int ShownFiles = 12;
    private readonly UserMenuItem _item;
    private readonly UserMenuContext _context;
    private readonly IProgramStarter _starter;
    private readonly StackPanel _lines = new() { Spacing = 2 };
    private readonly TextBlock _problem = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
    private readonly CheckBox _dontAsk = new() { Content = AppStrings.UserMenu_Ask_DontAsk, FontSize = 13 };

    public UserMenuAskDialog(UserMenuItem item, UserMenuPlan plan, UserMenuContext context, IProgramStarter starter)
    {
        _item = item;
        _context = context;
        _starter = starter;
        Title = AppStrings.UserMenu_Title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var script = item.Kind == UserMenuItemKind.Script;
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20, 8, 20, 16) };
        body.Children.Add(OptionsUi.Text(item.PlainName, 15, FontWeight.SemiBold, wrap: true));
        body.Children.Add(OptionsUi.Text(script ? AppStrings.UserMenu_Ask_Script : Loc.Format(AppStrings.UserMenu_Ask_Text, plan.ProgramName), 13, brush: "Brush.SecondaryText", wrap: true));
        body.Children.Add(new Border { Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = _lines }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border"));
        if (plan.Steps.Count > 1)
        {
            body.Children.Add(OptionsUi.Text(Loc.Format(AppStrings.UserMenu_Ask_Each, plan.Steps.Count), 12, brush: "Brush.SecondaryText", wrap: true));
        }

        if (!script)
        {
            Arguments = new TextBox { Text = item.Arguments, VerticalContentAlignment = VerticalAlignment.Center }.Res(TextBox.FontFamilyProperty, "Font.Mono");
            Avalonia.Automation.AutomationProperties.SetName(Arguments, AppStrings.UserMenu_Arguments);
            Arguments.TextChanged += (_, _) => Show(UserMenuRunner.Plan(_item, _context, _starter, out var error, Arguments.Text ?? ""), error);
            var row = new DockPanel();
            var label = OptionsUi.Text(AppStrings.UserMenu_Arguments, 13, brush: "Brush.SecondaryText").Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 10, 0); });
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(Arguments);
            body.Children.Add(row);
            body.Children.Add(OptionsUi.Text(AppStrings.UserMenu_Ask_ForOnce, 12, brush: "Brush.TertiaryText", wrap: true));
        }

        body.Children.Add(_problem);
        Show(plan, null);

        Run = new Button { Content = AppStrings.UserMenu_Ask_Run, Padding = new Thickness(14, 5) };
        Run.Classes.Add("accent");
        Run.Click += (_, _) => Answer();
        var cancel = new Button { Content = AppStrings.Common_Cancel, Padding = new Thickness(14, 5) };
        cancel.Click += (_, _) => Close(null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, Run } };
        var footerRow = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        footerRow.Children.Add(buttons);
        footerRow.Children.Add(_dontAsk.Also(c => c.VerticalAlignment = VerticalAlignment.Center));
        var footer = new Border { Padding = new Thickness(20, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerRow }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        Opened += (_, _) => (Arguments as Control ?? Run).Focus();
    }

    internal TextBox? Arguments { get; }

    internal Button Run { get; }

    internal CheckBox DontAskBox => _dontAsk;

    internal bool DontAskAgain => _dontAsk.IsChecked == true;

    internal IEnumerable<string> ShownLines => _lines.Children.OfType<DockPanel>().Select(d => d.Children.OfType<TextBlock>().Last().Text ?? "");

    private void Show(UserMenuPlan? plan, string? error)
    {
        _problem.Text = error ?? "";
        _problem.IsVisible = error is not null;
        if (Run is not null)
        {
            Run.IsEnabled = plan is not null;
        }

        if (plan is null)
        {
            return;
        }

        _lines.Children.Clear();
        var step = plan.Steps[0];
        var shown = UserMenuRunner.Shown(step);
        AddLine("", shown[0], program: true);
        for (var i = 1; i < shown.Count; i++)
        {
            AddLine(i.ToString(CultureInfo.CurrentCulture), shown[i]);
        }

        if (_item.Kind == UserMenuItemKind.Script && step.ListLines is { } files)
        {
            foreach (var file in files.Take(ShownFiles))
            {
                AddLine("$Files", file);
            }

            if (files.Count > ShownFiles)
            {
                AddLine("", Loc.Format(AppStrings.Common_AndMore, files.Count - ShownFiles));
            }
        }

        AddLine("", Loc.Format(AppStrings.UserMenu_Ask_StartIn, step.Start.Folder ?? ""), note: true);
    }

    private void AddLine(string number, string text, bool program = false, bool note = false)
    {
        var no = OptionsUi.Text(number, 12, brush: "Brush.TertiaryText").Also(t => { t.Width = 46; t.TextAlignment = TextAlignment.Right; t.Margin = new Thickness(0, 0, 10, 0); });
        var value = OptionsUi.Text(text, 12, brush: program ? "Brush.Accent" : note ? "Brush.SecondaryText" : "Brush.Text").Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis);
        if (!note)
        {
            value.Res(TextBlock.FontFamilyProperty, "Font.Mono");
        }

        ToolTip.SetTip(value, text);
        var line = new DockPanel();
        DockPanel.SetDock(no, Dock.Left);
        line.Children.Add(no);
        line.Children.Add(value);
        _lines.Children.Add(line);
    }

    internal void Answer()
    {
        if (Run.IsEnabled)
        {
            Close(Arguments?.Text ?? _item.Arguments);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.Source is not Button)
        {
            Answer();
            e.Handled = true;
        }
    }
}
