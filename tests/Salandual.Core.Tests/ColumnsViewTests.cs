// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/ColumnsViewTests.cs

using System.Collections.Concurrent;
using System.Globalization;
using Salandual.Core.Listing;
using Salandual.Core.Panels;

namespace Salandual.Core.Tests;

public sealed class ColumnsViewTests
{
    private readonly FakeFileSystem _fs = new FakeFileSystem()
        .Dir(@"C:\", "Work/")
        .Dir(@"C:\Work", "Projects/", "todo.txt:3")
        .Dir(@"C:\Work\Projects", "Clients/", "Website/", ".git/", "README.md:4")
        .Dir(@"C:\Work\Projects\Website", "docs/", "index.html:9", "style.css:2");

    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly FilePanel _panel;

    public ColumnsViewTests()
        => _panel = new FilePanel(_fs, new EntrySorter(CultureInfo.GetCultureInfo("en-US"), numericSort: true), _posted.Enqueue);

    private async Task<T> Pump<T>(Task<T> task)
    {
        while (!task.IsCompleted)
        {
            while (_posted.TryDequeue(out var a))
            {
                a();
            }

            await Task.Delay(2, TestContext.Current.CancellationToken);
        }

        return await task;
    }

    [Fact]
    public async Task Columns_HasNoUpRow_TheFocusStays_AndTheOtherViewsGetItBack()
    {
        Assert.True(await Pump(_panel.NavigateAsync(@"C:\Work\Projects", "Website")));
        Assert.Equal("..", _panel.Items[0].Name);
        _panel.Toggle(_panel.Focus);

        _panel.SetView(PanelView.Columns);
        Assert.False(_panel.HasUpEntry);
        Assert.DoesNotContain(_panel.Items, e => e.Name == "..");
        Assert.Equal("Website", _panel.FocusedEntry!.Value.Name);
        Assert.True(_panel.IsSelected(_panel.Focus));

        Assert.True(await Pump(_panel.NavigateAsync(@"C:\Work\Projects\Website")));
        Assert.Equal("docs", _panel.FocusedEntry!.Value.Name);
        await Pump(_panel.GoParentAsync().ContinueWith(_ => true, TaskScheduler.Default));
        Assert.Equal("Website", _panel.FocusedEntry!.Value.Name);
        Assert.False(_panel.HasUpEntry);

        _panel.SetView(PanelView.Detailed);
        Assert.Equal("..", _panel.Items[0].Name);
        Assert.Equal("Website", _panel.FocusedEntry!.Value.Name);
    }

    [Fact]
    public async Task TheColumnsAbove_AndAColumnsListing_AsThePanelListsItsOwn()
    {
        Assert.True(await Pump(_panel.NavigateAsync(@"C:\Work\Projects\Website")));
        Assert.Equal([@"C:\", @"C:\Work", @"C:\Work\Projects"], _panel.Ancestors());

        _panel.SetHideDotFiles(true);
        var items = await _panel.ListColumnAsync(@"C:\Work\Projects", TestContext.Current.CancellationToken);
        Assert.Equal(["Clients", "Website", "README.md"], items.Select(e => e.Name));
        _panel.SetHideDotFiles(false);
        items = await _panel.ListColumnAsync(@"C:\Work\Projects", TestContext.Current.CancellationToken);
        Assert.Contains(items, e => e.Name == ".git");

        Assert.Equal(PanelView.Columns, PanelViews.FromCommand("View.Mode1"));
        Assert.Equal("View.Mode1", PanelViews.Command(PanelView.Columns));
        Assert.True(_panel.Execute("View.Mode1", 20));
        Assert.Equal(PanelView.Columns, _panel.View);
    }
}
