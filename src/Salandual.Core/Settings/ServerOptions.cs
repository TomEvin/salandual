// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Settings/ServerOptions.cs

namespace Salandual.Core.Settings;

public enum TlsVersions { Tls12And13, Tls13Only }

public enum DataConnection { Passive, Active }

public enum ProxyKind { None, Windows, Socks5, Http }

public enum TransferMode { Binary, Ascii, Automatic }

public enum ServerType { Automatic, Unix, Windows, Vms, IbmZos, IbmI, Mlsd }

public enum ListCommand { Automatic, ListAll, List }

public sealed record ConnectionOptions
{
    public TlsVersions Tls { get; set; } = TlsVersions.Tls12And13;

    public DataConnection Data { get; set; } = DataConnection.Passive;

    public bool KeepAlive { get; set; } = true;

    public int KeepAliveSeconds { get; set; } = 60;

    public int KeepAliveStopMinutes { get; set; } = 30;

    public int ConnectSeconds { get; set; } = 20;

    public int AnswerSeconds { get; set; } = 30;

    public int Retries { get; set; } = 3;

    public int RetrySeconds { get; set; } = 10;

    public int Connections { get; set; } = 2;

    public ConnectionOptions Normalize() => this with
    {
        KeepAliveSeconds = Math.Clamp(KeepAliveSeconds, 10, 3600),
        KeepAliveStopMinutes = Math.Clamp(KeepAliveStopMinutes, 1, 24 * 60),
        ConnectSeconds = Math.Clamp(ConnectSeconds, 3, 300),
        AnswerSeconds = Math.Clamp(AnswerSeconds, 5, 600),
        Retries = Math.Clamp(Retries, 0, 10),
        RetrySeconds = Math.Clamp(RetrySeconds, 1, 300),
        Connections = Math.Clamp(Connections, 1, 8),
    };
}

public sealed record ProxyOptions
{
    public ProxyKind Kind { get; set; } = ProxyKind.None;

    public string Host { get; set; } = "";

    public int Port { get; set; }

    public string User { get; set; } = "";

    public int PortOrDefault => Port is > 0 and <= 65535 ? Port : Kind == ProxyKind.Socks5 ? 1080 : 8080;

    public ProxyOptions Normalize() => this with
    {
        Host = (Host ?? "").Trim(),
        Port = Port is > 0 and <= 65535 ? Port : 0,
        User = (User ?? "").Trim(),
    };
}

public sealed record TransferSettingsForServer
{
    public const string DefaultAsciiNames = "*.txt;*.htm;*.html;*.php;*.css;*.js;*.xml;*.csv";

    public TransferMode Mode { get; set; } = TransferMode.Binary;

    public string AsciiNames { get; set; } = DefaultAsciiNames;

    public string UploadPermissions { get; set; } = "";

    public bool Compress { get; set; }

    public int? UploadMode => UploadPermissions is { Length: 3 } p && p.All(c => c is >= '0' and <= '7') ? Convert.ToInt32(p, 8) : null;

    public TransferSettingsForServer Normalize() => this with
    {
        AsciiNames = string.IsNullOrWhiteSpace(AsciiNames) ? DefaultAsciiNames : AsciiNames.Trim(),
        UploadPermissions = (UploadPermissions ?? "").Trim() is { Length: 3 } p && p.All(c => c is >= '0' and <= '7') ? p : "",
    };
}

public sealed record ListingOptions
{
    public ServerType ServerType { get; set; } = ServerType.Automatic;

    public ListCommand ListCommand { get; set; } = ListCommand.Automatic;

    public int CodePage { get; set; }

    public int? TimeZoneMinutes { get; set; }

    public bool ShowHidden { get; set; } = true;

    public bool Cache { get; set; } = true;

    public int CacheMinutes { get; set; } = 10;

    public ListingOptions Normalize() => this with
    {
        CodePage = CodePage is >= 0 and <= 65001 ? CodePage : 0,
        TimeZoneMinutes = TimeZoneMinutes is { } m ? Math.Clamp(m / 15 * 15, -12 * 60, 14 * 60) : null,
        CacheMinutes = Math.Clamp(CacheMinutes, 1, 24 * 60),
    };
}

public sealed record ServerOptions
{
    public ConnectionOptions Connection { get; set; } = new();

    public ProxyOptions Proxy { get; set; } = new();

    public TransferSettingsForServer Transfers { get; set; } = new();

    public ListingOptions Listing { get; set; } = new();

    public static ServerOptions BuiltIn { get; } = new();

    public ServerOptions Normalize() => this with
    {
        Connection = (Connection ?? new()).Normalize(),
        Proxy = (Proxy ?? new()).Normalize(),
        Transfers = (Transfers ?? new()).Normalize(),
        Listing = (Listing ?? new()).Normalize(),
    };

    public static ServerOptions For(ServerBookmark? bookmark, ServerOptions defaults) => new()
    {
        Connection = bookmark?.Connection ?? defaults.Connection,
        Proxy = bookmark?.Proxy ?? defaults.Proxy,
        Transfers = bookmark?.Transfers ?? defaults.Transfers,
        Listing = bookmark?.Listing ?? defaults.Listing,
    };
}
