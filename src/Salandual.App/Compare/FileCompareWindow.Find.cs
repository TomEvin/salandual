// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Compare/FileCompareWindow.Find.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Compare;
using Salandual.Core.Resources;

namespace Salandual.App.Compare;

internal sealed class FileCompareHost
{
    public IClipboardAccess? Clipboard { get; init; }

    public Action<string>? Edit { get; init; }

    public Action<string>? ShowInPanel { get; init; }

    public Action? OpenOptions { get; init; }

    public Func<(string Name, Action<string, string> Start)?>? Program { get; init; }
}

internal sealed partial class FileCompareWindow
{
    internal FileCompareHost? Host { get; init; }

    internal Task Finding { get; private set; } = Task.CompletedTask;

    internal TextBox FindBox => _findBox;

    internal bool FindOpen => _findBar.IsVisible;

    internal string FindCountText => _findCount.Text ?? "";

    internal ToggleButton MatchCaseButton => _matchCase;

    internal ToggleButton WholeWordsButton => _wholeWords;

    internal ToggleButton RegexButton => _regex;

    internal Button HexBytesButton => _findHex;

    internal IReadOnlyList<Button> FindInButtons => _findIn;

    internal Button FindOnlyButton => _findOnly;

    internal Button MoreButton => _more;

    private FindSides _findSides;
    private bool _findHexBytes;
    private bool _findOnlyDifferences;

    private static ToggleButton FindOption(string text, string tip)
    {
        var button = new ToggleButton { Content = text, MinWidth = 28, Height = 26, Padding = new Thickness(6, 0), FontSize = 12, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add("opt");
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }

    private static Button FindSegment(string text)
    {
        var button = new Button { Content = text, Focusable = false, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(0) };
        button.Classes.Add("chip");
        return button;
    }

    private Control BuildFindBar()
    {
        _findBox.PlaceholderText = AppStrings.FileCompare_FindPrompt;
        Avalonia.Automation.AutomationProperties.SetName(_findBox, AppStrings.FileCompare_FindPrompt);
        _findBox.TextChanged += (_, _) => Finding = SearchAsync(go: true);
        foreach (var option in new[] { _matchCase, _wholeWords, _regex })
        {
            option.IsCheckedChanged += (_, _) => Finding = SearchAsync(go: true);
        }

        _findText.Classes.Add("on");
        _findText.Click += (_, _) => SetHexBytes(false);
        _findHex.Click += (_, _) => SetHexBytes(true);
        _findIn[0].Classes.Add("on");
        for (var i = 0; i < _findIn.Length; i++)
        {
            var sides = (FindSides)i;
            _findIn[i].Click += (_, _) =>
            {
                _findSides = sides;
                for (var j = 0; j < _findIn.Length; j++)
                {
                    _findIn[j].Classes.Set("on", j == (int)sides);
                }

                Finding = SearchAsync(go: true);
            };
        }

        _findOnly.Content = AppStrings.FileCompare_Only;
        _findOnly.Classes.Add("chip");
        _findOnly.Focusable = false;
        ToolTip.SetTip(_findOnly, AppStrings.FileCompare_FindOnlyTip);
        _findOnly.Click += (_, _) =>
        {
            _findOnlyDifferences = !_findOnlyDifferences;
            _findOnly.Classes.Set("on", _findOnlyDifferences);
            Finding = SearchAsync(go: true);
        };

        var prev = BarButton("Icon.ArrowUp", AppStrings.Viewer_FindPreviousTip, () => NextHit(-1));
        var next = BarButton("Icon.ArrowDown", AppStrings.Viewer_FindNextTip, () => NextHit(1));
        var close = BarButton("Icon.Cross", AppStrings.Viewer_FindCloseTip, CloseFind);
        var help = new TextBlock { Text = AppStrings.Viewer_FindHelp, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        help.Res(TextBlock.ForegroundProperty, "Brush.TertiaryText");
        _findCount.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        var inLabel = new TextBlock { Text = AppStrings.FileCompare_FindIn, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 2, 0) };
        inLabel.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                AppIcons.Glyph("Icon.Find", 16, 16, 1.3).Also(g => g.VerticalAlignment = VerticalAlignment.Center),
                _findBox,
                _matchCase,
                _wholeWords,
                _regex,
                Switch(_findText, _findHex),
                Separator(),
                inLabel,
                Switch(_findIn[0], _findIn[1], _findIn[2]),
                _findOnly,
                Separator(),
                prev,
                next,
                _findCount,
                help,
            },
        };
        var dock = new DockPanel { Margin = new Thickness(8, 5) };
        DockPanel.SetDock(close, Dock.Right);
        dock.Children.Add(close);
        dock.Children.Add(row);
        _findBar.Child = dock;
        _findBar.Res(Border.BackgroundProperty, "Brush.ColumnHeader").Res(Border.BorderBrushProperty, "Brush.Border");
        return _findBar;

