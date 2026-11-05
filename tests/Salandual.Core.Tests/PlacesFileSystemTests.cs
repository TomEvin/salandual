// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/PlacesFileSystemTests.cs

using Salandual.Core.Listing;
using Salandual.Core.Panels;
using Salandual.Core.Vfs;

namespace Salandual.Core.Tests;

public sealed class PlacesFileSystemTests
{
    private static FileEntry Folder(string name) => new(name, 0, 0, FileAttributes.Directory);

    private static (PlacesFileSystem Files, MemoryShellPlaces Places, FakeFileSystem Inner) Make()
    {
        var inner = new FakeFileSystem().Dir(@"C:\", "Users/").Dir(@"C:\Users", "me/").Dir(@"C:\Users\me", "Documents/", "Zeta/").Dir(@"C:\Users\me\Documents", "a.txt:3");
        var places = new MemoryShellPlaces();
        places.Items[ShellPlace.ThisPc].AddRange([
            new PlaceItem(Folder("Documents"), @"C:\Users\me\Documents"),
            new PlaceItem(Folder("C: System"), @"C:\") { IsDrive = true, Free = 25, Total = 100 },
            new PlaceItem(Folder("Archive"), @"C:\Users\me\Zeta"),
        ]);
        places.Items[ShellPlace.Libraries].Add(new PlaceItem(Folder("Music"), ShellPlace.Libraries + @"\Music"));
        places.Items[ShellPlace.Libraries + @"\Music"] = [new PlaceItem(Folder("Documents"), @"C:\Users\me\Documents")];
        places.Items[ShellPlace.RecycleBin].AddRange([
            new PlaceItem(new FileEntry("notes.txt", 5, 1, FileAttributes.Normal), @"Z:\$Recycle.Bin\S\$R1.txt") { From = @"C:\Work" },
            new PlaceItem(new FileEntry("notes.txt", 7, 2, FileAttributes.Normal), @"Z:\$Recycle.Bin\S\$R2.txt") { From = @"D:\Old" },
        ]);
        return (new PlacesFileSystem(inner, places), places, inner);
    }

    [Fact]
    public async Task ThisPc_KeepsItsOrder_AndEnterGoesWhereTheItemStandsFor()
    {
        var (files, _, _) = Make();
        var listing = await files.ListAsync(ShellPlace.ThisPc, TestContext.Current.CancellationToken);
        Assert.True(listing.KeepOrder);
        Assert.Equal(["Documents", "C: System", "Archive"], listing.Entries.Select(e => e.Name));
        Assert.Equal(@"C:\", files.Combine(ShellPlace.ThisPc, "C: System"));
        Assert.Equal(@"C:\Users\me\Documents", files.Combine(ShellPlace.ThisPc, "Documents"));
        Assert.Equal("C: System", files.NameOf(ShellPlace.ThisPc, @"C:"));
        Assert.Null(files.GetParent(ShellPlace.ThisPc));
        Assert.Equal(@"C:\Users", files.GetParent(@"C:\Users\me"));
    }

    [Fact]
    public async Task APanel_KeepsThisPcsOrder_AndOpensADrive()
    {
        var (files, _, _) = Make();
        var panel = new FilePanel(files, EntrySorter.ForCurrentCulture(numericSort: true), a => a());
        await panel.NavigateAsync(ShellPlace.ThisPc);
        Assert.False(panel.HasUpEntry);
        Assert.Equal(["Documents", "C: System", "Archive"], panel.Items.Select(e => e.Name));

        panel.SetFocus(1);
        await panel.OpenFocusedAsync();
        Assert.Equal(@"C:\", panel.Path);
    }

    [Fact]
    public async Task ALibrary_IsAPlaceInLibraries_AndBackspaceGoesBack()
    {
        var (files, _, _) = Make();
        var panel = new FilePanel(files, EntrySorter.ForCurrentCulture(numericSort: true), a => a());
        await panel.NavigateAsync(ShellPlace.Libraries);
        await panel.OpenFocusedAsync();
        Assert.Equal(ShellPlace.Libraries + @"\Music", panel.Path);
        Assert.True(panel.HasUpEntry);
        Assert.Equal("Libraries › Music", ShellPlace.Display(panel.Path));

        await panel.GoParentAsync();
        Assert.Equal(ShellPlace.Libraries, panel.Path);
        Assert.Equal("Music", panel.FocusedEntry?.Name);
    }

    [Fact]
    public async Task TheRecycleBin_NumbersTwoItemsOfOneName_AndItsItemsDontOpen()
    {
        var (files, _, _) = Make();
        var listing = await files.ListAsync(ShellPlace.RecycleBin, TestContext.Current.CancellationToken);
        Assert.False(listing.KeepOrder);
        Assert.Equal(["notes.txt", "notes (2).txt"], listing.Entries.Select(e => e.Name));
        Assert.Equal(@"D:\Old", files.Item(ShellPlace.RecycleBin, "notes (2).txt")?.From);
        Assert.Equal(@"Z:\$Recycle.Bin\S\$R2.txt", files.Item(ShellPlace.RecycleBin, "notes (2).txt")?.Target);

        var inside = files.Combine(ShellPlace.RecycleBin, "notes.txt");
        Assert.True(ShellPlace.IsPlace(inside));
        await Assert.ThrowsAsync<IOException>(() => files.ListAsync(inside, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PlaceNames_AreReadable()
    {
        Assert.Equal("This PC", ShellPlace.Display(ShellPlace.ThisPc));
        Assert.Equal("Recycle Bin", ShellPlace.ShortName(ShellPlace.RecycleBin.ToLowerInvariant()));
        Assert.Equal(ShellPlace.Home, ShellPlace.OfSetting(Settings.DriveMenuSettings.Home));
        Assert.False(ShellPlace.IsPlace(@"C:\"));
    }

    [Fact]
    public void SavedSettings_GetTheNewPlaces_ThisPcInTheStrip()
    {
        var old = new Settings.DriveMenuSettings { Places = [new(Settings.DriveMenuSettings.Desktop, false, false)] };
        var places = new Settings.AppSettings { DriveMenu = old }.Normalize().DriveMenu.Places;
        Assert.Equal(Settings.DriveMenuSettings.ThisPc, places[0].Id);
        Assert.True(places[0].InStrip);
        Assert.Contains(places, p => p.Id == Settings.DriveMenuSettings.RecycleBin && p.InMenu && !p.InStrip);
        Assert.False(places.Single(p => p.Id == Settings.DriveMenuSettings.Desktop).InMenu);
    }
}
