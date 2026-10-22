// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Editor/EditorWindow.ContextMenu.cs

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Salandual.App.Resources;
using Salandual.App.Viewer;

namespace Salandual.App.Editor;

internal sealed partial class EditorWindow
{
    private readonly Border _previewMenuAnchor = new() { Width = 1, Height = 1, IsHitTestVisible = false };

    internal MenuFlyout? LastContextMenu { get; private set; }

    private void BuildTextMenu()
    {
        _editor.ContextRequested += (_, e) =>
        {
            if (e.TryGetPosition(_editor, out var at) && _editor.GetPositionFromPoint(at) is { } position)
            {
                var offset = _editor.Document.GetOffset(position.Location);
                if (_editor.SelectionLength == 0 || offset < _editor.SelectionStart || offset > _editor.SelectionStart + _editor.SelectionLength)
                {
                    _editor.Select(offset, 0);
                    _editor.CaretOffset = offset;
                }
            }

            e.Handled = true;
            ShowTextMenu();
        };
    }

    internal MenuFlyout ShowTextMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.Pointer };
        var selection = _editor.SelectionLength > 0;
        var writable = !_editor.IsReadOnly;
        Add(menu, AppStrings.Common_Undo, "Ctrl+Z", () => _editor.Undo(), _editor.CanUndo);
        Add(menu, AppStrings.Editor_Redo, "Ctrl+Y", () => _editor.Redo(), _editor.CanRedo);
        menu.Items.Add(new Separator());
        Add(menu, AppStrings.Editor_Cut, "Ctrl+X", () => _editor.Cut(), selection && writable);
        Add(menu, AppStrings.Common_Copy, "Ctrl+C", () => _editor.Copy(), selection);
        Add(menu, AppStrings.Editor_Paste, "Ctrl+V", () => _editor.Paste(), writable);
        Add(menu, AppStrings.Editor_Delete, "Delete", () => _editor.Delete(), selection && writable);
        menu.Items.Add(new Separator());
        Add(menu, AppStrings.Editor_SelectAll, "Ctrl+A", () => _editor.SelectAll());
        if (CommentStyleNow is not null)
        {
            menu.Items.Add(MainWindow.EditWithMenuItem(new EditWithItem(AppStrings.Editor_CommentLine, "Ctrl+/", CommentLine, writable)).Also(m => m.ToggleType = MenuItemToggleType.None));
        }

        menu.ShowAt(_editor, showAtPointer: true);
        LastContextMenu = menu;
        return menu;
    }

    private void ShowPreviewMenu(JsonElement root)
    {
        if (_page is not { } page)
        {
            return;
        }

        double Number(string name) => root.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : 0;
        var href = root.TryGetProperty("href", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
        var selection = root.TryGetProperty("selection", out var s) && s.ValueKind == JsonValueKind.True;
        if (_previewMenuAnchor.Parent is null)
        {
            _pageCell.Children.Add(new Canvas { IsHitTestVisible = false, Children = { _previewMenuAnchor } });
        }

        Canvas.SetLeft(_previewMenuAnchor, Number("x"));
        Canvas.SetTop(_previewMenuAnchor, Number("y"));
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        Add(menu, AppStrings.Common_Copy, "Ctrl+C", () => _ = CopyPreviewSelectionAsync(), selection && _request.Clipboard is not null);
        Add(menu, AppStrings.Viewer_Menu_SelectAll, "Ctrl+A", () => _ = page.RunAsync(PageScript.Call("selectAll")));
        if (href is not null && Uri.TryCreate(href, UriKind.Absolute, out var link))
        {
            menu.Items.Add(new Separator());
            Add(menu, AppStrings.Viewer_OpenLink, null, () => OnPreviewLink(link));
            Add(menu, AppStrings.Viewer_CopyLink, null, () => _request.Clipboard?.SetText(link.IsFile ? link.LocalPath : link.AbsoluteUri), _request.Clipboard is not null);
        }

        menu.ShowAt(_previewMenuAnchor);
        LastContextMenu = menu;
    }

    private async Task CopyPreviewSelectionAsync()
    {
        if (_page is { } page && _request.Clipboard is { } clipboard && await page.RunAsync(PageScript.Call("selection")) is { Length: > 0 } text)
        {
            clipboard.SetText(text);
        }
    }

    private static void Add(MenuFlyout menu, string header, string? key, Action click, bool enabled = true)
    {
        var item = new MenuItem { Header = header, InputGesture = key is null ? null : KeyGesture.Parse(key), IsEnabled = enabled };
        item.Click += (_, _) => click();
        menu.Items.Add(item);
    }
}
