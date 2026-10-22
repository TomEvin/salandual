// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Focus.cs

using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Salandual.App;

public partial class MainWindow
{
    private Control? _lastFocus;

    private void InitFocusKeeping()
    {
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is Control c && IsContent(c))
            {
                _lastFocus = c;
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        Activated += (_, _) => Dispatcher.UIThread.Post(RestoreFocus, DispatcherPriority.Input);
    }

    internal Control? LastFocus => _lastFocus;

    internal void RestoreFocus()
    {
        if (!IsActive || FocusManager?.GetFocusedElement() is Control { IsEffectivelyVisible: true } now && IsContent(now))
        {
            return;
        }

        if (_lastFocus is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } last && last.IsAttachedToVisualTree() && last.Focus())
        {
            return;
        }

        ActivePanel.View.Focus();
    }

    private static bool IsContent(Control c)
        => c is not (MenuItem or Window) && c.TemplatedParent is not WindowDrawnDecorations;
}
