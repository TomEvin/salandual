// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/MemoryServers.cs

using System.Buffers.Binary;
using System.Text;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Remote;

public sealed class MemoryServer(string host)
{
    public string Host => host;

    public string? Password { get; set; } = "secret";

    public byte[] Key { get; set; } = MemoryServers.FakeKey(1);

    public bool NoTls { get; set; }

    public string Home { get; set; } = "/home/deploy";

    public Dictionary<string, byte[]?> Files { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Rights { get; } = new(StringComparer.Ordinal);

    public DateTime Time { get; set; } = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    public int Connects { get; set; }

    public Task? Hold { get; set; }

    public Task? ConnectHold { get; set; }

    public string? Refuse { get; set; }

    public Task? DownloadHold { get; set; }

    public Task? UploadHold { get; set; }

    public Dictionary<string, DateTime> Times { get; } = new(StringComparer.Ordinal);

    public bool NoChmod { get; set; }

    public bool ReadOnly { get; set; }

    public int KeepAlives { get; set; }

    public int Listings { get; set; }

    public int MostAtOnce { get; set; }

    internal int Running;

    public int DropHalfway { get; set; }

    public ServerTarget? LastTarget { get; set; }

    public void AddFolder(string path)
    {
        var p = ServerAddress.NormalizePath(path);
        while (p != "/")
        {
            Files[p] = null;
            var cut = p.LastIndexOf('/');
            p = cut <= 0 ? "/" : p[..cut];
        }
    }

    public void AddFile(string path, string content, string rights = "rw-r--r--")
    {
        path = ServerAddress.NormalizePath(path);
        var cut = path.LastIndexOf('/');
        AddFolder(cut <= 0 ? "/" : path[..cut]);
        Files[path] = Encoding.UTF8.GetBytes(content);
        Rights[path] = rights;
    }
}

public sealed class MemoryServers : IServerConnector
{
    private readonly List<MemorySession> _sessions = [];

    public Dictionary<string, MemoryServer> Servers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public MemoryServer Add(string host)
    {
        var server = new MemoryServer(host);
        Servers[host] = server;
        return server;
    }

    public static byte[] FakeKey(byte seed)
    {
        var type = "ssh-ed25519"u8;
        var blob = new byte[4 + type.Length + 4 + 32];
        BinaryPrimitives.WriteUInt32BigEndian(blob, (uint)type.Length);
        type.CopyTo(blob.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(4 + type.Length), 32);
        blob.AsSpan(8 + type.Length).Fill(seed);
        return blob;
    }

    public void DropAll()
    {
        lock (_sessions)
        {
            foreach (var s in _sessions)
            {
                s.Connected = false;
            }
        }
    }

