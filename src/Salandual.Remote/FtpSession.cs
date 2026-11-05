// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/FtpSession.cs

using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using FluentFTP;
using FluentFTP.Exceptions;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Remote;

internal sealed class FtpSession : IRemoteSession
{
    private readonly AsyncFtpClient _client;
    private readonly ConnectContext _context;
    private readonly FtpEncryptionMode _mode;
    private readonly NetworkCredential _credential;
    private readonly HashSet<string> _acceptedCertificates;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ServerOptions _options;

    private FtpSession(ServerTarget target, ConnectContext context, AsyncFtpClient client, FtpEncryptionMode mode, NetworkCredential credential, HashSet<string> accepted, string home)
    {
        Server = target.Address;
        _context = context;
        _client = client;
        _mode = mode;
        _credential = credential;
        _acceptedCertificates = accepted;
        _options = target.Settings;
        Home = home;
    }

    public ServerAddress Server { get; }

    public string Security => _mode == FtpEncryptionMode.None ? "FTP" : "FTPS";

    public bool Encrypted => _mode != FtpEncryptionMode.None;

    public bool IsConnected => _client.IsConnected;

    public string Home { get; }

    public static async Task<IRemoteSession> ConnectAsync(ServerTarget target, ConnectContext context, CancellationToken cancellationToken)
    {
        var address = target.Address;
        var log = context.Log;
        var mode = target.Address.Kind switch
        {
            ServerKind.Ftps => FtpEncryptionMode.Implicit,
            ServerKind.FtpPlain => FtpEncryptionMode.None,
            _ when target.PlainAllowed => FtpEncryptionMode.Auto,
            _ => FtpEncryptionMode.Explicit,
        };
        var anonymous = address.User.Length == 0 || string.Equals(address.User, "anonymous", StringComparison.OrdinalIgnoreCase);
        var accepted = new HashSet<string>(StringComparer.Ordinal);
        var wrong = false;
        for (var attempt = 0; ; attempt++)
        {
            var credential = anonymous
                ? new NetworkCredential("anonymous", "anonymous@")
                : new NetworkCredential(address.User, await context.Secrets.GetAsync(SecretKind.Password, wrong, cancellationToken).ConfigureAwait(false) ?? throw new ConnectCancelledException());
            var cancelled = false;
            var client = NewClient(address, target.Settings, mode, credential, context, accepted, log, () => cancelled = true, cancellationToken);
            log.Add(Loc.Format(Strings.ServerLog_Connecting, address.Host, address.Port));
            try
            {
                await client.Connect(cancellationToken).ConfigureAwait(false);
                var used = client.IsEncrypted ? (mode == FtpEncryptionMode.Implicit ? FtpEncryptionMode.Implicit : FtpEncryptionMode.Explicit) : FtpEncryptionMode.None;
                if (used == FtpEncryptionMode.None && mode != FtpEncryptionMode.None)
                {
                    log.Add(Strings.ServerLog_NoTls);
                }

                log.Add(Loc.Format(Strings.ServerLog_SignedIn, credential.UserName));
                var home = await client.GetWorkingDirectory(cancellationToken).ConfigureAwait(false);
                return new FtpSession(target, context, client, used, credential, accepted, string.IsNullOrEmpty(home) ? "/" : ServerAddress.NormalizePath(home));
            }
            catch (FtpSecurityNotAvailableException) when (mode == FtpEncryptionMode.Explicit)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                log.Add(Strings.ServerLog_NoTls);
                if (!await context.Questions.AskPlainAsync(address, cancellationToken).ConfigureAwait(false))
                {
                    throw new ConnectCancelledException();
                }

                context.SaidYesToPlain = true;
                mode = FtpEncryptionMode.None;
            }
            catch (FtpAuthenticationException) when (!anonymous && attempt < 3)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                log.Add(Strings.ServerLog_SignInRefused);
                wrong = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                if (cancelled)
                {
                    throw new ConnectCancelledException();
                }

                throw Translate(ex, address);
            }
        }
    }

    private static AsyncFtpClient NewClient(ServerAddress address, ServerOptions options, FtpEncryptionMode mode, NetworkCredential credential, ConnectContext context, HashSet<string> accepted, ConnectionLog log, Action? cancelled, CancellationToken cancellationToken, FtpDataType data = FtpDataType.Binary)
    {
        var c = options.Connection;
        var config = new FtpConfig
        {
            EncryptionMode = mode,
            DataConnectionType = c.Data == DataConnection.Active ? FtpDataConnectionType.AutoActive : FtpDataConnectionType.AutoPassive,
            ConnectTimeout = c.ConnectSeconds * 1000,
            ReadTimeout = c.AnswerSeconds * 1000,
            DataConnectionConnectTimeout = c.ConnectSeconds * 1000,
            DataConnectionReadTimeout = c.AnswerSeconds * 1000,
            SslProtocols = c.Tls == TlsVersions.Tls13Only ? SslProtocols.Tls13 : SslProtocols.Tls12 | SslProtocols.Tls13,
            ListingParser = Parser(options.Listing.ServerType),
            DownloadDataType = data,
            UploadDataType = data,
            Noop = false,
            ValidateAnyCertificate = false,
            TimeConversion = FtpDate.UTC,
            RetryAttempts = 1,
        };
        var client = NewClient(address, credential, config, RemoteOptions.Proxy(options.Proxy, address, context.ProxyPassword), log);
        if (options.Listing.CodePage > 0)
        {
            client.Encoding = EncodingOf(options.Listing.CodePage);
        }

        client.ValidateCertificate += (_, e) =>
        {
            var raw = e.Certificate.GetRawCertData();
            var fingerprint = TrustedServers.CertificateFingerprint(raw);
            if (e.PolicyErrors == System.Net.Security.SslPolicyErrors.None || accepted.Contains(fingerprint))
            {
                if (!e.IsDataConnection && accepted.Add(fingerprint))
                {
                    log.Add(e.PolicyErrors == System.Net.Security.SslPolicyErrors.None ? Strings.ServerLog_CertWindows : Loc.Format(Strings.ServerLog_CertKnown, fingerprint));
                }

                e.Accept = true;
                return;
            }

            var trust = context.Trusted.CheckCertificate(address.Host, address.Port, fingerprint);
            if (trust == KeyTrust.Trusted)
            {
                log.Add(Loc.Format(Strings.ServerLog_CertKnown, fingerprint));
                accepted.Add(fingerprint);
                e.Accept = true;
                return;
            }

            var subject = e.Certificate.Subject + " · " + e.Certificate.Issuer;
            var answer = context.Questions.AskTrustAsync(new TrustQuestion(address, "TLS", fingerprint, subject, e.PolicyErrorMessage, trust == KeyTrust.Changed), cancellationToken).GetAwaiter().GetResult();
            if (answer == TrustAnswer.Remember)
            {
                context.Trusted.Remember(address.Host, address.Port, "tls", "TLS", fingerprint);
            }

            e.Accept = answer != TrustAnswer.Cancel;
            if (e.Accept)
            {
                accepted.Add(fingerprint);
                log.Add(Loc.Format(Strings.ServerLog_CertTrusted, fingerprint));
            }
            else
            {
                log.Add(Loc.Format(Strings.ServerLog_CertRefused, fingerprint));
                cancelled?.Invoke();
            }
        };
        return client;
    }

    private static AsyncFtpClient NewClient(ServerAddress address, NetworkCredential credential, FtpConfig config, ProxyRoute? proxy, ConnectionLog log)
    {
        if (proxy is null)
        {
            return new AsyncFtpClient(address.Host, credential, address.Port, config);
        }

        log.Add(Loc.Format(Strings.ServerLog_Proxy, proxy.Host, proxy.Port));
        var profile = new FtpProxyProfile
        {
            ProxyHost = proxy.Host,
            ProxyPort = proxy.Port,
            ProxyCredentials = proxy.User.Length > 0 ? new NetworkCredential(proxy.User, proxy.Password ?? "") : null,
            FtpHost = address.Host,
            FtpPort = address.Port,
            FtpCredentials = credential,
        };
        AsyncFtpClient client = proxy.Kind == ProxyKind.Socks5
            ? new FluentFTP.Proxy.AsyncProxy.AsyncFtpClientSocks5Proxy(profile)
            : new FluentFTP.Proxy.AsyncProxy.AsyncFtpClientHttp11Proxy(profile);
        client.Config = config;
        return client;
    }

    private static FtpParser Parser(ServerType type) => type switch
    {
        ServerType.Unix => FtpParser.Unix,
        ServerType.Windows => FtpParser.Windows,
        ServerType.Vms => FtpParser.VMS,
        ServerType.IbmZos => FtpParser.IBMzOS,
        ServerType.IbmI => FtpParser.IBMOS400,
        ServerType.Mlsd => FtpParser.Machine,
        _ => FtpParser.Auto,
    };

    internal static System.Text.Encoding EncodingOf(int codePage)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        try
        {
            return System.Text.Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return System.Text.Encoding.UTF8;
        }
    }

    private FtpListOption ListOption => _options.Listing.ListCommand switch
    {
        ListCommand.ListAll => FtpListOption.ForceList | FtpListOption.AllFiles,
        ListCommand.List => FtpListOption.ForceList,
        _ => FtpListOption.AllFiles,
    };

    internal static Exception Translate(Exception ex, ServerAddress address) => ex switch
    {
        IOException io => io,
        FtpAuthenticationException => new SignInRefusedException(Strings.Server_SignInRefused, SecretKind.Password),
        SocketException s => new IOException(Loc.Format(Strings.Server_CantReach, address.Host, s.Message), s),
        TimeoutException => new IOException(Loc.Format(Strings.Server_TimedOut, address.Host), ex),
        FtpCommandException { CompletionCode: "550" } c => new DirectoryNotFoundException(c.Message, c),
        FtpException or AuthenticationException => new IOException(ex.Message, ex),
        _ => ex,
    };

    public async Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _context.Log.Add(Loc.Format(Strings.ServerLog_Listing, path));
            var listing = await _client.GetListing(path, ListOption, cancellationToken).ConfigureAwait(false);
            var items = new List<RemoteItem>(listing.Length);
            foreach (var item in listing)
            {
                if (item.Name is "." or ".." || item.Name.Length == 0 || item.Name.Contains('/'))
                {
                    continue;
                }

                var type = item.Type;
                var attributes = FileAttributes.Normal;
                var size = item.Size;
                if (type == FtpObjectType.Link)
                {
                    attributes = FileAttributes.ReparsePoint;
                    type = item.LinkObject?.Type ?? (await _client.DirectoryExists(item.FullName, cancellationToken).ConfigureAwait(false) ? FtpObjectType.Directory : FtpObjectType.File);
                    size = item.LinkObject?.Size ?? 0;
                }

                if (type == FtpObjectType.Directory)
                {
                    attributes = (attributes & ~FileAttributes.Normal) | FileAttributes.Directory;
                    size = 0;
                }

                var time = item.Modified == DateTime.MinValue ? 0 : ServerTime(item.Modified).Ticks;
                items.Add(new RemoteItem(new FileEntry(item.Name, Math.Max(0, size), time, attributes), Rights(item)));
            }

            _context.Log.Add(Loc.Plural(items.Count, Strings.ServerLog_Items_One, Strings.ServerLog_Items_Other));
            return items;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.Log.Add(Loc.Format(Strings.ServerLog_Failed, ex.Message));
            throw Translate(ex, Server);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Rights(FtpListItem item)
    {
        if (item.RawPermissions is { Length: >= 9 } raw)
        {
            var r = raw.Length == 10 ? raw[1..] : raw[^9..];
            return r.All(c => c is 'r' or 'w' or 'x' or '-' or 's' or 'S' or 't' or 'T') ? r : "";
        }

        return item.Chmod > 0 ? FromOctal(item.Chmod) : "";
    }

    private static string FromOctal(int chmod)
    {
        var digits = chmod.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(3, '0')[^3..];
        return string.Concat(digits.Select(d => (d - '0') is var v and >= 0 and <= 7 ? (((v & 4) != 0 ? "r" : "-") + ((v & 2) != 0 ? "w" : "-") + ((v & 1) != 0 ? "x" : "-")) : "---"));
    }

    public async Task DownloadAsync(string path, Stream to, long offset, Action<long> progress, CancellationToken cancellationToken)
    {
        var client = NewClient(Server, _options, _mode, _credential, _context, _acceptedCertificates, _context.Log, null, cancellationToken, DataType(path));
        try
        {
            await client.Connect(cancellationToken).ConfigureAwait(false);
            var ok = await client.DownloadStream(to, path, offset, new Progress(p => progress(p.TransferredBytes)), cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                throw new IOException(Loc.Format(Strings.Server_DownloadFailed, ServerAddress.NormalizePath(path)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex, Server);
        }
        finally
        {
            await QuietlyAsync(client).ConfigureAwait(false);
        }
    }

    public async Task UploadAsync(string path, Stream from, long offset, Action<long> progress, CancellationToken cancellationToken)
    {
        var client = NewClient(Server, _options, _mode, _credential, _context, _acceptedCertificates, _context.Log, null, cancellationToken, DataType(path));
        try
        {
            await client.Connect(cancellationToken).ConfigureAwait(false);
            await using (var to = offset > 0
                ? await client.OpenAppend(path, DataType(path), checkIfFileExists: false, token: cancellationToken).ConfigureAwait(false)
                : await client.OpenWrite(path, DataType(path), checkIfFileExists: false, token: cancellationToken).ConfigureAwait(false))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    progress(done);
                }
            }

            var reply = await client.GetReply(cancellationToken).ConfigureAwait(false);
            if (!reply.Success)
            {
                throw new IOException(reply.ErrorMessage is { Length: > 0 } m ? m : reply.Message);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex, Server);
        }
        finally
        {
            await QuietlyAsync(client).ConfigureAwait(false);
        }
    }

    public Task CreateFolderAsync(string path, CancellationToken cancellationToken)
        => CommandAsync(async ct =>
        {
            if (!await _client.CreateDirectory(path, false, ct).ConfigureAwait(false))
            {
                throw new IOException(Loc.Format(Strings.Server_CantMake, path));
            }
        }, cancellationToken);

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken) => CommandAsync(ct => _client.DeleteFile(path, ct), cancellationToken);

    public Task DeleteFolderAsync(string path, CancellationToken cancellationToken)
        => CommandAsync(async ct =>
        {
            var reply = await _client.Execute("RMD " + path, ct).ConfigureAwait(false);
            if (!reply.Success)
            {
                throw new IOException(reply.Message);
            }
        }, cancellationToken);

    public Task RenameAsync(string from, string to, CancellationToken cancellationToken) => CommandAsync(ct => _client.Rename(from, to, ct), cancellationToken);

    public Task SetPermissionsAsync(string path, int mode, CancellationToken cancellationToken)
        => CommandAsync(async ct =>
        {
            var reply = await _client.Execute("SITE CHMOD " + Convert.ToString(mode & 0xFFF, 8) + " " + path, ct).ConfigureAwait(false);
            if (!reply.Success)
            {
                throw new IOException(Loc.Format(Strings.Server_NoChmod, reply.Message));
            }
        }, cancellationToken);

    public async Task<bool> SetTimeAsync(string path, DateTime utc, CancellationToken cancellationToken)
    {
        if (!_client.HasFeature(FtpCapability.MFMT))
        {
            return false;
        }

        await CommandAsync(ct => _client.SetModifiedTime(path, utc, ct), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task KeepAliveAsync(CancellationToken cancellationToken) => CommandAsync(ct => _client.Execute("NOOP", ct), cancellationToken);

    private FtpDataType DataType(string path)
        => RemoteOptions.IsAscii(_options.Transfers, path[(path.LastIndexOf('/') + 1)..]) ? FtpDataType.ASCII : FtpDataType.Binary;

    private DateTime ServerTime(DateTime listed)
        => DateTime.SpecifyKind(_options.Listing.TimeZoneMinutes is { } minutes ? listed.AddMinutes(-minutes) : listed, DateTimeKind.Utc);

    private async Task CommandAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await work(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex, Server);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class Progress(Action<FtpProgress> report) : IProgress<FtpProgress>
    {
        public void Report(FtpProgress value) => report(value);
    }

    private static async Task QuietlyAsync(AsyncFtpClient client)
    {
        try
        {
            if (client.IsConnected)
            {
                await client.Disconnect().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or FtpException or SocketException or TimeoutException or ObjectDisposedException)
        {
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync() => await QuietlyAsync(_client).ConfigureAwait(false);
}

public sealed class ServerConnector : IServerConnector
{
    public Task<IRemoteSession> ConnectAsync(ServerTarget target, ConnectContext context, CancellationToken cancellationToken)
        => Task.Run(() => target.Address.Kind == ServerKind.Sftp
            ? SftpSession.ConnectAsync(target, context, cancellationToken)
            : FtpSession.ConnectAsync(target, context, cancellationToken), cancellationToken);
}
