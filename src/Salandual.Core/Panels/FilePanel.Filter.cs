// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/FilePanel.Filter.cs

using Salandual.Core.Listing;
using Salandual.Core.Vfs;

namespace Salandual.Core.Panels;

public sealed record PanelFilter(FileMask Mask, bool Folders = false);

public sealed partial class FilePanel
{
    private FileEntry[] _listed = [];
    private HashSet<string>? _hiddenNames;
    private List<string>? _keptQuickFilters;

    public PanelFilter? Filter { get; private set; }

    public bool HideDotFiles { get; private set; }

    public bool FilterThisFolderOnly { get; set; }

    public bool HiddenNamesStay { get; set; }

    public bool QuickFilterFirst { get; set; }

    public bool QuickFilterMode { get; private set; }

    public bool IsQuickFiltering => QuickFilterMode && QuickSearchText is not null;

    public IReadOnlyList<string> KeptQuickFilters => (IReadOnlyList<string>?)_keptQuickFilters ?? [];

    public int ListedCount => _listed.Length;

    public int ShownCount => Count - (HasUpEntry ? 1 : 0);

    public int HiddenNameCount { get; private set; }

    public bool IsFiltered => Filter is not null || _keptQuickFilters is not null;

    public void SetFilter(PanelFilter? filter)
    {
        Filter = filter;
        Refilter();
    }

    public void SetHideDotFiles(bool hide)
    {
        if (hide != HideDotFiles)
        {
            HideDotFiles = hide;
            Refilter();
        }
    }

    public void ClearFilter()
    {
        Filter = null;
        _keptQuickFilters = null;
        Refilter();
    }

    public bool HideNames(bool selected)
    {
        var names = new List<string>();
        var any = false;
        for (var i = HasUpEntry ? 1 : 0; i < _items.Length; i++)
        {
            any |= _selected[i];
            if (_selected[i] == selected)
            {
                names.Add(_items[i].Name);
            }
        }

        if (!any)
        {
            if (!selected || FocusedEntry is not { } focused || IsUp(Focus))
            {
                return false;
            }

            names = [focused.Name];
        }

        if (names.Count == 0 || Results is not null)
        {
            return false;
        }

        _hiddenNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _hiddenNames.UnionWith(names);
        Refilter(firstIfGone: false);
        return true;
    }

    public void ShowHiddenNames()
    {
        if (_hiddenNames is not null)
        {
            _hiddenNames = null;
            Refilter();
        }
    }

    public void ToggleQuickFilter()
    {
        if (QuickSearchText is null || (Results is not null && !IsFlat))
        {
            return;
        }

        QuickFilterMode = !QuickFilterMode;
        Refilter(firstIfGone: true);
        Changed?.Invoke(this, PanelChange.QuickSearch);
    }

    public static bool QuickFilterMatch(string name, string text) => QuickSearchPattern.IsMatch(name, "<" + text);

    private void LeaveFolderFilters()
    {
        if (FilterThisFolderOnly)
        {
            Filter = null;
        }

        if (!HiddenNamesStay)
        {
            _hiddenNames = null;
        }

        _keptQuickFilters = null;
    }

    private FileEntry[] Visible(FileEntry[] listed)
    {
        var quick = IsQuickFiltering ? "<" + QuickSearchText : null;
        if (!HideDotFiles && _hiddenNames is null && Filter is null && _keptQuickFilters is null && quick is null)
        {
            HiddenNameCount = 0;
            return listed;
        }

        var kept = _keptQuickFilters?.Select(k => "<" + k).ToArray();
        var hiddenNames = 0;
        var flat = IsFlat;
        var shown = new List<FileEntry>(listed.Length);
        foreach (var e in listed)
        {
            var name = flat ? SplitResult(e.Name).Name : e.Name;
            if (HideDotFiles && (e.Name.StartsWith('.') || (flat && e.Name.Contains("\\.", StringComparison.Ordinal))))
            {
                continue;
            }

            if (_hiddenNames is { } hidden && hidden.Contains(e.Name))
            {
                hiddenNames++;
                continue;
            }

            if (Filter is { } filter && (filter.Folders || !e.IsDirectory) && !filter.Mask.IsMatch(name))
            {
                continue;
            }

            if (kept is not null && !kept.All(k => QuickSearchPattern.IsMatch(name, k)))
            {
                continue;
            }

            if (quick is not null && !QuickSearchPattern.IsMatch(name, quick))
            {
                continue;
            }

            shown.Add(e);
        }

        HiddenNameCount = hiddenNames;
        return [.. shown];
    }

    private void Refilter(bool firstIfGone = false)
    {
        if ((Results is not null && !IsFlat) || Path.Length == 0)
        {
            Changed?.Invoke(this, PanelChange.Items);
            return;
        }

        var wasUp = IsUp(Focus);
        var focusName = wasUp ? null : FocusedEntry?.Name;
        var keep = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < _items.Length; i++)
        {
            if (_selected[i])
            {
                keep.Add(_items[i].Name);
            }
        }

        var oldFocus = Focus;
        var visible = IsFlat ? FlatItems(Visible(_listed)) : Visible(_listed);
        _items = HasUpEntry ? [UpEntry, .. visible] : visible;
        _selected = new bool[_items.Length];
        for (var i = HasUpEntry ? 1 : 0; i < _items.Length; i++)
        {
            _selected[i] = keep.Contains(_items[i].Name);
        }

        var found = focusName is null ? -1 : IndexOf(focusName);
        var first = HasUpEntry && _items.Length > 1 ? 1 : 0;
        Focus = wasUp ? 0 : found >= 0 ? found : firstIfGone ? first : Math.Clamp(oldFocus, 0, Math.Max(0, _items.Length - 1));
        _focusScreenRow = null;
        _extendState = null;
        Changed?.Invoke(this, PanelChange.Items | PanelChange.Selection | PanelChange.Focus);
    }
}
