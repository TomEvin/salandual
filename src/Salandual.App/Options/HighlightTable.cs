// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Options/HighlightTable.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Salandual.App.Dialogs;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Resources;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using static Salandual.App.Options.OptionsUi;

namespace Salandual.App.Options;

internal sealed class HighlightTable : StackPanel
{
    private readonly SettingsService _settings;
    private readonly ListBox _list;
    private readonly Button _edit, _copy, _delete, _up, _down, _defaults;
    private bool _refreshing;

    public HighlightTable(SettingsService settings)
    {
        _settings = settings;
        Spacing = 8;
        Styles.Add(new Style(x => x.OfType<Button>().Class("tool").Class(":disabled")) { Setters = { new Setter(OpacityProperty, 0.35) } });
        AddButton = Tool("Icon.Add", AppStrings.Hl_Add, () => _ = EditAsync(-1));
        _edit = Tool("Icon.Rename", AppStrings.Hl_Edit, () => _ = EditAsync(_list!.SelectedIndex));
        _copy = Tool("Icon.Copy", AppStrings.Hl_Copy, Copy);
        _delete = Tool("Icon.Bin", AppStrings.Hl_Delete, Delete);
        _up = Tool("Icon.ArrowUp", AppStrings.Hl_MoveUpTip, () => Move(-1));
        _down = Tool("Icon.ArrowDown", AppStrings.Hl_MoveDownTip, () => Move(1));
        _defaults = Tool("Icon.Undo", AppStrings.Hl_Defaults, () => _settings.Update(s => s with { Highlighting = s.Highlighting with { Rules = null } }));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        tools.Children.AddRange([AddButton, _edit, _copy, _delete, Gap(), _up, _down, Gap(), _defaults]);

        _list = new ListBox { Padding = new Thickness(2), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), MaxHeight = 360 }
            .Res(ListBox.BorderBrushProperty, "Brush.Border").Res(ListBox.BackgroundProperty, "Brush.PanelBackground");
        Avalonia.Automation.AutomationProperties.SetName(_list, AppStrings.OptCat_Highlighting);
        _list.SelectionChanged += (_, _) => Enable();
        _list.DoubleTapped += (_, _) => _ = EditAsync(_list.SelectedIndex);
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down)
            {
                Move(e.Key == Key.Up ? -1 : 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                _ = EditAsync(_list.SelectedIndex);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                Delete();
                e.Handled = true;
            }
        };

        Children.Add(tools);
        Children.Add(_list);
        Children.Add(Secondary(AppStrings.Hl_TableNote));
        Refresh();
        _list.SelectedIndex = 0;
    }

    internal ListBox List => _list;

    internal Button AddButton { get; }

    internal IReadOnlyList<Button> Tools => [AddButton, _edit, _copy, _delete, _up, _down, _defaults];

    private static Border Gap() => new Border { Width = 1, Height = 18, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center }.Res(Border.BackgroundProperty, "Brush.Border");

    private static Button Tool(string glyph, string tip, Action click)
    {
        var icon = AppIcons.Glyph(glyph, 16, 16, 1.3);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var b = new Button
        {
            Content = icon, Width = 30, Height = 30, Padding = default, Background = Brushes.Transparent, BorderThickness = default,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, tip);
        Avalonia.Automation.AutomationProperties.SetName(b, tip);
        b.Classes.Add("tool");
        b.Click += (_, _) => click();
        return b;
    }

    internal TextBlock? SampleOf { get; private set; }

    private List<HighlightRule> Rules => [.. _settings.Current.Highlighting.Effective];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _settings.Changed += OnSettingsChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _settings.Changed -= OnSettingsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Old.Highlighting, e.New.Highlighting))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        _refreshing = true;
        var selected = _list.SelectedIndex;
        var rules = Rules;
        _list.Items.Clear();
        SampleOf = null;
        for (var i = 0; i < rules.Count; i++)
        {
            _list.Items.Add(new ListBoxItem { Content = RuleRow(rules[i], i), Padding = new Thickness(10, 4), MinHeight = 0 });
        }

        _list.SelectedIndex = Math.Min(Math.Max(selected, 0), rules.Count - 1);
        _defaults.IsEnabled = _settings.Current.Highlighting.Rules is not null;
        _refreshing = false;
        Enable();
    }

    private Control RuleRow(HighlightRule rule, int index)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*,Auto"), ColumnSpacing = 10 };
        var sample = new TextBlock
        {
            Text = AppStrings.Hl_SampleAa,
            FontSize = 14,
            FontWeight = rule.Style == NameStyle.Bold ? FontWeight.SemiBold : FontWeight.Normal,
            FontStyle = rule.Style == NameStyle.Italic ? FontStyle.Italic : FontStyle.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (RuleColour(rule) is { } brush)
        {
            sample.Foreground = brush;
        }
        else
        {
            sample.Res(TextBlock.ForegroundProperty, "Brush.Text");
        }

        SampleOf ??= sample;

        var name = Text(rule.Name, 13);
        var summary = Text(Summary(rule), 12, brush: "Brush.TertiaryText");
        summary.TextTrimming = TextTrimming.CharacterEllipsis;
        var words = new StackPanel { Children = { name, summary }, VerticalAlignment = VerticalAlignment.Center };
        var on = Toggle(rule.On, value => Change(index, r => r with { On = value }));
        on.VerticalAlignment = VerticalAlignment.Center;
        Avalonia.Automation.AutomationProperties.SetName(on, rule.Name);
        if (!rule.On)
        {
            sample.Opacity = words.Opacity = 0.5;
        }

        Grid.SetColumn(words, 1);
        Grid.SetColumn(on, 2);
        grid.Children.Add(sample);
        grid.Children.Add(words);
        grid.Children.Add(on);
        return grid;
    }

    private IBrush? RuleColour(HighlightRule rule)
    {
        static Rgb Token(string token) => Panels.FolderLooks.ThemeBrush(token) is { Color: var c } ? new Rgb(c.R, c.G, c.B) : new Rgb(0x80, 0x80, 0x80);
        return Highlighter.Colour(rule.Colour, Token(ColorTokens.PanelBackground), Token, rule.OwnLight, rule.OwnDark) is { } rgb
            ? new ImmutableSolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B))
            : null;
    }

    internal static string Summary(HighlightRule rule)
    {
        var parts = new List<string> { rule.For switch { RuleTarget.Files => AppStrings.Hl_Files, RuleTarget.Folders => AppStrings.Hl_Folders, _ => AppStrings.Hl_FilesAndFolders } };
        if (rule.Masks.Length > 0)
        {
            parts.Add(rule.Masks);
        }

        var must = Names(rule.MustHave);
        if (must.Count > 0)
        {
            parts.Add(string.Join(" " + (rule.AnyOf ? AppStrings.Hl_Or : AppStrings.Hl_And) + " ", must));
        }

        parts.AddRange(Names(rule.MustNotHave).Select(n => Loc.Format(AppStrings.Hl_Not, n)));
        return string.Join(" · ", parts);
    }

    private static List<string> Names(FileAttributes attributes)
    {
        var names = new List<string>();
        foreach (var (bit, name) in new[]
        {
            (FileAttributes.ReadOnly, AppStrings.Find_AttrReadOnly), (FileAttributes.Hidden, AppStrings.Find_AttrHidden), (FileAttributes.System, AppStrings.Find_AttrSystem),
            (FileAttributes.Archive, AppStrings.Find_AttrArchive), (FileAttributes.Compressed, AppStrings.Find_AttrCompressed), (FileAttributes.Encrypted, AppStrings.Find_AttrEncrypted),
            ((FileAttributes)0x400000, AppStrings.Hl_AttrOnlineOnly), (FileAttributes.ReparsePoint, AppStrings.Hl_AttrLink),
        })
        {
            if ((attributes & bit) != 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private void Enable()
    {
        if (_refreshing)
        {
            return;
        }

        var i = _list.SelectedIndex;
        var count = _list.ItemCount;
        _edit.IsEnabled = _copy.IsEnabled = _delete.IsEnabled = i >= 0;
        _up.IsEnabled = i > 0;
        _down.IsEnabled = i >= 0 && i < count - 1;
    }

    private void Save(List<HighlightRule> rules, int select)
    {
        _settings.Update(s => s with { Highlighting = s.Highlighting with { Rules = rules } });
        _list.SelectedIndex = Math.Min(select, _list.ItemCount - 1);
        _list.Focus();
    }

    private void Change(int index, Func<HighlightRule, HighlightRule> change)
    {
        var rules = Rules;
        if (index >= 0 && index < rules.Count)
        {
            rules[index] = change(rules[index]);
            Save(rules, _list.SelectedIndex);
        }
    }

    internal void Move(int by)
    {
        var rules = Rules;
        var i = _list.SelectedIndex;
        var j = i + by;
        if (i < 0 || j < 0 || j >= rules.Count)
        {
            return;
        }

        (rules[i], rules[j]) = (rules[j], rules[i]);
        Save(rules, j);
    }

    private void Copy()
    {
        var rules = Rules;
        var i = _list.SelectedIndex;
        if (i < 0 || rules.Count >= HighlightSettings.MaxRules)
        {
            return;
        }

        rules.Insert(i + 1, rules[i] with { Name = Loc.Format(AppStrings.Hl_CopyName, rules[i].Name) });
        Save(rules, i + 1);
    }

    internal void Delete()
    {
        var rules = Rules;
        var i = _list.SelectedIndex;
        if (i < 0)
        {
            return;
        }

        rules.RemoveAt(i);
        Save(rules, Math.Max(0, i - 1));
    }

    internal async Task EditAsync(int index)
    {
        var rules = Rules;
        if (TopLevel.GetTopLevel(this) is not Window owner || index >= rules.Count || (index < 0 && rules.Count >= HighlightSettings.MaxRules))
        {
            return;
        }

        var start = index >= 0 ? rules[index] : new HighlightRule { Colour = NameColour.Blue };
        var dialog = new HighlightRuleDialog(start, _settings.Current.History.FilterMasks);
        DialogOpened?.Invoke(dialog);
        if (await dialog.ShowDialog<HighlightRule?>(owner) is not { } rule)
        {
            return;
        }

        rules = Rules;
        var at = index >= 0 ? index : Math.Min(rules.Count, _list.SelectedIndex + 1);
        if (index >= 0)
        {
            rules[index] = rule with { On = rules[index].On };
        }
        else
        {
            rules.Insert(at, rule);
        }

        Save(rules, at);
    }

    internal Action<HighlightRuleDialog>? DialogOpened { get; set; }
}

internal sealed class FolderLookTable : StackPanel
{
    private readonly SettingsService _settings;
    private readonly Panels.FolderLooks? _looks;
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly Button _resetAll;

    public FolderLookTable(SettingsService settings, Panels.FolderLooks? looks)
    {
        _settings = settings;
        _looks = looks;
        Spacing = 8;
        _resetAll = ActionButton(AppStrings.Hl_ResetAll, () => _settings.Update(s => s with { Highlighting = s.Highlighting with { Folders = new(StringComparer.OrdinalIgnoreCase) } }));
        _resetAll.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = _rows, ClipToBounds = true }.Res(Border.BorderBrushProperty, "Brush.Border"));
        Children.Add(Secondary(AppStrings.Hl_FoldersNote));
        Children.Add(_resetAll);
        Refresh();
    }

    internal int RowCount => _rows.Children.Count;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _settings.Changed += OnSettingsChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _settings.Changed -= OnSettingsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Old.Highlighting, e.New.Highlighting))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        _rows.Children.Clear();
        var folders = _settings.Current.Highlighting.Folders.OrderBy(f => f.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
        _resetAll.IsEnabled = folders.Count > 0;
        if (folders.Count == 0)
        {
            _rows.Children.Add(new Border { Padding = new Thickness(12, 10), Child = Secondary(AppStrings.Hl_NoFolders, 13) });
            return;
        }

        foreach (var (path, look) in folders)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 10 };
            var icon = new FolderLookIcon(look, _looks) { Width = 18, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            var gone = IsGone(path);
            var name = Text(path, 13, brush: gone ? "Brush.TertiaryText" : "Brush.Text");
            name.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
            name.VerticalAlignment = VerticalAlignment.Center;
            var what = gone ? Text(AppStrings.Hl_NotFound, 12, brush: "Brush.Warning") : Text(Describe(look), 12, brush: "Brush.SecondaryText");
            what.VerticalAlignment = VerticalAlignment.Center;
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (gone)
            {
                actions.Children.Add(Link(AppStrings.Hl_FindIt, () => _ = FindItAsync(path)));
            }

            actions.Children.Add(Link(gone ? AppStrings.Hl_Remove : AppStrings.FolderLook_Reset.Replace("_", ""), () => _settings.Update(s => s with { Highlighting = s.Highlighting.WithFolder(path, null) })));
            Grid.SetColumn(name, 1);
            Grid.SetColumn(what, 2);
            Grid.SetColumn(actions, 3);
            grid.Children.Add(icon);
            grid.Children.Add(name);
            grid.Children.Add(what);
            grid.Children.Add(actions);
            _rows.Children.Add(new Border { Padding = new Thickness(12, 5), BorderThickness = new Thickness(0, _rows.Children.Count == 0 ? 0 : 1, 0, 0), Child = grid }.Res(Border.BorderBrushProperty, "Brush.Border"));
        }
    }

    internal static string Describe(FolderLook look)
    {
        var parts = new List<string>();
        if (look.Colour != NameColour.Normal)
        {
            parts.Add(MainWindow.ColourName(look.Colour));
        }

        if (look.Image is not null)
        {
            parts.Add(AppStrings.Hl_AnImage);
        }
        else if (look.Mark is { } mark)
        {
            parts.Add(mark);
        }

        return string.Join(", ", parts);
    }

    private static bool IsGone(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (root is null || root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return false;
            }

            var drive = new DriveInfo(root);
            return drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady && !Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task FindItAsync(string path)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = AppStrings.Hl_FindItTitle, AllowMultiple = false });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } folder)
        {
            _settings.Update(s => s.Highlighting.Moved(path, folder) is { } moved ? s with { Highlighting = moved } : s);
        }
    }

    private static Button Link(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(6, 1), FontSize = 12, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        button.Res(Button.ForegroundProperty, "Brush.Accent");
        button.Click += (_, _) => click();
        return button;
    }
}

internal sealed class FolderLookIcon(FolderLook look, Panels.FolderLooks? looks) : Control
{
    public override void Render(DrawingContext context)
    {
        var at = new Rect(0, 1, 14, 14);
        var dark = this.FindResource("Brush.PanelBackground") is ISolidColorBrush { Color: var c } && ColorMath.Luminance(new(c.R, c.G, c.B)) < 0.2;
        var pen = new ImmutablePen((this.FindResource("Brush.SecondaryText") as IBrush ?? Brushes.Gray).ToImmutable(), 1.2);
        if (!Panels.FolderLooks.DrawFolder(context, look, at, dark, pen))
        {
            AppIcons.Draw(context, "Icon.FolderLoading", at.TopLeft, pen, this.FindResource("Brush.Warning") as IBrush, 14.0 / 16);
        }

        looks?.DrawBadge(context, look, at, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
            this.FindResource("Brush.PanelBackground") as IBrush ?? Brushes.White, new ImmutablePen((this.FindResource("Brush.BorderStrong") as IBrush ?? Brushes.Gray).ToImmutable(), 1),
            this.FindResource("Brush.Text") as IBrush ?? Brushes.Black);
    }
}
