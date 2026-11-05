// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/FilePanel.Columns.cs

using Salandual.Core.Listing;
using Salandual.Core.Vfs;

namespace Salandual.Core.Panels;

public sealed partial class FilePanel
{
    public IReadOnlyList<string> Ancestors()
    {
        var list = new List<string>();
        if (Path.Length == 0 || Results is not null)
        {
            return list;
        }

        for (var p = _provider.GetParent(Path); p is not null && list.Count < 256; p = _provider.GetParent(p))
        {
            list.Add(p);
        }

        list.Reverse();
        return list;
    }

    public string ChildPath(string folder, string name) => _provider.Combine(folder, name);

    public string NameOf(string path) => _provider.GetName(path);

    public Task<FileEntry[]> ListColumnAsync(string path, CancellationToken cancellationToken)
    {
        var sort = Sort;
        var showHidden = ShowHidden;
        var dots = HideDotFiles;
        var filter = FilterThisFolderOnly ? null : Filter;
        var sorter = _sorter;
        return Task.Run(async () =>
        {
            var listing = await _provider.ListAsync(path, cancellationToken).ConfigureAwait(false);
            var visible = EntryFilter.Apply(listing.Entries, showHidden);
            if (dots || filter is not null)
            {
                visible = [.. visible.Where(e => !(dots && e.Name.StartsWith('.')) && (filter is null || (!filter.Folders && e.IsDirectory) || filter.Mask.IsMatch(e.Name)))];
            }

            sorter.Sort(visible, sort);
            return visible;
        }, cancellationToken);
    }

    public object ColumnKey => (Sort, ShowHidden, HideDotFiles, FilterThisFolderOnly ? null : Filter, _sorter);

    public IDisposable? WatchColumn(string path, Action changed)
    {
        if ((_provider.Capabilities & ProviderCapabilities.Watch) == 0)
        {
            return null;
        }

        try
        {
            return _provider.Watch(path, change => _post(changed), _ => { });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private void SetUpEntry(bool has)
    {
        if (has == HasUpEntry || Results is not null || Path.Length == 0 || (has && _provider.GetParent(Path) is null))
        {
            return;
        }

        var focus = Focus;
        HasUpEntry = has;
        if (has)
        {
            _items = [UpEntry, .. _items];
            _selected = [false, .. _selected];
            Focus = focus + 1;
        }
        else
        {
            _items = _items[1..];
            _selected = _selected[1..];
            Focus = Math.Max(0, focus - 1);
        }

        _focusScreenRow = null;
        _extendState = null;
        Changed?.Invoke(this, PanelChange.Items | PanelChange.Selection | PanelChange.Focus);
    }
}
