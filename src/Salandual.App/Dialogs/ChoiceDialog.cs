// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/ChoiceDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.Core.Settings;

namespace Salandual.App.Dialogs;

internal sealed record Choice(string Id, string Label, string? KeyHint = null, Key? Key = null, bool Primary = false, bool Left = false);

internal sealed class ChoiceDialog : Window
{
    private readonly string _cancel;
    private readonly IReadOnlyList<Choice> _choices;

    public ChoiceDialog(string title, IReadOnlyList<string> paragraphs, IReadOnlyList<Choice> choices, string cancel, double width = 480, ConfirmGroup? group = null)
    {
        if (group is { } g && !App.EnterActs(g) && choices.Any(c => c.Id == cancel))
        {
            choices = [.. choices.Select(c => c.Primary
                ? c with { Primary = false, KeyHint = c.KeyHint == "Enter" ? null : c.KeyHint, Key = c.Key == Key.Enter ? null : c.Key }
                : c.Id == cancel ? c with { Primary = true } : c)];
        }

        _cancel = cancel;
        _choices = choices;
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var choice in choices)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = choice.Label } } };
            if (choice.KeyHint is { } hint)
            {
                content.Children.Add(new TextBlock { Text = hint, FontSize = 11, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center });
            }

            var button = new Button { Content = content, Padding = new Thickness(14, 5), IsDefault = choice.Primary, Tag = choice.Id };
            if (choice.Primary)
            {
                button.Classes.Add("accent");
            }

            button.Click += (_, _) => Close(choice.Id);
            Buttons.Add(button);
            (choice.Left ? left : right).Children.Add(button);
        }

        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new DockPanel { Children = { left, right } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        DockPanel.SetDock(left, Dock.Left);
        var body = new StackPanel { Margin = new Thickness(20, 12, 20, 18), Spacing = 8 };
        foreach (var p in paragraphs)
        {
            body.Children.Add(OptionsUi.Text(p, 13, wrap: true));
        }

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = TitleBarParts.ForDialog(this, root);
        List<Button> order = [.. left.Children.OfType<Button>(), .. right.Children.OfType<Button>()];
        DialogButtons.ArrowsAcross(this, () => order);
        Opened += (_, _) =>
        {
            if ((Buttons.FirstOrDefault(b => b.IsDefault) ?? Buttons.FirstOrDefault()) is { } first)
            {
                DialogButtons.FocusShown(first);
            }
        };
    }

    internal List<Button> Buttons { get; } = [];

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close(_cancel);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && _choices.FirstOrDefault(c => c.Key == e.Key) is { } choice)
        {
            Close(choice.Id);
            e.Handled = true;
        }
    }
}
