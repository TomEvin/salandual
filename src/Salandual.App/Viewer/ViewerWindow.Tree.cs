// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.Tree.cs

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.Core.Editing;
using Salandual.Core.Listing;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Viewer;
using Salandual.Platform.Windows.FileSystem;

using DataFormat = Salandual.Core.Viewer.DataFormat;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal sealed partial class ViewerWindow
{
    private readonly TreeContentView _treeView = new();
    private Grid _treeHost = null!;
    private ToggleButton _treeModeButton = null!;
    private FitRow _treeTools = null!;
    private Button _expand = null!;
    private Button _collapse = null!;
    private Button _copyValue = null!;
    private (string Path, DataTree Tree)? _treeRead;
    private List<DataNode> _treeHits = [];
    private int _treeHit = -1;
    private string? _treeHitsFor;

    private bool _isTree;

    private bool _treeMode;

    private bool _treeChord;

    private bool _treeReading;

    internal bool IsTree => _treeMode;

    internal TreeContentView TreeView => _treeView;

    internal DataTree? Tree => _treeRead is { } read && string.Equals(read.Path, _path, StringComparison.Ordinal) ? read.Tree : null;

    internal TextProblem? TreeProblem { get; private set; }

    private ToggleButton BuildTreeModeButton()
    {
        _treeModeButton = Mode(AppStrings.Viewer_Tree, "F6");
        _treeModeButton.Click += (_, _) => ShowTree();
        return _treeModeButton;
    }

    private FitRow BuildTreeTools()
    {
        _expand = Drop(AppStrings.Viewer_ExpandTip);
        SetDropText(_expand, AppStrings.Viewer_Expand);
        _expand.Click += (_, _) => ExpandMenu().ShowAt(_expand);
        _collapse = Tool(Label(null, AppStrings.Viewer_CollapseAll, null), AppStrings.Viewer_CollapseTip);
        _collapse.Click += (_, _) => _treeView.CollapseAll();
        _treeTools = Row(_expand, _collapse);
        return _treeTools;
    }

    private Button BuildCopyValueButton()
    {
        _copyValue = Tool(Glyph("Icon.CopyText"), AppStrings.Viewer_CopyValueTip);
        _copyValue.Click += (_, _) => CopyTreeValue();
        return _copyValue;
    }

    private Grid BuildTreeHost()
    {
        _treeHost = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _treeHost.Children.Add(_treeView);
        Grid.SetColumn(_treeView.Scroll, 1);
        _treeHost.Children.Add(_treeView.Scroll);
        _treeView.ViewChanged += () =>
        {
            if (_treeMode)
            {
                UpdateStatus();
                _copyValue.IsEnabled = _treeView.Selected is not null && _request.Clipboard is not null;
            }
        };
        _treeView.ContextRequested += (_, e) =>
        {
            var menu = new ContextMenu();
            foreach (var item in TreeItems(withFolding: true))
            {
                menu.Items.Add(item);
            }

            menu.Open(_treeView);
            LastContextMenu = menu;
            e.Handled = true;
        };
        return _treeHost;
    }

    private static DataFormat? TreeFormat(string path) => DataTree.FormatFor(WindowsPath.GetName(path));

    private async Task LoadTree(string path, bool keepPlace)
    {
        if (_document is not { } d || TreeFormat(path) is not { } format)
        {
            return;
        }

        var keep = keepPlace && Tree is { } before && _treeView.Selected is { } selected ? before.PathOf(selected, DataPathStyle.Dollar) : null;
        var name = WindowsPath.GetName(path);
        DataTree tree;
        _treeReading = true;
        UpdateStatus();
        try
        {
            tree = await Task.Run(() => DataTree.Read(d.Text(d.Lines.Start, d.Length), format, JsonText.AllowsComments(name), _closing.Token), _closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _treeReading = false;
        }

        if (!ReferenceEquals(_document, d) || !string.Equals(path, _path, StringComparison.Ordinal))
        {
            return;
        }

        if (tree.Problem is { } problem)
        {
            _treeRead = null;
            TreeProblem = problem;
            _treeMode = false;
            SetMode(false, 0);
            UpdateLayout();
            ShowNote(Loc.Format(AppStrings.Viewer_TreeMistake, FormatName(format), problem.Line.ToString("N0", CultureInfo.CurrentCulture), problem.Column.ToString("N0", CultureInfo.CurrentCulture), problem.Message));
            await GoToAsync(problem.Line.ToString(CultureInfo.InvariantCulture));
            UpdateStatus();
            return;
        }

        TreeProblem = null;
        _treeRead = (path, tree);
        _treeView.Show(tree, Options.TreeOpens == TreeOpening.FirstLevel ? 2 : 1);
        if (keep is not null && tree.Walk().FirstOrDefault(n => tree.PathOf(n, DataPathStyle.Dollar) == keep) is { } again)
        {
            _treeView.Reveal(again);
        }

        ShowTreeMode();
    }

    private void ShowTreeMode()
    {
        if (_findBar.IsVisible)
        {
            CloseFind();
        }

        _treeMode = true;
        _hexMode = false;
        _imageMode = false;
        _mediaMode = false;
        LeavePage();
        _message.IsVisible = false;
        _content.IsVisible = true;
        _content.Content = _treeHost;
        _vertical.IsVisible = false;
        _horizontal.IsVisible = false;
        UpdateToolbar();
        UpdateStatus();
        Dispatcher.UIThread.Post(() => _treeView.Focus(), DispatcherPriority.Input);
    }

    internal void ShowTree()
    {
        if (!_isTree)
        {
            return;
        }

        if (Tree is null)
        {
            _ = LoadAsync(_path, ViewerKind.Tree);
            return;
        }

        HideLimitBar();
        ShowTreeMode();
    }

    private bool TreeKey(Key key, KeyModifiers mods)
    {
        var plain = mods == KeyModifiers.None;
        var ctrl = mods == KeyModifiers.Control;
        var view = _treeView;
        if (_treeChord)
        {
            _treeChord = false;
            if (key is Key.D0 or Key.NumPad0 && ctrl)
            {
                view.CollapseAll();
                return true;
            }

            if (key == Key.J && ctrl)
            {
                view.ExpandTo(int.MaxValue);
                return true;
            }
        }

        if (key == Key.K && ctrl)
        {
            _treeChord = true;
            return true;
        }

        switch (key)
        {
            case Key.Up when plain: view.MoveBy(-1); return true;
            case Key.Down when plain: view.MoveBy(1); return true;
            case Key.PageUp when plain: view.MoveBy(-view.PageRows); return true;
            case Key.PageDown when plain: view.MoveBy(view.PageRows); return true;
            case Key.Home when plain || ctrl: view.SelectRow(0); return true;
            case Key.End when plain || ctrl: view.SelectRow(int.MaxValue); return true;
            case Key.Right when plain: view.Right(); return true;
            case Key.Left when plain: view.Left(); return true;
            case Key.Enter when plain && view.Selected is { CanOpen: true } branch: view.Toggle(branch); return true;
            case Key.Enter when plain || ctrl: _ = ShowInTextAsync(); return true;
            case Key.Add when plain: view.Unfold(); return true;
            case Key.Subtract when plain: view.Fold(); return true;
            case Key.Multiply when plain: view.ExpandBranch(); return true;
            case Key.C when ctrl: CopyTreeValue(); return true;
            case Key.G or Key.A or Key.S when ctrl: return true;
            case Key.F2 or Key.F8 when plain || mods == KeyModifiers.Shift: return true;
            default: return false;
        }
    }

    internal async Task ShowInTextAsync()
    {
        if (_treeView.Selected is not { } node)
        {
            return;
        }

        SetMode(false);
        UpdateLayout();
        await GoToAsync(node.Line.ToString(CultureInfo.InvariantCulture));
    }

    internal string? SelectedPath => Tree is { } tree && _treeView.Selected is { } node ? tree.PathOf(node, Options.TreePath) : null;

    internal void CopyTreeValue() => CopyTree(t => t.CopyValue(_treeView.Selected!));

    internal void CopyTreePath() => CopyTree(t => t.PathOf(_treeView.Selected!, Options.TreePath));

    internal void CopyTreeBranch() => CopyTree(t => t.Branch(_treeView.Selected!));

    private void CopyTree(Func<DataTree, string> text)
    {
        if (_request.Clipboard is { } clipboard && Tree is { } tree && _treeView.Selected is not null)
        {
            ShowStatusNote(clipboard.SetText(text(tree).ReplaceLineEndings("\r\n")) ? AppStrings.Viewer_TreeCopied : AppStrings.Viewer_ClipboardBusy);
        }
    }

    private List<Control> TreeItems(bool withFolding, bool besideToolbar = false)
    {
        var items = new List<Control>();
        var tree = Tree;
        var node = _treeView.Selected;
        var can = tree is not null && node is not null && _request.Clipboard is not null;
        MenuItem Item(string header, string? key, Action click, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGesture = key is null ? null : KeyGesture.Parse(key), IsEnabled = enabled };
            item.Click += (_, _) => click();
            items.Add(item);
            return item;
        }

        void Noted(string header, string note, Action click, bool enabled) => items.Add(MainWindow.EditWithMenuItem(new EditWithItem(header, note, click, enabled)).Also(m => m.ToggleType = MenuItemToggleType.None));

        var path = SelectedPath ?? "";
        Item(path.Length == 0 ? AppStrings.Viewer_CopyPath : Loc.Format(AppStrings.Viewer_CopyPathOf, path.Length > 60 ? string.Concat(path.AsSpan(0, 60), "…") : path), null, CopyTreePath, can && path.Length > 0);
        if (!besideToolbar || !OnToolbar(_copyValue))
        {
            Item(AppStrings.Viewer_CopyTreeValue, "Ctrl+C", CopyTreeValue, can);
        }

        Item(tree?.Format switch
        {
            DataFormat.Yaml => AppStrings.Viewer_CopyBranchYaml,
            DataFormat.Xml => AppStrings.Viewer_CopyBranchXml,
            _ => AppStrings.Viewer_CopyBranchJson,
        }, null, CopyTreeBranch, can && node!.IsBranch);
        Item(AppStrings.Viewer_ShowInText, "Ctrl+Enter", () => _ = ShowInTextAsync(), node is not null);
        if (withFolding)
        {
            items.Add(new Separator());
            Noted(AppStrings.Viewer_ExpandBranch, AppStrings.Viewer_KeyMultiply, _treeView.ExpandBranch, node is { CanOpen: true });
            if (!besideToolbar)
            {
                Noted(AppStrings.Viewer_ExpandAll, "Ctrl+K Ctrl+J", () => _treeView.ExpandTo(int.MaxValue), tree is not null);
                Noted(AppStrings.Viewer_CollapseAll, "Ctrl+K Ctrl+0", _treeView.CollapseAll, tree is not null);
            }
        }

        return items;
    }

    private MenuFlyout ExpandMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var item in ExpandItems())
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    private IEnumerable<Control> ExpandItems()
    {
        foreach (var level in new[] { 1, 2, 3 })
        {
            var item = new MenuItem { Header = Loc.Format(AppStrings.Viewer_ExpandLevel, level) };
            item.Click += (_, _) => _treeView.ExpandTo(level + 1);
            yield return item;
        }

        yield return new Separator();
        var all = new MenuItem { Header = AppStrings.Viewer_ExpandAll };
        all.Click += (_, _) => _treeView.ExpandTo(int.MaxValue);
        yield return all;
    }

    private async Task FindInTree(bool forward, bool restart)
    {
        if (Query is not { } query || Tree is not { } tree)
        {
            ClearTreeHits();
            _hits.Text = "";
            return;
        }

        if (query.ToRegex() is not { } regex)
        {
            ClearTreeHits();
            _hits.Text = AppStrings.Viewer_BadQuery;
            _hits.Res(TextBlock.ForegroundProperty, "Brush.Danger");
            return;
        }

        var key = $"{query.Text}\u0001{query.MatchCase}\u0001{query.WholeWords}\u0001{query.IsRegex}";
        if (restart || _treeHitsFor != key)
        {
            CancelFind();
            var cancel = new CancellationTokenSource();
            _findCancel = cancel;
            _hits.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
            _hits.Text = AppStrings.Viewer_Searching;
            List<DataNode> found;
            try
            {
                found = await Task.Run(() => tree.Find(regex, cancel.Token), cancel.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancel.IsCancellationRequested || !ReferenceEquals(Tree, tree))
            {
                return;
            }

            _treeHits = found;
            _treeHitsFor = key;
            _treeHit = -1;
            _treeView.Matches = [.. found];
            if (found.Count > 0)
            {
                var from = _treeView.Selected?.Offset ?? 0;
                var at = found.FindIndex(n => n.Offset >= from);
                if (at < 0)
                {
                    at = forward ? 0 : found.Count;
                }

                _treeHit = forward ? at - 1 : at;
            }
        }

        if (_treeHits.Count == 0)
        {
            _treeView.InvalidateVisual();
            _hits.Text = AppStrings.Viewer_NoHits;
            _hits.Res(TextBlock.ForegroundProperty, "Brush.Danger");
            return;
        }

        var n = _treeHits.Count;
        _treeHit = ((_treeHit + (forward ? 1 : -1)) % n + n) % n;
        _treeView.Reveal(_treeHits[_treeHit]);
        _hits.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText");
        _hits.Text = Loc.Format(AppStrings.Viewer_Hits, (_treeHit + 1).ToString("N0", CultureInfo.CurrentCulture), n.ToString("N0", CultureInfo.CurrentCulture));
    }

    private void ClearTreeHits()
    {
        _treeHits = [];
        _treeHit = -1;
        _treeHitsFor = null;
        _treeView.Matches = null;
        _treeView.InvalidateVisual();
    }

    private void UpdateTreeStatus()
    {
        var left = new List<Border>();
        var right = new List<Border>();
        if (_treeReading)
        {
            Status(left, AppStrings.Viewer_TreeReading, busy: true);
        }
        else if (Tree is { } tree)
        {
            var path = SelectedPath;
            Status(left, string.IsNullOrEmpty(path) ? AppStrings.Viewer_TreeTop : path, strong: true);
            Status(left, Loc.Format(AppStrings.Viewer_TreeCounts,
                Loc.Plural(tree.Values, AppStrings.Viewer_Values_One, AppStrings.Viewer_Values_Other),
                Loc.Plural(tree.Levels, AppStrings.Viewer_Levels_One, AppStrings.Viewer_Levels_Other)));
            Status(right, Loc.Format(AppStrings.Viewer_TreeValid, FormatName(tree.Format)));
            if (_document is { } d)
            {
                Status(right, d.IsDetected ? Loc.Format(AppStrings.Viewer_Detected, d.Encoding.Name) : d.Encoding.Name);
                Status(right, DisplayFormat.From(_settings.Current.Panels, CultureInfo.CurrentCulture).Size(d.Length));
            }
        }

        ShowStatus(left, right);
    }

    private static string FormatName(DataFormat format) => format switch
    {
        DataFormat.JsonLines => AppStrings.Viewer_FormatJsonLines,
        DataFormat.Yaml => AppStrings.Viewer_FormatYaml,
        DataFormat.Xml => AppStrings.Viewer_FormatXml,
        _ => AppStrings.Viewer_Json,
    };

    private string TreeKindName() => TreeFormat(_request.Path) switch
    {
        DataFormat.Yaml => AppStrings.Viewer_Kind_Yaml,
        DataFormat.Xml => AppStrings.Viewer_Kind_Xml,
        _ => AppStrings.Viewer_Kind_Json,
    };
}
