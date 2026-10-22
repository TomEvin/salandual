// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Editor/EditorWindow.Toolbar.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.FileSystem;
using static Salandual.App.Viewer.ToolbarParts;
using TextSearch = Salandual.Core.Viewer.TextSearch;

namespace Salandual.App.Editor;

internal sealed partial class EditorWindow
{
    private readonly Border _findBar = new() { BorderThickness = new Thickness(0, 0, 0, 1) };
    private readonly TextBox _findBox = new() { Width = 300, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _replaceBox = new() { Width = 300, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ToggleButton _matchCase = Flag("Aa", AppStrings.Viewer_MatchCase);
    private readonly ToggleButton _wholeWords = Flag("W", AppStrings.Viewer_WholeWords);
    private readonly ToggleButton _regex = Flag(".*", AppStrings.Viewer_Regex);
    private readonly ToggleButton _inSelection = Flag("⌶", AppStrings.Editor_InSelection);
    private readonly TextBlock _hits = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 80 };
    private readonly DispatcherTimer _findDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Border _sizeNotice = new() { IsVisible = false };
    private readonly Border _diskBar = new() { IsVisible = false };
    private readonly Border _errorBar = new() { IsVisible = false };
    private List<(int Start, int Length)> _matches = [];
    private int _current = -1;
    private Control _replaceRow = null!;
    private Button _save = null!;
    private Button _undo = null!;
    private Button _redo = null!;
    private Button _view = null!;
    private ToggleButton _wrap = null!;
    private ToggleButton _findToggle = null!;
    private ToggleButton _replaceToggle = null!;
    private Button _goTo = null!;
    private Button _more = null!;
    private Border _kindSeparator = null!;
    private Button _format = null!;
    private Button _minify = null!;
    private Border _checkChip = null!;
    private readonly TextBlock _checkChipText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Avalonia.Controls.Shapes.Ellipse _checkChipDot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    private Border _toolSeparator = null!;
    private FitRow _left = null!;
    private Avalonia.Controls.Shapes.Ellipse _moreDot = null!;

    internal string HitsText => _hits.Text ?? "";

    internal TextBox FindBox => _findBox;

    internal TextBox ReplaceBox => _replaceBox;

    internal Button FindPieces { get; private set; } = null!;

    internal Button ReplacePieces { get; private set; } = null!;

    internal Avalonia.Controls.Primitives.ToggleButton RegexFlag => _regex;

    internal bool FindBarOpen => _findBar.IsVisible;

    internal bool ReplaceOpen => _findBar.IsVisible && _replaceRow.IsVisible;

    internal IReadOnlyList<(int Start, int Length)> Matches => _matches;

    internal Button SaveButton => _save;

    internal Border SizeNotice => _sizeNotice;

    internal Border DiskBar => _diskBar;

    internal Border ErrorBar => _errorBar;

    private Control BuildToolbar()
    {
        _save = Tool(Label("Icon.Save", AppStrings.Common_Save, "Ctrl+S"), AppStrings.Editor_SaveTip);
        _save.BorderThickness = new Thickness(1);
        _save.Res(Button.BorderBrushProperty, "Brush.Accent");
        _save.Res(Button.ForegroundProperty, "Brush.Accent");
        _save.Click += (_, _) => _ = SaveAsync();
        _undo = Tool(Glyph("Icon.Undo"), AppStrings.Editor_UndoTip);
        _undo.Click += (_, _) => _editor.Undo();
        _redo = Tool(Glyph("Icon.Redo"), AppStrings.Editor_RedoTip);
        _redo.Click += (_, _) => _editor.Redo();
        _view = Tool(Label("Icon.File", AppStrings.Editor_View, "F3"), AppStrings.Editor_ViewTip);
        _view.Click += (_, _) => ViewFile();
        _format = Tool(Label("Icon.Format", AppStrings.Editor_Format, "Shift+Alt+F"), AppStrings.Editor_FormatTip);
        _format.Click += (_, _) => _ = FormatAsync();
        _minify = Tool(Label("Icon.Minify", AppStrings.Editor_Minify, null), AppStrings.Editor_MinifyTip);
        _minify.Click += (_, _) => _ = MinifyAsync();
        _checkChip = new Border
        {
            Padding = new Thickness(8, 3),
            Margin = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _checkChipDot, _checkChipText } },
        }.Res(Border.BackgroundProperty, "Brush.Hover");
        _checkChipText.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _checkChip.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                GoToProblem();
            }
        };
        _toolSeparator = Separator();
        var preview = BuildPreviewToggle();
        _wrap = Toggle(Label("Icon.Wrap", AppStrings.Viewer_Wrap, "F2"), AppStrings.Viewer_WrapTip);
        _wrap.Click += (_, _) => UpdateEditor(o => o with { Wrap = _wrap.IsChecked == true });

        _findToggle = Toggle(Label("Icon.Find", AppStrings.Viewer_Find, "Ctrl+F"), AppStrings.Viewer_FindTip);
        _findToggle.Click += (_, _) =>
        {
            if (_findBar.IsVisible && !_replaceRow.IsVisible)
            {
                CloseFind();
            }
            else
            {
                OpenFind(replace: false);
            }
        };
        _replaceToggle = Toggle(Label("Icon.Replace", AppStrings.Editor_Replace, "Ctrl+H"), AppStrings.Editor_ReplaceTip);
        _replaceToggle.Click += (_, _) =>
        {
            if (_findBar.IsVisible && _replaceRow.IsVisible)
            {
                CloseFind();
            }
            else
            {
                OpenFind(replace: true);
            }
        };
        _goTo = Tool(Label("Icon.GoTo", AppStrings.Viewer_GoTo, "Ctrl+G"), AppStrings.Editor_GoToTip);
        _goTo.Click += (_, _) => OpenGoTo();
        _moreDot = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -3, -4, 0) };
        _moreDot.Res(Avalonia.Controls.Shapes.Shape.FillProperty, "Brush.Accent");
        _more = Tool(new Panel { Children = { Glyph("Icon.More"), _moreDot } }, AppStrings.Viewer_MoreTip);
        _more.Click += (_, _) => BuildMoreMenu().ShowAt(_more);

        _kindSeparator = Separator();
        var left = _left = Row(_save, _undo, _redo, Separator(), _format, _minify, _checkChip, _toolSeparator, _view, preview, _kindSeparator, _wrap);
        left.HiddenChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _moreDot.IsVisible = _left.Hidden.Count > 0;
            ToolTip.SetTip(_more, _moreDot.IsVisible ? AppStrings.Viewer_MoreWithTools : AppStrings.Viewer_MoreTip);
        });
        var right = Row(_findToggle, _replaceToggle, _goTo, _more);
        var dock = new DockPanel { Margin = new Thickness(8, 4) };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(left);
        return new Border { Child = dock, BorderThickness = new Thickness(0, 0, 0, 1) }
            .Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
    }

    private void UpdateToolbar()
    {
        if (_save is null)
        {
            return;
        }

        _undo.IsEnabled = _editor.CanUndo;
        _redo.IsEnabled = _editor.CanRedo;
        _view.IsVisible = _language == SyntaxLanguage.Markdown && _request.View is not null;
        _format.IsVisible = CanFormat;
        _minify.IsVisible = _language == SyntaxLanguage.Json;
        var check = CheckText;
        _checkChip.IsVisible = check is not null;
        _checkChipText.Text = check ?? "";
        _checkChipDot.Res(Avalonia.Controls.Shapes.Shape.FillProperty, _problems.Count > 0 ? "Brush.Danger" : _checked ? "Brush.Success" : "Brush.TertiaryText");
        ToolTip.SetTip(_checkChip, _problems.Count > 0 ? _problems[0].Message : _checkKind == CheckKind.Yaml ? AppStrings.Editor_CheckYamlTip : AppStrings.Editor_CheckTip);
        _toolSeparator.IsVisible = CanFormat && (_view.IsVisible || PreviewKindNow is not null);
        _previewToggle.IsVisible = PreviewKindNow is not null;
        _previewToggle.IsChecked = _previewOn;
        _kindSeparator.IsVisible = CanFormat || _view.IsVisible || _previewToggle.IsVisible;
        _wrap.IsChecked = Options.Wrap;
        _findToggle.IsChecked = _findBar.IsVisible && !_replaceRow.IsVisible;
        _replaceToggle.IsChecked = _findBar.IsVisible && _replaceRow.IsVisible;
    }

    internal MenuFlyout BuildMoreMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Item(string header, string? key, Action click, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGesture = key is null ? null : KeyGesture.Parse(key), IsEnabled = enabled };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
        }

        void Noted(string header, string note, Action click, bool enabled = true)
        {
            menu.Items.Add(MainWindow.EditWithMenuItem(new EditWithItem(header, note, click, enabled)).Also(m => m.ToggleType = MenuItemToggleType.None));
        }

        void Check(string header, bool on, Action click)
        {
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
        }

        if (_left.Hidden.Count > 0)
        {
            menu.Items.Add(new MenuItem { Header = AppStrings.Viewer_NotOnToolbar, IsEnabled = false });
            foreach (var tool in _left.Hidden)
            {
                if (tool == _undo) { Item(AppStrings.Editor_UndoTip, null, () => _editor.Undo(), _editor.CanUndo); }
                else if (tool == _redo) { Item(AppStrings.Editor_RedoTip, null, () => _editor.Redo(), _editor.CanRedo); }
                else if (tool == _format) { Noted(AppStrings.Editor_Format, "Shift+Alt+F", () => _ = FormatAsync()); }
                else if (tool == _minify) { Item(AppStrings.Editor_Minify, null, () => _ = MinifyAsync()); }
                else if (tool == _checkChip) { Item(CheckText ?? "", null, GoToProblem, _problems.Count > 0); }
                else if (tool == _view) { Item(AppStrings.Editor_View, "F3", ViewFile); }
                else if (tool == _previewToggle) { Check(AppStrings.Editor_Preview, _previewOn, () => ShowPreview(!_previewOn)); }
                else if (tool == _wrap) { Check(AppStrings.Viewer_Wrap, Options.Wrap, () => UpdateEditor(o => o with { Wrap = !o.Wrap })); }
            }

            menu.Items.Add(new Separator());
        }

        Item(AppStrings.Editor_SaveAs, "Ctrl+Shift+S", () => _ = SaveAsAsync());
        Item(AppStrings.Editor_Reload, "Ctrl+R", () => _ = ReloadAsync(), _file.WriteTimeUtc != default);
        Item(AppStrings.Editor_View, "F3", ViewFile, _request.View is not null);
        if (_request.EditWith is not null)
        {
            var with = new MenuItem { Header = AppStrings.Editor_EditWith, InputGesture = KeyGesture.Parse("Ctrl+Shift+F4") };
            foreach (var item in _request.EditWith(_path))
            {
                with.Items.Add(MainWindow.EditWithMenuItem(item with { Run = () => _ = EditWithAsync(item.Run) }));
            }

            menu.Items.Add(with);
        }

        Item(AppStrings.Viewer_ShowInPanel, null, () => _request.ShowInPanel?.Invoke(_path), _request.ShowInPanel is not null);
        menu.Items.Add(new Separator());
        Item(AppStrings.Editor_SelectAll, "Ctrl+A", () => _editor.SelectAll());
        Item(AppStrings.Editor_DuplicateLine, "Ctrl+D", DuplicateLine);
        Noted(AppStrings.Editor_CommentLine, "Ctrl+/", CommentLine, CommentStyleNow is not null);
        if (_language == SyntaxLanguage.Json)
        {
            Noted(AppStrings.Editor_SortKeys, AppStrings.Editor_SortKeysNote, () => _ = SortKeysAsync());
        }

        Item(AppStrings.Editor_TrimSpaces, null, TrimTrailingSpaces);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = AppStrings.Editor_QuickSwitches, IsEnabled = false });
        if (_folding is not null)
        {
            Noted(AppStrings.Editor_FoldAll, "Ctrl+K Ctrl+0", () => FoldAll(true));
            Noted(AppStrings.Editor_UnfoldAll, "Ctrl+K Ctrl+J", () => FoldAll(false));
        }

        Check(AppStrings.Viewer_LineNumbers, Options.LineNumbers, () => UpdateEditor(o => o with { LineNumbers = !o.LineNumbers }));
        Check(AppStrings.Editor_ShowSpaces, Options.ShowSpaces, () => UpdateEditor(o => o with { ShowSpaces = !o.ShowSpaces }));
        Check(AppStrings.Editor_Strip, Options.Strip, () => UpdateEditor(o => o with { Strip = !o.Strip }));
        menu.Items.Add(new Separator());
        Item(AppStrings.Editor_Options, null, () => _request.OpenOptions?.Invoke("editors"), _request.OpenOptions is not null);
        return menu;
    }

    private void BuildFindBar()
    {
        FindPieces = Pieces(_regex, () => _findBox);
        ReplacePieces = Pieces(_regex, () => _replaceBox, replacement: true);
        _findBox.InnerRightContent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0), Children = { _matchCase, _wholeWords, _regex, FindPieces, _inSelection } };
        _replaceBox.InnerRightContent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0), Children = { ReplacePieces } };
        _findBox.PlaceholderText = AppStrings.Viewer_Find;
        _replaceBox.PlaceholderText = AppStrings.Editor_ReplaceWith;
        Avalonia.Automation.AutomationProperties.SetName(_findBox, AppStrings.Viewer_Find);
        Avalonia.Automation.AutomationProperties.SetName(_replaceBox, AppStrings.Editor_ReplaceWith);
        _findBox.TextChanged += (_, _) => FindAgainLater();
        _findDelay.Tick += (_, _) =>
        {
            _findDelay.Stop();
            Search(select: _findBox.IsFocused);
        };
        foreach (var flag in new[] { _matchCase, _wholeWords, _regex, _inSelection })
        {
            flag.Click += (_, _) => Search(select: true);
        }

        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                FindNext(forward: e.KeyModifiers != KeyModifiers.Shift);
                e.Handled = true;
            }
        };
        _replaceBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt))
                {
                    ReplaceAll();
                }
                else
                {
                    ReplaceOne();
                }

                e.Handled = true;
            }
        };

        var up = Tool(Label("Icon.ArrowUp", null, "Shift+F3"), AppStrings.Viewer_FindPreviousTip);
        up.Click += (_, _) => FindNext(forward: false);
        var down = Tool(Label("Icon.ArrowDown", null, "F3"), AppStrings.Viewer_FindNextTip);
        down.Click += (_, _) => FindNext(forward: true);
        var help = new TextBlock { Text = AppStrings.Viewer_FindHelp, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0) }
            .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
        var close = Tool(Glyph("Icon.Cross"), AppStrings.Viewer_FindCloseTip);
        close.Click += (_, _) => CloseFind();

        var replace = Tool(Label(null, AppStrings.Editor_ReplaceOne, "Enter"), AppStrings.Editor_ReplaceOneTip);
        replace.Click += (_, _) => ReplaceOne();
        var all = Tool(Label(null, AppStrings.Editor_ReplaceAll, "Ctrl+Alt+Enter"), AppStrings.Editor_ReplaceAllTip);
        all.Click += (_, _) => ReplaceAll();
        var allNote = new TextBlock { Text = AppStrings.Editor_ReplaceAllNote, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) }
            .Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");

        var findRow = Row(_findBox, _hits, up, down);
        findRow.Spacing = 8;
        var top = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(findRow, Dock.Left);
        top.Children.Add(close);
        top.Children.Add(findRow);
        top.Children.Add(help);
        var replaceRow = Row(_replaceBox, replace, all, allNote);
        replaceRow.Spacing = 8;
        _replaceRow = replaceRow;
        _findBar.Child = new StackPanel { Margin = new Thickness(8, 6), Spacing = 6, Children = { top, replaceRow } };
        _findBar.IsVisible = false;
        _findBar.Res(Border.BackgroundProperty, "Brush.WindowBackground").Res(Border.BorderBrushProperty, "Brush.Border");
    }

    internal void OpenFind(bool replace)
    {
        var selected = _editor.SelectedText;
        var isMatch = _matches.Any(m => m.Start == _editor.SelectionStart && m.Length == _editor.SelectionLength)
                      || (_findBox.Text is { Length: > 0 } && Query.Pattern(out _) is { } pattern && pattern.Match(selected) is { Success: true } hit && hit.Length == selected.Length);
        if (selected.Length is > 0 and <= 200 && !selected.Contains('\n') && _inSelection.IsChecked != true && !isMatch)
        {
            _findBox.Text = selected;
        }

        _findBar.IsVisible = true;
        _replaceRow.IsVisible = replace;
        UpdateToolbar();
        Search(select: false);
        Dispatcher.UIThread.Post(() =>
        {
            _findBox.Focus();
            _findBox.SelectAll();
        });
    }

    internal void CloseFind()
    {
        _findBar.IsVisible = false;
        _matches = [];
        _current = -1;
        _marks.Matches = _matches;
        _editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        _strip.InvalidateVisual();
        UpdateToolbar();
        _editor.Focus();
    }

    private void FindAgainLater()
    {
        if (_findBar.IsVisible)
        {
            _findDelay.Stop();
            _findDelay.Start();
        }
    }

    private TextQuery Query => new(_findBox.Text ?? "", _matchCase.IsChecked == true, _wholeWords.IsChecked == true, _regex.IsChecked == true);

    internal void Search(bool select)
    {
        _findDelay.Stop();
        var pattern = Query.Pattern(out var error);
        if (pattern is null)
        {
            _matches = [];
            _current = -1;
            _hits.Text = error is null ? "" : AppStrings.Editor_BadRegex;
            ToolTip.SetTip(_hits, error);
        }
        else
        {
            var text = _editor.Document.Text;
            var all = TextSearch.All(text, pattern);
            if (_inSelection.IsChecked == true && _editor.SelectionLength > 0)
            {
                var (from, to) = (_editor.SelectionStart, _editor.SelectionStart + _editor.SelectionLength);
                all = [.. all.Where(m => m.Start >= from && m.Start + m.Length <= to)];
            }

            _matches = all;
            ToolTip.SetTip(_hits, null);
            _current = select ? TextSearch.Next(_matches, _editor.SelectionLength > 0 ? _editor.SelectionStart : _editor.CaretOffset, forward: true) : -1;
            if (_current >= 0)
            {
                Show(_matches[_current]);
            }

            UpdateHits();
        }

        _marks.Matches = _matches;
        _editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        _strip.InvalidateVisual();
    }

    private void UpdateHits()
        => _hits.Text = _matches.Count == 0 ? (_findBox.Text is { Length: > 0 } ? AppStrings.Viewer_NoHits : "")
            : _current >= 0 ? Loc.Format(AppStrings.Editor_MatchOf, _current + 1, _matches.Count)
            : Loc.Format(AppStrings.Editor_Matches, _matches.Count);

    internal void FindNext(bool forward)
    {
        if (_findDelay.IsEnabled || _matches.Count == 0)
        {
            Search(select: false);
        }

        if (_matches.Count == 0)
        {
            return;
        }

        var caret = forward ? _editor.SelectionStart + _editor.SelectionLength : _editor.SelectionStart;
        if (forward && _editor.SelectionLength == 0)
        {
            caret = _editor.CaretOffset;
        }

        _current = TextSearch.Next(_matches, caret, forward);
        Show(_matches[_current]);
        UpdateHits();
    }

    private void Show((int Start, int Length) match)
    {
        _editor.Select(match.Start, match.Length);
        var location = _editor.Document.GetLocation(match.Start);
        _editor.ScrollTo(location.Line, location.Column);
    }

    internal void ReplaceOne()
    {
        if (Query.Pattern(out _) is not { } pattern)
        {
            return;
        }

        var start = _editor.SelectionStart;
        var length = _editor.SelectionLength;
        var hit = _matches.FindIndex(m => m.Start == start && m.Length == length);
        if (hit >= 0)
        {
            var with = TextSearch.Replacement(pattern, _editor.Document.Text, _matches[hit], _replaceBox.Text ?? "", Query.Regex);
            _editor.Document.Replace(start, length, with);
            _editor.CaretOffset = start + with.Length;
        }

        Search(select: false);
        FindNext(forward: true);
    }

    internal void ReplaceAll()
    {
        if (Query.Pattern(out _) is not { } pattern)
        {
            return;
        }

        Search(select: false);
        var text = _editor.Document.Text;
        var replacement = _replaceBox.Text ?? "";
        var document = _editor.Document;
        var count = _matches.Count;
        document.BeginUpdate();
        try
        {
            for (var i = _matches.Count - 1; i >= 0; i--)
            {
                var m = _matches[i];
                document.Replace(m.Start, m.Length, TextSearch.Replacement(pattern, text, m, replacement, Query.Regex));
            }
        }
        finally
        {
            document.EndUpdate();
        }

        Search(select: false);
        _hits.Text = Loc.Format(AppStrings.Editor_Replaced, count);
    }

    internal void OpenGoTo()
    {
        var box = new TextBox { Width = 220, PlaceholderText = Loc.Format(AppStrings.Editor_GoToPrompt, _editor.Document.LineCount) };
        var note = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 220, IsVisible = false }.Res(TextBlock.ForegroundProperty, "Brush.Danger");
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight, Content = new StackPanel { Spacing = 6, Children = { box, note } } };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            if (GoTo(box.Text ?? ""))
            {
                flyout.Hide();
                _editor.Focus();
            }
            else
            {
                note.Text = AppStrings.Editor_GoToBad;
                note.IsVisible = true;
            }
        };
        flyout.Opened += (_, _) => box.Focus();
        flyout.ShowAt(_goTo);
    }

    internal bool GoTo(string text)
    {
        var parts = text.Trim().Split(':', ',');
        if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out var line) || line < 1)
        {
            return false;
        }

        var column = 1;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out column) || column < 1))
        {
            return false;
        }

        line = Math.Min(line, _editor.Document.LineCount);
        var target = _editor.Document.GetLineByNumber(line);
        _editor.CaretOffset = target.Offset + Math.Min(column - 1, target.Length);
        _editor.Select(_editor.CaretOffset, 0);
        _editor.ScrollTo(line, column);
        return true;
    }

    private Control BuildNotices() => new StackPanel { Children = { _sizeNotice, _diskBar, _errorBar } };

    private static Border Notice(Border bar, string background, string icon, Control text, params Control[] actions)
    {
        var mark = new TextBlock { Text = icon, FontWeight = FontWeight.Bold, Width = 18, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.AddRange(actions);
        var dock = new DockPanel { Margin = new Thickness(10, 6) };
        DockPanel.SetDock(mark, Dock.Left);
        DockPanel.SetDock(row, Dock.Right);
        text.Margin = new Thickness(6, 0, 12, 0);
        dock.Children.Add(mark);
        dock.Children.Add(row);
        dock.Children.Add(text);
        bar.Child = dock;
        bar.BorderThickness = new Thickness(0, 0, 0, 1);
        bar.Res(Border.BackgroundProperty, background).Res(Border.BorderBrushProperty, "Brush.Border");
        return bar;
    }

    private static TextBlock NoticeText(string strong, string rest)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        text.Inlines!.Add(new Avalonia.Controls.Documents.Run(strong) { FontWeight = FontWeight.SemiBold });
        text.Inlines.Add(new Avalonia.Controls.Documents.Run(" " + rest));
        text.Res(TextBlock.ForegroundProperty, "Brush.Text");
        return text;
    }

    private Button LinkButton(string label, Action click, bool primary = false)
    {
        var b = new Button { Content = label, Padding = new Thickness(12, 4), VerticalAlignment = VerticalAlignment.Center };
        if (primary)
        {
            b.Classes.Add("accent");
        }

        b.Click += (_, _) => click();
        return b;
    }

    private void ShowSizeNotice((long Size, long Limit)? over)
    {
        _sizeNotice.IsVisible = over is not null;
        if (over is not { } o)
        {
            return;
        }

        var text = _request.Anyway
            ? NoticeText(AppStrings.Editor_PlainTitle, Loc.Format(AppStrings.Editor_AnywayText, Size(o.Size)))
            : NoticeText(AppStrings.Editor_PlainTitle, Loc.Format(AppStrings.Editor_PlainText, Size(o.Size), Size(o.Limit)));
        Notice(_sizeNotice, "Brush.InfoBackground", "i", text, LinkButton(AppStrings.Common_Close, () => _sizeNotice.IsVisible = false));
    }

    private void ShowDiskBar(bool gone)
    {
        var name = WindowsPath.GetName(_path);
        if (gone)
        {
            Notice(_diskBar, "Brush.WarningBackground", "!", NoticeText(Loc.Format(AppStrings.Editor_Gone_Title, name), AppStrings.Editor_Gone_Text),
                LinkButton(AppStrings.Common_Close, HideDiskBar));
        }
        else
        {
            Notice(_diskBar, "Brush.WarningBackground", "!",
                NoticeText(Loc.Format(AppStrings.Editor_Disk_Title, name), IsModified ? AppStrings.Editor_Disk_TextMine : AppStrings.Editor_Disk_Text),
                LinkButton(AppStrings.Editor_Reload_Button, () => _ = ReloadAsync(), primary: true),
                LinkButton(IsModified ? AppStrings.Editor_KeepMine : AppStrings.Editor_KeepIt, KeepMine));
        }

        _diskBar.IsVisible = true;
    }

    private void HideDiskBar() => _diskBar.IsVisible = false;

    private void ShowError(string message)
    {
        Notice(_errorBar, "Brush.DangerBackground", "!", new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, "Brush.Text"),
            LinkButton(AppStrings.Common_Close, HideError));
        _errorBar.IsVisible = true;
    }

    private void HideError() => _errorBar.IsVisible = false;
}
