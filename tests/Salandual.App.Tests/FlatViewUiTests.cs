// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/FlatViewUiTests.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Salandual.Core.Commands;
using Salandual.Core.Panels;
using Salandual.Core.Settings;

namespace Salandual.App.Tests;

public sealed class FlatViewUiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-flat-").FullName;
    private readonly NoShellAccess _shell = new();

    public FlatViewUiTests()
    {
        foreach (var file in new[] { "README.md", @"Clients\Acme\budget.xlsx", @"Clients\Acme\contract.pdf", @"Clients\Bolt\offer.pdf", @"Website\index.html", @"Website\docs\notes.txt" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Join(_dir, file))!);
            File.WriteAllText(Path.Join(_dir, file), "x");
        }
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<MainWindow> Open()
    {
        var w = new MainWindow { Width = 1100, Height = 700 };
        w.ShellAccess = _shell;
        w.Show();
        await w.ActivePanel.Panel.NavigateAsync(_dir);
        w.ActivePanel.View.Focus();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static string[] Names(MainWindow w) => [.. w.ActivePanel.Panel.Items.Where(e => e.Name != "..").Select(e => e.Name)];

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private static string Text(object? header) => header as string is { } s ? s.Replace("_", "", StringComparison.Ordinal) : "";

    private static MenuItem Item(ItemsControl menu, string header)
        => menu.Items.OfType<MenuItem>().First(m => Text(m.Header) == header);

    private static void Capture(Window w, string name)
    {
        if (Environment.GetEnvironmentVariable("SALANDUAL_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            for (var k = 0; k < 10; k++)
            {
                Dispatcher.UIThread.RunJobs();
            }

            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        }
    }

    private static bool Done(MainWindow w) => w.ActivePanel.Panel.Results?.Flat is { Reading: false };

    [AvaloniaFact]
    public async Task CtrlB_EveryFileWithItsFolder_TheBar_TheViewMenuTicked_AndBackAgain()
    {
        var w = await Open();
        try
        {
            var host = w.ActivePanel;
            Assert.True(w.Run("Panel.FlatView", PanelTarget.Active));
            await Until(() => Done(w));
            Assert.Equal(6, host.Panel.Count);
            Assert.Contains(@"Website\docs\notes.txt", Names(w));
            Assert.DoesNotContain("Clients", Names(w));
            Assert.True(host.ResultsBar!.IsVisible);
            Assert.StartsWith("Flat view of " + Path.GetFileName(_dir) + " · 6 files in 6 folders", host.ResultsText, StringComparison.Ordinal);
            Assert.False(host.ResultsStop!.IsVisible);
            Assert.True(Item(w.ShowViewMenu(host), "Flat view").IsChecked);
            w.ViewMenu!.Close();
            var columns = host.View.ShownColumns();
            var ext = columns.Single(c => c.Column == PanelColumn.Extension);
            var folder = host.View.FolderShown()!.Value;
            Assert.True(folder.X > ext.X + ext.Width && folder.X + folder.Width < columns.First(c => c.X > ext.X).X);
            Capture(w, "flat-column.png");

            w.Services.Settings.Update(s => s with { Panels = s.Panels with { FlatFolders = FlatFolderShows.Headings } });
            await Until(() => host.Panel.IsHeading(1));
            Assert.Equal("Clients\\Acme", Names(w)[1]);
            Capture(w, "flat-headings.png");
            w.Services.Settings.Update(s => s with { Panels = s.Panels with { FlatFolders = FlatFolderShows.InName } });
            await Until(() => Names(w)[0] == @"Clients\Acme\budget.xlsx");
            Capture(w, "flat-inname.png");

            File.WriteAllText(Path.Join(_dir, @"Website\docs\faq.md"), "x");
            await Until(() => Names(w).Contains(@"Website\docs\faq.md"));

            Assert.True(w.Run("Panel.FlatView", PanelTarget.Active));
            await Until(() => !host.Panel.IsFlat && Names(w).Contains("Clients"));
            Assert.False(host.ResultsBar.IsVisible);
        }
        finally
        {
            w.Services.Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task RightClickedFolders_OnlyTheirFiles_AndTheFilterWithSubfolders()
    {
        var w = await Open();
        try
        {
            var host = w.ActivePanel;
            var panel = host.Panel;
            panel.SelectWhere(e => e.Name is "Clients", true);
            panel.SetFocus(panel.Items.ToList().FindIndex(e => e.Name == "Clients"));
            var r = host.View.NameBounds(panel.Focus)!.Value;
            var p = host.View.TranslatePoint(new Point(r.X + 20, r.Y + (r.Height / 2)), w)!.Value;
            w.MouseDown(p, MouseButton.Right);
            w.MouseUp(p, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            var flat = Item(w.QuickMenu!, "Flat view");
            flat.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => Done(w));
            Assert.Equal(["Clients"], panel.Results!.Flat!.Folders);
            Assert.Equal([@"Clients\Acme\budget.xlsx", @"Clients\Acme\contract.pdf", @"Clients\Bolt\offer.pdf"], Names(w));
            Assert.StartsWith("Flat view of Clients in ", host.ResultsText, StringComparison.Ordinal);
            await panel.LeaveResultsAsync();
            await Until(() => !panel.IsFlat);

            var filter = w.FilterAsync(host);
            await Until(() => w.OpenFilterDialog is not null);
            var dialog = w.OpenFilterDialog!;
            Assert.False(dialog.SubfoldersBox.IsChecked);
            dialog.MaskBox.Text = "*.pdf";
            dialog.SubfoldersBox.IsChecked = true;
            dialog.Accept();
            await filter;
            await Until(() => Done(w));
            Assert.Equal([@"Clients\Acme\contract.pdf", @"Clients\Bolt\offer.pdf"], Names(w));
            Assert.Contains("2 of 6 files", host.ResultsText, StringComparison.Ordinal);
        }
        finally
        {
            w.Services.Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
            w.CloseNow();
        }
    }

    [AvaloniaFact]
    public async Task ABigTree_AsksBeforeReadingOn()
    {
        var w = await Open();
        try
        {
            w.Services.Settings.Update(s => s with { Panels = s.Panels with { FlatAskAbove = 2 } });
            Dispatcher.UIThread.RunJobs();
            w.Run("Panel.FlatView", PanelTarget.Active);
            await Until(() => w.FlatQuestion is { IsVisible: true });
            w.FlatQuestion!.Yes.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => Done(w));
            Assert.Equal(6, w.ActivePanel.Panel.Count);
            Assert.False(w.ActivePanel.Panel.Results!.Flat!.Stopped);
        }
        finally
        {
            w.Services.Settings.Update(_ => new AppSettings());
            Dispatcher.UIThread.RunJobs();
            w.CloseNow();
        }
    }
}