    public async Task<IRemoteSession> ConnectAsync(ServerTarget target, ConnectContext context, CancellationToken cancellationToken)
    {
        var address = target.Address;
        context.Log.Add(Loc.Format(Strings.ServerLog_Connecting, address.Host, address.Port));
        if (!Servers.TryGetValue(address.Host, out var server))
        {
            throw new IOException(Loc.Format(Strings.Server_CantReach, address.Host, "No such host is known."));
        }

        if (server.ConnectHold is { } hold)
        {
            await hold.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (server.Refuse is { } reason)
        {
            context.Log.Add(reason);
            throw new IOException(Loc.Format(Strings.Server_CantReach, address.Host, reason));
        }

        var plain = false;
        if (address.Kind == ServerKind.Sftp)
        {
            var algorithm = TrustedServers.SshAlgorithm(server.Key);
            var fingerprint = TrustedServers.SshFingerprint(server.Key);
            switch (context.Trusted.CheckSsh(address.Host, address.Port, server.Key))
            {
                case KeyTrust.Changed:
                    context.Log.Add(Loc.Format(Strings.ServerLog_KeyChanged, algorithm, fingerprint));
                    throw new ServerKeyChangedException(Loc.Format(Strings.Server_KeyChanged, address.Host), algorithm, "SHA256:" + fingerprint);
                case KeyTrust.Unknown:
                    var answer = await context.Questions.AskTrustAsync(new TrustQuestion(address, algorithm, "SHA256:" + fingerprint), cancellationToken).ConfigureAwait(false);
                    if (answer == TrustAnswer.Cancel)
                    {
                        context.Log.Add(Loc.Format(Strings.ServerLog_KeyRefused, algorithm, fingerprint));
                        throw new ConnectCancelledException();
                    }

                    if (answer == TrustAnswer.Remember)
                    {
                        context.Trusted.Remember(address.Host, address.Port, "ssh", algorithm, fingerprint);
                    }

                    context.Log.Add(Loc.Format(Strings.ServerLog_KeyTrusted, algorithm, fingerprint));
                    break;
                default:
                    context.Log.Add(Loc.Format(Strings.ServerLog_KeyKnown, algorithm, fingerprint));
                    break;
            }
        }
        else if (server.NoTls)
        {
            plain = true;
            if (address.Kind == ServerKind.Ftps)
            {
                throw new IOException("TLS isn't offered.");
            }

            if (address.Kind == ServerKind.Ftp && !target.PlainAllowed)
            {
                context.Log.Add(Strings.ServerLog_NoTls);
                if (!await context.Questions.AskPlainAsync(address, cancellationToken).ConfigureAwait(false))
                {
                    throw new ConnectCancelledException();
                }

                context.SaidYesToPlain = true;
            }
        }
        else
        {
            plain = address.Kind == ServerKind.FtpPlain;
        }

        var anonymous = address.Kind != ServerKind.Sftp && (address.User.Length == 0 || address.User == "anonymous");
        var wrong = false;
        for (var attempt = 0; !anonymous; attempt++)
        {
            var password = await context.Secrets.GetAsync(SecretKind.Password, wrong, cancellationToken).ConfigureAwait(false) ?? throw new ConnectCancelledException();
            if (server.Password is null || password == server.Password)
            {
                break;
            }

            context.Log.Add(Strings.ServerLog_SignInRefused);
            if (attempt >= 3)
            {
                throw new SignInRefusedException(Strings.Server_SignInRefused, SecretKind.Password);
            }

            wrong = true;
        }

        context.Log.Add(Loc.Format(Strings.ServerLog_SignedIn, anonymous ? "anonymous" : address.User));
        server.Connects++;
        server.LastTarget = target;
        var session = new MemorySession(address.WithPath("/"), server, context.Log, address.Kind == ServerKind.Sftp ? "SFTP" : plain ? "FTP" : "FTPS", !plain);
        lock (_sessions)
        {
            _sessions.Add(session);
        }

        return session;
    }

    private sealed class MemorySession(ServerAddress address, MemoryServer server, ConnectionLog log, string security, bool encrypted) : IRemoteSession
    {
        public ServerAddress Server => address;

        public string Security => security;

        public bool Encrypted => encrypted;

        public bool Connected { get; set; } = true;

        public bool IsConnected => Connected;

        public string Home => server.Home;

        public async Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken cancellationToken)
        {
            if (server.Hold is { } hold)
            {
                await hold.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return ListNow(path);
        }

        private IReadOnlyList<RemoteItem> ListNow(string path)
        {
            if (!Connected)
            {
                throw new IOException("The connection was closed.");
            }

            path = ServerAddress.NormalizePath(path);
            log.Add(Loc.Format(Strings.ServerLog_Listing, path));
            server.Listings++;
            if (path != "/" && (!server.Files.TryGetValue(path, out var folder) || folder is not null))
            {
                throw new DirectoryNotFoundException(path);
            }

            var prefix = path == "/" ? "/" : path + "/";
            var items = server.Files.Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal) && f.Key.IndexOf('/', prefix.Length) < 0 && f.Key.Length > prefix.Length)
                .Select(f => new RemoteItem(new FileEntry(f.Key[prefix.Length..], f.Value?.Length ?? 0, server.Times.GetValueOrDefault(f.Key, server.Time).Ticks, f.Value is null ? FileAttributes.Directory : FileAttributes.Normal),
                    server.Rights.GetValueOrDefault(f.Key, f.Value is null ? "rwxr-xr-x" : "rw-r--r--")))
                .ToList();
            log.Add(Loc.Plural(items.Count, Strings.ServerLog_Items_One, Strings.ServerLog_Items_Other));
            return items;
        }

