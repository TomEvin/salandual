// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Remote.Tests/RemoteTests.cs

using System.Security.Cryptography;
using System.Text;
using Salandual.Core.Operations;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Remote.Tests;

internal sealed class Answers : IServerQuestions
{
    public Queue<SecretAnswer?> Secrets { get; } = new();

    public TrustAnswer Trust { get; set; } = TrustAnswer.Remember;

    public bool Plain { get; set; } = true;

    public List<string> Asked { get; } = [];

    public Task<SecretAnswer?> AskSecretAsync(ServerAddress server, SecretKind kind, bool wrongBefore, bool keptBefore, CancellationToken cancellationToken)
    {
        Asked.Add($"{kind}{(wrongBefore ? " wrong" : "")}{(keptBefore ? " kept" : "")}");
        return Task.FromResult(Secrets.Count > 0 ? Secrets.Dequeue() : null);
    }

    public Task<TrustAnswer> AskTrustAsync(TrustQuestion question, CancellationToken cancellationToken)
    {
        Asked.Add("trust " + question.Algorithm + " " + question.Fingerprint);
        return Task.FromResult(Trust);
    }

    public Task<bool> AskPlainAsync(ServerAddress server, CancellationToken cancellationToken)
    {
        Asked.Add("plain " + server.Host);
        return Task.FromResult(Plain);
    }
}

