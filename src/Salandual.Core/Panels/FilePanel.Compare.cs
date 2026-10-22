// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/FilePanel.Compare.cs

using Salandual.Core.Compare;

namespace Salandual.Core.Panels;

public sealed partial class FilePanel
{
    private Dictionary<string, DifferenceKind>? _marks;

    public IReadOnlyDictionary<string, DifferenceKind>? CompareMarks => _marks;

    public void ShowComparison(IReadOnlyDictionary<string, DifferenceKind>? marks, IReadOnlyCollection<string>? select)
    {
        _marks = marks is null ? null : new Dictionary<string, DifferenceKind>(marks, StringComparer.OrdinalIgnoreCase);
        var change = PanelChange.Items;
        if (select is not null)
        {
            RememberSelection();
            var names = new HashSet<string>(select, StringComparer.OrdinalIgnoreCase);
            for (var i = HasUpEntry ? 1 : 0; i < _items.Length; i++)
            {
                _selected[i] = names.Contains(_items[i].Name);
            }

            _extendState = null;
            change |= PanelChange.Selection;
        }

        Changed?.Invoke(this, change);
    }

    public void HideComparison()
    {
        if (_marks is not null)
        {
            _marks = null;
            Changed?.Invoke(this, PanelChange.Items);
        }
    }
}
