// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Theme/TextScale.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace Salandual.App.Theme;

internal static class TextScale
{
    private static bool _installed;
    private static readonly List<TopLevel> _open = [];

    public static double Factor { get; private set; } = 1;

    public static double Of(double size) => size * Factor;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        TextBlock.FontSizeProperty.OverrideMetadata<TextBlock>(new StyledPropertyMetadata<double>(TextBlock.FontSizeProperty.GetDefaultValue(typeof(TextBlock)), coerce: Coerce));
        TextElement.FontSizeProperty.OverrideMetadata<TextPresenter>(new StyledPropertyMetadata<double>(TextElement.FontSizeProperty.GetDefaultValue(typeof(TextPresenter)), coerce: Coerce));

        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => _open.Add(w));
        Window.WindowClosedEvent.AddClassHandler<Window>((w, _) => _open.Remove(w));
    }

    private static double Coerce(AvaloniaObject o, double size) => size * Factor;

    public static bool Set(double factor)
    {
        factor = double.IsFinite(factor) ? Math.Clamp(factor, 1, 2.25) : 1;
        if (Math.Abs(factor - Factor) < 0.001)
        {
            return false;
        }

        Factor = factor;
        foreach (var window in _open.ToList())
        {
            Refresh(window);
        }

        return true;
    }

    internal static void Refresh(Visual root)
    {
        foreach (var v in root.GetSelfAndVisualDescendants())
        {
            if (v is TextBlock or TextPresenter)
            {
                ((AvaloniaObject)v).CoerceValue(TextBlock.FontSizeProperty);
            }
        }
    }
}
