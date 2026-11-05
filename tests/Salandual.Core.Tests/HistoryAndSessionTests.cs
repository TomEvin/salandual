// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/HistoryAndSessionTests.cs

using System.Globalization;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class HistoryAndSessionTests : IDisposable
{
    private readonly FakeFileSystem _fs = new FakeFileSystem()
        .Dir(@"C:\", "A/", "B/", "C/")
        .Dir(@"C:\A", "a1.txt:1", "a2.txt:1", "Sub/")
        .Dir(@"C:\B", "b.txt:1")
        .Dir(@"C:\C", "c.txt:1");

    private readonly FilePanel _panel;
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-session-").FullName;

    public HistoryAndSessionTests() => _panel = new FilePanel(_fs, new EntrySorter(CultureInfo.GetCultureInfo("en-US"), numericSort: true), a => a());

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Focus(string name) => _panel.SetFocus(_panel.Items.ToList().FindIndex(e => e.Name == name));

    [Fact]
    public async Task BackAndForward_ReturnWithTheFocusedItem()
    {
        await _panel.NavigateAsync(@"C:\A");
        Focus("a2.txt");
        await _panel.NavigateAsync(@"C:\B");
        await _panel.NavigateAsync(@"C:\C");
        Assert.True(_panel.CanGoBack);
        Assert.False(_panel.CanGoForward);

        Assert.True(await _panel.GoBackAsync());
        Assert.Equal(@"C:\B", _panel.Path);
        Assert.True(await _panel.GoBackAsync());
        Assert.Equal(@"C:\A", _panel.Path);
        Assert.Equal("a2.txt", _panel.FocusedEntry?.Name);
        Assert.False(_panel.CanGoBack);
        Assert.False(await _panel.GoBackAsync());

        Assert.True(await _panel.GoForwardAsync());
        Assert.Equal(@"C:\B", _panel.Path);
        Assert.True(_panel.Execute("History.Forward", 10));
        for (var i = 0; i < 200 && _panel.Path != @"C:\C"; i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.Equal(@"C:\C", _panel.Path);
        Assert.False(_panel.CanGoForward);
    }

    [Fact]
    public async Task NewFolder_AfterBack_ClearsForward()
    {
        await _panel.NavigateAsync(@"C:\A");
        await _panel.NavigateAsync(@"C:\B");
        await _panel.GoBackAsync();
        Assert.True(_panel.CanGoForward);
        await _panel.NavigateAsync(@"C:\C");
        Assert.False(_panel.CanGoForward);
        Assert.Equal([@"C:\A"], _panel.BackHistory.Select(h => h.Path));
    }

    [Fact]
    public async Task Back_SkipsFoldersThatAreGone()
    {
        await _panel.NavigateAsync(@"C:\A");
        await _panel.NavigateAsync(@"C:\B");
        await _panel.NavigateAsync(@"C:\C");
        _fs.DeleteDir(@"C:\B");
        var errors = 0;
        _panel.Error += (_, _) => errors++;

        Assert.True(await _panel.GoBackAsync());
        Assert.Equal(@"C:\A", _panel.Path);
        Assert.Equal(0, errors);
    }

    [Fact]
    public async Task Refresh_IsNotHistory_AndHistoryIsCapped()
    {
        await _panel.NavigateAsync(@"C:\A");
        await _panel.RefreshAsync();
        Assert.False(_panel.CanGoBack);

        for (var i = 0; i < FilePanel.MaxHistory + 10; i++)
        {
            await _panel.NavigateAsync(i % 2 == 0 ? @"C:\B" : @"C:\C");
        }

        Assert.Equal(FilePanel.MaxHistory, _panel.BackHistory.Count);
    }

    [Fact]
    public async Task StepsKept_FromOptions_AndASmallerNumberForgetsTheOldest()
    {
        _panel.HistoryLimit = 12;
        for (var i = 0; i < 30; i++)
        {
            await _panel.NavigateAsync(i % 2 == 0 ? @"C:\B" : @"C:\C");
        }

        Assert.Equal(12, _panel.BackHistory.Count);
        var newest = _panel.BackHistory[^1];
        _panel.HistoryLimit = 3;
        Assert.Equal(3, _panel.BackHistory.Count);
        Assert.Equal(newest, _panel.BackHistory[^1]);
    }

    [Fact]
    public async Task ManyStepsAtOnce_TheFoldersPassedOver_GoToForward_InOrder()
    {
        var changes = 0;
        _panel.HistoryChanged += (_, _) => changes++;
        foreach (var path in new[] { @"C:\", @"C:\A", @"C:\B", @"C:\C" })
        {
            await _panel.NavigateAsync(path);
        }

        Assert.Equal([@"C:\", @"C:\A", @"C:\B"], _panel.BackHistory.Select(h => h.Path));
        Assert.True(changes >= 3);

        Assert.True(await _panel.GoBackAsync(3));
        Assert.Equal(@"C:\", _panel.Path);
        Assert.False(_panel.CanGoBack);
        Assert.Equal([@"C:\C", @"C:\B", @"C:\A"], _panel.ForwardHistory.Select(h => h.Path));
        Assert.True(await _panel.GoForwardAsync(2));
        Assert.Equal(@"C:\B", _panel.Path);
        Assert.Equal([@"C:\", @"C:\A"], _panel.BackHistory.Select(h => h.Path));
        Assert.Equal([@"C:\C"], _panel.ForwardHistory.Select(h => h.Path));

        Assert.True(await _panel.GoBackAsync(99));
        Assert.Equal(@"C:\", _panel.Path);
        _panel.ClearHistory();
        Assert.False(_panel.CanGoBack || _panel.CanGoForward);
    }

    [Fact]
    public async Task Steps_AreNumbered_AcrossPanels_InTheOrderTaken()
    {
        var other = new FilePanel(_fs, new EntrySorter(CultureInfo.GetCultureInfo("en-US"), numericSort: true), a => a());
        await _panel.NavigateAsync(@"C:\A");
        await other.NavigateAsync(@"C:\B");
        await _panel.NavigateAsync(@"C:\C");
        await other.NavigateAsync(@"C:\C");
        Assert.True(_panel.BackHistory[0].Order < other.BackHistory[0].Order);
    }

    [Fact]
    public void Session_RoundTrip_WithDriveFolders()
    {
        var store = SessionStore.InDirectory(_dir);
        Assert.Same(SessionState.Empty, store.Load());

        var drives = new Dictionary<string, string> { [@"C:\"] = @"C:\Users\user\Pictures\Nyaralás 2026", [@"D:\"] = @"D:\Backup=old" };
        store.Save(new SessionState(@"C:\A", @"\\nas\photos", 1, drives));
        var back = store.Load();

        Assert.Equal(@"C:\A", back.Left);
        Assert.Equal(@"\\nas\photos", back.Right);
        Assert.Equal(1, back.Active);
        Assert.Equal(drives, back.LastFolderPerDrive);
        Assert.Equal(@"D:\Backup=old", back.LastFolderPerDrive[@"d:\"]);
    }

    [Fact]
    public void Session_BrokenLines_AreSkipped()
    {
        File.WriteAllText(Path.Combine(_dir, SessionStore.FileName), "left\tC:\\X\nnonsense\nright\t\n\tvalue\nactive\t7\ndrive:\tC:\\\n");
        var s = SessionStore.InDirectory(_dir).Load();
        Assert.Equal(@"C:\X", s.Left);
        Assert.Null(s.Right);
        Assert.Equal(0, s.Active);
        Assert.Empty(s.LastFolderPerDrive);
    }

    [Fact]
    public void Session_KeepsEachPanelsView()
    {
        var store = SessionStore.InDirectory(_dir);
        store.Save(new SessionState(@"C:\A", @"C:\B", 0, new Dictionary<string, string>(), LeftView: PanelView.Thumbnails, RightView: PanelView.Brief));
        var back = store.Load();
        Assert.Equal(PanelView.Thumbnails, back.LeftView);
        Assert.Equal(PanelView.Brief, back.RightView);

        File.WriteAllText(Path.Combine(_dir, SessionStore.FileName), "left\tC:\\X\nview:0\tSideways\nview:1\t5\n");
        var old = store.Load();
        Assert.Null(old.LeftView);
        Assert.Null(old.RightView);
    }

    [Fact]
    public void Session_KeepsEachSidesTabs_InOrder_WithTheirLooks()
    {
        var store = SessionStore.InDirectory(_dir);
        SavedTab[] tabs =
        [
            new(0, @"C:\Work", PanelView.Brief, new SortSpec(SortColumn.Time, Descending: true), Locked: true, Colour: NameColour.Teal),
            new(0, @"D:\Photos", Filter: "*.jpg|IMG_*", FilterFolders: true),
            new(1, @"\\nas\share"),
        ];
        store.Save(new SessionState(@"D:\Photos", @"\\nas\share", 0, new Dictionary<string, string>()) { Tabs = tabs, LeftTab = 1, RightTab = 0 });
        var back = store.Load();

        Assert.Equal(tabs, back.Tabs);
        Assert.Equal(1, back.LeftTab);
        Assert.Equal(0, back.RightTab);

        File.WriteAllText(Path.Combine(_dir, SessionStore.FileName), "left\tC:\\X\ntab:0\tBrief|Name|0|0|||\ntab:2\tBrief|Name|0|0|||C:\\Y\ntab:1\tWhatever|Upwards|1|1|Plaid|0|C:\\Z|\ntabon:0\tlots\n");
        var odd = store.Load();
        var only = Assert.Single(odd.Tabs);
        Assert.Equal(new SavedTab(1, @"C:\Z", Locked: true), only);
        Assert.Equal(0, odd.LeftTab);
    }

    [Fact]
    public async Task ViewCommands_ChangeThePanelsView_AndSayIt()
    {
        Assert.True(await _panel.NavigateAsync(@"C:\"));
        var changes = new List<PanelChange>();
        _panel.Changed += (_, c) => changes.Add(c);
        Assert.Equal(PanelView.Detailed, _panel.View);
        Assert.True(_panel.Execute("View.Mode5", 10));
        Assert.Equal(PanelView.Thumbnails, _panel.View);
        Assert.Contains(PanelChange.View, changes);

        changes.Clear();
        Assert.True(_panel.Execute("View.Mode5", 10));
        Assert.Empty(changes);

        Assert.True(_panel.Execute("View.Mode7", 10));
        Assert.Equal(PanelView.Types, _panel.View);
        Assert.False(_panel.Execute("View.Mode8", 10));
        Assert.Contains("View.Mode2", FilePanel.HandledCommands);
        Assert.DoesNotContain("View.Mode8", FilePanel.HandledCommands);

        Assert.Equal(PanelView.Detailed, PanelViews.Next(PanelView.Brief, 1));
        Assert.Equal(PanelView.Types, PanelViews.Next(PanelView.Tiles, 1));
        Assert.Equal(PanelView.Columns, PanelViews.Next(PanelView.Types, 1));
        Assert.Equal(PanelView.Brief, PanelViews.Next(PanelView.Columns, 1));
        Assert.Equal(PanelView.Columns, PanelViews.Next(PanelView.Brief, -1));
        Assert.Equal("View.Mode6", PanelViews.Command(PanelView.Tiles));
    }
}
