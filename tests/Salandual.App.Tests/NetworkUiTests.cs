// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/NetworkUiTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Salandual.App.Panels;
using Salandual.Core.Vfs;

namespace Salandual.App.Tests;

public sealed class NetworkUiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-network-").FullName;

    public NetworkUiTests() => Directory.CreateDirectory(Path.Join(_dir, "Projects"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FileEntry Folder(string name, FileAttributes extra = 0) => new(name, 0, 0, FileAttributes.Directory | extra);

    private async Task<(MainWindow W, MemoryShellPlaces Places)> Open(string path)
    {
        var w = new MainWindow { Width = 1100, Height = 640 };
        w.ShellAccess = new NoShellAccess();
        var places = (MemoryShellPlaces)w.Services.Places;
        places.Items[ShellPlace.Network].AddRange([
            new PlaceItem(Folder("NAS-01"), @"\\NAS-01") { IsComputer = true },
            new PlaceItem(Folder("OFFICE-PC"), @"\\OFFICE-PC") { IsComputer = true },
            new PlaceItem(Folder("S: Media"), @"S:\") { Comment = @"\\nas-01\media" },
        ]);
        places.Items[@"\\NAS-01"] = [
            new PlaceItem(Folder("Backup"), @"\\NAS-01\Backup") { Comment = "Nightly copies" },
            new PlaceItem(Folder("Media"), @"\\NAS-01\Media") { Comment = "Films and music" },
            new PlaceItem(Folder("C$", FileAttributes.Hidden), @"\\NAS-01\C$") { Comment = "Default share" },
        ];
        places.Items[ShellPlace.Shares].Add(new PlaceItem(Folder("Projects"), Path.Join(_dir, "Projects")) { From = Path.Join(_dir, "Projects") });
        places.Drives.AddRange([new NetworkDrive("S:", @"\\nas-01\media", 2), new NetworkDrive("T:", @"\\office-pc\shared", 0)]);
        w.Services.Settings.Update(s => s with { History = s.History with { NetworkFolders = [@"\\backup-srv\daily"] } });
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(path);
        await w.RightPanel.Panel.NavigateAsync(_dir);
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

    private static void Focus(MainWindow w, string name)
    {
        var panel = w.ActivePanel.Panel;
        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == name));
    }

    [AvaloniaFact]
    public async Task CtrlComma_OpensNetwork_TheComputersYourDrivesAndTheServersOpenedBefore()
    {
        var (w, _) = await Open(_dir);
        Press(w, Key.OemComma, RawInputModifiers.Control);
        await Until(() => w.ActivePanel.Panel.Path == ShellPlace.Network);
        var host = w.ActivePanel;
        Assert.Equal(["NAS-01", "OFFICE-PC", "S: Media", "backup-srv"], host.Panel.Items.Select(e => e.Name));
        Assert.Equal([("Network", ShellPlace.Network)], DirectoryLine.Split(host.Panel.Path));
        var place = host.View.Place!;
        Assert.Equal(@"\\nas-01\media", place.Text(host.Panel.Items[2]));
        Assert.Equal("opened before", place.Text(host.Panel.Items[3]));
        Assert.Equal("Icon.MenuNetwork", place.Glyph!(host.Panel.Items[0]));
        Assert.Null(place.Glyph(host.Panel.Items[2]));
        Capture(w, "network.png");

        Focus(w, "NAS-01");
        Press(w, Key.Enter);
        await Until(() => host.Panel.Path == @"\\NAS-01");
        Assert.Equal(["..", "Backup", "Media"], host.Panel.Items.Select(e => e.Name));
        Assert.Equal([("Network", ShellPlace.Network), ("NAS-01", @"\\NAS-01")], DirectoryLine.Split(host.Panel.Path));
        Assert.Equal("Films and music", host.View.Place!.Text(host.Panel.Items[2]));
        Capture(w, "network-server.png");

        Press(w, Key.Back);
        await Until(() => host.Panel.Path == ShellPlace.Network);
        Assert.Equal("NAS-01", host.Panel.FocusedEntry?.Name);
        w.Close();
    }

    [AvaloniaFact]
    public async Task AServerThatWantsAPassword_AsksWindows()
    {
        var (w, places) = await Open(_dir);
        places.Locked.Add(@"\\NAS-01");
        await w.ActivePanel.Panel.NavigateAsync(@"\\NAS-01");
        Assert.Contains(places.Done, d => d.Action == "signin" && d.Targets[0] == @"\\NAS-01");
        Assert.Equal(@"\\NAS-01", w.ActivePanel.Panel.Path);
        Assert.Equal([@"\\backup-srv\daily"], w.Services.Settings.Current.History.NetworkFolders);
        w.Close();
    }

    [AvaloniaFact]
    public async Task F11_ConnectsTheFocusedComputersShare_ThroughWindows()
    {
        var (w, places) = await Open(ShellPlace.Network);
        Focus(w, "NAS-01");
        Press(w, Key.F11);
        await Until(() => w.OpenConnectDialog is not null);
        var dialog = w.OpenConnectDialog!;
        Assert.Equal("Z:", dialog.Letter.SelectedItem);
        Assert.Equal(@"\\NAS-01\", dialog.Folder.Text);
        Assert.True(dialog.Reconnect.IsChecked);
        Assert.False(dialog.OtherUser.IsChecked);
        await Until(() => dialog.Folder.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { SelectionStart: 9, SelectionEnd: 9 });
        Capture(dialog, "network-connect.png");

        await dialog.AcceptAsync();
        Assert.True(dialog.Error.IsVisible);
        Assert.DoesNotContain(places.Done, d => d.Action == "connect");

        dialog.Folder.Text = @"\\NAS-01\Backup";
        dialog.OtherUser.IsChecked = true;
        await dialog.AcceptAsync();
        await Until(() => w.OpenConnectDialog is null);
        Assert.Contains(places.Done, d => d.Action == "connect" && d.Targets.SequenceEqual(["Z:", @"\\NAS-01\Backup", "reconnect", "other"]));
        Assert.Equal(@"\\NAS-01\Backup", w.Services.Settings.Current.History.NetworkFolders[0]);
        w.Close();
    }

    [AvaloniaFact]
    public async Task F11_CompletesFromYourHistoryAndTheServersShares()
    {
        var (w, _) = await Open(ShellPlace.Network);
        Press(w, Key.F11);
        await Until(() => w.OpenConnectDialog is not null);
        var dialog = w.OpenConnectDialog!;
        var populate = dialog.Folder.AsyncPopulator!;
        Assert.Equal([@"\\backup-srv\daily", @"\\backup-srv\"], (await populate(@"\\b", TestContext.Current.CancellationToken)).Cast<string>());
        Assert.Equal([@"\\NAS-01\Backup", @"\\NAS-01\Media"], (await populate(@"\\NAS-01\", TestContext.Current.CancellationToken)).Cast<string>());
        dialog.Close();
        w.Close();
    }

    [AvaloniaFact]
    public async Task F12_TicksYourDrives_AndAsksBeforeOneWithFilesOpenGoes()
    {
        var (w, places) = await Open(ShellPlace.Network);
        var asked = new List<string>();
        w.PlaceQuestion = title =>
        {
            asked.Add(title);
            return Task.FromResult(true);
        };

        Press(w, Key.F12);
        await Until(() => w.OpenDisconnectDialog is not null);
        var dialog = w.OpenDisconnectDialog!;
        Assert.Equal(["S:", "T:"], dialog.Boxes.Select(b => b.Content as string));
        Assert.All(dialog.Boxes, b => Assert.False(b.IsChecked));
        Assert.Contains("2 files open", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
        Capture(dialog, "network-disconnect.png");

        dialog.Boxes[0].IsChecked = true;
        dialog.Accept();
        await Until(() => places.Done.Any(d => d.Action == "disconnect"));
        Assert.Equal(["S: has files open"], asked);
        Assert.Contains(places.Done, d => d.Action == "disconnect" && d.Targets.SequenceEqual(["S:", "force"]));
        Assert.Equal("S: is disconnected.", w.NoticeMessage);
        Assert.DoesNotContain(places.Drives, d => d.Letter == "S:");
        w.Close();
    }

    [AvaloniaFact]
    public async Task F12_WithNoNetworkDrives_SaysSo()
    {
        var (w, places) = await Open(_dir);
        places.Drives.Clear();
        Press(w, Key.F12);
        await Until(() => w.NoticeMessage is not null);
        Assert.Equal("No network drives are connected.", w.NoticeMessage);
        Assert.Null(w.OpenDisconnectDialog);
        w.Close();
    }

    [AvaloniaFact]
    public async Task CtrlShiftF9_ThisPcsShares_EachWithItsFolder()
    {
        var (w, _) = await Open(_dir);
        Press(w, Key.F9, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Until(() => w.ActivePanel.Panel.Path == ShellPlace.Shares);
        var host = w.ActivePanel;
        Assert.Equal(["Projects"], host.Panel.Items.Select(e => e.Name));
        Assert.Equal(Path.Join(_dir, "Projects"), host.View.Place!.Text(host.Panel.Items[0]));
        Assert.Contains("Shared folders", w.Title);
        Capture(w, "network-shares.png");

        Press(w, Key.Enter);
        await Until(() => host.Panel.Path == Path.Join(_dir, "Projects"));
        w.Close();
    }

    [AvaloniaFact]
    public async Task TheDriveMenu_OpensNetwork_AndTheStripHasItOnlyWhenSwitchedOn()
    {
        var (w, _) = await Open(_dir);
        Assert.DoesNotContain(w.DriveStripItems(), i => i.Target == ShellPlace.Network);
        w.ToggleDriveMenu(w.ActivePanel);
        Dispatcher.UIThread.RunJobs();
        var network = w.DriveMenuFlyout!.Items.OfType<MenuItem>().First(i => (i.Header as Control)?.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Network") == true);
        Assert.True(network.IsEnabled);
        network.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => w.ActivePanel.Panel.Path == ShellPlace.Network);

        w.Services.Settings.Update(s => s with { DriveMenu = s.DriveMenu with { Places = [.. s.DriveMenu.Places.Select(p => p.Id == Core.Settings.DriveMenuSettings.Network ? p with { InStrip = true } : p)] } });
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(w.DriveStripItems(), i => i.Target == ShellPlace.Network && i.Label == "Network");
        w.Close();
    }
}
