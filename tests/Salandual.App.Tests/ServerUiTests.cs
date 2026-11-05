// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ServerUiTests.cs

using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.App.Panels;
using Salandual.Core.Operations;
using Salandual.Core.Settings;
using Salandual.Remote;

namespace Salandual.App.Tests;

public sealed class ServerUiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-servers-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Answers : IServerQuestions
    {
        public Queue<SecretAnswer?> Secrets { get; } = new();

        public List<string> Asked { get; } = [];

        public Task<SecretAnswer?> AskSecretAsync(ServerAddress server, SecretKind kind, bool wrongBefore, bool keptBefore, CancellationToken cancellationToken)
        {
            Asked.Add(kind.ToString());
            return Task.FromResult(Secrets.Count > 0 ? Secrets.Dequeue() : null);
        }

        public Task<TrustAnswer> AskTrustAsync(TrustQuestion question, CancellationToken cancellationToken)
        {
            Asked.Add("trust " + question.Algorithm);
            return Task.FromResult(TrustAnswer.Remember);
        }

        public Task<bool> AskPlainAsync(ServerAddress server, CancellationToken cancellationToken)
        {
            Asked.Add("plain");
            return Task.FromResult(true);
        }
    }

    private async Task<(MainWindow W, MemoryServer Server, Answers Answers)> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 640 };
        w.ShellAccess = new NoShellAccess();
        var servers = (MemoryServers)w.Services.Connector;
        var server = servers.Add("files.example.org");
        server.AddFile("/var/www/index.html", "<h1>Hello</h1>");
        server.AddFile("/var/www/release-2026-10.zip", "zip bytes");
        server.AddFile("/var/www/assets/logo.svg", "<svg/>", "rw-rw-r--");
        server.AddFolder("/home/deploy");
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

    private static async Task OnServer(MainWindow w, Answers answers)
    {
        answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: false));
        await w.OpenServerAsync(w.ActivePanel, new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!, Bookmark: "Website"));
        await Until(() => w.ActivePanel.Panel.Path == "sftp://deploy@files.example.org/var/www");
    }

    [AvaloniaFact]
    public async Task CtrlShiftF_ConnectToAServer_SavesABookmark_AndOpensTheServerInThePanel()
    {
        var (w, _, answers) = await Open();
        Press(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Until(() => w.OpenConnectServer is { IsVisible: true });
        var dialog = w.OpenConnectServer!;
        Assert.Equal("Connect to a server", dialog.Title);
        Assert.Equal("22", dialog.Port.Text);
        dialog.Host.Text = "files.example.org";
        dialog.User.Text = "deploy";
        dialog.StartIn.Text = "/var/www";
        dialog.NameBox.Text = "Website";
        Capture(dialog, "server-connect.png");

        dialog.SaveBookmark();
        var saved = Assert.Single(w.Services.Settings.Current.Servers.Bookmarks);
        Assert.Equal(("Website", ServerKind.Sftp, "files.example.org", 0, "deploy", "/var/www"), (saved.Name, saved.Kind, saved.Host, saved.Port, saved.User, saved.StartIn));

        dialog.Kind.SelectedIndex = 1;
        Assert.Equal("21", dialog.Port.Text);
        Assert.False(dialog.SignIn.IsEnabled);
        Assert.Equal(0, dialog.SignIn.SelectedIndex);
        dialog.Kind.SelectedIndex = 0;
        dialog.Port.Text = "0";
        dialog.Accept();
        Assert.True(dialog.Error.IsVisible);
        dialog.Port.Text = "22";

        answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: true));
        dialog.Accept();
        var host = w.ActivePanel;
        await Until(() => host.Panel.Path == "sftp://deploy@files.example.org/var/www");
        Assert.Equal(["trust ED25519", "Password"], answers.Asked);
        Assert.Equal("secret", w.Services.Secrets.Read("Salandual:sftp://deploy@files.example.org:22"));
        Assert.Equal(["..", "assets", "index.html", "release-2026-10.zip"], host.Panel.Items.Select(e => e.Name));
        Assert.Equal(new ServerFacts("SFTP", true), host.DirectoryLine.Server);
        Assert.Equal("rw-r--r--", host.View.Place!.Text(host.Panel.Items[2]));
        Assert.Equal([("deploy@files.example.org", "sftp://deploy@files.example.org/"), ("var", "sftp://deploy@files.example.org/var"), ("www", "sftp://deploy@files.example.org/var/www")],
            DirectoryLine.Split(host.Panel.Path));
        Capture(w, "server-panel.png");

        Focus(w, "assets");
        Press(w, Key.Enter);
        await Until(() => host.Panel.Path == "sftp://deploy@files.example.org/var/www/assets");
        Assert.Equal("rw-rw-r--", host.View.Place!.Text(host.Panel.Items.Single(e => e.Name == "logo.svg")));
        Press(w, Key.Back);
        await Until(() => host.Panel.Path == "sftp://deploy@files.example.org/var/www");
        Assert.Equal("assets", host.Panel.FocusedEntry?.Name);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F5_CopiesFromTheServerToTheOtherPanel_AndF3_ViewsACopy()
    {
        var (w, _, answers) = await Open();
        await OnServer(w, answers);
        Focus(w, "index.html");
        Press(w, Key.F5);
        await Until(() => w.OpenServerCopy is { IsVisible: true });
        var dialog = w.OpenServerCopy!;
        Assert.Equal("Copy 1 file from files.example.org", dialog.Title);
        Assert.Equal(_dir, dialog.To.Text);
        Capture(dialog, "server-copy.png");
        dialog.Accept();
        await Until(() => File.Exists(Path.Join(_dir, "index.html")));
        await Until(() => w.Operations.Jobs.All(j => j.IsFinished));
        Assert.Equal("<h1>Hello</h1>", File.ReadAllText(Path.Join(_dir, "index.html")));
        Assert.Contains("ZoneId=3", File.ReadAllText(Path.Join(_dir, "index.html") + ":Zone.Identifier"), StringComparison.Ordinal);

        Focus(w, "assets");
        var viewer = await w.ViewFromServerAsync(w.ActivePanel, alternate: false);
        Assert.Null(viewer);
        Focus(w, "index.html");
        viewer = await w.ViewFromServerAsync(w.ActivePanel, alternate: false);
        Assert.NotNull(viewer);
        Assert.StartsWith(w.Services.Temp.Root, viewer!.FilePath, StringComparison.OrdinalIgnoreCase);
        viewer.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F5ToAServer_CopiesYourFileThere_WithTheCopyDialogsOptions_AndTheQueue()
    {
        var (w, server, answers) = await Open();
        await OnServer(w, answers);
        File.WriteAllText(Path.Join(_dir, "local.txt"), "from my disk");
        Press(w, Key.Tab);
        await w.RightPanel.Panel.RefreshAsync();
        Focus(w, "local.txt");
        Press(w, Key.F5);
        await Until(() => w.OpenServerCopy is { IsVisible: true });
        var dialog = w.OpenServerCopy!;
        Assert.Equal("Copy 1 file to files.example.org", dialog.Title);
        Assert.Equal("sftp://deploy@files.example.org/var/www", dialog.To.Text);
        Assert.Equal("Add to queue  Shift+Enter", dialog.QueueButton.Content);
        dialog.ShowOptions();
        Assert.False(dialog.LimitBox.IsChecked);
        Capture(dialog, "server-upload.png");
        dialog.Accept();
        await Until(() => server.Files.ContainsKey("/var/www/local.txt"));
        await Until(() => w.Operations.Jobs.All(j => j.IsFinished));
        Assert.Equal("from my disk", System.Text.Encoding.UTF8.GetString(server.Files["/var/www/local.txt"]!));
        await Until(() => w.LeftPanel.Panel.Items.Any(e => e.Name == "local.txt"));
        Assert.True(File.Exists(Path.Join(_dir, "local.txt")));

        File.WriteAllText(Path.Join(_dir, "moved.txt"), "moving");
        await w.RightPanel.Panel.RefreshAsync();
        Focus(w, "moved.txt");
        Press(w, Key.F6);
        await Until(() => w.OpenServerCopy is { IsVisible: true, Title: "Move 1 file to files.example.org" });
        w.OpenServerCopy!.Accept();
        await Until(() => !File.Exists(Path.Join(_dir, "moved.txt")));
        Assert.Equal("moving", System.Text.Encoding.UTF8.GetString(server.Files["/var/www/moved.txt"]!));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F8OnAServer_AlwaysAsks_AndDeletesFoldersWithEverythingInThem()
    {
        var (w, server, answers) = await Open();
        await OnServer(w, answers);
        Focus(w, "assets");
        Press(w, Key.F8);
        await Until(() => w.OpenServerDelete is { IsVisible: true });
        var dialog = w.OpenServerDelete!;
        Assert.Equal("Delete 1 folder from files.example.org?", dialog.Title);
        Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "A server has no Recycle Bin: they can't be brought back.");
        Capture(dialog, "server-delete.png");
        dialog.Close("delete");
        await Until(() => !server.Files.ContainsKey("/var/www/assets"));
        Assert.False(server.Files.ContainsKey("/var/www/assets/logo.svg"));
        await Until(() => w.ActivePanel.Panel.Items.All(e => e.Name != "assets"));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F7AndF2OnAServer_MakeAFolder_AndRename()
    {
        var (w, server, answers) = await Open();
        await OnServer(w, answers);
        Press(w, Key.F7);
        await Until(() => w.OpenOperationDialog is Dialogs.NewFolderDialog);
        var dialog = (Dialogs.NewFolderDialog)w.OpenOperationDialog!;
        dialog.NameBox.Text = "index.html";
        Dispatcher.UIThread.RunJobs();
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        await Until(() => dialog.ErrorText.IsVisible);
        Assert.Same(dialog, w.OpenOperationDialog);
        dialog.NameBox.Text = "drafts";
        await Until(() => !dialog.ErrorText.IsVisible);
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        await Until(() => server.Files.TryGetValue("/var/www/drafts", out var f) && f is null);
        await Until(() => w.ActivePanel.Panel.FocusedEntry?.Name == "drafts");

        Focus(w, "index.html");
        Press(w, Key.F2);
        var rename = w.ActivePanel.Rename!;
        rename.Box.Text = "home.html";
        Press(w, Key.Enter);
        await rename.Finished;
        await Until(() => server.Files.ContainsKey("/var/www/home.html"));
        Assert.False(server.Files.ContainsKey("/var/www/index.html"));
        await Until(() => w.ActivePanel.Panel.FocusedEntry?.Name == "home.html");
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Permissions_TheTicksAndTheNumberGoTogether_AndTheServerTakesThem()
    {
        var (w, server, answers) = await Open();
        await OnServer(w, answers);
        Assert.Equal("Permissions", w.ActivePanel.View.Place!.Head);
        Focus(w, "index.html");
        Press(w, Key.Enter, RawInputModifiers.Alt);
        await Until(() => w.OpenPermissions is { IsVisible: true });
        var dialog = w.OpenPermissions!;
        Assert.Equal("Permissions · index.html", dialog.Title);
        Assert.Equal("644", dialog.Number.Text);
        Assert.Null(dialog.Scope);
        dialog.Boxes[2].IsChecked = true;
        Assert.Equal("744", dialog.Number.Text);
        dialog.Number.Text = "750";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([true, true, true, true, false, true, false, false, false], dialog.Boxes.Select(b => b.IsChecked == true));
        Capture(dialog, "server-permissions.png");
        dialog.Number.Text = "9x";
        dialog.Accept();
        Assert.True(dialog.Error.IsVisible);
        dialog.Number.Text = "750";
        Dispatcher.UIThread.RunJobs();
        dialog.Accept();
        await Until(() => server.Rights["/var/www/index.html"] == "rwxr-x---");
        await Until(() => w.ActivePanel.View.Place!.Text(w.ActivePanel.Panel.Items.Single(e => e.Name == "index.html")) == "rwxr-x---");
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F4OnAServer_AsksBeforeUploading_AndSaysWhenTheServersFileChanged()
    {
        var (w, server, answers) = await Open();
        await OnServer(w, answers);
        Focus(w, "index.html");
        var editor = await w.EditOnServerAsync(w.ActivePanel);
        Assert.NotNull(editor);
        await Until(() => editor!.Editor.Document.TextLength > 0);
        editor!.Editor.Document.Insert(0, "Edited. ");
        Assert.True(await editor.SaveAsync());
        await Until(() => w.OpenUploadEdited is { IsVisible: true });
        Assert.Equal("Upload index.html?", w.OpenUploadEdited!.Title);
        Capture(w.OpenUploadEdited, "server-upload-edited.png");
        w.OpenUploadEdited.Close("later");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("<h1>Hello</h1>", System.Text.Encoding.UTF8.GetString(server.Files["/var/www/index.html"]!));

        Assert.True(await editor.SaveAsync());
        await Until(() => w.OpenUploadEdited is { IsVisible: true, Title: "Upload index.html?" });
        w.OpenUploadEdited!.Close("always");
        await Until(() => System.Text.Encoding.UTF8.GetString(server.Files["/var/www/index.html"]!) == "Edited. <h1>Hello</h1>");
        await Until(() => w.Operations.Jobs.All(j => j.IsFinished));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        server.AddFile("/var/www/index.html", "their change, longer than before");
        editor.Editor.Document.Insert(0, "Again. ");
        Assert.True(await editor.SaveAsync());
        await Until(() => w.OpenUploadEdited is { IsVisible: true, Title: "index.html changed on the server" });
        w.OpenUploadEdited!.Close("later");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("their change, longer than before", System.Text.Encoding.UTF8.GetString(server.Files["/var/www/index.html"]!));
        editor.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AFileAlreadyThere_ThatLooksLikeACopyThatStopped_OffersResumeFirst()
    {
        var (w, _, _) = await Open();
        var job = new OperationJob(OperationKind.Copy, "Copying 1 file", "D: → files.example.org", ["D:"], _ => { });
        var mine = new ItemInfo("release.zip", FileAttributes.Normal, 48_210_944, DateTime.UtcNow, DateTime.UtcNow);
        var there = new ItemInfo("release.zip", FileAttributes.Normal, 20_971_520, DateTime.UtcNow, DateTime.UtcNow);
        var dialog = new Dialogs.ConflictDialog(job, new ConflictQuestion(@"D:\Work\release.zip", mine, "sftp://deploy@files.example.org/var/www/release.zip", there, 1, CanResume: true), Core.Listing.DisplayFormat.From(new PanelSettings(), System.Globalization.CultureInfo.CurrentCulture));
        dialog.Show(w);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(AnswerKind.Resume, dialog.Choices.Keys.First());
        Assert.Equal(2, dialog.Choices[AnswerKind.Resume].BorderThickness.Left);
        Capture(dialog, "server-resume.png");
        dialog.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheRightClickMenuOnAServer_HasStep2sItems()
    {
        var (w, _, answers) = await Open();
        await OnServer(w, answers);
        Focus(w, "index.html");
        w.Run("File.ContextMenu", Core.Commands.PanelTarget.Active);
        Dispatcher.UIThread.RunJobs();
        var headers = w.ServerMenu!.Items.OfType<MenuItem>().Select(i => i.Header?.ToString()).ToList();
        foreach (var expected in new[] { "View", "Edit", "Copy", "Move", "Rename", "Delete", "Permissions…", "New folder", "Disconnect" })
        {
            Assert.Contains(headers, h => h?.StartsWith(expected, StringComparison.Ordinal) == true);
        }

        Capture(w, "server-rmenu.png");
        w.ServerMenu.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheDriveMenu_ListsBookmarks_F12Disconnects_AndTheLogKeepsNoPassword()
    {
        var (w, _, answers) = await Open();
        w.Services.Settings.Update(s => s with { Servers = new ServerSettings { Bookmarks = [new ServerBookmark { Name = "Website", Host = "files.example.org", User = "deploy", StartIn = "/var/www" }] } });
        await OnServer(w, answers);

        w.ToggleDriveMenu(w.ActivePanel);
        Dispatcher.UIThread.RunJobs();
        var items = w.DriveMenuFlyout!.Items.OfType<MenuItem>().ToList();
        var texts = items.Select(i => string.Join(" ", i.Header is Control c ? c.GetSelfAndLogicalDescendants().OfType<TextBlock>().Select(t => t.Text) : [i.Header?.ToString()])).ToList();
        Assert.Contains(texts, t => t.Contains("FTP AND SFTP", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("Website connected", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("Connect to a server…", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("Disconnect from the server", StringComparison.Ordinal));
        Capture(w, "server-menu.png");
        w.ToggleDriveMenu(w.ActivePanel);
        Dispatcher.UIThread.RunJobs();

        w.Run("Server.Log", Core.Commands.PanelTarget.Active);
        await Until(() => w.OpenServerLog is { IsVisible: true });
        Assert.Contains("Signed in as deploy", w.OpenServerLog!.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", w.OpenServerLog.Text, StringComparison.Ordinal);
        Capture(w.OpenServerLog, "server-log.png");
        w.OpenServerLog.Close();

        w.Activate();
        w.ActivePanel.View.Focus();
        Press(w, Key.F12);
        await Until(() => w.ActivePanel.Panel.Path == _dir);
        Assert.Empty(w.Services.Servers.OpenServers);
        Assert.Null(w.ActivePanel.DirectoryLine.Server);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ARightClickOnTheServer_OpensTheServersMenu_NotWindows()
    {
        var (w, _, answers) = await Open();
        await OnServer(w, answers);
        var view = w.ActivePanel.View;
        var r = view.NameBounds(w.ActivePanel.Panel.Items.ToList().FindIndex(e => e.Name == "index.html"))!.Value;
        var p = view.TranslatePoint(new Point(r.X + 20, r.Y + (r.Height / 2)), w)!.Value;
        w.MouseDown(p, MouseButton.Right);
        w.MouseUp(p, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(w.QuickMenu);
        var headers = w.ServerMenu!.Items.OfType<MenuItem>().Select(i => i.Header?.ToString()).ToList();
        Assert.Contains("View", headers);
        Assert.Contains("Disconnect", headers);
        Assert.Contains("Copy the server's address", headers);
        Assert.Contains("Save as a bookmark…", headers);
        Assert.Contains("Connect to another server…", headers);
        w.ServerMenu.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ATypedAddress_Connects_ButNeverOneWithAPassword()
    {
        var (w, _, answers) = await Open();
        answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: false));
        var host = w.ActivePanel;
        await host.Panel.NavigateAsync("sftp://deploy@files.example.org/var/www/assets");
        await Until(() => host.Panel.Path == "sftp://deploy@files.example.org/var/www/assets");
        Assert.Equal(["..", "logo.svg"], host.Panel.Items.Select(e => e.Name));
        Assert.Equal("sftp://deploy@files.example.org/var/www", Platform.Windows.FileSystem.PathInput.Resolve("..", host.Panel.Path));
        Assert.Equal("sftp://deploy@files.example.org/home/deploy", Platform.Windows.FileSystem.PathInput.Resolve("/home/deploy", host.Panel.Path));
        Assert.Null(Platform.Windows.FileSystem.PathInput.Resolve("sftp://deploy:secret@files.example.org/", _dir));

        Assert.False(await host.Panel.NavigateAsync("sftp://deploy:secret@files.example.org/"));
        Assert.Equal("sftp://deploy@files.example.org/var/www/assets", host.Panel.Path);
        Assert.DoesNotContain(System.Text.Json.JsonSerializer.Serialize(w.Services.Settings.Current.History), "secret", StringComparison.Ordinal);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ASlowServer_GreysTheListAndSaysItIsWaiting_AndEscStops()
    {
        var (w, server, answers) = await Open();
        await OnServer(w, answers);
        var host = w.ActivePanel;
        var slow = new TaskCompletionSource();
        server.Hold = slow.Task;
        Focus(w, "assets");

        Press(w, Key.Enter);
        Assert.NotNull(host.Panel.LoadingSince);
        await Until(() => host.View.IsWaiting);
        await Until(() => host.Info.LeftText.StartsWith("Waiting for the server…", StringComparison.Ordinal));
        Assert.EndsWith("· Esc stops", host.Info.LeftText, StringComparison.Ordinal);
        Capture(w, "ftp-waiting.png");

        Press(w, Key.Escape);
        await Until(() => !host.View.IsWaiting);
        Assert.Equal("sftp://deploy@files.example.org/var/www", host.Panel.Path);
        Assert.DoesNotContain("Waiting", host.Info.LeftText, StringComparison.Ordinal);

        slow.SetResult();
        server.Hold = null;
        Focus(w, "assets");
        Press(w, Key.Enter);
        await Until(() => host.Panel.Path == "sftp://deploy@files.example.org/var/www/assets");
        Assert.False(host.View.IsWaiting);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AWaitingPanel_TakesNothingButEsc_TheFocusStays_AndTheOtherPanelWorks()
    {
        var (w, server, answers) = await Open();
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "b");
        await w.RightPanel.Panel.RefreshAsync();
        await OnServer(w, answers);
        var host = w.ActivePanel;
        var slow = new TaskCompletionSource();
        server.Hold = slow.Task;
        Focus(w, "assets");
        var focus = host.Panel.Focus;

        Press(w, Key.Enter);
        Assert.True(host.View.IsLocked);
        Assert.False(host.DirectoryLine.IsHitTestVisible);
        Press(w, Key.Down);
        Press(w, Key.End);
        Press(w, Key.F7);
        w.KeyTextInput("r");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(focus, host.Panel.Focus);
        Assert.Null(host.Panel.QuickSearchText);
        Assert.Equal("This panel is waiting. Esc stops it, and Tab goes to the other panel.", w.NoticeMessage);

        await Until(() => host.View.IsWaiting);
        var row = host.View.TranslatePoint(new Point(60, host.View.RowHeight * 4.5), w)!.Value;
        w.MouseDown(row, MouseButton.Left);
        w.MouseUp(row, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        await Until(() => host.Info.LeftText.StartsWith("Waiting for the server", StringComparison.Ordinal));
        Assert.Equal(focus, host.Panel.Focus);
        Capture(w, "ftp-waiting-locked.png");

        Press(w, Key.Tab);
        Assert.Same(w.RightPanel, w.ActivePanel);
        var before = w.RightPanel.Panel.Focus;
        Press(w, Key.Down);
        Assert.Equal(before + 1, w.RightPanel.Panel.Focus);

        Press(w, Key.Tab);
        Press(w, Key.Escape);
        await Until(() => !host.View.IsLocked);
        Assert.True(host.DirectoryLine.IsHitTestVisible);
        Press(w, Key.Down);
        Assert.Equal(focus + 1, host.Panel.Focus);
        slow.SetResult();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AServerThatDoesntAnswer_IsGivenUpOn_WithAReason_AndCtrlRConnectsAgain()
    {
        var (w, server, answers) = await Open();
        await OnServer(w, answers);
        var host = w.ActivePanel;
        w.Services.Servers.ListTimeout = TimeSpan.FromMilliseconds(600);
        var never = new TaskCompletionSource();
        server.Hold = never.Task;
        Focus(w, "assets");

        Press(w, Key.Enter);
        await Until(() => !host.View.IsLocked);
        Assert.Equal("sftp://deploy@files.example.org/var/www", host.Panel.Path);
        Assert.Equal("files.example.org didn't answer in 1 s. The panel stays where it was: Ctrl+R tries again.", w.NoticeMessage);
        var log = w.Services.Servers.LogOf(ServerAddress.Parse("sftp://deploy@files.example.org")!);
        Assert.Contains(log.Lines, l => l.Text.Contains("No answer in 1 s", StringComparison.Ordinal));

        server.Hold = null;
        var connects = server.Connects;
        Press(w, Key.R, RawInputModifiers.Control);
        await Until(() => server.Connects == connects + 1);
        Focus(w, "assets");
        Press(w, Key.Enter);
        await Until(() => host.Panel.Path == "sftp://deploy@files.example.org/var/www/assets");
        never.SetResult();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AKeptPassword_IsSeenInConnectToAServer_AndForgetRemovesIt()
    {
        var (w, _, answers) = await Open();
        answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: true));
        await w.OpenServerAsync(w.ActivePanel, new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!));
        await Until(() => w.ActivePanel.Panel.Path == "sftp://deploy@files.example.org/var/www");
        var target = "Salandual:sftp://deploy@files.example.org:22";
        Assert.NotNull(w.Services.Servers.Secrets.Read(target));

        Press(w, Key.F10, RawInputModifiers.Shift);
        var headers = w.ServerMenu!.Items.OfType<MenuItem>().Select(i => i.Header?.ToString()).ToList();
        Assert.Contains("Forget the kept password", headers);
        w.ServerMenu.Close();

        Press(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Until(() => w.OpenConnectServer is { IsVisible: true });
        var dialog = w.OpenConnectServer!;
        Assert.True(dialog.Forget.IsVisible);
        Assert.Equal("Forget password", dialog.Forget.Content);
        Capture(dialog, "server-kept.png");
        dialog.Forget.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(w.Services.Servers.Secrets.Read(target));
        Assert.False(dialog.Forget.IsVisible);

        dialog.User.Text = "someone";
        Assert.False(dialog.Forget.IsVisible);
        dialog.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheConnectingWindow_ShowsAtOnceWithTheLog_AndStaysUntilClosed()
    {
        var (w, server, answers) = await Open();
        Assert.False(new ServerSettings().CloseConnecting);
        w.Services.Settings.Update(s => s with { Servers = s.Servers with { CloseConnecting = false } });
        var slow = new TaskCompletionSource();
        server.ConnectHold = slow.Task;
        answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: false));
        var opening = w.OpenServerAsync(w.ActivePanel, new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!, Bookmark: "Website"));
        Assert.True(w.OpenConnecting is { IsVisible: true });
        var connecting = w.OpenConnecting!;
        Assert.Equal("Connecting · Website", connecting.Title);
        await Until(() => connecting.Text.Contains("files.example.org", StringComparison.Ordinal));
        Assert.False(connecting.CloseWhenConnected.IsChecked);
        Capture(connecting, "server-connecting.png");

        slow.SetResult();
        Assert.True(await opening);
        Assert.Equal("sftp://deploy@files.example.org/var/www", w.ActivePanel.Panel.Path);
        Assert.True(connecting.Connected);
        Assert.True(connecting.CloseButton.IsVisible);
        Assert.False(connecting.Cancel.IsVisible);
        Assert.Contains("Signed in as deploy", connecting.Text, StringComparison.Ordinal);
        Capture(connecting, "server-connected.png");
        connecting.CloseButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(connecting.IsClosed);
        Assert.Equal("sftp://deploy@files.example.org/var/www", w.ActivePanel.Panel.Path);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CloseWhenConnected_IsKeptAsTheSetting_AndThenTheWindowShowsOnlyWhenSlow_AndClosesByItself()
    {
        var (w, server, answers) = await Open();
        w.Services.Settings.Update(s => s with { Servers = s.Servers with { CloseConnecting = false } });
        var slow = new TaskCompletionSource();
        server.ConnectHold = slow.Task;
        answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: false));
        var target = new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!);
        var opening = w.OpenServerAsync(w.ActivePanel, target);
        var connecting = w.OpenConnecting!;
        connecting.CloseWhenConnected.IsChecked = true;
        Assert.True(w.Services.Settings.Current.Servers.CloseConnecting);
        slow.SetResult();
        Assert.True(await opening);
        Assert.True(connecting.IsClosed);

        await w.DisconnectServerAsync(w.ActivePanel);
        var slow2 = new TaskCompletionSource();
        server.ConnectHold = slow2.Task;
        answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: false));
        opening = w.OpenServerAsync(w.ActivePanel, target);
        Assert.Same(connecting, w.OpenConnecting);
        await Until(() => w.OpenConnecting is { IsVisible: true } && !ReferenceEquals(w.OpenConnecting, connecting));
        var second = w.OpenConnecting!;
        Assert.True(second.CloseWhenConnected.IsChecked);
        slow2.SetResult();
        Assert.True(await opening);
        Assert.True(second.IsClosed);

        var options = new Salandual.App.Options.OptionsWindow();
        Assert.Contains(Salandual.App.Options.OptionsWindow.ServersPage, options.Categories);
        options.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AConnectionThatFails_KeepsTheConnectingWindowWithTheReason_AndTryAgainConnects()
    {
        var (w, server, answers) = await Open();
        var slow = new TaskCompletionSource();
        server.ConnectHold = slow.Task;
        server.Refuse = "Connection refused";
        var target = new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!);
        var opening = w.OpenServerAsync(w.ActivePanel, target);
        await Until(() => w.OpenConnecting is { IsVisible: true });
        var connecting = w.OpenConnecting!;
        slow.SetResult();
        Assert.False(await opening);
        Assert.True(connecting.Failed);
        Assert.False(connecting.IsClosed);
        Assert.Contains("Connection refused", connecting.Error.Text, StringComparison.Ordinal);
        Assert.True(connecting.Retry.IsVisible);
        Assert.False(connecting.Cancel.IsVisible);
        Capture(connecting, "server-connecting-failed.png");

        server.Refuse = null;
        server.ConnectHold = null;
        answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: false));
        connecting.Retry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Until(() => w.ActivePanel.Panel.Path == "sftp://deploy@files.example.org/var/www");
        Assert.True(connecting.IsClosed);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CancelInTheConnectingWindow_StopsConnecting()
    {
        var (w, server, _) = await Open();
        var never = new TaskCompletionSource();
        server.ConnectHold = never.Task;
        var opening = w.OpenServerAsync(w.ActivePanel, new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!));
        await Until(() => w.OpenConnecting is { IsVisible: true });
        var connecting = w.OpenConnecting!;
        connecting.Cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.False(await opening);
        Assert.True(connecting.IsClosed);
        Assert.Equal("Not connected.", w.NoticeMessage);
        Assert.Equal(_dir, w.ActivePanel.Panel.Path);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F3OnAServer_ShowsHowFarTheCopyIs_InTheNotice()
    {
        var (w, server, answers) = await Open();
        await OnServer(w, answers);
        var half = new TaskCompletionSource();
        server.DownloadHold = half.Task;
        Focus(w, "release-2026-10.zip");
        var viewing = w.ViewFromServerAsync(w.ActivePanel, alternate: false);
        await Until(() => w.NoticeProgress is not null);
        Assert.Equal("Copying release-2026-10.zip from the server…", w.NoticeMessage);
        Assert.Contains(" of ", w.NoticeProgress, StringComparison.Ordinal);
        Assert.Contains("Cancel", w.NoticeButtons.Select(b => b.Content?.ToString()));
        Capture(w, "server-fetch-progress.png");
        half.SetResult();
        var viewer = await viewing;
        Assert.NotNull(viewer);
        viewer!.Close();

        var line = w.FetchProgress(20_300_000, 48_200_000, TimeSpan.FromSeconds(5));
        Assert.Matches(@"^\S+ \S+ of \S+ \S+ · \S+ \S+/s · .+ left$", line);

        Assert.DoesNotContain(" left", w.FetchProgress(4_000, 0, TimeSpan.FromSeconds(1)), StringComparison.Ordinal);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheCopyDialogs_HaveTheProgressWindowsCloseWhenDone_TheSameSetting()
    {
        var (w, _, answers) = await Open();
        await OnServer(w, answers);
        Focus(w, "index.html");
        Press(w, Key.F5);
        await Until(() => w.OpenServerCopy is { IsVisible: true });
        var dialog = w.OpenServerCopy!;
        Assert.True(dialog.CloseWhenDone.IsVisible);
        var before = w.Services.Settings.Current.Operations.CloseWhenDone;
        Assert.Equal(before, dialog.CloseWhenDone.IsChecked);
        dialog.CloseWhenDone.IsChecked = !before;
        Assert.Equal(!before, w.Services.Settings.Current.Operations.CloseWhenDone);
        Capture(dialog, "server-copy-close-when-done.png");
        dialog.Close();
        w.CloseNow();
    }
}
