// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/PageLook.cs

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Salandual.Core.Viewer;

namespace Salandual.App.Viewer;

internal static class PageLook
{
    public static PageColors Colors(Control control)
    {
        string Hex(string key, string fallback) => control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? string.Create(CultureInfo.InvariantCulture, $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}") : fallback;
        var l = PageColors.Light;
        var colors = new PageColors(
            Hex("Brush.PanelBackground", l.Background), Hex("Brush.Text", l.Text), Hex("Brush.SecondaryText", l.SecondaryText), Hex("Brush.TertiaryText", l.TertiaryText),
            Hex("Brush.Accent", l.Accent), Hex("Brush.Border", l.Border), Hex("Brush.BorderStrong", l.BorderStrong), Hex("Brush.Hover", l.Surface),
            Hex("Brush.TextSelection", l.Selection), Hex("Brush.ViewerMatch", l.Match), Hex("Brush.ViewerCurrentMatch", l.CurrentMatch));
        return colors with { Danger = Hex("Brush.Danger", l.Danger), Warning = Hex("Brush.Warning", l.Warning), Success = Hex("Brush.Success", l.Success) };
    }

    public static bool IsDark(Control control) => control.ActualThemeVariant == ThemeVariant.Dark;
}
