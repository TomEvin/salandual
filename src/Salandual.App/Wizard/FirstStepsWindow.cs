// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Wizard/FirstStepsWindow.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Wizard;

internal sealed record FirstStepsResult(FirstStepsChoices Choices, SettingsImport? Import, IReadOnlySet<SettingsPart>? Parts);

public sealed partial class FirstStepsWindow : Window
{
    internal enum Step { Welcome, Theme, Keyboard, Bars, Panels, FileManager, Ready }

    private static readonly Step[] _steps = Enum.GetValues<Step>();

    private readonly AppServices _services;
    private readonly ThemeService? _theme;
    private readonly Grid _stepsBar = new() { Margin = new Thickness(24, 4, 24, 10) };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Button _skip;
    private readonly Button _back;
    private readonly Button _next;
    private bool _finished;

    public FirstStepsWindow(AppServices services, bool fromHelp)
    {
        _services = services;
        _theme = (Application.Current as App)?.Theme;
        FromHelp = fromHelp;
        WindowsDark = _theme?.WindowsDark ?? false;
        Choices = FirstStepsChoices.From(services.Settings.Current, StartWithWindowsState.IsOn(services.StartWithWindows, services.ProgramPath), WindowsDark);

        Title = AppStrings.Wizard_Title;
        Width = 860;
        Height = 640;
        CanResize = false;
        ShowInTaskbar = !fromHelp;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Res(BackgroundProperty, "Brush.WindowBackground");

        _skip = new Button { Content = fromHelp ? AppStrings.Common_Cancel : AppStrings.Wizard_Skip, Padding = new Thickness(4, 5), Background = Brushes.Transparent, BorderThickness = default };
        _skip.Click += (_, _) => Close(null);
        _back = new Button { Content = AppStrings.Wizard_Back, Padding = new Thickness(16, 5), MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        _back.Click += (_, _) => GoBack();
        _next = new Button { Content = AppStrings.Wizard_Next, Padding = new Thickness(16, 5), MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        _next.Classes.Add("accent");
        _next.Click += (_, _) => GoNext();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _back, _next } };
        var footerRow = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        footerRow.Children.Add(buttons);
        footerRow.Children.Add(_skip.Also(s => s.HorizontalAlignment = HorizontalAlignment.Left));
        var footer = new Border { Padding = new Thickness(24, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerRow }
            .Res(Border.BorderBrushProperty, "Brush.Border");
        var top = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = _stepsBar }.Res(Border.BorderBrushProperty, "Brush.Border");

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        root.Children.Add(_scroll.Res(BackgroundProperty, "Brush.PanelBackground"));
        Content = TitleBarParts.ForDialog(this, root);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(null);
            }
        };
        Closing += (_, _) =>
        {
            if (!_finished && Choices.ThemeChanged)
            {
                _theme?.Apply(_services.Settings.Current.Appearance);
            }
        };
        Opened += (_, _) => _next.Focus();
        Show(Step.Welcome);
    }

    internal bool FromHelp { get; }

    internal bool WindowsDark { get; }

    internal FirstStepsChoices Choices { get; private set; }

    internal Step Current { get; private set; }

    internal (SettingsImport Import, IReadOnlySet<SettingsPart> Parts)? Imported { get; private set; }

    internal Func<Task<(SettingsImport, IReadOnlySet<SettingsPart>)?>>? PickImport { get; set; }

    internal Button NextButton => _next;

    internal Button BackButton => _back;

    internal Button SkipButton => _skip;

    internal Control? PageContent => _scroll.Content as Control;

    private static bool Skipped(Step step, bool imported) => imported && step is not Step.Welcome and not Step.Ready;

    internal void GoNext()
    {
        if (Current == Step.Ready)
        {
            _finished = true;
            Close(new FirstStepsResult(Choices, Imported?.Import, Imported?.Parts));
            return;
        }

        Show(Imported is not null ? Step.Ready : Current + 1);
    }

    internal void GoBack()
    {
        if (Current > Step.Welcome)
        {
            Show(Imported is not null ? Step.Welcome : Current - 1);
        }
    }

    internal void Show(Step step, object? focus = null)
    {
        var offset = step == Current ? _scroll.Offset : default;
        Current = step;
        BuildStepsBar();
        _scroll.Content = new Border
        {
            Padding = new Thickness(28, 22, 28, 24),
            Child = step switch
            {
                Step.Welcome => Welcome(),
                Step.Theme => ThemePage(),
                Step.Keyboard => KeyboardPage(),
                Step.Bars => BarsPage(),
                Step.Panels => PanelsPage(),
                Step.FileManager => FileManagerPage(),
                _ => ReadyPage(),
            },
        };
        _scroll.Offset = offset;
        _back.IsEnabled = step > Step.Welcome;
        _next.Content = step != Step.Ready ? AppStrings.Wizard_Next : FromHelp ? AppStrings.Wizard_Done : AppStrings.Wizard_Start;
        _skip.IsVisible = FromHelp || step != Step.Ready;
        if (focus is not null)
        {
            Dispatcher.UIThread.Post(() => this.GetLogicalDescendants().OfType<InputElement>().FirstOrDefault(c => Equals((c as Control)?.Tag, focus))?.Focus());
        }
    }

    private void Change(Func<FirstStepsChoices, FirstStepsChoices> change, object? focus)
    {
        Choices = change(Choices);
        if (Choices.ThemeChanged)
        {
            _theme?.Apply(Choices.AppearanceWith(_services.Settings.Current.Appearance, DateTime.Now));
        }

        Show(Current, focus);
    }

    private void BuildStepsBar()
    {
        _stepsBar.Children.Clear();
        _stepsBar.ColumnDefinitions = new ColumnDefinitions(string.Join(",", _steps.Select(_ => "*")));
        var imported = Imported is not null;
        foreach (var step in _steps)
        {
            var i = (int)step;
            var skipped = Skipped(step, imported);
            var on = step == Current;
            var done = step < Current && !skipped;
            var later = step == Step.FileManager;
            var mark = done
                ? AppIcons.Glyph("Icon.Tick", 11, 11, 1.8, "Brush.Accent")
                : new TextBlock { Text = skipped ? "–" : (i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), FontSize = 11, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal }
                    .Res(TextBlock.ForegroundProperty, on ? "Brush.OnAccent" : "Brush.SecondaryText");
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            mark.VerticalAlignment = VerticalAlignment.Center;
            var dot = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = Corners.Chip,
                BorderThickness = new Thickness(on ? 0 : 1),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = mark,
            }.Res(Border.BackgroundProperty, on ? "Brush.Accent" : "Brush.WindowBackground").Res(Border.BorderBrushProperty, done ? "Brush.Accent" : "Brush.BorderStrong");
            var name = new TextBlock
            {
                Text = StepName(step),
                FontSize = 11.5,
                FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal,
                FontStyle = later ? FontStyle.Italic : FontStyle.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }.Res(TextBlock.ForegroundProperty, on ? "Brush.Text" : later || skipped ? "Brush.TertiaryText" : "Brush.SecondaryText");
            var cell = new StackPanel { Spacing = 4, Children = { dot, name } };
            Grid.SetColumn(cell, i);
            _stepsBar.Children.Add(cell);
        }
    }

    internal static string StepName(Step step) => step switch
    {
        Step.Welcome => AppStrings.Wizard_Step_Welcome,
        Step.Theme => AppStrings.Wizard_Step_Theme,
        Step.Keyboard => AppStrings.Wizard_Step_Keyboard,
        Step.Bars => AppStrings.Wizard_Step_Bars,
        Step.Panels => AppStrings.Wizard_Step_Panels,
        Step.FileManager => AppStrings.Wizard_Step_FileManager,
        _ => AppStrings.Wizard_Step_Ready,
    };

    private static StackPanel Page(string title, string? sub, params Control?[] parts)
    {
        var page = new StackPanel { Spacing = 14 };
        var head = new StackPanel { Spacing = 4, Children = { Text(title, 20, FontWeight.SemiBold, wrap: true) } };
        if (sub is not null)
        {
            head.Children.Add(Secondary(sub, 13));
        }

        page.Children.Add(head);
        foreach (var part in parts)
        {
            if (part is not null)
            {
                page.Children.Add(part);
            }
        }

        return page;
    }

    private static StackPanel Field(string label, Control choices, string? example = null, string? description = null, string? badge = null)
    {
        var head = badge is null ? (Control)Text(label, 13, FontWeight.SemiBold)
            : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Text(label, 13, FontWeight.SemiBold), Badge(badge) } };
        var field = new StackPanel { Spacing = 6, Children = { head, choices } };
        if (example is not null)
        {
            field.Children.Add(new TextBlock { Text = example, FontSize = 12.5 }.Res(TextBlock.ForegroundProperty, "Brush.Text").Res(TextBlock.FontFamilyProperty, "Font.Mono"));
        }

        if (description is not null)
        {
            field.Children.Add(Secondary(description, 12));
        }

        return field;
    }

    private Control Pills<T>(IReadOnlyList<(T Value, string Label)> options, T current, Func<FirstStepsChoices, T, FirstStepsChoices> change, string tag, bool theme = false)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < options.Count; i++)
        {
            var (value, label) = options[i];
            var key = tag + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var pill = new ToggleButton
            {
                Content = label,
                Tag = key,
                IsChecked = EqualityComparer<T>.Default.Equals(value, current),
                Padding = new Thickness(12, 5),
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = Corners.Chip,
            };
            pill.Click += (_, _) => Change(c => change(theme ? c with { ThemeChanged = true } : c, value), key);
            row.Children.Add(pill);
        }

        return row;
    }

    private CheckBox Tick(string title, string? description, bool on, Action<bool>? changed, string tag, bool enabled = true, string? badge = null)
    {
        var text = new StackPanel { Spacing = 1 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Text(title, 13, FontWeight.SemiBold) } };
        if (badge is not null)
        {
            head.Children.Add(Badge(badge));
        }

        text.Children.Add(head);
        if (description is not null)
        {
            text.Children.Add(Secondary(description, 12));
        }

        var box = new CheckBox { Content = text, IsChecked = on, IsEnabled = enabled, Tag = tag, VerticalContentAlignment = VerticalAlignment.Top };
        Avalonia.Automation.AutomationProperties.SetName(box, title);
        if (changed is not null)
        {
            box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        }

        return box;
    }

    private static Border Box(Control child, double padding = 14) => new Border
    {
        CornerRadius = Corners.Chip,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(padding),
        Child = child,
    }.Res(Border.BorderBrushProperty, "Brush.Border").Res(Border.BackgroundProperty, "Brush.WindowBackground");

    private static TextBlock Rich(string text, double size = 13)
    {
        var block = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Brush.Text");
        var parts = text.Split("**");
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                block.Inlines!.Add(new Run(parts[i]) { FontWeight = i % 2 == 1 ? FontWeight.SemiBold : FontWeight.Normal });
            }
        }

        return block;
    }

    private static T Later<T>(T control)
        where T : Control
    {
        control.Opacity = 0.55;
        return control;
    }

    private static Grid Columns(Control left, Control right, double spacing = 14)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = spacing };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }
}
