// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/PanelView.cs

namespace Salandual.Core.Panels;

public enum PanelView
{
    Columns = 1,

    Brief = 2,

    Detailed = 3,

    Icons = 4,

    Thumbnails = 5,

    Tiles = 6,

    Types = 7,

    Own1 = 8,

    Own2 = 9,

    Own3 = 10,
}

public static class PanelViews
{
    public static IReadOnlyList<PanelView> All { get; } = [PanelView.Columns, PanelView.Brief, PanelView.Detailed, PanelView.Icons, PanelView.Thumbnails, PanelView.Tiles, PanelView.Types];

    public static IReadOnlyList<PanelView> Own { get; } = [PanelView.Own1, PanelView.Own2, PanelView.Own3];

    public static bool HasColumns(PanelView view) => view is PanelView.Detailed or PanelView.Types or PanelView.Own1 or PanelView.Own2 or PanelView.Own3;

    public static bool IsOwn(PanelView view) => view is PanelView.Own1 or PanelView.Own2 or PanelView.Own3;

    public static PanelView Next(PanelView view, int step)
    {
        var i = Math.Max(0, IndexOf(view));
        var n = All.Count;
        return All[(((i + step) % n) + n) % n];
    }

    public static PanelView? FromCommand(string id)
        => id.Length == "View.Mode".Length + 1 && id.StartsWith("View.Mode", StringComparison.Ordinal) && id[^1] is (>= '1' and <= '9') or '0'
            ? id[^1] == '0' ? PanelView.Own3 : (PanelView)(id[^1] - '0')
            : null;

    public static string Command(PanelView view) => "View.Mode" + (view == PanelView.Own3 ? "0" : ((int)view).ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static PanelView Valid(PanelView view, PanelView fallback = PanelView.Detailed) => Enum.IsDefined(view) ? view : fallback;

    private static int IndexOf(PanelView view)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i] == view)
            {
                return i;
            }
        }

        return -1;
    }
}
