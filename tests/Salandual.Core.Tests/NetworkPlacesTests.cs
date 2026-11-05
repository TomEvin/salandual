// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/NetworkPlacesTests.cs

using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Core.Tests;

public sealed class NetworkPlacesTests
{
    private static FileEntry Folder(string name, FileAttributes extra = 0) => new(name, 0, 0, FileAttributes.Directory | extra);

    private static (PlacesFileSystem Files, MemoryShellPlaces Places) Make(params string[] openedBefore)
    {
        var inner = new FakeFileSystem().Dir(@"\\NAS-01\Media", "Films/", "list.txt:3").Dir(@"\\NAS-01\Media\Films", "a.mkv:9");
        var places = new MemoryShellPlaces();
        places.Items[ShellPlace.Network].AddRange([
            new PlaceItem(Folder("NAS-01"), @"\\NAS-01") { IsComputer = true },
            new PlaceItem(Folder("S: Media"), @"S:\") { Comment = @"\\nas-01\media" },
        ]);
        places.Items[@"\\NAS-01"] = [
            new PlaceItem(Folder("Media"), @"\\NAS-01\Media") { Comment = "Films and music" },
            new PlaceItem(Folder("C$", FileAttributes.Hidden), @"\\NAS-01\C$") { Comment = "Default share" },
        ];
        return (new PlacesFileSystem(inner, places) { OpenedBefore = () => openedBefore }, places);
    }

    [Theory]
    [InlineData(@"\\NAS-01", true)]
    [InlineData(@"\\NAS-01\", true)]
    [InlineData(@"\\NAS-01\Media", false)]
    [InlineData(@"\\?\C:\x", false)]
    [InlineData(@"\\.\pipe", false)]
    [InlineData(@"\\", false)]
    [InlineData(@"C:\", false)]
    public void IsServer_OnlyAServerOnItsOwn(string path, bool server) => Assert.Equal(server, ShellPlace.IsServer(path));

    [Fact]
    public void ServerAndShare_AreReadFromAnyNetworkPath()
    {
        Assert.Equal(@"\\NAS-01", ShellPlace.ServerOf(@"\\NAS-01\Media\Films"));
        Assert.Equal(@"\\NAS-01\Media", ShellPlace.ShareOf(@"\\NAS-01\Media\Films"));
        Assert.Null(ShellPlace.ShareOf(@"\\NAS-01"));
        Assert.Null(ShellPlace.ServerOf(@"C:\Media"));
        Assert.True(ShellPlace.IsShareRoot(@"\\NAS-01\Media\"));
        Assert.False(ShellPlace.IsShareRoot(@"\\NAS-01\Media\Films"));
        Assert.Equal("Network", ShellPlace.Label(ShellPlace.Network));
        Assert.Equal("Shared folders", ShellPlace.Label(ShellPlace.Shares));
    }

    [Fact]
    public async Task Network_KeepsItsOrder_ThenTheServersOpenedBeforeItDidntFind()
    {
        var (files, _) = Make(@"\\backup-srv\daily", @"\\nas-01\media", @"\\BACKUP-SRV\weekly");
        var listing = await files.ListAsync(ShellPlace.Network, TestContext.Current.CancellationToken);
        Assert.True(listing.KeepOrder);
        Assert.Equal(["NAS-01", "S: Media", "backup-srv"], listing.Entries.Select(e => e.Name));
        Assert.Equal("opened before", files.Item(ShellPlace.Network, "backup-srv")!.Comment);
        Assert.Equal(@"\\backup-srv", files.Combine(ShellPlace.Network, "backup-srv"));
        Assert.Equal(@"S:\", files.Combine(ShellPlace.Network, "S: Media"));
    }

    [Fact]
    public async Task AServer_ListsItsShares_TheHiddenOnesOnlyWithHiddenFiles()
    {
        var (files, _) = Make();
        var panel = new FilePanel(files, EntrySorter.ForCurrentCulture(numericSort: true), a => a());
        await panel.NavigateAsync(@"\\NAS-01");
        Assert.Equal(["..", "Media"], panel.Items.Select(e => e.Name));
        Assert.Equal("Films and music", files.Item(@"\\NAS-01", "Media")!.Comment);

        panel.SetShowHidden(true);
        await panel.NavigateAsync(ShellPlace.Network);
        await panel.NavigateAsync(@"\\NAS-01");
        Assert.Contains("C$", panel.Items.Select(e => e.Name));
    }

    [Fact]
    public async Task GoingUp_FromAShare_ToItsServer_ThenNetwork_TheOneYouCameFromFocused()
    {
        var (files, _) = Make();
        var panel = new FilePanel(files, EntrySorter.ForCurrentCulture(numericSort: true), a => a());
        await panel.NavigateAsync(@"\\NAS-01\Media");
        Assert.True(panel.HasUpEntry);

        await panel.GoParentAsync();
        Assert.Equal(@"\\NAS-01", panel.Path);
        Assert.Equal("Media", panel.FocusedEntry?.Name);

        await panel.GoParentAsync();
        Assert.Equal(ShellPlace.Network, panel.Path);
        Assert.Equal("NAS-01", panel.FocusedEntry?.Name);
        Assert.False(panel.HasUpEntry);
    }

    [Fact]
    public async Task AServer_IsTheTop_WhileNetworkIsSwitchedOff()
    {
        var (files, _) = Make();
        files.ServersGoUpToNetwork = false;
        var panel = new FilePanel(files, EntrySorter.ForCurrentCulture(numericSort: true), a => a());
        await panel.NavigateAsync(@"\\NAS-01");
        Assert.False(panel.HasUpEntry);
        Assert.Equal(@"\\NAS-01", files.GetParent(@"\\NAS-01\Media"));
    }

    [Fact]
    public async Task AServerThatWantsAPassword_AsksWindows_ThenListsIt()
    {
        var (files, places) = Make();
        places.Locked.Add(@"\\NAS-01");
        var asked = new List<string>();
        files.SignIn = share =>
        {
            asked.Add(share);
            return places.SignInAsync(share, 0);
        };

        var listing = await files.ListAsync(@"\\NAS-01", TestContext.Current.CancellationToken);
        Assert.Equal([@"\\NAS-01"], asked);
        Assert.Contains("Media", listing.Entries.Select(e => e.Name));
    }

    [Fact]
    public async Task ACancelledSignIn_LeavesTheError()
    {
        var (files, places) = Make();
        places.Locked.Add(@"\\NAS-01");
        places.SignInAnswer = false;
        files.SignIn = share => places.SignInAsync(share, 0);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => files.ListAsync(@"\\NAS-01", TestContext.Current.CancellationToken));
        Assert.Single(places.Done, d => d.Action == "signin");
    }

    [Fact]
    public void APassword_IsAskedForOnlyWhereOneCanHelp()
    {
        static Exception Win32(int code) => new IOException("x", unchecked((int)0x80070000) | code);
        Assert.True(PlacesFileSystem.AsksForPassword(@"\\NAS-01\Media", new UnauthorizedAccessException()));
        Assert.True(PlacesFileSystem.AsksForPassword(@"\\NAS-01\Media\Films", Win32(1326)));
        Assert.False(PlacesFileSystem.AsksForPassword(@"\\NAS-01\Media\Films", new UnauthorizedAccessException()));
        Assert.False(PlacesFileSystem.AsksForPassword(@"C:\Private", new UnauthorizedAccessException()));
        Assert.False(PlacesFileSystem.AsksForPassword(@"\\NAS-01\Media", Win32(53)));
    }

    [Fact]
    public void TheSharesYouOpened_AreRememberedNewestFirst()
    {
        var history = new HistorySettings { NetworkFolders = HistorySettings.Push([@"\\nas-01\media", @"\\backup-srv\daily"], @"\\NAS-01\Media") };
        Assert.Equal([@"\\NAS-01\Media", @"\\backup-srv\daily"], history.NetworkFolders);
    }
}
