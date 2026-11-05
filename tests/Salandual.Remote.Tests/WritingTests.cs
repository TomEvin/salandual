// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Remote.Tests/WritingTests.cs

using System.Diagnostics;
using System.Text;
using Salandual.Core.Operations;
using Salandual.Core.Settings;
using Salandual.Platform.Windows.FileSystem;

namespace Salandual.Remote.Tests;

public sealed class WritingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-writing-").FullName;
    private readonly MemoryServers _servers = new();
    private readonly MemorySecretStore _secrets = new();
    private readonly Answers _answers = new();
    private readonly MemoryServer _files;
    private readonly RemoteFileSystem _remote;
    private readonly LocalOperations _fs = new();

    public WritingTests()
    {
        _files = _servers.Add("files.example.org");
        _files.AddFile("/var/www/index.html", "<h1>Hello</h1>");
        _files.AddFile("/var/www/assets/logo.svg", "<svg/>");
        _files.AddFile("/var/www/assets/icons/up.svg", "<svg up/>");
        _files.AddFolder("/var/www/logs");
        _remote = new RemoteFileSystem(new LocalFileSystemProvider(), _servers, _secrets, TrustedServers.InMemory()) { Questions = _answers };
        _answers.Secrets.Enqueue(new SecretAnswer("secret", false));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ServerAddress> ConnectAsync(string path = "/var/www")
        => ServerAddress.Parse(await _remote.ConnectAsync(new ServerTarget(ServerAddress.Parse("sftp://deploy@files.example.org" + path)!), Ct))!;

    private string Text(string path) => Encoding.UTF8.GetString(_files.Files[path]!);

    private string Local(params string[] parts) => Path.Join([_dir, .. parts]);

    private string Write(string relative, string content)
    {
        var path = Local(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task RunAsync(OperationJob job, Func<OperationQuestion, OperationAnswer>? answer = null)
    {
        var run = Task.Run(job.RunHere, Ct);
        while (!run.IsCompleted)
        {
            if (job.Question is { } q)
            {
                job.Answer(answer?.Invoke(q) ?? new OperationAnswer(AnswerKind.Cancel));
            }

            await Task.Delay(10, Ct);
        }

        await run;
    }

    [Fact]
    public async Task Upload_CopiesFilesAndFolders_MakesTheFolders_AndKeepsTheDates()
    {
        var www = await ConnectAsync();
        Write("site/about.html", "about");
        Write("site/css/main.css", "main {}");
        Directory.CreateDirectory(Local("site", "empty"));
        var date = new DateTime(2026, 3, 14, 9, 26, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Local("site", "about.html"), date);

        var job = RemoteDownload.Upload(_fs, _remote, new UploadRequest(Local("site"), [("about.html", false), ("css", true), ("empty", true)], www));
        await RunAsync(job);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal("about", Text("/var/www/about.html"));
        Assert.Equal("main {}", Text("/var/www/css/main.css"));
        Assert.Null(_files.Files["/var/www/empty"]);
        Assert.Equal(date, _files.Times["/var/www/about.html"]);
        Assert.Equal(OperationKind.Copy, job.Kind);
        Assert.Contains(_remote.LogOf(www).Lines, l => l.Text == "Copying /var/www/about.html to the server");
        Assert.Contains(_remote.LogOf(www).Lines, l => l.Text == "New folder /var/www/css");
        Assert.True(File.Exists(Local("site", "about.html")));
    }

    [Fact]
    public async Task UploadMove_RemovesYourFiles_OnlyOnceTheServerHasThem_AndTheEmptiedFolders()
    {
        var www = await ConnectAsync();
        Write("out/a.txt", "a");
        Write("out/deep/b.txt", "bb");
        _files.ReadOnly = false;

        var job = RemoteDownload.Upload(_fs, _remote, new UploadRequest(_dir, [("out", true)], www, new TransferSettings(Move: true)));
        await RunAsync(job);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(OperationKind.Move, job.Kind);
        Assert.Equal("bb", Text("/var/www/out/deep/b.txt"));
        Assert.False(Directory.Exists(Local("out")));
    }

    [Fact]
    public async Task UploadMove_ThatTheServerRefuses_LeavesYourFiles()
    {
        var www = await ConnectAsync();
        Write("keep.txt", "mine");
        _files.ReadOnly = true;

        var job = RemoteDownload.Upload(_fs, _remote, new UploadRequest(_dir, [("keep.txt", false)], www, new TransferSettings(Move: true)));
        await RunAsync(job, q => new OperationAnswer(q is ErrorQuestion ? AnswerKind.Skip : AnswerKind.Cancel));

        Assert.Equal(JobState.DoneWithIssues, job.State);
        Assert.Equal("mine", File.ReadAllText(Local("keep.txt")));
        Assert.Contains(_remote.LogOf(www).Lines, l => l.Text.StartsWith("Failed: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_OnlyMatching_AndSkipEmptyFolders()
    {
        var www = await ConnectAsync();
        Write("src/page.html", "p");
        Write("src/notes.txt", "n");
        Write("src/old/readme.txt", "r");

        var job = RemoteDownload.Upload(_fs, _remote, new UploadRequest(Local("src"), [("page.html", false), ("notes.txt", false), ("old", true)], www,
            new TransferSettings(Filter: "*.html", SkipEmptyFolders: true)));
        await RunAsync(job);

        Assert.True(_files.Files.ContainsKey("/var/www/page.html"));
        Assert.False(_files.Files.ContainsKey("/var/www/notes.txt"));
        Assert.False(_files.Files.ContainsKey("/var/www/old"));
    }

    [Fact]
    public async Task AFileAlreadyOnTheServer_IsAsked_WithResume_WhenItIsTheStartOfYours()
    {
        var www = await ConnectAsync();
        var full = new string('x', 3000) + "END";
        Write("release.zip", full);
        _files.AddFile("/var/www/release.zip", full[..1000]);
        _files.AddFile("/var/www/index2.html", "older");
        Write("index2.html", "mine, smaller");
        ConflictQuestion? asked = null;

        var job = RemoteDownload.Upload(_fs, _remote, new UploadRequest(_dir, [("release.zip", false)], www));
        await RunAsync(job, q =>
        {
            asked = (ConflictQuestion)q;
            return new OperationAnswer(AnswerKind.Resume);
        });

        Assert.True(asked!.CanResume);
        Assert.Equal(full, Text("/var/www/release.zip"));
        Assert.Contains(_remote.LogOf(www).Lines, l => l.Text == "Copying the rest of /var/www/release.zip to the server, from byte 1000");

        _files.AddFile("/var/www/index2.html", "a bigger file on the server");
        var second = RemoteDownload.Upload(_fs, _remote, new UploadRequest(_dir, [("index2.html", false)], www));
        await RunAsync(second, q =>
        {
            asked = (ConflictQuestion)q;
            return new OperationAnswer(AnswerKind.KeepBoth);
        });
        Assert.False(asked.CanResume);
        Assert.Equal("mine, smaller", Text("/var/www/index2 (2).html"));
        Assert.Equal("a bigger file on the server", Text("/var/www/index2.html"));
    }

    [Fact]
    public async Task TheSpeedLimit_SlowsACopyDown()
    {
        var www = await ConnectAsync();
        Write("big.bin", new string('b', 8000));
        var job = RemoteDownload.Upload(_fs, _remote, new UploadRequest(_dir, [("big.bin", false)], www, new TransferSettings(SpeedLimit: 10_000)));
        Assert.Equal(10_000, job.SpeedLimit);

        var clock = Stopwatch.StartNew();
        await RunAsync(job);

        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(500), clock.Elapsed.ToString());
        Assert.Equal(8000, _files.Files["/var/www/big.bin"]!.Length);
    }

    [Fact]
    public async Task ADownloadThatStopped_IsResumed()
    {
        var www = await ConnectAsync();
        var full = new string('d', 5000);
        _files.AddFile("/var/www/data.bin", full);
        Write("got/data.bin", full[..2000]);
        ConflictQuestion? asked = null;

        var job = RemoteDownload.Start(_fs, _remote, new DownloadRequest(www, [("data.bin", false)], Local("got")));
        await RunAsync(job, q =>
        {
            asked = (ConflictQuestion)q;
            return new OperationAnswer(AnswerKind.Resume);
        });

        Assert.True(asked!.CanResume);
        Assert.Equal(full, File.ReadAllText(Local("got", "data.bin")));
        Assert.Contains(_remote.LogOf(www).Lines, l => l.Text == "Copying the rest of /var/www/data.bin from the server, from byte 2000");
    }

    [Fact]
    public async Task DownloadMove_RemovesTheServersFiles_AndItsEmptiedFolders()
    {
        var www = await ConnectAsync();
        var job = RemoteDownload.Start(_fs, _remote, new DownloadRequest(www, [("assets", true)], Local(), new TransferSettings(Move: true, Verify: true)));
        await RunAsync(job);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal("<svg up/>", File.ReadAllText(Local("assets", "icons", "up.svg")));
        Assert.DoesNotContain(_files.Files.Keys, k => k.StartsWith("/var/www/assets", StringComparison.Ordinal));
        Assert.True(_files.Files.ContainsKey("/var/www/index.html"));
    }

    [Fact]
    public async Task Delete_RemovesFoldersWithEverythingInThem()
    {
        var www = await ConnectAsync();
        var job = RemoteChanges.Delete(_remote, www, [("assets", true), ("index.html", false)]);
        await RunAsync(job);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(OperationKind.Delete, job.Kind);
        Assert.DoesNotContain(_files.Files.Keys, k => k.StartsWith("/var/www/assets", StringComparison.Ordinal) || k == "/var/www/index.html");
        Assert.True(_files.Files.ContainsKey("/var/www/logs"));
        Assert.Contains(_remote.LogOf(www).Lines, l => l.Text == "Deleting /var/www/assets/icons");
    }

    [Theory]
    [InlineData(PermissionsScope.Itself, "rwxr-x---", "rw-r--r--", "rwxr-xr-x")]
    [InlineData(PermissionsScope.Files, "rwxr-x---", "rwxr-x---", "rwxr-xr-x")]
    [InlineData(PermissionsScope.Folders, "rwxr-x---", "rw-r--r--", "rwxr-x---")]
    [InlineData(PermissionsScope.All, "rwxr-x---", "rwxr-x---", "rwxr-x---")]
    public async Task Permissions_GoOnTheChosenItems_AndInsideFoldersAsChosen(PermissionsScope scope, string folder, string file, string inner)
    {
        var www = await ConnectAsync();
        var job = RemoteChanges.SetPermissions(_remote, www, [("assets", true)], 0b111_101_000, scope);
        await RunAsync(job);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(folder, _files.Rights["/var/www/assets"]);
        Assert.Equal(file, _files.Rights["/var/www/assets/logo.svg"]);
        Assert.Equal(inner, _files.Rights.GetValueOrDefault("/var/www/assets/icons", "rwxr-xr-x"));
        Assert.Contains(_remote.LogOf(www).Lines, l => l.Text == "Permissions of /var/www/assets: 750 (rwxr-x---)");
    }

    [Fact]
    public async Task Permissions_OnAServerWithoutChmod_SayWhy()
    {
        var www = await ConnectAsync();
        _files.NoChmod = true;
        string? said = null;
        var job = RemoteChanges.SetPermissions(_remote, www, [("index.html", false)], 0b110_100_100, PermissionsScope.Itself);
        await RunAsync(job, q =>
        {
            said = ((ErrorQuestion)q).Message;
            return new OperationAnswer(AnswerKind.Skip);
        });

        Assert.StartsWith("This server doesn't change permissions", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveWithinAServer_Renames_AndAsksAboutAFileThere()
    {
        var www = await ConnectAsync();
        _files.AddFile("/var/www/logs/index.html", "an older one");
        var logs = www.Child("logs");

        var job = RemoteChanges.MoveWithin(_remote, www, [("index.html", false), ("assets", true)], logs, ExistingFilePolicy.Ask);
        await RunAsync(job, q => new OperationAnswer(AnswerKind.KeepBoth));

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal("<h1>Hello</h1>", Text("/var/www/logs/index (2).html"));
        Assert.Equal("<svg up/>", Text("/var/www/logs/assets/icons/up.svg"));
        Assert.False(_files.Files.ContainsKey("/var/www/index.html"));
        Assert.Contains(_remote.LogOf(www).Lines, l => l.Text == "Renaming /var/www/assets to /var/www/logs/assets");

        var into = RemoteChanges.MoveWithin(_remote, logs, [("assets", true)], logs.Child("assets").Child("icons"), ExistingFilePolicy.Ask);
        await RunAsync(into);
        Assert.Equal(JobState.Failed, into.State);
    }

    [Theory]
    [InlineData("rw-r--r--", 0b110_100_100)]
    [InlineData("rwxr-xr-x", 0b111_101_101)]
    [InlineData("rwsr-xr-T", 0b111_101_100)]
    [InlineData("---------", 0)]
    public void PermissionsText_AndNumber_GoBothWays(string text, int mode)
    {
        Assert.Equal(mode, RemoteFileSystem.ModeOf(text));
        if (!text.Contains('s') && !text.Contains('T'))
        {
            Assert.Equal(text, RemoteFileSystem.RightsText(mode));
        }
    }

    [Fact]
    public void NotPermissions_AreNotRead() => Assert.Null(RemoteFileSystem.ModeOf("rw-r--r-"));
}
