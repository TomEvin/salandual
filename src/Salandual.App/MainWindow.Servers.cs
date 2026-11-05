// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Servers.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Salandual.App.Dialogs;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.App.Theme;
using Salandual.Core.Operations;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Remote;

namespace Salandual.App;

public partial class MainWindow
{
    private static readonly HashSet<string> _onServer =
    [
        "File.View", "File.ViewAlternate", "File.Copy", "File.ContextMenu", "Panel.DirectoryContextMenu", "Clipboard.CopyName", "Clipboard.CopyFullName", "Clipboard.CopyPath",
        "Network.DisconnectDrive", "Panel.ChangeDirectory",
    ];

    private CancellationTokenSource? _serverCopy;

    internal ConnectServerDialog? OpenConnectServer { get; private set; }

    internal ServerSecretDialog? OpenServerSecret { get; private set; }

    internal ChoiceDialog? OpenServerQuestion { get; private set; }

    internal ServerLogWindow? OpenServerLog { get; private set; }

    internal ContextMenu? ServerMenu { get; private set; }

    internal IServerQuestions? ServerAnswers { get; set; }

    private RemoteFileSystem Servers => _services.Servers;

    private void WireServers()
    {
        Servers.Questions = new WindowQuestions(this);
        ApplyServerSettings(_services.Settings.Current);
        if (!_services.IsIsolated)
        {
            Servers.StartKeepAlive();
        }

        Servers.BookmarkFor = address => _services.Settings.Current.Servers.Bookmarks.FirstOrDefault(b => ServerAddress.Of(b).SameServer(address)) is { } b ? TargetOf(b) : null;
        Servers.PlainAllowed += target => Dispatcher.UIThread.Post(() => KeepPlainAllowed(target));
        Servers.ServersChanged += () => Dispatcher.UIThread.Post(() =>
        {
            UpdateStrips();
            foreach (var host in _panels)
            {
                UpdateServerState(host);
            }
        });
    }

    private ServerTarget TargetOf(ServerBookmark b)
        => new(ServerAddress.Of(b), b.SignIn, b.KeyFile, b.PlainAllowed, b.Name, ServerOptions.For(b, _services.Settings.Current.Servers.Defaults));

    private void ApplyServerSettings(AppSettings s)
    {
        Servers.Defaults = s.Servers.Defaults;
        Servers.Trusted.ReadKnownHosts = s.Servers.ReadKnownHosts;
        Servers.Reconfigure();
    }

    internal static bool OnServer(PanelHost host) => ServerAddress.IsServerPath(host.Panel.Path) && host.Panel.Results is null;

    private void UpdateServerState(PanelHost host)
    {
        if (OnServer(host))
        {
            var open = Servers.ServerOf(host.Panel.Path);
            host.DirectoryLine.Server = open is null ? null : new ServerFacts(open.Security, open.Encrypted);
            var folder = host.Panel.Path;
            host.View.Place = new PlaceColumns(AppStrings.Server_PermissionsHead, e => Servers.RightsOf(folder, e.Name));
        }
        else
        {
            host.DirectoryLine.Server = null;
        }
    }

    private void KeepLastUsed(ServerAddress address)
    {
        var now = DateTimeOffset.UtcNow;
        _services.Settings.Update(s => s with
        {
            Servers = s.Servers with { Bookmarks = [.. s.Servers.Bookmarks.Select(b => ServerAddress.Of(b).SameServer(address) ? b with { LastUsed = now } : b)] },
        });
    }

    private void KeepPlainAllowed(ServerTarget target)
        => _services.Settings.Update(s => s with
        {
            Servers = s.Servers with { Bookmarks = [.. s.Servers.Bookmarks.Select(b => b.Name == target.Bookmark ? b with { PlainAllowed = true } : b)] },
        });

    private bool RunServerCommand(string id, PanelHost host)
    {
        switch (id)
        {
            case "Server.Connect":
                _ = ConnectServerAsync(host);
                return true;
            case "Server.Disconnect":
                _ = DisconnectServerAsync(host);
                return true;
            case "Server.Log":
                ShowServerLog(host);
                return true;
            default:
                return false;
        }
    }

