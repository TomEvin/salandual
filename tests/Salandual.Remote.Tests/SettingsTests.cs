// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Remote.Tests/SettingsTests.cs

using System.Buffers.Binary;
using System.Text;
using Renci.SshNet;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Remote.Tests;

public sealed class SettingsTests
{
    private readonly MemoryServers _servers = new();
    private readonly MemorySecretStore _secrets = new();
    private readonly Answers _answers = new();
    private readonly MemoryServer _files;
    private readonly RemoteFileSystem _remote;
    private DateTime _now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private readonly List<TimeSpan> _waited = [];

    public SettingsTests()
    {
        _files = _servers.Add("files.example.org");
        _files.AddFile("/var/www/index.html", "<h1>Hello</h1>");
        _files.AddFile("/var/www/.htaccess", "Deny from all");
        _files.AddFolder("/var/www/logs");
        _remote = new RemoteFileSystem(new LocalFileSystemProvider(), _servers, _secrets, TrustedServers.InMemory())
        {
            Questions = _answers,
            Now = () => _now,
            Delay = (t, _) =>
            {
                _waited.Add(t);
                return Task.CompletedTask;
            },
        };
        _answers.Secrets.Enqueue(new SecretAnswer("secret", false));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServerOptions With(Func<ConnectionOptions, ConnectionOptions>? connection = null, Func<TransferSettingsForServer, TransferSettingsForServer>? transfers = null, Func<ListingOptions, ListingOptions>? listing = null, ProxyOptions? proxy = null)
        => new()
        {
            Connection = (connection ?? (c => c))(new ConnectionOptions()),
            Transfers = (transfers ?? (t => t))(new TransferSettingsForServer()),
            Listing = (listing ?? (l => l))(new ListingOptions()),
            Proxy = proxy ?? new ProxyOptions(),
        };

    private async Task<ServerAddress> ConnectAsync(ServerOptions? options = null)
        => ServerAddress.Parse(await _remote.ConnectAsync(new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!, Options: options), Ct))!;

    private IReadOnlyList<string> Log(ServerAddress server) => [.. _remote.LogOf(server).Lines.Select(l => l.Text)];

    [Fact]
    public async Task Cache_KeepsAListingForItsMinutes_CtrlRAndAChangeListAgain()
    {
        var www = await ConnectAsync();
        await _remote.ListAsync(www.ToString(), Ct);
        await _remote.ListAsync(www.ToString(), Ct);
        Assert.Equal(1, _files.Listings);

        _remote.ForgetListing(www.ToString());
        await _remote.ListAsync(www.ToString(), Ct);
        Assert.Equal(2, _files.Listings);

        await _remote.CreateFolderAsync(www.Child("new"), Ct);
        var listed = await _remote.ListAsync(www.ToString(), Ct);
        Assert.Equal(3, _files.Listings);
        Assert.Contains(listed.Entries, e => e.Name == "new");

        _now += TimeSpan.FromMinutes(11);
        await _remote.ListAsync(www.ToString(), Ct);
        Assert.Equal(4, _files.Listings);
    }

    [Fact]
    public async Task Cache_Off_ListsEachTime()
    {
        var www = await ConnectAsync(With(listing: l => l with { Cache = false }));
        await _remote.ListAsync(www.ToString(), Ct);
        await _remote.ListAsync(www.ToString(), Ct);
        Assert.Equal(2, _files.Listings);
    }

    [Fact]
    public async Task HiddenFiles_Off_LeavesDotNamesOutOfThePanel_ButNotOutOfCopies()
    {
        var www = await ConnectAsync(With(listing: l => l with { ShowHidden = false }));
        var listed = await _remote.ListAsync(www.ToString(), Ct);
        Assert.DoesNotContain(listed.Entries, e => e.Name == ".htaccess");
        Assert.Contains(listed.Entries, e => e.Name == "index.html");
        var items = await _remote.ListItemsAsync(www, Ct);
        Assert.Contains(items, i => i.Entry.Name == ".htaccess");
    }

