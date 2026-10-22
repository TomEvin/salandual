// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Find/FindWindow.Criteria.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Salandual.App.Options;
using Salandual.App.Theme;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Search;
using Salandual.Core.Settings;

namespace Salandual.App.Find;

internal sealed class TextRow
{
    private readonly ComboBox _how;
    private readonly ToggleButton[] _kinds;
    private TextKind _kind;
    private bool _formatting;

    public TextRow(FindWindow owner, SearchText text, IReadOnlyList<string> history)
    {
        _how = OptionsUi.Combo<bool>([(true, AppStrings.Find_Contains), (false, AppStrings.Find_DoesntContain)], text.Contains, _ => owner.RowsChanged(), minWidth: 140);
        Avalonia.Automation.AutomationProperties.SetName(_how, AppStrings.Find_ContainsOrNot);
        Box = new ComboBox { IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 300, Text = text.Text, PlaceholderText = AppStrings.Find_AnyContent };
        foreach (var item in history)
        {
            Box.Items.Add(item);
        }

        Avalonia.Automation.AutomationProperties.SetName(Box, AppStrings.Find_Containing);
        Clear = FindWindow.AttachClear(Box);
        Box.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.TextProperty)
            {
                if (_kind == TextKind.Hex && !_formatting)
                {
                    Dispatcher.UIThread.Post(FormatHex, DispatcherPriority.Send);
                }

                owner.RowsChanged();
            }
        };

        Box.AddHandler(InputElement.TextInputEvent, (_, e) =>
        {
            if (_kind == TextKind.Hex && e.Text is { } typed && !HexInput.IsHex(typed))
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        _kinds = [.. new[] { (TextKind.Text, AppStrings.Find_KindText), (TextKind.Regex, AppStrings.Find_KindRegex), (TextKind.Hex, AppStrings.Find_KindHex) }.Select(k =>
        {
            var button = new ToggleButton { Content = k.Item2, Tag = k.Item1, IsChecked = k.Item1 == text.Kind, Padding = new Thickness(10, 4), Focusable = true };
            button.Classes.Add("seg");
            button.Click += (_, _) => SetKind(k.Item1, owner);
            button.IsCheckedChanged += (_, _) => button.IsChecked = (TextKind)button.Tag! == _kind;
            return button;
        })];
        var kinds = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Children = { _kinds[0], _kinds[1], _kinds[2] } };
        var kindsFrame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(2), Child = kinds }.Res(Border.BorderBrushProperty, "Brush.Border");

        _kind = text.Kind;
        Pieces = new Button { Content = ".* ▾", FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), Padding = new Thickness(8, 4), IsVisible = text.Kind == TextKind.Regex };
        ToolTip.SetTip(Pieces, AppStrings.Find_PiecesTip);
        Pieces.Flyout = RegexMenu.Create(() => Box);
        Remove = new Button { Content = AppIcons.Glyph("Icon.Cross", 10, 10, 1.5), Padding = new Thickness(8, 6), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        ToolTip.SetTip(Remove, AppStrings.Find_RemoveText);
        Remove.Click += (_, _) => owner.RemoveTextRow(this);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(_how);
        grid.Children.Add(Box);
        grid.Children.Add(kindsFrame);
        grid.Children.Add(Pieces);
        grid.Children.Add(Remove);
        for (var i = 0; i < grid.Children.Count; i++)
        {
            Grid.SetColumn(grid.Children[i], i);
            grid.Children[i].VerticalAlignment = VerticalAlignment.Center;
        }

        View = grid;
    }

    public Control View { get; }

    public ComboBox Box { get; }

    public Button Clear { get; }

    public Button Pieces { get; }

    public Button Remove { get; }

    public ComboBox How => _how;

    public string Text => Box.Text ?? "";

    public bool Contains => _how.SelectedIndex != 1;

    public TextKind Kind => _kind;

    public SearchText Read() => new(Text, Kind, Contains);

    public void SetKind(TextKind kind, FindWindow owner)
    {
        var before = _kind;
        _kind = kind;
        foreach (var button in _kinds)
        {
            button.IsChecked = (TextKind)button.Tag! == kind;
        }

        if (kind == TextKind.Hex && before != TextKind.Hex && Text.Length > 0)
        {
            SetText(HexInput.FromText(Text));
        }
        else if (before == TextKind.Hex && kind != TextKind.Hex && HexInput.ToText(Text) is { } readable)
        {
            SetText(readable);
        }

        Pieces.IsVisible = kind == TextKind.Regex;
        Box.FontFamily = kind == TextKind.Text ? FontFamily.Default : new FontFamily("Cascadia Mono,Consolas,monospace");
        Box.PlaceholderText = kind == TextKind.Hex ? AppStrings.Find_HexPlaceholder : AppStrings.Find_AnyContent;
        owner.RowsChanged();
    }

    private void SetText(string text)
    {
        _formatting = true;
        Box.Text = text;
        _formatting = false;
    }

    private void FormatHex()
    {
        var text = Text;
        var formatted = HexInput.Format(text);
        if (_kind != TextKind.Hex || formatted == text)
        {
            return;
        }

        var box = FindWindow.TextBoxIn(Box);
        var digits = HexInput.DigitsBefore(text, box?.CaretIndex ?? text.Length);
        SetText(formatted);
        if (box is not null)
        {
            box.CaretIndex = HexInput.CaretAfter(digits);
        }
    }
}