    private bool ForgetServerListing(PanelHost host)
    {
        if (OnServer(host))
        {
            Servers.ForgetListing(host.Panel.Path);
        }

        return false;
    }

    private bool RunOnServer(string id, PanelHost host)
    {
        if (id is "File.Copy" or "File.Move" or "File.CopyToEach" && !OnServer(host) && OnServer(OtherPanel(host)))
        {
            if (id == "File.CopyToEach" || ArchiveAt(host) is not null || !Path.IsPathFullyQualified(host.Panel.Path) || Core.Vfs.ShellPlace.IsPlace(host.Panel.Path) || host.Panel.Results is not null)
            {
                ShowNotice(AppStrings.Server_UploadFromDisk, 7, (AppStrings.Common_OK, () => { }));
            }
            else
            {
                _ = CopyToServerAsync(host, move: id == "File.Move");
            }

            return true;
        }

        if (!OnServer(host))
        {
            return false;
        }

        if (id == "Panel.Refresh")
        {
            return ForgetServerListing(host);
        }

        switch (id)
        {
            case "File.View" or "File.ViewAlternate":
                _ = ViewFromServerAsync(host, alternate: id == "File.ViewAlternate");
                return true;
            case "File.Copy" or "File.Move":
                _ = CopyFromServerAsync(host, move: id == "File.Move");
                return true;
            case "File.Delete" or "File.DeleteInverted":
                _ = DeleteOnServerAsync(host);
                return true;
            case "File.CreateDirectory":
                _ = NewFolderOnServerAsync(host);
                return true;
            case "File.QuickRename":
                QuickRenameOnServer(host);
                return true;
            case "File.Edit":
                _ = EditOnServerAsync(host);
                return true;
            case "File.EditNew":
                _ = EditNewOnServerAsync(host);
                return true;
            case "File.Properties" or "File.WindowsProperties":
                _ = PermissionsOnServerAsync(host);
                return true;
            case "Network.DisconnectDrive":
                _ = DisconnectServerAsync(host);
                return true;
            case "File.ContextMenu" or "Panel.DirectoryContextMenu":
                ShowServerMenu(host, id == "File.ContextMenu" && host.View.NameBounds(host.Panel.Focus) is { } r ? new Point(r.X + 24, r.Bottom) : new Point(40, 40), background: id == "Panel.DirectoryContextMenu");
                return true;
            case "Clipboard.CopyName" or "Clipboard.CopyFullName" or "Clipboard.CopyPath":
                CopyServerText(host, id);
                return true;
        }

        if (!_onServer.Contains(id) && NeedsFolder(id))
        {
            ShowNotice(AppStrings.Server_NotYet, 7, (AppStrings.Common_OK, () => { }));
            return true;
        }

        return false;
    }

    private static string? FocusedOnServer(PanelHost host, bool filesOnly)
        => host.Panel.FocusedEntry is { } entry && !host.Panel.IsUp(host.Panel.Focus) && (!filesOnly || !entry.IsDirectory) && ServerAddress.Parse(host.Panel.Path) is { } folder
            ? folder.Child(entry.Name).ToString()
            : null;

    private void CopyServerText(PanelHost host, string id)
    {
        var text = id switch
        {
            "Clipboard.CopyPath" => host.Panel.Path,
            "Clipboard.CopyName" => host.Panel.FocusedEntry is { } e && !host.Panel.IsUp(host.Panel.Focus) ? e.Name : null,
            _ => FocusedOnServer(host, filesOnly: false),
        };
        if (text is not null)
        {
            ShowNotice(FileClipboardAccess.SetText(text) ? Loc.Format(AppStrings.Notice_CopiedText, text) : AppStrings.Notice_ClipboardBusy);
        }
    }

