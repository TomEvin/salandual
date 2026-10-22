// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/WorkingDirectories.cs

namespace Salandual.Core.Panels;

public sealed record WorkingDirectory(string Path, int Side);

public sealed class WorkingDirectories
{
    public const int MaxShown = 20;

    private readonly List<WorkingDirectory> _items = [];

    public IReadOnlyList<WorkingDirectory> All => _items;

    public void Remember(string path, int side)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _items.RemoveAll(w => w.Side == side && string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, new WorkingDirectory(path, side));
        var kept = 0;
        _items.RemoveAll(w => w.Side == side && ++kept > MaxShown);
    }

    public IReadOnlyList<WorkingDirectory> For(int side, bool shared)
        => shared
            ? [.. _items.DistinctBy(w => w.Path, StringComparer.OrdinalIgnoreCase).Take(MaxShown)]
            : [.. _items.Where(w => w.Side == side).Take(MaxShown)];

    public void Clear(int side, bool shared, string leftPath, string rightPath)
    {
        _items.RemoveAll(w => shared || w.Side == side);
        if (shared || side == 1)
        {
            Remember(rightPath, 1);
        }

        if (shared || side == 0)
        {
            Remember(leftPath, 0);
        }
    }

    public void Load(IEnumerable<WorkingDirectory> saved)
    {
        _items.Clear();
        foreach (var w in saved.Reverse())
        {
            if (w.Side is 0 or 1)
            {
                Remember(w.Path, w.Side);
            }
        }
    }
}
