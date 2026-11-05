// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/ServerAddress.cs

using System.Globalization;
using Salandual.Core.Settings;

namespace Salandual.Remote;

public sealed record ServerAddress(ServerKind Kind, string Host, int Port, string User, string Path)
{
    public const string SftpScheme = "sftp://", FtpScheme = "ftp://", FtpsScheme = "ftps://";

    public static bool IsServerPath(string? path)
        => path is not null && (path.StartsWith(SftpScheme, StringComparison.OrdinalIgnoreCase) || path.StartsWith(FtpScheme, StringComparison.OrdinalIgnoreCase)
                                || path.StartsWith(FtpsScheme, StringComparison.OrdinalIgnoreCase));

    public static ServerAddress? Parse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        text = text.Trim();
        ServerKind kind;
        string rest;
        if (text.StartsWith(SftpScheme, StringComparison.OrdinalIgnoreCase))
        {
            (kind, rest) = (ServerKind.Sftp, text[SftpScheme.Length..]);
        }
        else if (text.StartsWith(FtpsScheme, StringComparison.OrdinalIgnoreCase))
        {
            (kind, rest) = (ServerKind.Ftps, text[FtpsScheme.Length..]);
        }
        else if (text.StartsWith(FtpScheme, StringComparison.OrdinalIgnoreCase))
        {
            (kind, rest) = (ServerKind.Ftp, text[FtpScheme.Length..]);
        }
        else
        {
            return null;
        }

        var slash = rest.IndexOf('/');
        var authority = slash < 0 ? rest : rest[..slash];
        var path = slash < 0 ? "/" : rest[slash..];
        var at = authority.LastIndexOf('@');
        var user = at < 0 ? "" : authority[..at];
        var hostPort = at < 0 ? authority : authority[(at + 1)..];
        var port = 0;
        string host;
        if (hostPort.StartsWith('['))
        {
            var close = hostPort.IndexOf(']');
            if (close < 0)
            {
                return null;
            }

            host = hostPort[1..close];
            if (close + 1 < hostPort.Length && (hostPort[close + 1] != ':' || !TryPort(hostPort[(close + 2)..], out port)))
            {
                return null;
            }
        }
        else
        {
            var colon = hostPort.LastIndexOf(':');
            host = colon < 0 ? hostPort : hostPort[..colon];
            if (colon >= 0 && !TryPort(hostPort[(colon + 1)..], out port))
            {
                return null;
            }
        }

        if (!IsHostName(host) || user.IndexOfAny(['/', '\\', ':']) >= 0 || HasPassword(text))
        {
            return null;
        }

        return new ServerAddress(kind, host, port == 0 ? ServerBookmark.DefaultPort(kind) : port, user, NormalizePath(path));
    }

    public static bool HasPassword(string text)
    {
        var start = text.IndexOf("://", StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var rest = text[(start + 3)..];
        var slash = rest.IndexOf('/');
        var authority = slash < 0 ? rest : rest[..slash];
        var at = authority.LastIndexOf('@');
        return at > 0 && authority[..at].Contains(':');
    }

    private static bool TryPort(string text, out int port)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is > 0 and <= 65535;

    public static bool IsHostName(string host)
        => host.Length is > 0 and <= 253 && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':' or '_') && !host.StartsWith('-') && !host.StartsWith('.');

    public static string NormalizePath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".").ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] == "..")
            {
                parts.RemoveAt(i);
                if (i > 0)
                {
                    parts.RemoveAt(i - 1);
                    i--;
                }

                i--;
            }
        }

        return "/" + string.Join('/', parts);
    }

    public static ServerAddress Of(ServerBookmark bookmark)
        => new(bookmark.Kind, bookmark.Host, bookmark.PortOrDefault, bookmark.User, NormalizePath(bookmark.StartIn));

    public string Scheme => Kind switch
    {
        ServerKind.Sftp => SftpScheme,
        ServerKind.Ftps => FtpsScheme,
        _ => FtpScheme,
    };

    public string Key => (Kind == ServerKind.FtpPlain ? FtpScheme : Scheme) + (User.Length > 0 ? User + "@" : "") + HostText + ":" + Port.ToString(CultureInfo.InvariantCulture);

    private string HostText => Host.Contains(':') ? "[" + Host + "]" : Host;

    public string Root => Scheme + (User.Length > 0 ? User + "@" : "") + HostText
                          + (Port == ServerBookmark.DefaultPort(Kind == ServerKind.FtpPlain ? ServerKind.Ftp : Kind) ? "" : ":" + Port.ToString(CultureInfo.InvariantCulture));

    public override string ToString() => Root + Path;

    public ServerAddress WithPath(string path) => this with { Path = NormalizePath(path) };

    public ServerAddress? Parent()
    {
        if (Path == "/")
        {
            return null;
        }

        var cut = Path.LastIndexOf('/');
        return this with { Path = cut <= 0 ? "/" : Path[..cut] };
    }

    public string Name => Path == "/" ? Host : Path[(Path.LastIndexOf('/') + 1)..];

    public ServerAddress Child(string name) => this with { Path = Path == "/" ? "/" + name : Path + "/" + name };

    public bool SameServer(ServerAddress other) => string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
}
