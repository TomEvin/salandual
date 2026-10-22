// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/ControlExtensions.cs

namespace Salandual.App;

internal static class ControlExtensions
{
    public static T Also<T>(this T value, Action<T> change)
    {
        change(value);
        return value;
    }

    public static T At<T>(this T control, int column)
        where T : Avalonia.Controls.Control
    {
        Avalonia.Controls.Grid.SetColumn(control, column);
        return control;
    }
}
