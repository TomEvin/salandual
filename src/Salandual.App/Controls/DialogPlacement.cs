// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/DialogPlacement.cs

using Avalonia;
using Avalonia.Controls;

namespace Salandual.App.Controls;

internal static class DialogPlacement
{
    public static void KeepCentered(Window window)
    {
        PixelPoint? placed = null;
        var moved = false;

        void Center()
        {
            if (moved || window.WindowStartupLocation != WindowStartupLocation.CenterOwner || window.Owner is not Window owner)
            {
                return;
            }

            var at = CenterOn(window, owner);
            if (at != window.Position)
            {
                placed = at;
                window.Position = at;
            }
        }

        window.Opened += (_, _) => Center();
        window.SizeChanged += (_, _) =>
        {
            if (window.IsVisible)
            {
                Center();
            }
        };
        window.PositionChanged += (_, e) =>
        {
            if (placed is { } p && e.Point != p)
            {
                moved = true;
            }
        };
    }

    internal static PixelPoint CenterOn(Window window, Window owner)
    {
        var ownerRect = new PixelRect(owner.Position, PixelSize.FromSize(owner.FrameSize ?? owner.ClientSize, owner.DesktopScaling));
        var size = PixelSize.FromSize(window.FrameSize ?? window.ClientSize, window.DesktopScaling);
        var x = ownerRect.Center.X - (size.Width / 2);
        var y = ownerRect.Center.Y - (size.Height / 2);
        if (owner.Screens.ScreenFromWindow(owner)?.WorkingArea is { } area)
        {
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - size.Width));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        }

        return new PixelPoint(x, y);
    }
}
