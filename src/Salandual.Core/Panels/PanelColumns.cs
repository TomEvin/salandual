// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/PanelColumns.cs

namespace Salandual.Core.Panels;

public enum PanelColumn
{
    Name,
    Extension,

    Type,
    Size,

    Items,

    Modified,
    Created,
    Accessed,
    Attributes,

    DosName,

    Git,
}

public sealed record ViewColumn(PanelColumn Column, bool Shown = true, int Width = 0);

public sealed record ColumnView
{
    public PanelView View { get; init; } = PanelView.Detailed;

    public string Name { get; init; } = "";

    public IReadOnlyList<ViewColumn> Columns { get; init; } = [];

    public bool SmartName { get; init; } = true;

    public int NameWidth { get; init; } = 260;
}

public static class ColumnViews
{
    public const int MinWidth = 24, MaxWidth = 2000, MinNameWidth = 80;

    public static IReadOnlyList<PanelColumn> AllColumns { get; } = Enum.GetValues<PanelColumn>();

    public static ColumnView Default(PanelView view)
    {
        PanelColumn[] shown = view == PanelView.Types
            ? [PanelColumn.Name, PanelColumn.Type, PanelColumn.Size, PanelColumn.Modified]
            : [PanelColumn.Name, PanelColumn.Extension, PanelColumn.Size, PanelColumn.Modified, PanelColumn.Attributes];
        return new ColumnView
        {
            View = view,
            Columns = [.. shown.Select(c => new ViewColumn(c)), .. AllColumns.Except(shown).Select(c => new ViewColumn(c, Shown: false))],
        };
    }

    public static ColumnView For(IReadOnlyList<ColumnView>? kept, PanelView view)
        => kept?.FirstOrDefault(v => v.View == view) is { } found ? Whole(found) : Default(view == PanelView.Types ? PanelView.Types : PanelView.Detailed) with { View = view };

    public static bool IsMade(IReadOnlyList<ColumnView>? kept, PanelView view)
        => PanelViews.IsOwn(view) && kept?.Any(v => v.View == view && v.Name.Length > 0) == true;

    public static IReadOnlyList<ColumnView> With(IReadOnlyList<ColumnView>? kept, ColumnView setup)
        => [.. (kept ?? []).Where(v => v.View != setup.View), Whole(setup)];

    public static IReadOnlyList<ColumnView> Without(IReadOnlyList<ColumnView>? kept, PanelView view)
        => [.. (kept ?? []).Where(v => v.View != view)];

    public static ColumnView Toggle(ColumnView view, PanelColumn column)
        => column == PanelColumn.Name ? view : view with { Columns = [.. view.Columns.Select(c => c.Column == column ? c with { Shown = !c.Shown } : c)] };

    public static ColumnView SetWidth(ColumnView view, PanelColumn column, int width)
        => column == PanelColumn.Name
            ? view with { SmartName = false, NameWidth = Math.Clamp(width, MinNameWidth, MaxWidth) }
            : view with { Columns = [.. view.Columns.Select(c => c.Column == column ? c with { Width = width <= 0 ? 0 : Math.Clamp(width, MinWidth, MaxWidth) } : c)] };

    public static ColumnView Move(ColumnView view, PanelColumn column, int step)
    {
        var list = view.Columns.ToList();
        var i = list.FindIndex(c => c.Column == column);
        var j = i + step;
        if (column == PanelColumn.Name || i < 0 || j < 1 || j >= list.Count)
        {
            return view;
        }

        (list[i], list[j]) = (list[j], list[i]);
        return view with { Columns = list };
    }

    private static ColumnView Whole(ColumnView view)
    {
        var seen = new HashSet<PanelColumn> { PanelColumn.Name };
        var columns = new List<ViewColumn> { new(PanelColumn.Name) };
        foreach (var c in view.Columns ?? [])
        {
            if (Enum.IsDefined(c.Column) && seen.Add(c.Column))
            {
                columns.Add(c with { Width = c.Width <= 0 ? 0 : Math.Clamp(c.Width, MinWidth, MaxWidth) });
            }
        }

        columns.AddRange(AllColumns.Where(seen.Add).Select(c => new ViewColumn(c, Shown: false)));
        return view with
        {
            Name = (view.Name ?? "").Trim(),
            Columns = columns,
            NameWidth = Math.Clamp(view.NameWidth, MinNameWidth, MaxWidth),
        };
    }
}
