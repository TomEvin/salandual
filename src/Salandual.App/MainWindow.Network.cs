// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/MainWindow.Network.cs

using Salandual.App.Dialogs;
using Salandual.App.Panels;
using Salandual.App.Resources;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.App;

public partial class MainWindow
{
    internal ConnectDriveDialog? OpenConnectDialog { get; private set; }

    internal DisconnectDrivesDialog? OpenDisconnectDialog { get; private set; }

    private readonly Dictionary<string, IReadOnlyList<string>> _sharesOf = new(StringComparer.OrdinalIgnoreCase);

    private void WireNetwork()
    {
        PlaceFiles.SignIn = share => _services.Places.SignInAsync(share, _hwnd);
        PlaceFiles.OpenedBefore = () => _services.Settings.Current.History.NetworkFolders;
    }

    private bool RunNetworkCommand(string id, PanelHost host)
    {
        switch (id)
        {
            case "Network.Open":
                _ = host.Panel.NavigateAsync(ShellPlace.Network);
                return true;
            case "Network.SharedDirectories":
                _ = host.Panel.NavigateAsync(ShellPlace.Shares);
                return true;
            case "Network.ConnectDrive":
                _ = ConnectDriveAsync(host);
                return true;
            case "Network.DisconnectDrive":
                _ = DisconnectDrivesAsync(host);
                return true;
            default:
                return false;
        }
    }

    private void UpdateNetworkState(PanelHost host)
    {
        var network = DriveMenu.Place(DriveMenuSettings.Network);
        PlaceFiles.ServersGoUpToNetwork = network.InMenu || network.InStrip;
        if (ShellPlace.ShareOf(host.Panel.Path) is { } share)
        {
            var history = _services.Settings.Current.History.NetworkFolders;
            if (history.Count == 0 || !string.Equals(history[0], share, StringComparison.OrdinalIgnoreCase))
            {
                _services.Settings.Update(s => s with { History = s.History with { NetworkFolders = HistorySettings.Push(s.History.NetworkFolders, share) } });
            }
        }
    }

    internal async Task ConnectDriveAsync(PanelHost host)
    {
        var letters = _services.Places.FreeLetters();
        if (letters.Count == 0)
        {
            ShowNotice(AppStrings.Net_NoLetter);
            return;
        }

        var panel = host.Panel;
        var folder = ShellPlace.ShareOf(panel.Path) ?? "";
        if (ShellPlace.IsServer(panel.Path) && panel.FocusedEntry is { } share && !panel.IsUp(panel.Focus))
        {
            folder = panel.Path.TrimEnd('\\') + "\\" + share.Name;
        }
        else if (ShellPlace.Is(panel.Path, ShellPlace.Network) && panel.FocusedEntry is { } computer && PlaceFiles.Item(panel.Path, computer.Name) is { IsComputer: true } item)
        {
            folder = item.Target + "\\";
        }

        var dialog = OpenConnectDialog = new ConnectDriveDialog(letters, folder, SuggestFoldersAsync, request => ConnectAsync(request));
        string? connected;
        try
        {
            connected = await dialog.ShowDialog<string?>(this);
        }
        finally
        {
            OpenConnectDialog = null;
        }

        if (connected is not null)
        {
            ShowNotice(Loc.Format(AppStrings.Net_Connected, connected, dialog.Folder.Text?.Trim().TrimEnd('\\') ?? ""));
            _ = LoadDrivesAsync();
            await panel.NavigateAsync(connected + "\\");
        }
    }

    private async Task<string?> ConnectAsync(ConnectRequest request)
    {
        try
        {
            await _services.Places.ConnectAsync(request.Letter, request.Folder, request.Reconnect, request.OtherUser, _hwnd);
            _services.Settings.Update(s => s with { History = s.History with { NetworkFolders = HistorySettings.Push(s.History.NetworkFolders, request.Folder) } });
            return null;
        }
        catch (OperationCanceledException)
        {
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private async Task<IEnumerable<object>> SuggestFoldersAsync(string? text, CancellationToken cancellationToken)
    {
        text ??= "";
        var found = new List<string>(_services.Settings.Current.History.NetworkFolders);
        if (PlaceFiles.ListedItems(ShellPlace.Network) is { } network)
        {
            found.AddRange(network.Where(i => i.IsComputer).Select(i => i.Target + "\\"));
        }

        if (ShellPlace.ServerOf(text) is { } server && text.Length > server.Length)
        {
            if (!_sharesOf.TryGetValue(server, out var shares))
            {
                try
                {
                    var listing = await PlaceFiles.ListAsync(server, cancellationToken);
                    shares = [.. listing.Entries.Where(e => (e.Attributes & FileAttributes.Hidden) == 0).Select(e => server + "\\" + e.Name)];
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    shares = [];
                }

                _sharesOf[server] = shares;
            }

            found.AddRange(shares);
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).Where(f => f.Length > text.Length && f.StartsWith(text, StringComparison.OrdinalIgnoreCase)).Take(12).ToList();
    }

    internal async Task DisconnectDrivesAsync(PanelHost host)
    {
        var drives = await _services.Places.NetworkDrivesAsync();
        if (drives.Count == 0)
        {
            ShowNotice(AppStrings.Net_NoDrives);
            return;
        }

        var ticked = host.Panel.Path is [_, ':', ..] path ? path[..2].ToUpperInvariant() : null;
        bool Shown(NetworkDrive d) => _panels.Any(p => p.Panel.Path.StartsWith(d.Letter, StringComparison.OrdinalIgnoreCase));
        var dialog = OpenDisconnectDialog = new DisconnectDrivesDialog(drives, ticked, d => Shown(d) ? null : d.OpenFiles);
        List<string>? letters;
        try
        {
            letters = await dialog.ShowDialog<List<string>?>(this);
        }
        finally
        {
            OpenDisconnectDialog = null;
        }

        var done = new List<string>();
        foreach (var letter in letters ?? [])
        {
            foreach (var on in _panels.Where(p => p.Panel.Path.StartsWith(letter, StringComparison.OrdinalIgnoreCase)))
            {
                await on.Panel.NavigateAsync(ShellPlace.ThisPc);
            }

            try
            {
                var result = await _services.Places.DisconnectAsync(letter, force: false);
                if (result == DisconnectResult.FilesOpen
                    && await AskAsync(Loc.Format(AppStrings.Net_FilesOpenTitle, letter), Loc.Format(AppStrings.Net_FilesOpenText, letter), AppStrings.Net_DisconnectButton))
                {
                    result = await _services.Places.DisconnectAsync(letter, force: true);
                }

                if (result == DisconnectResult.Done)
                {
                    done.Add(letter);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowNotice(Loc.Format(AppStrings.Place_Failed, ex.Message));
            }
        }

        if (done.Count > 0)
        {
            ShowNotice(done.Count == 1 ? Loc.Format(AppStrings.Net_Disconnected_One, done[0]) : Loc.Format(AppStrings.Net_Disconnected_Other, done.Count));
            _ = LoadDrivesAsync();
            foreach (var p in _panels.Where(p => ShellPlace.Is(p.Panel.Path, ShellPlace.Network) || ShellPlace.Is(p.Panel.Path, ShellPlace.ThisPc)))
            {
                _ = p.Panel.RefreshAsync();
            }
        }
    }
}
