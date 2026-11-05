// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/HighlightRuleDialog.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Salandual.App.Find;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Search;
using Salandual.Core.Settings;

namespace Salandual.App.Dialogs;

public sealed class HighlightRuleDialog : Window
{
    private static readonly NameColour[] _colours =
        [NameColour.Normal, NameColour.Dimmed, NameColour.Blue, NameColour.Green, NameColour.Purple, NameColour.Orange, NameColour.Red, NameColour.Teal, NameColour.Pink, NameColour.Grey, NameColour.Own];

    private static readonly (FileAttributes Attribute, Func<string> Name)[] _attributes =
    [
        (FileAttributes.ReadOnly, () => AppStrings.Find_AttrReadOnly), (FileAttributes.Hidden, () => AppStrings.Find_AttrHidden), (FileAttributes.System, () => AppStrings.Find_AttrSystem),
        (FileAttributes.Archive, () => AppStrings.Find_AttrArchive), (FileAttributes.Compressed, () => AppStrings.Find_AttrCompressed), (FileAttributes.Encrypted, () => AppStrings.Find_AttrEncrypted),
        (OnlineOnly, () => AppStrings.Hl_AttrOnlineOnly), (FileAttributes.ReparsePoint, () => AppStrings.Hl_AttrLink),
    ];

    private const FileAttributes OnlineOnly = (FileAttributes)0x400000;

    private readonly TextBox _name;
    private readonly ComboBox _masks;
    private readonly AttributeToggle[] _toggles;
    private readonly CheckBox _anyOf;
    private readonly ToggleButton[] _swatches;
    private readonly ToggleButton[] _for;
    private readonly ToggleButton[] _style;
    private readonly StackPanel _own;
    private Rgb _ownLight;
    private Rgb _ownDark;
    private readonly TextBlock _sampleLight;
    private readonly TextBlock _sampleDark;
    private readonly TextBlock _error;
    private NameColour _colour;
    private readonly bool _loading = true;

