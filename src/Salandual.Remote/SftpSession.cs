// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/SftpSession.cs

using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Remote;

internal sealed class SftpSession : IRemoteSession
{
    private readonly SftpClient _client;
    private readonly ConnectionLog _log;

    private SftpSession(ServerAddress server, SftpClient client, ConnectionLog log)
    {
        Server = server;
        _client = client;
        _log = log;
        Home = client.WorkingDirectory is { Length: > 0 } home ? home : "/";
    }

    public ServerAddress Server { get; }

    public string Security => "SFTP";

    public bool Encrypted => true;

    public bool IsConnected => _client.IsConnected;

    public string Home { get; }

    public static async Task<IRemoteSession> ConnectAsync(ServerTarget target, ConnectContext context, CancellationToken cancellationToken)
    {
        var address = target.Address;
        var log = context.Log;
        var wrong = false;
        string? trustedOnce = null;
        for (var attempt = 0; ; attempt++)
        {
            var (methods, kind) = await SignInMethodAsync(target, context, wrong, cancellationToken).ConfigureAwait(false);
            var info = Info(target, context, methods);
            var client = new SftpClient(info) { OperationTimeout = TimeSpan.FromSeconds(target.Settings.Connection.AnswerSeconds) };
            string? changed = null;
            client.HostKeyReceived += (_, e) =>
            {
                var blob = e.HostKey;
                var algorithm = TrustedServers.SshAlgorithm(blob);
                var fingerprint = TrustedServers.SshFingerprint(blob);
                switch (fingerprint == trustedOnce ? KeyTrust.Trusted : context.Trusted.CheckSsh(address.Host, address.Port, blob))
                {
                    case KeyTrust.Trusted:
                        log.Add(Loc.Format(Strings.ServerLog_KeyKnown, algorithm, fingerprint));
                        e.CanTrust = true;
                        return;
                    case KeyTrust.Changed:
                        changed = algorithm + " SHA256:" + fingerprint;
                        log.Add(Loc.Format(Strings.ServerLog_KeyChanged, algorithm, fingerprint));
                        e.CanTrust = false;
                        return;
                }

                var answer = context.Questions.AskTrustAsync(new TrustQuestion(address, algorithm, "SHA256:" + fingerprint), cancellationToken).GetAwaiter().GetResult();
                if (answer == TrustAnswer.Remember)
                {
                    context.Trusted.Remember(address.Host, address.Port, "ssh", algorithm, fingerprint);
                }
                else if (answer == TrustAnswer.Once)
                {
                    trustedOnce = fingerprint;
                }

                log.Add(Loc.Format(answer == TrustAnswer.Cancel ? Strings.ServerLog_KeyRefused : Strings.ServerLog_KeyTrusted, algorithm, fingerprint));
                e.CanTrust = answer != TrustAnswer.Cancel;
                if (answer == TrustAnswer.Cancel)
                {
                    changed = "";
                }
            };

            log.Add(Loc.Format(Strings.ServerLog_Connecting, address.Host, address.Port));
            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                log.Add(Loc.Format(target.SignIn switch { ServerSignIn.KeyFile => Strings.ServerLog_SignedInKey, ServerSignIn.Agent => Strings.ServerLog_SignedInAgent, _ => Strings.ServerLog_SignedIn }, address.User));
                return new SftpSession(address, client, log);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                client.Dispose();
                if (changed is { Length: > 0 })
                {
                    throw new ServerKeyChangedException(Loc.Format(Strings.Server_KeyChanged, address.Host), changed.Split(' ')[0], changed.Split(' ')[1]);
                }

                if (changed is not null)
                {
                    throw new ConnectCancelledException();
                }

                if (ex is SshAuthenticationException && attempt < 3 && target.SignIn == ServerSignIn.Password)
                {
                    log.Add(Strings.ServerLog_SignInRefused);
                    wrong = true;
                    continue;
                }

                throw Translate(ex, address);
            }
        }
    }

    private static async Task<(AuthenticationMethod[] Methods, SecretKind? Kind)> SignInMethodAsync(ServerTarget target, ConnectContext context, bool wrong, CancellationToken cancellationToken)
    {
        var user = target.Address.User;
        if (target.SignIn == ServerSignIn.Agent)
        {
            var agent = SshAgent.Find() ?? throw new IOException(Strings.Server_NoAgent);
            var source = agent.KeySource();
            if (source.HostKeyAlgorithms.Count == 0)
            {
                throw new IOException(Strings.Server_NoAgent);
            }

            context.Log.Add(Loc.Plural(source.HostKeyAlgorithms.Select(a => a.Data).Distinct(BlobComparer.Instance).Count(), Strings.ServerLog_AgentKeys_One, Strings.ServerLog_AgentKeys_Other));
            return ([new PrivateKeyAuthenticationMethod(user, source)], null);
        }

        if (target.SignIn == ServerSignIn.KeyFile)
        {
            if (!File.Exists(target.KeyFile))
            {
                throw new IOException(Loc.Format(Strings.Server_NoKeyFile, target.KeyFile));
            }

            try
            {
                return ([new PrivateKeyAuthenticationMethod(user, new PrivateKeyFile(target.KeyFile))], null);
            }
            catch (SshPassPhraseNullOrEmptyException)
            {
            }
            catch (Exception ex) when (ex is SshException or InvalidOperationException or FormatException or ArgumentException or NotSupportedException)
            {
                throw new IOException(Loc.Format(Strings.Server_BadKeyFile, Path.GetFileName(target.KeyFile)), ex);
            }

            var passphraseWrong = false;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var passphrase = await context.Secrets.GetAsync(SecretKind.Passphrase, passphraseWrong, cancellationToken).ConfigureAwait(false) ?? throw new ConnectCancelledException();
                try
                {
                    return ([new PrivateKeyAuthenticationMethod(user, new PrivateKeyFile(target.KeyFile, passphrase))], SecretKind.Passphrase);
                }
                catch (Exception ex) when (ex is SshException or System.Security.Cryptography.CryptographicException or InvalidOperationException or FormatException or ArgumentException)
                {
                    passphraseWrong = true;
                }
            }

            throw new IOException(Strings.Server_PassphraseWrong);
        }

        var password = await context.Secrets.GetAsync(SecretKind.Password, wrong, cancellationToken).ConfigureAwait(false) ?? throw new ConnectCancelledException();
        var keyboard = new KeyboardInteractiveAuthenticationMethod(user);
        keyboard.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
            {
                prompt.Response = password;
            }
        };
        return ([new PasswordAuthenticationMethod(user, password), keyboard], SecretKind.Password);
    }

    internal static ConnectionInfo Info(ServerTarget target, ConnectContext context, AuthenticationMethod[] methods)
    {
        var address = target.Address;
        var settings = target.Settings;
        ConnectionInfo info;
        if (RemoteOptions.Proxy(settings.Proxy, address, context.ProxyPassword) is { } proxy)
        {
            context.Log.Add(Loc.Format(Strings.ServerLog_Proxy, proxy.Host, proxy.Port));
            info = new ConnectionInfo(address.Host, address.Port, address.User, proxy.Kind == ProxyKind.Socks5 ? ProxyTypes.Socks5 : ProxyTypes.Http,
                proxy.Host, proxy.Port, proxy.User.Length > 0 ? proxy.User : null, proxy.User.Length > 0 ? proxy.Password ?? "" : null, methods);
        }
        else
        {
            info = new ConnectionInfo(address.Host, address.Port, address.User, methods);
        }

        info.Timeout = TimeSpan.FromSeconds(settings.Connection.ConnectSeconds);
        const string zlib = "zlib@openssh.com";
        if (!settings.Transfers.Compress)
        {
            info.CompressionAlgorithms.Remove(zlib);
        }
        else if (info.CompressionAlgorithms.ContainsKey(zlib))
        {
            info.CompressionAlgorithms.SetPosition(zlib, 0);
        }

        return info;
    }

    public async Task KeepAliveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.GetAttributesAsync(Home, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex, Server);
        }
    }

    private sealed class BlobComparer : IEqualityComparer<byte[]>
    {
        public static BlobComparer Instance { get; } = new();

        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj) => obj.Length > 8 ? BitConverter.ToInt32(obj, obj.Length - 4) : obj.Length;
    }

    internal static Exception Translate(Exception ex, ServerAddress address) => ex switch
    {
        IOException io => io,
        SshAuthenticationException => new SignInRefusedException(Strings.Server_SignInRefused, SecretKind.Password),
        SocketException s => new IOException(Loc.Format(Strings.Server_CantReach, address.Host, s.Message), s),
        SshOperationTimeoutException or TimeoutException => new IOException(Loc.Format(Strings.Server_TimedOut, address.Host), ex),
        SftpPathNotFoundException => new DirectoryNotFoundException(ex.Message, ex),
        SftpPermissionDeniedException => new UnauthorizedAccessException(ex.Message, ex),
        SshException => new IOException(ex.Message, ex),
        _ => ex,
    };

    public async Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken cancellationToken)
    {
        _log.Add(Loc.Format(Strings.ServerLog_Listing, path));
        var items = new List<RemoteItem>();
        try
        {
            await foreach (var file in _client.ListDirectoryAsync(path, cancellationToken).ConfigureAwait(false))
            {
                if (file.Name is "." or ".." || file.Name.Length == 0)
                {
                    continue;
                }

                var isFolder = file.IsDirectory;
                var size = file.Length;
                var time = file.LastWriteTimeUtc;
                var attributes = FileAttributes.Normal;
                if (file.IsSymbolicLink)
                {
                    attributes = FileAttributes.ReparsePoint;
                    try
                    {
                        var target = await _client.GetAttributesAsync(file.FullName, cancellationToken).ConfigureAwait(false);
                        isFolder = target.IsDirectory;
                        size = isFolder ? 0 : target.Size;
                        time = target.LastWriteTimeUtc;
                    }
                    catch (SshException)
                    {
                    }
                }

                if (isFolder)
                {
                    attributes = (attributes & ~FileAttributes.Normal) | FileAttributes.Directory;
                    size = 0;
                }

                items.Add(new RemoteItem(new FileEntry(file.Name, size, time.Ticks, attributes), Rights(file)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Add(Loc.Format(Strings.ServerLog_Failed, ex.Message));
            throw Translate(ex, Server);
        }

        _log.Add(Loc.Plural(items.Count, Strings.ServerLog_Items_One, Strings.ServerLog_Items_Other));
        return items;
    }

    private static string Rights(ISftpFile f)
        => new([f.OwnerCanRead ? 'r' : '-', f.OwnerCanWrite ? 'w' : '-', f.OwnerCanExecute ? 'x' : '-', f.GroupCanRead ? 'r' : '-', f.GroupCanWrite ? 'w' : '-',
            f.GroupCanExecute ? 'x' : '-', f.OthersCanRead ? 'r' : '-', f.OthersCanWrite ? 'w' : '-', f.OthersCanExecute ? 'x' : '-']);

    public async Task DownloadAsync(string path, Stream to, long offset, Action<long> progress, CancellationToken cancellationToken)
    {
        try
        {
            await using var from = await _client.OpenAsync(path, FileMode.Open, FileAccess.Read, cancellationToken).ConfigureAwait(false);
            if (offset > 0)
            {
                from.Seek(offset, SeekOrigin.Begin);
            }

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex, Server);
        }
    }

    public async Task UploadAsync(string path, Stream from, long offset, Action<long> progress, CancellationToken cancellationToken)
    {
        try
        {
            await using var to = await _client.OpenAsync(path, offset > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write, cancellationToken).ConfigureAwait(false);
            if (offset > 0)
            {
                to.Seek(offset, SeekOrigin.Begin);
            }

            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                progress(done);
            }

            if (offset == 0 && done == 0)
            {
                to.SetLength(0);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex, Server);
        }
    }

    public Task CreateFolderAsync(string path, CancellationToken cancellationToken) => RunAsync(() => _client.CreateDirectory(path), cancellationToken);

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken) => RunAsync(() => _client.DeleteFile(path), cancellationToken);

    public Task DeleteFolderAsync(string path, CancellationToken cancellationToken) => RunAsync(() => _client.DeleteDirectory(path), cancellationToken);

    public Task RenameAsync(string from, string to, CancellationToken cancellationToken) => RunAsync(() => _client.RenameFile(from, to), cancellationToken);

    public Task SetPermissionsAsync(string path, int mode, CancellationToken cancellationToken)
        => RunAsync(() => _client.ChangePermissions(path, (short)int.Parse(Convert.ToString(mode & 0x1FF, 8), System.Globalization.CultureInfo.InvariantCulture)), cancellationToken);

    public async Task<bool> SetTimeAsync(string path, DateTime utc, CancellationToken cancellationToken)
    {
        await RunAsync(() =>
        {
            var attributes = _client.GetAttributes(path);
            attributes.LastWriteTimeUtc = utc;
            attributes.LastAccessTimeUtc = utc;
            _client.SetAttributes(path, attributes);
        }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task RunAsync(Action work, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(work, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex, Server);
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }
        finally
        {
            _client.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
