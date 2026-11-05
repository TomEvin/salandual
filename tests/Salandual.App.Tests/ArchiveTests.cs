// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ArchiveTests.cs

using System.IO.Compression;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Salandual.App.Tests;

public sealed class ArchiveTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-archives-").FullName;
    private readonly string _zip;

    public ArchiveTests()
    {
        _zip = Path.Join(_dir, "photos.zip");
        using var archive = ZipFile.Open(_zip, ZipArchiveMode.Create);
        Add(archive, "Summer/readme.txt", "Hello from the archive", CompressionLevel.Optimal);
        Add(archive, "Summer/stored.txt", "stored", CompressionLevel.NoCompression);
        Add(archive, "top.txt", "Top file", CompressionLevel.Optimal);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static void Add(ZipArchive archive, string name, string text, CompressionLevel level)
    {
        using var s = archive.CreateEntry(name, level).Open();
        s.Write(System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(text + "\n", 40))));
    }

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir);
        await w.RightPanel.Panel.NavigateAsync(_dir);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static void Press(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(key, mods, PhysicalKey.None, null);
        w.KeyRelease(key, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
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

    private async Task<MainWindow> Inside(string folder = "")
    {
        var w = await Open();
        FocusOn(w, "photos.zip");
        Press(w, Key.Enter);
        await Until(() => w.ActivePanel.Panel.Path == _zip);
        if (folder.Length > 0)
        {
            FocusOn(w, folder);
            Press(w, Key.Enter);
            await Until(() => w.ActivePanel.Panel.Path == _zip + "\\" + folder);
        }

        return w;
    }

    [AvaloniaFact]
    public async Task EnterOpensAnArchive_BackspaceAtItsRootGoesBack()
    {
        var w = await Inside();
        var host = w.ActivePanel;
        Assert.Equal(["..", "Summer", "top.txt"], host.Panel.Items.Select(e => e.Name).ToArray());

        var facts = host.DirectoryLine.Archive;
        Assert.NotNull(facts);
        Assert.Equal(_zip, facts.ArchivePath);
        Assert.Equal("ZIP", facts.Format);
        Assert.StartsWith("3 files · ", facts.Contents, StringComparison.Ordinal);

        Assert.NotNull(host.View.PackedSize);
        FocusOn(w, "top.txt");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("packed", host.Info.LeftText, StringComparison.Ordinal);
        Assert.Contains("%", host.Info.LeftText, StringComparison.Ordinal);

        FocusOn(w, "Summer");
        Press(w, Key.Enter);
        await Until(() => host.Panel.Path == _zip + @"\Summer");
        Press(w, Key.Back);
        await Until(() => host.Panel.Path == _zip);
        Press(w, Key.Back);
        await Until(() => host.Panel.Path == _dir);
        Assert.Equal("photos.zip", host.Panel.FocusedEntry?.Name);
        Assert.Null(host.DirectoryLine.Archive);
        Assert.Null(host.View.PackedSize);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CtrlPageDown_LooksInsideAFileThatIsAZip()
    {
        var docx = Path.Join(_dir, "report.docx");
        File.Copy(_zip, docx);
        var w = await Open();
        await w.ActivePanel.Panel.RefreshAsync();
        FocusOn(w, "report.docx");
        Press(w, Key.PageDown, RawInputModifiers.Control);
        await Until(() => w.ActivePanel.Panel.Path == docx);
        Assert.Contains(w.ActivePanel.Panel.Items, e => e.Name == "Summer");

        Press(w, Key.Back);
        await Until(() => w.ActivePanel.Panel.Path == _dir);
        File.WriteAllText(Path.Join(_dir, "notes.txt"), "text");
        await w.ActivePanel.Panel.RefreshAsync();
        FocusOn(w, "notes.txt");
        Press(w, Key.PageDown, RawInputModifiers.Control);
        await Until(() => w.NoticeMessage is not null);
        Assert.Contains("notes.txt", w.NoticeMessage, StringComparison.Ordinal);
        Assert.Equal(_dir, w.ActivePanel.Panel.Path);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task WhatCantBeDoneInsideAnArchive_SaysWhy()
    {
        var w = await Inside();
        FocusOn(w, "top.txt");
        foreach (var (key, mods) in new[] { (Key.X, RawInputModifiers.Control), (Key.F5, RawInputModifiers.Alt) })
        {
            Press(w, key, mods);
            Assert.Contains("can't be done inside an archive", w.NoticeMessage, StringComparison.Ordinal);
            Assert.True(File.Exists(_zip));
        }

        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F3ViewsACopy_F4EditsACopy()
    {
        var w = await Inside("Summer");
        FocusOn(w, "readme.txt");
        Press(w, Key.F3);
        await Until(() => w.Viewers.Count == 1);
        var viewed = w.Viewers[0].FilePath;
        Assert.Equal("readme.txt", Path.GetFileName(viewed));
        Assert.DoesNotContain(_zip, viewed, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Hello from the archive", File.ReadAllText(viewed), StringComparison.Ordinal);
        w.Viewers[0].Close();

        Press(w, Key.F4);
        await Until(() => w.Editors.Count == 1 && w.Editors[0].Editor.Document.TextLength > 0);
        Assert.False(w.Editors[0].Editor.IsReadOnly);
        Assert.Equal("readme.txt", Path.GetFileName(w.Editors[0].FilePath));
        w.Editors[0].Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AnEncryptedEntryAsksForThePassword_LeavingTheArchiveForgetsIt()
    {
        var aes = Path.Join(_dir, "aes.zip");
        File.Copy(Fixture("aes.zip"), aes);
        var w = await Open();
        var asked = new List<bool>();
        w.ArchivePasswordQuestion = (_, wrong) =>
        {
            asked.Add(wrong);
            return Task.FromResult<string?>(asked.Count == 1 ? "wrong" : "secret");
        };
        await w.ActivePanel.Panel.RefreshAsync();
        FocusOn(w, "aes.zip");
        Press(w, Key.Enter);
        await Until(() => w.ActivePanel.Panel.Path == aes);
        Assert.Empty(asked);
        FocusOn(w, "top.txt");
        Assert.True((w.ActivePanel.Panel.FocusedEntry!.Value.Attributes & FileAttributes.Encrypted) != 0);

        Press(w, Key.F3);
        await Until(() => w.Viewers.Count == 1);
        Assert.Equal([false, true], asked);
        w.Viewers[0].Close();

        Press(w, Key.F3);
        await Until(() => w.Viewers.Count == 1);
        Assert.Equal(2, asked.Count);
        w.Viewers[0].Close();

        Press(w, Key.Back);
        await Until(() => w.ActivePanel.Panel.Path == _dir);
        FocusOn(w, "aes.zip");
        Press(w, Key.Enter);
        await Until(() => w.ActivePanel.Panel.Path == aes);
        FocusOn(w, "top.txt");
        Press(w, Key.F3);
        await Until(() => w.Viewers.Count == 1);
        Assert.Equal(3, asked.Count);
        w.Viewers[0].Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task PropertiesShowTheEntry_TheMenuHasWhatWorks()
    {
        var w = await Inside("Summer");
        FocusOn(w, "stored.txt");
        Press(w, Key.Enter, RawInputModifiers.Alt);
        await Until(() => w.OpenArchiveEntry is not null);
        var values = w.OpenArchiveEntry!.Values;
        Assert.Contains(values, v => v.Contains("bytes", StringComparison.Ordinal));
        Assert.Contains(values, v => v.Contains("100 %", StringComparison.Ordinal));
        Assert.Contains(values, v => v.StartsWith("ZIP · 3 files in 1 folder", StringComparison.Ordinal));
        w.OpenArchiveEntry.Close();
        Dispatcher.UIThread.RunJobs();

        Press(w, Key.F10, RawInputModifiers.Shift);
        Assert.NotNull(w.ArchiveMenu);
        var headers = w.ArchiveMenu.Items.OfType<Avalonia.Controls.MenuItem>().Select(i => i.Header as string).Where(h => h is not null).ToList();
        Assert.Contains(Resources.AppStrings.QuickMenu_View, headers);
        Assert.Contains(Resources.AppStrings.QuickMenu_Properties, headers);
        Assert.Contains(Resources.AppStrings.QuickMenu_Delete, headers);
        w.ArchiveMenu.Close();
        w.CloseNow();
    }

    private static string Fixture(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "tests", "Salandual.Archives.Tests")))
        {
            dir = Path.GetDirectoryName(dir)!;
        }

        return Path.Combine(dir, "tests", "Salandual.Archives.Tests", "Fixtures", name);
    }
}
