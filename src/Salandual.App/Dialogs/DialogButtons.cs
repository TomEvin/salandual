// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Dialogs/DialogButtons.cs

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Salandual.App.Dialogs;

internal static class DialogButtons
{
    public static void ArrowsAcross(Window window, Func<IReadOnlyList<Button>> order)
        => window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            var step = e.Key switch { Key.Left or Key.Up => -1, Key.Right or Key.Down => 1, _ => 0 };
            var buttons = order().Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled).ToList();
            if (step == 0 || e.KeyModifiers != KeyModifiers.None || buttons.Count == 0)
            {
                return;
            }

            var focused = window.FocusManager?.GetFocusedElement();
            var at = buttons.FindIndex(b => ReferenceEquals(b, focused));
            if (at < 0 && focused is not null && !ReferenceEquals(focused, window))
            {
                return;
            }

            var next = at < 0 ? Math.Max(buttons.FindIndex(b => b.IsDefault), 0) : Math.Clamp(at + step, 0, buttons.Count - 1);
            buttons[next].Focus(NavigationMethod.Directional);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

    public static void FocusShown(Control control) => control.Focus(NavigationMethod.Tab);
}
