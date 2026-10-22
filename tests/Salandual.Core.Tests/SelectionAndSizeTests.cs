// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/SelectionAndSizeTests.cs

using System.Collections.Concurrent;
using System.Globalization;
using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Vfs;

namespace Salandual.Core.Tests;

public sealed class SelectionAndSizeTests
{
    private readonly FakeFileSystem _fs = new FakeFileSystem()
        .Dir(@"C:\", "Data/", "Other/")
        .Dir(@"C:\Data", "Photos/", "Docs/", "IMG_0412.HEIC:3000", "IMG_0412.JPG:900", "IMG_0413.HEIC:3100", "notes.md:40", "README:5")
        .Dir(@"C:\Data\Photos", "a.jpg:100", "b.jpg:200", "Raw/")
        .Dir(@"C:\Data\Photos\Raw", "c.arw:1000")
        .Dir(@"C:\Data\Docs", "x.pdf:7")
        .Dir(@"C:\Other", "notes.md:1", "IMG_0412.HEIC:1");

    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly FilePanel _panel;

    public SelectionAndSizeTests()
        => _panel = new FilePanel(_fs, new EntrySorter(CultureInfo.GetCultureInfo("en-US"), numericSort: true), _posted.Enqueue);

    private string[] Names => [.. _panel.Items.Select(e => e.Name)];

    private string[] SelectedNames => [.. _panel.Items.Where((_, i) => _panel.IsSelected(i)).Select(e => e.Name)];

    private int IndexOf(string name) => Array.IndexOf(Names, name);

    private async Task Open(string path)
    {
        Assert.True(await _panel.NavigateAsync(path));
        Drain();
    }

    private void Drain()
    {
        while (_posted.TryDequeue(out var a))
        {
            a();
        }
    }

    private async Task WaitForSizes()
    {
        for (var i = 0; i < 200 && _panel.Items.Any(e => _panel.IsSizing(e)); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Drain();
        }
    }

    [Fact]
    public async Task SelectByMask_FilesOnly_UnlessFoldersSettingIsOn()
    {
        await Open(@"C:\Data");
        _panel.SelectByMask(FileMask.Parse("*.heic;D*")!, select: true);
        Assert.Equal(["IMG_0412.HEIC", "IMG_0413.HEIC"], SelectedNames);

        _panel.SelectFoldersWithNumPlus = true;
        _panel.SelectByMask(FileMask.Parse("*.*")!, select: true);
        Assert.Equal(["Docs", "Photos", "IMG_0412.HEIC", "IMG_0412.JPG", "IMG_0413.HEIC", "notes.md", "README"], SelectedNames);

        _panel.SelectByMask(FileMask.Parse("|IMG*")!, select: false);
        Assert.Equal(["IMG_0412.HEIC", "IMG_0412.JPG", "IMG_0413.HEIC"], SelectedNames);
    }

    [Fact]
    public async Task CtrlW_RestoresSelection_AfterUnselectAll()
    {
        await Open(@"C:\Data");
        _panel.SelectByMask(FileMask.Parse("*.heic")!, select: true);
        Assert.True(_panel.Execute("Selection.UnselectAll", 10));
        Assert.Empty(SelectedNames);

        Assert.True(_panel.Execute("Selection.Restore", 10));
        Assert.Equal(["IMG_0412.HEIC", "IMG_0413.HEIC"], SelectedNames);
    }

    [Fact]
    public async Task CtrlW_InAnotherFolder_SelectsTheSameNamesThatExist()
    {
        await Open(@"C:\Data");
        _panel.SelectByMask(FileMask.Parse("*.heic;*.md")!, select: true);
        await Open(@"C:\Other");
        Assert.Empty(SelectedNames);

        _panel.RestoreSelection();
        Assert.Equal(["IMG_0412.HEIC", "notes.md"], SelectedNames);
    }

    [Fact]
    public async Task CtrlW_NothingRemembered_DoesNothing()
    {
        await Open(@"C:\Data");
        _panel.Toggle(IndexOf("README"));
        Assert.False(_panel.CanRestoreSelection);
        _panel.RestoreSelection();
        Assert.Equal(["README"], SelectedNames);
    }

    [Fact]
    public async Task Space_OnFolder_SelectsAndCalculatesItsSize_IncludingSubfolders()
    {
        await Open(@"C:\Data");
        var changes = new List<PanelChange>();
        _panel.Changed += (_, c) => changes.Add(c);
        _panel.SetFocus(IndexOf("Photos"));

        Assert.True(_panel.Execute("Selection.ToggleAndSize", 10));
        Assert.Equal(["Photos"], SelectedNames);
        Assert.Equal("IMG_0412.HEIC", _panel.FocusedEntry?.Name);
        Assert.True(_panel.IsSizing(_panel.Items[IndexOf("Photos")]));
        await WaitForSizes();

        Assert.True(_panel.TryGetDirectorySize(_panel.Items[IndexOf("Photos")], out var size));
        Assert.Equal(1300, size);
        Assert.Equal(new SelectionSummary(0, 1, 1300), _panel.GetSelectionSummary());
        Assert.Contains(changes, c => (c & PanelChange.Sizes) != 0);

        _panel.SetFocus(IndexOf("Photos"));
        Assert.True(_panel.Execute("Selection.ToggleAndSize", 10));
        Assert.Empty(SelectedNames);
        Assert.Equal("Photos: 2 KB", InformationLine.Expand("$(FileName): $(FileSize)", _panel.Items[IndexOf("Photos")], false, DisplayFormat.Default, size));
    }

    [Fact]
    public async Task Space_OnFile_TogglesAndMovesOn()
    {
        await Open(@"C:\Data");
        _panel.SetFocus(IndexOf("notes.md"));
        _panel.Execute("Selection.ToggleAndSize", 10);
        Assert.Equal(["notes.md"], SelectedNames);
        Assert.Equal("README", _panel.FocusedEntry?.Name);
        Assert.DoesNotContain(_panel.Items, e => _panel.IsSizing(e));
    }

    [Fact]
    public async Task Sizes_AreForgottenInAnotherFolder_AndCancelStopsThem()
    {
        await Open(@"C:\Data");
        _panel.CalculateSize(IndexOf("Docs"));
        Assert.True(_panel.CancelSizing());
        Assert.False(_panel.CancelSizing());
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Drain();
        Assert.False(_panel.TryGetDirectorySize(_panel.Items[IndexOf("Docs")], out _));

        _panel.CalculateSize(IndexOf("Docs"));
        await WaitForSizes();
        Assert.True(_panel.TryGetDirectorySize(_panel.Items[IndexOf("Docs")], out var docs));
        Assert.Equal(7, docs);

        await Open(@"C:\");
        await Open(@"C:\Data");
        Assert.False(_panel.TryGetDirectorySize(_panel.Items[IndexOf("Docs")], out _));
    }

    [Fact]
    public async Task DirectorySize_SkipsLinkedFolders()
    {
        var fs = new LinkFs();
        Assert.Equal(5, await DirectorySize.ComputeAsync(fs, @"C:\L", TestContext.Current.CancellationToken));
    }

    private sealed class LinkFs : IFileSystemProvider
    {
        public ProviderCapabilities Capabilities => ProviderCapabilities.List;

        public Task<DirectoryListing> ListAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new DirectoryListing(path,
            [
                new FileEntry("f.txt", 5, 0, FileAttributes.Archive),
                new FileEntry("loop", 0, 0, FileAttributes.Directory | FileAttributes.ReparsePoint),
            ], TimeSpan.Zero));

        public string? GetParent(string path) => null;

        public string? GetRoot(string path) => path;

        public string GetName(string path) => path;

        public string Combine(string directory, string name) => directory + "\\" + name;

        public IDisposable Watch(string path, Action<DirectoryChange> onChange, Action<Exception> onError) => throw new NotSupportedException();
    }
}
