// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/FilePanel.Results.cs

using Salandual.Core.Listing;
using Salandual.Core.Vfs;

namespace Salandual.Core.Panels;

public sealed record PanelResults(string What, string ReturnPath, string? ReturnFocus);

public sealed partial class FilePanel
{
    private Func<string, FileEntry?>? _stat;
    private bool _keepOrder;

    public PanelResults? Results { get; private set; }

    public void ShowResults(string root, IReadOnlyList<FileEntry> entries, string what, Func<string, FileEntry?> stat, bool keepOrder = false)
    {
        _loadCts?.Cancel();
        ++_loadVersion;
        _watch?.Dispose();
        _watch = null;
        var back = Results is { } current ? (current.ReturnPath, current.ReturnFocus) : (Path, FocusedEntry is { } f && !IsUp(Focus) ? f.Name : null);
        if (Results is null)
        {
            RememberSelection();
            RememberScreenRow();
            RecordHistory();
        }

        _sizeCts?.Cancel();
        _sizeCts = null;
        _dirSizes.Clear();
        _sizing.Clear();
        _pendingFocus = null;
        _pendingSelect = null;
        _stat = stat;
        _keepOrder = keepOrder;
        Results = new PanelResults(what, back.Item1, back.Item2);
        Path = root;
        HasUpEntry = false;
        _items = [.. entries];
        if (!keepOrder)
        {
            SortResults(_items);
        }

        _selected = new bool[_items.Length];
        Focus = 0;
        _focusScreenRow = null;
        QuickSearchText = null;
        _extendState = null;
        Changed?.Invoke(this, PanelChange.Items | PanelChange.Selection | PanelChange.Focus | PanelChange.QuickSearch);
    }

    public Task<bool> LeaveResultsAsync()
        => Results is { } results ? NavigateAsync(results.ReturnPath, results.ReturnFocus) : Task.FromResult(false);

    public Task<bool> GoToResultAsync()
    {
        if (Results is null || FocusedEntry is not { } entry)
        {
            return Task.FromResult(false);
        }

        var (folder, name) = SplitResult(entry.Name);
        var target = folder.Length == 0 ? Path : Path.Length == 0 ? folder : _provider.Combine(Path, folder);
        if (target.EndsWith(':'))
        {
            target += "\\";
        }

        return NavigateAsync(target, name);
    }

    public static (string Folder, string Name) SplitResult(string name)
    {
        var slash = name.LastIndexOf('\\');
        return slash < 0 ? ("", name) : (name[..slash], name[(slash + 1)..]);
    }

    public void RenameResult(string oldName, string newName)
    {
        var i = IndexOf(oldName);
        if (Results is null || i < 0)
        {
            return;
        }

        var (folder, _) = SplitResult(oldName);
        _items[i] = _items[i] with { Name = folder.Length == 0 ? newName : folder + "\\" + newName };
        Focus = i;
        Changed?.Invoke(this, PanelChange.Items | PanelChange.Focus);
    }

    private async Task<bool> RefreshResultsAsync(string? focusName)
    {
        if (_stat is not { } stat)
        {
            return false;
        }

        var version = ++_loadVersion;
        var path = Path;
        var before = _items;
        var fresh = await Task.Run(() =>
        {
            var list = new List<FileEntry>(before.Length);
            foreach (var item in before)
            {
                if (stat(_provider.Combine(path, item.Name)) is { } now)
                {
                    list.Add(now with { Name = item.Name });
                }
            }

            return list.ToArray();
        });
        if (version != _loadVersion || Results is null)
        {
            return false;
        }

        var selected = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < _items.Length; i++)
        {
            if (_selected[i])
            {
                selected.Add(_items[i].Name);
            }
        }

        var focus = focusName ?? FocusedEntry?.Name;
        var oldFocus = Focus;
        _items = fresh;
        _selected = [.. _items.Select(e => selected.Contains(e.Name))];
        var found = focus is null ? -1 : IndexOf(focus);
        Focus = found >= 0 ? found : Math.Clamp(oldFocus, 0, Math.Max(0, _items.Length - 1));
        Changed?.Invoke(this, PanelChange.Items | PanelChange.Selection | PanelChange.Focus);
        return true;
    }

    private void SortResults(FileEntry[] items)
    {
        var sort = Sort;
        var names = StringComparer.CurrentCultureIgnoreCase;
        Array.Sort(items, (a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory)
            {
                return a.IsDirectory ? -1 : 1;
            }

            var (folderA, nameA) = SplitResult(a.Name);
            var (folderB, nameB) = SplitResult(b.Name);
            var c = sort.Column switch
            {
                SortColumn.Size => a.Size.CompareTo(b.Size),
                SortColumn.Time => a.LastWriteUtcTicks.CompareTo(b.LastWriteUtcTicks),
                SortColumn.Extension => names.Compare(a.Extension.ToString(), b.Extension.ToString()),
                _ => 0,
            };
            if (c == 0)
            {
                c = names.Compare(nameA, nameB);
            }

            if (c == 0)
            {
                c = names.Compare(folderA, folderB);
            }

            return sort.Descending ? -c : c;
        });
    }
}
