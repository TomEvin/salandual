// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : tests/Salandual.App.Tests/ArchivePackTests.cs

using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Salandual.App.Resources;
using Salandual.Archives;
using Salandual.Core.Operations;

namespace Salandual.App.Tests;

public sealed class ArchivePackTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salandual-pack-").FullName;
    private readonly string _left, _right;

    public ArchivePackTests()
    {
        _left = Directory.CreateDirectory(Path.Join(_dir, "Photos")).FullName;
        _right = Directory.CreateDirectory(Path.Join(_dir, "Backup")).FullName;
        Directory.CreateDirectory(Path.Join(_left, "Summer"));
        File.WriteAllText(Path.Join(_left, "Summer", "beach.txt"), "Sand");
        File.WriteAllText(Path.Join(_left, "Summer", "lake.txt"), "Water");
        File.WriteAllText(Path.Join(_left, "route.gpx"), "<gpx/>");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

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

    private static async Task Until(Func<bool> condition, int tries = 3000)
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

    private static void SelectAll(MainWindow w, params string[] names)
        => w.ActivePanel.Panel.SelectWhere(e => names.Contains(e.Name), select: true);

    private static async Task<OperationJob> PackDone(MainWindow w)
    {
        await Until(() => w.LastPackJob is { IsFinished: true });
        Dispatcher.UIThread.RunJobs();
        return w.LastPackJob!;
    }

    private static string[] Names(string archive)
        => [.. ArchiveReader.Open(archive, Path.GetFileName(archive), null, cancellation: TestContext.Current.CancellationToken).Items.Select(i => i.Path).Order(StringComparer.Ordinal)];

    [AvaloniaFact]
    public async Task AltF5_PacksTheFocusedFolderIntoAZipBesideIt()
    {
        var w = await Open();
        FocusOn(w, "Summer");

        Press(w, Key.F5, RawInputModifiers.Alt);
        await Until(() => w.OpenPackDialog is { IsVisible: true });
        var dialog = w.OpenPackDialog!;
        Assert.Equal("Pack 1 item (9 B) from Photos", dialog.Title);
        Assert.Equal(Path.Join(_left, "Summer.zip"), dialog.To.Text);
        Assert.Equal(PackFormat.Zip, dialog.Format);
        Assert.False(dialog.AdvancedOpen);
        Assert.Equal(string.Join(" · ", AppStrings.Pack_Sum_One, AppStrings.Pack_Sum_Stored, AppStrings.Pack_Sum_Kept), dialog.Summary);
        dialog.Accept(toQueue: false);
        var job = await PackDone(w);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(OperationKind.Pack, job.Kind);
        Assert.Equal(["Summer", "Summer\\beach.txt", "Summer\\lake.txt"], Names(Path.Join(_left, "Summer.zip")));
        Assert.True(Directory.Exists(Path.Join(_left, "Summer")));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task SeveralItems_AreNamedAfterTheirFolder_AndTheFormatSetsTheExtension()
    {
        var w = await Open();
        SelectAll(w, "Summer", "route.gpx");

        Press(w, Key.F5, RawInputModifiers.Alt);
        await Until(() => w.OpenPackDialog is { IsVisible: true });
        var dialog = w.OpenPackDialog!;
        Assert.Equal(Path.Join(_left, "Photos.zip"), dialog.To.Text);
        Assert.Equal(AppStrings.Pack_Where_ParentBeside, dialog.WhereNote);
        dialog.SetFormat(PackFormat.SevenZip);
        Assert.Equal(Path.Join(_left, "Photos.7z"), dialog.To.Text);
        dialog.SetFormat(PackFormat.TarGz);
        Assert.Equal(Path.Join(_left, "Photos.tar.gz"), dialog.To.Text);
        Assert.False(dialog.Password.IsEnabled);
        Assert.Equal(AppStrings.Pack_PasswordTar, dialog.PasswordNote);
        dialog.Accept(toQueue: false);
        var job = await PackDone(w);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(["Photos.tar"], Names(Path.Join(_left, "Photos.tar.gz")));
        Assert.Equal(PackFormat.TarGz, w.Services.Settings.Current.Operations.PackLastFormat);
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task HiddenNames_NeedA7zWithAPassword()
    {
        var w = await Open();
        FocusOn(w, "Summer");
        Press(w, Key.F5, RawInputModifiers.Alt);
        await Until(() => w.OpenPackDialog is { IsVisible: true });
        var dialog = w.OpenPackDialog!;
        dialog.OpenAdvanced();
        Assert.False(dialog.HideNamesBox.IsEnabled);
        dialog.SetFormat(PackFormat.SevenZip);
        Assert.False(dialog.HideNamesBox.IsEnabled);
        dialog.Password.Text = "secret";
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.HideNamesBox.IsEnabled);
        dialog.HideNamesBox.IsChecked = true;
        dialog.DeleteBox.IsChecked = true;
        Assert.Contains(AppStrings.Pack_Sum_Hidden, dialog.Summary);
        Assert.Contains(AppStrings.Pack_Sum_Delete, dialog.Summary);
        dialog.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task TheSettings_ChooseTheFormatAndThePlace()
    {
        var w = await Open();
        w.Services.Settings.Update(s => s with { Operations = s.Operations with { PackFirstFormat = PackFirstFormat.SevenZip, PackWhere = PackWhere.OtherPanel } });
        FocusOn(w, "route.gpx");

        Press(w, Key.F5, RawInputModifiers.Alt);
        await Until(() => w.OpenPackDialog is { IsVisible: true });
        var dialog = w.OpenPackDialog!;
        Assert.Equal(Path.Join(_right, "route.7z"), dialog.To.Text);
        dialog.Close();

        w.Services.Settings.Update(s => s with { Operations = s.Operations with { PackFirstFormat = PackFirstFormat.LastUsed, PackLastFormat = PackFormat.Tar } });
        Press(w, Key.F5, RawInputModifiers.Alt);
        await Until(() => w.OpenPackDialog is { IsVisible: true } d && !ReferenceEquals(d, dialog));
        Assert.Equal(Path.Join(_right, "route.tar"), w.OpenPackDialog!.To.Text);
        w.OpenPackDialog.Close();
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AnArchiveAlreadyThere_Asks_AndANewNameKeepsBoth()
    {
        File.WriteAllText(Path.Join(_left, "Summer.zip"), "not touched");
        var w = await Open();
        FocusOn(w, "Summer");
        Press(w, Key.F5, RawInputModifiers.Alt);
        await Until(() => w.OpenPackDialog is { IsVisible: true });
        w.OpenPackDialog!.Accept(toQueue: false);
        await Until(() => w.OpenPackExists is { IsVisible: true });
        var exists = w.OpenPackExists!;
        Assert.True(exists.AddOption.IsChecked);
        exists.NewNameOption.IsChecked = true;
        exists.Accept();
        var job = await PackDone(w);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal("not touched", File.ReadAllText(Path.Join(_left, "Summer.zip")));
        Assert.Equal(["Summer", "Summer\\beach.txt", "Summer\\lake.txt"], Names(Path.Join(_left, "Summer (2).zip")));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task AddingToAnArchive_KeepsWhatItHeld()
    {
        var w = await Open();
        FocusOn(w, "Summer");
        Press(w, Key.F5, RawInputModifiers.Alt);
        await Until(() => w.OpenPackDialog is { IsVisible: true });
        var first = w.OpenPackDialog!;
        first.To.Text = Path.Join(_right, "All.zip");
        first.Accept(toQueue: false);
        await PackDone(w);
        var before = w.LastPackJob;

        w.Services.Settings.Update(s => s with { Operations = s.Operations with { PackExisting = PackExisting.Add } });
        FocusOn(w, "route.gpx");
        Press(w, Key.F5, RawInputModifiers.Alt);
        await Until(() => w.OpenPackDialog is { IsVisible: true } d && !ReferenceEquals(d, first));
        w.OpenPackDialog!.To.Text = Path.Join(_right, "All.zip");
        w.OpenPackDialog.Accept(toQueue: false);
        await Until(() => w.LastPackJob != before && w.LastPackJob is { IsFinished: true });

        Assert.Equal(JobState.Done, w.LastPackJob!.State);
        Assert.Null(w.OpenPackExists);
        Assert.Equal(["Summer", "Summer\\beach.txt", "Summer\\lake.txt", "route.gpx"], Names(Path.Join(_right, "All.zip")));
        w.CloseNow();
    }

    [AvaloniaFact]
    public async Task InsideAnArchive_PackSaysWhy()
    {
        var w = await Open();
        FocusOn(w, "Summer");
        Press(w, Key.F5, RawInputModifiers.Alt);
        await Until(() => w.OpenPackDialog is { IsVisible: true });
        w.OpenPackDialog!.Accept(toQueue: false);
        await PackDone(w);
        await w.ActivePanel.Panel.RefreshAsync();
        await Until(() => w.ActivePanel.Panel.Items.Any(e => e.Name == "Summer.zip"));
        FocusOn(w, "Summer.zip");
        Press(w, Key.Enter);
        await Until(() => w.ActivePanel.Panel.Path == Path.Join(_left, "Summer.zip"));
        var dialog = w.OpenPackDialog;

        Press(w, Key.F5, RawInputModifiers.Alt);

        Assert.Same(dialog, w.OpenPackDialog);
        Assert.Contains("Summer.zip", w.NoticeMessage, StringComparison.Ordinal);
        w.CloseNow();
    }

    [Fact]
    public void PackIsBuilt_SoTheMenusAndTheMiddleToolbarOfferIt() => Assert.True(MainWindow.IsBuilt("Archive.Pack"));

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

        var settings = new Core.Settings.OperationSettings();
        var format = Core.Listing.DisplayFormat.From(new Core.Settings.PanelSettings(), System.Globalization.CultureInfo.CurrentCulture);
        var fs = new Platform.Windows.FileSystem.LocalOperations();
        var pack = new Dialogs.PackDialog(new Dialogs.PackContext("Pack 3 items (1.8 GB) from Photos", "2 folders and 1 file.", Path.Join(_left, "Photos.zip"), AppStrings.Pack_Where_ParentBeside, 1_900_000_000, settings), fs, format, _ => { });
        Save(pack, "pack.png");
        pack.OpenAdvanced();
        Save(pack, "pack-advanced.png");
        pack.SetFormat(PackFormat.TarXz);
        Save(pack, "pack-tar.png");
        pack.Close();

        var exists = new Dialogs.PackExistsDialog("Photos.zip", "In D:\\Photos, 1.6 GB, from 2 January 2025.", "Photos (2).zip", canAdd: true);
        Save(exists, "pack-exists.png");
        exists.Close();
    }
}
