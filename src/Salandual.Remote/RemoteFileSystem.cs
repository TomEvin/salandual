// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Remote/RemoteFileSystem.cs

using System.Collections.Concurrent;
using System.Diagnostics;
using Salandual.Core.Resources;
using Salandual.Core.Settings;
using Salandual.Core.Vfs;

namespace Salandual.Remote;

public sealed record OpenServer(ServerAddress Server, string Security, bool Encrypted, string? Bookmark, ConnectionLog Log, string Home = "/");

public sealed class RemoteFileSystem(IFileSystemProvider inner, IServerConnector connector, ISecretStore secrets, TrustedServers trusted) : IFileSystemProvider
{
    private readonly ConcurrentDictionary<string, Connection> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _rights = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConnectionLog> _logs = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Connection(ServerTarget target, SecretSource secrets, ConnectionLog log)
    {
        public ServerTarget Target { get; set; } = target;

        public SecretSource Secrets => secrets;

        public ConnectionLog Log => log;

        public IRemoteSession? Session { get; set; }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public DateTime LastUsed { get; set; }

        public DateTime LastPing { get; set; }

        public bool KeepAliveStopped { get; set; }

        public bool WasOpen { get; set; }
    }

    private sealed record Listed(DateTime At, IReadOnlyList<RemoteItem> Items);

    private readonly ConcurrentDictionary<string, Listed> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _slots = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _keepAlive;

    public ServerOptions Defaults { get; set; } = ServerOptions.BuiltIn;

    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    public IFileSystemProvider Inner => inner;

    public TrustedServers Trusted => trusted;

    public ISecretStore Secrets => secrets;

    public IServerQuestions? Questions { get; set; }

    public Func<ServerAddress, ServerTarget?>? BookmarkFor { get; set; }

    public event Action<ServerTarget>? PlainAllowed;

    public event Action? ServersChanged;

    public ProviderCapabilities Capabilities => inner.Capabilities;

    public IReadOnlyList<OpenServer> OpenServers
        => [.. _open.Values.Where(c => c.Session is { IsConnected: true }).Select(c => new OpenServer(c.Session!.Server, c.Session.Security, c.Session.Encrypted, c.Target.Bookmark, c.Log, c.Session.Home))];

    public OpenServer? ServerOf(string path)
        => ServerAddress.Parse(path) is { } address && _open.TryGetValue(address.Key, out var c) && c.Session is { } s
            ? new OpenServer(s.Server, s.Security, s.Encrypted, c.Target.Bookmark, c.Log, s.Home)
            : null;

    public ConnectionLog LogOf(ServerAddress server) => _logs.GetOrAdd(server.Key, _ => new ConnectionLog());

    public string? RightsOf(string folder, string name) => _rights.TryGetValue(folder, out var r) ? r.GetValueOrDefault(name) : null;

    public async Task<string> ConnectAsync(ServerTarget target, CancellationToken cancellationToken = default)
    {
        if (target.Options is null)
        {
            target = target with { Options = BookmarkFor?.Invoke(target.Address)?.Options ?? Defaults };
        }

        var connection = await OpenAsync(target, cancellationToken).ConfigureAwait(false);
        var session = connection.Session!;
        var path = target.Address.Path != "/" ? target.Address.Path : session.Home;
        return target.Address.WithPath(path).ToString();
    }

    private async Task<Connection> OpenAsync(ServerTarget target, CancellationToken cancellationToken)
    {
        var key = target.Address.Key;
        var questions = Questions ?? throw new IOException(Strings.Server_NoQuestions);
        var connection = _open.GetOrAdd(key, _ => new Connection(target, new SecretSource(target.Address, secrets, questions), LogOf(target.Address)));
        await connection.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (connection.Session is { IsConnected: true })
            {
                return connection;
            }

            if (connection.Session is { } dropped)
            {
                connection.Log.Add(Strings.ServerLog_Dropped);
                await QuietlyAsync(dropped.DisposeAsync).ConfigureAwait(false);
                connection.Session = null;
            }

            if (connection.Target.Bookmark is null && target.Bookmark is not null)
            {
                connection.Target = target;
            }

            var proxy = connection.Target.Settings.Proxy;
            var context = new ConnectContext(questions, connection.Secrets, trusted, connection.Log)
            {
                ProxyPassword = proxy.Kind is ProxyKind.Socks5 or ProxyKind.Http && proxy.User.Length > 0 ? secrets.Read(RemoteOptions.ProxyTarget(proxy)) : null,
            };
            try
            {
                connection.Session = await connector.ConnectAsync(connection.Target, context, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (connection.Session is null && !connection.WasOpen)
                {
                    connection.Secrets.Forget();
                    _open.TryRemove(key, out _);
                }

                throw;
            }

            connection.Secrets.SignedIn();
            connection.WasOpen = true;
            Touch(connection);
            if (context.SaidYesToPlain && connection.Target.Bookmark is not null)
            {
                connection.Target = connection.Target with { PlainAllowed = true };
                PlainAllowed?.Invoke(connection.Target);
            }

            ServersChanged?.Invoke();
            return connection;
        }
        finally
        {
            connection.Gate.Release();
        }
    }

