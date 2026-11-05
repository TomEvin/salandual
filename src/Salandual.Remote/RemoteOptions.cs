// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/RemoteOptions.cs

using System.Globalization;
using System.Net.Http;
using Salandual.Core.Listing;
using Salandual.Core.Settings;

namespace Salandual.Remote;

internal sealed record ProxyRoute(ProxyKind Kind, string Host, int Port, string User, string? Password);

public static class RemoteOptions
{
    public static string ProxyTarget(ProxyOptions options)
        => SecretSource.Prefix + "proxy://" + (options.User.Length > 0 ? options.User + "@" : "") + options.Host + ":" + options.PortOrDefault.ToString(CultureInfo.InvariantCulture);

    internal static ProxyRoute? Proxy(ProxyOptions options, ServerAddress server, string? password)
    {
        switch (options.Kind)
        {
            case ProxyKind.Socks5 or ProxyKind.Http when ServerAddress.IsHostName(options.Host):
                return new ProxyRoute(options.Kind, options.Host, options.PortOrDefault, options.User, options.User.Length > 0 ? password : null);
            case ProxyKind.Windows:
                var at = new Uri("http://" + (server.Host.Contains(':') ? "[" + server.Host + "]" : server.Host) + "/");
                var proxy = HttpClient.DefaultProxy;
                if (proxy.IsBypassed(at) || proxy.GetProxy(at) is not { } uri || uri == at || !ServerAddress.IsHostName(uri.Host))
                {
                    return null;
                }

                return new ProxyRoute(ProxyKind.Http, uri.Host, uri.Port, "", null);
            default:
                return null;
        }
    }

    public static bool IsAscii(TransferSettingsForServer transfers, string name) => transfers.Mode switch
    {
        TransferMode.Ascii => true,
        TransferMode.Automatic => FileMask.Parse(transfers.AsciiNames) is { } mask && mask.IsMatch(name),
        _ => false,
    };

    public static bool IsHidden(string name) => name.Length > 1 && name[0] == '.';
}
