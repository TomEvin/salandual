// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ShellPlacesUiTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.Core.Commands;
using Salandual.Core.Vfs;

namespace Salandual.App.Tests;

public sealed class ShellPlacesUiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-places-").FullName;
    private readonly NoShellAccess _shell = new();

    public ShellPlacesUiTests()
    {
        Directory.CreateDirectory(Path.Join(_dir, @"Documents\Reports"));
        Directory.CreateDirectory(Path.Join(_dir, "Out"));
        File.WriteAllText(Path.Join(_dir, @"Documents\plan.txt"), "x");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FileEntry Folder(string name) => new(name, 0, 0, FileAttributes.Directory);

    private async Task<(MainWindow W, MemoryShellPlaces Places)> Open(string path)
    {
        var w = new MainWindow { Width = 1100, Height = 640 };
        w.ShellAccess = _shell;
        var places = (MemoryShellPlaces)w.Services.Places;
        places.Items[ShellPlace.ThisPc].AddRange([
            new PlaceItem(Folder("Documents"), Path.Join(_dir, "Documents")),
            new PlaceItem(Folder("Out"), Path.Join(_dir, "Out")),
            new PlaceItem(Folder("Q: System"), @"Q:\") { IsDrive = true, Free = 82L << 30, Total = 476L << 30 },
            new PlaceItem(Folder("R: Backup"), @"R:\") { IsDrive = true, Free = 20L << 30, Total = 931L << 30 },
        ]);
        var deleted = new DateTime(2026, 10, 28, 9, 30, 0, DateTimeKind.Utc).Ticks;
        places.Items[ShellPlace.RecycleBin].AddRange([
            new PlaceItem(new FileEntry("old-report.pdf", 312_000, deleted, FileAttributes.Normal), @"Q:\$Recycle.Bin\S-1\$RAB12.pdf") { From = @"D:\Work\Reports" },
            new PlaceItem(new FileEntry("draft 2.docx", 48_000, deleted, FileAttributes.Normal), @"Q:\$Recycle.Bin\S-1\$RCD34.docx") { From = @"D:\Work" },
            new PlaceItem(Folder("Screenshots"), @"Q:\$Recycle.Bin\S-1\$REF56") { From = @"C:\Users\Sample\Pictures" },
        ]);
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(path);
        await w.RightPanel.Panel.NavigateAsync(Path.Join(_dir, "Out"));
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return (w, places);
    }

    private static void Press(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            for (var k = 0; k < 10; k++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    private static void Click(ContextMenu menu, string header)
    {
        var item = menu.Items.OfType<MenuItem>().First(i => (i.Header as string)?.Replace("_", "", StringComparison.Ordinal) == header);
        Assert.True(item.IsEnabled);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static List<string> Headers(ContextMenu menu) => [.. menu.Items.OfType<MenuItem>().Select(i => (i.Header as string ?? "").Replace("_", "", StringComparison.Ordinal))];

    private static void Focus(MainWindow w, string name)
    {
        var panel = w.ActivePanel.Panel;
        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == name));
    }

    [AvaloniaFact]
    public async Task ThisPc_YourFoldersThenTheDrives_WithHowFullEachIs_AndEnterOpensOne()
    {
        var (w, _) = await Open(ShellPlace.ThisPc);
        var host = w.ActivePanel;
        Assert.Equal(["Documents", "Out", "Q: System", "R: Backup"], host.Panel.Items.Select(e => e.Name));
        Assert.Equal([("This PC", ShellPlace.ThisPc)], DirectoryLine.Split(host.Panel.Path));
        Assert.Contains("This PC", w.Title);
        var place = host.View.Place!;
        Assert.Equal(1 - (82.0 / 476), place.Fill!(host.Panel.Items[2])!.Value, 3);
        Assert.Null(place.Fill(host.Panel.Items[0]));
        Assert.Contains("82", place.Text(host.Panel.Items[2]));
        Capture(w, "places-thispc.png");

        Focus(w, "Documents");
        Press(w, Key.Enter);
        await Until(() => host.Panel.Path == Path.Join(_dir, "Documents"));
        Assert.Null(host.View.Place);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Backspace_OnADrivesRoot_GoesToThisPc_TheDriveFocused()
    {
        var (w, places) = await Open(ShellPlace.ThisPc);
        var root = Path.GetPathRoot(_dir)!;
        places.Items[ShellPlace.ThisPc].Add(new PlaceItem(Folder("Temp drive"), root) { IsDrive = true, Free = 1, Total = 2 });
        var panel = w.ActivePanel.Panel;
        await panel.NavigateAsync(root);
        Press(w, Key.Back);
        await Until(() => panel.Path == ShellPlace.ThisPc && panel.FocusedEntry?.Name == "Temp drive");

        w.Services.Settings.Update(s => s with { DriveMenu = s.DriveMenu with { Places = [.. s.DriveMenu.Places.Select(p => p.Id == Core.Settings.DriveMenuSettings.ThisPc ? p with { InMenu = false, InStrip = false } : p)] } });
        await panel.NavigateAsync(root);
        Press(w, Key.Back);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(root, panel.Path);
        w.Close();
    }

    [AvaloniaFact]
    public async Task TheRecycleBin_ShowsWhereEachCameFrom_Restore_DeleteForGood_CopyOut_Empty()
    {
        var (w, places) = await Open(ShellPlace.RecycleBin);
        var host = w.ActivePanel;
        var asked = new List<string>();
        w.PlaceQuestion = title =>
        {
            asked.Add(title);
            return Task.FromResult(true);
        };
        Assert.Equal("From", host.View.Place!.Head);
        Assert.Equal("Deleted", host.View.Place.DateHead);
        Assert.Equal(@"D:\Work\Reports", host.View.Place.Text(host.Panel.Items.First(e => e.Name == "old-report.pdf")));
        Capture(w, "places-bin.png");

        Focus(w, "draft 2.docx");
        Press(w, Key.F3);
        Assert.Contains("F8", w.NoticeMessage);

        Focus(w, "old-report.pdf");
        w.Run("File.ContextMenu", PanelTarget.Active);
        var menu = w.PlaceItemsMenu!;
        Assert.Equal(["Restore", "Delete for good", "Copy out to the other panel…", "Move out to the other panel…", "Empty the Recycle Bin…", "Open in Explorer", "Refresh"], Headers(menu));
        Capture(w, "places-bin-menu.png");
        Click(menu, "Restore");
        await Until(() => places.Done.Count == 1 && host.Panel.Items.All(e => e.Name != "old-report.pdf"));
        Assert.Equal(("restore", @"Q:\$Recycle.Bin\S-1\$RAB12.pdf"), (places.Done[0].Action, places.Done[0].Targets.Single()));
        menu.Close();

        Focus(w, "draft 2.docx");
        Press(w, Key.F5);
        await Until(() => places.Done.Count == 2);
        Assert.Equal(("copy", Path.Join(_dir, "Out")), (places.Done[1].Action, places.Done[1].Folder));
        Assert.Equal("Copy 1 item out of the Recycle Bin?", asked[^1]);

        Press(w, Key.F8);
        await Until(() => places.Done.Count == 3);
        Assert.Equal("delete", places.Done[2].Action);
        Assert.Equal("Delete for good?", asked[^1]);

        w.Run("Panel.DirectoryContextMenu", PanelTarget.Active);
        Click(w.PlaceItemsMenu!, "Empty the Recycle Bin…");
        await Until(() => places.Done.Count == 4 && host.Panel.Count == 0);
        Assert.Equal("empty", places.Done[3].Action);
        w.Close();
    }

    [AvaloniaFact]
    public async Task ShiftF3_OpensExplorer_WithTheItemsSelected_OrThePlace()
    {
        var (w, _) = await Open(Path.Join(_dir, "Documents"));
        Focus(w, "plan.txt");
        Press(w, Key.F3, RawInputModifiers.Shift);
        await Until(() => _shell.Explored.Count == 1);
        Assert.Equal((Path.Join(_dir, "Documents"), "plan.txt"), (_shell.Explored[0].Folder, _shell.Explored[0].Names.Single()));

        await w.ActivePanel.Panel.NavigateAsync(ShellPlace.RecycleBin);
        Press(w, Key.F3, RawInputModifiers.Shift);
        await Until(() => _shell.Explored.Count == 2);
        Assert.Equal((ShellPlace.RecycleBin, 0), (_shell.Explored[1].Folder, _shell.Explored[1].Names.Length));
        w.Close();
    }

    [AvaloniaFact]
    public async Task GoToShortcutTarget_OpensTheTargetsFolder_TheTargetFocused()
    {
        var link = Path.Join(_dir, "Out", "Plan - Shortcut.lnk");
        File.WriteAllText(link, "made up");
        _shell.Shortcuts[link] = Path.Join(_dir, @"Documents\plan.txt");
        var (w, _) = await Open(Path.Join(_dir, "Out"));
        Focus(w, "Plan - Shortcut.lnk");
        w.Run("Navigation.ShortcutTarget", PanelTarget.Active);
        await Until(() => w.ActivePanel.Panel.Path == Path.Join(_dir, "Documents") && w.ActivePanel.Panel.FocusedEntry?.Name == "plan.txt");

        await w.ActivePanel.Panel.NavigateAsync(Path.Join(_dir, "Out"));
        Focus(w, "Plan - Shortcut.lnk");
        w.Run("File.ContextMenu", PanelTarget.Active);
        Assert.Contains("Go to shortcut target", Headers(w.QuickMenu!));
        w.QuickMenu!.Close();
        w.Close();
    }

    [AvaloniaFact]
    public async Task TheNewMenu_YourTemplatesFirst_ThenWindowsList_TheNewItemReadyToRename()
    {
        var templates = Path.Join(_dir, "Templates");
        Directory.CreateDirectory(templates);
        File.WriteAllText(Path.Join(templates, "Invoice.md"), "# Invoice");
        var (w, _) = await Open(Path.Join(_dir, "Out"));
        w.Services.Settings.Update(s => s with { Operations = s.Operations with { TemplatesFolder = templates } });
        var host = w.ActivePanel;

        Press(w, Key.N, RawInputModifiers.Control | RawInputModifiers.Shift);
        var menu = w.NewMenu!;
        Assert.True(menu.IsOpen);
        var headers = Headers(menu);
        Assert.Equal(["", "Invoice.md", "Folder", "Shortcut to Out", "TXT", "MD", "Templates folder…"], headers.Where(h => h is not null));
        Capture(w, "places-new-menu.png");

        Click(menu, "TXT");
        await Until(() => host.Panel.FocusedEntry?.Name == "New TXT.txt" && host.IsRenaming);
        Assert.Equal(0, new FileInfo(Path.Join(_dir, @"Out\New TXT.txt")).Length);
        Capture(w, "places-new-rename.png");
        host.CancelRename();

        w.Run("File.NewMenu", PanelTarget.Active);
        Click(w.NewMenu!, "TXT");
        await Until(() => host.Panel.FocusedEntry?.Name == "New TXT (2).txt");
        host.CancelRename();

        w.Run("File.NewMenu", PanelTarget.Active);
        Click(w.NewMenu!, "Invoice.md");
        await Until(() => host.Panel.FocusedEntry?.Name == "Invoice.md");
        Assert.Equal("# Invoice", File.ReadAllText(Path.Join(_dir, @"Out\Invoice.md")));
        host.CancelRename();

        await host.Panel.NavigateAsync(ShellPlace.ThisPc);
        w.Run("File.NewMenu", PanelTarget.Active);
        Assert.StartsWith("This PC lists places", w.NoticeMessage);
        w.Close();
    }

    [AvaloniaFact]
    public async Task TheDriveMenu_HasTheNewPlaces_AndTheStripThisPc()
    {
        var (w, _) = await Open(Path.Join(_dir, "Out"));
        Assert.Contains(w.DriveStripItems(), i => i.Target == ShellPlace.ThisPc && i.Label == "This PC");
        Assert.DoesNotContain(w.DriveStripItems(), i => i.Target == ShellPlace.RecycleBin);
        w.ToggleDriveMenu(w.ActivePanel);
        Dispatcher.UIThread.RunJobs();
        var texts = w.DriveMenuFlyout!.Items.OfType<MenuItem>().SelectMany(i => (i.Header as Control)?.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "") ?? []).ToList();
        foreach (var name in new[] { "This PC", "Home", "Libraries", "Recycle Bin" })
        {
            Assert.Contains(name, texts);
        }

        var bin = w.DriveMenuFlyout.Items.OfType<MenuItem>().First(i => (i.Header as Control)?.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Recycle Bin") == true);
        bin.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => w.ActivePanel.Panel.Path == ShellPlace.RecycleBin);
        w.Close();
    }

    [AvaloniaFact]
    public async Task TheStrip_NamesAPlace_AndItsMenuSwitchesThePlacesOnAndOff()
    {
        var (w, _) = await Open(Path.Join(_dir, "Out"));
        var pc = w.DriveStripItems().Single(i => i.Target == ShellPlace.ThisPc);
        Assert.Equal("This PC", pc.Tip);
        Assert.Equal("Icon.StripPc", pc.Glyph);

        var strip = w.LeftPanel.Strip;
        var menu = w.ShowStripMenu(w.LeftPanel, strip, pc);
        Dispatcher.UIThread.RunJobs();
        var places = menu.Items.OfType<MenuItem>().Single(i => i.Header as string == "Places in the drives _strip");
        var names = places.Items.OfType<MenuItem>().ToDictionary(i => (string)i.Header!);
        Assert.True(names["This PC"].IsChecked);
        Assert.False(names["Recycle Bin"].IsChecked);
        Assert.True(names["Documents"].IsEnabled);
        Assert.False(names["Registry: later (after v1.0)"].IsEnabled);
        Assert.True(names["FTP and SFTP"].IsEnabled);
        names["Recycle Bin"].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        names["Documents"].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(w.DriveStripItems(), i => i.Target == ShellPlace.RecycleBin && i.Glyph == "Icon.Bin" && i.Tip == "Recycle Bin");
        Assert.Contains(w.DriveStripItems(), i => i.Setting == Core.Settings.DriveMenuSettings.Documents);
        menu.Close();
        Dispatcher.UIThread.RunJobs();
        Capture(w, "places-strip.png");

        menu = w.ShowStripMenu(w.LeftPanel, strip, null);
        Dispatcher.UIThread.RunJobs();
        places = menu.Items.OfType<MenuItem>().Single(i => i.Header as string == "Places in the drives _strip");
        places.Items.OfType<MenuItem>().Single(i => i.Header as string == "_Icons only").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.Services.Settings.Current.DriveMenu.PlaceIconsOnly);
        Assert.Contains(w.DriveStripItems(), i => i.Target == ShellPlace.RecycleBin && i.Label == "" && i.Tip == "Recycle Bin");
        menu.Close();
        Dispatcher.UIThread.RunJobs();
        Capture(w, "places-strip-icons.png");

        menu = w.ShowStripMenu(w.LeftPanel, strip, pc);
        Dispatcher.UIThread.RunJobs();
        menu.Items.OfType<MenuItem>().First(i => i.Header as string == "_Hide from the bar").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(w.DriveStripItems(), i => i.Target == ShellPlace.ThisPc);
        menu.Close();
        w.Close();
    }
}