internal static class RegexMenu
{
    public static MenuFlyout Create(Func<Control> target)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Items.Add(new MenuItem { Header = AppStrings.Find_Pieces, IsEnabled = false });
        foreach (var piece in RegexPieces.All.Where(p => !p.Ready))
        {
            flyout.Items.Add(Item(piece, target));
        }

        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem { Header = AppStrings.Find_ReadyToUse, IsEnabled = false });
        foreach (var piece in RegexPieces.All.Where(p => p.Ready))
        {
            flyout.Items.Add(Item(piece, target));
        }

        return flyout;
    }

    public static MenuFlyout CreateReplacement(Func<Control> target)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var piece in RegexPieces.Replacements)
        {
            flyout.Items.Add(Item(piece, target));
        }

        return flyout;
    }

    private static MenuItem Item(RegexPiece piece, Func<Control> target)
    {
        var pattern = new TextBlock { Text = piece.Pattern.Length > 24 ? piece.Pattern[..23] + "…" : piece.Pattern, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), FontSize = 12, Width = 170, VerticalAlignment = VerticalAlignment.Center };
        pattern.Res(TextBlock.ForegroundProperty, "Brush.Accent");
        var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { pattern, new TextBlock { Text = piece.Description, VerticalAlignment = VerticalAlignment.Center } } };
        var item = new MenuItem { Header = header, Tag = piece };
        item.Click += (_, _) => Insert(target(), piece);
        return item;
    }

    public static void Insert(Control target, RegexPiece piece)
    {
        if (FindWindow.TextBoxIn(target) is not { } box)
        {
            return;
        }

        var text = box.Text ?? "";
        var (start, end) = (Math.Min(box.SelectionStart, box.SelectionEnd), Math.Max(box.SelectionStart, box.SelectionEnd));
        if (start == end)
        {
            start = end = Math.Clamp(box.CaretIndex, 0, text.Length);
        }

        var (result, caret) = RegexPieces.Insert(text, start, end, piece);
        box.Text = result;
        box.Focus();
        box.SelectionStart = box.SelectionEnd = box.CaretIndex = caret;
    }
}

internal sealed partial class FindWindow
{
    private readonly Border _morePanel = new() { IsVisible = false, Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
    private readonly Button _encodingChip = new();
    private readonly Button _addText = new();
    private readonly Button _advancedButton = new();
    private readonly TextBlock _advancedCaret = new() { FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _summaryField = new() { MinHeight = 28, Padding = new Thickness(10, 0, 2, 0), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand) };
    private readonly TextBlock _summaryText = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _summarySkipped = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private readonly Button _clearAdvanced = new();
    private bool _moreOpen;

    private readonly TextBox _minSize = new() { Width = 72 };
    private readonly TextBox _maxSize = new() { Width = 72 };
    private ComboBox _unit = null!;
    private ComboBox _modified = null!;
    private readonly NumericUpDown _withinCount = new() { Minimum = 1, Maximum = 999, Value = 30, FormatString = "0", ParsingNumberStyle = System.Globalization.NumberStyles.None, Width = 112 };
    private ComboBox _withinUnit = null!;
    private readonly CalendarDatePicker _from = new() { Width = 140, IsTodayHighlighted = true };
    private readonly CalendarDatePicker _to = new() { Width = 140, IsTodayHighlighted = true };
    private readonly TextBlock _and = OptionsUi.Text(AppStrings.Find_And, 13);
    private readonly Dictionary<FileAttributes, AttributeToggle> _attributes = [];
    private readonly TextBlock _attributesHelp = OptionsUi.Text(AppStrings.Find_AttributesHelp, 11.5, brush: "Brush.TertiaryText");
    private readonly TextBlock _skippedLabel = new();
    private readonly WrapPanel _skipped = new() { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
    private bool _applying;

    internal bool AdvancedOpen
    {
        get => _moreOpen;
        set
        {
            _moreOpen = value;
            _morePanel.IsVisible = value;
            _advancedButton.Classes.Set("on", value);
            _advancedCaret.Text = value ? "▴" : "▾";
        }
    }

    internal TextBox MinSizeBox => _minSize;

    internal TextBox MaxSizeBox => _maxSize;

    internal ComboBox UnitBox => _unit;

    internal ComboBox ModifiedBox => _modified;

    internal NumericUpDown WithinCount => _withinCount;

    internal ComboBox WithinUnitBox => _withinUnit;

    internal CalendarDatePicker FromDate => _from;

    internal CalendarDatePicker ToDate => _to;

    internal IReadOnlyDictionary<FileAttributes, AttributeToggle> AttributeBoxes => _attributes;

    internal Button AddTextButton => _addText;

    internal Button EncodingChip => _encodingChip;

    internal Button AdvancedButton => _advancedButton;

    internal string SummaryText => (_summaryText.Text ?? "") + (_summarySkipped.IsVisible ? " " + string.Concat(_summarySkipped.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : "");

    internal Border SummaryField => _summaryField;

    internal Button ClearAdvancedButton => _clearAdvanced;

    internal string SkippedLabel => _skippedLabel.Text ?? "";

    private Control BuildCriteria(Control? findButtons)
    {
        var lookInMenu = new Button { Content = "+ ▾", Padding = new Thickness(10, 5) };
        ToolTip.SetTip(lookInMenu, AppStrings.Find_AddFolderTip);
        lookInMenu.Flyout = LookInMenu();

        _addText.Content = AppStrings.Find_AnotherText;
        _addText.Classes.Add("link");
        _addText.Click += (_, _) =>
        {
            AddTextRow(new SearchText(""));
            _rows[^1].Box.Focus();
        };

        _encodingChip.Classes.Add("chip");
        _encodingChip.Flyout = EncodingMenu();
        UpdateEncodingChip();

        var options = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 14, LineSpacing = 6 };
        options.Children.Add(_addText);
        options.Children.Add(_allOrAnyGroup);
        options.Children.Add(_matchCase);
        options.Children.Add(_wholeWords);
        options.Children.Add(_encodingChip);
        foreach (var child in options.Children)
        {
            child.VerticalAlignment = VerticalAlignment.Center;
        }

        Control containing = _duplicates ? BuildSame() : new StackPanel { Spacing = 6, Children = { _textRows, options, _problem } };

        var lookIn = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 8 };
        var subfolders = OptionsUi.Text(AppStrings.Find_Subfolders, 13);
        foreach (var (control, column) in new (Control, int)[] { (_lookIn, 0), (lookInMenu, 1), (subfolders, 2), (_depth, 3), (_levels, 4), (_insideZip, 5) })
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, column);
            lookIn.Children.Add(control);
        }