    public HighlightRuleDialog(HighlightRule rule, IReadOnlyList<string> history)
    {
        Title = AppStrings.Hl_RuleTitle;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.PanelBackground");
        Styles.Add(FindStyles.Create());

        _name = new TextBox { Text = rule.Name, PlaceholderText = AppStrings.Hl_RuleNameHint };
        Avalonia.Automation.AutomationProperties.SetName(_name, AppStrings.Common_Name);

        _for = [Seg(AppStrings.Hl_Files), Seg(AppStrings.Hl_Folders), Seg(AppStrings.Hl_Both)];
        Choose(_for, (int)rule.For);
        var forRow = Segments(_for, i => Choose(_for, i));

        _masks = new ComboBox { IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 300, Text = rule.Masks, PlaceholderText = AppStrings.Hl_MasksHint };
        _masks.Bind(ComboBox.FontFamilyProperty, _masks.GetResourceObservable("Font.Mono"));
        Avalonia.Automation.AutomationProperties.SetName(_masks, AppStrings.Hl_Names);
        foreach (var item in history)
        {
            _masks.Items.Add(item);
        }

        _masks.SelectionChanged += (_, _) =>
        {
            if (_masks.SelectedItem is string picked)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _masks.Text = picked);
            }
        };

        var picks = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (label, mask) in FilterDialog.QuickPicks())
        {
            var pick = new Button { Content = label, Padding = new Thickness(8, 1), Margin = new Thickness(0, 0, 6, 0) };
            pick.Classes.Add("chip");
            pick.Click += (_, _) => _masks.Text = mask;
            picks.Children.Add(pick);
        }

        _toggles = [.. _attributes.Select(a => new AttributeToggle(a.Name(), Changed))];
        for (var i = 0; i < _attributes.Length; i++)
        {
            var bit = _attributes[i].Attribute;
            _toggles[i].Rule = (rule.MustHave & bit) != 0 ? AttributeRule.Must : (rule.MustNotHave & bit) != 0 ? AttributeRule.MustNot : AttributeRule.Either;
        }

        var chips = new WrapPanel();
        foreach (var t in _toggles)
        {
            t.View.Margin = new Thickness(0, 0, 6, 6);
            chips.Children.Add(t.View);
        }

        _anyOf = new CheckBox { Content = AppStrings.Hl_AnyOf, IsChecked = rule.AnyOf, IsEnabled = false };
        ToolTip.SetTip(_anyOf, AppStrings.Hl_AnyOfTip);
        ToolTip.SetShowOnDisabled(_anyOf, true);
        _anyOf.IsCheckedChanged += (_, _) => Changed();

        _colour = rule.Colour;
        _swatches = [.. _colours.Select(SwatchButton)];
        var swatchRow = new WrapPanel();
        foreach (var s in _swatches)
        {
            s.Margin = new Thickness(0, 0, 6, 6);
            swatchRow.Children.Add(s);
        }

        _ownLight = ColorMath.TryParseHex(rule.OwnLight, out var l) ? l : ColorMath.ParseHex("#0F6CBD");
        _ownDark = ColorMath.TryParseHex(rule.OwnDark, out var d) ? d : ColorMath.ParseHex("#479EF5");
        LightButton = OptionsUi.ColorButton(() => _ownLight, c =>
        {
            _ownLight = c;
            PickColour(NameColour.Own);
        }, AppStrings.Hl_OwnLight, 18);
        DarkButton = OptionsUi.ColorButton(() => _ownDark, c =>
        {
            _ownDark = c;
            PickColour(NameColour.Own);
        }, AppStrings.Hl_OwnDark, 18);
        var fromNamed = new Button { Content = AppStrings.Hl_OwnFromNamed, Padding = new Thickness(8, 2) };
        fromNamed.Classes.Add("chip");
        fromNamed.Flyout = NamedShades();
        _own = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { OptionsUi.Secondary(AppStrings.Hl_OwnLabel, 13), LightButton, OptionsUi.Secondary(AppStrings.Hl_OwnLight, 13), DarkButton, OptionsUi.Secondary(AppStrings.Hl_OwnDark, 13), fromNamed },
        };
        foreach (var c in _own.Children.OfType<TextBlock>())
        {
            c.VerticalAlignment = VerticalAlignment.Center;
        }

        fromNamed.Margin = new Thickness(6, 0, 0, 0);

        _style = [Seg(AppStrings.Hl_StyleNormal), Seg(AppStrings.Hl_StyleBold), Seg(AppStrings.Hl_StyleItalic)];
        _style[1].FontWeight = FontWeight.SemiBold;
        _style[2].FontStyle = FontStyle.Italic;
        Choose(_style, (int)rule.Style);
        var styleRow = Segments(_style, i =>
        {
            Choose(_style, i);
            Changed();
        });

        _sampleLight = new TextBlock();
        _sampleDark = new TextBlock();
        var samples = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        var light = Sample(_sampleLight, "#FFFFFF");
        var dark = Sample(_sampleDark, "#272727");
        Grid.SetColumn(dark, 2);
        samples.Children.Add(light);
        samples.Children.Add(dark);

        _error = OptionsUi.Text("", 12, brush: "Brush.Danger", wrap: true);
        _error.IsVisible = false;

        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14, RowSpacing = 10 };
        var row = 0;
        void Line(string label, Control control, bool top = false)
        {
            form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = OptionsUi.Secondary(label, 13);
            text.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center;
            text.Margin = new Thickness(0, top ? 5 : 0, 0, 0);
            Grid.SetRow(text, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            form.Children.Add(text);
            form.Children.Add(control);
            row++;
        }

        Line(AppStrings.Common_Name, _name);
        Line(AppStrings.Hl_For, forRow);
        Line(AppStrings.Hl_Names, new StackPanel { Children = { _masks, picks } }, top: true);
        Line(AppStrings.Find_Attributes, new StackPanel { Children = { chips, _anyOf } }, top: true);
        Line(AppStrings.Hl_Colour, new StackPanel { Spacing = 4, Children = { swatchRow, _own } }, top: true);
        Line(AppStrings.Hl_Style, styleRow);
        Line(AppStrings.Hl_LooksLike, samples, top: true);

        var ok = new Button { Content = AppStrings.Common_OK, IsDefault = true, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = AppStrings.Common_Cancel, MinWidth = 96, Padding = new Thickness(14, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        var footer = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
        }.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 18, 20, 16), Children = { form, _error } };
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = Controls.TitleBarParts.ForDialog(this, root);

        _masks.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.TextProperty)
            {
                Changed();
            }
        };
        _loading = false;
        Changed();
        Opened += (_, _) => _name.Focus();
    }

    public HighlightRule? Result { get; private set; }

    internal TextBox NameBox => _name;

    internal ComboBox MasksBox => _masks;

    internal IReadOnlyList<AttributeToggle> Attributes => _toggles;

    internal TextBlock ErrorText => _error;

    internal TextBlock SampleLight => _sampleLight;

    internal CheckBox AnyOfBox => _anyOf;

    internal Button LightButton { get; }

    internal Button DarkButton { get; }

    internal void PickOwn(Rgb light, Rgb dark)
    {
        (_ownLight, _ownDark) = (light, dark);
        PickColour(NameColour.Own);
    }

    private Flyout NamedShades()
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var panel = new StackPanel { Spacing = 2 };
        foreach (var colour in MainWindow.NamedColours)
        {
            var (light, dark) = Highlighter.Shades(colour)!.Value;
            var row = new Button { Padding = new Thickness(6, 3), Background = Brushes.Transparent, BorderThickness = default, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            row.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { OptionsUi.Swatch(ColorMath.ParseHex(light), 16), OptionsUi.Swatch(ColorMath.ParseHex(dark), 16), OptionsUi.Text(MainWindow.ColourName(colour), 13) },
            };
            row.Click += (_, _) =>
            {
                flyout.Hide();
                PickOwn(ColorMath.ParseHex(light), ColorMath.ParseHex(dark));
                LightButton.Content = OptionsUi.Swatch(_ownLight, 18);
                DarkButton.Content = OptionsUi.Swatch(_ownDark, 18);
            };
            panel.Children.Add(row);
        }

        flyout.Content = panel;
        return flyout;
    }

    internal void PickColour(NameColour colour)
    {
        _colour = colour;
        Changed();
    }

    internal void PickStyle(NameStyle style)
    {
        Choose(_style, (int)style);
        Changed();
    }

    internal void PickFor(RuleTarget target) => Choose(_for, (int)target);

    internal HighlightRule Current()
    {
        FileAttributes must = 0, mustNot = 0;
        for (var i = 0; i < _attributes.Length; i++)
        {
            if (_toggles[i].Rule == AttributeRule.Must)
            {
                must |= _attributes[i].Attribute;
            }
            else if (_toggles[i].Rule == AttributeRule.MustNot)
            {
                mustNot |= _attributes[i].Attribute;
            }
        }

        var own = _colour == NameColour.Own;
        return new HighlightRule
        {
            Name = (_name.Text ?? "").Trim(),
            For = (RuleTarget)Array.FindIndex(_for, b => b.IsChecked == true),
            Masks = (_masks.Text ?? "").Trim(),
            MustHave = must,
            MustNotHave = mustNot,
            AnyOf = _anyOf.IsChecked == true && System.Numerics.BitOperations.PopCount((uint)must) > 1,
            Colour = _colour,
            OwnLight = own ? ColorMath.ToHex(_ownLight) : null,
            OwnDark = own ? ColorMath.ToHex(_ownDark) : null,
            Style = (NameStyle)Math.Max(0, Array.FindIndex(_style, b => b.IsChecked == true)),
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && !_masks.IsDropDownOpen)
        {
            Close(null);
            e.Handled = true;
        }
    }

    internal void Accept()
    {
        var rule = Current();
        string? problem = rule.Name.Length == 0 ? AppStrings.Hl_NeedsName
            : rule.Masks.Length > 0 && FileMask.FromHistory(rule.Masks) is null ? AppStrings.Mask_TypeMask
            : null;
        if (problem is not null)
        {
            _error.Text = problem;
            _error.IsVisible = true;
            return;
        }

        Result = rule;
        Close(rule);
    }

    private void Changed()
    {
        if (_loading)
        {
            return;
        }

        _error.IsVisible = false;
        for (var i = 0; i < _colours.Length; i++)
        {
            _swatches[i].IsChecked = _colours[i] == _colour;
        }

        _own.Opacity = _colour == NameColour.Own ? 1 : 0.6;
        _anyOf.IsEnabled = _toggles.Count(t => t.Rule == AttributeRule.Must) > 1;
        var rule = Current();
        Paint(_sampleLight, rule, dark: false);
        Paint(_sampleDark, rule, dark: true);
    }

    private static void Paint(TextBlock sample, HighlightRule rule, bool dark)
    {
        var theme = ThemeCatalog.BuiltIn[dark ? 1 : 0];
        var tokens = ColorTokens.Resolve(theme);
        var background = tokens[ColorTokens.PanelBackground];
        var colour = Highlighter.Colour(rule.Colour, background, t => tokens[t], rule.OwnLight, rule.OwnDark) ?? tokens[ColorTokens.Text];
        sample.Text = SampleName(rule);
        sample.Foreground = new ImmutableSolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
        sample.FontWeight = rule.Style == NameStyle.Bold ? FontWeight.SemiBold : FontWeight.Normal;
        sample.FontStyle = rule.Style == NameStyle.Italic ? FontStyle.Italic : FontStyle.Normal;
    }

    internal static string SampleName(HighlightRule rule)
    {
        var first = rule.Masks.Split('|')[0].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (first is null || rule.Masks.StartsWith(FileMask.RegexPrefix, StringComparison.Ordinal))
        {
            return rule.For == RuleTarget.Folders ? AppStrings.Hl_SampleFolder : AppStrings.Hl_SampleFile;
        }

        var name = first.Replace("*", AppStrings.Hl_SampleStem).Replace('?', 'x');
        return name.EndsWith('.') ? name.TrimEnd('.') : name;
    }

    private Border Sample(TextBlock text, string background)
    {
        text.Margin = new Thickness(10, 6);
        text.FontFamily = (FontFamily?)this.FindResource("Font.Panel") ?? FontFamily.Default;
        return new Border
        {
            Background = new ImmutableSolidColorBrush(Color.Parse(background)),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Child = text,
        }.Res(Border.BorderBrushProperty, "Brush.Border");
    }

    private ToggleButton SwatchButton(NameColour colour)
    {
        var dark = Panels.FolderLooks.IsDarkTheme;
        Control face = colour switch
        {
            NameColour.Normal => new TextBlock { Text = AppStrings.Hl_SampleAa, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
            NameColour.Dimmed => new TextBlock { Text = AppStrings.Hl_SampleAa, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText"),
            NameColour.Own => new TextBlock { Text = AppStrings.Hl_OwnButton, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
            _ => new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(2), Background = Panels.FolderLooks.FolderBrush(colour, dark) },
        };
        var button = new ToggleButton { Content = face, Padding = new Thickness(5, 3), MinHeight = 26 };
        button.Classes.Add("chip");
        ToolTip.SetTip(button, MainWindow.ColourName(colour));
        Avalonia.Automation.AutomationProperties.SetName(button, MainWindow.ColourName(colour));
        button.Click += (_, _) => PickColour(colour);
        return button;
    }


    private static ToggleButton Seg(string text)
    {
        var button = new ToggleButton { Content = text, Padding = new Thickness(10, 3) };
        button.Classes.Add("seg");
        return button;
    }

    private static void Choose(ToggleButton[] buttons, int index)
    {
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].IsChecked = i == index;
        }
    }

    private static Border Segments(ToggleButton[] buttons, Action<int> picked)
    {
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            buttons[i].Click += (_, _) => picked(index);
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.AddRange(buttons);
        return new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = row,
        }.Res(Border.BorderBrushProperty, "Brush.Border");
    }
}