    internal async Task ConnectServerAsync(PanelHost host, bool fresh = false)
    {
        var bookmarks = _services.Settings.Current.Servers.Bookmarks;
        var here = fresh ? null : ServerAddress.Parse(host.Panel.Path);
        var start = here is null ? null
            : bookmarks.FirstOrDefault(b => ServerAddress.Of(b).SameServer(here)) ?? new ServerBookmark { Kind = here.Kind, Host = here.Host, Port = here.Port, User = here.User };
        var dialog = OpenConnectServer = new ConnectServerDialog(bookmarks, start, list => _services.Settings.Update(s => s with { Servers = s.Servers with { Bookmarks = [.. list] } }), Servers.Secrets, _services.Settings.Current.Servers.Defaults);
        ConnectServerResult? result;
        try
        {
            result = await dialog.ShowDialog<ConnectServerResult?>(this);
        }
        finally
        {
            host.View.Focus();
        }

        if (result is not null)
        {
            var b = result.Server;
            await OpenServerAsync(host, new ServerTarget(ServerAddress.Of(b), b.SignIn, b.KeyFile, b.PlainAllowed, result.Saved ? b.Name : null));
        }
    }

    internal async Task<bool> OpenServerAsync(PanelHost host, ServerTarget target)
    {
        ShowNotice(Loc.Format(AppStrings.Server_Connecting, target.Address.Host), 30);
        var name = target.Bookmark ?? target.Address.Host;
        using var cts = new CancellationTokenSource();
        var connecting = new ConnectingWindow(name, target.Address, Servers.LogOf(target.Address), FileClipboardAccess.SetText,
            onCancel: () =>
            {
                cts.Cancel();
                host.Panel.CancelLoading();
            },
            onRetry: () => _ = OpenServerAsync(host, target),
            closeWhenConnected: _services.Settings.Current.Servers.CloseConnecting,
            keepCloseWhenConnected: on => _services.Settings.Update(s => s with { Servers = s.Servers with { CloseConnecting = on } }));
        _connecting = connecting;
        _connectingShown = false;
        _connectingDue = false;
        var connected = false;
        var showTimer = DispatcherTimer.RunOnce(() => ShowConnecting(connecting), TimeSpan.FromMilliseconds(_services.Settings.Current.Servers.CloseConnecting ? ConnectingWindow.ShowAfterMs : 1));
        if (!_services.Settings.Current.Servers.CloseConnecting)
        {
            ShowConnecting(connecting);
        }

        string path;
        try
        {
            try
            {
                path = await Servers.ConnectAsync(target, cts.Token);
                KeepLastUsed(target.Address);
            }
            catch (Exception ex) when (ex is ConnectCancelledException or OperationCanceledException)
            {
                ShowNotice(AppStrings.Server_NotConnected);
                return false;
            }
            catch (ServerKeyChangedException ex)
            {
                showTimer.Dispose();
                EndConnecting(connecting, connected: false);
                await AskAsync(AppStrings.Server_KeyChangedTitle, [ex.Message, ex.Algorithm + " " + ex.Fingerprint], [new Choice("ok", AppStrings.Common_OK, Primary: true)], "ok");
                ShowNotice(AppStrings.Server_NotConnected);
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                showTimer.Dispose();
                if (ReferenceEquals(_connecting, connecting) && _connectingShown)
                {
                    _connecting = null;
                    Notice.IsVisible = false;
                    connecting.ShowFailed(ex.Message);
                }
                else
                {
                    ShowNotice(ex.Message, 10, (AppStrings.Server_ShowLog, () => ShowServerLog(target.Address, target.Bookmark)));
                }

                return false;
            }

            ShowNotice(Loc.Format(AppStrings.Server_Connected, name));
            await host.Panel.NavigateAsync(path);
            UpdateServerState(host);
            connected = true;
            return true;
        }
        finally
        {
            showTimer.Dispose();
            EndConnecting(connecting, connected);
        }
    }

    private ConnectingWindow? _connecting;
    private bool _connectingShown;
    private bool _connectingDue;
    private int _askingServer;

    internal ConnectingWindow? OpenConnecting { get; private set; }

    private void ShowConnecting(ConnectingWindow window)
    {
        if (ReferenceEquals(_connecting, window))
        {
            _connectingDue = true;
        }

        if (!ReferenceEquals(_connecting, window) || _askingServer > 0 || window.IsClosed)
        {
            return;
        }

        OpenConnecting = window;
        if (_connectingShown)
        {
            window.Show();
        }
        else
        {
            _connectingShown = true;
            window.Show(this);
        }
    }