        static StackPanel Switch(params Button[] buttons)
        {
            buttons[0].CornerRadius = new CornerRadius(4, 0, 0, 4);
            buttons[^1].CornerRadius = new CornerRadius(0, 4, 4, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = -1, VerticalAlignment = VerticalAlignment.Center };
            foreach (var b in buttons)
            {
                row.Children.Add(b);
            }

            return row;
        }

        static Border Separator() => new Border { Width = 1, Height = 22, Margin = new Thickness(2, 0) }.Res(Border.BackgroundProperty, "Brush.BorderStrong");

        static Button BarButton(string icon, string tip, Action click)
        {
            var button = new Button { Content = AppIcons.Glyph(icon, 16, 16, 1.3), Focusable = false, VerticalAlignment = VerticalAlignment.Center };
            button.Classes.Add("ftool");
            ToolTip.SetTip(button, tip);
            Avalonia.Automation.AutomationProperties.SetName(button, tip);
            button.Click += (_, _) => click();
            return button;
        }
    }

    private void SetHexBytes(bool hex)
    {
        _findHexBytes = hex;
        _findText.Classes.Set("on", !hex);
        _findHex.Classes.Set("on", hex);
        Finding = SearchAsync(go: true);
    }

    private void UpdateFindOptions()
    {
        var binary = _view.IsBinary;
        _matchCase.IsVisible = _wholeWords.IsVisible = _regex.IsVisible = !binary;
        ((Control)_findText.Parent!).IsVisible = binary;
        _findButton.Classes.Set("on", _findBar.IsVisible);
    }

    internal void OpenFind()
    {
        var was = _findBar.IsVisible;
        _findBar.IsVisible = true;
        UpdateFindOptions();
        _findBox.Focus();
        _findBox.SelectAll();
        if (!was && (_findBox.Text ?? "").Length > 0)
        {
            Finding = SearchAsync(go: true);
        }
    }

    internal void CloseFind()
    {
        _finding?.Cancel();
        _findBar.IsVisible = false;
        _findButton.Classes.Set("on", false);
        _view.SetHits([], -1);
        _view.Focus();
    }

    private void ToggleFind()
    {
        if (_findBar.IsVisible)
        {
            CloseFind();
        }
        else
        {
            OpenFind();
        }
    }

    private DiffFindQuery Query() => new(
        _findBox.Text ?? "",
        _matchCase.IsChecked == true,
        _wholeWords.IsChecked == true,
        _regex.IsChecked == true,
        _findSides,
        _findOnlyDifferences,
        _view.IsBinary && _findHexBytes);

    private async Task SearchAsync(bool go)
    {
        _finding?.Cancel();
        var cancel = (_finding = new CancellationTokenSource()).Token;
        var query = Query();
        if (query.Text.Length == 0 || !_findBar.IsVisible)
        {
            _view.SetHits([], -1);
            _findCount.Text = "";
            return;
        }

        List<DiffHit>? hits;
        _findCount.Text = AppStrings.Viewer_Searching;
        try
        {
            if (_view.Text is { } text && _view.LeftLines is { } left && _view.RightLines is { } right)
            {
                hits = await Task.Run(() => DiffFind.Text(text.Rows, left, right, query, cancel), cancel);
            }
            else if (_view.Binary is { } binary)
            {
                var (l, r) = (LeftPath, RightPath);
                hits = await Task.Run(
                    () =>
                    {
                        using var a = new FileStream(l, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
                        using var b = new FileStream(r, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
                        return DiffFind.Bytes(a, b, binary, query, cancel);
                    },
                    cancel);
            }
            else
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _findCount.Text = e.Message;
            return;
        }

        if (cancel.IsCancellationRequested)
        {
            return;
        }

        if (hits is null)
        {
            _view.SetHits([], -1);
            _findCount.Text = AppStrings.Viewer_BadQuery;
            return;
        }

        var current = -1;
        if (hits.Count > 0)
        {
            if (go)
            {
                var row = _view.CursorRow;
                current = hits.FindIndex(h => h.Row > row || (h.Row == row && h.Side >= _view.Side));
                current = current < 0 ? 0 : current;
            }
            else
            {
                current = Math.Clamp(_view.CurrentHit, 0, hits.Count - 1);
            }
        }

        _view.SetHits(hits, current);
        if (go && current >= 0)
        {
            _view.ShowHit(current);
        }

        UpdateFindCount();
    }

    internal void NextHit(int direction)
    {
        var count = _view.Hits.Count;
        if (count == 0)
        {
            return;
        }

        _view.ShowHit(((_view.CurrentHit + direction) % count + count) % count);
        UpdateFindCount();
    }

    private void UpdateFindCount()
    {
        var count = _view.Hits.Count;
        _findCount.Text = count == 0 ? AppStrings.Viewer_NoHits
            : count >= DiffFind.MaxHits ? Loc.Format(AppStrings.Viewer_HitsMany, _view.CurrentHit + 1, count)
            : Loc.Format(AppStrings.Viewer_Hits, _view.CurrentHit + 1, count);
    }

    private MenuFlyout BuildMoreMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Opening += (_, _) => FillMoreMenu(menu);
        return menu;
    }

    private void FillMoreMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        var text = _view.Text is not null;
        var anyDifference = _view.Blocks.Count > 0 && Comparing.IsCompleted;
        if (text)
        {
            Item(AppStrings.FileCompare_CopyAllRight, new KeyGesture(Key.Right, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift), anyDifference, () => CopyAll(toRight: true));
            Item(AppStrings.FileCompare_CopyAllLeft, new KeyGesture(Key.Left, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift), anyDifference, () => CopyAll(toRight: false));
            menu.Items.Add(new Separator());
        }

        var editable = Host?.Edit is not null && !IsModified;
        Item(AppStrings.FileCompare_EditLeft, null, editable, () => Host!.Edit!(LeftPath));
        Item(AppStrings.FileCompare_EditRight, null, editable, () => Host!.Edit!(RightPath));
        Item(AppStrings.FileCompare_ShowLeft, null, Host?.ShowInPanel is not null, () => Host!.ShowInPanel!(LeftPath));
        Item(AppStrings.FileCompare_ShowRight, null, Host?.ShowInPanel is not null, () => Host!.ShowInPanel!(RightPath));
        menu.Items.Add(new Separator());
        if (text)
        {
            Item(AppStrings.FileCompare_CopyLine, new KeyGesture(Key.C, KeyModifiers.Control), _view.CursorLineText() is not null && Host?.Clipboard is not null, CopyLine);
        }

        Item(AppStrings.FileCompare_CopyPaths, null, Host?.Clipboard is not null, () => Host!.Clipboard!.SetText(LeftPath + Environment.NewLine + RightPath));
        menu.Items.Add(new Separator());
        if (Host?.Program?.Invoke() is { } program)
        {
            Item(Loc.Format(AppStrings.FileCompare_CompareIn, program.Name), null, true, () => program.Start(LeftPath, RightPath));
        }

        Item(AppStrings.FileCompare_Options, null, Host?.OpenOptions is not null, () => Host!.OpenOptions!());
        if (IsModified && Host?.Edit is not null)
        {
            menu.Items.Add(new MenuItem { Header = AppStrings.FileCompare_EditWaits, IsEnabled = false, FontSize = 12 });
        }

        void Item(string header, KeyGesture? gesture, bool enabled, Action click)
        {
            var item = new MenuItem { Header = header, InputGesture = gesture, IsEnabled = enabled };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
        }
    }

    internal void CopyLine()
    {
        if (_view.CursorLineText() is { } line)
        {
            Host?.Clipboard?.SetText(line);
        }
    }

    internal void CopyAll(bool toRight)
    {
        if (_view.Text is not { } diff || !Comparing.IsCompleted || diff.Blocks.Count == 0)
        {
            return;
        }

        var (source, target) = toRight ? (_sides[0], _sides[1]) : (_sides[1], _sides[0]);
        target.Undo.Push(([.. target.Lines], [.. target.Copied], target.Pending));
        target.Pending += DiffCopy.Apply(diff.Rows, Enumerable.Range(0, diff.Rows.Count), toRight, source.Lines, target.Lines, target.Copied);
        _undoOrder.Add(toRight ? 1 : 0);
        Comparing = RecompareAsync(0);
    }
}
