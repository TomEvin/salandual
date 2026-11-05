// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ArchiveFindTests.cs

using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Find;
using Salandual.Core.Compare;
using Salandual.Core.Search;

namespace Salandual.App.Tests;

public sealed class ArchiveFindTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Local);
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-archive-find-").FullName;
    private readonly string _left, _zip;

    public ArchiveFindTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Website")).FullName;
        Write("CHANGELOG.md", "longer text", Noon.AddDays(4));
        Write("same.txt", "x", Noon);
        Write("notes.txt", "n", Noon);
        Directory.CreateDirectory(Path.Join(_dir, "Backup"));
        _zip = Path.Join(_dir, "Backup", "site.zip");
        using var archive = ZipFile.Open(_zip, ZipArchiveMode.Create);
        Add(archive, "CHANGELOG.md", "short", Noon);
        Add(archive, "same.txt", "y", Noon);
        Add(archive, "sitemap.xml", "s", Noon);
        Add(archive, "docs/invoice.txt", "deploy the invoice", Noon);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string name, string text, DateTime modified)
    {
        var path = Path.Join(_left, name);
        File.WriteAllText(path, text);
        File.SetLastWriteTime(path, modified);
    }

    private static void Add(ZipArchive archive, string name, string text, DateTime modified)
    {
        var entry = archive.CreateEntry(name);
        entry.LastWriteTime = modified;
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 1500 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static void Press(Control target, Key key)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target });
        Dispatcher.UIThread.RunJobs();
    }

    private async Task<(MainWindow Window, FindWindow Find)> FindInvoice()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_dir);
        var find = w.OpenFind(w.LeftPanel);
        find.NamedBox.Text = "invoice";
        find.InsideZipBox.IsChecked = true;
        await find.StartAsync(ResultMode.New);
        var hit = Assert.Single(find.Shown);
        Assert.Equal(Path.Join(_zip, "docs", "invoice.txt"), hit.FullPath);
        find.ResultList.SelectedItem = hit;
        return (w, find);
    }

    [AvaloniaFact]
    public async Task AFoundEntry_ShowsInsideTheArchiveInThePanel()
    {
        var (w, find) = await FindInvoice();

        Press(find.ResultList, Key.Space);
        await Until(() => w.LeftPanel.Panel.Path == Path.Join(_zip, "docs"));
        Assert.Equal("invoice.txt", w.LeftPanel.Panel.FocusedEntry?.Name);
        find.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task F3AndF4OnAFoundEntry_OpenACopy()
    {
        var (w, find) = await FindInvoice();

        Press(find.ResultList, Key.F3);
        await Until(() => w.Viewers.Count == 1);
        var viewed = w.Viewers[0].FilePath;
        Assert.Equal("invoice.txt", Path.GetFileName(viewed));
        Assert.DoesNotContain(_zip, viewed, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("deploy the invoice", File.ReadAllText(viewed));
        w.Viewers[0].Close();

        Press(find.ResultList, Key.F4);
        await Until(() => w.Editors.Count == 1 && w.Editors[0].Editor.Document.TextLength > 0);
        Assert.False(w.Editors[0].Editor.IsReadOnly);
        w.Editors[0].Close();
        find.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task CompareDirectories_WithAnArchiveOnOneSide()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.Show();
        await w.LeftPanel.Panel.NavigateAsync(_left);
        await w.RightPanel.Panel.NavigateAsync(_zip);
        w.LeftPanel.View.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.True(w.Run("Compare.Directories", Core.Commands.PanelTarget.Active));
        await Until(() => w.CompareDialog is not null);
        w.CompareDialog!.ContentBox.IsChecked = true;
        w.CompareDialog.Accept();
        await Until(() => w.Comparison is not null);

        var left = w.LeftPanel.Panel.CompareMarks!;
        var right = w.RightPanel.Panel.CompareMarks!;
        Assert.Equal(DifferenceKind.OnlyLeft, left["notes.txt"]);
        Assert.Equal(DifferenceKind.OnlyRight, right["sitemap.xml"]);
        Assert.Equal(DifferenceKind.OnlyRight, right["docs"]);
        Assert.Equal(DifferenceKind.NewerOrBigger, left["CHANGELOG.md"]);
        Assert.Equal(DifferenceKind.Different, left["same.txt"]);
        w.CloseNow();
    }
}