    private void EndConnecting(ConnectingWindow window, bool connected)
    {
        if (!ReferenceEquals(_connecting, window))
        {
            return;
        }

        _connecting = null;
        if (window.Failed || !_connectingShown)
        {
            return;
        }

        if (connected)
        {
            window.Done();
        }
        else
        {
            window.Abandon();
        }
    }

    private Task<T> WhileAskingAsync<T>(Func<Task<T>> ask)
        => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            AskingServer(true);
            try
            {
                return await ask();
            }
            finally
            {
                AskingServer(false);
            }
        });

    private void AskingServer(bool asking)
    {
        _askingServer = Math.Max(0, _askingServer + (asking ? 1 : -1));
        if (_connecting is not { } window || !_connectingDue)
        {
            return;
        }

        if (asking)
        {
            if (_connectingShown && !window.IsClosed)
            {
                window.Hide();
            }
        }
        else
        {
            ShowConnecting(window);
        }
    }

    internal async Task DisconnectServerAsync(PanelHost host, ServerAddress? server = null)
    {
        server ??= ServerAddress.Parse(host.Panel.Path) ?? Servers.OpenServers.FirstOrDefault()?.Server;
        if (server is null)
        {
            ShowNotice(AppStrings.Server_NoneOpen);
            return;
        }

        foreach (var panel in _panels.Where(p => ServerAddress.Parse(p.Panel.Path)?.SameServer(server) == true))
        {
            await panel.Panel.NavigateAsync(LocalFolderBefore(panel));
        }

        await Servers.DisconnectAsync(server);
        ShowNotice(Loc.Format(AppStrings.Server_Disconnected, server.Host));
    }

    private static string LocalFolderBefore(PanelHost host)
        => host.Panel.BackHistory.Reverse().Select(e => e.Path).FirstOrDefault(p => !ServerAddress.IsServerPath(p) && Path.IsPathFullyQualified(p) && Directory.Exists(p))
           ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    internal async Task<Viewer.ViewerWindow?> ViewFromServerAsync(PanelHost host, bool alternate)
    {
        if (FocusedOnServer(host, filesOnly: true) is not { } path || ServerAddress.Parse(path) is not { } file
            || await FetchAsync(file, host.Panel.FocusedEntry?.Size ?? 0) is not { } copy)
        {
            return null;
        }

        var kind = _services.Settings.Current.Viewers.KindFor(Path.GetFileName(copy), alternate);
        return OpenViewer(host, copy, kind, siblings: [copy]);
    }

    private async Task<string?> FetchAsync(ServerAddress file, long total)
    {
        if (RemoteDownload.SafeLocalName(file.Name) is not { } name)
        {
            ShowNotice(Core.Resources.Strings.Server_BadName);
            return null;
        }

        _serverCopy?.Cancel();
        var cts = _serverCopy = new CancellationTokenSource();
        var copy = Path.Combine(_services.Temp.NewFolder("server"), name);
        ShowNotice(Loc.Format(AppStrings.Server_Fetching, name), 30, (AppStrings.Common_Cancel, () => cts.Cancel()));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long lastShown = -1000;
        void Progress(long done)
        {
            var now = clock.ElapsedMilliseconds;
            if (now - Interlocked.Read(ref lastShown) < 200)
            {
                return;
            }

            Interlocked.Exchange(ref lastShown, now);
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_serverCopy, cts) && !cts.IsCancellationRequested)
                {
                    ShowNoticeProgress(total > 0 ? (double)done / total : 0, FetchProgress(done, total, clock.Elapsed));
                }
            });
        }

        try
        {
            await using (var to = new FileStream(copy, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                await Servers.DownloadAsync(file, to, 0, Progress, cts.Token);
            }

            RemoteDownload.MarkAsDownloaded(copy, file);
        }
        catch (OperationCanceledException)
        {
            ShowNotice(AppStrings.Server_FetchStopped);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice(ex.Message, 10, (AppStrings.Server_ShowLog, () => ShowServerLog(file, null)));
            return null;
        }
        finally
        {
            if (ReferenceEquals(_serverCopy, cts))
            {
                _serverCopy = null;
            }
        }

        Notice.IsVisible = false;
        return copy;
    }

    internal string FetchProgress(long done, long total, TimeSpan elapsed)
    {
        var format = CurrentFormat();
        var speed = elapsed.TotalSeconds > 0.2 ? done / elapsed.TotalSeconds : 0;
        if (speed <= 0)
        {
            return total > 0 ? Loc.Format(AppStrings.Server_FetchOf, format.Size(done), format.Size(total)) : format.Size(done);
        }

        var speedText = Salandual.App.Operations.Ui.Speed(format, speed);
        if (total <= 0)
        {
            return Loc.Format(AppStrings.Server_FetchProgressNoSize, format.Size(done), speedText);
        }

        var left = speed > 0 ? TimeSpan.FromSeconds(Math.Max(0, total - done) / speed) : (TimeSpan?)null;
        return left is { } l
            ? Loc.Format(AppStrings.Server_FetchProgress, format.Size(done), format.Size(total), speedText, Salandual.App.Operations.Ui.Left(l))
            : Loc.Format(AppStrings.Server_FetchProgressNoSize, Loc.Format(AppStrings.Server_FetchOf, format.Size(done), format.Size(total)), speedText);
    }

    private void ShowServerLog(PanelHost host)
    {
        if (ServerAddress.Parse(host.Panel.Path) is { } here)
        {
            ShowServerLog(here, Servers.ServerOf(host.Panel.Path)?.Bookmark);
        }
        else if (Servers.OpenServers.LastOrDefault() is { } last)
        {
            ShowServerLog(last.Server, last.Bookmark);
        }
        else
        {
            ShowNotice(AppStrings.Server_NoneOpen);
        }
    }

    internal void ShowServerLog(ServerAddress server, string? bookmark)
    {
        OpenServerLog?.Close();
        var window = OpenServerLog = new ServerLogWindow(bookmark ?? server.Host, Servers.LogOf(server), FileClipboardAccess.SetText);
        window.Show(this);
    }

    private void ShowServerMenu(PanelHost host, Point at, bool background)
    {
        ServerMenu?.Close();
        var menu = ServerMenu = new ContextMenu
        {
            PlacementTarget = host.View,
            Placement = Avalonia.Controls.PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(at, new Size(1, 1)),
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
        };
        void Add(string label, string? icon, string? command, Action run)
        {
            var item = new MenuItem { Header = label, Icon = icon is null ? null : AppIcons.ForPanel(icon, ReferenceEquals(host, _panels[1])), InputGesture = command is null ? null : GestureOf(command) };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }

        var file = !background && FocusedOnServer(host, filesOnly: true) is not null;
        if (file)
        {
            Add(Core.Commands.CommandText.Title("File.View"), "File.View", "File.View", () => _ = ViewFromServerAsync(host, alternate: false));
            Add(Core.Commands.CommandText.Title("File.Edit"), "File.Edit", "File.Edit", () => _ = EditOnServerAsync(host));
        }

        var onItem = !background && FocusedOnServer(host, filesOnly: false) is not null;
        if (onItem)
        {
            Add(Core.Commands.CommandText.Title("File.Copy"), "File.Copy", "File.Copy", () => _ = CopyFromServerAsync(host, move: false));
            Add(Core.Commands.CommandText.Title("File.Move"), "File.Move", "File.Move", () => _ = CopyFromServerAsync(host, move: true));
            Add(Core.Commands.CommandText.Title("File.QuickRename"), "File.QuickRename", "File.QuickRename", () => QuickRenameOnServer(host));
            Add(Core.Commands.CommandText.Title("File.Delete"), "File.Delete", "File.Delete", () => _ = DeleteOnServerAsync(host));
            Add(AppStrings.Server_Permissions, null, "File.Properties", () => _ = PermissionsOnServerAsync(host));
            menu.Items.Add(new Separator());
        }

        Add(AppStrings.NewFolder_Title + "…", "File.CreateDirectory", "File.CreateDirectory", () => _ = NewFolderOnServerAsync(host));
        Add(Core.Commands.CommandText.Title("File.EditNew"), null, "File.EditNew", () => _ = EditNewOnServerAsync(host));
        if (onItem)
        {
            menu.Items.Add(new Separator());
            Add(Core.Commands.CommandText.Title("Clipboard.CopyName"), null, "Clipboard.CopyName", () => CopyServerText(host, "Clipboard.CopyName"));
            Add(Core.Commands.CommandText.Title("Clipboard.CopyFullName"), null, "Clipboard.CopyFullName", () => CopyServerText(host, "Clipboard.CopyFullName"));
        }

        menu.Items.Add(new Separator());

        AddServerPart(menu.Items, host, withAddress: true);
        menu.Open(host.View);
    }

    private void AddServerPart(ItemCollection items, PanelHost host, bool withAddress)
    {
        var open = Servers.ServerOf(host.Panel.Path);
        var here = ServerAddress.Parse(host.Panel.Path);
        var name = open?.Bookmark ?? here?.Host ?? "";
        var bookmarked = here is not null && _services.Settings.Current.Servers.Bookmarks.Any(b => ServerAddress.Of(b).SameServer(here));
        void Add(string label, string? command, Action run)
        {
            var item = new MenuItem { Header = label, InputGesture = command is null ? null : GestureOf(command) };
            item.Click += (_, _) => run();
            items.Add(item);
        }

        if (items.Count > 0 && items[^1] is not Separator)
        {
            items.Add(new Separator());
        }

        items.Add(new MenuItem { Header = Options.OptionsUi.Text(Loc.Format(AppStrings.Server_MenuPart, name), 11, brush: "Brush.TertiaryText"), IsEnabled = false });
        Add(Core.Commands.CommandText.Title("Panel.Refresh"), "Panel.Refresh", () =>
        {
            Servers.ForgetListing(host.Panel.Path);
            _ = host.Panel.RefreshAsync();
        });
        if (withAddress)
        {
            Add(AppStrings.Server_CopyAddress, null, () => CopyServerText(host, "Clipboard.CopyPath"));
        }

        Add(bookmarked ? AppStrings.Server_EditBookmark : AppStrings.Server_SaveAsBookmark, null, () => _ = ConnectServerAsync(host));
        Add(AppStrings.Server_ConnectAnother, "Server.Connect", () => _ = ConnectServerAsync(host, fresh: true));
        if (here is not null && (SecretSource.IsKept(Servers.Secrets, here, SecretKind.Password) || SecretSource.IsKept(Servers.Secrets, here, SecretKind.Passphrase)))
        {
            Add(AppStrings.Server_ForgetKept, null, () =>
            {
                SecretSource.ForgetKept(Servers.Secrets, here);
                ShowNotice(Loc.Format(AppStrings.Server_Forgotten, name));
            });
        }

        Add(Core.Commands.CommandText.Title("Server.Log"), "Server.Log", () => ShowServerLog(host));
        Add(AppStrings.Server_DisconnectShort, "Network.DisconnectDrive", () => _ = DisconnectServerAsync(host));
    }

    private Avalonia.Input.KeyGesture? GestureOf(string id) => Input.KeyMapping.ToGesture(_services.Commands.ChordsFor(id).FirstOrDefault());

    private void AddServerItems(MenuFlyout flyout, PanelHost host, Action<string> header, Action<MenuItem, Action, char?> add)
    {
        flyout.Items.Add(new Separator());
        header(AppStrings.Place_Ftp);
        var open = Servers.OpenServers;
        var servers = _services.Settings.Current.Servers;
        foreach (var b in servers.ForDriveMenu(x => open.Any(o => o.Server.SameServer(ServerAddress.Of(x)))))
        {
            var address = ServerAddress.Of(b);
            var isOpen = open.Any(o => o.Server.SameServer(address));
            var hint = isOpen ? AppStrings.Server_ConnectedHint : ConnectServerDialog.KindTag(b.Kind);
            var bookmark = b;
            add(new MenuItem { Header = Row(b.Name, hint), Icon = MenuIcon(NetworkGlyph) }, () => _ = OpenServerAsync(host, TargetOf(bookmark)), null);
        }

        foreach (var o in open.Where(o => o.Bookmark is null))
        {
            var server = o.Server;
            add(new MenuItem { Header = Row(server.Root[server.Scheme.Length..], AppStrings.Server_ConnectedHint), Icon = MenuIcon(NetworkGlyph) }, () => _ = host.Panel.NavigateAsync(server.ToString()), null);
        }

        var connect = Core.Commands.CommandText.Title("Server.Connect") + "…";
        if (servers.Bookmarks.Count > 0)
        {
            connect = Loc.Format(AppStrings.DriveMenu_ConnectCount, connect, servers.Bookmarks.Count);
        }

        add(new MenuItem { Header = Row(connect, GestureOf("Server.Connect")?.ToString()) }, () => _ = ConnectServerAsync(host), null);
        if (open.Count > 0)
        {
            add(new MenuItem { Header = Row(Core.Commands.CommandText.Title("Server.Log"), null) }, () => ShowServerLog(host), null);
        }

        if (OnServer(host))
        {
            add(new MenuItem { Header = Row(Core.Commands.CommandText.Title("Server.Disconnect"), null) }, () => _ = DisconnectServerAsync(host), null);
        }
    }

    private async Task<string?> AskAsync(string title, IReadOnlyList<string> paragraphs, IReadOnlyList<Choice> choices, string cancel)
    {
        var dialog = OpenServerQuestion = new ChoiceDialog(title, paragraphs, choices, cancel, width: 520);
        return await dialog.ShowDialog<string?>(this);
    }

    private sealed class WindowQuestions(MainWindow window) : IServerQuestions
    {
        public Task<SecretAnswer?> AskSecretAsync(ServerAddress server, SecretKind kind, bool wrongBefore, bool keptBefore, CancellationToken cancellationToken)
            => window.ServerAnswers is { } answers
                ? answers.AskSecretAsync(server, kind, wrongBefore, keptBefore, cancellationToken)
                : window.WhileAskingAsync(async () =>
                {
                    var dialog = window.OpenServerSecret = new ServerSecretDialog(server, kind, wrongBefore, keptBefore);
                    return await dialog.ShowDialog<SecretAnswer?>(window);
                });

        public Task<TrustAnswer> AskTrustAsync(TrustQuestion q, CancellationToken cancellationToken)
            => window.ServerAnswers is { } answers
                ? answers.AskTrustAsync(q, cancellationToken)
                : window.WhileAskingAsync(async () =>
                {
                    var server = q.Server.Host + (q.Server.Port == ServerBookmark.DefaultPort(q.Server.Kind) ? "" : ":" + q.Server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    List<string> text = q.Subject is null
                        ? [Loc.Format(AppStrings.Server_KeyNew, server, q.Server.Port), Loc.Format(AppStrings.Server_KeyFingerprint, q.Algorithm, q.Fingerprint), AppStrings.Server_KeyCompare]
                        : [Loc.Format(q.Changed ? AppStrings.Server_CertChanged : AppStrings.Server_CertNew, server), q.Subject, Loc.Format(AppStrings.Server_CertFingerprint, q.Fingerprint), q.Problem ?? "", AppStrings.Server_KeyCompare];
                    var answer = await window.AskAsync(q.Subject is null ? AppStrings.Server_KeyTitle : AppStrings.Server_CertTitle, [.. text.Where(t => t.Length > 0)],
                        [new Choice("remember", AppStrings.Server_TrustRemember, Primary: true), new Choice("once", AppStrings.Server_TrustOnce), new Choice("cancel", AppStrings.Common_Cancel)], "cancel");
                    return answer switch { "remember" => TrustAnswer.Remember, "once" => TrustAnswer.Once, _ => TrustAnswer.Cancel };
                });

        public Task<bool> AskPlainAsync(ServerAddress server, CancellationToken cancellationToken)
            => window.ServerAnswers is { } answers
                ? answers.AskPlainAsync(server, cancellationToken)
                : window.WhileAskingAsync(async () => await window.AskAsync(AppStrings.Server_PlainTitle, [Loc.Format(AppStrings.Server_PlainText, server.Host), AppStrings.Server_PlainOnce],
                    [new Choice("plain", AppStrings.Server_PlainYes), new Choice("cancel", AppStrings.Common_Cancel, Primary: true)], "cancel") == "plain");
    }
}
