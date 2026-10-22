// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/ViewerWindow.ContextMenu.cs

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Salandual.App.Resources;
using Salandual.Core.Viewer;

using static Salandual.App.Viewer.ToolbarParts;

namespace Salandual.App.Viewer;

internal sealed partial class ViewerWindow
{
    private const int FindFromSelectionLimit = 200;

    internal ContextMenu? LastContextMenu { get; private set; }

    private void InitContextMenu()
    {
        foreach (var view in new ContentView[] { _text, _hex })
        {
            view.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(view).Properties.IsRightButtonPressed || view.OffsetAtPoint(e.GetPosition(view)) is not { } offset)
                {
                    return;
                }

                view.Focus();
                if (!view.HasSelection || offset < view.SelectionStart || offset >= view.SelectionEnd)
                {
                    view.Select(offset, offset);
                }
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            view.ContextRequested += (_, e) =>
            {
                var menu = BuildContextMenu();
                menu.Open(view);
                LastContextMenu = menu;
                e.Handled = true;
            };
        }
    }

    internal ContextMenu BuildContextMenu()
    {
        var view = Current;
        var selection = view.HasSelection;
        var length = view.SelectionEnd - view.SelectionStart;
        var menu = new ContextMenu();

        MenuItem Item(string header, string? key, Action click, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGesture = key is null ? null : KeyGesture.Parse(key), IsEnabled = enabled };
            item.Click += (_, _) => click();
            return item;
        }

        if (_hexMode)
        {
            var copyAs = new MenuItem { Header = AppStrings.Viewer_Menu_CopyAs, IsEnabled = selection };
            foreach (var (format, label) in new[]
            {
                (CopyFormat.HexBytes, AppStrings.Viewer_CopyAs_Hex),
                (CopyFormat.CArray, AppStrings.Viewer_CopyAs_C),
                (CopyFormat.Base64, AppStrings.Viewer_CopyAs_Base64),
                (CopyFormat.Text, AppStrings.Viewer_CopyAs_Text),
            })
            {
                copyAs.Items.Add(Item(label, format == CopyFormat.HexBytes ? "Ctrl+C" : null, () => _ = CopyAsync(format)));
            }

            menu.Items.Add(copyAs);
            menu.Items.Add(Item(AppStrings.Viewer_Menu_FindBytes, null, FindSelectedBytes, selection && length <= FindFromSelectionLimit));
        }
        else
        {
            menu.Items.Add(Item(AppStrings.Common_Copy, "Ctrl+C", () => _ = CopyAsync(), selection));
            menu.Items.Add(Item(AppStrings.Viewer_Menu_FindSelection, null, FindSelectedText, selection && length <= FindFromSelectionLimit));
        }

        menu.Items.Add(Item(AppStrings.Viewer_Menu_SelectAll, "Ctrl+A", view.SelectAll));
        menu.Items.Add(Item(AppStrings.Viewer_SaveSelection, "Ctrl+S", () => _ = SaveSelectionAsync(), selection));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(_hexMode ? AppStrings.Viewer_Menu_GoToOffset : AppStrings.Viewer_Menu_GoToLine, "Ctrl+G", () => OpenGoTo()));
        menu.Items.Add(Item(AppStrings.Viewer_Menu_SelectTo, null, () => OpenGoTo(extend: true)));
        return menu;
    }

    internal void FindSelectedText()
    {
        if (_document is not { } d || !Current.HasSelection)
        {
            return;
        }

        var text = d.Text(Current.SelectionStart, Current.SelectionEnd);
        _findBar.IsVisible = true;
        SetFindHex(false, search: false);
        _findBox.Text = text.ReplaceLineEndings(" ");
        _findDelay.Stop();
        UpdateToolbar();
        Finding = FindAsync(forward: true, from: Current.SelectionStart + 1);
    }

    internal void FindSelectedBytes()
    {
        if (_document is not { } d || !Current.HasSelection)
        {
            return;
        }

        var bytes = d.Read(Current.SelectionStart, Current.SelectionEnd, FindFromSelectionLimit);
        _findBar.IsVisible = true;
        SetFindHex(true, search: false);
        _findBox.Text = SelectionCopy.Format(bytes, CopyFormat.HexBytes);
        _findDelay.Stop();
        UpdateToolbar();
        Finding = FindAsync(forward: true, from: Current.SelectionStart + 1);
    }
}