        public async Task DownloadAsync(string path, Stream to, long offset, Action<long> progress, CancellationToken cancellationToken)
        {
            if (!Connected)
            {
                throw new IOException("The connection was closed.");
            }

            if (!server.Files.TryGetValue(ServerAddress.NormalizePath(path), out var bytes) || bytes is null)
            {
                throw new FileNotFoundException(path);
            }

            using var running = Running();
            if (server.DownloadHold is { } hold)
            {
                progress((bytes.Length - offset) / 2);
                await hold.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            var start = (int)Math.Min(offset, bytes.Length);
            if (DropNow())
            {
                var half = (bytes.Length - start) / 2;
                await to.WriteAsync(bytes.AsMemory(start, half), cancellationToken).ConfigureAwait(false);
                progress(half);
                throw new IOException("The connection was closed.");
            }

            await to.WriteAsync(bytes.AsMemory(start), cancellationToken).ConfigureAwait(false);
            progress(bytes.Length - offset);
        }

        private Counted Running()
        {
            lock (server.Files)
            {
                server.Running++;
                server.MostAtOnce = Math.Max(server.MostAtOnce, server.Running);
            }

            return new Counted(server);
        }

        private sealed class Counted(MemoryServer server) : IDisposable
        {
            public void Dispose()
            {
                lock (server.Files)
                {
                    server.Running--;
                }
            }
        }

        private bool DropNow()
        {
            lock (server.Files)
            {
                if (server.DropHalfway <= 0)
                {
                    return false;
                }

                server.DropHalfway--;
                Connected = false;
                return true;
            }
        }

        public Task KeepAliveAsync(CancellationToken cancellationToken)
        {
            if (!Connected)
            {
                throw new IOException("The connection was closed.");
            }

            server.KeepAlives++;
            return Task.CompletedTask;
        }


        public async Task UploadAsync(string path, Stream from, long offset, Action<long> progress, CancellationToken cancellationToken)
        {
            Alive();
            path = ServerAddress.NormalizePath(path);
            Parent(path);
            using var buffer = new MemoryStream();
            if (offset > 0)
            {
                if (!server.Files.TryGetValue(path, out var had) || had is null || had.Length < offset)
                {
                    throw new IOException("Resume: the file on the server is shorter.");
                }

                buffer.Write(had, 0, (int)offset);
            }

            using var running = Running();
            if (DropNow())
            {
                var half = new byte[Math.Max(1, (from.Length - from.Position) / 2)];
                var n = await from.ReadAsync(half, cancellationToken).ConfigureAwait(false);
                buffer.Write(half, 0, n);
                lock (server.Files)
                {
                    server.Files[path] = buffer.ToArray();
                }

                progress(n);
                throw new IOException("The connection was closed.");
            }

            if (server.UploadHold is { } hold)
            {
                var half = new byte[Math.Max(1, (from.Length - from.Position) / 2)];
                var n = await from.ReadAsync(half, cancellationToken).ConfigureAwait(false);
                buffer.Write(half, 0, n);
                lock (server.Files)
                {
                    server.Files[path] = buffer.ToArray();
                }

                progress(n);
                await hold.WaitAsync(cancellationToken).ConfigureAwait(false);
                await from.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                progress(buffer.Length - offset);
            }
            else
            {
                await from.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                progress(buffer.Length - offset);
            }

            lock (server.Files)
            {
                server.Files[path] = buffer.ToArray();
                server.Rights.TryAdd(path, "rw-r--r--");
                server.Times[path] = DateTime.UtcNow;
            }
        }

        public Task CreateFolderAsync(string path, CancellationToken cancellationToken)
        {
            Alive();
            path = ServerAddress.NormalizePath(path);
            Parent(path);
            if (server.Files.ContainsKey(path))
            {
                throw new IOException("Already there: " + path);
            }

            server.Files[path] = null;
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
        {
            Alive();
            path = ServerAddress.NormalizePath(path);
            if (!server.Files.TryGetValue(path, out var bytes) || bytes is null)
            {
                throw new FileNotFoundException(path);
            }

            server.Files.Remove(path);
            server.Rights.Remove(path);
            return Task.CompletedTask;
        }

        public Task DeleteFolderAsync(string path, CancellationToken cancellationToken)
        {
            Alive();
            path = ServerAddress.NormalizePath(path);
            if (!server.Files.TryGetValue(path, out var bytes) || bytes is not null)
            {
                throw new DirectoryNotFoundException(path);
            }

            if (server.Files.Keys.Any(k => k.StartsWith(path + "/", StringComparison.Ordinal)))
            {
                throw new IOException("The folder isn't empty.");
            }

            server.Files.Remove(path);
            return Task.CompletedTask;
        }

        public Task RenameAsync(string from, string to, CancellationToken cancellationToken)
        {
            Alive();
            from = ServerAddress.NormalizePath(from);
            to = ServerAddress.NormalizePath(to);
            if (!server.Files.ContainsKey(from))
            {
                throw new FileNotFoundException(from);
            }

            if (server.Files.ContainsKey(to))
            {
                throw new IOException("Already there: " + to);
            }

            Parent(to);
            foreach (var key in server.Files.Keys.Where(k => k == from || k.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
            {
                var moved = to + key[from.Length..];
                server.Files[moved] = server.Files[key];
                server.Files.Remove(key);
                if (server.Rights.Remove(key, out var r))
                {
                    server.Rights[moved] = r;
                }
            }

            return Task.CompletedTask;
        }

        public Task SetPermissionsAsync(string path, int mode, CancellationToken cancellationToken)
        {
            Alive();
            path = ServerAddress.NormalizePath(path);
            if (server.NoChmod)
            {
                throw new IOException(Loc.Format(Strings.Server_NoChmod, "500 'SITE CHMOD': command not understood"));
            }

            if (!server.Files.ContainsKey(path))
            {
                throw new FileNotFoundException(path);
            }

            server.Rights[path] = RemoteFileSystem.RightsText(mode);
            return Task.CompletedTask;
        }

        public Task<bool> SetTimeAsync(string path, DateTime utc, CancellationToken cancellationToken)
        {
            Alive();
            server.Times[ServerAddress.NormalizePath(path)] = utc;
            return Task.FromResult(true);
        }

        private void Alive()
        {
            if (!Connected)
            {
                throw new IOException("The connection was closed.");
            }

            if (server.ReadOnly)
            {
                throw new UnauthorizedAccessException("Permission denied");
            }
        }

        private void Parent(string path)
        {
            var cut = path.LastIndexOf('/');
            var parent = cut <= 0 ? "/" : path[..cut];
            if (parent != "/" && (!server.Files.TryGetValue(parent, out var p) || p is not null))
            {
                throw new DirectoryNotFoundException(parent);
            }
        }

        public ValueTask DisposeAsync()
        {
            Connected = false;
            return ValueTask.CompletedTask;
        }
    }
}
