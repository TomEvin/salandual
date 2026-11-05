// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ServerSettingsUiTests.cs

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Salandual.App.Dialogs;
using Salandual.App.Options;
using Salandual.App.Panels;
using Salandual.Core.Settings;
using Salandual.Remote;

namespace Salandual.App.Tests;

public sealed class ServerSettingsUiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-server-settings-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Answers : IServerQuestions
    {
        public Queue<SecretAnswer?> Secrets { get; } = new();

        public Task<SecretAnswer?> AskSecretAsync(ServerAddress server, SecretKind kind, bool wrongBefore, bool keptBefore, CancellationToken cancellationToken)
            => Task.FromResult(Secrets.Count > 0 ? Secrets.Dequeue() : null);

        public Task<TrustAnswer> AskTrustAsync(TrustQuestion question, CancellationToken cancellationToken) => Task.FromResult(TrustAnswer.Remember);

        public Task<bool> AskPlainAsync(ServerAddress server, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private async Task<(MainWindow W, MemoryServer Server, Answers Answers)> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 640 };
        w.ShellAccess = new NoShellAccess();
        var server = ((MemoryServers)w.Services.Connector).Add("files.example.org");
        server.AddFile("/var/www/index.html", "<h1>Hello</h1>");
        server.AddFile("/var/www/.htaccess", "Deny from all");
        var answers = new Answers();
        w.ServerAnswers = answers;
        w.Services.Settings.Update(s => s with { Servers = s.Servers with { CloseConnecting = true } });
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(_dir);
        await w.RightPanel.Panel.NavigateAsync(_dir);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return (w, server, answers);
    }

    private static void Press(Window w, Key key, RawInputModifiers mods = RawInputModifiers.None)
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

    private static async Task OnServer(MainWindow w, Answers answers, string bookmark = "Website")
    {
        answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: false));
        await w.OpenServerAsync(w.ActivePanel, new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!, Bookmark: bookmark));
        await Until(() => w.ActivePanel.Panel.Path == "sftp://deploy@files.example.org/var/www");
    }

    private static T Named<T>(Control root, string name) where T : Control
        => root.GetVisualDescendants().OfType<T>().First(c => AutomationProperties.GetName(c) == name);

    [AvaloniaFact]
    public async Task Settings_GivesABookmarkItsOwnTab_OverTheDefaults_KeptOnSave()
    {
        var (w, _, _) = await Open();
        Press(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Until(() => w.OpenConnectServer is { IsVisible: true });
        var connect = w.OpenConnectServer!;
        connect.Host.Text = "files.example.org";
        connect.User.Text = "deploy";
        connect.NameBox.Text = "Website";
        Assert.Equal("Uses the defaults in Options › FTP and SFTP", connect.SettingsNote.Text);

        connect.Settings.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Until(() => connect.OpenSettings is { IsVisible: true });
        var settings = connect.OpenSettings!;
        Assert.Equal("Settings · Website", settings.Title);
        Assert.All(settings.UseDefaults, u => Assert.True(u.IsChecked));
        Assert.False(settings.KeepEvery.IsEffectivelyEnabled);

        settings.UseDefaults[3].IsChecked = false;
        settings.Hidden.IsChecked = false;
        settings.CacheMinutes.Value = 3;
        settings.Accept();
        await Until(() => connect.OpenSettings is null);
        Assert.Equal("Its own: Listing and cache; the defaults for the rest", connect.SettingsNote.Text);

        connect.SaveBookmark();
        var b = Assert.Single(w.Services.Settings.Current.Servers.Bookmarks);
        Assert.Null(b.Connection);
        Assert.Null(b.Transfers);
        Assert.Equal((false, 3), (b.Listing!.ShowHidden, b.Listing.CacheMinutes));
        connect.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Settings_ShowsFtpsAndSftpsRows_UnderTheirHeadings_InOneSize()
    {
        var (w, _, _) = await Open();
        var dialog = new ServerSettingsDialog("Website", new ServerOptions(), null, w.Services.Servers.Secrets);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.DataConnectionBox.IsEffectivelyVisible);
        Assert.Equal(0, dialog.Tabs.SelectedIndex);
        dialog.Tabs.SelectedIndex = 2;
        dialog.UploadRightsBox.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var transfers = dialog.TabScroll(2);
        Assert.True(transfers.Extent.Height <= transfers.Viewport.Height + 1, $"{transfers.Extent.Height} {transfers.Viewport.Height} {dialog.Height}");
        dialog.UploadRightsBox.SelectedIndex = 0;
        dialog.Tabs.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        var connection = dialog.TabScroll(0);
        Assert.True(connection.Extent.Height > connection.Viewport.Height);
        var size = dialog.Bounds.Size;
        string[] files = ["server-settings-connection.png", "server-settings-proxy.png", "server-settings-transfers.png", "server-settings-listing.png"];
        for (var i = 0; i < 4; i++)
        {
            dialog.Tabs.SelectedIndex = i;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(size, dialog.Bounds.Size);
            Capture(dialog, files[i]);
        }

        Assert.True(dialog.ModeBox.IsEffectivelyVisible);
        Assert.True(dialog.Compress.IsEffectivelyVisible);
        dialog.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Settings_APermissionNumber_SaysWhatItAllows_AsYouType()
    {
        var (w, _, _) = await Open();
        var dialog = new ServerSettingsDialog(null, new ServerOptions(), null, null);
        dialog.Show();
        dialog.UploadRightsBox.SelectedIndex = 1;
        dialog.UploadNumber.Text = "644";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Owner: Read, Write · Group: Read · Others: Read", dialog.UploadMeaning.Text);
        dialog.UploadNumber.Text = "750";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Owner: Read, Write, Run · Group: Read, Run · Others: nothing", dialog.UploadMeaning.Text);
        dialog.Tabs.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        Capture(dialog, "server-settings-permissions.png");
        dialog.UploadNumber.Text = "79";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Type three digits from 0 to 7, like 644.", dialog.UploadMeaning.Text);
        dialog.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AProxyPassword_GoesToCredentialManager_NeverIntoTheSettings()
    {
        var (w, _, _) = await Open();
        var store = w.Services.Servers.Secrets;
        var dialog = new ServerSettingsDialog(null, new ServerOptions(), null, store);
        var closed = dialog.ShowDialog<ServerSettingsChoice?>(w);
        dialog.ProxyBox.SelectedIndex = 2;
        dialog.ProxyHost.Text = "proxy.example.com";
        dialog.ProxyUser.Text = "me";
        dialog.ProxyPassword.Text = "proxy-secret";
        Dispatcher.UIThread.RunJobs();
        dialog.Tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Capture(dialog, "server-settings-socks.png");
        Dispatcher.UIThread.RunJobs();
        dialog.Accept();
        var choice = await closed;
        Assert.NotNull(choice);
        Assert.Equal((ProxyKind.Socks5, "proxy.example.com", "me"), (choice!.Proxy!.Kind, choice.Proxy.Host, choice.Proxy.User));
        Assert.Equal("proxy-secret", store.Read("Salandual:proxy://me@proxy.example.com:1080"));
        Assert.DoesNotContain("proxy-secret", System.Text.Json.JsonSerializer.Serialize(choice.Over(new ServerOptions())));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Settings_ABadPermissionNumber_SaysSo()
    {
        var (w, _, _) = await Open();
        var dialog = new ServerSettingsDialog(null, new ServerOptions(), null, null);
        dialog.Show();
        dialog.UploadRightsBox.SelectedIndex = 1;
        dialog.UploadNumber.Text = "9x9";
        dialog.Accept();
        Assert.True(dialog.Error.IsVisible);
        Assert.True(dialog.IsVisible);
        dialog.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Authentication_Agent_HasNoKeyFile_AndIsKeptWithTheBookmark()
    {
        var (w, _, _) = await Open();
        Press(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Until(() => w.OpenConnectServer is { IsVisible: true });
        var connect = w.OpenConnectServer!;
        connect.Host.Text = "files.example.org";
        connect.User.Text = "deploy";
        connect.SignIn.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Agent", connect.SignIn.SelectedItem);
        Assert.False(connect.KeyFile.IsEffectivelyVisible);
        Assert.Equal(ServerSignIn.Agent, connect.Read()!.SignIn);

        connect.Kind.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ServerSignIn.Password, connect.Read()!.SignIn);
        connect.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Options_SavedSignIns_ListWhatIsKept_ForgetRemovesIt()
    {
        var (w, _, _) = await Open();
        w.Services.Settings.Update(s => s with
        {
            Servers = s.Servers with
            {
                Bookmarks = [new ServerBookmark { Name = "Website", Host = "files.example.org", User = "deploy" }, new ServerBookmark { Name = "Old mirror", Kind = ServerKind.Ftp, Host = "mirror.example.net", User = "anonymous" }],
            },
        });
        var store = w.Services.Servers.Secrets;
        var target = SecretSource.Target(ServerAddress.Parse("sftp://deploy@files.example.org")!, SecretKind.Password);
        store.Write(target, "deploy", "secret");
        store.Write("Salandual:proxy://me@proxy.example.com:1080", "me", "proxy-secret");

        var options = new OptionsWindow(w.Services) { Width = 1000, Height = 700 };
        options.Show();
        options.ShowCategory(OptionsWindow.ServersPage);
        Dispatcher.UIThread.RunJobs();
        var kept = options.KeptSignIns();
        Assert.Equal([("Website", "password"), ("Old mirror", "nothing"), ("Proxy proxy.example.com:1080", "proxy password")], kept.Select(k => (k.Who, k.What)));
        Capture(options, "options-ftp.png");

        Named<Button>(options, "Forget the kept password of Website").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(store.Read(target));
        Assert.NotNull(store.Read("Salandual:proxy://me@proxy.example.com:1080"));
        options.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Options_TrustedKeys_RemoveOne_AndTheDefaultsChange()
    {
        var (w, _, answers) = await Open();
        await OnServer(w, answers);
        var trusted = w.Services.Servers.Trusted;
        Assert.Single(trusted.Keys);

        var options = new OptionsWindow(w.Services) { Width = 1000, Height = 700 };
        options.Show();
        options.ShowCategory(OptionsWindow.ServersPage);
        Dispatcher.UIThread.RunJobs();
        Named<Button>(options, "Remove the trusted key of files.example.org:22").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(trusted.Keys);

        w.Services.Settings.Update(s => s with { Servers = s.Servers with { Defaults = new ServerOptions { Listing = new ListingOptions { ShowHidden = false } } } });
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.Services.Servers.Defaults.Listing.ShowHidden);
        options.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheStrip_HasFtpAndSftp_AndEachOpenServer()
    {
        var (w, _, answers) = await Open();
        w.Services.Settings.Update(s => s with { DriveMenu = s.DriveMenu with { Places = [.. s.DriveMenu.Places.Select(p => p.Id == DriveMenuSettings.Ftp ? p with { InStrip = true } : p)] } });
        Dispatcher.UIThread.RunJobs();
        var items = w.DriveStripItems();
        var ftp = Assert.Single(items, i => i.Setting == DriveMenuSettings.Ftp);
        Assert.Equal("FTP and SFTP", ftp.Label);

        await OnServer(w, answers);
        await Until(() => w.DriveStripItems().Any(i => i.Label == "Website"));
        var chip = w.DriveStripItems().First(i => i.Label == "Website");
        Assert.Equal("sftp://deploy@files.example.org/home/deploy", chip.Target);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlR_OnAServer_ListsFromTheServer_NotTheCache()
    {
        var (w, server, answers) = await Open();
        await OnServer(w, answers);
        var before = server.Listings;
        await w.ActivePanel.Panel.RefreshAsync();
        Assert.Equal(before, server.Listings);
        server.AddFile("/var/www/new.txt", "made elsewhere");
        Press(w, Key.R, RawInputModifiers.Control);
        for (var i = 0; i < 50; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(before + 1, server.Listings);
        await Until(() => w.ActivePanel.Panel.Items.Any(e => e.Name == "new.txt"));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task HiddenFilesOff_LeavesDotNamesOutOfTheServersPanel()
    {
        var (w, _, answers) = await Open();
        w.Services.Settings.Update(s => s with { Servers = s.Servers with { Defaults = new ServerOptions { Listing = new ListingOptions { ShowHidden = false } } } });
        Dispatcher.UIThread.RunJobs();
        await OnServer(w, answers, bookmark: null!);
        Assert.DoesNotContain(w.ActivePanel.Panel.Items, e => e.Name == ".htaccess");
        Assert.Contains(w.ActivePanel.Panel.Items, e => e.Name == "index.html");
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheDriveMenu_LeavesFtpOut_WhenItIsSwitchedOff()
    {
        var (w, _, _) = await Open();
        w.Services.Settings.Update(s => s with { DriveMenu = s.DriveMenu with { Places = [.. s.DriveMenu.Places.Select(p => p.Id == DriveMenuSettings.Ftp ? p with { InMenu = false } : p)] } });
        Press(w, Key.F1, RawInputModifiers.Alt);
        await Until(() => w.DriveMenuFlyout is { IsOpen: true });
        var texts = w.DriveMenuFlyout!.Items.OfType<MenuItem>().SelectMany(m => m.Header is Control c ? [.. c.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text), (c as TextBlock)?.Text] : new[] { m.Header?.ToString() }).ToList();
        Assert.DoesNotContain("FTP and SFTP", texts);
        Assert.DoesNotContain(texts, t => t?.StartsWith("Connect to a server", StringComparison.Ordinal) == true);
        w.DriveMenuFlyout.Hide();
        w.CloseNow();
    }
}