public sealed class RemoteTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-remote-").FullName;
    private readonly MemoryServers _servers = new();
    private readonly MemorySecretStore _secrets = new();
    private readonly Answers _answers = new();
    private readonly MemoryServer _files;

    public RemoteTests()
    {
        _files = _servers.Add("files.example.org");
        _files.AddFile("/var/www/index.html", "<h1>Hello</h1>");
        _files.AddFile("/var/www/styles.css", "body {}");
        _files.AddFile("/var/www/assets/logo.svg", "<svg/>", "rw-rw-r--");
        _files.AddFolder("/var/www/logs");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RemoteFileSystem Remote(TrustedServers? trusted = null)
        => new(new LocalFileSystemProvider(), _servers, _secrets, trusted ?? TrustedServers.InMemory()) { Questions = _answers };

    [Theory]
    [InlineData("sftp://deploy@files.example.org/var/www", ServerKind.Sftp, "files.example.org", 22, "deploy", "/var/www")]
    [InlineData("sftp://deploy@files.example.org:2222", ServerKind.Sftp, "files.example.org", 2222, "deploy", "/")]
    [InlineData("ftp://mirror.example.net/pub//linux/", ServerKind.Ftp, "mirror.example.net", 21, "", "/pub/linux")]
    [InlineData("FTPS://photos@photos.example.net/a/../b", ServerKind.Ftps, "photos.example.net", 990, "photos", "/b")]
    [InlineData("sftp://me@[2001:db8::1]:2200/x", ServerKind.Sftp, "2001:db8::1", 2200, "me", "/x")]
    public void Addresses_AreRead(string text, ServerKind kind, string host, int port, string user, string path)
    {
        var a = ServerAddress.Parse(text)!;
        Assert.Equal((kind, host, port, user, path), (a.Kind, a.Host, a.Port, a.User, a.Path));
    }

    [Theory]
    [InlineData("ftp://user:secret@host/x")]
    [InlineData("sftp://host name/x")]
    [InlineData("sftp://host:99999/")]
    [InlineData("http://host/")]
    [InlineData(@"C:\Users")]
    [InlineData("sftp://")]
    public void NotAddresses_AreRefused(string text) => Assert.Null(ServerAddress.Parse(text));

    [Fact]
    public void AnAddress_GoesUpAndDown_AndIsWrittenBack()
    {
        var a = ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!;
        Assert.Equal("sftp://deploy@files.example.org/var/www", a.ToString());
        Assert.Equal("sftp://deploy@files.example.org/var", a.Parent()!.ToString());
        Assert.Equal("sftp://deploy@files.example.org/", a.Parent()!.Parent()!.ToString());
        Assert.Null(a.WithPath("/").Parent());
        Assert.Equal("www", a.Name);
        Assert.Equal("files.example.org", a.WithPath("/").Name);
        Assert.Equal("sftp://deploy@files.example.org/var/www/logs", a.Child("logs").ToString());
        Assert.Equal("sftp://deploy@files.example.org:22", a.Key);
        Assert.Equal("sftp://deploy@files.example.org:2222/", ServerAddress.Parse("sftp://deploy@files.example.org:2222")!.ToString());
        Assert.True(ServerAddress.HasPassword("ftp://u:p@host/"));
        Assert.False(ServerAddress.HasPassword("ftp://u@host/a:b"));
    }

    [Fact]
    public void ATrustedKey_IsKnown_AnotherOneIsChanged_AndTheListIsKept()
    {
        var file = Path.Join(_dir, TrustedServers.FileName);
        var trusted = new TrustedServers(file, () => null);
        var key = MemoryServers.FakeKey(1);
        Assert.Equal(KeyTrust.Unknown, trusted.CheckSsh("files.example.org", 22, key));
        trusted.Remember("files.example.org", 22, "ssh", TrustedServers.SshAlgorithm(key), TrustedServers.SshFingerprint(key));
        Assert.Equal(KeyTrust.Trusted, trusted.CheckSsh("files.example.org", 22, key));
        Assert.Equal(KeyTrust.Changed, trusted.CheckSsh("files.example.org", 22, MemoryServers.FakeKey(2)));
        Assert.Equal(KeyTrust.Unknown, trusted.CheckSsh("files.example.org", 2222, key));

        var again = new TrustedServers(file, () => null);
        Assert.Equal(KeyTrust.Trusted, again.CheckSsh("files.example.org", 22, key));
        Assert.Equal("ED25519", again.Keys.Single().Algorithm);
        Assert.Equal(2, File.ReadAllLines(file).Length);
    }

    [Fact]
    public void KnownHosts_PlainHashedAndRevoked_AreRead_NeverWritten()
    {
        var key = MemoryServers.FakeKey(7);
        var other = MemoryServers.FakeKey(8);
        var salt = RandomNumberGenerator.GetBytes(20);
        var hashed = "|1|" + Convert.ToBase64String(salt) + "|" + Convert.ToBase64String(HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes("[hidden.example.org]:2222")));
        var lines = new[]
        {
            "# a comment",
            "files.example.org,192.0.2.10 ssh-ed25519 " + Convert.ToBase64String(key),
            hashed + " ssh-ed25519 " + Convert.ToBase64String(key),
            "@revoked bad.example.org ssh-ed25519 " + Convert.ToBase64String(key),
            "*.example.org ssh-ed25519 " + Convert.ToBase64String(key),
            "moved.example.org ssh-ed25519 " + Convert.ToBase64String(other),
        };
        var path = Path.Join(_dir, "known_hosts");
        File.WriteAllLines(path, lines);
        var before = File.GetLastWriteTimeUtc(path);
        var trusted = new TrustedServers(null, () => path);

        Assert.Equal(KeyTrust.Trusted, trusted.CheckSsh("files.example.org", 22, key));
        Assert.Equal(KeyTrust.Trusted, trusted.CheckSsh("192.0.2.10", 22, key));
        Assert.Equal(KeyTrust.Trusted, trusted.CheckSsh("hidden.example.org", 2222, key));
        Assert.Equal(KeyTrust.Changed, trusted.CheckSsh("bad.example.org", 22, key));
        Assert.Equal(KeyTrust.Unknown, trusted.CheckSsh("www.example.org", 22, key));
        Assert.Equal(KeyTrust.Changed, trusted.CheckSsh("moved.example.org", 22, key));
        trusted.ReadKnownHosts = false;
        Assert.Equal(KeyTrust.Unknown, trusted.CheckSsh("files.example.org", 22, key));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        Assert.Equal(lines, File.ReadAllLines(path));
    }

    [Fact]
    public async Task Connecting_AsksTheKeyThenThePassword_RemembersWhatYouTicked_AndListsWithRights()
    {
        var remote = Remote();
        _answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: true));
        var path = await remote.ConnectAsync(new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!), Ct);
        Assert.Equal("sftp://deploy@files.example.org/var/www", path);
        Assert.Equal(["trust ED25519 SHA256:" + TrustedServers.SshFingerprint(_files.Key), "Password"], _answers.Asked);
        Assert.Equal("secret", _secrets.Read("Salandual:sftp://deploy@files.example.org:22"));

        var listing = await remote.ListAsync(path, Ct);
        Assert.Equal(["assets", "index.html", "logs", "styles.css"], listing.Entries.Select(e => e.Name).Order());
        Assert.True(listing.Entries.Single(e => e.Name == "assets").IsDirectory);
        Assert.Equal("rw-r--r--", remote.RightsOf(path, "index.html"));
        Assert.Equal("SFTP", remote.ServerOf(path)!.Security);
        Assert.Equal("sftp://deploy@files.example.org/var", remote.GetParent(path));
        Assert.Equal("sftp://deploy@files.example.org/var/www/logs", remote.Combine(path, "logs"));
        Assert.Contains(remote.LogOf(remote.ServerOf(path)!.Server).Lines, l => l.Text.Contains("Signed in as deploy", StringComparison.Ordinal));
        Assert.DoesNotContain(remote.LogOf(remote.ServerOf(path)!.Server).Lines, l => l.Text.Contains("secret", StringComparison.Ordinal));

        await remote.DisconnectAsync(ServerAddress.Parse(path)!);
        Assert.Empty(remote.OpenServers);
        _answers.Asked.Clear();
        Assert.Equal(4, (await remote.ListAsync(path, Ct)).Entries.Length);
        Assert.Empty(_answers.Asked);
    }

    [Fact]
    public async Task AWrongPassword_IsAskedAgain_AndAKeptOneThatStoppedWorking_IsReplaced()
    {
        _secrets.Write("Salandual:sftp://deploy@files.example.org:22", "deploy", "old");
        var remote = Remote();
        _answers.Secrets.Enqueue(new SecretAnswer("typo", Remember: true));
        _answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: false));
        await remote.ConnectAsync(new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/")!), Ct);
        Assert.Equal(["Password wrong kept", "Password wrong kept"], _answers.Asked.Where(a => a.StartsWith("Password", StringComparison.Ordinal)));
        Assert.Null(_secrets.Read("Salandual:sftp://deploy@files.example.org:22"));
    }

    [Fact]
    public async Task Cancelling_KeepsNothing_AndAChangedKey_IsRefusedWithoutAsking()
    {
        var trusted = TrustedServers.InMemory();
        var remote = Remote(trusted);
        _answers.Trust = TrustAnswer.Cancel;
        await Assert.ThrowsAsync<ConnectCancelledException>(() => remote.ConnectAsync(new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/")!), Ct));
        Assert.Empty(trusted.Keys);
        Assert.Empty(remote.OpenServers);

        trusted.Remember("files.example.org", 22, "ssh", "ED25519", TrustedServers.SshFingerprint(MemoryServers.FakeKey(9)));
        _answers.Asked.Clear();
        _answers.Trust = TrustAnswer.Remember;
        await Assert.ThrowsAsync<ServerKeyChangedException>(() => remote.ConnectAsync(new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/")!), Ct));
        Assert.Empty(_answers.Asked);
    }

    [Fact]
    public async Task PlainFtp_IsAskedOnce_AndABookmarkRemembersYourYes()
    {
        var old = _servers.Add("old.example.net");
        old.NoTls = true;
        old.Password = null;
        old.AddFile("/pub/readme.txt", "hi");
        var remote = Remote();
        ServerTarget? kept = null;
        remote.PlainAllowed += t => kept = t;
        var path = await remote.ConnectAsync(new ServerTarget(ServerAddress.Parse("ftp://old.example.net/pub")!, Bookmark: "Old mirror"), Ct);
        Assert.Equal(["plain old.example.net"], _answers.Asked);
        Assert.Equal("Old mirror", kept!.Bookmark);
        Assert.True(kept.PlainAllowed);
        Assert.False(remote.ServerOf(path)!.Encrypted);

        _answers.Plain = false;
        await remote.DisconnectAsync(ServerAddress.Parse(path)!);
        await Assert.ThrowsAsync<ConnectCancelledException>(() => remote.ConnectAsync(new ServerTarget(ServerAddress.Parse("ftp://old.example.net/pub")!), Ct));
    }

    [Fact]
    public async Task ADroppedConnection_OpensAgain_WithTheSecretsStillInMemory()
    {
        var remote = Remote();
        _answers.Secrets.Enqueue(new SecretAnswer("secret", Remember: false));
        var path = await remote.ConnectAsync(new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!), Ct);
        _servers.DropAll();
        _answers.Asked.Clear();
        Assert.Equal(4, (await remote.ListAsync(path, Ct)).Entries.Length);
        Assert.Empty(_answers.Asked);
        Assert.Equal(2, _files.Connects);
        Assert.Null(_secrets.Read("Salandual:sftp://deploy@files.example.org:22"));
    }

    [Fact]
    public async Task OtherPaths_GoToTheFileSystemItWraps()
    {
        File.WriteAllText(Path.Join(_dir, "local.txt"), "x");
        var remote = Remote();
        Assert.Equal(["local.txt"], (await remote.ListAsync(_dir, Ct)).Entries.Select(e => e.Name));
        Assert.Equal(Path.GetDirectoryName(_dir), remote.GetParent(_dir));
        Assert.DoesNotContain(_servers.Servers.Values, s => s.Connects > 0);
    }

    [Fact]
    public async Task Download_CopiesFilesAndFolders_MarksThemAsDownloaded_AndRefusesBadNames()
    {
        _files.AddFile("/var/www/con.txt", "device name");
        _files.AddFile("/var/www/what?.txt", "a question mark");
        var remote = Remote();
        _answers.Secrets.Enqueue(new SecretAnswer("secret", false));
        var folder = ServerAddress.Parse(await remote.ConnectAsync(new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org/var/www")!), Ct))!;
        var target = Path.Join(_dir, "site");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Join(target, "index.html"), "mine");

        var job = RemoteDownload.Start(new LocalOperations(), remote, new DownloadRequest(folder,
            [("index.html", false), ("styles.css", false), ("assets", true), ("logs", true), ("con.txt", false), ("what?.txt", false)], target, new TransferSettings(ExistingFilePolicy.Skip)));
        job.RunHere();

        Assert.Equal(JobState.DoneWithIssues, job.State);
        Assert.Equal("mine", File.ReadAllText(Path.Join(target, "index.html")));
        Assert.Equal("body {}", File.ReadAllText(Path.Join(target, "styles.css")));
        Assert.Equal("<svg/>", File.ReadAllText(Path.Join(target, "assets", "logo.svg")));
        Assert.True(Directory.Exists(Path.Join(target, "logs")));
        Assert.Equal(2, job.Issues.Count);
        Assert.False(File.Exists(Path.Join(target, "con.txt")));
        Assert.Contains("ZoneId=3", File.ReadAllText(Path.Join(target, "styles.css") + ":Zone.Identifier"), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(target, "*.salandual-part", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("report.txt", true)]
    [InlineData(".htaccess", true)]
    [InlineData("..", false)]
    [InlineData("a\\b", false)]
    [InlineData("NUL", false)]
    [InlineData("com1.log", false)]
    [InlineData("name.", false)]
    [InlineData("tab\there", false)]
    public void ServerNames_AreCheckedBeforeTheyAreWritten(string name, bool ok) => Assert.Equal(ok, RemoteDownload.SafeLocalName(name) is not null);
}