        BuildMore();
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 8,
        };
        Add(Label(AppStrings.Find_Named), 0, 0);
        Add(_named, 0, 1, span: findButtons is null ? 2 : 1);
        if (findButtons is not null)
        {
            Add(findButtons, 0, 2);
        }

        Add(Label(AppStrings.Find_LookIn), 1, 0);
        Add(lookIn, 1, 1, span: 2);
        var containingLabel = Label(_duplicates ? AppStrings.Find_TheSame : AppStrings.Find_Containing);
        containingLabel.VerticalAlignment = VerticalAlignment.Top;
        containingLabel.Margin = new Thickness(0, 7, 0, 0);
        Add(containingLabel, 2, 0);
        Add(containing, 2, 1, span: 2);
        Add(AdvancedLine(), 3, 1, span: 2);
        Add(_morePanel, 4, 0, span: 3);
        _morePanel.Res(Border.BackgroundProperty, "Brush.PanelBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        UpdateSummary();

        return new Border { Padding = new Thickness(14, 12), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid, Classes = { "fcriteria" } }.Res(Border.BorderBrushProperty, "Brush.Border");

        void Add(Control control, int row, int column, int span = 1)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            Grid.SetColumnSpan(control, span);
            grid.Children.Add(control);
        }

        static TextBlock Label(string text)
        {
            var label = OptionsUi.Text(text, 13, brush: "Brush.SecondaryText");
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MinWidth = 80;
            label.TextAlignment = TextAlignment.Right;
            return label;
        }
    }

    private Control AdvancedLine()
    {
        _advancedButton.Classes.Add("chip");
        _advancedButton.MinHeight = 28;
        _advancedButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { AppIcons.Create("Icon.Advanced")!, new TextBlock { Text = AppStrings.Find_Advanced, VerticalAlignment = VerticalAlignment.Center }, _advancedCaret } };
        Avalonia.Automation.AutomationProperties.SetName(_advancedButton, AppStrings.Find_Advanced);
        _advancedButton.Click += (_, _) => AdvancedOpen = !AdvancedOpen;
        _advancedCaret.Text = "▾";

        _clearAdvanced.Classes.Add("clearadv");
        _clearAdvanced.Content = new Avalonia.Controls.Shapes.Path
        {
            Data = AppIcons.Get("Icon.Cross").Parts[0].Geometry,
            Stretch = Stretch.Uniform,
            Width = 8,
            Height = 8,
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _clearAdvanced.Padding = new Thickness(0);
        _clearAdvanced.Width = _clearAdvanced.Height = 22;
        _clearAdvanced.CornerRadius = new CornerRadius(11);
        _clearAdvanced.Margin = new Thickness(8, 0, 0, 0);
        _clearAdvanced.VerticalAlignment = VerticalAlignment.Center;
        _clearAdvanced.HorizontalContentAlignment = HorizontalAlignment.Center;
        _clearAdvanced.VerticalContentAlignment = VerticalAlignment.Center;
        _clearAdvanced.Focusable = false;
        ToolTip.SetTip(_clearAdvanced, AppStrings.Find_ResetMore);
        Avalonia.Automation.AutomationProperties.SetName(_clearAdvanced, AppStrings.Find_ResetMore);
        _clearAdvanced.Click += (_, _) => ResetMore();

        var inside = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        Grid.SetColumn(_summarySkipped, 1);
        Grid.SetColumn(_clearAdvanced, 2);
        inside.Children.Add(_summaryText);
        inside.Children.Add(_summarySkipped);
        inside.Children.Add(_clearAdvanced);
        _summaryField.Child = inside;
        _summaryField.Res(Border.BackgroundProperty, "Brush.PanelBackground").Res(Border.BorderBrushProperty, "Brush.Border");
        ToolTip.SetTip(_summaryField, AppStrings.Find_SummaryTip);
        _summaryField.Tapped += (_, e) => SummaryTapped(e.Source);

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        Grid.SetColumn(_summaryField, 1);
        line.Children.Add(_advancedButton);
        line.Children.Add(_summaryField);
        return line;
    }

    internal void SummaryTapped(object? source)
    {
        if (source is Visual visual && (visual == _clearAdvanced || Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(_clearAdvanced, visual)))
        {
            return;
        }

        AdvancedOpen = !AdvancedOpen;
    }

    internal void AddTextRow(SearchText text)
    {
        var row = new TextRow(this, text, _settings.Current.Find.Texts);
        _rows.Add(row);
        _textRows.Children.Add(row.View);
        RowsChanged();
    }

    internal void RemoveTextRow(TextRow row)
    {
        if (_rows.Count <= 1)
        {
            return;
        }

        _rows.Remove(row);
        _textRows.Children.Remove(row.View);
        RowsChanged();
    }

    internal void RowsChanged()
    {
        UpdateRows();
        UpdateTitle();
    }

    private void UpdateRows()
    {
        foreach (var row in _rows)
        {
            row.Remove.IsVisible = _rows.Count > 1;
        }

        _allOrAnyGroup.IsVisible = _rows.Count > 1;
    }

    internal static Button AttachClear(ComboBox box)
    {
        var clear = new Button
        {
            Content = AppIcons.Glyph("Icon.Cross", 9, 9, 1.5),
            Padding = new Thickness(6, 4),
            Margin = new Thickness(0, 0, 2, 0),
            Background = Brushes.Transparent,
            BorderThickness = default,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            IsVisible = !string.IsNullOrEmpty(box.Text),
        };
        ToolTip.SetTip(clear, AppStrings.Common_Clear);
        Avalonia.Automation.AutomationProperties.SetName(clear, AppStrings.Common_Clear);
        clear.Click += (_, _) =>
        {
            box.Text = "";
            box.Focus();
        };
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.TextProperty)
            {
                clear.IsVisible = !string.IsNullOrEmpty(box.Text);
            }
        };
        box.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<TextBox>("PART_EditableTextBox") is { } text)
            {
                text.InnerRightContent = clear;
            }
        };
        return clear;
    }

    private MenuFlyout FindMenu()
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Items.Add(Item(AppStrings.Find_Find, "Enter", ResultMode.New));
        flyout.Items.Add(Item(AppStrings.Find_FindAndAdd, "Ctrl+W", ResultMode.Add));
        flyout.Items.Add(Item(AppStrings.Find_KeepMatching, "Ctrl+I", ResultMode.Keep));
        flyout.Items.Add(Item(AppStrings.Find_RemoveMatching, "Ctrl+S", ResultMode.Remove));
        flyout.Items.Add(new Separator());
        var duplicates = new MenuItem { Header = AppStrings.Find_DuplicatesMenu, InputGesture = KeyGesture.Parse("Alt+Shift+F7") };
        duplicates.Click += (_, _) => _request.OpenDuplicates((_named.Text ?? "").Trim(), (_lookIn.Text ?? "").Trim());
        flyout.Items.Add(duplicates);
        return flyout;

        MenuItem Item(string header, string key, ResultMode mode)
        {
            var item = new MenuItem { Header = header, InputGesture = KeyGesture.Parse(key), Tag = mode };
            item.Click += (_, _) => _ = StartAsync(mode);
            return item;
        }
    }

    private MenuFlyout LookInMenu()
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var other = new MenuItem { Header = AppStrings.Find_OtherPanelFolder };
        other.Click += (_, _) => AddLookIn(_request.OtherPanelFolder());
        var browse = new MenuItem { Header = AppStrings.Find_BrowseFolder };
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = AppStrings.Find_LookIn });
            foreach (var folder in folders)
            {
                AddLookIn(Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(folder));
            }
        };
        var drives = new MenuItem { Header = AppStrings.Find_AllLocalDrives };
        drives.Click += (_, _) => _lookIn.Text = string.Join("; ", _request.LocalDrives());
        flyout.Items.Add(other);
        flyout.Items.Add(browse);
        flyout.Items.Add(drives);
        flyout.Opening += (_, _) => other.IsEnabled = _request.OtherPanelFolder() is { Length: > 0 };
        return flyout;
    }

    internal void AddLookIn(string? folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        var current = (_lookIn.Text ?? "").Trim().TrimEnd(';');
        var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!parts.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            _lookIn.Text = current.Length == 0 ? folder : current + "; " + folder;
        }
    }

    private static readonly (SearchEncodings Flag, Func<string> Label)[] _encodingItems =
    [
        (SearchEncodings.Auto, () => AppStrings.Find_EncodingAuto),
        (SearchEncodings.Ansi, () => Loc.Format(AppStrings.Find_EncodingAnsi, Core.Viewer.TextEncodings.Ansi.Name)),
        (SearchEncodings.Oem, () => Loc.Format(AppStrings.Find_EncodingOem, "CP" + System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)),
        (SearchEncodings.Utf8, () => "UTF-8"),
        (SearchEncodings.Utf16LE, () => AppStrings.Find_EncodingUtf16LE),
        (SearchEncodings.Utf16BE, () => "UTF-16 BE"),
    ];

    private Flyout EncodingMenu()
    {
        var list = new StackPanel { Spacing = 2 };
        var heading = OptionsUi.Text(AppStrings.Find_EncodingsHeading, 12, brush: "Brush.TertiaryText");
        heading.Margin = new Thickness(2, 0, 0, 2);
        list.Children.Add(heading);
        var boxes = new List<(SearchEncodings Flag, ToggleButton Box)>();
        foreach (var (flag, label) in _encodingItems)
        {
            var box = Toggle(label());
            box.Tag = flag;
            box.HorizontalAlignment = HorizontalAlignment.Stretch;
            box.HorizontalContentAlignment = HorizontalAlignment.Left;
            box.MinHeight = 24;
            box.Padding = new Thickness(8, 1);
            box.IsCheckedChanged += (_, _) =>
            {
                if (_updatingEncodings)
                {
                    return;
                }

                var value = boxes.Where(b => b.Box.IsChecked == true).Aggregate(SearchEncodings.None, (v, b) => v | b.Flag);
                if (flag == SearchEncodings.Auto && box.IsChecked == true)
                {
                    value = SearchEncodings.Auto;
                }
                else if (flag != SearchEncodings.Auto && box.IsChecked == true)
                {
                    value &= ~SearchEncodings.Auto;
                }

                Encodings = value;
                Sync();
            };
            boxes.Add((flag, box));
            list.Children.Add(box);
            if (flag == SearchEncodings.Auto)
            {
                list.Children.Add(new Separator { Margin = new Thickness(0, 1) });
            }
        }

        EncodingBoxes = [.. boxes.Select(b => b.Box)];
        var flyout = new Flyout { Content = list, Placement = PlacementMode.BottomEdgeAlignedLeft, FlyoutPresenterClasses = { "compact" } };
        flyout.Opening += (_, _) => Sync();
        _syncEncodings = Sync;
        Sync();
        return flyout;

        void Sync()
        {
            _updatingEncodings = true;
            foreach (var (flag, box) in boxes)
            {
                box.IsChecked = flag == SearchEncodings.Auto ? _encodings.HasFlag(SearchEncodings.Auto) : !_encodings.HasFlag(SearchEncodings.Auto) && _encodings.HasFlag(flag);
            }

            _updatingEncodings = false;
        }
    }

    private bool _updatingEncodings;
    private Action? _syncEncodings;

    internal IReadOnlyList<ToggleButton> EncodingBoxes { get; private set; } = [];

    private void UpdateEncodingChip()
    {
        _encodingChip.Content = EncodingsText(_encodings) + "  ▾";
        _encodingChip.Classes.Set("on", !_encodings.HasFlag(SearchEncodings.Auto));
    }

    internal static string EncodingsText(SearchEncodings encodings)
    {
        if (encodings.HasFlag(SearchEncodings.Auto) || encodings == SearchEncodings.None)
        {
            return AppStrings.Find_EncodingsAny;
        }

        var names = new List<string>();
        foreach (var (flag, label) in new (SearchEncodings, string)[] { (SearchEncodings.Ansi, "ANSI"), (SearchEncodings.Oem, "OEM"), (SearchEncodings.Utf8, "UTF-8"), (SearchEncodings.Utf16LE, "UTF-16 LE"), (SearchEncodings.Utf16BE, "UTF-16 BE") })
        {
            if (encodings.HasFlag(flag))
            {
                names.Add(label);
            }
        }

        return Loc.Format(AppStrings.Find_EncodingsSome, string.Join(", ", names));
    }

    private static readonly (FileAttributes Attribute, Func<string> Name)[] _attributeItems =
    [
        (FileAttributes.Directory, () => AppStrings.Find_AttrFolder),
        (FileAttributes.ReadOnly, () => AppStrings.Find_AttrReadOnly), (FileAttributes.Hidden, () => AppStrings.Find_AttrHidden), (FileAttributes.System, () => AppStrings.Find_AttrSystem),
        (FileAttributes.Archive, () => AppStrings.Find_AttrArchive), (FileAttributes.Compressed, () => AppStrings.Find_AttrCompressed), (FileAttributes.Encrypted, () => AppStrings.Find_AttrEncrypted),
    ];

    private static readonly (int Unit, string Name)[] _sizeUnits = [(0, AppStrings.Find_UnitBytes), (1, "KB"), (2, "MB"), (3, "GB")];

    private void BuildMore()
    {
        _unit = OptionsUi.Combo<int>(_sizeUnits, 1, _ => UpdateSummary(), minWidth: 70);
        Avalonia.Automation.AutomationProperties.SetName(_unit, AppStrings.Find_SizeUnit);
        _modified = OptionsUi.Combo<int>([(0, AppStrings.Find_AnyTime), (1, AppStrings.Find_WithinLast), (2, AppStrings.Find_Between)], 0, _ => UpdateModified(), minWidth: 120);
        Avalonia.Automation.AutomationProperties.SetName(_modified, AppStrings.Find_Modified);
        _withinUnit = OptionsUi.Combo<int>([(0, AppStrings.Find_Hours), (1, AppStrings.Find_Days), (2, AppStrings.Find_Weeks), (3, AppStrings.Find_Months)], 1, _ => UpdateSummary(), minWidth: 90);
        Avalonia.Automation.AutomationProperties.SetName(_withinUnit, AppStrings.Find_WithinUnit);
        Avalonia.Automation.AutomationProperties.SetName(_withinCount, AppStrings.Find_WithinCount);
        Avalonia.Automation.AutomationProperties.SetName(_from, AppStrings.Find_From);
        Avalonia.Automation.AutomationProperties.SetName(_to, AppStrings.Find_To);
        _minSize.PlaceholderText = AppStrings.Find_SizeFrom;
        _maxSize.PlaceholderText = AppStrings.Find_SizeTo;
        Avalonia.Automation.AutomationProperties.SetName(_minSize, AppStrings.Find_SizeFrom);
        Avalonia.Automation.AutomationProperties.SetName(_maxSize, AppStrings.Find_SizeTo);
        _withinCount.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<TextBox>("PART_TextBox") is { } text)
            {
                DigitsOnly(text, 3);
            }
        };
        foreach (var box in new[] { _minSize, _maxSize })
        {
            DigitsOnly(box, 6);
            box.TextChanged += (_, _) => UpdateSummary();

            foreach (var focus in new[] { false, true })
            {
                box.Styles.Add(new Style(x => (focus ? x.OfType<TextBox>().Class("error").Class(":focus") : x.OfType<TextBox>().Class("error")).Template().OfType<Border>().Name("PART_BorderElement"))
                {
                    Setters = { new Setter(Border.BorderBrushProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Brush.Danger")) },
                });
            }
        }

        _withinCount.ValueChanged += (_, _) => UpdateSummary();
        _from.SelectedDateChanged += (_, _) => UpdateSummary();
        _to.SelectedDateChanged += (_, _) => UpdateSummary();
        foreach (var picker in new[] { _from, _to })
        {
            picker.CalendarOpened += (_, _) => Dispatcher.UIThread.Post(() => MarkRange(picker), DispatcherPriority.Background);
        }


        var size = Line(_minSize, OptionsUi.Text("–", 13), _maxSize, _unit);
        var modified = Line(_modified, _withinCount, _withinUnit, _from, _and, _to);
        var attributes = Line();
        foreach (var (attribute, name) in _attributeItems)
        {
            var toggle = new AttributeToggle(name(), UpdateSummary);
            _attributes[attribute] = toggle;
            attributes.Children.Add(toggle.View);
        }

        attributes.Children.Add(_attributesHelp);
        FillSkipped();
        _skippedLabel.Text = AppStrings.Find_SkippedFolders;
        ToolTip.SetTip(_skipped, AppStrings.Find_SkippedHelp);

        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), ColumnSpacing = 10, RowSpacing = 4 };
        AddRow(0, Label(AppStrings.Find_Size), size);
        AddRow(1, Label(AppStrings.Find_Modified), modified);
        AddRow(2, Label(AppStrings.Find_Attributes), attributes);
        AddRow(3, _skippedLabel, _skipped);
        StyleLabel(_skippedLabel);
        _morePanel.Child = form;
        UpdateModified();

        SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 640;
            _skippedLabel.Text = narrow ? AppStrings.Find_SkippedShort : AppStrings.Find_SkippedFolders;
            _attributesHelp.IsVisible = !narrow;
        };

        void AddRow(int row, Control label, Control value)
        {
            Grid.SetRow(label, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            form.Children.Add(label);
            form.Children.Add(value);
        }

        static WrapPanel Line(params Control[] children)
        {
            var line = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 4, MinHeight = 30, VerticalAlignment = VerticalAlignment.Center };
            foreach (var child in children)
            {
                child.VerticalAlignment = VerticalAlignment.Center;
                line.Children.Add(child);
            }

            return line;
        }

        static TextBlock Label(string text) => StyleLabel(new TextBlock { Text = text });

        static TextBlock StyleLabel(TextBlock label)
        {
            label.FontSize = 12.5;
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
            return label;
        }
    }

    private void UpdateModified()
    {
        var mode = _modified.SelectedIndex;
        _withinCount.IsVisible = _withinUnit.IsVisible = mode == 1;
        _from.IsVisible = _and.IsVisible = _to.IsVisible = mode == 2;
        UpdateSummary();
    }

    private void FillSkipped()
    {
        _skipped.Children.Clear();
        foreach (var folder in _settings.Current.Find.SkippedFolders)
        {
            var chip = new Button { Content = folder + "  ✕" };
            chip.Classes.Add("chip");
            ToolTip.SetTip(chip, AppStrings.Find_RemoveSkipped);
            chip.Click += (_, _) => SetSkipped(_settings.Current.Find.SkippedFolders.Where(f => f != folder).ToList());
            _skipped.Children.Add(chip);
        }

        var add = new TextBox { Width = 200, MinHeight = 26, Padding = new Thickness(8, 3), FontSize = 12, PlaceholderText = AppStrings.Find_AddSkipped };
        Avalonia.Automation.AutomationProperties.SetName(add, AppStrings.Find_AddSkipped);
        add.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (add.Text ?? "").Trim() is { Length: > 0 } folder)
            {
                SetSkipped([.. _settings.Current.Find.SkippedFolders, folder]);
                e.Handled = true;
            }
        };
        _skipped.Children.Add(add);
    }

    internal void SetSkipped(List<string> folders)
    {
        _settings.Update(s => s with { Find = s.Find with { SkippedFolders = folders } });
        FillSkipped();
        UpdateSummary();
    }

    internal TextBox SkippedInput => _skipped.Children.OfType<TextBox>().Last();

    internal void ResetMore() => ApplyAdvanced(new SearchDefaults());

    private void ApplyAdvanced(SearchDefaults d)
    {
        _applying = true;
        _minSize.Text = d.MinSize;
        _maxSize.Text = d.MaxSize;
        _unit.SelectedIndex = d.SizeUnit;
        _modified.SelectedIndex = (int)d.Modified;
        _withinCount.Value = d.WithinCount;
        _withinUnit.SelectedIndex = d.WithinUnit;
        _from.SelectedDate = d.From;
        _to.SelectedDate = d.To;
        foreach (var (attribute, toggle) in _attributes)
        {
            toggle.Rule = d.MustHave.HasFlag(attribute) ? AttributeRule.Must : d.MustNotHave.HasFlag(attribute) ? AttributeRule.MustNot : AttributeRule.Either;
        }

        _applying = false;
        UpdateModified();
    }

    private SearchDefaults ReadAdvanced(SearchDefaults d) => d with
    {
        MinSize = (_minSize.Text ?? "").Trim(),
        MaxSize = (_maxSize.Text ?? "").Trim(),
        SizeUnit = Math.Max(_unit.SelectedIndex, 0),
        Modified = (ModifiedRule)Math.Max(_modified.SelectedIndex, 0),
        WithinCount = (int)(_withinCount.Value ?? 30),
        WithinUnit = Math.Max(_withinUnit.SelectedIndex, 0),
        From = _from.SelectedDate?.Date,
        To = _to.SelectedDate?.Date,
        MustHave = _attributes.Where(a => a.Value.Rule == AttributeRule.Must).Aggregate((FileAttributes)0, (all, a) => all | a.Key),
        MustNotHave = _attributes.Where(a => a.Value.Rule == AttributeRule.MustNot).Aggregate((FileAttributes)0, (all, a) => all | a.Key),
    };

    private SearchCriteria ApplyMore(SearchCriteria criteria)
    {
        var now = DateTime.UtcNow;
        DateTime? from = null, to = null;
        if (_modified.SelectedIndex == 1 && _withinCount.Value is { } count)
        {
            var n = (double)count;
            from = _withinUnit.SelectedIndex switch
            {
                0 => now.AddHours(-n),
                2 => now.AddDays(-7 * n),
                3 => now.AddMonths(-(int)n),
                _ => now.AddDays(-n),
            };
        }
        else if (_modified.SelectedIndex == 2)
        {
            from = _from.SelectedDate?.Date.ToUniversalTime();
            to = _to.SelectedDate?.Date.AddDays(1).ToUniversalTime();
        }

        return criteria with
        {
            MinSize = Bytes(_minSize.Text, _unit.SelectedIndex),
            MaxSize = Bytes(_maxSize.Text, _unit.SelectedIndex),
            ModifiedFrom = from,
            ModifiedTo = to,
            Attributes = _attributes.Where(a => a.Value.Rule != AttributeRule.Either).ToDictionary(a => a.Key, a => a.Value.Rule),
        };
    }

    internal static string? AdvancedText(SearchDefaults d)
    {
        var parts = new List<string>();
        var unit = _sizeUnits[Math.Clamp(d.SizeUnit, 0, 3)].Name;
        var min = Bytes(d.MinSize, d.SizeUnit) is not null;
        var max = Bytes(d.MaxSize, d.SizeUnit) is not null;
        if (min && max)
        {
            parts.Add(Loc.Format(AppStrings.Find_SumSizeBetween, d.MinSize, d.MaxSize + " " + unit));
        }
        else if (min)
        {
            parts.Add(Loc.Format(AppStrings.Find_SumSizeAtLeast, d.MinSize + " " + unit));
        }
        else if (max)
        {
            parts.Add(Loc.Format(AppStrings.Find_SumSizeAtMost, d.MaxSize + " " + unit));
        }

        if (d.Modified == ModifiedRule.Within)
        {
            string[] units = [AppStrings.Find_Hours, AppStrings.Find_Days, AppStrings.Find_Weeks, AppStrings.Find_Months];
            parts.Add(Loc.Format(AppStrings.Find_SumWithin, d.WithinCount.ToString(System.Globalization.CultureInfo.CurrentCulture), units[Math.Clamp(d.WithinUnit, 0, 3)]));
        }
        else if (d.Modified == ModifiedRule.Between && (d.From is not null || d.To is not null))
        {
            parts.Add(Loc.Format(AppStrings.Find_SumBetween, d.From?.ToString("d", System.Globalization.CultureInfo.CurrentCulture) ?? "…", d.To?.ToString("d", System.Globalization.CultureInfo.CurrentCulture) ?? "…"));
        }

        var attributes = _attributeItems.Where(a => d.MustHave.HasFlag(a.Attribute)).Select(a => a.Name())
            .Concat(_attributeItems.Where(a => d.MustNotHave.HasFlag(a.Attribute)).Select(a => Loc.Format(AppStrings.Find_SumNot, a.Name()))).ToList();
        if (attributes.Count > 0)
        {
            parts.Add(string.Join(", ", attributes));
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    internal string? SizeProblem()
    {
        var from = (_minSize.Text ?? "").Trim();
        var to = (_maxSize.Text ?? "").Trim();
        var fromBytes = from.Length > 0 ? Bytes(from, 0) : 0;
        var toBytes = to.Length > 0 ? Bytes(to, 0) : long.MaxValue;
        return fromBytes is null ? Loc.Format(AppStrings.Find_SizeNotNumber, from)
            : toBytes is null ? Loc.Format(AppStrings.Find_SizeNotNumber, to)
            : fromBytes > toBytes ? AppStrings.Find_SizeFromBigger
            : null;
    }

    internal static void DigitsOnly(TextBox box, int max)
    {
        box.MaxLength = max;
        box.AddHandler(TextInputEvent, (_, e) =>
        {
            if (e.Text is { } text && !text.All(char.IsAsciiDigit))
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        box.TextChanged += (_, _) =>
        {
            var text = box.Text ?? "";
            if (!text.All(char.IsAsciiDigit) || text.Length > max)
            {
                var digits = new string([.. text.Where(char.IsAsciiDigit).Take(max)]);
                box.Text = digits;
                box.CaretIndex = digits.Length;
            }
        };
    }

    internal string? AdvancedProblem()
        => SizeProblem()
            ?? (_modified.SelectedIndex == 2 && _from.SelectedDate?.Date > _to.SelectedDate?.Date ? AppStrings.Find_DatesFromLater : null);

    private void MarkRange(CalendarDatePicker picker)
    {
        if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(picker).OfType<Popup>().FirstOrDefault()?.Child is not { } child)
        {
            return;
        }

        if (Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualDescendants(child).OfType<Calendar>().FirstOrDefault() is { } calendar && _rangeCalendars.Add(calendar))
        {
            calendar.DisplayDateChanged += (_, _) => Dispatcher.UIThread.Post(() => MarkRange(picker), DispatcherPriority.Background);
        }

        if (this.TryFindResource("Brush.Accent", ActualThemeVariant, out var accent) && accent is ISolidColorBrush solid)
        {
            Resources["Brush.FindRange"] = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(solid.Color, 0.6);
        }

        var from = _from.SelectedDate?.Date;
        var to = _to.SelectedDate?.Date;
        foreach (var day in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(child).OfType<CalendarDayButton>())
        {
            var date = day.DataContext as DateTime?;
            var inRange = from <= to && date >= from && date <= to;
            day.Classes.Set("inrange", inRange);
            day.Classes.Set("rangeend", inRange && (date == from || date == to));
        }
    }

    private readonly HashSet<Calendar> _rangeCalendars = [];

    private void MarkAdvanced()
    {
        var dates = AdvancedProblem() == AppStrings.Find_DatesFromLater;
        foreach (var picker in new[] { _from, _to })
        {
            picker.Classes.Set("error", dates);
            ToolTip.SetTip(picker, dates ? AppStrings.Find_DatesFromLater : null);
        }

        var problem = SizeProblem();
        foreach (var box in new[] { _minSize, _maxSize })
        {
            var bad = problem is not null && (problem == AppStrings.Find_SizeFromBigger || Bytes((box.Text ?? "").Trim(), 0) is null && (box.Text ?? "").Trim().Length > 0);
            box.Classes.Set("error", bad);
            ToolTip.SetTip(box, bad ? problem : null);
        }
    }

    private void UpdateSummary()
    {
        if (_applying || _unit is null)
        {
            return;
        }

        MarkAdvanced();
        var text = AdvancedText(ReadAdvanced(new SearchDefaults()));
        _summaryText.Text = text ?? AppStrings.Find_NoAdvanced;
        _summaryText.FontStyle = text is null ? FontStyle.Italic : FontStyle.Normal;
        _summaryText.Res(TextBlock.ForegroundProperty, text is null ? "Brush.TertiaryText" : "Brush.Text");
        ToolTip.SetTip(_summaryText, text);
        _clearAdvanced.IsVisible = text is not null;

        var skipped = _settings.Current.Find.SkippedFolders.Count;
        _summarySkipped.IsVisible = skipped > 0;
        _summarySkipped.Inlines!.Clear();
        if (skipped > 0)
        {
            var count = new Avalonia.Controls.Documents.Run(skipped.ToString(System.Globalization.CultureInfo.CurrentCulture)) { FontWeight = FontWeight.SemiBold };
            count.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, _summarySkipped.GetResourceObservable("Brush.Warning"));
            Rich.Add(_summarySkipped.Inlines, AppStrings.Find_SumSkipped, null, count);
        }
    }
}

internal sealed class AttributeToggle
{
    private readonly string _name;
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _signText = new() { Width = 12, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private string _sign = "";
    private readonly Action _changed;
    private AttributeRule _rule;

    public AttributeToggle(string name, Action changed)
    {
        _name = name;
        _changed = changed;
        View = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _signText, _text } }, Padding = new Thickness(7, 2, 9, 2) };
        View.Classes.Add("chip");
        Avalonia.Automation.AutomationProperties.SetName(View, name);
        ToolTip.SetTip(View, AppStrings.Find_AttributeTip);
        View.Click += (_, _) => Rule = _rule switch { AttributeRule.Either => AttributeRule.Must, AttributeRule.Must => AttributeRule.MustNot, _ => AttributeRule.Either };
        Show();
    }

    public Button View { get; }

    public AttributeRule Rule
    {
        get => _rule;
        set
        {
            _rule = value;
            Show();
            _changed();
        }
    }

    public string Text => _sign + _name;

    private void Show()
    {
        _sign = _rule switch { AttributeRule.Must => "✓ ", AttributeRule.MustNot => "✕ ", _ => "" };
        _signText.Text = _rule switch { AttributeRule.Must => "✓", AttributeRule.MustNot => "✕", _ => "○" };
        if (_rule == AttributeRule.Either)
        {
            _signText.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
        }
        else
        {
            _signText.ClearValue(TextBlock.ForegroundProperty);
        }

        _text.Text = _name;
        _text.TextDecorations = _rule == AttributeRule.MustNot ? TextDecorations.Strikethrough : null;
        View.Classes.Set("on", _rule == AttributeRule.Must);
        View.Classes.Set("no", _rule == AttributeRule.MustNot);
    }
}
