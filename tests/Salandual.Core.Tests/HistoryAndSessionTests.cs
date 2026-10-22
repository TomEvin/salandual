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
}
