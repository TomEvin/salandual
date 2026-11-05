// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ArchiveUnpackTests.cs

using System.IO.Compression;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.Core.Operations;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Tests;

public sealed class ArchiveUnpackTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-unpack-").FullName;
    private readonly string _left, _right, _zip;

    public ArchiveUnpackTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Left")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Right")).FullName;
        _zip = Path.Join(_left, "photos.zip");
        using var archive = ZipFile.Open(_zip, ZipArchiveMode.Create);
        Add(archive, "Summer/readme.txt", "Hello from the archive");
        Add(archive, "Summer/beach.txt", "Sand");
        Add(archive, "top.txt", "Top file");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static void Add(ZipArchive archive, string name, string text)
    {
        using var s = archive.CreateEntry(name).Open();
        s.Write(System.Text.Encoding.UTF8.GetBytes(text));
    }

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_left);
        await w.RightPanel.Panel.NavigateAsync(_right);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition, int tries = 1000)
    {
        for (var i = 0; i < tries && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static void FocusOn(MainWindow w, string name)
    {
        var panel = w.ActivePanel.Panel;
        panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == name));
    }

    private static async Task JobsDone(MainWindow w)
    {
        await Until(() => w.Operations.Jobs.Count > 0 && w.Operations.Jobs.All(j => j.IsFinished));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task F5OutOfAnArchive_CopiesTheSelectedEntriesIntoTheOtherPanel()
    {
        var w = await Open();
        FocusOn(w, "photos.zip");
        Press(w, Key.Enter);
        await Until(() => w.ActivePanel.Panel.Path == _zip);
        FocusOn(w, "Summer");

        Press(w, Key.F5);
        await Until(() => w.OpenUnpackDialog is { IsVisible: true });
        var dialog = w.OpenUnpackDialog!;
        Assert.StartsWith("Copy 1 folder (", dialog.Title, StringComparison.Ordinal);
        Assert.EndsWith(") from photos.zip", dialog.Title, StringComparison.Ordinal);
        Assert.False(dialog.AdvancedOpen);
        Assert.Equal(AppStrings.UnpackCopy_Keeping_Both, dialog.Summary);
        Assert.Equal(_right + "\\", dialog.To.Text);
        dialog.Accept(toQueue: false);
        await JobsDone(w);

        Assert.Equal("Sand", File.ReadAllText(Path.Join(_right, "Summer", "beach.txt")));
        Assert.Equal(JobState.Done, w.Operations.Jobs[^1].State);
        Assert.Equal(OperationKind.Unpack, w.Operations.Jobs[^1].Kind);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task Unpack_MakesAFolderOnlyWhenItIsNeeded_AndKeepsTheArchive()
    {
        var w = await Open();
        FocusOn(w, "photos.zip");
        Press(w, Key.F9, RawInputModifiers.Alt);
        await Until(() => w.OpenUnpackDialog is { IsVisible: true });
        var dialog = w.OpenUnpackDialog!;
        Assert.Equal("Unpack photos.zip", dialog.Title);
        Assert.False(dialog.AdvancedOpen);
        Assert.Contains(AppStrings.Unpack_Sum_WhenNeeded, dialog.Summary, StringComparison.Ordinal);
        Assert.Contains(AppStrings.Unpack_Sum_Kept, dialog.Summary, StringComparison.Ordinal);
        Assert.False(dialog.DeleteBox.IsChecked);
        Assert.Contains(Path.Join(_right, "photos") + "\\", dialog.WhereText, StringComparison.Ordinal);

        dialog.OpenAdvanced();
        Assert.True(dialog.AdvancedOpen);
        dialog.AlwaysOption.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(AppStrings.Unpack_Sum_Always, dialog.Summary, StringComparison.Ordinal);
        dialog.Accept(toQueue: false);
        await JobsDone(w);

        Assert.Equal("Top file", File.ReadAllText(Path.Join(_right, "photos", "top.txt")));
        Assert.True(File.Exists(Path.Join(_right, "photos", "Summer", "readme.txt")));
        Assert.True(File.Exists(_zip));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task WhatWasNotUnpacked_IsListedWhenTheJobEnds()
    {
        var crafted = Path.Join(_left, "crafted.zip");
        using (var archive = ZipFile.Open(crafted, ZipArchiveMode.Create))
        {
            Add(archive, "../evil.txt", "evil");
            Add(archive, "docs/CON.txt", "device");
            Add(archive, "docs/fine.txt", "fine");
        }

        var w = await Open();
        await w.ActivePanel.Panel.RefreshAsync();
        FocusOn(w, "crafted.zip");
        Press(w, Key.F9, RawInputModifiers.Alt);
        await Until(() => w.OpenUnpackDialog is { IsVisible: true });
        w.OpenUnpackDialog!.Accept(toQueue: false);
        await JobsDone(w);
        await Until(() => w.OpenUnpackReport is { IsVisible: true });

        var issues = w.OpenUnpackReport!.Issues;
        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Message == Core.Resources.Strings.Unpack_Refused_LeavesTarget);
        Assert.True(File.Exists(Path.Join(_right, "docs", "fine.txt")));
        Assert.False(File.Exists(Path.Join(_dir, "evil.txt")));
        w.OpenUnpackReport.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task ASuspiciousArchiveAsksFirst_AndDontUnpackLeavesIt()
    {
        var bomb = Path.Join(_left, "found-on-usb.zip");
        using (var archive = ZipFile.Open(bomb, ZipArchiveMode.Create))
        {
            using var s = archive.CreateEntry("0.bin", CompressionLevel.SmallestSize).Open();
            s.Write(new byte[4 << 20]);
        }

        var w = await Open();
        w.Services.Settings.Update(s => s with { Operations = s.Operations with { UnpackAskMb = 1 } });
        UnpackSuspicionSeen? seen = null;
        w.UnpackLimitAnswer = suspicion =>
        {
            seen = new UnpackSuspicionSeen(suspicion.Unpacked, suspicion.Ratio);
            return false;
        };
        await w.ActivePanel.Panel.RefreshAsync();
        FocusOn(w, "found-on-usb.zip");
        Press(w, Key.F9, RawInputModifiers.Alt);
        await Until(() => w.OpenUnpackDialog is { IsVisible: true });
        w.OpenUnpackDialog!.Accept(toQueue: false);
        await Until(() => seen is not null);

        Assert.Equal(4 << 20, seen!.Size);
        Assert.True(seen.Ratio > 100);
        Assert.Empty(w.Operations.Jobs);
        Assert.Empty(Directory.GetFileSystemEntries(_right));
        w.CloseNow();
    }

    private sealed record UnpackSuspicionSeen(long Size, double Ratio);

    [AvaloniaFact]
    public async Task TestArchive_SaysItIsAllRight()
    {
        var w = await Open();
        FocusOn(w, "photos.zip");
        Press(w, Key.F9, RawInputModifiers.Alt | RawInputModifiers.Shift);
        await JobsDone(w);
        await Until(() => w.NoticeMessage?.Contains("photos.zip is all right", StringComparison.Ordinal) == true);
        Assert.Equal(OperationKind.Test, w.Operations.Jobs[^1].Kind);
        Assert.Empty(Directory.GetFileSystemEntries(_right));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlCInsideAnArchive_PutsUnpackedCopiesOnTheClipboard()
    {
        var w = await Open();
        var clip = new FakeClipboard();
        w.FileClipboardAccess = clip;
        FocusOn(w, "photos.zip");
        Press(w, Key.Enter);
        await Until(() => w.ActivePanel.Panel.Path == _zip);
        FocusOn(w, "top.txt");
        Press(w, Key.C, RawInputModifiers.Control);
        await Until(() => clip.Files.Count == 1);

        Assert.Equal("top.txt", Path.GetFileName(clip.Files[0]));
        Assert.DoesNotContain(_zip, clip.Files[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Top file", File.ReadAllText(clip.Files[0]));
        Assert.False(clip.Cut);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheRightClickMenuOfAnArchive_HasUnpackAndTest()
    {
        var w = await Open();
        FocusOn(w, "photos.zip");
        Press(w, Key.Enter);
        await Until(() => w.ActivePanel.Panel.Path == _zip);
        Press(w, Key.F10, RawInputModifiers.Shift);
        var headers = w.ArchiveMenu!.Items.OfType<Avalonia.Controls.MenuItem>().Select(i => i.Header as string).Where(h => h is not null).ToList();
        Assert.Contains(AppStrings.QuickMenu_Unpack, headers);
        Assert.Contains(AppStrings.QuickMenu_TestArchive, headers);
        Assert.Contains(AppStrings.QuickMenu_CopyToOther, headers);
        w.ArchiveMenu.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public void Pictures()
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        void Save(Avalonia.Controls.Window window, string name)
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        var contents = Archives.ArchiveReader.Open(_zip, "photos.zip", null);
        var settings = new Core.Settings.OperationSettings();
        var format = Core.Listing.DisplayFormat.From(new Core.Settings.PanelSettings(), System.Globalization.CultureInfo.CurrentCulture);
        var fs = new Platform.Windows.FileSystem.LocalOperations();
        var whole = new Archives.UnpackSource(contents, _zip, _zip, "", []);
        var unpack = new Dialogs.UnpackDialog(new Dialogs.UnpackContext(false, [whole], "Unpack photos.zip", "ZIP · 3 files in 1 folder · 30 B unpacked", _right, settings), fs, format, _ => { });
        Save(unpack, "unpack.png");
        unpack.OpenAdvanced();
        Save(unpack, "unpack-advanced.png");
        unpack.Close();

        var some = new Archives.UnpackSource(contents, _zip, _zip, "", ["Summer"]);
        var copy = new Dialogs.UnpackDialog(new Dialogs.UnpackContext(true, [some], "Copy 1 folder (26 B) from photos.zip", "", _right, settings), fs, format, _ => { });
        Save(copy, "copy.png");
        copy.OpenAdvanced();
        Save(copy, "copy-advanced.png");
        copy.Close();

        var largest = contents.Items.First(i => !i.IsDirectory);
        var limit = new Dialogs.UnpackLimitDialog("found-on-usb.zip", new Archives.UnpackSuspicion(4_700_000_000_000, 16, 43_000, 1.09e8, false, largest), "E:", 412_000_000_000, format);
        Save(limit, "limit.png");
        limit.Close();

        var report = new Dialogs.UnpackReportDialog(AppStrings.Unpack_Unpack, "6 entries of 2024.zip weren't unpacked", "The other 1,198 files are in E:\\Restore\\2024. These were left out, each with its reason:",
            AppStrings.UnpackReport_Why,
            [
                new OperationIssue(@"..\..\Startup\run.lnk", Core.Resources.Strings.Unpack_Refused_LeavesTarget),
                new OperationIssue(@"C:\Windows\win.ini", Core.Resources.Strings.Unpack_Refused_FullPath),
                new OperationIssue(@"Summer\CON.txt", Core.Resources.Loc.Format(Core.Resources.Strings.Unpack_Refused_Device, "CON")),
                new OperationIssue("notes.txt:hidden", Core.Resources.Strings.Unpack_Refused_Stream),
                new OperationIssue(@"Summer\docs → C:\Users", Core.Resources.Strings.Unpack_Refused_LinkOutside),
                new OperationIssue(@"Tihany\IMG_5120.jpg", Core.Resources.Strings.Unpack_Checksum),
            ],
            AppStrings.UnpackReport_Logged, _ => Task.CompletedTask);
        Save(report, "report.png");
        report.Close();
    }

    private sealed class FakeClipboard : IClipboardAccess
    {
        public ClipboardContent Content { get; set; } = new();

        public IReadOnlyList<string> Files => Content.Files;

        public bool Cut => Content.Cut;

        public uint SequenceNumber { get; private set; } = 1;

        public bool SetFiles(IReadOnlyList<string> paths, bool cut)
        {
            Content = new ClipboardContent { Files = [.. paths], Cut = cut };
            SequenceNumber++;
            return true;
        }

        public bool SetText(string text)
        {
            Content = new ClipboardContent { Text = text };
            SequenceNumber++;
            return true;
        }

        public void Clear()
        {
            Content = new ClipboardContent();
            SequenceNumber++;
        }

        public ClipboardContent? Read() => Content;
    }
}
