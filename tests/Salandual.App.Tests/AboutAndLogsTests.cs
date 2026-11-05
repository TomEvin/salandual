// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/AboutAndLogsTests.cs

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Salandual.App.Controls;
using Salandual.App.Diagnostics;
using Salandual.App.Dialogs;
using Salandual.Core;
using Salandual.Core.Commands;
using Salandual.Core.Diagnostics;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class AboutAndLogsTests : IDisposable
{
    private const string You = @"C:\Users\you";

    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-about-logs-").FullName;
    private readonly List<string> _copied = [];
    private readonly List<Uri> _opened = [];

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AboutActions Actions(List<string>? shown = null) => new(t => { _copied.Add(t); return true; }, u => { _opened.Add(u); return true; }, f => shown?.Add(f));

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void TheHelpMenu_HasLogsAndCrashReports_BeforeAbout()
    {
        var help = MainMenu.Create().First(n => n.Children?.Any(c => c.CommandId == "App.About") == true);
        var ids = help.Children!.Select(n => n.CommandId ?? "—").ToList();
        Assert.Equal(["Help.Contents", "Help.WhatIsThis", "Help.FirstSteps", "—", "Help.Logs", "—", "App.About"], ids);
        Assert.Equal("Logs and crash reports…", Core.Resources.Strings.Menu_Help_Logs.Replace("_", "", StringComparison.Ordinal));
        Assert.True(MainWindow.IsBuilt("App.About"));
        Assert.True(MainWindow.IsBuilt("Help.Logs"));
    }

    [AvaloniaFact]
    public void About_ShowsTheVersionTheFacts_AndCopiesThemWithYourFolderHidden()
    {
        var shown = new List<string>();
        var settings = Path.Combine(AppFacts.UserFolder, "AppData", "Roaming", "Salandual");
        var w = new AboutWindow(RenderingMode.Gpu, settings, Actions(shown));
        w.Show();
        Assert.False(w.ShowingThirdParty);
        Assert.StartsWith(ProductInfo.DisplayVersion + " · ", AppFacts.VersionLine(), StringComparison.Ordinal);
        Assert.Matches(@"^v\d+\.\d+.* \(\d+\.\d+\.\d+(\+[0-9a-f]{7})?\), x64$", AppFacts.Version);
        Assert.StartsWith("Windows 1", AppFacts.Windows, StringComparison.Ordinal);

        Click(w.CopyInfo);
        var info = Assert.Single(_copied);
        Assert.Contains("Rendering: GPU (ANGLE, DirectComposition)", info, StringComparison.Ordinal);
        Assert.Contains(@"Settings: %USERPROFILE%\AppData\Roaming\Salandual", info, StringComparison.Ordinal);
        Assert.DoesNotContain(AppFacts.UserFolder, info, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Copied", w.CopyInfo.Content);

        Click(w.SourceButton);
        Assert.Equal(new Uri(ProductInfo.RepositoryUrl), Assert.Single(_opened));

        Click(w.OpenSettings);
        Assert.Equal([settings], shown);
        Assert.False(w.IsVisible);
    }

    [AvaloniaFact]
    public void ThirdParty_EachRowShowsItsLicence_AndTheLicenceButtonShowsTheGpl()
    {
        var w = new AboutWindow(RenderingMode.Software, _dir, Actions());
        w.Show();
        Click(w.LicenceButton);
        Assert.True(w.ShowingThirdParty);
        Assert.Equal(ProductInfo.DisplayName, w.SelectedComponent!.Name);
        Assert.StartsWith("GNU GENERAL PUBLIC LICENSE", w.LicenceText.Trim(), StringComparison.Ordinal);

        foreach (var c in w.Components)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Text), c.Name);
            Assert.DoesNotContain("{0}", c.Text, StringComparison.Ordinal);
        }

        var magick = w.Components.First(c => c.Name.StartsWith("Magick.NET", StringComparison.Ordinal));
        w.ShowLicence(magick);
        Assert.Contains("ImageMagick License", w.LicenceText, StringComparison.Ordinal);
        Assert.Contains("Apache License", w.LicenceText, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "licences", "Magick.NET-notice.txt")));
        w.Close();
    }

    [Fact]
    public void ThirdParty_AgreesWithTheNoticesFile()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "THIRD-PARTY-NOTICES.md")))
        {
            dir = Path.GetDirectoryName(dir)!;
        }

        var notices = File.ReadAllText(Path.Combine(dir, "THIRD-PARTY-NOTICES.md"));
        foreach (var c in ThirdParty.All.Skip(1).Where(c => c.Version.Length > 0))
        {
            Assert.Contains("| " + c.Version + " |", notices.Replace("| with Magick.NET ", "| ", StringComparison.Ordinal), StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public void SevenClicksOnTheLogo_TheSalamanderRuns()
    {
        var now = new DateTime(2026, 10, 26, 14, 0, 0);
        var w = new AboutWindow(RenderingMode.Gpu, _dir, Actions()) { Clock = () => now };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, w.LogoTurn);

        void ClickAfter(double seconds)
        {
            now = now.AddSeconds(seconds);
            w.ClickLogo();
        }

        for (var i = 0; i < 3; i++)
        {
            ClickAfter(0.4);
        }

        Assert.Equal("", w.More.Text);
        ClickAfter(0.4);
        Assert.Equal("3 more…", w.More.Text);
        ClickAfter(2);
        Assert.Equal("", w.More.Text);
        for (var i = 0; i < 5; i++)
        {
            ClickAfter(0.3);
        }

        Assert.Equal("1 more…", w.More.Text);
        Assert.False(w.Running);
        ClickAfter(0.3);
        Assert.Equal("Two panels, one salamander. Thanks for looking this closely.", w.More.Text);
        Assert.True(w.Running);
        Assert.True(w.Runner.IsVisible);

        var start = w.Elapsed;
        w.Frame(start + 0.35);
        Assert.NotEqual(0, w.LogoTurn);
        var legs = w.Runner.FirstStep;
        w.Frame(start + 0.35 + (Salamander.Step / 2));
        Assert.NotEqual(legs, w.Runner.FirstStep);
        w.Frame(start + AboutWindow.RunSeconds + 0.1);
        Assert.False(w.Running);
        Assert.False(w.Runner.IsVisible);
        Assert.Equal(0, w.LogoTurn);
        Assert.NotEqual("", w.More.Text);
        w.Frame(start + 4.1);
        Assert.Equal("", w.More.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void WithAnimationEffectsOff_OnlyTheLineShows()
    {
        var w = new AboutWindow(RenderingMode.Gpu, _dir, Actions(), animate: false);
        w.Show();
        for (var i = 0; i < AboutWindow.ClicksForSurprise; i++)
        {
            w.ClickLogo();
        }

        Assert.Equal("Two panels, one salamander. Thanks for looking this closely.", w.More.Text);
        Assert.False(w.Running);
        Assert.False(w.Runner.IsVisible);
        w.Close();
    }

    [Fact]
    public void TheRun_FromOffTheLeft_SlowingInTheMiddle_FadingAtTheEnd()
    {
        Assert.Equal((-90.0, 1.0), Salamander.RunAt(0, 3, 400));
        var (middle, _) = Salamander.RunAt(1.5, 3, 400);
        Assert.InRange(middle, 0.40 * 400, 0.46 * 400);
        var (end, faded) = Salamander.RunAt(3, 3, 400);
        Assert.Equal((360.0, 0.0), (end, faded));
        Assert.True(Salamander.RunAt(2.9, 3, 400).Opacity < 1);
    }

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    [Fact]
    public void AProblem_IsReported_AndTheSameOneWithinAMinute_Closes()
    {
        var clock = new FakeClock(new DateTime(2026, 10, 26, 14, 0, 0));
        var log = new AppLog(_dir, clock);
        var problems = new ProblemHandler(log, () => "facts", () => ["Options"], clock.Local.AddHours(-2));

        Assert.Equal(ProblemKind.WentOn, problems.Handle(Thrown("first")));
        var first = problems.LastReport!;
        Assert.StartsWith("problem-", Path.GetFileName(first), StringComparison.Ordinal);
        Assert.Contains("Something went wrong · 26 October 2026, 14:00:00 · running 2 h 0 min", File.ReadAllText(first), StringComparison.Ordinal);
        Assert.False(problems.Crashed);

        clock.Local = clock.Local.AddMinutes(2);
        Assert.Equal(ProblemKind.WentOn, problems.Handle(Thrown("again, but later")));
        clock.Local = clock.Local.AddSeconds(20);
        Assert.Equal(ProblemKind.Crash, problems.Handle(Thrown("and again soon")));
        Assert.True(problems.Crashed);
        Assert.StartsWith("crash-", Path.GetFileName(problems.LastReport!), StringComparison.Ordinal);
        Assert.Equal(problems.LastReport, ProblemReports.TakeUnseen(_dir)!.Path);
        Assert.Contains(log.Recent, l => l.Contains("error    Closed unexpectedly: InvalidOperationException", StringComparison.Ordinal));

        problems.HandleFatal(Thrown("after the crash"));
        Assert.Equal(3, ProblemReports.List(_dir).Count);
        problems.HandleUnobserved(new AggregateException(new TimeoutException("slow")));
        Assert.EndsWith("warning  In the background: TimeoutException: slow", log.Recent[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheDialog_SaysWhereInSalandual()
        => Assert.Equal("AboutAndLogsTests.Thrown", ProblemHandler.Where(Thrown("x")));

    [AvaloniaFact]
    public void TheProblemDialog_GoOnIsTheDefault()
    {
        var w = new ProblemDialog(Thrown("Object reference not set to an instance of an object."));
        w.Show();
        Assert.True(w.GoOn.IsDefault);
        Assert.Equal(["Show the report", "Restart Salandual", "Go on"], new[] { w.ShowReport, w.Restart, w.GoOn }.Select(b => (string)b.Content!));
        w.Close();
    }

    [Theory]
    [InlineData(0, "today at 14:02")]
    [InlineData(1, "yesterday at 14:02")]
    [InlineData(3, "on 23 October at 14:02")]
    public void TheNoticeAtTheNextStart_SaysWhen(int daysAgo, string said)
    {
        var now = new DateTime(2026, 10, 26, 16, 0, 0);
        using var _ = new CultureScope("hu-HU");
        Assert.Equal(said, MainWindow.CrashWhen(new DateTime(2026, 10, 26, 14, 2, 0).AddDays(-daysAgo), now));
    }

    private void Fill()
    {
        var crash = ProblemReports.Build(ProblemKind.Crash, Thrown($@"Couldn't read {You}\Pictures\IMG_0412.HEIC"), "facts", new DateTime(2026, 10, 26, 14, 2, 11), TimeSpan.FromHours(2), [], []);
        ProblemReports.Write(_dir, ProblemKind.Crash, new DateTime(2026, 10, 26, 14, 2, 11), crash);
        ProblemReports.Write(_dir, ProblemKind.WentOn, new DateTime(2026, 10, 23, 9, 41, 0), "facts\nSomething went wrong\n\nSystem.IO.IOException: busy");
        File.Delete(Path.Combine(_dir, ProblemReports.UnseenName));
        File.WriteAllLines(Path.Combine(_dir, "salandual-2026-10-26.log"), ["14:01:40  info     Started: v0.1, GPU", "14:01:52  warning  Settings: saved after one retry", "14:02:03  warning  Viewer: x", "14:02:11  error    Closed unexpectedly"]);
        File.WriteAllLines(Path.Combine(_dir, "salandual-2026-10-25.log"), ["10:00:00  info     Started: v0.1, GPU"]);
    }

    private LogsWindow Logs() => new(_dir, You, t => { _copied.Add(t); return true; }, u => { _opened.Add(u); return true; }, now: () => new DateTime(2026, 10, 26, 16, 0, 0));

    [AvaloniaFact]
    public void TheList_ReportsThenTheDays_WithTheirChips()
    {
        Fill();
        using var _ = new CultureScope("hu-HU");
        var w = Logs();
        w.Show();
        Assert.Equal([("Closed unexpectedly", "crash", "Today 14:02 · InvalidOperationException"), ("Something went wrong", "went on", "23 Oct 09:41 · IOException"),
            ("Today", "1 error", "salandual-2026-10-26.log · 161 B"), ("Yesterday", "", "salandual-2026-10-25.log · 39 B")],
            w.Items.Select(i => (i.Title, i.Chip, i.Sub)));
        Assert.Equal(w.Items[0], w.Selected);
        w.Close();
    }

    [AvaloniaFact]
    public void HidePersonalPaths_OnByDefault_KeepsTheNames_AndCopyTakesWhatIsShown()
    {
        Fill();
        var w = Logs();
        w.Show();
        Assert.True(w.HidePaths.IsChecked);
        Assert.Contains(@"Couldn't read %USERPROFILE%\Pictures\IMG_0412.HEIC", w.Shown, StringComparison.Ordinal);
        Assert.DoesNotContain(You, w.Shown, StringComparison.Ordinal);
        Click(w.CopyButton);
        Assert.Equal(w.Shown, Assert.Single(_copied));
        Assert.Equal("Copied to the clipboard.", w.Status);

        w.HidePaths.IsChecked = false;
        Assert.Contains($@"Couldn't read {You}\Pictures\IMG_0412.HEIC", w.Shown, StringComparison.Ordinal);

        w.Select(w.Items[2].Path);
        Assert.StartsWith("14:01:40  info     Started", w.Shown, StringComparison.Ordinal);
        w.Close();
    }

    [AvaloniaFact]
    public void HowToReport_TheNewIssuePage_HasTheReportFilledIn()
    {
        Fill();
        var w = Logs();
        w.Show();
        Click(w.HowButton);
        Assert.True(w.ShowingGuide);
        Assert.Equal("Back to the text", w.HowButton.Content);
        Click(w.IssueButton!);
        var page = Assert.Single(_opened);
        Assert.StartsWith(ProductInfo.RepositoryUrl + "/issues/new?title=Closed%20unexpectedly%3A%20InvalidOperationException&body=", page.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("%25USERPROFILE%25", page.AbsoluteUri, StringComparison.Ordinal);
        Assert.Empty(_copied);

        var (address, cut) = LogsWindow.IssueAddress(w.Items[0], new string('x', 20_000));
        Assert.True(cut);
        Assert.True(address.AbsoluteUri.Length <= LogsWindow.MaxIssueAddress);
        Click(w.HowButton);
        Assert.False(w.ShowingGuide);
        w.Close();
    }

    [AvaloniaFact]
    public void DeleteAll_TakesOnlyItsOwnFiles()
    {
        Fill();
        File.WriteAllText(Path.Combine(_dir, "mine.txt"), "kept");
        var w = Logs();
        w.Show();
        w.DeleteFiles(AppLog.OwnFiles(_dir));
        Assert.Empty(w.Items);
        Assert.Equal(["mine.txt"], Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.Contains("Nothing here yet", w.Shown, StringComparison.Ordinal);
        Assert.False(w.CopyButton.IsEnabled);
        w.Close();
    }

    [AvaloniaFact]
    public async Task FromTheMenu_AboutAndTheLogsOpen()
    {
        var main = new MainWindow(AppServices.CreateIsolated());
        main.Show();
        Assert.True(main.Run("Help.Logs", PanelTarget.Active));
        Assert.NotNull(main.Logs);
        main.Logs!.Close();
        Assert.Null(main.Logs);

        Assert.True(main.Run("App.About", PanelTarget.Active));
        Dispatcher.UIThread.RunJobs();
        var about = main.About!;
        Assert.NotNull(about);
        about.Close();
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
        Assert.Null(main.About);
        main.Close();
    }

    [AvaloniaFact]
    public void Pictures()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Fill();
        Directory.CreateDirectory(dir);
        void Save(Window w, string name) => w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);

        var about = new AboutWindow(RenderingMode.Gpu, @"C:\Users\you\AppData\Roaming\Salandual", Actions());
        about.Show();
        Dispatcher.UIThread.RunJobs();
        Save(about, "about.png");
        for (var i = 0; i < AboutWindow.ClicksForSurprise; i++)
        {
            about.ClickLogo();
        }

        about.Frame(about.Elapsed + 1.4);
        Dispatcher.UIThread.RunJobs();
        Save(about, "about-surprise.png");
        about.ShowThirdParty(true);
        Dispatcher.UIThread.RunJobs();
        Save(about, "about-third.png");
        about.Close();

        var logs = Logs();
        logs.Show();
        Dispatcher.UIThread.RunJobs();
        Save(logs, "logs.png");
        logs.ShowGuide(true);
        Dispatcher.UIThread.RunJobs();
        Save(logs, "logs-how.png");
        logs.Close();

        var problem = new ProblemDialog(Thrown("Object reference not set to an instance of an object."));
        problem.Show();
        Dispatcher.UIThread.RunJobs();
        Save(problem, "problem.png");
        problem.Close();
    }

    private sealed class FakeClock(DateTime local) : TimeProvider
    {
        public DateTime Local { get; set; } = local;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(Local, TimeSpan.Zero);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _was = System.Globalization.CultureInfo.CurrentCulture;

        public CultureScope(string name) => System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(name);

        public void Dispose() => System.Globalization.CultureInfo.CurrentCulture = _was;
    }
}