    [Fact]
    public async Task ADroppedConnection_IsOpenedAgain_AtOnce()
    {
        var www = await ConnectAsync();
        _servers.DropAll();
        _remote.ForgetListing(www.ToString());
        await _remote.ListAsync(www.ToString(), Ct);
        Assert.Equal(2, _files.Connects);
        Assert.Empty(_waited);
        Assert.Contains(Log(www), l => l.Contains("The connection had closed: connecting again", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AServerThatStaysAway_IsTriedAsOftenAsTheSettingsSay_TheirSecondsApart()
    {
        var www = await ConnectAsync(With(c => c with { Retries = 3, RetrySeconds = 7 }));
        _servers.DropAll();
        _files.Refuse = "The server is down for maintenance.";
        _remote.ForgetListing(www.ToString());
        await Assert.ThrowsAnyAsync<IOException>(() => _remote.ListAsync(www.ToString(), Ct));
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7)], _waited);
        Assert.Contains(Log(www), l => l.Contains("(3 of 3)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoTriesAgain_AServerThatStaysAway_IsSaidAtOnce()
    {
        var www = await ConnectAsync(With(c => c with { Retries = 0 }));
        _servers.DropAll();
        _files.Refuse = "The server is down for maintenance.";
        _remote.ForgetListing(www.ToString());
        await Assert.ThrowsAnyAsync<IOException>(() => _remote.ListAsync(www.ToString(), Ct));
        Assert.Equal(1, _files.Connects);
        Assert.Empty(_waited);
        Assert.DoesNotContain(Log(www), l => l.Contains("trying again", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepAlive_WhileIdle_EveryItsSeconds_StoppingAfterItsMinutes()
    {
        var www = await ConnectAsync(With(c => c with { KeepAliveSeconds = 60, KeepAliveStopMinutes = 30 }));
        _now += TimeSpan.FromSeconds(30);
        await _remote.KeepAliveTickAsync();
        Assert.Equal(0, _files.KeepAlives);

        _now += TimeSpan.FromSeconds(31);
        await _remote.KeepAliveTickAsync();
        await _remote.KeepAliveTickAsync();
        Assert.Equal(1, _files.KeepAlives);

        _now += TimeSpan.FromSeconds(60);
        await _remote.KeepAliveTickAsync();
        Assert.Equal(2, _files.KeepAlives);

        _now += TimeSpan.FromMinutes(30);
        await _remote.KeepAliveTickAsync();
        Assert.Equal(2, _files.KeepAlives);
        Assert.Contains(Log(www), l => l.Contains("Keep alive stopped after 30 minutes", StringComparison.Ordinal));

        await _remote.ListAsync(www.ToString(), Ct);
        _now += TimeSpan.FromSeconds(61);
        await _remote.KeepAliveTickAsync();
        Assert.Equal(3, _files.KeepAlives);
    }

    [Fact]
    public async Task KeepAlive_Off_SendsNothing()
    {
        await ConnectAsync(With(c => c with { KeepAlive = false }));
        _now += TimeSpan.FromMinutes(5);
        await _remote.KeepAliveTickAsync();
        Assert.Equal(0, _files.KeepAlives);
    }

    [Fact]
    public async Task CopiesAtOnce_TheOthersWaitTheirTurn()
    {
        var www = await ConnectAsync(With(c => c with { Connections = 2 }));
        for (var i = 0; i < 4; i++)
        {
            _files.AddFile($"/var/www/f{i}.txt", new string('x', 100));
        }

        var hold = new TaskCompletionSource();
        _files.DownloadHold = hold.Task;
        var copies = Enumerable.Range(0, 4).Select(i => Task.Run(() => _remote.DownloadAsync(www.Child($"f{i}.txt"), new MemoryStream(), 0, _ => { }, Ct), Ct)).ToList();
        await Task.Delay(150, Ct);
        Assert.Equal(2, _files.MostAtOnce);
        hold.SetResult();
        await Task.WhenAll(copies);
        Assert.Equal(2, _files.MostAtOnce);
    }

    [Fact]
    public async Task ADownloadThatDrops_GoesOnFromWhereItStopped()
    {
        var www = await ConnectAsync();
        var content = string.Concat(Enumerable.Range(0, 1000).Select(i => (char)('a' + (i % 26))));
        _files.AddFile("/var/www/big.txt", content);
        _files.DropHalfway = 1;
        using var to = new MemoryStream();
        long reported = 0;
        await _remote.DownloadAsync(www.Child("big.txt"), to, 0, p => reported = p, Ct);
        Assert.Equal(content, Encoding.UTF8.GetString(to.ToArray()));
        Assert.Equal(1000, reported);
        Assert.Contains(Log(www), l => l.Contains("Copying the rest of /var/www/big.txt from the server, from byte 500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUploadThatDrops_GoesOnFromWhatTheServerHas()
    {
        var www = await ConnectAsync();
        var content = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 800).Select(i => (char)('A' + (i % 26)))));
        _files.DropHalfway = 1;
        using var from = new MemoryStream(content);
        await _remote.UploadAsync(www.Child("up.txt"), from, 0, _ => { }, Ct);
        Assert.Equal(content, _files.Files["/var/www/up.txt"]);
        Assert.Contains(Log(www), l => l.Contains("Copying the rest of /var/www/up.txt to the server, from byte 400", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PermissionsOnUpload_AreSetAfterIt()
    {
        var www = await ConnectAsync(With(transfers: t => t with { UploadPermissions = "600" }));
        using var from = new MemoryStream("secret notes"u8.ToArray());
        await _remote.UploadAsync(www.Child("notes.txt"), from, 0, _ => { }, Ct);
        Assert.Equal("rw-------", _files.Rights["/var/www/notes.txt"]);
    }

    [Fact]
    public async Task ATypedAddress_UsesTheDefaults_ABookmark_ItsOwn()
    {
        _remote.Defaults = With(c => c with { AnswerSeconds = 45 });
        var typed = ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!;
        await _remote.ListAsync(typed.ToString(), Ct);
        Assert.Equal(45, _files.LastTarget!.Settings.Connection.AnswerSeconds);
        Assert.Equal(TimeSpan.FromSeconds(45), _remote.AnswerLimit(typed));

        var other = _servers.Add("photos.example.net");
        other.AddFolder("/photos");
        var own = With(c => c with { AnswerSeconds = 90 });
        _remote.BookmarkFor = a => a.Host == "photos.example.net" ? new ServerTarget(a, Bookmark: "Photos", Options: own) : null;
        _answers.Secrets.Enqueue(new SecretAnswer("secret", false));
        await _remote.ListAsync("sftp://deploy@photos.example.net/photos", Ct);
        Assert.Equal(90, other.LastTarget!.Settings.Connection.AnswerSeconds);
    }

    [Fact]
    public async Task Reconfigure_GivesAnOpenConnectionTheNewDefaults()
    {
        var www = await ConnectAsync();
        await _remote.ListAsync(www.ToString(), Ct);
        _remote.Defaults = With(listing: l => l with { ShowHidden = false });
        _remote.Reconfigure();
        var listed = await _remote.ListAsync(www.ToString(), Ct);
        Assert.DoesNotContain(listed.Entries, e => e.Name == ".htaccess");
    }

    [Fact]
    public void Ascii_ForAll_OrForTheNamesListed()
    {
        var automatic = new TransferSettingsForServer { Mode = TransferMode.Automatic };
        Assert.True(RemoteOptions.IsAscii(automatic, "index.HTML"));
        Assert.True(RemoteOptions.IsAscii(automatic, "data.csv"));
        Assert.False(RemoteOptions.IsAscii(automatic, "photo.jpg"));
        Assert.True(RemoteOptions.IsAscii(new TransferSettingsForServer { Mode = TransferMode.Ascii }, "photo.jpg"));
        Assert.False(RemoteOptions.IsAscii(new TransferSettingsForServer(), "index.html"));
    }

    [Fact]
    public void Normalize_KeepsNumbersInRange_AndAPermissionNumberValid()
    {
        var c = new ConnectionOptions { KeepAliveSeconds = 1, Retries = 99, Connections = 0 }.Normalize();
        Assert.Equal(10, c.KeepAliveSeconds);
        Assert.Equal(10, c.Retries);
        Assert.Equal(1, c.Connections);
        Assert.Equal(0, new ProxyOptions { Port = 70000 }.Normalize().Port);
        Assert.Equal("", new TransferSettingsForServer { UploadPermissions = "999" }.Normalize().UploadPermissions);
        Assert.Equal(0b110_100_100, new TransferSettingsForServer { UploadPermissions = "644" }.Normalize().UploadMode);
        Assert.Equal(-12 * 60, new ListingOptions { TimeZoneMinutes = -999 }.Normalize().TimeZoneMinutes);
    }

    [Fact]
    public void ABookmark_UsesTheDefaults_ForEachTabItHasNoneFor()
    {
        var defaults = With(c => c with { Retries = 5 }, listing: l => l with { CacheMinutes = 3 });
        var b = new ServerBookmark { Host = "h", Transfers = new TransferSettingsForServer { Mode = TransferMode.Ascii } };
        var o = ServerOptions.For(b, defaults);
        Assert.Equal(5, o.Connection.Retries);
        Assert.Equal(TransferMode.Ascii, o.Transfers.Mode);
        Assert.Equal(3, o.Listing.CacheMinutes);
    }

    [Fact]
    public void Ssh_Compression_OnlyWhenTicked_ThenFirst()
    {
        var context = new ConnectContext(_answers, new SecretSource(ServerAddress.Parse("sftp://u@h")!, _secrets, _answers), TrustedServers.InMemory(), new ConnectionLog());
        AuthenticationMethod[] methods = [new PasswordAuthenticationMethod("u", "p")];
        var off = SftpSession.Info(new ServerTarget(ServerAddress.Parse("sftp://u@h")!), context, methods);
        Assert.DoesNotContain("zlib@openssh.com", off.CompressionAlgorithms.Keys);
        var on = SftpSession.Info(new ServerTarget(ServerAddress.Parse("sftp://u@h")!, Options: With(transfers: t => t with { Compress = true })), context, methods);
        Assert.Equal("zlib@openssh.com", on.CompressionAlgorithms.Keys.First());
    }

    [Fact]
    public void Ssh_ThroughASocksProxy_WithItsUser()
    {
        var context = new ConnectContext(_answers, new SecretSource(ServerAddress.Parse("sftp://u@h")!, _secrets, _answers), TrustedServers.InMemory(), new ConnectionLog()) { ProxyPassword = "proxy-secret" };
        var options = With(proxy: new ProxyOptions { Kind = ProxyKind.Socks5, Host = "proxy.example.com", Port = 1080, User = "me" });
        var info = SftpSession.Info(new ServerTarget(ServerAddress.Parse("sftp://u@h")!, Options: options), context, [new PasswordAuthenticationMethod("u", "p")]);
        Assert.Equal(ProxyTypes.Socks5, info.ProxyType);
        Assert.Equal("proxy.example.com", info.ProxyHost);
        Assert.Equal(1080, info.ProxyPort);
        Assert.Equal("me", info.ProxyUsername);
        Assert.Contains(context.Log.Lines, l => l.Text.Contains("proxy.example.com:1080", StringComparison.Ordinal));
        Assert.DoesNotContain(context.Log.Lines, l => l.Text.Contains("proxy-secret", StringComparison.Ordinal));
    }

    [Fact]
    public void AProxyPassword_IsKeptUnderItsOwnName()
        => Assert.Equal("Salandual:proxy://me@proxy.example.com:1080", RemoteOptions.ProxyTarget(new ProxyOptions { Kind = ProxyKind.Socks5, Host = "proxy.example.com", User = "me" }));

    [Fact]
    public void AProxyWithoutAServerName_IsNotUsed()
        => Assert.Null(RemoteOptions.Proxy(new ProxyOptions { Kind = ProxyKind.Http, Host = "not a host" }, ServerAddress.Parse("sftp://u@h")!, null));

    private sealed class FakeAgent : IAgentChannel
    {
        public List<byte[]> Asked { get; } = [];

        public static byte[] Blob(string algorithm)
        {
            var name = Encoding.ASCII.GetBytes(algorithm);
            var blob = new byte[4 + name.Length + 4 + 8];
            BinaryPrimitives.WriteUInt32BigEndian(blob, (uint)name.Length);
            name.CopyTo(blob, 4);
            BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(4 + name.Length), 8);
            return blob;
        }

        private static byte[] Strings(byte first, params byte[][] parts)
        {
            var bytes = new List<byte> { first };
            foreach (var p in parts)
            {
                var length = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(length, (uint)p.Length);
                bytes.AddRange(length);
                bytes.AddRange(p);
            }

            return [.. bytes];
        }

        public byte[] Request(byte[] message)
        {
            Asked.Add(message);
            if (message[0] == 11)
            {
                var list = Strings(12, Blob("ssh-ed25519"), "laptop"u8.ToArray(), Blob("ssh-rsa"), "old key"u8.ToArray());
                var count = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(count, 2);
                return [list[0], .. count, .. list[1..]];
            }

            return Strings(14, Strings(0, "ssh-ed25519"u8.ToArray(), [1, 2, 3])[1..]);
        }
    }

    [Fact]
    public void Agent_ListsItsKeys_AnRsaKeyUnderTheSha2Names()
    {
        var agent = new SshAgent(new FakeAgent());
        var keys = agent.Keys();
        Assert.Equal(["ssh-ed25519", "ssh-rsa"], keys.Select(k => k.Algorithm));
        Assert.Equal(["laptop", "old key"], keys.Select(k => k.Comment));
        var names = agent.KeySource().HostKeyAlgorithms.Select(a => a.Name).ToList();
        Assert.Equal(["ssh-ed25519", "rsa-sha2-512", "rsa-sha2-256", "ssh-rsa"], names);
    }

    [Fact]
    public void Agent_SignsWithTheKey_AskingRsaForSha256()
    {
        var channel = new FakeAgent();
        var agent = new SshAgent(channel);
        var rsa = agent.KeySource().HostKeyAlgorithms.First(a => a.Name == "rsa-sha2-256");
        Assert.Equal(FakeAgent.Blob("ssh-rsa"), rsa.Data);
        var signature = rsa.Sign([9, 9, 9]);
        Assert.Equal(4 + 11 + 4 + 3, signature.Length);
        var request = channel.Asked[^1];
        Assert.Equal(13, request[0]);
        Assert.Equal(SshAgent.RsaSha256, BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(request.Length - 4)));
    }

    [Fact]
    public void Agent_AWrongAnswer_IsRefused()
    {
        var agent = new SshAgent(new Broken());
        Assert.Throws<IOException>(() => agent.Keys());
    }

    private sealed class Broken : IAgentChannel
    {
        public byte[] Request(byte[] message) => [12, 0, 0, 0, 5, 0, 0, 0, 200];
    }
}