    private Task<Connection> ConnectionForAsync(ServerAddress address, CancellationToken cancellationToken)
    {
        if (_open.TryGetValue(address.Key, out var open) && open.Session is { IsConnected: true })
        {
            return Task.FromResult(open);
        }

        var target = open?.Target ?? BookmarkFor?.Invoke(address) ?? new ServerTarget(address, Options: Defaults);
        return OpenAsync(target with { Address = address }, cancellationToken);
    }

    public ServerOptions OptionsFor(ServerAddress address)
        => _open.TryGetValue(address.Key, out var open) ? open.Target.Settings : BookmarkFor?.Invoke(address)?.Settings ?? Defaults;

    public void Reconfigure()
    {
        foreach (var c in _open.Values)
        {
            var address = c.Target.Address;
            c.Target = c.Target.Bookmark is not null && BookmarkFor?.Invoke(address) is { } bookmark
                ? bookmark with { Address = address }
                : c.Target with { Options = c.Target.Bookmark is null ? Defaults : c.Target.Options };
        }
    }

    private void Touch(Connection connection)
    {
        connection.LastUsed = Now();
        connection.KeepAliveStopped = false;
    }

    public void StartKeepAlive() => _keepAlive ??= new Timer(_ => _ = KeepAliveTickAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    public async Task KeepAliveTickAsync()
    {
        var now = Now();
        foreach (var c in _open.Values)
        {
            var o = c.Target.Settings.Connection;
            if (c.Session is not { IsConnected: true } session || !o.KeepAlive)
            {
                continue;
            }

            var idle = now - c.LastUsed;
            if (idle >= TimeSpan.FromMinutes(o.KeepAliveStopMinutes))
            {
                if (!c.KeepAliveStopped)
                {
                    c.KeepAliveStopped = true;
                    c.Log.Add(Loc.Format(Strings.ServerLog_KeepAliveStopped, o.KeepAliveStopMinutes));
                }

                continue;
            }

            var every = TimeSpan.FromSeconds(o.KeepAliveSeconds);
            if (idle < every || now - c.LastPing < every)
            {
                continue;
            }

            c.LastPing = now;
            try
            {
                await session.KeepAliveAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(o.AnswerSeconds)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException or InvalidOperationException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string CacheKey(ServerAddress folder) => folder.Key + folder.Path;

    public void ForgetListing(string path)
    {
        if (ServerAddress.Parse(path) is { } folder)
        {
            _cache.TryRemove(CacheKey(folder), out _);
        }
    }

    private void Changed(ServerAddress item)
    {
        _cache.TryRemove(CacheKey(item), out _);
        if (item.Parent() is { } parent)
        {
            _cache.TryRemove(CacheKey(parent), out _);
        }
    }

    private void Keep(ServerAddress folder, IReadOnlyList<RemoteItem> items)
    {
        if (OptionsFor(folder).Listing.Cache)
        {
            _cache[CacheKey(folder)] = new Listed(Now(), items);
        }
    }

    public async Task DisconnectAsync(ServerAddress server)
    {
        if (!_open.TryRemove(server.Key, out var connection))
        {
            return;
        }

        connection.Secrets.Forget();
        if (connection.Session is { } session)
        {
            connection.Log.Add(Strings.ServerLog_Disconnected);
            await QuietlyAsync(session.DisposeAsync).ConfigureAwait(false);
        }

        foreach (var folder in _rights.Keys.Where(k => ServerAddress.Parse(k)?.SameServer(server) == true))
        {
            _rights.TryRemove(folder, out _);
        }

        foreach (var key in _cache.Keys.Where(k => k.StartsWith(server.Key + "/", StringComparison.OrdinalIgnoreCase)))
        {
            _cache.TryRemove(key, out _);
        }

        ServersChanged?.Invoke();
    }

    public async Task DisconnectAllAsync()
    {
        foreach (var c in _open.Values.ToList())
        {
            if (c.Session is { } s)
            {
                await DisconnectAsync(s.Server).ConfigureAwait(false);
            }
        }
    }

    private static async Task QuietlyAsync(Func<ValueTask> work)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or System.Net.Sockets.SocketException or TimeoutException)
        {
        }
    }

    public async Task<DirectoryListing> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        if (ServerAddress.Parse(path) is not { } address)
        {
            return ServerAddress.IsServerPath(path)
                ? throw new IOException(Strings.Server_BadAddress)
                : await inner.ListAsync(path, cancellationToken).ConfigureAwait(false);
        }

        var sw = Stopwatch.StartNew();
        var listing = OptionsFor(address).Listing;
        IReadOnlyList<RemoteItem> items;
        if (listing.Cache && _cache.TryGetValue(CacheKey(address), out var kept) && Now() - kept.At < TimeSpan.FromMinutes(listing.CacheMinutes)
            && _open.TryGetValue(address.Key, out var open) && open.Session is { IsConnected: true })
        {
            items = kept.Items;
        }
        else
        {
            items = await RunAsync(address, (s, ct) => s.ListAsync(address.Path, ct), cancellationToken, AnswerLimit(address)).ConfigureAwait(false);
            Keep(address, items);
        }

        var shown = address.ToString();
        _rights[shown] = items.Where(i => i.Rights.Length > 0).GroupBy(i => i.Entry.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Rights, StringComparer.Ordinal);
        var entries = OptionsFor(address).Listing.ShowHidden ? items.Select(i => i.Entry) : items.Where(i => !RemoteOptions.IsHidden(i.Entry.Name)).Select(i => i.Entry);
        return new DirectoryListing(shown, [.. entries], sw.Elapsed);
    }

    public async Task<T> RunAsync<T>(ServerAddress address, Func<IRemoteSession, CancellationToken, Task<T>> work, CancellationToken cancellationToken, TimeSpan? limit = null, bool again = true)
    {
        var tries = 0;
        Connection connection;
        try
        {
            connection = await ConnectionForAsync(address, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex) when (ex is not (SignInRefusedException or ServerKeyChangedException) && _open.TryGetValue(address.Key, out var was) && was.WasOpen
                                      && was.Target.Settings.Connection.Retries > 0 && !cancellationToken.IsCancellationRequested)
        {
            connection = await AgainAsync(address, was, ++tries, cancellationToken).ConfigureAwait(false);
        }

        while (true)
        {
            try
            {
                Touch(connection);
                var result = await LimitedAsync(address, connection, work, limit, cancellationToken).ConfigureAwait(false);
                Touch(connection);
                return result;
            }
            catch (Exception ex) when (again && IsDrop(ex, connection) && !cancellationToken.IsCancellationRequested && tries < connection.Target.Settings.Connection.Retries)
            {
                connection = await AgainAsync(address, connection, ++tries, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsDrop(Exception ex, Connection connection)
        => ex is IOException or System.Net.Sockets.SocketException or TimeoutException or ObjectDisposedException or InvalidOperationException
           && ex is not (FileNotFoundException or DirectoryNotFoundException) && connection.Session is { IsConnected: false };

    private async Task<Connection> AgainAsync(ServerAddress address, Connection connection, int tries, CancellationToken cancellationToken)
    {
        var o = connection.Target.Settings.Connection;
        while (true)
        {
            connection.Log.Add(Loc.Format(Strings.ServerLog_TryAgain, tries, o.Retries));
            if (tries > 1)
            {
                await Delay(TimeSpan.FromSeconds(o.RetrySeconds), cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await ConnectionForAsync(address, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex) when (ex is not (SignInRefusedException or ServerKeyChangedException) && tries < o.Retries && !cancellationToken.IsCancellationRequested)
            {
                tries++;
            }
        }
    }

    public TimeSpan? ListTimeout { get; set; }

    public TimeSpan AnswerLimit(ServerAddress address) => ListTimeout ?? TimeSpan.FromSeconds(OptionsFor(address).Connection.AnswerSeconds);

    private async Task<T> LimitedAsync<T>(ServerAddress address, Connection connection, Func<IRemoteSession, CancellationToken, Task<T>> work, TimeSpan? limit, CancellationToken cancellationToken)
    {
        var session = connection.Session!;
        if (limit is not { } time)
        {
            return await work(session, cancellationToken).ConfigureAwait(false);
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            return await work(session, stop.Token).WaitAsync(time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
        {
            await stop.CancelAsync().ConfigureAwait(false);
            var seconds = (int)Math.Round(time.TotalSeconds);
            connection.Log.Add(Loc.Format(Strings.ServerLog_NoAnswer, seconds));
            if (ReferenceEquals(connection.Session, session))
            {
                connection.Session = null;
            }

            _ = Task.Run(() => QuietlyAsync(session.DisposeAsync), CancellationToken.None);
            ServersChanged?.Invoke();
            throw new IOException(Loc.Format(Strings.Server_NoAnswer, address.Host, seconds));
        }
    }

    public async Task<IReadOnlyList<RemoteItem>> ListItemsAsync(ServerAddress folder, CancellationToken cancellationToken)
    {
        var items = await RunAsync(folder, (s, ct) => s.ListAsync(folder.Path, ct), cancellationToken, AnswerLimit(folder)).ConfigureAwait(false);
        Keep(folder, items);
        return items;
    }

    public async Task<RemoteItem?> StatAsync(ServerAddress item, CancellationToken cancellationToken)
    {
        if (item.Parent() is not { } folder)
        {
            return new RemoteItem(new FileEntry("/", 0, 0, FileAttributes.Directory), "");
        }

        try
        {
            var items = await ListItemsAsync(folder, cancellationToken).ConfigureAwait(false);
            return items.FirstOrDefault(i => i.Entry.Name == item.Name);
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async Task DownloadAsync(ServerAddress file, Stream to, long offset, Action<long> progress, CancellationToken cancellationToken)
    {
        using var slot = await SlotAsync(file, cancellationToken).ConfigureAwait(false);
        var settings = OptionsFor(file);
        var resumable = !RemoteOptions.IsAscii(settings.Transfers, file.Name) || file.Kind == ServerKind.Sftp;
        long done = 0;
        for (var tries = 0; ; tries++)
        {
            var from = offset + done;
            long now = 0;
            try
            {
                await WriteAsync(file, from > 0 ? Loc.Format(Strings.ServerLog_DownloadRest, file.Path, from) : Loc.Format(Strings.ServerLog_Download, file.Path),
                    (s, ct) => s.DownloadAsync(file.Path, to, from, p => progress(done + (now = p)), ct), cancellationToken, transfer: true).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (resumable && TransferDropped(ex, file) && tries < settings.Connection.Retries && !cancellationToken.IsCancellationRequested)
            {
                done += now;
                await TransferAgainAsync(file, settings, tries + 1, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task UploadAsync(ServerAddress file, Stream from, long offset, Action<long> progress, CancellationToken cancellationToken)
    {
        using (await SlotAsync(file, cancellationToken).ConfigureAwait(false))
        {
            var settings = OptionsFor(file);
            var resumable = from.CanSeek && (!RemoteOptions.IsAscii(settings.Transfers, file.Name) || file.Kind == ServerKind.Sftp);
            var start = from.CanSeek ? from.Position : 0;
            var at = offset;
            try
            {
                for (var tries = 0; ; tries++)
                {
                    var sent = at - offset;
                    try
                    {
                        await WriteAsync(file, at > 0 ? Loc.Format(Strings.ServerLog_UploadRest, file.Path, at) : Loc.Format(Strings.ServerLog_Upload, file.Path),
                            (s, ct) => s.UploadAsync(file.Path, from, at, p => progress(sent + p), ct), cancellationToken, transfer: true).ConfigureAwait(false);
                        break;
                    }
                    catch (Exception ex) when (resumable && TransferDropped(ex, file) && tries < settings.Connection.Retries && !cancellationToken.IsCancellationRequested)
                    {
                        await TransferAgainAsync(file, settings, tries + 1, cancellationToken).ConfigureAwait(false);
                        var there = await StatAsync(file, cancellationToken).ConfigureAwait(false);
                        at = Math.Clamp(there?.Entry.Size ?? offset, offset, offset + (from.Length - start));
                        from.Position = start + (at - offset);
                    }
                }
            }
            finally
            {
                Changed(file);
            }

            if (settings.Transfers.UploadMode is { } mode)
            {
                try
                {
                    await SetPermissionsAsync(file, mode, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                }
            }
        }
    }

    private bool TransferDropped(Exception ex, ServerAddress file)
        => ex is not (FileNotFoundException or DirectoryNotFoundException or SignInRefusedException or ServerKeyChangedException)
           && (ex is System.Net.Sockets.SocketException or TimeoutException || ex.InnerException is System.Net.Sockets.SocketException or TimeoutException
               || (ex is IOException or ObjectDisposedException && _open.TryGetValue(file.Key, out var c) && c.Session is { IsConnected: false }));

    private async Task TransferAgainAsync(ServerAddress file, ServerOptions settings, int tries, CancellationToken cancellationToken)
    {
        LogOf(file).Add(Loc.Format(Strings.ServerLog_TryAgain, tries, settings.Connection.Retries));
        if (tries > 1)
        {
            await Delay(TimeSpan.FromSeconds(settings.Connection.RetrySeconds), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IDisposable> SlotAsync(ServerAddress server, CancellationToken cancellationToken)
    {
        var slot = _slots.GetOrAdd(server.Key, _ => new SemaphoreSlim(OptionsFor(server).Connection.Connections));
        await slot.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Slot(slot);
    }

    private sealed class Slot(SemaphoreSlim slot) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                slot.Release();
            }
        }
    }

    public Task CreateFolderAsync(ServerAddress folder, CancellationToken cancellationToken)
        => WriteAsync(folder, Loc.Format(Strings.ServerLog_NewFolder, folder.Path), (s, ct) => s.CreateFolderAsync(folder.Path, ct), cancellationToken);

    public Task DeleteAsync(ServerAddress item, bool isFolder, CancellationToken cancellationToken)
        => WriteAsync(item, Loc.Format(Strings.ServerLog_Delete, item.Path),
            (s, ct) => isFolder ? s.DeleteFolderAsync(item.Path, ct) : s.DeleteFileAsync(item.Path, ct), cancellationToken);

    public async Task RenameAsync(ServerAddress from, ServerAddress to, CancellationToken cancellationToken)
    {
        await WriteAsync(from, Loc.Format(Strings.ServerLog_Rename, from.Path, to.Path), (s, ct) => s.RenameAsync(from.Path, to.Path, ct), cancellationToken).ConfigureAwait(false);
        Changed(to);
    }

    public Task SetPermissionsAsync(ServerAddress item, int mode, CancellationToken cancellationToken)
        => WriteAsync(item, Loc.Format(Strings.ServerLog_Permissions, item.Path, Convert.ToString(mode & 0xFFF, 8), RightsText(mode)),
            (s, ct) => s.SetPermissionsAsync(item.Path, mode, ct), cancellationToken);

    public async Task<bool> SetTimeAsync(ServerAddress item, DateTime utc, CancellationToken cancellationToken)
    {
        try
        {
            return await RunAsync(item, (s, ct) => s.SetTimeAsync(item.Path, utc, ct), cancellationToken, AnswerLimit(item)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            Changed(item);
        }
    }

    private async Task WriteAsync(ServerAddress address, string line, Func<IRemoteSession, CancellationToken, Task> work, CancellationToken cancellationToken, bool transfer = false)
    {
        var log = LogOf(address);
        log.Add(line);
        try
        {
            await RunAsync(address, async (s, ct) =>
            {
                await work(s, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken, transfer ? null : AnswerLimit(address), again: !transfer).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Add(Loc.Format(Strings.ServerLog_Failed, ex.Message));
            throw;
        }
        finally
        {
            if (!transfer)
            {
                Changed(address);
            }
        }
    }

    public static string RightsText(int mode)
    {
        Span<char> text = stackalloc char[9];
        for (var i = 0; i < 9; i++)
        {
            text[i] = (mode & (1 << (8 - i))) != 0 ? "rwx"[i % 3] : '-';
        }

        return new string(text);
    }

    public static int? ModeOf(string rights)
    {
        if (rights.Length != 9)
        {
            return null;
        }

        var mode = 0;
        for (var i = 0; i < 9; i++)
        {
            var c = rights[i];
            var slot = i % 3;
            var on = slot == 0 ? c == 'r' : slot == 1 ? c == 'w' : c is 'x' or 's' or 't';
            var off = c == '-' || (slot == 2 && c is 'S' or 'T');
            if (!on && !off)
            {
                return null;
            }

            mode |= on ? 1 << (8 - i) : 0;
        }

        return mode;
    }

    public string? GetParent(string path)
        => ServerAddress.Parse(path) is { } address ? address.Parent()?.ToString() : inner.GetParent(path);

    public string? GetRoot(string path)
        => ServerAddress.Parse(path) is { } address ? address.WithPath("/").ToString() : inner.GetRoot(path);

    public string GetName(string path)
        => ServerAddress.Parse(path) is { } address ? address.Name : inner.GetName(path);

    public string Combine(string directory, string name)
        => ServerAddress.Parse(directory) is { } address ? address.Child(name).ToString() : inner.Combine(directory, name);

    public IDisposable Watch(string path, Action<DirectoryChange> onChange, Action<Exception> onError)
        => ServerAddress.IsServerPath(path) ? new Nothing() : inner.Watch(path, onChange, onError);

    public IDisposable? WatchTree(string path, Action<IReadOnlyCollection<string>?> onChange, Action<Exception> onError)
        => ServerAddress.IsServerPath(path) ? null : inner.WatchTree(path, onChange, onError);

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
