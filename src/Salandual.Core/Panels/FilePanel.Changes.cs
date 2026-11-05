// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Panels/FilePanel.Changes.cs

using Salandual.Core.Vfs;

namespace Salandual.Core.Panels;

public sealed record PanelChanges(string Top, int Staged, int NotStaged);

public sealed record PanelHistory(string Top, string? Of, string? Upstream, int NotPushed, bool More);

public sealed record PanelCommit(string Top, string Sha, string Summary);

public sealed record PanelTree(string Top, string Sha, string Folder);

public sealed record PanelTags(string Top);

public sealed partial class FilePanel
{
    private Func<Task<(IReadOnlyList<FileEntry> Items, Func<PanelResults, PanelResults> Mark)?>>? _gitReader;
    private Func<Task<bool>>? _gitBack;

    public Func<FileEntry, Task<bool>>? GitItemOpened { get; set; }

    public bool IsChanges => Results?.Changes is not null;

    public bool IsHistory => Results?.History is not null;

    public bool IsCommit => Results?.Commit is not null;

    public bool IsGitList => IsChanges || IsHistory || IsCommit || IsTree || IsTags;

    public bool IsTags => Results?.Tags is not null;

    public bool IsTree => Results?.Tree is not null;

    public string? GitListTop => Results?.Changes?.Top ?? Results?.History?.Top ?? Results?.Commit?.Top ?? Results?.Tree?.Top ?? Results?.Tags?.Top;

    public void ShowChanges(string what, IReadOnlyList<FileEntry> items, PanelChanges changes, Func<Task<(IReadOnlyList<FileEntry> Items, PanelChanges Changes)?>> reread)
        => ShowGitList(what, items, r => r with { Changes = changes }, async () => await reread() is { } fresh ? (fresh.Items, r => r with { Changes = fresh.Changes }) : null);

    public void ShowGitList(string what, IReadOnlyList<FileEntry> items, Func<PanelResults, PanelResults> mark, Func<Task<(IReadOnlyList<FileEntry> Items, Func<PanelResults, PanelResults> Mark)?>> reread, string? focus = null, Func<Task<bool>>? back = null)
    {
        var probe = mark(new PanelResults(what, "", null));
        _gitReader = reread;
        _gitBack = back;
        if (Results is { } current && SameList(current, probe))
        {
            Results = mark(current with { What = what });
            Replace(items, focus);
            return;
        }

        ShowResults(probe.Changes?.Top ?? probe.History?.Top ?? probe.Commit?.Top ?? probe.Tree?.Top ?? probe.Tags!.Top, items, what, _ => null, keepOrder: true, mark);
        var found = focus is null ? -1 : IndexOf(focus);
        Focus = found >= 0 ? found : IsTree ? 0 : FirstFile(0);
        Changed?.Invoke(this, PanelChange.Items | PanelChange.Focus);
    }

    private static bool SameList(PanelResults a, PanelResults b)
        => (a.Changes, b.Changes) is ({ } ca, { } cb) ? Same(ca.Top, cb.Top)
            : (a.History, b.History) is ({ } ha, { } hb) ? Same(ha.Top, hb.Top) && string.Equals(ha.Of, hb.Of, StringComparison.OrdinalIgnoreCase)
            : (a.Tree, b.Tree) is ({ } ta, { } tb) ? Same(ta.Top, tb.Top) && ta.Sha == tb.Sha && Same(ta.Folder, tb.Folder)
            : (a.Tags, b.Tags) is ({ } ga, { } gb) ? Same(ga.Top, gb.Top)
            : (a.Commit, b.Commit) is ({ } ma, { } mb) && Same(ma.Top, mb.Top) && ma.Sha == mb.Sha;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public Task<bool> RereadChangesAsync(string? focusName = null) => RereadGitListAsync(focusName);

    public async Task<bool> RereadGitListAsync(string? focusName = null)
    {
        if (!IsGitList || _gitReader is not { } reader)
        {
            return false;
        }

        var version = _loadVersion;
        var before = Results!;
        var read = await reader();
        if (version != _loadVersion || Results is not { } now || !SameList(now, before))
        {
            return false;
        }

        if (read is not { } fresh)
        {
            await LeaveResultsAsync();
            return false;
        }

        Results = fresh.Mark(now);
        Replace(fresh.Items, focusName);
        return true;
    }

    private bool TryGitBack(out Task<bool> going)
    {
        going = Task.FromResult(false);
        if (!IsGitList || _gitBack is not { } back)
        {
            return false;
        }

        _gitBack = null;
        going = back();
        return true;
    }

    private void Replace(IReadOnlyList<FileEntry> items, string? focusName)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _items.Length; i++)
        {
            if (_selected[i] && !IsHeading(i))
            {
                selected.Add(_items[i].Name);
            }
        }

        var focus = focusName ?? (FocusedEntry is { IsDirectory: false } f ? f.Name : null);
        var oldFocus = Focus;
        _items = [.. items];
        _selected = [.. _items.Select(e => !e.IsDirectory && selected.Contains(e.Name))];
        var found = focus is null ? -1 : IndexOf(focus);
        Focus = found >= 0 ? found : FirstFile(Math.Clamp(oldFocus, 0, Math.Max(0, _items.Length - 1)));
        Changed?.Invoke(this, PanelChange.Items | PanelChange.Selection | PanelChange.Focus);
    }

    private int FirstFile(int from)
    {
        for (var i = from; i < _items.Length; i++)
        {
            if (!_items[i].IsDirectory)
            {
                return i;
            }
        }

        for (var i = Math.Min(from, _items.Length) - 1; i >= 0; i--)
        {
            if (!_items[i].IsDirectory)
            {
                return i;
            }
        }

        return 0;
    }
}
