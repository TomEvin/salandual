// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/FolderLookPicker.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Settings;

namespace Salandual.App.Panels;

internal sealed class FolderLookPicker : Border
{
    private const int Columns = 11;

    private readonly List<List<Control>> _rows = [];

    public FolderLookPicker(FolderLook current, bool any, Action<Func<FolderLook, FolderLook?>> change, Action pickImage, Action done)
    {
        Padding = new Thickness(12, 10);
        MinWidth = 280;

        var look = current;
        void Apply(Func<FolderLook, FolderLook?> c)
        {
            change(c);
            look = c(look) ?? new FolderLook();
            Light(ColourButtons!, b => Equals(b.Tag, look.Colour));
            Light(MarkButtons!, b => look.Image is null && Equals(b.Tag, look.Mark));
            ResetButton!.IsEnabled = true;
        }

        var colours = new List<Control> { Swatch(NameColour.Normal, current.Colour == NameColour.Normal, () => Apply(l => l with { Colour = NameColour.Normal })) };
        foreach (var colour in MainWindow.FolderColours)
        {
            colours.Add(Swatch(colour, current.Colour == colour, () => Apply(l => l with { Colour = colour })));
        }

        ColourButtons = [.. colours.Cast<ToggleButton>()];

        var marks = new List<Control> { Mark(null, current.Mark is null && current.Image is null, () => Apply(l => l with { Mark = null, Image = null })) };
        foreach (var mark in FolderMarks.All)
        {
            marks.Add(Mark(mark, current.Mark == mark && current.Image is null, () => Apply(l => l with { Mark = mark, Image = null })));
        }

        MarkButtons = [.. marks.Cast<ToggleButton>()];

        ImageButton = Link(current.Image is null ? AppStrings.FolderLook_Image.Replace("_", "") : AppStrings.FolderLook_OtherImage, () =>
        {
            done();
            pickImage();
        });
        ResetButton = Link(AppStrings.FolderLook_Reset.Replace("_", ""), () => Apply(_ => null));
        ResetButton.IsEnabled = any;
        OkButton = new Button { Content = AppStrings.Common_OK, Padding = new Thickness(16, 4), HorizontalAlignment = HorizontalAlignment.Right };
        OkButton.Classes.Add("accent");
        OkButton.Click += (_, _) => done();
        DockPanel.SetDock(OkButton, Dock.Right);

        var colourGrid = new UniformGrid { Columns = Columns };
        colourGrid.Children.AddRange(colours);
        var markGrid = new UniformGrid { Columns = Columns };
        markGrid.Children.AddRange(marks);

        foreach (var grid in new[] { colours, marks })
        {
            for (var i = 0; i < grid.Count; i += Columns)
            {
                _rows.Add(grid.Skip(i).Take(Columns).ToList());
            }
        }

        _rows.Add([ImageButton]);
        _rows.Add([ResetButton]);
        _rows.Add([OkButton]);

        Child = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Head(AppStrings.FolderLook_ColourHead),
                colourGrid,
                Rule(),
                Head(AppStrings.FolderLook_MarkHead),
                markGrid,
                Rule(),
                ImageButton,
                new DockPanel { LastChildFill = false, Children = { ResetButton, OkButton } },
            },
        };
    }

    internal IReadOnlyList<ToggleButton> ColourButtons { get; }

    internal IReadOnlyList<ToggleButton> MarkButtons { get; }

    internal Button ImageButton { get; }

    internal Button ResetButton { get; }

    internal Button OkButton { get; }

    public void FocusFirst() => (ColourButtons.FirstOrDefault(b => b.IsChecked == true) ?? ColourButtons[0]).Focus(NavigationMethod.Directional);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
        {
            return;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var row = focused is Control f ? _rows.FindIndex(r => r.Contains(f)) : -1;
        if (row < 0)
        {
            return;
        }

        var column = _rows[row].IndexOf((Control)focused!);
        (row, column) = e.Key switch
        {
            Key.Left => (row, Math.Max(0, column - 1)),
            Key.Right => (row, Math.Min(_rows[row].Count - 1, column + 1)),
            Key.Up => (Math.Max(0, row - 1), column),
            _ => (Math.Min(_rows.Count - 1, row + 1), column),
        };
        var target = _rows[row][Math.Min(column, _rows[row].Count - 1)];
        if (!target.IsEnabled && target == ResetButton)
        {
            target = e.Key == Key.Up ? ImageButton : OkButton;
        }

        target.Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    private static void Light(IReadOnlyList<ToggleButton> buttons, Func<ToggleButton, bool> lit)
    {
        foreach (var button in buttons)
        {
            button.IsChecked = lit(button);
        }
    }

    private static ToggleButton Swatch(NameColour colour, bool lit, Action pick)
    {
        Control face = colour is not NameColour.Normal and var c
            ? new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(2), Background = FolderLooks.FolderBrush(c, FolderLooks.IsDarkTheme) }
            : new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1), Child = new TextBlock { Text = "∅", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }
                .Res(Border.BorderBrushProperty, "Brush.BorderStrong");
        var name = colour is not NameColour.Normal ? MainWindow.ColourName(colour) : AppStrings.FolderLook_NoColour;
        var button = Toggle(face, name, lit, pick);
        button.Width = button.Height = 28;
        button.Margin = new Thickness(0, 0, 4, 4);
        button.Tag = colour;
        return button;
    }

    private static ToggleButton Mark(string? mark, bool lit, Action pick)
    {
        var face = new TextBlock { Text = mark ?? "∅", FontSize = mark is { Length: > 1 } ? 10 : 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var button = Toggle(face, mark ?? AppStrings.FolderLook_NoMark, lit, pick);
        button.Tag = mark;
        button.Width = button.Height = 28;
        button.Margin = new Thickness(0, 0, 4, 4);
        return button;
    }

    private static ToggleButton Toggle(Control face, string name, bool lit, Action pick)
    {
        var button = new ToggleButton
        {
            Content = face,
            IsChecked = lit,
            Padding = new Thickness(4),
            MinWidth = 0,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Classes.Add("chip");
        ToolTip.SetTip(button, name);
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) =>
        {
            button.IsChecked = true;
            pick();
        };
        return button;
    }

    private static TextBlock Head(string text) => OptionsUi.Text(text, 12, FontWeight.SemiBold, "Brush.SecondaryText");

    private static Border Rule() => new Border { Height = 1, Margin = new Thickness(-4, 2) }.Res(Border.BackgroundProperty, "Brush.Border");

    private static Button Link(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(4, 2), Background = Brushes.Transparent, BorderThickness = default, HorizontalAlignment = HorizontalAlignment.Left };
        button.Res(Button.ForegroundProperty, "Brush.Accent");
        button.Click += (_, _) => click();
        return button;
    }
}
