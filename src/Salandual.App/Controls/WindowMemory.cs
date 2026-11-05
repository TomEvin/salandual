// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Controls/WindowMemory.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Salandual.Core.Settings;

namespace Salandual.App.Controls;

internal static class WindowMemory
{
    public static void Remember(Window window, SettingsService settings, string key, Window? centreOn = null)
    {
        if (settings.Current.Windows.For(key) is { } place)
        {
            Restore(window, place, key == WindowSettings.Main, centreOn);
        }
        else if (centreOn is not null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = CentreOn(window, centreOn);
        }

        Track(window, (size, at, maximized) => settings.Update(s => s with
        {
            Windows = s.Windows.With(key, new WindowPlace
            {
                Width = size.Width,
                Height = size.Height,
                Maximized = maximized,
                X = key == WindowSettings.Main ? at.X : null,
                Y = key == WindowSettings.Main ? at.Y : null,
            }),
        }));
    }

    private static void Restore(Window window, WindowPlace place, bool withPlace, Window? centreOn)
    {
        var screens = window.Screens.All;
        var scaling = centreOn?.DesktopScaling ?? window.DesktopScaling;
        var onScreen = withPlace && place.IsOnScreen(screens.Select(s => (s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height)), scaling);

        var screen = onScreen ? window.Screens.ScreenFromPoint(new PixelPoint(place.X!.Value + 60, place.Y!.Value + 12))
            : centreOn is not null ? centreOn.Screens.ScreenFromWindow(centreOn)
            : window.Screens.Primary;
        var width = Math.Max(window.MinWidth, place.Width);
        var height = Math.Max(window.MinHeight, place.Height);
        if (screen is not null)
        {
            width = Math.Min(width, Math.Max(window.MinWidth, screen.WorkingArea.Width / screen.Scaling));
            height = Math.Min(height, Math.Max(window.MinHeight, (screen.WorkingArea.Height / screen.Scaling) - 32));
        }

        window.Width = width;
        window.Height = height;
        if (onScreen)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(place.X!.Value, place.Y!.Value);
        }
        else if (centreOn is not null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = CentreOn(window, centreOn);
        }
        else if (withPlace)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (place.Maximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    internal static PixelPoint CentreOn(Window window, Window owner)
    {
        var scale = owner.DesktopScaling;
        var size = new PixelSize((int)(window.Width * scale), (int)(window.Height * scale));
        var mine = owner.FrameSize ?? owner.ClientSize;
        var x = owner.Position.X + (((int)(mine.Width * scale) - size.Width) / 2);
        var y = owner.Position.Y + (((int)(mine.Height * scale) - size.Height) / 2);
        if (owner.Screens.ScreenFromWindow(owner)?.WorkingArea is { } area)
        {
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - size.Width));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        }

        return new PixelPoint(x, y);
    }

    private static void Track(Window window, Action<Size, PixelPoint, bool> save)
    {
        var size = new Size(window.Width, window.Height);
        var at = window.Position;
        var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            if (window.WindowState == WindowState.Normal && window.IsVisible)
            {
                (size, at) = (window.ClientSize, window.Position);
            }
        };
        window.Opened += (_, _) =>
        {
            if (window.WindowState == WindowState.Normal)
            {
                (size, at) = (window.ClientSize, window.Position);
            }
        };
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == TopLevel.ClientSizeProperty)
            {
                settle.Stop();
                settle.Start();
            }
        };
        window.PositionChanged += (_, _) =>
        {
            settle.Stop();
            settle.Start();
        };
        window.Closing += (_, e) =>
        {
            if (e.Cancel)
            {
                return;
            }

            settle.Stop();
            var maximized = window.WindowState is WindowState.Maximized or WindowState.FullScreen;
            if (!maximized)
            {
                (size, at) = (window.ClientSize, window.Position);
            }

            save(size, at, maximized);
        };
    }
}
