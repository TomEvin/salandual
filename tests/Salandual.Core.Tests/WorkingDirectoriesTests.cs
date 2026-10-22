// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/WorkingDirectoriesTests.cs

using Salandual.Core.Panels;
using Salandual.Core.Settings;

namespace Salandual.Core.Tests;

public sealed class WorkingDirectoriesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-work-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Remember_PutsTheFolderFirst_OncePerPanel()
    {
        var w = new WorkingDirectories();
        w.Remember(@"C:\A", 0);
        w.Remember(@"C:\B", 0);
        w.Remember(@"c:\a", 0);
        w.Remember(@"C:\A", 1);
        Assert.Equal([new WorkingDirectory(@"C:\A", 1), new WorkingDirectory(@"c:\a", 0), new WorkingDirectory(@"C:\B", 0)], w.All);
    }

    [Fact]
    public void PerPanel_ShowsThatPanelsFolders_Shared_ShowsEachFolderOnce()
    {
        var w = new WorkingDirectories();
        w.Remember(@"C:\Left1", 0);
        w.Remember(@"C:\Right1", 1);
        w.Remember(@"C:\Both", 0);
        w.Remember(@"C:\Both", 1);

        Assert.Equal([@"C:\Both", @"C:\Left1"], w.For(0, shared: false).Select(x => x.Path));
        Assert.Equal([@"C:\Both", @"C:\Right1"], w.For(1, shared: false).Select(x => x.Path));
        var shared = w.For(0, shared: true);
        Assert.Equal([@"C:\Both", @"C:\Right1", @"C:\Left1"], shared.Select(x => x.Path));
        Assert.Equal(1, shared[0].Side);
    }

    [Fact]
    public void EachPanel_KeepsTwentyFolders()
    {
        var w = new WorkingDirectories();
        for (var i = 0; i < 30; i++)
        {
            w.Remember($@"C:\L{i}", 0);
            w.Remember($@"C:\R{i}", 1);
        }

        Assert.Equal(WorkingDirectories.MaxShown, w.For(0, false).Count);
        Assert.Equal(@"C:\L29", w.For(0, false)[0].Path);
        Assert.Equal(@"C:\L10", w.For(0, false)[^1].Path);
        Assert.Equal(WorkingDirectories.MaxShown * 2, w.All.Count);
        Assert.Equal(WorkingDirectories.MaxShown, w.For(0, true).Count);
    }

    [Fact]
    public void Clear_ForgetsThePanelsFolders_ButKeepsWhatIsShownNow()
    {
        var w = new WorkingDirectories();
        w.Remember(@"C:\Old", 0);
        w.Remember(@"C:\Here", 0);
        w.Remember(@"D:\Right", 1);

        w.Clear(0, shared: false, @"C:\Here", @"D:\Right");
        Assert.Equal([@"C:\Here"], w.For(0, false).Select(x => x.Path));
        Assert.Equal([@"D:\Right"], w.For(1, false).Select(x => x.Path));

        w.Remember(@"D:\Other", 1);
        w.Clear(1, shared: true, @"C:\Here", @"D:\Right");
        Assert.Equal([@"C:\Here", @"D:\Right"], w.For(0, true).Select(x => x.Path));
    }

    [Fact]
    public void Session_KeepsTheWorkingDirectories_InOrder()
    {
        var w = new WorkingDirectories();
        w.Remember(@"C:\Users\user\Pictures\Nyaralás 2026", 0);
        w.Remember(@"\\nas\photos", 1);
        w.Remember(@"C:\A", 0);
        var store = SessionStore.InDirectory(_dir);
        store.Save(new SessionState(@"C:\A", @"\\nas\photos", 0, new Dictionary<string, string>(), w.All));

        var back = new WorkingDirectories();
        back.Load(store.Load().WorkingDirectories!);
        Assert.Equal(w.All, back.All);
    }
}
