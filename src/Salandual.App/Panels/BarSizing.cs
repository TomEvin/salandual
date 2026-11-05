// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Panels/BarSizing.cs

using Avalonia;
using Avalonia.Controls;
using Salandual.Core.Settings;

namespace Salandual.App.Panels;

public static class BarSizing
{
    public static readonly AttachedProperty<BarHeight> BarHeightProperty =
        AvaloniaProperty.RegisterAttached<Control, BarHeight>("BarHeight", typeof(BarSizing), BarHeight.Normal, inherits: true);

    public static BarHeight GetBarHeight(Control control) => control.GetValue(BarHeightProperty);

    public static void SetBarHeight(Control control, BarHeight value) => control.SetValue(BarHeightProperty, value);

    public static (double Strip, double Chip) Strip(BarHeight height, double fontSize, double small) => height switch
    {
        BarHeight.Lower => (Math.Round(fontSize * 2.6), Math.Round(small * 1.9)),
        BarHeight.Taller => (Math.Round(fontSize * 3.7), Math.Round(small * 3.0)),
        _ => (Math.Round(fontSize * 3.2), Math.Round(small * 2.55)),
    };

    public static double CommandBar(BarHeight height, double fontSize) => Math.Round(fontSize * height switch
    {
        BarHeight.Lower => 2.3,
        BarHeight.Taller => 3.5,
        _ => 3.0,
    });
}
