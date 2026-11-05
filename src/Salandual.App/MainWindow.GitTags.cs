// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.GitTags.cs

using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Git;
using Salandual.Core.Panels;
using Salandual.Core.Resources;
using Salandual.Core.Vfs;

namespace Salandual.App;

public partial class MainWindow
{
    private sealed record TagsShown(string Top, Dictionary<string, GitTag> ByName);

    private readonly Dictionary<PanelHost, TagsShown> _tagLists = [];

    internal async Task ShowTagsAsync(PanelHost host)
    {
        SetActive(Array.IndexOf(_panels, host), focus: false);
        if ((host.Panel.GitListTop ?? GitTopOrSay(host)) is { } top)
        {
            await ShowTagsCoreAsync(host, top, null);
        }
    }

    private async Task<bool> ShowTagsCoreAsync(PanelHost host, string top, string? focus)
    {
        if (await ReadTagsAsync(host, top) is not { } read)
        {
            ShowNotice(AppStrings.Git_ChangesUnreadable);
            return false;
        }

        var mark = (Func<PanelResults, PanelResults>)(r => r with { Tags = new PanelTags(top) });
        host.Panel.ShowGitList(Loc.Format(AppStrings.Git_Tags_What, Path.GetFileName(top)), read, mark, async () => await ReadTagsAsync(host, top) is { } again ? (again, mark) : null, focus);
        UpdateGitListState(host);
        if (read.Count == 0)
        {
            ShowNotice(AppStrings.Git_Tags_None);
        }

        host.View.Focus();
        return true;
    }

    private async Task<IReadOnlyList<FileEntry>?> ReadTagsAsync(PanelHost host, string top)
    {
        var tags = await Task.Run(() => _services.GitReader.Tags(top));
        if (tags is null)
        {
            return null;
        }

        var byName = new Dictionary<string, GitTag>(StringComparer.OrdinalIgnoreCase);
        var items = new List<FileEntry>(tags.Count);
        foreach (var tag in tags)
        {
            if (byName.TryAdd(tag.Name, tag))
            {
                items.Add(new FileEntry(tag.Name, 0, tag.When.UtcDateTime.Ticks, FileAttributes.Normal));
            }
        }

        _tagLists[host] = new TagsShown(top, byName);
        return items;
    }

    private GitTag? FocusedTag(PanelHost host, out string top)
    {
        top = "";
        if (host.Panel.IsTags && _tagLists.TryGetValue(host, out var shown) && host.Panel.FocusedEntry is { } entry && shown.ByName.TryGetValue(entry.Name, out var tag))
        {
            top = shown.Top;
            return tag;
        }

        return null;
    }

    private async Task OpenTagAsync(PanelHost host)
    {
        if (FocusedTag(host, out var top) is { } tag)
        {
            await ShowCommitAsync(host, top, tag.Commit, null, () => ShowTagsCoreAsync(host, top, tag.Name));
        }
    }

    private async Task<bool> BrowseTagAsync(PanelHost host)
    {
        if (FocusedTag(host, out var top) is not { } tag)
        {
            return false;
        }

        await BrowseFromAsync(host, top, tag.Commit, () => ShowTagsCoreAsync(host, top, tag.Name));
        return true;
    }

    private void UpdateTagsState(PanelHost host, TagsShown shown)
    {
        var format = CurrentFormat();
        host.View.Place = new PlaceColumns(AppStrings.Git_Tags_MessageHead, e => shown.ByName.TryGetValue(e.Name, out var t) ? Summary(t.Message) : null)
        {
            Hide = [PanelColumn.Extension, PanelColumn.Attributes, PanelColumn.Git],
            Size = e => shown.ByName.TryGetValue(e.Name, out var t) ? t.By : "",
            SizeHead = AppStrings.Git_Tags_ByHead,
            Share = 0.45,
            Glyph = _ => "Icon.GitCommit",
        };
        host.View.GitStateFor = _ => GitState.None;
        UpdateGitState(host);
        host.GitInfo = null;
        host.InfoInstead = e => shown.ByName.TryGetValue(e.Name, out var t)
            ? string.Join(" · ", new[]
            {
                t.Name,
                Loc.Format(AppStrings.Git_Tags_On, t.Commit.Short),
                t.By,
                format.Date(t.When.LocalDateTime) + " " + format.Time(t.When.LocalDateTime),
                t.IsAnnotated ? "" : AppStrings.Git_Tags_Lightweight,
            }.Where(s => s.Length > 0))
            : null;

        static string Summary(string message) => message.Split('\n')[0].TrimEnd('\r');
    }
}
