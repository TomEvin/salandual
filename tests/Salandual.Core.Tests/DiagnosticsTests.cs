// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.Core.Tests/DiagnosticsTests.cs

using Salandual.Core.Diagnostics;

namespace Salandual.Core.Tests;

public sealed class DiagnosticsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-logs-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    internal sealed class Clock(DateTime local) : TimeProvider
    {
        public DateTime Local { get; set; } = local;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(Local, TimeSpan.Zero);
    }

    [Fact]
    public void TheLog_OneFileADay_ALineEach_TheLastOnesKept()
    {
        var clock = new Clock(new DateTime(2026, 10, 26, 14, 1, 40));
        var log = new AppLog(_dir, clock);
        log.Write(LogLevel.Info, "Started: v0.1, GPU");
        clock.Local = clock.Local.AddSeconds(12);
        log.Write(LogLevel.Warning, "two\nlines");
        clock.Local = new DateTime(2026, 10, 27, 9, 0, 0);
        log.Write(LogLevel.Error, "next day");

        var first = File.ReadAllLines(Path.Combine(_dir, "salandual-2026-10-26.log"));
        Assert.Equal(["Salandual's log for Monday 26 October 2026", "14:01:40  info     Started: v0.1, GPU", "14:01:52  warning  two lines"], first);
        Assert.Equal(["Salandual's log for Tuesday 27 October 2026", "09:00:00  error    next day"], File.ReadAllLines(Path.Combine(_dir, "salandual-2026-10-27.log")));
        Assert.Equal([null, LogLevel.Info, LogLevel.Warning], first.Select(AppLog.LevelOf));
        Assert.Null(AppLog.LevelOf("   at Salandual.App.Panels.FilePanelView.Render()"));
        Assert.Equal(new DateTime(2026, 10, 27), AppLog.DayOf("salandual-2026-10-27.log"));
        Assert.Null(AppLog.DayOf("notes.log"));

        for (var i = 0; i < 30; i++)
        {
            log.Write(LogLevel.Info, "line " + i);
        }

        Assert.Equal(AppLog.RecentCount, log.Recent.Count);
        Assert.EndsWith("line 29", log.Recent[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAFolder_NothingIsWritten()
    {
        var log = new AppLog(null);
        log.Write(LogLevel.Error, "only in memory");
        Assert.Single(log.Recent);
        Assert.Equal(0, log.DeleteOld());
    }

    [Fact]
    public void Kept30Days_OnlySalandualsOwnFilesGo()
    {
        var clock = new Clock(new DateTime(2026, 10, 26, 12, 0, 0));
        var log = new AppLog(_dir, clock);
        foreach (var name in new[] { "salandual-2026-09-25.log", "salandual-2026-09-26.log", "salandual-2026-10-26.log", "notes.txt", "crash-2026-09-01-100000.txt" })
        {
            File.WriteAllText(Path.Combine(_dir, name), "x");
        }

        File.SetLastWriteTime(Path.Combine(_dir, "notes.txt"), new DateTime(2026, 1, 1));
        File.SetLastWriteTime(Path.Combine(_dir, "crash-2026-09-01-100000.txt"), new DateTime(2026, 9, 1));

        Assert.Equal(2, log.DeleteOld());
        Assert.Equal(["notes.txt", "salandual-2026-09-26.log", "salandual-2026-10-26.log"], Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    private static Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException(@"Couldn't read C:\Users\you\Pictures\IMG_0412.HEIC");
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    [Fact]
    public void AReport_SaysWhatWhenAndWhere_AndACrashIsSaidOnceAtTheNextStart()
    {
        var time = new DateTime(2026, 10, 26, 14, 2, 11);
        var text = ProblemReports.Build(ProblemKind.Crash, Thrown(), "Salandual v0.1 (0.1.0+5b777ac) x64 · Windows 11 · .NET 10.0.12 · GPU", time,
            new TimeSpan(2, 14, 30), ["Options"], ["14:01:40  info     Started"]);
        var lines = text.Split(Environment.NewLine);
        Assert.Equal("Closed unexpectedly · 26 October 2026, 14:02:11 · running 2 h 14 min", lines[1]);
        Assert.Equal(@"System.InvalidOperationException: Couldn't read C:\Users\you\Pictures\IMG_0412.HEIC", lines[3]);
        Assert.StartsWith("   at Salandual.Core.Tests.DiagnosticsTests.Thrown()", lines[4], StringComparison.Ordinal);
        Assert.Contains("Open: Options", lines);
        Assert.Equal("14:01:40  info     Started", lines[^2]);

        var crash = ProblemReports.Write(_dir, ProblemKind.Crash, time, text)!;
        var wentOn = ProblemReports.Write(_dir, ProblemKind.WentOn, time.AddMinutes(-5), "facts\nSomething went wrong\n\nSystem.IO.IOException: busy")!;
        var again = ProblemReports.Write(_dir, ProblemKind.WentOn, time.AddMinutes(-5), "facts\nSomething went wrong\n\nSystem.IO.IOException: busy")!;
        Assert.Equal("crash-2026-10-26-140211.txt", Path.GetFileName(crash));
        Assert.Equal("problem-2026-10-26-135711-2.txt", Path.GetFileName(again));

        var list = ProblemReports.List(_dir);
        Assert.Equal([(crash, ProblemKind.Crash, "InvalidOperationException"), (again, ProblemKind.WentOn, "IOException"), (wentOn, ProblemKind.WentOn, "IOException")],
            list.Select(r => (r.Path, r.Kind, r.ExceptionType)));
        Assert.Equal(crash, ProblemReports.TakeUnseen(_dir)?.Path);
        Assert.Null(ProblemReports.TakeUnseen(_dir));
        Assert.Equal(3, AppLog.OwnFiles(_dir).Count());
    }

    [Theory]
    [InlineData(40, "40 s")]
    [InlineData(5 * 60 + 3, "5 min")]
    [InlineData(2 * 3600 + 14 * 60, "2 h 14 min")]
    public void HowLongItRan(int seconds, string said) => Assert.Equal(said, ProblemReports.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void HidingPersonalPaths_TakesYourUserFolder_KeepsTheNames()
    {
        const string you = @"C:\Users\tom";
        const string text = @"Couldn't open c:\users\TOM\Pictures\IMG_0412.HEIC; C:\Users\tomas\a.txt and D:\Photos stay";
        Assert.Equal(@"Couldn't open %USERPROFILE%\Pictures\IMG_0412.HEIC; C:\Users\tomas\a.txt and D:\Photos stay", PersonalPaths.Hide(text, you));
        var parts = PersonalPaths.Split(text, you + @"\");
        Assert.Equal([false, true, false], parts.Select(p => p.Hidden));
        Assert.Equal("%USERPROFILE%", parts[1].Text);
        Assert.Equal(@"%USERPROFILE%", PersonalPaths.Hide(you, you));
        Assert.Equal("nothing", PersonalPaths.Hide("nothing", ""));
    }

    [Fact]
    public void TheVersion_HasItsCommitAndDay()
    {
        Assert.StartsWith(ProductInfo.Version, ProductInfo.FullVersion, StringComparison.Ordinal);
        Assert.NotNull(ProductInfo.BuildDate);
        Assert.True(ProductInfo.Commit.Length is 0 or 7);
        Assert.StartsWith("https://", ProductInfo.RepositoryUrl, StringComparison.Ordinal);
    }
}
