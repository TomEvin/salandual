// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/WindowSettings.cs

namespace Salandual.Core.Settings;

public sealed record WindowSettings
{
    public const string Main = "main";

    public const string Options = "options";

    public const string Differences = "differences";

    public const string FileCompare = "fileCompare";

    public const string HotPaths = "hotPaths";

    public const string FindLog = "findLog";

    public const string GitGraph = "gitGraph";

    public Dictionary<string, WindowPlace> Places { get; set; } = [];

    public WindowPlace? For(string window) => Places.GetValueOrDefault(window);

    public WindowSettings With(string window, WindowPlace place) => this with { Places = new(Places) { [window] = place } };

    internal WindowSettings Normalize() => this with
    {
        Places = (Places ?? []).Where(p => p.Key.Length > 0 && p.Value is not null).ToDictionary(p => p.Key, p => p.Value.Normalize(), StringComparer.Ordinal),
    };
}

public sealed record WindowPlace
{
    public double Width { get; set; }

    public double Height { get; set; }

    public bool Maximized { get; set; }

    public int? X { get; set; }

    public int? Y { get; set; }

    internal WindowPlace Normalize() => this with
    {
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 200, 20_000) : 800,
        Height = double.IsFinite(Height) ? Math.Clamp(Height, 150, 20_000) : 600,
        X = X is null || Y is null ? null : Math.Clamp(X.Value, -100_000, 100_000),
        Y = X is null || Y is null ? null : Math.Clamp(Y.Value, -100_000, 100_000),
    };

    public bool IsOnScreen(IEnumerable<(int X, int Y, int Width, int Height)> screens, double scaling)
    {
        if (X is not { } x || Y is not { } y)
        {
            return false;
        }

        var width = (int)(Width * scaling);
        var bar = (int)(32 * scaling);
        foreach (var s in screens)
        {
            var left = Math.Max(x, s.X);
            var right = Math.Min(x + width, s.X + s.Width);
            var top = Math.Max(y, s.Y);
            var bottom = Math.Min(y + bar, s.Y + s.Height);
            if (right - left >= 120 && bottom - top >= 24)
            {
                return true;
            }
        }

        return false;
    }
}
