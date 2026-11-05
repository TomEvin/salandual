// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ServerSettings.cs

namespace Salandual.Core.Settings;

public enum ServerKind
{
    Sftp,

    Ftp,

    Ftps,

    FtpPlain,
}

public enum ServerSignIn
{
    Password,
    KeyFile,

    Agent,
}

public sealed record ServerBookmark
{
    public string Name { get; set; } = "";

    public ServerKind Kind { get; set; } = ServerKind.Sftp;

    public string Host { get; set; } = "";

    public int Port { get; set; }

    public string User { get; set; } = "";

    public ServerSignIn SignIn { get; set; } = ServerSignIn.Password;

    public string KeyFile { get; set; } = "";

    public string StartIn { get; set; } = "";

    public bool PlainAllowed { get; set; }

    public ConnectionOptions? Connection { get; set; }

    public ProxyOptions? Proxy { get; set; }

    public TransferSettingsForServer? Transfers { get; set; }

    public ListingOptions? Listing { get; set; }

    public DateTimeOffset? LastUsed { get; set; }

    public static int DefaultPort(ServerKind kind) => kind switch
    {
        ServerKind.Sftp => 22,
        ServerKind.Ftps => 990,
        _ => 21,
    };

    public int PortOrDefault => Port is > 0 and <= 65535 ? Port : DefaultPort(Kind);
}

public sealed record ServerSettings
{
    public const int MaxBookmarks = 200;

    public List<ServerBookmark> Bookmarks { get; set; } = [];

    public bool CloseConnecting { get; set; }

    public ServerOptions Defaults { get; set; } = new();

    public bool ReadKnownHosts { get; set; } = true;

    public const int MaxInDriveMenu = 20;

    public int InDriveMenu { get; set; } = 3;

    public IReadOnlyList<ServerBookmark> ForDriveMenu(Func<ServerBookmark, bool> isOpen)
    {
        var open = Bookmarks.Where(isOpen).ToList();
        var rest = Bookmarks.Where(b => !isOpen(b)).Select((b, i) => (b, i)).OrderByDescending(x => x.b.LastUsed ?? DateTimeOffset.MinValue).ThenBy(x => x.i).Select(x => x.b);
        return [.. open, .. rest.Take(Math.Max(0, InDriveMenu - open.Count))];
    }

    internal ServerSettings Normalize() => this with
    {
        Bookmarks = [.. (Bookmarks ?? []).Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Host))
            .Select(b => b with
            {
                Name = string.IsNullOrWhiteSpace(b.Name) ? b.Host.Trim() : b.Name.Trim(),
                Host = b.Host.Trim(),
                User = (b.User ?? "").Trim(),
                KeyFile = (b.KeyFile ?? "").Trim(),
                StartIn = (b.StartIn ?? "").Trim(),
                Port = b.Port is > 0 and <= 65535 ? b.Port : 0,
                Connection = b.Connection?.Normalize(),
                Proxy = b.Proxy?.Normalize(),
                Transfers = b.Transfers?.Normalize(),
                Listing = b.Listing?.Normalize(),
            })
            .DistinctBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxBookmarks)],
        Defaults = (Defaults ?? new()).Normalize(),
        InDriveMenu = Math.Clamp(InDriveMenu, 0, MaxInDriveMenu),
    };
}
