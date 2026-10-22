// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Find/FindWindow.Menu.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Salandual.App.Options;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Listing;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Search;

namespace Salandual.App.Find;

internal sealed partial class FindWindow
{
    private readonly List<SearchHit> _renamed = [];

    internal ContextMenu? ResultMenu { get; private set; }

    private void OnListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        Point at;
        if (e.TryGetPosition(_list, out var point))
        {
            at = point;
            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: SearchHit hit } && !Selected.Contains(hit))
            {
                _list.SelectedItems?.Clear();
                _list.SelectedItem = hit;
            }
        }
        else
        {
            at = _list.SelectedIndex >= 0 && _list.ContainerFromIndex(_list.SelectedIndex) is { } row && row.TranslatePoint(new Point(24, row.Bounds.Height), _list) is { } p ? p : new Point(40, 40);
        }

        ShowResultMenu(at);
    }

    internal ContextMenu? ShowResultMenu(Point at)
    {
        var hits = Selected;
        if (hits.Count == 0)
        {
            return null;
        }

        ResultMenu?.Close();
        var menu = new ContextMenu
        {
            PlacementTarget = _list,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(at, new Size(1, 1)),
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
        };
        var items = menu.Items;
        var single = hits.Count == 1 ? hits[0] : null;
        var local = hits.Where(h => h.Archive is null).ToList();
        var allLocal = local.Count == hits.Count;
        var fileHere = single is { IsDirectory: false, Archive: null };

        MenuItem Cmd(string header, string? icon, string? key, Action run, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled, Icon = icon is null ? null : AppIcons.Create(icon), InputGesture = key is null ? null : KeyGesture.Parse(key) };
            item.Click += (_, _) => run();
            items.Add(item);
            return item;
        }

        void AddSeparator()
        {
            if (items.Count > 0 && items[^1] is not Separator)
            {
                items.Add(new Separator());
            }
        }

        if (single is null)
        {
            var bytes = hits.Where(h => !h.IsDirectory).Sum(h => h.Size);
            items.Add(new MenuItem { Header = new TextBlock { Text = Loc.Format(AppStrings.QuickMenu_SelectedHead, Loc.Plural(hits.Count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other), _format.Size(bytes)), FontSize = 12 }.Res(TextBlock.ForegroundProperty, "Brush.SecondaryText"), IsEnabled = false });
            items.Add(new Separator());
            Cmd(Loc.Format(AppStrings.QuickMenu_OpenAll, hits.Count), "Navigation.Execute", null, OpenAll, local.Any(h => !h.IsDirectory));
        }
        else
        {
            Cmd(AppStrings.QuickMenu_Open, "Navigation.Execute", "Enter", () => OpenItem(single), single.Archive is null).FontWeight = FontWeight.SemiBold;
            if (fileHere)
            {
                Cmd(AppStrings.QuickMenu_OpenWith, "Icon.OpenWith", null, () => _request.OpenWith(single.FullPath));
            }
        }

        Cmd(AppStrings.QuickMenu_JumpToFile, "Icon.Path", "Ctrl+J", () => ShowInPanel(Focused));
        Cmd(AppStrings.QuickMenu_View, "File.View", "F3", () => ViewItem(single), fileHere);
        Cmd(AppStrings.QuickMenu_Edit, "File.Edit", "F4", () => EditItem(single), fileHere);
        AddSeparator();
        Cmd(AppStrings.QuickMenu_Cut, "Clipboard.Cut", "Ctrl+X", () => CopyFiles(cut: true), allLocal);
        Cmd(AppStrings.QuickMenu_Copy, "Clipboard.Copy", "Ctrl+C", () => CopyFiles(cut: false), allLocal);
        AddSeparator();
        var what = Loc.Plural(hits.Count, AppStrings.Count_Items_One, AppStrings.Count_Items_Other);
        Cmd(single is null ? Loc.Format(AppStrings.QuickMenu_CopyItemsToOther, what) : AppStrings.QuickMenu_CopyToOther, "File.Copy", "F5", () => CopyOrMove(OperationKind.Copy), allLocal);
        Cmd(single is null ? Loc.Format(AppStrings.QuickMenu_MoveItemsToOther, what) : AppStrings.QuickMenu_MoveToOther, "File.Move", "F6", () => CopyOrMove(OperationKind.Move), allLocal);
        if (single is not null)
        {
            Cmd(AppStrings.QuickMenu_Rename, "File.QuickRename", "F2", () => RenameItem(single), allLocal);
        }

        Cmd(single is null ? Loc.Format(AppStrings.QuickMenu_DeleteItems, what) : AppStrings.QuickMenu_Delete, "File.Delete", "F8", () => DeleteSelected(inverted: false), allLocal);
        AddSeparator();
        var paths = Cmd(single is null ? AppStrings.QuickMenu_CopyPaths : AppStrings.QuickMenu_CopyPath, "Icon.Path", null, () => { });
        foreach (var (header, key, part) in new (string, string, Func<SearchHit, string>)[]
        {
            (AppStrings.QuickMenu_FullName, "Alt+Insert", h => h.FullPath),
            (AppStrings.QuickMenu_Name, "Alt+Shift+Insert", h => h.Name),
            (AppStrings.QuickMenu_FolderPath, "Ctrl+Alt+Insert", h => h.Folder),
        })
        {
            var item = new MenuItem { Header = header, InputGesture = KeyGesture.Parse(key) };
            item.Click += (_, _) => CopyText(part);
            paths.Items.Add(item);
        }

        Cmd(AppStrings.Find_KeyHide, null, "Ctrl+H", HideSelected);
        AddSeparator();

        var oneFolder = allLocal && hits.Select(h => h.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        var windows = new MenuItem { Header = AppStrings.QuickMenu_WindowsCommands, Icon = AppIcons.Create("Icon.Windows"), IsEnabled = oneFolder };
        if (oneFolder)
        {
            _request.WindowsCommands(windows, menu, hits[0].Folder, [.. hits.Select(h => h.Name)], this);
        }

        items.Add(windows);
        Cmd(AppStrings.QuickMenu_Properties, "File.Properties", "Alt+Enter", () => ShowProperties(windows: false), allLocal);

        ResultMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(ResultMenu, menu))
            {
                ResultMenu = null;
            }
        };
        menu.Open(_list);
        return menu;
    }

    private void OpenAll()
    {
        foreach (var hit in Selected.Where(h => !h.IsDirectory && h.Archive is null))
        {
            _request.OpenFile(hit.FullPath);
        }
    }

    internal void CopyOrMove(OperationKind kind)
    {
        var hits = Selected.Where(h => h.Archive is null).ToList();
        if (hits.Count > 0)
        {
            _ = AfterJobAsync(_request.CopyOrMove(kind, hits, this), hits, kind == OperationKind.Move);
        }
    }

    internal void DeleteSelected(bool inverted)
    {
        var hits = Selected.Where(h => h.Archive is null).ToList();
        if (hits.Count > 0)
        {
            _ = AfterJobAsync(_request.Delete(hits, inverted, this), hits, drop: true);
        }
    }

    private async Task AfterJobAsync(Task<OperationJob?> start, IReadOnlyList<SearchHit> hits, bool drop)
    {
        var job = await start;
        if (job is null || !drop)
        {
            return;
        }

        await job.Completion;
        DropMissing(hits);
    }

    internal void RenameItem(SearchHit? hit)
    {
        if (hit is null || hit.Archive is not null)
        {
            return;
        }

        _renamed.Add(hit);
        _request.Rename(hit);
    }

    private void DropRenamed()
    {
        if (_renamed.Count > 0)
        {
            DropMissing([.. _renamed]);
            _renamed.Clear();
        }
    }

    internal void DropMissing(IReadOnlyList<SearchHit> hits)
    {
        var gone = hits.Where(h => h.Archive is null && !_request.Exists(h.FullPath)).ToHashSet();
        if (gone.Count == 0)
        {
            return;
        }

        var index = _list.SelectedIndex;
        _found.RemoveAll(gone.Contains);
        foreach (var hit in gone)
        {
            _shown.Remove(hit);
        }

        _list.SelectedIndex = Math.Min(Math.Max(index, 0), _shown.Count - 1);
        UpdateHeader();
        if (_duplicates)
        {
            Regroup();
        }
    }
}
